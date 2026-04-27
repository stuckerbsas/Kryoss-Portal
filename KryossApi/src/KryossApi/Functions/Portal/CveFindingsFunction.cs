using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class CveFindingsFunction
{
    private readonly KryossDbContext _db;
    private readonly ICveService _cve;

    public CveFindingsFunction(KryossDbContext db, ICveService cve)
    {
        _db = db;
        _cve = cve;
    }

    [Function("CveFindings_List")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cve-findings")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var severityFilter = query["severity"];
        var machineFilter = Guid.TryParse(query["machineId"], out var mid) ? mid : (Guid?)null;

        var q = _db.MachineCveFindings
            .Where(f => f.OrganizationId == orgId && f.Status == "open");

        if (severityFilter is not null)
            q = q.Where(f => f.Severity == severityFilter);
        if (machineFilter.HasValue)
            q = q.Where(f => f.MachineId == machineFilter.Value);

        var findings = await q
            .OrderByDescending(f => f.CvssScore)
            .ThenByDescending(f => f.FoundAt)
            .Take(500)
            .Select(f => new
            {
                f.Id,
                f.MachineId,
                machineName = f.Machine.Hostname,
                f.CveId,
                f.SoftwareName,
                f.InstalledVersion,
                f.FixedVersion,
                f.Severity,
                f.CvssScore,
                f.Description,
                f.Status,
                f.FoundAt,
            })
            .ToListAsync();

        var summary = await _db.MachineCveFindings
            .Where(f => f.OrganizationId == orgId && f.Status == "open")
            .GroupBy(f => f.Severity)
            .Select(g => new { severity = g.Key, count = g.Count() })
            .ToListAsync();

        var affectedMachines = await _db.MachineCveFindings
            .Where(f => f.OrganizationId == orgId && f.Status == "open")
            .Select(f => f.MachineId)
            .Distinct()
            .CountAsync();

        var totalMachines = await _db.Machines
            .Where(m => m.OrganizationId == orgId && m.IsActive)
            .CountAsync();

        var uniqueCves = await _db.MachineCveFindings
            .Where(f => f.OrganizationId == orgId && f.Status == "open")
            .Select(f => f.CveId)
            .Distinct()
            .CountAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new
        {
            totalFindings = findings.Count,
            affectedMachines,
            totalMachines,
            uniqueCves,
            summary,
            findings,
        });
        return res;
    }

    [Function("CveFindings_Rescan")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Rescan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/cve-findings/rescan")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<RescanRequest>();
        if (body is null || body.OrganizationId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        await _cve.ScanOrganizationAsync(body.OrganizationId);

        var count = await _db.MachineCveFindings
            .Where(f => f.OrganizationId == body.OrganizationId && f.Status == "open")
            .CountAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { status = "completed", findingsCount = count });
        return res;
    }

    [Function("CveFindings_Dismiss")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Dismiss(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/cve-findings/{findingId}/dismiss")] HttpRequestData req,
        string findingId)
    {
        if (!int.TryParse(findingId, out var id))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var finding = await _db.MachineCveFindings.FindAsync(id);
        if (finding is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        finding.Status = "ignored";
        finding.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { finding.Id, finding.Status });
        return res;
    }

    [Function("CveFindings_Stats")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> Stats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cve-findings/stats")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var topCves = await _db.MachineCveFindings
            .Where(f => f.OrganizationId == orgId && f.Status == "open")
            .GroupBy(f => new { f.CveId, f.Severity, f.CvssScore, f.Description })
            .Select(g => new
            {
                g.Key.CveId,
                g.Key.Severity,
                g.Key.CvssScore,
                g.Key.Description,
                machineCount = g.Select(x => x.MachineId).Distinct().Count(),
            })
            .OrderByDescending(x => x.CvssScore)
            .Take(20)
            .ToListAsync();

        var topSoftware = await _db.MachineCveFindings
            .Where(f => f.OrganizationId == orgId && f.Status == "open")
            .GroupBy(f => f.SoftwareName)
            .Select(g => new
            {
                softwareName = g.Key,
                cveCount = g.Select(x => x.CveId).Distinct().Count(),
                machineCount = g.Select(x => x.MachineId).Distinct().Count(),
                maxCvss = g.Max(x => x.CvssScore),
            })
            .OrderByDescending(x => x.maxCvss)
            .Take(20)
            .ToListAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { topCves, topSoftware });
        return res;
    }

    private class RescanRequest
    {
        public Guid OrganizationId { get; set; }
    }
}
