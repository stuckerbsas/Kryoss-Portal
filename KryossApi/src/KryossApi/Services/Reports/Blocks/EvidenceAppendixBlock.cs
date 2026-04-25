using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class EvidenceAppendixBlock : IReportBlock
{
    private readonly int _maxItems;
    public EvidenceAppendixBlock(int maxItems = 20) => _maxItems = maxItems;

    public string Render(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new StringBuilder();
        var budget = new PageBudget();

        var items = data.ControlResults
            .Where(r => r.Status == "fail" && r.Severity is "critical" or "high")
            .GroupBy(r => r.ControlDefId)
            .Select(g => new
            {
                ControlId = g.First().ControlId,
                Name = g.First().Name,
                Category = g.First().Category,
                Severity = g.First().Severity,
                MachineCount = g.Select(r => r.RunId).Distinct().Count(),
            })
            .OrderByDescending(c => c.Severity == "critical" ? 2 : 1)
            .ThenByDescending(c => c.MachineCount)
            .Take(_maxItems)
            .ToList();

        if (items.Count == 0) return "";

        var pageTitle = es ? "Apéndice de Evidencia" : "Evidence Appendix";
        budget.StartPage(sb, pageTitle, data.Branding);

        foreach (var item in items)
        {
            var cardH = 65;
            if (budget.WouldOverflow(cardH))
                budget.NewPage(sb, pageTitle, data.Branding);

            sb.AppendLine("<div class='evidence-item'>");
            sb.AppendLine($"<div class='evidence-head'>{ReportHelpers.HtmlEncode(item.ControlId)} — {ReportHelpers.HtmlEncode(item.Name)}</div>");
            sb.AppendLine($"<div class='evidence-meta'>{ReportHelpers.HtmlEncode(ReportTranslations.TranslateCategory(item.Category, options.Lang))} · {item.MachineCount} {(es ? "equipos" : "machines")}</div>");
            sb.AppendLine($"<div class='evidence-code'>{(es ? "Estado" : "Status")}: FAIL · {ReportHelpers.HtmlEncode(item.Severity.ToUpperInvariant())}</div>");
            sb.AppendLine("</div>");
            budget.Spend(cardH);
        }

        budget.EndPage(sb);
        return sb.ToString();
    }
}
