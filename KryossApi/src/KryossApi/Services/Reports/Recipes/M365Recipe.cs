using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class M365Recipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.Runs | ReportDataNeeds.M365 | ReportDataNeeds.Cloud | ReportDataNeeds.Ctas;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Seguridad Microsoft 365 y Copilot" : "M365 Security & Copilot Readiness";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("m365");
        yield return new KpiBlock(KpiVariant.Business);
        yield return new M365Block();
        yield return new M365CopilotBlock();
        yield return new CloudPostureBlock(compact: true);
        yield return new CtaBlock();
    }
}

internal class M365CopilotBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options) =>
        M365Block.RenderCopilotPage(data, options);
}
