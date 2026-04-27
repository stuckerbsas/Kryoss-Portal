using System.Globalization;
using System.Text;
using KryossApi.Services.CloudAssessment.Benchmarks;

namespace KryossApi.Services.CloudAssessment;

/// <summary>
/// Renders the Benchmark Analysis section as self-contained HTML.
/// Used by the standalone benchmark report endpoint; does not modify
/// the Windows endpoint ReportService chain.
/// </summary>
internal static class BenchmarkReportBuilder
{
    public static string Build(
        BenchmarkReport report,
        string organizationName,
        string? industryLabel,
        DateTime generatedAt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='en'><head><meta charset='utf-8'>");
        sb.AppendLine("<meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none';\">");
        sb.AppendLine($"<title>Benchmark Analysis — {Esc(organizationName)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(@"
            :root { --kryoss-green:#008852; --kryoss-light:#A2C564; --kryoss-dark:#3D4043; }
            body { font-family:'Montserrat', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
                   margin:0; padding:32px; color:#0F172A; background:#F8FAFC; }
            .container { max-width:900px; margin:0 auto; background:#fff; padding:32px;
                         border-radius:8px; box-shadow:0 1px 3px rgba(0,0,0,0.1); }
            h1 { font-size:22pt; color:var(--kryoss-green); margin:0 0 4px 0; }
            h2 { font-size:14pt; color:var(--kryoss-dark); margin:28px 0 10px 0;
                 border-bottom:2px solid var(--kryoss-green); padding-bottom:4px; }
            .meta { color:#64748B; font-size:10pt; margin-bottom:24px; }
            .availability { display:grid; grid-template-columns:repeat(3,1fr); gap:12px; margin:16px 0; }
            .avail-card { border:1px solid #E2E8F0; border-radius:6px; padding:12px;
                          font-size:10pt; background:#F8FAFC; }
            .avail-card.on { border-color:#A2C564; background:#F0F9E8; }
            .avail-card .label { color:#64748B; font-size:9pt; text-transform:uppercase; letter-spacing:0.5px; }
            .avail-card .value { font-size:13pt; font-weight:600; margin-top:4px; }
            .avail-card .sub { color:#64748B; font-size:9pt; margin-top:2px; }
            table { width:100%; border-collapse:collapse; margin:12px 0; font-size:10pt; }
            th { text-align:left; padding:8px 10px; background:#F1F5F9; color:#334155;
                 font-weight:600; border-bottom:2px solid #CBD5E1; }
            td { padding:8px 10px; border-bottom:1px solid #E2E8F0; vertical-align:middle; }
            .num { text-align:right; font-variant-numeric:tabular-nums; }
            .pill { display:inline-block; padding:2px 8px; border-radius:10px; font-size:9pt; font-weight:600; }
            .pill.above { background:#D1FAE5; color:#065F46; }
            .pill.at { background:#DBEAFE; color:#1E40AF; }
            .pill.below { background:#FEE2E2; color:#991B1B; }
            .pill.insufficient { background:#F1F5F9; color:#64748B; }
            .note { font-size:9pt; color:#64748B; margin-top:16px; padding:10px;
                    background:#F8FAFC; border-left:3px solid var(--kryoss-green); }
            .dash { color:#94A3B8; }
        ");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<div class='container'>");

        sb.AppendLine("<h1>Benchmark Analysis</h1>");
        sb.AppendLine($"<p class='meta'>{Esc(organizationName)}");
        if (!string.IsNullOrWhiteSpace(industryLabel))
            sb.Append($" &middot; {Esc(industryLabel)}");
        sb.AppendLine($" &middot; {generatedAt:yyyy-MM-dd HH:mm} UTC</p>");

        // Availability cards
        sb.AppendLine("<h2>Benchmark availability</h2>");
        sb.AppendLine("<div class='availability'>");
        var av = report.Availability;
        sb.Append(AvailabilityCard("Franchise peers", av.FranchiseBenchmarkAvailable,
            $"{av.FranchiseOrgCount} orgs",
            av.FranchiseBenchmarkAvailable
                ? "Active"
                : $"Need {Math.Max(0, av.FranchiseThreshold - av.FranchiseOrgCount)} more"));
        sb.Append(AvailabilityCard("Industry", av.IndustryBenchmarkAvailable,
            av.IndustryCode ?? "Not set",
            av.IndustryBenchmarkAvailable ? "Baseline loaded" : "Set industry to enable"));
        sb.Append(AvailabilityCard("Global Kryoss", av.GlobalBenchmarkAvailable,
            av.GlobalBenchmarkAvailable ? $"{av.GlobalOrgCount} orgs" : $"{av.GlobalOrgCount} / {av.GlobalThreshold}",
            av.GlobalBenchmarkAvailable ? "Active" : "Dataset growing"));
        sb.AppendLine("</div>");

        // Metric tables by category
        RenderTable(sb, "Overall score", report.Metrics.Where(m => m.Category == "overall").ToList(), av);
        RenderTable(sb, "Areas", report.Metrics.Where(m => m.Category == "area").ToList(), av);
        RenderTable(sb, "Compliance frameworks", report.Metrics.Where(m => m.Category == "framework").ToList(), av);
        RenderTable(sb, "Operational metrics", report.Metrics.Where(m => m.Category == "metric").ToList(), av);

        sb.AppendLine("<div class='note'>");
        sb.Append($"Franchise benchmarks activate with {av.FranchiseThreshold}+ orgs in a franchise; ");
        sb.Append($"global benchmarks with {av.GlobalThreshold}+ orgs. ");
        sb.Append("Per-organization values are never shared outside their franchise — only aggregates.");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    private static string AvailabilityCard(string label, bool available, string value, string sub)
    {
        var cls = available ? "avail-card on" : "avail-card";
        return $"<div class='{cls}'><div class='label'>{Esc(label)}</div>" +
               $"<div class='value'>{Esc(value)}</div>" +
               $"<div class='sub'>{Esc(sub)}</div></div>";
    }

    private static void RenderTable(
        StringBuilder sb,
        string heading,
        List<MetricComparison> rows,
        BenchmarkAvailability av)
    {
        if (rows.Count == 0) return;

        sb.AppendLine($"<h2>{Esc(heading)}</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th>Metric</th>");
        sb.AppendLine("<th class='num'>You</th>");
        sb.AppendLine("<th class='num'>Franchise</th>");
        sb.AppendLine("<th class='num'>Industry</th>");
        sb.AppendLine("<th class='num'>Global</th>");
        sb.AppendLine("<th>Verdict</th>");
        sb.AppendLine("</tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var m in rows)
        {
            var isCount = m.MetricKey.EndsWith("_count");
            var unit = m.Category is "area" or "overall" ? " / 5" : isCount ? "" : "%";

            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{Esc(m.DisplayName)}</td>");
            sb.AppendLine($"<td class='num'>{FormatCell(m.OrgValue, unit, isCount)}</td>");
            sb.AppendLine(av.FranchiseBenchmarkAvailable
                ? $"<td class='num'>{FormatCell(m.FranchiseAvg, unit, isCount)} <span class='dash'>{FormatPercentile(m.FranchisePercentile)}</span></td>"
                : "<td class='num dash'>—</td>");
            sb.AppendLine(av.IndustryBenchmarkAvailable
                ? $"<td class='num'>{FormatCell(m.IndustryP50, unit, isCount)} <span class='dash'>{FormatPercentile(m.IndustryPercentile)}</span></td>"
                : "<td class='num dash'>—</td>");
            sb.AppendLine(av.GlobalBenchmarkAvailable
                ? $"<td class='num'>{FormatCell(m.GlobalAvg, unit, isCount)} <span class='dash'>{FormatPercentile(m.GlobalPercentile)}</span></td>"
                : "<td class='num dash'>—</td>");
            sb.AppendLine($"<td>{VerdictPill(m.Verdict)}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");
    }

    private static string FormatCell(decimal? value, string unit, bool isCount)
    {
        if (value is null) return "<span class='dash'>—</span>";
        var v = isCount ? Math.Round(value.Value).ToString(CultureInfo.InvariantCulture)
                        : value.Value.ToString("F1", CultureInfo.InvariantCulture);
        return $"{v}<span class='dash'>{unit}</span>";
    }

    private static string FormatPercentile(decimal? p)
    {
        if (p is null) return "";
        return $"P{(int)Math.Round(p.Value)}";
    }

    private static string VerdictPill(string verdict) => verdict switch
    {
        "above_peer" => "<span class='pill above'>Above peer</span>",
        "at_peer" => "<span class='pill at'>At peer</span>",
        "below_peer" => "<span class='pill below'>Below peer</span>",
        _ => "<span class='pill insufficient'>Insufficient data</span>",
    };

    private static string Esc(string? s) =>
        s is null ? "" : System.Net.WebUtility.HtmlEncode(s);
}
