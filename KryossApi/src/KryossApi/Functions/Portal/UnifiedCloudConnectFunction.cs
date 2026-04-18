using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
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

/// <summary>
/// Unified cloud connect endpoints: single OAuth2 auth-code flow that
/// connects M365 (Graph), Azure ARM (Reader RBAC), and Power BI admin
/// in one redirect round-trip.
/// </summary>
public class UnifiedCloudConnectFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly M365Config _m365Config;
    private readonly ICloudAssessmentService _caService;
    private readonly ILogger<UnifiedCloudConnectFunction> _log;

    private const string ReaderRoleDefinitionId = "acdd72a7-3385-48ef-bd42-f606fba81ae7";

    public UnifiedCloudConnectFunction(
        KryossDbContext db,
        ICurrentUserService user,
        M365Config m365Config,
        ICloudAssessmentService caService,
        ILogger<UnifiedCloudConnectFunction> log)
    {
        _db = db;
        _user = user;
        _m365Config = m365Config;
        _caService = caService;
        _log = log;
    }

    // ── Endpoint 1: GET /v2/cloud/connect-url ──

    /// <summary>
    /// Returns the Microsoft /authorize URL that triggers admin consent for
    /// Graph + ARM + PBI permissions in a single redirect.
    /// GET /v2/cloud/connect-url?organizationId={guid}
    /// </summary>
    [Function("Cloud_ConnectUrl")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> ConnectUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud/connect-url")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        if (!await UserCanAccessOrg(orgId))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        var orgExists = await _db.Organizations.AnyAsync(o => o.Id == orgId);
        if (!orgExists)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Organization not found" });
            return notFound;
        }

        if (string.IsNullOrWhiteSpace(_m365Config.ClientId))
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = "Cloud scanner app registration not configured" });
            return err;
        }

        var redirectUri = Uri.EscapeDataString(
            $"{_m365Config.CallbackBaseUrl.TrimEnd('/')}/v2/cloud/connect-callback");

        var scope = Uri.EscapeDataString(
            "https://management.azure.com/user_impersonation offline_access");

        var url = $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(_m365Config.ClientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={redirectUri}" +
            $"&scope={scope}" +
            $"&prompt=admin_consent" +
            $"&state={orgId}";

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { url });
        return response;
    }

    // ── Endpoint 2: GET /v2/cloud/connect-callback ──

    /// <summary>
    /// Browser redirect callback from Microsoft after admin consent + auth code grant.
    /// Exchanges code for ARM token, creates M365Tenant, auto-assigns Azure Reader,
    /// verifies PBI, starts cloud assessment scan, redirects to portal.
    /// No auth required (browser redirect from Microsoft).
    /// </summary>
    [Function("Cloud_ConnectCallback")]
    public async Task<HttpResponseData> ConnectCallback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud/connect-callback")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

        var errorParam = query["error"];
        var errorDesc = query["error_description"];
        var stateParam = query["state"];
        var code = query["code"];
        var tenantParam = query["tenant"];

        Guid.TryParse(stateParam, out var orgId);

        // Handle consent denial from Microsoft
        if (!string.IsNullOrEmpty(errorParam))
        {
            var slug = await ResolveOrgSlug(orgId);
            var redirectUrl = BuildPortalRedirect(slug, "cloud-assessment",
                $"error={Uri.EscapeDataString(errorDesc ?? errorParam)}");
            return Redirect(req, redirectUrl);
        }

        // Validate required params
        if (string.IsNullOrWhiteSpace(code) || orgId == Guid.Empty || string.IsNullOrWhiteSpace(tenantParam))
        {
            var slug = await ResolveOrgSlug(orgId);
            return Redirect(req, BuildPortalRedirect(slug, "cloud-assessment",
                $"error={Uri.EscapeDataString("Invalid callback parameters: code, state, and tenant are required")}"));
        }

        // Verify org exists
        var org = await _db.Organizations
            .Where(o => o.Id == orgId)
            .Select(o => new { o.Id, o.Name })
            .FirstOrDefaultAsync();

        if (org is null)
        {
            return Redirect(req, BuildPortalRedirect("unknown", "cloud-assessment",
                $"error={Uri.EscapeDataString("Organization not found")}"));
        }

        var orgSlug = GenerateSlug(org.Name);
        var results = new ConnectResults();

        try
        {
            // Step 1: Exchange auth code for ARM delegated token via MSAL
            string armToken;
            try
            {
                var redirectUri = $"{_m365Config.CallbackBaseUrl.TrimEnd('/')}/v2/cloud/connect-callback";
                var cca = ConfidentialClientApplicationBuilder
                    .Create(_m365Config.ClientId)
                    .WithClientSecret(_m365Config.ClientSecret)
                    .WithAuthority($"https://login.microsoftonline.com/{tenantParam}/v2.0")
                    .WithRedirectUri(redirectUri)
                    .Build();

                var authResult = await cca.AcquireTokenByAuthorizationCode(
                    new[] { "https://management.azure.com/user_impersonation" }, code)
                    .ExecuteAsync();

                armToken = authResult.AccessToken;
            }
            catch (MsalException ex)
            {
                _log.LogWarning("MSAL token exchange failed for org {OrgId}: {Error}", orgId, ex.Message);
                return Redirect(req, BuildPortalRedirect(orgSlug, "cloud-assessment",
                    $"error={Uri.EscapeDataString($"Token exchange failed: {ex.Message}")}"));
            }

            // Step 2: Create/update M365Tenant row (consent flow — no per-customer creds)
            var existingTenant = await _db.M365Tenants
                .FirstOrDefaultAsync(t => t.OrganizationId == orgId);

            Guid m365TenantDbId;
            if (existingTenant is null)
            {
                var tenant = new M365Tenant
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = orgId,
                    TenantId = tenantParam,
                    Status = "active",
                    CreatedAt = DateTime.UtcNow,
                    ConsentGrantedAt = DateTime.UtcNow
                };
                _db.M365Tenants.Add(tenant);
                m365TenantDbId = tenant.Id;
            }
            else
            {
                existingTenant.TenantId = tenantParam;
                existingTenant.Status = "active";
                existingTenant.ConsentGrantedAt = DateTime.UtcNow;
                m365TenantDbId = existingTenant.Id;
            }

            results.M365Connected = true;
            await _db.SaveChangesAsync();

            _db.Actlog.Add(new Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "cloud-assessment",
                Action = "unified.connect.m365",
                EntityType = "Organization",
                EntityId = orgId.ToString(),
                Message = $"Unified connect: M365 tenant {tenantParam} connected"
            });
            await _db.SaveChangesAsync();

            // Step 3: Auto-assign Azure Reader on all subscriptions
            await TryAutoAssignAzureReader(armToken, tenantParam, orgId, results);

            // Step 4: Verify Power BI admin API access
            await TryVerifyPowerBi(tenantParam, orgId, results);

            // Step 5: Start cloud assessment scan
            try
            {
                var scanId = await _caService.StartScanAsync(orgId, m365TenantDbId);
                results.ScanId = scanId;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Cloud assessment scan failed for org {OrgId}: {Error}", orgId, ex.Message);
            }

            _db.Actlog.Add(new Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "cloud-assessment",
                Action = "unified.connect.completed",
                EntityType = "Organization",
                EntityId = orgId.ToString(),
                Message = $"Unified connect completed: m365={results.M365Connected}, azureSubs={results.AzureSubsConnected}, azureRbacFailed={results.AzureRbacFailed}, pbi={results.PbiConnected}"
            });
            await _db.SaveChangesAsync();

            // Build success redirect with all status params
            var queryParams = new List<string>
            {
                "cloud_connected=true",
                $"m365={results.M365Connected.ToString().ToLowerInvariant()}",
                $"azure_subs={results.AzureSubsConnected}",
                $"azure_rbac_failed={results.AzureRbacFailed.ToString().ToLowerInvariant()}",
                $"pbi={results.PbiConnected.ToString().ToLowerInvariant()}"
            };

            if (results.ScanId.HasValue)
                queryParams.Add($"scan_id={results.ScanId.Value}");

            if (!string.IsNullOrEmpty(results.PbiNote))
                queryParams.Add($"pbi_note={Uri.EscapeDataString(results.PbiNote)}");

            if (!string.IsNullOrEmpty(results.AzureNote))
                queryParams.Add($"azure_note={Uri.EscapeDataString(results.AzureNote)}");

            return Redirect(req, BuildPortalRedirect(orgSlug, "cloud-assessment",
                string.Join("&", queryParams)));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unified cloud connect failed for org {OrgId}", orgId);
            return Redirect(req, BuildPortalRedirect(orgSlug, "cloud-assessment",
                $"error={Uri.EscapeDataString($"Unexpected error: {ex.Message}")}"));
        }
    }

    // ── Helper: TryAutoAssignAzureReader ──

    private async Task TryAutoAssignAzureReader(string armToken, string tenantId, Guid orgId, ConnectResults results)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri("https://management.azure.com") };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", armToken);

            // List subscriptions
            using var subsResp = await http.GetAsync("/subscriptions?api-version=2022-12-01");
            if (!subsResp.IsSuccessStatusCode)
            {
                var status = (int)subsResp.StatusCode;
                results.AzureNote = $"ARM API returned {status} listing subscriptions";
                _log.LogWarning("ARM list subscriptions failed for org {OrgId}: HTTP {Status}", orgId, status);
                return;
            }

            var subscriptions = new List<(string SubId, string? DisplayName, string? State)>();
            await using (var stream = await subsResp.Content.ReadAsStreamAsync())
            using (var doc = await JsonDocument.ParseAsync(stream))
            {
                if (doc.RootElement.TryGetProperty("value", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var subId = item.TryGetProperty("subscriptionId", out var sid) ? sid.GetString() : null;
                        var displayName = item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
                        var state = item.TryGetProperty("state", out var st) ? st.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(subId))
                            subscriptions.Add((subId!, displayName, state));
                    }
                }
            }

            results.AzureSubsConnected = subscriptions.Count;

            if (subscriptions.Count == 0)
            {
                results.AzureNote = "Authenticated but no subscriptions visible";
                return;
            }

            // Resolve Kryoss SPN object ID in the customer tenant
            var spnObjectId = await TryResolveSpnObjectId(tenantId);
            if (spnObjectId is null)
            {
                results.AzureRbacFailed = true;
                results.AzureNote = "Could not resolve Kryoss SPN in tenant — Reader role not auto-assigned";
                // Still upsert subscription rows as connected (ARM access works)
                await UpsertAzureSubscriptions(orgId, tenantId, subscriptions);
                return;
            }

            // Assign Reader role on each subscription
            bool anyFailed = false;
            foreach (var (subId, _, _) in subscriptions)
            {
                try
                {
                    var roleAssignmentId = Guid.NewGuid();
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

                    var putUrl = $"/subscriptions/{subId}/providers/Microsoft.Authorization/roleAssignments/{roleAssignmentId}?api-version=2022-04-01";
                    var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                    using var putResp = await http.PutAsync(putUrl, content);

                    // 409 = already assigned → success
                    if (!putResp.IsSuccessStatusCode && putResp.StatusCode != HttpStatusCode.Conflict)
                    {
                        var putStatus = (int)putResp.StatusCode;
                        _log.LogWarning("Reader role assignment failed on sub {SubId}: HTTP {Status}", subId, putStatus);
                        anyFailed = true;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning("Reader role assignment exception on sub {SubId}: {Error}", subId, ex.Message);
                    anyFailed = true;
                }
            }

            results.AzureRbacFailed = anyFailed;
            if (anyFailed)
                results.AzureNote = "Reader role could not be assigned on some subscriptions (insufficient permissions)";

            await UpsertAzureSubscriptions(orgId, tenantId, subscriptions);
        }
        catch (Exception ex)
        {
            results.AzureRbacFailed = true;
            results.AzureNote = $"Azure auto-assign failed: {ex.Message}";
            _log.LogWarning("TryAutoAssignAzureReader failed for org {OrgId}: {Error}", orgId, ex.Message);
        }
    }

    private async Task UpsertAzureSubscriptions(Guid orgId, string tenantId,
        List<(string SubId, string? DisplayName, string? State)> subscriptions)
    {
        var now = DateTime.UtcNow;
        var existing = await _db.CloudAssessmentAzureSubscriptions
            .Where(s => s.OrganizationId == orgId)
            .ToListAsync();

        var returnedSubIds = subscriptions.Select(s => s.SubId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (subId, displayName, state) in subscriptions)
        {
            var row = existing.FirstOrDefault(r => string.Equals(r.SubscriptionId, subId, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new CloudAssessmentAzureSubscription
                {
                    OrganizationId = orgId,
                    SubscriptionId = subId,
                    DisplayName = displayName,
                    State = state,
                    TenantId = tenantId,
                    ConsentState = "connected",
                    ConnectedAt = now,
                    LastVerifiedAt = now,
                    ErrorMessage = null,
                    CreatedAt = now
                };
                _db.CloudAssessmentAzureSubscriptions.Add(row);
            }
            else
            {
                row.DisplayName = displayName;
                row.State = state;
                row.TenantId = tenantId;
                row.ConsentState = "connected";
                row.ConnectedAt ??= now;
                row.LastVerifiedAt = now;
                row.ErrorMessage = null;
            }
        }

        // Mark stale rows
        foreach (var stale in existing.Where(r => !returnedSubIds.Contains(r.SubscriptionId)))
        {
            stale.ConsentState = "failed";
            stale.ErrorMessage = "Subscription no longer accessible";
            stale.LastVerifiedAt = now;
        }

        await _db.SaveChangesAsync();
    }

    // ── Helper: TryVerifyPowerBi ──

    private async Task TryVerifyPowerBi(string tenantId, Guid orgId, ConnectResults results)
    {
        try
        {
            // Acquire PBI token via app-only (client credentials)
            AccessToken token;
            try
            {
                var credential = new ClientSecretCredential(tenantId, _m365Config.ClientId, _m365Config.ClientSecret);
                token = await credential.GetTokenAsync(
                    new TokenRequestContext(new[] { "https://analysis.windows.net/powerbi/api/.default" }),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                results.PbiNote = $"PBI token acquisition failed: {ex.Message}";
                await UpsertPbiConnection(orgId, "unavailable", results.PbiNote);
                return;
            }

            // Probe admin API
            using var http = new HttpClient { BaseAddress = new Uri("https://api.powerbi.com") };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var probeResp = await http.GetAsync("/v1.0/myorg/admin/groups?$top=1");

            if (probeResp.IsSuccessStatusCode)
            {
                results.PbiConnected = true;
                await UpsertPbiConnection(orgId, "connected", null);
            }
            else
            {
                var status = (int)probeResp.StatusCode;
                results.PbiNote = status == 403
                    ? "PBI admin API returned 403 — tenant setting not enabled or SPN not in allow-list"
                    : $"PBI admin API returned HTTP {status}";
                await UpsertPbiConnection(orgId, "unavailable", results.PbiNote);
            }
        }
        catch (Exception ex)
        {
            results.PbiNote = $"PBI verify failed: {ex.Message}";
            await UpsertPbiConnection(orgId, "unavailable", results.PbiNote);
            _log.LogWarning("TryVerifyPowerBi failed for org {OrgId}: {Error}", orgId, ex.Message);
        }
    }

    // ── Helper: UpsertPbiConnection ──

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

    // ── Helper: TryResolveSpnObjectId ──

    private async Task<string?> TryResolveSpnObjectId(string customerTenantId)
    {
        try
        {
            var credential = new ClientSecretCredential(customerTenantId, _m365Config.ClientId, _m365Config.ClientSecret);
            var graph = new Microsoft.Graph.GraphServiceClient(credential);
            var appId = _m365Config.ClientId;
            var result = await graph.ServicePrincipals.GetAsync(r =>
                r.QueryParameters.Filter = $"appId eq '{appId}'");

            var spn = result?.Value?.FirstOrDefault();
            if (spn is null)
            {
                _log.LogInformation("Kryoss SPN not found in tenant {TenantId}", customerTenantId);
                return null;
            }
            return spn.Id;
        }
        catch (Exception ex)
        {
            _log.LogInformation("SPN resolve failed for tenant {TenantId}: {Error}", customerTenantId, ex.Message);
            return null;
        }
    }

    // ── Helper: UserCanAccessOrg ──

    private async Task<bool> UserCanAccessOrg(Guid organizationId)
    {
        if (_user.IsAdmin) return true;
        if (_user.FranchiseId.HasValue)
            return await _db.Organizations.AnyAsync(o => o.Id == organizationId && o.FranchiseId == _user.FranchiseId.Value);
        return _user.OrganizationId.HasValue && organizationId == _user.OrganizationId.Value;
    }

    // ── Helper: slug + redirect ──

    private static string GenerateSlug(string name)
    {
        return System.Text.RegularExpressions.Regex
            .Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-")
            .Trim('-');
    }

    private async Task<string> ResolveOrgSlug(Guid orgId)
    {
        if (orgId == Guid.Empty) return "unknown";
        var orgName = await _db.Organizations
            .Where(o => o.Id == orgId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync();
        return GenerateSlug(orgName ?? "unknown");
    }

    private string BuildPortalRedirect(string orgSlug, string page, string? queryString)
    {
        var baseUrl = _m365Config.PortalBaseUrl.TrimEnd('/');
        var path = $"/organizations/{Uri.EscapeDataString(orgSlug)}/{page}";
        return string.IsNullOrEmpty(queryString) ? $"{baseUrl}{path}" : $"{baseUrl}{path}?{queryString}";
    }

    private static HttpResponseData Redirect(HttpRequestData req, string url)
    {
        var response = req.CreateResponse(HttpStatusCode.Redirect);
        response.Headers.Add("Location", url);
        return response;
    }

    // ── Internal DTO ──

    private class ConnectResults
    {
        public bool M365Connected { get; set; }
        public int AzureSubsConnected { get; set; }
        public bool AzureRbacFailed { get; set; }
        public bool PbiConnected { get; set; }
        public Guid? ScanId { get; set; }
        public string? PbiNote { get; set; }
        public string? AzureNote { get; set; }
    }
}
