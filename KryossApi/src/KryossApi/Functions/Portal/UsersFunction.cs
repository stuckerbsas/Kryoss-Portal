using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class UsersFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IActlogService _actlog;

    public UsersFunction(KryossDbContext db, ICurrentUserService user, IActlogService actlog)
    {
        _db = db;
        _user = user;
        _actlog = actlog;
    }

    [Function("Users_List")]
    [RequirePermission("admin:read")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/users")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var search = query["search"];
        var roleCode = query["role"];
        var pageStr = query["page"];
        var pageSizeStr = query["pageSize"];

        int page = int.TryParse(pageStr, out var p) ? Math.Max(1, p) : 1;
        int pageSize = int.TryParse(pageSizeStr, out var ps) ? Math.Clamp(ps, 1, 100) : 25;

        IQueryable<Data.Entities.User> q = _db.Users.Where(u => u.DeletedAt == null);

        if (!_user.IsAdmin && _user.FranchiseId.HasValue)
            q = q.Where(u => u.FranchiseId == _user.FranchiseId.Value);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(u => u.Email.Contains(search) || u.DisplayName.Contains(search));

        if (!string.IsNullOrWhiteSpace(roleCode))
            q = q.Where(u => u.Role.Code == roleCode);

        var total = await q.CountAsync();
        var users = await q
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.Franchise)
            .Include(u => u.Organization)
            .OrderBy(u => u.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.AuthSource,
                u.LastLoginAt,
                u.Phone,
                u.JobTitle,
                role = new { u.Role.Id, u.Role.Code, u.Role.Name },
                franchise = u.Franchise != null ? new { u.Franchise.Id, u.Franchise.Name } : null,
                organization = u.Organization != null ? new { u.Organization.Id, u.Organization.Name } : null,
                u.CreatedAt
            }).ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { total, page, pageSize, items = users });
        return response;
    }

    [Function("Users_Get")]
    [RequirePermission("admin:read")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/users/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.Franchise)
            .Include(u => u.Organization)
            .Where(u => u.Id == id && u.DeletedAt == null)
            .FirstOrDefaultAsync();

        if (user is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "User not found" });
            return notFound;
        }

        if (!_user.IsAdmin && _user.FranchiseId.HasValue && user.FranchiseId != _user.FranchiseId.Value)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            user.AuthSource,
            user.LastLoginAt,
            user.Phone,
            user.JobTitle,
            role = new { user.Role.Id, user.Role.Code, user.Role.Name },
            franchise = user.Franchise != null ? new { user.Franchise.Id, user.Franchise.Name } : null,
            organization = user.Organization != null ? new { user.Organization.Id, user.Organization.Name } : null,
            user.CreatedAt,
            user.ModifiedAt
        });
        return response;
    }

    [Function("Users_Update")]
    [RequirePermission("admin:edit")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/users/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null);

        if (user is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "User not found" });
            return notFound;
        }

        if (!_user.IsAdmin && _user.FranchiseId.HasValue && user.FranchiseId != _user.FranchiseId.Value)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        // Cannot edit own role (prevent self-demotion/elevation)
        if (user.Id == _user.UserId)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Cannot modify your own account" });
            return forbidden;
        }

        var body = await req.ReadFromJsonAsync<UpdateUserRequest>();
        if (body is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Request body required" });
            return bad;
        }

        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        if (body.RoleCode is not null)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == body.RoleCode && r.DeletedAt == null);
            if (role is null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = $"Unknown role: {body.RoleCode}" });
                return bad;
            }
            // Only super_admin can assign super_admin
            if (role.Code == "super_admin" && !_user.IsAdmin)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Only super_admin can assign super_admin role" });
                return forbidden;
            }
            if (user.RoleId != role.Id)
            {
                oldValues["role"] = user.Role.Code;
                newValues["role"] = role.Code;
                user.RoleId = role.Id;
            }
        }

        if (body.FranchiseId.HasValue && body.FranchiseId != user.FranchiseId)
        {
            if (!_user.IsAdmin)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Only super_admin can change franchise" });
                return forbidden;
            }
            oldValues["franchiseId"] = user.FranchiseId;
            newValues["franchiseId"] = body.FranchiseId;
            user.FranchiseId = body.FranchiseId;
        }

        if (body.OrganizationId.HasValue)
        {
            if (body.OrganizationId.Value == Guid.Empty)
            {
                if (user.OrganizationId != null)
                {
                    oldValues["organizationId"] = user.OrganizationId;
                    newValues["organizationId"] = null;
                    user.OrganizationId = null;
                }
            }
            else if (body.OrganizationId != user.OrganizationId)
            {
                oldValues["organizationId"] = user.OrganizationId;
                newValues["organizationId"] = body.OrganizationId;
                user.OrganizationId = body.OrganizationId;
            }
        }

        if (body.DisplayName is not null && body.DisplayName != user.DisplayName)
        {
            oldValues["displayName"] = user.DisplayName;
            newValues["displayName"] = body.DisplayName;
            user.DisplayName = body.DisplayName;
        }

        if (body.Phone is not null && body.Phone != user.Phone)
        {
            oldValues["phone"] = user.Phone;
            newValues["phone"] = body.Phone;
            user.Phone = body.Phone;
        }

        if (body.JobTitle is not null && body.JobTitle != user.JobTitle)
        {
            oldValues["jobTitle"] = user.JobTitle;
            newValues["jobTitle"] = body.JobTitle;
            user.JobTitle = body.JobTitle;
        }

        if (newValues.Count > 0)
        {
            await _db.SaveChangesAsync();
            await _actlog.LogAsync("SEC", "admin", "user.updated",
                $"User '{user.Email}' updated",
                entityType: "User", entityId: user.Id.ToString(),
                oldValues: JsonSerializer.Serialize(oldValues),
                newValues: JsonSerializer.Serialize(newValues));
        }

        await _db.Entry(user).Reference(u => u.Role).LoadAsync();
        await _db.Entry(user).Reference(u => u.Franchise).LoadAsync();
        await _db.Entry(user).Reference(u => u.Organization).LoadAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            user.AuthSource,
            user.LastLoginAt,
            user.Phone,
            user.JobTitle,
            role = new { user.Role.Id, user.Role.Code, user.Role.Name },
            franchise = user.Franchise != null ? new { user.Franchise.Id, user.Franchise.Name } : null,
            organization = user.Organization != null ? new { user.Organization.Id, user.Organization.Name } : null,
            user.CreatedAt,
            user.ModifiedAt
        });
        return response;
    }

    [Function("Users_Delete")]
    [RequirePermission("admin:delete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v2/users/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null);
        if (user is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "User not found" });
            return notFound;
        }

        if (!_user.IsAdmin && _user.FranchiseId.HasValue && user.FranchiseId != _user.FranchiseId.Value)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        if (user.Id == _user.UserId)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Cannot delete your own account" });
            return forbidden;
        }

        _db.Users.Remove(user); // AuditInterceptor converts to soft-delete
        await _db.SaveChangesAsync();

        await _actlog.LogAsync("SEC", "admin", "user.deleted",
            $"User '{user.Email}' deleted",
            entityType: "User", entityId: user.Id.ToString());

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("Roles_List")]
    [RequirePermission("admin:read")]
    public async Task<HttpResponseData> ListRoles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/roles")] HttpRequestData req)
    {
        var roles = await _db.Roles
            .AsNoTracking()
            .Where(r => r.DeletedAt == null)
            .OrderBy(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.Code,
                r.Name,
                r.IsSystem,
                permissionCount = r.RolePermissions.Count
            }).ToListAsync();

        // Non-admin cannot see super_admin role
        if (!_user.IsAdmin)
            roles = roles.Where(r => r.Code != "super_admin").ToList();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(roles);
        return response;
    }
}

public class UpdateUserRequest
{
    public string? RoleCode { get; set; }
    public Guid? FranchiseId { get; set; }
    public Guid? OrganizationId { get; set; }
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
}
