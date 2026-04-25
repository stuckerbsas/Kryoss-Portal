using System.Text;

namespace KryossApi.Services.Reports.Blocks;

/// <summary>
/// Renders the big risk score + frictionless audit banner used on the
/// Preventa Opener report (page 2). Shows score with aggressive risk framing,
/// and incorporates cloud gaps when cloud data is present.
/// </summary>
public class RiskScoreBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Vectores de Compromiso Cr\u00edtico" : "Critical Compromise Vectors";

    public int EstimateHeight(ReportData data) => data.HasCloudData ? 500 : 420;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var brand = data.Branding;

        var avgScore = (int)Math.Round(data.AvgScore);
        string verdict, verdictColor;
        if (avgScore < 50)      { verdict = es ? "CR\u00cdTICO" : "CRITICAL"; verdictColor = "#C0392B"; }
        else if (avgScore < 70) { verdict = es ? "ALTO"       : "HIGH";     verdictColor = "#D97706"; }
        else if (avgScore < 85) { verdict = es ? "MEDIO"      : "MEDIUM";   verdictColor = "#2563EB"; }
        else                    { verdict = es ? "BAJO"       : "LOW";      verdictColor = "#006536"; }

        var avgDurationSec = data.Runs.Count > 0 && data.Runs.Any(r => r.DurationMs > 0)
            ? Math.Max(1, (int)Math.Round(data.Runs.Where(r => r.DurationMs > 0).Average(r => (r.DurationMs ?? 0) / 1000.0)))
            : 0;

        // Big risk score
        sb.AppendLine($"<div class='big-number-box' style='border-color:{verdictColor};background:#fef2f2'>");
        sb.AppendLine($"<div style='font-size:10px;font-weight:700;letter-spacing:0.15em;text-transform:uppercase;color:#666;margin-bottom:8px'>{ReportHelpers.HtmlEncode(es ? "NIVEL DE MADUREZ EN CIBERSEGURIDAD" : "CYBERSECURITY MATURITY LEVEL")}</div>");
        sb.AppendLine($"<div class='big-number' style='color:{verdictColor}'>{avgScore}<span style='font-size:40px;color:#888'>/100</span></div>");
        sb.AppendLine($"<div style='display:inline-block;padding:6px 24px;background:{verdictColor};color:#fff;font-weight:900;font-size:13px;letter-spacing:0.12em;border-radius:4px;margin-top:12px'>{ReportHelpers.HtmlEncode(verdict)}</div>");

        // Cloud gap callout when cloud data is available
        if (data.HasCloudData && data.CloudScan != null)
        {
            var cloudScore = data.CloudScan.OverallScore;
            sb.AppendLine($"<div style='margin-top:16px;padding:10px 16px;background:#fff3cd;border:1px solid #f59e0b;border-radius:6px;font-size:12px;color:#92400e;text-align:left'>");
            sb.AppendLine($"<strong>&#9729; {ReportHelpers.HtmlEncode(es ? "Brecha en la Nube: " : "Cloud Security Gap: ")}</strong>");
            sb.AppendLine(es
                ? $"{ReportHelpers.HtmlEncode(data.Org.Name)} tambi\u00e9n tiene una puntuaci\u00f3n de seguridad cloud del <strong>{cloudScore:F0}%</strong>. Las vulnerabilidades en el endpoint y en la nube se combinan: una cuenta comprometida en M365 otorga acceso a todos los archivos, correos y aplicaciones corporativas."
                : $"{ReportHelpers.HtmlEncode(data.Org.Name)} also carries a cloud security score of <strong>{cloudScore:F0}%</strong>. Endpoint and cloud weaknesses compound each other: one compromised M365 account grants access to all corporate files, email and applications.");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");

        // Frictionless audit banner
        sb.AppendLine("<div class='insight-box' style='background:#f0fdf4;border-color:#006536'>");
        sb.AppendLine($"<p><strong style='color:#006536;font-size:14px'>&#9201; {ReportHelpers.HtmlEncode(es ? "Auditor\u00eda Sin Fricci\u00f3n" : "Frictionless Audit")}</strong></p>");

        var durationClause = avgDurationSec > 0
            ? (es
                ? $"Esta auditor\u00eda se realiz\u00f3 en un promedio de <strong>{avgDurationSec} segundos por equipo</strong>, "
                : $"This audit ran in an average of <strong>{avgDurationSec} seconds per machine</strong>, ")
            : (es ? "Esta auditor\u00eda se realiz\u00f3 " : "This audit ran ");

        sb.AppendLine(es
            ? $"<p style='margin-top:8px'>{durationClause}de forma <strong>pasiva</strong>, sin impacto en su operaci\u00f3n actual: <strong>cero reinicios</strong>, <strong>cero instalaci\u00f3n persistente</strong>, <strong>cero fricci\u00f3n para sus usuarios</strong>. El hallazgo de estos vectores cr\u00edticos no requiri\u00f3 pausar absolutamente nada.</p>"
            : $"<p style='margin-top:8px'>{durationClause}in a fully <strong>passive</strong> manner, with no impact on your current operations: <strong>zero reboots</strong>, <strong>zero persistent install</strong>, <strong>zero user friction</strong>. Surfacing these critical vectors did not require pausing anything.</p>");

        const string statStyle  = "min-width:78px;padding:10px 12px;flex:1";
        const string valueStyle = "font-size:18px;font-weight:800;line-height:1";
        const string labelStyle = "font-size:8px;margin-top:3px;line-height:1.2";
        sb.AppendLine("<div class='summary-grid' style='margin-top:12px;gap:8px'>");
        sb.AppendLine($"<div class='stat pass-stat' style='{statStyle}'><span class='stat-value' style='{valueStyle}'>{data.TotalMachines}</span><span class='stat-label' style='{labelStyle}'>{ReportHelpers.HtmlEncode(es ? "Equipos auditados" : "Machines audited")}</span></div>");
        if (avgDurationSec > 0)
            sb.AppendLine($"<div class='stat pass-stat' style='{statStyle}'><span class='stat-value' style='{valueStyle}'>{avgDurationSec}s</span><span class='stat-label' style='{labelStyle}'>{ReportHelpers.HtmlEncode(es ? "Promedio por equipo" : "Avg per machine")}</span></div>");
        sb.AppendLine($"<div class='stat pass-stat' style='{statStyle}'><span class='stat-value' style='{valueStyle}'>0</span><span class='stat-label' style='{labelStyle}'>{ReportHelpers.HtmlEncode(es ? "Reinicios" : "Reboots")}</span></div>");
        sb.AppendLine($"<div class='stat pass-stat' style='{statStyle}'><span class='stat-value' style='{valueStyle}'>0</span><span class='stat-label' style='{labelStyle}'>{ReportHelpers.HtmlEncode(es ? "Interrupciones" : "Disruptions")}</span></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");

        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb,
            SectionTitle(options)!,
            data.Branding,
            es ? "ANÁLISIS DE EXPOSICIÓN" : "EXPOSURE ANALYSIS");
        sb.AppendLine("<div class='pb'>");
        sb.Append(RenderContent(data, options));
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }
}
