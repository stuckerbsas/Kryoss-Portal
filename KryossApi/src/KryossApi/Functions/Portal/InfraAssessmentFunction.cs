using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using KryossApi.Services.InfraAssessment;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class InfraAssessmentFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IInfraAssessmentService _service;

    public InfraAssessmentFunction(
        KryossDbContext db,
        ICurrentUserService user,
        IInfraAssessmentService service)
    {
        _db = db;
        _user = user;
        _service = service;
    }

    [Function("InfraAssessment_Scan")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Scan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/infra-assessment/scan")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<InfraAssessmentScanRequest>();
        if (body is null || body.OrganizationId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        if (!_user.IsAdmin)
        {
            var orgBelongs = _user.FranchiseId.HasValue &&
                await _db.Organizations.AnyAsync(o => o.Id == body.OrganizationId && o.FranchiseId == _user.FranchiseId.Value);
            if (!orgBelongs)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "access_denied" });
                return forbidden;
            }
        }

        var scan = await _service.StartScanAsync(body.OrganizationId, body.Scope);

        var res = req.CreateResponse(HttpStatusCode.Accepted);
        await res.WriteAsJsonAsync(new { scanId = scan.Id, status = scan.Status });
        return res;
    }

    [Function("InfraAssessment_Latest")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Latest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/infra-assessment")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var parsedOrgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId query param required" });
            return bad;
        }

        var scan = await _service.GetLatestAsync(parsedOrgId);
        if (scan is null)
        {
            var empty = req.CreateResponse(HttpStatusCode.OK);
            await empty.WriteAsJsonAsync(new { scan = (object?)null });
            return empty;
        }

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(MapScanDetail(scan));
        return res;
    }

    [Function("InfraAssessment_Detail")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Detail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/infra-assessment/{scanId}")] HttpRequestData req,
        string scanId)
    {
        if (!Guid.TryParse(scanId, out var parsedScanId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "invalid scanId" });
            return bad;
        }

        var scan = await _service.GetDetailAsync(parsedScanId);
        if (scan is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "scan_not_found" });
            return notFound;
        }

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(MapScanDetail(scan));
        return res;
    }

    [Function("InfraAssessment_History")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> History(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/infra-assessment/history")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var parsedOrgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId query param required" });
            return bad;
        }

        var scans = await _service.GetHistoryAsync(parsedOrgId);
        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(scans.Select(s => new
        {
            s.Id,
            s.Status,
            s.OverallHealth,
            s.SiteCount,
            s.DeviceCount,
            s.FindingCount,
            s.StartedAt,
            s.CompletedAt,
            s.CreatedAt
        }));
        return res;
    }

    private static object MapScanDetail(Data.Entities.InfraAssessmentScan scan) => new
    {
        scan.Id,
        scan.OrganizationId,
        scan.Status,
        scan.Scope,
        scan.OverallHealth,
        scan.SiteCount,
        scan.DeviceCount,
        scan.FindingCount,
        scan.StartedAt,
        scan.CompletedAt,
        scan.CreatedAt,
        Sites = scan.Sites.Select(s => new
        {
            s.Id, s.SiteName, s.Location, s.SiteType,
            s.DeviceCount, s.UserCount, s.ConnectivityType
        }),
        Devices = scan.Devices.Select(d => new
        {
            d.Id, d.SiteId, d.Hostname, d.DeviceType, d.Vendor,
            d.Model, d.Role, d.IpAddress, d.Os, d.Firmware, d.SerialNumber
        }),
        Connectivity = scan.Connectivity.Select(c => new
        {
            c.Id, c.SiteAId, c.SiteBId, c.LinkType,
            c.BandwidthMbps, c.LatencyMs, c.UptimePct, c.CostMonthlyUsd
        }),
        Capacity = scan.Capacity.Select(c => new
        {
            c.Id, c.DeviceId, c.MetricKey, c.CurrentValue,
            c.PeakValue, c.Threshold, c.TrendDirection
        }),
        Findings = scan.Findings.Select(f => new
        {
            f.Id, f.Area, f.Service, f.Feature, f.Status,
            f.Priority, f.Observation, f.Recommendation, f.LinkText, f.LinkUrl
        })
    };
}

public class InfraAssessmentScanRequest
{
    public Guid OrganizationId { get; set; }
    public string? Scope { get; set; }
}
