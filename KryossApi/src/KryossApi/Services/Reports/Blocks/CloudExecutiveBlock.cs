using System.Text;
using KryossApi.Data.Entities;

namespace KryossApi.Services.Reports.Blocks;

public class CloudExecutiveBlock : IReportBlock
{
    private static readonly Dictionary<string, decimal> HoursByPriority = new()
    {
        ["critical"] = 4m,
        ["high"] = 2m,
        ["medium"] = 1m,
        ["low"] = 0.5m,
    };

    public string Render(ReportData data, ReportOptions options)
    {
        if (!data.HasCloudData || data.CloudFindings == null || data.CloudFindings.Count == 0)
        {
            var sp = options.IsSpanish;
            var noData = new StringBuilder();
            var budget0 = new PageBudget();
            budget0.StartPage(noData, sp ? "Informe Ejecutivo Cloud" : "Cloud Executive Summary", data.Branding);
            noData.AppendLine("<div style='text-align:center;padding:60px 20px'>");
            noData.AppendLine($"<div style='font-size:14pt;font-weight:700;color:#64748b;margin-bottom:12px'>{(sp ? "Sin datos de Cloud Assessment" : "No Cloud Assessment Data")}</div>");
            noData.AppendLine($"<div style='font-size:10pt;color:#94a3b8'>{(sp ? "Conecte M365/Azure desde el portal para generar este informe." : "Connect M365/Azure from the portal to generate this report.")}</div>");
            noData.AppendLine("</div>");
            budget0.EndPage(noData);
            return noData.ToString();
        }

        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var findings = data.CloudFindings;
        var budget = new PageBudget();

        // --- Page 1: Summary + Radar + KPI ---
        budget.StartPage(sb, es ? "Informe Ejecutivo Cloud" : "Cloud Executive Summary", data.Branding);

        if (data.CloudScan?.OverallScore != null)
        {
            var score = data.CloudScan.OverallScore.Value;
            var color = score >= 80 ? "#16a34a" : score >= 60 ? "#eab308" : "#ef4444";
            sb.AppendLine($"<div style='text-align:center;margin:16px 0'>");
            sb.AppendLine($"<div style='font-size:11pt;color:#6b7280'>{(es ? "Puntuación General Cloud" : "Overall Cloud Score")}</div>");
            sb.AppendLine($"<div style='font-size:36pt;font-weight:900;color:{color}'>{score:F0}</div>");
            sb.AppendLine("</div>");
            budget.Spend(80);
        }

        if (data.AreaScores != null && data.AreaScores.Count > 0)
        {
            CloudPostureBlock.RenderRadarChartStatic(sb, data.AreaScores, es);
            budget.Spend(PageBudget.RadarChart);
        }

        // KPI row
        var critical = findings.Count(f => f.Priority == "critical");
        var high = findings.Count(f => f.Priority == "high");
        var medium = findings.Count(f => f.Priority == "medium");
        var low = findings.Count(f => f.Priority is "low" or "");
        var actionRequired = findings.Count(f => f.Status == "action_required");

        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(5,1fr);gap:8px;margin:16px 0'>");
        RenderKpiCard(sb, es ? "Críticos" : "Critical", critical, "#ef4444");
        RenderKpiCard(sb, es ? "Altos" : "High", high, "#f97316");
        RenderKpiCard(sb, es ? "Medios" : "Medium", medium, "#eab308");
        RenderKpiCard(sb, es ? "Bajos" : "Low", low, "#22c55e");
        RenderKpiCard(sb, es ? "Acción Req." : "Action Req.", actionRequired, "#7c3aed");
        sb.AppendLine("</div>");
        budget.Spend(PageBudget.ScoreCards);

        // Hour estimate
        decimal totalHours = findings.Sum(f => HoursByPriority.GetValueOrDefault(f.Priority, 0.5m));
        sb.AppendLine("<div style='background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:16px;margin:16px 0;text-align:center'>");
        sb.AppendLine($"<div style='font-size:10pt;color:#64748b'>{(es ? "Horas Estimadas de Remediación Total" : "Total Estimated Remediation Hours")}</div>");
        sb.AppendLine($"<div style='font-size:28pt;font-weight:900;color:#1e293b'>{totalHours:F1}h</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#94a3b8'>{(es ? "Basado en complejidad por prioridad" : "Based on complexity by priority")}</div>");
        sb.AppendLine("</div>");
        budget.Spend(80);

        budget.EndPage(sb);

        // --- Pages 2+: Findings by area with PageBudget ---
        foreach (var area in new[] { "identity", "endpoint", "data", "productivity", "azure" })
        {
            var areaFindings = findings.Where(f => f.Area == area).ToList();
            if (areaFindings.Count == 0) continue;

            var pageTitle = AreaLabel(area, es);
            budget.StartPage(sb, pageTitle, data.Branding);

            if (data.AreaScores != null && data.AreaScores.TryGetValue(area, out var areaScore))
            {
                var c = areaScore >= 80 ? "#16a34a" : areaScore >= 60 ? "#eab308" : "#ef4444";
                sb.AppendLine($"<div style='display:flex;align-items:center;gap:12px;margin-bottom:12px'>");
                sb.AppendLine($"<div style='font-size:24pt;font-weight:800;color:{c}'>{areaScore:F0}</div>");
                sb.AppendLine($"<div style='font-size:10pt;color:#6b7280'>/ 100</div>");
                sb.AppendLine("</div>");
                budget.Spend(40);
            }

            decimal areaHours = 0;
            sb.AppendLine("<table class='data-table'><thead><tr>");
            sb.AppendLine($"<th>{(es ? "Servicio" : "Service")}</th>");
            sb.AppendLine($"<th>{(es ? "Hallazgo" : "Finding")}</th>");
            sb.AppendLine($"<th>{(es ? "Prioridad" : "Priority")}</th>");
            sb.AppendLine($"<th>{(es ? "Estado" : "Status")}</th>");
            sb.AppendLine($"<th>{(es ? "Horas Est." : "Est. Hours")}</th>");
            sb.AppendLine("</tr></thead><tbody>");
            budget.Spend(PageBudget.TableHeader);

            foreach (var f in areaFindings.OrderByDescending(f => PriorityRank(f.Priority)))
            {
                var obsLines = PageBudget.LineCount(f.Observation);
                var rowHeight = obsLines > 1 ? 8 + obsLines * PageBudget.LinePx9pt : PageBudget.TableRow;
                if (budget.WouldOverflow(rowHeight))
                {
                    sb.AppendLine("</tbody></table>");
                    budget.NewPage(sb, pageTitle, data.Branding);
                    sb.AppendLine("<table class='data-table'><thead><tr>");
                    sb.AppendLine($"<th>{(es ? "Servicio" : "Service")}</th><th>{(es ? "Hallazgo" : "Finding")}</th><th>{(es ? "Prioridad" : "Priority")}</th><th>{(es ? "Estado" : "Status")}</th><th>{(es ? "Horas Est." : "Est. Hours")}</th>");
                    sb.AppendLine("</tr></thead><tbody>");
                    budget.Spend(PageBudget.TableHeader);
                }

                var hours = HoursByPriority.GetValueOrDefault(f.Priority, 0.5m);
                areaHours += hours;
                var rowClass = f.Priority is "critical" ? "fail" : f.Priority is "high" ? "warn" : "";

                sb.AppendLine($"<tr class='{rowClass}'>");
                sb.AppendLine($"<td><strong>{ReportHelpers.HtmlEncode(f.Feature)}</strong></td>");
                sb.AppendLine($"<td style='font-size:9pt'>{PageBudget.WordWrap(ReportHelpers.HtmlEncode(f.Observation ?? "—"))}</td>");
                sb.AppendLine($"<td><span class='badge {PriorityBadge(f.Priority)}'>{ReportHelpers.HtmlEncode(PriorityLabel(f.Priority, es))}</span></td>");
                sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(StatusLabel(f.Status, es))}</td>");
                sb.AppendLine($"<td>{hours:F1}h</td>");
                sb.AppendLine("</tr>");
                budget.Spend(rowHeight);
            }

            if (budget.WouldOverflow(PageBudget.TableRow))
            {
                sb.AppendLine("</tbody></table>");
                budget.NewPage(sb, pageTitle, data.Branding);
                sb.AppendLine("<table class='data-table'><tbody>");
            }
            sb.AppendLine($"<tr style='font-weight:700;border-top:2px solid #1E293B'>");
            sb.AppendLine($"<td colspan='4'><strong>{(es ? "Subtotal" : "Subtotal")} — {ReportHelpers.HtmlEncode(AreaLabel(area, es))}</strong></td>");
            sb.AppendLine($"<td>{areaHours:F1}h</td>");
            sb.AppendLine("</tr>");
            sb.AppendLine("</tbody></table>");

            budget.EndPage(sb);
        }

        // --- Final page: Hour breakdown ---
        budget.StartPage(sb, es ? "Resumen de Esfuerzo" : "Remediation Effort Summary", data.Branding);

        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Área" : "Area")}</th><th>{(es ? "Hallazgos" : "Findings")}</th><th>{(es ? "Críticos" : "Critical")}</th><th>{(es ? "Altos" : "High")}</th><th>{(es ? "Medios" : "Medium")}</th><th>{(es ? "Bajos" : "Low")}</th><th>{(es ? "Horas Est." : "Est. Hours")}</th>");
        sb.AppendLine("</tr></thead><tbody>");
        budget.Spend(PageBudget.TableHeader);

        decimal grandTotal = 0;
        foreach (var area in new[] { "identity", "endpoint", "data", "productivity", "azure" })
        {
            var af = findings.Where(f => f.Area == area).ToList();
            if (af.Count == 0) continue;
            var crit = af.Count(f => f.Priority == "critical");
            var hi = af.Count(f => f.Priority == "high");
            var med = af.Count(f => f.Priority == "medium");
            var lo = af.Count(f => f.Priority is "low" or "");
            var hrs = af.Sum(f => HoursByPriority.GetValueOrDefault(f.Priority, 0.5m));
            grandTotal += hrs;

            sb.AppendLine($"<tr><td><strong>{ReportHelpers.HtmlEncode(AreaLabel(area, es))}</strong></td>");
            sb.AppendLine($"<td>{af.Count}</td>");
            sb.AppendLine($"<td style='color:#ef4444;font-weight:600'>{(crit > 0 ? crit.ToString() : "—")}</td>");
            sb.AppendLine($"<td style='color:#f97316;font-weight:600'>{(hi > 0 ? hi.ToString() : "—")}</td>");
            sb.AppendLine($"<td style='color:#eab308'>{(med > 0 ? med.ToString() : "—")}</td>");
            sb.AppendLine($"<td style='color:#22c55e'>{(lo > 0 ? lo.ToString() : "—")}</td>");
            sb.AppendLine($"<td>{hrs:F1}h</td></tr>");
            budget.Spend(PageBudget.TableRow);
        }

        sb.AppendLine($"<tr style='font-weight:700;border-top:2px solid #1E293B;font-size:11pt'>");
        sb.AppendLine($"<td>TOTAL</td><td>{findings.Count}</td><td style='color:#ef4444'>{critical}</td><td style='color:#f97316'>{high}</td><td style='color:#eab308'>{medium}</td><td style='color:#22c55e'>{low}</td><td style='font-size:12pt'>{grandTotal:F1}h</td>");
        sb.AppendLine("</tr></tbody></table>");

        sb.AppendLine("<div style='margin-top:20px;padding:12px;background:#f1f5f9;border-radius:6px;font-size:8pt;color:#64748b'>");
        sb.AppendLine($"<strong>{(es ? "Metodología de estimación:" : "Estimation methodology:")}</strong><br>");
        sb.AppendLine(es
            ? "Crítico = 4h · Alto = 2h · Medio = 1h · Bajo = 0.5h por hallazgo. Las horas reales pueden variar según la complejidad del entorno."
            : "Critical = 4h · High = 2h · Medium = 1h · Low = 0.5h per finding. Actual hours may vary based on environment complexity.");
        sb.AppendLine("</div>");

        budget.EndPage(sb);
        return sb.ToString();
    }

    private static void RenderKpiCard(StringBuilder sb, string label, int value, string color)
    {
        sb.AppendLine("<div style='border:1px solid #e5e7eb;border-radius:8px;padding:10px;text-align:center'>");
        sb.AppendLine($"<div style='font-size:20pt;font-weight:700;color:{color}'>{value}</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#6b7280'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }

    private static int PriorityRank(string? p) => p switch { "critical" => 4, "high" => 3, "medium" => 2, _ => 1 };
    private static string AreaLabel(string area, bool es) => area switch
    {
        "identity" => es ? "Identidad y Acceso" : "Identity & Access",
        "endpoint" => es ? "Dispositivos" : "Endpoint Protection",
        "data" => es ? "Protección de Datos" : "Data Protection",
        "productivity" => es ? "Productividad" : "Productivity & Collaboration",
        "azure" => "Azure Infrastructure",
        _ => area
    };
    private static string PriorityBadge(string? p) => p switch { "critical" or "high" => "badge-action", "medium" => "badge-warning", _ => "badge-muted" };
    private static string PriorityLabel(string? p, bool es) => p switch { "critical" => es ? "Crítico" : "Critical", "high" => es ? "Alto" : "High", "medium" => es ? "Medio" : "Medium", "low" => es ? "Bajo" : "Low", _ => p ?? "" };
    private static string StatusLabel(string? s, bool es) => s switch { "action_required" => es ? "Acción Requerida" : "Action Required", "warning" => es ? "Advertencia" : "Warning", "ok" or "good" => es ? "Correcto" : "OK", _ => s ?? "" };
}
