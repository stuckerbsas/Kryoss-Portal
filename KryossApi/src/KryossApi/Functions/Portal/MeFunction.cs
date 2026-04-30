using System.Net;
using System.Text.Json;
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
            phone = user.Phone,
            jobTitle = user.JobTitle,
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

    [Function("Me_Update")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/me")] HttpRequestData req,
        FunctionContext context)
    {
        if (_user.UserId == Guid.Empty)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauth;
        }

        MeUpdateRequest? body = null;
        if (context.Items.TryGetValue("RequestBodyBytes", out var rawObj) && rawObj is byte[] rawBytes && rawBytes.Length > 0)
            body = JsonSerializer.Deserialize<MeUpdateRequest>(rawBytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        else
            body = await req.ReadFromJsonAsync<MeUpdateRequest>();

        if (body is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid payload" });
            return bad;
        }

        var user = await _db.Users.FindAsync(_user.UserId);
        if (user is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "User not found" });
            return notFound;
        }

        if (body.DisplayName is not null) user.DisplayName = body.DisplayName.Trim();
        if (body.Phone is not null) user.Phone = string.IsNullOrWhiteSpace(body.Phone) ? null : body.Phone.Trim();
        if (body.JobTitle is not null) user.JobTitle = string.IsNullOrWhiteSpace(body.JobTitle) ? null : body.JobTitle.Trim();

        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { user.Id, user.DisplayName, user.Phone, user.JobTitle });
        return response;
    }
}

public class MeUpdateRequest
{
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
}
