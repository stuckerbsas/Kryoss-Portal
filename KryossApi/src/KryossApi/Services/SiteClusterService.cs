using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface ISiteClusterService
{
    Task RebuildSitesAsync(Guid organizationId);
}

public class SiteClusterService : ISiteClusterService
{
    private readonly KryossDbContext _db;
    private readonly IWanHealthService _wan;

    public SiteClusterService(KryossDbContext db, IWanHealthService wan)
    {
        _db = db;
        _wan = wan;
    }

    public async Task RebuildSitesAsync(Guid organizationId)
    {
        var machines = await _db.Machines
            .Where(m => m.OrganizationId == organizationId && m.LastPublicIp != null)
            .Select(m => new { m.Id, m.LastPublicIp })
            .ToListAsync();

        var grouped = machines
            .GroupBy(m => m.LastPublicIp!)
            .ToList();

        var existingSites = await _db.NetworkSites
            .Where(s => s.OrganizationId == organizationId)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var cutoff90d = now.AddDays(-90);

        foreach (var group in grouped)
        {
            var ip = group.Key;
            var agentCount = group.Count();

            var site = existingSites.FirstOrDefault(s =>
                string.Equals(s.PublicIp, ip, StringComparison.OrdinalIgnoreCase));

            var ipChanges = await _db.MachinePublicIpHistory
                .Where(h => h.PublicIp == ip && h.FirstSeen >= cutoff90d
                    && group.Select(g => g.Id).Contains(h.MachineId))
                .Select(h => h.MachineId)
                .Distinct()
                .CountAsync();

            var latestDiag = await _db.MachineNetworkDiags
                .Where(d => group.Select(g => g.Id).Contains(d.MachineId))
                .OrderByDescending(d => d.ScannedAt)
                .FirstOrDefaultAsync();

            var geoSource = await _db.MachinePublicIpHistory
                .Where(h => h.PublicIp == ip && h.GeoCountry != null)
                .OrderByDescending(h => h.LastSeen)
                .FirstOrDefaultAsync();

            if (site is not null)
            {
                site.AgentCount = agentCount;
                site.IpChanges90d = ipChanges;
                site.AvgDownMbps = latestDiag?.DownloadMbps;
                site.AvgUpMbps = latestDiag?.UploadMbps;
                site.AvgLatencyMs = latestDiag?.InternetLatencyMs;
                if (geoSource is not null && site.GeoCountry is null)
                    ApplyGeo(site, geoSource);
                site.UpdatedAt = now;
            }
            else
            {
                var newSite = new NetworkSite
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    SiteName = geoSource?.GeoCity is not null ? $"{geoSource.GeoCity} Office" : $"Site ({ip})",
                    PublicIp = ip,
                    AgentCount = agentCount,
                    IpChanges90d = ipChanges,
                    AvgDownMbps = latestDiag?.DownloadMbps,
                    AvgUpMbps = latestDiag?.UploadMbps,
                    AvgLatencyMs = latestDiag?.InternetLatencyMs,
                    IsAutoDerived = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                if (geoSource is not null) ApplyGeo(newSite, geoSource);
                _db.NetworkSites.Add(newSite);
            }
        }

        // Mark stale auto-derived sites with no matching machines
        var activeIps = grouped.Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in existingSites.Where(s => s.IsAutoDerived && s.PublicIp != null && !activeIps.Contains(s.PublicIp)))
        {
            stale.AgentCount = 0;
            stale.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();

        await _wan.EvaluateAsync(organizationId);
    }

    private static void ApplyGeo(NetworkSite site, MachinePublicIpHistory src)
    {
        site.GeoCountry = src.GeoCountry;
        site.GeoRegion = src.GeoRegion;
        site.GeoCity = src.GeoCity;
        site.GeoLat = src.GeoLat;
        site.GeoLon = src.GeoLon;
        site.Isp = src.Isp;
        site.Asn = src.Asn;
        site.AsnOrg = src.AsnOrg;
        site.ConnType = src.ConnType;
    }
}
