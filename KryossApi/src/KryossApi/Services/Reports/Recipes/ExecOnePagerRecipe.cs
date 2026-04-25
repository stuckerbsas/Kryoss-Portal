using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class ExecOnePagerRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud
        | ReportDataNeeds.Hygiene | ReportDataNeeds.Enrichment
        | ReportDataNeeds.M365 | ReportDataNeeds.Ctas;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Resumen Ejecutivo" : "Executive One-Pager";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("exec-onepager");
        yield return new ExecOnePagerBlock();
        yield return new CtaBlock();
    }
}
