namespace KryossApi.Data.Entities;

public class MachineService
{
    public long Id { get; set; }
    public Guid MachineId { get; set; }
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Status { get; set; } = null!;
    public string StartupType { get; set; } = null!;
    public DateTime UpdatedAt { get; set; }

    public Machine Machine { get; set; } = null!;
}
