using System.Net;
using KryossApi.Data;
using KryossApi.Services;
using KryossApi.Services.Reports;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Agent;

public class ReportDownloadFunction
{
    private readonly KryossDbContext _db;
    private readonly IReportComposer _composer;

    public ReportDownloadFunction(KryossDbContext db, IReportComposer composer)
    {
        _db = db;
        _composer = composer;
    }

    [Function("Agent_ReportDownload")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/report")] HttpRequestData req)
    {
        var agentIdHeader = req.Headers.TryGetValues("X-Agent-Id", out var vals)
            ? vals.FirstOrDefault() : null;
        if (!Guid.TryParse(agentIdHeader, out var agentId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "X-Agent-Id required" });
            return bad;
        }

        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.AgentId == agentId);
        if (machine is null)
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            await nf.WriteAsJsonAsync(new { error = "unknown agent" });
            return nf;
        }

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var type = query["type"] ?? "preventas";
        var tone = query["tone"] ?? "detailed";
        var lang = query["lang"] ?? "es";

        var options = new ReportOptions(Lang: lang, Tone: tone);
        var html = await _composer.GenerateAsync(machine.OrganizationId, type, options);

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        await response.WriteStringAsync(html);
        return response;
    }
}
