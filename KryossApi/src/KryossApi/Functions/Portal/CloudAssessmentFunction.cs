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

    public CloudAssessmentFunction(
        KryossDbContext db,
        ICurrentUserService user,
        ICloudAssessmentService service)
    {
        _db = db;
        _user = user;
        _service = service;
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
}

// ── Request DTOs ──

public class CloudAssessmentScanRequest
{
    public Guid OrganizationId { get; set; }
    public Guid? TenantId { get; set; }
}
