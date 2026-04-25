using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class AssetMatrixBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Matriz de Activos" : "Asset Matrix";

    public int EstimateHeight(ReportData data) => 100 + data.Runs.Count * 24;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        ReportHelpers.AppendAssetMatrix(sb, data.Runs, data.Branding, options.Lang);
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
