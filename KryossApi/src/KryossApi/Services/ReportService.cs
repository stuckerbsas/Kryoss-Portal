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

        // Load enrichment data for this machine
        var machineId = run.MachineId;
        var disks = await _db.MachineDisks.Where(d => d.MachineId == machineId).OrderBy(d => d.DriveLetter).ToListAsync();
        var ports = await _db.MachinePorts.Where(p => p.MachineId == machineId).OrderBy(p => p.Port).ToListAsync();
        var threats = await _db.MachineThreats.Where(t => t.MachineId == machineId).OrderByDescending(t => t.DetectedAt).ToListAsync();

        var enrichment = new MachineEnrichment { Disks = disks, Ports = ports, Threats = threats };

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
            "executive" => BuildExecutiveReport(run, results, branding, frameworkName, frameworkScores, enrichment),
            "presales" => BuildPresalesReport(run, results, branding, frameworkName, frameworkScores, enrichment),
            _ => BuildTechnicalReport(run, results, branding, frameworkName, frameworkScores, enrichment)
        };
    }

    // ======================================================================
    // PER-RUN: TECHNICAL REPORT
    // ======================================================================
    private static string BuildTechnicalReport(AssessmentRun run, List<ReportControlResult> results,
        ReportBranding brand, string? frameworkName, List<FrameworkScoreDto> frameworkScores,
        MachineEnrichment enrichment)
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

        // Disk inventory (per-run)
        if (enrichment.Disks.Count > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Disk Inventory", brand);
            sb.AppendLine("<div class='pb'>");
            AppendDiskTable(sb, enrichment.Disks);
            sb.AppendLine("</div></div>");
        }

        // Port scan results (per-run)
        if (enrichment.Ports.Count > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Open Ports", brand);
            sb.AppendLine("<div class='pb'>");
            var riskyPorts = enrichment.Ports.Where(p => p.Risk != null).ToList();
            if (riskyPorts.Count > 0)
            {
                sb.AppendLine($"<div class='insight-box'><p>Found <strong>{riskyPorts.Count}</strong> ports flagged as risky out of <strong>{enrichment.Ports.Count}</strong> open ports on this machine.</p></div>");
            }
            AppendPortTable(sb, enrichment.Ports);
            sb.AppendLine("</div></div>");
        }

        // Threat findings (per-run)
        if (enrichment.Threats.Count > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Threat Detections", brand);
            sb.AppendLine("<div class='pb'>");
            sb.AppendLine($"<div class='insight-box fail-box'><p>Detected <strong>{enrichment.Threats.Count}</strong> threat signatures on this machine. Immediate investigation is recommended.</p></div>");
            AppendThreatTable(sb, enrichment.Threats);
            sb.AppendLine("</div></div>");
        }

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
        ReportBranding brand, string? frameworkName, List<FrameworkScoreDto> frameworkScores,
        MachineEnrichment enrichment)
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

        var riskyPorts = enrichment.Ports.Where(p => p.Risk != null).ToList();

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat pass-stat'><span class='stat-value'>{run.PassCount}</span><span class='stat-label'>Controls Passed</span></div>");
        sb.AppendLine($"<div class='stat warn-stat'><span class='stat-value'>{run.WarnCount}</span><span class='stat-label'>Warnings</span></div>");
        sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{run.FailCount}</span><span class='stat-label'>Controls Failed</span></div>");
        if (enrichment.Threats.Count > 0)
            sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{enrichment.Threats.Count}</span><span class='stat-label'>Threats Detected</span></div>");
        if (riskyPorts.Count > 0)
            sb.AppendLine($"<div class='stat warn-stat'><span class='stat-value'>{riskyPorts.Count}</span><span class='stat-label'>Risky Ports</span></div>");
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
            foreach (var r in criticalFailures.Take(15))
            {
                sb.AppendLine($"<tr class='fail'><td>{HtmlEncode(r.Name)}</td>");
                sb.AppendLine($"<td><span class='severity {r.Severity}'>{HtmlEncode(r.Severity)}</span></td>");
                sb.AppendLine($"<td>{HtmlEncode(r.Finding ?? "\u2014")}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("</div></div>");

        // Threat and port highlights (if any)
        if (enrichment.Threats.Count > 0 || riskyPorts.Count > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Network & Threat Summary", brand);
            sb.AppendLine("<div class='pb'>");

            if (enrichment.Threats.Count > 0)
            {
                sb.AppendLine("<h3>Threat Detections</h3>");
                sb.AppendLine($"<div class='insight-box fail-box'><p>Detected <strong>{enrichment.Threats.Count}</strong> threat signatures on this system. Categories include: {HtmlEncode(string.Join(", ", enrichment.Threats.Select(t => t.Category).Distinct().Take(5)))}.</p></div>");
                AppendThreatTable(sb, enrichment.Threats.Take(10).ToList());
            }

            if (riskyPorts.Count > 0)
            {
                sb.AppendLine("<h3>Risky Open Ports</h3>");
                AppendPortTable(sb, riskyPorts);
            }

            sb.AppendLine("</div></div>");
        }

        // System info page
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "System Information", brand);
        sb.AppendLine("<div class='pb'>");
        sb.AppendLine("<table class='info-table'>");
        sb.AppendLine($"<tr><td>Hostname</td><td>{HtmlEncode(run.Machine.Hostname)}</td></tr>");
        sb.AppendLine($"<tr><td>OS</td><td>{HtmlEncode(run.Machine.OsName ?? "N/A")} {HtmlEncode(run.Machine.OsVersion ?? "")}</td></tr>");
        sb.AppendLine($"<tr><td>Manufacturer / Model</td><td>{HtmlEncode(run.Machine.Manufacturer ?? "N/A")} / {HtmlEncode(run.Machine.Model ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>CPU</td><td>{HtmlEncode(run.Machine.CpuName ?? "N/A")}</td></tr>");
        sb.AppendLine($"<tr><td>RAM</td><td>{run.Machine.RamGb ?? 0} GB</td></tr>");
        sb.AppendLine($"<tr><td>TPM</td><td>{(run.Machine.TpmPresent == true ? $"Yes ({HtmlEncode(run.Machine.TpmVersion ?? "unknown")})" : "No")}</td></tr>");
        sb.AppendLine($"<tr><td>Secure Boot</td><td>{(run.Machine.SecureBoot == true ? "Enabled" : "Disabled")}</td></tr>");
        sb.AppendLine($"<tr><td>BitLocker</td><td>{(run.Machine.Bitlocker == true ? "Enabled" : "Disabled")}</td></tr>");
        sb.AppendLine($"<tr><td>Domain</td><td>{HtmlEncode(run.Machine.DomainStatus ?? "N/A")} {(run.Machine.DomainName != null ? $"({HtmlEncode(run.Machine.DomainName)})" : "")}</td></tr>");
        sb.AppendLine("</table>");

        if (enrichment.Disks.Count > 0)
        {
            sb.AppendLine("<h3>Disk Inventory</h3>");
            AppendDiskTable(sb, enrichment.Disks);
        }

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
        ReportBranding brand, string? frameworkName, List<FrameworkScoreDto> frameworkScores,
        MachineEnrichment enrichment)
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
        var riskyPorts = enrichment.Ports.Where(p => p.Risk != null).ToList();

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{totalControls}</span><span class='stat-label'>Security Controls Evaluated</span></div>");
        sb.AppendLine($"<div class='stat pass-stat'><span class='stat-value'>{passCount}</span><span class='stat-label'>Passed</span></div>");
        sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{failCount}</span><span class='stat-label'>Issues Found</span></div>");
        if (enrichment.Threats.Count > 0)
            sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{enrichment.Threats.Count}</span><span class='stat-label'>Threats Detected</span></div>");
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

        // Framework compliance bars
        if (frameworkScores.Count > 0)
        {
            sb.AppendLine("<h3>Framework Compliance</h3>");
            AppendFrameworkBars(sb, frameworkScores);
        }

        sb.AppendLine("</div></div>");

        // Threat & port page (the "scare" page)
        if (enrichment.Threats.Count > 0 || riskyPorts.Count > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "What's Lurking on This System", brand);
            sb.AppendLine("<div class='pb'>");

            if (enrichment.Threats.Count > 0)
            {
                var categories = enrichment.Threats.Select(t => t.Category).Distinct().ToList();
                sb.AppendLine($"<div class='insight-box fail-box'><p>We detected <strong>{enrichment.Threats.Count}</strong> threat signatures across <strong>{categories.Count}</strong> categories including: {HtmlEncode(string.Join(", ", categories.Take(6)))}.</p></div>");
                AppendThreatTable(sb, enrichment.Threats.Take(10).ToList());
            }

            if (riskyPorts.Count > 0)
            {
                sb.AppendLine("<h3>Risky Open Ports</h3>");
                var rdpPorts = riskyPorts.Where(p => p.Port == 3389).ToList();
                var smbPorts = riskyPorts.Where(p => p.Port == 445).ToList();
                if (rdpPorts.Count > 0)
                    sb.AppendLine($"<div class='insight-box fail-box'><p>RDP (port 3389) is open on this machine. RDP is the number one attack vector for ransomware.</p></div>");
                if (smbPorts.Count > 0)
                    sb.AppendLine($"<div class='insight-box fail-box'><p>SMB (port 445) is open. SMB is commonly exploited by worms and lateral-movement tools.</p></div>");
                AppendPortTable(sb, riskyPorts);
            }

            sb.AppendLine("</div></div>");
        }

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

        // Load enrichment data for all machines in the org
        var machineIds = runs.Select(r => r.MachineId).ToList();
        var allDisks = await _db.MachineDisks.Where(d => machineIds.Contains(d.MachineId)).OrderBy(d => d.DriveLetter).ToListAsync();
        var allPorts = await _db.MachinePorts.Where(p => machineIds.Contains(p.MachineId)).OrderBy(p => p.Port).ToListAsync();
        var allThreats = await _db.MachineThreats.Where(t => machineIds.Contains(t.MachineId)).OrderByDescending(t => t.DetectedAt).ToListAsync();

        var orgEnrichment = new OrgEnrichment
        {
            Disks = allDisks,
            Ports = allPorts,
            Threats = allThreats
        };

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
            "technical" => BuildOrgTechnicalReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment),
            "presales" => BuildOrgPresalesReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment),
            _ => BuildOrgExecutiveReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment)
        };
    }

    // ======================================================================
    // ORG: EXECUTIVE REPORT
    // ======================================================================
    private static string BuildOrgExecutiveReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName,
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment)
    {
        var reportTitle = frameworkName != null ? $"{frameworkName} Organization Report" : "Security Assessment Report";
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        var avgScore = runs.Average(r => r.GlobalScore ?? 0);
        var orgGrade = GetGrade(avgScore);
        var totalPass = runs.Sum(r => r.PassCount ?? 0);
        var totalWarn = runs.Sum(r => r.WarnCount ?? 0);
        var totalFail = runs.Sum(r => r.FailCount ?? 0);
        var scanDate = runs.Max(r => r.CompletedAt ?? r.StartedAt);

        var riskyPorts = enrichment.Ports.Where(p => p.Risk != null).ToList();
        var threatCount = enrichment.Threats.Count;

        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true);

        // ---- PAGE 1: COVER ----
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

        // ---- PAGE 2: EXECUTIVE SUMMARY ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Executive Summary", brand);
        sb.AppendLine("<div class='pb'>");

        // Business-language narrative paragraph
        var riskLevel = avgScore >= 80 ? "acceptable" : avgScore >= 60 ? "moderate" : avgScore >= 30 ? "concerning" : "critically low";
        sb.AppendLine("<div class='insight-box'>");
        sb.AppendLine($"<p>Your organization has <strong>{totalMachines}</strong> devices under management. Our assessment evaluated <strong>{totalPass + totalWarn + totalFail}</strong> security controls across your fleet and found <strong>{riskLevel}</strong> levels of compliance. ");
        if (avgScore < 60)
            sb.AppendLine("Significant remediation is required to bring your environment to an acceptable security baseline.");
        else if (avgScore < 80)
            sb.AppendLine("Several areas need hardening to meet industry best practices.");
        else
            sb.AppendLine("Your overall security posture is strong, with targeted improvements still available.");
        sb.AppendLine("</p></div>");

        // KPI cards
        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{totalMachines}</span><span class='stat-label'>Total Devices</span></div>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{avgScore:F1}%</span><span class='stat-label'>Avg. Compliance</span></div>");
        if (threatCount > 0)
            sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{threatCount}</span><span class='stat-label'>Threats Detected</span></div>");
        if (riskyPorts.Count > 0)
            sb.AppendLine($"<div class='stat warn-stat'><span class='stat-value'>{riskyPorts.Select(p => p.MachineId).Distinct().Count()}</span><span class='stat-label'>Devices with Risky Ports</span></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></div>");

        // ---- PAGE 3: RISK DASHBOARD ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Risk Dashboard", brand);
        sb.AppendLine("<div class='pb'>");

        // Framework compliance bars
        if (frameworkScores.Count > 0)
        {
            sb.AppendLine("<h3>Framework Compliance</h3>");
            AppendFrameworkBars(sb, frameworkScores);
        }

        // Top 5 critical findings in business language
        var criticalFailures = allResults
            .Where(r => r.Status == "fail" && (r.Severity == "critical" || r.Severity == "high"))
            .GroupBy(r => new { r.ControlId, r.Name, r.Severity })
            .Select(g => new { g.Key.ControlId, g.Key.Name, g.Key.Severity, AffectedCount = g.Count() })
            .OrderByDescending(x => x.Severity == "critical" ? 1 : 0)
            .ThenByDescending(x => x.AffectedCount)
            .Take(5)
            .ToList();

        if (criticalFailures.Count > 0)
        {
            sb.AppendLine("<h3>Top Critical Findings</h3>");
            sb.AppendLine("<div class='risk-cards'>");
            var riskNum = 1;
            foreach (var f in criticalFailures)
            {
                sb.AppendLine("<div class='risk-card'>");
                sb.AppendLine($"<div class='risk-num'>{riskNum++}</div>");
                sb.AppendLine("<div class='risk-body'>");
                sb.AppendLine($"<strong>{HtmlEncode(f.Name)}</strong>");
                sb.AppendLine($"<span class='severity {f.Severity}'>{HtmlEncode(f.Severity)}</span>");
                sb.AppendLine($"<p class='risk-detail'>Affects {f.AffectedCount} of {totalMachines} devices</p>");
                sb.AppendLine("</div></div>");
            }
            sb.AppendLine("</div>");
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

        sb.AppendLine("</div></div>");

        // ---- PAGE 4: AD HEALTH ----
        if (hygiene != null)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Active Directory Health", brand);
            sb.AppendLine("<div class='pb'>");
            AppendHygieneSummary(sb, hygiene);

            // Derive extended AD stats from findings
            var privilegedCount = hygiene.Findings.Count(f => f.Status == "Privileged");
            var kerberoastable = hygiene.Findings.Count(f => f.Status == "Kerberoastable");
            var unconstrainedDelegation = hygiene.Findings.Count(f => f.Status == "UnconstrainedDelegation");
            var lapsFindings = hygiene.Findings.Where(f => f.Status == "NoLAPS").ToList();
            var domainLevelFinding = hygiene.Findings.FirstOrDefault(f => f.Status == "DomainLevel");

            // Security highlights table
            sb.AppendLine("<h3>Security Highlights</h3>");
            sb.AppendLine("<table class='results-table'>");
            sb.AppendLine("<tr><th>Metric</th><th>Value</th><th>Status</th></tr>");

            if (domainLevelFinding != null)
            {
                var lvlClass = domainLevelFinding.Detail?.Contains("2008") == true || domainLevelFinding.Detail?.Contains("2003") == true ? "fail" : "pass";
                sb.AppendLine($"<tr class='{lvlClass}'><td>Domain Functional Level</td><td>{HtmlEncode(domainLevelFinding.Detail ?? "Unknown")}</td>");
                sb.AppendLine($"<td><span class='severity {(lvlClass == "fail" ? "critical" : "low")}'>{(lvlClass == "fail" ? "Outdated" : "OK")}</span></td></tr>");
            }

            sb.AppendLine($"<tr class='{(privilegedCount > 10 ? "fail" : privilegedCount > 5 ? "warn" : "pass")}'><td>Privileged Accounts</td><td>{privilegedCount}</td>");
            sb.AppendLine($"<td><span class='severity {(privilegedCount > 10 ? "high" : privilegedCount > 5 ? "medium" : "low")}'>{(privilegedCount > 10 ? "Excessive" : privilegedCount > 5 ? "Review" : "OK")}</span></td></tr>");

            var lapsPercent = hygiene.TotalMachines > 0 ? Math.Round((double)lapsFindings.Count / hygiene.TotalMachines * 100) : 0;
            var lapsCoverage = hygiene.TotalMachines > 0 ? 100 - lapsPercent : 0;
            sb.AppendLine($"<tr class='{(lapsCoverage < 50 ? "fail" : lapsCoverage < 90 ? "warn" : "pass")}'><td>LAPS Coverage</td><td>{lapsCoverage:F0}%</td>");
            sb.AppendLine($"<td><span class='severity {(lapsCoverage < 50 ? "critical" : lapsCoverage < 90 ? "medium" : "low")}'>{(lapsCoverage < 50 ? "Critical" : lapsCoverage < 90 ? "Needs Improvement" : "OK")}</span></td></tr>");

            sb.AppendLine($"<tr class='{(hygiene.DormantMachines > 20 ? "fail" : hygiene.DormantMachines > 5 ? "warn" : "pass")}'><td>Dormant Machines</td><td>{hygiene.DormantMachines}</td>");
            sb.AppendLine($"<td><span class='severity {(hygiene.DormantMachines > 20 ? "high" : hygiene.DormantMachines > 5 ? "medium" : "low")}'>{(hygiene.DormantMachines > 20 ? "Cleanup Needed" : hygiene.DormantMachines > 5 ? "Review" : "OK")}</span></td></tr>");

            sb.AppendLine($"<tr class='{(hygiene.PwdNeverExpire > 10 ? "fail" : hygiene.PwdNeverExpire > 0 ? "warn" : "pass")}'><td>Password Never Expires</td><td>{hygiene.PwdNeverExpire}</td>");
            sb.AppendLine($"<td><span class='severity {(hygiene.PwdNeverExpire > 10 ? "high" : hygiene.PwdNeverExpire > 0 ? "medium" : "low")}'>{(hygiene.PwdNeverExpire > 10 ? "Policy Violation" : hygiene.PwdNeverExpire > 0 ? "Review" : "OK")}</span></td></tr>");

            if (kerberoastable > 0)
            {
                sb.AppendLine($"<tr class='fail'><td>Kerberoastable Accounts</td><td>{kerberoastable}</td>");
                sb.AppendLine("<td><span class='severity critical'>Critical</span></td></tr>");
            }
            if (unconstrainedDelegation > 0)
            {
                sb.AppendLine($"<tr class='fail'><td>Unconstrained Delegation</td><td>{unconstrainedDelegation}</td>");
                sb.AppendLine("<td><span class='severity high'>High Risk</span></td></tr>");
            }

            sb.AppendLine("</table>");
            sb.AppendLine("</div></div>");
        }

        // ---- PAGE 5: NETWORK SECURITY ----
        if (riskyPorts.Count > 0 || threatCount > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Network Security", brand);
            sb.AppendLine("<div class='pb'>");

            if (riskyPorts.Count > 0)
            {
                sb.AppendLine("<h3>Risky Open Ports</h3>");
                // Group by port to show how many machines have each risky port
                var portGroups = riskyPorts
                    .GroupBy(p => new { p.Port, p.Service, p.Risk })
                    .Select(g => new { g.Key.Port, g.Key.Service, g.Key.Risk, MachineCount = g.Select(x => x.MachineId).Distinct().Count() })
                    .OrderByDescending(x => x.MachineCount)
                    .Take(10)
                    .ToList();

                sb.AppendLine("<table class='results-table'>");
                sb.AppendLine("<tr><th>Port</th><th>Service</th><th>Risk</th><th>Affected Devices</th></tr>");
                foreach (var pg in portGroups)
                {
                    sb.AppendLine("<tr class='fail'>");
                    sb.AppendLine($"<td>{pg.Port}</td>");
                    sb.AppendLine($"<td>{HtmlEncode(pg.Service ?? "Unknown")}</td>");
                    sb.AppendLine($"<td><span class='severity high'>{HtmlEncode(pg.Risk ?? "Risky")}</span></td>");
                    sb.AppendLine($"<td>{pg.MachineCount} / {totalMachines}</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }

            if (threatCount > 0)
            {
                sb.AppendLine("<h3>Threat Detections</h3>");
                // Group by category
                var threatCats = enrichment.Threats
                    .GroupBy(t => t.Category)
                    .Select(g => new { Category = g.Key, Count = g.Count(), MachineCount = g.Select(x => x.MachineId).Distinct().Count() })
                    .OrderByDescending(x => x.Count)
                    .ToList();

                sb.AppendLine($"<div class='insight-box fail-box'><p>Detected <strong>{threatCount}</strong> threat signatures across <strong>{threatCats.Count}</strong> categories on <strong>{enrichment.Threats.Select(t => t.MachineId).Distinct().Count()}</strong> devices.</p></div>");

                sb.AppendLine("<table class='results-table'>");
                sb.AppendLine("<tr><th>Category</th><th>Signatures</th><th>Devices Affected</th></tr>");
                foreach (var tc in threatCats)
                {
                    sb.AppendLine("<tr class='fail'>");
                    sb.AppendLine($"<td>{HtmlEncode(tc.Category)}</td>");
                    sb.AppendLine($"<td>{tc.Count}</td>");
                    sb.AppendLine($"<td>{tc.MachineCount}</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }

            sb.AppendLine("</div></div>");
        }

        // ---- PAGE 6: FLEET OVERVIEW ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Fleet Overview", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<table class='fleet-table'>");
        sb.AppendLine("<tr><th>Hostname</th><th>OS</th><th>Score</th><th>Grade</th><th>Pass</th><th>Warn</th><th>Fail</th></tr>");
        foreach (var run in runs.OrderBy(r => r.GlobalScore))
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

        // ---- PAGE 7: RECOMMENDATIONS ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Recommendations", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='insight-box'><p>Based on our comprehensive assessment of <strong>{0}</strong> devices, we recommend the following remediation actions, prioritized by risk impact.</p></div>".Replace("{0}", totalMachines.ToString()));

        AppendDataDrivenRecommendations(sb, runs, allResults, hygiene, enrichment, brand);

        sb.AppendLine("</div></div>");

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
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment)
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

        // Fleet Overview with hardware details
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Fleet Overview", brand);
        sb.AppendLine("<div class='pb'>");

        // Hardware summary stats
        var tpmCount = runs.Count(r => r.Machine.TpmPresent == true);
        var secBootCount = runs.Count(r => r.Machine.SecureBoot == true);
        var bitlockerCount = runs.Count(r => r.Machine.Bitlocker == true);
        var domainJoined = runs.Count(r => r.Machine.DomainStatus != null && r.Machine.DomainStatus != "Workgroup");

        sb.AppendLine("<h3>Hardware Security Summary</h3>");
        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat {(tpmCount == totalMachines ? "pass-stat" : tpmCount > 0 ? "warn-stat" : "fail-stat")}'><span class='stat-value'>{tpmCount}/{totalMachines}</span><span class='stat-label'>TPM Present</span></div>");
        sb.AppendLine($"<div class='stat {(secBootCount == totalMachines ? "pass-stat" : secBootCount > 0 ? "warn-stat" : "fail-stat")}'><span class='stat-value'>{secBootCount}/{totalMachines}</span><span class='stat-label'>Secure Boot</span></div>");
        sb.AppendLine($"<div class='stat {(bitlockerCount == totalMachines ? "pass-stat" : bitlockerCount > 0 ? "warn-stat" : "fail-stat")}'><span class='stat-value'>{bitlockerCount}/{totalMachines}</span><span class='stat-label'>BitLocker</span></div>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{domainJoined}/{totalMachines}</span><span class='stat-label'>Domain Joined</span></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<table class='fleet-table'>");
        sb.AppendLine("<tr><th>Hostname</th><th>OS</th><th>CPU</th><th>RAM</th><th>TPM</th><th>BitLocker</th><th>Score</th><th>Grade</th></tr>");
        foreach (var run in runs.OrderByDescending(r => r.GlobalScore))
        {
            var rowClass = (run.GlobalScore ?? 0) >= 80 ? "pass" : (run.GlobalScore ?? 0) >= 60 ? "warn" : "fail";
            sb.AppendLine($"<tr class='{rowClass}'>");
            sb.AppendLine($"<td class='hostname'>{HtmlEncode(run.Machine.Hostname)}</td>");
            sb.AppendLine($"<td>{HtmlEncode(run.Machine.OsName ?? "N/A")}</td>");
            sb.AppendLine($"<td>{HtmlEncode(TruncateCpu(run.Machine.CpuName))}</td>");
            sb.AppendLine($"<td>{run.Machine.RamGb ?? 0} GB</td>");
            sb.AppendLine($"<td>{(run.Machine.TpmPresent == true ? "Yes" : "No")}</td>");
            sb.AppendLine($"<td>{(run.Machine.Bitlocker == true ? "Yes" : "No")}</td>");
            sb.AppendLine($"<td><strong>{run.GlobalScore:F1}%</strong></td>");
            sb.AppendLine($"<td><span class='grade-mini grade-{run.Grade?.Replace("+", "plus")}'>{HtmlEncode(run.Grade ?? "N/A")}</span></td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
        sb.AppendLine("</div></div>");

        // Disk inventory page
        if (enrichment.Disks.Count > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Disk Inventory", brand);
            sb.AppendLine("<div class='pb'>");

            // Group disks by machine
            var disksByMachine = enrichment.Disks.GroupBy(d => d.MachineId).ToList();
            var machineNameMap = runs.ToDictionary(r => r.MachineId, r => r.Machine.Hostname);

            sb.AppendLine("<table class='results-table'>");
            sb.AppendLine("<tr><th>Machine</th><th>Drive</th><th>Type</th><th>Size</th><th>Free</th><th>FS</th><th>Usage</th></tr>");
            foreach (var group in disksByMachine)
            {
                var hostname = machineNameMap.GetValueOrDefault(group.Key, "Unknown");
                foreach (var d in group)
                {
                    var usedPct = d.TotalGb > 0 ? Math.Round((1.0 - (double)(d.FreeGb ?? 0) / d.TotalGb.Value) * 100) : 0;
                    var rowClass = usedPct > 90 ? "fail" : usedPct > 75 ? "warn" : "pass";
                    sb.AppendLine($"<tr class='{rowClass}'>");
                    sb.AppendLine($"<td class='hostname'>{HtmlEncode(hostname)}</td>");
                    sb.AppendLine($"<td>{HtmlEncode(d.DriveLetter)}</td>");
                    sb.AppendLine($"<td>{HtmlEncode(d.DiskType ?? "N/A")}</td>");
                    sb.AppendLine($"<td class='num'>{d.TotalGb ?? 0} GB</td>");
                    sb.AppendLine($"<td class='num'>{d.FreeGb ?? 0:F1} GB</td>");
                    sb.AppendLine($"<td>{HtmlEncode(d.FileSystem ?? "N/A")}</td>");
                    sb.AppendLine($"<td class='num'><strong>{usedPct:F0}%</strong></td>");
                    sb.AppendLine("</tr>");
                }
            }
            sb.AppendLine("</table>");
            sb.AppendLine("</div></div>");
        }

        // Port scan page
        if (enrichment.Ports.Count > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Network Port Scan", brand);
            sb.AppendLine("<div class='pb'>");

            var riskyPorts = enrichment.Ports.Where(p => p.Risk != null).ToList();
            var machinesWithRiskyPorts = riskyPorts.Select(p => p.MachineId).Distinct().Count();

            sb.AppendLine("<div class='summary-grid'>");
            sb.AppendLine($"<div class='stat'><span class='stat-value'>{enrichment.Ports.Count}</span><span class='stat-label'>Total Open Ports</span></div>");
            sb.AppendLine($"<div class='stat {(riskyPorts.Count > 0 ? "fail-stat" : "pass-stat")}'><span class='stat-value'>{riskyPorts.Count}</span><span class='stat-label'>Risky Ports</span></div>");
            sb.AppendLine($"<div class='stat'><span class='stat-value'>{machinesWithRiskyPorts}</span><span class='stat-label'>Devices with Risky Ports</span></div>");
            sb.AppendLine("</div>");

            if (riskyPorts.Count > 0)
            {
                sb.AppendLine("<h3>Risky Ports by Machine</h3>");
                var machineNameMap = runs.ToDictionary(r => r.MachineId, r => r.Machine.Hostname);
                sb.AppendLine("<table class='results-table'>");
                sb.AppendLine("<tr><th>Machine</th><th>Port</th><th>Protocol</th><th>Service</th><th>Risk</th></tr>");
                foreach (var p in riskyPorts.Take(50))
                {
                    var hostname = machineNameMap.GetValueOrDefault(p.MachineId, "Unknown");
                    sb.AppendLine("<tr class='fail'>");
                    sb.AppendLine($"<td class='hostname'>{HtmlEncode(hostname)}</td>");
                    sb.AppendLine($"<td>{p.Port}</td>");
                    sb.AppendLine($"<td>{HtmlEncode(p.Protocol)}</td>");
                    sb.AppendLine($"<td>{HtmlEncode(p.Service ?? "Unknown")}</td>");
                    sb.AppendLine($"<td><span class='severity high'>{HtmlEncode(p.Risk ?? "Risky")}</span></td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
                if (riskyPorts.Count > 50)
                    sb.AppendLine($"<p class='cat-summary'>Showing 50 of {riskyPorts.Count} risky ports.</p>");
            }

            sb.AppendLine("</div></div>");
        }

        // Threat detections page
        if (enrichment.Threats.Count > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "Threat Detections", brand);
            sb.AppendLine("<div class='pb'>");

            var threatCats = enrichment.Threats
                .GroupBy(t => t.Category)
                .Select(g => new { Category = g.Key, Count = g.Count(), MachineCount = g.Select(x => x.MachineId).Distinct().Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            sb.AppendLine($"<div class='insight-box fail-box'><p>Detected <strong>{enrichment.Threats.Count}</strong> threat signatures across <strong>{threatCats.Count}</strong> categories.</p></div>");

            sb.AppendLine("<h3>Threats by Category</h3>");
            sb.AppendLine("<table class='results-table'>");
            sb.AppendLine("<tr><th>Category</th><th>Signatures</th><th>Devices</th></tr>");
            foreach (var tc in threatCats)
            {
                sb.AppendLine("<tr class='fail'>");
                sb.AppendLine($"<td>{HtmlEncode(tc.Category)}</td>");
                sb.AppendLine($"<td class='num'>{tc.Count}</td>");
                sb.AppendLine($"<td class='num'>{tc.MachineCount}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table>");

            // Detailed threat list
            var machineNameMap2 = runs.ToDictionary(r => r.MachineId, r => r.Machine.Hostname);
            sb.AppendLine("<h3>Detailed Findings</h3>");
            sb.AppendLine("<table class='results-table'>");
            sb.AppendLine("<tr><th>Machine</th><th>Threat</th><th>Category</th><th>Severity</th><th>Vector</th></tr>");
            foreach (var t in enrichment.Threats.Take(50))
            {
                var hostname = machineNameMap2.GetValueOrDefault(t.MachineId, "Unknown");
                sb.AppendLine("<tr class='fail'>");
                sb.AppendLine($"<td class='hostname'>{HtmlEncode(hostname)}</td>");
                sb.AppendLine($"<td>{HtmlEncode(t.ThreatName)}</td>");
                sb.AppendLine($"<td>{HtmlEncode(t.Category)}</td>");
                sb.AppendLine($"<td><span class='severity {HtmlEncode(t.Severity)}'>{HtmlEncode(t.Severity)}</span></td>");
                sb.AppendLine($"<td>{HtmlEncode(t.Vector)}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
            if (enrichment.Threats.Count > 50)
                sb.AppendLine($"<p class='cat-summary'>Showing 50 of {enrichment.Threats.Count} threats.</p>");

            sb.AppendLine("</div></div>");
        }

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
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment)
    {
        var reportTitle = frameworkName != null ? $"{frameworkName} Security Posture" : "Security Posture Assessment";
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        var avgScore = runs.Average(r => r.GlobalScore ?? 0);
        var orgGrade = GetGrade(avgScore);
        var scanDate = runs.Max(r => r.CompletedAt ?? r.StartedAt);
        var totalFail = runs.Sum(r => r.FailCount ?? 0);
        var riskyPorts = enrichment.Ports.Where(p => p.Risk != null).ToList();
        var threatCount = enrichment.Threats.Count;

        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true);

        // ---- PAGE 1: COVER ----
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

        // ---- PAGE 2: WHAT WE FOUND (THE HOOK) ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "What We Found", brand);
        sb.AppendLine("<div class='pb'>");

        // Big scary number
        var totalIssues = totalFail + threatCount + riskyPorts.Count;
        sb.AppendLine("<div class='big-number-box'>");
        sb.AppendLine($"<div class='big-number'>{totalIssues}</div>");
        sb.AppendLine("<div class='big-number-label'>security issues identified in your network</div>");
        sb.AppendLine("</div>");

        // Build headline findings dynamically
        sb.AppendLine("<div class='headline-findings'>");

        // AD hygiene headlines
        if (hygiene != null)
        {
            var lapsFindings = hygiene.Findings.Count(f => f.Status == "NoLAPS");
            var lapsCoverage = hygiene.TotalMachines > 0 ? Math.Round(100.0 - (double)lapsFindings / hygiene.TotalMachines * 100) : 100;

            if (lapsCoverage < 50)
            {
                sb.AppendLine("<div class='headline-item fail-box'>");
                sb.AppendLine($"<div class='headline-icon'>!</div>");
                sb.AppendLine($"<div class='headline-text'><strong>{lapsCoverage:F0}% LAPS coverage</strong> &mdash; local administrator passwords are not being managed, allowing lateral movement across your network with a single compromised credential.</div>");
                sb.AppendLine("</div>");
            }

            if (hygiene.DormantMachines > 10)
            {
                sb.AppendLine("<div class='headline-item fail-box'>");
                sb.AppendLine($"<div class='headline-icon'>!</div>");
                sb.AppendLine($"<div class='headline-text'><strong>{hygiene.DormantMachines} ghost computers</strong> are still listed in your Active Directory. These orphaned objects expand your attack surface and indicate a lack of lifecycle management.</div>");
                sb.AppendLine("</div>");
            }

            if (hygiene.PwdNeverExpire > 10)
            {
                sb.AppendLine("<div class='headline-item fail-box'>");
                sb.AppendLine($"<div class='headline-icon'>!</div>");
                sb.AppendLine($"<div class='headline-text'><strong>{hygiene.PwdNeverExpire} user accounts</strong> have passwords that never expire. This violates every major compliance framework and makes credential theft significantly more dangerous.</div>");
                sb.AppendLine("</div>");
            }

            var domainLevelFinding = hygiene.Findings.FirstOrDefault(f => f.Status == "DomainLevel");
            if (domainLevelFinding != null && (domainLevelFinding.Detail?.Contains("2008") == true || domainLevelFinding.Detail?.Contains("2003") == true))
            {
                sb.AppendLine("<div class='headline-item fail-box'>");
                sb.AppendLine($"<div class='headline-icon'>!</div>");
                sb.AppendLine($"<div class='headline-text'>Your domain is running at <strong>{HtmlEncode(domainLevelFinding.Detail ?? "an outdated")} functional level</strong>. This prevents the use of modern security features and leaves your environment vulnerable to well-known attacks.</div>");
                sb.AppendLine("</div>");
            }
        }

        // Port scan headlines
        var rdpMachineCount = riskyPorts.Where(p => p.Port == 3389).Select(p => p.MachineId).Distinct().Count();
        if (rdpMachineCount > 0)
        {
            sb.AppendLine("<div class='headline-item fail-box'>");
            sb.AppendLine($"<div class='headline-icon'>!</div>");
            sb.AppendLine($"<div class='headline-text'><strong>RDP is open on {rdpMachineCount} machines</strong> &mdash; Remote Desktop Protocol is the number one attack vector for ransomware. Every exposed RDP port is a direct entry point for attackers.</div>");
            sb.AppendLine("</div>");
        }

        // Threat headlines
        if (threatCount > 0)
        {
            var threatCategories = enrichment.Threats.Select(t => t.Category).Distinct().ToList();
            sb.AppendLine("<div class='headline-item fail-box'>");
            sb.AppendLine($"<div class='headline-icon'>!</div>");
            sb.AppendLine($"<div class='headline-text'>We detected <strong>{threatCount} threat signatures</strong> including {HtmlEncode(string.Join(", ", threatCategories.Take(3)))}. This indicates potentially compromised systems that require immediate investigation.</div>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>"); // headline-findings

        sb.AppendLine("</div></div>");

        // ---- PAGE 3: YOUR RISK EXPOSURE ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Your Risk Exposure", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='insight-box'>");
        sb.AppendLine($"<p>We assessed <strong>{totalMachines}</strong> devices in your environment against industry security standards. Your organization scores <strong>{avgScore:F1}%</strong> on average. ");
        if (avgScore < 60)
            sb.AppendLine("This is significantly below the industry average of 60-80% and indicates critical risk exposure.</p>");
        else if (avgScore < 80)
            sb.AppendLine("While not critically low, this is below the target range of 80%+ that compliance frameworks recommend.</p>");
        else
            sb.AppendLine("This places your organization in a solid position, though targeted improvements can further reduce risk.</p>");
        sb.AppendLine("</div>");

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

        sb.AppendLine("</div></div>");

        // ---- PAGE 4: WHAT'S LURKING IN YOUR NETWORK ----
        if (threatCount > 0 || riskyPorts.Count > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, "What's Lurking in Your Network", brand);
            sb.AppendLine("<div class='pb'>");

            if (threatCount > 0)
            {
                var threatCats = enrichment.Threats
                    .GroupBy(t => t.Category)
                    .Select(g => new { Category = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ToList();

                sb.AppendLine("<h3>Threat Detections</h3>");
                sb.AppendLine($"<div class='insight-box fail-box'><p>Our deep scan detected <strong>{threatCount}</strong> threat signatures across your network. These include known malware families, suspicious tools, and potentially unwanted software.</p></div>");

                sb.AppendLine("<table class='results-table'>");
                sb.AppendLine("<tr><th>Category</th><th>Detections</th></tr>");
                foreach (var tc in threatCats)
                {
                    sb.AppendLine($"<tr class='fail'><td>{HtmlEncode(tc.Category)}</td><td class='num'>{tc.Count}</td></tr>");
                }
                sb.AppendLine("</table>");
            }

            if (riskyPorts.Count > 0)
            {
                sb.AppendLine("<h3>Risky Open Ports</h3>");
                var portGroups = riskyPorts
                    .GroupBy(p => new { p.Port, p.Service })
                    .Select(g => new { g.Key.Port, g.Key.Service, MachineCount = g.Select(x => x.MachineId).Distinct().Count() })
                    .OrderByDescending(x => x.MachineCount)
                    .Take(8)
                    .ToList();

                sb.AppendLine("<table class='results-table'>");
                sb.AppendLine("<tr><th>Port</th><th>Service</th><th>Exposed Devices</th></tr>");
                foreach (var pg in portGroups)
                {
                    sb.AppendLine($"<tr class='fail'><td>{pg.Port}</td><td>{HtmlEncode(pg.Service ?? "Unknown")}</td><td>{pg.MachineCount} devices</td></tr>");
                }
                sb.AppendLine("</table>");

                if (rdpMachineCount > 0)
                    sb.AppendLine($"<div class='insight-box fail-box'><p>RDP (port 3389) is open on <strong>{rdpMachineCount}</strong> machines. This is the most common entry point for ransomware attacks.</p></div>");
            }

            sb.AppendLine("</div></div>");
        }

        // ---- PAGE 5: THE COST OF INACTION ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "The Cost of Inaction", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='insight-box'>");
        sb.AppendLine("<p>Organizations of comparable size face an average data breach cost of <strong>$4.45 million</strong> (IBM, 2024). Ransomware attacks targeting small and mid-size businesses increased by <strong>150%</strong> in the past two years. The median downtime after a ransomware attack is <strong>22 days</strong>.</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("<h3>Your Specific Risk Factors</h3>");
        sb.AppendLine("<div class='risk-cards'>");
        var riskNum = 1;

        if (avgScore < 60)
        {
            sb.AppendLine($"<div class='risk-card'><div class='risk-num'>{riskNum++}</div><div class='risk-body'><strong>Below-average compliance ({avgScore:F0}%)</strong>");
            sb.AppendLine("<p class='risk-detail'>Your environment falls significantly below industry standards, making it an easier target for automated attacks.</p></div></div>");
        }

        if (rdpMachineCount > 0)
        {
            sb.AppendLine($"<div class='risk-card'><div class='risk-num'>{riskNum++}</div><div class='risk-body'><strong>RDP exposed on {rdpMachineCount} machines</strong>");
            sb.AppendLine("<p class='risk-detail'>Exposed RDP combined with weak credentials could allow an attacker to gain initial access in minutes.</p></div></div>");
        }

        if (hygiene != null)
        {
            var lapsFindings = hygiene.Findings.Count(f => f.Status == "NoLAPS");
            var lapsCoverage = hygiene.TotalMachines > 0 ? Math.Round(100.0 - (double)lapsFindings / hygiene.TotalMachines * 100) : 100;

            if (lapsCoverage < 50)
            {
                sb.AppendLine($"<div class='risk-card'><div class='risk-num'>{riskNum++}</div><div class='risk-body'><strong>No LAPS deployment ({lapsCoverage:F0}% coverage)</strong>");
                sb.AppendLine("<p class='risk-detail'>Without LAPS, a single compromised local admin password gives access to every machine in your network.</p></div></div>");
            }

            if (hygiene.PwdNeverExpire > 10)
            {
                sb.AppendLine($"<div class='risk-card'><div class='risk-num'>{riskNum++}</div><div class='risk-body'><strong>{hygiene.PwdNeverExpire} accounts with non-expiring passwords</strong>");
                sb.AppendLine("<p class='risk-detail'>Credentials that never rotate are prime targets for credential stuffing and pass-the-hash attacks.</p></div></div>");
            }
        }

        if (threatCount > 0)
        {
            sb.AppendLine($"<div class='risk-card'><div class='risk-num'>{riskNum++}</div><div class='risk-body'><strong>{threatCount} threat signatures already present</strong>");
            sb.AppendLine("<p class='risk-detail'>Existing threats in your environment indicate active or past compromise that requires immediate forensic investigation.</p></div></div>");
        }

        // Fill up to at least 3 risk items with generic ones if needed
        if (riskNum <= 3)
        {
            var bitlockerMissing = runs.Count(r => r.Machine.Bitlocker != true);
            if (bitlockerMissing > 0)
            {
                sb.AppendLine($"<div class='risk-card'><div class='risk-num'>{riskNum++}</div><div class='risk-body'><strong>{bitlockerMissing} devices without disk encryption</strong>");
                sb.AppendLine("<p class='risk-detail'>Lost or stolen devices without encryption expose all data stored on them, potentially triggering breach notification requirements.</p></div></div>");
            }
        }

        sb.AppendLine("</div>"); // risk-cards

        sb.AppendLine("</div></div>");

        // ---- PAGE 6: OUR RECOMMENDATION ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, "Our Recommendation", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='recommendation-box'>");
        sb.AppendLine($"<h3>A 3-Phase Remediation Plan</h3>");
        sb.AppendLine($"<p>Based on our assessment of <strong>{totalMachines}</strong> devices in <strong>{HtmlEncode(org.Name)}</strong>'s environment, we recommend a structured approach:</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='next-steps'>");

        // Phase 1: Immediate
        sb.AppendLine("<div class='step'><span class='step-num'>1</span><div>");
        sb.AppendLine("<strong>Phase 1: Immediate (Week 1)</strong>");
        sb.AppendLine("<ul class='phase-list'>");
        if (threatCount > 0)
            sb.AppendLine("<li>Investigate and remediate detected threat signatures</li>");
        if (rdpMachineCount > 0)
            sb.AppendLine("<li>Close or restrict RDP access on exposed machines</li>");
        if (hygiene != null)
        {
            var lapsCount = hygiene.Findings.Count(f => f.Status == "NoLAPS");
            if (lapsCount > 0)
                sb.AppendLine($"<li>Deploy LAPS across all {hygiene.TotalMachines} machines</li>");
        }
        sb.AppendLine("<li>Patch critical security vulnerabilities</li>");
        sb.AppendLine("</ul></div></div>");

        // Phase 2: Short-term
        sb.AppendLine("<div class='step'><span class='step-num'>2</span><div>");
        sb.AppendLine("<strong>Phase 2: Short-term (Month 1)</strong>");
        sb.AppendLine("<ul class='phase-list'>");
        if (hygiene != null)
        {
            if (hygiene.DormantMachines > 0)
                sb.AppendLine($"<li>Clean up {hygiene.DormantMachines} dormant computer objects from AD</li>");
            if (hygiene.PwdNeverExpire > 0)
                sb.AppendLine($"<li>Enforce password expiration policy for {hygiene.PwdNeverExpire} accounts</li>");
            var domainLevel = hygiene.Findings.FirstOrDefault(f => f.Status == "DomainLevel");
            if (domainLevel != null && (domainLevel.Detail?.Contains("2008") == true || domainLevel.Detail?.Contains("2003") == true))
                sb.AppendLine("<li>Upgrade domain functional level to 2016 or later</li>");
        }
        sb.AppendLine("<li>Implement security baseline hardening across the fleet</li>");
        sb.AppendLine("</ul></div></div>");

        // Phase 3: Ongoing
        sb.AppendLine("<div class='step'><span class='step-num'>3</span><div>");
        sb.AppendLine("<strong>Phase 3: Ongoing</strong>");
        sb.AppendLine("<ul class='phase-list'>");
        sb.AppendLine("<li>Monthly security assessments with trending reports</li>");
        sb.AppendLine("<li>Continuous compliance monitoring</li>");
        sb.AppendLine("<li>Quarterly AD hygiene reviews</li>");
        sb.AppendLine("</ul></div></div>");

        sb.AppendLine("</div>"); // next-steps

        sb.AppendLine($"<div class='cta-box'>");
        sb.AppendLine($"<p>Contact <strong>{HtmlEncode(brand.CompanyName)}</strong> to discuss your customized remediation plan and managed security services.</p>");
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

    private static string TruncateCpu(string? cpuName)
    {
        if (cpuName == null) return "N/A";
        // Shorten long CPU names for tables
        var name = cpuName.Replace("(R)", "").Replace("(TM)", "").Replace("CPU ", "").Trim();
        return name.Length > 30 ? name[..27] + "..." : name;
    }

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

    private static void AppendDiskTable(StringBuilder sb, List<MachineDisk> disks)
    {
        sb.AppendLine("<table class='results-table'>");
        sb.AppendLine("<tr><th>Drive</th><th>Label</th><th>Type</th><th>Size</th><th>Free</th><th>FS</th><th>Usage</th></tr>");
        foreach (var d in disks)
        {
            var usedPct = d.TotalGb > 0 ? Math.Round((1.0 - (double)(d.FreeGb ?? 0) / d.TotalGb.Value) * 100) : 0;
            var rowClass = usedPct > 90 ? "fail" : usedPct > 75 ? "warn" : "pass";
            sb.AppendLine($"<tr class='{rowClass}'>");
            sb.AppendLine($"<td><strong>{HtmlEncode(d.DriveLetter)}</strong></td>");
            sb.AppendLine($"<td>{HtmlEncode(d.Label ?? "\u2014")}</td>");
            sb.AppendLine($"<td>{HtmlEncode(d.DiskType ?? "N/A")}</td>");
            sb.AppendLine($"<td class='num'>{d.TotalGb ?? 0} GB</td>");
            sb.AppendLine($"<td class='num'>{d.FreeGb ?? 0:F1} GB</td>");
            sb.AppendLine($"<td>{HtmlEncode(d.FileSystem ?? "N/A")}</td>");
            sb.AppendLine($"<td class='num'><strong>{usedPct:F0}%</strong></td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
    }

    private static void AppendPortTable(StringBuilder sb, List<MachinePort> ports)
    {
        sb.AppendLine("<table class='results-table'>");
        sb.AppendLine("<tr><th>Port</th><th>Protocol</th><th>Status</th><th>Service</th><th>Risk</th></tr>");
        foreach (var p in ports.Take(30))
        {
            var rowClass = p.Risk != null ? "fail" : "pass";
            sb.AppendLine($"<tr class='{rowClass}'>");
            sb.AppendLine($"<td>{p.Port}</td>");
            sb.AppendLine($"<td>{HtmlEncode(p.Protocol)}</td>");
            sb.AppendLine($"<td>{HtmlEncode(p.Status)}</td>");
            sb.AppendLine($"<td>{HtmlEncode(p.Service ?? "\u2014")}</td>");
            sb.AppendLine($"<td>{(p.Risk != null ? $"<span class='severity high'>{HtmlEncode(p.Risk)}</span>" : "<span class='severity low'>OK</span>")}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
        if (ports.Count > 30)
            sb.AppendLine($"<p class='cat-summary'>Showing 30 of {ports.Count} ports.</p>");
    }

    private static void AppendThreatTable(StringBuilder sb, List<MachineThreat> threats)
    {
        sb.AppendLine("<table class='results-table'>");
        sb.AppendLine("<tr><th>Threat</th><th>Category</th><th>Severity</th><th>Vector</th><th>Detail</th></tr>");
        foreach (var t in threats.Take(20))
        {
            sb.AppendLine("<tr class='fail'>");
            sb.AppendLine($"<td><strong>{HtmlEncode(t.ThreatName)}</strong></td>");
            sb.AppendLine($"<td>{HtmlEncode(t.Category)}</td>");
            sb.AppendLine($"<td><span class='severity {HtmlEncode(t.Severity)}'>{HtmlEncode(t.Severity)}</span></td>");
            sb.AppendLine($"<td>{HtmlEncode(t.Vector)}</td>");
            sb.AppendLine($"<td>{HtmlEncode(t.Detail ?? "\u2014")}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
        if (threats.Count > 20)
            sb.AppendLine($"<p class='cat-summary'>Showing 20 of {threats.Count} threats.</p>");
    }

    private static void AppendDataDrivenRecommendations(StringBuilder sb, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, HygieneScanDto? hygiene, OrgEnrichment enrichment,
        ReportBranding brand)
    {
        var recommendations = new List<(string Priority, string Title, string Description, string Effort)>();
        var totalMachines = runs.Count;

        // AD hygiene recommendations
        if (hygiene != null)
        {
            var domainLevel = hygiene.Findings.FirstOrDefault(f => f.Status == "DomainLevel");
            if (domainLevel != null && (domainLevel.Detail?.Contains("2008") == true || domainLevel.Detail?.Contains("2003") == true))
                recommendations.Add(("Critical", "Upgrade Domain Functional Level",
                    $"Current level ({HtmlEncode(domainLevel.Detail ?? "outdated")}) prevents modern security features like Protected Users group and authentication silos.",
                    "1-2 weeks"));

            var lapsCount = hygiene.Findings.Count(f => f.Status == "NoLAPS");
            if (lapsCount > 0)
                recommendations.Add(("Critical", $"Deploy LAPS Across {hygiene.TotalMachines} Machines",
                    $"Currently {Math.Round(100.0 - (double)lapsCount / Math.Max(hygiene.TotalMachines, 1) * 100):F0}% coverage. LAPS prevents lateral movement by ensuring unique local admin passwords.",
                    "1-2 days"));

            if (hygiene.DormantMachines > 10)
                recommendations.Add(("High", $"Remove {hygiene.DormantMachines} Dormant Computer Objects",
                    "Orphaned computer accounts expand the attack surface and indicate poor lifecycle management.",
                    "1 day"));

            if (hygiene.PwdNeverExpire > 10)
                recommendations.Add(("High", $"Enforce Password Expiration for {hygiene.PwdNeverExpire} Accounts",
                    "Non-expiring passwords violate NIST, CIS, and HIPAA requirements.",
                    "1-2 days"));

            var privileged = hygiene.Findings.Count(f => f.Status == "Privileged");
            if (privileged > 10)
                recommendations.Add(("High", $"Review {privileged} Privileged Accounts",
                    "Excessive admin accounts increase the blast radius of credential compromise.",
                    "1-2 days"));
        }

        // Port-based recommendations
        var riskyPorts = enrichment.Ports.Where(p => p.Risk != null).ToList();
        var rdpCount = riskyPorts.Where(p => p.Port == 3389).Select(p => p.MachineId).Distinct().Count();
        if (rdpCount > 0)
            recommendations.Add(("Critical", $"Close or Restrict RDP on {rdpCount} Machines",
                "RDP is the leading ransomware attack vector. Implement VPN or RD Gateway instead of direct exposure.",
                "1-3 days"));

        // Threat-based recommendations
        if (enrichment.Threats.Count > 0)
            recommendations.Add(("Critical", $"Investigate {enrichment.Threats.Count} Threat Detections",
                $"Active threats detected across {enrichment.Threats.Select(t => t.MachineId).Distinct().Count()} devices. Immediate forensic analysis recommended.",
                "Immediate"));

        // Hardware recommendations
        var noBitlocker = runs.Count(r => r.Machine.Bitlocker != true);
        if (noBitlocker > 0)
            recommendations.Add(("Medium", $"Enable BitLocker on {noBitlocker} Devices",
                "Unencrypted drives expose data if devices are lost or stolen.",
                "1-2 days"));

        var noTpm = runs.Count(r => r.Machine.TpmPresent != true);
        if (noTpm > 0 && noTpm < totalMachines)
            recommendations.Add(("Medium", $"Enable TPM on {noTpm} Devices",
                "TPM enables hardware-backed security features including BitLocker and Windows Hello.",
                "Varies"));

        // Compliance-based recommendations
        var worstCategories = allResults
            .GroupBy(r => r.Category)
            .Select(g => new { Category = g.Key, Pct = g.Count() > 0 ? Math.Round((double)g.Count(x => x.Status == "pass") / g.Count() * 100) : 0 })
            .Where(x => x.Pct < 50)
            .OrderBy(x => x.Pct)
            .Take(3)
            .ToList();

        foreach (var cat in worstCategories)
        {
            recommendations.Add(("Medium", $"Harden {cat.Category} ({cat.Pct:F0}% compliance)",
                $"This category is significantly below target. Addressing these controls will have the highest impact on overall compliance.",
                "1-2 weeks"));
        }

        // Render recommendations table (top 10)
        sb.AppendLine("<table class='results-table'>");
        sb.AppendLine("<tr><th>#</th><th>Priority</th><th>Recommendation</th><th>Effort</th></tr>");
        var recNum = 1;
        foreach (var rec in recommendations.Take(10))
        {
            var sevClass = rec.Priority == "Critical" ? "critical" : rec.Priority == "High" ? "high" : "medium";
            var rowClass = rec.Priority == "Critical" ? "fail" : rec.Priority == "High" ? "warn" : "pass";
            sb.AppendLine($"<tr class='{rowClass}'>");
            sb.AppendLine($"<td class='num'>{recNum++}</td>");
            sb.AppendLine($"<td><span class='severity {sevClass}'>{HtmlEncode(rec.Priority)}</span></td>");
            sb.AppendLine($"<td><strong>{rec.Title}</strong><br><span style='font-size:11px;color:#666'>{rec.Description}</span></td>");
            sb.AppendLine($"<td>{HtmlEncode(rec.Effort)}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
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
        .results-table tr:nth-child(even), .fleet-table tr:nth-child(even) { background: #fafafa; }
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
        .insight-box.fail-box { background: #fef2f2; border-color: #fecaca; }

        /* Big number box (presales) */
        .big-number-box { text-align: center; padding: 32px 20px; margin: 16px 0; background: #fef2f2;
                          border: 2px solid #C0392B; border-radius: 12px; }
        .big-number { font-size: 72px; font-weight: 900; color: #C0392B; line-height: 1; }
        .big-number-label { font-size: 16px; font-weight: 600; color: #666; margin-top: 8px; }

        /* Headline findings (presales) */
        .headline-findings { margin: 20px 0; }
        .headline-item { display: flex; gap: 14px; padding: 14px 18px; border-radius: 8px; margin-bottom: 10px;
                         align-items: flex-start; background: #fef2f2; border: 1px solid #fecaca; }
        .headline-icon { background: #C0392B; color: #fff; width: 28px; height: 28px; border-radius: 50%;
                         display: flex; align-items: center; justify-content: center; font-weight: 900;
                         font-size: 16px; flex-shrink: 0; }
        .headline-text { flex: 1; font-size: 13px; line-height: 1.6; }
        .headline-text strong { color: #7f1d1d; }

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

        /* Phase lists */
        .phase-list { margin: 6px 0 0 16px; font-size: 12px; color: #555; }
        .phase-list li { margin-bottom: 4px; }

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
        .results-table tr:nth-child(even), .fleet-table tr:nth-child(even) { background: #fafafa; }
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
        .insight-box.fail-box { background: #fef2f2; border-color: #fecaca; }

        /* Big number box (presales) */
        .big-number-box { text-align: center; padding: 32px 20px; margin: 16px 0; background: #fef2f2;
                          border: 2px solid #C0392B; border-radius: 12px; }
        .big-number { font-size: 72px; font-weight: 900; color: #C0392B; line-height: 1; }
        .big-number-label { font-size: 16px; font-weight: 600; color: #666; margin-top: 8px; }

        /* Headline findings (presales) */
        .headline-findings { margin: 20px 0; }
        .headline-item { display: flex; gap: 14px; padding: 14px 18px; border-radius: 8px; margin-bottom: 10px;
                         align-items: flex-start; background: #fef2f2; border: 1px solid #fecaca; }
        .headline-icon { background: #C0392B; color: #fff; width: 28px; height: 28px; border-radius: 50%;
                         display: flex; align-items: center; justify-content: center; font-weight: 900;
                         font-size: 16px; flex-shrink: 0; }
        .headline-text { flex: 1; font-size: 13px; line-height: 1.6; }
        .headline-text strong { color: #7f1d1d; }

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

        /* Phase lists */
        .phase-list { margin: 6px 0 0 16px; font-size: 12px; color: #555; }
        .phase-list li { margin-bottom: 4px; }

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

internal class MachineEnrichment
{
    public List<MachineDisk> Disks { get; set; } = [];
    public List<MachinePort> Ports { get; set; } = [];
    public List<MachineThreat> Threats { get; set; } = [];
}

internal class OrgEnrichment
{
    public List<MachineDisk> Disks { get; set; } = [];
    public List<MachinePort> Ports { get; set; } = [];
    public List<MachineThreat> Threats { get; set; } = [];
}
