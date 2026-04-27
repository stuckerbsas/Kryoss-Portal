namespace KryossApi.Data.Entities;

public class MachinePatchStatus
{
    public Guid Id { get; set; }
    public Guid MachineId { get; set; }
    public Guid OrganizationId { get; set; }
    public string? UpdateSource { get; set; }
    public string? WsusServer { get; set; }
    public string? WufbRing { get; set; }
    public DateTime? LastCheckUtc { get; set; }
    public DateTime? LastInstallUtc { get; set; }
    public bool RebootPending { get; set; }
    public int InstalledCount30d { get; set; }
    public int InstalledCount90d { get; set; }
    public int ComplianceScore { get; set; }
    public bool NinjaManaged { get; set; }
    public string? WuServiceStatus { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Machine Machine { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
}

public class MachinePatch
{
    public Guid Id { get; set; }
    public Guid MachineId { get; set; }
    public Guid OrganizationId { get; set; }
    public string HotfixId { get; set; } = null!;
    public string? Description { get; set; }
    public DateTime? InstalledOn { get; set; }
    public string? InstalledBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Machine Machine { get; set; } = null!;
}
