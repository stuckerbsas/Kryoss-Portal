namespace KryossApi.Data.Entities;

public class MachineDisk
{
    public long Id { get; set; }
    public Guid MachineId { get; set; }
    public string DriveLetter { get; set; } = null!;
    public string? Label { get; set; }
    public string? DiskType { get; set; }
    public int? TotalGb { get; set; }
    public decimal? FreeGb { get; set; }
    public string? FileSystem { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Machine Machine { get; set; } = null!;
}
