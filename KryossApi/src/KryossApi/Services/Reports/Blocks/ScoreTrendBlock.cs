using System.Text;
namespace KryossApi.Services.Reports.Blocks;

public class ScoreTrendBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Evolución del Score" : "Score Evolution", data.Branding);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div style='text-align:center;padding:1.5em 0;'>");
        sb.AppendLine($"<div style='font-size:3.5em;font-weight:700;'>{data.AvgScore:F1}%</div>");

        if (data.PreviousMonthScore.HasValue)
        {
            var delta = data.AvgScore - data.PreviousMonthScore.Value;
            var arrow = delta > 0 ? "▲" : delta < 0 ? "▼" : "=";
            var color = delta > 0 ? "#008852" : delta < 0 ? "#C0392B" : "#6B7280";
            sb.AppendLine($"<div style='font-size:1.5em;color:{color};'>{arrow} {Math.Abs(delta):F1} pts</div>");
        }
        else
        {
            sb.AppendLine("<div style='font-size:1.2em;color:#6B7280;'>— BASELINE</div>");
        }
        sb.AppendLine("</div>");

        if (data.ScoreHistory != null && data.ScoreHistory.Count > 1)
            RenderSparkline(sb, data.ScoreHistory);

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static void RenderSparkline(StringBuilder sb, List<MonthlyScore> history)
    {
        var width = 500;
        var height = 120;
        var step = width / Math.Max(1, history.Count - 1);
        var points = new List<string>();

        for (int i = 0; i < history.Count; i++)
        {
            var x = i * step;
            var y = height - (int)(history[i].Score / 100m * height);
            points.Add($"{x},{y}");
        }

        sb.AppendLine($"<svg viewBox='0 0 {width} {height + 20}' style='width:100%;max-width:500px;margin:1em auto;display:block;'>");
        for (int pct = 25; pct <= 75; pct += 25)
        {
            var gy = height - (int)(pct / 100.0 * height);
            sb.AppendLine($"<line x1='0' y1='{gy}' x2='{width}' y2='{gy}' stroke='#E2E8F0' stroke-width='1'/>");
        }
        sb.AppendLine($"<polyline points='{string.Join(" ", points)}' fill='none' stroke='#008852' stroke-width='3'/>");
        for (int i = 0; i < history.Count; i++)
        {
            var x = i * step;
            var y = height - (int)(history[i].Score / 100m * height);
            sb.AppendLine($"<circle cx='{x}' cy='{y}' r='4' fill='#008852'/>");
            sb.AppendLine($"<text x='{x}' y='{height + 15}' text-anchor='middle' font-size='10' fill='#64748B'>{history[i].Month:MMM}</text>");
        }
        sb.AppendLine("</svg>");
    }
}
