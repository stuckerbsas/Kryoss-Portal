using System.Text.Json.Serialization;

namespace KryossAgent.Models;

public class ScheduleResponse
{
    [JsonPropertyName("runNow")]
    public bool RunNow { get; set; }

    [JsonPropertyName("runAt")]
    public DateTime RunAt { get; set; }

    [JsonPropertyName("windowStart")]
    public string WindowStart { get; set; } = "";

    [JsonPropertyName("windowEnd")]
    public string WindowEnd { get; set; } = "";

    [JsonPropertyName("slotOffsetSec")]
    public int SlotOffsetSec { get; set; }
}
