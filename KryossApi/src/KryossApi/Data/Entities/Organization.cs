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
