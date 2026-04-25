namespace KryossApi.Data.Entities;

public class InfraHypervisorConfig
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Platform { get; set; } = null!; // vmware, hyperv, proxmox
    public string? DisplayName { get; set; }
    public string HostUrl { get; set; } = null!;
    public string? Username { get; set; }
    public string? EncryptedPassword { get; set; }
    public string? ApiToken { get; set; }
    public bool VerifySsl { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime? LastTestedAt { get; set; }
    public bool? LastTestOk { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Organization Organization { get; set; } = null!;
}

public class InfraHypervisor
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public Guid? ConfigId { get; set; }
    public Guid? SiteId { get; set; }
    public string Platform { get; set; } = null!;
    public string HostFqdn { get; set; } = null!;
    public string? Version { get; set; }
    public string? ClusterName { get; set; }
    public int? CpuCoresTotal { get; set; }
    public decimal? RamGbTotal { get; set; }
    public decimal? StorageGbTotal { get; set; }
    public decimal? CpuUsagePct { get; set; }
    public decimal? RamUsagePct { get; set; }
    public int VmCount { get; set; }
    public int VmRunning { get; set; }
    public bool? HaEnabled { get; set; }
    public string PowerState { get; set; } = "on";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public InfraAssessmentScan Scan { get; set; } = null!;
    public InfraHypervisorConfig? Config { get; set; }
    public InfraAssessmentSite? Site { get; set; }
    public ICollection<InfraVm> Vms { get; set; } = new List<InfraVm>();
}

public class InfraVm
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public Guid HypervisorId { get; set; }
    public string VmName { get; set; } = null!;
    public string? Os { get; set; }
    public string PowerState { get; set; } = "on";
    public int? CpuCores { get; set; }
    public decimal? RamGb { get; set; }
    public decimal? DiskGb { get; set; }
    public decimal? CpuAvgPct { get; set; }
    public decimal? RamAvgPct { get; set; }
    public decimal? DiskUsedPct { get; set; }
    public int SnapshotCount { get; set; }
    public int? OldestSnapshotDays { get; set; }
    public DateTime? LastBackup { get; set; }
    public DateTime? LastLogin { get; set; }
    public string? IpAddress { get; set; }
    public string? ToolsStatus { get; set; }
    public bool IsTemplate { get; set; }
    public bool IsIdle { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public InfraAssessmentScan Scan { get; set; } = null!;
    public InfraHypervisor Hypervisor { get; set; } = null!;
}
