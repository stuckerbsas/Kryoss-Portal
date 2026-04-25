using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class ExecOnePagerBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Resumen Ejecutivo" : "Executive Summary";

    public int EstimateHeight(ReportData data) => 550;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        var avgScore = data.AvgScore;
        var grade = data.OrgGrade;
        var gradeColor = avgScore >= 85 ? "#15803D" : avgScore >= 60 ? "#B45309" : "#991B1B";
        var criticalFails = data.ControlResults.Where(r => r.Status == "fail" && r.Severity == "critical").Select(r => r.ControlId).Distinct().Count();
        var highFails = data.ControlResults.Where(r => r.Status == "fail" && r.Severity == "high").Select(r => r.ControlId).Distinct().Count();
        var totalChecks = data.ControlResults.Select(r => r.ControlId).Distinct().Count();
        var passRate = totalChecks > 0 ? 100.0 * data.ControlResults.Where(r => r.Status == "pass").Select(r => r.ControlId).Distinct().Count() / totalChecks : 0;

        // Score + grade hero
        sb.AppendLine("<div style='display:grid;grid-template-columns:auto 1fr;gap:20px;align-items:center;margin-bottom:16px'>");
        sb.AppendLine($"<div style='text-align:center;padding:16px 24px;border:2px solid {gradeColor};border-radius:12px;background:{(avgScore >= 85 ? "#F0FDF4" : avgScore >= 60 ? "#FFFBEB" : "#FEF2F2")}'>");
        sb.AppendLine($"<div style='font-size:36pt;font-weight:900;color:{gradeColor};line-height:1'>{grade}</div>");
        sb.AppendLine($"<div style='font-size:14pt;font-weight:700;color:{gradeColor}'>{avgScore:F1}%</div>");
        sb.AppendLine("</div>");

        // Quick stats
        sb.AppendLine("<div>");
        sb.AppendLine($"<div style='display:grid;grid-template-columns:repeat(4,1fr);gap:8px'>");
        RenderMiniKpi(sb, data.TotalMachines.ToString(), es ? "Equipos" : "Machines", "#0F172A");
        RenderMiniKpi(sb, $"{passRate:F0}%", es ? "Cumplimiento" : "Compliance", "#15803D");
        RenderMiniKpi(sb, criticalFails.ToString(), es ? "Críticos" : "Critical", "#991B1B");
        RenderMiniKpi(sb, highFails.ToString(), es ? "Altos" : "High", "#B45309");
        sb.AppendLine("</div>");
        sb.AppendLine("</div></div>");

        // Framework scores (compact)
        if (data.FrameworkScores.Count > 0)
        {
            sb.AppendLine($"<h3 style='margin:12px 0 6px'>{(es ? "Cumplimiento por Framework" : "Framework Compliance")}</h3>");
            sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(auto-fill,minmax(120px,1fr));gap:6px'>");
            foreach (var fw in data.FrameworkScores.OrderByDescending(f => f.Score))
            {
                var fwColor = fw.Score >= 80 ? "#15803D" : fw.Score >= 60 ? "#B45309" : "#991B1B";
                sb.AppendLine($"<div style='border:1px solid #E2E8F0;border-radius:6px;padding:8px;text-align:center'>");
                sb.AppendLine($"<div style='font-size:16pt;font-weight:700;color:{fwColor}'>{fw.Score:F0}%</div>");
                sb.AppendLine($"<div style='font-size:7pt;color:#64748B;font-weight:600'>{ReportHelpers.HtmlEncode(fw.Code)}</div>");
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div>");
        }

        // Cloud score (if available)
        if (data.HasCloudData && data.AreaScores != null && data.AreaScores.Count > 0)
        {
            var cloudAvg = Math.Round(data.AreaScores.Values.Average(), 1);
            var cColor = cloudAvg >= 80 ? "#15803D" : cloudAvg >= 60 ? "#B45309" : "#991B1B";
            sb.AppendLine($"<div style='margin-top:12px;padding:10px 14px;border:1px solid #E2E8F0;border-left:3px solid {cColor};border-radius:4px;display:flex;justify-content:space-between;align-items:center'>");
            sb.AppendLine($"<div><strong>{(es ? "Cloud Security Score" : "Cloud Security Score")}</strong><span style='color:#64748B;font-size:8pt;margin-left:8px'>{data.AreaScores.Count} {(es ? "áreas" : "areas")}</span></div>");
            sb.AppendLine($"<div style='font-size:16pt;font-weight:700;color:{cColor}'>{cloudAvg:F0}%</div>");
            sb.AppendLine("</div>");
        }

        // Top 5 critical findings (compact list)
        var topFindings = data.ControlResults
            .Where(r => r.Status == "fail" && r.Severity is "critical" or "high")
            .GroupBy(r => r.ControlDefId)
            .Select(g => new { Name = g.First().Name, Severity = g.First().Severity, Machines = g.Select(r => r.RunId).Distinct().Count() })
            .OrderByDescending(x => x.Severity == "critical" ? 2 : 1)
            .ThenByDescending(x => x.Machines)
            .Take(5)
            .ToList();

        if (topFindings.Count > 0)
        {
            sb.AppendLine($"<h3 style='margin:12px 0 6px'>{(es ? "Hallazgos Principales" : "Top Findings")}</h3>");
            sb.AppendLine("<table class='data-table'><thead><tr>");
            sb.AppendLine($"<th>{(es ? "Control" : "Control")}</th><th>{(es ? "Severidad" : "Severity")}</th><th>{(es ? "Equipos" : "Machines")}</th>");
            sb.AppendLine("</tr></thead><tbody>");
            foreach (var f in topFindings)
            {
                var sevClass = f.Severity == "critical" ? "fail" : "warn";
                sb.AppendLine($"<tr><td>{ReportHelpers.HtmlEncode(f.Name)}</td><td class='{sevClass}'>{f.Severity}</td><td>{f.Machines}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, SectionTitle(options)!, data.Branding);
        sb.AppendLine("<div class='pb'>");
        sb.Append(RenderContent(data, options));
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static void RenderMiniKpi(StringBuilder sb, string value, string label, string color)
    {
        sb.AppendLine($"<div style='text-align:center;padding:8px;border:1px solid #E2E8F0;border-top:2px solid {color};border-radius:4px'>");
        sb.AppendLine($"<div style='font-size:16pt;font-weight:800;color:{color}'>{value}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }
}
