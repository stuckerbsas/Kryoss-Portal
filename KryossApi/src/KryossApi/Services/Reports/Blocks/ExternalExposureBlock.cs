using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class ExternalExposureBlock : IFlowBlock
{
    private readonly bool _compact;
    public ExternalExposureBlock(bool compact = false) => _compact = compact;

    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Exposición Externa" : "External Exposure";

    public int EstimateHeight(ReportData data) => _compact ? 160 : 300;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        if (!data.HasExternalScanData) return "";

        var es = options.IsSpanish;
        var sb = new StringBuilder();
        var scan = data.LatestExternalScan!;
        var findings = scan.Findings;
        var results = scan.Results;

        var openPorts = results.Count(r => r.Status == "open");
        var critFindings = findings.Count(f => f.Severity == "critical");
        var highFindings = findings.Count(f => f.Severity == "high");
        var medFindings = findings.Count(f => f.Severity == "medium");

        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(5,1fr);gap:8px;margin-bottom:12px'>");
        RenderKpi(sb, scan.Target, es ? "IP/Rango Escaneado" : "Scanned Target", "#475569");
        RenderKpi(sb, openPorts.ToString(), es ? "Puertos Abiertos" : "Open Ports", openPorts > 5 ? "#ea580c" : "#16a34a");
        RenderKpi(sb, critFindings.ToString(), es ? "Críticos" : "Critical", critFindings > 0 ? "#dc2626" : "#16a34a");
        RenderKpi(sb, highFindings.ToString(), es ? "Altos" : "High", highFindings > 0 ? "#ea580c" : "#16a34a");
        RenderKpi(sb, medFindings.ToString(), es ? "Medios" : "Medium", medFindings > 0 ? "#d97706" : "#16a34a");
        sb.AppendLine("</div>");

        if (findings.Count > 0)
        {
            var topFindings = findings
                .OrderBy(f => f.Severity == "critical" ? 0 : f.Severity == "high" ? 1 : f.Severity == "medium" ? 2 : 3)
                .Take(_compact ? 5 : 15)
                .ToList();

            sb.AppendLine("<table style='width:100%;border-collapse:collapse;font-size:8pt'>");
            sb.AppendLine("<thead><tr style='background:#f1f5f9;text-align:left'>");
            sb.AppendLine($"<th style='padding:3px 6px'>{(es ? "Severidad" : "Severity")}</th>");
            sb.AppendLine($"<th style='padding:3px 6px'>{(es ? "Hallazgo" : "Finding")}</th>");
            if (!_compact)
            {
                sb.AppendLine($"<th style='padding:3px 6px'>{(es ? "Puerto" : "Port")}</th>");
                sb.AppendLine($"<th style='padding:3px 6px'>IP</th>");
            }
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var f in topFindings)
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
                sb.AppendLine($"<td style='padding:3px 6px'>{ReportHelpers.HtmlEncode(f.Title)}</td>");
                if (!_compact)
                {
                    sb.AppendLine($"<td style='padding:3px 6px;font-family:monospace'>{(f.Port.HasValue ? f.Port.Value.ToString() : "—")}</td>");
                    sb.AppendLine($"<td style='padding:3px 6px;font-family:monospace;font-size:7pt'>{ReportHelpers.HtmlEncode(f.PublicIp ?? "—")}</td>");
                }
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        if (!_compact && results.Count > 0)
        {
            var openResults = results.Where(r => r.Status == "open").Take(20).ToList();
            if (openResults.Count > 0)
            {
                sb.AppendLine($"<div style='font-size:8pt;font-weight:600;margin:10px 0 4px'>{(es ? "Puertos Abiertos Detectados" : "Detected Open Ports")}</div>");
                sb.AppendLine("<table style='width:100%;border-collapse:collapse;font-size:7pt'>");
                sb.AppendLine("<thead><tr style='background:#f8fafc;text-align:left'>");
                sb.AppendLine("<th style='padding:2px 6px'>IP</th><th style='padding:2px 6px'>Port</th>");
                sb.AppendLine($"<th style='padding:2px 6px'>{(es ? "Servicio" : "Service")}</th>");
                sb.AppendLine($"<th style='padding:2px 6px'>{(es ? "Riesgo" : "Risk")}</th>");
                sb.AppendLine("</tr></thead><tbody>");

                foreach (var r in openResults)
                {
                    var riskColor = r.Risk switch
                    {
                        "critical" => "#dc2626",
                        "high" => "#ea580c",
                        "medium" => "#d97706",
                        _ => "#6b7280"
                    };
                    sb.AppendLine("<tr style='border-bottom:1px solid #f1f5f9'>");
                    sb.AppendLine($"<td style='padding:2px 6px;font-family:monospace'>{ReportHelpers.HtmlEncode(r.IpAddress)}</td>");
                    sb.AppendLine($"<td style='padding:2px 6px;font-family:monospace'>{r.Port}/{r.Protocol}</td>");
                    sb.AppendLine($"<td style='padding:2px 6px'>{ReportHelpers.HtmlEncode(r.ServiceName ?? r.Service ?? "—")}</td>");
                    sb.AppendLine($"<td style='padding:2px 6px;color:{riskColor};font-weight:600'>{ReportHelpers.HtmlEncode(r.Risk ?? "—")}</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
        }

        if (scan.CompletedAt.HasValue)
        {
            var scanDate = scan.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm");
            sb.AppendLine($"<div style='text-align:right;font-size:7pt;color:#94a3b8;margin-top:4px'>{(es ? "Escaneo realizado" : "Scan completed")}: {scanDate} UTC</div>");
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
        sb.AppendLine($"<div style='font-size:14pt;font-weight:700;color:{color}'>{ReportHelpers.HtmlEncode(value)}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#6b7280'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }
}
