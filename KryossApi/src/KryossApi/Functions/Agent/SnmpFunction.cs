using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Agent;

public class SnmpFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public SnmpFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    [Function("Snmp_GetConfig")]
    public async Task<HttpResponseData> GetConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/snmp-config")] HttpRequestData req)
    {
        var orgId = _user.OrganizationId ?? Guid.Empty;
        if (orgId == Guid.Empty)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var config = await _db.SnmpConfigs
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.Enabled);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        if (config == null)
        {
            // Default: v2c + community "public" + auto-discover targets
            await resp.WriteAsJsonAsync(new
            {
                version = 2,
                community = "public",
                username = (string?)null,
                authProtocol = (string?)null,
                authPassword = (string?)null,
                privProtocol = (string?)null,
                privPassword = (string?)null,
                targets = (List<string>?)null,
            });
        }
        else
        {
            await resp.WriteAsJsonAsync(new
            {
                version = (int)config.SnmpVersion,
                community = config.Community,
                username = config.Username,
                authProtocol = config.AuthProtocol,
                authPassword = config.AuthPassword,
                privProtocol = config.PrivProtocol,
                privPassword = config.PrivPassword,
                targets = string.IsNullOrEmpty(config.Targets)
                    ? null
                    : JsonSerializer.Deserialize<List<string>>(config.Targets),
            });
        }
        return resp;
    }

    [Function("Snmp_SubmitResults")]
    public async Task<HttpResponseData> SubmitResults(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/snmp")] HttpRequestData req,
        FunctionContext context)
    {
        _db.Database.SetCommandTimeout(120);
        var orgId = _user.OrganizationId ?? Guid.Empty;
        if (orgId == Guid.Empty)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        // Body may have been consumed by HMAC middleware — check Items first
        string? body = null;
        if (context.Items.TryGetValue("RequestBodyBytes", out var rawObj) && rawObj is byte[] rawBytes && rawBytes.Length > 0)
            body = System.Text.Encoding.UTF8.GetString(rawBytes);
        else
            body = await req.ReadAsStringAsync();

        if (string.IsNullOrEmpty(body))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var payload = JsonSerializer.Deserialize<SnmpPayload>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (payload?.Devices == null)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var scanSource = req.Headers.TryGetValues("X-Machine-Name", out var mnVals)
            ? mnVals.FirstOrDefault() : null;

        // Phase 1: client-side dedup by IP (last wins)
        var byIp = payload.Devices
            .GroupBy(d => d.Ip)
            .Select(g => g.Last())
            .ToList();

        // Phase 2: MAC-based merge — same MAC with different IPs = same physical device.
        // Keep the entry with the richest data (has sysName/sysDescr), track secondary IPs.
        var merged = new List<(SnmpDeviceDto Device, List<string> AllIps)>();
        var macIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var dev in byIp)
        {
            var mac = dev.MacAddress?.ToUpperInvariant();
            if (mac is not null and not "" and not "FF:FF:FF:FF:FF:FF" and not "00:00:00:00:00:00"
                && macIndex.TryGetValue(mac, out var idx))
            {
                var (existing, ips) = merged[idx];
                ips.Add(dev.Ip);
                // Keep the richer entry (has SNMP data vs ARP-only)
                if (dev.SysName != null && existing.SysName == null)
                {
                    ips.Remove(dev.Ip);
                    ips.Add(existing.Ip);
                    merged[idx] = (dev, ips);
                }
            }
            else
            {
                var entry = (dev, new List<string> { dev.Ip });
                if (mac is not null and not "")
                    macIndex[mac] = merged.Count;
                merged.Add(entry);
            }
        }

        // Phase 3: Filter ARP-only noise — skip devices with no SNMP data AND unknown vendor
        var useful = merged.Where(m =>
            m.Device.SysName != null || m.Device.SysDescr != null
            || m.Device.DeviceType is not null and not "unknown"
            || m.Device.Vendor != null
            || m.Device.HostResources != null
        ).ToList();
        int filtered = merged.Count - useful.Count;

        // Load existing devices for this org — by IP and by MAC for merge lookups
        var allExisting = await _db.SnmpDevices
            .Where(d => d.OrganizationId == orgId)
            .ToListAsync();
        var existingByIp = allExisting
            .GroupBy(d => d.IpAddress)
            .ToDictionary(g => g.Key, g => g.First());
        var existingByMac = allExisting
            .Where(d => d.MacAddress != null)
            .GroupBy(d => d.MacAddress!)
            .ToDictionary(g => g.Key, g => g.First());

        // Load machines for correlation (match by IP)
        var machineIps = await _db.Set<Data.Entities.Machine>()
            .Where(m => m.OrganizationId == orgId && m.IpAddress != null)
            .Select(m => new { m.Id, m.IpAddress })
            .ToListAsync();
        var machineByIp = machineIps
            .Where(m => m.IpAddress != null)
            .ToDictionary(m => m.IpAddress!, m => m.Id);

        var now = DateTime.UtcNow;
        int created = 0, updated = 0, mergedCount = 0;
        int batchCounter = 0;

        foreach (var (dev, allIps) in useful)
        {
            var primaryIp = dev.Ip;
            var secondaryIps = allIps.Where(ip => ip != primaryIp).ToList();
            var mac = dev.MacAddress?.ToUpperInvariant();

            // Find existing: first by MAC (physical device), then by IP (fallback)
            SnmpDevice? existing = null;
            if (mac is not null and not "" && existingByMac.TryGetValue(mac, out var byMac))
                existing = byMac;
            if (existing == null)
                existingByIp.TryGetValue(primaryIp, out existing);

            // Machine correlation
            Guid? machineId = null;
            foreach (var ip in allIps)
                if (machineByIp.TryGetValue(ip, out var mId)) { machineId = mId; break; }

            if (existing != null)
            {
                var lastScannedAt = existing.ScannedAt;

                // IP may have changed (DHCP) — update primary IP
                if (existing.IpAddress != primaryIp && dev.SysName != null)
                    existing.IpAddress = primaryIp;

                existing.MacAddress = dev.MacAddress;
                existing.Vendor = dev.Vendor;
                existing.SysName = dev.SysName ?? existing.SysName;
                existing.SysDescr = dev.SysDescr ?? existing.SysDescr;
                existing.SysUptimeSec = dev.SysUptime;
                existing.SysContact = dev.SysContact;
                existing.SysLocation = dev.SysLocation;
                existing.SysObjectId = dev.SysObjectId ?? existing.SysObjectId;
                existing.EntityModel = dev.EntityInfo?.Model ?? existing.EntityModel;
                existing.EntitySerial = dev.EntityInfo?.Serial ?? existing.EntitySerial;
                existing.EntityMfg = dev.EntityInfo?.Manufacturer ?? existing.EntityMfg;
                existing.EntityFirmware = dev.EntityInfo?.FirmwareVersion ?? existing.EntityFirmware;
                existing.InterfaceCount = dev.Interfaces?.Count ?? existing.InterfaceCount;
                existing.LldpNeighborCount = dev.LldpNeighbors?.Count ?? 0;
                existing.CdpNeighborCount = dev.CdpNeighbors?.Count ?? 0;
                existing.DeviceType = (dev.DeviceType is not null and not "unknown") ? dev.DeviceType : existing.DeviceType;
                existing.PageCount = dev.PageCount ?? existing.PageCount;
                MapHostResources(dev.HostResources, existing);
                existing.VendorData = dev.VendorData != null ? JsonSerializer.Serialize(dev.VendorData) : existing.VendorData;
                existing.ReverseDns = dev.ReverseDns ?? existing.ReverseDns;
                existing.PingLatencyMs = dev.PingLatencyMs ?? existing.PingLatencyMs;
                existing.PingLossPct = dev.PingLossPct ?? existing.PingLossPct;
                existing.PingJitterMs = dev.PingJitterMs ?? existing.PingJitterMs;
                existing.RawData = JsonSerializer.Serialize(dev);
                existing.ScannedAt = now;
                existing.IsStale = false;
                existing.ScanSource = scanSource;
                existing.MachineId = machineId ?? existing.MachineId;
                existing.SecondaryIps = secondaryIps.Count > 0 ? JsonSerializer.Serialize(secondaryIps) : null;

                // Only replace interfaces/supplies if this scan has data
                if (dev.Interfaces is { Count: > 0 })
                {
                    var prevIfaces = await _db.SnmpDeviceInterfaces
                        .Where(i => i.DeviceId == existing.Id)
                        .Select(i => new { i.IfIndex, i.InOctets, i.OutOctets })
                        .ToListAsync();
                    var prevByIdx = prevIfaces.GroupBy(i => i.IfIndex).ToDictionary(g => g.Key, g => g.First());
                    var interval = (int)(now - lastScannedAt).TotalSeconds;

                    await _db.SnmpDeviceInterfaces.Where(i => i.DeviceId == existing.Id).ExecuteDeleteAsync();
                    foreach (var iface in dev.Interfaces)
                    {
                        var mapped = MapInterface(iface);
                        if (interval > 0 && prevByIdx.TryGetValue(iface.Index, out var prev)
                            && prev.InOctets.HasValue && prev.OutOctets.HasValue
                            && iface.InOctets >= prev.InOctets && iface.OutOctets >= prev.OutOctets)
                        {
                            mapped.PrevInOctets = prev.InOctets;
                            mapped.PrevOutOctets = prev.OutOctets;
                            mapped.SampleIntervalSec = interval;
                            mapped.InRateBps = (iface.InOctets - prev.InOctets.Value) * 8 / interval;
                            mapped.OutRateBps = (iface.OutOctets - prev.OutOctets.Value) * 8 / interval;
                        }
                        existing.Interfaces.Add(mapped);
                    }
                }
                if (dev.PrinterSupplies is { Count: > 0 })
                {
                    await _db.SnmpDeviceSupplies.Where(s => s.DeviceId == existing.Id).ExecuteDeleteAsync();
                    foreach (var s in dev.PrinterSupplies)
                        existing.Supplies.Add(MapSupply(s));
                }
                if ((dev.LldpNeighbors is { Count: > 0 }) || (dev.CdpNeighbors is { Count: > 0 }))
                {
                    await _db.SnmpDeviceNeighbors.Where(n => n.DeviceId == existing.Id).ExecuteDeleteAsync();
                    AddNeighbors(existing, dev, now);
                }
                updated++;
            }
            else
            {
                var device = new SnmpDevice
                {
                    OrganizationId = orgId,
                    IpAddress = primaryIp,
                    MacAddress = dev.MacAddress,
                    Vendor = dev.Vendor,
                    SysName = dev.SysName,
                    SysDescr = dev.SysDescr,
                    SysUptimeSec = dev.SysUptime,
                    SysContact = dev.SysContact,
                    SysLocation = dev.SysLocation,
                    SysObjectId = dev.SysObjectId,
                    EntityModel = dev.EntityInfo?.Model,
                    EntitySerial = dev.EntityInfo?.Serial,
                    EntityMfg = dev.EntityInfo?.Manufacturer,
                    EntityFirmware = dev.EntityInfo?.FirmwareVersion,
                    InterfaceCount = dev.Interfaces?.Count ?? 0,
                    LldpNeighborCount = dev.LldpNeighbors?.Count ?? 0,
                    CdpNeighborCount = dev.CdpNeighbors?.Count ?? 0,
                    DeviceType = dev.DeviceType,
                    PageCount = dev.PageCount,
                    VendorData = dev.VendorData != null ? JsonSerializer.Serialize(dev.VendorData) : null,
                    ReverseDns = dev.ReverseDns,
                    PingLatencyMs = dev.PingLatencyMs,
                    PingLossPct = dev.PingLossPct,
                    PingJitterMs = dev.PingJitterMs,
                    RawData = JsonSerializer.Serialize(dev),
                    ScannedAt = now,
                    FirstSeenAt = now,
                    MachineId = machineId,
                    ScanSource = scanSource,
                    SecondaryIps = secondaryIps.Count > 0 ? JsonSerializer.Serialize(secondaryIps) : null,
                };
                MapHostResources(dev.HostResources, device);

                if (dev.Interfaces != null)
                    foreach (var iface in dev.Interfaces)
                        device.Interfaces.Add(MapInterface(iface));
                if (dev.PrinterSupplies != null)
                    foreach (var s in dev.PrinterSupplies)
                        device.Supplies.Add(MapSupply(s));
                AddNeighbors(device, dev, now);

                _db.SnmpDevices.Add(device);
                created++;
            }

            if (++batchCounter % 10 == 0)
                await _db.SaveChangesAsync();
        }

        // Mark stale: devices in this org not seen in this scan (>30 days since last scan)
        var thirtyDaysAgo = now.AddDays(-30);
        await _db.SnmpDevices
            .Where(d => d.OrganizationId == orgId && d.ScannedAt < thirtyDaysAgo && !d.IsStale)
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.IsStale, true));

        await _db.SaveChangesAsync();

        // Resolve neighbor links: match remoteSysName/remoteChassisId/remoteIp to known devices
        await ResolveNeighborLinks(orgId);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { created, updated, filtered, merged = mergedCount });
        return resp;
    }

    private async Task ResolveNeighborLinks(Guid orgId)
    {
        var devices = await _db.SnmpDevices
            .Where(d => d.OrganizationId == orgId && !d.IsStale)
            .Select(d => new { d.Id, d.SysName, d.MacAddress, d.IpAddress })
            .ToListAsync();

        var unresolved = await _db.SnmpDeviceNeighbors
            .Where(n => n.Device.OrganizationId == orgId && n.ResolvedDeviceId == null)
            .ToListAsync();

        if (unresolved.Count == 0) return;

        var bySysName = devices.Where(d => d.SysName != null)
            .GroupBy(d => d.SysName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var byMac = devices.Where(d => d.MacAddress != null)
            .GroupBy(d => d.MacAddress!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var byIp = devices.GroupBy(d => d.IpAddress)
            .ToDictionary(g => g.Key, g => g.First().Id);

        foreach (var n in unresolved)
        {
            int? resolved = null;
            if (n.RemoteSysName != null && bySysName.TryGetValue(n.RemoteSysName, out var bySn))
                resolved = bySn;
            else if (n.RemoteChassisId != null && byMac.TryGetValue(n.RemoteChassisId, out var byM))
                resolved = byM;
            else if (n.RemoteIp != null && byIp.TryGetValue(n.RemoteIp, out var byI))
                resolved = byI;

            if (resolved != null && resolved != n.DeviceId)
                n.ResolvedDeviceId = resolved;
        }
        await _db.SaveChangesAsync();
    }

    [Function("Snmp_GetProfiles")]
    public async Task<HttpResponseData> GetProfiles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/snmp-profiles")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var sysObjectIds = query["sysObjectIds"]?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];

        var profiles = await _db.SnmpDeviceProfiles
            .Where(p => p.Enabled)
            .Include(p => p.Oids)
            .ToListAsync();

        var matched = new List<object>();
        foreach (var sysOid in sysObjectIds.Distinct())
        {
            var profile = profiles.FirstOrDefault(p => sysOid.StartsWith(p.OidPrefix));
            if (profile != null && !matched.Any(m => ((dynamic)m).oidPrefix == profile.OidPrefix))
            {
                matched.Add(new
                {
                    oidPrefix = profile.OidPrefix,
                    vendor = profile.VendorName,
                    oids = profile.Oids.Select(o => new
                    {
                        o.Oid, o.Name, o.Category, o.DataType, o.Unit, o.Walk,
                    }),
                });
            }
        }

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { profiles = matched });
        return resp;
    }

    private static void AddNeighbors(SnmpDevice device, SnmpDeviceDto dto, DateTime now)
    {
        if (dto.LldpNeighbors != null)
            foreach (var n in dto.LldpNeighbors)
                device.Neighbors.Add(new SnmpDeviceNeighbor
                {
                    Protocol = "lldp",
                    LocalPort = n.LocalPort,
                    RemoteChassisId = n.RemoteChassisId,
                    RemotePortId = n.RemotePortId,
                    RemotePortDesc = n.RemotePortDesc,
                    RemoteSysName = n.RemoteSysName,
                    RemoteSysDesc = n.RemoteSysDesc,
                    UpdatedAt = now,
                });
        if (dto.CdpNeighbors != null)
            foreach (var n in dto.CdpNeighbors)
                device.Neighbors.Add(new SnmpDeviceNeighbor
                {
                    Protocol = "cdp",
                    LocalPort = n.LocalPort,
                    RemoteDeviceIdStr = n.RemoteDeviceId,
                    RemotePortId = n.RemotePortId,
                    RemoteIp = n.RemoteIp,
                    RemotePlatform = n.RemotePlatform,
                    UpdatedAt = now,
                });
    }

    private static SnmpDeviceInterface MapInterface(SnmpInterfaceDto iface) => new()
    {
        IfIndex = iface.Index,
        Name = iface.Name,
        Description = iface.Description,
        IfType = iface.Type,
        SpeedMbps = iface.SpeedMbps,
        MacAddress = iface.MacAddress,
        AdminStatus = (short?)iface.AdminStatus,
        OperStatus = (short?)iface.OperStatus,
        InOctets = iface.InOctets,
        OutOctets = iface.OutOctets,
        InErrors = iface.InErrors,
        OutErrors = iface.OutErrors,
        InDiscards = iface.InDiscards,
        OutDiscards = iface.OutDiscards,
    };

    private static SnmpDeviceSupply MapSupply(PrinterSupplyDto s) => new()
    {
        Description = s.Description ?? "unknown",
        SupplyType = s.SupplyType ?? "other",
        Color = s.Color,
        LevelPercent = s.LevelPercent,
        MaxCapacity = s.MaxCapacity,
        CurrentLevel = s.CurrentLevel,
    };

    private static void MapHostResources(HostResourcesDto? hr, SnmpDevice device)
    {
        if (hr == null) return;
        device.CpuLoadPct = hr.CpuLoadPercent;
        device.MemoryTotalMb = hr.MemoryTotalMb;
        device.MemoryUsedMb = hr.MemoryUsedMb;
        device.ProcessCount = hr.ProcessCount;

        // Aggregate disk totals from storage entries (fixedDisk only)
        if (hr.Storage is { Count: > 0 })
        {
            var disks = hr.Storage.Where(s => s.Type is "fixedDisk" or "removableDisk" or "networkDisk").ToList();
            if (disks.Count > 0)
            {
                device.DiskTotalGb = (int)(disks.Sum(d => d.TotalMb) / 1024);
                device.DiskUsedGb = (int)(disks.Sum(d => d.UsedMb) / 1024);
            }
        }
    }
}

// DTOs for deserialization
internal class SnmpPayload
{
    public List<SnmpDeviceDto>? Devices { get; set; }
}

internal class SnmpDeviceDto
{
    public string Ip { get; set; } = null!;
    public string? MacAddress { get; set; }
    public string? Vendor { get; set; }
    public string? SysName { get; set; }
    public string? SysDescr { get; set; }
    public long? SysUptime { get; set; }
    public string? SysContact { get; set; }
    public string? SysLocation { get; set; }
    public string? SysObjectId { get; set; }
    public string? DeviceType { get; set; }
    public long? PageCount { get; set; }
    public Dictionary<string, string>? VendorData { get; set; }
    public List<SnmpInterfaceDto>? Interfaces { get; set; }
    public SnmpEntityDto? EntityInfo { get; set; }
    public List<LldpNeighborDto>? LldpNeighbors { get; set; }
    public List<CdpNeighborDto>? CdpNeighbors { get; set; }
    public List<PrinterSupplyDto>? PrinterSupplies { get; set; }
    public HostResourcesDto? HostResources { get; set; }
    public string? ReverseDns { get; set; }
    public double? PingLatencyMs { get; set; }
    public double? PingLossPct { get; set; }
    public double? PingJitterMs { get; set; }
}

internal class HostResourcesDto
{
    public int? CpuLoadPercent { get; set; }
    public long? MemoryTotalMb { get; set; }
    public long? MemoryUsedMb { get; set; }
    public int? ProcessCount { get; set; }
    public List<StorageEntryDto>? Storage { get; set; }
}

internal class StorageEntryDto
{
    public string Description { get; set; } = null!;
    public long TotalMb { get; set; }
    public long UsedMb { get; set; }
    public string Type { get; set; } = null!;
}

internal class LldpNeighborDto
{
    public string? LocalPort { get; set; }
    public string? RemoteChassisId { get; set; }
    public string? RemotePortId { get; set; }
    public string? RemotePortDesc { get; set; }
    public string? RemoteSysName { get; set; }
    public string? RemoteSysDesc { get; set; }
}

internal class CdpNeighborDto
{
    public string? LocalPort { get; set; }
    public string? RemoteDeviceId { get; set; }
    public string? RemotePortId { get; set; }
    public string? RemoteIp { get; set; }
    public string? RemotePlatform { get; set; }
}

internal class SnmpInterfaceDto
{
    public int Index { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int Type { get; set; }
    public long? SpeedMbps { get; set; }
    public string? MacAddress { get; set; }
    public int AdminStatus { get; set; }
    public int OperStatus { get; set; }
    public long InOctets { get; set; }
    public long OutOctets { get; set; }
    public long InErrors { get; set; }
    public long OutErrors { get; set; }
    public long InDiscards { get; set; }
    public long OutDiscards { get; set; }
}

internal class SnmpEntityDto
{
    public string? Model { get; set; }
    public string? Serial { get; set; }
    public string? Manufacturer { get; set; }
    public string? FirmwareVersion { get; set; }
}

internal class PrinterSupplyDto
{
    public string? Description { get; set; }
    public string? SupplyType { get; set; }
    public string? Color { get; set; }
    public int? LevelPercent { get; set; }
    public int? MaxCapacity { get; set; }
    public int? CurrentLevel { get; set; }
}
