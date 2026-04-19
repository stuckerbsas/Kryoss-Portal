using KryossApi.Services.CopilotReadiness.Recommendations;

namespace KryossApi.Services.CopilotReadiness.Pipelines;

public class SharepointSiteResult
{
    public string SiteUrl { get; init; } = string.Empty;
    public string? SiteTitle { get; init; }
    public int TotalFiles { get; init; }
    public int LabeledFiles { get; init; }
    public int OversharedFiles { get; init; }
    public string? RiskLevel { get; init; }
    public List<string> TopLabels { get; init; } = [];
}

public class ExternalUserResult
{
    public string UserPrincipal { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? EmailDomain { get; init; }
    public DateTimeOffset? LastSignIn { get; init; }
    public string? RiskLevel { get; init; }
    public int SitesAccessed { get; init; }
    public string? HighestPermission { get; init; }
}

public class PipelineResult
{
    public string PipelineName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty; // ok | partial | failed | no_consent
    public string? Error { get; init; }
    public List<RecommendationResult> Findings { get; init; } = [];
    public Dictionary<string, string> Metrics { get; init; } = [];
    public List<SharepointSiteResult> SharepointSites { get; init; } = [];
    public List<ExternalUserResult> ExternalUsers { get; init; } = [];

    /// <summary>
    /// Typed insights bag populated by the pipeline. Used by BusinessRules
    /// for cross-check logic. Null when not populated or pipeline failed.
    /// Cast to the appropriate type (IdentityInsights, EndpointInsights, etc.).
    /// </summary>
    public object? Insights { get; set; }
}
