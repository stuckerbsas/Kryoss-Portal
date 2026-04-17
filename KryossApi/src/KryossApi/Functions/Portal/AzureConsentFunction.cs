using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
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
using Microsoft.Graph;

namespace KryossApi.Functions.Portal;

/// <summary>
/// Azure ARM consent endpoints for the Cloud Assessment (CA-6) Azure
/// Infrastructure pipeline. Unlike M365/Graph consent (OAuth scopes),
/// Azure ARM uses per-subscription RBAC role assignment. The Kryoss
/// multi-tenant app (<see cref="M365Config.ClientId"/>) is added to the
/// customer's tenant via admin consent, and the customer then grants the
/// Reader role on each subscription they want scanned.
/// </summary>
public class AzureConsentFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly M365Config _m365Config;
    private readonly ILogger<AzureConsentFunction> _log;

    public AzureConsentFunction(
        KryossDbContext db,
        ICurrentUserService user,
        M365Config m365Config,
        ILogger<AzureConsentFunction> log)
    {
        _db = db;
        _user = user;
        _m365Config = m365Config;
        _log = log;
    }

    /// <summary>
    /// Return instructions for the customer admin to grant Reader on their
    /// Azure subscription(s) to the Kryoss SPN. Does NOT create a DB row —
    /// subscription rows are inserted on /verify once ARM returns them.
    /// POST /v2/cloud-assessment/azure/connect
    /// </summary>
    [Function("AzureConsent_Connect")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Connect(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/cloud-assessment/azure/connect")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadFromJsonAsync<AzureConnectRequest>();
            if (body is null || body.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(body.TenantId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "organizationId and tenantId are required" });
                return bad;
            }

            if (!await UserCanAccessOrg(body.OrganizationId))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(_m365Config.ClientId))
            {
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteAsJsonAsync(new { error = "Kryoss scanner app registration not configured" });
                return err;
            }

            var appId = _m365Config.ClientId;
            var spnObjectId = await TryResolveKryossSpnObjectIdAsync(body.TenantId!, _log);

            var azCliCommand = BuildAzCliCommand(appId, spnObjectId);
            var portalUrl =
                $"https://portal.azure.com/#@{Uri.EscapeDataString(body.TenantId!)}" +
                "/resource/subscriptions/{subscription-id}/users";

            var spnResolutionNote = spnObjectId != null
                ? "Kryoss service principal resolved in your tenant. Use the command below, substituting your subscription ID."
                : "Kryoss service principal could not be resolved automatically (the multi-tenant app may not have been consented in this tenant yet, or the query returned no match). " +
                  $"Discover the object ID manually with: az ad sp show --id {appId} --query id -o tsv — or run the two-step command below, which discovers it for you.";

            _db.Actlog.Add(new Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "cloud-assessment",
                Action = "azure.consent.instructions",
                EntityType = "Organization",
                EntityId = body.OrganizationId.ToString(),
                Message = $"Azure consent instructions issued (tenantId={body.TenantId}, spnResolved={spnObjectId != null})"
            });
            await _db.SaveChangesAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                appId,
                servicePrincipalObjectId = spnObjectId,
                azCliCommand,
                portalUrl,
                spnResolutionNote
            });
            return response;
        }
        catch (Exception ex)
        {
            // TEMPORARY: return actual error for debugging
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = ex.Message, stack = ex.StackTrace, inner = ex.InnerException?.Message });
            return err;
        }
    }

    /// <summary>
    /// Verify ARM access by acquiring a token for the customer tenant and
    /// listing subscriptions the Kryoss SPN has Reader on. Upserts rows in
    /// cloud_assessment_azure_subscriptions for each subscription returned.
    /// POST /v2/cloud-assessment/azure/verify
    /// </summary>
    [Function("AzureConsent_Verify")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Verify(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/cloud-assessment/azure/verify")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadFromJsonAsync<AzureVerifyRequest>();
            if (body is null || body.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(body.TenantId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "organizationId and tenantId are required" });
                return bad;
            }

            if (!await UserCanAccessOrg(body.OrganizationId))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(_m365Config.ClientId) || string.IsNullOrWhiteSpace(_m365Config.ClientSecret))
            {
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteAsJsonAsync(new { error = "Kryoss scanner app registration not configured" });
                return err;
            }

            // Step 1 — acquire ARM token for the customer's tenant.
            AccessToken token;
            try
            {
                var credential = new ClientSecretCredential(body.TenantId, _m365Config.ClientId, _m365Config.ClientSecret);
                token = await credential.GetTokenAsync(
                    new TokenRequestContext(new[] { "https://management.azure.com/.default" }),
                    CancellationToken.None);
            }
            catch (AuthenticationFailedException afex)
            {
                var reason = $"Token acquisition failed: {afex.Message}";
                await TouchExistingRowsOnFailure(body.OrganizationId, reason);
                await LogVerifyOutcome(body.OrganizationId, "warn", $"Azure verify failed: {reason}");
                var resp = req.CreateResponse(HttpStatusCode.OK);
                await resp.WriteAsJsonAsync(new { connected = false, error = reason });
                return resp;
            }
            catch (Exception tex)
            {
                var reason = $"Token acquisition failed: {tex.Message}";
                await TouchExistingRowsOnFailure(body.OrganizationId, reason);
                await LogVerifyOutcome(body.OrganizationId, "warn", $"Azure verify failed: {reason}");
                var resp = req.CreateResponse(HttpStatusCode.OK);
                await resp.WriteAsJsonAsync(new { connected = false, error = reason });
                return resp;
            }

            // Step 2 — call ARM /subscriptions.
            using var http = new HttpClient { BaseAddress = new Uri("https://management.azure.com") };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var armResp = await http.GetAsync("/subscriptions?api-version=2022-12-01");
            if (!armResp.IsSuccessStatusCode)
            {
                var status = (int)armResp.StatusCode;
                var reason = $"ARM API returned {status}";
                await TouchExistingRowsOnFailure(body.OrganizationId, reason);

                _db.Actlog.Add(new Actlog
                {
                    Timestamp = DateTime.UtcNow,
                    Severity = "warn",
                    Module = "cloud-assessment",
                    Action = "azure.consent.verified",
                    EntityType = "Organization",
                    EntityId = body.OrganizationId.ToString(),
                    Message = $"Azure verify failed: ARM {status}"
                });
                await _db.SaveChangesAsync();

                var resp = req.CreateResponse(HttpStatusCode.OK);
                await resp.WriteAsJsonAsync(new { connected = false, error = reason });
                return resp;
            }

            // Step 3 — parse subscription list.
            var subscriptions = new List<(string SubscriptionId, string? DisplayName, string? State)>();
            await using (var stream = await armResp.Content.ReadAsStreamAsync())
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

            var now = DateTime.UtcNow;

            // Step 4a — 200 with empty subs: authenticated but no Reader yet.
            if (subscriptions.Count == 0)
            {
                _db.Actlog.Add(new Actlog
                {
                    Timestamp = now,
                    Severity = "info",
                    Module = "cloud-assessment",
                    Action = "azure.consent.verified",
                    EntityType = "Organization",
                    EntityId = body.OrganizationId.ToString(),
                    Message = "Azure verify completed: 0 subscriptions (authenticated, no Reader yet)"
                });
                await _db.SaveChangesAsync();

                var resp = req.CreateResponse(HttpStatusCode.OK);
                await resp.WriteAsJsonAsync(new
                {
                    connected = false,
                    missingRoles = new[] { "Reader" },
                    subscriptionCount = 0,
                    message = "Authenticated but no subscriptions have Reader role assigned yet."
                });
                return resp;
            }

            // Step 4b — 200 with subs: upsert. Existing rows not in the returned
            // set are marked failed (audit trail — do NOT delete).
            var existing = await _db.CloudAssessmentAzureSubscriptions
                .Where(s => s.OrganizationId == body.OrganizationId)
                .ToListAsync();

            var returnedSubIds = subscriptions.Select(s => s.SubscriptionId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (subId, displayName, state) in subscriptions)
            {
                var row = existing.FirstOrDefault(r => string.Equals(r.SubscriptionId, subId, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    row = new CloudAssessmentAzureSubscription
                    {
                        OrganizationId = body.OrganizationId,
                        SubscriptionId = subId,
                        DisplayName = displayName,
                        State = state,
                        TenantId = body.TenantId,
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
                    row.TenantId = body.TenantId;
                    row.ConsentState = "connected";
                    row.ConnectedAt ??= now;
                    row.LastVerifiedAt = now;
                    row.ErrorMessage = null;
                }
            }

            // Rows that used to exist but are no longer returned — mark failed.
            foreach (var stale in existing.Where(r => !returnedSubIds.Contains(r.SubscriptionId)))
            {
                stale.ConsentState = "failed";
                stale.ErrorMessage = "Subscription no longer accessible";
                stale.LastVerifiedAt = now;
            }

            _db.Actlog.Add(new Actlog
            {
                Timestamp = now,
                Severity = "info",
                Module = "cloud-assessment",
                Action = "azure.consent.verified",
                EntityType = "Organization",
                EntityId = body.OrganizationId.ToString(),
                Message = $"Azure verify completed: {subscriptions.Count} subscription(s) connected"
            });

            await _db.SaveChangesAsync();

            var okResp = req.CreateResponse(HttpStatusCode.OK);
            await okResp.WriteAsJsonAsync(new
            {
                connected = true,
                subscriptions = subscriptions.Select(s => new
                {
                    subscriptionId = s.SubscriptionId,
                    displayName = s.DisplayName,
                    state = s.State
                })
            });
            return okResp;
        }
        catch (Exception ex)
        {
            // TEMPORARY: return actual error for debugging
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = ex.Message, stack = ex.StackTrace, inner = ex.InnerException?.Message });
            return err;
        }
    }

    /// <summary>
    /// List all Azure subscriptions known for an org.
    /// GET /v2/cloud-assessment/azure/subscriptions?organizationId={guid}
    /// </summary>
    [Function("AzureConsent_ListSubscriptions")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> ListSubscriptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/azure/subscriptions")] HttpRequestData req)
    {
        try
        {
            var orgId = ResolveOrgId(req);
            if (orgId is null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "organizationId required" });
                return bad;
            }

            if (!await UserCanAccessOrg(orgId.Value))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            var rows = await _db.CloudAssessmentAzureSubscriptions
                .Where(s => s.OrganizationId == orgId.Value)
                .OrderBy(s => s.DisplayName)
                .Select(s => new
                {
                    id = s.Id,
                    subscriptionId = s.SubscriptionId,
                    displayName = s.DisplayName,
                    state = s.State,
                    tenantId = s.TenantId,
                    consentState = s.ConsentState,
                    connectedAt = s.ConnectedAt,
                    lastVerifiedAt = s.LastVerifiedAt,
                    errorMessage = s.ErrorMessage
                })
                .ToListAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(rows);
            return response;
        }
        catch (Exception ex)
        {
            // TEMPORARY: return actual error for debugging
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = ex.Message, stack = ex.StackTrace, inner = ex.InnerException?.Message });
            return err;
        }
    }

    /// <summary>
    /// Remove a single Azure subscription from the org.
    /// DELETE /v2/cloud-assessment/azure/subscriptions/{subscriptionId}?organizationId={guid}
    /// </summary>
    [Function("AzureConsent_DeleteSubscription")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> DeleteSubscription(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v2/cloud-assessment/azure/subscriptions/{subscriptionId}")] HttpRequestData req,
        string subscriptionId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(subscriptionId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "subscriptionId is required" });
                return bad;
            }

            var orgId = ResolveOrgId(req);
            if (orgId is null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "organizationId required" });
                return bad;
            }

            if (!await UserCanAccessOrg(orgId.Value))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            var row = await _db.CloudAssessmentAzureSubscriptions
                .FirstOrDefaultAsync(s => s.OrganizationId == orgId.Value && s.SubscriptionId == subscriptionId);

            if (row is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Subscription not found for this organization" });
                return notFound;
            }

            _db.CloudAssessmentAzureSubscriptions.Remove(row);

            _db.Actlog.Add(new Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "cloud-assessment",
                Action = "azure.subscription.removed",
                EntityType = "Organization",
                EntityId = orgId.Value.ToString(),
                Message = $"Azure subscription removed: {subscriptionId}"
            });

            await _db.SaveChangesAsync();

            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (Exception ex)
        {
            // TEMPORARY: return actual error for debugging
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = ex.Message, stack = ex.StackTrace, inner = ex.InnerException?.Message });
            return err;
        }
    }

    // ── Helpers ──

    /// <summary>
    /// Try to resolve the Kryoss service principal object ID in the customer's
    /// tenant by calling Graph /servicePrincipals?$filter=appId eq '&lt;KRYOSS_APP_ID&gt;'.
    /// Returns null on any failure (token, 403, empty result) — never throws.
    /// </summary>
    private async Task<string?> TryResolveKryossSpnObjectIdAsync(string customerTenantId, ILogger log)
    {
        try
        {
            var credential = new ClientSecretCredential(customerTenantId, _m365Config.ClientId, _m365Config.ClientSecret);
            var graph = new GraphServiceClient(credential);
            var appId = _m365Config.ClientId;
            var result = await graph.ServicePrincipals.GetAsync(r =>
                r.QueryParameters.Filter = $"appId eq '{appId}'");

            var spn = result?.Value?.FirstOrDefault();
            if (spn is null)
            {
                log.LogInformation("Kryoss SPN not found in tenant {TenantId} (empty result)", customerTenantId);
                return null;
            }
            return spn.Id;
        }
        catch (Exception ex)
        {
            log.LogInformation("SPN resolve failed for tenant {TenantId}: {Error}", customerTenantId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Build a copy-paste az CLI command for assigning Reader to the Kryoss SPN.
    /// If the SPN object ID was resolved, use it directly; otherwise emit a
    /// two-step command that discovers the ID via az ad sp show.
    /// </summary>
    private static string BuildAzCliCommand(string appId, string? spnObjectId)
    {
        if (spnObjectId != null)
        {
            return $"az role assignment create \\\n" +
                   $"  --assignee-object-id {spnObjectId} \\\n" +
                   $"  --assignee-principal-type ServicePrincipal \\\n" +
                   $"  --role \"Reader\" \\\n" +
                   $"  --scope \"/subscriptions/<SUBSCRIPTION_ID>\"";
        }

        return $"# Step 1 — discover Kryoss SPN object ID in your tenant\n" +
               $"SPN_OBJECT_ID=$(az ad sp show --id {appId} --query id -o tsv)\n" +
               $"\n" +
               $"# Step 2 — assign Reader role\n" +
               $"az role assignment create \\\n" +
               $"  --assignee-object-id $SPN_OBJECT_ID \\\n" +
               $"  --assignee-principal-type ServicePrincipal \\\n" +
               $"  --role \"Reader\" \\\n" +
               $"  --scope \"/subscriptions/<SUBSCRIPTION_ID>\"";
    }

    /// <summary>
    /// On ARM/token failure, update LastVerifiedAt + ErrorMessage on all
    /// existing rows for the org but keep their ConsentState intact (so the
    /// failure is surfaced without blowing away history). Caller is expected
    /// to SaveChanges separately.
    /// </summary>
    private async Task LogVerifyOutcome(Guid organizationId, string severity, string message)
    {
        _db.Actlog.Add(new Actlog
        {
            Timestamp = DateTime.UtcNow,
            Severity = severity,
            Module = "cloud-assessment",
            Action = "azure.consent.verified",
            EntityType = "Organization",
            EntityId = organizationId.ToString(),
            Message = message
        });
        await _db.SaveChangesAsync();
    }

    private async Task TouchExistingRowsOnFailure(Guid organizationId, string reason)
    {
        var rows = await _db.CloudAssessmentAzureSubscriptions
            .Where(s => s.OrganizationId == organizationId)
            .ToListAsync();
        if (rows.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var r in rows)
        {
            r.LastVerifiedAt = now;
            r.ErrorMessage = reason;
        }
        await _db.SaveChangesAsync();
    }

    private async Task<bool> UserCanAccessOrg(Guid organizationId)
    {
        if (_user.IsAdmin) return true;
        var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
            await _db.Organizations.AnyAsync(o => o.Id == organizationId && o.FranchiseId == _user.FranchiseId.Value);
        var orgBelongsToUser = _user.OrganizationId.HasValue && organizationId == _user.OrganizationId.Value;
        return orgBelongsToFranchise || orgBelongsToUser;
    }

    private Guid? ResolveOrgId(HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        if (Guid.TryParse(orgIdStr, out var parsed))
            return parsed;
        if (_user.OrganizationId.HasValue)
            return _user.OrganizationId.Value;
        return null;
    }
}

// ── Request DTOs ──

public class AzureConnectRequest
{
    public Guid OrganizationId { get; set; }
    public string? TenantId { get; set; }
}

public class AzureVerifyRequest
{
    public Guid OrganizationId { get; set; }
    public string? TenantId { get; set; }
}
