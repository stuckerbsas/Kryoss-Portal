using System.Net;
using KryossApi.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// GET /api/v2/me — Returns the current authenticated user's profile and permissions.
/// No [RequirePermission] — any authenticated user can read their own profile.
/// </summary>
public class MeFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public MeFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    [Function("Me_Get")]
    public async Task<HttpResponseData> Me(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/me")] HttpRequestData req)
    {
        if (_user.UserId == Guid.Empty)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauth;
        }

        var user = await _db.Users
            .Include(u => u.Role)
                .ThenInclude(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .Include(u => u.Franchise)
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == _user.UserId);

        if (user is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "User not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            id = user.Id,
            email = user.Email,
            displayName = user.DisplayName,
            authSource = user.AuthSource,
            lastLoginAt = user.LastLoginAt,
            role = new
            {
                id = user.Role.Id,
                code = user.Role.Code,
                name = user.Role.Name,
                isSystem = user.Role.IsSystem
            },
            franchise = user.Franchise != null ? new
            {
                id = user.Franchise.Id,
                name = user.Franchise.Name
            } : null,
            organization = user.Organization != null ? new
            {
                id = user.Organization.Id,
                name = user.Organization.Name
            } : null,
            permissions = user.Role.RolePermissions
                .Select(rp => rp.Permission.Slug)
                .OrderBy(s => s)
                .ToArray()
        });
        return response;
    }
}
