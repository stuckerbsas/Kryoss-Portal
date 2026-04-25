using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class FrameworkRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud;

    public string ReportTitle(ReportOptions options) =>
        $"{options.FrameworkName} {(options.IsSpanish ? "Informe de Cumplimiento" : "Compliance Report")}";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new FrameworkCoverBlock();
        yield return new FrameworkGaugeBlock();
        yield return new CategoryBreakdownBlock();
        yield return new GapAnalysisBlock();
        yield return new ControlDetailBlock();
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: false);
        yield return new EvidenceAppendixBlock();
        yield return new TimelineBlock();
    }
}
