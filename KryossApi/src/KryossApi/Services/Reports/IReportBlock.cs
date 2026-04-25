namespace KryossApi.Services.Reports;

[Flags]
public enum ReportDataNeeds
{
    None             = 0,
    Runs             = 1 << 0,
    ControlResults   = 1 << 1,
    FrameworkScores  = 1 << 2,
    Enrichment       = 1 << 3,
    PreviousScore    = 1 << 4,
    ScoreHistory     = 1 << 5,
    Hygiene          = 1 << 6,
    M365             = 1 << 7,
    Cloud            = 1 << 8,
    Network          = 1 << 9,
    Ctas             = 1 << 10,
    ServiceCatalog   = 1 << 11,

    EndpointCore = Runs | ControlResults | FrameworkScores,
    All = Runs | ControlResults | FrameworkScores | Enrichment | PreviousScore
        | ScoreHistory | Hygiene | M365 | Cloud | Network | Ctas | ServiceCatalog
}

public interface IReportBlock
{
    string Render(ReportData data, ReportOptions options);
}

/// <summary>
/// Blocks that can share a page with other blocks. ReportComposer packs
/// them onto the current page if they fit, or starts a new page if not.
/// Blocks that DON'T implement this get their own page (legacy behavior).
/// </summary>
public interface IFlowBlock : IReportBlock
{
    string? SectionTitle(ReportOptions options);
    int EstimateHeight(ReportData data);
    string RenderContent(ReportData data, ReportOptions options);
}

public interface IReportRecipe
{
    string ReportTitle(ReportOptions options);
    IEnumerable<IReportBlock> GetBlocks(ReportData data);
    ReportDataNeeds DataNeeds => ReportDataNeeds.All;
}

public interface ISelfContainedRecipe : IReportRecipe
{
    ReportData BuildSyntheticData();
}
