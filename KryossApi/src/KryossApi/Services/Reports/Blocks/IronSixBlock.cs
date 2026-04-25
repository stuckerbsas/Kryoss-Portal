using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class IronSixBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Los 6 de Hierro" : "The Iron Six";

    public int EstimateHeight(ReportData data) => 300;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        ReportHelpers.AppendSixIronsHardeningAudit(sb, data.Runs, data.ControlResults, data.Hygiene, options.Lang);
        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, SectionTitle(options)!, data.Branding);
        sb.AppendLine("<div class='pb'>");
        sb.Append(RenderContent(data, options));
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }
}
