using System.Net.Http.Json;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CloudAssessment;

public interface IAlertService
{
    Task EvaluateAndFireAsync(Guid scanId, CancellationToken ct = default);
}

public class AlertService : IAlertService
{
    private readonly KryossDbContext _db;
    private readonly ILogger<AlertService> _log;
    private readonly IHttpClientFactory _httpFactory;

    public AlertService(KryossDbContext db, ILogger<AlertService> log, IHttpClientFactory httpFactory)
    {
        _db = db;
        _log = log;
        _httpFactory = httpFactory;
    }

    public async Task EvaluateAndFireAsync(Guid scanId, CancellationToken ct = default)
    {
        var scan = await _db.CloudAssessmentScans
            .Where(s => s.Id == scanId && s.Status == "completed")
            .Select(s => new ScanSnapshot
            {
                Id = s.Id, OrganizationId = s.OrganizationId,
                OverallScore = s.OverallScore, AreaScores = s.AreaScores
            })
            .FirstOrDefaultAsync(ct);

        if (scan is null) return;

        var org = await _db.Organizations
            .Where(o => o.Id == scan.OrganizationId)
            .Select(o => new { o.Id, o.FranchiseId, o.Name })
            .FirstOrDefaultAsync(ct);

        if (org is null) return;

        var rules = await _db.CloudAssessmentAlertRules
            .Where(r => r.FranchiseId == org.FranchiseId && r.IsEnabled)
            .ToListAsync(ct);

        if (rules.Count == 0) return;

        var previousScan = await _db.CloudAssessmentScans
            .Where(s => s.OrganizationId == scan.OrganizationId
                        && s.Status == "completed"
                        && s.Id != scan.Id)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new ScanSnapshot
            {
                Id = s.Id, OrganizationId = s.OrganizationId,
                OverallScore = s.OverallScore, AreaScores = s.AreaScores
            })
            .FirstOrDefaultAsync(ct);

        var currentFindings = await _db.CloudAssessmentFindings
            .Where(f => f.ScanId == scanId)
            .Select(f => new FindingKey { Area = f.Area, Service = f.Service, Feature = f.Feature, Status = f.Status, Priority = f.Priority })
            .ToListAsync(ct);

        HashSet<string>? previousFindingKeys = null;
        if (previousScan is not null)
        {
            var prevKeys = await _db.CloudAssessmentFindings
                .Where(f => f.ScanId == previousScan.Id)
                .Select(f => f.Area + "|" + f.Service + "|" + f.Feature)
                .ToListAsync(ct);
            previousFindingKeys = new HashSet<string>(prevKeys, StringComparer.OrdinalIgnoreCase);
        }

        var currentFrameworkScores = await _db.CloudAssessmentFrameworkScores
            .Where(s => s.ScanId == scanId)
            .Include(s => s.Framework)
            .Select(s => new FrameworkScoreSnapshot { Code = s.Framework.Code, CompliancePct = s.ScorePct })
            .ToListAsync(ct);

        var alerts = new List<CloudAssessmentAlertSent>();

        foreach (var rule in rules)
        {
            var triggered = rule.RuleType switch
            {
                "score_drop" => EvalScoreDrop(scan.OverallScore, previousScan?.OverallScore, rule.Threshold ?? 0.5m),
                "new_critical" => EvalNewFindings(currentFindings, previousFindingKeys, "Critical"),
                "new_high_regulated" => EvalNewFindings(currentFindings, previousFindingKeys, "High"),
                "framework_below" => EvalFrameworkBelow(currentFrameworkScores, rule.FrameworkCode, rule.Threshold ?? 70m),
                _ => false
            };

            if (!triggered) continue;

            var summary = BuildSummary(rule, org.Name, scan.OverallScore, previousScan?.OverallScore);
            var severity = rule.RuleType is "new_critical" or "score_drop" ? "high" : "medium";

            alerts.Add(new CloudAssessmentAlertSent
            {
                ScanId = scanId,
                RuleId = rule.Id,
                OrganizationId = org.Id,
                Severity = severity,
                RuleType = rule.RuleType,
                Summary = summary,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    orgName = org.Name,
                    scanId,
                    overallScore = scan.OverallScore,
                    previousScore = previousScan?.OverallScore,
                    ruleType = rule.RuleType,
                    threshold = rule.Threshold
                }),
                DeliveryStatus = "pending",
                FiredAt = DateTime.UtcNow
            });
        }

        if (alerts.Count == 0) return;

        _db.CloudAssessmentAlertsSent.AddRange(alerts);
        await _db.SaveChangesAsync(ct);

        foreach (var alert in alerts)
        {
            var rule = rules.First(r => r.Id == alert.RuleId);
            await DeliverAsync(alert, rule, ct);
        }

        await _db.SaveChangesAsync(ct);

        _db.Actlog.Add(new Actlog
        {
            Timestamp = DateTime.UtcNow,
            Severity = "info",
            Module = "cloud-assessment",
            Action = "alerts.evaluated",
            EntityType = "CloudAssessmentScan",
            EntityId = scanId.ToString(),
            Message = $"Evaluated {rules.Count} rules, fired {alerts.Count} alerts for org {org.Name}"
        });
        await _db.SaveChangesAsync(ct);
    }

    private static bool EvalScoreDrop(decimal? current, decimal? previous, decimal threshold)
    {
        if (current is null || previous is null) return false;
        return (previous.Value - current.Value) >= threshold;
    }

    private static bool EvalNewFindings(
        List<FindingKey> findings, HashSet<string>? previousKeys, string targetPriority)
    {
        if (previousKeys is null) return false;
        foreach (var f in findings)
        {
            if (!string.Equals(f.Priority, targetPriority, StringComparison.OrdinalIgnoreCase)) continue;
            var key = $"{f.Area}|{f.Service}|{f.Feature}";
            if (!previousKeys.Contains(key)) return true;
        }
        return false;
    }

    private static bool EvalFrameworkBelow(
        List<FrameworkScoreSnapshot> scores, string? code, decimal threshold)
    {
        if (string.IsNullOrEmpty(code)) return false;
        var match = scores.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
        return match is not null && match.CompliancePct < threshold;
    }

    private static string BuildSummary(CloudAssessmentAlertRule rule, string orgName, decimal? current, decimal? previous) =>
        rule.RuleType switch
        {
            "score_drop" => $"[{orgName}] Cloud Assessment score dropped from {previous:F2} to {current:F2} (threshold: {rule.Threshold:F2})",
            "new_critical" => $"[{orgName}] New critical finding detected in Cloud Assessment",
            "new_high_regulated" => $"[{orgName}] New high-priority finding in regulated framework",
            "framework_below" => $"[{orgName}] Framework {rule.FrameworkCode} compliance below {rule.Threshold}%",
            _ => $"[{orgName}] Cloud Assessment alert triggered ({rule.RuleType})"
        };

    private async Task DeliverAsync(CloudAssessmentAlertSent alert, CloudAssessmentAlertRule rule, CancellationToken ct)
    {
        try
        {
            if (rule.DeliveryChannel is "webhook" or "both" && !string.IsNullOrEmpty(rule.WebhookUrl))
            {
                using var http = _httpFactory.CreateClient("AlertWebhook");
                http.Timeout = TimeSpan.FromSeconds(10);
                var response = await http.PostAsJsonAsync(rule.WebhookUrl, new
                {
                    alert.Severity, alert.RuleType, alert.Summary,
                    alert.ScanId, alert.OrganizationId, alert.FiredAt
                }, ct);
                response.EnsureSuccessStatusCode();
            }

            if (rule.DeliveryChannel is "email" or "both" && !string.IsNullOrEmpty(rule.TargetEmail))
            {
                _log.LogInformation("Alert email queued for {Email}: {Summary}", rule.TargetEmail, alert.Summary);
                // TODO: SendGrid integration when API key configured
            }

            alert.DeliveryStatus = "sent";
            alert.DeliveredAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Alert delivery failed for rule {RuleId}", rule.Id);
            alert.DeliveryStatus = "failed";
            alert.ErrorMessage = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
        }
    }

    private sealed class ScanSnapshot
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public decimal? OverallScore { get; set; }
        public string? AreaScores { get; set; }
    }

    private sealed class FindingKey
    {
        public string Area { get; set; } = null!;
        public string Service { get; set; } = null!;
        public string Feature { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string Priority { get; set; } = null!;
    }

    private sealed class FrameworkScoreSnapshot
    {
        public string Code { get; set; } = null!;
        public decimal CompliancePct { get; set; }
    }
}
