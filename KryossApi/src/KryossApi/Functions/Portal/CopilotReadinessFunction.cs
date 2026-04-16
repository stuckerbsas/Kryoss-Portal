using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using KryossApi.Services.CopilotReadiness;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class CopilotReadinessFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly ICopilotReadinessService _service;

    public CopilotReadinessFunction(
        KryossDbContext db,
        ICurrentUserService user,
        ICopilotReadinessService service)
    {
        _db = db;
        _user = user;
        _service = service;
    }

    /// <summary>
    /// Trigger a new Copilot Readiness scan.
    /// POST /v2/copilot-readiness/scan
    /// </summary>
    [Function("CopilotReadiness_Scan")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Scan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/copilot-readiness/scan")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<CopilotReadinessScanRequest>();
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

        // Check M365 tenant is connected
        var tenant = await _db.M365Tenants
            .FirstOrDefaultAsync(t => t.OrganizationId == body.OrganizationId && t.Status == "active");

        if (tenant is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "No active M365 tenant connected to this organization. Connect M365 first." });
            return notFound;
        }

        var scanId = await _service.StartScanAsync(body.OrganizationId, tenant.Id, tenant.TenantId);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { scanId, status = "running" });
        return response;
    }

    /// <summary>
    /// Get latest Copilot Readiness scan for an organization.
    /// GET /v2/copilot-readiness?organizationId={guid}
    /// </summary>
    [Function("CopilotReadiness_Get")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/copilot-readiness")] HttpRequestData req)
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

    /// <summary>
    /// Get full detail for a specific scan.
    /// GET /v2/copilot-readiness/{scanId}
    /// </summary>
    [Function("CopilotReadiness_Detail")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Detail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/copilot-readiness/{scanId}")] HttpRequestData req,
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
    /// Get scan history for an organization.
    /// GET /v2/copilot-readiness/history?organizationId={guid}
    /// </summary>
    [Function("CopilotReadiness_History")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> History(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/copilot-readiness/history")] HttpRequestData req)
    {
        var orgId = ResolveOrgId(req);
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var result = await _service.GetScanHistoryAsync(orgId.Value);

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

public class CopilotReadinessScanRequest
{
    public Guid OrganizationId { get; set; }
}
