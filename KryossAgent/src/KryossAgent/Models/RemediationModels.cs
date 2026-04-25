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
    public int ControlDefId { get; set; }

    [JsonPropertyName("controlId")]
    public string? ControlId { get; set; }
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
}
