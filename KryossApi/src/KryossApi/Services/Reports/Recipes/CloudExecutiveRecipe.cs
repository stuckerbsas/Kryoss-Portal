using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class CloudExecutiveRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.Runs | ReportDataNeeds.Cloud | ReportDataNeeds.M365 | ReportDataNeeds.Ctas;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Informe Ejecutivo Cloud" : "Cloud Executive Report";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("cloud-executive");
        yield return new KpiBlock(KpiVariant.Business);
        yield return new CloudExecutiveBlock();
        yield return new FrameworkGaugeBlock();
        yield return new CtaBlock();
    }
}
