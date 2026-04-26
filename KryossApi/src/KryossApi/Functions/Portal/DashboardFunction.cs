using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

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

    [Function("Dashboard_Fleet")]
    public async Task<HttpResponseData> Fleet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/dashboard/fleet")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        IQueryable<Data.Entities.Machine> machineQuery = _db.Machines.Where(m => m.IsActive);
        if (Guid.TryParse(orgIdStr, out var orgId))
            machineQuery = machineQuery.Where(m => m.OrganizationId == orgId);
        else if (_user.OrganizationId.HasValue)
            machineQuery = machineQuery.Where(m => m.OrganizationId == _user.OrganizationId.Value);

        var machineIds = await machineQuery.Select(m => m.Id).ToListAsync();
        var totalMachines = machineIds.Count;

        if (totalMachines == 0)
        {
            var empty = req.CreateResponse(HttpStatusCode.OK);
            await empty.WriteAsJsonAsync(new
            {
                totalMachines = 0, assessedMachines = 0, avgScore = 0.0,
                gradeDistribution = new Dictionary<string, int>(),
                totalPass = 0, totalWarn = 0, totalFail = 0,
                topFailingControls = Array.Empty<object>(),
                frameworkScores = Array.Empty<object>(),
                agentVersions = Array.Empty<object>()
            });
            return empty;
        }

        // Latest run per machine — GroupBy+Max then join back (EF Core translates this reliably)
        var latestPerMachine = _db.AssessmentRuns
            .Where(r => machineIds.Contains(r.MachineId))
            .GroupBy(r => r.MachineId)
            .Select(g => new { MachineId = g.Key, MaxStartedAt = g.Max(r => r.StartedAt) });

        var latestRuns = await _db.AssessmentRuns
            .AsNoTracking()
            .Where(r => latestPerMachine.Any(lp => lp.MachineId == r.MachineId && lp.MaxStartedAt == r.StartedAt))
            .Select(r => new
            {
                r.Id, r.MachineId, r.GlobalScore, r.Grade,
                r.PassCount, r.WarnCount, r.FailCount
            })
            .ToListAsync();

        var assessedMachines = latestRuns.Count;
        var avgScore = latestRuns.Count > 0
            ? Math.Round(latestRuns.Average(r => (double)(r.GlobalScore ?? 0)), 1) : 0;

        var gradeDistribution = latestRuns
            .GroupBy(r => r.Grade ?? "N/A")
            .ToDictionary(g => g.Key, g => g.Count());

        var runIds = latestRuns.Select(r => r.Id).ToList();

        var topFailing = await _db.ControlResults
            .AsNoTracking()
            .Where(cr => runIds.Contains(cr.RunId) && cr.Status == "fail")
            .GroupBy(cr => cr.ControlDefId)
            .Select(g => new { controlDefId = g.Key, failCount = g.Count() })
            .OrderByDescending(x => x.failCount)
            .Take(10)
            .Join(_db.ControlDefs, x => x.controlDefId, cd => cd.Id,
                (x, cd) => new { cd.ControlId, cd.Name, cd.Severity, x.failCount })
            .ToListAsync();

        var frameworkScores = await _db.RunFrameworkScores
            .AsNoTracking()
            .Where(fs => runIds.Contains(fs.RunId))
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
                (x, fw) => new { fw.Code, fw.Name, x.avgScore, x.totalPass, x.totalWarn, x.totalFail, x.machineCount })
            .OrderBy(x => x.Code)
            .ToListAsync();

        var agentVersions = await machineQuery
            .Where(m => m.AgentVersion != null)
            .GroupBy(m => m.AgentVersion!)
            .Select(g => new { version = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
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
            frameworkScores,
            agentVersions
        });
        return response;
    }

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
            .AsNoTracking()
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
                r.Id, r.MachineId, r.GlobalScore, r.Grade,
                r.PassCount, r.WarnCount, r.FailCount, r.StartedAt
            }).ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { months, dataPoints = runs });
        return response;
    }

    [Function("Dashboard_OrgComparison")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> OrgComparison(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/dashboard/org-comparison")] HttpRequestData req)
    {
        // Batch: org list + machine counts + latest scores in 3 queries instead of N*3
        var orgs = await _db.Organizations
            .AsNoTracking()
            .Select(o => new { o.Id, o.Name })
            .ToListAsync();

        var orgIds = orgs.Select(o => o.Id).ToList();

        var machineCounts = await _db.Machines
            .AsNoTracking()
            .Where(m => orgIds.Contains(m.OrganizationId) && m.IsActive)
            .GroupBy(m => m.OrganizationId)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrgId, x => x.Count);

        // Use denormalized latest_score on machines — no assessment_runs query needed
        var latestRunsByOrg = await _db.Machines
            .AsNoTracking()
            .Where(m => orgIds.Contains(m.OrganizationId) && m.IsActive && m.LatestScore != null)
            .GroupBy(m => m.OrganizationId)
            .Select(g => new
            {
                OrgId = g.Key,
                AssessedCount = g.Count(),
                AvgScore = Math.Round(g.Average(m => (double)(m.LatestScore ?? 0)), 1),
                LastAssessment = g.Max(m => m.LatestScanAt)
            })
            .ToDictionaryAsync(x => x.OrgId);

        var result = orgs.Select(o =>
        {
            machineCounts.TryGetValue(o.Id, out var mc);
            latestRunsByOrg.TryGetValue(o.Id, out var run);
            return new
            {
                o.Id, o.Name,
                machineCount = mc,
                assessedMachines = run?.AssessedCount ?? 0,
                avgScore = run?.AvgScore ?? 0,
                lastAssessment = run?.LastAssessment
            };
        }).OrderBy(o => o.avgScore).ToList();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }
}
