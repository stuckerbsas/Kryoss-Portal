using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class AssetMatrixBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, options.IsSpanish ? "Matriz de Activos" : "Asset Matrix", data.Branding);
        sb.AppendLine("<div class='pb'>");
        ReportHelpers.AppendAssetMatrix(sb, data.Runs, data.Branding, options.Lang);
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }
}
