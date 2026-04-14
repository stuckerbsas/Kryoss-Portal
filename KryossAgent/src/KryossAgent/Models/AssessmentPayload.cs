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

    // v1.5.1: When true, agent calls ProtocolAuditService.Configure() post-enroll
    // to enable NTLM+SMB1 audit and resize event logs for 90-day retention.
    [JsonPropertyName("protocolAuditEnabled")]
    public bool ProtocolAuditEnabled { get; set; }
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
}
