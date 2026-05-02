using System.Net;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Portal;

[RequirePermission("machines:read")]
public class ExternalExposureFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IExternalScanner _scanner;
    private readonly ILogger<ExternalExposureFunction> _log;

    public ExternalExposureFunction(KryossDbContext db, ICurrentUserService user, IExternalScanner scanner, ILogger<ExternalExposureFunction> log)
    {
        _db = db;
        _user = user;
        _scanner = scanner;
        _log = log;
    }

    [Function("ExternalExposure_Scan")]
    [RequirePermission("assessment:export")]
    public async Task<HttpResponseData> StartScan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/external-scan")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<ExternalScanRequest>();
        var target = (body?.Target ?? body?.PublicIp)?.Trim();
        if (body is null || !Guid.TryParse(body.OrganizationId, out var orgId) || string.IsNullOrWhiteSpace(target))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId and publicIp/domain required" });
            return bad;
        }

        // Validate: must be valid IP or domain
        var isIp = System.Net.IPAddress.TryParse(target, out _);
        var isDomain = !isIp && target.Contains('.') && System.Text.RegularExpressions.Regex.IsMatch(target, @"^[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?)*$");
        if (!isIp && !isDomain)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid target. Provide a valid IP address or domain name." });
            return bad;
        }

        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId);
        if (org is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        if (!org.ExternalScanConsent)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "External scan consent not granted. Enable it in organization settings." });
            return forbidden;
        }

        var scanId = await _scanner.RunScanAsync(orgId, target, _user.UserId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { scanId });
        return response;
    }

    [Function("ExternalExposure_Latest")]
    public async Task<HttpResponseData> GetLatest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/external-scan")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];
        Guid? orgId = Guid.TryParse(orgIdStr, out var parsed) ? parsed : _user.OrganizationId;
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var scan = await _db.ExternalScans
            .Where(s => s.OrganizationId == orgId.Value && s.Status == "completed")
            .OrderByDescending(s => s.CompletedAt)
            .Select(s => new
            {
                s.Id,
                s.Target,
                s.Status,
                s.StartedAt,
                s.CompletedAt,
                results = s.Results.OrderBy(r => r.Port).Select(r => new
                {
                    r.IpAddress,
                    r.Port,
                    r.Protocol,
                    r.Status,
                    r.Service,
                    r.Risk,
                    r.Banner,
                    r.ServiceName,
                    r.ServiceVersion,
                }).ToList(),
                findings = s.Findings.OrderByDescending(f =>
                    f.Severity == "critical" ? 4 :
                    f.Severity == "high" ? 3 :
                    f.Severity == "medium" ? 2 : 1)
                .Select(f => new
                {
                    f.Severity,
                    f.Title,
                    f.Description,
                    f.Remediation,
                    f.Port,
                    f.Category,
                }).ToList(),
            })
            .FirstOrDefaultAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(scan ?? (object)new { message = "No external scan found" });
        return response;
    }

    [Function("ExternalExposure_History")]
    public async Task<HttpResponseData> History(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/external-scan/history")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];
        Guid? orgId = Guid.TryParse(orgIdStr, out var parsed) ? parsed : _user.OrganizationId;
        if (orgId is null)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var scans = await _db.ExternalScans
            .Where(s => s.OrganizationId == orgId.Value)
            .OrderByDescending(s => s.CreatedAt)
            .Take(20)
            .Select(s => new
            {
                s.Id,
                s.Target,
                s.Status,
                s.StartedAt,
                s.CompletedAt,
                openPorts = s.Results.Count,
                criticalFindings = s.Findings.Count(f => f.Severity == "critical"),
                highFindings = s.Findings.Count(f => f.Severity == "high"),
            })
            .ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { scans });
        return response;
    }
    [Function("ExternalExposure_AutoScan")]
    [RequirePermission("assessment:export")]
    public async Task<HttpResponseData> AutoScan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/external-scan/auto")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<AutoScanRequest>();
        if (body is null || !Guid.TryParse(body.OrganizationId, out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId);
        if (org is null)
            return req.CreateResponse(HttpStatusCode.NotFound);
        if (!org.ExternalScanConsent)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "External scan consent not granted." });
            return forbidden;
        }

        var targets = await DiscoverTargetsAsync(orgId);
        if (targets.Count == 0)
        {
            var empty = req.CreateResponse(HttpStatusCode.OK);
            await empty.WriteAsJsonAsync(new { message = "No targets discovered", scanIds = Array.Empty<Guid>() });
            return empty;
        }

        var scanIds = new List<object>();
        foreach (var t in targets)
        {
            try
            {
                var scanId = await _scanner.RunScanAsync(orgId, t.Value, _user.UserId);
                scanIds.Add(new { scanId, target = t.Value, source = t.Source });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Auto-scan failed for {Target}", t.Value);
                scanIds.Add(new { scanId = (Guid?)null, target = t.Value, source = t.Source, error = ex.Message });
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { scanned = scanIds.Count, scanIds });
        return response;
    }

    [Function("ExternalExposure_Targets")]
    public async Task<HttpResponseData> Targets(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/external-scan/targets")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        Guid? orgId = Guid.TryParse(query["organizationId"], out var parsed) ? parsed : _user.OrganizationId;
        if (orgId is null)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var targets = await DiscoverTargetsAsync(orgId.Value);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { targets });
        return response;
    }

    private async Task<List<DiscoveredTarget>> DiscoverTargetsAsync(Guid orgId)
    {
        var targets = new List<DiscoveredTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var siteIps = await _db.NetworkSites
            .Where(s => s.OrganizationId == orgId && s.PublicIp != null)
            .Select(s => new { s.PublicIp, s.SiteName })
            .ToListAsync();

        foreach (var s in siteIps)
        {
            if (s.PublicIp != null && seen.Add(s.PublicIp))
                targets.Add(new DiscoveredTarget(s.PublicIp, "network_site", s.SiteName));
        }

        var latestCaScanId = await _db.Set<CloudAssessmentScan>()
            .Where(s => s.OrganizationId == orgId && s.Status == "completed")
            .OrderByDescending(s => s.CompletedAt)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync();

        if (latestCaScanId != null)
        {
            var domains = await _db.CloudAssessmentMailDomains
                .Where(d => d.ScanId == latestCaScanId.Value && d.IsVerified)
                .Select(d => d.Domain)
                .ToListAsync();

            foreach (var d in domains)
            {
                if (seen.Add(d))
                    targets.Add(new DiscoveredTarget(d, "cloud_domain", null));
            }
        }

        return targets;
    }
}

internal record DiscoveredTarget(string Value, string Source, string? Label);

internal class AutoScanRequest
{
    public string? OrganizationId { get; set; }
}

internal class ExternalScanRequest
{
    public string? OrganizationId { get; set; }
    public string? PublicIp { get; set; }
    public string? Target { get; set; }
}
