using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// AD Hygiene: receive findings from agent network scan, serve to portal.
/// Agent submits via API key auth (v1), portal reads via Bearer auth (v2).
/// </summary>
public class HygieneFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public HygieneFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    /// <summary>
    /// Agent submits AD hygiene findings after a network scan.
    /// POST /v1/hygiene
    /// </summary>
    [Function("Hygiene_Submit")]
    public async Task<HttpResponseData> Submit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/hygiene")] HttpRequestData req,
        FunctionContext context)
    {
        // Body may have been consumed by HMAC middleware — check Items first
        HygieneSubmitRequest? body = null;
        if (context.Items.TryGetValue("RequestBodyBytes", out var rawObj) && rawObj is byte[] rawBytes && rawBytes.Length > 0)
        {
            body = JsonSerializer.Deserialize<HygieneSubmitRequest>(rawBytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        else
        {
            body = await req.ReadFromJsonAsync<HygieneSubmitRequest>();
        }

        if (body is null || body.Findings is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid hygiene payload" });
            return bad;
        }

        var orgId = _user.OrganizationId;
        if (orgId is null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Organization context required" });
            return unauth;
        }

        var scan = new AdHygieneScan
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId.Value,
            ScannedBy = body.ScannedBy ?? "Unknown",
            ScannedAt = DateTime.UtcNow,
            TotalMachines = body.TotalMachines ?? body.Findings.Count(f => f.ObjectType == "Computer"),
            TotalUsers = body.TotalUsers ?? body.Findings.Count(f => f.ObjectType == "User"),
            StaleMachines = body.Findings.Count(f => f.ObjectType == "Computer" && f.Status == "Stale"),
            DormantMachines = body.Findings.Count(f => f.ObjectType == "Computer" && f.Status == "Dormant"),
            StaleUsers = body.Findings.Count(f => f.ObjectType == "User" && f.Status is "Stale" or "OldPassword"),
            DormantUsers = body.Findings.Count(f => f.ObjectType == "User" && f.Status == "Dormant"),
            DisabledUsers = body.Findings.Count(f => f.ObjectType == "User" && f.Status == "Disabled"),
            PwdNeverExpire = body.Findings.Count(f => f.ObjectType == "User" && f.Status == "PwdNeverExpires"),
        };

        _db.AdHygieneScans.Add(scan);

        foreach (var f in body.Findings)
        {
            _db.AdHygieneFindings.Add(new AdHygieneFinding
            {
                ScanId = scan.Id,
                Name = f.Name,
                ObjectType = f.ObjectType,
                Status = f.Status,
                DaysInactive = f.DaysInactive,
                Detail = f.Detail,
            });
        }

        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { scanId = scan.Id, findings = body.Findings.Count });
        return response;
    }

    /// <summary>
    /// Portal reads the latest hygiene scan for an org.
    /// GET /v2/hygiene?organizationId={guid}
    /// </summary>
    [Function("Hygiene_Get")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/hygiene")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        Guid orgId;
        if (Guid.TryParse(orgIdStr, out var parsed))
            orgId = parsed;
        else if (_user.OrganizationId.HasValue)
            orgId = _user.OrganizationId.Value;
        else
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var latestScan = await _db.AdHygieneScans
            .Where(s => s.OrganizationId == orgId)
            .OrderByDescending(s => s.ScannedAt)
            .Select(s => new
            {
                s.Id,
                s.ScannedBy,
                s.ScannedAt,
                s.TotalMachines,
                s.TotalUsers,
                s.StaleMachines,
                s.DormantMachines,
                s.StaleUsers,
                s.DormantUsers,
                s.DisabledUsers,
                s.PwdNeverExpire,
                findings = _db.AdHygieneFindings
                    .Where(f => f.ScanId == s.Id)
                    .OrderBy(f => f.ObjectType)
                    .ThenByDescending(f => f.DaysInactive)
                    .Select(f => new
                    {
                        f.Name,
                        f.ObjectType,
                        f.Status,
                        f.DaysInactive,
                        f.Detail
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(latestScan ?? (object)new { });
        return response;
    }
}

public class HygieneSubmitRequest
{
    public string? ScannedBy { get; set; }
    public int? TotalMachines { get; set; }
    public int? TotalUsers { get; set; }
    public List<HygieneFindingItem> Findings { get; set; } = [];
}

public class HygieneFindingItem
{
    public string Name { get; set; } = null!;
    public string ObjectType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int DaysInactive { get; set; }
    public string? Detail { get; set; }
}
