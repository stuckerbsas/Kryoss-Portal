using System.Text;
namespace KryossApi.Services.Reports.Blocks;

public class GapAnalysisBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        var failingControls = data.ControlResults
            .Where(r => r.Status == "fail")
            .OrderByDescending(r => r.Severity == "critical" ? 4 : r.Severity == "high" ? 3 : r.Severity == "medium" ? 2 : 1)
            .ToList();

        var cloudFails = data.HasCloudData && data.CloudFindings != null
            ? data.CloudFindings.Where(f => f.Status == "fail").ToList()
            : [];

        if (failingControls.Count == 0 && cloudFails.Count == 0)
            return "";

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Análisis de Brechas" : "Gap Analysis", data.Branding);
        sb.AppendLine("<div class='pb'>");

        // Endpoint gaps grouped by category
        var grouped = failingControls.GroupBy(r => r.Category).OrderByDescending(g => g.Count());
        foreach (var group in grouped)
        {
            sb.AppendLine($"<h3>{ReportHelpers.HtmlEncode(group.Key)} ({group.Count()})</h3>");
            sb.AppendLine("<table class='data-table'><thead><tr>");
            sb.AppendLine($"<th>{(es ? "Control" : "Control")}</th><th>{(es ? "Severidad" : "Severity")}</th><th>{(es ? "Máquinas" : "Machines")}</th><th>{(es ? "Remediación" : "Remediation")}</th>");
            sb.AppendLine("</tr></thead><tbody>");

            var dedupedControls = group.GroupBy(r => r.ControlDefId)
                .Select(g => new { Control = g.First(), MachineCount = g.Select(r => r.RunId).Distinct().Count() })
                .OrderByDescending(x => x.Control.Severity == "critical" ? 4 : x.Control.Severity == "high" ? 3 : 2)
                .ThenByDescending(x => x.MachineCount);

            foreach (var item in dedupedControls)
            {
                var sevClass = item.Control.Severity == "critical" ? "fail" : item.Control.Severity == "high" ? "warn" : "";
                sb.AppendLine($"<tr><td>{ReportHelpers.HtmlEncode(item.Control.Name)}</td>");
                sb.AppendLine($"<td class='{sevClass}'>{item.Control.Severity}</td>");
                sb.AppendLine($"<td>{item.MachineCount}</td>");
                sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(item.Control.Remediation ?? "—")}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        // Cloud gaps grouped by area
        if (cloudFails.Count > 0)
        {
            sb.AppendLine($"<h3>{(es ? "Brechas Cloud" : "Cloud Gaps")} ({cloudFails.Count})</h3>");
            var cloudGrouped = cloudFails.GroupBy(f => f.Area).OrderByDescending(g => g.Count());
            foreach (var areaGroup in cloudGrouped)
            {
                sb.AppendLine($"<h4 style='color:#475569;margin:0.75em 0 0.25em;'>{ReportHelpers.HtmlEncode(areaGroup.Key)}</h4>");
                foreach (var f in areaGroup)
                {
                    var priorityClass = f.Priority == "high" ? "fail" : f.Priority == "medium" ? "warn" : "";
                    sb.AppendLine("<div class='finding'>");
                    sb.AppendLine($"<strong class='{priorityClass}'>{ReportHelpers.HtmlEncode(f.Feature)}</strong>");
                    if (!string.IsNullOrEmpty(f.Observation))
                        sb.AppendLine($"<p style='color:#64748B;margin:0.25em 0;'>{ReportHelpers.HtmlEncode(f.Observation)}</p>");
                    if (!string.IsNullOrEmpty(f.Recommendation))
                        sb.AppendLine($"<p>{ReportHelpers.HtmlEncode(f.Recommendation)}</p>");
                    sb.AppendLine("</div>");
                }
            }
        }

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }
}
