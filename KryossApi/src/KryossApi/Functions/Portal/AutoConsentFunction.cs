using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using KryossApi.Services.CloudAssessment;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace KryossApi.Functions.Portal;

public class AutoConsentFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly M365Config _config;
    private readonly IFabricAdminService _fabric;
    private readonly ILogger<AutoConsentFunction> _log;

    private const string ReaderRoleDefinitionId = "acdd72a7-3385-48ef-bd42-f606fba81ae7";

    public AutoConsentFunction(
        KryossDbContext db,
        ICurrentUserService user,
        M365Config config,
        IFabricAdminService fabric,
        ILogger<AutoConsentFunction> log)
    {
        _db = db;
        _user = user;
        _config = config;
        _fabric = fabric;
        _log = log;
    }

    // ── Track A: Power BI / Fabric Admin auto-enable ──

    [Function("AutoConsent_PbiUrl")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> PbiAutoConsentUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/powerbi/auto-enable-url")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var redirectUri = Uri.EscapeDataString(
            $"{_config.CallbackBaseUrl.TrimEnd('/')}/v2/cloud-assessment/powerbi/auto-enable-callback");
        var scope = Uri.EscapeDataString(
            "https://api.fabric.microsoft.com/Tenant.ReadWrite.All offline_access");

        var url = $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(_config.ClientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={redirectUri}" +
            $"&scope={scope}" +
            $"&prompt=consent" +
            $"&state={Uri.EscapeDataString($"pbi|{orgId}")}";

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { url });
        return res;
    }

    [Function("AutoConsent_PbiCallback")]
    public async Task<HttpResponseData> PbiAutoConsentCallback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/powerbi/auto-enable-callback")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var state = query["state"];
        var code = query["code"];
        var error = query["error"];

        var orgId = ParseOrgId(state);
        var orgSlug = await ResolveOrgSlug(orgId);

        if (!string.IsNullOrEmpty(error))
        {
            var desc = query["error_description"] ?? error;
            return Redirect(req, BuildRedirect(orgSlug, $"pbi_error={Uri.EscapeDataString(desc)}"));
        }

        if (string.IsNullOrEmpty(code))
            return Redirect(req, BuildRedirect(orgSlug, "pbi_error=missing_code"));

        try
        {
            var redirectUri = $"{_config.CallbackBaseUrl.TrimEnd('/')}/v2/cloud-assessment/powerbi/auto-enable-callback";
            var cca = ConfidentialClientApplicationBuilder
                .Create(_config.ClientId)
                .WithClientSecret(_config.ClientSecret)
                .WithAuthority("https://login.microsoftonline.com/common/v2.0")
                .WithRedirectUri(redirectUri)
                .Build();

            var authResult = await cca.AcquireTokenByAuthorizationCode(
                new[] { "https://api.fabric.microsoft.com/Tenant.ReadWrite.All" }, code)
                .ExecuteAsync();

            var tenantId = authResult.TenantId;
            var fabricToken = authResult.AccessToken;

            var spnObjectId = await ResolveSpnObjectId(tenantId);

            var result = await _fabric.EnableServicePrincipalAccessAsync(
                fabricToken, spnObjectId ?? _config.ClientId);

            if (!result.Success)
            {
                _log.LogWarning("Fabric auto-enable failed for org {OrgId}: {Error}", orgId, result.Error);
                return Redirect(req, BuildRedirect(orgSlug, $"pbi_error={Uri.EscapeDataString(result.Error ?? "unknown")}"));
            }

            // Update PBI connection state
            await UpsertPbiConnection(orgId, "connected", null);

            _db.Actlog.Add(new Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "cloud-assessment",
                Action = "powerbi.auto-enable.success",
                EntityType = "Organization",
                EntityId = orgId.ToString(),
                Message = $"Fabric ServicePrincipalAccess auto-enabled for tenant {tenantId}"
            });
            await _db.SaveChangesAsync();

            return Redirect(req, BuildRedirect(orgSlug, "pbi_autoenabled=true"));
        }
        catch (MsalException ex)
        {
            _log.LogWarning("PBI auto-enable token exchange failed: {Error}", ex.Message);
            return Redirect(req, BuildRedirect(orgSlug, $"pbi_error={Uri.EscapeDataString(ex.Message)}"));
        }
    }

    // ── Track B: Standalone Azure auto-assign Reader ──

    [Function("AutoConsent_AzureUrl")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> AzureAutoConsentUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/azure/auto-assign-url")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var tenant = await _db.M365Tenants
            .Where(t => t.OrganizationId == orgId)
            .Select(t => t.TenantId)
            .FirstOrDefaultAsync();

        var authority = string.IsNullOrEmpty(tenant) ? "common" : tenant;

        var redirectUri = Uri.EscapeDataString(
            $"{_config.CallbackBaseUrl.TrimEnd('/')}/v2/cloud-assessment/azure/auto-assign-callback");
        var scope = Uri.EscapeDataString(
            "https://management.azure.com/user_impersonation offline_access");

        var url = $"https://login.microsoftonline.com/{authority}/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(_config.ClientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={redirectUri}" +
            $"&scope={scope}" +
            $"&state={Uri.EscapeDataString($"azauto|{orgId}")}";

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { url });
        return res;
    }

    [Function("AutoConsent_AzureCallback")]
    public async Task<HttpResponseData> AzureAutoConsentCallback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/azure/auto-assign-callback")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var state = query["state"];
        var code = query["code"];
        var error = query["error"];

        var orgId = ParseOrgId(state);
        var orgSlug = await ResolveOrgSlug(orgId);

        if (!string.IsNullOrEmpty(error))
        {
            var desc = query["error_description"] ?? error;
            return Redirect(req, BuildRedirect(orgSlug, $"azure_error={Uri.EscapeDataString(desc)}"));
        }

        if (string.IsNullOrEmpty(code))
            return Redirect(req, BuildRedirect(orgSlug, "azure_error=missing_code"));

        try
        {
            var tenant = await _db.M365Tenants
                .Where(t => t.OrganizationId == orgId)
                .Select(t => t.TenantId)
                .FirstOrDefaultAsync() ?? "common";

            var redirectUri = $"{_config.CallbackBaseUrl.TrimEnd('/')}/v2/cloud-assessment/azure/auto-assign-callback";
            var cca = ConfidentialClientApplicationBuilder
                .Create(_config.ClientId)
                .WithClientSecret(_config.ClientSecret)
                .WithAuthority($"https://login.microsoftonline.com/{tenant}/v2.0")
                .WithRedirectUri(redirectUri)
                .Build();

            var authResult = await cca.AcquireTokenByAuthorizationCode(
                new[] { "https://management.azure.com/user_impersonation" }, code)
                .ExecuteAsync();

            var armToken = authResult.AccessToken;
            var tenantId = authResult.TenantId;

            // Discover subs + assign Reader
            var (subsCount, failed, note) = await AutoAssignReader(armToken, tenantId, orgId);

            _db.Actlog.Add(new Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "cloud-assessment",
                Action = "azure.auto-assign.completed",
                EntityType = "Organization",
                EntityId = orgId.ToString(),
                Message = $"Azure auto-assign: {subsCount} subs, failed={failed}"
            });
            await _db.SaveChangesAsync();

            var qs = $"azure_autoassigned=true&azure_subs={subsCount}&azure_failed={failed}";
            if (!string.IsNullOrEmpty(note))
                qs += $"&azure_note={Uri.EscapeDataString(note)}";
            return Redirect(req, BuildRedirect(orgSlug, qs));
        }
        catch (MsalException ex)
        {
            _log.LogWarning("Azure auto-assign token exchange failed: {Error}", ex.Message);
            return Redirect(req, BuildRedirect(orgSlug, $"azure_error={Uri.EscapeDataString(ex.Message)}"));
        }
    }

    private async Task<(int subsCount, bool failed, string? note)> AutoAssignReader(
        string armToken, string tenantId, Guid orgId)
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://management.azure.com") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", armToken);

        using var subsResp = await http.GetAsync("/subscriptions?api-version=2022-12-01");
        if (!subsResp.IsSuccessStatusCode)
            return (0, true, $"ARM returned {(int)subsResp.StatusCode}");

        var subscriptions = new List<(string SubId, string? DisplayName, string? State)>();
        await using (var stream = await subsResp.Content.ReadAsStreamAsync())
        using (var doc = await JsonDocument.ParseAsync(stream))
        {
            if (doc.RootElement.TryGetProperty("value", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var subId = item.TryGetProperty("subscriptionId", out var s) ? s.GetString() : null;
                    var name = item.TryGetProperty("displayName", out var n) ? n.GetString() : null;
                    var st = item.TryGetProperty("state", out var sv) ? sv.GetString() : null;
                    if (!string.IsNullOrEmpty(subId))
                        subscriptions.Add((subId!, name, st));
                }
            }
        }

        if (subscriptions.Count == 0)
            return (0, false, "No subscriptions visible");

        var spnObjectId = await ResolveSpnObjectId(tenantId);
        if (spnObjectId is null)
        {
            await UpsertAzureSubs(orgId, tenantId, subscriptions);
            return (subscriptions.Count, true, "SPN not found in tenant — role not assigned");
        }

        bool anyFailed = false;
        foreach (var (subId, _, _) in subscriptions)
        {
            var roleDefId = $"/subscriptions/{subId}/providers/Microsoft.Authorization/roleDefinitions/{ReaderRoleDefinitionId}";
            var body = JsonSerializer.Serialize(new
            {
                properties = new
                {
                    roleDefinitionId = roleDefId,
                    principalId = spnObjectId,
                    principalType = "ServicePrincipal"
                }
            });

            var url = $"/subscriptions/{subId}/providers/Microsoft.Authorization/roleAssignments/{Guid.NewGuid()}?api-version=2022-04-01";
            using var putResp = await http.PutAsync(url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            if (!putResp.IsSuccessStatusCode && putResp.StatusCode != HttpStatusCode.Conflict)
                anyFailed = true;
        }

        await UpsertAzureSubs(orgId, tenantId, subscriptions);
        return (subscriptions.Count, anyFailed, anyFailed ? "Reader role failed on some subs" : null);
    }

    private async Task UpsertAzureSubs(Guid orgId, string tenantId,
        List<(string SubId, string? DisplayName, string? State)> subs)
    {
        var now = DateTime.UtcNow;
        var existing = await _db.CloudAssessmentAzureSubscriptions
            .Where(s => s.OrganizationId == orgId)
            .ToListAsync();

        foreach (var (subId, name, state) in subs)
        {
            var row = existing.FirstOrDefault(r =>
                string.Equals(r.SubscriptionId, subId, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                _db.CloudAssessmentAzureSubscriptions.Add(new CloudAssessmentAzureSubscription
                {
                    OrganizationId = orgId,
                    SubscriptionId = subId,
                    DisplayName = name,
                    State = state,
                    TenantId = tenantId,
                    ConsentState = "connected",
                    ConnectedAt = now,
                    LastVerifiedAt = now,
                    CreatedAt = now,
                });
            }
            else
            {
                row.ConsentState = "connected";
                row.LastVerifiedAt = now;
                row.ErrorMessage = null;
            }
        }
        await _db.SaveChangesAsync();
    }

    private async Task UpsertPbiConnection(Guid orgId, string state, string? error)
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
                LastVerifiedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            _db.CloudAssessmentPowerBiConnections.Add(conn);
        }
        else
        {
            conn.ConnectionState = state;
            conn.Enabled = state == "connected";
            conn.ErrorMessage = error;
            conn.LastVerifiedAt = DateTime.UtcNow;
            conn.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    private async Task<string?> ResolveSpnObjectId(string tenantId)
    {
        try
        {
            var credential = new Azure.Identity.ClientSecretCredential(tenantId, _config.ClientId, _config.ClientSecret);
            var graph = new Microsoft.Graph.GraphServiceClient(credential);
            var result = await graph.ServicePrincipals.GetAsync(r =>
                r.QueryParameters.Filter = $"appId eq '{_config.ClientId}'");
            return result?.Value?.FirstOrDefault()?.Id;
        }
        catch { return null; }
    }

    private static Guid ParseOrgId(string? state)
    {
        if (string.IsNullOrEmpty(state)) return Guid.Empty;
        var parts = state.Split('|');
        return parts.Length > 1 && Guid.TryParse(parts[1], out var id) ? id : Guid.Empty;
    }

    private async Task<string> ResolveOrgSlug(Guid orgId)
    {
        if (orgId == Guid.Empty) return "unknown";
        var name = await _db.Organizations
            .Where(o => o.Id == orgId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync();
        return System.Text.RegularExpressions.Regex
            .Replace((name ?? "unknown").ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
    }

    private string BuildRedirect(string slug, string qs)
    {
        var baseUrl = _config.PortalBaseUrl.TrimEnd('/');
        return $"{baseUrl}/organizations/{Uri.EscapeDataString(slug)}/cloud-assessment?{qs}";
    }

    private static HttpResponseData Redirect(HttpRequestData req, string url)
    {
        var resp = req.CreateResponse(HttpStatusCode.Redirect);
        resp.Headers.Add("Location", url);
        return resp;
    }
}
