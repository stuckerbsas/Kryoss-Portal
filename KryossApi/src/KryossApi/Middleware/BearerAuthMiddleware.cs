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
///
/// HIGH-02 SECURITY WARNING: This middleware trusts X-MS-CLIENT-PRINCIPAL
/// without cryptographic verification. Easy Auth is configured with
/// requireAuthentication=false (AllowAnonymous), so Azure does NOT block
/// unauthenticated requests. Security relies on:
///   1. Azure SWA proxy strips/replaces the X-MS-CLIENT-PRINCIPAL header
///      before forwarding — preventing client-side forgery.
///   2. The Function App URL (func-kryoss.azurewebsites.net) MUST be
///      firewalled to only accept traffic from the SWA backend.
///
/// If the Function App is directly accessible without SWA in front, an
/// attacker can craft a fake X-MS-CLIENT-PRINCIPAL header and impersonate
/// any user. MITIGATIONS:
///   - Network restriction: configure Azure Networking on the Function App
///     to accept only the SWA backend IP range.
///   - Long-term: validate the JWT from the Authorization header directly
///     using Microsoft.Identity.Web instead of trusting Easy Auth headers.
///   - Alternative: set requireAuthentication=true in Easy Auth config.
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

        // Public endpoints and OAuth callbacks — no Bearer token required
        if (path.Contains("/consent-callback", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/connect-callback", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/v2/version", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/v2/reports/diagnose/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Read EasyAuth header (set by Azure App Service / SWA authentication)
        var principalHeader = httpReq.Headers.TryGetValues("X-MS-CLIENT-PRINCIPAL", out var principalValues)
            ? principalValues.FirstOrDefault() : null;

        EasyAuthPrincipal? principal = null;

        if (!string.IsNullOrEmpty(principalHeader))
        {
            // HIGH-02: Additional header check
            var principalIdHeader = httpReq.Headers.TryGetValues("X-MS-CLIENT-PRINCIPAL-ID", out var pidValues)
                ? pidValues.FirstOrDefault() : null;
            if (string.IsNullOrEmpty(principalIdHeader))
            {
                var logger2 = context.InstanceServices.GetRequiredService<ILogger<BearerAuthMiddleware>>();
                logger2.LogWarning("X-MS-CLIENT-PRINCIPAL present but X-MS-CLIENT-PRINCIPAL-ID missing — possible forgery attempt");
            }

            var principalJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(principalHeader));
            principal = JsonSerializer.Deserialize<EasyAuthPrincipal>(principalJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        else
        {
            // Fallback: decode JWT from Authorization header (for local dev / direct API calls)
            var authHeader = httpReq.Headers.TryGetValues("Authorization", out var authValues)
                ? authValues.FirstOrDefault() : null;

            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var jwt = authHeader["Bearer ".Length..];
                principal = DecodeJwtToPrincipal(jwt);
            }
        }

        if (principal is null)
        {
            var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "Authentication required" });
            context.GetInvocationResult().Value = resp;
            return;
        }

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
                // Auto-provision: any authenticated Entra ID user gets created
                // with the 'viewer' role. Admins can upgrade via the portal later.
                logger.LogInformation(
                    "Auto-provisioning new user from Entra ID. OID: {Oid}, Email: {Email}",
                    objectId, emailFromClaims ?? "(unknown)");

                var viewerRole = await db.Roles
                    .Include(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
                    .FirstOrDefaultAsync(r => r.Code == "viewer");

                if (viewerRole is null)
                {
                    // Fallback: try any role that exists
                    viewerRole = await db.Roles
                        .Include(r => r.RolePermissions)
                            .ThenInclude(rp => rp.Permission)
                        .OrderBy(r => r.Id)
                        .FirstOrDefaultAsync();
                }

                if (viewerRole is null)
                {
                    logger.LogError("Auto-provision failed: no roles found in database");
                    var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                    await resp.WriteAsJsonAsync(new { error = "System not configured — no roles" });
                    context.GetInvocationResult().Value = resp;
                    return;
                }

                var firstFranchise = await db.Franchises.FirstOrDefaultAsync();

                user = new User
                {
                    Id = Guid.NewGuid(),
                    EntraOid = objectId,
                    Email = emailFromClaims ?? $"{objectId}@auto",
                    DisplayName = nameFromClaims ?? "New User",
                    RoleId = viewerRole.Id,
                    FranchiseId = firstFranchise?.Id,
                    AuthSource = "entra",
                    CreatedBy = Guid.Empty,
                    CreatedAt = DateTime.UtcNow
                };

                db.Users.Add(user);
                await db.SaveChangesAsync();

                user.Role = viewerRole;

                logger.LogInformation(
                    "Auto-provisioned user {UserId} as {Role} (franchise: {FranchiseId})",
                    user.Id, viewerRole.Code, user.FranchiseId);
            }
        }

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;

        // Keep display_name in sync with the latest JWT `name` claim (users can
        // update their name in Entra ID without ever hitting our profile UI).
        if (!string.IsNullOrWhiteSpace(nameFromClaims) && user.DisplayName != nameFromClaims)
        {
            user.DisplayName = nameFromClaims;
        }

        await db.SaveChangesAsync();

        // Populate current user context
        var currentUser = context.InstanceServices.GetRequiredService<ICurrentUserService>() as CurrentUserService;
        if (currentUser is not null)
        {
            currentUser.UserId = user.Id;
            currentUser.Email = user.Email;
            currentUser.DisplayName = user.DisplayName;
            currentUser.Phone = user.Phone;
            currentUser.JobTitle = user.JobTitle;
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

    private static EasyAuthPrincipal? DecodeJwtToPrincipal(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var payload = parts[1];
            // Fix base64url padding
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (claims is null) return null;

            var claimList = new List<EasyAuthClaim>();
            foreach (var (key, value) in claims)
            {
                if (value.ValueKind == JsonValueKind.String)
                    claimList.Add(new EasyAuthClaim { Typ = key, Val = value.GetString()! });
                else if (value.ValueKind == JsonValueKind.Number)
                    claimList.Add(new EasyAuthClaim { Typ = key, Val = value.GetRawText() });
            }

            // Map standard JWT claims to the types EasyAuth uses
            var oid = claims.TryGetValue("oid", out var oidEl) ? oidEl.GetString() : null;
            if (oid != null)
                claimList.Add(new EasyAuthClaim { Typ = "http://schemas.microsoft.com/identity/claims/objectidentifier", Val = oid });

            var tid = claims.TryGetValue("tid", out var tidEl) ? tidEl.GetString() : null;
            if (tid != null)
                claimList.Add(new EasyAuthClaim { Typ = "http://schemas.microsoft.com/identity/claims/tenantid", Val = tid });

            var email = claims.TryGetValue("preferred_username", out var emailEl) ? emailEl.GetString()
                : claims.TryGetValue("email", out emailEl) ? emailEl.GetString() : null;

            return new EasyAuthPrincipal
            {
                AuthTyp = "Bearer",
                Claims = claimList,
                UserDetails = email
            };
        }
        catch
        {
            return null;
        }
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
