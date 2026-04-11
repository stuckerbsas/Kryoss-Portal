namespace KryossApi.Data.Entities;

public class Assessment : IAuditable
{
    public int Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public ICollection<AssessmentControl> AssessmentControls { get; set; } = [];
}

public class AssessmentControl
{
    public int AssessmentId { get; set; }
    public int ControlDefId { get; set; }

    public Assessment Assessment { get; set; } = null!;
    public ControlDef ControlDef { get; set; } = null!;
}

public class AssessmentRun
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid MachineId { get; set; }
    public int? AssessmentId { get; set; }
    public string? AgentVersion { get; set; }
    public string? ControlsVersion { get; set; }
    public decimal? GlobalScore { get; set; }
    public string? Grade { get; set; } // A+, A, B, C, D, F
    public short? TotalPoints { get; set; }
    public short? EarnedPoints { get; set; }
    public short? PassCount { get; set; }
    public short? WarnCount { get; set; }
    public short? FailCount { get; set; }
    public int? DurationMs { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? RawPayload { get; set; } // full agent JSON

    // Navigation
    public Organization Organization { get; set; } = null!;
    public Machine Machine { get; set; } = null!;
    public Assessment? Assessment { get; set; }
    public ICollection<ControlResult> ControlResults { get; set; } = [];
    public ICollection<RunFrameworkScore> RunFrameworkScores { get; set; } = [];
}

public class ControlResult
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public int ControlDefId { get; set; }
    public string Status { get; set; } = null!; // pass, warn, fail, info, error
    public short Score { get; set; }
    public short MaxScore { get; set; }
    public string? Finding { get; set; }
    public string? ActualValue { get; set; }

    public AssessmentRun Run { get; set; } = null!;
    public ControlDef ControlDef { get; set; } = null!;
}

public class RunFrameworkScore
{
    public Guid RunId { get; set; }
    public int FrameworkId { get; set; }
    public decimal Score { get; set; }
    public short PassCount { get; set; }
    public short WarnCount { get; set; }
    public short FailCount { get; set; }

    public AssessmentRun Run { get; set; } = null!;
    public Framework Framework { get; set; } = null!;
}
