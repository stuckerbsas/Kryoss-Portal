using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class FrameworkRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        $"{options.FrameworkName} {(options.IsSpanish ? "Informe de Cumplimiento" : "Compliance Report")}";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("framework");
        yield return new FrameworkGaugeBlock();
        yield return new GapAnalysisBlock();
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: false);
        yield return new TimelineBlock();
    }
}
