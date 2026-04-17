using KryossApi.Services.CloudAssessment.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;

namespace KryossApi.Services.CloudAssessment.Recommendations;

/// <summary>
/// Placeholder stub for CA-6 Subsession B Azure recommendations.
///
/// TODO: replaced by B3 — this stub returns an empty list so the Azure
/// pipeline (Task B2) compiles and runs end-to-end while the real
/// finding-generation logic is still being authored.
///
/// B3 will replace the body with concrete rules over
/// <see cref="AzureInsights"/> covering ARM / Defender for Cloud / storage /
/// key vault / network / compute / policy service surfaces.
/// </summary>
public static class AzureRecommendations
{
    /// <summary>
    /// Stub entry point. Returns an empty list of findings.
    /// </summary>
    public static List<RecommendationResult> Generate(AzureInsights ins) =>
        new List<RecommendationResult>();
}
