using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class RemediationStatusBlock : IFlowBlock
{
    private readonly bool _compact;
    public RemediationStatusBlock(bool compact = false) => _compact = compact;

    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Estado de Remediación" : "Remediation Status";

    public int EstimateHeight(ReportData data) => _compact ? 180 : 350;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        if (!data.HasRemediationData) return "";

        var es = options.IsSpanish;
        var tasks = data.RemediationTasks;
        var sb = new StringBuilder();

        var completed = tasks.Count(t => t.Status == "completed");
        var pending = tasks.Count(t => t.Status == "pending");
        var failed = tasks.Count(t => t.Status == "failed");
        var inProgress = tasks.Count(t => t.Status is "approved" or "executing");
        var rolled = tasks.Count(t => t.Status == "rolled_back");

        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(5,1fr);gap:8px;margin-bottom:12px'>");
        RenderKpi(sb, tasks.Count.ToString(), es ? "Total Tareas" : "Total Tasks", "#1e293b");
        RenderKpi(sb, completed.ToString(), es ? "Completadas" : "Completed", "#16a34a");
        RenderKpi(sb, pending.ToString(), es ? "Pendientes" : "Pending", pending > 0 ? "#d97706" : "#6b7280");
        RenderKpi(sb, failed.ToString(), es ? "Fallidas" : "Failed", failed > 0 ? "#dc2626" : "#16a34a");
        RenderKpi(sb, rolled.ToString(), es ? "Revertidas" : "Rolled Back", rolled > 0 ? "#ea580c" : "#6b7280");
        sb.AppendLine("</div>");

        // Progress bar
        if (tasks.Count > 0)
        {
            var pctComplete = completed * 100.0 / tasks.Count;
            sb.AppendLine("<div style='background:#e5e7eb;border-radius:4px;height:12px;margin-bottom:12px;overflow:hidden'>");
            sb.AppendLine($"<div style='background:#16a34a;height:100%;width:{pctComplete:F0}%;transition:width 0.3s'></div>");
            sb.AppendLine("</div>");
            sb.AppendLine($"<div style='text-align:center;font-size:8pt;color:#6b7280;margin-bottom:8px'>{pctComplete:F0}% {(es ? "completado" : "complete")}</div>");
        }

        // Task table
        var shown = tasks.Take(_compact ? 10 : 25).ToList();
        sb.AppendLine("<table style='width:100%;border-collapse:collapse;font-size:8pt'>");
        sb.AppendLine("<thead><tr style='background:#f1f5f9;text-align:left'>");
        sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Equipo" : "Machine")}</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Control" : "Control")}</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Acción" : "Action")}</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Estado" : "Status")}</th>");
        if (!_compact)
            sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Fecha" : "Date")}</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var t in shown)
        {
            var (statusColor, statusLabel) = t.Status switch
            {
                "completed" => ("#16a34a", es ? "OK" : "DONE"),
                "failed" => ("#dc2626", es ? "FALLO" : "FAIL"),
                "pending" => ("#d97706", es ? "PEND" : "PEND"),
                "rolled_back" => ("#ea580c", es ? "REV" : "REV"),
                _ => ("#3b82f6", t.Status.ToUpperInvariant())
            };
            sb.AppendLine("<tr style='border-bottom:1px solid #e5e7eb'>");
            sb.AppendLine($"<td style='padding:3px 6px;font-size:7pt'>{ReportHelpers.HtmlEncode(t.Machine?.Hostname ?? "—")}</td>");
            sb.AppendLine($"<td style='padding:3px 6px;font-family:monospace;font-size:7pt'>{ReportHelpers.HtmlEncode(t.ControlDef?.ControlId ?? "—")}</td>");
            sb.AppendLine($"<td style='padding:3px 6px;font-size:7pt'>{ReportHelpers.HtmlEncode(t.ActionType)}</td>");
            sb.AppendLine($"<td style='padding:3px 6px'><span style='background:{statusColor};color:#fff;padding:1px 6px;border-radius:3px;font-size:7pt'>{statusLabel}</span></td>");
            if (!_compact)
                sb.AppendLine($"<td style='padding:3px 6px;font-size:7pt'>{t.CompletedAt?.ToString("yyyy-MM-dd") ?? t.CreatedAt.ToString("yyyy-MM-dd")}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");

        if (tasks.Count > shown.Count)
        {
            var remaining = tasks.Count - shown.Count;
            sb.AppendLine($"<div style='text-align:center;font-size:7pt;color:#94a3b8;margin-top:4px'>{remaining} {(es ? "tareas adicionales no mostradas" : "additional tasks not shown")}</div>");
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
}
