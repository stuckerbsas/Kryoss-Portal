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
    private readonly IBlobPayloadService? _blob;

    public MachinesFunction(KryossDbContext db, ICurrentUserService user, IBlobPayloadService? blob = null)
    {
        _db = db;
        _user = user;
        _blob = blob;
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
                m.AadTenantId,
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
                // Agent config
                m.ConfigComplianceIntervalHours,
                m.ConfigSnmpIntervalMinutes,
                m.ConfigEnableNetworkScan,
                m.ConfigNetworkScanIntervalHours,
                m.ConfigEnablePassiveDiscovery,
                m.ForceScanRequestedAt,
                // Service mode
                m.AgentMode,
                m.AgentUptimeSeconds,
                m.LastHeartbeatAt,
                // Loop status (v2.8.0)
                m.LastErrorAt,
                m.LastErrorPhase,
                m.LastErrorMsg,
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

        var localAdmins = await _db.MachineLocalAdmins
            .Where(a => a.MachineId == id)
            .Select(a => new { a.Name, a.Type, a.Source })
            .ToListAsync();

        var loopStatusList = await _db.MachineLoopStatuses
            .Where(ls => ls.MachineId == id)
            .ToListAsync();
        var loopStatus = loopStatusList.ToDictionary(
            ls => ls.LoopName,
            ls => new { state = ls.State, lastRunAt = ls.LastRunAt, lastDurationMs = ls.DurationMs, lastError = ls.LastError });

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
            machine.AadTenantId,
            machine.SystemAgeDays,
            machine.LastBootAt,
            machine.IsActive,
            machine.LastSeenAt,
            machine.FirstSeenAt,
            machine.assessmentHistory,
            disks,
            localAdmins = localAdmins.Count > 0 ? (object)localAdmins : null,
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
            // Service mode
            machine.AgentMode,
            machine.AgentUptimeSeconds,
            machine.LastHeartbeatAt,
            // Loop status
            loopStatus = loopStatus.Count > 0 ? loopStatus : null,
            machine.LastErrorAt,
            machine.LastErrorPhase,
            machine.LastErrorMsg,
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

        var software = await _db.MachineSoftware
            .Include(ms => ms.Software)
            .Where(ms => ms.MachineId == machineId && ms.RemovedAt == null)
            .OrderBy(ms => ms.Software.Name)
            .Select(ms => new
            {
                ms.Software.Name,
                ms.Version,
                ms.Software.Publisher,
                ms.Software.Category,
                ms.DetectedAt,
            })
            .ToListAsync();

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

    [Function("Machine_Activity")]
    public async Task<HttpResponseData> Activity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/machines/{id}/activity")] HttpRequestData req,
        string id)
    {
        if (!Guid.TryParse(id, out var machineId))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var page = int.TryParse(qs["page"], out var p) ? p : 1;
        var pageSize = int.TryParse(qs["pageSize"], out var ps) ? Math.Min(ps, 100) : 50;
        var severityFilter = qs["severity"];

        var machineIdStr = machineId.ToString();
        var actlogItems = await _db.Actlog
            .Where(a => a.MachineId == machineId
                || (a.EntityType == "machine" && a.EntityId == machineIdStr))
            .OrderByDescending(a => a.Timestamp)
            .Take(200)
            .Select(a => new ActivityEntry
            {
                Timestamp = a.Timestamp,
                Type = a.Action,
                Severity = a.Severity,
                Action = a.Message ?? a.Action,
                ActorEmail = a.ActorEmail,
                Source = "actlog",
            })
            .ToListAsync();

        var remlogItems = await _db.RemediationLogs
            .Where(r => r.MachineId == machineId)
            .OrderByDescending(r => r.Timestamp)
            .Take(200)
            .Select(r => new ActivityEntry
            {
                Timestamp = r.Timestamp,
                Type = r.EventType,
                Severity = r.EventType == "rejected" ? "ERR"
                    : r.EventType == "failed" ? "WARN"
                    : r.EventType == "service_heal" ? "WARN"
                    : "INFO",
                Action = r.ServiceName != null
                    ? r.EventType + ": " + r.ActionType + " on " + r.ServiceName
                    : r.EventType + ": " + r.ActionType,
                Source = "remediation",
                ServiceName = r.ServiceName,
                ErrorMessage = r.ErrorMessage,
            })
            .ToListAsync();

        IEnumerable<ActivityEntry> combined = actlogItems.Concat(remlogItems)
            .OrderByDescending(a => a.Timestamp);

        if (!string.IsNullOrEmpty(severityFilter))
            combined = combined.Where(a => a.Severity == severityFilter);

        var allItems = combined.ToList();
        var total = allItems.Count;
        var items = allItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { total, page, pageSize, items });
        return ok;
    }
}

internal class ActivityEntry
{
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string? ActorEmail { get; set; }
    public string Source { get; set; } = null!;
    public string? ServiceName { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AgentConfigPatch
{
    public int? ComplianceIntervalHours { get; set; }
    public int? SnmpIntervalMinutes { get; set; }
    public bool? EnableNetworkScan { get; set; }
    public int? NetworkScanIntervalHours { get; set; }
    public bool? EnablePassiveDiscovery { get; set; }
}

[RequirePermission("machines:read")]
public class LocalAdminsFunction
{
    private readonly KryossDbContext _db;

    public LocalAdminsFunction(KryossDbContext db) => _db = db;

    [Function("LocalAdmins_ByOrg")]
    public async Task<HttpResponseData> ByOrg(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/local-admins")] HttpRequestData req)
    {
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(qs["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var admins = await _db.MachineLocalAdmins
            .Where(a => a.Machine.OrganizationId == orgId && a.Machine.IsActive)
            .Select(a => new
            {
                a.Name,
                a.Type,
                a.Source,
                machineId = a.MachineId,
                hostname = a.Machine.Hostname,
            })
            .ToListAsync();

        var grouped = admins
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                name = g.Key,
                type = g.First().Type,
                source = g.First().Source,
                machineCount = g.Count(),
                machines = g.Select(m => new { m.machineId, m.hostname }).ToList(),
            })
            .OrderByDescending(g => g.machineCount)
            .ThenBy(g => g.name)
            .ToList();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { totalAccounts = grouped.Count, totalEntries = admins.Count, admins = grouped });
        return ok;
    }
}
