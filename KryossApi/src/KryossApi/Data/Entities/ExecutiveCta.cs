namespace KryossApi.Data.Entities;

/// <summary>
/// Persistence for the C-Level report Block 3 CTAs. The rule engine
/// auto-detects candidates at report-generation time; the operator can
/// edit, suppress, or add manual CTAs via the portal. Rows persist per
/// org per reporting period so the next generate call can replay the
/// operator's edits.
/// </summary>
public class ExecutiveCta : IAuditable
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTime PeriodStart { get; set; }
    public string? AutoDetectedRule { get; set; }
    public string PriorityCategory { get; set; } = null!; // Incidentes|Hardening|Budget|Risk
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool IsSuppressed { get; set; }
    public bool IsManual { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
