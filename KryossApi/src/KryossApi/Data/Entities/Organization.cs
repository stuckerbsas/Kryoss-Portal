namespace KryossApi.Data.Entities;

public class Organization : IAuditable
{
    public Guid Id { get; set; }
    public Guid FranchiseId { get; set; }
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string Status { get; set; } = "prospect"; // prospect, current, disabled
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public int BrandId { get; set; }
    public Guid? EntraTenantId { get; set; }

    // v1.5.1: Protocol Usage Audit — MSP opts in per-org via portal toggle.
    // When true, agents configure NTLM+SMB1 audit on next run and resize
    // event logs for 90-day retention. See sql/026_protocol_audit.sql.
    public bool ProtocolAuditEnabled { get; set; }
    public DateTime? ProtocolAuditEnabledAt { get; set; }
    public string? ProtocolAuditEnabledBy { get; set; }

    // Audit
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation
    public Franchise Franchise { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public ICollection<Machine> Machines { get; set; } = [];
    public ICollection<AssessmentRun> AssessmentRuns { get; set; } = [];
    public ICollection<EnrollmentCode> EnrollmentCodes { get; set; } = [];
}
