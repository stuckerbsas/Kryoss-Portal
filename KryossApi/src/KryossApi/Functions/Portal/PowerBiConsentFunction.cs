using System.Net;
using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Portal;

public class PowerBiConsentFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly M365Config _m365Config;
    private readonly ILogger<PowerBiConsentFunction> _log;

    public PowerBiConsentFunction(
        KryossDbContext db,
        ICurrentUserService user,
        M365Config m365Config,
        ILogger<PowerBiConsentFunction> log)
    {
        _db = db;
        _user = user;
        _m365Config = m365Config;
        _log = log;
    }

    /// <summary>
    /// Return instructions for enabling Power BI admin API access.
    /// POST /v2/cloud-assessment/powerbi/connect
    /// </summary>
    [Function("PowerBiConsent_Connect")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Connect(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/cloud-assessment/powerbi/connect")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadFromJsonAsync<PowerBiConnectRequest>();
            if (body is null || body.OrganizationId == Guid.Empty)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
                return bad;
            }

            if (!await UserCanAccessOrg(body.OrganizationId))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            var appId = _m365Config.ClientId;

            // Upsert connection row with state=pending.
            var conn = await _db.CloudAssessmentPowerBiConnections
                .FirstOrDefaultAsync(c => c.OrganizationId == body.OrganizationId);

            if (conn is null)
            {
                conn = new CloudAssessmentPowerBiConnection
                {
                    OrganizationId = body.OrganizationId,
                    Enabled = false,
                    ConnectionState = "pending",
                    UpdatedAt = DateTime.UtcNow
                };
                _db.CloudAssessmentPowerBiConnections.Add(conn);
            }
            else
            {
                conn.ConnectionState = "pending";
                conn.ErrorMessage = null;
                conn.UpdatedAt = DateTime.UtcNow;
            }

            _db.Actlog.Add(new Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "cloud-assessment",
                Action = "powerbi.connect.requested",
                EntityType = "Organization",
                EntityId = body.OrganizationId.ToString(),
                Message = "Power BI connect instructions issued"
            });
            await _db.SaveChangesAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                appId,
                tenantSettingsUrl = "https://app.powerbi.com/admin-portal/tenantSettings",
                requiredPermissions = new[] { "Tenant.Read.All (Power BI Service)" },
                instructions = new[]
                {
                    "1. In Azure AD → App registrations → find the Kryoss app → API permissions → Add 'Tenant.Read.All' under Power BI Service → Grant admin consent.",
                    $"2. Go to Power BI Admin Portal → Tenant settings → Developer settings → Enable 'Service principals can use read-only admin APIs' → Add the Kryoss SPN (App ID: {appId}) or a security group containing it.",
                    "3. Come back here and click 'Verify' to confirm access."
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = ex.Message, inner = ex.InnerException?.Message });
            return err;
        }
    }

    /// <summary>
    /// Verify PBI admin API access by probing GET /admin/workspaces/modified?$top=1.
    /// POST /v2/cloud-assessment/powerbi/verify
    /// </summary>
    [Function("PowerBiConsent_Verify")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Verify(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/cloud-assessment/powerbi/verify")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadFromJsonAsync<PowerBiVerifyRequest>();
            if (body is null || body.OrganizationId == Guid.Empty)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
                return bad;
            }

            if (!await UserCanAccessOrg(body.OrganizationId))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            // Resolve the tenant ID from the M365 tenant connected to this org.
            var tenant = await _db.M365Tenants
                .Where(t => t.OrganizationId == body.OrganizationId)
                .Select(t => new { t.TenantId })
                .FirstOrDefaultAsync();

            if (tenant is null)
            {
                var resp = req.CreateResponse(HttpStatusCode.OK);
                await resp.WriteAsJsonAsync(new { connected = false, error = "No M365 tenant connected. Connect M365 first." });
                return resp;
            }

            string clientId = _m365Config.ClientId;
            string clientSecret = _m365Config.ClientSecret;

            // Acquire PBI token.
            AccessToken token;
            try
            {
                var credential = new ClientSecretCredential(tenant.TenantId, clientId, clientSecret);
                token = await credential.GetTokenAsync(
                    new TokenRequestContext(new[] { "https://analysis.windows.net/powerbi/api/.default" }),
                    CancellationToken.None);
            }
            catch (Exception tex)
            {
                var reason = $"Power BI token acquisition failed: {tex.Message}";
                await UpsertConnectionState(body.OrganizationId, "failed", reason);
                await LogVerify(body.OrganizationId, "warn", $"PBI verify failed: {reason}");
                var resp = req.CreateResponse(HttpStatusCode.OK);
                await resp.WriteAsJsonAsync(new { connected = false, error = reason });
                return resp;
            }

            // Probe PBI Admin API.
            using var http = new HttpClient { BaseAddress = new Uri("https://api.powerbi.com") };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var probeResp = await http.GetAsync("/v1.0/myorg/admin/groups?$top=1");

            if (!probeResp.IsSuccessStatusCode)
            {
                var status = (int)probeResp.StatusCode;
                string diagnostic = status == 403
                    ? "403 Forbidden — the tenant setting 'Service principals can use read-only admin APIs' is not enabled, or the Kryoss SPN is not in the allow-list."
                    : $"Power BI Admin API returned HTTP {status}";

                await UpsertConnectionState(body.OrganizationId, "failed", diagnostic);
                await LogVerify(body.OrganizationId, "warn", $"PBI verify failed: {diagnostic}");
                var resp = req.CreateResponse(HttpStatusCode.OK);
                await resp.WriteAsJsonAsync(new { connected = false, error = diagnostic });
                return resp;
            }

            // Success.
            await UpsertConnectionState(body.OrganizationId, "connected", null);
            await LogVerify(body.OrganizationId, "info", "PBI verify success");

            var okResp = req.CreateResponse(HttpStatusCode.OK);
            await okResp.WriteAsJsonAsync(new { connected = true });
            return okResp;
        }
        catch (Exception ex)
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = ex.Message, inner = ex.InnerException?.Message });
            return err;
        }
    }

    /// <summary>
    /// Get Power BI connection status for an org.
    /// GET /v2/cloud-assessment/powerbi/connection?organizationId={guid}
    /// </summary>
    [Function("PowerBiConsent_GetConnection")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetConnection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/powerbi/connection")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        if (!await UserCanAccessOrg(orgId))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        var conn = await _db.CloudAssessmentPowerBiConnections
            .Where(c => c.OrganizationId == orgId)
            .Select(c => new
            {
                c.OrganizationId, c.Enabled, c.ConnectionState,
                c.LastVerifiedAt, c.ErrorMessage, c.UpdatedAt
            })
            .FirstOrDefaultAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(conn ?? (object)new { connectionState = "none" });
        return response;
    }

    /// <summary>
    /// Disconnect Power BI governance pipeline for an org.
    /// DELETE /v2/cloud-assessment/powerbi/connection?organizationId={guid}
    /// </summary>
    [Function("PowerBiConsent_Disconnect")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Disconnect(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v2/cloud-assessment/powerbi/connection")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        if (!await UserCanAccessOrg(orgId))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        var conn = await _db.CloudAssessmentPowerBiConnections
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId);

        if (conn is not null)
        {
            conn.Enabled = false;
            conn.ConnectionState = "disconnected";
            conn.UpdatedAt = DateTime.UtcNow;
        }

        _db.Actlog.Add(new Actlog
        {
            Timestamp = DateTime.UtcNow,
            Severity = "info",
            Module = "cloud-assessment",
            Action = "powerbi.disconnected",
            EntityType = "Organization",
            EntityId = orgId.ToString(),
            Message = "Power BI governance disconnected"
        });
        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { disconnected = true });
        return response;
    }

    // ── Helpers ──

    private async Task UpsertConnectionState(Guid orgId, string state, string? error)
    {
        var conn = await _db.CloudAssessmentPowerBiConnections
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId);

        if (conn is null)
        {
            conn = new CloudAssessmentPowerBiConnection
            {
                OrganizationId = orgId,
                ConnectionState = state,
                Enabled = state == "connected",
                LastVerifiedAt = state == "connected" ? DateTime.UtcNow : null,
                ErrorMessage = error,
                UpdatedAt = DateTime.UtcNow
            };
            _db.CloudAssessmentPowerBiConnections.Add(conn);
        }
        else
        {
            conn.ConnectionState = state;
            conn.Enabled = state == "connected";
            conn.ErrorMessage = error;
            conn.UpdatedAt = DateTime.UtcNow;
            if (state == "connected")
                conn.LastVerifiedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    private async Task LogVerify(Guid orgId, string severity, string message)
    {
        _db.Actlog.Add(new Actlog
        {
            Timestamp = DateTime.UtcNow,
            Severity = severity,
            Module = "cloud-assessment",
            Action = "powerbi.verify.attempted",
            EntityType = "Organization",
            EntityId = orgId.ToString(),
            Message = message
        });
        await _db.SaveChangesAsync();
    }

    private async Task<bool> UserCanAccessOrg(Guid orgId)
    {
        if (_user.IsAdmin) return true;
        if (_user.FranchiseId.HasValue)
            return await _db.Organizations.AnyAsync(o => o.Id == orgId && o.FranchiseId == _user.FranchiseId.Value);
        return _user.OrganizationId.HasValue && orgId == _user.OrganizationId.Value;
    }

    // ── Request DTOs ──
    private record PowerBiConnectRequest(Guid OrganizationId);
    private record PowerBiVerifyRequest(Guid OrganizationId);
}
