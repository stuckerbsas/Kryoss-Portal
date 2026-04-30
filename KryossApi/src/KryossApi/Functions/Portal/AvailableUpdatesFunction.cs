using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Portal;

public class AvailableUpdatesFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly ILogger<AvailableUpdatesFunction> _logger;

    public AvailableUpdatesFunction(KryossDbContext db, ICurrentUserService user, ILogger<AvailableUpdatesFunction> logger)
    {
        _db = db;
        _user = user;
        _logger = logger;
    }

    // ── Agent POST: submit available updates for a machine ──

    [Function("AvailableUpdates_Submit")]
    public async Task<HttpResponseData> Submit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/available-updates")] HttpRequestData req,
        FunctionContext context)
    {
        var orgId = _user.OrganizationId;
        if (orgId is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var agentIdHeader = req.Headers.TryGetValues("X-Agent-Id", out var vals) ? vals.FirstOrDefault() : null;
        if (!Guid.TryParse(agentIdHeader, out var agentGuid))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.AgentId == agentGuid && m.OrganizationId == orgId.Value);
        if (machine is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        List<AvailableUpdateDto>? updates = null;
        if (context.Items.TryGetValue("RequestBodyBytes", out var rawObj) && rawObj is byte[] rawBytes && rawBytes.Length > 0)
            updates = JsonSerializer.Deserialize<List<AvailableUpdateDto>>(rawBytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        else
            updates = await req.ReadFromJsonAsync<List<AvailableUpdateDto>>();

        updates ??= [];

        var now = DateTime.UtcNow;
        var existing = await _db.MachineAvailableUpdates
            .Where(u => u.MachineId == machine.Id)
            .ToListAsync();

        var existingByKb = existing.ToDictionary(u => u.KbNumber, StringComparer.OrdinalIgnoreCase);
        var incomingKbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Cross-reference with installed patches
        var installedKbs = await _db.MachinePatches
            .Where(p => p.MachineId == machine.Id)
            .Select(p => p.HotfixId)
            .ToListAsync();
        var installedSet = new HashSet<string>(installedKbs, StringComparer.OrdinalIgnoreCase);

        var inserted = 0;
        var refreshed = 0;
        var resolved = 0;

        foreach (var dto in updates)
        {
            if (string.IsNullOrWhiteSpace(dto.KbNumber)) continue;
            incomingKbs.Add(dto.KbNumber);

            // WUC-04: if KB is in installed patches, skip (trust PatchCollector)
            if (installedSet.Contains(dto.KbNumber)) continue;

            if (existingByKb.TryGetValue(dto.KbNumber, out var row))
            {
                if (row.IsPending)
                {
                    row.DetectedAt = now;
                    row.Title = dto.Title ?? row.Title;
                    row.Severity = dto.Severity ?? row.Severity;
                    row.Classification = dto.Classification ?? row.Classification;
                    row.IsMandatory = dto.IsMandatory;
                    row.MaxDownloadSize = dto.MaxDownloadSize ?? row.MaxDownloadSize;
                    row.ReleaseDate = dto.ReleaseDate ?? row.ReleaseDate;
                    row.SupportUrl = dto.SupportUrl ?? row.SupportUrl;
                    refreshed++;
                }
            }
            else
            {
                _db.MachineAvailableUpdates.Add(new MachineAvailableUpdate
                {
                    MachineId = machine.Id,
                    OrganizationId = orgId.Value,
                    KbNumber = dto.KbNumber,
                    Title = dto.Title ?? dto.KbNumber,
                    Severity = dto.Severity,
                    Classification = dto.Classification,
                    IsMandatory = dto.IsMandatory,
                    MaxDownloadSize = dto.MaxDownloadSize,
                    ReleaseDate = dto.ReleaseDate,
                    SupportUrl = dto.SupportUrl,
                    DetectedAt = now,
                    IsPending = true,
                });
                inserted++;
            }
        }

        // KBs no longer in scan + still pending → installed since last scan
        foreach (var row in existing)
        {
            if (row.IsPending && !incomingKbs.Contains(row.KbNumber))
            {
                row.IsPending = false;
                row.InstalledAt = now;
                resolved++;
            }
        }

        // Also mark any that PatchCollector already shows as installed
        foreach (var row in existing)
        {
            if (row.IsPending && installedSet.Contains(row.KbNumber))
            {
                row.IsPending = false;
                row.InstalledAt = now;
                resolved++;
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("AvailableUpdates: machine={Machine} inserted={Inserted} refreshed={Refreshed} resolved={Resolved}",
            machine.Hostname, inserted, refreshed, resolved);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { ack = true, inserted, refreshed, resolved });
        return resp;
    }

    // ── Portal GET: org-level aggregation ──

    [Function("AvailableUpdates_OrgSummary")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> OrgSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/organizations/{orgId}/available-updates")] HttpRequestData req,
        string orgId)
    {
        if (!Guid.TryParse(orgId, out var oid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "invalid orgId" });
            return bad;
        }

        var totalMachines = await _db.Machines
            .CountAsync(m => m.OrganizationId == oid && m.IsActive && m.DeletedAt == null);

        var pendingUpdates = await _db.MachineAvailableUpdates
            .Where(u => u.OrganizationId == oid && u.IsPending)
            .GroupBy(u => new { u.KbNumber, u.Title, u.Severity, u.Classification, u.ReleaseDate })
            .Select(g => new
            {
                g.Key.KbNumber,
                g.Key.Title,
                g.Key.Severity,
                g.Key.Classification,
                g.Key.ReleaseDate,
                machinesPending = g.Count(),
            })
            .OrderByDescending(x => x.machinesPending)
            .ToListAsync();

        var installedCounts = await _db.MachineAvailableUpdates
            .Where(u => u.OrganizationId == oid && !u.IsPending)
            .GroupBy(u => u.KbNumber)
            .Select(g => new { KbNumber = g.Key, count = g.Count() })
            .ToDictionaryAsync(x => x.KbNumber, x => x.count, StringComparer.OrdinalIgnoreCase);

        var result = pendingUpdates.Select(u => new
        {
            u.KbNumber,
            u.Title,
            u.Severity,
            u.Classification,
            u.ReleaseDate,
            u.machinesPending,
            machinesInstalled = installedCounts.GetValueOrDefault(u.KbNumber, 0),
            totalMachines,
        }).ToList();

        var machinesWithPending = await _db.MachineAvailableUpdates
            .Where(u => u.OrganizationId == oid && u.IsPending)
            .Select(u => u.MachineId)
            .Distinct()
            .CountAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            totalMachines,
            machinesWithPending,
            totalPendingKbs = pendingUpdates.Count,
            updates = result,
        });
        return resp;
    }

    // ── Portal GET: per-machine available updates ──

    [Function("AvailableUpdates_Machine")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> MachineUpdates(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/machines/{machineId}/available-updates")] HttpRequestData req,
        string machineId)
    {
        if (!Guid.TryParse(machineId, out var mid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "invalid machineId" });
            return bad;
        }

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var showAll = query["status"] == "all";

        var q = _db.MachineAvailableUpdates.Where(u => u.MachineId == mid);
        if (!showAll)
            q = q.Where(u => u.IsPending);

        var updates = await q
            .OrderByDescending(u => u.IsPending)
            .ThenByDescending(u => u.DetectedAt)
            .Select(u => new
            {
                u.KbNumber,
                u.Title,
                u.Severity,
                u.Classification,
                u.IsMandatory,
                u.MaxDownloadSize,
                u.ReleaseDate,
                u.SupportUrl,
                u.DetectedAt,
                u.InstalledAt,
                u.IsPending,
            })
            .ToListAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            total = updates.Count,
            pending = updates.Count(u => u.IsPending),
            updates,
        });
        return resp;
    }
}

internal class AvailableUpdateDto
{
    public string KbNumber { get; set; } = null!;
    public string? Title { get; set; }
    public string? Severity { get; set; }
    public string? Classification { get; set; }
    public bool IsMandatory { get; set; }
    public long? MaxDownloadSize { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? SupportUrl { get; set; }
}
