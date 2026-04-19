using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class TimelineBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        if (data.ServiceCatalog.Count == 0) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;

        var phases = data.ServiceCatalog
            .Select(svc => new { Svc = svc, Affected = ServiceCatalogBlock.CountAffected(svc, data) })
            .Where(x => x.Affected > 0)
            .OrderByDescending(x => x.Svc.Severity == "critical" ? 4 : x.Svc.Severity == "high" ? 3 : 2)
            .ToList();

        if (phases.Count == 0) return "";

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Roadmap de Remediación" : "Remediation Roadmap", data.Branding);
        sb.AppendLine("<div class='pb'>");

        int weekOffset = 0;
        foreach (var phase in phases)
        {
            var totalHours = phase.Affected * phase.Svc.BaseHours;
            var weeks = Math.Max(1, (int)Math.Ceiling((double)totalHours / 40));
            var name = es ? phase.Svc.NameEs : phase.Svc.NameEn;
            var sevColor = phase.Svc.Severity == "critical" ? "#C0392B" : phase.Svc.Severity == "high" ? "#D97706" : "#008852";
            var barWidthPct = Math.Min(90, weeks * 15);

            sb.AppendLine("<div style='margin-bottom:0.75em;'>");
            sb.AppendLine($"<div style='display:flex;justify-content:space-between;font-size:0.85em;'>");
            sb.AppendLine($"<span><strong>{ReportHelpers.HtmlEncode(name)}</strong></span>");
            sb.AppendLine($"<span>{(es ? "Semana" : "Week")} {weekOffset + 1}–{weekOffset + weeks}</span>");
            sb.AppendLine("</div>");
            sb.AppendLine($"<div style='background:#E2E8F0;border-radius:4px;height:24px;margin-top:4px;'>");
            sb.AppendLine($"<div style='background:{sevColor};width:{barWidthPct}%;height:100%;border-radius:4px;margin-left:{weekOffset * 10}%;'></div>");
            sb.AppendLine("</div></div>");

            weekOffset += weeks;
        }

        sb.AppendLine($"<p style='margin-top:1.5em;color:#64748B;font-size:0.9em;'>{(es ? "Duración total estimada" : "Total estimated duration")}: {weekOffset} {(es ? "semanas" : "weeks")}</p>");
        sb.AppendLine("</div></div>");

        return sb.ToString();
    }
}
