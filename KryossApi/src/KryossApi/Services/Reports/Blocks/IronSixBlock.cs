using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class IronSixBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, options.IsSpanish ? "Los 6 de Hierro" : "The Iron Six", data.Branding);
        sb.AppendLine("<div class='pb'>");
        ReportHelpers.AppendSixIronsHardeningAudit(sb, data.Runs, data.ControlResults, data.Hygiene, options.Lang);
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }
}
