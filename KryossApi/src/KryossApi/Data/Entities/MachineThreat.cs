namespace KryossApi.Data.Entities;

public class MachineThreat
{
    public long Id { get; set; }
    public Guid MachineId { get; set; }
    public string ThreatName { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string Vector { get; set; } = null!;
    public string? Detail { get; set; }
    public DateTime DetectedAt { get; set; }
    public Machine Machine { get; set; } = null!;
}
