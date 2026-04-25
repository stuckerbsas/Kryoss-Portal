using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class M365Block : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Seguridad Microsoft 365" : "Microsoft 365 Security";

    public int EstimateHeight(ReportData data)
    {
        if (!data.M365Connected || data.M365Findings.Count == 0) return 0;
        return 200 + data.M365Findings.GroupBy(f => f.Category).Count() * 60;
    }

    public string RenderContent(ReportData data, ReportOptions options)
    {
        if (!data.M365Connected || data.M365Findings.Count == 0) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;

        var findings = data.M365Findings;
        var passed = findings.Count(f => f.Status == "pass");
        var failed = findings.Count(f => f.Status == "fail");
        var total = findings.Count;
        var passRate = total > 0 ? 100.0 * passed / total : 0;
        var scoreColor = passRate >= 80 ? "#15803D" : passRate >= 60 ? "#B45309" : "#991B1B";

        // Summary KPIs
        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:16px'>");
        RenderKpi(sb, $"{passRate:F0}%", es ? "Cumplimiento" : "Compliance", scoreColor);
        RenderKpi(sb, passed.ToString(), es ? "Aprobados" : "Passed", "#15803D");
        RenderKpi(sb, failed.ToString(), es ? "Fallidos" : "Failed", "#991B1B");
        RenderKpi(sb, total.ToString(), es ? "Total Checks" : "Total Checks", "#0F172A");
        sb.AppendLine("</div>");

        // Severity breakdown
        var critCount = findings.Count(f => f.Status == "fail" && f.Severity == "critical");
        var highCount = findings.Count(f => f.Status == "fail" && f.Severity == "high");
        var medCount = findings.Count(f => f.Status == "fail" && f.Severity == "medium");

        if (critCount + highCount + medCount > 0)
        {
            sb.AppendLine($"<div style='display:flex;gap:8px;margin-bottom:16px'>");
            if (critCount > 0)
                sb.AppendLine($"<span class='badge badge-action'>{critCount} {(es ? "Críticos" : "Critical")}</span>");
            if (highCount > 0)
                sb.AppendLine($"<span class='badge badge-warning'>{highCount} {(es ? "Altos" : "High")}</span>");
            if (medCount > 0)
                sb.AppendLine($"<span class='badge badge-muted'>{medCount} {(es ? "Medios" : "Medium")}</span>");
            sb.AppendLine("</div>");
        }

        // By category
        var categories = findings.GroupBy(f => f.Category).OrderByDescending(g => g.Count(f => f.Status == "fail"));
        foreach (var cat in categories)
        {
            var catPassed = cat.Count(f => f.Status == "pass");
            var catFailed = cat.Count(f => f.Status == "fail");
            var catTotal = cat.Count();
            var catPct = catTotal > 0 ? 100.0 * catPassed / catTotal : 0;
            var barColor = catPct >= 80 ? "#15803D" : catPct >= 60 ? "#B45309" : "#991B1B";

            sb.AppendLine($"<div style='margin-bottom:12px'>");
            sb.AppendLine($"<div style='display:flex;justify-content:space-between;align-items:center;margin-bottom:4px'>");
            sb.AppendLine($"<strong style='font-size:9pt'>{ReportHelpers.HtmlEncode(cat.Key)}</strong>");
            sb.AppendLine($"<span style='font-size:8pt;color:{barColor};font-weight:700'>{catPassed}/{catTotal}</span>");
            sb.AppendLine("</div>");

            // Progress bar
            sb.AppendLine($"<div style='height:6px;background:#E2E8F0;border-radius:3px;overflow:hidden'>");
            sb.AppendLine($"<div style='height:100%;width:{catPct:F0}%;background:{barColor};border-radius:3px'></div>");
            sb.AppendLine("</div>");

            // Failed items in this category
            var failedItems = cat.Where(f => f.Status == "fail").OrderByDescending(f => f.Severity == "critical" ? 3 : f.Severity == "high" ? 2 : 1);
            foreach (var item in failedItems)
            {
                var sevClass = item.Severity == "critical" ? "fail" : item.Severity == "high" ? "warn" : "";
                sb.AppendLine($"<div style='padding:4px 0 4px 12px;font-size:8pt;border-left:2px solid #E2E8F0;margin-top:3px'>");
                sb.AppendLine($"<span class='{sevClass}' style='font-weight:600'>{ReportHelpers.HtmlEncode(item.Name)}</span>");
                if (!string.IsNullOrEmpty(item.Finding))
                    sb.AppendLine($" <span style='color:#64748B'>— {ReportHelpers.HtmlEncode(item.Finding)}</span>");
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div>");
        }

        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var content = RenderContent(data, options);
        if (string.IsNullOrEmpty(content)) return "";
        var sb = new StringBuilder();
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, SectionTitle(options)!, data.Branding);
        sb.AppendLine("<div class='pb'>");
        sb.Append(content);
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    // Copilot readiness page (D1-D6 if cloud data available)
    public static string RenderCopilotPage(ReportData data, ReportOptions options)
    {
        if (!data.HasCloudData || data.CloudFindings == null) return "";

        var copilotFindings = data.CloudFindings
            .Where(f => f.Service?.Contains("Copilot", StringComparison.OrdinalIgnoreCase) == true
                     || f.Feature?.Contains("Copilot", StringComparison.OrdinalIgnoreCase) == true
                     || f.Area == "productivity")
            .ToList();

        if (copilotFindings.Count == 0) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Preparación para Copilot" : "Copilot Readiness", data.Branding);
        sb.AppendLine("<div class='pb'>");

        var actionRequired = copilotFindings.Count(f => f.Status == "action_required");
        var warnings = copilotFindings.Count(f => f.Status == "warning");
        var ready = copilotFindings.Count(f => f.Status == "success");

        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(3,1fr);gap:10px;margin-bottom:16px'>");
        RenderKpi(sb, ready.ToString(), es ? "Listo" : "Ready", "#15803D");
        RenderKpi(sb, warnings.ToString(), es ? "Advertencias" : "Warnings", "#B45309");
        RenderKpi(sb, actionRequired.ToString(), es ? "Acción Req." : "Action Req.", "#991B1B");
        sb.AppendLine("</div>");

        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Área" : "Area")}</th><th>{(es ? "Verificación" : "Check")}</th><th>{(es ? "Estado" : "Status")}</th><th>{(es ? "Observación" : "Observation")}</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var f in copilotFindings.OrderBy(f => f.Status == "success" ? 2 : f.Status == "warning" ? 1 : 0))
        {
            var statusBadge = f.Status switch
            {
                "success" => "<span class='badge badge-ok'>✓</span>",
                "warning" => "<span class='badge badge-warning'>⚠</span>",
                _ => "<span class='badge badge-action'>✗</span>"
            };
            sb.AppendLine($"<tr>");
            sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(f.Area)}</td>");
            sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(f.Feature)}</td>");
            sb.AppendLine($"<td>{statusBadge}</td>");
            sb.AppendLine($"<td style='font-size:8pt;color:#64748B'>{ReportHelpers.HtmlEncode(f.Observation ?? "—")}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static void RenderKpi(StringBuilder sb, string value, string label, string color)
    {
        sb.AppendLine($"<div style='text-align:center;padding:10px;border:1px solid #E2E8F0;border-top:3px solid {color};border-radius:6px'>");
        sb.AppendLine($"<div style='font-size:20pt;font-weight:800;color:{color}'>{value}</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#64748B'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }
}
