using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// Machine list and detail for the portal.
/// Includes latest assessment score, hardware, and software.
/// </summary>
[RequirePermission("machines:read")]
public class MachinesFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public MachinesFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    [Function("Machines_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/machines")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];
        var search = query["search"];
        var pageStr = query["page"];
        var pageSizeStr = query["pageSize"];

        int page = int.TryParse(pageStr, out var p) ? Math.Max(1, p) : 1;
        int pageSize = int.TryParse(pageSizeStr, out var ps) ? Math.Clamp(ps, 1, 100) : 25;

        IQueryable<Data.Entities.Machine> q = _db.Machines
            .Where(m => m.IsActive);

        if (Guid.TryParse(orgIdStr, out var orgId))
            q = q.Where(m => m.OrganizationId == orgId);
        else if (_user.OrganizationId.HasValue)
            q = q.Where(m => m.OrganizationId == _user.OrganizationId.Value);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(m => m.Hostname.Contains(search) || (m.OsName != null && m.OsName.Contains(search)));

        var total = await q.CountAsync();
        var machines = await q
            .OrderBy(m => m.Hostname)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.Id,
                m.OrganizationId,
                m.Hostname,
                m.OsName,
                m.OsVersion,
                m.CpuName,
                m.RamGb,
                m.DiskType,
                m.IpAddress,
                m.DomainStatus,
                m.IsActive,
                m.LastSeenAt,
                m.FirstSeenAt,
                // Latest assessment from runs
                latestScore = _db.AssessmentRuns
                    .Where(r => r.MachineId == m.Id)
                    .OrderByDescending(r => r.StartedAt)
                    .Select(r => new { r.GlobalScore, r.Grade, r.StartedAt })
                    .FirstOrDefault()
            }).ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { total, page, pageSize, items = machines });
        return response;
    }

    [Function("Machines_Get")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/machines/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var machine = await _db.Machines
            .Where(m => m.Id == id)
            .Select(m => new
            {
                m.Id,
                m.OrganizationId,
                m.AgentId,
                m.Hostname,
                // OS
                m.OsName,
                m.OsVersion,
                m.OsBuild,
                // Hardware
                m.Manufacturer,
                m.Model,
                m.SerialNumber,
                m.CpuName,
                m.CpuCores,
                m.RamGb,
                m.DiskType,
                m.DiskSizeGb,
                m.DiskFreeGb,
                // Security
                m.TpmPresent,
                m.TpmVersion,
                m.SecureBoot,
                m.Bitlocker,
                // Network
                m.IpAddress,
                m.MacAddress,
                // Identity
                m.DomainStatus,
                m.DomainName,
                // Lifecycle
                m.SystemAgeDays,
                m.LastBootAt,
                m.IsActive,
                m.LastSeenAt,
                m.FirstSeenAt,
                // Assessment history (last 10 runs)
                assessmentHistory = _db.AssessmentRuns
                    .Where(r => r.MachineId == m.Id)
                    .OrderByDescending(r => r.StartedAt)
                    .Take(10)
                    .Select(r => new
                    {
                        r.Id,
                        r.GlobalScore,
                        r.Grade,
                        r.PassCount,
                        r.WarnCount,
                        r.FailCount,
                        r.DurationMs,
                        r.StartedAt
                    }).ToList()
            }).FirstOrDefaultAsync();

        if (machine is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Machine not found" });
            return notFound;
        }

        // HIGH-01: Verify the machine belongs to the authenticated user's org/franchise
        if (!_user.IsAdmin)
        {
            var hasAccess = (_user.OrganizationId.HasValue && machine.OrganizationId == _user.OrganizationId.Value)
                || (_user.FranchiseId.HasValue && await _db.Organizations
                    .AnyAsync(o => o.Id == machine.OrganizationId && o.FranchiseId == _user.FranchiseId.Value));
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        // Load per-disk inventory
        var disks = await _db.MachineDisks
            .Where(d => d.MachineId == id)
            .OrderBy(d => d.DriveLetter)
            .Select(d => new
            {
                d.DriveLetter,
                d.Label,
                d.DiskType,
                d.TotalGb,
                d.FreeGb,
                d.FileSystem
            })
            .ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            machine.Id,
            machine.OrganizationId,
            machine.AgentId,
            machine.Hostname,
            machine.OsName,
            machine.OsVersion,
            machine.OsBuild,
            machine.Manufacturer,
            machine.Model,
            machine.SerialNumber,
            machine.CpuName,
            machine.CpuCores,
            machine.RamGb,
            machine.DiskType,
            machine.DiskSizeGb,
            machine.DiskFreeGb,
            machine.TpmPresent,
            machine.TpmVersion,
            machine.SecureBoot,
            machine.Bitlocker,
            machine.IpAddress,
            machine.MacAddress,
            machine.DomainStatus,
            machine.DomainName,
            machine.SystemAgeDays,
            machine.LastBootAt,
            machine.IsActive,
            machine.LastSeenAt,
            machine.FirstSeenAt,
            machine.assessmentHistory,
            disks
        });
        return response;
    }

    [Function("Machines_GetRunDetail")]
    public async Task<HttpResponseData> GetRunDetail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/machines/{machineId:guid}/runs/{runId:guid}")] HttpRequestData req,
        Guid machineId, Guid runId)
    {
        var run = await _db.AssessmentRuns
            .Where(r => r.Id == runId && r.MachineId == machineId)
            .Select(r => new
            {
                r.Id,
                r.GlobalScore,
                r.Grade,
                r.PassCount,
                r.WarnCount,
                r.FailCount,
                r.TotalPoints,
                r.EarnedPoints,
                r.AgentVersion,
                r.DurationMs,
                r.StartedAt,
                r.CompletedAt,
                // Per-framework scores
                frameworkScores = _db.RunFrameworkScores
                    .Where(fs => fs.RunId == r.Id)
                    .Join(_db.Frameworks, fs => fs.FrameworkId, fw => fw.Id,
                        (fs, fw) => new
                        {
                            fw.Code,
                            fw.Name,
                            fs.Score,
                            fs.PassCount,
                            fs.WarnCount,
                            fs.FailCount
                        })
                    .OrderBy(x => x.Code)
                    .ToList(),
                results = _db.ControlResults
                    .Where(cr => cr.RunId == r.Id)
                    .Join(_db.ControlDefs, cr => cr.ControlDefId, cd => cd.Id,
                        (cr, cd) => new
                        {
                            cd.ControlId,
                            cd.Name,
                            cd.Type,
                            cd.Severity,
                            categoryName = cd.Category.Name,
                            cr.Status,
                            cr.Score,
                            cr.MaxScore,
                            cr.Finding,
                            cr.ActualValue
                        })
                    .OrderBy(x => x.categoryName)
                    .ThenBy(x => x.ControlId)
                    .ToList()
            }).FirstOrDefaultAsync();

        if (run is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Run not found" });
            return notFound;
        }

        // HIGH-01: Verify the machine belongs to the authenticated user's org/franchise
        if (!_user.IsAdmin)
        {
            var machineOrgId = await _db.Machines
                .Where(m => m.Id == machineId)
                .Select(m => m.OrganizationId)
                .FirstOrDefaultAsync();
            var hasAccess = (_user.OrganizationId.HasValue && machineOrgId == _user.OrganizationId.Value)
                || (_user.FranchiseId.HasValue && await _db.Organizations
                    .AnyAsync(o => o.Id == machineOrgId && o.FranchiseId == _user.FranchiseId.Value));
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(run);
        return response;
    }

    /// <summary>
    /// Software inventory for a machine, parsed from the latest assessment run's RawPayload.
    /// </summary>
    [Function("Machines_GetSoftware")]
    public async Task<HttpResponseData> GetSoftware(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/machines/{machineId:guid}/software")] HttpRequestData req,
        Guid machineId)
    {
        // HIGH-01: Verify the machine belongs to the authenticated user's org/franchise
        if (!_user.IsAdmin)
        {
            var machineOrgId = await _db.Machines
                .Where(m => m.Id == machineId)
                .Select(m => m.OrganizationId)
                .FirstOrDefaultAsync();
            var hasAccess = (_user.OrganizationId.HasValue && machineOrgId == _user.OrganizationId.Value)
                || (_user.FranchiseId.HasValue && await _db.Organizations
                    .AnyAsync(o => o.Id == machineOrgId && o.FranchiseId == _user.FranchiseId.Value));
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        var rawPayload = await _db.AssessmentRuns
            .Where(r => r.MachineId == machineId)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => r.RawPayload)
            .FirstOrDefaultAsync();

        if (rawPayload is null)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { items = Array.Empty<object>() });
            return response;
        }

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var payload = JsonSerializer.Deserialize<AgentPayload>(rawPayload, opts);
        var software = payload?.Software?
            .OrderBy(s => s.Name)
            .Select(s => new { s.Name, s.Version, s.Publisher })
            .ToList() ?? [];

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { total = software.Count, items = software });
        return resp;
    }
}
