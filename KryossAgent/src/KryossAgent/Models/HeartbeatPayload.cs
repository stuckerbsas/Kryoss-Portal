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
}
