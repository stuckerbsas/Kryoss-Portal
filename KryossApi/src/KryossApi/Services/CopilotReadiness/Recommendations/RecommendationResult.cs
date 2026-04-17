namespace KryossApi.Services.CopilotReadiness.Recommendations;

public class RecommendationResult
{
    public string Service { get; init; } = string.Empty;
    public string Feature { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string? Observation { get; init; }
    public string? Recommendation { get; init; }
    public string? LinkText { get; init; }
    public string? LinkUrl { get; init; }

    // Factory methods

    public static RecommendationResult Success(
        string service, string feature,
        string? observation = null, string? linkText = null, string? linkUrl = null) =>
        new()
        {
            Service = service,
            Feature = feature,
            Status = "success",
            Priority = "none",
            Observation = observation,
            LinkText = linkText,
            LinkUrl = linkUrl
        };

    public static RecommendationResult ActionRequired(
        string service, string feature,
        string priority,
        string? observation = null, string? recommendation = null,
        string? linkText = null, string? linkUrl = null) =>
        new()
        {
            Service = service,
            Feature = feature,
            Status = "action_required",
            Priority = priority,
            Observation = observation,
            Recommendation = recommendation,
            LinkText = linkText,
            LinkUrl = linkUrl
        };

    public static RecommendationResult Warning(
        string service, string feature,
        string? observation = null, string? recommendation = null,
        string? linkText = null, string? linkUrl = null) =>
        new()
        {
            Service = service,
            Feature = feature,
            Status = "warning",
            Priority = "medium",
            Observation = observation,
            Recommendation = recommendation,
            LinkText = linkText,
            LinkUrl = linkUrl
        };

    public static RecommendationResult Disabled(
        string service, string feature,
        string? observation = null, string? recommendation = null,
        string? linkText = null, string? linkUrl = null) =>
        new()
        {
            Service = service,
            Feature = feature,
            Status = "disabled",
            Priority = "high",
            Observation = observation,
            Recommendation = recommendation,
            LinkText = linkText,
            LinkUrl = linkUrl
        };

    public static RecommendationResult NotLicensed(
        string service, string feature,
        string? observation = null, string? recommendation = null,
        string? linkText = null, string? linkUrl = null) =>
        new()
        {
            Service = service,
            Feature = feature,
            Status = "not_licensed",
            Priority = "info",
            Observation = observation,
            Recommendation = recommendation,
            LinkText = linkText,
            LinkUrl = linkUrl
        };

    public static RecommendationResult PermissionRequired(
        string service, string feature,
        string? observation = null, string? recommendation = null,
        string? linkText = null, string? linkUrl = null) =>
        new()
        {
            Service = service,
            Feature = feature,
            Status = "permission_required",
            Priority = "info",
            Observation = observation,
            Recommendation = recommendation,
            LinkText = linkText,
            LinkUrl = linkUrl
        };

    /// <summary>
    /// Informational finding that is neither a success nor an action item —
    /// e.g. "5 subscriptions scanned, multi-region footprint detected" or
    /// "empty subscription". Used by pipelines that want to emit context
    /// without implying PASS/WARN/FAIL.
    /// </summary>
    public static RecommendationResult Insight(
        string service, string feature,
        string? observation = null, string? recommendation = null,
        string? linkText = null, string? linkUrl = null) =>
        new()
        {
            Service = service,
            Feature = feature,
            Status = "insight",
            Priority = "informational",
            Observation = observation,
            Recommendation = recommendation,
            LinkText = linkText,
            LinkUrl = linkUrl
        };
}
