using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface IPublicIpTracker
{
    Task TrackAsync(Guid machineId, string? publicIp);
}

public class PublicIpTracker : IPublicIpTracker
{
    private readonly KryossDbContext _db;
    private readonly IGeoIpService _geo;

    public PublicIpTracker(KryossDbContext db, IGeoIpService geo)
    {
        _db = db;
        _geo = geo;
    }

    public async Task TrackAsync(Guid machineId, string? publicIp)
    {
        if (string.IsNullOrWhiteSpace(publicIp)) return;

        var ip = publicIp.Split(',')[0].Trim();
        if (string.IsNullOrEmpty(ip)) return;

        var machine = await _db.Machines.FindAsync(machineId);
        if (machine is null) return;

        var now = DateTime.UtcNow;
        var ipChanged = !string.Equals(machine.LastPublicIp, ip, StringComparison.OrdinalIgnoreCase);

        machine.LastPublicIp = ip;
        machine.LastPublicIpAt = now;

        var existing = await _db.MachinePublicIpHistory
            .Where(h => h.MachineId == machineId && h.PublicIp == ip)
            .OrderByDescending(h => h.LastSeen)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            existing.LastSeen = now;
            if (existing.GeoCountry is null)
                await EnrichWithGeoAsync(existing, ip);
        }
        else
        {
            var entry = new MachinePublicIpHistory
            {
                MachineId = machineId,
                PublicIp = ip,
                FirstSeen = now,
                LastSeen = now,
            };
            await EnrichWithGeoAsync(entry, ip);
            _db.MachinePublicIpHistory.Add(entry);
        }

        await _db.SaveChangesAsync();
    }

    private async Task EnrichWithGeoAsync(MachinePublicIpHistory entry, string ip)
    {
        try
        {
            var geo = await _geo.LookupAsync(ip);
            if (geo is null) return;
            entry.GeoCountry = geo.Country;
            entry.GeoRegion = geo.Region;
            entry.GeoCity = geo.City;
            entry.GeoLat = geo.Lat;
            entry.GeoLon = geo.Lon;
            entry.Isp = geo.Isp;
            entry.Asn = geo.Asn;
            entry.AsnOrg = geo.AsnOrg;
            entry.ConnType = geo.ConnType;
        }
        catch { /* non-fatal */ }
    }
}
