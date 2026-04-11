using System.Text;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface IReportService
{
    Task<string> GenerateHtmlReportAsync(Guid runId, string reportType = "technical", string? frameworkCode = null);
    Task<string> GenerateOrgReportAsync(Guid orgId, string reportType = "executive", string? frameworkCode = null);
}

/// <summary>
/// Generates branded HTML assessment reports.
/// Supports: executive (summary), technical (full detail), presales (highlight risks).
/// White-label: uses franchise branding (logo, colors, name).
/// </summary>
public class ReportService : IReportService
{
    private readonly KryossDbContext _db;

    public ReportService(KryossDbContext db)
    {
        _db = db;
    }

    public async Task<string> GenerateHtmlReportAsync(Guid runId, string reportType = "technical", string? frameworkCode = null)
    {
        var run = await _db.AssessmentRuns
            .Include(r => r.Machine)
            .Include(r => r.Organization)
                .ThenInclude(o => o.Franchise)
            .Include(r => r.Organization)
                .ThenInclude(o => o.Brand)
            .FirstOrDefaultAsync(r => r.Id == runId)
            ?? throw new InvalidOperationException($"Run {runId} not found");

        var results = await _db.ControlResults
            .Where(cr => cr.RunId == runId)
            .Join(_db.ControlDefs.Include(cd => cd.Category), cr => cr.ControlDefId, cd => cd.Id,
                (cr, cd) => new ReportControlResult
                {
                    ControlDefId = cd.Id,
                    ControlId = cd.ControlId,
                    Name = cd.Name,
                    Category = cd.Category.Name,
                    Type = cd.Type,
                    Severity = cd.Severity ?? "medium",
                    Status = cr.Status,
                    Score = cr.Score,
                    MaxScore = cr.MaxScore,
                    Finding = cr.Finding,
                    ActualValue = cr.ActualValue,
                    Remediation = cd.Remediation
                })
            .OrderBy(r => r.Category)
            .ThenBy(r => r.ControlId)
            .ToListAsync();

        // Framework filter
        string? frameworkName = null;
        if (!string.IsNullOrEmpty(frameworkCode))
        {
            var framework = await _db.Frameworks
                .FirstOrDefaultAsync(f => f.Code == frameworkCode && f.IsActive);

            if (framework != null)
            {
                frameworkName = framework.Name;
                var controlDefIdsInFramework = await _db.ControlFrameworks
                    .Where(cf => cf.FrameworkId == framework.Id)
                    .Select(cf => cf.ControlDefId)
                    .ToListAsync();
                var controlDefIdsSet = new HashSet<int>(controlDefIdsInFramework);

                results = results.Where(r => controlDefIdsSet.Contains(r.ControlDefId)).ToList();
            }
        }

        var brand = run.Organization.Brand;
        var franchise = run.Organization.Franchise;
        var branding = new ReportBranding
        {
            CompanyName = brand?.Name ?? franchise.BrandName ?? franchise.Name,
            PrimaryColor = brand?.ColorPrimary ?? franchise.BrandColorPrimary ?? "#006536",
            AccentColor = brand?.ColorAccent ?? franchise.BrandColorAccent ?? "#A2C564",
            LogoUrl = brand?.LogoUrl ?? franchise.BrandLogoUrl
        };

        return reportType switch
        {
            "executive" => BuildExecutiveReport(run, results, branding, frameworkName),
            "presales" => BuildPresalesReport(run, results, branding, frameworkName),
            _ => BuildTechnicalReport(run, results, branding, frameworkName)
        };
    }

    private static string BuildTechnicalReport(AssessmentRun run, List<ReportControlResult> results, ReportBranding brand, string? frameworkName = null)
    {
        var reportTitle = frameworkName != null ? $"{frameworkName} Compliance Report" : "Security Assessment Report";
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='en'><head><meta charset='UTF-8'>");
        sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.AppendLine($"<title>{HtmlEncode(reportTitle)} - {HtmlEncode(run.Machine.Hostname)}</title>");
        sb.AppendLine("<link href='https://fonts.googleapis.com/css2?family=Montserrat:wght@300;400;500;600;700;900&display=swap' rel='stylesheet'>");
        sb.AppendLine("<style>");
        sb.AppendLine(GetReportStyles(brand));
        sb.AppendLine("</style></head><body>");

        // Cover page
        sb.AppendLine($"<div class='cover'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(run.Machine.Hostname)}</h2>");
        sb.AppendLine($"<p class='meta'>{HtmlEncode(run.Organization.Name)}</p>");
        sb.AppendLine($"<p class='meta'>{run.StartedAt:MMMM dd, yyyy}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{run.Grade?.Replace("+", "plus")}'>{HtmlEncode(run.Grade ?? "N/A")}</div>");
        sb.AppendLine($"<p class='score'>{run.GlobalScore}%</p>");
        sb.AppendLine("</div>");

        // Summary
        sb.AppendLine("<div class='section'>");
        sb.AppendLine("<h2>Executive Summary</h2>");
        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{run.PassCount}</span><span class='stat-label'>Passed</span></div>");
        sb.AppendLine($"<div class='stat warn'><span class='stat-value'>{run.WarnCount}</span><span class='stat-label'>Warnings</span></div>");
        sb.AppendLine($"<div class='stat fail'><span class='stat-value'>{run.FailCount}</span><span class='stat-label'>Failed</span></div>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{run.EarnedPoints}/{run.TotalPoints}</span><span class='stat-label'>Points</span></div>");
        sb.AppendLine("</div>");

        // System info
        sb.AppendLine("<h3>System Information</h3>");
        sb.AppendLine("<table class='info-table'>");
        sb.AppendLine($"<tr><td>Hostname</td><td>{HtmlEncode(run.Machine.Hostname)}</td></tr>");
        sb.AppendLine($"<tr><td>OS</td><td>{HtmlEncode(run.Machine.OsName ?? "N/A")} {HtmlEncode(run.Machine.OsVersion ?? "")}</td></tr>");
        sb.AppendLine($"<tr><td>CPU</td><td>{HtmlEncode(run.Machine.CpuName ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>RAM</td><td>{run.Machine.RamGb ?? 0} GB</td></tr>");
        sb.AppendLine($"<tr><td>Agent Version</td><td>{HtmlEncode(run.AgentVersion ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>Duration</td><td>{(run.DurationMs ?? 0) / 1000.0:F1}s</td></tr>");
        sb.AppendLine("</table></div>");

        // Results by category
        var grouped = results.GroupBy(r => r.Category).OrderBy(g => g.Key);
        foreach (var category in grouped)
        {
            var catPass = category.Count(r => r.Status == "pass");
            var catTotal = category.Count();
            sb.AppendLine("<div class='section'>");
            sb.AppendLine($"<h2>{HtmlEncode(category.Key)} <span class='cat-score'>({catPass}/{catTotal})</span></h2>");
            sb.AppendLine("<table class='results-table'>");
            sb.AppendLine("<tr><th>ID</th><th>Control</th><th>Severity</th><th>Status</th><th>Finding</th></tr>");

            foreach (var r in category)
            {
                var statusClass = r.Status switch { "pass" => "pass", "warn" => "warn", _ => "fail" };
                sb.AppendLine($"<tr class='{statusClass}'>");
                sb.AppendLine($"<td>{HtmlEncode(r.ControlId)}</td>");
                sb.AppendLine($"<td>{HtmlEncode(r.Name)}</td>");
                sb.AppendLine($"<td><span class='severity {HtmlEncode(r.Severity)}'>{HtmlEncode(r.Severity)}</span></td>");
                sb.AppendLine($"<td><span class='status-badge {statusClass}'>{HtmlEncode(r.Status.ToUpperInvariant())}</span></td>");
                sb.AppendLine($"<td>{HtmlEncode(r.Finding ?? "—")}</td>");
                sb.AppendLine("</tr>");

                if (r.Status == "fail" && r.Remediation is not null)
                {
                    sb.AppendLine($"<tr class='remediation-row'><td colspan='5'>");
                    sb.AppendLine($"<strong>Remediation:</strong> {HtmlEncode(r.Remediation)}");
                    sb.AppendLine("</td></tr>");
                }
            }
            sb.AppendLine("</table></div>");
        }

        // Footer
        sb.AppendLine($"<div class='footer'>");
        sb.AppendLine($"<p>Generated by {HtmlEncode(brand.CompanyName)} &mdash; Your Technology Advisor</p>");
        sb.AppendLine($"<p>Report ID: {run.Id} &bull; {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private static string BuildExecutiveReport(AssessmentRun run, List<ReportControlResult> results, ReportBranding brand, string? frameworkName = null)
    {
        // Executive report: only summary + critical/high failures + grade
        var reportTitle = frameworkName != null ? $"{frameworkName} Executive Summary" : "Security Assessment";
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='en'><head><meta charset='UTF-8'>");
        sb.AppendLine($"<title>{HtmlEncode(reportTitle)} - {HtmlEncode(run.Machine.Hostname)}</title>");
        sb.AppendLine("<link href='https://fonts.googleapis.com/css2?family=Montserrat:wght@300;400;500;600;700;900&display=swap' rel='stylesheet'>");
        sb.AppendLine($"<style>{GetReportStyles(brand)}</style></head><body>");

        sb.AppendLine($"<div class='cover'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>Executive Summary</h2>");
        sb.AppendLine($"<p class='meta'>{HtmlEncode(run.Organization.Name)} &mdash; {run.StartedAt:MMMM dd, yyyy}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{run.Grade?.Replace("+", "plus")}'>{HtmlEncode(run.Grade ?? "N/A")}</div>");
        sb.AppendLine($"<p class='score'>{run.GlobalScore}% Compliance</p>");
        sb.AppendLine("</div>");

        // Key metrics
        sb.AppendLine("<div class='section'><h2>Key Findings</h2>");
        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{run.PassCount}</span><span class='stat-label'>Controls Passed</span></div>");
        sb.AppendLine($"<div class='stat fail'><span class='stat-value'>{run.FailCount}</span><span class='stat-label'>Controls Failed</span></div>");
        sb.AppendLine("</div>");

        // Critical/high failures only
        var criticalFailures = results.Where(r => r.Status == "fail" && (r.Severity == "critical" || r.Severity == "high")).ToList();
        if (criticalFailures.Count > 0)
        {
            sb.AppendLine("<h3>Critical & High Risk Issues</h3>");
            sb.AppendLine("<table class='results-table'>");
            sb.AppendLine("<tr><th>Control</th><th>Severity</th><th>Finding</th></tr>");
            foreach (var r in criticalFailures)
            {
                sb.AppendLine($"<tr class='fail'><td>{HtmlEncode(r.Name)}</td>");
                sb.AppendLine($"<td><span class='severity {r.Severity}'>{HtmlEncode(r.Severity)}</span></td>");
                sb.AppendLine($"<td>{HtmlEncode(r.Finding ?? "—")}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine($"<div class='footer'><p>{HtmlEncode(brand.CompanyName)} &mdash; Your Technology Advisor</p></div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string BuildPresalesReport(AssessmentRun run, List<ReportControlResult> results, ReportBranding brand, string? frameworkName = null)
    {
        // Presales: score + grade prominent, top risks, recommendation to remediate
        var reportTitle = frameworkName != null ? $"{frameworkName} Posture Assessment" : "Security Posture Assessment";
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='en'><head><meta charset='UTF-8'>");
        sb.AppendLine($"<title>{HtmlEncode(reportTitle)} - {HtmlEncode(run.Machine.Hostname)}</title>");
        sb.AppendLine("<link href='https://fonts.googleapis.com/css2?family=Montserrat:wght@300;400;500;600;700;900&display=swap' rel='stylesheet'>");
        sb.AppendLine($"<style>{GetReportStyles(brand)}</style></head><body>");

        sb.AppendLine($"<div class='cover'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(run.Organization.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{HtmlEncode(run.Machine.Hostname)} &mdash; {run.StartedAt:MMMM dd, yyyy}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{run.Grade?.Replace("+", "plus")}'>{HtmlEncode(run.Grade ?? "N/A")}</div>");
        sb.AppendLine($"<p class='score'>{run.GlobalScore}%</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='section'><h2>Assessment Results</h2>");
        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{run.PassCount}</span><span class='stat-label'>Passed</span></div>");
        sb.AppendLine($"<div class='stat warn'><span class='stat-value'>{run.WarnCount}</span><span class='stat-label'>Warnings</span></div>");
        sb.AppendLine($"<div class='stat fail'><span class='stat-value'>{run.FailCount}</span><span class='stat-label'>Failed</span></div>");
        sb.AppendLine("</div>");

        // Category breakdown
        sb.AppendLine("<h3>By Category</h3>");
        sb.AppendLine("<table class='results-table'>");
        sb.AppendLine("<tr><th>Category</th><th>Pass</th><th>Fail</th><th>Score</th></tr>");
        foreach (var cat in results.GroupBy(r => r.Category).OrderBy(g => g.Key))
        {
            var catPass = cat.Count(r => r.Status == "pass");
            var catTotal = cat.Count();
            var pct = catTotal > 0 ? Math.Round((double)catPass / catTotal * 100) : 0;
            sb.AppendLine($"<tr><td>{HtmlEncode(cat.Key)}</td><td>{catPass}</td><td>{catTotal - catPass}</td><td>{pct}%</td></tr>");
        }
        sb.AppendLine("</table>");

        // Top 5 risks
        var topRisks = results.Where(r => r.Status == "fail")
            .OrderByDescending(r => r.Severity == "critical" ? 4 : r.Severity == "high" ? 3 : r.Severity == "medium" ? 2 : 1)
            .Take(5).ToList();
        if (topRisks.Count > 0)
        {
            sb.AppendLine("<h3>Top Risks</h3>");
            sb.AppendLine("<ol>");
            foreach (var r in topRisks)
                sb.AppendLine($"<li><strong>{HtmlEncode(r.Name)}</strong> ({r.Severity})</li>");
            sb.AppendLine("</ol>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine($"<div class='footer'><p>{HtmlEncode(brand.CompanyName)} &mdash; Your Technology Advisor</p></div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    // ==========================================================================
    // ORG-LEVEL CONSOLIDATED REPORT
    // ==========================================================================

    public async Task<string> GenerateOrgReportAsync(Guid orgId, string reportType = "executive", string? frameworkCode = null)
    {
        var org = await _db.Organizations
            .Include(o => o.Franchise)
            .Include(o => o.Brand)
            .FirstOrDefaultAsync(o => o.Id == orgId)
            ?? throw new InvalidOperationException($"Organization {orgId} not found");

        // Get the latest run per machine (using a subquery to pick max CompletedAt per machine)
        var latestRunIds = await _db.AssessmentRuns
            .Where(r => r.OrganizationId == orgId && r.CompletedAt != null)
            .GroupBy(r => r.MachineId)
            .Select(g => g.OrderByDescending(r => r.CompletedAt).First().Id)
            .ToListAsync();

        if (latestRunIds.Count == 0)
            throw new InvalidOperationException($"No completed assessment runs found for organization {orgId}");

        var runs = await _db.AssessmentRuns
            .Include(r => r.Machine)
            .Where(r => latestRunIds.Contains(r.Id))
            .OrderBy(r => r.Machine.Hostname)
            .ToListAsync();

        // Aggregate control results across all latest runs for the detail view
        var allResults = await _db.ControlResults
            .Where(cr => latestRunIds.Contains(cr.RunId))
            .Join(_db.ControlDefs.Include(cd => cd.Category), cr => cr.ControlDefId, cd => cd.Id,
                (cr, cd) => new OrgControlResult
                {
                    ControlDefId = cd.Id,
                    RunId = cr.RunId,
                    ControlId = cd.ControlId,
                    Name = cd.Name,
                    Category = cd.Category.Name,
                    Severity = cd.Severity ?? "medium",
                    Status = cr.Status,
                    Finding = cr.Finding,
                    Remediation = cd.Remediation
                })
            .ToListAsync();

        // Framework filter
        string? frameworkName = null;
        if (!string.IsNullOrEmpty(frameworkCode))
        {
            var framework = await _db.Frameworks
                .FirstOrDefaultAsync(f => f.Code == frameworkCode && f.IsActive);

            if (framework != null)
            {
                frameworkName = framework.Name;
                var controlDefIdsInFramework = await _db.ControlFrameworks
                    .Where(cf => cf.FrameworkId == framework.Id)
                    .Select(cf => cf.ControlDefId)
                    .ToListAsync();
                var controlDefIdsSet = new HashSet<int>(controlDefIdsInFramework);

                allResults = allResults.Where(r => controlDefIdsSet.Contains(r.ControlDefId)).ToList();
            }
        }

        var brand = org.Brand;
        var franchise = org.Franchise;
        var branding = new ReportBranding
        {
            CompanyName = brand?.Name ?? franchise.BrandName ?? franchise.Name,
            PrimaryColor = brand?.ColorPrimary ?? franchise.BrandColorPrimary ?? "#006536",
            AccentColor = brand?.ColorAccent ?? franchise.BrandColorAccent ?? "#A2C564",
            LogoUrl = brand?.LogoUrl ?? franchise.BrandLogoUrl
        };

        return reportType switch
        {
            "technical" => BuildOrgTechnicalReport(org, runs, allResults, branding, frameworkName),
            "presales" => BuildOrgPresalesReport(org, runs, allResults, branding, frameworkName),
            _ => BuildOrgExecutiveReport(org, runs, allResults, branding, frameworkName)
        };
    }

    private static string BuildOrgExecutiveReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName = null)
    {
        var reportTitle = frameworkName != null ? $"{frameworkName} Organization Report" : "Organization Report";
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        var avgScore = runs.Average(r => r.GlobalScore ?? 0);
        var orgGrade = GetGrade(avgScore);
        var totalPass = runs.Sum(r => r.PassCount ?? 0);
        var totalWarn = runs.Sum(r => r.WarnCount ?? 0);
        var totalFail = runs.Sum(r => r.FailCount ?? 0);
        var scanDate = runs.Max(r => r.CompletedAt ?? r.StartedAt);

        sb.AppendLine("<!DOCTYPE html><html lang='en'><head><meta charset='UTF-8'>");
        sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.AppendLine($"<title>{HtmlEncode(reportTitle)} - {HtmlEncode(org.Name)}</title>");
        sb.AppendLine("<link href='https://fonts.googleapis.com/css2?family=Montserrat:wght@300;400;500;600;700;900&display=swap' rel='stylesheet'>");
        sb.AppendLine($"<style>{GetOrgReportStyles(brand)}</style></head><body>");

        // Cover page
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{(frameworkName != null ? HtmlEncode(frameworkName.ToUpperInvariant()) : "SECURITY ASSESSMENT")}</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(org.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{scanDate:MMMM dd, yyyy} &mdash; {totalMachines} devices assessed</p>");
        sb.AppendLine($"<div class='grade-badge grade-{orgGrade.Replace("+", "plus")}'>{HtmlEncode(orgGrade)}</div>");
        sb.AppendLine($"<p class='score'>{avgScore:F1}%</p>");
        sb.AppendLine("</div></div>");

        // Executive Summary page
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Executive Summary", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{totalMachines}</span><span class='stat-label'>Devices Assessed</span></div>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{avgScore:F1}%</span><span class='stat-label'>Avg. Score</span></div>");
        sb.AppendLine($"<div class='stat pass-stat'><span class='stat-value'>{totalPass}</span><span class='stat-label'>Controls Passed</span></div>");
        sb.AppendLine($"<div class='stat warn-stat'><span class='stat-value'>{totalWarn}</span><span class='stat-label'>Warnings</span></div>");
        sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{totalFail}</span><span class='stat-label'>Controls Failed</span></div>");
        sb.AppendLine("</div>");

        // Grade distribution
        var gradeGroups = runs.GroupBy(r => r.Grade ?? "N/A").OrderBy(g => g.Key);
        sb.AppendLine("<h3>Grade Distribution</h3>");
        sb.AppendLine("<div class='grade-dist'>");
        foreach (var g in gradeGroups)
        {
            var pct = (double)g.Count() / totalMachines * 100;
            var gClass = g.Key.Replace("+", "plus");
            sb.AppendLine($"<div class='grade-bar'><span class='grade-label'>{HtmlEncode(g.Key)}</span>");
            sb.AppendLine($"<div class='bar-track'><div class='bar-fill grade-{gClass}' style='width:{pct:F0}%'></div></div>");
            sb.AppendLine($"<span class='grade-count'>{g.Count()}</span></div>");
        }
        sb.AppendLine("</div>");

        // Critical findings summary
        var criticalFailures = allResults
            .Where(r => r.Status == "fail" && (r.Severity == "critical" || r.Severity == "high"))
            .GroupBy(r => new { r.ControlId, r.Name, r.Severity })
            .Select(g => new { g.Key.ControlId, g.Key.Name, g.Key.Severity, AffectedCount = g.Count() })
            .OrderByDescending(x => x.Severity == "critical" ? 1 : 0)
            .ThenByDescending(x => x.AffectedCount)
            .Take(10)
            .ToList();

        if (criticalFailures.Count > 0)
        {
            sb.AppendLine("<h3>Top Critical &amp; High Risk Issues</h3>");
            sb.AppendLine("<table class='results-table'>");
            sb.AppendLine("<tr><th>ID</th><th>Control</th><th>Severity</th><th>Affected Devices</th></tr>");
            foreach (var f in criticalFailures)
            {
                sb.AppendLine($"<tr class='fail'><td>{HtmlEncode(f.ControlId)}</td>");
                sb.AppendLine($"<td>{HtmlEncode(f.Name)}</td>");
                sb.AppendLine($"<td><span class='severity {f.Severity}'>{HtmlEncode(f.Severity)}</span></td>");
                sb.AppendLine($"<td>{f.AffectedCount} / {totalMachines}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("</div></div>");

        // Fleet Overview page — device cards
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Fleet Overview", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<table class='fleet-table'>");
        sb.AppendLine("<tr><th>Hostname</th><th>OS</th><th>Score</th><th>Grade</th><th>Pass</th><th>Warn</th><th>Fail</th></tr>");
        foreach (var run in runs.OrderByDescending(r => r.GlobalScore))
        {
            var rowClass = (run.GlobalScore ?? 0) >= 80 ? "pass" : (run.GlobalScore ?? 0) >= 60 ? "warn" : "fail";
            sb.AppendLine($"<tr class='{rowClass}'>");
            sb.AppendLine($"<td class='hostname'>{HtmlEncode(run.Machine.Hostname)}</td>");
            sb.AppendLine($"<td>{HtmlEncode(run.Machine.OsName ?? "N/A")}</td>");
            sb.AppendLine($"<td><strong>{run.GlobalScore:F1}%</strong></td>");
            sb.AppendLine($"<td><span class='grade-mini grade-{run.Grade?.Replace("+", "plus")}'>{HtmlEncode(run.Grade ?? "N/A")}</span></td>");
            sb.AppendLine($"<td>{run.PassCount}</td><td>{run.WarnCount}</td><td>{run.FailCount}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
        sb.AppendLine("</div></div>");

        // Footer
        sb.AppendLine($"<div class='footer'>");
        sb.AppendLine($"<p>Generated by {HtmlEncode(brand.CompanyName)} &mdash; Your Technology Advisor</p>");
        sb.AppendLine($"<p>{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC &bull; {totalMachines} devices</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private static string BuildOrgTechnicalReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName = null)
    {
        var reportTitle = frameworkName != null ? $"{frameworkName} Technical Assessment" : "Technical Assessment";
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        var avgScore = runs.Average(r => r.GlobalScore ?? 0);
        var orgGrade = GetGrade(avgScore);
        var scanDate = runs.Max(r => r.CompletedAt ?? r.StartedAt);

        sb.AppendLine("<!DOCTYPE html><html lang='en'><head><meta charset='UTF-8'>");
        sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.AppendLine($"<title>{HtmlEncode(reportTitle)} - {HtmlEncode(org.Name)}</title>");
        sb.AppendLine("<link href='https://fonts.googleapis.com/css2?family=Montserrat:wght@300;400;500;600;700;900&display=swap' rel='stylesheet'>");
        sb.AppendLine($"<style>{GetOrgReportStyles(brand)}</style></head><body>");

        // Cover
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{(frameworkName != null ? HtmlEncode(frameworkName.ToUpperInvariant()) + " " : "")}TECHNICAL ASSESSMENT</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(org.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{scanDate:MMMM dd, yyyy} &mdash; {totalMachines} devices</p>");
        sb.AppendLine($"<div class='grade-badge grade-{orgGrade.Replace("+", "plus")}'>{HtmlEncode(orgGrade)}</div>");
        sb.AppendLine($"<p class='score'>{avgScore:F1}%</p>");
        sb.AppendLine("</div></div>");

        // Fleet Overview
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Fleet Overview", brand);
        sb.AppendLine("<div class='pb'>");
        sb.AppendLine("<table class='fleet-table'>");
        sb.AppendLine("<tr><th>Hostname</th><th>OS</th><th>CPU</th><th>RAM</th><th>Score</th><th>Grade</th></tr>");
        foreach (var run in runs.OrderByDescending(r => r.GlobalScore))
        {
            var rowClass = (run.GlobalScore ?? 0) >= 80 ? "pass" : (run.GlobalScore ?? 0) >= 60 ? "warn" : "fail";
            sb.AppendLine($"<tr class='{rowClass}'>");
            sb.AppendLine($"<td class='hostname'>{HtmlEncode(run.Machine.Hostname)}</td>");
            sb.AppendLine($"<td>{HtmlEncode(run.Machine.OsName ?? "N/A")}</td>");
            sb.AppendLine($"<td>{HtmlEncode(run.Machine.CpuName ?? "N/A")}</td>");
            sb.AppendLine($"<td>{run.Machine.RamGb ?? 0} GB</td>");
            sb.AppendLine($"<td><strong>{run.GlobalScore:F1}%</strong></td>");
            sb.AppendLine($"<td><span class='grade-mini grade-{run.Grade?.Replace("+", "plus")}'>{HtmlEncode(run.Grade ?? "N/A")}</span></td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
        sb.AppendLine("</div></div>");

        // Control details by category — aggregated across all machines
        // For each control, show how many machines pass/warn/fail
        var controlSummary = allResults
            .GroupBy(r => new { r.ControlId, r.Name, r.Category, r.Severity })
            .Select(g => new
            {
                g.Key.ControlId,
                g.Key.Name,
                g.Key.Category,
                g.Key.Severity,
                PassCount = g.Count(x => x.Status == "pass"),
                WarnCount = g.Count(x => x.Status == "warn"),
                FailCount = g.Count(x => x.Status == "fail"),
                Remediation = g.Where(x => x.Status == "fail").Select(x => x.Remediation).FirstOrDefault()
            })
            .OrderBy(x => x.Category)
            .ThenBy(x => x.ControlId)
            .ToList();

        var categories = controlSummary.GroupBy(c => c.Category).OrderBy(g => g.Key);
        foreach (var cat in categories)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, cat.Key, brand);
            sb.AppendLine("<div class='pb'>");

            var catPass = cat.Sum(c => c.PassCount);
            var catTotal = cat.Sum(c => c.PassCount + c.WarnCount + c.FailCount);
            var catPct = catTotal > 0 ? Math.Round((double)catPass / catTotal * 100) : 0;
            sb.AppendLine($"<p class='cat-summary'>Category compliance: <strong>{catPct}%</strong> ({catPass}/{catTotal} checks passed across {totalMachines} devices)</p>");

            sb.AppendLine("<table class='results-table'>");
            sb.AppendLine("<tr><th>ID</th><th>Control</th><th>Severity</th><th>Pass</th><th>Warn</th><th>Fail</th></tr>");

            foreach (var ctrl in cat)
            {
                var dominant = ctrl.FailCount > 0 ? "fail" : ctrl.WarnCount > 0 ? "warn" : "pass";
                sb.AppendLine($"<tr class='{dominant}'>");
                sb.AppendLine($"<td>{HtmlEncode(ctrl.ControlId)}</td>");
                sb.AppendLine($"<td>{HtmlEncode(ctrl.Name)}</td>");
                sb.AppendLine($"<td><span class='severity {ctrl.Severity}'>{HtmlEncode(ctrl.Severity)}</span></td>");
                sb.AppendLine($"<td class='num pass-cell'>{ctrl.PassCount}</td>");
                sb.AppendLine($"<td class='num warn-cell'>{ctrl.WarnCount}</td>");
                sb.AppendLine($"<td class='num fail-cell'>{ctrl.FailCount}</td>");
                sb.AppendLine("</tr>");

                if (ctrl.FailCount > 0 && ctrl.Remediation is not null)
                {
                    sb.AppendLine($"<tr class='remediation-row'><td colspan='6'>");
                    sb.AppendLine($"<strong>Remediation:</strong> {HtmlEncode(ctrl.Remediation)}");
                    sb.AppendLine("</td></tr>");
                }
            }
            sb.AppendLine("</table></div></div>");
        }

        // Footer
        sb.AppendLine($"<div class='footer'>");
        sb.AppendLine($"<p>Generated by {HtmlEncode(brand.CompanyName)} &mdash; Your Technology Advisor</p>");
        sb.AppendLine($"<p>{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC &bull; {totalMachines} devices</p>");
        sb.AppendLine("</div></body></html>");

        return sb.ToString();
    }

    private static string BuildOrgPresalesReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName = null)
    {
        var reportTitle = frameworkName != null ? $"{frameworkName} Security Posture" : "Security Posture Assessment";
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        var avgScore = runs.Average(r => r.GlobalScore ?? 0);
        var orgGrade = GetGrade(avgScore);
        var scanDate = runs.Max(r => r.CompletedAt ?? r.StartedAt);
        var totalFail = runs.Sum(r => r.FailCount ?? 0);

        sb.AppendLine("<!DOCTYPE html><html lang='en'><head><meta charset='UTF-8'>");
        sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.AppendLine($"<title>{HtmlEncode(reportTitle)} - {HtmlEncode(org.Name)}</title>");
        sb.AppendLine("<link href='https://fonts.googleapis.com/css2?family=Montserrat:wght@300;400;500;600;700;900&display=swap' rel='stylesheet'>");
        sb.AppendLine($"<style>{GetOrgReportStyles(brand)}</style></head><body>");

        // Cover
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{(frameworkName != null ? HtmlEncode(frameworkName.ToUpperInvariant()) + " " : "")}SECURITY POSTURE</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(org.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{scanDate:MMMM dd, yyyy}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{orgGrade.Replace("+", "plus")}'>{HtmlEncode(orgGrade)}</div>");
        sb.AppendLine($"<p class='score'>{avgScore:F1}%</p>");
        sb.AppendLine("</div></div>");

        // Key numbers
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Assessment Results", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{totalMachines}</span><span class='stat-label'>Devices Assessed</span></div>");
        sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{totalFail}</span><span class='stat-label'>Total Failures</span></div>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{avgScore:F1}%</span><span class='stat-label'>Average Compliance</span></div>");
        sb.AppendLine("</div>");

        // Category breakdown
        var catBreakdown = allResults
            .GroupBy(r => r.Category)
            .Select(g => new
            {
                Category = g.Key,
                Pass = g.Count(x => x.Status == "pass"),
                Total = g.Count(),
                Pct = g.Count() > 0 ? Math.Round((double)g.Count(x => x.Status == "pass") / g.Count() * 100) : 0
            })
            .OrderBy(x => x.Pct)
            .ToList();

        sb.AppendLine("<h3>Compliance by Category</h3>");
        sb.AppendLine("<table class='results-table'>");
        sb.AppendLine("<tr><th>Category</th><th>Pass</th><th>Fail</th><th>Compliance</th></tr>");
        foreach (var cat in catBreakdown)
        {
            var rowClass = cat.Pct >= 80 ? "pass" : cat.Pct >= 60 ? "warn" : "fail";
            sb.AppendLine($"<tr class='{rowClass}'><td>{HtmlEncode(cat.Category)}</td>");
            sb.AppendLine($"<td>{cat.Pass}</td><td>{cat.Total - cat.Pass}</td>");
            sb.AppendLine($"<td><strong>{cat.Pct}%</strong></td></tr>");
        }
        sb.AppendLine("</table>");

        // Top 10 risks
        var topRisks = allResults
            .Where(r => r.Status == "fail")
            .GroupBy(r => new { r.ControlId, r.Name, r.Severity })
            .Select(g => new { g.Key.Name, g.Key.Severity, Count = g.Count() })
            .OrderByDescending(x => x.Severity == "critical" ? 4 : x.Severity == "high" ? 3 : x.Severity == "medium" ? 2 : 1)
            .ThenByDescending(x => x.Count)
            .Take(10)
            .ToList();

        if (topRisks.Count > 0)
        {
            sb.AppendLine("<h3>Top Risks Across Your Environment</h3>");
            sb.AppendLine("<ol class='risk-list'>");
            foreach (var r in topRisks)
                sb.AppendLine($"<li><strong>{HtmlEncode(r.Name)}</strong> <span class='severity {r.Severity}'>{r.Severity}</span> &mdash; {r.Count}/{totalMachines} devices</li>");
            sb.AppendLine("</ol>");
        }

        sb.AppendLine("</div></div>");

        sb.AppendLine($"<div class='footer'>");
        sb.AppendLine($"<p>{HtmlEncode(brand.CompanyName)} &mdash; Your Technology Advisor</p>");
        sb.AppendLine($"<p>Contact us to discuss remediation and managed security services.</p>");
        sb.AppendLine("</div></body></html>");

        return sb.ToString();
    }

    // Helpers for org reports
    private static string GetGrade(decimal score) => score switch
    {
        >= 97 => "A+",
        >= 93 => "A",
        >= 90 => "A-",
        >= 87 => "B+",
        >= 83 => "B",
        >= 80 => "B-",
        >= 77 => "C+",
        >= 73 => "C",
        >= 70 => "C-",
        >= 67 => "D+",
        >= 63 => "D",
        >= 60 => "D-",
        _ => "F"
    };

    private static void AppendRibbonSvg(StringBuilder sb)
    {
        sb.AppendLine(@"<svg class='cover-ribbon' viewBox='0 0 360 900' xmlns='http://www.w3.org/2000/svg'>
            <rect fill='#006536' x='280' y='-100' width='45' height='1200' transform='rotate(-35 300 450)'/>
            <rect fill='#2BB673' x='320' y='-100' width='45' height='1200' transform='rotate(-35 340 450)'/>
            <rect fill='#39B54A' x='360' y='-100' width='45' height='1200' transform='rotate(-35 380 450)'/>
            <rect fill='#8DC63F' x='400' y='-100' width='45' height='1200' transform='rotate(-35 420 450)'/>
            <rect fill='#B2D235' x='440' y='-100' width='45' height='1200' transform='rotate(-35 460 450)'/>
            <rect fill='#D3E173' x='480' y='-100' width='45' height='1200' transform='rotate(-35 500 450)'/>
        </svg>");
    }

    private static void AppendPageHeader(StringBuilder sb, string title, ReportBranding brand)
    {
        sb.AppendLine("<div class='ph'>");
        sb.AppendLine($"<h1>{HtmlEncode(title)}</h1>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' alt=''>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='stripe'></div>");
    }

    private static string GetOrgReportStyles(ReportBranding brand) => $$"""
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: 'Montserrat', 'Verdana', sans-serif; color: #333; font-size: 13px; line-height: 1.6;
               -webkit-print-color-adjust: exact; print-color-adjust: exact; background: #ECEAE4; }

        /* Cover */
        .cover { width: 210mm; min-height: 297mm; margin: 0 auto 20px; background: #3D4043; position: relative;
                 overflow: hidden; display: flex; align-items: flex-end; page-break-after: always; }
        .cover-ribbon { position: absolute; bottom: 0; right: 0; width: 50%; height: 100%; pointer-events: none; }
        .cover-content { padding: 44px; position: relative; z-index: 2; color: #fff; }
        .cover .logo { height: 50px; margin-bottom: 30px; display: block; }
        .eyebrow { font-size: 9px; font-weight: 700; letter-spacing: 0.2em; text-transform: uppercase; color: {{brand.AccentColor}}; margin-bottom: 12px; }
        .cover h1 { font-size: 34px; font-weight: 700; line-height: 1.1; margin-bottom: 8px; }
        .cover h2 { font-size: 20px; font-weight: 400; color: {{brand.AccentColor}}; margin-bottom: 8px; }
        .cover .meta { font-size: 12px; opacity: 0.6; margin-bottom: 20px; }
        .cover .score { font-size: 36px; font-weight: 900; margin-top: 8px; }

        /* Grade badge */
        .grade-badge { display: inline-block; font-size: 48px; font-weight: 900; padding: 12px 28px;
                       border-radius: 10px; margin: 16px 0 4px; color: #fff; }
        .grade-Aplus, .grade-A, .grade-A- { background: {{brand.PrimaryColor}}; }
        .grade-Bplus, .grade-B, .grade-B- { background: #2563EB; }
        .grade-Cplus, .grade-C, .grade-C- { background: #D97706; }
        .grade-Dplus, .grade-D, .grade-D- { background: #EA580C; }
        .grade-F { background: #C0392B; }

        /* Pages */
        .page { width: 210mm; margin: 0 auto 20px; background: #fff; overflow: hidden; page-break-after: always; }
        .ph { background: #3D4043; padding: 14px 36px; display: flex; justify-content: space-between; align-items: center; }
        .ph h1 { font-size: 12px; font-weight: 600; color: #fff; letter-spacing: 0.02em; }
        .ph img { height: 22px; }
        .stripe { height: 5px; background: linear-gradient(90deg, #006536 0%, #2BB673 20%, #39B54A 40%, #8DC63F 60%, #B2D235 80%, #D3E173 100%); }
        .pb { padding: 24px 36px 32px; }

        /* Summary grid */
        .summary-grid { display: flex; gap: 16px; margin: 16px 0 24px; flex-wrap: wrap; }
        .stat { background: #f8f9fa; border: 1px solid #e5e7eb; border-radius: 8px; padding: 16px 20px;
                text-align: center; flex: 1; min-width: 120px; }
        .stat.pass-stat { background: #f0fdf4; border-color: {{brand.PrimaryColor}}33; }
        .stat.warn-stat { background: #fffbeb; border-color: #D9770633; }
        .stat.fail-stat { background: #fef2f2; border-color: #C0392B33; }
        .stat-value { display: block; font-size: 28px; font-weight: 700; color: #1a1a1a; }
        .stat-label { display: block; font-size: 11px; color: #666; margin-top: 4px; text-transform: uppercase; letter-spacing: 0.05em; }

        /* Typography */
        h3 { font-size: 14px; font-weight: 700; color: {{brand.PrimaryColor}}; margin: 20px 0 10px;
             border-bottom: 2px solid {{brand.AccentColor}}; padding-bottom: 6px; }
        .cat-summary { font-size: 12px; color: #666; margin-bottom: 12px; }

        /* Grade distribution */
        .grade-dist { margin: 12px 0; }
        .grade-bar { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; }
        .grade-label { width: 30px; font-weight: 700; font-size: 13px; text-align: right; }
        .bar-track { flex: 1; height: 22px; background: #f0f0f0; border-radius: 4px; overflow: hidden; }
        .bar-fill { height: 100%; border-radius: 4px; min-width: 2px; }
        .grade-count { width: 30px; font-weight: 600; font-size: 13px; }

        /* Tables */
        .results-table, .fleet-table { width: 100%; border-collapse: collapse; margin: 10px 0; font-size: 12px; }
        .results-table th, .fleet-table th { background: #3D4043; color: #fff; padding: 8px 10px;
                                              text-align: left; font-weight: 600; font-size: 11px; }
        .results-table td, .fleet-table td { padding: 6px 10px; border-bottom: 1px solid #eee; }
        .results-table tr.pass, .fleet-table tr.pass { background: #f0fdf4; }
        .results-table tr.fail, .fleet-table tr.fail { background: #fef2f2; }
        .results-table tr.warn, .fleet-table tr.warn { background: #fffbeb; }
        .hostname { font-weight: 600; font-family: monospace; }
        .num { text-align: center; font-weight: 600; }
        .pass-cell { color: {{brand.PrimaryColor}}; }
        .warn-cell { color: #D97706; }
        .fail-cell { color: #C0392B; }

        /* Mini grades in fleet table */
        .grade-mini { padding: 2px 8px; border-radius: 4px; font-weight: 700; font-size: 11px; color: #fff; }

        /* Severity + status badges */
        .severity { padding: 2px 6px; border-radius: 3px; font-size: 10px; font-weight: 600; }
        .severity.critical { background: #7f1d1d; color: #fff; }
        .severity.high { background: #C0392B; color: #fff; }
        .severity.medium { background: #D97706; color: #fff; }
        .severity.low { background: #2563EB; color: #fff; }

        .remediation-row td { background: #f9fafb; font-size: 11px; color: #555; border-left: 3px solid {{brand.PrimaryColor}}; }

        /* Risk list */
        .risk-list { margin: 10px 0 10px 20px; }
        .risk-list li { margin-bottom: 8px; font-size: 13px; }

        /* Footer */
        .footer { text-align: center; padding: 20px; color: #999; font-size: 11px; border-top: 1px solid #eee; margin-top: 20px; }

        @media print { .page { margin: 0; box-shadow: none; } body { background: #fff; } }
        """;

    private static string GetReportStyles(ReportBranding brand) => $$"""
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: 'Montserrat', 'Verdana', sans-serif; color: #333; font-size: 14px; }
        .cover { background: #3D4043; color: #fff; padding: 60px 40px; text-align: center; page-break-after: always; }
        .cover h1 { font-size: 28px; font-weight: 700; margin-bottom: 10px; }
        .cover h2 { font-size: 20px; font-weight: 400; margin-bottom: 5px; }
        .cover .meta { font-size: 14px; opacity: 0.8; margin: 4px 0; }
        .cover .logo { max-height: 60px; margin-bottom: 30px; }
        .cover .score { font-size: 36px; font-weight: 900; margin-top: 10px; }
        .grade-badge { display: inline-block; font-size: 48px; font-weight: 900; padding: 15px 30px; border-radius: 12px; margin: 20px 0 5px; }
        .grade-Aplus, .grade-A { background: {{brand.PrimaryColor}}; }
        .grade-B { background: #2563EB; }
        .grade-C { background: #D97706; }
        .grade-D { background: #EA580C; }
        .grade-F { background: #C0392B; }
        .section { padding: 30px 40px; page-break-inside: avoid; }
        .section h2 { color: {{brand.PrimaryColor}}; font-size: 20px; border-bottom: 3px solid {{brand.AccentColor}}; padding-bottom: 8px; margin-bottom: 16px; }
        .section h3 { color: #3D4043; font-size: 16px; margin: 16px 0 8px; }
        .cat-score { font-weight: 400; font-size: 14px; color: #666; }
        .summary-grid { display: flex; gap: 20px; margin: 16px 0; }
        .stat { background: #f0fdf4; border: 1px solid {{brand.PrimaryColor}}33; border-radius: 8px; padding: 16px 24px; text-align: center; flex: 1; }
        .stat.warn { background: #fffbeb; border-color: #D9770633; }
        .stat.fail { background: #fef2f2; border-color: #C0392B33; }
        .stat-value { display: block; font-size: 28px; font-weight: 700; }
        .stat-label { display: block; font-size: 12px; color: #666; margin-top: 4px; }
        .info-table { width: 100%; border-collapse: collapse; margin: 8px 0; }
        .info-table td { padding: 6px 12px; border-bottom: 1px solid #eee; }
        .info-table td:first-child { font-weight: 600; width: 150px; color: #555; }
        .results-table { width: 100%; border-collapse: collapse; margin: 8px 0; font-size: 13px; }
        .results-table th { background: #3D4043; color: #fff; padding: 8px 10px; text-align: left; font-weight: 600; }
        .results-table td { padding: 6px 10px; border-bottom: 1px solid #eee; }
        .results-table tr.pass { background: #f0fdf4; }
        .results-table tr.fail { background: #fef2f2; }
        .results-table tr.warn { background: #fffbeb; }
        .status-badge { padding: 2px 8px; border-radius: 4px; font-weight: 600; font-size: 11px; text-transform: uppercase; }
        .status-badge.pass { background: {{brand.PrimaryColor}}; color: #fff; }
        .status-badge.warn { background: #D97706; color: #fff; }
        .status-badge.fail { background: #C0392B; color: #fff; }
        .severity { padding: 2px 6px; border-radius: 3px; font-size: 11px; font-weight: 600; }
        .severity.critical { background: #7f1d1d; color: #fff; }
        .severity.high { background: #C0392B; color: #fff; }
        .severity.medium { background: #D97706; color: #fff; }
        .severity.low { background: #2563EB; color: #fff; }
        .remediation-row td { background: #f9fafb; font-size: 12px; color: #555; border-left: 3px solid {{brand.PrimaryColor}}; }
        .footer { text-align: center; padding: 20px; color: #999; font-size: 11px; border-top: 1px solid #eee; margin-top: 30px; }
        @media print { .section { page-break-inside: avoid; } }
        """;

    private static string HtmlEncode(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? "");
}

internal class ReportControlResult
{
    public int ControlDefId { get; set; }
    public string ControlId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string Status { get; set; } = null!;
    public short Score { get; set; }
    public short MaxScore { get; set; }
    public string? Finding { get; set; }
    public string? ActualValue { get; set; }
    public string? Remediation { get; set; }
}

internal class OrgControlResult
{
    public int ControlDefId { get; set; }
    public Guid RunId { get; set; }
    public string ControlId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Finding { get; set; }
    public string? Remediation { get; set; }
}

internal class ReportBranding
{
    public string CompanyName { get; set; } = "TeamLogic IT";
    public string PrimaryColor { get; set; } = "#006536";
    public string AccentColor { get; set; } = "#A2C564";
    public string? LogoUrl { get; set; }
}
