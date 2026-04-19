using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class CLevelRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Informe Ejecutivo C-Level" : "C-Level Security Briefing";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("c-level");
        yield return new SemaforoBlock();
        yield return new KpiBlock();
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: true);
        yield return new CtaBlock();
    }
}
