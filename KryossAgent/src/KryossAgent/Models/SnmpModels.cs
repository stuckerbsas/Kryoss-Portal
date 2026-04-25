using System.Text.Json.Serialization;

namespace KryossAgent.Models;

public class SnmpCredentials
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2; // 1, 2 (v2c), 3

    [JsonPropertyName("community")]
    public string? Community { get; set; } = "public";

    // SNMPv3
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("authProtocol")]
    public string? AuthProtocol { get; set; } // MD5, SHA

    [JsonPropertyName("authPassword")]
    public string? AuthPassword { get; set; }

    [JsonPropertyName("privProtocol")]
    public string? PrivProtocol { get; set; } // DES, AES

    [JsonPropertyName("privPassword")]
    public string? PrivPassword { get; set; }

    [JsonPropertyName("targets")]
    public List<string>? Targets { get; set; } // specific IPs to scan, null = auto-discover
}

public class SnmpDeviceResult
{
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = null!;

    [JsonPropertyName("sysName")]
    public string? SysName { get; set; }

    [JsonPropertyName("sysDescr")]
    public string? SysDescr { get; set; }

    [JsonPropertyName("sysUptime")]
    public long? SysUptimeSeconds { get; set; }

    [JsonPropertyName("sysContact")]
    public string? SysContact { get; set; }

    [JsonPropertyName("sysLocation")]
    public string? SysLocation { get; set; }

    [JsonPropertyName("sysObjectId")]
    public string? SysObjectId { get; set; }

    [JsonPropertyName("interfaces")]
    public List<SnmpInterfaceResult> Interfaces { get; set; } = [];

    [JsonPropertyName("entityInfo")]
    public SnmpEntityInfo? Entity { get; set; }

    [JsonPropertyName("lldpNeighbors")]
    public List<LldpNeighbor> LldpNeighbors { get; set; } = [];

    [JsonPropertyName("cdpNeighbors")]
    public List<CdpNeighbor> CdpNeighbors { get; set; } = [];

    [JsonPropertyName("macAddress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MacAddress { get; set; }

    [JsonPropertyName("vendor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Vendor { get; set; }

    [JsonPropertyName("deviceType")]
    public string DeviceType { get; set; } = "unknown";

    [JsonPropertyName("printerSupplies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PrinterSupply>? PrinterSupplies { get; set; }

    [JsonPropertyName("pageCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long? PageCount { get; set; }

    [JsonPropertyName("vendorData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? VendorData { get; set; }

    [JsonPropertyName("hostResources")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SnmpHostResources? HostResources { get; set; }

    [JsonPropertyName("reverseDns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReverseDns { get; set; }

    [JsonPropertyName("pingLatencyMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PingLatencyMs { get; set; }

    [JsonPropertyName("pingLossPct")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PingLossPct { get; set; }

    [JsonPropertyName("pingJitterMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PingJitterMs { get; set; }

    [JsonPropertyName("scannedAt")]
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
}

public class SnmpHostResources
{
    [JsonPropertyName("cpuLoadPercent")]
    public int? CpuLoadPercent { get; set; }

    [JsonPropertyName("memoryTotalMb")]
    public long? MemoryTotalMb { get; set; }

    [JsonPropertyName("memoryUsedMb")]
    public long? MemoryUsedMb { get; set; }

    [JsonPropertyName("processCount")]
    public int? ProcessCount { get; set; }

    [JsonPropertyName("storage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SnmpStorageEntry>? Storage { get; set; }
}

public class SnmpStorageEntry
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = null!;

    [JsonPropertyName("totalMb")]
    public long TotalMb { get; set; }

    [JsonPropertyName("usedMb")]
    public long UsedMb { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!; // ram, fixedDisk, virtualMemory, other
}

public class PrinterSupply
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = null!;

    [JsonPropertyName("supplyType")]
    public string SupplyType { get; set; } = null!; // toner, ink, drum, fuser, other

    [JsonPropertyName("color")]
    public string? Color { get; set; } // black, cyan, magenta, yellow, etc.

    [JsonPropertyName("levelPercent")]
    public int? LevelPercent { get; set; } // 0-100, or null if unknown

    [JsonPropertyName("maxCapacity")]
    public int? MaxCapacity { get; set; }

    [JsonPropertyName("currentLevel")]
    public int? CurrentLevel { get; set; }
}

public class SnmpInterfaceResult
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("speedMbps")]
    public long? SpeedMbps { get; set; }

    [JsonPropertyName("macAddress")]
    public string? MacAddress { get; set; }

    [JsonPropertyName("adminStatus")]
    public int AdminStatus { get; set; } // 1=up, 2=down, 3=testing

    [JsonPropertyName("operStatus")]
    public int OperStatus { get; set; } // 1=up, 2=down

    [JsonPropertyName("inOctets")]
    public long InOctets { get; set; }

    [JsonPropertyName("outOctets")]
    public long OutOctets { get; set; }

    [JsonPropertyName("inErrors")]
    public long InErrors { get; set; }

    [JsonPropertyName("outErrors")]
    public long OutErrors { get; set; }

    [JsonPropertyName("inDiscards")]
    public long InDiscards { get; set; }

    [JsonPropertyName("outDiscards")]
    public long OutDiscards { get; set; }
}

public class SnmpEntityInfo
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("serial")]
    public string? Serial { get; set; }

    [JsonPropertyName("firmwareVersion")]
    public string? FirmwareVersion { get; set; }

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }
}

public class LldpNeighbor
{
    [JsonPropertyName("localPort")]
    public string? LocalPort { get; set; }

    [JsonPropertyName("remoteChassisId")]
    public string? RemoteChassisId { get; set; }

    [JsonPropertyName("remotePortId")]
    public string? RemotePortId { get; set; }

    [JsonPropertyName("remotePortDesc")]
    public string? RemotePortDesc { get; set; }

    [JsonPropertyName("remoteSysName")]
    public string? RemoteSysName { get; set; }

    [JsonPropertyName("remoteSysDesc")]
    public string? RemoteSysDesc { get; set; }
}

public class CdpNeighbor
{
    [JsonPropertyName("localPort")]
    public string? LocalPort { get; set; }

    [JsonPropertyName("remoteDeviceId")]
    public string? RemoteDeviceId { get; set; }

    [JsonPropertyName("remotePortId")]
    public string? RemotePortId { get; set; }

    [JsonPropertyName("remoteIp")]
    public string? RemoteIp { get; set; }

    [JsonPropertyName("remotePlatform")]
    public string? RemotePlatform { get; set; }
}

public class SnmpScanResult
{
    [JsonPropertyName("devices")]
    public List<SnmpDeviceResult> Devices { get; set; } = [];

    [JsonPropertyName("unreachable")]
    public List<string> Unreachable { get; set; } = [];

    [JsonPropertyName("scannedAt")]
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
}

public class SnmpProfilesResponse
{
    [JsonPropertyName("profiles")]
    public List<SnmpVendorProfile> Profiles { get; set; } = [];
}

public class SnmpVendorProfile
{
    [JsonPropertyName("oidPrefix")]
    public string OidPrefix { get; set; } = null!;

    [JsonPropertyName("vendor")]
    public string Vendor { get; set; } = null!;

    [JsonPropertyName("oids")]
    public List<SnmpProfileOidEntry> Oids { get; set; } = [];
}

public class SnmpProfileOidEntry
{
    [JsonPropertyName("oid")]
    public string Oid { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("category")]
    public string Category { get; set; } = null!;

    [JsonPropertyName("dataType")]
    public string DataType { get; set; } = "gauge";

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("walk")]
    public bool Walk { get; set; }
}
