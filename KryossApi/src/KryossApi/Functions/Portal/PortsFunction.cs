using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// Port scan results: agent submits via HMAC (v1), portal reads via Bearer (v2).
/// </summary>
public class PortsFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public PortsFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    // ── Agent submits port scan results ──

    /// <summary>
    /// POST /v1/ports — Agent submits port scan results for a machine.
    /// Auth: HMAC (agent route).
    /// </summary>
    [Function("Ports_Submit")]
    public async Task<HttpResponseData> Submit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/ports")] HttpRequestData req,
        FunctionContext context)
    {
        // Body may have been consumed by HMAC middleware — check Items first
        PortsSubmitRequest? body = null;
        if (context.Items.TryGetValue("RequestBodyBytes", out var rawObj) && rawObj is byte[] rawBytes && rawBytes.Length > 0)
        {
            body = JsonSerializer.Deserialize<PortsSubmitRequest>(rawBytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        else
        {
            body = await req.ReadFromJsonAsync<PortsSubmitRequest>();
        }

        if (body is null || body.Ports is null || string.IsNullOrWhiteSpace(body.MachineHostname))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid port scan payload" });
            return bad;
        }

        var orgId = _user.OrganizationId;
        if (orgId is null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Organization context required" });
            return unauth;
        }

        // Resolve machine by hostname + org
        var machine = await _db.Machines
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId.Value
                                   && m.Hostname == body.MachineHostname
                                   && m.IsActive);

        if (machine is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = $"Machine '{body.MachineHostname}' not found" });
            return notFound;
        }

        // Upsert: delete old ports for this machine, then insert new ones
        await _db.MachinePorts
            .Where(p => p.MachineId == machine.Id)
            .ExecuteDeleteAsync();

        var now = DateTime.UtcNow;
        var seen = new HashSet<(int, string)>();
        foreach (var p in body.Ports)
        {
            var key = (p.Port, (p.Protocol ?? "TCP").ToUpperInvariant());
            if (!seen.Add(key)) continue;
            _db.MachinePorts.Add(new MachinePort
            {
                MachineId = machine.Id,
                Port = p.Port,
                Protocol = p.Protocol ?? "TCP",
                Status = p.Status ?? "open",
                Service = p.Service,
                Risk = p.Risk,
                Banner = p.Banner,
                ServiceName = p.ServiceName,
                ServiceVersion = p.ServiceVersion,
                ScannedAt = now,
            });
        }

        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { machineId = machine.Id, portsCount = body.Ports.Count });
        return response;
    }

    /// <summary>
    /// POST /v1/ports/bulk — Agent submits port scan results for multiple machines in one call.
    /// Auth: HMAC (agent route).
    /// </summary>
    [Function("Ports_SubmitBulk")]
    public async Task<HttpResponseData> SubmitBulk(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/ports/bulk")] HttpRequestData req,
        FunctionContext context)
    {
        PortsBulkSubmitRequest? body = null;
        if (context.Items.TryGetValue("RequestBodyBytes", out var rawObj) && rawObj is byte[] rawBytes && rawBytes.Length > 0)
        {
            body = JsonSerializer.Deserialize<PortsBulkSubmitRequest>(rawBytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        else
        {
            body = await req.ReadFromJsonAsync<PortsBulkSubmitRequest>();
        }

        if (body?.Machines is null or { Count: 0 })
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid bulk port payload" });
            return bad;
        }

        var orgId = _user.OrganizationId;
        if (orgId is null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Organization context required" });
            return unauth;
        }

        var hostnames = body.Machines.Select(m => m.MachineHostname).Distinct().ToList();
        var knownMachines = await _db.Machines
            .Where(m => m.OrganizationId == orgId.Value && m.IsActive && hostnames.Contains(m.Hostname))
            .ToDictionaryAsync(m => m.Hostname, StringComparer.OrdinalIgnoreCase);

        int saved = 0;
        var skipped = new List<string>();
        var now = DateTime.UtcNow;

        foreach (var entry in body.Machines)
        {
            if (!knownMachines.TryGetValue(entry.MachineHostname, out var machine))
            {
                skipped.Add(entry.MachineHostname);
                continue;
            }

            await _db.MachinePorts
                .Where(p => p.MachineId == machine.Id)
                .ExecuteDeleteAsync();

            var seen = new HashSet<(int, string)>();
            foreach (var p in entry.Ports)
            {
                var key = (p.Port, (p.Protocol ?? "TCP").ToUpperInvariant());
                if (!seen.Add(key)) continue;
                _db.MachinePorts.Add(new MachinePort
                {
                    MachineId = machine.Id,
                    Port = p.Port,
                    Protocol = p.Protocol ?? "TCP",
                    Status = p.Status ?? "open",
                    Service = p.Service,
                    Risk = p.Risk,
                    Banner = p.Banner,
                    ServiceName = p.ServiceName,
                    ServiceVersion = p.ServiceVersion,
                    ScannedAt = now,
                });
            }

            saved++;
        }

        if (saved > 0)
            await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { saved, skipped = skipped.Count, skippedHosts = skipped });
        return response;
    }

    // ── Portal reads machine ports ──

    /// <summary>
    /// GET /v2/ports?machineId={guid} — Portal reads port scan results for a machine.
    /// Auth: Bearer + machines:read.
    /// </summary>
    [Function("Ports_Machine")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> GetMachinePorts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/ports")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var machineIdStr = query["machineId"];

        if (!Guid.TryParse(machineIdStr, out var machineId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "machineId is required" });
            return bad;
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

        var ports = await _db.MachinePorts
            .Where(p => p.MachineId == machineId)
            .OrderByDescending(p =>
                p.Risk == "critical" ? 4 :
                p.Risk == "high" ? 3 :
                p.Risk == "medium" ? 2 :
                p.Risk == "low" ? 1 : 0)
            .ThenBy(p => p.Port)
            .Select(p => new
            {
                p.Port,
                p.Protocol,
                p.Status,
                p.Service,
                p.Risk,
                p.Banner,
                p.ServiceName,
                p.ServiceVersion,
                p.ScannedAt,
            })
            .ToListAsync();

        var totalOpen = ports.Count(p => p.Status == "open");
        var critical = ports.Count(p => p.Risk == "critical");
        var high = ports.Count(p => p.Risk == "high");
        var medium = ports.Count(p => p.Risk == "medium");

        var result = new
        {
            totalOpen,
            critical,
            high,
            medium,
            ports,
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    // ── Portal reads org-level port summary ──

    /// <summary>
    /// GET /v2/ports/org?organizationId={guid} — Org-level port scan summary.
    /// Auth: Bearer + machines:read.
    /// </summary>
    [Function("Ports_Org")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> GetOrgPorts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/ports/org")] HttpRequestData req)
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

        // Get all ports for those machines
        var allPorts = await _db.MachinePorts
            .Where(p => machineIds.Contains(p.MachineId))
            .Include(p => p.Machine)
            .ToListAsync();

        var machinesWithRiskyPorts = allPorts
            .Where(p => p.Risk is "critical" or "high" or "medium")
            .Select(p => p.MachineId)
            .Distinct()
            .Count();

        var criticalPorts = allPorts.Count(p => p.Risk == "critical");
        var highRiskPorts = allPorts.Count(p => p.Risk == "high");

        // Top risky ports: group by port+service+risk, count machines
        var topRiskyPorts = allPorts
            .Where(p => p.Risk is "critical" or "high" or "medium")
            .GroupBy(p => new { p.Port, p.Service, p.Risk })
            .Select(g => new
            {
                g.Key.Port,
                service = g.Key.Service,
                risk = g.Key.Risk,
                machineCount = g.Select(p => p.MachineId).Distinct().Count(),
                machines = g.Select(p => p.Machine.Hostname).Distinct().OrderBy(h => h).ToList(),
            })
            .OrderByDescending(x =>
                x.risk == "critical" ? 3 :
                x.risk == "high" ? 2 : 1)
            .ThenByDescending(x => x.machineCount)
            .Take(20)
            .ToList();

        var result = new
        {
            totalMachines,
            machinesWithRiskyPorts,
            criticalPorts,
            highRiskPorts,
            topRiskyPorts,
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }
}

// ── DTOs ──

public class PortsSubmitRequest
{
    public string MachineHostname { get; set; } = null!;
    public List<PortItem> Ports { get; set; } = [];
}

public class PortItem
{
    public int Port { get; set; }
    public string? Protocol { get; set; }
    public string? Status { get; set; }
    public string? Service { get; set; }
    public string? Risk { get; set; }
    public string? Banner { get; set; }
    public string? ServiceName { get; set; }
    public string? ServiceVersion { get; set; }
}

public class PortsBulkSubmitRequest
{
    public List<PortsSubmitRequest> Machines { get; set; } = [];
}
