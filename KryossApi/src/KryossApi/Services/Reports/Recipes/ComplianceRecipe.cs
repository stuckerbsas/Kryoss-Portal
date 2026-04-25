using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class ComplianceRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud | ReportDataNeeds.Hygiene;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Scorecard de Cumplimiento" : "Compliance Scorecard";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("compliance");
        yield return new ComplianceScorecardBlock();
        yield return new FrameworkGaugeBlock();
        yield return new CategoryBreakdownBlock();
        yield return new GapAnalysisBlock();
        yield return new ControlDetailBlock();
        yield return new EvidenceAppendixBlock();
        yield return new MethodologyBlock(AudiencePerspective.Audit);
    }
}
