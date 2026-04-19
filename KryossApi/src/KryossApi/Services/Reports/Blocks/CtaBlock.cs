using System.Text;
using KryossApi.Services;

namespace KryossApi.Services.Reports.Blocks;

/// <summary>
/// Block 3: Executive Decisions Required. 12-rule auto-detection via CtaRuleEngine,
/// merged with data.SavedCtas (operator edits/suppressions). Max 2 CTAs shown.
/// Capital-sin-linked CTA is always promoted to slot 1.
/// Empty state = positive closure card.
/// </summary>
public class CtaBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Decisiones Ejecutivas" : "Executive Decisions", data.Branding);
        sb.AppendLine("<div class='pb'>");

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

        // Capital-sin CTA promoted to slot 1
        if (capitalSin != null)
        {
            var linked = ctaCandidates.FirstOrDefault(c => c.RuleId == capitalSin.LinkedCtaRule);
            if (linked != null && !suppressedIds.Contains(linked.RuleId))
            {
                var title = editedMap.TryGetValue(linked.RuleId, out var edited) ? edited.Title : linked.Title;
                var desc  = editedMap.TryGetValue(linked.RuleId, out edited)     ? edited.Description : linked.Description;
                finalCtas.Add((title, desc, linked.Category));
            }
        }

        foreach (var c in ctaCandidates)
        {
            if (finalCtas.Count >= 2) break;
            if (capitalSin != null && c.RuleId == capitalSin.LinkedCtaRule) continue;
            if (suppressedIds.Contains(c.RuleId)) continue;
            var title = editedMap.TryGetValue(c.RuleId, out var edited) ? edited.Title : c.Title;
            var desc  = editedMap.TryGetValue(c.RuleId, out edited)     ? edited.Description : c.Description;
            finalCtas.Add((title, desc, c.Category));
        }

        foreach (var m in data.SavedCtas.Where(c => c.IsManual && !c.IsSuppressed))
        {
            if (finalCtas.Count >= 2) break;
            finalCtas.Add((m.Title, m.Description, m.PriorityCategory));
        }

        sb.AppendLine($"<h3 style='font-size:12px;margin:8px 0 12px;color:#1E293B;border-bottom:2px solid #0F172A;padding-bottom:4px;text-transform:uppercase;letter-spacing:0.08em'>{ReportHelpers.HtmlEncode(es ? "Decisiones Ejecutivas Requeridas" : "Executive Decisions Required")}</h3>");

        if (finalCtas.Count == 0)
        {
            sb.AppendLine("<div style='background:#F0FDF4;border:1px solid #BBF7D0;border-left:4px solid #15803D;border-radius:4px;padding:16px 22px'>");
            sb.AppendLine($"<div style='font-size:11pt;font-weight:700;color:#14532D;margin-bottom:4px'>\u2713 {ReportHelpers.HtmlEncode(es ? "Postura s\u00F3lida \u2014 sin decisiones ejecutivas pendientes" : "Solid posture \u2014 no pending executive decisions")}</div>");
            sb.AppendLine($"<div style='font-size:10pt;color:#166534;line-height:1.55'>{ReportHelpers.HtmlEncode(es ? "Este mes no requiere acci\u00F3n del CEO. El programa de hardening contin\u00FAa de forma rutinaria." : "This period requires no CEO action. The hardening program continues on schedule.")}</div>");
            sb.AppendLine("</div>");
        }
        else
        {
            int n = 1;
            foreach (var (title, desc, cat) in finalCtas)
            {
                var catColor = cat switch
                {
                    "Incidentes" => "#991B1B",
                    "Hardening"  => "#0F172A",
                    "Budget"     => "#B45309",
                    _            => "#64748B"
                };
                sb.AppendLine($"<div style='background:#F8F9FA;border:1px solid #CBD5E1;border-left:5px solid {catColor};border-radius:4px;padding:14px 20px;margin-bottom:10px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
                sb.AppendLine($"<div style='display:inline-block;padding:2px 8px;background:{catColor};color:#fff;font-size:8px;font-weight:800;letter-spacing:0.12em;text-transform:uppercase;border-radius:2px;margin-bottom:6px'>{ReportHelpers.HtmlEncode(cat.ToUpperInvariant())} \u00B7 #{n}</div>");
                sb.AppendLine($"<div style='font-size:12pt;font-weight:700;color:#0F172A;margin-bottom:4px'>{ReportHelpers.HtmlEncode(title)}</div>");
                sb.AppendLine($"<div style='font-size:10pt;color:#334155;line-height:1.55'>{ReportHelpers.HtmlEncode(desc)}</div>");
                sb.AppendLine("</div>");
                n++;
            }
        }

        sb.AppendLine("</div></div>");

        return sb.ToString();
    }
}
