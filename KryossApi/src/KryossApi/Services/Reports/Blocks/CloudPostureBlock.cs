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

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Postura Cloud" : "Cloud Posture", data.Branding);
        sb.AppendLine("<div class='pb'>");

        if (data.AreaScores != null && data.AreaScores.Count > 0)
        {
            RenderRadarChart(sb, data.AreaScores, es);
            RenderAreaScoreCards(sb, data.AreaScores, es);
        }

        if (data.CloudFindings != null && data.CloudFindings.Count > 0)
        {
            if (_compact)
            {
                var critical = data.CloudFindings
                    .Where(f => f.Priority is "critical" or "high")
                    .OrderByDescending(f => f.Priority == "critical" ? 2 : 1)
                    .Take(3)
                    .ToList();
                if (critical.Count > 0)
                {
                    sb.AppendLine($"<h3>{ReportHelpers.HtmlEncode(es ? "Hallazgos Críticos Cloud" : "Critical Cloud Findings")}</h3>");
                    RenderFindingsList(sb, critical, es);
                }
            }
            else
            {
                foreach (var area in new[] { "identity", "endpoint", "data", "productivity", "azure" })
                {
                    var areaFindings = data.CloudFindings.Where(f => f.Area == area).ToList();
                    if (areaFindings.Count > 0)
                        RenderAreaSection(sb, area, areaFindings, es);
                }
            }
        }

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    // ── Radar chart (SVG pentagon) ──────────────────────────────────────────

    private static void RenderRadarChart(StringBuilder sb, Dictionary<string, decimal> areaScores, bool es)
    {
        // 5 axes: identity, endpoint, data, productivity, azure
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
        // Start at top (-PI/2)
        const double startAngle = -Math.PI / 2;

        static (double x, double y) Point(double angle, double radius, int cx, int cy) =>
            (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));

        sb.AppendLine("<div class='radar-container' style='text-align:center;margin:12px 0'>");
        sb.AppendLine("<svg viewBox='0 0 400 400' xmlns='http://www.w3.org/2000/svg' style='max-width:280px;display:inline-block'>");

        // Grid rings at 25%, 50%, 75%, 100%
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

        // Axis lines from center to each vertex
        for (int i = 0; i < 5; i++)
        {
            var angle = startAngle + i * step;
            var (x, y) = Point(angle, r, cx, cy);
            sb.AppendLine($"<line x1='{cx}' y1='{cy}' x2='{x:F1}' y2='{y:F1}' stroke='#d1d5db' stroke-width='1'/>");
        }

        // Data polygon
        var dataPoints = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var key = axes[i].Item1;
            var score = areaScores.TryGetValue(key, out var s) ? (double)s : 0.0;
            var angle = startAngle + i * step;
            var (x, y) = Point(angle, r * score / 100.0, cx, cy);
            dataPoints.Add($"{x:F1},{y:F1}");
        }
        sb.AppendLine($"<polygon points='{string.Join(" ", dataPoints)}' fill='#006536' fill-opacity='0.2' stroke='#006536' stroke-width='2'/>");

        // Score dots + axis labels
        for (int i = 0; i < 5; i++)
        {
            var (key, label) = axes[i];
            var score = areaScores.TryGetValue(key, out var s) ? (double)s : 0.0;
            var angle = startAngle + i * step;

            // Dot at score position
            var (dx, dy) = Point(angle, r * score / 100.0, cx, cy);
            sb.AppendLine($"<circle cx='{dx:F1}' cy='{dy:F1}' r='4' fill='#006536'/>");

            // Label at edge + small padding
            var (lx, ly) = Point(angle, r + 22, cx, cy);
            var anchor = lx < cx - 5 ? "end" : lx > cx + 5 ? "start" : "middle";
            sb.AppendLine($"<text x='{lx:F1}' y='{ly:F1}' text-anchor='{anchor}' dominant-baseline='middle' font-size='11' fill='#374151' font-family='Montserrat,sans-serif'>{ReportHelpers.HtmlEncode(label)}</text>");

            // Score label near the dot
            sb.AppendLine($"<text x='{dx:F1}' y='{dy - 8:F1}' text-anchor='middle' font-size='9' fill='#006536' font-weight='600' font-family='Montserrat,sans-serif'>{score:F0}</text>");
        }

        sb.AppendLine("</svg>");
        sb.AppendLine("</div>");
    }

    // ── Area score cards ────────────────────────────────────────────────────

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
            if (!areaScores.TryGetValue(key, out var score)) continue;
            var color = score >= 80 ? "#16a34a" : score >= 60 ? "#eab308" : "#ef4444";
            sb.AppendLine("<div style='border:1px solid #e5e7eb;border-radius:8px;padding:10px;text-align:center'>");
            sb.AppendLine($"<div style='font-size:18pt;font-weight:700;color:{color}'>{score:F0}</div>");
            sb.AppendLine($"<div style='font-size:8pt;color:#6b7280'>{ReportHelpers.HtmlEncode(label)}</div>");
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");
    }

    // ── Area section (full mode) ────────────────────────────────────────────

    private static void RenderAreaSection(StringBuilder sb, string area, List<CloudAssessmentFinding> findings, bool es)
    {
        sb.AppendLine($"<h3>{ReportHelpers.HtmlEncode(AreaLabel(area, es))}</h3>");
        RenderFindingsList(sb, findings, es);
    }

    // ── Findings list ───────────────────────────────────────────────────────

    private static void RenderFindingsList(StringBuilder sb, List<CloudAssessmentFinding> findings, bool es)
    {
        foreach (var f in findings)
        {
            var borderColor = f.Status == "action_required" ? "#ef4444"
                            : f.Status == "warning" ? "#eab308"
                            : "#16a34a";
            var bg = f.Status == "action_required" ? "#fafafa" : "#fafafa";

            sb.AppendLine($"<div style='margin-bottom:12px;padding:10px;border-left:3px solid {borderColor};background:{bg};border-radius:0 4px 4px 0'>");

            sb.AppendLine("<div style='display:flex;justify-content:space-between;align-items:center'>");
            sb.AppendLine($"<strong style='font-size:10pt'>{ReportHelpers.HtmlEncode(f.Feature)}</strong>");
            sb.AppendLine($"<span class='badge {SeverityBadgeClass(f.Priority)}'>{ReportHelpers.HtmlEncode(PriorityLabel(f.Priority, es))}</span>");
            sb.AppendLine("</div>");

            if (!string.IsNullOrEmpty(f.Observation))
                sb.AppendLine($"<div style='margin-top:4px;font-size:9pt;color:#4b5563'>{ReportHelpers.HtmlEncode(f.Observation)}</div>");

            if (!string.IsNullOrEmpty(f.Recommendation))
                sb.AppendLine($"<div style='margin-top:6px;font-size:9pt'><strong>{ReportHelpers.HtmlEncode(es ? "Acción:" : "Action:")}</strong> {ReportHelpers.HtmlEncode(f.Recommendation)}</div>");

            sb.AppendLine("</div>");
        }
    }

    // ── Static helpers ──────────────────────────────────────────────────────

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
