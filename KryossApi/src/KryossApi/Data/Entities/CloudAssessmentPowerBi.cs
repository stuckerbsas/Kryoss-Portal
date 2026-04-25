namespace KryossApi.Data.Entities;

public class CloudAssessmentPowerBiConnection
{
    public Guid OrganizationId { get; set; }
    public bool Enabled { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public string ConnectionState { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}

public class CloudAssessmentPowerBiWorkspace
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public string WorkspaceId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Type { get; set; }
    public string? State { get; set; }
    public bool? IsOnDedicatedCapacity { get; set; }
    public string? CapacityId { get; set; }
    public bool? HasWorkspaceLevelSettings { get; set; }
    public int? MemberCount { get; set; }
    public int? AdminCount { get; set; }
    public int? ExternalUserCount { get; set; }
    public int? DatasetCount { get; set; }
    public int? ReportCount { get; set; }
    public int? DashboardCount { get; set; }
    public int? DataflowCount { get; set; }
    public DateTime? LastUpdatedDate { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

public class CloudAssessmentPowerBiGateway
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public string GatewayId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Type { get; set; }
    public bool? PublicKeyValid { get; set; }
    public string? Status { get; set; }
    public string? Version { get; set; }
    public string? ContactInformation { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

public class CloudAssessmentPowerBiCapacity
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public string? CapacityId { get; set; }
    public string? DisplayName { get; set; }
    public string? Sku { get; set; }
    public string? Region { get; set; }
    public string? State { get; set; }
    public decimal? UsagePct { get; set; }
    public int? AdminCount { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

public class CloudAssessmentPowerBiActivitySummary
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public int? ActivitiesTotal { get; set; }
    public int? UniqueUsers { get; set; }
    public int? ViewReportCount { get; set; }
    public int? EditReportCount { get; set; }
    public int? CreateDatasetCount { get; set; }
    public int? DeleteCount { get; set; }
    public int? ShareExternalCount { get; set; }
    public int? ExportCount { get; set; }
    public int? PeriodDays { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}
