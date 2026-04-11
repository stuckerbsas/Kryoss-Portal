using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// Fleet dashboard endpoints: summary KPIs, score trends, top risks.
/// Used by the portal dashboard page.
/// </summary>
[RequirePermission("assessment:read")]
public class DashboardFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public DashboardFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    /// <summary>
    /// Fleet summary: total machines, avg score, grade distribution, top failing controls.
    /// </summary>
    [Function("Dashboard_Fleet")]
    public async Task<HttpResponseData> Fleet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/dashboard/fleet")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        // Get all machines for this org/franchise
        IQueryable<Data.Entities.Machine> machineQuery = _db.Machines.Where(m => m.IsActive);
        if (Guid.TryParse(orgIdStr, out var orgId))
            machineQuery = machineQuery.Where(m => m.OrganizationId == orgId);
        else if (_user.OrganizationId.HasValue)
            machineQuery = machineQuery.Where(m => m.OrganizationId == _user.OrganizationId.Value);

        var machineIds = await machineQuery.Select(m => m.Id).ToListAsync();
        var totalMachines = machineIds.Count;

        // Get latest run per machine
        var latestRuns = await _db.AssessmentRuns
            .Where(r => machineIds.Contains(r.MachineId))
            .GroupBy(r => r.MachineId)
            .Select(g => g.OrderByDescending(r => r.StartedAt).First())
            .ToListAsync();

        var assessedMachines = latestRuns.Count;
        var avgScore = latestRuns.Count > 0
            ? Math.Round(latestRuns.Average(r => (double)(r.GlobalScore ?? 0)), 1)
            : 0;

        var gradeDistribution = latestRuns
            .GroupBy(r => r.Grade ?? "N/A")
            .ToDictionary(g => g.Key, g => g.Count());

        // Top 10 most-failing controls across fleet
        var latestRunIds = latestRuns.Select(r => r.Id).ToList();
        var topFailing = await _db.ControlResults
            .Where(cr => latestRunIds.Contains(cr.RunId) && cr.Status == "fail")
            .GroupBy(cr => cr.ControlDefId)
            .Select(g => new
            {
                controlDefId = g.Key,
                failCount = g.Count()
            })
            .OrderByDescending(x => x.failCount)
            .Take(10)
            .Join(_db.ControlDefs, x => x.controlDefId, cd => cd.Id,
                (x, cd) => new { cd.ControlId, cd.Name, cd.Severity, x.failCount })
            .ToListAsync();

        // Aggregate framework scores across latest runs
        var frameworkScores = await _db.RunFrameworkScores
            .Where(fs => latestRunIds.Contains(fs.RunId))
            .GroupBy(fs => fs.FrameworkId)
            .Select(g => new
            {
                frameworkId = g.Key,
                avgScore = Math.Round(g.Average(fs => (double)fs.Score), 1),
                totalPass = g.Sum(fs => (int)fs.PassCount),
                totalWarn = g.Sum(fs => (int)fs.WarnCount),
                totalFail = g.Sum(fs => (int)fs.FailCount),
                machineCount = g.Count()
            })
            .Join(_db.Frameworks, x => x.frameworkId, fw => fw.Id,
                (x, fw) => new
                {
                    fw.Code,
                    fw.Name,
                    x.avgScore,
                    x.totalPass,
                    x.totalWarn,
                    x.totalFail,
                    x.machineCount
                })
            .OrderBy(x => x.Code)
            .ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            totalMachines,
            assessedMachines,
            avgScore,
            gradeDistribution,
            totalPass = latestRuns.Sum(r => r.PassCount ?? 0),
            totalWarn = latestRuns.Sum(r => r.WarnCount ?? 0),
            totalFail = latestRuns.Sum(r => r.FailCount ?? 0),
            topFailingControls = topFailing,
            frameworkScores
        });
        return response;
    }

    /// <summary>
    /// Score trend over time for an org or specific machine.
    /// </summary>
    [Function("Dashboard_Trend")]
    public async Task<HttpResponseData> Trend(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/dashboard/trend")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];
        var machineIdStr = query["machineId"];
        var monthsStr = query["months"];

        int months = int.TryParse(monthsStr, out var m) ? Math.Clamp(m, 1, 24) : 6;
        var since = DateTime.UtcNow.AddMonths(-months);

        IQueryable<Data.Entities.AssessmentRun> q = _db.AssessmentRuns
            .Where(r => r.StartedAt >= since);

        if (Guid.TryParse(machineIdStr, out var machineId))
            q = q.Where(r => r.MachineId == machineId);
        else if (Guid.TryParse(orgIdStr, out var oid))
            q = q.Where(r => r.OrganizationId == oid);
        else if (_user.OrganizationId.HasValue)
            q = q.Where(r => r.OrganizationId == _user.OrganizationId.Value);

        var runs = await q
            .OrderBy(r => r.StartedAt)
            .Select(r => new
            {
                r.Id,
                r.MachineId,
                r.GlobalScore,
                r.Grade,
                r.PassCount,
                r.WarnCount,
                r.FailCount,
                r.StartedAt
            }).ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { months, dataPoints = runs });
        return response;
    }

    /// <summary>
    /// Cross-org comparison (franchise-level view).
    /// Shows each org's avg score, machine count, last assessment date.
    /// </summary>
    [Function("Dashboard_OrgComparison")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> OrgComparison(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/dashboard/org-comparison")] HttpRequestData req)
    {
        // Only useful for franchise owners or admins
        var orgs = await _db.Organizations
            .Select(o => new
            {
                o.Id,
                o.Name,
                machineCount = _db.Machines.Count(m => m.OrganizationId == o.Id && m.IsActive),
                latestRuns = _db.AssessmentRuns
                    .Where(r => r.OrganizationId == o.Id)
                    .GroupBy(r => r.MachineId)
                    .Select(g => g.OrderByDescending(r => r.StartedAt).First())
                    .ToList()
            }).ToListAsync();

        var result = orgs.Select(o => new
        {
            o.Id,
            o.Name,
            o.machineCount,
            assessedMachines = o.latestRuns.Count,
            avgScore = o.latestRuns.Count > 0
                ? Math.Round(o.latestRuns.Average(r => (double)(r.GlobalScore ?? 0)), 1) : 0,
            lastAssessment = o.latestRuns.Count > 0
                ? o.latestRuns.Max(r => r.StartedAt) : (DateTime?)null
        }).OrderBy(o => o.avgScore).ToList();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }
}
