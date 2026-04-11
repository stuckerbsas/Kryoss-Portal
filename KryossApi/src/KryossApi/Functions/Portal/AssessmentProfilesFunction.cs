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
/// CRUD for assessment profiles. Each org can have multiple profiles
/// (e.g., "CIS L1 Full", "Quick Scan") with different control subsets.
/// </summary>
[RequirePermission("assessment:read")]
public class AssessmentProfilesFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IActlogService _actlog;

    public AssessmentProfilesFunction(KryossDbContext db, ICurrentUserService user, IActlogService actlog)
    {
        _db = db;
        _user = user;
        _actlog = actlog;
    }

    [Function("AssessmentProfiles_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/assessments")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        IQueryable<Assessment> q = _db.Assessments;

        if (Guid.TryParse(orgIdStr, out var orgId))
            q = q.Where(a => a.OrganizationId == orgId);
        else if (_user.OrganizationId.HasValue)
            q = q.Where(a => a.OrganizationId == _user.OrganizationId.Value);

        var profiles = await q.Select(a => new
        {
            a.Id,
            a.OrganizationId,
            a.Name,
            a.Description,
            a.IsDefault,
            a.IsActive,
            controlCount = _db.AssessmentControls.Count(ac => ac.AssessmentId == a.Id),
            a.CreatedAt
        }).ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(profiles);
        return response;
    }

    [Function("AssessmentProfiles_Get")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/assessments/{id:int}")] HttpRequestData req,
        int id)
    {
        var assessment = await _db.Assessments
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id,
                a.OrganizationId,
                a.Name,
                a.Description,
                a.IsDefault,
                a.IsActive,
                controls = _db.AssessmentControls
                    .Where(ac => ac.AssessmentId == a.Id)
                    .Join(_db.ControlDefs, ac => ac.ControlDefId, cd => cd.Id,
                        (ac, cd) => new { cd.Id, cd.ControlId, cd.Name, cd.Type, cd.Severity })
                    .ToList()
            }).FirstOrDefaultAsync();

        if (assessment is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Assessment not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(assessment);
        return response;
    }

    [Function("AssessmentProfiles_Create")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/assessments")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<CreateAssessmentRequest>();
        if (body is null || body.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(body.Name))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId and name are required" });
            return bad;
        }

        var assessment = new Assessment
        {
            OrganizationId = body.OrganizationId,
            Name = body.Name,
            Description = body.Description,
            IsDefault = body.IsDefault,
            IsActive = true
        };
        _db.Assessments.Add(assessment);
        await _db.SaveChangesAsync();

        // Add controls if specified
        if (body.ControlDefIds is { Count: > 0 })
        {
            var controls = body.ControlDefIds.Select(cid => new AssessmentControl
            {
                AssessmentId = assessment.Id,
                ControlDefId = cid
            });
            _db.AssessmentControls.AddRange(controls);
            await _db.SaveChangesAsync();
        }

        await _actlog.LogAsync("INFO", "assessment", "assessment_profile.created",
            $"Assessment profile '{body.Name}' created with {body.ControlDefIds?.Count ?? 0} controls",
            entityType: "Assessment", entityId: assessment.Id.ToString());

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new { id = assessment.Id });
        return response;
    }

    [Function("AssessmentProfiles_Update")]
    [RequirePermission("assessment:edit")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v2/assessments/{id:int}")] HttpRequestData req,
        int id)
    {
        var body = await req.ReadFromJsonAsync<UpdateAssessmentRequest>();
        var assessment = await _db.Assessments.FindAsync(id);
        if (assessment is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Assessment not found" });
            return notFound;
        }

        if (body?.Name is not null) assessment.Name = body.Name;
        if (body?.Description is not null) assessment.Description = body.Description;
        if (body?.IsDefault is not null) assessment.IsDefault = body.IsDefault.Value;
        if (body?.IsActive is not null) assessment.IsActive = body.IsActive.Value;

        // Replace controls if provided
        if (body?.ControlDefIds is not null)
        {
            var existing = await _db.AssessmentControls
                .Where(ac => ac.AssessmentId == id)
                .ToListAsync();
            _db.AssessmentControls.RemoveRange(existing);

            var newControls = body.ControlDefIds.Select(cid => new AssessmentControl
            {
                AssessmentId = id,
                ControlDefId = cid
            });
            _db.AssessmentControls.AddRange(newControls);
        }

        await _db.SaveChangesAsync();

        await _actlog.LogAsync("INFO", "assessment", "assessment_profile.updated",
            $"Assessment profile '{assessment.Name}' updated",
            entityType: "Assessment", entityId: id.ToString());

        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}

public class CreateAssessmentRequest
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public List<int>? ControlDefIds { get; set; }
}

public class UpdateAssessmentRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? IsDefault { get; set; }
    public bool? IsActive { get; set; }
    public List<int>? ControlDefIds { get; set; }
}
