using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// Threat detection findings: portal reads via Bearer (v2).
/// </summary>
public class ThreatsFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public ThreatsFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    // ── Portal reads machine threats ──

    /// <summary>
    /// GET /v2/threats?machineId={guid} — Portal reads threat findings for a machine.
    /// Auth: Bearer + machines:read.
    /// </summary>
    [Function("Threats_Machine")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> GetMachineThreats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/threats")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var machineIdStr = query["machineId"];

        if (!Guid.TryParse(machineIdStr, out var machineId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "machineId is required" });
            return bad;
        }

        var threats = await _db.MachineThreats
            .Where(t => t.MachineId == machineId)
            .OrderByDescending(t =>
                t.Severity == "critical" ? 4 :
                t.Severity == "high" ? 3 :
                t.Severity == "medium" ? 2 :
                t.Severity == "low" ? 1 : 0)
            .ThenBy(t => t.ThreatName)
            .Select(t => new
            {
                t.ThreatName,
                t.Category,
                t.Severity,
                t.Vector,
                t.Detail,
                t.DetectedAt,
            })
            .ToListAsync();

        var result = new
        {
            total = threats.Count,
            critical = threats.Count(t => t.Severity == "critical"),
            high = threats.Count(t => t.Severity == "high"),
            medium = threats.Count(t => t.Severity == "medium"),
            low = threats.Count(t => t.Severity == "low"),
            threats,
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    // ── Portal reads org-level threat summary ──

    /// <summary>
    /// GET /v2/threats/org?organizationId={guid} — Org-level threat summary.
    /// Auth: Bearer + machines:read.
    /// </summary>
    [Function("Threats_Org")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> GetOrgThreats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/threats/org")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        Guid? orgId = Guid.TryParse(orgIdStr, out var parsed) ? parsed : _user.OrganizationId;
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        // Get all active machine IDs for this org
        var machineIds = await _db.Machines
            .Where(m => m.OrganizationId == orgId.Value && m.IsActive)
            .Select(m => m.Id)
            .ToListAsync();

        var totalMachines = machineIds.Count;

        // Get all threats for those machines
        var allThreats = await _db.MachineThreats
            .Where(t => machineIds.Contains(t.MachineId))
            .Include(t => t.Machine)
            .ToListAsync();

        var machinesWithThreats = allThreats
            .Select(t => t.MachineId)
            .Distinct()
            .Count();

        var criticalThreats = allThreats.Count(t => t.Severity == "critical");
        var highThreats = allThreats.Count(t => t.Severity == "high");

        // Top threats: group by threat name + severity + category, count machines
        var topThreats = allThreats
            .GroupBy(t => new { t.ThreatName, t.Severity, t.Category })
            .Select(g => new
            {
                g.Key.ThreatName,
                g.Key.Severity,
                g.Key.Category,
                machineCount = g.Select(t => t.MachineId).Distinct().Count(),
                machines = g.Select(t => t.Machine.Hostname).Distinct().OrderBy(h => h).ToList(),
            })
            .OrderByDescending(x =>
                x.Severity == "critical" ? 4 :
                x.Severity == "high" ? 3 :
                x.Severity == "medium" ? 2 :
                x.Severity == "low" ? 1 : 0)
            .ThenByDescending(x => x.machineCount)
            .Take(30)
            .ToList();

        var result = new
        {
            totalMachines,
            machinesWithThreats,
            criticalThreats,
            highThreats,
            topThreats,
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }
}
