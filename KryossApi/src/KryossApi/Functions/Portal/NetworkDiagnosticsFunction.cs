using System.Net;
using KryossApi.Data;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class NetworkDiagnosticsFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public NetworkDiagnosticsFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    [Function("NetworkDiagnostics_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/network-diagnostics")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgId = query["organizationId"];
        if (string.IsNullOrEmpty(orgId) || !Guid.TryParse(orgId, out var orgGuid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("{\"error\":\"organizationId required\"}");
            return bad;
        }

        // Latest scan per machine — correlated subquery (GroupBy+First breaks navigation props in EF8)
        var diags = await _db.MachineNetworkDiags
            .Where(d => d.Machine.OrganizationId == orgGuid)
            .Where(d => d.Id == _db.MachineNetworkDiags
                .Where(d2 => d2.MachineId == d.MachineId)
                .OrderByDescending(d2 => d2.ScannedAt)
                .Select(d2 => d2.Id)
                .FirstOrDefault())
            .OrderBy(d => d.Machine.Hostname)
            .Select(d => new
            {
                d.Id,
                machineId = d.MachineId,
                machineName = d.Machine.Hostname,
                d.RunId,
                d.DownloadMbps,
                d.UploadMbps,
                d.InternetLatencyMs,
                d.GatewayLatencyMs,
                d.GatewayIp,
                d.RouteCount,
                d.VpnDetected,
                d.AdapterCount,
                d.WifiCount,
                vpnAdapterCount = d.VpnAdapterCount,
                d.EthCount,
                d.BandwidthSendMbps,
                d.BandwidthRecvMbps,
                d.DnsResolutionMs,
                d.CloudEndpointCount,
                d.CloudEndpointAvgMs,
                d.TriggeredByIpChange,
                d.ScannedAt,
                latencyPeers = Array.Empty<object>(),
                routes = Array.Empty<object>(),
            })
            .ToListAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(diags);
        return resp;
    }

    [Function("NetworkDiagnostics_Detail")]
    public async Task<HttpResponseData> Detail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/network-diagnostics/{machineId}")] HttpRequestData req,
        string machineId)
    {
        if (!Guid.TryParse(machineId, out var mId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            return bad;
        }

        var diag = await _db.MachineNetworkDiags
            .Include(d => d.LatencyPeers)
            .Include(d => d.Routes)
            .Where(d => d.MachineId == mId)
            .OrderByDescending(d => d.ScannedAt)
            .FirstOrDefaultAsync();

        if (diag == null)
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            diag.Id,
            diag.MachineId,
            diag.RunId,
            diag.DownloadMbps,
            diag.UploadMbps,
            diag.InternetLatencyMs,
            diag.RouteCount,
            diag.VpnDetected,
            diag.VpnAdapters,
            diag.AdapterCount,
            diag.BandwidthSendMbps,
            diag.BandwidthRecvMbps,
            diag.DnsResolutionMs,
            diag.CloudEndpointCount,
            diag.CloudEndpointAvgMs,
            diag.TriggeredByIpChange,
            diag.ScannedAt,
            latencyPeers = diag.LatencyPeers.Select(p => new
            {
                p.Host, p.Subnet, p.Reachable,
                p.AvgMs, p.MinMs, p.MaxMs, p.JitterMs,
                p.PacketLoss, p.TotalSent,
            }),
            routes = diag.Routes.Select(r => new
            {
                r.Destination, r.Mask, r.NextHop,
                r.InterfaceIndex, r.Metric, r.RouteType, r.Protocol,
            }),
        });
        return resp;
    }
}
