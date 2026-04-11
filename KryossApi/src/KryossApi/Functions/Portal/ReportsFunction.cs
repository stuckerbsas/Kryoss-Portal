using System.Net;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

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

    public ReportsFunction(IReportService reports, IActlogService actlog)
    {
        _reports = reports;
        _actlog = actlog;
    }

    [Function("Reports_Generate")]
    public async Task<HttpResponseData> Generate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/reports/{runId:guid}")] HttpRequestData req,
        Guid runId)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var reportType = query["type"] ?? "technical"; // technical, executive, presales
        var frameworkCode = query["framework"]; // NIST, CIS, HIPAA, ISO27001, PCI-DSS

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
