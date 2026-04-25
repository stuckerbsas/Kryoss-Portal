using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class MonthlyRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud
        | ReportDataNeeds.PreviousScore | ReportDataNeeds.ScoreHistory
        | ReportDataNeeds.Hygiene;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Informe de Progreso Mensual" : "Monthly Progress Report";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("monthly");
        yield return new KpiBlock();
        yield return new ScoreTrendBlock(showDelta: true);
        yield return new FrameworkGaugeBlock();
        yield return new CategoryBreakdownBlock();
        yield return new TopFindingsBlock(topN: 10, splitResolved: true);
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: true);
    }
}
