namespace KryossApi.Data.Entities;

public class RemediationAction
{
    public int Id { get; set; }
    public int ControlDefId { get; set; }
    public string ActionType { get; set; } = null!;
    public string RiskLevel { get; set; } = "low";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ControlDef ControlDef { get; set; } = null!;
}

public class RemediationTask
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid MachineId { get; set; }
    public int? ControlDefId { get; set; }
    public int? ActionId { get; set; }
    public string ActionType { get; set; } = null!;
    public string? Params { get; set; }
    public string Status { get; set; } = "pending";
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? SignatureHash { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Machine Machine { get; set; } = null!;
    public ControlDef? ControlDef { get; set; }
    public RemediationAction? Action { get; set; }
}

public class OrgAutoRemediate
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public int ControlDefId { get; set; }
    public Guid EnabledBy { get; set; }
    public DateTime EnabledAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public ControlDef ControlDef { get; set; } = null!;
}
