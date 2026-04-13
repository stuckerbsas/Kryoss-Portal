using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Portal;

/// <summary>
/// External port scan / pentest: cloud-side scan of public IPs.
/// All endpoints require Bearer auth + RBAC.
/// </summary>
public class ExternalScanFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly ExternalScanService _scanService;
    private readonly ILogger<ExternalScanFunction> _log;

    public ExternalScanFunction(
        KryossDbContext db,
        ICurrentUserService user,
        ExternalScanService scanService,
        ILogger<ExternalScanFunction> log)
    {
        _db = db;
        _user = user;
        _scanService = scanService;
        _log = log;
    }

    // ── POST /v2/external-scan — Start a new external scan ──

    /// <summary>
    /// Creates a scan record, runs the scan inline, returns results.
    /// Body: { organizationId, target }
    /// </summary>
    [Function("ExternalScan_Start")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> StartScan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/external-scan")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<StartScanRequest>();
        if (body is null || string.IsNullOrWhiteSpace(body.Target))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "target is required" });
            return bad;
        }

        if (body.OrganizationId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        // Verify org exists
        var orgExists = await _db.Organizations.AnyAsync(o => o.Id == body.OrganizationId);
        if (!orgExists)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Organization not found" });
            return notFound;
        }

        // HIGH-01 + MED-06: Verify the user has access to this organization
        if (!_user.IsAdmin)
        {
            var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
                await _db.Organizations.AnyAsync(o => o.Id == body.OrganizationId && o.FranchiseId == _user.FranchiseId.Value);
            var orgBelongsToUser = _user.OrganizationId.HasValue && body.OrganizationId == _user.OrganizationId.Value;
            if (!orgBelongsToFranchise && !orgBelongsToUser)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        // Create scan record
        var scan = new ExternalScan
        {
            Id = Guid.NewGuid(),
            OrganizationId = body.OrganizationId,
            Target = body.Target.Trim(),
            Status = "pending",
            CreatedBy = _user.UserId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.ExternalScans.Add(scan);
        await _db.SaveChangesAsync();

        // Run inline
        try
        {
            scan = await _scanService.RunScanAsync(scan.Id);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "External scan {ScanId} failed", scan.Id);
            // scan.Status is already "failed" inside RunScanAsync
        }

        // Build response
        var openResults = scan.Results.Where(r => r.Status == "open").ToList();
        var ips = openResults.Select(r => r.IpAddress).Distinct().ToList();

        var result = new
        {
            scanId = scan.Id,
            status = scan.Status,
            target = scan.Target,
            ipsFound = scan.Results.Select(r => r.IpAddress).Distinct().Count(),
            openPorts = openResults.Count,
            criticalPorts = openResults.Count(r => r.Risk == "critical"),
            highPorts = openResults.Count(r => r.Risk == "high"),
            startedAt = scan.StartedAt,
            completedAt = scan.CompletedAt,
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    // ── GET /v2/external-scan?organizationId={guid} — Latest scan for org ──

    [Function("ExternalScan_Latest")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetLatest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/external-scan")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        if (!Guid.TryParse(orgIdStr, out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        // HIGH-01: Verify the user has access to this organization
        if (!_user.IsAdmin)
        {
            var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
                await _db.Organizations.AnyAsync(o => o.Id == orgId && o.FranchiseId == _user.FranchiseId.Value);
            var orgBelongsToUser = _user.OrganizationId.HasValue && orgId == _user.OrganizationId.Value;
            if (!orgBelongsToFranchise && !orgBelongsToUser)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        var scan = await _db.ExternalScans
            .Where(s => s.OrganizationId == orgId)
            .OrderByDescending(s => s.CreatedAt)
            .Include(s => s.Results)
            .FirstOrDefaultAsync();

        if (scan is null)
        {
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }

        var responseObj = BuildScanResponse(scan);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(responseObj);
        return resp;
    }

    // ── GET /v2/external-scan/{scanId} — Specific scan ──

    [Function("ExternalScan_Detail")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetDetail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/external-scan/{scanId}")] HttpRequestData req,
        string scanId)
    {
        if (!Guid.TryParse(scanId, out var id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid scanId" });
            return bad;
        }

        var scan = await _db.ExternalScans
            .Include(s => s.Results)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (scan is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Scan not found" });
            return notFound;
        }

        // HIGH-01: Verify the scan belongs to the authenticated user's org/franchise
        if (!_user.IsAdmin)
        {
            var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
                await _db.Organizations.AnyAsync(o => o.Id == scan.OrganizationId && o.FranchiseId == _user.FranchiseId.Value);
            var orgBelongsToUser = _user.OrganizationId.HasValue && scan.OrganizationId == _user.OrganizationId.Value;
            if (!orgBelongsToFranchise && !orgBelongsToUser)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        var responseObj = BuildScanResponse(scan);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(responseObj);
        return resp;
    }

    // ── GET /v2/external-scan/history?organizationId={guid} — All scans for org ──

    [Function("ExternalScan_History")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/external-scan/history")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        if (!Guid.TryParse(orgIdStr, out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        // HIGH-01: Verify the user has access to this organization
        if (!_user.IsAdmin)
        {
            var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
                await _db.Organizations.AnyAsync(o => o.Id == orgId && o.FranchiseId == _user.FranchiseId.Value);
            var orgBelongsToUser = _user.OrganizationId.HasValue && orgId == _user.OrganizationId.Value;
            if (!orgBelongsToFranchise && !orgBelongsToUser)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        var scans = await _db.ExternalScans
            .Where(s => s.OrganizationId == orgId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(20)
            .Select(s => new
            {
                s.Id,
                s.Target,
                s.Status,
                s.StartedAt,
                s.CompletedAt,
                s.CreatedAt,
                openPorts = s.Results.Count(r => r.Status == "open"),
                criticalPorts = s.Results.Count(r => r.Risk == "critical"),
            })
            .ToListAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(scans);
        return resp;
    }

    private static object BuildScanResponse(ExternalScan scan)
    {
        var openResults = scan.Results.Where(r => r.Status == "open").ToList();
        var allIps = scan.Results.Select(r => r.IpAddress).Distinct().ToList();

        return new
        {
            scan.Id,
            scan.Target,
            scan.Status,
            scan.StartedAt,
            scan.CompletedAt,
            scan.CreatedAt,
            summary = new
            {
                totalIps = allIps.Count,
                totalOpen = openResults.Count,
                criticalPorts = openResults.Count(r => r.Risk == "critical"),
                highPorts = openResults.Count(r => r.Risk == "high"),
                mediumPorts = openResults.Count(r => r.Risk == "medium"),
                infoPorts = openResults.Count(r => r.Risk == "info"),
            },
            results = scan.Results
                .Where(r => r.Status == "open")
                .OrderByDescending(r =>
                    r.Risk == "critical" ? 4 :
                    r.Risk == "high" ? 3 :
                    r.Risk == "medium" ? 2 : 1)
                .ThenBy(r => r.IpAddress)
                .ThenBy(r => r.Port)
                .Select(r => new
                {
                    r.IpAddress,
                    r.Port,
                    r.Protocol,
                    r.Status,
                    r.Service,
                    r.Risk,
                    r.Banner,
                    r.Detail,
                }),
        };
    }
}

// ── DTOs ──

public class StartScanRequest
{
    public Guid OrganizationId { get; set; }
    public string Target { get; set; } = null!;
}
