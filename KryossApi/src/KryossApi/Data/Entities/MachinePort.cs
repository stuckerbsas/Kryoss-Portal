namespace KryossApi.Data.Entities;

public class MachinePort
{
    public long Id { get; set; }
    public Guid MachineId { get; set; }
    public int Port { get; set; }
    public string Protocol { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Service { get; set; }
    public string? Risk { get; set; }
    public string? Banner { get; set; }
    public string? ServiceName { get; set; }
    public string? ServiceVersion { get; set; }
    public DateTime ScannedAt { get; set; }
    public Machine Machine { get; set; } = null!;
}
