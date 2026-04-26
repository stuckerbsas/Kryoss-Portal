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
            .AsNoTracking()
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
                m.AgentVersion,
                m.IsActive,
                m.IsTrial,
                m.TrialExpiresAt,
                m.LastSeenAt,
                m.FirstSeenAt,
                m.LastHeartbeatAt,
                m.AgentMode,
                m.LatestScore,
                m.LatestGrade,
                m.LatestScanAt,
            }).ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            total, page, pageSize,
            items = machines.Select(m => new
            {
                m.Id, m.OrganizationId, m.Hostname, m.OsName, m.OsVersion,
                m.CpuName, m.RamGb, m.DiskType, m.IpAddress, m.DomainStatus,
                m.AgentVersion, m.IsActive, m.IsTrial, m.TrialExpiresAt,
                m.LastSeenAt, m.FirstSeenAt, m.LastHeartbeatAt, m.AgentMode,
                latestScore = m.LatestScore != null ? new { globalScore = m.LatestScore, grade = m.LatestGrade, startedAt = m.LatestScanAt } : null,
            })
        });
        return response;
    }

    [Function("Machines_GetByHostname")]
    public async Task<HttpResponseData> GetByHostname(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/machines/by-hostname/{hostname}")] HttpRequestData req,
        string hostname)
    {
        var machineId = await _db.Machines
            .Where(m => m.Hostname == hostname && m.IsActive)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();
        if (machineId == Guid.Empty)
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            await nf.WriteAsJsonAsync(new { error = "Machine not found" });
            return nf;
        }
        return await Get(req, machineId);
    }

    [Function("Machines_Get")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/machines/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var machine = await _db.Machines
            .AsNoTracking()
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
                // Agent
                m.AgentVersion,
                // Lifecycle
                m.SystemAgeDays,
                m.LastBootAt,
                m.IsActive,
                m.IsTrial,
                m.TrialExpiresAt,
                m.LastSeenAt,
                m.FirstSeenAt,
                m.LocalAdminsJson,
                // Agent config
                m.ConfigComplianceIntervalHours,
                m.ConfigSnmpIntervalMinutes,
                m.ConfigEnableNetworkScan,
                m.ConfigNetworkScanIntervalHours,
                m.ConfigEnablePassiveDiscovery,
                m.ForceScanRequestedAt,
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

        // Per-machine local administrators (from agent payload)
        object? localAdmins = null;
        if (!string.IsNullOrEmpty(machine.LocalAdminsJson))
        {
            try { localAdmins = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(machine.LocalAdminsJson); }
            catch { }
        }

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
            disks,
            localAdmins,
            agentConfig = new
            {
                complianceIntervalHours = machine.ConfigComplianceIntervalHours,
                snmpIntervalMinutes = machine.ConfigSnmpIntervalMinutes,
                enableNetworkScan = machine.ConfigEnableNetworkScan,
                networkScanIntervalHours = machine.ConfigNetworkScanIntervalHours,
                enablePassiveDiscovery = machine.ConfigEnablePassiveDiscovery,
            },
            scanPending = machine.ForceScanRequestedAt.HasValue,
            scanRequestedAt = machine.ForceScanRequestedAt,
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

    [Function("Machines_TriggerScan")]
    [RequirePermission("machines:write")]
    public async Task<HttpResponseData> TriggerScan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/machines/{machineId:guid}/trigger-scan")] HttpRequestData req,
        Guid machineId)
    {
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.Id == machineId);
        if (machine is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

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

        machine.ForceScanRequestedAt = DateTime.UtcNow;
        machine.ForceScanRequestedBy = _user.UserId;
        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { queued = true, message = "Scan will run on next heartbeat" });
        return response;
    }

    [Function("Machines_PatchAgentConfig")]
    public async Task<HttpResponseData> PatchAgentConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/machines/{machineId:guid}/agent-config")] HttpRequestData req,
        Guid machineId)
    {
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.Id == machineId);
        if (machine is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        var body = await req.ReadFromJsonAsync<AgentConfigPatch>();
        if (body is null)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        if (body.ComplianceIntervalHours.HasValue)
            machine.ConfigComplianceIntervalHours = Math.Clamp(body.ComplianceIntervalHours.Value, 1, 168);
        if (body.SnmpIntervalMinutes.HasValue)
            machine.ConfigSnmpIntervalMinutes = Math.Clamp(body.SnmpIntervalMinutes.Value, 30, 1440);
        if (body.EnableNetworkScan.HasValue)
            machine.ConfigEnableNetworkScan = body.EnableNetworkScan.Value;
        if (body.NetworkScanIntervalHours.HasValue)
            machine.ConfigNetworkScanIntervalHours = Math.Clamp(body.NetworkScanIntervalHours.Value, 1, 168);
        if (body.EnablePassiveDiscovery.HasValue)
            machine.ConfigEnablePassiveDiscovery = body.EnablePassiveDiscovery.Value;

        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            complianceIntervalHours = machine.ConfigComplianceIntervalHours,
            snmpIntervalMinutes = machine.ConfigSnmpIntervalMinutes,
            enableNetworkScan = machine.ConfigEnableNetworkScan,
            networkScanIntervalHours = machine.ConfigNetworkScanIntervalHours,
            enablePassiveDiscovery = machine.ConfigEnablePassiveDiscovery,
        });
        return response;
    }
}

public class AgentConfigPatch
{
    public int? ComplianceIntervalHours { get; set; }
    public int? SnmpIntervalMinutes { get; set; }
    public bool? EnableNetworkScan { get; set; }
    public int? NetworkScanIntervalHours { get; set; }
    public bool? EnablePassiveDiscovery { get; set; }
}
