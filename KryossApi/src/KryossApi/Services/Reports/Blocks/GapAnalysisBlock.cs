using System.Text;
using KryossApi.Data.Entities;

namespace KryossApi.Services.Reports.Blocks;

public class GapAnalysisBlock : IReportBlock
{
    private const int RemediationTaskCount = 20;
    private const int BarHeight = 56; // label row(18) + bar(6) + pills(14) + margin(8) + safety(10)

    public string Render(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;

        var failingControls = data.ControlResults
            .Where(r => r.Status == "fail")
            .ToList();

        var cloudFails = data.HasCloudData && data.CloudFindings != null
            ? data.CloudFindings.Where(f => f.Status is "action_required" or "warning" or "disabled").ToList()
            : [];

        if (failingControls.Count == 0 && cloudFails.Count == 0)
            return "";

        var sb = new StringBuilder();
        var budget = new PageBudget();

        var categories = BuildCategories(failingControls);

        // ── Page 1+: Category health bars ──
        RenderCategoryBars(sb, categories, data, es, budget);

        // ── Remediation Task List (top 20 quick wins) ──
        var allControls = categories.SelectMany(c => c.Controls).ToList();
        RenderRemediationTasks(sb, allControls, cloudFails, data, es, budget);

        return sb.ToString();
    }

    private static List<CategoryGroup> BuildCategories(List<OrgControlResult> failingControls)
    {
        return failingControls
            .GroupBy(r => r.Category)
            .Select(g =>
            {
                var controls = g.GroupBy(r => r.ControlDefId)
                    .Select(cg => new AggControl
                    {
                        Name = cg.First().Name,
                        Category = cg.First().Category,
                        Severity = cg.First().Severity,
                        Remediation = cg.First().Remediation,
                        MachineCount = cg.Select(r => r.RunId).Distinct().Count()
                    })
                    .OrderByDescending(c => SeverityRank(c.Severity))
                    .ThenByDescending(c => c.MachineCount)
                    .ToList();

                return new CategoryGroup
                {
                    Name = g.Key,
                    Controls = controls,
                    CriticalCount = controls.Count(c => c.Severity == "critical"),
                    HighCount = controls.Count(c => c.Severity == "high"),
                    UniqueMachines = g.Select(r => r.RunId).Distinct().Count()
                };
            })
            .OrderByDescending(c => c.CriticalCount)
            .ThenByDescending(c => c.HighCount)
            .ThenByDescending(c => c.Controls.Count)
            .ToList();
    }

    // ── Category health bars ──

    private static void RenderCategoryBars(StringBuilder sb, List<CategoryGroup> categories,
        ReportData data, bool es, PageBudget budget)
    {
        var pageTitle = es ? "Análisis de Brechas" : "Gap Analysis";
        budget.StartPage(sb, pageTitle, data.Branding);

        var totalUniqueControls = categories.Sum(c => c.Controls.Count);
        sb.AppendLine($"<p style='font-size:10pt;color:#475569;margin-bottom:14px'>");
        sb.Append(es
            ? $"{totalUniqueControls} controles fallidos en {data.TotalMachines} máquinas, agrupados en {categories.Count} categorías."
            : $"{totalUniqueControls} failing controls across {data.TotalMachines} machines, grouped into {categories.Count} categories.");
        sb.AppendLine("</p>");
        budget.Spend(PageBudget.Paragraph);

        foreach (var cat in categories)
        {
            if (budget.WouldOverflow(BarHeight))
                budget.NewPage(sb, pageTitle, data.Branding);

            var affectedPct = data.TotalMachines > 0
                ? Math.Round(100.0 * cat.UniqueMachines / data.TotalMachines)
                : 0;
            var barColor = cat.CriticalCount > 0 ? "#ef4444"
                         : cat.HighCount > 0 ? "#f97316"
                         : "#eab308";
            var barWidth = Math.Max(2, affectedPct);

            sb.AppendLine("<div style='margin-bottom:8px'>");

            // Label row
            sb.AppendLine("<div style='display:flex;justify-content:space-between;align-items:baseline;margin-bottom:2px'>");
            sb.AppendLine($"<span style='font-size:9pt;font-weight:600;color:#1e293b'>{ReportHelpers.HtmlEncode(ReportTranslations.TranslateCategory(cat.Name, es ? "es" : "en"))}</span>");
            sb.AppendLine($"<span style='font-size:8pt;color:#6b7280'>{cat.Controls.Count} {(es ? "controles" : "controls")} · {cat.UniqueMachines}/{data.TotalMachines} {(es ? "máquinas" : "machines")}</span>");
            sb.AppendLine("</div>");

            // Bar
            sb.AppendLine("<div style='background:#f1f5f9;border-radius:4px;height:6px;position:relative'>");
            sb.AppendLine($"<div style='background:{barColor};border-radius:4px;height:6px;width:{barWidth:F0}%'></div>");
            sb.AppendLine("</div>");

            // Severity pills
            sb.AppendLine("<div style='margin-top:2px;font-size:7pt;color:#94a3b8'>");
            if (cat.CriticalCount > 0)
                sb.Append($"<span style='color:#ef4444;font-weight:600'>{cat.CriticalCount} {(es ? "críticos" : "critical")}</span> · ");
            if (cat.HighCount > 0)
                sb.Append($"<span style='color:#f97316;font-weight:600'>{cat.HighCount} {(es ? "altos" : "high")}</span> · ");
            sb.AppendLine($"{affectedPct:F0}% {(es ? "afectado" : "affected")}");
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");
            budget.Spend(BarHeight);
        }

        if (data.HasCloudData && data.CloudFindings != null)
        {
            var actionable = data.CloudFindings.Where(f => f.Status is "action_required" or "warning" or "disabled").ToList();
            if (actionable.Count > 0)
                RenderCloudSummaryTable(sb, actionable, es, budget, data.Branding);
        }

        budget.EndPage(sb);
    }

    // ── Remediation Task List (top 20 quick wins) ──

    private static void RenderRemediationTasks(StringBuilder sb, List<AggControl> allControls,
        List<CloudAssessmentFinding> cloudFails, ReportData data, bool es, PageBudget budget)
    {
        var tasks = allControls
            .Where(c => !string.IsNullOrWhiteSpace(c.Remediation))
            .OrderByDescending(c => SeverityRank(c.Severity))
            .ThenByDescending(c => c.MachineCount)
            .Take(RemediationTaskCount)
            .ToList();

        // Add cloud quick wins (critical/high with recommendations)
        var cloudTasks = cloudFails
            .Where(f => f.Priority is "critical" or "high" && !string.IsNullOrWhiteSpace(f.Recommendation))
            .OrderByDescending(f => f.Priority == "critical" ? 2 : 1)
            .Take(5)
            .ToList();

        if (tasks.Count == 0 && cloudTasks.Count == 0) return;

        var pageTitle = es ? "Plan de Remediación — Quick Wins" : "Remediation Plan — Quick Wins";
        budget.StartPage(sb, pageTitle, data.Branding);

        sb.AppendLine($"<p style='font-size:10pt;color:#475569;margin-bottom:14px'>");
        sb.Append(es
            ? $"Top {tasks.Count + cloudTasks.Count} acciones priorizadas por severidad e impacto."
            : $"Top {tasks.Count + cloudTasks.Count} actions prioritized by severity and impact.");
        sb.AppendLine("</p>");
        budget.Spend(PageBudget.Paragraph);

        var taskNum = 1;

        foreach (var t in tasks)
        {
            var cardCost = PageBudget.RemediationCardHeight(t.Remediation);
            if (budget.WouldOverflow(cardCost))
                budget.NewPage(sb, pageTitle, data.Branding);

            var borderColor = SeverityColor(t.Severity);
            sb.AppendLine($"<div style='margin-bottom:10px;padding:8px 12px;border-left:4px solid {borderColor};background:#fafafa;border-radius:0 4px 4px 0'>");

            sb.AppendLine("<div style='display:flex;justify-content:space-between;align-items:center'>");
            sb.AppendLine($"<span style='font-size:9pt;font-weight:700;color:#1e293b'>#{taskNum}. {ReportHelpers.HtmlEncode(t.Name)}</span>");
            sb.AppendLine($"<span class='badge {SeverityBadgeClass(t.Severity)}'>{ReportHelpers.HtmlEncode(SeverityLabel(t.Severity, es))}</span>");
            sb.AppendLine("</div>");

            sb.AppendLine($"<div style='font-size:7pt;color:#94a3b8;margin-top:2px'>{ReportHelpers.HtmlEncode(ReportTranslations.TranslateCategory(t.Category, es ? "es" : "en"))} · {t.MachineCount} {(es ? "máquinas" : "machines")}</div>");

            sb.AppendLine($"<div style='margin-top:6px;font-size:8pt;color:#334155;line-height:1.4'>{PageBudget.WordWrap(ReportHelpers.HtmlEncode(t.Remediation))}</div>");

            sb.AppendLine("</div>");
            budget.Spend(cardCost);
            taskNum++;
        }

        foreach (var f in cloudTasks)
        {
            var cardCost = PageBudget.RemediationCardHeight(f.Recommendation);
            if (budget.WouldOverflow(cardCost))
                budget.NewPage(sb, pageTitle, data.Branding);

            var borderColor = f.Priority == "critical" ? "#ef4444" : "#f97316";
            sb.AppendLine($"<div style='margin-bottom:10px;padding:8px 12px;border-left:4px solid {borderColor};background:#f0f9ff;border-radius:0 4px 4px 0'>");

            sb.AppendLine("<div style='display:flex;justify-content:space-between;align-items:center'>");
            sb.AppendLine($"<span style='font-size:9pt;font-weight:700;color:#1e293b'>#{taskNum}. {ReportHelpers.HtmlEncode(f.Feature)}</span>");
            sb.AppendLine($"<span class='badge badge-action'>Cloud</span>");
            sb.AppendLine("</div>");

            sb.AppendLine($"<div style='font-size:7pt;color:#94a3b8;margin-top:2px'>{ReportHelpers.HtmlEncode(AreaLabel(f.Area, es))}</div>");

            sb.AppendLine($"<div style='margin-top:6px;font-size:8pt;color:#334155;line-height:1.4'>{PageBudget.WordWrap(ReportHelpers.HtmlEncode(f.Recommendation))}</div>");

            sb.AppendLine("</div>");
            budget.Spend(cardCost);
            taskNum++;
        }

        budget.EndPage(sb);
    }

    private static void RenderCloudSummaryTable(StringBuilder sb, List<CloudAssessmentFinding> cloudFails,
        bool es, PageBudget budget, ReportBranding branding)
    {
        var byArea = cloudFails.GroupBy(f => f.Area)
            .Select(g => new { Area = g.Key, Count = g.Count(), Critical = g.Count(f => f.Priority is "critical" or "high") })
            .OrderByDescending(a => a.Critical)
            .ToList();

        if (budget.WouldOverflow(PageBudget.H3 + PageBudget.TableHeader + PageBudget.TableRow))
            budget.NewPage(sb, es ? "Brechas Cloud" : "Cloud Gaps", branding);

        sb.AppendLine($"<h3 style='margin-top:14px'>{(es ? "Brechas Cloud" : "Cloud Gaps")} ({cloudFails.Count})</h3>");
        budget.Spend(PageBudget.H3);

        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Área" : "Area")}</th>");
        sb.AppendLine($"<th style='text-align:center'>{(es ? "Hallazgos" : "Findings")}</th>");
        sb.AppendLine($"<th style='text-align:center'>{(es ? "Crítico/Alto" : "Critical/High")}</th>");
        sb.AppendLine("</tr></thead><tbody>");
        budget.Spend(PageBudget.TableHeader);

        foreach (var a in byArea)
        {
            sb.AppendLine($"<tr><td>{ReportHelpers.HtmlEncode(AreaLabel(a.Area, es))}</td>");
            sb.AppendLine($"<td style='text-align:center'>{a.Count}</td>");
            sb.AppendLine($"<td style='text-align:center;color:#ef4444;font-weight:600'>{(a.Critical > 0 ? a.Critical.ToString() : "—")}</td>");
            sb.AppendLine("</tr>");
            budget.Spend(PageBudget.TableRow);
        }
        sb.AppendLine("</tbody></table>");
    }

    // ── Helpers ──

    private static int SeverityRank(string? sev) => sev switch
    {
        "critical" => 4, "high" => 3, "medium" => 2, _ => 1
    };

    private static string SeverityColor(string? sev) => sev switch
    {
        "critical" => "#ef4444", "high" => "#f97316", "medium" => "#eab308", _ => "#94a3b8"
    };

    private static string SeverityBadgeClass(string? sev) => sev switch
    {
        "critical" or "high" => "badge-action",
        "medium" => "badge-warning",
        _ => "badge-muted"
    };

    private static string SeverityLabel(string? sev, bool es) => sev switch
    {
        "critical" => es ? "Crítico" : "Critical",
        "high" => es ? "Alto" : "High",
        "medium" => es ? "Medio" : "Medium",
        "low" => es ? "Bajo" : "Low",
        _ => sev ?? ""
    };

    private static string AreaLabel(string? area, bool es) => area switch
    {
        "identity" => es ? "Identidad" : "Identity",
        "endpoint" => es ? "Dispositivos" : "Endpoint",
        "data" => es ? "Datos" : "Data",
        "productivity" => es ? "Productividad" : "Productivity",
        "azure" => "Azure",
        _ => area ?? ""
    };

    private class AggControl
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Severity { get; set; } = "";
        public string? Remediation { get; set; }
        public int MachineCount { get; set; }
    }

    private class CategoryGroup
    {
        public string Name { get; set; } = "";
        public List<AggControl> Controls { get; set; } = [];
        public int CriticalCount { get; set; }
        public int HighCount { get; set; }
        public int UniqueMachines { get; set; }
    }
}
