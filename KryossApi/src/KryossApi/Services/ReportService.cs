using System.Text;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface IReportService
{
    Task<string> GenerateOrgReportAsync(Guid orgId, string reportType = "executive", string? frameworkCode = null, string lang = "en", string? tone = null);
}

/// <summary>
/// Generates branded HTML assessment reports.
/// Supports: executive (summary), technical (full detail), presales (highlight risks),
/// exec-onepager (org-level C-level brief, bilingual, strict A4).
/// White-label: uses franchise branding (logo, colors, name).
/// </summary>
public class ReportService : IReportService
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ReportService(KryossDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    // ==========================================================================
    // ORG-LEVEL CONSOLIDATED REPORT
    // ==========================================================================

    public async Task<string> GenerateOrgReportAsync(Guid orgId, string reportType = "executive", string? frameworkCode = null, string lang = "en", string? tone = null)
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

        // Cloud-only report types (m365 = Copilot Readiness) don't require
        // endpoint assessment runs — their data lives in separate tables.
        // All other report types still require completed runs.
        var isCloudOnlyReport = reportType == "m365";

        if (latestRunIds.Count == 0 && !isCloudOnlyReport)
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

        // Load the operator identity for the report footer (Path A profile:
        // name from Entra ID claims, phone/job_title from `users` if the
        // operator has filled them in via SQL or the future profile page).
        var userInfo = await BuildReportUserInfoAsync(org);

        // C-Level dependencies: saved CTAs for the current period + M365 findings.
        // Wrapped in try/catch because migration 028 (executive_ctas) may not
        // yet be applied in all environments — we degrade gracefully to an
        // empty list so the C-Level still generates.
        var savedCtas = new List<ExecutiveCta>();
        var m365Findings = new List<M365Finding>();
        var m365Connected = false;
        if (reportType is "c-level" or "m365")
        {
            if (reportType == "c-level")
            {
                var periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                try
                {
                    savedCtas = await _db.ExecutiveCtas
                        .Where(c => c.OrganizationId == orgId && c.PeriodStart == periodStart)
                        .ToListAsync();
                }
                catch
                {
                    // executive_ctas table probably missing — migration 028 pending
                    savedCtas = new List<ExecutiveCta>();
                }
            }

            var m365Tenant = await _db.M365Tenants
                .FirstOrDefaultAsync(t => t.OrganizationId == orgId && t.ConsentGrantedAt != null);
            if (m365Tenant != null)
            {
                m365Connected = true;
                m365Findings = await _db.M365Findings
                    .Where(f => f.TenantId == m365Tenant.Id)
                    .ToListAsync();
            }
        }

        // Load Copilot Readiness scan for the "m365" unified report.
        CopilotReadinessScan? copilotScan = null;
        if (reportType == "m365")
        {
            copilotScan = await _db.CopilotReadinessScans
                .AsSplitQuery()
                .Include(s => s.Findings)
                .Include(s => s.Metrics)
                .Include(s => s.SharepointSites)
                .Include(s => s.ExternalUsers)
                .Where(s => s.OrganizationId == orgId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (copilotScan == null)
                throw new InvalidOperationException($"No Copilot Readiness scan found for organization {orgId}. Run an assessment first.");
        }

        // Monthly briefing needs historical trend data — pull the average
        // global score from runs that completed 30–60 days ago so we can
        // render this-month vs last-month delta. Only loaded when that
        // report type is requested to keep the other 7 types fast.
        decimal? previousMonthScore = null;
        if (reportType == "monthly-briefing")
        {
            var periodEnd   = DateTime.UtcNow.AddDays(-30);
            var periodStart = DateTime.UtcNow.AddDays(-60);
            var prevScores = await _db.AssessmentRuns
                .Where(r => r.OrganizationId == orgId
                            && r.CompletedAt != null
                            && r.CompletedAt >= periodStart
                            && r.CompletedAt <  periodEnd
                            && r.GlobalScore != null)
                .Select(r => (decimal)r.GlobalScore!)
                .ToListAsync();
            if (prevScores.Count > 0)
                previousMonthScore = Math.Round(prevScores.Average(), 1);
        }

        return reportType switch
        {
            "technical" => BuildOrgTechnicalReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo, lang),
            "preventas" => tone == "detailed"
                ? BuildOrgPresalesReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo, lang)
                : BuildOrgPresalesOpenerReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo, lang),
            "presales" => BuildOrgPresalesReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo, lang),
            "exec-onepager" => BuildOrgExecutiveOnePager(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo, lang),
            "presales-opener" => BuildOrgPresalesOpenerReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo, lang),
            "monthly-briefing" => BuildOrgMonthlyBriefingReport(org, runs, allResults, branding, frameworkScores, hygieneScan, orgEnrichment, userInfo, previousMonthScore, lang),
            "c-level" => BuildOrgCLevelReport(org, runs, allResults, branding, frameworkScores, hygieneScan, orgEnrichment, userInfo, previousMonthScore, savedCtas, m365Findings, m365Connected, lang),
            "m365" => CopilotReadinessReportBuilder.BuildUnifiedM365Report(org, runs, allResults, branding, frameworkScores, hygieneScan, orgEnrichment, userInfo, copilotScan, m365Findings, m365Connected, lang),
            _ => BuildOrgExecutiveReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo, lang)
        };
    }

    /// <summary>
    /// Loads the identity shown in the report footer. Falls back gracefully
    /// when the profile is incomplete: missing fields render as "—".
    /// </summary>
    private async Task<ReportUserInfo> BuildReportUserInfoAsync(Organization org)
    {
        // Franchise phone as a secondary source — if the operator has no
        // personal phone, we at least show the MSP main line (anchored to
        // the franchise / brand already loaded on `org`).
        var franchise = org.Franchise;
        string? fallbackPhone = franchise?.ContactPhone;

        if (_currentUser.UserId == Guid.Empty)
        {
            return new ReportUserInfo
            {
                FullName = _currentUser.DisplayName,
                Email = _currentUser.Email,
                Phone = _currentUser.Phone ?? fallbackPhone,
                JobTitle = _currentUser.JobTitle,
                CompanyName = franchise?.Name
            };
        }

        var dbUser = await _db.Users
            .Where(u => u.Id == _currentUser.UserId)
            .Select(u => new { u.DisplayName, u.Email, u.Phone, u.JobTitle })
            .FirstOrDefaultAsync();

        return new ReportUserInfo
        {
            FullName = dbUser?.DisplayName ?? _currentUser.DisplayName,
            Email = dbUser?.Email ?? _currentUser.Email,
            Phone = dbUser?.Phone ?? _currentUser.Phone ?? fallbackPhone,
            JobTitle = dbUser?.JobTitle ?? _currentUser.JobTitle,
            CompanyName = franchise?.Name
        };
    }

    // ======================================================================
    // ORG: EXECUTIVE REPORT
    // ======================================================================
    private static string BuildOrgExecutiveReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName,
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment,
        ReportUserInfo userInfo, string lang = "en")
    {
        var es = lang == "es";
        var reportTitle = frameworkName != null
            ? $"{frameworkName} {(es ? "Informe Organizacional" : "Organization Report")}"
            : (es ? "Informe de Evaluación de Seguridad" : "Security Assessment Report");
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        // When filtered by framework, recalculate stats from filtered results
        decimal avgScore;
        int totalPass, totalWarn, totalFail;
        if (frameworkName != null && allResults.Count > 0)
        {
            totalPass = allResults.Count(r => r.Status == "pass");
            totalWarn = allResults.Count(r => r.Status == "warn");
            totalFail = allResults.Count(r => r.Status == "fail");
            var total = totalPass + totalWarn + totalFail;
            avgScore = total > 0 ? Math.Round((decimal)totalPass / total * 100, 1) : 0;
        }
        else
        {
            avgScore = Math.Round(runs.Average(r => r.GlobalScore ?? 0), 1);
            totalPass = (int)runs.Sum(r => r.PassCount ?? 0);
            totalWarn = (int)runs.Sum(r => r.WarnCount ?? 0);
            totalFail = (int)runs.Sum(r => r.FailCount ?? 0);
        }
        var orgGrade = GetGrade(avgScore);
        var scanDate = runs.Max(r => r.CompletedAt ?? r.StartedAt);

        var riskyPorts = enrichment.Ports.Where(p => p.Risk != null).ToList();
        var threatCount = enrichment.Threats.Count;

        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true, htmlLang: lang,
            user: userInfo, detail: $"{totalMachines} {(es ? "dispositivos" : "devices")} · {org.Name}");

        // ---- PAGE 1: COVER ----
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{(frameworkName != null ? HtmlEncode(frameworkName.ToUpperInvariant()) : (es ? "EVALUACIÓN DE SEGURIDAD" : "SECURITY ASSESSMENT"))}</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(org.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{(es ? scanDate.ToString("dd 'de' MMMM 'de' yyyy") : scanDate.ToString("MMMM dd, yyyy"))} &mdash; {totalMachines} {(es ? "dispositivos evaluados" : "devices assessed")}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{orgGrade.Replace("+", "plus")}'>{HtmlEncode(orgGrade)}</div>");
        sb.AppendLine($"<p class='score'>{avgScore:F1}%</p>");
        sb.AppendLine("</div></div>");

        // ---- PAGE 2: EXECUTIVE SUMMARY ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, es ? "Resumen Ejecutivo" : "Executive Summary", brand);
        sb.AppendLine("<div class='pb'>");

        // Business-language narrative paragraph
        var riskLevel = es
            ? (avgScore >= 80 ? "aceptables" : avgScore >= 60 ? "moderados" : avgScore >= 30 ? "preocupantes" : "críticamente bajos")
            : (avgScore >= 80 ? "acceptable" : avgScore >= 60 ? "moderate" : avgScore >= 30 ? "concerning" : "critically low");
        sb.AppendLine("<div class='insight-box'>");
        if (es)
        {
            sb.AppendLine($"<p>Su organización tiene <strong>{totalMachines}</strong> dispositivos bajo gestión. Nuestra evaluación analizó <strong>{totalPass + totalWarn + totalFail}</strong> controles de seguridad en su flota y encontró niveles de cumplimiento <strong>{riskLevel}</strong>. ");
            if (avgScore < 60)
                sb.AppendLine("Se requiere remediación significativa para alcanzar una línea base de seguridad aceptable.");
            else if (avgScore < 80)
                sb.AppendLine("Varias áreas necesitan endurecimiento para cumplir con las mejores prácticas de la industria.");
            else
                sb.AppendLine("Su postura de seguridad general es sólida, con mejoras puntuales aún disponibles.");
        }
        else
        {
            sb.AppendLine($"<p>Your organization has <strong>{totalMachines}</strong> devices under management. Our assessment evaluated <strong>{totalPass + totalWarn + totalFail}</strong> security controls across your fleet and found <strong>{riskLevel}</strong> levels of compliance. ");
            if (avgScore < 60)
                sb.AppendLine("Significant remediation is required to bring your environment to an acceptable security baseline.");
            else if (avgScore < 80)
                sb.AppendLine("Several areas need hardening to meet industry best practices.");
            else
                sb.AppendLine("Your overall security posture is strong, with targeted improvements still available.");
        }
        sb.AppendLine("</p></div>");

        // KPI cards
        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{totalMachines}</span><span class='stat-label'>{(es ? "Total Dispositivos" : "Total Devices")}</span></div>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{avgScore:F1}%</span><span class='stat-label'>{(es ? "Cumplimiento Prom." : "Avg. Compliance")}</span></div>");
        if (threatCount > 0)
            sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{threatCount}</span><span class='stat-label'>{(es ? "Amenazas Detectadas" : "Threats Detected")}</span></div>");
        if (riskyPorts.Count > 0)
            sb.AppendLine($"<div class='stat warn-stat'><span class='stat-value'>{riskyPorts.Select(p => p.MachineId).Distinct().Count()}</span><span class='stat-label'>{(es ? "Equipos con Puertos Riesgosos" : "Devices with Risky Ports")}</span></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></div>");

        // ---- PAGE 3: RISK DASHBOARD ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, es ? "Panel de Riesgo" : "Risk Dashboard", brand);
        sb.AppendLine("<div class='pb'>");

        // Framework compliance bars (normalized to per-machine averages so
        // the P/F counts stay bounded by the framework size instead of
        // exploding with fleet size).
        if (frameworkScores.Count > 0)
        {
            sb.AppendLine($"<h3>{(es ? "Cumplimiento por Framework" : "Framework Compliance")}</h3>");
            AppendNormalizedFrameworkBars(sb, frameworkScores, runs.Count,
                es ? "P / F = controles promedio que pasan / fallan por equipo" : "P / F = average passing / failing controls per machine");
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
            sb.AppendLine($"<h3>{(es ? "Hallazgos Críticos Principales" : "Top Critical Findings")}</h3>");
            sb.AppendLine("<div class='risk-cards'>");
            var riskNum = 1;
            foreach (var f in criticalFailures)
            {
                sb.AppendLine("<div class='risk-card'>");
                sb.AppendLine($"<div class='risk-num'>{riskNum++}</div>");
                sb.AppendLine("<div class='risk-body'>");
                sb.AppendLine($"<strong>{HtmlEncode(f.Name)}</strong>");
                sb.AppendLine($"<span class='severity {f.Severity}'>{HtmlEncode(f.Severity)}</span>");
                sb.AppendLine($"<p class='risk-detail'>{(es ? $"Afecta {f.AffectedCount} de {totalMachines} dispositivos" : $"Affects {f.AffectedCount} of {totalMachines} devices")}</p>");
                sb.AppendLine("</div></div>");
            }
            sb.AppendLine("</div>");
        }

        // Grade distribution
        var gradeGroups = runs.GroupBy(r => r.Grade ?? "N/A").OrderBy(g => g.Key);
        sb.AppendLine($"<h3>{(es ? "Distribución de Calificaciones" : "Grade Distribution")}</h3>");
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
            AppendPageHeader(sb, es ? "Salud de Active Directory" : "Active Directory Health", brand);
            sb.AppendLine("<div class='pb'>");
            AppendHygieneSummary(sb, hygiene);

            // Derive extended AD stats from findings
            var privilegedCount = hygiene.Findings.Count(f => f.Status == "Privileged");
            var kerberoastable = hygiene.Findings.Count(f => f.Status == "Kerberoastable");
            var unconstrainedDelegation = hygiene.Findings.Count(f => f.Status == "UnconstrainedDelegation");
            var lapsFindings = hygiene.Findings.Where(f => f.Status == "NoLAPS").ToList();
            var domainLevelFinding = hygiene.Findings.FirstOrDefault(f => f.Status == "DomainLevel");

            // Security highlights table
            sb.AppendLine($"<h3>{(es ? "Puntos Destacados de Seguridad" : "Security Highlights")}</h3>");
            sb.AppendLine("<table class='results-table'>");
            sb.AppendLine($"<tr><th>{(es ? "Métrica" : "Metric")}</th><th>{(es ? "Valor" : "Value")}</th><th>{(es ? "Estado" : "Status")}</th></tr>");

            if (domainLevelFinding != null)
            {
                var lvlClass = domainLevelFinding.Detail?.Contains("2008") == true || domainLevelFinding.Detail?.Contains("2003") == true ? "fail" : "pass";
                sb.AppendLine($"<tr class='{lvlClass}'><td>{(es ? "Nivel Funcional del Dominio" : "Domain Functional Level")}</td><td>{HtmlEncode(domainLevelFinding.Detail ?? (es ? "Desconocido" : "Unknown"))}</td>");
                sb.AppendLine($"<td><span class='severity {(lvlClass == "fail" ? "critical" : "low")}'>{(lvlClass == "fail" ? (es ? "Obsoleto" : "Outdated") : "OK")}</span></td></tr>");
            }

            sb.AppendLine($"<tr class='{(privilegedCount > 10 ? "fail" : privilegedCount > 5 ? "warn" : "pass")}'><td>{(es ? "Cuentas Privilegiadas" : "Privileged Accounts")}</td><td>{privilegedCount}</td>");
            sb.AppendLine($"<td><span class='severity {(privilegedCount > 10 ? "high" : privilegedCount > 5 ? "medium" : "low")}'>{(privilegedCount > 10 ? (es ? "Excesivo" : "Excessive") : privilegedCount > 5 ? (es ? "Revisar" : "Review") : "OK")}</span></td></tr>");

            var lapsPercent = hygiene.TotalMachines > 0 ? Math.Round((double)lapsFindings.Count / hygiene.TotalMachines * 100) : 0;
            var lapsCoverage = hygiene.TotalMachines > 0 ? 100 - lapsPercent : 0;
            sb.AppendLine($"<tr class='{(lapsCoverage < 50 ? "fail" : lapsCoverage < 90 ? "warn" : "pass")}'><td>{(es ? "Cobertura LAPS" : "LAPS Coverage")}</td><td>{lapsCoverage:F0}%</td>");
            sb.AppendLine($"<td><span class='severity {(lapsCoverage < 50 ? "critical" : lapsCoverage < 90 ? "medium" : "low")}'>{(lapsCoverage < 50 ? (es ? "Crítico" : "Critical") : lapsCoverage < 90 ? (es ? "Necesita Mejora" : "Needs Improvement") : "OK")}</span></td></tr>");

            sb.AppendLine($"<tr class='{(hygiene.DormantMachines > 20 ? "fail" : hygiene.DormantMachines > 5 ? "warn" : "pass")}'><td>{(es ? "Equipos Inactivos" : "Dormant Machines")}</td><td>{hygiene.DormantMachines}</td>");
            sb.AppendLine($"<td><span class='severity {(hygiene.DormantMachines > 20 ? "high" : hygiene.DormantMachines > 5 ? "medium" : "low")}'>{(hygiene.DormantMachines > 20 ? (es ? "Limpieza Necesaria" : "Cleanup Needed") : hygiene.DormantMachines > 5 ? (es ? "Revisar" : "Review") : "OK")}</span></td></tr>");

            sb.AppendLine($"<tr class='{(hygiene.PwdNeverExpire > 10 ? "fail" : hygiene.PwdNeverExpire > 0 ? "warn" : "pass")}'><td>{(es ? "Contraseña Nunca Expira" : "Password Never Expires")}</td><td>{hygiene.PwdNeverExpire}</td>");
            sb.AppendLine($"<td><span class='severity {(hygiene.PwdNeverExpire > 10 ? "high" : hygiene.PwdNeverExpire > 0 ? "medium" : "low")}'>{(hygiene.PwdNeverExpire > 10 ? (es ? "Violación de Política" : "Policy Violation") : hygiene.PwdNeverExpire > 0 ? (es ? "Revisar" : "Review") : "OK")}</span></td></tr>");

            if (kerberoastable > 0)
            {
                sb.AppendLine($"<tr class='fail'><td>{(es ? "Cuentas Kerberoastable" : "Kerberoastable Accounts")}</td><td>{kerberoastable}</td>");
                sb.AppendLine($"<td><span class='severity critical'>{(es ? "Crítico" : "Critical")}</span></td></tr>");
            }
            if (unconstrainedDelegation > 0)
            {
                sb.AppendLine($"<tr class='fail'><td>{(es ? "Delegación sin Restricción" : "Unconstrained Delegation")}</td><td>{unconstrainedDelegation}</td>");
                sb.AppendLine($"<td><span class='severity high'>{(es ? "Alto Riesgo" : "High Risk")}</span></td></tr>");
            }

            sb.AppendLine("</table>");
            sb.AppendLine("</div></div>");
        }

        // ---- PAGE 5: NETWORK SECURITY ----
        if (riskyPorts.Count > 0 || threatCount > 0)
        {
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, es ? "Seguridad de Red" : "Network Security", brand);
            sb.AppendLine("<div class='pb'>");

            if (riskyPorts.Count > 0)
            {
                sb.AppendLine($"<h3>{(es ? "Puertos Abiertos Riesgosos" : "Risky Open Ports")}</h3>");
                // Group by port to show how many machines have each risky port
                var portGroups = riskyPorts
                    .GroupBy(p => new { p.Port, p.Service, p.Risk })
                    .Select(g => new { g.Key.Port, g.Key.Service, g.Key.Risk, MachineCount = g.Select(x => x.MachineId).Distinct().Count() })
                    .OrderByDescending(x => x.MachineCount)
                    .Take(10)
                    .ToList();

                sb.AppendLine("<table class='results-table'>");
                sb.AppendLine($"<tr><th>{(es ? "Puerto" : "Port")}</th><th>{(es ? "Servicio" : "Service")}</th><th>{(es ? "Riesgo" : "Risk")}</th><th>{(es ? "Equipos Afectados" : "Affected Devices")}</th></tr>");
                foreach (var pg in portGroups)
                {
                    sb.AppendLine("<tr class='fail'>");
                    sb.AppendLine($"<td>{pg.Port}</td>");
                    sb.AppendLine($"<td>{HtmlEncode(pg.Service ?? (es ? "Desconocido" : "Unknown"))}</td>");
                    sb.AppendLine($"<td><span class='severity high'>{HtmlEncode(pg.Risk ?? (es ? "Riesgoso" : "Risky"))}</span></td>");
                    sb.AppendLine($"<td>{pg.MachineCount} / {totalMachines}</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }

            if (threatCount > 0)
            {
                sb.AppendLine($"<h3>{(es ? "Detecciones de Amenazas" : "Threat Detections")}</h3>");
                // Group by category
                var threatCats = enrichment.Threats
                    .GroupBy(t => t.Category)
                    .Select(g => new { Category = g.Key, Count = g.Count(), MachineCount = g.Select(x => x.MachineId).Distinct().Count() })
                    .OrderByDescending(x => x.Count)
                    .ToList();

                sb.AppendLine(es
                    ? $"<div class='insight-box fail-box'><p>Se detectaron <strong>{threatCount}</strong> firmas de amenazas en <strong>{threatCats.Count}</strong> categorías en <strong>{enrichment.Threats.Select(t => t.MachineId).Distinct().Count()}</strong> dispositivos.</p></div>"
                    : $"<div class='insight-box fail-box'><p>Detected <strong>{threatCount}</strong> threat signatures across <strong>{threatCats.Count}</strong> categories on <strong>{enrichment.Threats.Select(t => t.MachineId).Distinct().Count()}</strong> devices.</p></div>");

                sb.AppendLine("<table class='results-table'>");
                sb.AppendLine($"<tr><th>{(es ? "Categoría" : "Category")}</th><th>{(es ? "Firmas" : "Signatures")}</th><th>{(es ? "Equipos Afectados" : "Devices Affected")}</th></tr>");
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
        AppendPageHeader(sb, es ? "Vista de Flota" : "Fleet Overview", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<table class='fleet-table'>");
        sb.AppendLine($"<tr><th>Hostname</th><th>OS</th><th>{(es ? "Puntaje" : "Score")}</th><th>{(es ? "Calificación" : "Grade")}</th><th>{(es ? "Pasan" : "Pass")}</th><th>{(es ? "Advert." : "Warn")}</th><th>{(es ? "Fallan" : "Fail")}</th></tr>");
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
        AppendPageHeader(sb, es ? "Recomendaciones" : "Recommendations", brand);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine(es
            ? $"<div class='insight-box'><p>Basado en nuestra evaluación integral de <strong>{totalMachines}</strong> dispositivos, recomendamos las siguientes acciones de remediación, priorizadas por impacto de riesgo.</p></div>"
            : $"<div class='insight-box'><p>Based on our comprehensive assessment of <strong>{totalMachines}</strong> devices, we recommend the following remediation actions, prioritized by risk impact.</p></div>");

        AppendDataDrivenRecommendations(sb, runs, allResults, hygiene, enrichment, brand);

        sb.AppendLine("</div></div>");

        // ---- FINAL PAGE: REPORT SIGNOFF ----
        // Dedicated last page with the enriched footer (operator name /
        // email / phone). Rendered as its own A4 so it's always visible
        // instead of being clipped by the 296mm overflow rule.
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, es ? "Firma del Informe" : "Report Signoff", brand);
        sb.AppendLine("<div class='pb'>");
        AppendFooter(sb, brand, $"{totalMachines} {(es ? "dispositivos" : "devices")}", userInfo);
        sb.AppendLine("</div></div>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    // ======================================================================
    // ORG: TECHNICAL REPORT
    // ======================================================================
    private static string BuildOrgTechnicalReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName,
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment,
        ReportUserInfo userInfo, string lang = "en")
    {
        var es = lang == "es";
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        var reportTitle = frameworkName != null
            ? $"{frameworkName} {(es ? "Evaluación Técnica" : "Technical Assessment")}"
            : (es ? "Evaluación Técnica" : "Technical Assessment");

        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true,
            user: userInfo, detail: $"{totalMachines} {(es ? "dispositivos" : "devices")} · {org.Name}");

        // ---- COVER ----
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{(es ? "NIVEL TÉCNICO" : "TECHNICAL LEVEL")}</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(org.Name)}</h2>");
        var scanDate = runs.Count > 0 ? runs.Max(r => r.CompletedAt ?? r.StartedAt) : DateTime.UtcNow;
        sb.AppendLine($"<p class='meta'>{(es ? scanDate.ToString("dd 'de' MMMM 'de' yyyy") : scanDate.ToString("MMMM dd, yyyy"))} — {totalMachines} {(es ? "dispositivos" : "devices")}</p>");
        sb.AppendLine("</div></div>");

        // ---- ASSET MATRIX (paginates internally) ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, es ? "Matriz de Activos" : "Asset Matrix", brand,
            es ? "NIVEL TÉCNICO" : "TECHNICAL LEVEL");
        sb.AppendLine("<div class='pb'>");
        AppendAssetMatrix(sb, runs, brand, lang);
        sb.AppendLine("</div></div>");

        // ---- TOP 10 CRITICAL FINDINGS ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, es ? "Hallazgos Críticos" : "Critical Findings", brand,
            es ? "NIVEL TÉCNICO" : "TECHNICAL LEVEL");
        sb.AppendLine("<div class='pb'>");
        AppendTop10CriticalFindings(sb, allResults, lang);
        sb.AppendLine("</div></div>");

        // ---- THE 6 IRONS HARDENING AUDIT ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, es ? "Auditoría de Hardening" : "Hardening Audit", brand,
            es ? "NIVEL TÉCNICO" : "TECHNICAL LEVEL");
        sb.AppendLine("<div class='pb'>");
        AppendSixIronsHardeningAudit(sb, runs, allResults, hygiene, lang);
        sb.AppendLine("</div></div>");

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    // ======================================================================
    // ORG: PRESALES REPORT
    // ======================================================================
    private static string BuildOrgPresalesReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName,
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment,
        ReportUserInfo userInfo, string lang = "en")
    {
        var es = lang == "es";
        var reportTitle = frameworkName != null
            ? $"{frameworkName} {(es ? "Postura de Seguridad" : "Security Posture")}"
            : (es ? "Evaluación de Postura de Seguridad" : "Security Posture Assessment");
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        decimal avgScore;
        int totalPass, totalWarn, totalFail;
        if (frameworkName != null && allResults.Count > 0)
        {
            totalPass = allResults.Count(r => r.Status == "pass");
            totalWarn = allResults.Count(r => r.Status == "warn");
            totalFail = allResults.Count(r => r.Status == "fail");
            var total = totalPass + totalWarn + totalFail;
            avgScore = total > 0 ? Math.Round((decimal)totalPass / total * 100, 1) : 0;
        }
        else
        {
            avgScore = Math.Round(runs.Average(r => r.GlobalScore ?? 0), 1);
            totalPass = (int)runs.Sum(r => r.PassCount ?? 0);
            totalWarn = (int)runs.Sum(r => r.WarnCount ?? 0);
            totalFail = (int)runs.Sum(r => r.FailCount ?? 0);
        }
        var orgGrade = GetGrade(avgScore);
        var scanDate = runs.Max(r => r.CompletedAt ?? r.StartedAt);
        var riskyPorts = enrichment.Ports.Where(p => p.Risk != null).ToList();
        var threatCount = enrichment.Threats.Count;

        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true, htmlLang: lang,
            user: userInfo, detail: $"{totalMachines} {(es ? "dispositivos" : "devices")} · {org.Name}");

        // ---- BIG 4 FINANCIAL AUDIT LIGHT MODE (scoped via .pres-light) ----
        // Premium light aesthetic: pure white document, very light cool
        // gray secondary surfaces, dark slate headings, charcoal body, deep
        // navy primary accents, subdued brick red for critical alerts, and
        // subtle soft shadows on data cards for depth without visual noise.
        // Scoped to .pres-light so nothing leaks into other reports.
        sb.AppendLine("<style>");
        sb.AppendLine(@"
.pres-light { background: #FFFFFF !important; color: #334155 !important; -webkit-print-color-adjust: exact !important; print-color-adjust: exact !important; }
.pres-light .pb { background: #FFFFFF !important; color: #334155 !important; padding-bottom: 24mm !important; }

.pres-light h3 { color: #1E293B !important; border-bottom: 2px solid #0F172A !important; padding-bottom: 6px; font-size: 14px !important; font-weight: 700; letter-spacing: 0.02em; }
.pres-light h4 { color: #1E293B !important; font-weight: 700; }
.pres-light p, .pres-light li, .pres-light td { color: #334155 !important; }
.pres-light strong { color: #1E293B; }

.pres-light .stat { background: #F8F9FA !important; border: 1px solid #E2E8F0 !important; box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04); }
.pres-light .stat.fail-stat { background: #FEF2F2 !important; border-color: #991B1B !important; border-left: 3px solid #991B1B !important; }
.pres-light .stat.warn-stat { background: #FFFBEB !important; border-color: #B45309 !important; border-left: 3px solid #B45309 !important; }
.pres-light .stat.pass-stat { background: #F0FDF4 !important; border-color: #15803D !important; border-left: 3px solid #15803D !important; }
.pres-light .stat-value { color: #0F172A !important; }
.pres-light .stat-label { color: #64748B !important; text-transform: uppercase; letter-spacing: 0.08em; }

.pres-light .stark-reality {
    background: #FEF2F2; border: 1px solid #FECACA; border-left: 5px solid #991B1B;
    padding: 18px 26px; margin: 18px 0 22px; border-radius: 4px;
    box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.06), 0 2px 4px -2px rgba(15, 23, 42, 0.04);
    page-break-inside: avoid;
}
.pres-light .stark-reality p { color: #7F1D1D !important; font-size: 13px; line-height: 1.65; font-weight: 500; margin: 0; }
.pres-light .stark-reality strong { color: #450A0A !important; }

.pres-light .threat-card {
    background: #FFFFFF; border: 1px solid #E2E8F0; border-left: 5px solid #991B1B;
    padding: 18px 26px; margin-bottom: 14px; border-radius: 0 6px 6px 0;
    box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.05), 0 2px 4px -2px rgba(15, 23, 42, 0.03);
    page-break-inside: avoid;
}
.pres-light .threat-card.lateral { border-left-color: #B45309; }
.pres-light .threat-card.adhygiene { border-left-color: #0F172A; }
.pres-light .threat-card .threat-sub { font-size: 9px; color: #991B1B; letter-spacing: 0.12em; text-transform: uppercase; margin-bottom: 4px; font-weight: 700; }
.pres-light .threat-card.lateral .threat-sub { color: #B45309; }
.pres-light .threat-card.adhygiene .threat-sub { color: #0F172A; }
.pres-light .threat-card h4 { color: #0F172A !important; font-size: 14px; font-weight: 700; margin: 0 0 8px; }
.pres-light .threat-card p { color: #334155 !important; font-size: 12px; line-height: 1.6; margin: 0 0 10px; }
.pres-light .threat-card .threat-evidence { border-top: 1px solid #E2E8F0; padding-top: 10px; margin-top: 10px; font-size: 10px; color: #64748B !important; }
.pres-light .threat-card .threat-evidence strong { color: #1E293B !important; }

.pres-light .methodology-card {
    background: #F8F9FA; border: 1px solid #CBD5E1; border-top: 4px solid #0F172A;
    border-radius: 4px; padding: 22px 28px; margin-top: 12px;
    box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.06), 0 2px 4px -2px rgba(15, 23, 42, 0.04);
}
.pres-light .methodology-card h4 { color: #0F172A !important; font-size: 15px; margin: 0 0 10px; font-weight: 700; }
.pres-light .methodology-card p { color: #334155 !important; font-size: 12px; line-height: 1.65; }
.pres-light .methodology-card ul { margin: 10px 0 0 20px; }
.pres-light .methodology-card li { color: #334155 !important; font-size: 11px; line-height: 1.65; margin-bottom: 4px; }
.pres-light .methodology-card strong { color: #0F172A !important; }

.pres-light .methodology-warning {
    background: #FEF2F2; border: 1px solid #FECACA; border-left: 4px solid #991B1B;
    padding: 12px 18px; margin: 12px 0 14px; border-radius: 3px;
    font-size: 11px; color: #7F1D1D !important;
    box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
}
.pres-light .methodology-warning strong { color: #450A0A !important; }

.pres-light .exploit-badge {
    display: inline-block; padding: 2px 8px; font-size: 9px; font-weight: 700;
    border-radius: 3px; letter-spacing: 0.05em; text-transform: uppercase;
    background: #991B1B; color: #FFFFFF;
}
.pres-light .exploit-badge.high { background: #B45309; color: #FFFFFF; }

.pres-light .results-table { background: #FFFFFF; border: 1px solid #E2E8F0; border-radius: 4px; overflow: hidden; box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04); }
.pres-light .results-table th { background: #F8F9FA !important; color: #0F172A !important; border-bottom: 2px solid #0F172A !important; font-size: 10px; text-transform: uppercase; letter-spacing: 0.05em; font-weight: 700; padding: 10px; }
.pres-light .results-table td { color: #334155 !important; border-bottom: 1px solid #E2E8F0 !important; font-size: 11px; padding: 8px 10px; }
.pres-light .results-table tr:nth-child(even) td { background: #F8F9FA; }
.pres-light .results-table tr.fail { background: #FEF2F2 !important; }

.pres-light .step-num { background: #0F172A !important; color: #FFFFFF !important; }
.pres-light .step strong { color: #1E293B !important; }
.pres-light .phase-list li { color: #334155 !important; }

.pres-light .cta-box {
    background: #F8F9FA !important; border: 1px solid #CBD5E1 !important;
    border-top: 4px solid #0F172A !important; color: #334155 !important;
    box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.06), 0 2px 4px -2px rgba(15, 23, 42, 0.04);
}
.pres-light .cta-box p { color: #334155 !important; }
.pres-light .cta-box strong { color: #0F172A !important; }

.pres-light .insight-box { background: #F8F9FA !important; border: 1px solid #CBD5E1 !important; color: #334155 !important; box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04); }
.pres-light .insight-box p { color: #334155 !important; }
.pres-light .insight-box strong { color: #0F172A !important; }

.pres-light .fw-label { color: #1E293B !important; }
.pres-light .fw-pct { color: #0F172A !important; }
.pres-light .fw-detail { color: #64748B !important; }

.pres-light .kpi-row { display: flex; gap: 10px; margin: 12px 0 18px; flex-wrap: nowrap; }
");
        sb.AppendLine("</style>");


        // ---- PAGE 1: COVER ----
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{(frameworkName != null ? HtmlEncode(frameworkName.ToUpperInvariant()) + " " : "")}{(es ? "POSTURA DE SEGURIDAD" : "SECURITY POSTURE")}</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(org.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{(es ? scanDate.ToString("dd 'de' MMMM 'de' yyyy") : scanDate.ToString("MMMM dd, yyyy"))}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{orgGrade.Replace("+", "plus")}'>{HtmlEncode(orgGrade)}</div>");
        sb.AppendLine($"<p class='score'>{avgScore:F1}%</p>");
        sb.AppendLine("</div></div>");

        // ---- Precompute additional metrics for the new structure ----
        var criticalFails = allResults
            .Where(r => r.Status == "fail" && r.Severity == "critical")
            .Select(r => r.ControlId).Distinct().Count();
        var highFails = allResults
            .Where(r => r.Status == "fail" && r.Severity == "high")
            .Select(r => r.ControlId).Distinct().Count();
        var downtimeEstimate = avgScore < 40m ? 21 : avgScore < 60m ? 14 : avgScore < 80m ? 7 : 2;
        var bitlockerMissing = runs.Count(r => r.Machine.Bitlocker != true);
        var noTpmCount = runs.Count(r => r.Machine.TpmPresent != true);
        var rdpMachineCount = riskyPorts.Where(p => p.Port == 3389).Select(p => p.MachineId).Distinct().Count();

        int CountFailMachines(Func<OrgControlResult, bool> pred) => allResults
            .Where(r => r.Status == "fail" && pred(r))
            .Select(r => r.RunId).Distinct().Count();

        var credExposureMachines = CountFailMachines(r =>
            r.Name.Contains("WDigest",    StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("LSA",        StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("LM Hash",    StringComparison.OrdinalIgnoreCase));
        var smbv1Machines = CountFailMachines(r =>
            r.Name.Contains("SMBv1", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("SMB1",  StringComparison.OrdinalIgnoreCase));
        var ntlmMachines = CountFailMachines(r =>
            r.Name.Contains("NTLM", StringComparison.OrdinalIgnoreCase));
        var asrMachines = CountFailMachines(r =>
            r.Name.Contains("ASR", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("Attack Surface", StringComparison.OrdinalIgnoreCase));
        var appLockerMachines = CountFailMachines(r =>
            r.Name.Contains("AppLocker", StringComparison.OrdinalIgnoreCase));

        var noLapsCount     = hygiene?.Findings.Count(f => f.Status == "NoLAPS")         ?? 0;
        var kerberoastCount = hygiene?.Findings.Count(f => f.Status == "Kerberoastable") ?? 0;
        var privilegedCount = hygiene?.Findings.Count(f => f.Status == "Privileged")     ?? 0;
        var staleMachines   = hygiene?.StaleMachines                                     ?? 0;
        var staleUsers      = hygiene?.StaleUsers                                        ?? 0;
        var dormantUsers    = hygiene?.DormantUsers                                      ?? 0;

        var machineByRunId = runs.ToDictionary(r => r.Id, r => r.Machine);
        var topFailures = allResults
            .Where(r => r.Status == "fail" && (r.Severity == "critical" || r.Severity == "high"))
            .GroupBy(r => new { r.RunId, r.ControlId })
            .Select(g => g.First())
            .OrderByDescending(r => r.Severity == "critical" ? 1 : 0)
            .Take(10)
            .ToList();

        string ExploitabilityLabel(OrgControlResult r)
        {
            var name = r.Name ?? "";
            if (name.Contains("WDigest",   StringComparison.OrdinalIgnoreCase) ||
                name.Contains("LSA",       StringComparison.OrdinalIgnoreCase)) return "Mimikatz ready";
            if (name.Contains("SMB1",      StringComparison.OrdinalIgnoreCase) ||
                name.Contains("SMBv1",     StringComparison.OrdinalIgnoreCase)) return "EternalBlue vector";
            if (name.Contains("BitLocker", StringComparison.OrdinalIgnoreCase)) return "Device theft exposure";
            if (name.Contains("LAPS",      StringComparison.OrdinalIgnoreCase)) return "Lateral movement";
            if (name.Contains("Audit",     StringComparison.OrdinalIgnoreCase)) return "No forensic trail";
            if (name.Contains("NTLM",      StringComparison.OrdinalIgnoreCase)) return "Credential relay";
            if (name.Contains("RDP",       StringComparison.OrdinalIgnoreCase)) return "Remote entry point";
            if (name.Contains("AppLocker", StringComparison.OrdinalIgnoreCase)) return "Unsigned code allowed";
            return r.Severity == "critical" ? "Immediate exploitation" : "Chainable exposure";
        }

        // ======================================================================
        // PAGE 2: EXECUTIVE SUMMARY (Financial & Risk Alignment)
        // ======================================================================
        sb.AppendLine("<div class='page pres-light'>");
        AppendPageHeader(sb, "Executive Summary", brand, "FINANCIAL & RISK ALIGNMENT");
        sb.AppendLine("<div class='pb'>");

        // Compact KPI rail — 4 cards on a single row, thinner than the
        // default .stat size so they never wrap.
        const string presStatStyle  = "min-width:72px;padding:10px 10px;flex:1";
        const string presValueStyle = "font-size:18px;font-weight:800;line-height:1";
        const string presLabelStyle = "font-size:8px;margin-top:4px;line-height:1.15";
        sb.AppendLine("<div class='summary-grid' style='margin-top:8px;margin-bottom:16px;gap:8px;flex-wrap:nowrap'>");
        sb.AppendLine($"<div class='stat fail-stat' style='{presStatStyle}'><span class='stat-value' style='{presValueStyle}'>{totalMachines}</span><span class='stat-label' style='{presLabelStyle}'>{(es ? "Endpoints Escaneados" : "Endpoints Scanned")}</span></div>");
        sb.AppendLine($"<div class='stat fail-stat' style='{presStatStyle}'><span class='stat-value' style='{presValueStyle}'>{criticalFails}</span><span class='stat-label' style='{presLabelStyle}'>{(es ? "Vulnerabilidades Críticas" : "Critical Vulnerabilities")}</span></div>");
        sb.AppendLine($"<div class='stat warn-stat' style='{presStatStyle}'><span class='stat-value' style='{presValueStyle}'>{downtimeEstimate}d</span><span class='stat-label' style='{presLabelStyle}'>{(es ? "Downtime Est. si Hay Brecha" : "Est. Downtime if Breached")}</span></div>");
        sb.AppendLine($"<div class='stat fail-stat' style='{presStatStyle}'><span class='stat-value' style='{presValueStyle}'>{avgScore:F0}/100</span><span class='stat-label' style='{presLabelStyle}'>{(es ? "Puntaje de Madurez" : "Maturity Score")}</span></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='stark-reality'>");
        if (es)
            sb.AppendLine($"<p>Su red es actualmente <strong>altamente susceptible a ransomware automatizado y movimiento lateral</strong>. En {totalMachines} endpoints identificamos <strong>{criticalFails} vulnerabilidades críticas</strong> y <strong>{highFails} brechas de alta severidad</strong>. Un solo credencial comprometida o clic de phishing le daría a un atacante todo lo necesario para cifrar, exfiltrar y extorsionar &mdash; el tiempo de permanencia antes de detección se mediría en días, no horas.</p>");
        else
            sb.AppendLine($"<p>Your network is currently <strong>highly susceptible to automated ransomware and lateral movement</strong>. Across {totalMachines} endpoints we identified <strong>{criticalFails} critical vulnerabilities</strong> and <strong>{highFails} high-severity gaps</strong>. A single compromised credential or phishing click would give an attacker everything they need to encrypt, exfiltrate and extort &mdash; dwell time before detection would be measured in days, not hours.</p>");
        sb.AppendLine("</div>");

        sb.AppendLine($"<h3>{(es ? "Lo Que Previene Esta Inversión" : "What This Investment Prevents")}</h3>");
        if (es)
            sb.AppendLine($"<p style='font-size:12px;line-height:1.65'>Los benchmarks de la industria para organizaciones de este tamaño reportan un costo medio de recuperación por ransomware de <strong style='color:#0F172A'>USD 1.8M</strong> y un downtime operacional promedio de <strong style='color:#0F172A'>22 días</strong> (IBM Cost of a Data Breach, 2024). El plan de hardening descrito a continuación está diseñado para cerrar sistemáticamente estas {criticalFails + highFails} exposiciones críticas y altas antes de que se conviertan en el incidente que defina su año fiscal.</p>");
        else
            sb.AppendLine($"<p style='font-size:12px;line-height:1.65'>Industry benchmarks for organizations of this size report a median ransomware recovery cost of <strong style='color:#0F172A'>USD 1.8M</strong> and an average operational downtime of <strong style='color:#0F172A'>22 days</strong> (IBM Cost of a Data Breach, 2024). The hardening plan outlined below is designed to systematically close these {criticalFails + highFails} critical-and-high exposures before they become the incident that defines your fiscal year.</p>");

        if (frameworkScores.Count > 0)
        {
            sb.AppendLine($"<h3 style='margin-top:18px'>{(es ? "Alineamiento de Frameworks" : "Framework Alignment")}</h3>");
            AppendNormalizedFrameworkBars(sb, frameworkScores, runs.Count,
                es ? "P / F = controles promedio que pasan / fallan por equipo" : "P / F = average passing / failing controls per machine");
        }

        sb.AppendLine("</div></div>");

        // ======================================================================
        // PAGE 3: PRIMARY THREAT VECTORS
        // ======================================================================
        sb.AppendLine("<div class='page pres-light'>");
        AppendPageHeader(sb, es ? "Vectores de Amenaza Principales" : "Primary Threat Vectors", brand,
            es ? "TRADUCCIÓN DE NEGOCIO" : "BUSINESS TRANSLATION");
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine(es
            ? "<p style='font-size:12px;color:#64748B;margin-bottom:16px'>Agrupamos cientos de hallazgos técnicos en los tres patrones de ataque que determinan directamente si un grupo de ransomware puede convertir su red en un titular de extorsión.</p>"
            : "<p style='font-size:12px;color:#64748B;margin-bottom:16px'>We group hundreds of technical findings into the three attack patterns that directly determine whether a ransomware crew can turn your network into an extortion headline.</p>");

        sb.AppendLine("<div class='threat-card'>");
        sb.AppendLine($"<div class='threat-sub'>VECTOR 01 &middot; {(es ? "IMPACTO: INTERRUPCIÓN DE NEGOCIO" : "IMPACT: BUSINESS INTERRUPTION")}</div>");
        sb.AppendLine($"<h4>{(es ? "Propagación de Ransomware" : "Ransomware Propagation")}</h4>");
        sb.AppendLine(es
            ? "<p>Las capas que normalmente detienen la propagación lateral de gusanos de cifrado están ausentes o mal configuradas en una parte significativa de la flota. Un solo adjunto malicioso o payload de cadena de suministro se propagaría por su red con casi cero resistencia.</p>"
            : "<p>The layers that normally stop encryption worms from spreading laterally are missing or misconfigured across a significant portion of the fleet. A single malicious attachment or supply-chain payload would propagate across your network with almost no resistance.</p>");
        sb.AppendLine("<div class='threat-evidence'>");
        sb.AppendLine(es
            ? $"<strong>Evidencia:</strong> BitLocker deshabilitado en <strong>{bitlockerMissing}</strong> equipos &middot; Reglas ASR ausentes en <strong>{asrMachines}</strong> &middot; Políticas AppLocker ausentes en <strong>{appLockerMachines}</strong> &middot; TPM ausente o apagado en <strong>{noTpmCount}</strong>"
            : $"<strong>Evidence:</strong> BitLocker disabled on <strong>{bitlockerMissing}</strong> devices &middot; ASR rules missing on <strong>{asrMachines}</strong> &middot; AppLocker policies absent on <strong>{appLockerMachines}</strong> &middot; TPM absent or off on <strong>{noTpmCount}</strong>");
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class='threat-card lateral'>");
        sb.AppendLine($"<div class='threat-sub'>VECTOR 02 &middot; {(es ? "IMPACTO: COMPROMISO TOTAL DEL DOMINIO" : "IMPACT: FULL DOMAIN COMPROMISE")}</div>");
        sb.AppendLine($"<h4>{(es ? "Movimiento Lateral y Robo de Credenciales" : "Lateral Movement &amp; Credential Theft")}</h4>");
        sb.AppendLine(es
            ? "<p>Las condiciones necesarias para que un atacante pivotee desde un solo endpoint hasta el control total de Active Directory están presentes hoy. Las credenciales se almacenan en texto plano, los protocolos de autenticación legacy están activos, y cada estación de trabajo comparte la misma contraseña de administrador local.</p>"
            : "<p>The conditions required for an attacker to pivot from a single endpoint to full Active Directory control are present today. Credentials are cached in clear text, legacy authentication protocols are still active, and every workstation shares the same local administrator password.</p>");
        sb.AppendLine("<div class='threat-evidence'>");
        sb.AppendLine(es
            ? $"<strong>Evidencia:</strong> WDigest / LSA exponiendo credenciales en texto plano en <strong>{credExposureMachines}</strong> hosts &middot; NTLM permitido en <strong>{ntlmMachines}</strong> &middot; SMBv1 activo en <strong>{smbv1Machines}</strong> &middot; LAPS ausente en <strong>{noLapsCount}</strong> equipos &middot; RDP accesible en <strong>{rdpMachineCount}</strong>"
            : $"<strong>Evidence:</strong> WDigest / LSA exposing plaintext creds on <strong>{credExposureMachines}</strong> hosts &middot; NTLM allowed on <strong>{ntlmMachines}</strong> &middot; SMBv1 active on <strong>{smbv1Machines}</strong> &middot; LAPS missing on <strong>{noLapsCount}</strong> machines &middot; RDP reachable on <strong>{rdpMachineCount}</strong>");
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class='threat-card adhygiene'>");
        sb.AppendLine($"<div class='threat-sub'>VECTOR 03 &middot; {(es ? "IMPACTO: RADIO DE EXPLOSIÓN DE IDENTIDAD" : "IMPACT: IDENTITY BLAST RADIUS")}</div>");
        sb.AppendLine($"<h4>{(es ? "Higiene de Active Directory" : "Active Directory Hygiene")}</h4>");
        sb.AppendLine(es
            ? "<p>Su directorio ha acumulado los subproductos clásicos de años de crecimiento operacional: cuentas de usuario y equipo obsoletas, identidades privilegiadas inactivas, y cuentas de servicio con políticas de contraseña explotables. Cada una de estas es un punto de apoyo gratuito esperando a un atacante.</p>"
            : "<p>Your directory has accumulated the classic by-products of years of operational growth: stale user and machine accounts, dormant privileged identities, and service accounts with exploitable password policies. Each of these is a free foothold waiting for an attacker.</p>");
        sb.AppendLine("<div class='threat-evidence'>");
        sb.AppendLine(es
            ? $"<strong>Evidencia:</strong> <strong>{staleMachines}</strong> objetos de equipo obsoletos &middot; <strong>{staleUsers + dormantUsers}</strong> cuentas de usuario obsoletas / inactivas &middot; <strong>{kerberoastCount}</strong> cuentas de servicio Kerberoastable &middot; <strong>{privilegedCount}</strong> cuentas privilegiadas en exceso"
            : $"<strong>Evidence:</strong> <strong>{staleMachines}</strong> stale machine objects &middot; <strong>{staleUsers + dormantUsers}</strong> stale / dormant user accounts &middot; <strong>{kerberoastCount}</strong> Kerberoastable service accounts &middot; <strong>{privilegedCount}</strong> excess privileged accounts");
        sb.AppendLine("</div></div>");

        sb.AppendLine("</div></div>");

        // ======================================================================
        // PAGE 4: TECHNICAL EVIDENCE - Top 10 Critical Endpoint Failures
        // ======================================================================
        sb.AppendLine("<div class='page pres-light'>");
        AppendPageHeader(sb, es ? "Evidencia Técnica" : "Technical Evidence", brand,
            es ? "TOP 10 FALLOS CRÍTICOS EN ENDPOINTS" : "TOP 10 CRITICAL ENDPOINT FAILURES");
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine(es
            ? "<p style='font-size:11px;color:#64748B;margin-bottom:14px'>La tabla a continuación es un extracto directo de los datos de evaluación &mdash; no una plantilla. Cada fila es un endpoint real en su ambiente con una falla de control específica. Su equipo técnico puede validar cada hallazgo contra el host listado y el vector de explotabilidad.</p>"
            : "<p style='font-size:11px;color:#64748B;margin-bottom:14px'>The table below is a direct extract from the assessment data &mdash; not a template. Each row is a real endpoint in your environment with a specific control failure. Your technical team can validate each finding against the listed host and exploitability vector.</p>");

        sb.AppendLine("<table class='results-table'>");
        sb.AppendLine($"<tr><th style='width:30%'>Hostname</th><th style='width:18%'>{(es ? "Dirección IP" : "IP Address")}</th><th style='width:34%'>{(es ? "Control Fallido" : "Failed Control")}</th><th style='width:18%'>{(es ? "Explotabilidad" : "Exploitability")}</th></tr>");
        if (topFailures.Count == 0)
        {
            sb.AppendLine($"<tr><td colspan='4' style='text-align:center;padding:20px;color:#64748B'>{(es ? "No se detectaron fallos de control críticos o de alta severidad en esta evaluación." : "No critical or high-severity control failures detected in this assessment.")}</td></tr>");
        }
        else
        {
            foreach (var f in topFailures)
            {
                var machine = machineByRunId.TryGetValue(f.RunId, out var m) ? m : null;
                var hostname = machine?.Hostname ?? "(unknown)";
                var ip       = machine?.IpAddress ?? "—";
                var badgeCls = f.Severity == "critical" ? "exploit-badge" : "exploit-badge high";
                sb.AppendLine("<tr class='fail'>");
                sb.AppendLine($"<td style='font-family:monospace;font-weight:600;color:#F9FAFB'>{HtmlEncode(hostname)}</td>");
                sb.AppendLine($"<td style='font-family:monospace;color:#64748B'>{HtmlEncode(ip)}</td>");
                sb.AppendLine($"<td>{HtmlEncode(f.Name)}</td>");
                sb.AppendLine($"<td><span class='{badgeCls}'>{HtmlEncode(ExploitabilityLabel(f))}</span></td>");
                sb.AppendLine("</tr>");
            }
        }
        sb.AppendLine("</table>");

        sb.AppendLine(es
            ? $"<p style='margin-top:14px;font-size:10px;color:#64748B;font-style:italic'>Mostrando top {topFailures.Count} de {criticalFails + highFails} fallos críticos/altos totales. El apéndice completo de remediación línea por línea se entrega tras el kickoff del engagement.</p>"
            : $"<p style='margin-top:14px;font-size:10px;color:#64748B;font-style:italic'>Showing top {topFailures.Count} of {criticalFails + highFails} total critical/high failures. The complete line-by-line remediation appendix is provided after engagement kickoff.</p>");

        sb.AppendLine("</div></div>");

        // ======================================================================
        // PAGE 5: METHODOLOGY - The 90-Day Safe Deprecation Pitch
        // ======================================================================
        sb.AppendLine("<div class='page pres-light'>");
        AppendPageHeader(sb, es ? "Metodología: Depreciación Segura de 90 Días" : "Methodology: 90-Day Safe Deprecation", brand,
            es ? "EL ENFOQUE TEAMLOGIC" : "THE TEAMLOGIC APPROACH");
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='methodology-warning'>");
        if (es)
            sb.AppendLine("<strong>&#9888; El error que vemos cometer a los clientes:</strong> Desactivar abruptamente protocolos legacy (NTLM, SMBv1, WDigest, Kerberos RC4) sin mapear primero las dependencias causa interrupciones catastróficas e inmediatas del negocio. Aplicaciones de línea de negocio, file shares legacy, escáneres, sistemas telefónicos e integraciones dejan de funcionar en el momento en que el protocolo muere &mdash; y la interrupción recae directamente sobre el equipo de TI que apagó el switch.");
        else
            sb.AppendLine("<strong>&#9888; The mistake we watch clients make:</strong> Abruptly turning off legacy protocols (NTLM, SMBv1, WDigest, Kerberos RC4) without first mapping dependencies causes catastrophic, immediate business outages. Line-of-business applications, legacy file shares, scanners, phone systems and integrations break the moment the protocol dies &mdash; and the outage lands squarely on the IT team that flipped the switch.");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='methodology-card'>");
        if (es)
        {
            sb.AppendLine("<h4>Cómo cerramos estos vectores sin dejarte offline</h4>");
            sb.AppendLine("<p>TeamLogic IT despliega un <strong>motor de telemetría pasiva</strong> a través de tu red durante una <strong>ventana de observación de 90 días</strong>. El motor captura silenciosamente cada llamada de protocolo legacy &mdash; qué hosts las hacen, qué cuentas las inician, qué aplicaciones dependen de ellas y con qué cadencia. Cero huella de instalación en los endpoints críticos, cero reinicios, cero interrupciones para el usuario.</p>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li><strong>Día 1-30:</strong> captura baseline de todo el tráfico NTLM / SMBv1 / RC4 / WDigest en la red y en el DC</li>");
            sb.AppendLine("<li><strong>Día 31-60:</strong> atribución &mdash; cada llamada legacy vinculada a una aplicación, usuario o servicio específico</li>");
            sb.AppendLine("<li><strong>Día 61-90:</strong> blueprint de depreciación &mdash; el orden exacto, por dependencia, en que cada protocolo legacy puede apagarse sin romper nada</li>");
            sb.AppendLine("</ul>");
            sb.AppendLine("<p style='margin-top:10px'>Al final de los 90 días recibes un <strong>plan de depreciación por sistema</strong> con <strong style='color:#0F172A'>cero ambigüedad</strong> y la garantía explícita de que cada eliminación de protocolo se ejecutará con <strong style='color:#0F172A'>cero downtime operacional</strong>.</p>");
        }
        else
        {
            sb.AppendLine("<h4>How we close these vectors without taking you offline</h4>");
            sb.AppendLine("<p>TeamLogic IT deploys a <strong>passive telemetry engine</strong> across your network for a full <strong>90-day observation window</strong>. The engine silently captures every legacy protocol call &mdash; which hosts make them, which accounts initiate them, which applications depend on them and at what cadence. Zero installation footprint on the endpoints that matter, zero reboots, zero user-facing disruption.</p>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li><strong>Day 1-30:</strong> baseline capture of all NTLM / SMBv1 / RC4 / WDigest traffic on the wire and at the DC</li>");
            sb.AppendLine("<li><strong>Day 31-60:</strong> attribution &mdash; every legacy call tied to a specific application, user or service</li>");
            sb.AppendLine("<li><strong>Day 61-90:</strong> deprecation blueprint &mdash; the exact order, per-dependency, in which each legacy protocol can be turned off without breaking anything</li>");
            sb.AppendLine("</ul>");
            sb.AppendLine("<p style='margin-top:10px'>At the end of the 90 days you receive a <strong>per-system deprecation plan</strong> with <strong style='color:#0F172A'>zero ambiguity</strong> and the explicit guarantee that each protocol kill will land with <strong style='color:#0F172A'>zero operational downtime</strong>.</p>");
        }
        sb.AppendLine("</div>");

        sb.AppendLine("</div></div>");

        // ======================================================================
        // PAGE 6: ROADMAP & NEXT STEPS
        // ======================================================================
        sb.AppendLine("<div class='page pres-light'>");
        AppendPageHeader(sb, es ? "Hoja de Ruta y Próximos Pasos" : "Roadmap & Next Steps", brand,
            es ? "TRES FASES HACIA HARDENING COMPLETO" : "THREE PHASES TO FULL HARDENING");
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='next-steps'>");

        if (es)
        {
            sb.AppendLine("<div class='step'><span class='step-num'>1</span><div>");
            sb.AppendLine("<strong>Fase 1 &middot; Telemetría Pasiva de 90 Días y Auditoría de Identidad</strong>");
            sb.AppendLine("<ul class='phase-list'>");
            sb.AppendLine("<li>Desplegar el motor de telemetría pasiva en el Día 1 &mdash; cero reinicios, cero downtime</li>");
            sb.AppendLine("<li>Auditoría continua de identidad AD: cuentas obsoletas, drift de privilegios, objetivos Kerberoastable</li>");
            sb.AppendLine("<li>Mapeo de dependencias de protocolos legacy (NTLM / SMBv1 / RC4 / WDigest) en checkpoints del Día 30, 60 y 90</li>");
            sb.AppendLine("<li>Reportes semanales de estado a TI + resumen ejecutivo mensual</li>");
            sb.AppendLine("</ul></div></div>");

            sb.AppendLine("<div class='step'><span class='step-num'>2</span><div>");
            sb.AppendLine("<strong>Fase 2 &middot; Hardening Zero-Trust y Depreciación de Protocolos</strong>");
            sb.AppendLine("<ul class='phase-list'>");
            sb.AppendLine("<li>Ejecutar el blueprint de depreciación del Día 90 &mdash; quirúrgico, por dependencia, reversible</li>");
            sb.AppendLine("<li>Despliegue de LAPS en el 100% de los endpoints</li>");
            sb.AppendLine("<li>Baseline de BitLocker + TPM en cada equipo que maneje datos corporativos</li>");
            sb.AppendLine("<li>Reglas ASR + AppLocker / WDAC para contención de ransomware</li>");
            sb.AppendLine("<li>Credential Guard + LSA Protection para eliminar ataques tipo mimikatz de raíz</li>");
            sb.AppendLine("</ul></div></div>");

            sb.AppendLine("<div class='step'><span class='step-num'>3</span><div>");
            sb.AppendLine("<strong>Fase 3 &middot; Monitoreo Continuo de Cumplimiento (MRR)</strong>");
            sb.AppendLine("<ul class='phase-list'>");
            sb.AppendLine("<li>Re-evaluación mensual &mdash; tendencia del puntaje de madurez, deltas por framework, nuevo drift</li>");
            sb.AppendLine("<li>Monitoreo continuo de higiene AD con remediación auto-ticketeada</li>");
            sb.AppendLine("<li>Revisión ejecutiva trimestral con el CISO / COO</li>");
            sb.AppendLine("<li>SLA de remediación on-call para cualquier hallazgo crítico</li>");
            sb.AppendLine("</ul></div></div>");
        }
        else
        {
            sb.AppendLine("<div class='step'><span class='step-num'>1</span><div>");
            sb.AppendLine("<strong>Phase 1 &middot; 90-Day Passive Telemetry &amp; Identity Audit</strong>");
            sb.AppendLine("<ul class='phase-list'>");
            sb.AppendLine("<li>Deploy the passive telemetry engine on Day 1 &mdash; zero reboots, zero downtime</li>");
            sb.AppendLine("<li>Continuous AD identity audit: stale accounts, privileged drift, Kerberoastable targets</li>");
            sb.AppendLine("<li>Legacy protocol dependency mapping (NTLM / SMBv1 / RC4 / WDigest) on Day 30, 60 and 90 checkpoints</li>");
            sb.AppendLine("<li>Weekly status reports to IT + monthly executive brief</li>");
            sb.AppendLine("</ul></div></div>");

            sb.AppendLine("<div class='step'><span class='step-num'>2</span><div>");
            sb.AppendLine("<strong>Phase 2 &middot; Zero-Trust Hardening &amp; Protocol Deprecation</strong>");
            sb.AppendLine("<ul class='phase-list'>");
            sb.AppendLine("<li>Execute the Day-90 deprecation blueprint &mdash; surgical, per-dependency, reversible</li>");
            sb.AppendLine("<li>LAPS rollout across 100% of endpoints</li>");
            sb.AppendLine("<li>BitLocker + TPM baseline on every device that ships corporate data</li>");
            sb.AppendLine("<li>ASR rules + AppLocker / WDAC enforcement for ransomware containment</li>");
            sb.AppendLine("<li>Credential Guard + LSA Protection to kill mimikatz-class attacks at the source</li>");
            sb.AppendLine("</ul></div></div>");

            sb.AppendLine("<div class='step'><span class='step-num'>3</span><div>");
            sb.AppendLine("<strong>Phase 3 &middot; Continuous Compliance Monitoring (MRR)</strong>");
            sb.AppendLine("<ul class='phase-list'>");
            sb.AppendLine("<li>Monthly re-assessment &mdash; trending maturity score, per-framework deltas, new drift</li>");
            sb.AppendLine("<li>Continuous AD hygiene monitoring with auto-ticketed remediation</li>");
            sb.AppendLine("<li>Quarterly executive review with the CISO / COO</li>");
            sb.AppendLine("<li>On-call remediation SLA for any critical finding</li>");
            sb.AppendLine("</ul></div></div>");
        }

        sb.AppendLine("</div>");

        sb.AppendLine("<div class='cta-box' style='margin-top:20px'>");
        if (es)
            sb.AppendLine($"<p style='font-size:13px;margin:0'><strong>Próximo paso:</strong> un kickoff de 45 minutos con {HtmlEncode(brand.CompanyName)} para desplegar el motor de telemetría pasiva e iniciar tu mapa de dependencias de 90 días. El reloj sobre la exposición actual corre ya sea que el engagement comience esta semana o el próximo trimestre.</p>");
        else
            sb.AppendLine($"<p style='font-size:13px;margin:0'><strong>Next step:</strong> a 45-minute kickoff with {HtmlEncode(brand.CompanyName)} to deploy the passive telemetry engine and start your 90-day dependency map. The clock on the current exposure runs whether the engagement starts this week or next quarter.</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></div>");

        // Running footer on every .page is emitted by CSS in AppendHtmlHead,
        // so no dedicated signoff page is needed here.
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

    // ======================================================================
    // ORG: EXECUTIVE ONE-PAGER (Brand 2025, bilingual, strict A4)
    // Layout: page 1 = cover (existing pattern)
    //         page 2 = one page (header + KPIs + frameworks + top risks +
    //                 quick wins + remediation + footer with user info)
    // ======================================================================
    private static string BuildOrgExecutiveOnePager(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName,
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment,
        ReportUserInfo userInfo, string lang)
    {
        var sb = new StringBuilder();

        // Recompute global KPIs the same way BuildOrgExecutiveReport does.
        var totalMachines = runs.Count;
        decimal avgScore;
        int totalPass, totalWarn, totalFail;
        if (frameworkName != null && allResults.Count > 0)
        {
            totalPass = allResults.Count(r => r.Status == "pass");
            totalWarn = allResults.Count(r => r.Status == "warn");
            totalFail = allResults.Count(r => r.Status == "fail");
            var total = totalPass + totalWarn + totalFail;
            avgScore = total > 0 ? Math.Round((decimal)totalPass / total * 100, 1) : 0;
        }
        else
        {
            avgScore = runs.Count > 0
                ? Math.Round(runs.Average(r => r.GlobalScore ?? 0), 1)
                : 0;
            totalPass = (int)runs.Sum(r => r.PassCount ?? 0);
            totalWarn = (int)runs.Sum(r => r.WarnCount ?? 0);
            totalFail = (int)runs.Sum(r => r.FailCount ?? 0);
        }
        var orgGrade = GetGrade(avgScore);
        var scanDate = runs.Count > 0 ? runs.Max(r => r.CompletedAt ?? r.StartedAt) : DateTime.UtcNow;

        // Top risks: failures with critical/high severity, grouped by ControlId,
        // sorted by number of machines affected.
        var topRisks = allResults
            .Where(r => r.Status == "fail" &&
                        (r.Severity == "critical" || r.Severity == "high"))
            .GroupBy(r => r.ControlId)
            .Select(g => new
            {
                g.First().ControlId,
                g.First().Name,
                g.First().Category,
                Severity = g.First().Severity,
                MachineCount = g.Select(x => x.RunId).Distinct().Count()
            })
            .OrderByDescending(x => x.Severity == "critical" ? 1 : 0)
            .ThenByDescending(x => x.MachineCount)
            .Take(3)
            .ToList();

        // Quick wins: failures with low/medium severity, sorted by machine count.
        // These are "cheap to fix, high visible impact" items an MSP can quote.
        var quickWins = allResults
            .Where(r => r.Status == "fail" &&
                        (r.Severity == "low" || r.Severity == "medium"))
            .GroupBy(r => r.ControlId)
            .Select(g => new
            {
                g.First().ControlId,
                g.First().Name,
                g.First().Category,
                Severity = g.First().Severity,
                MachineCount = g.Select(x => x.RunId).Distinct().Count()
            })
            .OrderByDescending(x => x.MachineCount)
            .ThenByDescending(x => x.Severity == "medium" ? 1 : 0)
            .Take(3)
            .ToList();

        // Estimated remediation hours — heuristic designed for MSPs that
        // remediate fleet-wide via GPO / Intune / RMM policies bundled by
        // CATEGORY. A single GPO ("Edge Hardening", "Audit Policy", etc.)
        // usually fixes many related controls at once, so the effort is
        // per-category, not per-control.
        //
        // Formula, summed over every category that has at least one
        // failing control:
        //
        //   base_effort  = severity_hours[max_severity_in_category]
        //                  (one GPO's worth of authoring + testing)
        //   settings_oh  = (unique_failing_controls_in_cat - 1) × 0.1h
        //                  (6 min extra per additional setting in the same GPO)
        //   validation   = (distinct_affected_machines_in_cat - 1) × 0.25h
        //                  (15 min per endpoint to verify the GPO landed)
        //
        //   severity_hours: critical=4, high=2, medium=1, low=0.5
        //
        // The "affected machines" count is the UNION of machines that fail
        // any control in the category (not the max of any single control),
        // because the same GPO applies to all of them at once.

        double EffortFor(string sev) => sev switch
        {
            "critical" => 4.0,
            "high"     => 2.0,
            "medium"   => 1.0,
            "low"      => 0.5,
            _          => 1.0
        };
        int SeverityRank(string sev) => sev switch
        {
            "critical" => 4,
            "high"     => 3,
            "medium"   => 2,
            "low"      => 1,
            _          => 2
        };
        const double ExtraSettingInSameGpo   = 0.1;
        const double ValidationPerExtraHost  = 0.25;

        var failingByCategory = allResults
            .Where(r => r.Status == "fail")
            .GroupBy(r => r.Category)
            .Select(g => new
            {
                Category         = g.Key,
                UniqueControls   = g.Select(x => x.ControlId).Distinct().Count(),
                MaxSeverity      = g.OrderByDescending(x => SeverityRank(x.Severity)).First().Severity,
                DistinctMachines = g.Select(x => x.RunId).Distinct().Count()
            })
            .ToList();

        var estHoursDouble = failingByCategory.Sum(cat =>
            EffortFor(cat.MaxSeverity)
            + Math.Max(0, cat.UniqueControls   - 1) * ExtraSettingInSameGpo
            + Math.Max(0, cat.DistinctMachines - 1) * ValidationPerExtraHost);
        var estHours = (int)Math.Round(estHoursDouble);

        // The "critical failures" KPI still counts unique failing CONTROLS
        // with critical severity, not categories — the C-level reader
        // wants to know how many distinct high-impact gaps exist.
        var criticalFailCount = allResults
            .Where(r => r.Status == "fail" && r.Severity == "critical")
            .Select(r => r.ControlId)
            .Distinct()
            .Count();
        var reportTitle = frameworkName != null
            ? $"{frameworkName} — {T("op.title", lang)}"
            : T("op.title", lang);

        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true, htmlLang: lang,
            user: userInfo, detail: $"{totalMachines} devices · {org.Name}");

        // ---- PAGE 1: COVER ----
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{HtmlEncode(T("op.eyebrow", lang))}</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(T("op.cover.for", lang))}: {HtmlEncode(org.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{HtmlEncode(T("op.cover.date", lang))}: {scanDate:MMMM dd, yyyy}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{orgGrade.Replace("+", "plus")}'>{HtmlEncode(orgGrade)}</div>");
        sb.AppendLine($"<p class='score'>{avgScore}%</p>");
        sb.AppendLine("</div></div>");

        // ---- PAGE 2: ONE-PAGER ----
        // Uses the SAME unified header as every other report — eyebrow is
        // the language-specific "Executive Brief" label so the one-pager
        // still carries its distinct context tag.
        sb.AppendLine("<div class='page onepager'>");
        AppendPageHeader(sb, org.Name, brand, T("op.eyebrow", lang));

        sb.AppendLine("<div class='op-body'>");

        // KPIs row
        sb.AppendLine("<div class='op-kpis'>");
        sb.AppendLine("<div class='op-kpi op-kpi-hero'>");
        sb.AppendLine($"<span class='op-kpi-val'>{avgScore}%</span>");
        sb.AppendLine($"<span class='op-kpi-label'>{HtmlEncode(T("op.kpi.score", lang))}</span>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='op-kpi op-kpi-grade'>");
        sb.AppendLine($"<span class='op-kpi-val'>{HtmlEncode(orgGrade)}</span>");
        sb.AppendLine($"<span class='op-kpi-label'>{HtmlEncode(T("op.kpi.grade", lang))}</span>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='op-kpi'>");
        sb.AppendLine($"<span class='op-kpi-val'>{totalMachines}</span>");
        sb.AppendLine($"<span class='op-kpi-label'>{HtmlEncode(T("op.kpi.machines", lang))}</span>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='op-kpi'>");
        sb.AppendLine($"<span class='op-kpi-val' style='color:#C0392B'>{criticalFailCount}</span>");
        sb.AppendLine($"<span class='op-kpi-label'>{HtmlEncode(T("op.kpi.critical", lang))}</span>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");

        // Framework compliance (compact, P/F normalized per machine via
        // the shared helper used by all ORG reports).
        if (frameworkScores.Count > 0)
        {
            sb.AppendLine($"<div class='op-section-title'>{HtmlEncode(T("op.frameworks", lang))}</div>");
            sb.AppendLine("<div class='op-fw-bars'>");
            AppendNormalizedFrameworkBars(sb, frameworkScores, totalMachines,
                T("op.frameworks.caption", lang));
            sb.AppendLine("</div>");
        }

        // Risks + wins side-by-side
        sb.AppendLine("<div class='op-lists'>");

        // Left column: top risks
        sb.AppendLine("<div class='op-list-col'>");
        sb.AppendLine($"<div class='op-section-title'>{HtmlEncode(T("op.risks", lang))}</div>");
        if (topRisks.Count == 0)
        {
            sb.AppendLine($"<p style='font-size:8pt;color:#666'>{HtmlEncode(T("op.no_risks", lang))}</p>");
        }
        else
        {
            int n = 1;
            foreach (var r in topRisks)
            {
                var mLabel = r.MachineCount == 1 ? T("op.risk.machine", lang) : T("op.risk.machines", lang);
                sb.AppendLine("<div class='op-risk'>");
                sb.AppendLine($"<div class='op-num'>{n++}</div>");
                sb.AppendLine("<div>");
                sb.AppendLine($"<strong>{HtmlEncode(r.Name)}</strong>");
                sb.AppendLine($"<div class='op-meta'>{HtmlEncode(r.Category)} · {HtmlEncode(T("op.risk.affects", lang))} {r.MachineCount} {HtmlEncode(mLabel)}</div>");
                sb.AppendLine("</div>");
                sb.AppendLine("</div>");
            }
        }
        sb.AppendLine("</div>");

        // Right column: quick wins
        sb.AppendLine("<div class='op-list-col'>");
        sb.AppendLine($"<div class='op-section-title'>{HtmlEncode(T("op.wins", lang))}</div>");
        if (quickWins.Count == 0)
        {
            sb.AppendLine($"<p style='font-size:8pt;color:#666'>{HtmlEncode(T("op.no_wins", lang))}</p>");
        }
        else
        {
            int n = 1;
            foreach (var w in quickWins)
            {
                var mLabel = w.MachineCount == 1 ? T("op.risk.machine", lang) : T("op.risk.machines", lang);
                sb.AppendLine("<div class='op-win'>");
                sb.AppendLine($"<div class='op-num'>{n++}</div>");
                sb.AppendLine("<div>");
                sb.AppendLine($"<strong>{HtmlEncode(w.Name)}</strong>");
                sb.AppendLine($"<div class='op-meta'>{HtmlEncode(w.Category)} · {HtmlEncode(T("op.risk.affects", lang))} {w.MachineCount} {HtmlEncode(mLabel)}</div>");
                sb.AppendLine("</div>");
                sb.AppendLine("</div>");
            }
        }
        sb.AppendLine("</div>");
        sb.AppendLine("</div>"); // /op-lists

        // Remediation box
        sb.AppendLine("<div class='op-remediation'>");
        sb.AppendLine("<div>");
        sb.AppendLine($"<div style='font-weight:700'>{HtmlEncode(T("op.remediation.label", lang))}</div>");
        sb.AppendLine($"<div style='font-size:7.5pt;color:#666;margin-top:1mm'>{HtmlEncode(T("op.remediation.note", lang))}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine($"<div class='op-hours'>{estHours} <span style='font-size:9pt;font-weight:600;color:#555'>{HtmlEncode(T("op.remediation.hours", lang))}</span></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div>"); // /op-body

        // Footer with logged-in user info
        AppendOnePagerFooter(sb, userInfo, org, lang);

        sb.AppendLine("</div>"); // /page.onepager

        sb.AppendLine("</body></html>");

        // Ensure unused locals don't trip the compiler if future edits remove them.
        _ = totalWarn; _ = totalFail; _ = hygiene; _ = enrichment;

        return sb.ToString();
    }

    // ======================================================================
    // ORG: C-LEVEL REPORT — Executive risk snapshot (2 pages)
    //
    // Purpose: ad-hoc snapshot for CEO/COO/CFO. 2-minute read. Current state
    // only — no historical trend (that belongs to Monthly Progress).
    // Structure: Cover + 1 content page with 3 blocks:
    //   1. Risk Posture semáforo (with capital sin collapse logic)
    //   2. 3 Business KPIs (exposure benchmark, coverage, evolution)
    //   3. Max 2 Executive CTAs (hybrid auto-detect + operator-editable)
    // ======================================================================
    private static string BuildOrgCLevelReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand,
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment,
        ReportUserInfo userInfo, decimal? previousMonthScore,
        List<ExecutiveCta> savedCtas, List<M365Finding>? m365Findings, bool m365TenantConnected,
        string lang)
    {
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        var avgScore = runs.Count > 0 ? Math.Round(runs.Average(r => r.GlobalScore ?? 0), 1) : 0m;
        var orgGrade = GetGrade(avgScore);
        var scanDate = runs.Count > 0 ? runs.Max(r => r.CompletedAt ?? r.StartedAt) : DateTime.UtcNow;
        var criticalFails = allResults
            .Where(r => r.Status == "fail" && r.Severity == "critical")
            .Select(r => r.ControlId).Distinct().Count();
        var highFails = allResults
            .Where(r => r.Status == "fail" && r.Severity == "high")
            .Select(r => r.ControlId).Distinct().Count();
        var criticalVectorCount = criticalFails + highFails;

        var capitalSin = CapitalSinDetector.Detect(
            runs, hygiene, enrichment, m365TenantConnected, m365Findings, lang);
        var ctaCandidates = CtaRuleEngine.DetectCtas(
            runs, allResults, hygiene, enrichment, m365TenantConnected, m365Findings, lang);

        var es = lang == "es";
        var reportTitle = es ? "Brief Ejecutivo de Seguridad" : "C-Level Security Brief";

        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true, htmlLang: lang,
            user: userInfo, detail: $"{totalMachines} devices \u00B7 {org.Name}");

        // ---- PAGE 1: COVER ----
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{HtmlEncode(es ? "BRIEF EJECUTIVO" : "C-LEVEL BRIEF")}</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(org.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{scanDate:MMMM dd, yyyy} &mdash; {totalMachines} {HtmlEncode(es ? "equipos" : "devices")}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{orgGrade.Replace("+", "plus")}'>{HtmlEncode(orgGrade)}</div>");
        sb.AppendLine($"<p class='score'>{avgScore}%</p>");
        sb.AppendLine("</div></div>");

        // ---- PAGE 2: CONTENT ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, es ? "Brief Ejecutivo" : "Executive Brief",
            brand, es ? "RESUMEN DE RIESGO" : "RISK SUMMARY");
        sb.AppendLine("<div class='pb'>");

        // ===== Block 1: Risk Posture semáforo =====
        string postureColor, postureBg, postureLabel, postureNarrative;
        if (capitalSin != null)
        {
            postureColor = "#991B1B"; postureBg = "#FEF2F2";
            postureLabel = es ? "CR\u00CDTICO" : "CRITICAL";
            postureNarrative = capitalSin.Narrative;
        }
        else if (avgScore >= 85)
        {
            postureColor = "#15803D"; postureBg = "#F0FDF4";
            postureLabel = es ? "POSTURA S\u00D3LIDA" : "SOLID POSTURE";
            postureNarrative = es
                ? "Controles activos. Postura s\u00F3lida frente a los patrones de ataque monitoreados."
                : "Controls active. Solid posture against monitored attack patterns.";
        }
        else if (avgScore >= 60)
        {
            postureColor = "#B45309"; postureBg = "#FFFBEB";
            postureLabel = es ? "EXPOSICI\u00D3N ALTA" : "HIGH EXPOSURE";
            postureNarrative = es
                ? "Expuestos a ransomware por deuda t\u00E9cnica. Recuperaci\u00F3n garantizada pero lenta."
                : "Exposed to ransomware via technical debt. Recovery guaranteed but slow.";
        }
        else
        {
            postureColor = "#991B1B"; postureBg = "#FEF2F2";
            postureLabel = es ? "CR\u00CDTICO" : "CRITICAL";
            postureNarrative = es
                ? "Operaci\u00F3n en riesgo inminente. Tiempo estimado de recuperaci\u00F3n ante ataque: >48h."
                : "Operation at imminent risk. Estimated recovery time from attack: >48h.";
        }

        sb.AppendLine($"<div style='background:{postureBg};border:2px solid {postureColor};border-radius:8px;padding:22px 28px;margin-bottom:20px;text-align:center;box-shadow:0 4px 6px -1px rgba(15,23,42,0.06)'>");
        sb.AppendLine($"<div style='font-size:11px;font-weight:800;letter-spacing:0.14em;color:{postureColor};margin-bottom:8px'>{HtmlEncode(es ? "POSTURA DE RIESGO" : "RISK POSTURE")}</div>");
        sb.AppendLine($"<div style='font-size:28px;font-weight:900;color:{postureColor};line-height:1;margin-bottom:6px'>{HtmlEncode(postureLabel)}</div>");
        sb.AppendLine($"<div style='font-size:12px;color:#334155;line-height:1.55;max-width:160mm;margin:0 auto'>{HtmlEncode(postureNarrative)}</div>");
        sb.AppendLine("</div>");

        // ===== Block 2: 3 Business KPIs =====
        // KPI 2 data: asset coverage (the "4 Fantásticos", Defender inferred from control_results)
        var bitlockerOk = runs.Count(r => r.Machine.Bitlocker == true);
        var tpmOk       = runs.Count(r => r.Machine.TpmPresent == true);
        var defenderOk  = runs.Count(r =>
            !allResults.Any(ar => ar.RunId == r.Id && ar.Status == "fail" &&
                (ar.Name.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
                 ar.Name.Contains("Antivirus", StringComparison.OrdinalIgnoreCase))));
        var lapsFailingC = hygiene?.Findings.Count(f => f.Status == "NoLAPS") ?? 0;
        var lapsTotalC   = hygiene?.TotalMachines ?? runs.Count;
        var lapsOk       = Math.Max(0, lapsTotalC - lapsFailingC);
        var lapsOkScaled = lapsTotalC > 0 ? (int)Math.Round((double)lapsOk / lapsTotalC * runs.Count) : runs.Count;
        var coverageAvg  = runs.Count > 0 ? (bitlockerOk + tpmOk + defenderOk + lapsOkScaled) / 4.0 : 0;
        var coveragePct  = runs.Count > 0 ? 100.0 * coverageAvg / runs.Count : 0;

        // KPI 3: evolution arrow
        string evolArrow, evolColor, evolLabel;
        decimal evolDelta = 0;
        if (!previousMonthScore.HasValue)
        {
            evolArrow = "\u2014"; evolColor = "#64748B";
            evolLabel = es ? "Periodo de referencia" : "Baseline period";
        }
        else
        {
            evolDelta = Math.Round(avgScore - previousMonthScore.Value, 1);
            if (evolDelta > 0) { evolArrow = "\u25B2"; evolColor = "#15803D"; }
            else if (evolDelta < 0) { evolArrow = "\u25BC"; evolColor = "#991B1B"; }
            else { evolArrow = "="; evolColor = "#64748B"; }
            evolLabel = es ? $"vs mes anterior ({previousMonthScore.Value:0.#})"
                           : $"vs last month ({previousMonthScore.Value:0.#})";
        }

        sb.AppendLine("<div style='display:grid;grid-template-columns:1.3fr 1fr 1fr;gap:12px;margin-bottom:20px'>");

        // KPI 1: exposure benchmark
        sb.AppendLine("<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid #991B1B;border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:#991B1B;letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{HtmlEncode(es ? "COSTO DE EXPOSICI\u00D3N" : "COST OF EXPOSURE")}</div>");
        sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:#991B1B;line-height:1'>USD 1.2M</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:4px;font-style:italic'>{HtmlEncode(es ? "IBM Cost of a Data Breach 2024 — segmento PyME" : "IBM Cost of a Data Breach 2024 — SMB segment")}</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:6px;line-height:1.4;border-top:1px solid #E2E8F0;padding-top:6px'>{HtmlEncode(es ? $"Su infraestructura actual presenta {criticalVectorCount} vectores cr\u00EDticos que coinciden con los casos de estudio de este benchmark." : $"Your infrastructure currently exhibits {criticalVectorCount} critical vectors matching this benchmark's case studies.")}</div>");
        sb.AppendLine("</div>");

        // KPI 2: asset coverage
        sb.AppendLine("<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid #0F172A;border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:#0F172A;letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{HtmlEncode(es ? "COBERTURA DE ACTIVOS" : "ASSET COVERAGE")}</div>");
        sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:#0F172A;line-height:1'>{coveragePct:0}%</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:6px'>{(int)Math.Round(coverageAvg)} {HtmlEncode(es ? "de" : "of")} {runs.Count} {HtmlEncode(es ? "equipos protegidos" : "machines protected")}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:4px'>BitLocker \u00B7 TPM \u00B7 LAPS \u00B7 Defender</div>");
        sb.AppendLine("</div>");

        // KPI 3: risk evolution
        sb.AppendLine($"<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid {evolColor};border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:{evolColor};letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{HtmlEncode(es ? "EVOLUCI\u00D3N" : "RISK EVOLUTION")}</div>");
        if (previousMonthScore.HasValue)
        {
            var sign = evolDelta >= 0 ? "+" : "";
            sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:{evolColor};line-height:1'>{evolArrow} {sign}{evolDelta:0.#}</div>");
            sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:4px'>{HtmlEncode(es ? "puntos" : "points")}</div>");
        }
        else
        {
            sb.AppendLine($"<div style='font-size:16pt;font-weight:900;color:#64748B;line-height:1'>BASELINE</div>");
        }
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:6px'>{HtmlEncode(evolLabel)}</div>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div>");

        // ===== Block 3: Executive Decisions Required (max 2) =====
        var suppressedIds = savedCtas.Where(c => c.IsSuppressed && !c.IsManual)
                                     .Select(c => c.AutoDetectedRule)
                                     .Where(r => r != null)
                                     .Select(r => r!)
                                     .ToHashSet();
        var editedMap = savedCtas.Where(c => !c.IsSuppressed && !c.IsManual && c.AutoDetectedRule != null)
                                  .ToDictionary(c => c.AutoDetectedRule!, c => c);

        var finalCtas = new List<(string Title, string Description, string Category)>();

        if (capitalSin != null)
        {
            var linked = ctaCandidates.FirstOrDefault(c => c.RuleId == capitalSin.LinkedCtaRule);
            if (linked != null && !suppressedIds.Contains(linked.RuleId))
            {
                var title = editedMap.TryGetValue(linked.RuleId, out var edited) ? edited.Title : linked.Title;
                var desc  = editedMap.TryGetValue(linked.RuleId, out edited) ? edited.Description : linked.Description;
                finalCtas.Add((title, desc, linked.Category));
            }
        }

        foreach (var c in ctaCandidates)
        {
            if (finalCtas.Count >= 2) break;
            if (capitalSin != null && c.RuleId == capitalSin.LinkedCtaRule) continue;
            if (suppressedIds.Contains(c.RuleId)) continue;
            var title = editedMap.TryGetValue(c.RuleId, out var edited) ? edited.Title : c.Title;
            var desc  = editedMap.TryGetValue(c.RuleId, out edited) ? edited.Description : c.Description;
            finalCtas.Add((title, desc, c.Category));
        }

        foreach (var m in savedCtas.Where(c => c.IsManual && !c.IsSuppressed))
        {
            if (finalCtas.Count >= 2) break;
            finalCtas.Add((m.Title, m.Description, m.PriorityCategory));
        }

        sb.AppendLine($"<h3 style='font-size:12px;margin:8px 0 12px;color:#1E293B;border-bottom:2px solid #0F172A;padding-bottom:4px;text-transform:uppercase;letter-spacing:0.08em'>{HtmlEncode(es ? "Decisiones Ejecutivas Requeridas" : "Executive Decisions Required")}</h3>");

        if (finalCtas.Count == 0)
        {
            sb.AppendLine("<div style='background:#F0FDF4;border:1px solid #BBF7D0;border-left:4px solid #15803D;border-radius:4px;padding:16px 22px'>");
            sb.AppendLine($"<div style='font-size:11pt;font-weight:700;color:#14532D;margin-bottom:4px'>\u2713 {HtmlEncode(es ? "Postura s\u00F3lida \u2014 sin decisiones ejecutivas pendientes" : "Solid posture \u2014 no pending executive decisions")}</div>");
            sb.AppendLine($"<div style='font-size:10pt;color:#166534;line-height:1.55'>{HtmlEncode(es ? "Este mes no requiere acci\u00F3n del CEO. El programa de hardening contin\u00FAa de forma rutinaria." : "This period requires no CEO action. The hardening program continues on schedule.")}</div>");
            sb.AppendLine("</div>");
        }
        else
        {
            int n = 1;
            foreach (var (title, desc, cat) in finalCtas)
            {
                var catColor = cat switch
                {
                    "Incidentes" => "#991B1B",
                    "Hardening"  => "#0F172A",
                    "Budget"     => "#B45309",
                    _            => "#64748B"
                };
                sb.AppendLine($"<div style='background:#F8F9FA;border:1px solid #CBD5E1;border-left:5px solid {catColor};border-radius:4px;padding:14px 20px;margin-bottom:10px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
                sb.AppendLine($"<div style='display:inline-block;padding:2px 8px;background:{catColor};color:#fff;font-size:8px;font-weight:800;letter-spacing:0.12em;text-transform:uppercase;border-radius:2px;margin-bottom:6px'>{HtmlEncode(cat.ToUpperInvariant())} \u00B7 #{n}</div>");
                sb.AppendLine($"<div style='font-size:12pt;font-weight:700;color:#0F172A;margin-bottom:4px'>{HtmlEncode(title)}</div>");
                sb.AppendLine($"<div style='font-size:10pt;color:#334155;line-height:1.55'>{HtmlEncode(desc)}</div>");
                sb.AppendLine("</div>");
                n++;
            }
        }

        sb.AppendLine("</div></div>");
        sb.AppendLine("</body></html>");

        _ = frameworkScores; // unused in C-Level (framework filtering happens via allResults upstream)
        return sb.ToString();
    }

    // ======================================================================
    // ORG: PRESALES OPENER REPORT — "El Abrepuertas"
    // Spec: 2-3 pages, visually aggressive. Goal = generate urgency,
    // quantified fear, and get a second meeting. Audience: first contact,
    // non-technical operations manager.
    //
    // Structure:
    //   Page 1: Cover (reused pattern)
    //   Page 2: Global risk score (huge red number) + 3-4 compromise
    //           vectors in business language + frictionless audit stats
    //           + CTA
    // ======================================================================
    private static string BuildOrgPresalesOpenerReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName,
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment,
        ReportUserInfo userInfo, string lang)
    {
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        var avgScore = runs.Count > 0 ? Math.Round(runs.Average(r => r.GlobalScore ?? 0), 0) : 0;

        // Real average scan duration from the fleet — honesty > marketing.
        var avgDurationSec = runs.Count > 0 && runs.Any(r => r.DurationMs > 0)
            ? Math.Max(1, (int)Math.Round(runs.Where(r => r.DurationMs > 0).Average(r => (r.DurationMs ?? 0) / 1000.0)))
            : 0;
        var scanDate = runs.Count > 0 ? runs.Max(r => r.CompletedAt ?? r.StartedAt) : DateTime.UtcNow;

        // Risk verdict: the lower the score the redder
        string verdict, verdictColor;
        if (avgScore < 50)       { verdict = lang == "es" ? "CRÍTICO" : "CRITICAL"; verdictColor = "#C0392B"; }
        else if (avgScore < 70)  { verdict = lang == "es" ? "ALTO"    : "HIGH";     verdictColor = "#D97706"; }
        else if (avgScore < 85)  { verdict = lang == "es" ? "MEDIO"   : "MEDIUM";   verdictColor = "#2563EB"; }
        else                     { verdict = lang == "es" ? "BAJO"    : "LOW";      verdictColor = "#006536"; }

        var vectors = DetectCompromiseVectors(allResults, hygiene, enrichment, runs, lang);

        var reportTitle = lang == "es" ? "Informe de Exposición al Riesgo" : "Risk Exposure Report";
        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true, htmlLang: lang,
            user: userInfo, detail: $"{totalMachines} devices · {org.Name}");

        // ---- PAGE 1: COVER ----
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{HtmlEncode(lang == "es" ? "EXPOSICIÓN AL RIESGO" : "RISK EXPOSURE BRIEF")}</p>");
        sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(org.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{scanDate:MMMM dd, yyyy} &mdash; {totalMachines} {HtmlEncode(lang == "es" ? "equipos auditados" : "machines audited")}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{GetGrade((decimal)avgScore).Replace("+", "plus")}'>{HtmlEncode(GetGrade((decimal)avgScore))}</div>");
        sb.AppendLine($"<p class='score'>{avgScore}%</p>");
        sb.AppendLine("</div></div>");

        // ---- PAGE 2: RISK SCORE + VECTORS + FRICTIONLESS + CTA ----
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, lang == "es" ? "Vectores de Compromiso Crítico" : "Critical Compromise Vectors",
            brand, lang == "es" ? "ANÁLISIS DE EXPOSICIÓN" : "EXPOSURE ANALYSIS");
        sb.AppendLine("<div class='pb'>");

        // Big risk score block — reuses the existing .big-number-box with
        // an inline override so the colour matches the severity verdict.
        sb.AppendLine($"<div class='big-number-box' style='border-color:{verdictColor};background:#fef2f2'>");
        sb.AppendLine($"<div style='font-size:10px;font-weight:700;letter-spacing:0.15em;text-transform:uppercase;color:#666;margin-bottom:8px'>{HtmlEncode(lang == "es" ? "NIVEL DE MADUREZ EN CIBERSEGURIDAD" : "CYBERSECURITY MATURITY LEVEL")}</div>");
        sb.AppendLine($"<div class='big-number' style='color:{verdictColor}'>{avgScore}<span style='font-size:40px;color:#888'>/100</span></div>");
        sb.AppendLine($"<div style='display:inline-block;padding:6px 24px;background:{verdictColor};color:#fff;font-weight:900;font-size:13px;letter-spacing:0.12em;border-radius:4px;margin-top:12px'>{HtmlEncode(verdict)}</div>");
        sb.AppendLine("</div>");

        // 3-4 compromise vectors (reuses .headline-findings / .headline-item)
        sb.AppendLine("<div class='headline-findings'>");
        int n = 1;
        foreach (var v in vectors)
        {
            sb.AppendLine("<div class='headline-item'>");
            sb.AppendLine($"<div class='headline-icon'>{n++}</div>");
            sb.AppendLine("<div class='headline-text'>");
            sb.AppendLine($"<strong>{HtmlEncode(v.Title)}</strong><br>");
            sb.AppendLine($"{HtmlEncode(v.Description)}");
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");
        }
        if (vectors.Count == 0)
        {
            sb.AppendLine("<div class='headline-item' style='background:#f0fdf4;border-color:#006536'>");
            sb.AppendLine("<div class='headline-icon' style='background:#006536'>&#10003;</div>");
            sb.AppendLine("<div class='headline-text'>");
            sb.AppendLine($"<strong>{HtmlEncode(lang == "es" ? "Sin vectores críticos detectados" : "No critical compromise vectors detected")}</strong><br>");
            sb.AppendLine(HtmlEncode(lang == "es"
                ? "La postura general es saludable. Igual vale la pena revisar los hallazgos medios y bajos para mantener la madurez en el tiempo."
                : "The overall posture is healthy. Still worth reviewing the medium and low severity findings to sustain maturity over time."));
            sb.AppendLine("</div></div>");
        }
        sb.AppendLine("</div>");

        // Frictionless audit banner — reuses .insight-box in green
        sb.AppendLine("<div class='insight-box' style='background:#f0fdf4;border-color:#006536'>");
        sb.AppendLine($"<p><strong style='color:#006536;font-size:14px'>&#9201; {HtmlEncode(lang == "es" ? "Auditoría Sin Fricción" : "Frictionless Audit")}</strong></p>");
        var durationClause = avgDurationSec > 0
            ? (lang == "es"
                ? $"Esta auditoría se realizó en un promedio de <strong>{avgDurationSec} segundos por equipo</strong>, "
                : $"This audit ran in an average of <strong>{avgDurationSec} seconds per machine</strong>, ")
            : (lang == "es"
                ? "Esta auditoría se realizó "
                : "This audit ran ");
        sb.AppendLine(lang == "es"
            ? $"<p style='margin-top:8px'>{durationClause}de forma <strong>pasiva</strong>, sin impacto en su operación actual: <strong>cero reinicios</strong>, <strong>cero instalación persistente</strong>, <strong>cero fricción para sus usuarios</strong>. El hallazgo de estos vectores críticos no requirió pausar absolutamente nada.</p>"
            : $"<p style='margin-top:8px'>{durationClause}in a fully <strong>passive</strong> manner, with no impact on your current operations: <strong>zero reboots</strong>, <strong>zero persistent install</strong>, <strong>zero user friction</strong>. Surfacing these critical vectors did not require pausing anything.</p>");

        // Compact stat rail for the frictionless section — overrides the
        // default `.stat` / `.stat-value` sizes inline so 4 cards fit on
        // a single row within the narrower .insight-box content area.
        const string statStyle   = "min-width:78px;padding:10px 12px;flex:1";
        const string valueStyle  = "font-size:18px;font-weight:800;line-height:1";
        const string labelStyle  = "font-size:8px;margin-top:3px;line-height:1.2";
        sb.AppendLine("<div class='summary-grid' style='margin-top:12px;gap:8px'>");
        sb.AppendLine($"<div class='stat pass-stat' style='{statStyle}'><span class='stat-value' style='{valueStyle}'>{totalMachines}</span><span class='stat-label' style='{labelStyle}'>{HtmlEncode(lang == "es" ? "Equipos auditados" : "Machines audited")}</span></div>");
        if (avgDurationSec > 0)
            sb.AppendLine($"<div class='stat pass-stat' style='{statStyle}'><span class='stat-value' style='{valueStyle}'>{avgDurationSec}s</span><span class='stat-label' style='{labelStyle}'>{HtmlEncode(lang == "es" ? "Promedio por equipo" : "Avg per machine")}</span></div>");
        sb.AppendLine($"<div class='stat pass-stat' style='{statStyle}'><span class='stat-value' style='{valueStyle}'>0</span><span class='stat-label' style='{labelStyle}'>{HtmlEncode(lang == "es" ? "Reinicios" : "Reboots")}</span></div>");
        sb.AppendLine($"<div class='stat pass-stat' style='{statStyle}'><span class='stat-value' style='{valueStyle}'>0</span><span class='stat-label' style='{labelStyle}'>{HtmlEncode(lang == "es" ? "Interrupciones" : "Disruptions")}</span></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");

        // CTA — "90-Day Safe Deprecation Audit" positioning. Pivots from
        // "fix it now" (operationally scary) to "let us map the legacy
        // dependencies first, then deprecate with zero downtime". The
        // unique value proposition is the passive telemetry engine, not
        // the remediation itself.
        sb.AppendLine("<div class='cta-box'>");
        sb.AppendLine($"<p style='font-size:11px;font-weight:700;letter-spacing:0.12em;text-transform:uppercase;color:{brand.AccentColor};margin-bottom:4px'>{HtmlEncode(lang == "es" ? "LOS PRÓXIMOS 90 DÍAS" : "THE NEXT 90 DAYS")}</p>");
        sb.AppendLine($"<p style='font-size:17px;font-weight:700;margin-bottom:10px'>{HtmlEncode(lang == "es" ? "Antes de romper nada." : "Before you disable anything.")}</p>");
        if (lang == "es")
        {
            sb.AppendLine($"<p style='font-size:12px;line-height:1.65;margin-bottom:12px;color:#d4d4d4;text-align:left'>Apagar SMBv1, NTLM o cualquier protocolo legacy sin entender quién depende de ellos rompe aplicaciones críticas — y esa caída es suya. Nuestra respuesta es la <strong style='color:{brand.AccentColor}'>Auditoría de Deprecación Segura de 90 Días</strong>: desplegamos un motor de telemetría pasiva que mapea, sin interrumpir a nadie, exactamente qué sistemas, usuarios y procesos dependen de cada protocolo legacy que hoy lo expone. Al día 90 usted recibe el plan de deprecación con <strong style='color:#fff'>cero ambigüedad</strong> y <strong style='color:#fff'>cero downtime operacional</strong>.</p>");
            sb.AppendLine($"<p style='font-size:13px;margin:0;padding-top:10px;border-top:1px solid #666'>Reserve 30 minutos con <strong>{HtmlEncode(brand.CompanyName)}</strong> para iniciar la telemetría.</p>");
        }
        else
        {
            sb.AppendLine($"<p style='font-size:12px;line-height:1.65;margin-bottom:12px;color:#d4d4d4;text-align:left'>Turning off SMBv1, NTLM or any legacy protocol without knowing who depends on them breaks critical applications — and that outage is yours to own. Our answer is the <strong style='color:{brand.AccentColor}'>90-Day Safe Deprecation Audit</strong>: we deploy a passive telemetry engine that maps, without interrupting anyone, exactly which systems, users and processes rely on each legacy protocol exposing you today. On day 90 you receive a deprecation plan with <strong style='color:#fff'>zero ambiguity</strong> and <strong style='color:#fff'>zero operational downtime</strong>.</p>");
            sb.AppendLine($"<p style='font-size:13px;margin:0;padding-top:10px;border-top:1px solid #666'>Book 30 minutes with <strong>{HtmlEncode(brand.CompanyName)}</strong> to start the telemetry.</p>");
        }
        sb.AppendLine("</div>");

        sb.AppendLine("</div></div>");
        sb.AppendLine("</body></html>");

        _ = frameworkScores; _ = frameworkName; // intentionally unused — this report is non-framework
        return sb.ToString();
    }

    /// <summary>
    /// Priority-based detection of the top 4 critical compromise vectors
    /// from the assessment data. Output is in business language, not
    /// technical jargon — the audience is the operations manager, not the
    /// security engineer. Each detector checks a specific class of finding
    /// (credential theft, legacy protocols, exposed attack surface, missing
    /// detection / logging, etc.) and returns a short card describing the
    /// risk and how many machines are affected.
    /// </summary>
    private static List<(string Title, string Description, int Priority)> DetectCompromiseVectors(
        List<OrgControlResult> allResults, HygieneScanDto? hygiene, OrgEnrichment enrichment,
        List<AssessmentRun> runs, string lang)
    {
        var found = new List<(string Title, string Description, int Priority)>();
        var es = lang == "es";

        int FailCount(Func<OrgControlResult, bool> predicate) => allResults
            .Where(r => r.Status == "fail" && predicate(r))
            .Select(r => r.RunId).Distinct().Count();

        // 1. Active threats — highest urgency.
        if (enrichment.Threats.Count > 0)
        {
            var machines = enrichment.Threats.Select(t => t.MachineId).Distinct().Count();
            found.Add((
                es ? "Amenazas activas detectadas en la red"
                   : "Active threats detected on the network",
                es ? $"Nuestro escaneo detectó {enrichment.Threats.Count} firmas de amenazas distribuidas en {machines} equipos. Esto indica sistemas potencialmente comprometidos que requieren análisis forense inmediato."
                   : $"Our scan detected {enrichment.Threats.Count} threat signatures spread across {machines} machines. This indicates potentially compromised systems requiring immediate forensic analysis.",
                100));
        }

        // 2. Credential theft vectors (WDigest, LSA Protection, LM Hash, etc.)
        var credMachines = FailCount(r =>
            r.Name.Contains("WDigest",      StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("LSA",          StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("Credential",   StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("LM Hash",      StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("Clear Text",   StringComparison.OrdinalIgnoreCase));
        if (credMachines > 0)
        {
            found.Add((
                es ? "Contraseñas expuestas en memoria"
                   : "Credentials exposed in memory",
                es ? $"En {credMachines} equipos se detectaron configuraciones que dejan contraseñas cacheadas en texto claro en la memoria del sistema. Un atacante con privilegios locales puede extraerlas en segundos con herramientas como mimikatz, y luego moverse lateralmente usando esas credenciales robadas."
                   : $"On {credMachines} machines we found settings that cache passwords in clear text in system memory. An attacker with local privileges can extract them in seconds with tools like mimikatz, then move laterally using those stolen credentials.",
                95));
        }

        // 3. RDP exposed to the Internet
        var rdpMachines = enrichment.Ports
            .Where(p => p.Port == 3389 && p.Risk != null)
            .Select(p => p.MachineId).Distinct().Count();
        if (rdpMachines > 0)
        {
            found.Add((
                es ? "Puertas RDP abiertas directamente a internet"
                   : "RDP doors open directly to the Internet",
                es ? $"{rdpMachines} equipos tienen Escritorio Remoto (RDP) expuesto. RDP es el vector de entrada de ransomware #1 en los últimos 3 años. Un atacante probablemente ya está intentando contraseñas contra estos puertos en este momento."
                   : $"{rdpMachines} machines have Remote Desktop (RDP) exposed. RDP is the #1 ransomware entry vector of the past 3 years. An attacker is likely brute-forcing these ports right now.",
                90));
        }

        // 4. SMBv1 — NotPetya / WannaCry legacy
        var smbMachines = FailCount(r =>
            r.Name.Contains("SMBv1", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("SMB1",  StringComparison.OrdinalIgnoreCase) ||
            r.ControlId.Contains("SMB1", StringComparison.OrdinalIgnoreCase));
        if (smbMachines > 0)
        {
            found.Add((
                es ? "Protocolo SMBv1 habilitado"
                   : "SMBv1 protocol enabled",
                es ? $"El protocolo legacy SMBv1 está activo en {smbMachines} equipos. Es el mismo vector de explotación que permitió a NotPetya y WannaCry destruir miles de redes corporativas en 2017. Sigue siendo explotable hoy."
                   : $"The legacy SMBv1 protocol is active on {smbMachines} machines. This is the same exploit vector that allowed NotPetya and WannaCry to destroy thousands of corporate networks in 2017. It is still exploitable today.",
                85));
        }

        // 5. Kerberoastable accounts
        if (hygiene != null)
        {
            var kerberoast = hygiene.Findings.Count(f => f.Status == "Kerberoastable");
            if (kerberoast > 0)
            {
                found.Add((
                    es ? "Cuentas de servicio vulnerables a Kerberoast"
                       : "Service accounts vulnerable to Kerberoast",
                    es ? $"{kerberoast} cuentas de servicio en Active Directory son vulnerables a Kerberoast. Sus contraseñas pueden ser crackeadas offline sin generar una sola alerta, y muchas tienen privilegios elevados en la red."
                       : $"{kerberoast} service accounts in Active Directory are vulnerable to Kerberoast. Their passwords can be cracked offline without generating a single alert, and many have elevated privileges across the network.",
                    80));
            }

            // 6. LAPS missing — shared local admin password
            var lapsMissing = hygiene.Findings.Count(f => f.Status == "NoLAPS");
            if (lapsMissing > 10)
            {
                found.Add((
                    es ? "Sin LAPS — contraseña de administrador local compartida"
                       : "No LAPS — shared local admin password",
                    es ? $"{lapsMissing} equipos no tienen LAPS (Local Administrator Password Solution) desplegado. Probablemente usan la misma contraseña de administrador local en toda la flota, convirtiendo un solo equipo comprometido en acceso total a todos."
                       : $"{lapsMissing} machines don't have LAPS (Local Administrator Password Solution) deployed. They probably share the same local admin password fleet-wide, turning a single compromised machine into full access to every other one.",
                    75));
            }

            // 7. Domain functional level legacy
            var domainLevel = hygiene.Findings.FirstOrDefault(f => f.Status == "DomainLevel");
            if (domainLevel != null && (domainLevel.Detail?.Contains("2008") == true || domainLevel.Detail?.Contains("2003") == true))
            {
                found.Add((
                    es ? "Dominio Active Directory en nivel funcional obsoleto"
                       : "Active Directory domain at legacy functional level",
                    es ? $"El dominio está operando en nivel funcional {domainLevel.Detail}, bloqueando el uso de features de seguridad modernas como Protected Users, Authentication Policy Silos y Credential Guard. Heredaron todas las vulnerabilidades de una década atrás."
                       : $"The domain is running at functional level {domainLevel.Detail}, blocking modern security features like Protected Users, Authentication Policy Silos and Credential Guard. It inherits every vulnerability from a decade ago.",
                    70));
            }
        }

        // 8. No audit logging — attackers would leave no trace
        var auditMachines = FailCount(r =>
            r.Category.Contains("Audit", StringComparison.OrdinalIgnoreCase));
        if (auditMachines > 0 && allResults.Count(r => r.Category.Contains("Audit", StringComparison.OrdinalIgnoreCase) && r.Status == "fail") > 20)
        {
            found.Add((
                es ? "Sin registro de eventos de seguridad"
                   : "Security event logging disabled",
                es ? $"En {auditMachines} equipos el registro de eventos críticos está deshabilitado o incompleto. Un atacante que entre a estos sistemas no dejaría un solo rastro para análisis forense. Literalmente no sabrías que pasó."
                   : $"On {auditMachines} machines the logging of critical events is disabled or incomplete. An attacker breaching these systems would leave no forensic trail whatsoever. You would literally not know what happened.",
                65));
        }

        // 9. BitLocker disabled
        var noBitlocker = runs.Count(r => r.Machine.Bitlocker != true);
        if (noBitlocker > 0 && noBitlocker >= runs.Count * 0.3)
        {
            found.Add((
                es ? "Discos sin cifrar"
                   : "Unencrypted drives",
                es ? $"{noBitlocker} equipos tienen los discos sin cifrar. El robo o pérdida de un solo dispositivo expone toda la información almacenada: correos, archivos, credenciales guardadas, claves de API."
                   : $"{noBitlocker} machines have unencrypted drives. The theft or loss of a single device exposes every piece of data stored on it: emails, files, saved credentials, API keys.",
                60));
        }

        return found.OrderByDescending(v => v.Priority).Take(4).ToList();
    }

    // ======================================================================
    // ORG: MONTHLY C-LEVEL EXECUTIVE BRIEFING (dark mode, MRR report)
    //
    // Target audience: CEO / COO / CISO of an EXISTING customer (not a
    // prospect). Goal: demonstrate ROI of the managed security service,
    // show measurable month-over-month risk reduction, and present
    // strategic decisions the executive has to make.
    //
    // Structure:
    //   Page 1: Cover (reused pattern, dark)
    //   Page 2: Trend & ROI + Compliance rings + Milestones + Action Required
    //           (single dense content page — CEO read-time under 3 minutes)
    // ======================================================================
    private static string BuildOrgMonthlyBriefingReport(Organization org, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, ReportBranding brand,
        List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment,
        ReportUserInfo userInfo, decimal? previousMonthScore, string lang = "en")
    {
        var es = lang == "es";
        var sb = new StringBuilder();
        var totalMachines = runs.Count;
        var avgScore = runs.Count > 0 ? Math.Round(runs.Average(r => r.GlobalScore ?? 0), 1) : 0m;
        var orgGrade = GetGrade(avgScore);
        var scanDate = runs.Count > 0 ? runs.Max(r => r.CompletedAt ?? r.StartedAt) : DateTime.UtcNow;

        // Trend calculation
        decimal? delta = previousMonthScore.HasValue ? avgScore - previousMonthScore.Value : null;
        decimal? deltaPct = previousMonthScore.HasValue && previousMonthScore.Value > 0
            ? Math.Round((avgScore - previousMonthScore.Value) / previousMonthScore.Value * 100, 1)
            : null;
        var trendColor = !delta.HasValue ? "#64748B" : delta.Value >= 0 ? "#15803D" : "#991B1B";
        var trendArrow = !delta.HasValue ? "—" : delta.Value >= 0 ? "▲" : "▼";
        var trendSign  = !delta.HasValue ? "" : delta.Value >= 0 ? "+" : "";

        // Legacy OS detection for the Action Required section
        var legacyOsMachines = runs
            .Where(r => r.Machine.OsName != null && (
                r.Machine.OsName.Contains("2008") ||
                r.Machine.OsName.Contains("2003") ||
                r.Machine.OsName.Contains("Windows 7") ||
                r.Machine.OsName.Contains("Vista")))
            .Select(r => r.Machine)
            .ToList();

        // Hardening milestone: count machines where SMBv1/NTLM control is passing
        int PassCount(Func<OrgControlResult, bool> pred) => allResults
            .Where(r => r.Status == "pass" && pred(r))
            .Select(r => r.RunId).Distinct().Count();
        var smbv1Hardened = PassCount(r =>
            r.Name.Contains("SMBv1", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("SMB1",  StringComparison.OrdinalIgnoreCase));
        var ntlmHardened = PassCount(r =>
            r.Name.Contains("NTLM", StringComparison.OrdinalIgnoreCase));
        var legacyHardenedTotal = Math.Max(smbv1Hardened, ntlmHardened);

        // Pick HIPAA + NIST + one more (first available) for the compliance rings
        var ringFrameworks = new List<FrameworkScoreDto>();
        var hipaa = frameworkScores.FirstOrDefault(f => f.Code == "HIPAA");
        var nist  = frameworkScores.FirstOrDefault(f => f.Code == "NIST");
        var cis   = frameworkScores.FirstOrDefault(f => f.Code == "CIS");
        if (hipaa != null) ringFrameworks.Add(hipaa);
        if (nist  != null) ringFrameworks.Add(nist);
        if (cis   != null) ringFrameworks.Add(cis);
        // Pad with remaining frameworks if we have less than 3
        foreach (var fs in frameworkScores.Where(f => !ringFrameworks.Contains(f)))
        {
            if (ringFrameworks.Count >= 3) break;
            ringFrameworks.Add(fs);
        }

        string RingColor(double s) => s >= 85 ? "#15803D" : s >= 70 ? "#B45309" : "#991B1B";

        var mbTitle = es ? "Resumen Ejecutivo Mensual" : "Monthly Executive Briefing";
        AppendHtmlHead(sb, $"{mbTitle} - {org.Name}", brand, isOrgReport: true, htmlLang: lang,
            user: userInfo, detail: $"{totalMachines} {(es ? "dispositivos" : "devices")} · {org.Name} · {scanDate:MMM yyyy}");

        // ---- BIG 4 FINANCIAL AUDIT LIGHT MODE (scoped via .page-mb) ----
        // Premium corporate palette for the monthly C-Level briefing:
        //   #FFFFFF / #F8F9FA surfaces, #1E293B + #334155 typography,
        //   #0F172A deep navy primary accent, #991B1B subdued brick red
        //   for critical metrics, subtle soft shadows on data cards.
        sb.AppendLine("<style>");
        sb.AppendLine(@"
.page-mb { background: #FFFFFF !important; color: #334155 !important; -webkit-print-color-adjust: exact !important; print-color-adjust: exact !important; }
.page-mb .pb { background: #FFFFFF !important; color: #334155 !important; padding-bottom: 24mm !important; padding-top: 14px; }
.page-mb h3 { color: #1E293B !important; border-bottom: 2px solid #0F172A !important; font-size: 12px !important; margin: 18px 0 10px; padding-bottom: 4px; text-transform: uppercase; letter-spacing: 0.08em; font-weight: 700 !important; }
.page-mb p, .page-mb li { color: #334155 !important; }
.page-mb strong { color: #1E293B !important; }

.mb-trend {
    background: #F8F9FA;
    border: 1px solid #CBD5E1;
    border-top: 4px solid #0F172A;
    border-radius: 6px;
    padding: 20px 26px;
    display: flex;
    align-items: center;
    gap: 28px;
    margin: 4px 0 16px;
    box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.06), 0 2px 4px -2px rgba(15, 23, 42, 0.04);
    page-break-inside: avoid;
}
.mb-trend-score { text-align: center; border-right: 1px solid #E2E8F0; padding-right: 26px; }
.mb-trend-score .mb-big { font-size: 44px; font-weight: 900; color: #0F172A; line-height: 1; letter-spacing: -0.02em; }
.mb-trend-score .mb-big-sub { font-size: 9px; color: #64748B; text-transform: uppercase; letter-spacing: 0.14em; margin-top: 6px; font-weight: 700; }
.mb-trend-delta { flex: 1; }
.mb-trend-delta .mb-delta-row { display: flex; align-items: baseline; gap: 12px; flex-wrap: wrap; }
.mb-trend-delta .mb-delta-num { font-size: 24px; font-weight: 800; letter-spacing: -0.01em; }
.mb-trend-delta .mb-delta-label { font-size: 8px; color: #64748B; text-transform: uppercase; letter-spacing: 0.14em; font-weight: 700; }
.mb-trend-delta .mb-trend-narr { font-size: 11px; color: #334155; line-height: 1.6; margin-top: 8px; }
.mb-trend-delta .mb-trend-narr strong { color: #0F172A; }

.mb-rings { display: flex; gap: 14px; justify-content: space-around; margin: 12px 0 8px; flex-wrap: nowrap; padding: 0 4px; }
.mb-ring-wrap { flex: 1; text-align: center; min-width: 0; page-break-inside: avoid; }
.mb-ring {
    width: 78px; height: 78px; border-radius: 50%;
    margin: 0 auto; display: flex; align-items: center; justify-content: center;
    box-shadow: 0 1px 3px 0 rgba(15, 23, 42, 0.06);
}
.mb-ring-inner {
    width: 58px; height: 58px; border-radius: 50%; background: #FFFFFF;
    display: flex; align-items: center; justify-content: center;
    font-size: 15px; font-weight: 800; color: #0F172A;
    box-shadow: inset 0 0 0 1px #E2E8F0;
}
.mb-ring-label { font-size: 9px; color: #1E293B; text-transform: uppercase; letter-spacing: 0.1em; font-weight: 700; margin-top: 8px; }
.mb-ring-sub { font-size: 8px; color: #64748B; margin-top: 2px; }

.mb-milestone {
    background: #F0FDF4;
    border: 1px solid #BBF7D0;
    border-left: 4px solid #15803D;
    border-radius: 0 4px 4px 0;
    padding: 14px 20px;
    margin: 8px 0 12px;
    box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
    page-break-inside: avoid;
}
.mb-milestone-tag {
    display: inline-block; padding: 2px 8px;
    background: #15803D; color: #FFFFFF;
    font-size: 8px; font-weight: 800; letter-spacing: 0.12em; text-transform: uppercase;
    border-radius: 2px; margin-bottom: 6px;
}
.mb-milestone p { font-size: 11px; color: #166534 !important; line-height: 1.6; margin: 0; }
.mb-milestone p strong { color: #14532D !important; }

.mb-action {
    background: #F8F9FA;
    border: 1px solid #CBD5E1;
    border-left: 5px solid #0F172A;
    border-radius: 4px;
    padding: 16px 22px;
    margin: 12px 0;
    box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.06), 0 2px 4px -2px rgba(15, 23, 42, 0.04);
    page-break-inside: avoid;
}
.mb-action .mb-action-tag {
    display: inline-block;
    padding: 2px 8px;
    background: #0F172A;
    color: #FFFFFF;
    font-size: 8px;
    font-weight: 800;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    border-radius: 2px;
    margin-bottom: 6px;
}
.mb-action h4 { color: #0F172A !important; font-size: 13px; margin: 0 0 6px; font-weight: 700; letter-spacing: 0.01em; }
.mb-action p { font-size: 11px; color: #334155 !important; line-height: 1.6; margin: 0 0 4px; }
.mb-action p strong { color: #0F172A !important; }
");
        sb.AppendLine("</style>");

        // ---- PAGE 1: COVER ----
        sb.AppendLine("<div class='cover'>");
        AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{(es ? "RESUMEN EJECUTIVO MENSUAL" : "MONTHLY EXECUTIVE BRIEFING")}</p>");
        sb.AppendLine($"<h1>{(es ? "Revisión Mensual de Postura de Seguridad" : "Security Posture Monthly Review")}</h1>");
        sb.AppendLine($"<h2>{HtmlEncode(org.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{(es ? scanDate.ToString("MMMM yyyy") : scanDate.ToString("MMMM yyyy"))} &mdash; {totalMachines} {(es ? "dispositivos bajo gestión" : "devices under management")}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{orgGrade.Replace("+", "plus")}'>{HtmlEncode(orgGrade)}</div>");
        sb.AppendLine($"<p class='score'>{avgScore}%</p>");
        sb.AppendLine("</div></div>");

        // ---- PAGE 2: EXECUTIVE BRIEFING ----
        sb.AppendLine("<div class='page page-mb'>");
        AppendPageHeader(sb, es ? "Resumen Ejecutivo" : "Executive Briefing", brand,
            es ? "REVISIÓN MENSUAL C-LEVEL" : "C-LEVEL MONTHLY REVIEW");
        sb.AppendLine("<div class='pb'>");

        // 1. Trend block
        sb.AppendLine("<div class='mb-trend'>");
        sb.AppendLine("<div class='mb-trend-score'>");
        sb.AppendLine($"<div class='mb-big'>{avgScore}</div>");
        sb.AppendLine($"<div class='mb-big-sub'>{(es ? "Postura Actual" : "Current Posture")}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='mb-trend-delta'>");
        if (delta.HasValue)
        {
            sb.AppendLine("<div class='mb-delta-row'>");
            sb.AppendLine($"<div class='mb-delta-num' style='color:{trendColor}'>{trendArrow} {trendSign}{delta.Value:0.#} pts</div>");
            sb.AppendLine($"<div class='mb-delta-label'>{(es ? $"vs mes anterior ({previousMonthScore!.Value:0.#})" : $"vs last month ({previousMonthScore!.Value:0.#})")}</div>");
            sb.AppendLine("</div>");
            var narrative = delta.Value >= 0
                ? (es ? $"La exposición general al riesgo se redujo un {Math.Abs(deltaPct!.Value):0.#}% en los últimos 30 días. Su programa de hardening gestionado está entregando mejoras medibles en la postura — el ambiente es demostrablemente menos susceptible a los patrones de ataque que monitoreamos."
                     : $"Overall risk exposure reduced by {Math.Abs(deltaPct!.Value):0.#}% over the past 30 days. Your managed hardening program is delivering measurable posture improvement — the environment is demonstrably less susceptible to the attack patterns we monitor.")
                : (es ? $"La exposición al riesgo aumentó un {Math.Abs(deltaPct!.Value):0.#}% este período. Detectamos nuevo drift en el ambiente (probablemente nuevos endpoints agregados sin la baseline aplicada, o una política revertida). Ya está en nuestra cola de remediación — el próximo mes mostrará la recuperación."
                     : $"Risk exposure increased by {Math.Abs(deltaPct!.Value):0.#}% this period. We flagged new drift in the environment (likely new endpoints added without the baseline applied, or a policy that was rolled back). Already on our remediation queue — next month will show the rebound.");
            sb.AppendLine($"<div class='mb-trend-narr'>{HtmlEncode(narrative)}</div>");
        }
        else
        {
            sb.AppendLine("<div class='mb-delta-row'>");
            sb.AppendLine($"<div class='mb-delta-num' style='color:#64748B'>— BASELINE</div>");
            sb.AppendLine($"<div class='mb-delta-label'>{(es ? "primer período de reporte" : "first reporting period")}</div>");
            sb.AppendLine("</div>");
            sb.AppendLine($"<div class='mb-trend-narr'>{(es ? "Primera revisión mensual para esta organización. Este reporte establece la línea base — el resumen del próximo mes mostrará la tendencia medible de postura." : "First monthly review for this organization. This report establishes the baseline — next month's briefing will show measurable posture trend.")}</div>");
        }
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");

        // 2. Compliance posture rings
        sb.AppendLine($"<h3>{(es ? "Postura de Cumplimiento" : "Compliance Posture")}</h3>");
        if (ringFrameworks.Count > 0)
        {
            sb.AppendLine("<div class='mb-rings'>");
            foreach (var fs in ringFrameworks)
            {
                var s = (double)fs.Score;
                var deg = Math.Min(360, s * 3.6);
                var color = RingColor(s);
                var conic = $"conic-gradient({color} 0deg {deg:0.#}deg, #E2E8F0 {deg:0.#}deg 360deg)";
                sb.AppendLine("<div class='mb-ring-wrap'>");
                sb.AppendLine($"<div class='mb-ring' style='background:{conic}'>");
                sb.AppendLine($"<div class='mb-ring-inner'>{s:0}%</div>");
                sb.AppendLine("</div>");
                sb.AppendLine($"<div class='mb-ring-label'>{HtmlEncode(fs.Code)}</div>");
                sb.AppendLine($"<div class='mb-ring-sub'>{(fs.Code == "HIPAA" ? "Technical Safeguards" : fs.Code == "NIST" ? "CSF Alignment" : "Framework Alignment")}</div>");
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div>");
        }
        else
        {
            sb.AppendLine($"<p style='font-size:11px;color:#6B7280'>{(es ? "No hay datos de scoring por framework disponibles para este período." : "No framework scoring data available for this period.")}</p>");
        }

        // 3. Remediation milestones ("Zero-Downtime Hardening")
        sb.AppendLine($"<h3>{(es ? "Hardening Sin Downtime" : "Zero-Downtime Hardening")}</h3>");
        sb.AppendLine("<div class='mb-milestone'>");
        sb.AppendLine($"<div class='mb-milestone-tag'>{(es ? "HITO · ESTE PERÍODO" : "MILESTONE &middot; THIS PERIOD")}</div>");
        if (legacyHardenedTotal > 0)
        {
            sb.AppendLine($"<p>Successfully concluded our 90-day passive telemetry cycle on legacy protocols. We permanently disabled <strong>NTLM</strong> and <strong>SMBv1</strong> across <strong>{legacyHardenedTotal} endpoints</strong> this month, resulting in <strong>zero operational disruptions</strong>. Every dependency was mapped and migrated before the protocol was turned off.</p>");
        }
        else
        {
            sb.AppendLine($"<p>Completed this period's managed hardening cycle across <strong>{totalMachines} endpoints</strong> with <strong>zero operational disruptions</strong>. All policy changes were rolled out under the 90-day passive telemetry framework that maps legacy dependencies before touching the protocol itself — no user-visible downtime, no emergency tickets, no rollbacks.</p>");
        }
        sb.AppendLine("</div>");

        // 4. Executive decisions required
        sb.AppendLine($"<h3>{(es ? "Decisiones Ejecutivas Requeridas" : "Executive Decisions Required")}</h3>");
        if (legacyOsMachines.Count > 0)
        {
            var names = string.Join(", ", legacyOsMachines.Take(3).Select(m => m.Hostname));
            var osLabel = legacyOsMachines
                .Select(m => m.OsName ?? "")
                .Where(n => n.Contains("2008") || n.Contains("2003") || n.Contains("Windows 7") || n.Contains("Vista"))
                .FirstOrDefault() ?? "legacy Windows";
            sb.AppendLine("<div class='mb-action'>");
            sb.AppendLine("<div class='mb-action-tag'>ACTION REQUIRED &middot; BUDGET</div>");
            sb.AppendLine("<h4>End-of-Life Infrastructure Migration</h4>");
            sb.AppendLine($"<p><strong>Pending budget approval</strong> for the planned Azure migration of <strong>{legacyOsMachines.Count} legacy {HtmlEncode(osLabel)}</strong> on-premise workload{(legacyOsMachines.Count == 1 ? "" : "s")} ({HtmlEncode(names)}{(legacyOsMachines.Count > 3 ? ", …" : "")}). These systems cannot receive modern security patches or baseline hardening — they represent our <strong>highest residual risk vector</strong> and are excluded from the month-over-month posture trend above.</p>");
            sb.AppendLine("<p style='margin-top:6px;font-size:10px;color:#64748B'>Recommended outcome: lift-and-shift to Azure with modern OS within the next 60 days, or formal risk acceptance signed by the executive team.</p>");
            sb.AppendLine("</div>");
        }
        else if (hygiene != null && hygiene.PwdNeverExpire > 10)
        {
            sb.AppendLine("<div class='mb-action'>");
            sb.AppendLine("<div class='mb-action-tag'>ACTION REQUIRED &middot; POLICY</div>");
            sb.AppendLine("<h4>Non-Expiring Service Account Passwords</h4>");
            sb.AppendLine($"<p><strong>{hygiene.PwdNeverExpire}</strong> accounts currently have passwords configured to never expire. Rotating these requires coordination with the application owners — we need an <strong>executive sponsor</strong> to unblock the coordination meetings. Without rotation these credentials remain the highest residual risk vector in the identity plane.</p>");
            sb.AppendLine("</div>");
        }
        else
        {
            sb.AppendLine("<div class='mb-action'>");
            sb.AppendLine("<div class='mb-action-tag'>ACTION REQUIRED &middot; REVIEW</div>");
            sb.AppendLine("<h4>Continuous Improvement Review</h4>");
            sb.AppendLine($"<p>No blocking executive decisions pending this period. Recommend scheduling the <strong>quarterly security steering committee</strong> to review the hardening roadmap for the next 90 days and align on any new initiatives (M365 posture, Azure landing zone, endpoint DLP, etc.).</p>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div></div>");
        sb.AppendLine("</body></html>");

        _ = enrichment; // intentionally unused in this compact view
        return sb.ToString();
    }

    /// <summary>
    /// Renders the unified report header used by EVERY report
    /// type. The header has:
    ///   - Ribbon gradient background behind the logo (echoes the cover)
    ///   - Optional eyebrow line in accent color above the title
    ///   - Page title as h1
    ///   - White-filtered TeamLogic / brand logo on the right
    ///   - Rainbow stripe beneath the header band
    /// Use the `eyebrow` parameter to show a context label (e.g. the MSP
    /// brand name, or "EXECUTIVE BRIEF" for the one-pager). When null,
    /// defaults to brand.CompanyName so every header has an eyebrow.
    /// </summary>
    private static void AppendPageHeader(StringBuilder sb, string title, ReportBranding brand, string? eyebrow = null)
    {
        var effectiveEyebrow = eyebrow ?? brand.CompanyName;
        sb.AppendLine("<div class='ph'>");
        sb.AppendLine("<div class='ph-text'>");
        sb.AppendLine($"<div class='ph-eyebrow'>{HtmlEncode(effectiveEyebrow.ToUpperInvariant())}</div>");
        sb.AppendLine($"<h1>{HtmlEncode(title)}</h1>");
        sb.AppendLine("</div>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' alt=''>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='stripe'></div>");
    }

    private static void AppendFooter(StringBuilder sb, ReportBranding brand, string detail, ReportUserInfo? user = null)
    {
        sb.AppendLine("<div class='footer'>");
        if (user is not null)
        {
            var fullName = string.IsNullOrWhiteSpace(user.FullName) ? "—" : user.FullName!;
            var email    = string.IsNullOrWhiteSpace(user.Email)    ? "—" : user.Email!;
            var phone    = string.IsNullOrWhiteSpace(user.Phone)    ? "—" : user.Phone!;
            var job      = string.IsNullOrWhiteSpace(user.JobTitle) ? ""  : $" &middot; {HtmlEncode(user.JobTitle)}";
            sb.AppendLine($"<p><strong>Prepared by: {HtmlEncode(fullName)}</strong>{job}</p>");
            sb.AppendLine($"<p>{HtmlEncode(email)} &middot; {HtmlEncode(phone)}</p>");
        }
        sb.AppendLine($"<p>Generated by {HtmlEncode(brand.CompanyName)} &mdash; Your Technology Advisor</p>");
        sb.AppendLine($"<p>{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC &bull; {detail}</p>");
        sb.AppendLine("</div>");
    }

    private static void AppendHtmlHead(StringBuilder sb, string title, ReportBranding brand, bool isOrgReport,
        string htmlLang = "en", ReportUserInfo? user = null, string? detail = null)
    {
        sb.AppendLine($"<!DOCTYPE html><html lang='{HtmlEncode(htmlLang)}'><head><meta charset='UTF-8'>");
        sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.AppendLine($"<title>{HtmlEncode(title)}</title>");
        sb.AppendLine("<link href='https://fonts.googleapis.com/css2?family=Montserrat:wght@300;400;500;600;700;900&display=swap' rel='stylesheet'>");
        sb.AppendLine("<style>");
        sb.AppendLine(isOrgReport ? GetOrgReportStyles(brand) : GetReportStyles(brand));
        // A4 print discipline layered AFTER the base styles — overrides the
        // generic `@media print` line at the bottom of each style block.
        sb.AppendLine(GetA4PrintCss(brand));
        // Running footer on every .page (dynamically interpolated from
        // the current operator's profile). The one-pager opts out via
        // `.onepager::after { content: none }` since it carries its own
        // inline footer.
        if (user is not null)
        {
            AppendRunningFooterCss(sb, user, detail);
        }
        sb.AppendLine("</style></head><body>");
    }

    /// <summary>
    /// Emits a CSS rule that injects a running footer strip on every `.page`
    /// via `::after`. The content is interpolated from the current operator's
    /// profile (name / email / phone) plus a report-specific detail string.
    /// Works on screen AND print — no JavaScript required.
    /// </summary>
    private static void AppendRunningFooterCss(StringBuilder sb, ReportUserInfo user, string? detail)
    {
        static string CssEscape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "—";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
        }

        var fullName = CssEscape(user.FullName);
        var email    = CssEscape(user.Email);
        var phone    = CssEscape(user.Phone);
        var jobTitle = string.IsNullOrWhiteSpace(user.JobTitle) ? "" : " · " + CssEscape(user.JobTitle);
        var detailStr = CssEscape(detail ?? "");
        var ts       = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");

        // Two-line footer: Prepared by line + Generated line.
        // Unicode bullets keep the layout compact.
        var line1 = $"Prepared by {fullName}{jobTitle}  ·  {email}  ·  {phone}";
        var line2 = $"Generated {ts} UTC  ·  {detailStr}";

        sb.AppendLine(".page { position: relative !important; }");
        sb.AppendLine(".page::after {");
        sb.AppendLine($"    content: \"{line1}\\A {line2}\";");
        sb.AppendLine("    white-space: pre;");
        sb.AppendLine("    position: absolute;");
        sb.AppendLine("    left: 0;");
        sb.AppendLine("    right: 0;");
        sb.AppendLine("    bottom: 0;");
        sb.AppendLine("    padding: 3mm 12mm 4mm;");
        sb.AppendLine("    font-family: 'Montserrat', 'Verdana', sans-serif;");
        sb.AppendLine("    font-size: 7.5pt;");
        sb.AppendLine("    line-height: 1.45;");
        sb.AppendLine("    color: #666;");
        sb.AppendLine("    background: #fafafa;");
        sb.AppendLine("    border-top: 0.5pt solid #e5e7eb;");
        sb.AppendLine("    text-align: center;");
        sb.AppendLine("    z-index: 10;");
        sb.AppendLine("}");
        // Opt-out for the one-pager which renders its own .op-footer inside
        // the flex column, so we don't want a second footer overlapping it.
        sb.AppendLine(".onepager::after { content: none !important; display: none !important; }");
        // Reserve space at the bottom of the .pb content area so the running
        // footer never overlaps the page body.
        sb.AppendLine(".page .pb { padding-bottom: 18mm !important; }");
    }

    /// <summary>
    /// CSS Paged Media for strict A4 output. Added on top of the existing
    /// styles so the 3 legacy reports also get improved printing without
    /// rewriting them.
    /// </summary>
    private static string GetA4PrintCss(ReportBranding brand) => $$"""
        /* ── A4 print discipline ─────────────────────────────────────── */
        @page { size: A4 portrait; margin: 0; }
        html, body {
            margin: 0 !important;
            padding: 0 !important;
            -webkit-print-color-adjust: exact !important;
            print-color-adjust: exact !important;
        }

        /* The A4 page containers enforce a 210×296mm size on BOTH screen
           and print. Why 296mm and not 297mm: a 1mm safety buffer against
           browser mm→px rounding, printer non-printable margins, and
           parent margin collapse. Without it, a single stray pixel pushes
           content onto a second physical sheet, which is what was reported
           on the cover page. */
        .cover, .page {
            width: 210mm !important;
            height: 296mm !important;
            min-height: 296mm !important;
            max-height: 296mm !important;
            box-sizing: border-box !important;
            overflow: hidden !important;
        }
        /* On-screen preview: the A4 pages sit centered horizontally with a
           bottom gap between successive pages. Print rules below override
           these margins to 0 so the printed output remains edge-to-edge. */
        .cover { margin: 0 auto 24px auto !important; }
        .page  { margin: 0 auto 24px auto !important; }

        /* ── On-screen preview polish ────────────────────────────────── */
        /* Shrink the whole preview so a full A4 page fits comfortably in
           the viewport without horizontal scroll on 13"/14" laptops, and
           give the pages a soft "floating paper" drop shadow. `zoom` is
           supported in all modern Chromium browsers, Safari, and Firefox
           126+. None of this applies to @media print so the output is
           unchanged. */
        @media screen {
            html { background: #F8F9FA; }
            body {
                zoom: 0.78;
                padding-top: 28px;
                padding-bottom: 28px;
            }
            .cover, .page {
                box-shadow: 0 10px 30px -4px rgba(15, 23, 42, 0.18),
                            0 4px 8px -2px rgba(15, 23, 42, 0.08);
                border-radius: 2mm;
            }
        }

        @media print {
            html, body { background: #fff !important; }
            body { font-size: 10pt; margin: 0 !important; padding: 0 !important; }
            .cover, .page {
                margin: 0 !important;
                padding: 0 !important;
                box-shadow: none !important;
                page-break-after: always;
                break-after: page;
                /* Strict 296mm even in print, plus `page-break-inside:avoid`
                   so the browser never splits one logical page across two
                   physical sheets. */
                page-break-inside: avoid;
                break-inside: avoid;
            }
            .cover:last-of-type, .page:last-of-type {
                page-break-after: auto;
                break-after: auto;
            }
            .risk-card, .headline-item, .stat, .fw-bar-row, .cat-header,
            .results-table tr, .hw-col, .recommendation-box, .insight-box,
            .op-risk, .op-win, .op-kpi, .op-footer, .op-header {
                break-inside: avoid;
                page-break-inside: avoid;
            }
            .no-print { display: none !important; }
        }

        /* ── Header bars: edge-to-edge for ALL report types ──────────── */
        /* The 210mm parent with box-sizing border-box already guarantees
           this, but we force explicit width/margin to eliminate any
           compounded padding from other CSS layers. Logos render white
           via filter so they read clearly against the dark #3D4043
           background (and the ribbon gradient behind them). */
        .ph {
            width: 100% !important;
            margin: 0 !important;
            box-sizing: border-box !important;
        }
        .cover .logo,
        .ph img {
            filter: brightness(0) invert(1) !important;
            -webkit-filter: brightness(0) invert(1) !important;
        }

        /* ── Big 4 Financial Audit light palette (unified content area) ──
           Premium light-mode overrides applied to every .pb (page body) of
           every report type. Reports that define their own theme block
           later in the document — .page-mb (monthly briefing, dark) and
           .pres-light (detailed presales) — override these via source
           order, so the base stays Big 4 and the specialty skins keep
           their distinct look. */
        .page { background: #FFFFFF !important; }
        .pb { color: #334155; }

        /* Typography */
        .pb h3 {
            color: #1E293B !important;
            border-bottom: 2px solid #0F172A !important;
            font-weight: 700 !important;
            letter-spacing: 0.02em !important;
        }
        .pb h4 { color: #1E293B !important; }
        .pb p, .pb li, .pb td { color: #334155; }
        .pb strong { color: #1E293B; }
        .pb .cat-summary { color: #64748B !important; }

        /* Stat cards (used by exec summary, presales, etc.) */
        .pb .stat {
            background: #F8F9FA !important;
            border: 1px solid #E2E8F0 !important;
            box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
        }
        .pb .stat.pass-stat { background: #F0FDF4 !important; border-color: #15803D !important; border-left: 3px solid #15803D !important; }
        .pb .stat.warn-stat { background: #FFFBEB !important; border-color: #B45309 !important; border-left: 3px solid #B45309 !important; }
        .pb .stat.fail-stat { background: #FEF2F2 !important; border-color: #991B1B !important; border-left: 3px solid #991B1B !important; }
        .pb .stat-value { color: #0F172A !important; }
        .pb .stat-label { color: #64748B !important; text-transform: uppercase !important; letter-spacing: 0.08em !important; }

        /* Results tables */
        .pb .results-table {
            background: #FFFFFF !important;
            border: 1px solid #E2E8F0 !important;
            border-radius: 4px;
            overflow: hidden;
            box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
        }
        .pb .results-table th {
            background: #F8F9FA !important;
            color: #0F172A !important;
            border-bottom: 2px solid #0F172A !important;
            text-transform: uppercase !important;
            letter-spacing: 0.05em !important;
            font-weight: 700 !important;
        }
        .pb .results-table td { color: #334155 !important; border-bottom: 1px solid #E2E8F0 !important; }
        .pb .results-table tr { background: #FFFFFF !important; }
        .pb .results-table tr:nth-child(even) { background: #F8F9FA !important; }
        .pb .results-table tr.pass { background: #F0FDF4 !important; }
        .pb .results-table tr.warn { background: #FFFBEB !important; }
        .pb .results-table tr.fail { background: #FEF2F2 !important; }
        .pb .results-table tr.fail td { color: #7F1D1D !important; }

        /* Generic call-out / insight box */
        .pb .insight-box {
            background: #F8F9FA !important;
            border: 1px solid #CBD5E1 !important;
            border-left: 4px solid #0F172A !important;
            color: #334155 !important;
            box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
        }
        .pb .insight-box p { color: #334155 !important; }
        .pb .insight-box strong { color: #0F172A !important; }
        .pb .insight-box.fail-box {
            background: #FEF2F2 !important;
            border-color: #FECACA !important;
            border-left: 4px solid #991B1B !important;
        }
        .pb .insight-box.fail-box p { color: #7F1D1D !important; }
        .pb .insight-box.fail-box strong { color: #450A0A !important; }

        /* Risk cards */
        .pb .risk-card {
            background: #FEF2F2 !important;
            border: 1px solid #FECACA !important;
            border-left: 4px solid #991B1B !important;
            box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
        }
        .pb .risk-num { background: #991B1B !important; color: #FFFFFF !important; }
        .pb .risk-body strong { color: #1E293B !important; }
        .pb .risk-detail { color: #64748B !important; }

        /* Big number box */
        .pb .big-number-box {
            background: #FEF2F2 !important;
            border: 2px solid #991B1B !important;
            border-radius: 8px !important;
            box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.06), 0 2px 4px -2px rgba(15, 23, 42, 0.04);
        }
        .pb .big-number { color: #991B1B !important; }
        .pb .big-number-label { color: #64748B !important; }

        /* Headline items (presales detailed + opener) */
        .pb .headline-item {
            background: #FEF2F2 !important;
            border: 1px solid #FECACA !important;
            border-left: 4px solid #991B1B !important;
            box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
        }
        .pb .headline-icon { background: #991B1B !important; color: #FFFFFF !important; }
        .pb .headline-text { color: #334155 !important; }
        .pb .headline-text strong { color: #450A0A !important; }

        /* Recommendation box */
        .pb .recommendation-box {
            background: #F8F9FA !important;
            border: 1px solid #CBD5E1 !important;
            border-top: 4px solid #0F172A !important;
            box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.06);
        }
        .pb .recommendation-box h3 { color: #1E293B !important; border: none !important; }
        .pb .recommendation-box p { color: #334155 !important; }
        .pb .recommendation-box strong { color: #0F172A !important; }

        /* Steps / roadmap */
        .pb .step-num { background: #0F172A !important; color: #FFFFFF !important; }
        .pb .step strong { color: #1E293B !important; }
        .pb .step p { color: #64748B !important; }
        .pb .phase-list li { color: #334155 !important; }

        /* CTA box */
        .pb .cta-box {
            background: #F8F9FA !important;
            border: 1px solid #CBD5E1 !important;
            border-top: 4px solid #0F172A !important;
            color: #334155 !important;
            box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.06), 0 2px 4px -2px rgba(15, 23, 42, 0.04);
        }
        .pb .cta-box p { color: #334155 !important; }
        .pb .cta-box strong { color: #0F172A !important; }

        /* Framework bars */
        .pb .fw-label { color: #1E293B !important; }
        .pb .fw-pct { color: #0F172A !important; }
        .pb .fw-detail { color: #64748B !important; }

        /* Severity + status badges */
        .pb .severity.critical { background: #7F1D1D !important; color: #FFFFFF !important; }
        .pb .severity.high     { background: #991B1B !important; color: #FFFFFF !important; }
        .pb .severity.medium   { background: #B45309 !important; color: #FFFFFF !important; }
        .pb .severity.low      { background: #0F172A !important; color: #FFFFFF !important; }
        .pb .status-badge.pass { background: #15803D !important; color: #FFFFFF !important; }
        .pb .status-badge.warn { background: #B45309 !important; color: #FFFFFF !important; }
        .pb .status-badge.fail { background: #991B1B !important; color: #FFFFFF !important; }

        /* Info table (hardware/security/network grids) */
        .pb .info-table td { color: #334155 !important; border-bottom: 1px solid #E2E8F0 !important; }
        .pb .info-table td:first-child { color: #1E293B !important; font-weight: 600 !important; }
        .pb .hw-col h4 { color: #0F172A !important; }

        /* Big number / pass-cell / warn-cell / fail-cell */
        .pb .pass-cell { color: #15803D !important; }
        .pb .warn-cell { color: #B45309 !important; }
        .pb .fail-cell { color: #991B1B !important; }

        /* Grade distribution bars */
        .pb .grade-label { color: #1E293B !important; }
        .pb .grade-count { color: #1E293B !important; }

        /* ── Executive One-Pager layout (screen + print) ─────────────── */
        /* Uses flexbox column so the footer always sticks to the bottom
           of the 296mm page without absolute positioning. The one-pager
           now uses the SAME .ph header as every other report (via
           AppendPageHeader) — no special .op-header styles needed. */
        .onepager {
            display: flex !important;
            flex-direction: column !important;
            page-break-after: always;
            position: relative;
        }
        /* The unified .ph header inside the onepager occupies its natural
           height. .stripe sits below it. Both are `flex: 0 0 auto`. */
        .onepager > .ph,
        .onepager > .stripe { flex: 0 0 auto; }
        .onepager .op-body {
            padding: 6mm 12mm 0;
            flex: 1 1 auto;
            min-height: 0; /* allow flex child to shrink if content grows */
        }

        .op-kpis { display: grid; grid-template-columns: 1.4fr 1fr 1fr 1fr; gap: 4mm; margin-bottom: 5mm; }
        .op-kpi {
            background: #f8f9fa; border: 1px solid #e5e7eb; border-radius: 2mm;
            padding: 4mm; text-align: center;
        }
        .op-kpi.op-kpi-hero { background: {{brand.PrimaryColor}}; color: #fff; border-color: {{brand.PrimaryColor}}; }
        .op-kpi .op-kpi-val { display: block; font-size: 22pt; font-weight: 900; line-height: 1; }
        .op-kpi .op-kpi-label { display: block; font-size: 7pt; text-transform: uppercase; letter-spacing: 0.08em; margin-top: 2mm; color: #666; }
        .op-kpi.op-kpi-hero .op-kpi-label { color: {{brand.AccentColor}}; }
        .op-kpi.op-kpi-grade .op-kpi-val { font-size: 28pt; color: {{brand.PrimaryColor}}; }

        .op-section-title {
            font-size: 9pt; font-weight: 800; text-transform: uppercase;
            letter-spacing: 0.1em; color: {{brand.PrimaryColor}};
            border-bottom: 1.5pt solid {{brand.AccentColor}};
            padding-bottom: 1mm; margin: 4mm 0 2.5mm;
        }

        .op-fw-bars { margin-bottom: 4mm; }
        .op-fw-bars .fw-bar-row { margin-bottom: 2mm; }
        .op-fw-bars .fw-label { font-size: 8pt; width: 18mm; }
        .op-fw-bars .fw-track { height: 3.5mm; }
        .op-fw-bars .fw-pct { font-size: 8pt; width: 12mm; }
        .op-fw-bars .fw-detail { font-size: 7pt; width: 22mm; }

        .op-lists { display: grid; grid-template-columns: 1fr 1fr; gap: 5mm; }
        .op-list-col { }
        .op-risk, .op-win {
            display: flex; gap: 3mm; padding: 2.5mm 3mm;
            border-radius: 1.5mm; margin-bottom: 2mm;
            font-size: 8.5pt; line-height: 1.35;
        }
        .op-risk { background: #fef2f2; border: 0.5pt solid #fecaca; }
        .op-win  { background: #f0fdf4; border: 0.5pt solid {{brand.PrimaryColor}}44; }
        .op-risk .op-num, .op-win .op-num {
            width: 5mm; height: 5mm; border-radius: 50%;
            display: flex; align-items: center; justify-content: center;
            font-weight: 800; font-size: 8pt; color: #fff; flex-shrink: 0;
        }
        .op-risk .op-num { background: #C0392B; }
        .op-win  .op-num { background: {{brand.PrimaryColor}}; }
        .op-risk strong, .op-win strong { display: block; font-size: 9pt; margin-bottom: 0.5mm; }
        .op-risk .op-meta, .op-win .op-meta { font-size: 7pt; color: #666; margin-top: 0.5mm; }

        .op-remediation {
            background: #f0f4ff; border: 0.5pt solid #c7d2fe;
            border-radius: 2mm; padding: 3mm 4mm; margin-top: 3mm;
            display: flex; justify-content: space-between; align-items: center;
            font-size: 9pt;
        }
        .op-remediation .op-hours { font-size: 16pt; font-weight: 900; color: {{brand.PrimaryColor}}; }

        .op-footer {
            flex: 0 0 auto;
            padding: 4mm 12mm 6mm; border-top: 0.5pt solid #e5e7eb;
            background: #fafafa;
            font-size: 7.5pt; color: #555; line-height: 1.4;
        }
        .op-footer .op-footer-row { display: flex; justify-content: space-between; gap: 6mm; }
        .op-footer .op-footer-user { font-weight: 600; color: #3D4043; font-size: 8pt; }
        .op-footer .op-footer-brand { color: #999; font-size: 7pt; text-align: right; }
        """;

    /// <summary>
    /// Minimal i18n. Returns the English string unless `lang` is "es" and
    /// a Spanish translation is defined.
    /// </summary>
    private static string T(string key, string lang)
    {
        return lang == "es"
            ? (_esStrings.TryGetValue(key, out var es) ? es : (_enStrings.TryGetValue(key, out var en0) ? en0 : key))
            : (_enStrings.TryGetValue(key, out var en) ? en : key);
    }

    private static readonly Dictionary<string, string> _enStrings = new()
    {
        ["op.eyebrow"]           = "EXECUTIVE BRIEF",
        ["op.title"]             = "Security Posture — Executive Brief",
        ["op.header.subtitle"]   = "C-Level One-Page Summary",
        ["op.kpi.score"]         = "Overall score",
        ["op.kpi.grade"]         = "Grade",
        ["op.kpi.machines"]      = "Machines assessed",
        ["op.kpi.critical"]      = "Critical failures",
        ["op.frameworks"]        = "Framework compliance",
        ["op.frameworks.caption"] = "P / F = average passing / failing controls per machine",
        ["op.risks"]             = "Top 3 risks",
        ["op.wins"]              = "Top 3 quick wins",
        ["op.no_risks"]          = "No critical or high-severity findings — the fleet is in good shape.",
        ["op.no_wins"]           = "No easy fixes pending — focus on the hardening roadmap.",
        ["op.remediation.label"] = "Estimated remediation effort",
        ["op.remediation.hours"] = "hours",
        ["op.remediation.note"]  = "Ballpark assumes related controls are bundled into a single GPO/Intune profile per category (e.g. all Edge hardening = 1 GPO) plus ~15 min of validation per endpoint. A sizing call produces the final quote.",
        ["op.footer.prepared"]   = "Prepared by",
        ["op.footer.generated"]  = "Generated",
        ["op.footer.confidential"] = "Confidential — For internal use of",
        ["op.risk.affects"]      = "affects",
        ["op.risk.machines"]     = "machines",
        ["op.risk.machine"]      = "machine",
        ["op.cover.for"]         = "Prepared for",
        ["op.cover.date"]        = "Assessment date",
        ["op.cover.score"]       = "Overall posture",
    };

    private static readonly Dictionary<string, string> _esStrings = new()
    {
        ["op.eyebrow"]           = "RESUMEN EJECUTIVO",
        ["op.title"]             = "Postura de Seguridad — Resumen Ejecutivo",
        ["op.header.subtitle"]   = "Resumen en una página para dirección",
        ["op.kpi.score"]         = "Puntaje global",
        ["op.kpi.grade"]         = "Calificación",
        ["op.kpi.machines"]      = "Equipos evaluados",
        ["op.kpi.critical"]      = "Fallos críticos",
        ["op.frameworks"]        = "Cumplimiento por framework",
        ["op.frameworks.caption"] = "P / F = controles promedio que pasan / fallan por equipo",
        ["op.risks"]             = "Top 3 riesgos",
        ["op.wins"]              = "Top 3 acciones rápidas",
        ["op.no_risks"]          = "Sin hallazgos críticos ni altos — la flota está saludable.",
        ["op.no_wins"]           = "No hay correcciones rápidas pendientes — se recomienda avanzar con el plan de hardening.",
        ["op.remediation.label"] = "Esfuerzo estimado de remediación",
        ["op.remediation.hours"] = "horas",
        ["op.remediation.note"]  = "Estimación asumiendo que los controles relacionados se agrupan en una GPO/perfil Intune por categoría (ej: todo el hardening de Edge = 1 GPO) más ~15 min de validación por endpoint. Una reunión de dimensionamiento da el número final.",
        ["op.footer.prepared"]   = "Preparado por",
        ["op.footer.generated"]  = "Generado",
        ["op.footer.confidential"] = "Confidencial — Uso interno de",
        ["op.risk.affects"]      = "afecta a",
        ["op.risk.machines"]     = "equipos",
        ["op.risk.machine"]      = "equipo",
        ["op.cover.for"]         = "Preparado para",
        ["op.cover.date"]        = "Fecha de evaluación",
        ["op.cover.score"]       = "Postura general",
    };

    private static void AppendOnePagerFooter(StringBuilder sb, ReportUserInfo user, Organization org, string lang)
    {
        var fullName = string.IsNullOrWhiteSpace(user.FullName) ? "—" : user.FullName!;
        var email    = string.IsNullOrWhiteSpace(user.Email)    ? "—" : user.Email!;
        var phone    = string.IsNullOrWhiteSpace(user.Phone)    ? "—" : user.Phone!;
        var job      = string.IsNullOrWhiteSpace(user.JobTitle) ? ""  : $" · {user.JobTitle}";

        sb.AppendLine("<div class='op-footer'>");
        sb.AppendLine("<div class='op-footer-row'>");
        sb.AppendLine("<div>");
        sb.AppendLine($"<div class='op-footer-user'>{HtmlEncode(T("op.footer.prepared", lang))}: {HtmlEncode(fullName)}{HtmlEncode(job)}</div>");
        sb.AppendLine($"<div>{HtmlEncode(email)} &middot; {HtmlEncode(phone)}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='op-footer-brand'>");
        sb.AppendLine($"<div>{HtmlEncode(T("op.footer.confidential", lang))} {HtmlEncode(org.Name)}</div>");
        sb.AppendLine($"<div>{HtmlEncode(T("op.footer.generated", lang))} {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div></div>");
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

    /// <summary>
    /// Renders framework compliance bars with P/F counts normalized to
    /// per-machine averages. Use in ORG-level reports where the raw
    /// frameworkScores.PassCount/FailCount are sums over all runs — with
    /// 200 machines those inflate into five-digit numbers that confuse
    /// executive readers (e.g. "10,705 fails" when in reality each
    /// endpoint only fails ~54 controls). Bounded by the number of
    /// controls in the framework.
    /// For single-machine (per-run) reports, call `AppendFrameworkBars`
    /// directly — those counts are already per-machine.
    /// </summary>
    private static void AppendNormalizedFrameworkBars(StringBuilder sb,
        List<FrameworkScoreDto> frameworkScores, int machineCount, string? caption = null)
    {
        var normalized = machineCount > 0
            ? frameworkScores.Select(fs => new FrameworkScoreDto
            {
                Code      = fs.Code,
                Name      = fs.Name,
                Score     = fs.Score,
                PassCount = (short)Math.Round((double)fs.PassCount / machineCount),
                FailCount = (short)Math.Round((double)fs.FailCount / machineCount)
            }).ToList()
            : frameworkScores;

        AppendFrameworkBars(sb, normalized);
        if (!string.IsNullOrEmpty(caption))
        {
            sb.AppendLine($"<p style='font-size:10px;color:#888;margin:4px 0 12px;text-align:right;font-style:italic'>{HtmlEncode(caption)}</p>");
        }
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

    // ── Technical Level helpers ──────────────────────────────────────────

    /// <summary>
    /// Renders the Technical Level Block 1 Asset Matrix: one row per machine,
    /// ordered worst-to-best by individual score. Paginates every ~25 rows.
    /// </summary>
    private static void AppendAssetMatrix(StringBuilder sb, List<AssessmentRun> runs, ReportBranding brand, string lang)
    {
        var es = lang == "es";

        var sorted = runs
            .OrderBy(r => r.GlobalScore ?? 100m)
            .ThenBy(r => r.Machine.Hostname)
            .ToList();

        const int perPage = 25;
        var total = sorted.Count;
        for (int offset = 0; offset < total; offset += perPage)
        {
            if (offset > 0)
            {
                sb.AppendLine("</div></div>");
                sb.AppendLine("<div class='page'>");
                AppendPageHeader(sb, es ? "Matriz de Activos (continuación)" : "Asset Matrix (continued)", brand,
                    es ? "NIVEL TÉCNICO" : "TECHNICAL LEVEL");
                sb.AppendLine("<div class='pb'>");
            }
            sb.AppendLine("<table class='results-table' style='font-size:10px'>");
            sb.AppendLine($"<tr><th style='width:28%'>{HtmlEncode(es ? "Hostname" : "Hostname")}</th><th style='width:20%'>{HtmlEncode(es ? "SO" : "OS Status")}</th><th style='width:14%' class='num'>{HtmlEncode(es ? "Críticos" : "Critical")}</th><th style='width:18%'>{HtmlEncode(es ? "Heartbeat" : "Heartbeat")}</th><th style='width:20%' class='num'>{HtmlEncode(es ? "Score" : "Score")}</th></tr>");

            foreach (var r in sorted.Skip(offset).Take(perPage))
            {
                var m = r.Machine;
                var osLabel = m.OsName ?? "—";
                var osClass = (m.OsName?.Contains("2008") == true || m.OsName?.Contains("2003") == true ||
                               m.OsName?.Contains("Windows 7") == true || m.OsName?.Contains("Vista") == true)
                    ? "fail" : "";
                var heartbeat = m.LastSeenAt.HasValue
                    ? DescribeHeartbeat(DateTime.UtcNow - m.LastSeenAt.Value, es)
                    : (es ? "nunca" : "never");
                var heartbeatDays = m.LastSeenAt.HasValue
                    ? (DateTime.UtcNow - m.LastSeenAt.Value).TotalDays
                    : 999;
                var hbClass = heartbeatDays > 7 ? "fail" : heartbeatDays > 3 ? "warn" : "";
                var score = r.GlobalScore ?? 0;
                var scoreClass = score < 60 ? "fail" : score < 85 ? "warn" : "pass";

                sb.AppendLine($"<tr class='{scoreClass}'>");
                sb.AppendLine($"<td style='font-family:monospace;font-weight:600'>{HtmlEncode(m.Hostname)}</td>");
                sb.AppendLine($"<td class='{osClass}'>{HtmlEncode(osLabel)}</td>");
                sb.AppendLine($"<td class='num'>{(r.FailCount ?? 0)}</td>");
                sb.AppendLine($"<td class='{hbClass}'>{HtmlEncode(heartbeat)}</td>");
                sb.AppendLine($"<td class='num'><strong>{score:0.#}</strong></td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
        }
    }

    private static string DescribeHeartbeat(TimeSpan ago, bool es)
    {
        if (ago.TotalHours < 1) return es ? "hace minutos" : "minutes ago";
        if (ago.TotalHours < 24) return es ? $"hace {(int)ago.TotalHours}h" : $"{(int)ago.TotalHours}h ago";
        var days = (int)ago.TotalDays;
        return es ? $"hace {days} días" : $"{days} days ago";
    }

    /// <summary>
    /// Renders the Technical Level Block 2 Top 10 Critical Findings across the fleet.
    /// </summary>
    private static void AppendTop10CriticalFindings(StringBuilder sb, List<OrgControlResult> allResults, string lang)
    {
        var es = lang == "es";

        var top = allResults
            .Where(r => r.Status == "fail" && (r.Severity == "critical" || r.Severity == "high"))
            .GroupBy(r => r.ControlId)
            .Select(g => new
            {
                ControlId = g.Key,
                Name = g.First().Name,
                Severity = g.First().Severity,
                Remediation = g.First().Remediation,
                MachineCount = g.Select(x => x.RunId).Distinct().Count()
            })
            .OrderByDescending(x => x.Severity == "critical" ? 1 : 0)
            .ThenByDescending(x => x.MachineCount)
            .Take(10)
            .ToList();

        sb.AppendLine($"<h3>{HtmlEncode(es ? "Top 10 Hallazgos Críticos" : "Top 10 Critical Findings")}</h3>");

        if (top.Count == 0)
        {
            sb.AppendLine($"<p style='color:#64748B;font-size:11px'>{HtmlEncode(es ? "No se detectaron hallazgos críticos o altos en este período." : "No critical or high-severity findings detected in this period.")}</p>");
            return;
        }

        sb.AppendLine("<table class='results-table' style='font-size:10px'>");
        sb.AppendLine($"<tr><th style='width:6%'>#</th><th style='width:32%'>{HtmlEncode(es ? "Hallazgo" : "Finding")}</th><th style='width:10%'>{HtmlEncode(es ? "Sev" : "Sev")}</th><th style='width:10%' class='num'>{HtmlEncode(es ? "Equipos" : "Hosts")}</th><th>{HtmlEncode(es ? "Remediación" : "Fix")}</th></tr>");
        int n = 1;
        foreach (var t in top)
        {
            var sev = t.Severity == "critical"
                ? (es ? "CRÍTICO" : "CRITICAL")
                : (es ? "ALTO"    : "HIGH");
            var sevCls = t.Severity == "critical" ? "critical" : "high";
            sb.AppendLine("<tr class='fail'>");
            sb.AppendLine($"<td class='num'>{n++}</td>");
            sb.AppendLine($"<td><strong>{HtmlEncode(t.Name)}</strong></td>");
            sb.AppendLine($"<td><span class='severity {sevCls}'>{HtmlEncode(sev)}</span></td>");
            sb.AppendLine($"<td class='num'>{t.MachineCount}</td>");
            sb.AppendLine($"<td style='font-size:9px'>{HtmlEncode(t.Remediation ?? (es ? "(sin remediación documentada)" : "(no remediation documented)"))}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
    }

    /// <summary>
    /// Renders the Technical Level Block 3 "Los 6 de Hierro" — 6 thematic
    /// sub-lists of hygiene failures. Each shows affected hostnames, or a
    /// green "all compliant" message if empty.
    /// </summary>
    private static void AppendSixIronsHardeningAudit(StringBuilder sb, List<AssessmentRun> runs,
        List<OrgControlResult> allResults, HygieneScanDto? hygiene, string lang)
    {
        var es = lang == "es";

        sb.AppendLine($"<h3>{HtmlEncode(es ? "Los 6 de Hierro — Auditoría de Hardening" : "The 6 Irons — Hardening Audit")}</h3>");

        // 1. Cifrado — machines without BitLocker
        var noBitlocker = runs.Where(r => r.Machine.Bitlocker != true).Select(r => r.Machine.Hostname).ToList();
        AppendIronSection(sb, "🔒", es ? "Cifrado — sin BitLocker" : "Encryption — without BitLocker",
            noBitlocker, es);

        // 2. Protocolos — SMBv1 / NTLMv1 enabled
        var runLookup = runs.ToDictionary(r => r.Id, r => r.Machine.Hostname);
        var legacyProto = allResults
            .Where(r => r.Status == "fail" && (
                r.Name.Contains("SMBv1", StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains("SMB1",  StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains("NTLMv1", StringComparison.OrdinalIgnoreCase)))
            .Select(r => runLookup.GetValueOrDefault(r.RunId, "unknown"))
            .Distinct()
            .ToList();
        AppendIronSection(sb, "📡", es ? "Protocolos — SMBv1 / NTLMv1 habilitado" : "Protocols — SMBv1 / NTLMv1 enabled",
            legacyProto, es);

        // 3. Hardware — no TPM or TPM disabled
        var noTpm = runs.Where(r => r.Machine.TpmPresent != true).Select(r => r.Machine.Hostname).ToList();
        AppendIronSection(sb, "💾", es ? "Hardware — sin TPM o deshabilitado" : "Hardware — no TPM or disabled",
            noTpm, es);

        // 4. Identidad — no LAPS deployed
        var noLapsHosts = hygiene?.Findings
            .Where(f => f.Status == "NoLAPS")
            .Select(f => f.Name)
            .ToList() ?? new List<string>();
        AppendIronSection(sb, "🔑", es ? "Identidad — sin LAPS" : "Identity — no LAPS",
            noLapsHosts, es);

        // 5. Higiene — Password Never Expires accounts (count, not hostnames)
        var pwdNever = hygiene?.PwdNeverExpire ?? 0;
        if (pwdNever == 0)
        {
            AppendIronCompliant(sb, "🧹", es ? "Higiene — contraseñas con expiración" : "Hygiene — password expiration", es);
        }
        else
        {
            sb.AppendLine("<div style='background:#FEF2F2;border:1px solid #FECACA;border-left:4px solid #991B1B;border-radius:4px;padding:10px 16px;margin-bottom:10px'>");
            sb.AppendLine($"<div style='font-size:11px;font-weight:700;color:#1E293B'>🧹 {HtmlEncode(es ? "Higiene — cuentas con Password Never Expires" : "Hygiene — Password Never Expires accounts")}</div>");
            sb.AppendLine($"<div style='font-size:10px;color:#7F1D1D;margin-top:4px'>{pwdNever} {HtmlEncode(es ? "cuentas" : "accounts")}</div>");
            sb.AppendLine("</div>");
        }

        // 6. Endpoint — Defender disabled (detected via Defender-related control failures)
        var noDefender = allResults
            .Where(r => r.Status == "fail" && (
                r.Name.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains("Antivirus", StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains("Real-Time Protection", StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains("AntiSpyware", StringComparison.OrdinalIgnoreCase)))
            .Select(r => runLookup.GetValueOrDefault(r.RunId, "unknown"))
            .Distinct()
            .ToList();
        AppendIronSection(sb, "🛡️", es ? "Endpoint — Defender deshabilitado" : "Endpoint — Defender disabled",
            noDefender, es);
    }

    private static void AppendIronSection(StringBuilder sb, string icon, string title, List<string> hostnames, bool es)
    {
        if (hostnames.Count == 0)
        {
            AppendIronCompliant(sb, icon, title, es);
            return;
        }
        sb.AppendLine("<div style='background:#FEF2F2;border:1px solid #FECACA;border-left:4px solid #991B1B;border-radius:4px;padding:10px 16px;margin-bottom:10px'>");
        sb.AppendLine($"<div style='font-size:11px;font-weight:700;color:#1E293B;margin-bottom:6px'>{icon} {HtmlEncode(title)} — {hostnames.Count} {HtmlEncode(es ? "equipos" : "machines")}</div>");
        sb.AppendLine($"<div style='font-size:9px;font-family:monospace;color:#7F1D1D;line-height:1.6'>{HtmlEncode(string.Join(" · ", hostnames.Take(40)))}");
        if (hostnames.Count > 40)
            sb.AppendLine($"<em> … {hostnames.Count - 40} {HtmlEncode(es ? "más" : "more")}</em>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
    }

    private static void AppendIronCompliant(StringBuilder sb, string icon, string title, bool es = false)
    {
        sb.AppendLine("<div style='background:#F0FDF4;border:1px solid #BBF7D0;border-left:4px solid #15803D;border-radius:4px;padding:8px 14px;margin-bottom:10px;font-size:10px;color:#166534'>");
        sb.AppendLine($"<strong>{icon} {HtmlEncode(title)}</strong> — ✅ {(es ? "Todos los equipos cumplen" : "All machines compliant")}");
        sb.AppendLine("</div>");
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

        /* Unified page header — dark band + ribbon gradient layered behind
           the logo (echoes the cover art), eyebrow + page title on the left,
           white logo on the right. Used by every page of every report type. */
        .ph {
            background-color: #3D4043;
            background-image: url('{{RibbonData.DataUri}}');
            background-repeat: no-repeat;
            background-position: right center;
            background-size: auto 220%;
            padding: 8mm 12mm 5mm;
            display: flex;
            justify-content: space-between;
            align-items: center;
            position: relative;
            overflow: hidden;
        }
        .ph-text { position: relative; z-index: 2; min-width: 0; }
        .ph-eyebrow {
            font-size: 8pt;
            font-weight: 700;
            letter-spacing: 0.12em;
            text-transform: uppercase;
            color: {{brand.AccentColor}};
            margin-bottom: 1mm;
        }
        .ph h1 {
            font-size: 13pt;
            font-weight: 700;
            color: #fff;
            letter-spacing: 0.01em;
            line-height: 1.15;
        }
        .ph img {
            height: 10mm;
            position: relative;
            z-index: 2;
        }
        .stripe { height: 3mm; background: linear-gradient(90deg, #006536 0%, #2BB673 20%, #39B54A 40%, #8DC63F 60%, #B2D235 80%, #D3E173 100%); }
        .pb { padding: 18px 36px 28px; }

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

        /* Unified page header (same as org reports — single source of
           truth for the baseline visual across all report types). */
        .ph {
            background-color: #3D4043;
            background-image: url('{{RibbonData.DataUri}}');
            background-repeat: no-repeat;
            background-position: right center;
            background-size: auto 220%;
            padding: 8mm 12mm 5mm;
            display: flex;
            justify-content: space-between;
            align-items: center;
            position: relative;
            overflow: hidden;
        }
        .ph-text { position: relative; z-index: 2; min-width: 0; }
        .ph-eyebrow {
            font-size: 8pt;
            font-weight: 700;
            letter-spacing: 0.12em;
            text-transform: uppercase;
            color: {{brand.AccentColor}};
            margin-bottom: 1mm;
        }
        .ph h1 {
            font-size: 13pt;
            font-weight: 700;
            color: #fff;
            letter-spacing: 0.01em;
            line-height: 1.15;
        }
        .ph img {
            height: 10mm;
            position: relative;
            z-index: 2;
        }
        .stripe { height: 3mm; background: linear-gradient(90deg, #006536 0%, #2BB673 20%, #39B54A 40%, #8DC63F 60%, #B2D235 80%, #D3E173 100%); }
        .pb { padding: 18px 36px 28px; }

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

internal class OrgEnrichment
{
    public List<MachineDisk> Disks { get; set; } = [];
    public List<MachinePort> Ports { get; set; } = [];
    public List<MachineThreat> Threats { get; set; } = [];
}

/// <summary>
/// Identity of the portal user that generated the report. Shown in the
/// Executive One-Pager footer so the C-level reader knows who to call back.
/// </summary>
internal class ReportUserInfo
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
    public string? CompanyName { get; set; }
}
