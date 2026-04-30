using System.Text.Json.Serialization;

namespace KryossAgent.Models;

/// <summary>
/// Full payload uploaded via POST /v1/results.
/// Contains raw check results + hardware + software inventory.
/// </summary>
public class AssessmentPayload
{
    [JsonPropertyName("agentId")]
    public Guid AgentId { get; set; }

    [JsonPropertyName("agentVersion")]
    public string AgentVersion { get; set; } = "1.0.0";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("durationMs")]
    public int DurationMs { get; set; }

    [JsonPropertyName("platform")]
    public PlatformInfo? Platform { get; set; }

    [JsonPropertyName("hardware")]
    public HardwareInfo? Hardware { get; set; }

    [JsonPropertyName("software")]
    public List<SoftwareItem> Software { get; set; } = [];

    [JsonPropertyName("results")]
    public List<CheckResult> Results { get; set; } = [];

    [JsonPropertyName("networkDiag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NetworkDiagResult? NetworkDiag { get; set; }

    [JsonPropertyName("localAdmins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LocalAdminItem>? LocalAdmins { get; set; }

    [JsonPropertyName("patchStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PatchStatusInfo? PatchStatus { get; set; }
}

public class PlatformInfo
{
    [JsonPropertyName("os")]
    public string? Os { get; set; }

    [JsonPropertyName("build")]
    public string? Build { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public class HardwareInfo
{
    [JsonPropertyName("cpu")]
    public string? Cpu { get; set; }

    [JsonPropertyName("cpuCores")]
    public short? CpuCores { get; set; }

    [JsonPropertyName("ramGb")]
    public short? RamGb { get; set; }

    [JsonPropertyName("diskType")]
    public string? DiskType { get; set; }

    [JsonPropertyName("diskSizeGb")]
    public int? DiskSizeGb { get; set; }

    [JsonPropertyName("diskFreeGb")]
    public decimal? DiskFreeGb { get; set; }

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("serialNumber")]
    public string? SerialNumber { get; set; }

    // Security
    [JsonPropertyName("tpmPresent")]
    public bool? TpmPresent { get; set; }

    [JsonPropertyName("tpmVersion")]
    public string? TpmVersion { get; set; }

    [JsonPropertyName("secureBoot")]
    public bool? SecureBoot { get; set; }

    [JsonPropertyName("bitlocker")]
    public bool? Bitlocker { get; set; }

    // Network
    [JsonPropertyName("ipAddress")]
    public string? IpAddress { get; set; }

    [JsonPropertyName("macAddress")]
    public string? MacAddress { get; set; }

    // Identity
    [JsonPropertyName("domainStatus")]
    public string? DomainStatus { get; set; }

    [JsonPropertyName("domainName")]
    public string? DomainName { get; set; }

    [JsonPropertyName("aadTenantId")]
    public string? AadTenantId { get; set; }

    // OS role: 1=Workstation, 2=DomainController, 3=Server
    [JsonPropertyName("productType")]
    public int? ProductType { get; set; }

    // Lifecycle
    [JsonPropertyName("systemAgeDays")]
    public int? SystemAgeDays { get; set; }

    [JsonPropertyName("lastBootAt")]
    public DateTime? LastBootAt { get; set; }

    // Multi-disk inventory
    [JsonPropertyName("disks")]
    public List<DiskInfo> Disks { get; set; } = [];

    // Threat detection
    [JsonPropertyName("threats")]
    public List<ThreatFinding> Threats { get; set; } = [];

    // Legacy compat
    [JsonPropertyName("tpm")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tpm { get; set; }
}

public class DiskInfo
{
    [JsonPropertyName("driveLetter")]
    public string DriveLetter { get; set; } = null!;

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    [JsonPropertyName("diskType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DiskType { get; set; }

    [JsonPropertyName("totalGb")]
    public int? TotalGb { get; set; }

    [JsonPropertyName("freeGb")]
    public decimal? FreeGb { get; set; }

    [JsonPropertyName("fileSystem")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileSystem { get; set; }
}

public class SoftwareItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    [JsonPropertyName("publisher")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publisher { get; set; }
}

public class ThreatFinding
{
    [JsonPropertyName("threatName")]
    public string ThreatName { get; set; } = null!;

    [JsonPropertyName("category")]
    public string Category { get; set; } = null!;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = null!;

    [JsonPropertyName("vector")]
    public string Vector { get; set; } = null!;

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }
}

/// <summary>
/// POST /v1/hygiene payload
/// </summary>
public class HygienePayload
{
    [JsonPropertyName("scannedBy")]
    public string ScannedBy { get; set; } = null!;

    [JsonPropertyName("totalMachines")]
    public int TotalMachines { get; set; }

    [JsonPropertyName("totalUsers")]
    public int TotalUsers { get; set; }

    [JsonPropertyName("findings")]
    public List<HygieneFinding> Findings { get; set; } = [];
}

public class HygieneFinding
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("objectType")]
    public string ObjectType { get; set; } = null!;

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("daysInactive")]
    public int DaysInactive { get; set; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }
}

/// <summary>
/// POST /v1/ports payload
/// </summary>
public class PortPayload
{
    [JsonPropertyName("machineHostname")]
    public string MachineHostname { get; set; } = null!;

    [JsonPropertyName("ports")]
    public List<PortEntry> Ports { get; set; } = [];
}

public class PortEntry
{
    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = null!;

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("service")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Service { get; set; }

    [JsonPropertyName("risk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Risk { get; set; }

    [JsonPropertyName("banner")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Banner { get; set; }

    [JsonPropertyName("serviceName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServiceName { get; set; }

    [JsonPropertyName("serviceVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServiceVersion { get; set; }
}

public class PortBulkPayload
{
    [JsonPropertyName("machines")]
    public List<PortPayload> Machines { get; set; } = [];
}

public class PatchStatusInfo
{
    [JsonPropertyName("updateSource")]
    public string? UpdateSource { get; set; }

    [JsonPropertyName("wsusServer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WsusServer { get; set; }

    [JsonPropertyName("wufbRing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WufbRing { get; set; }

    [JsonPropertyName("lastCheckUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastCheckUtc { get; set; }

    [JsonPropertyName("lastInstallUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastInstallUtc { get; set; }

    [JsonPropertyName("rebootPending")]
    public bool RebootPending { get; set; }

    [JsonPropertyName("installedCount30d")]
    public int InstalledCount30d { get; set; }

    [JsonPropertyName("installedCount90d")]
    public int InstalledCount90d { get; set; }

    [JsonPropertyName("wuServiceStatus")]
    public string? WuServiceStatus { get; set; }

    [JsonPropertyName("ninjaManaged")]
    public bool NinjaManaged { get; set; }

    [JsonPropertyName("hotfixes")]
    public List<HotfixItem> Hotfixes { get; set; } = [];
}

public class HotfixItem
{
    [JsonPropertyName("hotfixId")]
    public string HotfixId { get; set; } = null!;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("installedOn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? InstalledOn { get; set; }

    [JsonPropertyName("installedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstalledBy { get; set; }
}

/// <summary>
/// POST /v1/dc-health payload
/// </summary>
public class DcHealthPayload
{
    [JsonPropertyName("scannedBy")]
    public string ScannedBy { get; set; } = null!;

    [JsonPropertyName("schemaVersion")]
    public int? SchemaVersion { get; set; }

    [JsonPropertyName("schemaVersionLabel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SchemaVersionLabel { get; set; }

    [JsonPropertyName("forestLevel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ForestLevel { get; set; }

    [JsonPropertyName("domainLevel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DomainLevel { get; set; }

    [JsonPropertyName("forestName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ForestName { get; set; }

    [JsonPropertyName("domainName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DomainName { get; set; }

    [JsonPropertyName("schemaMaster")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SchemaMaster { get; set; }

    [JsonPropertyName("domainNamingMaster")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DomainNamingMaster { get; set; }

    [JsonPropertyName("pdcEmulator")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PdcEmulator { get; set; }

    [JsonPropertyName("ridMaster")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RidMaster { get; set; }

    [JsonPropertyName("infrastructureMaster")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InfrastructureMaster { get; set; }

    [JsonPropertyName("fsmoSinglePoint")]
    public bool FsmoSinglePoint { get; set; }

    [JsonPropertyName("replPartnerCount")]
    public int ReplPartnerCount { get; set; }

    [JsonPropertyName("replFailureCount")]
    public int ReplFailureCount { get; set; }

    [JsonPropertyName("lastSuccessfulRepl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastSuccessfulRepl { get; set; }

    [JsonPropertyName("siteCount")]
    public int SiteCount { get; set; }

    [JsonPropertyName("subnetCount")]
    public int SubnetCount { get; set; }

    [JsonPropertyName("dcCount")]
    public int DcCount { get; set; }

    [JsonPropertyName("gcCount")]
    public int GcCount { get; set; }

    [JsonPropertyName("replicationPartners")]
    public List<DcReplPartner> ReplicationPartners { get; set; } = [];
}

public class DcReplPartner
{
    [JsonPropertyName("partnerHostname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PartnerHostname { get; set; }

    [JsonPropertyName("partnerDn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PartnerDn { get; set; }

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = null!;

    [JsonPropertyName("namingContext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NamingContext { get; set; }

    [JsonPropertyName("lastSuccess")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastSuccess { get; set; }

    [JsonPropertyName("lastAttempt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastAttempt { get; set; }

    [JsonPropertyName("failureCount")]
    public int FailureCount { get; set; }

    [JsonPropertyName("lastError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastError { get; set; }

    [JsonPropertyName("transport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Transport { get; set; }
}

/// <summary>
/// Request body for POST /v1/enroll
/// </summary>
public class EnrollRequest
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = null!;

    [JsonPropertyName("os")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Os { get; set; }

    [JsonPropertyName("osVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OsVersion { get; set; }

    [JsonPropertyName("osBuild")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OsBuild { get; set; }

    [JsonPropertyName("productType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ProductType { get; set; }
}

public class LocalAdminItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!; // User, Group

    [JsonPropertyName("source")]
    public string Source { get; set; } = null!; // Local, Domain
}

/// <summary>
/// Response from POST /v1/enroll
/// </summary>
public class EnrollmentResponse
{
    [JsonPropertyName("agentId")]
    public Guid AgentId { get; set; }

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = null!;

    [JsonPropertyName("apiSecret")]
    public string ApiSecret { get; set; } = null!;

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = null!;

    [JsonPropertyName("assessmentId")]
    public int? AssessmentId { get; set; }

    [JsonPropertyName("assessmentName")]
    public string? AssessmentName { get; set; }

    [JsonPropertyName("protocolAuditEnabled")]
    public bool ProtocolAuditEnabled { get; set; }

    [JsonPropertyName("isTrial")]
    public bool IsTrial { get; set; }

    [JsonPropertyName("trialExpiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? TrialExpiresAt { get; set; }

    [JsonPropertyName("organizationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Guid OrganizationId { get; set; }

    // Per-machine auth credentials (v2.2+)
    [JsonPropertyName("machineSecret")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MachineSecret { get; set; }

    [JsonPropertyName("sessionKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionKey { get; set; }

    [JsonPropertyName("sessionKeyExpiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? SessionKeyExpiresAt { get; set; }
}

/// <summary>
/// Response from POST /v1/results
/// </summary>
public class ResultsResponse
{
    [JsonPropertyName("runId")]
    public Guid RunId { get; set; }

    [JsonPropertyName("score")]
    public decimal Score { get; set; }

    [JsonPropertyName("grade")]
    public string Grade { get; set; } = null!;

    [JsonPropertyName("passCount")]
    public int PassCount { get; set; }

    [JsonPropertyName("warnCount")]
    public int WarnCount { get; set; }

    [JsonPropertyName("failCount")]
    public int FailCount { get; set; }

    [JsonPropertyName("yourPublicIp")]
    public string? YourPublicIp { get; set; }

    [JsonPropertyName("speedtestRequested")]
    public bool SpeedtestRequested { get; set; }
}
