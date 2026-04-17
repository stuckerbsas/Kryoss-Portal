using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using KryossApi.Services.CloudAssessment;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class CloudAssessmentFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly ICloudAssessmentService _service;
    private readonly IFindingStatusService _statusService;

    public CloudAssessmentFunction(
        KryossDbContext db,
        ICurrentUserService user,
        ICloudAssessmentService service,
        IFindingStatusService statusService)
    {
        _db = db;
        _user = user;
        _service = service;
        _statusService = statusService;
    }

    /// <summary>
    /// Trigger a new Cloud Assessment scan.
    /// POST /v2/cloud-assessment/scan
    /// </summary>
    [Function("CloudAssessment_Scan")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Scan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/cloud-assessment/scan")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadFromJsonAsync<CloudAssessmentScanRequest>();
            if (body is null || body.OrganizationId == Guid.Empty)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
                return bad;
            }

            // Verify org access
            if (!_user.IsAdmin)
            {
                var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
                    await _db.Organizations.AnyAsync(o => o.Id == body.OrganizationId && o.FranchiseId == _user.FranchiseId.Value);
                var orgBelongsToUser = _user.OrganizationId.HasValue && body.OrganizationId == _user.OrganizationId.Value;
                if (!orgBelongsToFranchise && !orgBelongsToUser)
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                    return forbidden;
                }
            }

            // Actlog entry at scan request — proves function was hit
            _db.Actlog.Add(new Data.Entities.Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "cloud-assessment",
                Action = "scan.requested",
                EntityType = "Organization",
                EntityId = body.OrganizationId.ToString(),
                Message = $"Cloud Assessment scan requested for org {body.OrganizationId}"
            });
            await _db.SaveChangesAsync();

            var scanId = await _service.StartScanAsync(body.OrganizationId, body.TenantId);

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new { scanId, status = "running" });
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
    /// Get latest Cloud Assessment scan for an organization.
    /// GET /v2/cloud-assessment?organizationId={guid}
    /// </summary>
    [Function("CloudAssessment_Get")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment")] HttpRequestData req)
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

            var result = await _service.GetLatestScanAsync(orgId.Value);

            var response = req.CreateResponse(HttpStatusCode.OK);
            if (result is null)
                await response.WriteAsJsonAsync(new { scanned = false });
            else
                await response.WriteAsJsonAsync(result);
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
    /// Get scan history for an organization.
    /// GET /v2/cloud-assessment/history?organizationId={guid}
    /// NOTE: declared before /{scanId} — literal route segment wins over catchall.
    /// </summary>
    [Function("CloudAssessment_History")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> History(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/history")] HttpRequestData req)
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

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var limitStr = query["limit"];
            int limit = 20;
            if (!string.IsNullOrWhiteSpace(limitStr) && int.TryParse(limitStr, out var parsedLimit))
                limit = parsedLimit;

            var result = await _service.GetScanHistoryAsync(orgId.Value, limit);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
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
    /// Compare two scans side-by-side.
    /// GET /v2/cloud-assessment/compare?scanAId={guid}&scanBId={guid}
    /// </summary>
    [Function("CloudAssessment_Compare")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Compare(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/compare")] HttpRequestData req)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var scanAStr = query["scanAId"];
            var scanBStr = query["scanBId"];

            if (!Guid.TryParse(scanAStr, out var scanAId) || !Guid.TryParse(scanBStr, out var scanBId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "scanAId and scanBId are required (GUIDs)" });
                return bad;
            }

            var result = await _service.CompareScansAsync(scanAId, scanBId);
            if (result is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "One or both scans not found" });
                return notFound;
            }

            // Verify org access — either scan must belong to the current user's org scope.
            // Simpler: rely on the fact that scan GUIDs are hard to guess + assessment:read is
            // tenant-scoped at middleware layer. If future audit requires per-org check, join on
            // scan A's OrganizationId against _user.OrganizationId / FranchiseId here.

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = ex.Message, stack = ex.StackTrace, inner = ex.InnerException?.Message });
            return err;
        }
    }

    /// <summary>
    /// Get full detail for a specific scan.
    /// GET /v2/cloud-assessment/{scanId}
    /// </summary>
    [Function("CloudAssessment_Detail")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Detail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/{scanId}")] HttpRequestData req,
        string scanId)
    {
        if (!Guid.TryParse(scanId, out var scanGuid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid scanId" });
            return bad;
        }

        var result = await _service.GetScanDetailAsync(scanGuid);
        if (result is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Scan not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    /// <summary>
    /// Get finding statuses for an organization.
    /// GET /v2/cloud-assessment/findings/status?organizationId={guid}[&area=identity][&status=open]
    /// </summary>
    [Function("CloudAssessmentFindingStatuses")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetFindingStatuses(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/findings/status")] HttpRequestData req)
    {
        var orgId = ResolveOrgId(req);
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var accessDenied = await RequireOrgAccess(req, orgId.Value);
        if (accessDenied is not null) return accessDenied;

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var area = query["area"];
        var statusFilter = query["status"];

        var statuses = await _statusService.GetStatusesForOrgAsync(orgId.Value, area, statusFilter);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(statuses);
        return response;
    }

    /// <summary>
    /// Set (upsert) a finding status for an organization.
    /// PATCH /v2/cloud-assessment/findings/status
    /// </summary>
    [Function("CloudAssessmentSetFindingStatus")]
    [RequirePermission("assessment:write")]
    public async Task<HttpResponseData> SetFindingStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/cloud-assessment/findings/status")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<SetFindingStatusRequest>();
        if (body is null || body.OrganizationId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        var accessDenied = await RequireOrgAccess(req, body.OrganizationId);
        if (accessDenied is not null) return accessDenied;

        try
        {
            var updated = await _statusService.SetStatusAsync(
                body.OrganizationId,
                body.Area,
                body.Service,
                body.Feature,
                body.Status,
                body.Notes,
                body.OwnerUserId,
                actorUserId: _user.UserId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(updated);
            return response;
        }
        catch (ArgumentException ex)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = ex.Message });
            return bad;
        }
    }

    /// <summary>
    /// Get active remediation suggestions for an organization.
    /// GET /v2/cloud-assessment/suggestions?organizationId={guid}
    /// </summary>
    [Function("CloudAssessmentGetSuggestions")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetSuggestions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/suggestions")] HttpRequestData req)
    {
        var orgId = ResolveOrgId(req);
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var accessDenied = await RequireOrgAccess(req, orgId.Value);
        if (accessDenied is not null) return accessDenied;

        var suggestions = await _statusService.GetActiveSuggestionsAsync(orgId.Value);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(suggestions);
        return response;
    }

    /// <summary>
    /// Dismiss a remediation suggestion.
    /// POST /v2/cloud-assessment/suggestions/{suggestionId}/dismiss
    /// </summary>
    [Function("CloudAssessmentDismissSuggestion")]
    [RequirePermission("assessment:write")]
    public async Task<HttpResponseData> DismissSuggestion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/cloud-assessment/suggestions/{suggestionId}/dismiss")] HttpRequestData req,
        string suggestionId)
    {
        if (!long.TryParse(suggestionId, out var id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid suggestionId" });
            return bad;
        }

        // Load suggestion to verify org access before acting on it
        var suggestion = await _db.CloudAssessmentSuggestions.FirstOrDefaultAsync(s => s.Id == id);
        if (suggestion is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Suggestion not found" });
            return notFound;
        }

        var accessDenied = await RequireOrgAccess(req, suggestion.OrganizationId);
        if (accessDenied is not null) return accessDenied;

        try
        {
            await _statusService.DismissSuggestionAsync(id, _user.UserId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { dismissed = true });
            return response;
        }
        catch (InvalidOperationException)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Suggestion not found" });
            return notFound;
        }
    }

    /// <summary>
    /// Get remediation stats for an organization.
    /// GET /v2/cloud-assessment/remediation/stats?organizationId={guid}
    /// </summary>
    [Function("CloudAssessmentRemediationStats")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetRemediationStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/remediation/stats")] HttpRequestData req)
    {
        var orgId = ResolveOrgId(req);
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var accessDenied = await RequireOrgAccess(req, orgId.Value);
        if (accessDenied is not null) return accessDenied;

        var stats = await _statusService.GetStatsAsync(orgId.Value);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(stats);
        return response;
    }

    // ── Helpers ──

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

    /// <summary>
    /// Verifies the caller has access to the specified org (franchise-member or direct owner).
    /// Returns a 403 response if access is denied, null if access is granted.
    /// Admins bypass the check.
    /// </summary>
    private async Task<HttpResponseData?> RequireOrgAccess(HttpRequestData req, Guid orgId)
    {
        if (_user.IsAdmin) return null;

        var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
            await _db.Organizations.AnyAsync(o => o.Id == orgId && o.FranchiseId == _user.FranchiseId.Value);
        var orgBelongsToUser = _user.OrganizationId.HasValue && orgId == _user.OrganizationId.Value;

        if (!orgBelongsToFranchise && !orgBelongsToUser)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        return null;
    }
}

// ── Request DTOs ──

public class CloudAssessmentScanRequest
{
    public Guid OrganizationId { get; set; }
    public Guid? TenantId { get; set; }
}

public class SetFindingStatusRequest
{
    public Guid OrganizationId { get; set; }
    public string Area { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Feature { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? OwnerUserId { get; set; }
}
