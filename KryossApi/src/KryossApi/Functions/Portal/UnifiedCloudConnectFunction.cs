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
            "https://graph.microsoft.com/.default offline_access");

        var url = $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(_m365Config.ClientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={redirectUri}" +
            $"&scope={scope}" +
            $"&prompt=select_account" +
            $"&state={Uri.EscapeDataString($"graph|{orgId}")}";

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { url });
        return response;
    }

    // ── Endpoint 2: GET /v2/cloud/connect-callback ──

    /// <summary>
    /// Two-phase browser redirect callback:
    /// Phase 1: state="graph|{orgId}" → exchange code for Graph token (proves consent), save M365, redirect to /authorize for ARM
    /// Phase 2: state="arm|{orgId}|{tenantId}" → exchange code for ARM token, discover subs, verify PBI, start scan
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

        // Handle consent denial from Microsoft
        if (!string.IsNullOrEmpty(errorParam))
        {
            var orgIdForError = ParseOrgIdFromState(stateParam);
            var slug = await ResolveOrgSlug(orgIdForError);
            return Redirect(req, BuildPortalRedirect(slug, "cloud-assessment",
                $"error={Uri.EscapeDataString(errorDesc ?? errorParam)}"));
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrWhiteSpace(stateParam))
        {
            var fallbackSlug = await ResolveOrgSlug(ParseOrgIdFromState(stateParam));
            return Redirect(req, BuildPortalRedirect(fallbackSlug, "cloud-assessment",
                $"error={Uri.EscapeDataString("Invalid callback: missing code or state")}"));
        }

        // Route by state prefix
        if (stateParam.StartsWith("graph|"))
            return await HandleGraphConsentCallback(req, stateParam, code);

        if (stateParam.StartsWith("arm|"))
            return await HandleAuthCodeCallback(req, stateParam, code);

        var slug2 = await ResolveOrgSlug(ParseOrgIdFromState(stateParam));
        return Redirect(req, BuildPortalRedirect(slug2, "cloud-assessment",
            $"error={Uri.EscapeDataString("Invalid callback state")}"));
    }

    // ── Phase 1: Graph consent code → exchange token, extract tenant, save M365, redirect to ARM ──

    private async Task<HttpResponseData> HandleGraphConsentCallback(
        HttpRequestData req, string stateParam, string code)
    {
        // state = "graph|{orgId}"
        var parts = stateParam.Split('|');
        var orgId = parts.Length > 1 && Guid.TryParse(parts[1], out var id) ? id : Guid.Empty;

        if (orgId == Guid.Empty)
        {
            var slug = await ResolveOrgSlug(orgId);
            return Redirect(req, BuildPortalRedirect(slug, "cloud-assessment",
                $"error={Uri.EscapeDataString("Invalid Graph callback: missing orgId in state")}"));
        }

        var org = await _db.Organizations
            .Where(o => o.Id == orgId)
            .Select(o => new { o.Id, o.Name })
            .FirstOrDefaultAsync();

        if (org is null)
            return Redirect(req, BuildPortalRedirect("unknown", "cloud-assessment",
                $"error={Uri.EscapeDataString("Organization not found")}"));

        // Exchange code for Graph token to extract tenant ID
        string tenantId;
        try
        {
            var redirectUri = $"{_m365Config.CallbackBaseUrl.TrimEnd('/')}/v2/cloud/connect-callback";
            var cca = ConfidentialClientApplicationBuilder
                .Create(_m365Config.ClientId)
                .WithClientSecret(_m365Config.ClientSecret)
                .WithAuthority("https://login.microsoftonline.com/common/v2.0")
                .WithRedirectUri(redirectUri)
                .Build();

            var authResult = await cca.AcquireTokenByAuthorizationCode(
                new[] { "https://graph.microsoft.com/.default" }, code)
                .ExecuteAsync();

            tenantId = authResult.TenantId;
        }
        catch (MsalException ex)
        {
            _log.LogWarning("Graph token exchange failed for org {OrgId}: {Error}", orgId, ex.Message);
            var slug = GenerateSlug(org.Name);
            return Redirect(req, BuildPortalRedirect(slug, "cloud-assessment",
                $"error={Uri.EscapeDataString($"Graph consent failed: {ex.Message}")}"));
        }

        // Save M365 tenant
        var existingTenant = await _db.M365Tenants
            .FirstOrDefaultAsync(t => t.OrganizationId == orgId);

        if (existingTenant is null)
        {
            _db.M365Tenants.Add(new M365Tenant
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                TenantId = tenantId,
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                ConsentGrantedAt = DateTime.UtcNow
            });
        }
        else
        {
            existingTenant.TenantId = tenantId;
            existingTenant.Status = "active";
            existingTenant.ConsentGrantedAt = DateTime.UtcNow;
        }

        _db.Actlog.Add(new Actlog
        {
            Timestamp = DateTime.UtcNow,
            Severity = "info",
            Module = "cloud-assessment",
            Action = "unified.connect.graph",
            EntityType = "Organization",
            EntityId = orgId.ToString(),
            Message = $"Unified connect phase 1: Graph consent for tenant {tenantId}"
        });
        await _db.SaveChangesAsync();

        // Programmatic consent for optional APIs (Defender, etc.)
        // Graph is already consented; try to provision service principals for non-Graph APIs
        // so client_credentials can access them during the scan.
        await TryGrantOptionalApiPermissions(tenantId, orgId);

        // Redirect to /authorize for ARM delegated token
        var armRedirectUri = Uri.EscapeDataString(
            $"{_m365Config.CallbackBaseUrl.TrimEnd('/')}/v2/cloud/connect-callback");
        var armScope = Uri.EscapeDataString(
            "https://management.azure.com/user_impersonation offline_access");

        var authorizeUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(_m365Config.ClientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={armRedirectUri}" +
            $"&scope={armScope}" +
            $"&state={Uri.EscapeDataString($"arm|{orgId}|{tenantId}")}";

        _log.LogInformation("Phase 1 complete for org {OrgId}, tenant {TenantId}, redirecting for ARM token", orgId, tenantId);
        return Redirect(req, authorizeUrl);
    }

    // ── Phase 2: Auth code received → exchange for ARM token, discover subs, PBI, scan ──

    private async Task<HttpResponseData> HandleAuthCodeCallback(
        HttpRequestData req, string? stateParam, string code)
    {
        var (orgId, tenantId) = ParseArmState(stateParam);

        if (orgId == Guid.Empty || string.IsNullOrWhiteSpace(tenantId))
        {
            var slug = await ResolveOrgSlug(orgId);
            return Redirect(req, BuildPortalRedirect(slug, "cloud-assessment",
                $"error={Uri.EscapeDataString("Invalid auth code callback: missing orgId or tenantId in state")}"));
        }

        var org = await _db.Organizations
            .Where(o => o.Id == orgId)
            .Select(o => new { o.Id, o.Name })
            .FirstOrDefaultAsync();

        if (org is null)
            return Redirect(req, BuildPortalRedirect("unknown", "cloud-assessment",
                $"error={Uri.EscapeDataString("Organization not found")}"));

        var orgSlug = GenerateSlug(org.Name);
        var results = new ConnectResults { M365Connected = true };

        try
        {
            // Exchange code for ARM delegated token
            string armToken;
            try
            {
                var redirectUri = $"{_m365Config.CallbackBaseUrl.TrimEnd('/')}/v2/cloud/connect-callback";
                var cca = ConfidentialClientApplicationBuilder
                    .Create(_m365Config.ClientId)
                    .WithClientSecret(_m365Config.ClientSecret)
                    .WithAuthority($"https://login.microsoftonline.com/{tenantId}/v2.0")
                    .WithRedirectUri(redirectUri)
                    .Build();

                var authResult = await cca.AcquireTokenByAuthorizationCode(
                    new[] { "https://management.azure.com/user_impersonation" }, code)
                    .ExecuteAsync();

                armToken = authResult.AccessToken;
            }
            catch (MsalException ex)
            {
                _log.LogWarning("ARM token exchange failed for org {OrgId}: {Error}", orgId, ex.Message);
                results.AzureNote = $"ARM token exchange failed: {ex.Message}";
                return await BuildFinalRedirect(req, orgId, orgSlug, tenantId, results);
            }

            // Discover Azure subscriptions with delegated ARM token
            await TryAutoAssignAzureReader(armToken, tenantId, orgId, results);

            await TryVerifyPowerBi(tenantId, orgId, results);

            // Start scan
            var m365Tenant = await _db.M365Tenants
                .FirstOrDefaultAsync(t => t.OrganizationId == orgId);
            if (m365Tenant is not null)
            {
                try
                {
                    var scanId = await _caService.StartScanAsync(orgId, m365Tenant.Id);
                    results.ScanId = scanId;
                }
                catch (Exception ex)
                {
                    _log.LogWarning("Cloud assessment scan failed for org {OrgId}: {Error}", orgId, ex.Message);
                }
            }

            return await BuildFinalRedirect(req, orgId, orgSlug, tenantId, results);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unified cloud connect phase 2 failed for org {OrgId}", orgId);
            return Redirect(req, BuildPortalRedirect(orgSlug, "cloud-assessment",
                $"error={Uri.EscapeDataString($"Unexpected error: {ex.Message}")}"));
        }
    }

    private async Task<HttpResponseData> BuildFinalRedirect(
        HttpRequestData req, Guid orgId, string orgSlug, string tenantId, ConnectResults results)
    {
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

    // ── Helper: TryGrantOptionalApiPermissions ──
    // After Graph consent, try to programmatically grant app permissions for
    // optional APIs (Defender, etc.) using the Graph API. If the target API
    // doesn't exist in the tenant (no license), this silently skips.

    private static readonly (string AppId, string[] RoleIds, string Name)[] OptionalApis = new[]
    {
        // Microsoft Threat Protection (Defender for Endpoint)
        // App roles: AdvancedHunting.Read.All, Machine.Read.All, Vulnerability.Read.All,
        //            SecurityRecommendation.Read.All, Software.Read.All, Score.Read.All
        ("8ee8fdad-f234-4243-8f3b-15c294843740", new[]
        {
            "dd98c7f5-2d42-42d3-a0e4-633161547251", // AdvancedHunting.Read.All
            "ea8291d3-4b9a-44b5-bc3a-6cea3026dc79", // Machine.Read.All
            "41269fc5-d04d-4bfd-80a5-f8fee4f3e55e", // Vulnerability.Read.All
            "6443965c-7b2c-4150-a6d4-e52a03704da6", // SecurityRecommendation.Read.All
            "37a27590-87eb-4bb5-8a10-85e0ff394ef9", // Software.Read.All
            "02a4a1f0-20e0-4e25-be0e-55568da41a31", // Score.Read.All
        }, "Microsoft Threat Protection"),

        // Office 365 Exchange Online — Exchange.ManageAsApp
        ("00000002-0000-0ff1-ce00-000000000000", new[]
        {
            "dc50a0fb-09a3-484d-be87-e023b12c6440", // Exchange.ManageAsApp
        }, "Office 365 Exchange Online"),

        // Power BI Service — Tenant.Read.All
        ("00000009-0000-0000-c000-000000000000", new[]
        {
            "654b31ae-d941-4e22-8798-7add8fdf049f", // Tenant.Read.All
        }, "Power BI Service"),
    };

    // Exchange Administrator role template ID for directory role assignment.
    private const string ExchangeAdminRoleTemplateId = "29232cdf-9323-42fd-ade2-1d097af3e4de";

    private async Task TryGrantOptionalApiPermissions(string tenantId, Guid orgId)
    {
        try
        {
            var credential = new ClientSecretCredential(tenantId, _m365Config.ClientId, _m365Config.ClientSecret);
            using var http = new HttpClient();
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            http.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");

            // Resolve our app's service principal in the target tenant
            var ourSpnResp = await http.GetAsync(
                $"servicePrincipals?$filter=appId eq '{_m365Config.ClientId}'&$select=id");
            if (!ourSpnResp.IsSuccessStatusCode) return;
            using var ourSpnDoc = await JsonDocument.ParseAsync(await ourSpnResp.Content.ReadAsStreamAsync());
            var ourSpnId = ourSpnDoc.RootElement.GetProperty("value").EnumerateArray()
                .FirstOrDefault().TryGetProperty("id", out var spId) ? spId.GetString() : null;
            if (ourSpnId is null) return;

            foreach (var (apiAppId, roleIds, apiName) in OptionalApis)
            {
                try
                {
                    // Check if the API's service principal exists in the tenant
                    var apiSpnResp = await http.GetAsync(
                        $"servicePrincipals?$filter=appId eq '{apiAppId}'&$select=id");
                    if (!apiSpnResp.IsSuccessStatusCode) continue;
                    using var apiSpnDoc = await JsonDocument.ParseAsync(await apiSpnResp.Content.ReadAsStreamAsync());
                    var apiSpnArray = apiSpnDoc.RootElement.GetProperty("value");
                    if (apiSpnArray.GetArrayLength() == 0)
                    {
                        _log.LogInformation("Optional API {Api} not found in tenant {Tenant} — skipping", apiName, tenantId);
                        continue;
                    }
                    var apiSpnId = apiSpnArray[0].GetProperty("id").GetString()!;

                    // Grant each app role
                    foreach (var roleId in roleIds)
                    {
                        var body = JsonSerializer.Serialize(new
                        {
                            principalId = ourSpnId,
                            resourceId = apiSpnId,
                            appRoleId = roleId
                        });
                        var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                        var resp = await http.PostAsync($"servicePrincipals/{ourSpnId}/appRoleAssignments", content);
                        // 409 = already assigned, that's fine
                    }

                    _log.LogInformation("Granted {Api} permissions in tenant {Tenant}", apiName, tenantId);
                }
                catch (Exception ex)
                {
                    _log.LogInformation("Optional API {Api} grant failed for tenant {Tenant}: {Error}",
                        apiName, tenantId, ex.Message);
                }
            }

            // Exchange.ManageAsApp also requires Exchange Administrator directory role.
            await TryAssignExchangeAdminRole(http, ourSpnId, tenantId);
        }
        catch (Exception ex)
        {
            _log.LogInformation("TryGrantOptionalApiPermissions failed for tenant {Tenant}: {Error}",
                tenantId, ex.Message);
        }
    }

    private async Task TryAssignExchangeAdminRole(HttpClient http, string spnObjectId, string tenantId)
    {
        try
        {
            // Resolve Exchange Administrator role definition ID in the tenant
            var roleResp = await http.GetAsync(
                $"directoryRoles?$filter=roleTemplateId eq '{ExchangeAdminRoleTemplateId}'&$select=id");
            string? roleId = null;

            if (roleResp.IsSuccessStatusCode)
            {
                using var roleDoc = await JsonDocument.ParseAsync(await roleResp.Content.ReadAsStreamAsync());
                var roles = roleDoc.RootElement.GetProperty("value");
                if (roles.GetArrayLength() > 0)
                    roleId = roles[0].GetProperty("id").GetString();
            }

            // Role not yet activated in tenant — activate it
            if (roleId is null)
            {
                var activateBody = JsonSerializer.Serialize(new { roleTemplateId = ExchangeAdminRoleTemplateId });
                var activateContent = new StringContent(activateBody, System.Text.Encoding.UTF8, "application/json");
                var activateResp = await http.PostAsync("directoryRoles", activateContent);
                if (activateResp.IsSuccessStatusCode)
                {
                    using var activateDoc = await JsonDocument.ParseAsync(await activateResp.Content.ReadAsStreamAsync());
                    roleId = activateDoc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                }
            }

            if (roleId is null)
            {
                _log.LogInformation("Could not resolve Exchange Administrator role in tenant {Tenant}", tenantId);
                return;
            }

            // Assign our SPN to the role
            var memberBody = JsonSerializer.Serialize(new
            {
                // Graph expects @odata.id for directory objects
                OdataId = $"https://graph.microsoft.com/v1.0/servicePrincipals/{spnObjectId}"
            });
            // Use $ref endpoint for role member assignment
            var body = JsonSerializer.Serialize(new { @odata = new { id = $"https://graph.microsoft.com/v1.0/directoryObjects/{spnObjectId}" } });
            var refBody = $"{{\"@odata.id\":\"https://graph.microsoft.com/v1.0/directoryObjects/{spnObjectId}\"}}";
            var refContent = new StringContent(refBody, System.Text.Encoding.UTF8, "application/json");
            var assignResp = await http.PostAsync($"directoryRoles/{roleId}/members/$ref", refContent);

            // 204 = success, 400 with "already exist" = already assigned
            if (assignResp.IsSuccessStatusCode || (int)assignResp.StatusCode == 400)
                _log.LogInformation("Exchange Administrator role assigned to SPN in tenant {Tenant}", tenantId);
            else
                _log.LogInformation("Exchange Admin role assignment returned {Status} in tenant {Tenant}",
                    (int)assignResp.StatusCode, tenantId);
        }
        catch (Exception ex)
        {
            _log.LogInformation("TryAssignExchangeAdminRole failed for tenant {Tenant}: {Error}", tenantId, ex.Message);
        }
    }

    // ── Helper: state parsing ──
    // State formats: "graph|{orgId}" or "arm|{orgId}|{tenantId}"

    private static Guid ParseOrgIdFromState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return Guid.Empty;
        var parts = state.Split('|');
        // Try index 1 (prefixed format: "graph|orgId" or "arm|orgId|tid")
        if (parts.Length > 1 && Guid.TryParse(parts[1], out var id1)) return id1;
        // Fallback: try index 0 (bare orgId)
        return Guid.TryParse(parts[0], out var id0) ? id0 : Guid.Empty;
    }

    private static (Guid OrgId, string? TenantId) ParseArmState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return (Guid.Empty, null);
        var parts = state.Split('|');
        // "arm|{orgId}|{tenantId}"
        var orgId = parts.Length > 1 && Guid.TryParse(parts[1], out var id) ? id : Guid.Empty;
        var tenantId = parts.Length > 2 ? parts[2] : null;
        return (orgId, tenantId);
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
