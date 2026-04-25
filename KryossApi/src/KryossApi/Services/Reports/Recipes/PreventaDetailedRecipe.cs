using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class PreventaDetailedRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud
        | ReportDataNeeds.Hygiene | ReportDataNeeds.Enrichment
        | ReportDataNeeds.ServiceCatalog | ReportDataNeeds.M365;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Propuesta de Seguridad" : "Security Proposal";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("preventa-detailed");
        yield return new RiskScoreBlock();
        yield return new CategoryBreakdownBlock();
        yield return new ThreatVectorsBlock();
        yield return new TopFindingsBlock(topN: 10);
        yield return new RiskRoiBlock();
        yield return new MethodologyBlock();
        yield return new ServiceCatalogBlock(showPricing: false);
        yield return new TimelineBlock();
    }
}
