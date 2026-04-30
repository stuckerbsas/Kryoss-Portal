namespace KryossApi.Data.Entities;

public class ControlCheckParam
{
    public int Id { get; set; }
    public int ControlDefId { get; set; }
    public string ParamName { get; set; } = null!;
    public string? ParamValue { get; set; }
    public ControlDef ControlDef { get; set; } = null!;
}

public class MachineLocalAdmin
{
    public long Id { get; set; }
    public Guid MachineId { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string? Source { get; set; }
    public Machine Machine { get; set; } = null!;
}

public class MachineLoopStatus
{
    public Guid MachineId { get; set; }
    public string LoopName { get; set; } = null!;
    public string State { get; set; } = "idle";
    public DateTime? LastRunAt { get; set; }
    public int? DurationMs { get; set; }
    public string? LastError { get; set; }
    public Machine Machine { get; set; } = null!;
}

public class OrgPriorityService
{
    public int Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string ServiceName { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
}

public class MachineTracerouteHop
{
    public long Id { get; set; }
    public int DiagId { get; set; }
    public short HopNumber { get; set; }
    public string? IpAddress { get; set; }
    public string? Hostname { get; set; }
    public decimal? RttMs { get; set; }
    public MachineNetworkDiag Diag { get; set; } = null!;
}

public class CloudFindingProperty
{
    public long Id { get; set; }
    public long AzureResourceId { get; set; }
    public string PropName { get; set; } = null!;
    public string? PropValue { get; set; }
    public CloudAssessmentAzureResource AzureResource { get; set; } = null!;
}

public class CloudResourceRiskFlag
{
    public long Id { get; set; }
    public long AzureResourceId { get; set; }
    public string FlagCode { get; set; } = null!;
    public CloudAssessmentAzureResource AzureResource { get; set; } = null!;
}

public class MailDomainSpfWarning
{
    public long Id { get; set; }
    public Guid MailDomainId { get; set; }
    public string WarningText { get; set; } = null!;
    public CloudAssessmentMailDomain MailDomain { get; set; } = null!;
}

public class MailDomainDkimSelector
{
    public long Id { get; set; }
    public Guid MailDomainId { get; set; }
    public string Selector { get; set; } = null!;
    public bool IsValid { get; set; }
    public CloudAssessmentMailDomain MailDomain { get; set; } = null!;
}

public class SharedMailboxDelegate
{
    public long Id { get; set; }
    public Guid MailboxId { get; set; }
    public string UserEmail { get; set; } = null!;
    public string PermissionType { get; set; } = null!;
    public CloudAssessmentSharedMailbox Mailbox { get; set; } = null!;
}

public class AlertPayloadField
{
    public long Id { get; set; }
    public long AlertId { get; set; }
    public string FieldName { get; set; } = null!;
    public string? FieldValue { get; set; }
    public CloudAssessmentAlertSent Alert { get; set; } = null!;
}

public class RemediationActionParam
{
    public int Id { get; set; }
    public int RemediationActionId { get; set; }
    public string ParamName { get; set; } = null!;
    public string? ParamValue { get; set; }
    public string ParamType { get; set; } = "string";
    public RemediationAction RemediationAction { get; set; } = null!;
}

public class CveProductMap
{
    public int Id { get; set; }
    public int CveEntryId { get; set; }
    public int SoftwareId { get; set; }
    public string? AffectedBelow { get; set; }
    public string? FixedVersion { get; set; }
    public CveEntry CveEntry { get; set; } = null!;
    public Software Software { get; set; } = null!;
}

public class Software : IAuditable
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Publisher { get; set; }
    public string? Category { get; set; }
    public bool IsBlacklisted { get; set; }
    public bool IsEol { get; set; }
    public DateTime? EolDate { get; set; }
    public string? CpeVendor { get; set; }
    public string? CpeProduct { get; set; }
    public bool IsCommercial { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class MachineSoftware
{
    public int Id { get; set; }
    public Guid MachineId { get; set; }
    public int SoftwareId { get; set; }
    public string? Version { get; set; }
    public DateTime? InstallDate { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? RemovedAt { get; set; }

    public Machine Machine { get; set; } = null!;
    public Software Software { get; set; } = null!;
}
