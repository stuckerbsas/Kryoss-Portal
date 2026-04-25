using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class SectionHeaderBlock : IFlowBlock
{
    private readonly string _title;
    public SectionHeaderBlock(string title) => _title = title;

    public string? SectionTitle(ReportOptions options) => _title;

    public int EstimateHeight(ReportData data) => 160;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<h3 style='font-size:18pt;text-align:center;border:none;padding:60px 0'>{ReportHelpers.HtmlEncode(_title)}</h3>");
        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, _title, data.Branding);
        sb.AppendLine("<div class='pb'>");
        sb.Append(RenderContent(data, options));
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }
}
