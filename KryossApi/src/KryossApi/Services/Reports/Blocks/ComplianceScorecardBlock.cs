using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class ComplianceScorecardBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Scorecard de Cumplimiento" : "Compliance Scorecard";

    public int EstimateHeight(ReportData data)
    {
        int h = 200;
        h += data.FrameworkScores.Count * 30;
        if (data.CloudFrameworkScores != null) h += 100;
        if (data.Benchmarks != null) h += 120;
        return h;
    }

    public string RenderContent(ReportData data, ReportOptions options)
    {
        if (data.FrameworkScores.Count == 0) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;

        // Visual gauges grid
        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(auto-fill,minmax(140px,1fr));gap:14px;margin-bottom:20px'>");
        foreach (var fw in data.FrameworkScores.OrderByDescending(f => f.Score))
        {
            var color = fw.Score >= 80 ? "#15803D" : fw.Score >= 60 ? "#B45309" : "#991B1B";
            var bg = fw.Score >= 80 ? "#F0FDF4" : fw.Score >= 60 ? "#FFFBEB" : "#FEF2F2";
            var total = fw.PassCount + fw.FailCount;
            var pct = total > 0 ? 100.0 * fw.PassCount / total : 0;

            sb.AppendLine($"<div style='border:1px solid #E2E8F0;border-radius:8px;padding:14px;text-align:center;background:{bg}'>");

            // SVG ring gauge
            const int size = 80, stroke = 7;
            var radius = (size - stroke) / 2;
            var circ = 2 * Math.PI * radius;
            var offset = circ * (1 - pct / 100.0);
            sb.AppendLine($"<svg viewBox='0 0 {size} {size}' style='width:70px;height:70px;display:block;margin:0 auto 6px'>");
            sb.AppendLine($"<circle cx='{size / 2}' cy='{size / 2}' r='{radius}' fill='none' stroke='#E2E8F0' stroke-width='{stroke}'/>");
            sb.AppendLine($"<circle cx='{size / 2}' cy='{size / 2}' r='{radius}' fill='none' stroke='{color}' stroke-width='{stroke}' stroke-dasharray='{circ:F1}' stroke-dashoffset='{offset:F1}' transform='rotate(-90 {size / 2} {size / 2})' stroke-linecap='round'/>");
            sb.AppendLine($"<text x='{size / 2}' y='{size / 2 + 4}' text-anchor='middle' font-size='16' font-weight='700' fill='{color}' font-family='Montserrat,sans-serif'>{fw.Score:F0}</text>");
            sb.AppendLine("</svg>");

            sb.AppendLine($"<div style='font-size:10pt;font-weight:700;color:#0F172A'>{ReportHelpers.HtmlEncode(fw.Code)}</div>");
            sb.AppendLine($"<div style='font-size:7pt;color:#64748B'>{fw.PassCount} / {total} {(es ? "controles" : "controls")}</div>");
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");

        // Detail table
        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>Framework</th><th>{(es ? "Nombre" : "Name")}</th><th>Score</th><th>{(es ? "Aprobados" : "Passed")}</th><th>{(es ? "Fallidos" : "Failed")}</th><th>{(es ? "Grado" : "Grade")}</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var fw in data.FrameworkScores.OrderByDescending(f => f.Score))
        {
            var grade = ReportHelpers.GetGrade((decimal)fw.Score);
            var gradeClass = fw.Score >= 80 ? "pass" : fw.Score >= 60 ? "warn" : "fail";
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td><strong>{ReportHelpers.HtmlEncode(fw.Code)}</strong></td>");
            sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(fw.Name)}</td>");
            sb.AppendLine($"<td style='font-weight:700'>{fw.Score:F1}%</td>");
            sb.AppendLine($"<td class='pass'>{fw.PassCount}</td>");
            sb.AppendLine($"<td class='fail'>{fw.FailCount}</td>");
            sb.AppendLine($"<td class='{gradeClass}' style='font-weight:700'>{grade}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");

        // Cloud frameworks (if available)
        if (data.CloudFrameworkScores != null && data.CloudFrameworkScores.Count > 0)
        {
            sb.AppendLine($"<h3 style='margin-top:16px'>{(es ? "Cumplimiento Cloud" : "Cloud Compliance")}</h3>");
            sb.AppendLine("<table class='data-table'><thead><tr>");
            sb.AppendLine($"<th>Framework</th><th>Score</th><th>{(es ? "Aprobados" : "Passed")}</th><th>{(es ? "Fallidos" : "Failed")}</th>");
            sb.AppendLine("</tr></thead><tbody>");
            foreach (var cf in data.CloudFrameworkScores)
            {
                var fwName = cf.Framework?.Name ?? cf.FrameworkId.ToString()[..8];
                sb.AppendLine($"<tr><td><strong>{ReportHelpers.HtmlEncode(fwName)}</strong></td>");
                sb.AppendLine($"<td style='font-weight:700'>{cf.ScorePct:F1}%</td>");
                sb.AppendLine($"<td class='pass'>{cf.PassingControls}</td><td class='fail'>{cf.FailingControls}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        // Benchmark comparison (if available)
        if (data.Benchmarks != null)
        {
            RenderBenchmarks(sb, data, es);
        }

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

    private static void RenderBenchmarks(StringBuilder sb, ReportData data, bool es)
    {
        var bm = data.Benchmarks!;
        sb.AppendLine($"<h3 style='margin-top:16px'>{(es ? "Comparativa" : "Benchmark Comparison")}</h3>");
        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Métrica" : "Metric")}</th>");
        sb.AppendLine($"<th>{(es ? "Su Org" : "Your Org")}</th>");
        if (bm.FranchisePeers != null) sb.AppendLine($"<th>{(es ? "Pares Franquicia" : "Franchise Peers")}</th>");
        if (bm.IndustryBaseline != null) sb.AppendLine($"<th>{(es ? "Industria" : "Industry")}</th>");
        if (bm.GlobalKryoss != null) sb.AppendLine($"<th>Global</th>");
        sb.AppendLine("</tr></thead><tbody>");

        var metrics = new[] { ("overall", es ? "Score General" : "Overall Score"), ("endpoint", "Endpoint"), ("identity", es ? "Identidad" : "Identity"), ("data", es ? "Datos" : "Data") };
        foreach (var (key, label) in metrics)
        {
            var orgVal = key == "overall" ? data.AvgScore : (data.AreaScores?.GetValueOrDefault(key) ?? 0);
            sb.AppendLine($"<tr><td>{ReportHelpers.HtmlEncode(label)}</td><td style='font-weight:700'>{orgVal:F0}</td>");
            if (bm.FranchisePeers != null)
            {
                var val = bm.FranchisePeers.GetValueOrDefault(key);
                var cls = orgVal > val ? "pass" : orgVal < val ? "fail" : "";
                sb.AppendLine($"<td class='{cls}'>{val:F0}</td>");
            }
            if (bm.IndustryBaseline != null)
            {
                var val = bm.IndustryBaseline.GetValueOrDefault(key);
                var cls = orgVal > val ? "pass" : orgVal < val ? "fail" : "";
                sb.AppendLine($"<td class='{cls}'>{val:F0}</td>");
            }
            if (bm.GlobalKryoss != null)
            {
                var val = bm.GlobalKryoss.GetValueOrDefault(key);
                var cls = orgVal > val ? "pass" : orgVal < val ? "fail" : "";
                sb.AppendLine($"<td class='{cls}'>{val:F0}</td>");
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");
    }
}
