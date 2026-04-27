using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using KryossApi.Services.CloudAssessment;
using KryossApi.Services.CloudAssessment.Benchmarks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class BenchmarkFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IBenchmarkService _benchmark;

    public BenchmarkFunction(
        KryossDbContext db,
        ICurrentUserService user,
        IBenchmarkService benchmark)
    {
        _db = db;
        _user = user;
        _benchmark = benchmark;
    }

    /// <summary>
    /// GET /v2/cloud-assessment/benchmarks/industries
    /// Returns the supported industry list for org dropdown.
    /// NOTE: literal route wins over /{scanId} — declared first.
    /// </summary>
    [Function("Benchmark_Industries")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Industries(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/benchmarks/industries")] HttpRequestData req)
    {
        var options = await _benchmark.GetIndustryOptionsAsync();
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            industries = options,
            employeeBands = EmployeeBands.All
        });
        return resp;
    }

    /// <summary>
    /// GET /v2/cloud-assessment/benchmarks/franchise-summary?franchiseId={guid}
    /// Franchise-level leaderboard. MSP view of all their orgs ranked.
    /// </summary>
    [Function("Benchmark_FranchiseSummary")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> FranchiseSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/benchmarks/franchise-summary")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var franchiseIdStr = query["franchiseId"];

        if (!Guid.TryParse(franchiseIdStr, out var franchiseId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "franchiseId is required (guid)" });
            return bad;
        }

        // Access: admin OR member of franchise
        if (!_user.IsAdmin)
        {
            if (!_user.FranchiseId.HasValue || _user.FranchiseId.Value != franchiseId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        var result = await _benchmark.GetFranchiseLeaderboardAsync(franchiseId, CancellationToken.None);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(result);
        return resp;
    }

    /// <summary>
    /// GET /v2/cloud-assessment/benchmarks/{scanId}/report
    /// Returns the Benchmark Analysis section as self-contained HTML.
    /// NOTE: declared before /{scanId} JSON route so the literal /report segment wins.
    /// </summary>
    [Function("Benchmark_HtmlReport")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> HtmlReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/benchmarks/{scanId}/report")] HttpRequestData req,
        string scanId)
    {
        if (!Guid.TryParse(scanId, out var scanGuid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "invalid scanId" });
            return bad;
        }

        var scan = await _db.CloudAssessmentScans
            .AsNoTracking()
            .Where(s => s.Id == scanGuid)
            .Select(s => new { s.Id, s.OrganizationId })
            .FirstOrDefaultAsync();
        if (scan is null)
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            await nf.WriteAsJsonAsync(new { error = "scan not found" });
            return nf;
        }

        var denied = await RequireOrgAccess(req, scan.OrganizationId);
        if (denied != null) return denied;

        var org = await _db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == scan.OrganizationId);

        var report = await _benchmark.GetBenchmarkReportAsync(scanGuid, CancellationToken.None);

        string? industryLabel = null;
        if (!string.IsNullOrWhiteSpace(org?.IndustryCode))
        {
            var match = IndustryCodes.All.FirstOrDefault(i => i.Code == org.IndustryCode);
            industryLabel = match.Label ?? org.IndustryCode;
        }

        var html = BenchmarkReportBuilder.Build(
            report,
            org?.Name ?? "Organization",
            industryLabel,
            DateTime.UtcNow);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "text/html; charset=utf-8");
        resp.Headers.Add("Content-Disposition", "attachment; filename=\"report.html\"");
        await resp.WriteStringAsync(html);
        return resp;
    }

    /// <summary>
    /// GET /v2/cloud-assessment/benchmarks/{scanId}
    /// Returns the benchmark report for a specific scan.
    /// </summary>
    [Function("Benchmark_GetReport")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/benchmarks/{scanId}")] HttpRequestData req,
        string scanId)
    {
        if (!Guid.TryParse(scanId, out var scanGuid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "invalid scanId" });
            return bad;
        }

        var scan = await _db.CloudAssessmentScans
            .AsNoTracking()
            .Where(s => s.Id == scanGuid)
            .Select(s => new { s.Id, s.OrganizationId })
            .FirstOrDefaultAsync();

        if (scan is null)
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            await nf.WriteAsJsonAsync(new { error = "scan not found" });
            return nf;
        }

        var denied = await RequireOrgAccess(req, scan.OrganizationId);
        if (denied != null) return denied;

        var report = await _benchmark.GetBenchmarkReportAsync(scanGuid, CancellationToken.None);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(report);
        return resp;
    }

    /// <summary>
    /// PATCH /v2/organizations/{orgId}/industry
    /// Body: { industryCode, industrySubcode?, employeeBand? }
    /// Updates org industry metadata (used by benchmarks).
    /// </summary>
    [Function("Benchmark_SetOrgIndustry")]
    [RequirePermission("org:update")]
    public async Task<HttpResponseData> SetOrgIndustry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/organizations/{orgId}/industry")] HttpRequestData req,
        string orgId)
    {
        if (!Guid.TryParse(orgId, out var orgGuid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "invalid orgId" });
            return bad;
        }

        var body = await req.ReadFromJsonAsync<SetIndustryRequest>();
        if (body is null || string.IsNullOrWhiteSpace(body.IndustryCode))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "industryCode is required" });
            return bad;
        }

        // Validate industry code
        var validIndustry = IndustryCodes.All.Any(i => i.Code == body.IndustryCode);
        if (!validIndustry)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "unknown industryCode" });
            return bad;
        }

        // Validate band if provided
        if (!string.IsNullOrWhiteSpace(body.EmployeeBand) && !EmployeeBands.All.Contains(body.EmployeeBand))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "unknown employeeBand" });
            return bad;
        }

        var denied = await RequireOrgAccess(req, orgGuid);
        if (denied != null) return denied;

        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == orgGuid);
        if (org is null)
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            await nf.WriteAsJsonAsync(new { error = "organization not found" });
            return nf;
        }

        org.IndustryCode = body.IndustryCode;
        org.IndustrySubcode = body.IndustrySubcode;
        if (!string.IsNullOrWhiteSpace(body.EmployeeBand))
            org.EmployeeCountBand = body.EmployeeBand;

        _db.Actlog.Add(new Data.Entities.Actlog
        {
            Timestamp = DateTime.UtcNow,
            Severity = "info",
            Module = "cloud-assessment",
            Action = "org.industry.set",
            EntityType = "Organization",
            EntityId = orgGuid.ToString(),
            Message = $"Org industry set: {body.IndustryCode}/{body.IndustrySubcode ?? "-"} band={body.EmployeeBand ?? "-"}"
        });

        await _db.SaveChangesAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            orgId = org.Id,
            industryCode = org.IndustryCode,
            industrySubcode = org.IndustrySubcode,
            employeeBand = org.EmployeeCountBand
        });
        return resp;
    }

    // ── Helpers ──

    private async Task<HttpResponseData?> RequireOrgAccess(HttpRequestData req, Guid orgId)
    {
        if (_user.IsAdmin) return null;

        var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
            await _db.Organizations.AnyAsync(o => o.Id == orgId && o.FranchiseId == _user.FranchiseId.Value);
        var orgBelongsToUser = _user.OrganizationId.HasValue && orgId == _user.OrganizationId.Value;

        if (!orgBelongsToFranchise && !orgBelongsToUser)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        return null;
    }
}

public class SetIndustryRequest
{
    public string IndustryCode { get; set; } = null!;
    public string? IndustrySubcode { get; set; }
    public string? EmployeeBand { get; set; }
}
