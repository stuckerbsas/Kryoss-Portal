namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// Pre-computed Azure infrastructure metrics for the Cloud Assessment
/// (CA-6 Subsession B) Azure pipeline. Mirrors the shape of
/// <see cref="EndpointInsights"/>.
///
/// Produced once per scan and consumed by
/// <c>AzureRecommendations</c> (Task B3) to emit findings. The pipeline
/// also persists <see cref="Resources"/> to
/// <c>cloud_assessment_azure_resources</c> and serializes the counters
/// into the <c>PipelineResult.Metrics</c> dictionary using snake_case keys.
/// </summary>
public class AzureInsights
{
    // ============================================================
    // Subscription-level counters
    // ============================================================
    public int SubscriptionsScanned { get; set; }
    public int ResourcesTotal { get; set; }

    // Resource counts per (relevant) type
    public int VmsCount { get; set; }
    public int StorageAccountsCount { get; set; }
    public int KeyVaultsCount { get; set; }
    public int NsgCount { get; set; }
    public int PublicIpCount { get; set; }
    public int SqlDatabasesCount { get; set; }

    // ============================================================
    // Defender for Cloud
    // ============================================================
    public int AssessmentsHealthy { get; set; }
    public int AssessmentsUnhealthy { get; set; }
    public int AssessmentsNotApplicable { get; set; }

    /// <summary>
    /// Secure score percentage (0..100). Null when the secureScores/ascScore
    /// endpoint does not expose a value for the subscription (e.g. Defender
    /// for Cloud not enabled or caller lacks read permission).
    /// </summary>
    public decimal? SecureScorePct { get; set; }

    // ============================================================
    // Public exposure
    // ============================================================
    public int StorageAccountsPublicBlob { get; set; }
    public int StorageAccountsHttpEnabled { get; set; }
    public int StorageAccountsNoSoftDelete { get; set; }
    public int NsgAnyAnyAllowRules { get; set; }

    // ============================================================
    // Key Vault hygiene
    // ============================================================
    public int KeyVaultsNoSoftDelete { get; set; }
    public int KeyVaultsNoPurgeProtection { get; set; }

    // ============================================================
    // Virtual Machines
    // ============================================================
    public int VmsUnencryptedOsDisk { get; set; }
    public int VmsWithoutManagedIdentity { get; set; }

    // ============================================================
    // Azure Policy
    // ============================================================
    /// <summary>
    /// Non-compliant resources across all policies in a subscription.
    /// Null when the policy collector was skipped (403/404 tolerated) so
    /// the metric is only emitted when we actually have a number.
    /// </summary>
    public int? PolicyNonCompliantResources { get; set; }

    // ============================================================
    // Per-subscription breakdown + full resource rows (persisted at end)
    // ============================================================
    public List<AzureSubscriptionInsight> Subscriptions { get; } = new();
    public List<AzureResourceRow> Resources { get; } = new();
}

/// <summary>
/// Per-subscription rollup used by report rendering and recommendations.
/// </summary>
public class AzureSubscriptionInsight
{
    public string SubscriptionId { get; init; } = string.Empty;
    public string? DisplayName { get; set; }
    public int ResourceCount { get; set; }
    public int UnhealthyAssessments { get; set; }
    public Dictionary<string, int> LocationBreakdown { get; } = new();
}

/// <summary>
/// Minimal representation of a single ARM resource collected during the
/// Azure pipeline. The pipeline batches all rows into
/// <c>cloud_assessment_azure_resources</c> in a single
/// <c>SaveChangesAsync</c> call at the end of <c>RunAsync</c>.
/// </summary>
public class AzureResourceRow
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Location { get; set; }
    public string? Kind { get; set; }

    /// <summary>
    /// Subset of the resource's JSON properties that matter for
    /// recommendation rules (e.g. storage <c>allowBlobPublicAccess</c>).
    /// Kept minimal to avoid persisting unnecessary data.
    /// </summary>
    public string? PropertiesJson { get; set; }

    /// <summary>
    /// Risk-flag strings appended by type-specific collectors. Serialized
    /// to JSON by the pipeline before persistence.
    /// </summary>
    public List<string> RiskFlags { get; } = new();
}
