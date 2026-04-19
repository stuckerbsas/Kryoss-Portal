using System.Text;

namespace KryossApi.Services.Reports.Blocks;

/// <summary>
/// Block 2: Business KPIs grid.
/// KPI 1: Cost of Exposure (IBM 2024 static benchmark).
/// KPI 2: Asset Coverage — the "4 Fantásticos" (BitLocker, TPM, LAPS, Defender).
/// KPI 3: Risk Evolution — delta vs previous month.
/// KPI 4 (conditional): Cloud Security Score — only when data.HasCloudData.
/// </summary>
public class KpiBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Indicadores Clave" : "Key Performance Indicators", data.Branding);
        sb.AppendLine("<div class='pb'>");

        var runs      = data.Runs;
        var avgScore  = data.AvgScore;
        var allResults = data.ControlResults;

        var criticalFails = allResults
            .Where(r => r.Status == "fail" && r.Severity == "critical")
            .Select(r => r.ControlId).Distinct().Count();
        var highFails = allResults
            .Where(r => r.Status == "fail" && r.Severity == "high")
            .Select(r => r.ControlId).Distinct().Count();
        var criticalVectorCount = criticalFails + highFails;

        // KPI 2: the "4 Fantásticos" coverage
        var bitlockerOk = runs.Count(r => r.Machine.Bitlocker == true);
        var tpmOk       = runs.Count(r => r.Machine.TpmPresent == true);
        var defenderOk  = runs.Count(r =>
            !allResults.Any(ar => ar.RunId == r.Id && ar.Status == "fail" &&
                (ar.Name.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
                 ar.Name.Contains("Antivirus", StringComparison.OrdinalIgnoreCase))));
        var lapsFailing = data.Hygiene?.Findings.Count(f => f.Status == "NoLAPS") ?? 0;
        var lapsTotal   = data.Hygiene?.TotalMachines ?? runs.Count;
        var lapsOk      = Math.Max(0, lapsTotal - lapsFailing);
        var lapsOkScaled = lapsTotal > 0 ? (int)Math.Round((double)lapsOk / lapsTotal * runs.Count) : runs.Count;
        var coverageAvg  = runs.Count > 0 ? (bitlockerOk + tpmOk + defenderOk + lapsOkScaled) / 4.0 : 0;
        var coveragePct  = runs.Count > 0 ? 100.0 * coverageAvg / runs.Count : 0;

        // KPI 3: risk evolution
        string evolArrow, evolColor, evolLabel;
        decimal evolDelta = 0;
        if (!data.PreviousMonthScore.HasValue)
        {
            evolArrow = "\u2014"; evolColor = "#64748B";
            evolLabel = es ? "Periodo de referencia" : "Baseline period";
        }
        else
        {
            evolDelta = Math.Round(avgScore - data.PreviousMonthScore.Value, 1);
            if (evolDelta > 0)      { evolArrow = "\u25B2"; evolColor = "#15803D"; }
            else if (evolDelta < 0) { evolArrow = "\u25BC"; evolColor = "#991B1B"; }
            else                    { evolArrow = "=";      evolColor = "#64748B"; }
            evolLabel = es ? $"vs mes anterior ({data.PreviousMonthScore.Value:0.#})"
                           : $"vs last month ({data.PreviousMonthScore.Value:0.#})";
        }

        // KPI 4: cloud score (conditional)
        decimal? cloudScore = null;
        if (data.HasCloudData && data.AreaScores != null && data.AreaScores.Count > 0)
            cloudScore = Math.Round(data.AreaScores.Values.Average(), 1);

        var cols = cloudScore.HasValue ? "1.3fr 1fr 1fr 1fr" : "1.3fr 1fr 1fr";
        sb.AppendLine($"<div style='display:grid;grid-template-columns:{cols};gap:12px;margin-bottom:20px'>");

        // KPI 1: exposure benchmark
        sb.AppendLine("<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid #991B1B;border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:#991B1B;letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "COSTO DE EXPOSICI\u00D3N" : "COST OF EXPOSURE")}</div>");
        sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:#991B1B;line-height:1'>USD 1.2M</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:4px;font-style:italic'>{ReportHelpers.HtmlEncode(es ? "IBM Cost of a Data Breach 2024 \u00B7 segmento PyME" : "IBM Cost of a Data Breach 2024 \u00B7 SMB segment")}</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:6px;line-height:1.4;border-top:1px solid #E2E8F0;padding-top:6px'>{ReportHelpers.HtmlEncode(es ? $"Su infraestructura actual presenta {criticalVectorCount} vectores cr\u00EDticos que coinciden con los casos de estudio de este benchmark." : $"Your infrastructure currently exhibits {criticalVectorCount} critical vectors matching this benchmark's case studies.")}</div>");
        sb.AppendLine("</div>");

        // KPI 2: asset coverage
        sb.AppendLine("<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid #0F172A;border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:#0F172A;letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "COBERTURA DE ACTIVOS" : "ASSET COVERAGE")}</div>");
        sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:#0F172A;line-height:1'>{coveragePct:0}%</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:6px'>{(int)Math.Round(coverageAvg)} {ReportHelpers.HtmlEncode(es ? "de" : "of")} {runs.Count} {ReportHelpers.HtmlEncode(es ? "equipos protegidos" : "machines protected")}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:4px'>BitLocker \u00B7 TPM \u00B7 LAPS \u00B7 Defender</div>");
        sb.AppendLine("</div>");

        // KPI 3: risk evolution
        sb.AppendLine($"<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid {evolColor};border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:{evolColor};letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "EVOLUCI\u00D3N" : "RISK EVOLUTION")}</div>");
        if (data.PreviousMonthScore.HasValue)
        {
            var sign = evolDelta >= 0 ? "+" : "";
            sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:{evolColor};line-height:1'>{evolArrow} {sign}{evolDelta:0.#}</div>");
            sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:4px'>{ReportHelpers.HtmlEncode(es ? "puntos" : "points")}</div>");
        }
        else
        {
            sb.AppendLine($"<div style='font-size:16pt;font-weight:900;color:#64748B;line-height:1'>BASELINE</div>");
        }
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:6px'>{ReportHelpers.HtmlEncode(evolLabel)}</div>");
        sb.AppendLine("</div>");

        // KPI 4: cloud score (only when data.HasCloudData)
        if (cloudScore.HasValue)
        {
            var cloudColor = cloudScore >= 85 ? "#15803D" : cloudScore >= 60 ? "#B45309" : "#991B1B";
            sb.AppendLine($"<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid {cloudColor};border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
            sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:{cloudColor};letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "NUBE / M365" : "CLOUD SECURITY")}</div>");
            sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:{cloudColor};line-height:1'>{cloudScore:0.#}%</div>");
            sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:6px'>{ReportHelpers.HtmlEncode(es ? $"{data.AreaScores!.Count} \u00E1reas evaluadas" : $"{data.AreaScores!.Count} areas assessed")}</div>");
            sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:4px'>{ReportHelpers.HtmlEncode(es ? "Score promedio Cloud Assessment" : "Cloud Assessment average score")}</div>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine("</div></div>");

        return sb.ToString();
    }
}
