using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class MonthlyRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Informe de Progreso Mensual" : "Monthly Progress Report";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("monthly");
        yield return new ScoreTrendBlock();
        yield return new DeltaBlock();
        yield return new TopFindingsBlock(topN: 10, splitResolved: true);
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: true);
        yield return new KpiBlock();
    }
}
