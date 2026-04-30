using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class RiskAssessmentRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud
        | ReportDataNeeds.Hygiene | ReportDataNeeds.Enrichment
        | ReportDataNeeds.M365 | ReportDataNeeds.Ctas | ReportDataNeeds.ServiceCatalog
        | ReportDataNeeds.Cve | ReportDataNeeds.ExternalScan
        | ReportDataNeeds.DcHealth | ReportDataNeeds.Wan;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Evaluación de Riesgos y Amenazas" : "Risk & Threat Assessment";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("risk-assessment");
        yield return new SemaforoBlock();
        yield return new Top3RiskBlock();
        yield return new RiskSummaryBlock();
        yield return new RiskRoiBlock();
        if (data.HasCveData)
            yield return new VulnerabilityBlock(compact: false);
        if (data.HasExternalScanData)
            yield return new ExternalExposureBlock(compact: false);
        if (data.HasDcHealthData)
            yield return new DcHealthBlock(compact: true);
        if (data.HasWanData)
            yield return new WanHealthBlock(compact: true);
        yield return new ThreatVectorsBlock();
        yield return new TopFindingsBlock(topN: 10);
        yield return new CtaBlock();
    }
}
