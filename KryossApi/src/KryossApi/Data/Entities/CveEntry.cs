namespace KryossApi.Data.Entities;

public class CveEntry
{
    public int Id { get; set; }
    public string CveId { get; set; } = null!;
    public string ProductPattern { get; set; } = null!;
    public string? Vendor { get; set; }
    public string? AffectedBelow { get; set; }
    public string? AffectedAbove { get; set; }
    public string? FixedVersion { get; set; }
    public string Severity { get; set; } = "medium";
    public decimal? CvssScore { get; set; }
    public string? Description { get; set; }
    public string? CweId { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string Source { get; set; } = "builtin";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class MachineCveFinding
{
    public int Id { get; set; }
    public Guid MachineId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? RunId { get; set; }
    public string CveId { get; set; } = null!;
    public string SoftwareName { get; set; } = null!;
    public string? SoftwareVersion { get; set; }
    public string? InstalledVersion { get; set; }
    public string? FixedVersion { get; set; }
    public string Severity { get; set; } = null!;
    public decimal? CvssScore { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "open";
    public DateTime FoundAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    public Machine Machine { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
}

public class CveSyncLog
{
    public int Id { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    public int EntriesAdded { get; set; }
    public int EntriesUpdated { get; set; }
    public string Source { get; set; } = "nvd";
    public string Status { get; set; } = "success";
    public string? ErrorMessage { get; set; }
}
