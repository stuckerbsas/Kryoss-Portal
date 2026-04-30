using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class CLevelRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud
        | ReportDataNeeds.Hygiene | ReportDataNeeds.Enrichment
        | ReportDataNeeds.M365 | ReportDataNeeds.PreviousScore
        | ReportDataNeeds.Ctas | ReportDataNeeds.ScoreHistory | ReportDataNeeds.Network
        | ReportDataNeeds.Cve | ReportDataNeeds.Patch | ReportDataNeeds.ExternalScan
        | ReportDataNeeds.DcHealth | ReportDataNeeds.Wan;

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
        if (data.HasCveData)
            yield return new VulnerabilityBlock(compact: true);
        if (data.HasPatchData)
            yield return new PatchComplianceBlock(compact: true);
        if (data.HasExternalScanData)
            yield return new ExternalExposureBlock(compact: true);
        if (data.HasNetworkData)
            yield return new NetworkMiniBlock();
        if (data.HasDcHealthData)
            yield return new DcHealthBlock(compact: true);
        if (data.HasWanData)
            yield return new WanHealthBlock(compact: true);
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: true);
        yield return new CtaBlock();
    }
}
