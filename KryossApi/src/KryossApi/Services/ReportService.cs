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

        // Load per-run framework scores
        var frameworkScores = await _db.RunFrameworkScores
            .Where(fs => fs.RunId == runId)
            .Join(_db.Frameworks, fs => fs.FrameworkId, fw => fw.Id,
                (fs, fw) => new FrameworkScoreDto
                {
                    Code = fw.Code,
                    Name = fw.Name,
                    Score = Math.Round((double)fs.Score, 1),
                    PassCount = fs.PassCount,
                    FailCount = fs.FailCount
                })
            .OrderBy(x => x.Code)
            .ToListAsync();

        var brand = run.Organization.Brand;
        var franchise = run.Organization.Franchise;
        var branding = new ReportBranding
        {
            CompanyName = brand?.Name ?? franchise.BrandName ?? franchise.Name,
            PrimaryColor = brand?.ColorPrimary ?? franchise.BrandColorPrimary ?? "#006536",
            AccentColor = brand?.ColorAccent ?? franchise.BrandColorAccent ?? "#A2C564",
            LogoUrl = brand?.LogoUrl ?? franchise.BrandLogoUrl ?? LogoData.DataUri
        };

        return reportType switch
        {
            "executive" => BuildExecutiveReport(run, results, branding, frameworkName, frameworkScores),
            "presales" => BuildPresalesReport(run, results, branding, frameworkName, frameworkScores),
            _ => BuildTechnicalReport(run, results, branding, frameworkName, frameworkScores)
        };
    }

    // ======================================================================
    // PER-RUN: TECHNICAL REPORT
    // ======================================================================
    private static string BuildTechnicalReport(AssessmentRun run, List<ReportControlResult> results,
        ReportBranding brand, string? frameworkName, List<FrameworkScoreDto> frameworkScores)
    {
        var reportTitle = frameworkName != null ? $"{frameworkName} Compliance Report" : "Security Assessment Report";
        var sb = new StringBuilder();
        AppendHtmlHead(sb, $"{reportTitle} - {run.Machine.Hostname}", brand, isOrgReport: false);

        // Cover page
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{(frameworkName != null ? HtmlEncode(frameworkName.ToUpperInvariant()) : "SECURITY ASSESSMENT")}</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(run.Machine.Hostname)}</h2>");
        sb.AppendLine($"<p class='meta'>{HtmlEncode(run.Organization.Name)} &mdash; {run.StartedAt:MMMM dd, yyyy}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{run.Grade?.Replace("+", "plus")}'>{HtmlEncode(run.Grade ?? "N/A")}</div>");
        sb.AppendLine($"<p class='score'>{run.GlobalScore}%</p>");
        sb.AppendLine("</div></div>");

        // Summary page
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Executive Summary", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat pass-stat'><span class='stat-value'>{run.PassCount}</span><span class='stat-label'>Passed</span></div>");
        sb.AppendLine($"<div class='stat warn-stat'><span class='stat-value'>{run.WarnCount}</span><span class='stat-label'>Warnings</span></div>");
        sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{run.FailCount}</span><span class='stat-label'>Failed</span></div>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{run.EarnedPoints}/{run.TotalPoints}</span><span class='stat-label'>Points</span></div>");
        sb.AppendLine("</div>");

        // Framework compliance bars
        if (frameworkScores.Count > 0)
        {
            sb.AppendLine("<h3>Framework Compliance</h3>");
            AppendFrameworkBars(sb, frameworkScores);
        }

        // System info
        sb.AppendLine("<h3>System Information</h3>");
        sb.AppendLine("<table class='info-table'>");
        sb.AppendLine($"<tr><td>Hostname</td><td>{HtmlEncode(run.Machine.Hostname)}</td></tr>");
        sb.AppendLine($"<tr><td>OS</td><td>{HtmlEncode(run.Machine.OsName ?? "N/A")} {HtmlEncode(run.Machine.OsVersion ?? "")}</td></tr>");
        sb.AppendLine($"<tr><td>CPU</td><td>{HtmlEncode(run.Machine.CpuName ?? "N/A")} ({run.Machine.CpuCores ?? 0} cores)</td></tr>");
        sb.AppendLine($"<tr><td>RAM</td><td>{run.Machine.RamGb ?? 0} GB</td></tr>");
        sb.AppendLine($"<tr><td>Agent Version</td><td>{HtmlEncode(run.AgentVersion ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>Duration</td><td>{(run.DurationMs ?? 0) / 1000.0:F1}s</td></tr>");
        sb.AppendLine("</table>");

        // Hardware / Security / Network info
        AppendMachineHardwareSection(sb, run.Machine);

        sb.AppendLine("</div></div>");

        // Results by category
        var grouped = results.GroupBy(r => r.Category).OrderBy(g => g.Key);
        foreach (var category in grouped)
        {
            var catPass = category.Count(r => r.Status == "pass");
            var catWarn = category.Count(r => r.Status == "warn");
            var catFail = category.Count(r => r.Status == "fail");
            var catTotal = category.Count();
            var catPct = catTotal > 0 ? Math.Round((double)catPass / catTotal * 100) : 0;

            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, category.Key, brand);
            sb.AppendLine("<div class='pb'>");

            // Category header with pass/fail bar
            sb.AppendLine($"<div class='cat-header'>");
            sb.AppendLine($"<span class='cat-summary'>Compliance: <strong>{catPct}%</strong> ({catPass}/{catTotal})</span>");
            sb.AppendLine($"<div class='cat-bar'>");
            if (catPass > 0) sb.AppendLine($"<div class='cat-bar-pass' style='width:{(double)catPass / catTotal * 100}%'></div>");
            if (catWarn > 0) sb.AppendLine($"<div class='cat-bar-warn' style='width:{(double)catWarn / catTotal * 100}%'></div>");
            if (catFail > 0) sb.AppendLine($"<div class='cat-bar-fail' style='width:{(double)catFail / catTotal * 100}%'></div>");
            sb.AppendLine("</div></div>");

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
                sb.AppendLine($"<td>{HtmlEncode(r.Finding ?? "\u2014")}</td>");
                sb.AppendLine("</tr>");

                if (r.Status == "fail" && r.Remediation is not null)
                {
                    sb.AppendLine($"<tr class='remediation-row'><td colspan='5'>");
                    sb.AppendLine($"<strong>Remediation:</strong> {HtmlEncode(r.Remediation)}");
                    sb.AppendLine("</td></tr>");
                }
            }
            sb.AppendLine("</table></div></div>");
        }

        // Footer
        AppendFooter(sb, brand, $"Report ID: {run.Id}");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    // ======================================================================
    // PER-RUN: EXECUTIVE REPORT
    // ======================================================================
    private static string BuildExecutiveReport(AssessmentRun run, List<ReportControlResult> results,
        ReportBranding brand, string? frameworkName, List<FrameworkScoreDto> frameworkScores)
    {
        var reportTitle = frameworkName != null ? $"{frameworkName} Executive Summary" : "Security Assessment";
        var sb = new StringBuilder();
        AppendHtmlHead(sb, $"{reportTitle} - {run.Machine.Hostname}", brand, isOrgReport: false);

        // Cover page
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{(frameworkName != null ? HtmlEncode(frameworkName.ToUpperInvariant()) : "EXECUTIVE SUMMARY")}</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(run.Machine.Hostname)}</h2>");
        sb.AppendLine($"<p class='meta'>{HtmlEncode(run.Organization.Name)} &mdash; {run.StartedAt:MMMM dd, yyyy}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{run.Grade?.Replace("+", "plus")}'>{HtmlEncode(run.Grade ?? "N/A")}</div>");
        sb.AppendLine($"<p class='score'>{run.GlobalScore}% Compliance</p>");
        sb.AppendLine("</div></div>");

        // Key metrics page
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Key Findings", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat pass-stat'><span class='stat-value'>{run.PassCount}</span><span class='stat-label'>Controls Passed</span></div>");
        sb.AppendLine($"<div class='stat warn-stat'><span class='stat-value'>{run.WarnCount}</span><span class='stat-label'>Warnings</span></div>");
        sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{run.FailCount}</span><span class='stat-label'>Controls Failed</span></div>");
        sb.AppendLine("</div>");

        // Framework compliance bars
        if (frameworkScores.Count > 0)
        {
            sb.AppendLine("<h3>Framework Compliance</h3>");
            AppendFrameworkBars(sb, frameworkScores);
        }

        // Critical/high failures only
        var criticalFailures = results.Where(r => r.Status == "fail" && (r.Severity == "critical" || r.Severity == "high")).ToList();
        if (criticalFailures.Count > 0)
        {
            sb.AppendLine("<h3>Critical &amp; High Risk Issues</h3>");
            sb.AppendLine("<table class='results-table'>");
            sb.AppendLine("<tr><th>Control</th><th>Severity</th><th>Finding</th></tr>");
            foreach (var r in criticalFailures)
            {
                sb.AppendLine($"<tr class='fail'><td>{HtmlEncode(r.Name)}</td>");
                sb.AppendLine($"<td><span class='severity {r.Severity}'>{HtmlEncode(r.Severity)}</span></td>");
                sb.AppendLine($"<td>{HtmlEncode(r.Finding ?? "\u2014")}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        // System info
        sb.AppendLine("<h3>System Information</h3>");
        sb.AppendLine("<table class='info-table'>");
        sb.AppendLine($"<tr><td>Hostname</td><td>{HtmlEncode(run.Machine.Hostname)}</td></tr>");
        sb.AppendLine($"<tr><td>OS</td><td>{HtmlEncode(run.Machine.OsName ?? "N/A")} {HtmlEncode(run.Machine.OsVersion ?? "")}</td></tr>");
        sb.AppendLine($"<tr><td>Manufacturer / Model</td><td>{HtmlEncode(run.Machine.Manufacturer ?? "N/A")} / {HtmlEncode(run.Machine.Model ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>CPU</td><td>{HtmlEncode(run.Machine.CpuName ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>RAM</td><td>{run.Machine.RamGb ?? 0} GB</td></tr>");
        sb.AppendLine("</table>");

        sb.AppendLine("</div></div>");

        // Footer
        AppendFooter(sb, brand, $"Report ID: {run.Id}");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    // ======================================================================
    // PER-RUN: PRESALES REPORT
    // ======================================================================
    private static string BuildPresalesReport(AssessmentRun run, List<ReportControlResult> results,
        ReportBranding brand, string? frameworkName, List<FrameworkScoreDto> frameworkScores)
    {
        var reportTitle = frameworkName != null ? $"{frameworkName} Posture Assessment" : "Security Posture Assessment";
        var sb = new StringBuilder();
        AppendHtmlHead(sb, $"{reportTitle} - {run.Machine.Hostname}", brand, isOrgReport: false);

        // Cover page
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{(frameworkName != null ? HtmlEncode(frameworkName.ToUpperInvariant()) + " " : "")}SECURITY POSTURE</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(run.Organization.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{HtmlEncode(run.Machine.Hostname)} &mdash; {run.StartedAt:MMMM dd, yyyy}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{run.Grade?.Replace("+", "plus")}'>{HtmlEncode(run.Grade ?? "N/A")}</div>");
        sb.AppendLine($"<p class='score'>{run.GlobalScore}%</p>");
        sb.AppendLine("</div></div>");

        // What We Found page
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "What We Found", brand);
        sb.AppendLine("<div class='pb'>");

        var totalControls = results.Count;
        var passCount = run.PassCount ?? 0;
        var failCount = run.FailCount ?? 0;
        var warnCount = run.WarnCount ?? 0;

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{totalControls}</span><span class='stat-label'>Security Controls Evaluated</span></div>");
        sb.AppendLine($"<div class='stat pass-stat'><span class='stat-value'>{passCount}</span><span class='stat-label'>Passed</span></div>");
        sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{failCount}</span><span class='stat-label'>Issues Found</span></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='insight-box'>");
        sb.AppendLine($"<p>We evaluated <strong>{totalControls}</strong> industry-standard security controls on this system. ");
        if (failCount == 0)
            sb.AppendLine("The system is in excellent compliance. No critical issues were found.</p>");
        else if ((run.GlobalScore ?? 0) >= 80)
            sb.AppendLine($"While the system shows a strong baseline, we identified <strong>{failCount}</strong> areas that need attention to reach full compliance.</p>");
        else
            sb.AppendLine($"We identified <strong>{failCount}</strong> configuration gaps that expose the system to potential security risks. Immediate remediation is recommended.</p>");
        sb.AppendLine("</div>");

        // Top 5 risks with business-friendly impact descriptions
        var topRisks = results.Where(r => r.Status == "fail")
            .OrderByDescending(r => r.Severity == "critical" ? 4 : r.Severity == "high" ? 3 : r.Severity == "medium" ? 2 : 1)
            .Take(5).ToList();
        if (topRisks.Count > 0)
        {
            sb.AppendLine("<h3>Top Risks Identified</h3>");
            sb.AppendLine("<div class='risk-cards'>");
            var riskNum = 1;
            foreach (var r in topRisks)
            {
                sb.AppendLine($"<div class='risk-card'>");
                sb.AppendLine($"<div class='risk-num'>{riskNum++}</div>");
                sb.AppendLine($"<div class='risk-body'>");
                sb.AppendLine($"<strong>{HtmlEncode(r.Name)}</strong>");
                sb.AppendLine($"<span class='severity {r.Severity}'>{HtmlEncode(r.Severity)}</span>");
                if (r.Finding != null)
                    sb.AppendLine($"<p class='risk-detail'>{HtmlEncode(r.Finding)}</p>");
                sb.AppendLine("</div></div>");
            }
            sb.AppendLine("</div>");
        }

        // Category breakdown
        sb.AppendLine("<h3>Compliance by Category</h3>");
        sb.AppendLine("<table class='results-table'>");
        sb.AppendLine("<tr><th>Category</th><th>Pass</th><th>Fail</th><th>Score</th></tr>");
        foreach (var cat in results.GroupBy(r => r.Category).OrderBy(g => g.Key))
        {
            var catPass = cat.Count(r => r.Status == "pass");
            var catTotal = cat.Count();
            var pct = catTotal > 0 ? Math.Round((double)catPass / catTotal * 100) : 0;
            var rowClass = pct >= 80 ? "pass" : pct >= 60 ? "warn" : "fail";
            sb.AppendLine($"<tr class='{rowClass}'><td>{HtmlEncode(cat.Key)}</td><td>{catPass}</td><td>{catTotal - catPass}</td><td><strong>{pct}%</strong></td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("</div></div>");

        // Recommendation page
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Recommendation", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='recommendation-box'>");
        sb.AppendLine($"<h3>Based on Our Assessment</h3>");
        sb.AppendLine($"<p>Based on our comprehensive security assessment of <strong>{HtmlEncode(run.Machine.Hostname)}</strong>, ");
        if ((run.GlobalScore ?? 0) < 60)
            sb.AppendLine("we have identified significant security gaps that require immediate attention. We strongly recommend a prioritized remediation plan to address the critical and high-risk findings first.");
        else if ((run.GlobalScore ?? 0) < 80)
            sb.AppendLine("we have identified several areas where security hardening would significantly improve the organization's security posture. A structured remediation program will help close these gaps.");
        else
            sb.AppendLine("the system demonstrates a solid security foundation. We recommend addressing the remaining findings to achieve full compliance and maintain this strong security posture.");
        sb.AppendLine("</p></div>");

        sb.AppendLine("<h3>Next Steps</h3>");
        sb.AppendLine("<div class='next-steps'>");
        sb.AppendLine("<div class='step'><span class='step-num'>1</span><div><strong>Review Findings</strong><p>Go through the identified risks and prioritize based on business impact.</p></div></div>");
        sb.AppendLine("<div class='step'><span class='step-num'>2</span><div><strong>Remediation Plan</strong><p>We will develop a customized remediation roadmap tailored to your environment.</p></div></div>");
        sb.AppendLine("<div class='step'><span class='step-num'>3</span><div><strong>Implementation</strong><p>Our team will implement the security hardening measures and verify compliance.</p></div></div>");
        sb.AppendLine("<div class='step'><span class='step-num'>4</span><div><strong>Ongoing Monitoring</strong><p>Continuous assessment ensures your environment stays secure as threats evolve.</p></div></div>");
        sb.AppendLine("</div>");

        sb.AppendLine($"<div class='cta-box'>");
        sb.AppendLine($"<p>Contact <strong>{HtmlEncode(brand.CompanyName)}</strong> to discuss remediation options and managed security services.</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></div>");

        // Footer
        AppendFooter(sb, brand, $"Report ID: {run.Id}");
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

        // Load latest AD Hygiene scan for the org
        var hygieneScan = await _db.AdHygieneScans
            .Where(s => s.OrganizationId == orgId)
            .OrderByDescending(s => s.ScannedAt)
            .Select(s => new HygieneScanDto
            {
                ScannedAt = s.ScannedAt,
                TotalMachines = s.TotalMachines,
                TotalUsers = s.TotalUsers,
                StaleMachines = s.StaleMachines,
                DormantMachines = s.DormantMachines,
                StaleUsers = s.StaleUsers,
                DormantUsers = s.DormantUsers,
                DisabledUsers = s.DisabledUsers,
                PwdNeverExpire = s.PwdNeverExpire,
                Findings = _db.AdHygieneFindings
                    .Where(f => f.ScanId == s.Id)
                    .OrderBy(f => f.ObjectType).ThenByDescending(f => f.DaysInactive)
                    .ToList()
            })
            .FirstOrDefaultAsync();

        // Load aggregate framework scores
        var frameworkScores = await _db.RunFrameworkScores
            .Where(fs => latestRunIds.Contains(fs.RunId))
            .GroupBy(fs => fs.FrameworkId)
            .Select(g => new
            {
                frameworkId = g.Key,
                avgScore = Math.Round(g.Average(fs => (double)fs.Score), 1),
                totalPass = g.Sum(fs => (int)fs.PassCount),
                totalFail = g.Sum(fs => (int)fs.FailCount),
            })
            .Join(_db.Frameworks, x => x.frameworkId, fw => fw.Id,
                (x, fw) => new FrameworkScoreDto
                {
                    Code = fw.Code,
                    Name = fw.Name,
                    Score = x.avgScore,
                    PassCount = (short)x.totalPass,
                    FailCount = (short)x.totalFail
                })
            .OrderBy(x => x.Code)
            .ToListAsync();

        var brand = org.Brand;
        var franchise = org.Franchise;
        var branding = new ReportBranding
        {
            CompanyName = brand?.Name ?? franchise.BrandName ?? franchise.Name,
            PrimaryColor = brand?.ColorPrimary ?? franchise.BrandColorPrimary ?? "#006536",
            AccentColor = brand?.ColorAccent ?? franchise.BrandColorAccent ?? "#A2C564",
            LogoUrl = brand?.LogoUrl ?? franchise.BrandLogoUrl ?? LogoData.DataUri
        };

        return reportType switch
        {
            "technical" => BuildOrgTechnicalReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan),
            "presales" => BuildOrgPresalesReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan),
            _ => BuildOrgExecutiveReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan)
        };
    }

    // ======================================================================
    // ORG: EXECUTIVE REPORT
    // ======================================================================
    private static string BuildOrgExecutiveReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName,
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene)
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

        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true);

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

        // Framework compliance bars
        if (frameworkScores.Count > 0)
        {
            sb.AppendLine("<h3>Framework Compliance</h3>");
            AppendFrameworkBars(sb, frameworkScores);
        }

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

        // Fleet Overview page
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

        // AD Hygiene Summary page (if data exists)
        if (hygiene != null)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Active Directory Hygiene", brand);
            sb.AppendLine("<div class='pb'>");
            AppendHygieneSummary(sb, hygiene);
            sb.AppendLine("</div></div>");
        }

        // Footer
        AppendFooter(sb, brand, $"{totalMachines} devices");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    // ======================================================================
    // ORG: TECHNICAL REPORT
    // ======================================================================
    private static string BuildOrgTechnicalReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName,
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene)
    {
        var reportTitle = frameworkName != null ? $"{frameworkName} Technical Assessment" : "Technical Assessment";
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        var avgScore = runs.Average(r => r.GlobalScore ?? 0);
        var orgGrade = GetGrade(avgScore);
        var scanDate = runs.Max(r => r.CompletedAt ?? r.StartedAt);

        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true);

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

        // Framework compliance page
        if (frameworkScores.Count > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Framework Compliance", brand);
            sb.AppendLine("<div class='pb'>");
            AppendFrameworkBars(sb, frameworkScores);
            sb.AppendLine("</div></div>");
        }

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

        // AD Hygiene page
        if (hygiene != null)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Active Directory Hygiene", brand);
            sb.AppendLine("<div class='pb'>");
            AppendHygieneSummary(sb, hygiene);

            // Detailed findings table
            if (hygiene.Findings.Count > 0)
            {
                sb.AppendLine("<h3>Detailed Findings</h3>");
                sb.AppendLine("<table class='results-table'>");
                sb.AppendLine("<tr><th>Name</th><th>Type</th><th>Status</th><th>Days Inactive</th><th>Detail</th></tr>");
                foreach (var f in hygiene.Findings.Take(50))
                {
                    var rowClass = f.DaysInactive > 365 ? "fail" : f.DaysInactive > 90 ? "warn" : "pass";
                    sb.AppendLine($"<tr class='{rowClass}'>");
                    sb.AppendLine($"<td>{HtmlEncode(f.Name)}</td>");
                    sb.AppendLine($"<td>{HtmlEncode(f.ObjectType)}</td>");
                    sb.AppendLine($"<td><span class='severity {(f.DaysInactive > 365 ? "critical" : f.DaysInactive > 90 ? "medium" : "low")}'>{HtmlEncode(f.Status)}</span></td>");
                    sb.AppendLine($"<td class='num'>{f.DaysInactive}</td>");
                    sb.AppendLine($"<td>{HtmlEncode(f.Detail ?? "\u2014")}</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
                if (hygiene.Findings.Count > 50)
                    sb.AppendLine($"<p class='cat-summary'>Showing 50 of {hygiene.Findings.Count} findings.</p>");
            }
            sb.AppendLine("</div></div>");
        }

        // Control details by category
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
            var catWarn = cat.Sum(c => c.WarnCount);
            var catFail = cat.Sum(c => c.FailCount);
            var catTotal = catPass + catWarn + catFail;
            var catPct = catTotal > 0 ? Math.Round((double)catPass / catTotal * 100) : 0;

            sb.AppendLine($"<div class='cat-header'>");
            sb.AppendLine($"<span class='cat-summary'>Category compliance: <strong>{catPct}%</strong> ({catPass}/{catTotal} checks passed across {totalMachines} devices)</span>");
            sb.AppendLine($"<div class='cat-bar'>");
            if (catPass > 0) sb.AppendLine($"<div class='cat-bar-pass' style='width:{(double)catPass / catTotal * 100}%'></div>");
            if (catWarn > 0) sb.AppendLine($"<div class='cat-bar-warn' style='width:{(double)catWarn / catTotal * 100}%'></div>");
            if (catFail > 0) sb.AppendLine($"<div class='cat-bar-fail' style='width:{(double)catFail / catTotal * 100}%'></div>");
            sb.AppendLine("</div></div>");

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
        AppendFooter(sb, brand, $"{totalMachines} devices");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    // ======================================================================
    // ORG: PRESALES REPORT
    // ======================================================================
    private static string BuildOrgPresalesReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName,
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene)
    {
        var reportTitle = frameworkName != null ? $"{frameworkName} Security Posture" : "Security Posture Assessment";
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        var avgScore = runs.Average(r => r.GlobalScore ?? 0);
        var orgGrade = GetGrade(avgScore);
        var scanDate = runs.Max(r => r.CompletedAt ?? r.StartedAt);
        var totalFail = runs.Sum(r => r.FailCount ?? 0);

        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true);

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

        // What We Found page
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "What We Found", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{totalMachines}</span><span class='stat-label'>Devices Assessed</span></div>");
        sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{totalFail}</span><span class='stat-label'>Total Issues Found</span></div>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{avgScore:F1}%</span><span class='stat-label'>Average Compliance</span></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='insight-box'>");
        sb.AppendLine($"<p>We assessed <strong>{totalMachines}</strong> devices across your environment against industry security standards. ");
        if (avgScore >= 80)
            sb.AppendLine($"Your organization demonstrates a strong security posture with an average score of <strong>{avgScore:F1}%</strong>. There are still areas for improvement.");
        else if (avgScore >= 60)
            sb.AppendLine($"Your average compliance score of <strong>{avgScore:F1}%</strong> indicates moderate risk. Several security configurations need attention to meet industry standards.");
        else
            sb.AppendLine($"With an average score of <strong>{avgScore:F1}%</strong>, your environment has significant security gaps that could expose your business to cyber threats. Immediate action is recommended.");
        sb.AppendLine("</p></div>");

        // Framework compliance bars
        if (frameworkScores.Count > 0)
        {
            sb.AppendLine("<h3>Framework Compliance</h3>");
            AppendFrameworkBars(sb, frameworkScores);
        }

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

        // Top 5 risks
        var topRisks = allResults
            .Where(r => r.Status == "fail")
            .GroupBy(r => new { r.ControlId, r.Name, r.Severity })
            .Select(g => new { g.Key.Name, g.Key.Severity, Count = g.Count() })
            .OrderByDescending(x => x.Severity == "critical" ? 4 : x.Severity == "high" ? 3 : x.Severity == "medium" ? 2 : 1)
            .ThenByDescending(x => x.Count)
            .Take(5)
            .ToList();

        if (topRisks.Count > 0)
        {
            sb.AppendLine("<h3>Top Risks Across Your Environment</h3>");
            sb.AppendLine("<div class='risk-cards'>");
            var riskNum = 1;
            foreach (var r in topRisks)
            {
                sb.AppendLine($"<div class='risk-card'>");
                sb.AppendLine($"<div class='risk-num'>{riskNum++}</div>");
                sb.AppendLine($"<div class='risk-body'>");
                sb.AppendLine($"<strong>{HtmlEncode(r.Name)}</strong>");
                sb.AppendLine($"<span class='severity {r.Severity}'>{r.Severity}</span>");
                sb.AppendLine($"<p class='risk-detail'>Affects {r.Count}/{totalMachines} devices</p>");
                sb.AppendLine("</div></div>");
            }
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div></div>");

        // AD Hygiene highlights (only the scariest stuff for presales)
        if (hygiene != null)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Active Directory Hygiene", brand);
            sb.AppendLine("<div class='pb'>");

            sb.AppendLine("<div class='insight-box'>");
            sb.AppendLine("<p>Our Active Directory health scan reveals important security hygiene findings that require attention:</p>");
            sb.AppendLine("</div>");

            sb.AppendLine("<div class='summary-grid'>");
            if (hygiene.DormantMachines > 0)
                sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{hygiene.DormantMachines}</span><span class='stat-label'>Dormant Machines</span></div>");
            if (hygiene.DormantUsers > 0)
                sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{hygiene.DormantUsers}</span><span class='stat-label'>Dormant Users</span></div>");
            if (hygiene.PwdNeverExpire > 0)
                sb.AppendLine($"<div class='stat warn-stat'><span class='stat-value'>{hygiene.PwdNeverExpire}</span><span class='stat-label'>Password Never Expires</span></div>");
            if (hygiene.DisabledUsers > 0)
                sb.AppendLine($"<div class='stat warn-stat'><span class='stat-value'>{hygiene.DisabledUsers}</span><span class='stat-label'>Disabled Users in AD</span></div>");
            sb.AppendLine("</div>");

            // Highlight the scariest findings
            var scaryFindings = hygiene.Findings
                .Where(f => f.DaysInactive > 365 || f.Status == "PwdNeverExpires" || f.Status == "OldPassword")
                .Take(10)
                .ToList();

            if (scaryFindings.Count > 0)
            {
                sb.AppendLine("<h3>Notable Findings</h3>");
                sb.AppendLine("<table class='results-table'>");
                sb.AppendLine("<tr><th>Name</th><th>Type</th><th>Issue</th><th>Days Inactive</th></tr>");
                foreach (var f in scaryFindings)
                {
                    sb.AppendLine($"<tr class='fail'>");
                    sb.AppendLine($"<td>{HtmlEncode(f.Name)}</td>");
                    sb.AppendLine($"<td>{HtmlEncode(f.ObjectType)}</td>");
                    sb.AppendLine($"<td><span class='severity critical'>{HtmlEncode(f.Status)}</span></td>");
                    sb.AppendLine($"<td class='num'>{f.DaysInactive}</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }

            sb.AppendLine("</div></div>");
        }

        // Recommendation page
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Recommendation", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='recommendation-box'>");
        sb.AppendLine($"<h3>Based on Our Assessment</h3>");
        sb.AppendLine($"<p>After evaluating <strong>{totalMachines}</strong> devices in <strong>{HtmlEncode(org.Name)}</strong>'s environment, ");
        if (avgScore < 60)
            sb.AppendLine("we have identified critical security gaps across the fleet. Without remediation, these gaps leave the organization vulnerable to ransomware, data breaches, and compliance violations.");
        else if (avgScore < 80)
            sb.AppendLine("we see a moderate security posture with room for improvement. Addressing the identified risks will significantly reduce the organization's attack surface.");
        else
            sb.AppendLine("we found a generally solid security foundation. Addressing the remaining gaps will bring the organization to full compliance with industry standards.");
        sb.AppendLine("</p></div>");

        sb.AppendLine("<h3>Next Steps</h3>");
        sb.AppendLine("<div class='next-steps'>");
        sb.AppendLine("<div class='step'><span class='step-num'>1</span><div><strong>Review Findings</strong><p>Go through the identified risks and prioritize based on business impact.</p></div></div>");
        sb.AppendLine("<div class='step'><span class='step-num'>2</span><div><strong>Remediation Plan</strong><p>We will develop a customized remediation roadmap tailored to your environment.</p></div></div>");
        sb.AppendLine("<div class='step'><span class='step-num'>3</span><div><strong>Implementation</strong><p>Our team will implement the security hardening measures and verify compliance.</p></div></div>");
        sb.AppendLine("<div class='step'><span class='step-num'>4</span><div><strong>Ongoing Monitoring</strong><p>Continuous assessment ensures your environment stays secure as threats evolve.</p></div></div>");
        sb.AppendLine("</div>");

        sb.AppendLine($"<div class='cta-box'>");
        sb.AppendLine($"<p>Contact <strong>{HtmlEncode(brand.CompanyName)}</strong> to discuss remediation options and managed security services.</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></div>");

        // Footer
        AppendFooter(sb, brand, $"{totalMachines} devices");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    // ======================================================================
    // SHARED HELPERS
    // ======================================================================

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
        sb.AppendLine($"<img class='cover-ribbon' src='{RibbonData.DataUri}' alt='' />");
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

    private static void AppendFooter(StringBuilder sb, ReportBranding brand, string detail)
    {
        sb.AppendLine("<div class='footer'>");
        sb.AppendLine($"<p>Generated by {HtmlEncode(brand.CompanyName)} &mdash; Your Technology Advisor</p>");
        sb.AppendLine($"<p>{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC &bull; {detail}</p>");
        sb.AppendLine("</div>");
    }

    private static void AppendHtmlHead(StringBuilder sb, string title, ReportBranding brand, bool isOrgReport)
    {
        sb.AppendLine("<!DOCTYPE html><html lang='en'><head><meta charset='UTF-8'>");
        sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.AppendLine($"<title>{HtmlEncode(title)}</title>");
        sb.AppendLine("<link href='https://fonts.googleapis.com/css2?family=Montserrat:wght@300;400;500;600;700;900&display=swap' rel='stylesheet'>");
        sb.AppendLine("<style>");
        sb.AppendLine(isOrgReport ? GetOrgReportStyles(brand) : GetReportStyles(brand));
        sb.AppendLine("</style></head><body>");
    }

    private static void AppendFrameworkBars(StringBuilder sb, List<FrameworkScoreDto> frameworkScores)
    {
        sb.AppendLine("<div class='framework-bars'>");
        foreach (var fs in frameworkScores)
        {
            var barColor = fs.Score >= 80 ? "#006536" : fs.Score >= 60 ? "#D97706" : "#C0392B";
            sb.AppendLine("<div class='fw-bar-row'>");
            sb.AppendLine($"<span class='fw-label'>{HtmlEncode(fs.Code)}</span>");
            sb.AppendLine($"<div class='fw-track'><div class='fw-fill' style='width:{fs.Score}%;background:{barColor}'></div></div>");
            sb.AppendLine($"<span class='fw-pct'>{fs.Score}%</span>");
            sb.AppendLine($"<span class='fw-detail'>({fs.PassCount}P / {fs.FailCount}F)</span>");
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");
    }

    private static void AppendMachineHardwareSection(StringBuilder sb, Machine machine)
    {
        sb.AppendLine("<h3>Hardware &amp; Security</h3>");
        sb.AppendLine("<div class='hw-grid'>");

        // Hardware column
        sb.AppendLine("<div class='hw-col'>");
        sb.AppendLine("<h4>Hardware</h4>");
        sb.AppendLine("<table class='info-table'>");
        sb.AppendLine($"<tr><td>Manufacturer</td><td>{HtmlEncode(machine.Manufacturer ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>Model</td><td>{HtmlEncode(machine.Model ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>Serial</td><td>{HtmlEncode(machine.SerialNumber ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>Disk</td><td>{HtmlEncode(machine.DiskType ?? "N/A")} {machine.DiskSizeGb ?? 0} GB ({machine.DiskFreeGb ?? 0:F1} GB free)</td></tr>");
        sb.AppendLine("</table></div>");

        // Security column
        sb.AppendLine("<div class='hw-col'>");
        sb.AppendLine("<h4>Security</h4>");
        sb.AppendLine("<table class='info-table'>");
        sb.AppendLine($"<tr><td>TPM</td><td>{(machine.TpmPresent == true ? $"Yes ({HtmlEncode(machine.TpmVersion ?? "unknown")})" : "No")}</td></tr>");
        sb.AppendLine($"<tr><td>Secure Boot</td><td>{(machine.SecureBoot == true ? "Enabled" : "Disabled")}</td></tr>");
        sb.AppendLine($"<tr><td>BitLocker</td><td>{(machine.Bitlocker == true ? "Enabled" : "Disabled")}</td></tr>");
        sb.AppendLine("</table></div>");

        // Network column
        sb.AppendLine("<div class='hw-col'>");
        sb.AppendLine("<h4>Network</h4>");
        sb.AppendLine("<table class='info-table'>");
        sb.AppendLine($"<tr><td>IP Address</td><td>{HtmlEncode(machine.IpAddress ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>MAC Address</td><td>{HtmlEncode(machine.MacAddress ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>Domain</td><td>{HtmlEncode(machine.DomainStatus ?? "N/A")} {(machine.DomainName != null ? $"({HtmlEncode(machine.DomainName)})" : "")}</td></tr>");
        sb.AppendLine("</table></div>");

        sb.AppendLine("</div>");
    }

    private static void AppendHygieneSummary(StringBuilder sb, HygieneScanDto hygiene)
    {
        sb.AppendLine($"<p class='cat-summary'>Last scan: {hygiene.ScannedAt:MMMM dd, yyyy} &mdash; {hygiene.TotalMachines} machines, {hygiene.TotalUsers} users in directory</p>");

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{hygiene.TotalMachines}</span><span class='stat-label'>Total AD Machines</span></div>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{hygiene.TotalUsers}</span><span class='stat-label'>Total AD Users</span></div>");
        sb.AppendLine($"<div class='stat {(hygiene.DormantMachines > 0 ? "fail-stat" : "")}'><span class='stat-value'>{hygiene.DormantMachines}</span><span class='stat-label'>Dormant Machines</span></div>");
        sb.AppendLine($"<div class='stat {(hygiene.DormantUsers > 0 ? "fail-stat" : "")}'><span class='stat-value'>{hygiene.DormantUsers}</span><span class='stat-label'>Dormant Users</span></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat {(hygiene.StaleMachines > 0 ? "warn-stat" : "")}'><span class='stat-value'>{hygiene.StaleMachines}</span><span class='stat-label'>Stale Machines</span></div>");
        sb.AppendLine($"<div class='stat {(hygiene.StaleUsers > 0 ? "warn-stat" : "")}'><span class='stat-value'>{hygiene.StaleUsers}</span><span class='stat-label'>Stale Users</span></div>");
        sb.AppendLine($"<div class='stat {(hygiene.PwdNeverExpire > 0 ? "warn-stat" : "")}'><span class='stat-value'>{hygiene.PwdNeverExpire}</span><span class='stat-label'>Password Never Expires</span></div>");
        sb.AppendLine($"<div class='stat {(hygiene.DisabledUsers > 0 ? "warn-stat" : "")}'><span class='stat-value'>{hygiene.DisabledUsers}</span><span class='stat-label'>Disabled Users</span></div>");
        sb.AppendLine("</div>");
    }

    // ======================================================================
    // STYLES
    // ======================================================================

    private static string GetOrgReportStyles(ReportBranding brand) => $$"""
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: 'Montserrat', 'Verdana', sans-serif; color: #333; font-size: 13px; line-height: 1.6;
               -webkit-print-color-adjust: exact; print-color-adjust: exact; background: #ECEAE4; }

        /* Cover */
        .cover { width: 210mm; min-height: 297mm; margin: 0 auto 20px; background: #3D4043; position: relative;
                 overflow: hidden; display: flex; align-items: flex-end; page-break-after: always; }
        .cover-ribbon { position: absolute; bottom: 0; right: -404px; width: 100%; height: 59%; pointer-events: none; object-fit: cover; object-position: right bottom; }
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
        h4 { font-size: 12px; font-weight: 600; color: #3D4043; margin: 8px 0 4px; }
        .cat-summary { font-size: 12px; color: #666; margin-bottom: 12px; }

        /* Category header with bar */
        .cat-header { margin-bottom: 12px; }
        .cat-bar { height: 8px; border-radius: 4px; display: flex; overflow: hidden; margin-top: 6px; background: #f0f0f0; }
        .cat-bar-pass { background: {{brand.PrimaryColor}}; }
        .cat-bar-warn { background: #D97706; }
        .cat-bar-fail { background: #C0392B; }

        /* Framework compliance bars */
        .framework-bars { margin: 12px 0; }
        .fw-bar-row { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
        .fw-label { width: 60px; font-weight: 700; font-size: 11px; text-align: right; text-transform: uppercase; }
        .fw-track { flex: 1; height: 20px; background: #f0f0f0; border-radius: 4px; overflow: hidden; }
        .fw-fill { height: 100%; border-radius: 4px; min-width: 2px; transition: width 0.3s; }
        .fw-pct { width: 40px; font-weight: 700; font-size: 12px; }
        .fw-detail { font-size: 10px; color: #888; width: 70px; }

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

        .status-badge { padding: 2px 8px; border-radius: 4px; font-weight: 600; font-size: 11px; text-transform: uppercase; }
        .status-badge.pass { background: {{brand.PrimaryColor}}; color: #fff; }
        .status-badge.warn { background: #D97706; color: #fff; }
        .status-badge.fail { background: #C0392B; color: #fff; }

        .remediation-row td { background: #f9fafb; font-size: 11px; color: #555; border-left: 3px solid {{brand.PrimaryColor}}; }

        /* Risk cards */
        .risk-cards { margin: 12px 0; }
        .risk-card { display: flex; gap: 12px; padding: 12px; border: 1px solid #fecaca; border-radius: 8px;
                     background: #fef2f2; margin-bottom: 8px; align-items: flex-start; }
        .risk-num { background: #C0392B; color: #fff; width: 28px; height: 28px; border-radius: 50%;
                    display: flex; align-items: center; justify-content: center; font-weight: 700;
                    font-size: 13px; flex-shrink: 0; }
        .risk-body { flex: 1; }
        .risk-body strong { display: block; margin-bottom: 4px; font-size: 13px; }
        .risk-detail { font-size: 11px; color: #666; margin-top: 4px; }

        /* Insight box */
        .insight-box { background: #f0f4ff; border: 1px solid #c7d2fe; border-radius: 8px; padding: 16px 20px;
                       margin: 16px 0; font-size: 13px; line-height: 1.7; }
        .insight-box p { margin: 0; }

        /* Recommendation box */
        .recommendation-box { background: #f0fdf4; border: 1px solid {{brand.PrimaryColor}}44; border-radius: 8px;
                              padding: 20px 24px; margin: 16px 0; }
        .recommendation-box h3 { border: none; margin-top: 0; padding-bottom: 8px; }
        .recommendation-box p { font-size: 13px; line-height: 1.7; }

        /* Next steps */
        .next-steps { margin: 16px 0; }
        .step { display: flex; gap: 14px; margin-bottom: 14px; align-items: flex-start; }
        .step-num { background: {{brand.PrimaryColor}}; color: #fff; width: 32px; height: 32px; border-radius: 50%;
                    display: flex; align-items: center; justify-content: center; font-weight: 700;
                    font-size: 14px; flex-shrink: 0; }
        .step strong { display: block; font-size: 13px; margin-bottom: 2px; }
        .step p { font-size: 12px; color: #666; margin: 0; }

        /* CTA box */
        .cta-box { background: #3D4043; color: #fff; border-radius: 8px; padding: 20px 24px; margin-top: 24px;
                   text-align: center; }
        .cta-box p { margin: 0; font-size: 14px; }

        /* Hardware grid */
        .hw-grid { display: flex; gap: 16px; margin: 12px 0; flex-wrap: wrap; }
        .hw-col { flex: 1; min-width: 200px; }

        /* Info table */
        .info-table { width: 100%; border-collapse: collapse; margin: 4px 0; }
        .info-table td { padding: 4px 8px; border-bottom: 1px solid #eee; font-size: 12px; }
        .info-table td:first-child { font-weight: 600; width: 120px; color: #555; }

        /* Risk list (legacy) */
        .risk-list { margin: 10px 0 10px 20px; }
        .risk-list li { margin-bottom: 8px; font-size: 13px; }

        /* Footer */
        .footer { text-align: center; padding: 20px; color: #999; font-size: 11px; border-top: 1px solid #eee; margin-top: 20px; }

        @media print { .page { margin: 0; box-shadow: none; } body { background: #fff; } }
        """;

    private static string GetReportStyles(ReportBranding brand) => $$"""
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: 'Montserrat', 'Verdana', sans-serif; color: #333; font-size: 13px; line-height: 1.6;
               -webkit-print-color-adjust: exact; print-color-adjust: exact; background: #ECEAE4; }

        /* Cover */
        .cover { width: 210mm; min-height: 297mm; margin: 0 auto 20px; background: #3D4043; position: relative;
                 overflow: hidden; display: flex; align-items: flex-end; page-break-after: always; }
        .cover-ribbon { position: absolute; bottom: 0; right: -404px; width: 100%; height: 59%; pointer-events: none; object-fit: cover; object-position: right bottom; }
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
        h4 { font-size: 12px; font-weight: 600; color: #3D4043; margin: 8px 0 4px; }
        .cat-summary { font-size: 12px; color: #666; margin-bottom: 12px; }
        .cat-score { font-weight: 400; font-size: 14px; color: #666; }

        /* Category header with bar */
        .cat-header { margin-bottom: 12px; }
        .cat-bar { height: 8px; border-radius: 4px; display: flex; overflow: hidden; margin-top: 6px; background: #f0f0f0; }
        .cat-bar-pass { background: {{brand.PrimaryColor}}; }
        .cat-bar-warn { background: #D97706; }
        .cat-bar-fail { background: #C0392B; }

        /* Framework compliance bars */
        .framework-bars { margin: 12px 0; }
        .fw-bar-row { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
        .fw-label { width: 60px; font-weight: 700; font-size: 11px; text-align: right; text-transform: uppercase; }
        .fw-track { flex: 1; height: 20px; background: #f0f0f0; border-radius: 4px; overflow: hidden; }
        .fw-fill { height: 100%; border-radius: 4px; min-width: 2px; }
        .fw-pct { width: 40px; font-weight: 700; font-size: 12px; }
        .fw-detail { font-size: 10px; color: #888; width: 70px; }

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

        /* Mini grades */
        .grade-mini { padding: 2px 8px; border-radius: 4px; font-weight: 700; font-size: 11px; color: #fff; }

        /* Severity + status badges */
        .severity { padding: 2px 6px; border-radius: 3px; font-size: 10px; font-weight: 600; }
        .severity.critical { background: #7f1d1d; color: #fff; }
        .severity.high { background: #C0392B; color: #fff; }
        .severity.medium { background: #D97706; color: #fff; }
        .severity.low { background: #2563EB; color: #fff; }

        .status-badge { padding: 2px 8px; border-radius: 4px; font-weight: 600; font-size: 11px; text-transform: uppercase; }
        .status-badge.pass { background: {{brand.PrimaryColor}}; color: #fff; }
        .status-badge.warn { background: #D97706; color: #fff; }
        .status-badge.fail { background: #C0392B; color: #fff; }

        .remediation-row td { background: #f9fafb; font-size: 11px; color: #555; border-left: 3px solid {{brand.PrimaryColor}}; }

        /* Risk cards */
        .risk-cards { margin: 12px 0; }
        .risk-card { display: flex; gap: 12px; padding: 12px; border: 1px solid #fecaca; border-radius: 8px;
                     background: #fef2f2; margin-bottom: 8px; align-items: flex-start; }
        .risk-num { background: #C0392B; color: #fff; width: 28px; height: 28px; border-radius: 50%;
                    display: flex; align-items: center; justify-content: center; font-weight: 700;
                    font-size: 13px; flex-shrink: 0; }
        .risk-body { flex: 1; }
        .risk-body strong { display: block; margin-bottom: 4px; font-size: 13px; }
        .risk-detail { font-size: 11px; color: #666; margin-top: 4px; }

        /* Insight box */
        .insight-box { background: #f0f4ff; border: 1px solid #c7d2fe; border-radius: 8px; padding: 16px 20px;
                       margin: 16px 0; font-size: 13px; line-height: 1.7; }
        .insight-box p { margin: 0; }

        /* Recommendation box */
        .recommendation-box { background: #f0fdf4; border: 1px solid {{brand.PrimaryColor}}44; border-radius: 8px;
                              padding: 20px 24px; margin: 16px 0; }
        .recommendation-box h3 { border: none; margin-top: 0; padding-bottom: 8px; }
        .recommendation-box p { font-size: 13px; line-height: 1.7; }

        /* Next steps */
        .next-steps { margin: 16px 0; }
        .step { display: flex; gap: 14px; margin-bottom: 14px; align-items: flex-start; }
        .step-num { background: {{brand.PrimaryColor}}; color: #fff; width: 32px; height: 32px; border-radius: 50%;
                    display: flex; align-items: center; justify-content: center; font-weight: 700;
                    font-size: 14px; flex-shrink: 0; }
        .step strong { display: block; font-size: 13px; margin-bottom: 2px; }
        .step p { font-size: 12px; color: #666; margin: 0; }

        /* CTA box */
        .cta-box { background: #3D4043; color: #fff; border-radius: 8px; padding: 20px 24px; margin-top: 24px;
                   text-align: center; }
        .cta-box p { margin: 0; font-size: 14px; }

        /* Hardware grid */
        .hw-grid { display: flex; gap: 16px; margin: 12px 0; flex-wrap: wrap; }
        .hw-col { flex: 1; min-width: 200px; }

        /* Info table */
        .info-table { width: 100%; border-collapse: collapse; margin: 4px 0; }
        .info-table td { padding: 4px 8px; border-bottom: 1px solid #eee; font-size: 12px; }
        .info-table td:first-child { font-weight: 600; width: 120px; color: #555; }

        /* Grade distribution */
        .grade-dist { margin: 12px 0; }
        .grade-bar { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; }
        .grade-label { width: 30px; font-weight: 700; font-size: 13px; text-align: right; }
        .bar-track { flex: 1; height: 22px; background: #f0f0f0; border-radius: 4px; overflow: hidden; }
        .bar-fill { height: 100%; border-radius: 4px; min-width: 2px; }
        .grade-count { width: 30px; font-weight: 600; font-size: 13px; }

        /* Footer */
        .footer { text-align: center; padding: 20px; color: #999; font-size: 11px; border-top: 1px solid #eee; margin-top: 20px; }

        @media print { .page { margin: 0; box-shadow: none; } body { background: #fff; } }
        """;

    private static string HtmlEncode(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? "");
}

// ======================================================================
// INTERNAL DTOS
// ======================================================================

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

internal class FrameworkScoreDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public double Score { get; set; }
    public short PassCount { get; set; }
    public short FailCount { get; set; }
}

internal class HygieneScanDto
{
    public DateTime ScannedAt { get; set; }
    public int TotalMachines { get; set; }
    public int TotalUsers { get; set; }
    public int StaleMachines { get; set; }
    public int DormantMachines { get; set; }
    public int StaleUsers { get; set; }
    public int DormantUsers { get; set; }
    public int DisabledUsers { get; set; }
    public int PwdNeverExpire { get; set; }
    public List<AdHygieneFinding> Findings { get; set; } = [];
}
