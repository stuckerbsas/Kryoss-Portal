using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KryossApi.Middleware;

/// <summary>
/// Authenticates portal requests via Entra ID / B2C bearer tokens.
/// Azure App Service / Functions handles JWT validation — we read the
/// decoded claims from X-MS-CLIENT-PRINCIPAL-* headers (EasyAuth).
/// Also supports Azure Static Web Apps principal format (same header,
/// additional fields: identityProvider, userId, userDetails, userRoles).
/// Then resolve the user from the DB and populate ICurrentUserService.
///
/// Bootstrap: if no users exist at all, the first authenticated caller
/// is auto-provisioned as super_admin to allow initial setup.
/// </summary>
public class BearerAuthMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpReq = await context.GetHttpRequestDataAsync();
        if (httpReq is null)
        {
            await next(context);
            return;
        }

        // Only process portal routes (v2/*)
        var path = httpReq.Url.AbsolutePath;
        if (!path.Contains("/v2/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Read EasyAuth header (set by Azure App Service / SWA authentication)
        var principalHeader = httpReq.Headers.TryGetValues("X-MS-CLIENT-PRINCIPAL", out var principalValues)
            ? principalValues.FirstOrDefault() : null;

        if (string.IsNullOrEmpty(principalHeader))
        {
            var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "Authentication required" });
            context.GetInvocationResult().Value = resp;
            return;
        }

        // Decode base64 principal (Azure EasyAuth / SWA format)
        var principalJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(principalHeader));
        var principal = JsonSerializer.Deserialize<EasyAuthPrincipal>(principalJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (principal is null)
        {
            var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "Invalid principal" });
            context.GetInvocationResult().Value = resp;
            return;
        }

        // Extract OID — works for both EasyAuth and SWA (both have Claims array)
        var oid = principal.Claims?.FirstOrDefault(c => c.Typ == "http://schemas.microsoft.com/identity/claims/objectidentifier")?.Val
            ?? principal.Claims?.FirstOrDefault(c => c.Typ == "oid")?.Val;

        if (string.IsNullOrEmpty(oid) || !Guid.TryParse(oid, out var objectId))
        {
            var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "Missing object identifier claim" });
            context.GetInvocationResult().Value = resp;
            return;
        }

        // Extract tenant ID from claims (for future multi-tenant use)
        var tid = principal.Claims?.FirstOrDefault(c => c.Typ == "http://schemas.microsoft.com/identity/claims/tenantid")?.Val
            ?? principal.Claims?.FirstOrDefault(c => c.Typ == "tid")?.Val;

        // Extract email and display name from claims for bootstrap
        var emailFromClaims = principal.Claims?.FirstOrDefault(c => c.Typ == "preferred_username")?.Val
            ?? principal.Claims?.FirstOrDefault(c => c.Typ == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Val
            ?? principal.Claims?.FirstOrDefault(c => c.Typ == "email")?.Val
            ?? principal.UserDetails; // SWA-specific fallback (contains the email)

        var nameFromClaims = principal.Claims?.FirstOrDefault(c => c.Typ == "name")?.Val
            ?? principal.Claims?.FirstOrDefault(c => c.Typ == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Val
            ?? emailFromClaims; // fall back to email if no name claim

        var logger = context.InstanceServices.GetRequiredService<ILogger<BearerAuthMiddleware>>();
        var db = context.InstanceServices.GetRequiredService<KryossDbContext>();

        // Find user by Entra OID or B2C OID
        var user = await db.Users
            .Include(u => u.Role)
                .ThenInclude(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.EntraOid == objectId || u.B2cOid == objectId);

        if (user is null)
        {
            // Bootstrap: if zero users exist, auto-create the first as super_admin
            var totalUsers = await db.Users.CountAsync(); // respects soft-delete filter

            if (totalUsers == 0)
            {
                logger.LogWarning(
                    "No users exist — bootstrapping first user as super_admin. OID: {Oid}, Email: {Email}",
                    objectId, emailFromClaims ?? "(unknown)");

                var superAdminRole = await db.Roles
                    .Include(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
                    .FirstOrDefaultAsync(r => r.Code == "super_admin");

                if (superAdminRole is null)
                {
                    logger.LogError("Bootstrap failed: super_admin role not found in database");
                    var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                    await resp.WriteAsJsonAsync(new { error = "System not configured" });
                    context.GetInvocationResult().Value = resp;
                    return;
                }

                var firstFranchise = await db.Franchises.FirstOrDefaultAsync();

                user = new User
                {
                    Id = Guid.NewGuid(),
                    EntraOid = objectId,
                    Email = emailFromClaims ?? $"{objectId}@bootstrap",
                    DisplayName = nameFromClaims ?? "System Administrator",
                    RoleId = superAdminRole.Id,
                    FranchiseId = firstFranchise?.Id,
                    AuthSource = "entra",
                    CreatedBy = Guid.Empty,
                    CreatedAt = DateTime.UtcNow
                };

                db.Users.Add(user);
                await db.SaveChangesAsync();

                // Re-attach the role for downstream use (already loaded)
                user.Role = superAdminRole;

                logger.LogWarning(
                    "Bootstrap complete: user {UserId} created as super_admin (franchise: {FranchiseId})",
                    user.Id, user.FranchiseId);
            }
            else
            {
                logger.LogWarning("Unknown user OID: {Oid}", objectId);
                var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Forbidden);
                await resp.WriteAsJsonAsync(new { error = "User not registered in Kryoss" });
                context.GetInvocationResult().Value = resp;
                return;
            }
        }

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Populate current user context
        var currentUser = context.InstanceServices.GetRequiredService<ICurrentUserService>() as CurrentUserService;
        if (currentUser is not null)
        {
            currentUser.UserId = user.Id;
            currentUser.Email = user.Email;
            currentUser.FranchiseId = user.FranchiseId;
            currentUser.OrganizationId = user.OrganizationId;
            currentUser.IsAdmin = user.Role.Code == "super_admin";
            currentUser.Permissions = user.Role.RolePermissions
                .Select(rp => rp.Permission.Slug)
                .ToArray();
            currentUser.IpAddress = httpReq.Headers.TryGetValues("X-Forwarded-For", out var fwdValues)
                ? fwdValues.FirstOrDefault() : null;
            currentUser.SessionId = httpReq.Headers.TryGetValues("X-MS-TOKEN-AAD-ID-TOKEN", out var tokenValues)
                ? tokenValues.FirstOrDefault()?[..Math.Min(32, tokenValues.FirstOrDefault()?.Length ?? 0)] : null;
        }

        await next(context);
    }
}

internal class EasyAuthPrincipal
{
    public string? AuthTyp { get; set; }
    public List<EasyAuthClaim>? Claims { get; set; }
    public string? NameTyp { get; set; }
    public string? RoleTyp { get; set; }

    // SWA-specific fields (present in Azure Static Web Apps format, absent in App Service EasyAuth)
    public string? IdentityProvider { get; set; }
    public string? UserId { get; set; }
    public string? UserDetails { get; set; }
    public List<string>? UserRoles { get; set; }
}

internal class EasyAuthClaim
{
    public string Typ { get; set; } = null!;
    public string Val { get; set; } = null!;
}
