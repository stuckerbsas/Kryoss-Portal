using System.Net;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class AlertFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public AlertFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    [Function("Alert_ListRules")]
    [RequirePermission("admin:read")]
    public async Task<HttpResponseData> ListRules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/alert-rules")] HttpRequestData req)
    {
        if (!_user.FranchiseId.HasValue)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "franchise context required" });
            return forbidden;
        }

        var rules = await _db.CloudAssessmentAlertRules
            .Where(r => r.FranchiseId == _user.FranchiseId.Value)
            .OrderBy(r => r.RuleType)
            .Select(r => new
            {
                r.Id, r.RuleType, r.Threshold, r.FrameworkCode,
                r.IsEnabled, r.DeliveryChannel, r.TargetEmail, r.WebhookUrl,
                r.CreatedAt, r.UpdatedAt
            })
            .ToListAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(rules);
        return res;
    }

    [Function("Alert_CreateRule")]
    [RequirePermission("admin:write")]
    public async Task<HttpResponseData> CreateRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/alert-rules")] HttpRequestData req)
    {
        if (!_user.FranchiseId.HasValue)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "franchise context required" });
            return forbidden;
        }

        var body = await req.ReadFromJsonAsync<AlertRuleRequest>();
        if (body is null || string.IsNullOrEmpty(body.RuleType))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "ruleType is required" });
            return bad;
        }

        var validTypes = new[] { "score_drop", "new_critical", "new_high_regulated", "framework_below" };
        if (!validTypes.Contains(body.RuleType))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = $"Invalid ruleType. Valid: {string.Join(", ", validTypes)}" });
            return bad;
        }

        var rule = new CloudAssessmentAlertRule
        {
            Id = Guid.NewGuid(),
            FranchiseId = _user.FranchiseId.Value,
            RuleType = body.RuleType,
            Threshold = body.Threshold,
            FrameworkCode = body.FrameworkCode,
            IsEnabled = body.IsEnabled ?? true,
            DeliveryChannel = body.DeliveryChannel ?? "email",
            TargetEmail = body.TargetEmail,
            WebhookUrl = body.WebhookUrl,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.CloudAssessmentAlertRules.Add(rule);
        await _db.SaveChangesAsync();

        var res = req.CreateResponse(HttpStatusCode.Created);
        await res.WriteAsJsonAsync(new { rule.Id, rule.RuleType, rule.Threshold, rule.IsEnabled });
        return res;
    }

    [Function("Alert_UpdateRule")]
    [RequirePermission("admin:write")]
    public async Task<HttpResponseData> UpdateRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/alert-rules/{ruleId}")] HttpRequestData req,
        string ruleId)
    {
        if (!Guid.TryParse(ruleId, out var id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "invalid ruleId" });
            return bad;
        }

        var rule = await _db.CloudAssessmentAlertRules.FindAsync(id);
        if (rule is null || (_user.FranchiseId.HasValue && rule.FranchiseId != _user.FranchiseId.Value))
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            await nf.WriteAsJsonAsync(new { error = "rule not found" });
            return nf;
        }

        var body = await req.ReadFromJsonAsync<AlertRuleRequest>();
        if (body is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            return bad;
        }

        if (body.Threshold.HasValue) rule.Threshold = body.Threshold.Value;
        if (body.FrameworkCode is not null) rule.FrameworkCode = body.FrameworkCode;
        if (body.IsEnabled.HasValue) rule.IsEnabled = body.IsEnabled.Value;
        if (body.DeliveryChannel is not null) rule.DeliveryChannel = body.DeliveryChannel;
        if (body.TargetEmail is not null) rule.TargetEmail = body.TargetEmail;
        if (body.WebhookUrl is not null) rule.WebhookUrl = body.WebhookUrl;
        rule.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { rule.Id, rule.RuleType, rule.Threshold, rule.IsEnabled, rule.UpdatedAt });
        return res;
    }

    [Function("Alert_DeleteRule")]
    [RequirePermission("admin:write")]
    public async Task<HttpResponseData> DeleteRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v2/alert-rules/{ruleId}")] HttpRequestData req,
        string ruleId)
    {
        if (!Guid.TryParse(ruleId, out var id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            return bad;
        }

        var rule = await _db.CloudAssessmentAlertRules.FindAsync(id);
        if (rule is null || (_user.FranchiseId.HasValue && rule.FranchiseId != _user.FranchiseId.Value))
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }

        _db.CloudAssessmentAlertRules.Remove(rule);
        await _db.SaveChangesAsync();

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("Alert_History")]
    [RequirePermission("admin:read")]
    public async Task<HttpResponseData> History(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/alerts/history")] HttpRequestData req)
    {
        var orgIdStr = req.Query["organizationId"];
        Guid? orgId = Guid.TryParse(orgIdStr, out var oid) ? oid : null;

        var query = _db.CloudAssessmentAlertsSent.AsQueryable();

        if (orgId.HasValue)
            query = query.Where(a => a.OrganizationId == orgId.Value);
        else if (_user.FranchiseId.HasValue)
            query = query.Where(a => a.Organization.FranchiseId == _user.FranchiseId.Value);

        var alerts = await query
            .OrderByDescending(a => a.FiredAt)
            .Take(100)
            .Select(a => new
            {
                a.Id, a.ScanId, a.OrganizationId, a.Severity, a.RuleType,
                a.Summary, a.DeliveryStatus, a.FiredAt, a.DeliveredAt
            })
            .ToListAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(alerts);
        return res;
    }

    [Function("Alert_Test")]
    [RequirePermission("admin:write")]
    public async Task<HttpResponseData> TestRule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/alert-rules/{ruleId}/test")] HttpRequestData req,
        string ruleId)
    {
        if (!Guid.TryParse(ruleId, out var id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            return bad;
        }

        var rule = await _db.CloudAssessmentAlertRules.FindAsync(id);
        if (rule is null || (_user.FranchiseId.HasValue && rule.FranchiseId != _user.FranchiseId.Value))
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new
        {
            message = $"Test alert would fire to {rule.DeliveryChannel}: {rule.TargetEmail ?? rule.WebhookUrl}",
            rule.RuleType,
            rule.Threshold,
            rule.DeliveryChannel
        });
        return res;
    }
}

public class AlertRuleRequest
{
    public string? RuleType { get; set; }
    public decimal? Threshold { get; set; }
    public string? FrameworkCode { get; set; }
    public bool? IsEnabled { get; set; }
    public string? DeliveryChannel { get; set; }
    public string? TargetEmail { get; set; }
    public string? WebhookUrl { get; set; }
}
