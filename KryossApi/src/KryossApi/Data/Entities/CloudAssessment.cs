namespace KryossApi.Data.Entities;

public class CloudAssessmentScan
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? TenantId { get; set; }
    public string? AzureSubscriptionIds { get; set; }
    public string Status { get; set; } = "running";
    public decimal? OverallScore { get; set; }
    public string? AreaScores { get; set; }
    public string? Verdict { get; set; }
    public string? PipelineStatus { get; set; }
    public string? FeatureInventory { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Copilot Readiness D1-D6 scores (computed from same scan data)
    public decimal? CopilotD1Score { get; set; }
    public decimal? CopilotD2Score { get; set; }
    public decimal? CopilotD3Score { get; set; }
    public decimal? CopilotD4Score { get; set; }
    public decimal? CopilotD5Score { get; set; }
    public decimal? CopilotD6Score { get; set; }
    public decimal? CopilotOverall { get; set; }
    public string? CopilotVerdict { get; set; }

    public Organization Organization { get; set; } = null!;
    public M365Tenant? Tenant { get; set; }
    public ICollection<CloudAssessmentFinding> Findings { get; set; } = [];
    public ICollection<CloudAssessmentMetric> Metrics { get; set; } = [];
    public ICollection<CloudAssessmentLicense> Licenses { get; set; } = [];
    public ICollection<CloudAssessmentAdoption> Adoptions { get; set; } = [];
    public ICollection<CloudAssessmentWastedLicense> WastedLicenses { get; set; } = [];
    public ICollection<CloudAssessmentAzureResource> AzureResources { get; set; } = [];
    public ICollection<CloudAssessmentFrameworkScore> FrameworkScores { get; set; } = [];
    public ICollection<CloudAssessmentSharepointSite> SharepointSites { get; set; } = [];
    public ICollection<CloudAssessmentExternalUser> ExternalUsers { get; set; } = [];
    public ICollection<CloudAssessmentMailDomain> MailDomains { get; set; } = [];
    public ICollection<CloudAssessmentMailboxRisk> MailboxRisks { get; set; } = [];
    public ICollection<CloudAssessmentSharedMailbox> SharedMailboxes { get; set; } = [];
}

public class CloudAssessmentFinding
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string Area { get; set; } = null!;
    public string Service { get; set; } = null!;
    public string Feature { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string Priority { get; set; } = "";
    public string? Observation { get; set; }
    public string? Recommendation { get; set; }
    public string? LinkText { get; set; }
    public string? LinkUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

public class CloudAssessmentMetric
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string Area { get; set; } = null!;
    public string MetricKey { get; set; } = null!;
    public string MetricValue { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

public class CloudAssessmentAzureSubscription
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string SubscriptionId { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? State { get; set; }
    public string? TenantId { get; set; }
    public string? ConsentState { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}

public class CloudAssessmentLicense
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string SkuPartNumber { get; set; } = null!;
    public string? FriendlyName { get; set; }
    public int Purchased { get; set; }
    public int Assigned { get; set; }
    public int Available { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

public class CloudAssessmentAdoption
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string Area { get; set; } = null!;
    public string ServiceName { get; set; } = null!;
    public int LicensedCount { get; set; }
    public int Active30d { get; set; }
    public decimal AdoptionRate { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

public class CloudAssessmentWastedLicense
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string UserPrincipal { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Sku { get; set; }
    public DateTime? LastSignIn { get; set; }
    public int? DaysInactive { get; set; }
    public decimal? EstimatedCostYear { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

public class CloudAssessmentFindingStatus
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Area { get; set; } = null!;
    public string Service { get; set; } = null!;
    public string Feature { get; set; } = null!;
    public string Status { get; set; } = null!;
    public Guid? OwnerUserId { get; set; }
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public Organization Organization { get; set; } = null!;
}

/// <summary>
/// Per-scan cache of Azure ARM resources collected by the CA-6 Subsession B
/// Azure pipeline. Holds the slice of properties and the list of detected risk
/// flags that recommendations consume downstream. Rows are tied to a scan and
/// cascade-delete with it.
/// </summary>
public class CloudAssessmentAzureResource
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string SubscriptionId { get; set; } = null!;
    public string ResourceType { get; set; } = null!;
    public string ResourceId { get; set; } = null!;
    public string? Name { get; set; }
    public string? Location { get; set; }
    public string? Kind { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
    public ICollection<CloudFindingProperty> Properties { get; set; } = [];
    public ICollection<CloudResourceRiskFlag> RiskFlags { get; set; } = [];
}

public class CloudAssessmentSuggestion
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ScanId { get; set; }
    public string Area { get; set; } = null!;
    public string Service { get; set; } = null!;
    public string Feature { get; set; } = null!;
    public string SuggestionType { get; set; } = null!;  // "likely_resolved" | "possible_regression"
    public DateTime CreatedAt { get; set; }
    public DateTime? DismissedAt { get; set; }
    public Guid? DismissedBy { get; set; }

    public Organization Organization { get; set; } = null!;
    public CloudAssessmentScan Scan { get; set; } = null!;
}

public class CloudAssessmentFramework
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Authority { get; set; }
    public string? DocUrl { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ICollection<CloudAssessmentFrameworkControl> Controls { get; set; } = [];
}

public class CloudAssessmentFrameworkControl
{
    public Guid Id { get; set; }
    public Guid FrameworkId { get; set; }
    public string ControlCode { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentFramework Framework { get; set; } = null!;
    public ICollection<CloudAssessmentFindingControlMapping> Mappings { get; set; } = [];
}

public class CloudAssessmentFindingControlMapping
{
    public Guid Id { get; set; }
    public string Area { get; set; } = null!;
    public string Service { get; set; } = null!;
    public string Feature { get; set; } = null!;
    public Guid FrameworkControlId { get; set; }
    public string Coverage { get; set; } = null!;
    public string? Rationale { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentFrameworkControl FrameworkControl { get; set; } = null!;
}

public class CloudAssessmentFrameworkScore
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public Guid FrameworkId { get; set; }
    public int TotalControls { get; set; }
    public int CoveredControls { get; set; }
    public int PassingControls { get; set; }
    public int FailingControls { get; set; }
    public int UnmappedControls { get; set; }
    public decimal ScorePct { get; set; }
    public string? Grade { get; set; }
    public DateTime ComputedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
    public CloudAssessmentFramework Framework { get; set; } = null!;
}

public class CloudAssessmentSharepointSite
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string SiteUrl { get; set; } = null!;
    public string? SiteTitle { get; set; }
    public int TotalFiles { get; set; }
    public int LabeledFiles { get; set; }
    public int OversharedFiles { get; set; }
    public string? RiskLevel { get; set; }
    public string? TopLabels { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

public class CloudAssessmentExternalUser
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string UserPrincipal { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? EmailDomain { get; set; }
    public DateTime? LastSignIn { get; set; }
    public string? RiskLevel { get; set; }
    public int SitesAccessed { get; set; }
    public string? HighestPermission { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

/// <summary>
/// CA-10 Mail Flow: per-domain email security posture (SPF / DKIM / DMARC /
/// MTA-STS / BIMI). Populated by the MailFlowPipeline in Task 2. Rows are
/// scan-scoped and cascade-delete with the parent scan.
/// JSON columns (SpfWarnings, DkimSelectors) store serialized string arrays.
/// </summary>
public class CloudAssessmentMailDomain
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public string Domain { get; set; } = null!;
    public bool IsDefault { get; set; }
    public bool IsVerified { get; set; }
    // SPF
    public string? SpfRecord { get; set; }
    public bool? SpfValid { get; set; }
    public string? SpfMechanism { get; set; }
    public int? SpfLookupCount { get; set; }
    // DKIM
    public bool? DkimS1Present { get; set; }
    public bool? DkimS2Present { get; set; }
    // DMARC
    public string? DmarcRecord { get; set; }
    public bool? DmarcValid { get; set; }
    public string? DmarcPolicy { get; set; }
    public string? DmarcSubdomainPolicy { get; set; }
    public int? DmarcPct { get; set; }
    public string? DmarcRua { get; set; }
    public string? DmarcRuf { get; set; }
    // MTA-STS
    public string? MtaStsRecord { get; set; }
    public string? MtaStsPolicy { get; set; }
    // BIMI
    public bool? BimiPresent { get; set; }
    // Aggregate
    public decimal? Score { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

/// <summary>
/// CA-10 Mail Flow: mailbox-level risk findings (external forwarding rules,
/// auto-forward tenant setting, shared-mailbox password enabled, orphaned
/// shared mailboxes). Scan-scoped, cascade-delete.
/// </summary>
public class CloudAssessmentMailboxRisk
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public string UserPrincipalName { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string RiskType { get; set; } = null!;
    public string? RiskDetail { get; set; }
    public string? ForwardTarget { get; set; }
    public string? Severity { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

/// <summary>
/// CA-10 Mail Flow: shared mailbox inventory with delegate permissions and
/// activity metadata. FullAccessUsers / SendAsUsers are JSON-serialized
/// string arrays. Scan-scoped, cascade-delete.
/// </summary>
public class CloudAssessmentSharedMailbox
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public string MailboxUpn { get; set; } = null!;
    public string? DisplayName { get; set; }
    public int? DelegatesCount { get; set; }
    public bool? HasPasswordEnabled { get; set; }
    public DateTime? LastActivity { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}
