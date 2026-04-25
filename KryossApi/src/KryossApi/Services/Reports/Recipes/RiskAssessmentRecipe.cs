using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class RiskAssessmentRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud
        | ReportDataNeeds.Hygiene | ReportDataNeeds.Enrichment
        | ReportDataNeeds.M365 | ReportDataNeeds.Ctas | ReportDataNeeds.ServiceCatalog;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Evaluación de Riesgos y Amenazas" : "Risk & Threat Assessment";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("risk-assessment");
        yield return new SemaforoBlock();
        yield return new Top3RiskBlock();
        yield return new RiskSummaryBlock();
        yield return new RiskRoiBlock();
        yield return new ThreatVectorsBlock();
        yield return new TopFindingsBlock(topN: 10);
        yield return new CtaBlock();
    }
}
