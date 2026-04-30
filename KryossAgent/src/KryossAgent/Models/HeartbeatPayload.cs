using System.Text.Json.Serialization;

namespace KryossAgent.Models;

public class HeartbeatPayload
{
    [JsonPropertyName("agentId")]
    public Guid AgentId { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = null!;

    [JsonPropertyName("uptimeSeconds")]
    public long UptimeSeconds { get; set; }

    [JsonPropertyName("lastScanAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastScanAt { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "service";

    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AgentErrorDto>? Errors { get; set; }

    [JsonPropertyName("loopStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, LoopStatusDto>? LoopStatus { get; set; }
}
