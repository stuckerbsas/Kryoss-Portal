using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class TechnicalRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud | ReportDataNeeds.Hygiene
        | ReportDataNeeds.Cve | ReportDataNeeds.Patch | ReportDataNeeds.ExternalScan
        | ReportDataNeeds.DcHealth | ReportDataNeeds.Wan | ReportDataNeeds.Remediation;

    public string ReportTitle(ReportOptions options) =>
        options.FrameworkName != null
            ? $"{options.FrameworkName} {(options.IsSpanish ? "Informe Técnico" : "Technical Report")}"
            : (options.IsSpanish ? "Informe Técnico de Seguridad" : "Security Technical Report");

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("technical");
        yield return new AssetMatrixBlock();
        yield return new CategoryBreakdownBlock();
        yield return new TopFindingsBlock(topN: 10);
        yield return new ControlDetailBlock();
        yield return new IronSixBlock();
        if (data.HasCveData)
            yield return new VulnerabilityBlock(compact: false);
        if (data.HasPatchData)
            yield return new PatchComplianceBlock(compact: false);
        if (data.HasExternalScanData)
            yield return new ExternalExposureBlock(compact: false);
        if (data.HasDcHealthData)
            yield return new DcHealthBlock(compact: false);
        if (data.HasWanData)
            yield return new WanHealthBlock(compact: false);
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: false);
        if (data.HasRemediationData)
            yield return new RemediationStatusBlock(compact: false);
        yield return new GapAnalysisBlock();
        yield return new EvidenceAppendixBlock();
        yield return new MethodologyBlock(AudiencePerspective.Technical);
    }
}
