namespace KryossApi.Data.Entities;

public class Machine : IAuditable
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? SiteId { get; set; }
    public Guid AgentId { get; set; }
    public string Hostname { get; set; } = null!;

    // OS
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public string? OsBuild { get; set; }

    // Platform scope (resolved server-side from OsName at enrollment / OS drift).
    // Used by ControlsFunction to filter control_platforms for the agent's OS.
    public int? PlatformId { get; set; }
    public Platform? Platform { get; set; }

    // Hardware fingerprint (stable opaque hash from the agent).
    // NULL = not yet captured (backfill on next signed request).
    // Non-NULL = any future request whose X-Hwid differs is rejected.
    // See security-baseline.md §Hardware binding / P1 #7.
    public string? Hwid { get; set; }

    // Hardware
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? CpuName { get; set; }
    public short? CpuCores { get; set; }
    public short? RamGb { get; set; }
    public string? DiskType { get; set; }
    public int? DiskSizeGb { get; set; }
    public decimal? DiskFreeGb { get; set; }

    // Security hardware
    public bool? TpmPresent { get; set; }
    public string? TpmVersion { get; set; }
    public bool? SecureBoot { get; set; }
    public bool? Bitlocker { get; set; }

    // Network
    public string? IpAddress { get; set; }
    public string? MacAddress { get; set; }

    // Identity
    public string? DomainStatus { get; set; }
    public string? DomainName { get; set; }

    // Lifecycle
    public int? SystemAgeDays { get; set; }
    public DateTime? LastBootAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Audit
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation
    public Organization Organization { get; set; } = null!;
    public ICollection<AssessmentRun> AssessmentRuns { get; set; } = [];
}
