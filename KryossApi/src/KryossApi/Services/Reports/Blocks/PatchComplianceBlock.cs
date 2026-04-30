using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class PatchComplianceBlock : IFlowBlock
{
    private readonly bool _compact;
    public PatchComplianceBlock(bool compact = false) => _compact = compact;

    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Estado de Parches" : "Patch Compliance";

    public int EstimateHeight(ReportData data) => _compact ? 160 : 300;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        if (!data.HasPatchData) return "";

        var es = options.IsSpanish;
        var sb = new StringBuilder();
        var statuses = data.PatchStatuses;

        var avgScore = statuses.Count > 0 ? (int)Math.Round(statuses.Average(s => s.ComplianceScore)) : 0;
        var rebootPending = statuses.Count(s => s.RebootPending);
        var wuStopped = statuses.Count(s => s.WuServiceStatus != null && s.WuServiceStatus != "Running");
        var unmanaged = statuses.Count(s => string.IsNullOrEmpty(s.UpdateSource) || s.UpdateSource == "Unknown");
        var reporting = statuses.Count;

        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(5,1fr);gap:8px;margin-bottom:12px'>");
        RenderKpi(sb, $"{avgScore}%", es ? "Cumplimiento Prom." : "Avg Compliance", ScoreColor(avgScore));
        RenderKpi(sb, reporting.ToString(), es ? "Equipos Reportando" : "Machines Reporting", "#475569");
        RenderKpi(sb, rebootPending.ToString(), es ? "Reinicio Pendiente" : "Reboot Pending", rebootPending > 0 ? "#ea580c" : "#16a34a");
        RenderKpi(sb, wuStopped.ToString(), es ? "WU Detenido" : "WU Stopped", wuStopped > 0 ? "#dc2626" : "#16a34a");
        RenderKpi(sb, unmanaged.ToString(), es ? "Sin Gestión" : "Unmanaged", unmanaged > 0 ? "#d97706" : "#16a34a");
        sb.AppendLine("</div>");

        if (_compact) return sb.ToString();

        var sourceGroups = statuses
            .GroupBy(s => s.UpdateSource ?? "Unknown")
            .OrderByDescending(g => g.Count())
            .ToList();

        sb.AppendLine($"<div style='font-size:8pt;font-weight:600;margin:8px 0 4px'>{(es ? "Distribución por Fuente" : "Update Source Distribution")}</div>");
        sb.AppendLine("<div style='display:flex;gap:6px;margin-bottom:10px'>");
        foreach (var g in sourceGroups)
        {
            var pct = (int)Math.Round(100.0 * g.Count() / statuses.Count);
            sb.AppendLine($"<div style='flex:1;background:#f8fafc;border:1px solid #e2e8f0;border-radius:4px;padding:6px;text-align:center'>");
            sb.AppendLine($"<div style='font-size:12pt;font-weight:700'>{g.Count()}</div>");
            sb.AppendLine($"<div style='font-size:7pt;color:#6b7280'>{ReportHelpers.HtmlEncode(g.Key)} ({pct}%)</div>");
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");

        var worst = statuses.Where(s => s.ComplianceScore < 70).Take(10).ToList();
        if (worst.Count > 0)
        {
            sb.AppendLine($"<div style='font-size:8pt;font-weight:600;margin:8px 0 4px'>{(es ? "Equipos con Bajo Cumplimiento (<70%)" : "Low Compliance Machines (<70%)")}</div>");
            sb.AppendLine("<table style='width:100%;border-collapse:collapse;font-size:8pt'>");
            sb.AppendLine("<thead><tr style='background:#fef2f2;text-align:left'>");
            sb.AppendLine($"<th style='padding:3px 6px'>{(es ? "Equipo" : "Machine")}</th>");
            sb.AppendLine($"<th style='padding:3px 6px'>{(es ? "Score" : "Score")}</th>");
            sb.AppendLine($"<th style='padding:3px 6px'>{(es ? "Fuente" : "Source")}</th>");
            sb.AppendLine($"<th style='padding:3px 6px'>{(es ? "Reinicio" : "Reboot")}</th>");
            sb.AppendLine($"<th style='padding:3px 6px'>{(es ? "Último Check" : "Last Check")}</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var s in worst)
            {
                sb.AppendLine("<tr style='border-bottom:1px solid #e5e7eb'>");
                sb.AppendLine($"<td style='padding:3px 6px'>{ReportHelpers.HtmlEncode(s.Machine?.Hostname ?? "—")}</td>");
                sb.AppendLine($"<td style='padding:3px 6px;font-weight:600;color:{ScoreColor(s.ComplianceScore)}'>{s.ComplianceScore}%</td>");
                sb.AppendLine($"<td style='padding:3px 6px;font-size:7pt'>{ReportHelpers.HtmlEncode(s.UpdateSource ?? "—")}</td>");
                sb.AppendLine($"<td style='padding:3px 6px'>{(s.RebootPending ? "⚠ Yes" : "No")}</td>");
                sb.AppendLine($"<td style='padding:3px 6px;font-size:7pt'>{(s.LastCheckUtc.HasValue ? s.LastCheckUtc.Value.ToString("yyyy-MM-dd") : "—")}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
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

    private static void RenderKpi(StringBuilder sb, string value, string label, string color)
    {
        sb.AppendLine($"<div style='border:1px solid #e5e7eb;border-radius:6px;padding:8px;text-align:center'>");
        sb.AppendLine($"<div style='font-size:16pt;font-weight:700;color:{color}'>{value}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#6b7280'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }

    private static string ScoreColor(int score) => score switch
    {
        >= 90 => "#16a34a",
        >= 70 => "#d97706",
        _ => "#dc2626"
    };
}
