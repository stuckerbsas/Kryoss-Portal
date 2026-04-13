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

    [Function("Reports_Generate")]
    public async Task<HttpResponseData> Generate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/reports/{runId:guid}")] HttpRequestData req,
        Guid runId)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var reportType = query["type"] ?? "technical"; // technical, executive, presales
        var frameworkCode = query["framework"]; // NIST, CIS, HIPAA, ISO27001, PCI-DSS

        // HIGH-01: Verify the run belongs to the authenticated user's org/franchise
        if (!_user.IsAdmin)
        {
            var runOrgId = await _db.AssessmentRuns
                .Where(r => r.Id == runId)
                .Join(_db.Machines, r => r.MachineId, m => m.Id, (r, m) => m.OrganizationId)
                .FirstOrDefaultAsync();
            var hasAccess = (_user.OrganizationId.HasValue && runOrgId == _user.OrganizationId.Value)
                || (_user.FranchiseId.HasValue && await _db.Organizations
                    .AnyAsync(o => o.Id == runOrgId && o.FranchiseId == _user.FranchiseId.Value));
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        try
        {
            var html = await _reports.GenerateHtmlReportAsync(runId, reportType, frameworkCode);

            await _actlog.LogAsync("INFO", "reports", "report.generated",
                $"Generated {reportType} report for run {runId}",
                entityType: "AssessmentRun", entityId: runId.ToString());

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
            var html = await _reports.GenerateOrgReportAsync(orgId, reportType, frameworkCode);

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
