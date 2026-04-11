using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// CRUD for control definitions. Admin can create/edit/deactivate controls.
/// Controls are the building blocks of assessments.
/// </summary>
[RequirePermission("controls:read")]
public class ControlDefsFunction
{
    private readonly KryossDbContext _db;
    private readonly IActlogService _actlog;

    public ControlDefsFunction(KryossDbContext db, IActlogService actlog)
    {
        _db = db;
        _actlog = actlog;
    }

    [Function("ControlDefs_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/controls")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var categoryStr = query["categoryId"];
        var type = query["type"];
        var severity = query["severity"];
        var search = query["search"];
        var pageStr = query["page"];
        var pageSizeStr = query["pageSize"];

        int page = int.TryParse(pageStr, out var p) ? Math.Max(1, p) : 1;
        int pageSize = int.TryParse(pageSizeStr, out var ps) ? Math.Clamp(ps, 1, 100) : 50;

        IQueryable<ControlDef> q = _db.ControlDefs
            .Include(c => c.Category);

        if (int.TryParse(categoryStr, out var catId))
            q = q.Where(c => c.CategoryId == catId);
        if (!string.IsNullOrEmpty(type))
            q = q.Where(c => c.Type == type);
        if (!string.IsNullOrEmpty(severity))
            q = q.Where(c => c.Severity == severity);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(c => c.Name.Contains(search) || c.ControlId.Contains(search));

        var total = await q.CountAsync();
        var controls = await q
            .OrderBy(c => c.ControlId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.ControlId,
                c.Name,
                c.Type,
                c.Severity,
                c.IsActive,
                c.Version,
                categoryName = c.Category.Name,
                frameworks = _db.ControlFrameworks
                    .Where(cf => cf.ControlDefId == c.Id)
                    .Join(_db.Frameworks, cf => cf.FrameworkId, f => f.Id,
                        (cf, f) => new { f.Code, cf.FrameworkRef })
                    .ToList()
            }).ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { total, page, pageSize, items = controls });
        return response;
    }

    [Function("ControlDefs_Get")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/controls/{id:int}")] HttpRequestData req,
        int id)
    {
        var control = await _db.ControlDefs
            .Include(c => c.Category)
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id,
                c.ControlId,
                c.Name,
                c.Type,
                c.Severity,
                c.CheckJson,
                c.Remediation,
                c.IsActive,
                c.Version,
                categoryName = c.Category.Name,
                frameworks = _db.ControlFrameworks
                    .Where(cf => cf.ControlDefId == c.Id)
                    .Join(_db.Frameworks, cf => cf.FrameworkId, f => f.Id,
                        (cf, f) => new { f.Id, f.Code, f.Name, cf.FrameworkRef })
                    .ToList(),
                platforms = _db.ControlPlatforms
                    .Where(cp => cp.ControlDefId == c.Id)
                    .Join(_db.Platforms, cp => cp.PlatformId, pl => pl.Id,
                        (cp, pl) => new { pl.Id, pl.Code, pl.Name })
                    .ToList(),
                // Where this control fails most
                failStats = _db.ControlResults
                    .Where(cr => cr.ControlDefId == c.Id && cr.Status == "fail")
                    .Count()
            }).FirstOrDefaultAsync();

        if (control is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Control not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(control);
        return response;
    }

    [Function("ControlDefs_Create")]
    [RequirePermission("controls:create")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/controls")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<UpsertControlRequest>();
        if (body is null || string.IsNullOrWhiteSpace(body.ControlId) || string.IsNullOrWhiteSpace(body.Name))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "controlId, name, type, and checkJson are required" });
            return bad;
        }

        // Check for duplicate controlId
        var exists = await _db.ControlDefs.AnyAsync(c => c.ControlId == body.ControlId);
        if (exists)
        {
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(new { error = $"Control '{body.ControlId}' already exists" });
            return conflict;
        }

        var control = new ControlDef
        {
            ControlId = body.ControlId,
            CategoryId = body.CategoryId,
            Name = body.Name,
            Type = body.Type!,
            Severity = body.Severity,
            CheckJson = body.CheckJson!,
            Remediation = body.Remediation,
            IsActive = true,
            Version = 1
        };
        _db.ControlDefs.Add(control);
        await _db.SaveChangesAsync();

        // Add framework/platform mappings
        if (body.FrameworkIds is { Count: > 0 })
        {
            _db.ControlFrameworks.AddRange(body.FrameworkIds.Select(fid => new ControlFramework
            {
                ControlDefId = control.Id,
                FrameworkId = fid
            }));
        }
        if (body.PlatformIds is { Count: > 0 })
        {
            _db.ControlPlatforms.AddRange(body.PlatformIds.Select(pid => new ControlPlatform
            {
                ControlDefId = control.Id,
                PlatformId = pid
            }));
        }
        await _db.SaveChangesAsync();

        await _actlog.LogAsync("INFO", "controls", "control_def.created",
            $"Control '{body.ControlId}' created: {body.Name}",
            entityType: "ControlDef", entityId: control.Id.ToString());

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new { id = control.Id });
        return response;
    }

    [Function("ControlDefs_Update")]
    [RequirePermission("controls:edit")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v2/controls/{id:int}")] HttpRequestData req,
        int id)
    {
        var body = await req.ReadFromJsonAsync<UpsertControlRequest>();
        var control = await _db.ControlDefs.FindAsync(id);
        if (control is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Control not found" });
            return notFound;
        }

        if (body?.Name is not null) control.Name = body.Name;
        if (body?.Type is not null) control.Type = body.Type;
        if (body?.Severity is not null) control.Severity = body.Severity;
        if (body?.CheckJson is not null) control.CheckJson = body.CheckJson;
        if (body?.Remediation is not null) control.Remediation = body.Remediation;
        if (body?.CategoryId > 0) control.CategoryId = body.CategoryId;
        if (body?.IsActive is not null) control.IsActive = body.IsActive.Value;
        control.Version++;

        await _db.SaveChangesAsync();

        await _actlog.LogAsync("INFO", "controls", "control_def.updated",
            $"Control '{control.ControlId}' updated to version {control.Version}",
            entityType: "ControlDef", entityId: id.ToString());

        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}

public class UpsertControlRequest
{
    public string? ControlId { get; set; }
    public int CategoryId { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Severity { get; set; }
    public string? CheckJson { get; set; }
    public string? Remediation { get; set; }
    public bool? IsActive { get; set; }
    public List<int>? FrameworkIds { get; set; }
    public List<int>? PlatformIds { get; set; }
}
