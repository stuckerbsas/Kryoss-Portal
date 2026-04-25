using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class CLevelRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud
        | ReportDataNeeds.Hygiene | ReportDataNeeds.Enrichment
        | ReportDataNeeds.M365 | ReportDataNeeds.PreviousScore
        | ReportDataNeeds.Ctas | ReportDataNeeds.ScoreHistory | ReportDataNeeds.Network;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Informe Ejecutivo C-Level" : "C-Level Security Briefing";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("c-level");
        yield return new SemaforoBlock();
        yield return new KpiBlock();
        yield return new FrameworkGaugeBlock();
        yield return new ScoreTrendBlock(showDelta: true);
        yield return new Top3RiskBlock();
        if (data.HasNetworkData)
            yield return new NetworkMiniBlock();
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: true);
        yield return new CtaBlock();
    }
}
