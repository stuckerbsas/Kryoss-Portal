using System.Text.Json.Serialization;

namespace KryossAgent.Models;

public class HeartbeatResponse
{
    [JsonPropertyName("ack")]
    public bool Ack { get; set; }

    [JsonPropertyName("pendingTasks")]
    public List<PendingRemediationTask>? PendingTasks { get; set; }

    [JsonPropertyName("newMachineSecret")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NewMachineSecret { get; set; }

    [JsonPropertyName("newSessionKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NewSessionKey { get; set; }

    [JsonPropertyName("newSessionKeyExpiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? NewSessionKeyExpiresAt { get; set; }

    [JsonPropertyName("config")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentRemoteConfig? Config { get; set; }

    [JsonPropertyName("forceScan")]
    public bool ForceScan { get; set; }

    [JsonPropertyName("latestAgentVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LatestAgentVersion { get; set; }

    [JsonPropertyName("minAgentVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MinAgentVersion { get; set; }

    [JsonPropertyName("apiVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApiVersion { get; set; }

    [JsonPropertyName("modeDev")]
    public bool ModeDev { get; set; }
}

public class AgentRemoteConfig
{
    [JsonPropertyName("complianceIntervalHours")]
    public int ComplianceIntervalHours { get; set; } = 24;

    [JsonPropertyName("snmpIntervalMinutes")]
    public int SnmpIntervalMinutes { get; set; } = 240;

    [JsonPropertyName("enableNetworkScan")]
    public bool EnableNetworkScan { get; set; }

    [JsonPropertyName("networkScanIntervalHours")]
    public int NetworkScanIntervalHours { get; set; } = 12;

    [JsonPropertyName("enablePassiveDiscovery")]
    public bool EnablePassiveDiscovery { get; set; } = true;

    [JsonPropertyName("priorityServices")]
    public List<string>? PriorityServices { get; set; }
}

public class PendingRemediationTask
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = null!;

    [JsonPropertyName("params")]
    public string? Params { get; set; }

    [JsonPropertyName("controlDefId")]
    public int? ControlDefId { get; set; }

    [JsonPropertyName("controlId")]
    public string? ControlId { get; set; }

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    [JsonPropertyName("approvedAt")]
    public DateTime? ApprovedAt { get; set; }
}

public class TaskResultPayload
{
    [JsonPropertyName("taskId")]
    public long TaskId { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("previousValue")]
    public string? PreviousValue { get; set; }

    [JsonPropertyName("newValue")]
    public string? NewValue { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("executedAt")]
    public DateTime? ExecutedAt { get; set; }

    [JsonPropertyName("restorePointCreated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RestorePointCreated { get; set; }

    [JsonPropertyName("restorePointName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RestorePointName { get; set; }
}

public class ServiceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("startupType")]
    public string StartupType { get; set; } = null!;
}
