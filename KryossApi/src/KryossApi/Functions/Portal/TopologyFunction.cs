using System.Net;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

[RequirePermission("machines:read")]
public class TopologyFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public TopologyFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    [Function("Topology_Get")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/topology")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgId = query["organizationId"];
        if (string.IsNullOrEmpty(orgId) || !Guid.TryParse(orgId, out var orgGuid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var devices = await _db.SnmpDevices
            .Where(d => d.OrganizationId == orgGuid && !d.IsStale)
            .Select(d => new
            {
                d.Id, d.IpAddress, d.MacAddress, d.SysName, d.SysDescr, d.Vendor,
                d.DeviceType, d.SysLocation, d.EntityModel, d.EntityMfg,
                d.InterfaceCount, d.LldpNeighborCount, d.CdpNeighborCount,
                d.CpuLoadPct, d.MemoryTotalMb, d.MemoryUsedMb,
                d.MachineId,
            })
            .ToListAsync();

        // Load interface traffic for edge bandwidth
        var ifaceTraffic = await _db.SnmpDeviceInterfaces
            .Where(i => i.Device.OrganizationId == orgGuid && i.InRateBps != null)
            .Select(i => new { i.DeviceId, i.Name, i.InRateBps, i.OutRateBps, i.SpeedMbps })
            .ToListAsync();
        var trafficByDevice = ifaceTraffic.GroupBy(i => i.DeviceId)
            .ToDictionary(g => g.Key, g => new {
                totalInBps = g.Sum(i => i.InRateBps ?? 0),
                totalOutBps = g.Sum(i => i.OutRateBps ?? 0),
                maxSpeedMbps = g.Max(i => i.SpeedMbps ?? 0),
            });

        var neighbors = await _db.SnmpDeviceNeighbors
            .Where(n => n.Device.OrganizationId == orgGuid)
            .Select(n => new
            {
                n.DeviceId, n.Protocol, n.LocalPort,
                n.RemotePortId, n.RemotePortDesc, n.RemoteSysName,
                n.RemoteChassisId, n.RemoteDeviceIdStr,
                n.RemoteIp, n.RemotePlatform,
                n.ResolvedDeviceId,
            })
            .ToListAsync();

        // Build nodes
        var nodes = devices.Select(d => new
        {
            id = d.Id,
            label = d.SysName ?? d.IpAddress,
            ip = d.IpAddress,
            mac = d.MacAddress,
            type = d.DeviceType ?? "unknown",
            vendor = d.Vendor,
            model = d.EntityModel,
            manufacturer = d.EntityMfg,
            location = d.SysLocation,
            interfaceCount = d.InterfaceCount ?? 0,
            neighborCount = d.LldpNeighborCount + d.CdpNeighborCount,
            cpuLoadPct = d.CpuLoadPct,
            memoryTotalMb = d.MemoryTotalMb,
            memoryUsedMb = d.MemoryUsedMb,
            isAgent = d.MachineId != null,
        }).ToList();

        // Build edges — only resolved links (both ends are known devices)
        var seenEdges = new HashSet<string>();
        var edges = new List<object>();
        foreach (var n in neighbors.Where(n => n.ResolvedDeviceId != null))
        {
            var a = Math.Min(n.DeviceId, n.ResolvedDeviceId!.Value);
            var b = Math.Max(n.DeviceId, n.ResolvedDeviceId!.Value);
            var key = $"{a}-{b}";
            if (!seenEdges.Add(key)) continue;

            // Aggregate traffic from source device
            trafficByDevice.TryGetValue(n.DeviceId, out var srcTraffic);
            edges.Add(new
            {
                source = n.DeviceId,
                target = n.ResolvedDeviceId,
                protocol = n.Protocol,
                sourcePort = n.LocalPort,
                targetPort = n.RemotePortId ?? n.RemotePortDesc,
                trafficInBps = srcTraffic?.totalInBps,
                trafficOutBps = srcTraffic?.totalOutBps,
            });
        }

        // Unresolved neighbors — devices seen by LLDP/CDP but not in our SNMP inventory
        var unresolvedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var phantomNodes = new List<object>();
        foreach (var n in neighbors.Where(n => n.ResolvedDeviceId == null))
        {
            var phantomKey = n.RemoteSysName ?? n.RemoteChassisId ?? n.RemoteIp ?? n.RemoteDeviceIdStr;
            if (phantomKey == null || !unresolvedSet.Add(phantomKey)) continue;

            var phantomId = $"phantom-{phantomKey}";
            phantomNodes.Add(new
            {
                id = phantomId,
                label = n.RemoteSysName ?? n.RemoteDeviceIdStr ?? n.RemoteIp ?? "Unknown",
                ip = n.RemoteIp,
                type = ClassifyPhantom(n.RemotePlatform, n.RemoteSysName),
                vendor = (string?)null,
                phantom = true,
                platform = n.RemotePlatform,
            });
            edges.Add(new
            {
                source = (object)n.DeviceId,
                target = (object)phantomId,
                protocol = n.Protocol,
                sourcePort = n.LocalPort,
                targetPort = n.RemotePortId,
            });
        }

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            nodes = nodes.Cast<object>().Concat(phantomNodes).ToList(),
            edges,
            stats = new
            {
                totalDevices = devices.Count,
                resolvedLinks = edges.Count(e => true) - phantomNodes.Count,
                phantomDevices = phantomNodes.Count,
            },
        });
        return resp;
    }

    private static string ClassifyPhantom(string? platform, string? sysName)
    {
        var text = (platform ?? sysName ?? "").ToLowerInvariant();
        if (text.Contains("switch") || text.Contains("catalyst")) return "switch";
        if (text.Contains("router") || text.Contains("isr") || text.Contains("asr")) return "router";
        if (text.Contains("air-") || text.Contains("wireless") || text.Contains("wap")) return "access_point";
        if (text.Contains("phone") || text.Contains("voip")) return "phone";
        if (text.Contains("firewall") || text.Contains("asa") || text.Contains("fortigate")) return "firewall";
        return "unknown";
    }
}
