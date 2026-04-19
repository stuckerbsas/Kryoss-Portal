using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace KryossApi.Functions.Portal;

/// <summary>
/// DEPRECATED (CA-12): All Copilot Readiness endpoints now return 410 Gone.
/// Use Cloud Assessment equivalents:
///   POST /v2/cloud-assessment/scan
///   GET  /v2/cloud-assessment?organizationId={id}
///   GET  /v2/cloud-assessment/{scanId}
///   GET  /v2/cloud-assessment/copilot-lens/{scanId}
///   GET  /v2/cloud-assessment/history?organizationId={id}
/// </summary>
public class CopilotReadinessFunction
{
    private static readonly object DeprecationBody = new
    {
        error = "Gone",
        message = "Copilot Readiness endpoints are deprecated. Use Cloud Assessment instead.",
        migration = new
        {
            scan = "POST /v2/cloud-assessment/scan",
            latest = "GET /v2/cloud-assessment?organizationId={id}",
            detail = "GET /v2/cloud-assessment/{scanId}",
            copilotLens = "GET /v2/cloud-assessment/copilot-lens/{scanId}",
            history = "GET /v2/cloud-assessment/history?organizationId={id}"
        },
        deprecatedSince = "2026-04-18"
    };

    private static async Task<HttpResponseData> Gone(HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.Gone);
        response.Headers.Add("Deprecation", "true");
        response.Headers.Add("Sunset", "2026-05-18");
        await response.WriteAsJsonAsync(DeprecationBody);
        return response;
    }

    [Function("CopilotReadiness_Scan")]
    public async Task<HttpResponseData> Scan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/copilot-readiness/scan")] HttpRequestData req)
        => await Gone(req);

    [Function("CopilotReadiness_Get")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/copilot-readiness")] HttpRequestData req)
        => await Gone(req);

    [Function("CopilotReadiness_Detail")]
    public async Task<HttpResponseData> Detail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/copilot-readiness/{scanId}")] HttpRequestData req,
        string scanId)
        => await Gone(req);

    [Function("CopilotReadiness_History")]
    public async Task<HttpResponseData> History(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/copilot-readiness/history")] HttpRequestData req)
        => await Gone(req);
}

// ── Request DTOs (kept for backward compatibility during deprecation period) ──

public class CopilotReadinessScanRequest
{
    public Guid OrganizationId { get; set; }
}
