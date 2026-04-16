namespace KryossApi.Data.Entities;

public class CopilotReadinessScan
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid TenantId { get; set; }
    public string Status { get; set; } = "running";
    public decimal? D1Score { get; set; }
    public decimal? D2Score { get; set; }
    public decimal? D3Score { get; set; }
    public decimal? D4Score { get; set; }
    public decimal? D5Score { get; set; }
    public decimal? D6Score { get; set; }
    public decimal? OverallScore { get; set; }
    public string? Verdict { get; set; }
    public string? PipelineStatus { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public M365Tenant Tenant { get; set; } = null!;
    public ICollection<CopilotReadinessMetric> Metrics { get; set; } = [];
    public ICollection<CopilotReadinessFinding> Findings { get; set; } = [];
    public ICollection<CopilotReadinessSharepoint> SharepointSites { get; set; } = [];
    public ICollection<CopilotReadinessExternalUser> ExternalUsers { get; set; } = [];
}

public class CopilotReadinessMetric
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string Dimension { get; set; } = null!;
    public string MetricKey { get; set; } = null!;
    public string MetricValue { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public CopilotReadinessScan Scan { get; set; } = null!;
}

public class CopilotReadinessFinding
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string Service { get; set; } = null!;
    public string Feature { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string Priority { get; set; } = "";
    public string? Observation { get; set; }
    public string? Recommendation { get; set; }
    public string? LinkText { get; set; }
    public string? LinkUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public CopilotReadinessScan Scan { get; set; } = null!;
}

public class CopilotReadinessSharepoint
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

    public CopilotReadinessScan Scan { get; set; } = null!;
}

public class CopilotReadinessExternalUser
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

    public CopilotReadinessScan Scan { get; set; } = null!;
}
