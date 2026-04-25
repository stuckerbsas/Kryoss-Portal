using System.Text.Json.Serialization;

namespace KryossAgent.Models;

/// <summary>
/// Wrapper for offline mode: includes machine identity so a collector
/// can upload on behalf of machines without internet access.
/// Serialized to shared folder as JSON file.
/// </summary>
public class OfflineCollectPayload
{
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = "";

    [JsonPropertyName("hwid")]
    public string? Hwid { get; set; }

    [JsonPropertyName("enrollmentCode")]
    public string? EnrollmentCode { get; set; }

    [JsonPropertyName("osName")]
    public string? OsName { get; set; }

    [JsonPropertyName("osVersion")]
    public string? OsVersion { get; set; }

    [JsonPropertyName("osBuild")]
    public string? OsBuild { get; set; }

    [JsonPropertyName("productType")]
    public int? ProductType { get; set; }

    [JsonPropertyName("agentVersion")]
    public string AgentVersion { get; set; } = "1.5.1";

    [JsonPropertyName("collectedAt")]
    public string CollectedAt { get; set; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("payload")]
    public AssessmentPayload? Payload { get; set; }
}
