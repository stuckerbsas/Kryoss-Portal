# Unified Cloud Connect — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace three separate connect flows (M365, Azure, Power BI) with a single "Connect Cloud" button that uses a delegated auth code flow to grant all permissions at once, auto-detects what's available, and runs a full scan automatically.

**Architecture:** Single OAuth2 authorization code flow with `prompt=admin_consent` acquires delegated ARM token + grants admin consent for Graph + PBI app permissions. Callback auto-assigns Azure Reader RBAC via ARM API, auto-verifies PBI admin API access, creates M365 tenant row, and triggers full cloud assessment scan. Services without access show "N/A" instead of manual connect flows.

**Tech Stack:** .NET 8 Azure Functions, EF Core 8, Microsoft.Identity (ClientSecretCredential + ConfidentialClientApplication for auth code exchange), React 18, TanStack Query, shadcn/ui

---

## File Map

| Action | File | Responsibility |
|--------|------|---------------|
| Create | `KryossApi/src/KryossApi/Functions/Portal/UnifiedCloudConnectFunction.cs` | New endpoint: consent URL generation + callback handler |
| Modify | `KryossApi/src/KryossApi/Functions/Portal/CloudAssessmentFunction.cs` | Add scan-after-connect trigger (or reuse existing scan endpoint) |
| Modify | `KryossApi/src/KryossApi/Services/M365Config.cs` | Add `CallbackBaseUrl` property for unified callback |
| Modify | `KryossPortal/src/api/cloudAssessment.ts` | Add `useUnifiedCloudConnect` hook + types |
| Modify | `KryossPortal/src/components/cloud-assessment/OverviewTab.tsx` | Add "Connect Cloud" button when no M365 tenant connected |
| Modify | `KryossPortal/src/components/cloud-assessment/CloudAssessmentPage.tsx` | Handle `?cloud_connected=true` callback param, show N/A badges on tabs |
| Modify | `KryossPortal/src/components/cloud-assessment/PowerBiTab.tsx` | Show "N/A" state when PBI not available (instead of manual connect) |
| Modify | `KryossPortal/src/components/cloud-assessment/ConnectAzureCard.tsx` | Show only as fallback when auto-assign failed; simplify to "assign Reader manually" |
| Create | `KryossPortal/src/components/cloud-assessment/CloudConnectCard.tsx` | Unified connect card with progress animation |
| Create | `KryossPortal/src/components/cloud-assessment/ConnectProgressModal.tsx` | Modal showing step-by-step progress after callback redirect |

---

## Design Decisions

### 1. Auth Code Flow (not pure admin consent)

Current M365 consent uses `/adminconsent` endpoint → only grants app permissions, no user token returned.

New flow uses `/authorize` with `prompt=admin_consent`:
- Grants admin consent for all app-level permissions (Graph, PBI) — same as before
- Returns an **authorization code** we exchange for a delegated ARM token
- Delegated token has the admin's Azure RBAC permissions → we can auto-assign Reader

Scopes requested in `/authorize`:
```
https://management.azure.com/user_impersonation offline_access
```
The `prompt=admin_consent` also triggers consent for all app-level permissions configured on the app registration (Graph + PBI).

### 2. Auto-assign Reader RBAC

After exchanging the auth code for a delegated ARM token:
1. `GET /subscriptions` — list subs the admin can see
2. For each sub: `PUT /.../{subId}/providers/Microsoft.Authorization/roleAssignments/{newGuid}` with principalId = Kryoss SPN object ID, roleDefinitionId = Reader role GUID
3. If 403 (admin lacks Owner/UAA) → gracefully degrade, show manual instructions
4. If 200 → sub is connected

### 3. PBI Auto-verify

After consent callback, acquire app-only PBI token and probe `/v1.0/myorg/admin/groups?$top=1`:
- 200 → PBI connected
- 403 → PBI N/A (tenant setting not enabled; show note)

### 4. Callback Flow

```
Portal → GET /v2/cloud/connect-url?orgId=X
       ← { url: "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?..." }

Admin clicks URL → Microsoft consent screen → approves → redirect to:
  GET /v2/cloud/connect-callback?code=AUTH_CODE&state=ORG_ID&tenant=TENANT_ID

Server callback:
  1. Exchange auth code → ARM delegated token
  2. Create/update M365 tenant row (consent flow, no per-customer creds)
  3. List ARM subscriptions → auto-assign Reader → upsert azure_subscriptions rows
  4. Acquire PBI app-only token → probe admin API → upsert PBI connection row
  5. Start full cloud assessment scan (async)
  6. Redirect to portal: /organizations/{slug}/cloud-assessment?cloud_connected=true
     Query params: &azure_subs=N&azure_failed=true/false&pbi=true/false

Portal on load:
  - Parse params → show ConnectProgressModal
  - Poll scan status until complete
  - Show results with N/A for unavailable services
```

### 5. N/A Handling

- **Azure N/A**: Admin didn't have Owner on any sub. Show: "Azure infrastructure scanning requires Reader role. [Copy az CLI command]"
- **PBI N/A**: Tenant setting not enabled. Show: "Power BI governance requires admin API access. [Link to PBI admin portal]"
- **M365**: Always available after consent (app permissions granted)

### 6. Reader Role GUID

Azure built-in Reader role: `acdd72a7-3385-48ef-bd42-f606fba81ae7` (constant, same in all tenants).

### 7. App Registration Changes Needed

The Entra app registration (`M365ScannerClientId`) needs:
- **Add**: Delegated permission `https://management.azure.com/user_impersonation`
- **Add**: Redirect URI `https://func-kryoss.azurewebsites.net/v2/cloud/connect-callback` (type: Web)
- Existing app permissions (Graph read-only, PBI Tenant.Read.All) stay as-is

### 8. NuGet: Microsoft.Identity.Client

Need `Microsoft.Identity.Client` (MSAL.NET) for ConfidentialClientApplication to exchange auth code for token. Already available via `Azure.Identity` transitive dependency — verify, may need explicit reference.

---

### Task 1: M365Config + NuGet prep

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/M365Config.cs`
- Modify: `KryossApi/src/KryossApi/Program.cs` (bind new config)
- Modify: `KryossApi/src/KryossApi/KryossApi.csproj` (if MSAL needs explicit ref)

- [ ] **Step 1: Add CallbackBaseUrl to M365Config**

```csharp
public class M365Config
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string PortalBaseUrl { get; set; } = "https://zealous-dune-0ac672d10.6.azurestaticapps.net";
    public string CallbackBaseUrl { get; set; } = "https://func-kryoss.azurewebsites.net";
}
```

- [ ] **Step 2: Verify MSAL availability**

Check if `Microsoft.Identity.Client.ConfidentialClientApplicationBuilder` compiles. If not, add:
```xml
<PackageReference Include="Microsoft.Identity.Client" Version="4.*" />
```

Run: `dotnet build src/KryossApi/KryossApi.csproj`

- [ ] **Step 3: Commit**

```bash
git add src/KryossApi/Services/M365Config.cs src/KryossApi/KryossApi.csproj
git commit -m "feat(ca-10): add CallbackBaseUrl to M365Config for unified cloud connect"
```

---

### Task 2: UnifiedCloudConnectFunction — consent URL + callback

**Files:**
- Create: `KryossApi/src/KryossApi/Functions/Portal/UnifiedCloudConnectFunction.cs`

This is the core backend task. Two endpoints:

- [ ] **Step 1: Create UnifiedCloudConnectFunction.cs with ConnectUrl endpoint**

`GET /v2/cloud/connect-url?organizationId={guid}`

Returns `{ url }` where URL is:
```
https://login.microsoftonline.com/common/oauth2/v2.0/authorize
  ?client_id={appId}
  &response_type=code
  &redirect_uri={callbackBaseUrl}/v2/cloud/connect-callback
  &response_mode=query
  &scope=https://management.azure.com/user_impersonation offline_access
  &prompt=admin_consent
  &state={orgId}
```

```csharp
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

public class UnifiedCloudConnectFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly M365Config _m365Config;
    private readonly ICloudAssessmentService _caService;
    private readonly ILogger<UnifiedCloudConnectFunction> _log;

    private const string ReaderRoleId = "acdd72a7-3385-48ef-bd42-f606fba81ae7";

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

    [Function("UnifiedCloud_ConnectUrl")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> ConnectUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud/connect-url")] HttpRequestData req)
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

        var redirectUri = Uri.EscapeDataString(
            $"{_m365Config.CallbackBaseUrl.TrimEnd('/')}/v2/cloud/connect-callback");

        var url = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize" +
            $"?client_id={Uri.EscapeDataString(_m365Config.ClientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={redirectUri}" +
            $"&response_mode=query" +
            $"&scope={Uri.EscapeDataString("https://management.azure.com/user_impersonation offline_access")}" +
            $"&prompt=admin_consent" +
            $"&state={orgId}";

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { url });
        return response;
    }

    // ... ConnectCallback in next step
}
```

- [ ] **Step 2: Add ConnectCallback endpoint**

`GET /v2/cloud/connect-callback?code=X&state=ORG_ID&tenant=TENANT_ID`

No auth required (browser redirect from Microsoft). This method:

1. Validates params (code, state=orgId, tenant)
2. Exchanges auth code for ARM delegated token via MSAL ConfidentialClientApplication
3. Creates M365 tenant row (consent flow — no per-customer creds)
4. Lists ARM subscriptions via delegated token
5. For each sub, attempts `PUT roleAssignment` to assign Reader to Kryoss SPN
6. Resolves Kryoss SPN object ID via Graph (same as AzureConsentFunction)
7. Verifies PBI admin API access (app-only token)
8. Upserts PBI connection row
9. Starts cloud assessment scan
10. Redirects to portal with result query params

```csharp
[Function("UnifiedCloud_ConnectCallback")]
public async Task<HttpResponseData> ConnectCallback(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud/connect-callback")] HttpRequestData req)
{
    var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
    var code = query["code"];
    var stateParam = query["state"];
    var tenantParam = query["tenant"];
    var errorParam = query["error"];
    var errorDesc = query["error_description"];

    Guid.TryParse(stateParam, out var orgId);

    // Resolve org for redirect
    var org = orgId != Guid.Empty
        ? await _db.Organizations.Where(o => o.Id == orgId).Select(o => new { o.Id, o.Name }).FirstOrDefaultAsync()
        : null;
    var orgSlug = org != null ? GenerateSlug(org.Name) : "unknown";

    string BuildRedirect(string queryString) =>
        $"{_m365Config.PortalBaseUrl.TrimEnd('/')}/organizations/{Uri.EscapeDataString(orgSlug)}/cloud-assessment?{queryString}";

    // Handle error from Microsoft
    if (!string.IsNullOrEmpty(errorParam))
    {
        var redirect = req.CreateResponse(HttpStatusCode.Redirect);
        redirect.Headers.Add("Location", BuildRedirect($"error={Uri.EscapeDataString(errorDesc ?? errorParam)}"));
        return redirect;
    }

    if (string.IsNullOrWhiteSpace(code) || org is null || string.IsNullOrWhiteSpace(tenantParam))
    {
        var redirect = req.CreateResponse(HttpStatusCode.Redirect);
        redirect.Headers.Add("Location", BuildRedirect("error=Invalid+callback+parameters"));
        return redirect;
    }

    var results = new ConnectResults();

    try
    {
        // ── Step 1: Exchange auth code for ARM delegated token ──
        var redirectUri = $"{_m365Config.CallbackBaseUrl.TrimEnd('/')}/v2/cloud/connect-callback";
        var cca = ConfidentialClientApplicationBuilder
            .Create(_m365Config.ClientId)
            .WithClientSecret(_m365Config.ClientSecret)
            .WithAuthority("https://login.microsoftonline.com/common")
            .WithRedirectUri(redirectUri)
            .Build();

        AuthenticationResult authResult;
        try
        {
            authResult = await cca.AcquireTokenByAuthorizationCode(
                new[] { "https://management.azure.com/user_impersonation" }, code)
                .WithTenantId(tenantParam)
                .ExecuteAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Auth code exchange failed for org {OrgId}", orgId);
            var redirect = req.CreateResponse(HttpStatusCode.Redirect);
            redirect.Headers.Add("Location", BuildRedirect($"error={Uri.EscapeDataString("Auth code exchange failed: " + ex.Message)}"));
            return redirect;
        }

        // ── Step 2: Create/update M365 tenant row ──
        var existingTenant = await _db.M365Tenants.FirstOrDefaultAsync(t => t.OrganizationId == orgId);
        if (existingTenant is null)
        {
            existingTenant = new M365Tenant
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                TenantId = tenantParam,
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                ConsentGrantedAt = DateTime.UtcNow
            };
            _db.M365Tenants.Add(existingTenant);
            await _db.SaveChangesAsync();
            results.M365Connected = true;
        }
        else
        {
            existingTenant.Status = "active";
            existingTenant.ConsentGrantedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            results.M365Connected = true;
        }

        // ── Step 3: Azure — list subs + auto-assign Reader ──
        await TryAutoAssignAzureReader(authResult.AccessToken, tenantParam, orgId, results);

        // ── Step 4: PBI — verify admin API access ──
        await TryVerifyPowerBi(tenantParam, orgId, results);

        // ── Step 5: Trigger full cloud assessment scan ──
        try
        {
            var scanId = await _caService.StartScanAsync(orgId, existingTenant.Id);
            results.ScanId = scanId;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Auto-scan failed after unified connect for org {OrgId}", orgId);
            results.ScanError = ex.Message;
        }

        // ── Actlog ──
        _db.Actlog.Add(new Actlog
        {
            Timestamp = DateTime.UtcNow,
            Severity = "info",
            Module = "cloud-assessment",
            Action = "unified.connect.completed",
            EntityType = "Organization",
            EntityId = orgId.ToString(),
            Message = $"Unified connect: m365={results.M365Connected} azure_subs={results.AzureSubsConnected} azure_failed={results.AzureAutoAssignFailed} pbi={results.PbiConnected} scan={results.ScanId}"
        });
        await _db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        _log.LogError(ex, "Unified cloud connect failed for org {OrgId}", orgId);
        var redirect = req.CreateResponse(HttpStatusCode.Redirect);
        redirect.Headers.Add("Location", BuildRedirect($"error={Uri.EscapeDataString("Connect failed: " + ex.Message)}"));
        return redirect;
    }

    // Build success redirect with status params
    var qs = $"cloud_connected=true" +
        $"&m365={results.M365Connected}" +
        $"&azure_subs={results.AzureSubsConnected}" +
        $"&azure_rbac_failed={results.AzureAutoAssignFailed}" +
        $"&pbi={results.PbiConnected}" +
        (results.ScanId.HasValue ? $"&scan_id={results.ScanId}" : "") +
        (results.PbiError != null ? $"&pbi_note={Uri.EscapeDataString(results.PbiError)}" : "") +
        (results.AzureError != null ? $"&azure_note={Uri.EscapeDataString(results.AzureError)}" : "");

    var successRedirect = req.CreateResponse(HttpStatusCode.Redirect);
    successRedirect.Headers.Add("Location", BuildRedirect(qs));
    return successRedirect;
}
```

- [ ] **Step 3: Add helper methods**

```csharp
private async Task TryAutoAssignAzureReader(string armToken, string tenantId, Guid orgId, ConnectResults results)
{
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://management.azure.com") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", armToken);

        // List subscriptions the admin can see
        using var subsResp = await http.GetAsync("/subscriptions?api-version=2022-12-01");
        if (!subsResp.IsSuccessStatusCode)
        {
            results.AzureError = $"ARM API returned {(int)subsResp.StatusCode}";
            return;
        }

        var subs = new List<(string Id, string? Name, string? State)>();
        await using (var stream = await subsResp.Content.ReadAsStreamAsync())
        using (var doc = await JsonDocument.ParseAsync(stream))
        {
            if (doc.RootElement.TryGetProperty("value", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var subId = item.TryGetProperty("subscriptionId", out var s) ? s.GetString() : null;
                    var name = item.TryGetProperty("displayName", out var n) ? n.GetString() : null;
                    var state = item.TryGetProperty("state", out var st) ? st.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(subId))
                        subs.Add((subId!, name, state));
                }
            }
        }

        if (subs.Count == 0)
        {
            results.AzureError = "No subscriptions visible to admin";
            return;
        }

        // Resolve Kryoss SPN object ID in this tenant for RBAC assignment
        var spnObjectId = await TryResolveSpnObjectId(tenantId);
        if (spnObjectId is null)
        {
            // SPN not yet provisioned — admin consent should have created it.
            // Fallback: use app-only credential to try
            var credential = new ClientSecretCredential(tenantId, _m365Config.ClientId, _m365Config.ClientSecret);
            try
            {
                var graph = new Microsoft.Graph.GraphServiceClient(credential);
                var spResult = await graph.ServicePrincipals.GetAsync(r =>
                    r.QueryParameters.Filter = $"appId eq '{_m365Config.ClientId}'");
                spnObjectId = spResult?.Value?.FirstOrDefault()?.Id;
            }
            catch { /* ignore */ }
        }

        var now = DateTime.UtcNow;
        int assigned = 0;
        int failed = 0;

        foreach (var (subId, name, state) in subs)
        {
            bool readerAssigned = false;

            // Try auto-assign Reader if we have SPN object ID
            if (spnObjectId != null)
            {
                try
                {
                    var roleAssignmentId = Guid.NewGuid();
                    var body = JsonSerializer.Serialize(new
                    {
                        properties = new
                        {
                            roleDefinitionId = $"/subscriptions/{subId}/providers/Microsoft.Authorization/roleDefinitions/{ReaderRoleId}",
                            principalId = spnObjectId,
                            principalType = "ServicePrincipal"
                        }
                    });

                    var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                    var putResp = await http.PutAsync(
                        $"/subscriptions/{subId}/providers/Microsoft.Authorization/roleAssignments/{roleAssignmentId}?api-version=2022-04-01",
                        content);

                    if (putResp.IsSuccessStatusCode || (int)putResp.StatusCode == 409) // 409 = already assigned
                    {
                        readerAssigned = true;
                        assigned++;
                    }
                    else
                    {
                        failed++;
                        _log.LogWarning("Reader assignment failed for sub {SubId}: HTTP {Status}", subId, (int)putResp.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _log.LogWarning(ex, "Reader assignment exception for sub {SubId}", subId);
                }
            }
            else
            {
                failed++;
            }

            // Upsert azure_subscriptions row
            var existing = await _db.CloudAssessmentAzureSubscriptions
                .FirstOrDefaultAsync(s => s.OrganizationId == orgId && s.SubscriptionId == subId);

            if (existing is null)
            {
                _db.CloudAssessmentAzureSubscriptions.Add(new CloudAssessmentAzureSubscription
                {
                    OrganizationId = orgId,
                    SubscriptionId = subId,
                    DisplayName = name,
                    State = state,
                    TenantId = tenantId,
                    ConsentState = readerAssigned ? "connected" : "pending",
                    ConnectedAt = readerAssigned ? now : null,
                    LastVerifiedAt = now,
                    ErrorMessage = readerAssigned ? null : "Reader role not assigned — admin lacks Owner permission",
                    CreatedAt = now
                });
            }
            else
            {
                existing.DisplayName = name;
                existing.State = state;
                existing.TenantId = tenantId;
                if (readerAssigned)
                {
                    existing.ConsentState = "connected";
                    existing.ConnectedAt ??= now;
                    existing.ErrorMessage = null;
                }
                existing.LastVerifiedAt = now;
            }
        }

        await _db.SaveChangesAsync();
        results.AzureSubsConnected = assigned;
        results.AzureAutoAssignFailed = failed > 0;
        if (failed > 0 && assigned == 0)
            results.AzureError = "Admin lacks Owner/UAA role — manual Reader assignment needed";
    }
    catch (Exception ex)
    {
        results.AzureError = ex.Message;
        _log.LogWarning(ex, "Azure auto-assign failed for org {OrgId}", orgId);
    }
}

private async Task TryVerifyPowerBi(string tenantId, Guid orgId, ConnectResults results)
{
    try
    {
        var credential = new ClientSecretCredential(tenantId, _m365Config.ClientId, _m365Config.ClientSecret);
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(new[] { "https://analysis.windows.net/powerbi/api/.default" }),
            CancellationToken.None);

        using var http = new HttpClient { BaseAddress = new Uri("https://api.powerbi.com") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var probe = await http.GetAsync("/v1.0/myorg/admin/groups?$top=1");

        if (probe.IsSuccessStatusCode)
        {
            await UpsertPbiConnection(orgId, "connected", null);
            results.PbiConnected = true;
        }
        else
        {
            var note = (int)probe.StatusCode == 403
                ? "Enable 'Service principals can use read-only admin APIs' in Power BI Admin Portal"
                : $"Power BI Admin API returned HTTP {(int)probe.StatusCode}";
            await UpsertPbiConnection(orgId, "unavailable", note);
            results.PbiConnected = false;
            results.PbiError = note;
        }
    }
    catch (Exception ex)
    {
        results.PbiConnected = false;
        results.PbiError = $"PBI verification failed: {ex.Message}";
        await UpsertPbiConnection(orgId, "unavailable", results.PbiError);
    }
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
            ErrorMessage = error,
            UpdatedAt = DateTime.UtcNow,
            LastVerifiedAt = state == "connected" ? DateTime.UtcNow : null
        };
        _db.CloudAssessmentPowerBiConnections.Add(conn);
    }
    else
    {
        conn.ConnectionState = state;
        conn.Enabled = state == "connected";
        conn.ErrorMessage = error;
        conn.UpdatedAt = DateTime.UtcNow;
        if (state == "connected") conn.LastVerifiedAt = DateTime.UtcNow;
    }

    await _db.SaveChangesAsync();
}

private async Task<string?> TryResolveSpnObjectId(string tenantId)
{
    try
    {
        var credential = new ClientSecretCredential(tenantId, _m365Config.ClientId, _m365Config.ClientSecret);
        var graph = new Microsoft.Graph.GraphServiceClient(credential);
        var result = await graph.ServicePrincipals.GetAsync(r =>
            r.QueryParameters.Filter = $"appId eq '{_m365Config.ClientId}'");
        return result?.Value?.FirstOrDefault()?.Id;
    }
    catch { return null; }
}

private async Task<bool> UserCanAccessOrg(Guid orgId)
{
    if (_user.IsAdmin) return true;
    if (_user.FranchiseId.HasValue)
        return await _db.Organizations.AnyAsync(o => o.Id == orgId && o.FranchiseId == _user.FranchiseId.Value);
    return _user.OrganizationId.HasValue && orgId == _user.OrganizationId.Value;
}

private static string GenerateSlug(string name) =>
    System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');

private class ConnectResults
{
    public bool M365Connected { get; set; }
    public int AzureSubsConnected { get; set; }
    public bool AzureAutoAssignFailed { get; set; }
    public string? AzureError { get; set; }
    public bool PbiConnected { get; set; }
    public string? PbiError { get; set; }
    public Guid? ScanId { get; set; }
    public string? ScanError { get; set; }
}
```

- [ ] **Step 4: Build check**

Run: `dotnet build src/KryossApi/KryossApi.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/KryossApi/Functions/Portal/UnifiedCloudConnectFunction.cs
git commit -m "feat(ca-10): unified cloud connect — auth code flow + auto-assign Reader + PBI verify"
```

---

### Task 3: Portal — CloudConnectCard + ConnectProgressModal

**Files:**
- Create: `KryossPortal/src/components/cloud-assessment/CloudConnectCard.tsx`
- Create: `KryossPortal/src/components/cloud-assessment/ConnectProgressModal.tsx`
- Modify: `KryossPortal/src/api/cloudAssessment.ts`

- [ ] **Step 1: Add useUnifiedCloudConnectUrl hook to cloudAssessment.ts**

```typescript
export function useUnifiedCloudConnectUrl(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['cloud-connect-url', organizationId],
    queryFn: () =>
      apiFetch<{ url: string }>(
        `/v2/cloud/connect-url?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}
```

- [ ] **Step 2: Create CloudConnectCard.tsx**

Single "Connect Cloud" button card with explanation. Replaces ConnectForm from M365Tab for the cloud assessment context.

```tsx
import { ExternalLink, Loader2, Cloud } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { useUnifiedCloudConnectUrl } from '@/api/cloudAssessment';

export function CloudConnectCard({ orgId }: { orgId: string }) {
  const { data, isLoading } = useUnifiedCloudConnectUrl(orgId);

  const handleConnect = () => {
    if (data?.url) window.location.href = data.url;
  };

  return (
    <Card className="max-w-2xl mx-auto">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Cloud className="h-5 w-5" />
          Connect Cloud Services
        </CardTitle>
        <CardDescription>
          One-click setup for Microsoft 365, Azure, and Power BI security scanning.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="rounded-lg border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800 space-y-2">
          <p className="font-medium">What happens when you click Connect:</p>
          <ol className="list-decimal list-inside space-y-1 text-xs">
            <li>Microsoft's admin consent screen opens</li>
            <li>Sign in with a Global Administrator account</li>
            <li>Approve read-only security audit permissions</li>
            <li>Azure Reader access is assigned automatically (if admin has Owner role)</li>
            <li>Power BI governance access is verified</li>
            <li>Full cloud assessment scan runs automatically</li>
          </ol>
          <p className="text-xs text-blue-600 mt-2">
            All permissions are read-only. Services without access show as "Not Available".
          </p>
        </div>

        <Button
          onClick={handleConnect}
          disabled={isLoading || !data?.url}
          className="w-full h-12 text-base"
          size="lg"
        >
          {isLoading ? (
            <>
              <Loader2 className="mr-2 h-5 w-5 animate-spin" />
              Loading...
            </>
          ) : (
            <>
              <ExternalLink className="mr-2 h-5 w-5" />
              Connect Cloud Services
            </>
          )}
        </Button>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 3: Create ConnectProgressModal.tsx**

Shown after redirect. Parses URL params, shows connection results (M365 check, Azure subs, PBI status), and polls scan status.

```tsx
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { CheckCircle2, XCircle, AlertTriangle, Loader2, Copy, Check } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { toast } from 'sonner';
import { useCloudAssessmentDetail } from '@/api/cloudAssessment';

interface ConnectResult {
  m365: boolean;
  azureSubs: number;
  azureRbacFailed: boolean;
  pbi: boolean;
  scanId: string | null;
  pbiNote: string | null;
  azureNote: string | null;
  error: string | null;
}

function parseConnectParams(params: URLSearchParams): ConnectResult | null {
  if (!params.has('cloud_connected') && !params.has('error')) return null;
  return {
    m365: params.get('m365') === 'true',
    azureSubs: parseInt(params.get('azure_subs') ?? '0'),
    azureRbacFailed: params.get('azure_rbac_failed') === 'true',
    pbi: params.get('pbi') === 'true',
    scanId: params.get('scan_id'),
    pbiNote: params.get('pbi_note'),
    azureNote: params.get('azure_note'),
    error: params.get('error'),
  };
}

function StatusLine({ ok, label, note }: { ok: boolean | null; label: string; note?: string | null }) {
  return (
    <div className="flex items-start gap-2 text-sm">
      {ok === true && <CheckCircle2 className="h-4 w-4 text-green-600 mt-0.5 shrink-0" />}
      {ok === false && <AlertTriangle className="h-4 w-4 text-amber-500 mt-0.5 shrink-0" />}
      {ok === null && <Loader2 className="h-4 w-4 animate-spin mt-0.5 shrink-0" />}
      <div>
        <span className={ok === false ? 'text-amber-700' : ''}>{label}</span>
        {note && <p className="text-xs text-muted-foreground mt-0.5">{note}</p>}
      </div>
    </div>
  );
}

export function ConnectProgressModal({ orgId }: { orgId: string }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const [result, setResult] = useState<ConnectResult | null>(null);
  const [dismissed, setDismissed] = useState(false);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    const parsed = parseConnectParams(searchParams);
    if (parsed) {
      setResult(parsed);
      // Clean URL params
      const newParams = new URLSearchParams();
      setSearchParams(newParams, { replace: true });

      if (parsed.error) {
        toast.error(`Cloud connect failed: ${parsed.error}`);
      }
    }
  }, []);

  const { data: scanDetail } = useCloudAssessmentDetail(result?.scanId ?? undefined);
  const scanDone = scanDetail && scanDetail.status !== 'running';

  if (!result || dismissed) return null;
  if (result.error) return null; // error handled by toast

  const azCliCmd = `az role assignment create \\\n  --assignee-object-id $(az ad sp show --id <APP_ID> --query id -o tsv) \\\n  --assignee-principal-type ServicePrincipal \\\n  --role "Reader" \\\n  --scope "/subscriptions/<SUBSCRIPTION_ID>"`;

  const handleCopy = () => {
    navigator.clipboard.writeText(azCliCmd).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  };

  return (
    <Card className="border-green-200 bg-green-50/30 mb-4">
      <CardHeader className="pb-2">
        <CardTitle className="text-base flex items-center gap-2">
          <CheckCircle2 className="h-5 w-5 text-green-600" />
          Cloud Services Connected
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <StatusLine ok={result.m365} label="Microsoft 365 / Entra ID — Connected" />
        <StatusLine
          ok={result.azureSubs > 0}
          label={result.azureSubs > 0
            ? `Azure — ${result.azureSubs} subscription(s) connected`
            : 'Azure — Not Available'}
          note={result.azureRbacFailed && result.azureSubs === 0
            ? result.azureNote ?? 'Admin needs Owner role to auto-assign. Assign Reader manually.'
            : result.azureNote}
        />
        <StatusLine
          ok={result.pbi}
          label={result.pbi ? 'Power BI Governance — Connected' : 'Power BI — Not Available'}
          note={!result.pbi ? result.pbiNote : null}
        />

        {/* Azure manual instructions if RBAC failed */}
        {result.azureRbacFailed && result.azureSubs === 0 && (
          <div className="mt-2 p-3 rounded border bg-white text-xs space-y-2">
            <p className="font-medium">To enable Azure scanning, run this in Azure CLI:</p>
            <div className="relative">
              <pre className="bg-muted rounded p-2 overflow-x-auto pr-10 text-xs">{azCliCmd}</pre>
              <Button variant="ghost" size="sm" onClick={handleCopy} className="absolute top-1 right-1 h-6 px-1.5">
                {copied ? <Check className="h-3 w-3 text-green-600" /> : <Copy className="h-3 w-3" />}
              </Button>
            </div>
          </div>
        )}

        {/* Scan progress */}
        {result.scanId && (
          <StatusLine
            ok={scanDone ? true : null}
            label={scanDone ? 'Cloud assessment scan complete' : 'Running cloud assessment scan...'}
          />
        )}

        <div className="pt-2 text-right">
          <Button variant="ghost" size="sm" onClick={() => setDismissed(true)}>
            Dismiss
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 4: Build check**

Run: `cd KryossPortal && npx tsc --noEmit`
Expected: no errors

- [ ] **Step 5: Commit**

```bash
git add src/api/cloudAssessment.ts src/components/cloud-assessment/CloudConnectCard.tsx src/components/cloud-assessment/ConnectProgressModal.tsx
git commit -m "feat(ca-10): CloudConnectCard + ConnectProgressModal — unified connect UI"
```

---

### Task 4: Wire into OverviewTab + CloudAssessmentPage

**Files:**
- Modify: `KryossPortal/src/components/cloud-assessment/OverviewTab.tsx`
- Modify: `KryossPortal/src/components/cloud-assessment/CloudAssessmentPage.tsx`
- Modify: `KryossPortal/src/api/m365.ts` (or cloudAssessment.ts — need M365 connected check)

- [ ] **Step 1: Add M365 connected check to OverviewTab**

In `OverviewTab`, if no scan data exists and M365 is not connected, show `CloudConnectCard` instead of empty state.

Import `useM365` from `@/api/m365` and `CloudConnectCard`:

```tsx
import { useM365 } from '@/api/m365';
import { CloudConnectCard } from './CloudConnectCard';
```

At the beginning of the OverviewTab component, add:
```tsx
const { data: m365Status } = useM365(orgId);
const m365Connected = m365Status?.connected === true;
```

If `!m365Connected` and no scan history, render `CloudConnectCard` instead of the regular overview.

- [ ] **Step 2: Add ConnectProgressModal to CloudAssessmentPage**

Import and render at top of the page:
```tsx
import { ConnectProgressModal } from './ConnectProgressModal';
```

Inside `CloudAssessmentPage`, before `<Tabs>`:
```tsx
<ConnectProgressModal orgId={orgId} />
```

- [ ] **Step 3: Add 'powerbi' to AREAS in OverviewTab**

The radar chart and area score cards need to include Power BI. In `OverviewTab.tsx`:

```tsx
const AREAS = [
  { key: 'identity', label: 'Identity', icon: Users },
  { key: 'endpoint', label: 'Endpoint', icon: Shield },
  { key: 'data', label: 'Data', icon: Database },
  { key: 'productivity', label: 'Productivity', icon: Activity },
  { key: 'azure', label: 'Azure', icon: Cloud },
  { key: 'powerbi', label: 'Power BI', icon: BarChart3 },
] as const;
```

(Import `BarChart3` from lucide-react.)

- [ ] **Step 4: Build check**

Run: `cd KryossPortal && npx tsc --noEmit`

- [ ] **Step 5: Commit**

```bash
git add src/components/cloud-assessment/OverviewTab.tsx src/components/cloud-assessment/CloudAssessmentPage.tsx
git commit -m "feat(ca-10): wire CloudConnectCard + ConnectProgressModal + powerbi area in overview"
```

---

### Task 5: PowerBiTab + Azure tab N/A states

**Files:**
- Modify: `KryossPortal/src/components/cloud-assessment/PowerBiTab.tsx`
- Modify: `KryossPortal/src/components/cloud-assessment/CloudAssessmentPage.tsx` (AzureTab)

- [ ] **Step 1: PowerBiTab — add "unavailable" state**

Currently PowerBiTab shows `ConnectPowerBiCard` when not connected. Change to show N/A card with PBI admin portal link when `connectionState === 'unavailable'`.

In `PowerBiTab`:
```tsx
const isConnected = connection && 'connectionState' in connection && connection.connectionState === 'connected';
const isUnavailable = connection && 'connectionState' in connection && connection.connectionState === 'unavailable';

if (isUnavailable) {
  return (
    <Card>
      <CardContent className="py-8 text-center space-y-3">
        <BarChart3 className="h-8 w-8 text-gray-400 mx-auto" />
        <p className="text-sm font-medium text-muted-foreground">Power BI Governance — Not Available</p>
        <p className="text-xs text-muted-foreground max-w-md mx-auto">
          {connection.errorMessage ?? 'The Power BI Admin API is not accessible for this tenant.'}
        </p>
        <a
          href="https://app.powerbi.com/admin-portal/tenantSettings"
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex items-center gap-1 text-xs text-blue-600 hover:underline"
        >
          Open Power BI Admin Portal
          <ExternalLink className="h-3 w-3" />
        </a>
      </CardContent>
    </Card>
  );
}

if (!isConnected) {
  return <ConnectPowerBiCard orgId={orgId} />;
}
```

Add `ExternalLink` to lucide imports.

- [ ] **Step 2: AzureTab — add N/A state when no subs and no RBAC**

In `AzureTab` within `CloudAssessmentPage.tsx`, when `!hasSubs` but M365 is connected, show a compact "assign Reader" card instead of full `ConnectAzureCard`.

Add a new state check: if M365 is connected but no Azure subs, show abbreviated instructions.

- [ ] **Step 3: Add N/A badge to tab triggers**

When a service is unavailable, show a subtle "(N/A)" badge on the tab trigger:

```tsx
<TabsTrigger value="azure">
  Azure {!hasAzureScanData && hasMissingAzureRbac && <span className="text-xs text-muted-foreground ml-1">(N/A)</span>}
</TabsTrigger>
```

Similar for Power BI tab.

- [ ] **Step 4: Build check**

Run: `cd KryossPortal && npx tsc --noEmit`

- [ ] **Step 5: Commit**

```bash
git add src/components/cloud-assessment/PowerBiTab.tsx src/components/cloud-assessment/CloudAssessmentPage.tsx
git commit -m "feat(ca-10): N/A states for Power BI + Azure tabs when services unavailable"
```

---

### Task 6: Clean up old M365Tab connect flow integration

**Files:**
- Modify: `KryossPortal/src/components/org-detail/M365Tab.tsx`

- [ ] **Step 1: Update M365Tab ConnectForm to reference unified flow**

The M365Tab's `ConnectForm` currently has its own consent URL logic. Since cloud assessment now handles unified connect, update the M365Tab to:
- Show a note directing users to Cloud Assessment for the unified connect
- Keep the manual connect as fallback (for orgs that only want M365 without CA)
- Add link: "For full cloud security scanning (M365 + Azure + Power BI), use [Cloud Assessment > Connect Cloud]"

- [ ] **Step 2: Build check**

Run: `cd KryossPortal && npx tsc --noEmit`

- [ ] **Step 3: Commit**

```bash
git add src/components/org-detail/M365Tab.tsx
git commit -m "feat(ca-10): M365Tab references unified connect flow"
```

---

### Task 7: App Registration documentation + CLAUDE.md update

**Files:**
- Modify: `CLAUDE.md` (root)

- [ ] **Step 1: Document app registration changes needed**

Add to CLAUDE.md decision log:

```
| 2026-04-18 | CA-10: Unified Cloud Connect | Single "Connect Cloud" button replaces 3 separate flows (M365/Azure/PBI). Auth code flow with `prompt=admin_consent` grants Graph+PBI app permissions AND returns delegated ARM token for auto-assigning Reader RBAC. N/A for services without access. App registration needs: delegated `user_impersonation` on ARM, redirect URI `/v2/cloud/connect-callback`. |
```

- [ ] **Step 2: Document the redirect URI that must be added to app registration**

Add to known gaps / deploy notes:

```
### Deploy: Unified Cloud Connect (CA-10)
1. App Registration (M365ScannerClientId) changes:
   - Add **delegated** permission: `Azure Service Management > user_impersonation`
   - Add **redirect URI** (Web): `https://func-kryoss.azurewebsites.net/v2/cloud/connect-callback`
2. Deploy API + Portal
3. No SQL migration needed
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(ca-10): unified cloud connect decision log + deploy notes"
```

---

## App Registration Checklist (manual, post-deploy)

These changes must be made in the Azure portal on the Kryoss multi-tenant app registration:

1. **API permissions > Add a permission > Azure Service Management > Delegated > user_impersonation** → Grant admin consent
2. **Authentication > Add a platform > Web > Redirect URI:** `https://func-kryoss.azurewebsites.net/v2/cloud/connect-callback`
3. Existing Graph + PBI app permissions remain unchanged
