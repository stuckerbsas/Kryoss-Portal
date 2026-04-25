using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class Top3RiskBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Top 3 Riesgos" : "Top 3 Risks";

    public int EstimateHeight(ReportData data) => 220;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new StringBuilder();

        var topCategories = data.ControlResults
            .Where(r => r.Status == "fail" && r.Severity is "critical" or "high")
            .GroupBy(r => r.Category)
            .OrderByDescending(g => g.Count(r => r.Severity == "critical"))
            .ThenByDescending(g => g.Count())
            .Take(3)
            .Select(g =>
            {
                var machines = g.Select(r => r.RunId).Distinct().Count();
                var cost = g.Count(r => r.Severity == "critical") * 40000m + g.Count(r => r.Severity == "high") * 15000m;
                return new TopRiskItem(
                    ReportTranslations.TranslateCategory(g.Key, options.Lang),
                    es ? $"{g.Select(r => r.ControlDefId).Distinct().Count()} controles fallidos en {machines} equipos"
                       : $"{g.Select(r => r.ControlDefId).Distinct().Count()} failing controls across {machines} machines",
                    $"USD {cost:N0}");
            })
            .ToList();

        if (topCategories.Count == 0) return "";

        sb.AppendLine("<div class='top-risk-grid'>");
        foreach (var risk in topCategories)
        {
            sb.AppendLine("<div class='top-risk-card'>");
            sb.AppendLine($"<div class='top-risk-title'>{ReportHelpers.HtmlEncode(risk.Title)}</div>");
            sb.AppendLine($"<div class='top-risk-body'>{ReportHelpers.HtmlEncode(risk.Body)}</div>");
            if (risk.CostEstimate != null)
                sb.AppendLine($"<div class='top-risk-cost'>{ReportHelpers.HtmlEncode(risk.CostEstimate)}</div>");
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");

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

    private record TopRiskItem(string Title, string Body, string? CostEstimate);
}
