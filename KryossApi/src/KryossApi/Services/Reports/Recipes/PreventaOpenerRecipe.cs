using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class PreventaOpenerRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud
        | ReportDataNeeds.Hygiene | ReportDataNeeds.Enrichment;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Evaluación de Riesgo" : "Risk Assessment";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("preventa-opener");
        yield return new KpiBlock(KpiVariant.Compact);
        yield return new RiskScoreBlock();
        yield return new Top3RiskBlock();
        yield return new ThreatVectorsBlock();
        if (data.HasNetworkData)
            yield return new NetworkMiniBlock();
        yield return new NextStepBlock();
    }
}
