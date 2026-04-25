using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class NetworkSitesFunction
{
    private readonly KryossDbContext _db;
    private readonly ISiteClusterService _cluster;

    public NetworkSitesFunction(KryossDbContext db, ISiteClusterService cluster)
    {
        _db = db;
        _cluster = cluster;
    }

    [Function("NetworkSites_List")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/network-sites")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var sites = await _db.NetworkSites
            .Where(s => s.OrganizationId == orgId)
            .OrderByDescending(s => s.AgentCount)
            .Select(s => new
            {
                s.Id, s.SiteName, s.PublicIp,
                s.GeoCountry, s.GeoRegion, s.GeoCity,
                s.GeoLat, s.GeoLon,
                s.Isp, s.Asn, s.AsnOrg, s.ConnType,
                s.ContractedDownMbps, s.ContractedUpMbps,
                s.AgentCount, s.DeviceCount, s.IpChanges90d,
                s.AvgDownMbps, s.AvgUpMbps, s.AvgLatencyMs,
                s.IsAutoDerived, s.UpdatedAt
            })
            .ToListAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(sites);
        return res;
    }

    [Function("NetworkSites_Rebuild")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Rebuild(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/network-sites/rebuild")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<RebuildRequest>();
        if (body is null || body.OrganizationId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        await _cluster.RebuildSitesAsync(body.OrganizationId);

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { status = "rebuilt" });
        return res;
    }

    [Function("NetworkSites_Update")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/network-sites/{siteId}")] HttpRequestData req,
        string siteId)
    {
        if (!Guid.TryParse(siteId, out var id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "invalid siteId" });
            return bad;
        }

        var site = await _db.NetworkSites.FindAsync(id);
        if (site is null)
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            await nf.WriteAsJsonAsync(new { error = "site_not_found" });
            return nf;
        }

        var body = await req.ReadFromJsonAsync<SiteUpdateRequest>();
        if (body is not null)
        {
            if (body.SiteName is not null) site.SiteName = body.SiteName;
            if (body.ContractedDownMbps.HasValue) site.ContractedDownMbps = body.ContractedDownMbps;
            if (body.ContractedUpMbps.HasValue) site.ContractedUpMbps = body.ContractedUpMbps;
            site.IsAutoDerived = false;
            site.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { site.Id, site.SiteName, site.ContractedDownMbps, site.ContractedUpMbps });
        return res;
    }

    [Function("NetworkSites_IpHistory")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> IpHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/network-sites/ip-history")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var machineIds = await _db.Machines
            .Where(m => m.OrganizationId == orgId)
            .Select(m => m.Id)
            .ToListAsync();

        var history = await _db.MachinePublicIpHistory
            .Where(h => machineIds.Contains(h.MachineId))
            .OrderByDescending(h => h.LastSeen)
            .Take(200)
            .Select(h => new
            {
                h.Id, h.MachineId, h.PublicIp,
                h.FirstSeen, h.LastSeen,
                h.GeoCountry, h.GeoCity, h.Isp, h.Asn, h.ConnType,
                MachineName = h.Machine.Hostname
            })
            .ToListAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(history);
        return res;
    }

    [Function("NetworkSites_SpeedHistory")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> SpeedHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/network-sites/{siteId}/speed-history")] HttpRequestData req,
        string siteId)
    {
        if (!Guid.TryParse(siteId, out var id))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var site = await _db.NetworkSites.FindAsync(id);
        if (site is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        var machineIds = await _db.Machines
            .Where(m => m.OrganizationId == site.OrganizationId && m.LastPublicIp == site.PublicIp)
            .Select(m => m.Id)
            .ToListAsync();

        var history = await _db.MachineNetworkDiags
            .Where(d => machineIds.Contains(d.MachineId) && d.ScannedAt >= DateTime.UtcNow.AddDays(-90))
            .OrderBy(d => d.ScannedAt)
            .Select(d => new
            {
                d.ScannedAt,
                d.DownloadMbps,
                d.UploadMbps,
                d.InternetLatencyMs,
                d.DnsResolutionMs,
                d.CloudEndpointAvgMs,
                MachineName = d.Machine.Hostname,
            })
            .Take(500)
            .ToListAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new
        {
            siteId = site.Id,
            siteName = site.SiteName,
            contractedDownMbps = site.ContractedDownMbps,
            contractedUpMbps = site.ContractedUpMbps,
            history,
        });
        return res;
    }

    [Function("NetworkSites_Machines")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> SiteMachines(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/network-sites/{siteId}/machines")] HttpRequestData req,
        string siteId)
    {
        if (!Guid.TryParse(siteId, out var id))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var site = await _db.NetworkSites.FindAsync(id);
        if (site is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        var machines = await _db.Machines
            .Where(m => m.OrganizationId == site.OrganizationId && m.LastPublicIp == site.PublicIp)
            .Select(m => new
            {
                m.Id,
                m.Hostname,
                m.OsName,
                m.LastPublicIp,
                m.LastPublicIpAt,
                m.LastSeenAt,
                LatestDiag = _db.MachineNetworkDiags
                    .Where(d => d.MachineId == m.Id)
                    .OrderByDescending(d => d.ScannedAt)
                    .Select(d => new { d.DownloadMbps, d.UploadMbps, d.InternetLatencyMs, d.DnsResolutionMs, d.CloudEndpointAvgMs })
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(machines);
        return res;
    }

    private class RebuildRequest { public Guid OrganizationId { get; set; } }
    private class SiteUpdateRequest
    {
        public string? SiteName { get; set; }
        public decimal? ContractedDownMbps { get; set; }
        public decimal? ContractedUpMbps { get; set; }
    }
}
