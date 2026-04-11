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
/// Recycle bin: list and restore soft-deleted organizations, machines, and enrollment codes.
/// </summary>
public class RecycleBinFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IActlogService _actlog;

    public RecycleBinFunction(KryossDbContext db, ICurrentUserService user, IActlogService actlog)
    {
        _db = db;
        _user = user;
        _actlog = actlog;
    }

    [Function("RecycleBin_List")]
    [RequirePermission("recycle_bin:read")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/recycle-bin")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var typeFilter = query["type"]; // "organization", "machine", "enrollment_code"

        var items = new List<object>();

        // --- Organizations ---
        if (typeFilter is null or "organization")
        {
            var orgs = await _db.Organizations
                .IgnoreQueryFilters()
                .Where(o => o.DeletedAt != null)
                .Select(o => new
                {
                    o.Id,
                    o.Name,
                    o.DeletedAt,
                    o.DeletedBy,
                    machineCount = _db.Machines.IgnoreQueryFilters()
                        .Count(m => m.OrganizationId == o.Id),
                    enrollmentCodeCount = _db.EnrollmentCodes.IgnoreQueryFilters()
                        .Count(e => e.OrganizationId == o.Id)
                })
                .ToListAsync();

            foreach (var o in orgs)
            {
                var email = await ResolveDeletedByEmail(o.DeletedBy);
                items.Add(new
                {
                    entityType = "organization",
                    id = o.Id.ToString(),
                    name = o.Name,
                    description = $"{o.machineCount} machines, {o.enrollmentCodeCount} enrollment codes",
                    deletedAt = o.DeletedAt,
                    deletedByEmail = email,
                    canRestore = true
                });
            }
        }

        // --- Machines ---
        if (typeFilter is null or "machine")
        {
            var machines = await _db.Machines
                .IgnoreQueryFilters()
                .Where(m => m.DeletedAt != null)
                .Select(m => new
                {
                    m.Id,
                    m.Hostname,
                    m.DeletedAt,
                    m.DeletedBy,
                    m.OrganizationId,
                    orgName = _db.Organizations.IgnoreQueryFilters()
                        .Where(o => o.Id == m.OrganizationId)
                        .Select(o => o.Name)
                        .FirstOrDefault(),
                    orgDeletedAt = _db.Organizations.IgnoreQueryFilters()
                        .Where(o => o.Id == m.OrganizationId)
                        .Select(o => o.DeletedAt)
                        .FirstOrDefault()
                })
                .ToListAsync();

            foreach (var m in machines)
            {
                var email = await ResolveDeletedByEmail(m.DeletedBy);
                items.Add(new
                {
                    entityType = "machine",
                    id = m.Id.ToString(),
                    name = m.Hostname,
                    description = $"Org: {m.orgName ?? "Unknown"}",
                    deletedAt = m.DeletedAt,
                    deletedByEmail = email,
                    canRestore = m.orgDeletedAt == null // can only restore if parent org is not deleted
                });
            }
        }

        // --- Enrollment codes ---
        if (typeFilter is null or "enrollment_code")
        {
            var codes = await _db.EnrollmentCodes
                .IgnoreQueryFilters()
                .Where(e => e.DeletedAt != null)
                .Select(e => new
                {
                    e.Id,
                    e.Code,
                    e.Label,
                    e.DeletedAt,
                    e.DeletedBy,
                    e.OrganizationId,
                    orgName = _db.Organizations.IgnoreQueryFilters()
                        .Where(o => o.Id == e.OrganizationId)
                        .Select(o => o.Name)
                        .FirstOrDefault(),
                    orgDeletedAt = _db.Organizations.IgnoreQueryFilters()
                        .Where(o => o.Id == e.OrganizationId)
                        .Select(o => o.DeletedAt)
                        .FirstOrDefault()
                })
                .ToListAsync();

            foreach (var c in codes)
            {
                var email = await ResolveDeletedByEmail(c.DeletedBy);
                var displayName = !string.IsNullOrWhiteSpace(c.Label) ? $"{c.Code} ({c.Label})" : c.Code;
                items.Add(new
                {
                    entityType = "enrollment_code",
                    id = c.Id.ToString(),
                    name = displayName,
                    description = $"Org: {c.orgName ?? "Unknown"}",
                    deletedAt = c.DeletedAt,
                    deletedByEmail = email,
                    canRestore = c.orgDeletedAt == null
                });
            }
        }

        // Sort by DeletedAt descending
        var sorted = items
            .OrderByDescending(i =>
            {
                var prop = i.GetType().GetProperty("deletedAt");
                return prop?.GetValue(i) as DateTime?;
            })
            .ToList();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { items = sorted });
        return response;
    }

    [Function("RecycleBin_Restore")]
    [RequirePermission("recycle_bin:restore")]
    public async Task<HttpResponseData> Restore(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/recycle-bin/{entityType}/{id}/restore")] HttpRequestData req,
        string entityType,
        string id)
    {
        return entityType switch
        {
            "organization" => await RestoreOrganization(req, id),
            "machine" => await RestoreMachine(req, id),
            "enrollment_code" => await RestoreEnrollmentCode(req, id),
            _ => await BadRequest(req, $"Unknown entity type '{entityType}'. Valid values: organization, machine, enrollment_code")
        };
    }

    private async Task<HttpResponseData> RestoreOrganization(HttpRequestData req, string idStr)
    {
        if (!Guid.TryParse(idStr, out var id))
            return await BadRequest(req, "Invalid organization ID format (expected GUID)");

        var org = await _db.Organizations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id && o.DeletedAt != null);

        if (org is null)
            return await NotFound(req, "Deleted organization not found");

        // Restore org
        org.DeletedAt = null;
        org.DeletedBy = null;

        // Cascade restore children
        var machines = await _db.Machines
            .IgnoreQueryFilters()
            .Where(m => m.OrganizationId == id && m.DeletedAt != null)
            .ToListAsync();

        foreach (var m in machines)
        {
            m.DeletedAt = null;
            m.DeletedBy = null;
        }

        var enrollmentCodes = await _db.EnrollmentCodes
            .IgnoreQueryFilters()
            .Where(e => e.OrganizationId == id && e.DeletedAt != null)
            .ToListAsync();

        foreach (var e in enrollmentCodes)
        {
            e.DeletedAt = null;
            e.DeletedBy = null;
        }

        var cryptoKeys = await _db.OrgCryptoKeys
            .IgnoreQueryFilters()
            .Where(k => k.OrganizationId == id && k.DeletedAt != null)
            .ToListAsync();

        foreach (var k in cryptoKeys)
        {
            k.DeletedAt = null;
            k.DeletedBy = null;
        }

        await _db.SaveChangesAsync();

        await _actlog.LogAsync("SEC", "recycle_bin", "organization.restored",
            $"Organization '{org.Name}' restored with {machines.Count} machines, {enrollmentCodes.Count} codes, {cryptoKeys.Count} keys",
            entityType: "Organization", entityId: org.Id.ToString(),
            newValues: JsonSerializer.Serialize(new
            {
                machines = machines.Count,
                enrollmentCodes = enrollmentCodes.Count,
                cryptoKeys = cryptoKeys.Count
            }));

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            entityType = "organization",
            id = org.Id,
            restoredChildren = new
            {
                machines = machines.Count,
                enrollmentCodes = enrollmentCodes.Count,
                cryptoKeys = cryptoKeys.Count
            }
        });
        return response;
    }

    private async Task<HttpResponseData> RestoreMachine(HttpRequestData req, string idStr)
    {
        if (!Guid.TryParse(idStr, out var id))
            return await BadRequest(req, "Invalid machine ID format (expected GUID)");

        var machine = await _db.Machines
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt != null);

        if (machine is null)
            return await NotFound(req, "Deleted machine not found");

        // Check parent org is not soft-deleted
        var orgDeleted = await _db.Organizations
            .IgnoreQueryFilters()
            .Where(o => o.Id == machine.OrganizationId)
            .Select(o => o.DeletedAt)
            .FirstOrDefaultAsync();

        if (orgDeleted != null)
            return await BadRequest(req, "Parent organization is deleted. Restore it first.");

        machine.DeletedAt = null;
        machine.DeletedBy = null;
        await _db.SaveChangesAsync();

        await _actlog.LogAsync("SEC", "recycle_bin", "machine.restored",
            $"Machine '{machine.Hostname}' restored",
            entityType: "Machine", entityId: machine.Id.ToString());

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            entityType = "machine",
            id = machine.Id
        });
        return response;
    }

    private async Task<HttpResponseData> RestoreEnrollmentCode(HttpRequestData req, string idStr)
    {
        if (!int.TryParse(idStr, out var id))
            return await BadRequest(req, "Invalid enrollment code ID format (expected integer)");

        var code = await _db.EnrollmentCodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id && e.DeletedAt != null);

        if (code is null)
            return await NotFound(req, "Deleted enrollment code not found");

        // Check parent org is not soft-deleted
        var orgDeleted = await _db.Organizations
            .IgnoreQueryFilters()
            .Where(o => o.Id == code.OrganizationId)
            .Select(o => o.DeletedAt)
            .FirstOrDefaultAsync();

        if (orgDeleted != null)
            return await BadRequest(req, "Parent organization is deleted. Restore it first.");

        code.DeletedAt = null;
        code.DeletedBy = null;
        await _db.SaveChangesAsync();

        await _actlog.LogAsync("SEC", "recycle_bin", "enrollment_code.restored",
            $"Enrollment code '{code.Code}' restored",
            entityType: "EnrollmentCode", entityId: code.Id.ToString());

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            entityType = "enrollment_code",
            id = code.Id
        });
        return response;
    }

    private async Task<string?> ResolveDeletedByEmail(Guid? deletedBy)
    {
        if (deletedBy is null) return null;

        return await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == deletedBy.Value)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();
    }

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string error)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { error });
        return response;
    }

    private static async Task<HttpResponseData> NotFound(HttpRequestData req, string error)
    {
        var response = req.CreateResponse(HttpStatusCode.NotFound);
        await response.WriteAsJsonAsync(new { error });
        return response;
    }
}
