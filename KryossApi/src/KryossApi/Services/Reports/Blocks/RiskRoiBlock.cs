using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class RiskRoiBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new StringBuilder();
        var budget = new PageBudget();

        var items = BuildRiskRoiItems(data, es);
        if (items.Count == 0) return "";

        var pageTitle = es ? "Análisis ROI de Riesgo" : "Risk ROI Analysis";
        budget.StartPage(sb, pageTitle, data.Branding);

        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>#</th><th>{(es ? "Riesgo" : "Risk")}</th><th>{(es ? "Impacto" : "Impact")}</th>");
        sb.AppendLine($"<th>{(es ? "Mitigación" : "Mitigation")}</th><th>ROI</th>");
        sb.AppendLine($"<th>{(es ? "Severidad" : "Severity")}</th>");
        sb.AppendLine("</tr></thead><tbody>");
        budget.Spend(PageBudget.TableHeader);

        int rank = 1;
        foreach (var item in items)
        {
            if (budget.WouldOverflow(PageBudget.TableRow))
            {
                sb.AppendLine("</tbody></table>");
                budget.NewPage(sb, pageTitle, data.Branding);
                sb.AppendLine("<table class='data-table'><thead><tr>");
                sb.AppendLine($"<th>#</th><th>{(es ? "Riesgo" : "Risk")}</th><th>{(es ? "Impacto" : "Impact")}</th><th>{(es ? "Mitigación" : "Mitigation")}</th><th>ROI</th><th>{(es ? "Severidad" : "Severity")}</th>");
                sb.AppendLine("</tr></thead><tbody>");
                budget.Spend(PageBudget.TableHeader);
            }

            var rowClass = rank <= 3 ? " style='background:#fef2f2'" : "";
            sb.AppendLine($"<tr{rowClass}>");
            sb.AppendLine($"<td><div class='risk-num'>{rank}</div></td>");
            sb.AppendLine($"<td><strong>{ReportHelpers.HtmlEncode(item.Title)}</strong></td>");
            sb.AppendLine($"<td>USD {item.ImpactUsd:N0}</td>");
            sb.AppendLine($"<td>USD {item.MitigationUsd:N0}</td>");
            sb.AppendLine($"<td style='font-weight:700;color:#15803d'>{item.RoiRatio:F1}x</td>");
            sb.AppendLine($"<td><span class='badge {SevBadge(item.Severity)}'>{ReportHelpers.HtmlEncode(SevLabel(item.Severity, es))}</span></td>");
            sb.AppendLine("</tr>");
            budget.Spend(PageBudget.TableRow);
            rank++;
        }

        sb.AppendLine("</tbody></table>");
        budget.EndPage(sb);
        return sb.ToString();
    }

    private static List<RiskRoiItem> BuildRiskRoiItems(ReportData data, bool es)
    {
        var items = new List<RiskRoiItem>();
        var categories = data.ControlResults
            .Where(r => r.Status == "fail")
            .GroupBy(r => r.Category)
            .OrderByDescending(g => g.Count(r => r.Severity == "critical"))
            .ThenByDescending(g => g.Count())
            .Take(10);

        foreach (var cat in categories)
        {
            var critCount = cat.Count(r => r.Severity == "critical");
            var highCount = cat.Count(r => r.Severity == "high");
            var sev = critCount > 0 ? "critical" : highCount > 0 ? "high" : "medium";
            var machines = cat.Select(r => r.RunId).Distinct().Count();
            var impact = sev == "critical" ? 120000m : sev == "high" ? 45000m : 15000m;
            var mitigation = machines * (sev == "critical" ? 2000m : sev == "high" ? 800m : 400m);
            var roi = mitigation > 0 ? impact / mitigation : 0;

            var catName = es ? ReportTranslations.TranslateCategory(cat.Key, "es") : cat.Key;
            items.Add(new RiskRoiItem(catName, impact, mitigation, Math.Round(roi, 1), sev));
        }

        return items.OrderByDescending(i => i.RoiRatio).ToList();
    }

    private static string SevBadge(string s) => s switch { "critical" or "high" => "badge-action", "medium" => "badge-warning", _ => "badge-muted" };
    private static string SevLabel(string s, bool es) => s switch { "critical" => es ? "Crítico" : "Critical", "high" => es ? "Alto" : "High", "medium" => es ? "Medio" : "Medium", _ => s };

    private record RiskRoiItem(string Title, decimal ImpactUsd, decimal MitigationUsd, decimal RoiRatio, string Severity);
}
