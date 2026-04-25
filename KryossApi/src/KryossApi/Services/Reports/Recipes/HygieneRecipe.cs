using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class HygieneRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Hygiene | ReportDataNeeds.Enrichment | ReportDataNeeds.Ctas;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Auditoría Active Directory" : "Active Directory Audit";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("hygiene");
        yield return new KpiBlock(KpiVariant.Compact);
        yield return new HygieneBlock();
        yield return new TopFindingsBlock(topN: 10);
        yield return new IronSixBlock();
        yield return new CtaBlock();
    }
}
