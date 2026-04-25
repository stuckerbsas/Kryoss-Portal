using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class DecisionsMatrixBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Matriz de Decisiones" : "Decisions Matrix";

    public int EstimateHeight(ReportData data) => 320;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new StringBuilder();

        var categories = data.ControlResults
            .Where(r => r.Status == "fail")
            .GroupBy(r => r.Category)
            .Select(g => new DecisionCategory(
                g.Key,
                g.Select(r => r.ControlDefId).Distinct().Count(),
                g.Count(r => r.Severity == "critical"),
                g.Count(r => r.Severity == "high"),
                g.Select(r => r.RunId).Distinct().Count()))
            .OrderByDescending(c => c.CritCount).ThenByDescending(c => c.HighCount)
            .ToList();

        if (categories.Count == 0) return "";

        var approved = categories.Where(c => c.CritCount > 0).Take(3).ToList();
        var pending = categories.Where(c => c.CritCount == 0 && c.HighCount > 0).Take(3).ToList();
        var recommended = categories.Where(c => c.CritCount == 0 && c.HighCount == 0).Take(3).ToList();

        sb.AppendLine("<div class='decisions-grid'>");
        RenderColumn(sb, es ? "Aprobado y Urgente" : "Approved & Funded", "approved", "#15803D", approved, es, data);
        RenderColumn(sb, es ? "Pendiente de Aprobación" : "Pending Approval", "pending", "#B45309", pending, es, data);
        RenderColumn(sb, es ? "Recomendado Siguiente" : "Recommended Next", "recommended", "#2563EB", recommended, es, data);
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

    private record DecisionCategory(string Name, int Count, int CritCount, int HighCount, int Machines);

    private static void RenderColumn(StringBuilder sb, string header, string cssClass, string color,
        List<DecisionCategory> items, bool es, ReportData data)
    {
        sb.AppendLine($"<div class='decisions-col {cssClass}'>");
        sb.AppendLine($"<h4>{ReportHelpers.HtmlEncode(header)}</h4>");

        if (items.Count == 0)
        {
            sb.AppendLine($"<div style='font-size:9pt;color:#94a3b8;padding:8px 0'>{(es ? "Sin elementos" : "No items")}</div>");
        }
        else
        {
            foreach (var item in items)
            {
                var rate = data.Rate?.HourlyRate ?? 150m;
                var cost = item.Machines * rate * (item.CritCount > 0 ? 4m : 2m);
                sb.AppendLine("<div class='decision-item'>");
                sb.AppendLine($"<div class='decision-title'>{ReportHelpers.HtmlEncode(item.Name)}</div>");
                sb.AppendLine($"<div class='decision-ask'>{item.Count} {(es ? "controles" : "controls")} · {item.Machines} {(es ? "equipos" : "machines")}</div>");
                sb.AppendLine($"<div class='decision-cost'>{data.Rate?.Currency ?? "USD"} {cost:N0}</div>");
                sb.AppendLine("</div>");
            }
        }

        sb.AppendLine("</div>");
    }
}
