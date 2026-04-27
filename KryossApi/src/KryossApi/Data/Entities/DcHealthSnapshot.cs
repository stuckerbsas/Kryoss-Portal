namespace KryossApi.Data.Entities;

public class DcHealthSnapshot
{
    public Guid Id { get; set; }
    public Guid MachineId { get; set; }
    public Guid OrganizationId { get; set; }
    public int? SchemaVersion { get; set; }
    public string? SchemaVersionLabel { get; set; }
    public string? ForestLevel { get; set; }
    public string? DomainLevel { get; set; }
    public string? ForestName { get; set; }
    public string? DomainName { get; set; }
    public string? SchemaMaster { get; set; }
    public string? DomainNamingMaster { get; set; }
    public string? PdcEmulator { get; set; }
    public string? RidMaster { get; set; }
    public string? InfrastructureMaster { get; set; }
    public bool FsmoSinglePoint { get; set; }
    public int ReplPartnerCount { get; set; }
    public int ReplFailureCount { get; set; }
    public DateTime? LastSuccessfulRepl { get; set; }
    public int SiteCount { get; set; }
    public int SubnetCount { get; set; }
    public int DcCount { get; set; }
    public int GcCount { get; set; }
    public DateTime ScannedAt { get; set; }
    public string? ScannedBy { get; set; }

    public Machine Machine { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
    public List<DcReplicationPartner> ReplicationPartners { get; set; } = [];
}

public class DcReplicationPartner
{
    public Guid Id { get; set; }
    public Guid SnapshotId { get; set; }
    public string? PartnerHostname { get; set; }
    public string? PartnerDn { get; set; }
    public string? Direction { get; set; }
    public string? NamingContext { get; set; }
    public DateTime? LastSuccess { get; set; }
    public DateTime? LastAttempt { get; set; }
    public int FailureCount { get; set; }
    public string? LastError { get; set; }
    public string? Transport { get; set; }

    public DcHealthSnapshot Snapshot { get; set; } = null!;
}
