using System.Text;
using KryossApi.Services;

namespace KryossApi.Services.Reports.Blocks;

/// <summary>
/// Block 1: Risk Posture semáforo. Forces RED on 4 capital sins regardless of score.
/// Score-based fallback: ≥85 GREEN, 60-84 YELLOW, <60 RED.
/// </summary>
public class SemaforoBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Postura de Riesgo" : "Risk Posture", data.Branding);
        sb.AppendLine("<div class='pb'>");

        var capitalSin = CapitalSinDetector.Detect(
            data.Runs, data.Hygiene, data.Enrichment,
            data.M365Connected, data.M365Findings, options.Lang);

        var avgScore = data.AvgScore;

        string postureColor, postureBg, postureLabel, postureNarrative;
        if (capitalSin != null)
        {
            postureColor = "#991B1B"; postureBg = "#FEF2F2";
            postureLabel = es ? "CR\u00CDTICO" : "CRITICAL";
            postureNarrative = capitalSin.Narrative;
        }
        else if (avgScore >= 85)
        {
            postureColor = "#15803D"; postureBg = "#F0FDF4";
            postureLabel = es ? "POSTURA S\u00D3LIDA" : "SOLID POSTURE";
            postureNarrative = es
                ? "Controles activos. Postura s\u00F3lida frente a los patrones de ataque monitoreados."
                : "Controls active. Solid posture against monitored attack patterns.";
        }
        else if (avgScore >= 60)
        {
            postureColor = "#B45309"; postureBg = "#FFFBEB";
            postureLabel = es ? "EXPOSICI\u00D3N ALTA" : "HIGH EXPOSURE";
            postureNarrative = es
                ? "Expuestos a ransomware por deuda t\u00E9cnica. Recuperaci\u00F3n garantizada pero lenta."
                : "Exposed to ransomware via technical debt. Recovery guaranteed but slow.";
        }
        else
        {
            postureColor = "#991B1B"; postureBg = "#FEF2F2";
            postureLabel = es ? "CR\u00CDTICO" : "CRITICAL";
            postureNarrative = es
                ? "Operaci\u00F3n en riesgo inminente. Tiempo estimado de recuperaci\u00F3n ante ataque: >48h."
                : "Operation at imminent risk. Estimated recovery time from attack: >48h.";
        }

        sb.AppendLine($"<div style='background:{postureBg};border:2px solid {postureColor};border-radius:8px;padding:22px 28px;margin-bottom:20px;text-align:center;box-shadow:0 4px 6px -1px rgba(15,23,42,0.06)'>");
        sb.AppendLine($"<div style='font-size:11px;font-weight:800;letter-spacing:0.14em;color:{postureColor};margin-bottom:8px'>{ReportHelpers.HtmlEncode(es ? "POSTURA DE RIESGO" : "RISK POSTURE")}</div>");
        sb.AppendLine($"<div style='font-size:28px;font-weight:900;color:{postureColor};line-height:1;margin-bottom:6px'>{ReportHelpers.HtmlEncode(postureLabel)}</div>");
        sb.AppendLine($"<div style='font-size:12px;color:#334155;line-height:1.55;max-width:160mm;margin:0 auto'>{ReportHelpers.HtmlEncode(postureNarrative)}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div></div>");

        return sb.ToString();
    }
}
