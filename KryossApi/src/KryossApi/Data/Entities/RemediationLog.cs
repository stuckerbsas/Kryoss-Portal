namespace KryossApi.Data.Entities;

public class RemediationLog
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public Guid MachineId { get; set; }
    public Guid OrganizationId { get; set; }
    public string EventType { get; set; } = null!;
    public Guid? ActorId { get; set; }
    public string ActionType { get; set; } = null!;
    public int? ControlDefId { get; set; }
    public string? ServiceName { get; set; }
    public string? ParamsHash { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SignatureHash { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; }
}
