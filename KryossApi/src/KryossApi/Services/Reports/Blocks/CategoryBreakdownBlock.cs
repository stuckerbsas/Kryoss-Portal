using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class CategoryBreakdownBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new StringBuilder();
        var budget = new PageBudget();

        var categories = data.ControlResults
            .GroupBy(r => r.Category)
            .Select(g => new
            {
                Name = g.Key,
                Passing = g.Count(r => r.Status == "pass"),
                Failing = g.Count(r => r.Status == "fail"),
                NA = g.Count(r => r.Status is "na" or "skip"),
            })
            .Where(c => c.Passing + c.Failing > 0)
            .OrderByDescending(c => c.Failing)
            .ToList();

        if (categories.Count == 0) return "";

        const int RowHeight = 38; // table row + cat-bar (24px text + 14px bar)
        var pageTitle = es ? "Desglose por Categoría" : "Category Breakdown";
        budget.StartPage(sb, pageTitle, data.Branding);

        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Categoría" : "Category")}</th>");
        sb.AppendLine($"<th style='text-align:center'>{(es ? "Aprobados" : "Passing")}</th>");
        sb.AppendLine($"<th style='text-align:center'>{(es ? "Fallidos" : "Failing")}</th>");
        sb.AppendLine($"<th style='text-align:center'>N/A</th>");
        sb.AppendLine($"<th>{(es ? "Cobertura" : "Coverage")}</th>");
        sb.AppendLine("</tr></thead><tbody>");
        budget.Spend(PageBudget.TableHeader);

        foreach (var cat in categories)
        {
            if (budget.WouldOverflow(RowHeight))
            {
                sb.AppendLine("</tbody></table>");
                budget.NewPage(sb, pageTitle, data.Branding);
                sb.AppendLine("<table class='data-table'><thead><tr>");
                sb.AppendLine($"<th>{(es ? "Categoría" : "Category")}</th><th style='text-align:center'>{(es ? "Aprobados" : "Passing")}</th><th style='text-align:center'>{(es ? "Fallidos" : "Failing")}</th><th style='text-align:center'>N/A</th><th>{(es ? "Cobertura" : "Coverage")}</th>");
                sb.AppendLine("</tr></thead><tbody>");
                budget.Spend(PageBudget.TableHeader);
            }

            var total = cat.Passing + cat.Failing;
            var passPct = total > 0 ? 100.0 * cat.Passing / total : 0;
            var failPct = total > 0 ? 100.0 * cat.Failing / total : 0;

            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(ReportTranslations.TranslateCategory(cat.Name, options.Lang))}</td>");
            sb.AppendLine($"<td style='text-align:center' class='pass-cell'>{cat.Passing}</td>");
            sb.AppendLine($"<td style='text-align:center' class='fail-cell'>{cat.Failing}</td>");
            sb.AppendLine($"<td style='text-align:center'>{cat.NA}</td>");
            sb.AppendLine("<td>");
            sb.AppendLine("<div class='cat-bar'>");
            sb.AppendLine($"<div class='cat-bar-pass' style='width:{passPct:F0}%'></div>");
            sb.AppendLine($"<div class='cat-bar-fail' style='width:{failPct:F0}%'></div>");
            sb.AppendLine("</div>");
            sb.AppendLine("</td>");
            sb.AppendLine("</tr>");
            budget.Spend(RowHeight);
        }

        sb.AppendLine("</tbody></table>");
        budget.EndPage(sb);
        return sb.ToString();
    }
}
