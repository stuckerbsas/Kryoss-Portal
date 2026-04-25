using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

[RequirePermission("machines:read")]
public class ExternalExposureFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IExternalScanner _scanner;

    public ExternalExposureFunction(KryossDbContext db, ICurrentUserService user, IExternalScanner scanner)
    {
        _db = db;
        _user = user;
        _scanner = scanner;
    }

    [Function("ExternalExposure_Scan")]
    [RequirePermission("assessment:export")]
    public async Task<HttpResponseData> StartScan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/external-scan")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<ExternalScanRequest>();
        if (body is null || !Guid.TryParse(body.OrganizationId, out var orgId) || string.IsNullOrWhiteSpace(body.PublicIp))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId and publicIp required" });
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

        var scanId = await _scanner.RunScanAsync(orgId, body.PublicIp, _user.UserId);

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
}

internal class ExternalScanRequest
{
    public string? OrganizationId { get; set; }
    public string? PublicIp { get; set; }
}
