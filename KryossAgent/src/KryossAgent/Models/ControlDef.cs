using System.Text.Json.Serialization;

namespace KryossAgent.Models;

/// <summary>
/// Control definition downloaded from GET /v1/controls.
/// Only contains agent-relevant fields — no expected values, severity, or names.
/// </summary>
public class ControlDef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!; // BL-001

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!; // registry, secedit, auditpol, firewall, service, netaccount, command, eventlog, certstore, bitlocker, tpm

    // Sub-variant within an engine (e.g. "max_size", "retention", "count_self_signed")
    [JsonPropertyName("checkType")]
    public string? CheckType { get; set; }

    // Registry
    [JsonPropertyName("hive")]
    public string? Hive { get; set; } // HKLM, HKU

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("valueName")]
    public string? ValueName { get; set; }

    // Secedit
    [JsonPropertyName("settingName")]
    public string? SettingName { get; set; }

    // Auditpol
    [JsonPropertyName("subcategory")]
    public string? Subcategory { get; set; }

    // Firewall
    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("property")]
    public string? Property { get; set; }

    // Service
    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; set; }

    // NetAccount
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    // Command
    [JsonPropertyName("executable")]
    public string? Executable { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    // Command override timeout (seconds). ShellEngine default = 15.
    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; set; }

    // Dependency control id (server-side evaluation only; agent treats as opaque)
    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    // EventLog engine
    [JsonPropertyName("logName")]
    public string? LogName { get; set; } // "Security", "System", "Application"

    // CertStore engine
    [JsonPropertyName("storeName")]
    public string? StoreName { get; set; } // "My", "Root", "CA", "TrustedPublisher"

    [JsonPropertyName("storeLocation")]
    public string? StoreLocation { get; set; } // "LocalMachine", "CurrentUser"

    // BitLocker engine
    [JsonPropertyName("drive")]
    public string? Drive { get; set; } // "C:", or "*" for any

    // Display message for progress
    [JsonPropertyName("display")]
    public string? Display { get; set; }
}

/// <summary>
/// Response from GET /v1/controls
/// </summary>
public class ControlsResponse
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("checks")]
    public List<ControlDef> Checks { get; set; } = [];
}
