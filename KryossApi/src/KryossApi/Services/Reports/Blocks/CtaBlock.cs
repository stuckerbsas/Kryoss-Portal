using System.Text;
using KryossApi.Services;

namespace KryossApi.Services.Reports.Blocks;

public class CtaBlock : IFlowBlock
{
    private readonly CtaMode _mode;
    public CtaBlock(CtaMode mode = CtaMode.Simple) => _mode = mode;

    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Decisiones Ejecutivas" : "Executive Decisions";

    public int EstimateHeight(ReportData data) => _mode == CtaMode.Stepped ? 500 : 250;

    private List<(string Title, string Description, string Category)> ComputeCtas(ReportData data, ReportOptions options)
    {
        var capitalSin = CapitalSinDetector.Detect(
            data.Runs, data.Hygiene, data.Enrichment,
            data.M365Connected, data.M365Findings, options.Lang);

        var ctaCandidates = CtaRuleEngine.DetectCtas(
            data.Runs, data.ControlResults, data.Hygiene, data.Enrichment,
            data.M365Connected, data.M365Findings, options.Lang);

        var suppressedIds = data.SavedCtas
            .Where(c => c.IsSuppressed && !c.IsManual && c.AutoDetectedRule != null)
            .Select(c => c.AutoDetectedRule!)
            .ToHashSet();

        var editedMap = data.SavedCtas
            .Where(c => !c.IsSuppressed && !c.IsManual && c.AutoDetectedRule != null)
            .ToDictionary(c => c.AutoDetectedRule!, c => c);

        var finalCtas = new List<(string Title, string Description, string Category)>();

        if (capitalSin != null)
        {
            var linked = ctaCandidates.FirstOrDefault(c => c.RuleId == capitalSin.LinkedCtaRule);
            if (linked != null && !suppressedIds.Contains(linked.RuleId))
            {
                var title = editedMap.TryGetValue(linked.RuleId, out var edited) ? edited.Title : linked.Title;
                var desc = editedMap.TryGetValue(linked.RuleId, out edited) ? edited.Description : linked.Description;
                finalCtas.Add((title, desc, linked.Category));
            }
        }

        foreach (var c in ctaCandidates)
        {
            if (finalCtas.Count >= (_mode == CtaMode.Stepped ? 10 : 2)) break;
            if (capitalSin != null && c.RuleId == capitalSin.LinkedCtaRule) continue;
            if (suppressedIds.Contains(c.RuleId)) continue;
            var title = editedMap.TryGetValue(c.RuleId, out var edited) ? edited.Title : c.Title;
            var desc = editedMap.TryGetValue(c.RuleId, out edited) ? edited.Description : c.Description;
            finalCtas.Add((title, desc, c.Category));
        }

        foreach (var m in data.SavedCtas.Where(c => c.IsManual && !c.IsSuppressed))
        {
            if (finalCtas.Count >= (_mode == CtaMode.Stepped ? 10 : 2)) break;
            finalCtas.Add((m.Title, m.Description, m.PriorityCategory));
        }

        return finalCtas;
    }

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var finalCtas = ComputeCtas(data, options);

        sb.AppendLine($"<h3 style='font-size:12px;margin:8px 0 12px;color:#1E293B;border-bottom:2px solid #0F172A;padding-bottom:4px;text-transform:uppercase;letter-spacing:0.08em'>{ReportHelpers.HtmlEncode(es ? "Decisiones Ejecutivas Requeridas" : "Executive Decisions Required")}</h3>");

        if (finalCtas.Count == 0)
            RenderEmptyState(sb, es);
        else if (_mode == CtaMode.Stepped)
            RenderStepped(sb, finalCtas, es);
        else
            RenderSimple(sb, finalCtas, es);

        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var title = SectionTitle(options)!;
        var budget = new PageBudget();
        var ctas = ComputeCtas(data, options);

        budget.StartPage(sb, title, data.Branding);
        sb.AppendLine($"<h3 style='font-size:12px;margin:8px 0 12px;color:#1E293B;border-bottom:2px solid #0F172A;padding-bottom:4px;text-transform:uppercase;letter-spacing:0.08em'>{ReportHelpers.HtmlEncode(es ? "Decisiones Ejecutivas Requeridas" : "Executive Decisions Required")}</h3>");
        budget.Spend(PageBudget.H3);

        if (ctas.Count == 0)
        {
            RenderEmptyState(sb, es);
        }
        else if (_mode == CtaMode.Stepped)
        {
            sb.AppendLine("<div class='next-steps'>");
            int n = 1;
            foreach (var (ctaTitle, desc, cat) in ctas)
            {
                var descLines = PageBudget.LineCount(desc);
                var stepHeight = 55 + Math.Max(1, descLines) * PageBudget.LinePx9pt;
                if (budget.WouldOverflow(stepHeight))
                {
                    sb.AppendLine("</div>");
                    budget.NewPage(sb, title, data.Branding);
                    sb.AppendLine("<div class='next-steps'>");
                }
                sb.AppendLine("<div class='step'>");
                sb.AppendLine($"<div class='step-num'>{n}</div>");
                sb.AppendLine("<div>");
                sb.AppendLine($"<div style='font-size:11pt;font-weight:700;color:#0F172A'>{ReportHelpers.HtmlEncode(ctaTitle)}</div>");
                sb.AppendLine($"<div style='font-size:9pt;color:#334155;line-height:1.5;margin-top:2px'>{ReportHelpers.HtmlEncode(desc)}</div>");
                sb.AppendLine($"<div style='font-size:7pt;color:#94a3b8;margin-top:3px'>{ReportHelpers.HtmlEncode(cat)}</div>");
                sb.AppendLine("</div>");
                sb.AppendLine("</div>");
                budget.Spend(stepHeight);
                n++;
            }
            sb.AppendLine("</div>");
        }
        else
        {
            int n = 1;
            foreach (var (ctaTitle, desc, cat) in ctas)
            {
                var descLines = PageBudget.LineCount(desc);
                var cardHeight = 55 + Math.Max(1, descLines) * PageBudget.LinePx9pt;
                if (budget.WouldOverflow(cardHeight))
                    budget.NewPage(sb, title, data.Branding);
                var catColor = CatColor(cat);
                sb.AppendLine($"<div style='background:#F8F9FA;border:1px solid #CBD5E1;border-left:5px solid {catColor};border-radius:4px;padding:14px 20px;margin-bottom:10px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
                sb.AppendLine($"<div style='display:inline-block;padding:2px 8px;background:{catColor};color:#fff;font-size:8px;font-weight:800;letter-spacing:0.12em;text-transform:uppercase;border-radius:2px;margin-bottom:6px'>{ReportHelpers.HtmlEncode(cat.ToUpperInvariant())} · #{n}</div>");
                sb.AppendLine($"<div style='font-size:12pt;font-weight:700;color:#0F172A;margin-bottom:4px'>{ReportHelpers.HtmlEncode(ctaTitle)}</div>");
                sb.AppendLine($"<div style='font-size:10pt;color:#334155;line-height:1.55'>{ReportHelpers.HtmlEncode(desc)}</div>");
                sb.AppendLine("</div>");
                budget.Spend(cardHeight);
                n++;
            }
        }

        budget.EndPage(sb);
        return sb.ToString();
    }

    private static void RenderEmptyState(StringBuilder sb, bool es)
    {
        sb.AppendLine("<div style='background:#F0FDF4;border:1px solid #BBF7D0;border-left:4px solid #15803D;border-radius:4px;padding:16px 22px'>");
        sb.AppendLine($"<div style='font-size:11pt;font-weight:700;color:#14532D;margin-bottom:4px'>✓ {ReportHelpers.HtmlEncode(es ? "Postura sólida — sin decisiones ejecutivas pendientes" : "Solid posture — no pending executive decisions")}</div>");
        sb.AppendLine($"<div style='font-size:10pt;color:#166534;line-height:1.55'>{ReportHelpers.HtmlEncode(es ? "Este mes no requiere acción del CEO. El programa de hardening continúa de forma rutinaria." : "This period requires no CEO action. The hardening program continues on schedule.")}</div>");
        sb.AppendLine("</div>");
    }

    private static void RenderSimple(StringBuilder sb, List<(string Title, string Description, string Category)> ctas, bool es)
    {
        int n = 1;
        foreach (var (title, desc, cat) in ctas)
        {
            var catColor = CatColor(cat);
            sb.AppendLine($"<div style='background:#F8F9FA;border:1px solid #CBD5E1;border-left:5px solid {catColor};border-radius:4px;padding:14px 20px;margin-bottom:10px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
            sb.AppendLine($"<div style='display:inline-block;padding:2px 8px;background:{catColor};color:#fff;font-size:8px;font-weight:800;letter-spacing:0.12em;text-transform:uppercase;border-radius:2px;margin-bottom:6px'>{ReportHelpers.HtmlEncode(cat.ToUpperInvariant())} · #{n}</div>");
            sb.AppendLine($"<div style='font-size:12pt;font-weight:700;color:#0F172A;margin-bottom:4px'>{ReportHelpers.HtmlEncode(title)}</div>");
            sb.AppendLine($"<div style='font-size:10pt;color:#334155;line-height:1.55'>{ReportHelpers.HtmlEncode(desc)}</div>");
            sb.AppendLine("</div>");
            n++;
        }
    }

    private static void RenderStepped(StringBuilder sb, List<(string Title, string Description, string Category)> ctas, bool es)
    {
        sb.AppendLine("<div class='next-steps'>");
        int n = 1;
        foreach (var (title, desc, cat) in ctas)
        {
            sb.AppendLine("<div class='step'>");
            sb.AppendLine($"<div class='step-num'>{n}</div>");
            sb.AppendLine("<div>");
            sb.AppendLine($"<div style='font-size:11pt;font-weight:700;color:#0F172A'>{ReportHelpers.HtmlEncode(title)}</div>");
            sb.AppendLine($"<div style='font-size:9pt;color:#334155;line-height:1.5;margin-top:2px'>{ReportHelpers.HtmlEncode(desc)}</div>");
            sb.AppendLine($"<div style='font-size:7pt;color:#94a3b8;margin-top:3px'>{ReportHelpers.HtmlEncode(cat)}</div>");
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");
            n++;
        }
        sb.AppendLine("</div>");
    }

    private static string CatColor(string cat) => cat switch
    {
        "Incidentes" => "#991B1B",
        "Hardening" => "#0F172A",
        "Budget" => "#B45309",
        _ => "#64748B"
    };
}
