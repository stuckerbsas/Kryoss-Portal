using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// CRUD for enrollment codes. Portal users generate codes to enroll machines.
/// </summary>
[RequirePermission("assessment:create")]
public class EnrollmentCodesFunction
{
    private readonly KryossDbContext _db;
    private readonly IEnrollmentService _enrollment;
    private readonly ICurrentUserService _user;
    private readonly IActlogService _actlog;

    public EnrollmentCodesFunction(KryossDbContext db, IEnrollmentService enrollment,
        ICurrentUserService user, IActlogService actlog)
    {
        _db = db;
        _enrollment = enrollment;
        _user = user;
        _actlog = actlog;
    }

    [Function("EnrollmentCodes_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/enrollment-codes")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        IQueryable<Data.Entities.EnrollmentCode> q = _db.EnrollmentCodes
            .Include(e => e.Assessment)
            .Include(e => e.UsedByMachine)
            .OrderByDescending(e => e.CreatedAt);

        // Filter by org if specified (franchise users can see all their orgs)
        if (Guid.TryParse(orgIdStr, out var orgId))
            q = q.Where(e => e.OrganizationId == orgId);
        else if (_user.OrganizationId.HasValue)
            q = q.Where(e => e.OrganizationId == _user.OrganizationId.Value);

        var codes = await q.Select(e => new
        {
            e.Id,
            e.Code,
            e.OrganizationId,
            e.Label,
            assessmentName = e.Assessment != null ? e.Assessment.Name : null,
            usedBy = e.UsedByMachine != null ? e.UsedByMachine.Hostname : null,
            e.UsedAt,
            e.ExpiresAt,
            e.CreatedAt,
            isExpired = e.ExpiresAt < DateTime.UtcNow,
            isUsed = e.UsedBy != null,
            e.IsTrial,
            e.TrialDays
        }).ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(codes);
        return response;
    }

    [Function("EnrollmentCodes_Create")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/enrollment-codes")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<CreateEnrollmentCodeRequest>();
        if (body is null || body.OrganizationId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        var code = await _enrollment.GenerateCodeAsync(
            body.OrganizationId,
            body.AssessmentId,
            body.Label,
            body.ExpiryDays ?? 7,
            body.MaxUses,
            body.IsTrial,
            body.TrialDays
        );

        await _actlog.LogAsync("INFO", "assessment", "enrollment_code.created",
            $"Enrollment code generated for org {body.OrganizationId}: {code[..8]}...",
            entityType: "EnrollmentCode");

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new { code });
        return response;
    }

    [Function("EnrollmentCodes_Delete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v2/enrollment-codes/{id:int}")] HttpRequestData req,
        int id)
    {
        var code = await _db.EnrollmentCodes.FindAsync(id);
        if (code is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Code not found" });
            return notFound;
        }

        if (code.UsedBy is not null)
        {
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(new { error = "Cannot delete a used code" });
            return conflict;
        }

        _db.EnrollmentCodes.Remove(code); // soft delete via AuditInterceptor
        await _db.SaveChangesAsync();

        await _actlog.LogAsync("INFO", "assessment", "enrollment_code.deleted",
            $"Enrollment code {code.Code[..8]}... deleted",
            entityType: "EnrollmentCode", entityId: id.ToString());

        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}

public class CreateEnrollmentCodeRequest
{
    public Guid OrganizationId { get; set; }
    public int? AssessmentId { get; set; }
    public string? Label { get; set; }
    public int? ExpiryDays { get; set; }
    public int? MaxUses { get; set; }   // NULL = single-use, N = can enroll N machines
    public bool IsTrial { get; set; }
    public int? TrialDays { get; set; }  // NULL = 30 days default
}
