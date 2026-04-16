using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// Generate and serve assessment reports (HTML).
/// Supports: technical, executive, presales report types.
/// </summary>
[RequirePermission("reports:read")]
public class ReportsFunction
{
    private readonly IReportService _reports;
    private readonly IActlogService _actlog;
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public ReportsFunction(IReportService reports, IActlogService actlog, KryossDbContext db, ICurrentUserService user)
    {
        _reports = reports;
        _actlog = actlog;
        _db = db;
        _user = user;
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

        try
        {
            var html = await _reports.GenerateOrgReportAsync(orgId, reportType, frameworkCode, lang, tone);

            await _actlog.LogAsync("INFO", "reports", "org_report.generated",
                $"Generated {reportType} org report for {orgId}",
                entityType: "Organization", entityId: orgId.ToString());

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/html; charset=utf-8");
            await response.WriteStringAsync(html);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = ex.Message });
            return notFound;
        }
    }
}
