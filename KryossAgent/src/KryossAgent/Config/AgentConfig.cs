using Microsoft.Win32;

namespace KryossAgent.Config;

/// <summary>
/// Agent configuration stored in Windows Registry (HKLM\SOFTWARE\Kryoss\Agent).
/// SYSTEM-only ACL in production — only the agent (running as SYSTEM) can read/write.
/// </summary>
public class AgentConfig
{
    private const string RegistryPath = @"SOFTWARE\Kryoss\Agent";

    // Intentional: cloud-hosted service, overridable via --api-url or binary patching. SPKI pinning (PinnedHttpHandler) guards DNS.
    public string ApiUrl { get; set; } = "https://func-kryoss.azurewebsites.net";
    public Guid AgentId { get; set; }
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string PublicKeyPem { get; set; } = "";
    public string? MachineSecret { get; set; }
    public string? SessionKey { get; set; }
    public DateTime? SessionKeyExpiresAt { get; set; }
    public int? AssessmentId { get; set; }
    public string? AssessmentName { get; set; }

    /// <summary>
    /// Comma-separated list of base64-encoded SHA-256 hashes of the expected
    /// server SubjectPublicKeyInfo (SPKI). When set, the agent REJECTS any
    /// TLS connection whose leaf cert's SPKI hash doesn't match one of these.
    /// When null/empty, the agent runs in "log-only" mode: it prints the
    /// observed SPKI hash on every connect so operators can capture the
    /// production value before enforcing. See security-baseline.md §SPKI pinning.
    /// Two slots minimum in production (primary + backup for rotation).
    /// </summary>
    public string[]? SpkiPins { get; set; }

    public int ScanIntervalMinutes { get; set; } = 240;
    public int ComplianceIntervalHours { get; set; } = 24;
    public bool EnableNetworkScan { get; set; }
    public int NetworkScanIntervalHours { get; set; } = 12;
    public bool EnablePassiveDiscovery { get; set; } = true;

    public bool IsEnrolled => AgentId != Guid.Empty && !string.IsNullOrEmpty(ApiKey);

    /// <summary>
    /// Load config from registry. Returns default (unenrolled) config if not found.
    /// </summary>
    public static AgentConfig Load()
    {
        var config = new AgentConfig();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryPath);
            if (key is null) return config;

            config.ApiUrl = key.GetValue("ApiUrl") as string ?? config.ApiUrl;
            var agentIdStr = key.GetValue("AgentId") as string;
            if (Guid.TryParse(agentIdStr, out var agentId))
                config.AgentId = agentId;
            config.ApiKey = key.GetValue("ApiKey") as string ?? "";
            config.ApiSecret = key.GetValue("ApiSecret") as string ?? "";
            config.PublicKeyPem = key.GetValue("PublicKeyPem") as string ?? "";
            config.MachineSecret = key.GetValue("MachineSecret") as string;
            config.SessionKey = key.GetValue("SessionKey") as string;
            config.SessionKeyExpiresAt = key.GetValue("SessionKeyExpiresAt") is string expStr && DateTime.TryParse(expStr, out var exp) ? exp : null;
            var assessmentIdStr = key.GetValue("AssessmentId") as string;
            if (int.TryParse(assessmentIdStr, out var assessmentId))
                config.AssessmentId = assessmentId;
            config.AssessmentName = key.GetValue("AssessmentName") as string;

            // SpkiPins is a comma-separated list in the registry (REG_SZ).
            // Split on comma, trim, drop empties. Nothing fancy — the base64
            // alphabet doesn't include comma so this is unambiguous.
            if (int.TryParse(key.GetValue("ScanIntervalMinutes") as string, out var scanInterval) && scanInterval > 0)
                config.ScanIntervalMinutes = scanInterval;
            if (int.TryParse(key.GetValue("ComplianceIntervalHours") as string, out var compInterval) && compInterval > 0)
                config.ComplianceIntervalHours = compInterval;
            if (key.GetValue("EnableNetworkScan") is string ens)
                config.EnableNetworkScan = ens == "1";
            if (int.TryParse(key.GetValue("NetworkScanIntervalHours") as string, out var netInterval) && netInterval > 0)
                config.NetworkScanIntervalHours = netInterval;
            if (key.GetValue("EnablePassiveDiscovery") is string epd)
                config.EnablePassiveDiscovery = epd != "0";

            var pinsRaw = key.GetValue("SpkiPins") as string;
            if (!string.IsNullOrWhiteSpace(pinsRaw))
            {
                config.SpkiPins = pinsRaw
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] Failed to read registry config: {ex.Message}");
        }
        return config;
    }

    public void Save(bool debugAcl = false)
    {
        using var key = Registry.LocalMachine.CreateSubKey(RegistryPath);
        key.SetValue("ApiUrl", ApiUrl);
        key.SetValue("AgentId", AgentId.ToString());
        key.SetValue("ApiKey", ApiKey);
        key.SetValue("ApiSecret", ApiSecret);
        key.SetValue("PublicKeyPem", PublicKeyPem);
        if (MachineSecret is not null) key.SetValue("MachineSecret", MachineSecret);
        if (SessionKey is not null) key.SetValue("SessionKey", SessionKey);
        if (SessionKeyExpiresAt is not null) key.SetValue("SessionKeyExpiresAt", SessionKeyExpiresAt.Value.ToString("O"));
        if (AssessmentId.HasValue)
            key.SetValue("AssessmentId", AssessmentId.Value.ToString());
        if (AssessmentName is not null)
            key.SetValue("AssessmentName", AssessmentName);
        key.SetValue("ScanIntervalMinutes", ScanIntervalMinutes.ToString());
        key.SetValue("ComplianceIntervalHours", ComplianceIntervalHours.ToString());
        key.SetValue("EnableNetworkScan", EnableNetworkScan ? "1" : "0");
        key.SetValue("NetworkScanIntervalHours", NetworkScanIntervalHours.ToString());
        key.SetValue("EnablePassiveDiscovery", EnablePassiveDiscovery ? "1" : "0");

        try
        {
            var security = new System.Security.AccessControl.RegistrySecurity();

            security.AddAccessRule(new System.Security.AccessControl.RegistryAccessRule(
                new System.Security.Principal.SecurityIdentifier(
                    System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
                System.Security.AccessControl.RegistryRights.FullControl,
                System.Security.AccessControl.InheritanceFlags.ContainerInherit |
                    System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                System.Security.AccessControl.PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));

            if (debugAcl)
            {
                Console.Error.WriteLine("[WARN] --debug-acl: registry ACL includes Administrators (testing only)");
                security.AddAccessRule(new System.Security.AccessControl.RegistryAccessRule(
                    new System.Security.Principal.SecurityIdentifier(
                        System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null),
                    System.Security.AccessControl.RegistryRights.FullControl,
                    System.Security.AccessControl.InheritanceFlags.ContainerInherit |
                        System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                    System.Security.AccessControl.PropagationFlags.None,
                    System.Security.AccessControl.AccessControlType.Allow));
            }

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            key.SetAccessControl(security);
        }
        catch
        {
            if (Environment.GetEnvironmentVariable("KRYOSS_VERBOSE") == "1")
                Console.Error.WriteLine("[WARN] Failed to set ACL on registry key.");
        }
    }

    /// <summary>
    /// Wipes the registry key entirely. Called after a successful upload cycle
    /// when no offline queue items remain — the agent has no reason to keep
    /// credentials on disk between runs.
    ///
    /// Safe to call when the key doesn't exist.
    /// </summary>
    public static void Wipe()
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            if (Environment.GetEnvironmentVariable("KRYOSS_VERBOSE") == "1")
                Console.Error.WriteLine($"[WARN] Failed to wipe registry key: {ex.Message}");
        }
    }
}
