using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// CRUD for organizations. Portal users manage their client organizations.
/// Cascade soft-delete removes org + machines + enrollment codes + crypto keys.
/// </summary>
public class OrganizationsFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IActlogService _actlog;

    public OrganizationsFunction(KryossDbContext db, ICurrentUserService user, IActlogService actlog)
    {
        _db = db;
        _user = user;
        _actlog = actlog;
    }

    [Function("Organizations_List")]
    [RequirePermission("organizations:read")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/organizations")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var status = query["status"];
        var search = query["search"];
        var pageStr = query["page"];
        var pageSizeStr = query["pageSize"];

        int page = int.TryParse(pageStr, out var p) ? Math.Max(1, p) : 1;
        int pageSize = int.TryParse(pageSizeStr, out var ps) ? Math.Clamp(ps, 1, 100) : 25;

        IQueryable<Data.Entities.Organization> q = _db.Organizations;

        // RLS scoping: non-admin users only see their franchise's orgs
        if (!_user.IsAdmin && _user.FranchiseId.HasValue)
            q = q.Where(o => o.FranchiseId == _user.FranchiseId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(o => o.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(o => o.Name.Contains(search) || (o.LegalName != null && o.LegalName.Contains(search)));

        var total = await q.CountAsync();
        var items = await q
            .OrderBy(o => o.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.FranchiseId,
                o.Name,
                o.LegalName,
                o.TaxId,
                o.Status,
                o.EntraTenantId,
                o.BrandId,
                brand = _db.Brands
                    .Where(b => b.Id == o.BrandId)
                    .Select(b => new { b.Id, b.Code, b.Name })
                    .FirstOrDefault(),
                machineCount = _db.Machines.Count(m => m.OrganizationId == o.Id && m.IsActive),
                lastAssessmentAt = _db.AssessmentRuns
                    .Where(r => r.OrganizationId == o.Id)
                    .Max(r => (DateTime?)r.StartedAt),
                enrollmentCodeCount = _db.EnrollmentCodes
                    .Count(e => e.OrganizationId == o.Id),
                o.CreatedAt,
                o.ModifiedAt
            }).ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { total, page, pageSize, items });
        return response;
    }

    [Function("Organizations_Get")]
    [RequirePermission("organizations:read")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/organizations/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var org = await _db.Organizations
            .Where(o => o.Id == id)
            .Select(o => new
            {
                o.Id,
                o.FranchiseId,
                o.Name,
                o.LegalName,
                o.TaxId,
                o.Status,
                o.EntraTenantId,
                o.BrandId,
                brand = _db.Brands
                    .Where(b => b.Id == o.BrandId)
                    .Select(b => new { b.Id, b.Code, b.Name })
                    .FirstOrDefault(),
                machineCount = _db.Machines.Count(m => m.OrganizationId == o.Id && m.IsActive),
                lastAssessmentAt = _db.AssessmentRuns
                    .Where(r => r.OrganizationId == o.Id)
                    .Max(r => (DateTime?)r.StartedAt),
                enrollmentCodeCount = _db.EnrollmentCodes
                    .Count(e => e.OrganizationId == o.Id),
                o.CreatedAt,
                o.ModifiedAt
            }).FirstOrDefaultAsync();

        if (org is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Organization not found" });
            return notFound;
        }

        // RLS: non-admin can only see their franchise's org
        if (!_user.IsAdmin && _user.FranchiseId.HasValue && org.FranchiseId != _user.FranchiseId.Value)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(org);
        return response;
    }

    [Function("Organizations_Create")]
    [RequirePermission("organizations:create")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/organizations")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<CreateOrganizationRequest>();
        if (body is null || string.IsNullOrWhiteSpace(body.Name))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "name is required" });
            return bad;
        }

        if (!_user.FranchiseId.HasValue)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Franchise context required" });
            return bad;
        }

        // Non-admin cannot set status other than prospect or a custom brandId
        var status = body.Status ?? "prospect";
        int brandId;

        if (!_user.IsAdmin)
        {
            status = "prospect";
            var defaultBrand = await _db.Brands.FirstOrDefaultAsync(b => b.Code == "teamlogic");
            brandId = defaultBrand?.Id ?? 1;
        }
        else
        {
            if (body.BrandId.HasValue)
                brandId = body.BrandId.Value;
            else
            {
                var defaultBrand = await _db.Brands.FirstOrDefaultAsync(b => b.Code == "teamlogic");
                brandId = defaultBrand?.Id ?? 1;
            }
        }

        var org = new Data.Entities.Organization
        {
            Id = Guid.NewGuid(),
            FranchiseId = _user.FranchiseId.Value,
            Name = body.Name.Trim(),
            LegalName = body.LegalName?.Trim(),
            TaxId = body.TaxId?.Trim(),
            Status = status,
            BrandId = brandId,
            EntraTenantId = body.EntraTenantId
        };

        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();

        await _actlog.LogAsync("INFO", "organizations", "organization.created",
            $"Organization '{org.Name}' created (status={org.Status})",
            entityType: "Organization", entityId: org.Id.ToString(),
            newValues: JsonSerializer.Serialize(new { org.Name, org.LegalName, org.TaxId, org.Status, org.BrandId, org.EntraTenantId }));

        // Return detail shape
        var brand = await _db.Brands
            .Where(b => b.Id == org.BrandId)
            .Select(b => new { b.Id, b.Code, b.Name })
            .FirstOrDefaultAsync();

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            org.Id,
            org.FranchiseId,
            org.Name,
            org.LegalName,
            org.TaxId,
            org.Status,
            org.EntraTenantId,
            org.BrandId,
            brand,
            machineCount = 0,
            lastAssessmentAt = (DateTime?)null,
            enrollmentCodeCount = 0,
            org.CreatedAt,
            org.ModifiedAt
        });
        return response;
    }

    [Function("Organizations_Update")]
    [RequirePermission("organizations:edit")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/organizations/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var org = await _db.Organizations
            .Include(o => o.Brand)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (org is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Organization not found" });
            return notFound;
        }

        // RLS: non-admin can only edit their franchise's org
        if (!_user.IsAdmin && _user.FranchiseId.HasValue && org.FranchiseId != _user.FranchiseId.Value)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        var body = await req.ReadFromJsonAsync<UpdateOrganizationRequest>();
        if (body is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Request body is required" });
            return bad;
        }

        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        if (body.Name is not null && body.Name != org.Name)
        {
            oldValues["name"] = org.Name;
            newValues["name"] = body.Name.Trim();
            org.Name = body.Name.Trim();
        }

        if (body.LegalName is not null && body.LegalName != org.LegalName)
        {
            oldValues["legalName"] = org.LegalName;
            newValues["legalName"] = body.LegalName.Trim();
            org.LegalName = body.LegalName.Trim();
        }

        if (body.TaxId is not null && body.TaxId != org.TaxId)
        {
            oldValues["taxId"] = org.TaxId;
            newValues["taxId"] = body.TaxId.Trim();
            org.TaxId = body.TaxId.Trim();
        }

        if (body.EntraTenantId.HasValue && body.EntraTenantId != org.EntraTenantId)
        {
            oldValues["entraTenantId"] = org.EntraTenantId;
            newValues["entraTenantId"] = body.EntraTenantId;
            org.EntraTenantId = body.EntraTenantId;
        }

        // Only super_admin can change status or brandId
        if (body.Status is not null && body.Status != org.Status)
        {
            if (!_user.IsAdmin)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Only administrators can change organization status" });
                return forbidden;
            }
            oldValues["status"] = org.Status;
            newValues["status"] = body.Status;
            org.Status = body.Status;
        }

        if (body.BrandId.HasValue && body.BrandId.Value != org.BrandId)
        {
            if (!_user.IsAdmin)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Only administrators can change brand" });
                return forbidden;
            }
            oldValues["brandId"] = org.BrandId;
            newValues["brandId"] = body.BrandId.Value;
            org.BrandId = body.BrandId.Value;
        }

        if (newValues.Count > 0)
        {
            await _db.SaveChangesAsync();
            await _actlog.LogAsync("INFO", "organizations", "organization.updated",
                $"Organization '{org.Name}' updated",
                entityType: "Organization", entityId: org.Id.ToString(),
                oldValues: JsonSerializer.Serialize(oldValues),
                newValues: JsonSerializer.Serialize(newValues));
        }

        var brand = await _db.Brands
            .Where(b => b.Id == org.BrandId)
            .Select(b => new { b.Id, b.Code, b.Name })
            .FirstOrDefaultAsync();

        var machineCount = await _db.Machines.CountAsync(m => m.OrganizationId == org.Id && m.IsActive);
        var lastAssessmentAt = await _db.AssessmentRuns
            .Where(r => r.OrganizationId == org.Id)
            .MaxAsync(r => (DateTime?)r.StartedAt);
        var enrollmentCodeCount = await _db.EnrollmentCodes
            .CountAsync(e => e.OrganizationId == org.Id);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            org.Id,
            org.FranchiseId,
            org.Name,
            org.LegalName,
            org.TaxId,
            org.Status,
            org.EntraTenantId,
            org.BrandId,
            brand,
            machineCount,
            lastAssessmentAt,
            enrollmentCodeCount,
            org.CreatedAt,
            org.ModifiedAt
        });
        return response;
    }

    [Function("Organizations_Delete")]
    [RequirePermission("organizations:delete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v2/organizations/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var org = await _db.Organizations
            .FirstOrDefaultAsync(o => o.Id == id);

        if (org is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Organization not found" });
            return notFound;
        }

        // RLS: non-admin can only delete their franchise's org
        if (!_user.IsAdmin && _user.FranchiseId.HasValue && org.FranchiseId != _user.FranchiseId.Value)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        // Cascade soft-delete in one transaction
        var machines = await _db.Machines
            .Where(m => m.OrganizationId == id)
            .ToListAsync();

        var enrollmentCodes = await _db.EnrollmentCodes
            .Where(e => e.OrganizationId == id)
            .ToListAsync();

        var cryptoKeys = await _db.OrgCryptoKeys
            .Where(k => k.OrganizationId == id)
            .ToListAsync();

        // Remove each entity (AuditInterceptor converts to soft-delete)
        foreach (var machine in machines)
            _db.Machines.Remove(machine);

        foreach (var code in enrollmentCodes)
            _db.EnrollmentCodes.Remove(code);

        foreach (var key in cryptoKeys)
            _db.OrgCryptoKeys.Remove(key);

        _db.Organizations.Remove(org);

        await _db.SaveChangesAsync();

        await _actlog.LogAsync("SEC", "organizations", "organization.cascade_deleted",
            $"Organization '{org.Name}' cascade-deleted (machines={machines.Count}, codes={enrollmentCodes.Count}, keys={cryptoKeys.Count})",
            entityType: "Organization", entityId: org.Id.ToString(),
            newValues: JsonSerializer.Serialize(new
            {
                machineCount = machines.Count,
                enrollmentCodeCount = enrollmentCodes.Count,
                cryptoKeyCount = cryptoKeys.Count
            }));

        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}

public class CreateOrganizationRequest
{
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Status { get; set; }
    public int? BrandId { get; set; }
    public Guid? EntraTenantId { get; set; }
}

public class UpdateOrganizationRequest
{
    public string? Name { get; set; }
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Status { get; set; }
    public int? BrandId { get; set; }
    public Guid? EntraTenantId { get; set; }
}
