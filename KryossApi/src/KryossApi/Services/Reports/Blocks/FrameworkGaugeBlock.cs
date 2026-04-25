using System.Text;
namespace KryossApi.Services.Reports.Blocks;

public class FrameworkGaugeBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.FrameworkCode != null
            ? (options.IsSpanish ? $"Cumplimiento {options.FrameworkName}" : $"{options.FrameworkName} Compliance")
            : (options.IsSpanish ? "Cumplimiento por Framework" : "Framework Compliance");

    public int EstimateHeight(ReportData data)
    {
        int h = 40;
        if (data.FrameworkScores.Count > 0) h += data.FrameworkScores.Count * 40;
        if (data.PreviousMonthScore.HasValue) h += 30;
        if (data.CloudFrameworkScores != null && data.CloudFrameworkScores.Count > 0) h += 120;
        return Math.Min(h, 500);
    }

    public string RenderContent(ReportData data, ReportOptions options)
    {
        if (data.FrameworkScores.Count == 0 && options.FrameworkCode == null)
            return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;

        if (options.FrameworkCode != null)
        {
            var fw = data.FrameworkScores.FirstOrDefault(f => f.Code == options.FrameworkCode);
            if (fw != null)
            {
                var grade = ReportHelpers.GetGrade((decimal)fw.Score);
                var color = fw.Score >= 80 ? "#008852" : fw.Score >= 60 ? "#D97706" : "#C0392B";
                sb.AppendLine("<div style='text-align:center;padding:2em 0;'>");
                sb.AppendLine($"<div style='font-size:4em;font-weight:700;color:{color};'>{fw.Score:F1}%</div>");
                sb.AppendLine($"<div style='font-size:2em;color:{color};margin:0.25em 0;'>{grade}</div>");
                sb.AppendLine($"<div style='font-size:1.1em;color:#64748B;'>{fw.PassCount} {(es ? "aprobados" : "passed")} / {fw.FailCount} {(es ? "fallidos" : "failed")}</div>");
                sb.AppendLine("</div>");
            }

            if (data.CloudFrameworkScores != null)
            {
                var cloudFw = data.CloudFrameworkScores.FirstOrDefault(f =>
                    f.Framework?.Name != null &&
                    f.Framework.Name.Contains(options.FrameworkCode, StringComparison.OrdinalIgnoreCase));

                if (cloudFw != null)
                {
                    var cloudColor = cloudFw.ScorePct >= 80 ? "#008852" : cloudFw.ScorePct >= 60 ? "#D97706" : "#C0392B";
                    sb.AppendLine("<div style='text-align:center;padding:1em 0;border-top:1px solid #E2E8F0;margin-top:1em;'>");
                    sb.AppendLine($"<div style='font-size:0.9em;color:#64748B;margin-bottom:0.5em;'>{(es ? "Score Cloud" : "Cloud Score")}</div>");
                    sb.AppendLine($"<div style='font-size:2.5em;font-weight:700;color:{cloudColor};'>{cloudFw.ScorePct:F1}%</div>");
                    sb.AppendLine($"<div style='font-size:1.1em;color:{cloudColor};'>{cloudFw.Grade}</div>");
                    sb.AppendLine($"<div style='font-size:0.9em;color:#64748B;'>{cloudFw.PassingControls} {(es ? "aprobados" : "passed")} / {cloudFw.FailingControls} {(es ? "fallidos" : "failed")}</div>");
                    sb.AppendLine("</div>");
                }
            }
        }
        else
        {
            ReportHelpers.AppendFrameworkBars(sb, data.FrameworkScores);
        }

        if (data.PreviousMonthScore.HasValue)
        {
            var delta = data.AvgScore - data.PreviousMonthScore.Value;
            var arrow = delta > 0 ? "&#9650;" : delta < 0 ? "&#9660;" : "=";
            var color = delta > 0 ? "#008852" : delta < 0 ? "#C0392B" : "#6B7280";
            sb.AppendLine($"<p style='color:{color};font-size:1.1em;margin-top:1em;text-align:center;'>{arrow} {Math.Abs(delta):F1} pts vs {(es ? "período anterior" : "previous period")}</p>");
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
}
