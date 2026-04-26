using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

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
/// HIGH-02 FIX (C3): Primary auth path now validates JWT signature via OIDC
/// discovery (Entra ID v2.0). X-MS-CLIENT-PRINCIPAL is only trusted as
/// fallback when X-Forwarded-Host indicates the request came through Azure
/// Static Web Apps (*.azurestaticapps.net or *kryoss*). Direct callers
/// MUST supply a valid Authorization: Bearer token.
/// </summary>
public class BearerAuthMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly string _tenantId =
        Environment.GetEnvironmentVariable("AzureAd__TenantId") ?? "840e016d-d1c4-4329-8cb0-670f2554525d";

    private static readonly string _clientId =
        Environment.GetEnvironmentVariable("AzureAd__ClientId") ?? "83bd6db8-3cbb-40fa-bdd4-0ef5347b1923";

    private static readonly ConfigurationManager<OpenIdConnectConfiguration> _configManager =
        new($"https://login.microsoftonline.com/{_tenantId}/v2.0/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());

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

        EasyAuthPrincipal? principal = null;
        var actlog = context.InstanceServices.GetService<IActlogService>();

        // Primary path: validate JWT from Authorization header
        var authHeader = httpReq.Headers.TryGetValues("Authorization", out var authValues)
            ? authValues.FirstOrDefault() : null;

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var jwt = authHeader["Bearer ".Length..];
            var logger0 = context.InstanceServices.GetRequiredService<ILogger<BearerAuthMiddleware>>();
            principal = await ValidateJwtAsync(jwt, logger0, actlog);
        }

        // Fallback: X-MS-CLIENT-PRINCIPAL only from SWA
        if (principal is null)
        {
            var principalHeader = httpReq.Headers.TryGetValues("X-MS-CLIENT-PRINCIPAL", out var principalValues)
                ? principalValues.FirstOrDefault() : null;

            if (!string.IsNullOrEmpty(principalHeader))
            {
                var forwardedHost = httpReq.Headers.TryGetValues("X-Forwarded-Host", out var fhValues)
                    ? fhValues.FirstOrDefault() : null;

                var isSwa = forwardedHost != null &&
                    (forwardedHost.Contains(".azurestaticapps.net", StringComparison.OrdinalIgnoreCase) ||
                     forwardedHost.Contains("kryoss", StringComparison.OrdinalIgnoreCase));

                if (isSwa)
                {
                    var principalJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(principalHeader));
                    principal = JsonSerializer.Deserialize<EasyAuthPrincipal>(principalJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                else
                {
                    var logger1 = context.InstanceServices.GetRequiredService<ILogger<BearerAuthMiddleware>>();
                    logger1.LogWarning("X-MS-CLIENT-PRINCIPAL from non-SWA source rejected (Host: {Host})", forwardedHost);
                    if (actlog != null)
                        await actlog.LogAsync("WARN", "auth", "bearer_untrusted_header",
                            $"X-MS-CLIENT-PRINCIPAL from non-SWA host: {forwardedHost}");
                }
            }
        }

        if (principal is null)
        {
            var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "Authentication required" });
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

    private static async Task<EasyAuthPrincipal?> ValidateJwtAsync(string jwt, ILogger logger, IActlogService? actlog)
    {
        try
        {
            var oidcConfig = await _configManager.GetConfigurationAsync(CancellationToken.None);

            var validationParams = new TokenValidationParameters
            {
                ValidIssuer = $"https://login.microsoftonline.com/{_tenantId}/v2.0",
                ValidAudiences = new[] { _clientId, $"api://{_clientId}" },
                IssuerSigningKeys = oidcConfig.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true
            };

            var handler = new JsonWebTokenHandler();
            var result = await handler.ValidateTokenAsync(jwt, validationParams);

            if (!result.IsValid)
            {
                logger.LogWarning("JWT validation failed: {Error}", result.Exception?.Message);
                if (actlog != null)
                    await actlog.LogAsync("ERR", "auth", "bearer_jwt_invalid",
                        result.Exception?.Message);
                return null;
            }

            var claimList = new List<EasyAuthClaim>();
            foreach (var claim in result.Claims)
            {
                if (claim.Value is string strVal)
                    claimList.Add(new EasyAuthClaim { Typ = claim.Key, Val = strVal });
                else
                    claimList.Add(new EasyAuthClaim { Typ = claim.Key, Val = claim.Value?.ToString() ?? "" });
            }

            // Map short JWT claims to the schema URIs that downstream code expects
            var oid = result.Claims.TryGetValue("oid", out var oidVal) ? oidVal?.ToString() : null;
            if (oid != null)
                claimList.Add(new EasyAuthClaim { Typ = "http://schemas.microsoft.com/identity/claims/objectidentifier", Val = oid });

            var tid = result.Claims.TryGetValue("tid", out var tidVal) ? tidVal?.ToString() : null;
            if (tid != null)
                claimList.Add(new EasyAuthClaim { Typ = "http://schemas.microsoft.com/identity/claims/tenantid", Val = tid });

            var email = result.Claims.TryGetValue("preferred_username", out var emailVal) ? emailVal?.ToString()
                : result.Claims.TryGetValue("email", out emailVal) ? emailVal?.ToString() : null;

            logger.LogInformation("JWT validated for OID {Oid}", oid);

            return new EasyAuthPrincipal
            {
                AuthTyp = "Bearer",
                Claims = claimList,
                UserDetails = email
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JWT validation exception");
            if (actlog != null)
                await actlog.LogAsync("ERR", "auth", "bearer_jwt_invalid", ex.Message);
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
