using System.Text;
namespace KryossApi.Services.Reports.Blocks;

public class DeltaBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        var critical = data.ControlResults.Count(r => r.Status == "fail" && r.Severity == "critical");
        var high = data.ControlResults.Count(r => r.Status == "fail" && r.Severity == "high");
        var medium = data.ControlResults.Count(r => r.Status == "fail" && r.Severity == "medium");
        var low = data.ControlResults.Count(r => r.Status == "fail" && r.Severity == "low");
        var totalPass = data.ControlResults.Count(r => r.Status == "pass");

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Estado Actual de Hallazgos" : "Current Findings Status", data.Branding);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value' style='color:#008852;'>{totalPass}</span><span class='stat-label'>{(es ? "Controles Aprobados" : "Passing Controls")}</span></div>");
        if (critical > 0)
            sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{critical}</span><span class='stat-label'>{(es ? "Críticos" : "Critical")}</span></div>");
        if (high > 0)
            sb.AppendLine($"<div class='stat warn-stat'><span class='stat-value'>{high}</span><span class='stat-label'>{(es ? "Altos" : "High")}</span></div>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{medium + low}</span><span class='stat-label'>{(es ? "Medio/Bajo" : "Medium/Low")}</span></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }
}
