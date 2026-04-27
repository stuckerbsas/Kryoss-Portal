using System.Text;
using System.Text.Json;
using System.Web;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services.CloudAssessment;

public interface ICloudAssessmentReportService
{
    Task<string> GenerateAsync(Guid orgId, string reportType, string lang = "en");
}

public class CloudAssessmentReportService : ICloudAssessmentReportService
{
    private readonly KryossDbContext _db;

    public CloudAssessmentReportService(KryossDbContext db) => _db = db;

    public async Task<string> GenerateAsync(Guid orgId, string reportType, string lang = "en")
    {
        var org = await _db.Organizations
            .Include(o => o.Franchise)
            .Include(o => o.Brand)
            .FirstOrDefaultAsync(o => o.Id == orgId)
            ?? throw new InvalidOperationException($"Organization {orgId} not found");

        var scan = await _db.CloudAssessmentScans
            .Where(s => s.OrganizationId == orgId && s.Status == "completed")
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("No completed Cloud Assessment scan found");

        var findings = await _db.CloudAssessmentFindings
            .Where(f => f.ScanId == scan.Id)
            .ToListAsync();

        var frameworkScores = await _db.CloudAssessmentFrameworkScores
            .Where(s => s.ScanId == scan.Id)
            .Join(_db.CloudAssessmentFrameworks, s => s.FrameworkId, f => f.Id, (s, f) => new FwScoreDto
            {
                FrameworkName = f.Name,
                FrameworkCode = f.Code,
                Grade = s.Grade ?? "F",
                ScorePct = s.ScorePct,
                TotalControls = s.TotalControls,
                PassingControls = s.PassingControls,
                FailingControls = s.FailingControls,
                CoveredControls = s.CoveredControls
            })
            .OrderByDescending(x => x.ScorePct)
            .ToListAsync();

        var areaScores = ParseAreaScores(scan.AreaScores);
        var branding = ResolveBranding(org);
        var es = lang == "es";

        return reportType switch
        {
            "c-level" => BuildCLevel(org, scan, findings, frameworkScores, areaScores, branding, es),
            "franchise" => BuildFranchise(org, scan, findings, frameworkScores, areaScores, branding, es),
            "technical" => BuildTechnical(org, scan, findings, branding, es),
            "presales" => BuildPresales(org, scan, findings, areaScores, branding, es),
            _ => throw new ArgumentException($"Unknown report type: {reportType}")
        };
    }

    // ======================================================================
    //  BRANDING
    // ======================================================================

    private record Branding(string CompanyName, string PrimaryColor, string AccentColor, string? LogoUrl, string FontFamily);

    private static Branding ResolveBranding(Organization org)
    {
        var brand = org.Brand;
        var franchise = org.Franchise;
        return new Branding(
            CompanyName: brand?.Name ?? franchise.BrandName ?? franchise.Name,
            PrimaryColor: brand?.ColorPrimary ?? franchise.BrandColorPrimary ?? "#006536",
            AccentColor: brand?.ColorAccent ?? franchise.BrandColorAccent ?? "#A2C564",
            LogoUrl: brand?.LogoUrl ?? franchise.BrandLogoUrl,
            FontFamily: brand?.FontFamily ?? "Montserrat"
        );
    }

    // ======================================================================
    //  SHARED HTML INFRASTRUCTURE
    // ======================================================================

    private static string PageShell(Branding b, string title, string bodyContent, bool es)
    {
        var fontImport = b.FontFamily == "Montserrat"
            ? "<link href='https://fonts.googleapis.com/css2?family=Montserrat:wght@300;400;500;600;700&display=swap' rel='stylesheet'>"
            : "";

        return $@"<!DOCTYPE html>
<html lang='{(es ? "es" : "en")}'>
<head>
<meta charset='utf-8'>
<meta http-equiv='Content-Security-Policy' content=""default-src 'none'; style-src 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; img-src data:; base-uri 'none'; form-action 'none';"">
<title>{Enc(title)}</title>
{fontImport}
<style>
@page {{ size: A4; margin: 15mm 18mm 20mm 18mm; }}
* {{ box-sizing: border-box; margin: 0; padding: 0; }}
body {{ font-family: '{b.FontFamily}', 'Segoe UI', sans-serif; font-size: 10pt; color: #1a1a1a; line-height: 1.5; }}
.page {{ page-break-after: always; min-height: 257mm; position: relative; padding: 0; }}
.page:last-child {{ page-break-after: auto; }}

/* Cover */
.cover {{ display: flex; flex-direction: column; justify-content: center; align-items: center; min-height: 257mm; text-align: center; background: linear-gradient(135deg, {b.PrimaryColor} 0%, {Darken(b.PrimaryColor)} 100%); color: white; padding: 40mm 30mm; }}
.cover img.logo {{ max-width: 220px; max-height: 80px; margin-bottom: 30px; }}
.cover h1 {{ font-size: 28pt; font-weight: 700; margin-bottom: 10px; }}
.cover h2 {{ font-size: 14pt; font-weight: 400; opacity: 0.9; margin-bottom: 30px; }}
.cover .meta {{ font-size: 10pt; opacity: 0.7; }}

/* Content pages */
.content {{ padding: 5mm 0; }}
h2 {{ color: {b.PrimaryColor}; font-size: 14pt; font-weight: 600; margin: 16px 0 8px 0; padding-bottom: 4px; border-bottom: 2px solid {b.AccentColor}; }}
h3 {{ color: #333; font-size: 11pt; font-weight: 600; margin: 12px 0 6px 0; }}

/* Tables */
table {{ width: 100%; border-collapse: collapse; margin: 8px 0 16px 0; font-size: 9pt; }}
th {{ background: {b.PrimaryColor}; color: white; padding: 6px 8px; text-align: left; font-weight: 600; }}
td {{ padding: 5px 8px; border-bottom: 1px solid #e5e7eb; }}
tr:nth-child(even) {{ background: #f9fafb; }}

/* Badges */
.badge {{ display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 8pt; font-weight: 600; }}
.badge-success {{ background: #dcfce7; color: #166534; }}
.badge-warning {{ background: #fef3c7; color: #92400e; }}
.badge-action {{ background: #fee2e2; color: #991b1b; }}
.badge-info {{ background: #dbeafe; color: #1e40af; }}
.badge-muted {{ background: #f3f4f6; color: #6b7280; }}

/* Score cards */
.score-grid {{ display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; margin: 12px 0; }}
.score-card {{ border: 1px solid #e5e7eb; border-radius: 8px; padding: 12px; text-align: center; }}
.score-card .value {{ font-size: 20pt; font-weight: 700; }}
.score-card .label {{ font-size: 8pt; color: #6b7280; }}

/* Grade */
.grade {{ display: inline-block; width: 36px; height: 36px; line-height: 36px; text-align: center; border-radius: 50%; font-weight: 700; font-size: 14pt; color: white; }}
.grade-a {{ background: #16a34a; }} .grade-b {{ background: #84cc16; }} .grade-c {{ background: #eab308; }} .grade-d {{ background: #f97316; }} .grade-f {{ background: #ef4444; }}

/* Radar */
.radar-container {{ text-align: center; margin: 10px 0; }}
.radar-container svg {{ max-width: 280px; }}

/* Package summary */
.pkg-row {{ display: flex; justify-content: space-between; align-items: center; padding: 8px 12px; border-bottom: 1px solid #e5e7eb; }}
.pkg-row:nth-child(even) {{ background: #f9fafb; }}
.pkg-name {{ font-weight: 600; }}
.pkg-hours {{ font-size: 14pt; font-weight: 700; color: {b.PrimaryColor}; }}

/* Footer */
.footer {{ position: fixed; bottom: 0; left: 0; right: 0; padding: 4mm 18mm; font-size: 7pt; color: #9ca3af; display: flex; justify-content: space-between; border-top: 1px solid #e5e7eb; }}

/* Print */
@media print {{
  body {{ -webkit-print-color-adjust: exact; print-color-adjust: exact; }}
  .page {{ page-break-after: always; }}
}}
</style>
</head>
<body>
{bodyContent}
<div class='footer'>
  <span>{Enc(b.CompanyName)} — {(es ? "Confidencial" : "Confidential")}</span>
  <span>{(es ? "Generado por" : "Generated by")} Kryoss Platform</span>
</div>
</body>
</html>";
    }

    private static string CoverPage(Branding b, Organization org, CloudAssessmentScan scan, string reportTitle, string reportSubtitle, bool es)
    {
        var logoHtml = b.LogoUrl != null ? $"<img class='logo' src='{Enc(b.LogoUrl)}' alt='{Enc(b.CompanyName)}'>" : "";
        var date = scan.CreatedAt.ToString("MMMM d, yyyy");
        return $@"
<div class='page cover'>
  {logoHtml}
  <h1>{Enc(reportTitle)}</h1>
  <h2>{Enc(reportSubtitle)}</h2>
  <div style='margin-top: 40px;'>
    <div style='font-size: 16pt; font-weight: 600;'>{Enc(org.Name)}</div>
    <div class='meta' style='margin-top: 10px;'>{date}</div>
    <div class='meta'>{(es ? "Preparado por" : "Prepared by")} {Enc(b.CompanyName)}</div>
  </div>
</div>";
    }

    // ======================================================================
    //  TIER 1: C-LEVEL
    // ======================================================================

    private string BuildCLevel(Organization org, CloudAssessmentScan scan,
        List<CloudAssessmentFinding> findings, List<FwScoreDto> frameworkScores,
        Dictionary<string, decimal> areaScores, Branding b, bool es)
    {
        var sb = new StringBuilder();

        sb.Append(CoverPage(b, org, scan,
            es ? "Evaluacion de Seguridad Cloud" : "Cloud Security Assessment",
            es ? "Resumen Ejecutivo" : "Executive Summary", es));

        // Page 2: Overall posture
        sb.Append("<div class='page'><div class='content'>");
        sb.Append($"<h2>{(es ? "Postura General de Seguridad" : "Overall Security Posture")}</h2>");

        var overallScore = scan.OverallScore ?? 0m;
        var grade = GradeFromScore(overallScore);

        sb.Append("<div style='text-align:center; margin: 20px 0;'>");
        sb.Append($"<div class='grade grade-{grade.ToLower()[0]}'>{Enc(grade)}</div>");
        sb.Append($"<div style='font-size: 24pt; font-weight: 700; margin-top: 8px;'>{overallScore:F0}<span style='font-size: 12pt; color: #6b7280;'>/100</span></div>");
        sb.Append("</div>");

        // Area scores
        sb.Append("<div class='score-grid'>");
        var areaLabels = new[] { ("identity", es ? "Identidad" : "Identity"), ("endpoint", "Endpoint"), ("data", es ? "Datos" : "Data"), ("productivity", es ? "Productividad" : "Productivity"), ("azure", "Azure"), ("mail_flow", es ? "Email" : "Email"), ("powerbi", "Power BI") };
        foreach (var (area, label) in areaLabels)
        {
            if (areaScores.TryGetValue(area, out var score))
                sb.Append($"<div class='score-card'><div class='value' style='color:{ScoreColor(score)}'>{score:F0}</div><div class='label'>{Enc(label)}</div></div>");
        }
        sb.Append("</div>");

        // Top critical findings (max 5)
        var critical = findings
            .Where(f => f.Status == "action_required" && f.Priority is "critical" or "high")
            .Take(5)
            .ToList();

        if (critical.Count > 0)
        {
            sb.Append($"<h2>{(es ? "Hallazgos Criticos" : "Critical Findings")}</h2>");
            sb.Append("<table><tr><th>#</th><th>{(es ? \"Area\" : \"Area\")}</th><th>{(es ? \"Hallazgo\" : \"Finding\")}</th><th>{(es ? \"Impacto\" : \"Impact\")}</th></tr>");
            int i = 1;
            foreach (var f in critical)
            {
                var entry = RemediationCatalog.Get(f.Area, f.Service, f.Feature);
                sb.Append($"<tr><td>{i++}</td><td>{Enc(f.Area)}</td><td>{Enc(f.Feature)}</td><td>{Enc(entry?.RemediationSummary ?? f.Observation ?? "")}</td></tr>");
            }
            sb.Append("</table>");
        }

        // Compliance summary
        if (frameworkScores.Count > 0)
        {
            sb.Append($"<h2>{(es ? "Cumplimiento Normativo" : "Compliance Summary")}</h2>");
            sb.Append($"<table><tr><th>{(es ? "Marco" : "Framework")}</th><th>{(es ? "Puntuacion" : "Score")}</th><th>{(es ? "Grado" : "Grade")}</th><th>{(es ? "Controles" : "Controls")}</th></tr>");
            foreach (var fs in frameworkScores)
            {
                sb.Append($"<tr><td>{Enc(fs.FrameworkName)}</td><td>{fs.ScorePct:F1}%</td><td><span class='grade grade-{fs.Grade.ToLower()[0]}' style='width:24px;height:24px;line-height:24px;font-size:10pt;'>{Enc(fs.Grade)}</span></td><td>{fs.PassingControls}/{fs.TotalControls}</td></tr>");
            }
            sb.Append("</table>");
        }

        sb.Append("</div></div>");
        return PageShell(b, es ? "Evaluacion Cloud - Ejecutivo" : "Cloud Assessment - Executive", sb.ToString(), es);
    }

    // ======================================================================
    //  TIER 2: FRANCHISE (scope + hours + revenue)
    // ======================================================================

    private string BuildFranchise(Organization org, CloudAssessmentScan scan,
        List<CloudAssessmentFinding> findings, List<FwScoreDto> frameworkScores,
        Dictionary<string, decimal> areaScores, Branding b, bool es)
    {
        var sb = new StringBuilder();

        sb.Append(CoverPage(b, org, scan,
            es ? "Propuesta de Remediacion Cloud" : "Cloud Remediation Proposal",
            es ? "Alcance y Estimacion de Horas" : "Scope & Hour Estimate", es));

        // Page 2: Package summary
        sb.Append("<div class='page'><div class='content'>");
        sb.Append($"<h2>{(es ? "Resumen de Paquetes de Servicio" : "Service Package Summary")}</h2>");

        var actionFindings = findings
            .Where(f => f.Status is "action_required" or "warning")
            .ToList();

        var packageData = new Dictionary<string, (decimal Hours, int Findings, List<string> Items)>();
        foreach (var pkg in RemediationCatalog.PackageOrder)
            packageData[pkg] = (0, 0, new List<string>());

        foreach (var f in actionFindings)
        {
            var entry = RemediationCatalog.Get(f.Area, f.Service, f.Feature);
            if (entry == null) continue;
            if (!packageData.ContainsKey(entry.ServicePackage))
                packageData[entry.ServicePackage] = (0, 0, new List<string>());
            var (h, c, items) = packageData[entry.ServicePackage];
            packageData[entry.ServicePackage] = (h + entry.EstimatedHours, c + 1, items);
            items.Add(entry.Feature);
        }

        decimal totalHours = 0;
        foreach (var pkg in RemediationCatalog.PackageOrder)
        {
            if (!packageData.ContainsKey(pkg) || packageData[pkg].Findings == 0) continue;
            var (hours, count, _) = packageData[pkg];
            totalHours += hours;
            sb.Append($"<div class='pkg-row'><div><span class='pkg-name'>{Enc(pkg)}</span><br><span style='font-size:8pt;color:#6b7280;'>{count} {(es ? "hallazgos" : "findings")}</span></div><div class='pkg-hours'>{hours:F1}h</div></div>");
        }

        sb.Append($"<div class='pkg-row' style='border-top: 2px solid {b.PrimaryColor}; font-weight: 700;'><div><span class='pkg-name' style='font-size:12pt;'>{(es ? "TOTAL ESTIMADO" : "TOTAL ESTIMATE")}</span></div><div class='pkg-hours' style='font-size:18pt;'>{totalHours:F1}h</div></div>");
        sb.Append("</div></div>");

        // Page 3: Detailed breakdown per package
        sb.Append("<div class='page'><div class='content'>");
        sb.Append($"<h2>{(es ? "Detalle por Paquete" : "Package Breakdown")}</h2>");

        foreach (var pkg in RemediationCatalog.PackageOrder)
        {
            if (!packageData.ContainsKey(pkg) || packageData[pkg].Findings == 0) continue;
            var (hours, count, items) = packageData[pkg];

            sb.Append($"<h3>{Enc(pkg)} — {hours:F1}h</h3>");
            sb.Append("<table><tr>");
            sb.Append($"<th>{(es ? "Hallazgo" : "Finding")}</th>");
            sb.Append($"<th>{(es ? "Estado" : "Status")}</th>");
            sb.Append($"<th>{(es ? "Horas" : "Hours")}</th>");
            sb.Append($"<th>{(es ? "Licencia Requerida" : "License Required")}</th>");
            sb.Append($"<th>{(es ? "Costo/usuario/mes" : "Cost/user/mo")}</th>");
            sb.Append("</tr>");

            foreach (var f in actionFindings)
            {
                var entry = RemediationCatalog.Get(f.Area, f.Service, f.Feature);
                if (entry == null || entry.ServicePackage != pkg) continue;
                var statusClass = f.Status == "action_required" ? "badge-action" : "badge-warning";
                sb.Append($"<tr><td>{Enc(f.Feature)}</td><td><span class='badge {statusClass}'>{StatusLabel(f.Status, es)}</span></td><td style='text-align:right;font-weight:600;'>{entry.EstimatedHours:F1}</td><td>{Enc(entry.LicenseRequired ?? "—")}</td><td>{(entry.MonthlyCostPerUser.HasValue ? $"${entry.MonthlyCostPerUser.Value:F2}" : "—")}</td></tr>");
            }
            sb.Append("</table>");
        }

        // Success items (what's already good)
        var successCount = findings.Count(f => f.Status == "success");
        sb.Append($"<div style='margin-top:16px;padding:12px;background:#dcfce7;border-radius:8px;'>");
        sb.Append($"<strong>{successCount}</strong> {(es ? "controles ya cumplen con las mejores practicas" : "controls already meet best practices")}.");
        sb.Append("</div>");

        sb.Append("</div></div>");
        return PageShell(b, es ? "Propuesta Cloud" : "Cloud Proposal", sb.ToString(), es);
    }

    // ======================================================================
    //  TIER 3: TECHNICAL (step-by-step remediation)
    // ======================================================================

    private string BuildTechnical(Organization org, CloudAssessmentScan scan,
        List<CloudAssessmentFinding> findings,
        Branding b, bool es)
    {
        var sb = new StringBuilder();

        sb.Append(CoverPage(b, org, scan,
            es ? "Guia Tecnica de Remediacion Cloud" : "Cloud Remediation Technical Guide",
            es ? "Instrucciones Paso a Paso" : "Step-by-Step Instructions", es));

        // Group findings by area, then by priority
        var grouped = findings
            .Where(f => f.Status is "action_required" or "warning")
            .OrderBy(f => f.Priority == "critical" ? 0 : f.Priority == "high" ? 1 : f.Priority == "medium" ? 2 : 3)
            .GroupBy(f => f.Area)
            .ToList();

        foreach (var areaGroup in grouped)
        {
            sb.Append("<div class='page'><div class='content'>");
            sb.Append($"<h2>{AreaLabel(areaGroup.Key, es)}</h2>");

            foreach (var f in areaGroup)
            {
                var entry = RemediationCatalog.Get(f.Area, f.Service, f.Feature);
                var priorityClass = f.Priority is "critical" or "high" ? "badge-action" : "badge-warning";

                sb.Append($"<div style='margin-bottom:16px; padding:10px; border-left: 3px solid {(f.Status == "action_required" ? "#ef4444" : "#eab308")}; background: #fafafa;'>");
                sb.Append($"<div style='display:flex; justify-content:space-between; align-items:center;'>");
                sb.Append($"<strong>{Enc(f.Feature)}</strong>");
                sb.Append($"<span class='badge {priorityClass}'>{Enc(f.Priority ?? "medium")}</span>");
                sb.Append("</div>");

                if (!string.IsNullOrEmpty(f.Observation))
                    sb.Append($"<div style='margin-top:4px; font-size:9pt; color:#4b5563;'>{Enc(f.Observation)}</div>");

                if (!string.IsNullOrEmpty(f.Recommendation))
                {
                    sb.Append($"<div style='margin-top:6px;'><strong>{(es ? "Accion:" : "Action:")}</strong> {Enc(f.Recommendation)}</div>");
                }
                else if (entry != null)
                {
                    sb.Append($"<div style='margin-top:6px;'><strong>{(es ? "Accion:" : "Action:")}</strong> {Enc(entry.RemediationSummary)}</div>");
                }

                if (!string.IsNullOrEmpty(f.LinkUrl))
                    sb.Append($"<div style='margin-top:4px; font-size:8pt;'><a href='{Enc(f.LinkUrl)}' style='color:{b.PrimaryColor};'>{Enc(f.LinkText ?? "Documentation")}</a></div>");

                if (entry != null)
                    sb.Append($"<div style='margin-top:4px; font-size:8pt; color:#6b7280;'>{(es ? "Tiempo estimado:" : "Estimated time:")} {entry.EstimatedHours:F1}h | {(es ? "Paquete:" : "Package:")} {Enc(entry.ServicePackage)}</div>");

                sb.Append("</div>");
            }

            sb.Append("</div></div>");
        }

        return PageShell(b, es ? "Guia Tecnica Cloud" : "Cloud Technical Guide", sb.ToString(), es);
    }

    // ======================================================================
    //  TIER 4: PRESALES (hook without detail)
    // ======================================================================

    private string BuildPresales(Organization org, CloudAssessmentScan scan,
        List<CloudAssessmentFinding> findings, Dictionary<string, decimal> areaScores,
        Branding b, bool es)
    {
        var sb = new StringBuilder();

        sb.Append(CoverPage(b, org, scan,
            es ? "Auditoria de Seguridad Cloud" : "Cloud Security Audit",
            es ? "Hallazgos Preliminares" : "Preliminary Findings", es));

        // Single page: scores + counts (NO specific findings)
        sb.Append("<div class='page'><div class='content'>");

        var overallScore = scan.OverallScore ?? 0m;
        var grade = GradeFromScore(overallScore);

        sb.Append("<div style='text-align:center; margin: 30px 0;'>");
        sb.Append($"<div style='font-size: 10pt; color: #6b7280;'>{(es ? "Puntuacion General" : "Overall Score")}</div>");
        sb.Append($"<div class='grade grade-{grade.ToLower()[0]}' style='width:60px;height:60px;line-height:60px;font-size:24pt;margin:10px auto;'>{Enc(grade)}</div>");
        sb.Append($"<div style='font-size: 28pt; font-weight: 700;'>{overallScore:F0}<span style='font-size:14pt;color:#6b7280;'>/100</span></div>");
        sb.Append("</div>");

        // Area breakdown — counts only, no details
        sb.Append($"<h2>{(es ? "Hallazgos por Area" : "Findings by Area")}</h2>");
        sb.Append("<table><tr>");
        sb.Append($"<th>{(es ? "Area" : "Area")}</th>");
        sb.Append($"<th style='text-align:center;'>{(es ? "Criticos" : "Critical")}</th>");
        sb.Append($"<th style='text-align:center;'>{(es ? "Advertencias" : "Warnings")}</th>");
        sb.Append($"<th style='text-align:center;'>{(es ? "Cumplidos" : "Passing")}</th>");
        sb.Append("</tr>");

        var areaGroups = findings.GroupBy(f => f.Area).OrderBy(g => g.Key);
        foreach (var g in areaGroups)
        {
            var critical = g.Count(f => f.Status == "action_required");
            var warnings = g.Count(f => f.Status == "warning");
            var passing = g.Count(f => f.Status == "success");
            sb.Append($"<tr><td><strong>{AreaLabel(g.Key, es)}</strong></td><td style='text-align:center;'>{(critical > 0 ? $"<span class='badge badge-action'>{critical}</span>" : "—")}</td><td style='text-align:center;'>{(warnings > 0 ? $"<span class='badge badge-warning'>{warnings}</span>" : "—")}</td><td style='text-align:center;'><span class='badge badge-success'>{passing}</span></td></tr>");
        }
        sb.Append("</table>");

        // Estimated remediation (total only — no breakdown)
        decimal totalHours = 0;
        foreach (var f in findings.Where(f => f.Status is "action_required" or "warning"))
        {
            var entry = RemediationCatalog.Get(f.Area, f.Service, f.Feature);
            if (entry != null) totalHours += entry.EstimatedHours;
        }

        sb.Append($"<div style='margin-top: 24px; padding: 20px; background: linear-gradient(135deg, {b.PrimaryColor}10, {b.AccentColor}20); border-radius: 12px; text-align: center;'>");
        sb.Append($"<div style='font-size: 10pt; color: #6b7280;'>{(es ? "Remediacion Estimada" : "Estimated Remediation")}</div>");
        sb.Append($"<div style='font-size: 28pt; font-weight: 700; color: {b.PrimaryColor};'>{totalHours:F0} {(es ? "horas" : "hours")}</div>");
        sb.Append($"<div style='font-size: 9pt; color: #6b7280; margin-top: 8px;'>{(es ? "Contactenos para una propuesta detallada de remediacion" : "Contact us for a detailed remediation proposal")}</div>");
        sb.Append("</div>");

        sb.Append("</div></div>");
        return PageShell(b, es ? "Auditoria Cloud" : "Cloud Audit", sb.ToString(), es);
    }

    // ======================================================================
    //  HELPERS
    // ======================================================================

    private static Dictionary<string, decimal> ParseAreaScores(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try
        {
            var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.TryGetDecimal(out var val))
                    dict[prop.Name] = val;
            }
            return dict;
        }
        catch { return new(); }
    }

    private static string GradeFromScore(decimal score) => score switch
    {
        >= 90 => "A+",
        >= 80 => "A",
        >= 70 => "B",
        >= 55 => "C",
        >= 40 => "D",
        _ => "F"
    };

    private static string ScoreColor(decimal score) => score switch
    {
        >= 80 => "#16a34a",
        >= 60 => "#eab308",
        _ => "#ef4444"
    };

    private static string StatusLabel(string status, bool es) => status switch
    {
        "action_required" => es ? "Accion Requerida" : "Action Required",
        "warning" => es ? "Advertencia" : "Warning",
        "success" => es ? "Cumplido" : "Passing",
        "not_licensed" => es ? "Sin Licencia" : "Not Licensed",
        "insight" => "Insight",
        _ => status
    };

    private static string AreaLabel(string area, bool es) => area switch
    {
        "identity" => es ? "Identidad y Acceso" : "Identity & Access",
        "endpoint" => es ? "Endpoint y Dispositivos" : "Endpoint & Devices",
        "data" => es ? "Proteccion de Datos" : "Data Protection",
        "productivity" => es ? "Productividad" : "Productivity",
        "azure" => es ? "Infraestructura Azure" : "Azure Infrastructure",
        "powerbi" => "Power BI",
        "mail_flow" => es ? "Seguridad de Email" : "Email Security",
        _ => area
    };

    private static string Darken(string hex)
    {
        if (hex.Length < 7) return hex;
        try
        {
            int r = Math.Max(0, Convert.ToInt32(hex[1..3], 16) - 40);
            int g = Math.Max(0, Convert.ToInt32(hex[3..5], 16) - 40);
            int bVal = Math.Max(0, Convert.ToInt32(hex[5..7], 16) - 40);
            return $"#{r:X2}{g:X2}{bVal:X2}";
        }
        catch { return hex; }
    }

    private static string Enc(string? s) => HttpUtility.HtmlEncode(s ?? "");

    private record FwScoreDto
    {
        public string FrameworkName { get; init; } = "";
        public string FrameworkCode { get; init; } = "";
        public string Grade { get; init; } = "F";
        public decimal ScorePct { get; init; }
        public int TotalControls { get; init; }
        public int PassingControls { get; init; }
        public int FailingControls { get; init; }
        public int CoveredControls { get; init; }
    }
}
