using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class PatchComplianceFunction
{
    private readonly KryossDbContext _db;

    public PatchComplianceFunction(KryossDbContext db) => _db = db;

    [Function("PatchCompliance_Summary")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> Summary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/patch-compliance")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var statuses = await _db.MachinePatchStatuses
            .Where(p => p.OrganizationId == orgId)
            .ToListAsync();

        var totalMachines = await _db.Machines
            .CountAsync(m => m.OrganizationId == orgId && m.IsActive && m.DeletedAt == null);

        var rebootPending = statuses.Count(s => s.RebootPending);
        var unmanaged = statuses.Count(s => s.UpdateSource is "standalone" or "unknown");
        var wuStopped = statuses.Count(s => s.WuServiceStatus is "stopped" or "disabled");
        var avgScore = statuses.Count > 0 ? (int)statuses.Average(s => s.ComplianceScore) : 0;

        var sourceDistribution = statuses
            .GroupBy(s => s.UpdateSource ?? "unknown")
            .Select(g => new { source = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .ToList();

        var neverChecked = statuses.Count(s => s.LastCheckUtc == null);
        var staleCheck = statuses.Count(s =>
            s.LastCheckUtc.HasValue && (DateTime.UtcNow - s.LastCheckUtc.Value).TotalDays > 14);
        var ninjaManaged = statuses.Count(s => s.NinjaManaged);

        var machines = await _db.MachinePatchStatuses
            .Where(p => p.OrganizationId == orgId)
            .Join(_db.Machines.Where(m => m.IsActive && m.DeletedAt == null),
                p => p.MachineId, m => m.Id,
                (p, m) => new
                {
                    m.Id,
                    m.Hostname,
                    m.OsName,
                    p.UpdateSource,
                    p.LastCheckUtc,
                    p.LastInstallUtc,
                    p.RebootPending,
                    p.InstalledCount30d,
                    p.ComplianceScore,
                    p.NinjaManaged,
                    p.WuServiceStatus,
                })
            .OrderBy(x => x.ComplianceScore)
            .ToListAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            totalMachines,
            reportingMachines = statuses.Count,
            avgComplianceScore = avgScore,
            rebootPending,
            unmanaged,
            wuStopped,
            neverChecked,
            staleCheck,
            ninjaManaged,
            sourceDistribution,
            machines,
        });
        return resp;
    }

    [Function("PatchCompliance_MachinePatches")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> MachinePatches(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/patch-compliance/{machineId}/patches")] HttpRequestData req,
        string machineId)
    {
        if (!Guid.TryParse(machineId, out var mid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "invalid machineId" });
            return bad;
        }

        var patches = await _db.MachinePatches
            .Where(p => p.MachineId == mid)
            .OrderByDescending(p => p.InstalledOn)
            .Select(p => new
            {
                p.HotfixId,
                p.Description,
                p.InstalledOn,
                p.InstalledBy,
            })
            .ToListAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { patches, total = patches.Count });
        return resp;
    }
}
