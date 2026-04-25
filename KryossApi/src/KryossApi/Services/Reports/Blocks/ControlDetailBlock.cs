using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class ControlDetailBlock : IReportBlock
{
    private readonly string? _frameworkFilter;
    public ControlDetailBlock(string? frameworkFilter = null) => _frameworkFilter = frameworkFilter;

    public string Render(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new StringBuilder();
        var budget = new PageBudget();

        var failing = data.ControlResults
            .Where(r => r.Status == "fail")
            .GroupBy(r => r.ControlDefId)
            .Select(g => new
            {
                ControlId = g.First().ControlId,
                Name = g.First().Name,
                Category = g.First().Category,
                Severity = g.First().Severity,
                Remediation = g.First().Remediation,
                MachineCount = g.Select(r => r.RunId).Distinct().Count(),
            })
            .OrderByDescending(c => c.Severity == "critical" ? 4 : c.Severity == "high" ? 3 : 2)
            .ThenByDescending(c => c.MachineCount)
            .Take(30)
            .ToList();

        if (failing.Count == 0) return "";

        var pageTitle = es ? "Detalle de Controles" : "Control Details";
        budget.StartPage(sb, pageTitle, data.Branding);

        foreach (var ctrl in failing)
        {
            var reqLines = PageBudget.LineCount(ctrl.Remediation);
            var cardHeight = 80 + reqLines * PageBudget.LinePx8pt;

            if (budget.WouldOverflow(cardHeight))
                budget.NewPage(sb, pageTitle, data.Branding);

            var sevClass = ctrl.Severity switch { "critical" => "critical", "high" => "high", "medium" => "medium", _ => "low" };
            sb.AppendLine($"<div class='control-card {sevClass}'>");

            sb.AppendLine("<div class='control-head'>");
            sb.AppendLine($"<span class='control-code'>{ReportHelpers.HtmlEncode(ctrl.ControlId)}</span>");
            sb.AppendLine($"<span class='control-title'>{ReportHelpers.HtmlEncode(ctrl.Name)}</span>");
            sb.AppendLine($"<span class='badge {SevBadge(ctrl.Severity)}'>{ReportHelpers.HtmlEncode(SevLabel(ctrl.Severity, es))}</span>");
            sb.AppendLine("</div>");

            if (!string.IsNullOrEmpty(ctrl.Remediation))
                sb.AppendLine($"<div class='control-req'>{PageBudget.WordWrap(ReportHelpers.HtmlEncode(ctrl.Remediation))}</div>");

            sb.AppendLine("<div class='control-meta'>");
            sb.AppendLine($"<span>{ReportHelpers.HtmlEncode(ReportTranslations.TranslateCategory(ctrl.Category, options.Lang))} · {ctrl.MachineCount} {(es ? "equipos" : "machines")}</span>");
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");
            budget.Spend(cardHeight);
        }

        budget.EndPage(sb);
        return sb.ToString();
    }

    private static string SevBadge(string? s) => s switch { "critical" or "high" => "badge-action", "medium" => "badge-warning", _ => "badge-muted" };
    private static string SevLabel(string? s, bool es) => s switch { "critical" => es ? "Crítico" : "Critical", "high" => es ? "Alto" : "High", "medium" => es ? "Medio" : "Medium", _ => s ?? "" };
}
