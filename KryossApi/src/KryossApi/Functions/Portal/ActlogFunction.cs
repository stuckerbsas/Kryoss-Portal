using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

[RequirePermission("admin:read")]
public class ActlogFunction
{
    private readonly KryossDbContext _db;

    public ActlogFunction(KryossDbContext db) => _db = db;

    [Function("Actlog_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/actlog")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var severity = query["severity"];
        var module = query["module"];
        var limitStr = query["limit"];
        var limit = int.TryParse(limitStr, out var l) && l > 0 && l <= 500 ? l : 100;

        var q = _db.Actlog.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(severity))
            q = q.Where(a => a.Severity == severity.ToUpperInvariant());

        if (!string.IsNullOrEmpty(module))
            q = q.Where(a => a.Module == module);

        var items = await q
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new
            {
                a.Id,
                a.Timestamp,
                a.ActorEmail,
                a.ActorIp,
                a.Severity,
                a.Module,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.ResponseCode,
                a.DurationMs,
                a.Message
            })
            .ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(items);
        return response;
    }
}
