namespace KryossApi.Data.Entities;

public class SnmpConfig
{
    public int Id { get; set; }
    public Guid OrganizationId { get; set; }
    public short SnmpVersion { get; set; } = 2;
    public string? Community { get; set; }
    public string? Username { get; set; }
    public string? AuthProtocol { get; set; }
    public string? AuthPassword { get; set; }
    public string? PrivProtocol { get; set; }
    public string? PrivPassword { get; set; }
    public string? Targets { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}

public class SnmpDevice
{
    public int Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string IpAddress { get; set; } = null!;
    public string? MacAddress { get; set; }
    public string? Vendor { get; set; }
    public string? SysName { get; set; }
    public string? SysDescr { get; set; }
    public long? SysUptimeSec { get; set; }
    public string? SysContact { get; set; }
    public string? SysLocation { get; set; }
    public string? SysObjectId { get; set; }
    public string? EntityModel { get; set; }
    public string? EntitySerial { get; set; }
    public string? EntityMfg { get; set; }
    public string? EntityFirmware { get; set; }
    public int? InterfaceCount { get; set; }
    public int LldpNeighborCount { get; set; }
    public int CdpNeighborCount { get; set; }
    public string? DeviceType { get; set; }
    public long? PageCount { get; set; }
    public int? CpuLoadPct { get; set; }
    public long? MemoryTotalMb { get; set; }
    public long? MemoryUsedMb { get; set; }
    public int? DiskTotalGb { get; set; }
    public int? DiskUsedGb { get; set; }
    public int? ProcessCount { get; set; }
    public string? RawData { get; set; }
    public string? VendorData { get; set; }
    public DateTime ScannedAt { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public Guid? MachineId { get; set; }
    public bool IsStale { get; set; }
    public string? SecondaryIps { get; set; }
    public string? ScanSource { get; set; }
    public string? ReverseDns { get; set; }
    public double? PingLatencyMs { get; set; }
    public double? PingLossPct { get; set; }
    public double? PingJitterMs { get; set; }

    public Organization Organization { get; set; } = null!;
    public Machine? Machine { get; set; }
    public ICollection<SnmpDeviceInterface> Interfaces { get; set; } = [];
    public ICollection<SnmpDeviceSupply> Supplies { get; set; } = [];
    public ICollection<SnmpDeviceNeighbor> Neighbors { get; set; } = [];
}

public class SnmpDeviceNeighbor
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public string Protocol { get; set; } = null!;
    public string? LocalPort { get; set; }
    public string? RemoteChassisId { get; set; }
    public string? RemotePortId { get; set; }
    public string? RemotePortDesc { get; set; }
    public string? RemoteSysName { get; set; }
    public string? RemoteSysDesc { get; set; }
    public string? RemoteDeviceIdStr { get; set; }
    public string? RemoteIp { get; set; }
    public string? RemotePlatform { get; set; }
    public int? ResolvedDeviceId { get; set; }
    public DateTime UpdatedAt { get; set; }

    public SnmpDevice Device { get; set; } = null!;
    public SnmpDevice? ResolvedDevice { get; set; }
}

public class SnmpDeviceSupply
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public string Description { get; set; } = null!;
    public string SupplyType { get; set; } = null!;
    public string? Color { get; set; }
    public int? LevelPercent { get; set; }
    public int? MaxCapacity { get; set; }
    public int? CurrentLevel { get; set; }

    public SnmpDevice Device { get; set; } = null!;
}

public class SnmpDeviceProfile
{
    public int Id { get; set; }
    public string VendorName { get; set; } = null!;
    public string OidPrefix { get; set; } = null!;
    public bool Enabled { get; set; } = true;

    public ICollection<SnmpProfileOid> Oids { get; set; } = [];
}

public class SnmpProfileOid
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public string Oid { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string DataType { get; set; } = "gauge";
    public string? Unit { get; set; }
    public bool Walk { get; set; }

    public SnmpDeviceProfile Profile { get; set; } = null!;
}

public class SnmpDeviceInterface
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public int IfIndex { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? IfType { get; set; }
    public long? SpeedMbps { get; set; }
    public string? MacAddress { get; set; }
    public short? AdminStatus { get; set; }
    public short? OperStatus { get; set; }
    public long? InOctets { get; set; }
    public long? OutOctets { get; set; }
    public long? InErrors { get; set; }
    public long? OutErrors { get; set; }
    public long? InDiscards { get; set; }
    public long? OutDiscards { get; set; }
    public long? PrevInOctets { get; set; }
    public long? PrevOutOctets { get; set; }
    public int? SampleIntervalSec { get; set; }
    public long? InRateBps { get; set; }
    public long? OutRateBps { get; set; }

    public SnmpDevice Device { get; set; } = null!;
}
