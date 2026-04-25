using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class NetworkMiniBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Red — Vista Ejecutiva" : "Network — Executive View";

    public int EstimateHeight(ReportData data) => 160;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        if (!data.HasNetworkData) return "";

        var es = options.IsSpanish;
        var sb = new StringBuilder();
        var diags = data.NetworkDiags;

        sb.AppendLine("<div class='network-mini-split'>");

        sb.AppendLine("<div class='network-mini-map'>");
        sb.AppendLine($"<span>{(es ? "Mapa de sitios (próximamente)" : "Site map (coming soon)")}</span>");
        sb.AppendLine("</div>");

        var avgDown = diags.Average(d => d.DownloadMbps ?? 0);
        var avgLat = diags.Average(d => d.InternetLatencyMs ?? 0);
        var vpnCount = diags.Count(d => d.VpnDetected);
        var issues = diags.Count(d => (d.InternetLatencyMs ?? 0) > 100 || (d.DownloadMbps ?? 0) < 10);

        sb.AppendLine("<div class='network-mini-kpis'>");
        RenderMiniKpi(sb, diags.Count.ToString(), es ? "Dispositivos" : "Devices", "#1e293b");
        RenderMiniKpi(sb, $"{avgDown:F0} Mbps", es ? "Descarga Prom." : "Avg Download", avgDown >= 50 ? "#16a34a" : "#eab308");
        RenderMiniKpi(sb, $"{avgLat:F0} ms", es ? "Latencia Prom." : "Avg Latency", avgLat <= 50 ? "#16a34a" : "#eab308");
        RenderMiniKpi(sb, issues.ToString(), es ? "Alertas de Red" : "Network Alerts", issues > 0 ? "#ef4444" : "#16a34a");
        sb.AppendLine("</div>");

        sb.AppendLine("</div>");
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

    private static void RenderMiniKpi(StringBuilder sb, string value, string label, string color)
    {
        sb.AppendLine($"<div class='stat' style='border:1px solid #e5e7eb;border-radius:6px;padding:10px;text-align:center'>");
        sb.AppendLine($"<div style='font-size:18pt;font-weight:700;color:{color}'>{value}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#6b7280'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }
}
