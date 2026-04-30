using System.Text.Json.Serialization;

namespace KryossAgent.Models;

public class AgentError
{
    public string Phase { get; set; } = null!;
    public string Message { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public string? Target { get; set; }
    public bool IsTimeout { get; set; }
}

public class AgentErrorDto
{
    [JsonPropertyName("phase")] public string Phase { get; set; } = null!;
    [JsonPropertyName("message")] public string Message { get; set; } = null!;
    [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
    [JsonPropertyName("target")] public string? Target { get; set; }
    [JsonPropertyName("isTimeout")] public bool IsTimeout { get; set; }
}

public class LoopStatusDto
{
    [JsonPropertyName("lastRunAt")] public DateTime? LastRunAt { get; set; }
    [JsonPropertyName("lastDurationMs")] public int? LastDurationMs { get; set; }
    [JsonPropertyName("lastError")] public string? LastError { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = "idle";
}
