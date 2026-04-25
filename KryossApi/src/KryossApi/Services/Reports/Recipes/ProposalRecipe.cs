using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class ProposalRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud
        | ReportDataNeeds.Hygiene | ReportDataNeeds.Enrichment
        | ReportDataNeeds.M365 | ReportDataNeeds.ServiceCatalog;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Propuesta Comercial de Remediación" : "Remediation Business Proposal";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("proposal");
        yield return new KpiBlock(KpiVariant.Business);
        yield return new SemaforoBlock();
        yield return new RiskRoiBlock();
        yield return new DecisionsMatrixBlock();
        yield return new TopFindingsBlock(topN: 10);
        yield return new GapAnalysisBlock();
        yield return new ServiceCatalogBlock(showPricing: true, tierGrid: true);
        yield return new TimelineBlock();
    }
}
