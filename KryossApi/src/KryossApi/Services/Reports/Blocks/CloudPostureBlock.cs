using System.Text;
using KryossApi.Data.Entities;

namespace KryossApi.Services.Reports.Blocks;

public class CloudPostureBlock : IReportBlock
{
    private readonly bool _compact;

    public CloudPostureBlock(bool compact = false) => _compact = compact;

    public string Render(ReportData data, ReportOptions options)
    {
        if (!data.HasCloudData) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var budget = new PageBudget();
        var pageTitle = es ? "Postura Cloud" : "Cloud Posture";

        // ── Page 1: Radar + Score Cards ──
        budget.StartPage(sb, pageTitle, data.Branding);

        if (data.AreaScores != null && data.AreaScores.Count > 0)
        {
            RenderRadarChart(sb, data.AreaScores, es);
            budget.Spend(PageBudget.RadarChart);
            RenderAreaScoreCards(sb, data.AreaScores, es);
            budget.Spend(PageBudget.ScoreCards);
        }

        // ── Network-to-cloud performance (fills whitespace below radar) ──
        if (data.NetworkDiags.Count > 0)
        {
            RenderCloudPerformance(sb, data, es);
            budget.Spend(PageBudget.ScoreCards * 2);
        }

        if (_compact && data.CloudFindings != null && data.CloudFindings.Count > 0)
        {
            var critical = data.CloudFindings
                .Where(f => f.Priority is "critical" or "high")
                .OrderByDescending(f => f.Priority == "critical" ? 2 : 1)
                .Take(3)
                .ToList();
            if (critical.Count > 0)
            {
                sb.AppendLine($"<h3>{ReportHelpers.HtmlEncode(es ? "Hallazgos Críticos Cloud" : "Critical Cloud Findings")}</h3>");
                budget.Spend(PageBudget.H3);
                foreach (var f in critical)
                {
                    var cardH = PageBudget.CloudFindingHeight(f.Observation, f.Recommendation);
                    if (budget.WouldOverflow(cardH))
                        budget.NewPage(sb, pageTitle, data.Branding);
                    RenderFindingCard(sb, f, es);
                    budget.Spend(cardH);
                }
            }
        }

        budget.EndPage(sb);

        // ── Full mode: area findings with page budget ──
        if (!_compact && data.CloudFindings != null && data.CloudFindings.Count > 0)
        {
            var findingsTitle = es ? "Postura Cloud — Hallazgos" : "Cloud Posture — Findings";
            budget.StartPage(sb, findingsTitle, data.Branding);

            foreach (var area in new[] { "identity", "endpoint", "data", "productivity", "azure" })
            {
                var areaFindings = data.CloudFindings.Where(f => f.Area == area).ToList();
                if (areaFindings.Count == 0) continue;

                var actionable = areaFindings
                    .Where(f => f.Status is "action_required" or "warning" or "disabled")
                    .OrderByDescending(f => f.Priority == "critical" ? 4 : f.Priority == "high" ? 3 : f.Priority == "medium" ? 2 : 1)
                    .ToList();
                var okCount = areaFindings.Count - actionable.Count;

                if (actionable.Count == 0) continue;

                if (budget.WouldOverflow(PageBudget.H3 + PageBudget.FindingCard))
                    budget.NewPage(sb, findingsTitle, data.Branding);

                var areaLabel = AreaLabel(area, es);
                var suffix = okCount > 0 ? $" · {okCount} OK" : "";
                sb.AppendLine($"<h3>{ReportHelpers.HtmlEncode(areaLabel)} ({actionable.Count} {(es ? "hallazgos" : "findings")}{suffix})</h3>");
                budget.Spend(PageBudget.H3);

                foreach (var f in actionable)
                {
                    var cardH = PageBudget.CloudFindingHeight(f.Observation, f.Recommendation);
                    if (budget.WouldOverflow(cardH))
                        budget.NewPage(sb, findingsTitle, data.Branding);

                    RenderFindingCard(sb, f, es);
                    budget.Spend(cardH);
                }
            }

            budget.EndPage(sb);
        }

        return sb.ToString();
    }

    private static double NormalizeScore(decimal raw) =>
        raw <= 5.0m ? (double)(raw * 20m) : (double)raw;

    internal static void RenderRadarChartStatic(StringBuilder sb, Dictionary<string, decimal> areaScores, bool es) =>
        RenderRadarChart(sb, areaScores, es);

    private static void RenderRadarChart(StringBuilder sb, Dictionary<string, decimal> areaScores, bool es)
    {
        var axes = new[]
        {
            ("identity",     es ? "Identidad"     : "Identity"),
            ("endpoint",     es ? "Dispositivos"  : "Endpoint"),
            ("data",         es ? "Datos"          : "Data"),
            ("productivity", es ? "Productividad" : "Productivity"),
            ("azure",        "Azure"),
        };

        const int cx = 200, cy = 200, r = 150;
        const double step = 2 * Math.PI / 5;
        const double startAngle = -Math.PI / 2;

        static (double x, double y) Point(double angle, double radius, int cx, int cy) =>
            (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));

        sb.AppendLine("<div class='radar-container' style='text-align:center;margin:12px 0'>");
        sb.AppendLine("<svg viewBox='0 0 400 400' xmlns='http://www.w3.org/2000/svg' style='max-width:280px;display:inline-block'>");

        foreach (var pct in new[] { 0.25, 0.50, 0.75, 1.0 })
        {
            var pts = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                var angle = startAngle + i * step;
                var (x, y) = Point(angle, r * pct, cx, cy);
                pts.Add($"{x:F1},{y:F1}");
            }
            sb.AppendLine($"<polygon points='{string.Join(" ", pts)}' fill='none' stroke='#e5e7eb' stroke-width='1'/>");
        }

        for (int i = 0; i < 5; i++)
        {
            var angle = startAngle + i * step;
            var (x, y) = Point(angle, r, cx, cy);
            sb.AppendLine($"<line x1='{cx}' y1='{cy}' x2='{x:F1}' y2='{y:F1}' stroke='#d1d5db' stroke-width='1'/>");
        }

        var dataPoints = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var key = axes[i].Item1;
            var score = areaScores.TryGetValue(key, out var s) ? NormalizeScore(s) : 0.0;
            var angle = startAngle + i * step;
            var (x, y) = Point(angle, r * score / 100.0, cx, cy);
            dataPoints.Add($"{x:F1},{y:F1}");
        }
        sb.AppendLine($"<polygon points='{string.Join(" ", dataPoints)}' fill='#006536' fill-opacity='0.2' stroke='#006536' stroke-width='2'/>");

        for (int i = 0; i < 5; i++)
        {
            var (key, label) = axes[i];
            var score = areaScores.TryGetValue(key, out var s) ? NormalizeScore(s) : 0.0;
            var angle = startAngle + i * step;

            var (dx, dy) = Point(angle, r * score / 100.0, cx, cy);
            sb.AppendLine($"<circle cx='{dx:F1}' cy='{dy:F1}' r='4' fill='#006536'/>");

            var (lx, ly) = Point(angle, r + 22, cx, cy);
            var anchor = lx < cx - 5 ? "end" : lx > cx + 5 ? "start" : "middle";
            sb.AppendLine($"<text x='{lx:F1}' y='{ly:F1}' text-anchor='{anchor}' dominant-baseline='middle' font-size='11' fill='#374151' font-family='Montserrat,sans-serif'>{ReportHelpers.HtmlEncode(label)}</text>");

            sb.AppendLine($"<text x='{dx:F1}' y='{dy - 8:F1}' text-anchor='middle' font-size='9' fill='#006536' font-weight='600' font-family='Montserrat,sans-serif'>{score:F0}</text>");
        }

        sb.AppendLine("</svg>");
        sb.AppendLine("</div>");
    }

    private static void RenderAreaScoreCards(StringBuilder sb, Dictionary<string, decimal> areaScores, bool es)
    {
        var areas = new[]
        {
            ("identity",     es ? "Identidad"     : "Identity"),
            ("endpoint",     es ? "Dispositivos"  : "Endpoint"),
            ("data",         es ? "Datos"          : "Data"),
            ("productivity", es ? "Productividad" : "Productivity"),
            ("azure",        "Azure"),
        };

        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(5,1fr);gap:8px;margin:10px 0'>");
        foreach (var (key, label) in areas)
        {
            if (!areaScores.TryGetValue(key, out var raw)) continue;
            var score = NormalizeScore(raw);
            var color = score >= 80 ? "#16a34a" : score >= 60 ? "#eab308" : "#ef4444";
            sb.AppendLine("<div style='border:1px solid #e5e7eb;border-radius:8px;padding:10px;text-align:center'>");
            sb.AppendLine($"<div style='font-size:18pt;font-weight:700;color:{color}'>{score:F0}</div>");
            sb.AppendLine($"<div style='font-size:8pt;color:#6b7280'>{ReportHelpers.HtmlEncode(label)}</div>");
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");
    }

    private static void RenderCloudPerformance(StringBuilder sb, ReportData data, bool es)
    {
        var diags = data.NetworkDiags;
        var withCloud = diags.Where(d => d.CloudEndpointAvgMs.HasValue && d.CloudEndpointAvgMs > 0).ToList();
        var withSpeed = diags.Where(d => d.DownloadMbps.HasValue && d.DownloadMbps > 0).ToList();
        var withDns = diags.Where(d => d.DnsResolutionMs.HasValue && d.DnsResolutionMs > 0).ToList();
        var withLatency = diags.Where(d => d.InternetLatencyMs.HasValue && d.InternetLatencyMs > 0).ToList();

        var overallScore = data.CloudScan?.OverallScore;

        sb.AppendLine($"<h3 style='margin-top:14px'>{ReportHelpers.HtmlEncode(es ? "Rendimiento hacia Cloud" : "Cloud Connectivity")}</h3>");
        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin:10px 0'>");

        // Overall cloud score
        if (overallScore.HasValue)
        {
            var score = (double)overallScore.Value;
            var color = score >= 80 ? "#16a34a" : score >= 60 ? "#eab308" : "#ef4444";
            RenderKpiCard(sb, $"{score:F0}", es ? "Score Cloud" : "Cloud Score", color);
        }

        // Avg download speed
        if (withSpeed.Count > 0)
        {
            var avgDown = withSpeed.Average(d => (double)d.DownloadMbps!.Value);
            var color = avgDown >= 50 ? "#16a34a" : avgDown >= 10 ? "#eab308" : "#ef4444";
            RenderKpiCard(sb, $"{avgDown:F0}", es ? "Mbps Descarga (prom)" : "Avg Download Mbps", color);
        }

        // Avg cloud endpoint latency
        if (withCloud.Count > 0)
        {
            var avgCloud = withCloud.Average(d => (double)d.CloudEndpointAvgMs!.Value);
            var color = avgCloud <= 50 ? "#16a34a" : avgCloud <= 150 ? "#eab308" : "#ef4444";
            RenderKpiCard(sb, $"{avgCloud:F0}ms", es ? "Latencia M365 (prom)" : "Avg M365 Latency", color);
        }

        // Avg DNS resolution
        if (withDns.Count > 0)
        {
            var avgDns = withDns.Average(d => (double)d.DnsResolutionMs!.Value);
            var color = avgDns <= 30 ? "#16a34a" : avgDns <= 100 ? "#eab308" : "#ef4444";
            RenderKpiCard(sb, $"{avgDns:F0}ms", es ? "DNS (prom)" : "Avg DNS", color);
        }

        // Internet latency fallback if cloud latency missing
        if (withCloud.Count == 0 && withLatency.Count > 0)
        {
            var avgLat = withLatency.Average(d => (double)d.InternetLatencyMs!.Value);
            var color = avgLat <= 30 ? "#16a34a" : avgLat <= 100 ? "#eab308" : "#ef4444";
            RenderKpiCard(sb, $"{avgLat:F0}ms", es ? "Latencia Internet (prom)" : "Avg Internet Latency", color);
        }

        sb.AppendLine("</div>");

        // Machine count footnote
        sb.AppendLine($"<p style='font-size:7pt;color:#94a3b8;margin-top:2px'>");
        sb.Append(es
            ? $"Basado en {diags.Count} diagnósticos de red · {data.CloudScan?.CompletedAt?.ToString("dd/MM/yyyy") ?? "—"}"
            : $"Based on {diags.Count} network diagnostics · {data.CloudScan?.CompletedAt?.ToString("yyyy-MM-dd") ?? "—"}");
        sb.AppendLine("</p>");
    }

    private static void RenderKpiCard(StringBuilder sb, string value, string label, string color)
    {
        sb.AppendLine("<div style='border:1px solid #e5e7eb;border-radius:8px;padding:12px;text-align:center'>");
        sb.AppendLine($"<div style='font-size:20pt;font-weight:700;color:{color}'>{value}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#6b7280;margin-top:2px'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }

    private static void RenderFindingCard(StringBuilder sb, CloudAssessmentFinding f, bool es)
    {
        var borderColor = f.Status == "action_required" ? "#ef4444"
                        : f.Status == "warning" ? "#eab308"
                        : "#16a34a";

        sb.AppendLine($"<div style='margin-bottom:8px;padding:8px 10px;border-left:3px solid {borderColor};background:#fafafa;border-radius:0 4px 4px 0'>");
        sb.AppendLine("<div style='display:flex;justify-content:space-between;align-items:center'>");
        sb.AppendLine($"<strong style='font-size:9pt'>{ReportHelpers.HtmlEncode(f.Feature)}</strong>");
        sb.AppendLine($"<span class='badge {SeverityBadgeClass(f.Priority)}'>{ReportHelpers.HtmlEncode(PriorityLabel(f.Priority, es))}</span>");
        sb.AppendLine("</div>");

        if (!string.IsNullOrEmpty(f.Observation))
            sb.AppendLine($"<div style='margin-top:3px;font-size:8pt;color:#4b5563;line-height:1.4'>{PageBudget.WordWrap(ReportHelpers.HtmlEncode(f.Observation))}</div>");

        if (!string.IsNullOrEmpty(f.Recommendation))
            sb.AppendLine($"<div style='margin-top:4px;font-size:8pt;line-height:1.4'><strong>{ReportHelpers.HtmlEncode(es ? "Acción:" : "Action:")}</strong> {PageBudget.WordWrap(ReportHelpers.HtmlEncode(f.Recommendation))}</div>");

        sb.AppendLine("</div>");
    }

    private static string AreaLabel(string area, bool es) => area switch
    {
        "identity"     => es ? "Identidad"     : "Identity",
        "endpoint"     => es ? "Dispositivos"  : "Endpoint",
        "data"         => es ? "Datos"          : "Data",
        "productivity" => es ? "Productividad" : "Productivity",
        "azure"        => "Azure",
        _              => area
    };

    private static string SeverityBadgeClass(string? priority) => priority switch
    {
        "critical" => "badge-action",
        "high"     => "badge-action",
        "medium"   => "badge-warning",
        _          => "badge-muted"
    };

    private static string PriorityLabel(string? priority, bool es) => priority switch
    {
        "critical" => es ? "Crítico"   : "Critical",
        "high"     => es ? "Alto"      : "High",
        "medium"   => es ? "Medio"     : "Medium",
        "low"      => es ? "Bajo"      : "Low",
        _          => priority ?? ""
    };
}
