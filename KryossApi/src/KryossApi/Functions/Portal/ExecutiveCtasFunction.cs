using System.Net;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// CRUD for operator-editable CTAs used by the C-Level report Block 3.
/// Persistence is per org per reporting period (calendar month). The
/// portal fetches the list via GET, allows the operator to edit title/
/// description/suppress via PATCH, add manual CTAs via POST, and remove
/// via DELETE (soft-deleted by the AuditInterceptor).
/// </summary>
[RequirePermission("reports:read")]
public class ExecutiveCtasFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IActlogService _actlog;

    public ExecutiveCtasFunction(KryossDbContext db, ICurrentUserService user, IActlogService actlog)
    {
        _db = db;
        _user = user;
        _actlog = actlog;
    }

    public record CtaDto(Guid Id, Guid OrganizationId, DateTime PeriodStart, string? AutoDetectedRule,
        string PriorityCategory, string Title, string Description, bool IsSuppressed, bool IsManual);

    public record UpsertCtaDto(Guid OrganizationId, DateTime PeriodStart, string? AutoDetectedRule,
        string PriorityCategory, string Title, string Description, bool IsSuppressed, bool IsManual);

    private async Task<bool> HasOrgAccessAsync(Guid orgId)
    {
        if (_user.IsAdmin) return true;
        if (_user.OrganizationId.HasValue && _user.OrganizationId.Value == orgId) return true;
        if (_user.FranchiseId.HasValue)
            return await _db.Organizations.AnyAsync(o => o.Id == orgId && o.FranchiseId == _user.FranchiseId.Value);
        return false;
    }

    [Function("ExecutiveCtas_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/executive-ctas")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["orgId"], out var orgId))
            return await Err(req, HttpStatusCode.BadRequest, "orgId required");
        if (!DateTime.TryParse(query["period"], out var periodStart))
            periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        if (!await HasOrgAccessAsync(orgId))
            return await Err(req, HttpStatusCode.Forbidden, "Access denied");

        try
        {
            var rows = await _db.ExecutiveCtas
                .Where(c => c.OrganizationId == orgId && c.PeriodStart == periodStart)
                .Select(c => new CtaDto(c.Id, c.OrganizationId, c.PeriodStart, c.AutoDetectedRule,
                    c.PriorityCategory, c.Title, c.Description, c.IsSuppressed, c.IsManual))
                .ToListAsync();

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(rows);
            return resp;
        }
        catch
        {
            // Migration 028 may not be applied yet — return empty list
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(Array.Empty<CtaDto>());
            return resp;
        }
    }

    [Function("ExecutiveCtas_Create")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/executive-ctas")] HttpRequestData req)
    {
        var dto = await req.ReadFromJsonAsync<UpsertCtaDto>();
        if (dto == null) return await Err(req, HttpStatusCode.BadRequest, "body required");

        if (!await HasOrgAccessAsync(dto.OrganizationId))
            return await Err(req, HttpStatusCode.Forbidden, "Access denied");

        var entity = new ExecutiveCta
        {
            Id = Guid.NewGuid(),
            OrganizationId = dto.OrganizationId,
            PeriodStart = dto.PeriodStart,
            AutoDetectedRule = dto.AutoDetectedRule,
            PriorityCategory = dto.PriorityCategory,
            Title = dto.Title,
            Description = dto.Description,
            IsSuppressed = dto.IsSuppressed,
            IsManual = dto.IsManual,
            CreatedBy = _user.UserId,
            CreatedAt = DateTime.UtcNow
        };
        _db.ExecutiveCtas.Add(entity);
        await _db.SaveChangesAsync();
        await _actlog.LogAsync("INFO", "reports", "cta.create",
            $"Created CTA {entity.Id} for org {entity.OrganizationId}",
            entityType: "ExecutiveCta", entityId: entity.Id.ToString());

        var resp = req.CreateResponse(HttpStatusCode.Created);
        await resp.WriteAsJsonAsync(new CtaDto(entity.Id, entity.OrganizationId, entity.PeriodStart,
            entity.AutoDetectedRule, entity.PriorityCategory, entity.Title, entity.Description,
            entity.IsSuppressed, entity.IsManual));
        return resp;
    }

    [Function("ExecutiveCtas_Update")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/executive-ctas/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var entity = await _db.ExecutiveCtas.FirstOrDefaultAsync(c => c.Id == id);
        if (entity == null) return await Err(req, HttpStatusCode.NotFound, "CTA not found");

        if (!await HasOrgAccessAsync(entity.OrganizationId))
            return await Err(req, HttpStatusCode.Forbidden, "Access denied");

        var dto = await req.ReadFromJsonAsync<UpsertCtaDto>();
        if (dto == null) return await Err(req, HttpStatusCode.BadRequest, "body required");
        entity.Title = dto.Title;
        entity.Description = dto.Description;
        entity.IsSuppressed = dto.IsSuppressed;
        entity.PriorityCategory = dto.PriorityCategory;
        entity.ModifiedAt = DateTime.UtcNow;
        entity.ModifiedBy = _user.UserId;
        await _db.SaveChangesAsync();
        await _actlog.LogAsync("INFO", "reports", "cta.update",
            $"Updated CTA {entity.Id}",
            entityType: "ExecutiveCta", entityId: entity.Id.ToString());

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new CtaDto(entity.Id, entity.OrganizationId, entity.PeriodStart,
            entity.AutoDetectedRule, entity.PriorityCategory, entity.Title, entity.Description,
            entity.IsSuppressed, entity.IsManual));
        return resp;
    }

    [Function("ExecutiveCtas_Delete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v2/executive-ctas/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var entity = await _db.ExecutiveCtas.FirstOrDefaultAsync(c => c.Id == id);
        if (entity == null) return await Err(req, HttpStatusCode.NotFound, "CTA not found");

        if (!await HasOrgAccessAsync(entity.OrganizationId))
            return await Err(req, HttpStatusCode.Forbidden, "Access denied");

        _db.ExecutiveCtas.Remove(entity); // soft-delete via AuditInterceptor
        await _db.SaveChangesAsync();
        await _actlog.LogAsync("INFO", "reports", "cta.delete",
            $"Deleted CTA {id}",
            entityType: "ExecutiveCta", entityId: id.ToString());

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private static async Task<HttpResponseData> Err(HttpRequestData req, HttpStatusCode code, string msg)
    {
        var resp = req.CreateResponse(code);
        await resp.WriteAsJsonAsync(new { error = msg });
        return resp;
    }
}
