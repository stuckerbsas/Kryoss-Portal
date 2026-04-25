using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class KpiBlock : IFlowBlock
{
    private readonly KpiVariant _variant;
    public KpiBlock(KpiVariant variant = KpiVariant.Exec) => _variant = variant;

    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Indicadores Clave" : "Key Performance Indicators";

    public int EstimateHeight(ReportData data) => _variant == KpiVariant.Compact ? 100 : 200;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        var runs = data.Runs;
        var avgScore = data.AvgScore;
        var allResults = data.ControlResults;

        var criticalFails = allResults
            .Where(r => r.Status == "fail" && r.Severity == "critical")
            .Select(r => r.ControlId).Distinct().Count();
        var highFails = allResults
            .Where(r => r.Status == "fail" && r.Severity == "high")
            .Select(r => r.ControlId).Distinct().Count();
        var criticalVectorCount = criticalFails + highFails;

        var bitlockerOk = runs.Count(r => r.Machine.Bitlocker == true);
        var tpmOk = runs.Count(r => r.Machine.TpmPresent == true);
        var defenderOk = runs.Count(r =>
            !allResults.Any(ar => ar.RunId == r.Id && ar.Status == "fail" &&
                (ar.Name.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
                 ar.Name.Contains("Antivirus", StringComparison.OrdinalIgnoreCase))));
        var lapsFailing = data.Hygiene?.Findings.Count(f => f.Status == "NoLAPS") ?? 0;
        var lapsTotal = data.Hygiene?.TotalMachines ?? runs.Count;
        var lapsOk = Math.Max(0, lapsTotal - lapsFailing);
        var lapsOkScaled = lapsTotal > 0 ? (int)Math.Round((double)lapsOk / lapsTotal * runs.Count) : runs.Count;
        var coverageAvg = runs.Count > 0 ? (bitlockerOk + tpmOk + defenderOk + lapsOkScaled) / 4.0 : 0;
        var coveragePct = runs.Count > 0 ? 100.0 * coverageAvg / runs.Count : 0;

        string evolArrow, evolColor, evolLabel;
        decimal evolDelta = 0;
        if (!data.PreviousMonthScore.HasValue)
        {
            evolArrow = "—"; evolColor = "#64748B";
            evolLabel = es ? "Periodo de referencia" : "Baseline period";
        }
        else
        {
            evolDelta = Math.Round(avgScore - data.PreviousMonthScore.Value, 1);
            if (evolDelta > 0) { evolArrow = "▲"; evolColor = "#15803D"; }
            else if (evolDelta < 0) { evolArrow = "▼"; evolColor = "#991B1B"; }
            else { evolArrow = "="; evolColor = "#64748B"; }
            evolLabel = es ? $"vs mes anterior ({data.PreviousMonthScore.Value:0.#})"
                           : $"vs last month ({data.PreviousMonthScore.Value:0.#})";
        }

        decimal? cloudScore = null;
        if (data.HasCloudData && data.AreaScores != null && data.AreaScores.Count > 0)
            cloudScore = Math.Round(data.AreaScores.Values.Average(), 1);

        switch (_variant)
        {
            case KpiVariant.Compact:
                RenderCompact(sb, es, runs.Count, criticalVectorCount, coveragePct, evolArrow, evolColor,
                    evolDelta, evolLabel, cloudScore, data);
                break;
            case KpiVariant.Business:
                RenderBusiness(sb, es, runs.Count, criticalVectorCount, coveragePct, coverageAvg,
                    evolArrow, evolColor, evolDelta, evolLabel, cloudScore, data);
                break;
            default:
                RenderExec(sb, es, runs.Count, criticalVectorCount, coveragePct, coverageAvg,
                    evolArrow, evolColor, evolDelta, evolLabel, cloudScore, data);
                break;
        }

        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, SectionTitle(options)!, data.Branding);
        sb.AppendLine("<div class='pb'>");
        sb.Append(RenderContent(data, options));
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static void RenderExec(StringBuilder sb, bool es, int runCount, int criticalVectorCount,
        double coveragePct, double coverageAvg, string evolArrow, string evolColor,
        decimal evolDelta, string evolLabel, decimal? cloudScore, ReportData data)
    {
        var cols = cloudScore.HasValue ? "1.3fr 1fr 1fr 1fr" : "1.3fr 1fr 1fr";
        sb.AppendLine($"<div style='display:grid;grid-template-columns:{cols};gap:12px;margin-bottom:20px'>");
        RenderExposureCard(sb, es, criticalVectorCount);
        RenderCoverageCard(sb, es, coveragePct, coverageAvg, runCount);
        RenderEvolutionCard(sb, es, evolArrow, evolColor, evolDelta, evolLabel, data.PreviousMonthScore.HasValue);
        if (cloudScore.HasValue)
            RenderCloudCard(sb, es, cloudScore.Value, data.AreaScores!.Count);
        sb.AppendLine("</div>");
    }

    private static void RenderBusiness(StringBuilder sb, bool es, int runCount, int criticalVectorCount,
        double coveragePct, double coverageAvg, string evolArrow, string evolColor,
        decimal evolDelta, string evolLabel, decimal? cloudScore, ReportData data)
    {
        var cols = cloudScore.HasValue ? "1.3fr 1fr 1fr 1fr" : "1.3fr 1fr 1fr";
        sb.AppendLine($"<div style='display:grid;grid-template-columns:{cols};gap:12px;margin-bottom:20px'>");
        sb.AppendLine("<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid #991B1B;border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:#991B1B;letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "USUARIOS EN RIESGO" : "USERS AT RISK")}</div>");
        sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:#991B1B;line-height:1'>{criticalVectorCount}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:4px'>{ReportHelpers.HtmlEncode(es ? "Vectores críticos y altos activos" : "Active critical & high vectors")}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid #0F172A;border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:#0F172A;letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "ACTIVOS PROTEGIDOS" : "PROTECTED ASSETS")}</div>");
        sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:#0F172A;line-height:1'>{coveragePct:0}%</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:6px'>{(int)Math.Round(coverageAvg)} / {runCount}</div>");
        sb.AppendLine("</div>");
        RenderEvolutionCard(sb, es, evolArrow, evolColor, evolDelta, evolLabel, data.PreviousMonthScore.HasValue);
        if (cloudScore.HasValue)
            RenderCloudCard(sb, es, cloudScore.Value, data.AreaScores!.Count);
        sb.AppendLine("</div>");
    }

    private static void RenderCompact(StringBuilder sb, bool es, int runCount, int criticalVectorCount,
        double coveragePct, string evolArrow, string evolColor, decimal evolDelta,
        string evolLabel, decimal? cloudScore, ReportData data)
    {
        var items = new List<(string Label, string Value, string Color)>
        {
            (es ? "Vectores Críticos" : "Critical Vectors", criticalVectorCount.ToString(), "#991B1B"),
            (es ? "Cobertura" : "Coverage", $"{coveragePct:0}%", "#0F172A"),
            (es ? "Evolución" : "Evolution",
                data.PreviousMonthScore.HasValue ? $"{evolArrow}{evolDelta:+0.#;-0.#;0}" : "BASELINE",
                evolColor),
        };
        if (cloudScore.HasValue)
            items.Add((es ? "Cloud Score" : "Cloud Score", $"{cloudScore:0.#}%",
                cloudScore >= 85 ? "#15803D" : cloudScore >= 60 ? "#B45309" : "#991B1B"));

        var colCount = Math.Min(items.Count, 6);
        sb.AppendLine($"<div style='display:grid;grid-template-columns:repeat({colCount},1fr);gap:8px;margin-bottom:16px'>");
        foreach (var (label, value, color) in items)
        {
            sb.AppendLine($"<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid {color};border-radius:6px;padding:8px 10px;text-align:center'>");
            sb.AppendLine($"<div style='font-size:16pt;font-weight:900;color:{color};line-height:1'>{value}</div>");
            sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:3px'>{ReportHelpers.HtmlEncode(label)}</div>");
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");
    }

    private static void RenderExposureCard(StringBuilder sb, bool es, int criticalVectorCount)
    {
        sb.AppendLine("<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid #991B1B;border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:#991B1B;letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "COSTO DE EXPOSICIÓN" : "COST OF EXPOSURE")}</div>");
        sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:#991B1B;line-height:1'>USD 1.2M</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:4px;font-style:italic'>{ReportHelpers.HtmlEncode(es ? "IBM Cost of a Data Breach 2024 · segmento PyME" : "IBM Cost of a Data Breach 2024 · SMB segment")}</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:6px;line-height:1.4;border-top:1px solid #E2E8F0;padding-top:6px'>{ReportHelpers.HtmlEncode(es ? $"Su infraestructura actual presenta {criticalVectorCount} vectores críticos que coinciden con los casos de estudio de este benchmark." : $"Your infrastructure currently exhibits {criticalVectorCount} critical vectors matching this benchmark's case studies.")}</div>");
        sb.AppendLine("</div>");
    }

    private static void RenderCoverageCard(StringBuilder sb, bool es, double coveragePct, double coverageAvg, int runCount)
    {
        sb.AppendLine("<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid #0F172A;border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:#0F172A;letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "COBERTURA DE ACTIVOS" : "ASSET COVERAGE")}</div>");
        sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:#0F172A;line-height:1'>{coveragePct:0}%</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:6px'>{(int)Math.Round(coverageAvg)} {ReportHelpers.HtmlEncode(es ? "de" : "of")} {runCount} {ReportHelpers.HtmlEncode(es ? "equipos protegidos" : "machines protected")}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:4px'>BitLocker · TPM · LAPS · Defender</div>");
        sb.AppendLine("</div>");
    }

    private static void RenderEvolutionCard(StringBuilder sb, bool es, string evolArrow, string evolColor,
        decimal evolDelta, string evolLabel, bool hasPrevious)
    {
        sb.AppendLine($"<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid {evolColor};border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:{evolColor};letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "EVOLUCIÓN" : "RISK EVOLUTION")}</div>");
        if (hasPrevious)
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
    }

    private static void RenderCloudCard(StringBuilder sb, bool es, decimal cloudScore, int areaCount)
    {
        var cloudColor = cloudScore >= 85 ? "#15803D" : cloudScore >= 60 ? "#B45309" : "#991B1B";
        sb.AppendLine($"<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid {cloudColor};border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:{cloudColor};letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "NUBE / M365" : "CLOUD SECURITY")}</div>");
        sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:{cloudColor};line-height:1'>{cloudScore:0.#}%</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:6px'>{ReportHelpers.HtmlEncode(es ? $"{areaCount} áreas evaluadas" : $"{areaCount} areas assessed")}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:4px'>{ReportHelpers.HtmlEncode(es ? "Score promedio Cloud Assessment" : "Cloud Assessment average score")}</div>");
        sb.AppendLine("</div>");
    }
}
