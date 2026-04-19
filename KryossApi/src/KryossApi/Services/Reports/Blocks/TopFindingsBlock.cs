using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class TopFindingsBlock : IReportBlock
{
    private readonly int _topN;
    private readonly bool _splitResolved;

    public TopFindingsBlock(int topN = 10, bool splitResolved = false)
    {
        _topN = topN;
        _splitResolved = splitResolved;
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, options.IsSpanish ? "Hallazgos Críticos" : "Critical Findings", data.Branding);
        sb.AppendLine("<div class='pb'>");
        ReportHelpers.AppendTop10CriticalFindings(sb, data.ControlResults, options.Lang);
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }
}
