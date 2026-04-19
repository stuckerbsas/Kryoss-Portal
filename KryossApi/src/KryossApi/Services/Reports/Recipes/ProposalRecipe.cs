using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class ProposalRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Propuesta Comercial de Remediación" : "Remediation Business Proposal";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("proposal");
        yield return new SemaforoBlock();
        yield return new TopFindingsBlock(topN: 10);
        yield return new GapAnalysisBlock();
        yield return new ServiceCatalogBlock(showPricing: true);
        yield return new TimelineBlock();
    }
}
