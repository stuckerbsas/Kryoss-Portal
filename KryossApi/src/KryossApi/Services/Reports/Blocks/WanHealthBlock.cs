using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class WanHealthBlock : IFlowBlock
{
    private readonly bool _compact;
    public WanHealthBlock(bool compact = false) => _compact = compact;

    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Salud WAN y Conectividad" : "WAN & Connectivity Health";

    public int EstimateHeight(ReportData data) => _compact ? 200 : 400;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        if (!data.HasWanData) return "";

        var es = options.IsSpanish;
        var sites = data.NetworkSites;
        var findings = data.WanFindings;
        var sb = new StringBuilder();

        var avgWan = sites.Where(s => s.WanScore.HasValue).Select(s => s.WanScore!.Value).DefaultIfEmpty(0).Average();
        var critical = findings.Count(f => f.Severity == "critical");
        var high = findings.Count(f => f.Severity == "high");
        var totalDevices = sites.Sum(s => s.AgentCount + s.DeviceCount);

        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(5,1fr);gap:8px;margin-bottom:12px'>");
        RenderKpi(sb, sites.Count.ToString(), es ? "Sitios" : "Sites", "#1e293b");
        RenderKpi(sb, totalDevices.ToString(), es ? "Dispositivos" : "Devices", "#475569");
        RenderKpi(sb, $"{avgWan:F0}", es ? "Score WAN Prom." : "Avg WAN Score",
            avgWan >= 80 ? "#16a34a" : avgWan >= 60 ? "#d97706" : "#dc2626");
        RenderKpi(sb, critical.ToString(), es ? "Críticos" : "Critical", critical > 0 ? "#dc2626" : "#16a34a");
        RenderKpi(sb, high.ToString(), es ? "Altos" : "High", high > 0 ? "#ea580c" : "#16a34a");
        sb.AppendLine("</div>");

        // Site table
        sb.AppendLine("<table style='width:100%;border-collapse:collapse;font-size:8pt'>");
        sb.AppendLine("<thead><tr style='background:#f1f5f9;text-align:left'>");
        sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Sitio" : "Site")}</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>IP</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>ISP</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>↓ Mbps</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>↑ Mbps</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Latencia" : "Latency")}</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Jitter" : "Jitter")}</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Pérdida" : "Loss")}</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Score" : "Score")}</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var s in sites.Take(_compact ? 5 : 20))
        {
            var scoreColor = (s.WanScore ?? 0) >= 80 ? "#16a34a" : (s.WanScore ?? 0) >= 60 ? "#d97706" : "#dc2626";
            sb.AppendLine("<tr style='border-bottom:1px solid #e5e7eb'>");
            sb.AppendLine($"<td style='padding:3px 6px;font-weight:600'>{ReportHelpers.HtmlEncode(s.SiteName)}</td>");
            sb.AppendLine($"<td style='padding:3px 6px;font-family:monospace;font-size:7pt'>{ReportHelpers.HtmlEncode(s.PublicIp ?? "—")}</td>");
            sb.AppendLine($"<td style='padding:3px 6px;font-size:7pt'>{ReportHelpers.HtmlEncode(s.Isp ?? "—")}</td>");
            sb.AppendLine($"<td style='padding:3px 6px'>{s.AvgDownMbps?.ToString("F1") ?? "—"}</td>");
            sb.AppendLine($"<td style='padding:3px 6px'>{s.AvgUpMbps?.ToString("F1") ?? "—"}</td>");
            sb.AppendLine($"<td style='padding:3px 6px'>{s.AvgLatencyMs?.ToString("F0") ?? "—"} ms</td>");
            sb.AppendLine($"<td style='padding:3px 6px'>{s.AvgJitterMs?.ToString("F1") ?? "—"} ms</td>");
            sb.AppendLine($"<td style='padding:3px 6px'>{s.AvgPacketLossPct?.ToString("F1") ?? "—"}%</td>");
            sb.AppendLine($"<td style='padding:3px 6px'><span style='background:{scoreColor};color:#fff;padding:1px 6px;border-radius:3px;font-size:7pt;font-weight:700'>{s.WanScore?.ToString("F0") ?? "—"}</span></td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");

        if (_compact) return sb.ToString();

        // WAN findings
        if (findings.Count > 0)
        {
            sb.AppendLine($"<div style='font-size:9pt;font-weight:600;margin:12px 0 4px'>{(es ? "Hallazgos WAN" : "WAN Findings")}</div>");
            sb.AppendLine("<table style='width:100%;border-collapse:collapse;font-size:8pt'>");
            sb.AppendLine("<thead><tr style='background:#f1f5f9;text-align:left'>");
            sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Severidad" : "Severity")}</th>");
            sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Categoría" : "Category")}</th>");
            sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Hallazgo" : "Finding")}</th>");
            sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Detalle" : "Detail")}</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var f in findings.Take(15))
            {
                var sevColor = f.Severity switch
                {
                    "critical" => "#dc2626",
                    "high" => "#ea580c",
                    "medium" => "#d97706",
                    _ => "#6b7280"
                };
                sb.AppendLine("<tr style='border-bottom:1px solid #e5e7eb'>");
                sb.AppendLine($"<td style='padding:3px 6px'><span style='background:{sevColor};color:#fff;padding:1px 6px;border-radius:3px;font-size:7pt'>{ReportHelpers.HtmlEncode(f.Severity.ToUpperInvariant())}</span></td>");
                sb.AppendLine($"<td style='padding:3px 6px'>{ReportHelpers.HtmlEncode(f.Category)}</td>");
                sb.AppendLine($"<td style='padding:3px 6px'>{ReportHelpers.HtmlEncode(f.Title)}</td>");
                sb.AppendLine($"<td style='padding:3px 6px;font-size:7pt'>{ReportHelpers.HtmlEncode(f.Detail ?? "—")}</td>");
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
}
