namespace KryossApi.Data.Entities;

public class CloudAssessmentAlertRule
{
    public Guid Id { get; set; }
    public Guid FranchiseId { get; set; }
    public string RuleType { get; set; } = null!;
    public decimal? Threshold { get; set; }
    public string? FrameworkCode { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string DeliveryChannel { get; set; } = "email";
    public string? TargetEmail { get; set; }
    public string? WebhookUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Franchise Franchise { get; set; } = null!;
}

public class CloudAssessmentAlertSent
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public Guid RuleId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Severity { get; set; } = null!;
    public string RuleType { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string? PayloadJson { get; set; }
    public string DeliveryStatus { get; set; } = "pending";
    public DateTime? DeliveredAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime FiredAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
    public CloudAssessmentAlertRule Rule { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
}
