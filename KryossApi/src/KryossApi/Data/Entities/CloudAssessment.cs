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
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public M365Tenant? Tenant { get; set; }
    public ICollection<CloudAssessmentFinding> Findings { get; set; } = [];
    public ICollection<CloudAssessmentMetric> Metrics { get; set; } = [];
    public ICollection<CloudAssessmentLicense> Licenses { get; set; } = [];
    public ICollection<CloudAssessmentAdoption> Adoptions { get; set; } = [];
    public ICollection<CloudAssessmentWastedLicense> WastedLicenses { get; set; } = [];
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
