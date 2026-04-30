namespace KryossApi.Data.Entities;

public class MachineAvailableUpdate
{
    public long Id { get; set; }
    public Guid MachineId { get; set; }
    public Guid OrganizationId { get; set; }
    public string KbNumber { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Severity { get; set; }
    public string? Classification { get; set; }
    public bool IsMandatory { get; set; }
    public long? MaxDownloadSize { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? SupportUrl { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? InstalledAt { get; set; }
    public bool IsPending { get; set; } = true;

    public Machine Machine { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
}
