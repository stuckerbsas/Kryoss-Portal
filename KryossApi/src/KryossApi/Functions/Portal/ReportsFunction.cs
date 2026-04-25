using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using KryossApi.Services.Reports;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

[RequirePermission("reports:read")]
public class ReportsFunction
{
    private readonly IReportService _reports;
    private readonly IReportComposer _composer;
    private readonly IActlogService _actlog;
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    private static readonly HashSet<string> _composerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "c-level", "executive", "technical", "preventa", "preventas", "presales", "presales-opener",
        "preventa-opener", "preventa-detailed",
        "monthly", "monthly-briefing", "framework", "compliance", "proposal", "network",
        "cloud-executive", "exec-onepager", "m365", "hygiene", "risk-assessment", "inventory",
        "test-fixture"
    };

    public ReportsFunction(IReportService reports, IReportComposer composer, IActlogService actlog, KryossDbContext db, ICurrentUserService user)
    {
        _reports = reports;
        _composer = composer;
        _actlog = actlog;
        _db = db;
        _user = user;
    }

    /// <summary>
    /// Diagnostic endpoint: runs each block individually with timing + error capture.
    /// GET /v2/reports/diagnose/{orgId}?type=technical
    /// </summary>
    [Function("Reports_Diagnose")]
    public async Task<HttpResponseData> Diagnose(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/reports/diagnose/{orgId:guid}")] HttpRequestData req,
        Guid orgId)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var reportType = query["type"] ?? "technical";
        var lang = (query["lang"] ?? "en").ToLowerInvariant();
        if (lang != "es") lang = "en";
        var tone = query["tone"]?.ToLowerInvariant();
        if (tone != "opener" && tone != "detailed") tone = "opener";
        var frameworkCode = query["framework"];

        try
        {
            string? frameworkName = null;
            if (!string.IsNullOrEmpty(frameworkCode))
            {
                var fw = await _db.Frameworks.FirstOrDefaultAsync(f => f.Code == frameworkCode && f.IsActive);
                frameworkName = fw?.Name;
            }

            var reportOptions = new ReportOptions(Lang: lang, FrameworkCode: frameworkCode, FrameworkName: frameworkName, Tone: tone);
            string? singleType = reportType != "all" ? reportType : null;
            var diag = await _composer.DiagnoseAsync(orgId, singleType, reportOptions);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(diag);
            return response;
        }
        catch (Exception ex)
        {
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = ex.Message, type = ex.GetType().Name, stack = ex.StackTrace, inner = ex.InnerException?.Message });
            return error;
        }
    }

    /// <summary>
    /// DEPRECATED since 2026-04-15. Per-run reports are replaced by org-level reports.
    /// Returns HTTP 410 Gone with a migration message.
    /// </summary>
    [Function("Reports_Generate")]
    public async Task<HttpResponseData> Generate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/reports/{runId:guid}")] HttpRequestData req,
        Guid runId)
    {
        _ = runId;
        var response = req.CreateResponse(HttpStatusCode.Gone);
        await response.WriteAsJsonAsync(new
        {
            error = "Per-run reports have been deprecated",
            message = "Report scope is now always organization-level. Use GET /v2/reports/org/{orgId}?type=technical to generate org-wide Technical reports. For single-machine troubleshooting, use the portal's machine detail view.",
            status = 410,
            deprecatedSince = "2026-04-15"
        });
        return response;
    }

    /// <summary>
    /// Consolidated organization report: all machines, latest assessment each.
    /// GET /v2/reports/org/{orgId}?type=executive|technical|presales
    /// </summary>
    [Function("Reports_GenerateOrg")]
    public async Task<HttpResponseData> GenerateOrg(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/reports/org/{orgId:guid}")] HttpRequestData req,
        Guid orgId)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var reportType = query["type"] ?? "executive";
        var frameworkCode = query["framework"]; // NIST, CIS, HIPAA, ISO27001, PCI-DSS
        var lang = (query["lang"] ?? "en").ToLowerInvariant();
        if (lang != "es") lang = "en";
        var tone = query["tone"]?.ToLowerInvariant();
        if (tone != "opener" && tone != "detailed") tone = "opener";

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

        // Diagnostic mode: ?diag=1 runs block-by-block timing instead of generating HTML
        var isDiag = query["diag"] == "1";
        if (isDiag && _composerTypes.Contains(reportType))
        {
            try
            {
                var diagOpts = new ReportOptions(Lang: lang, FrameworkCode: frameworkCode, FrameworkName: null, Tone: tone);
                var diag = await _composer.DiagnoseAsync(orgId, reportType, diagOpts);
                var r = req.CreateResponse(HttpStatusCode.OK);
                await r.WriteAsJsonAsync(diag);
                return r;
            }
            catch (Exception ex)
            {
                var r = req.CreateResponse(HttpStatusCode.InternalServerError);
                await r.WriteAsJsonAsync(new { error = ex.Message, type = ex.GetType().Name, stack = ex.StackTrace, inner = ex.InnerException?.Message });
                return r;
            }
        }

        try
        {
            string html;

            if (_composerTypes.Contains(reportType))
            {
                if (reportType == "framework" && string.IsNullOrEmpty(frameworkCode))
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = "Framework report requires ?framework= parameter" });
                    return badReq;
                }

                string? frameworkName = null;
                if (!string.IsNullOrEmpty(frameworkCode))
                {
                    var fw = await _db.Frameworks.FirstOrDefaultAsync(f => f.Code == frameworkCode && f.IsActive);
                    frameworkName = fw?.Name;
                }

                var reportOptions = new ReportOptions(
                    Lang: lang,
                    FrameworkCode: frameworkCode,
                    FrameworkName: frameworkName,
                    Tone: tone
                );
                html = await _composer.GenerateAsync(orgId, reportType, reportOptions);
            }
            else
            {
                html = await _reports.GenerateOrgReportAsync(orgId, reportType, frameworkCode, lang, tone);
            }

            await _actlog.LogAsync("INFO", "reports", "org_report.generated",
                $"Generated {reportType} org report for {orgId}",
                entityType: "Organization", entityId: orgId.ToString());

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/html; charset=utf-8");
            if (req.Headers.TryGetValues("Origin", out var successOrigins))
            {
                response.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", successOrigins.First());
                response.Headers.TryAddWithoutValidation("Access-Control-Allow-Credentials", "true");
            }
            await response.WriteStringAsync(html);
            return response;
        }
        catch (Exception ex)
        {
            try
            {
                await _actlog.LogAsync("ERR", "reports", $"org_report.crash.{reportType}",
                    $"{ex.GetType().Name}: {ex.Message} | inner: {ex.InnerException?.Message} | stack: {ex.StackTrace?[..Math.Min(500, ex.StackTrace?.Length ?? 0)]}",
                    entityType: "Organization", entityId: orgId.ToString());
            }
            catch { }

            var statusCode = ex is InvalidOperationException ? HttpStatusCode.NotFound : HttpStatusCode.InternalServerError;
            var error = req.CreateResponse(statusCode);
            if (req.Headers.TryGetValues("Origin", out var origins))
            {
                error.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", origins.First());
                error.Headers.TryAddWithoutValidation("Access-Control-Allow-Credentials", "true");
            }
            await error.WriteAsJsonAsync(new
            {
                error = ex.Message,
                type = ex.GetType().Name,
                stack = ex.StackTrace,
                inner = ex.InnerException?.Message
            });
            return error;
        }
    }
}
