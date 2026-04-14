using System.Diagnostics.Eventing.Reader;
using Microsoft.Win32;

namespace KryossAgent.Services;

/// <summary>
/// v1.5.1 — Protocol Usage Audit configuration.
///
/// When the enrollment response sets `protocolAuditEnabled=true`, the agent
/// calls <see cref="Configure"/> post-enroll to:
///
///   1. Enable NTLM inbound auditing (LSA MSV1_0 AuditReceivingNTLMTraffic=2)
///   2. Enable NTLM outbound auditing (LSA MSV1_0 RestrictSendingNTLMTraffic=1)
///   3. Enable SMB1 access auditing (LanmanServer AuditSmb1Access=1)
///   4. Resize Security log to 500 MB (for 90-day auth event retention)
///   5. Resize Microsoft-Windows-NTLM/Operational to 300 MB
///   6. Resize Microsoft-Windows-SMBServer/Audit to 300 MB
///
/// All operations are IDEMPOTENT and log errors non-fatally. The agent will
/// attempt configuration on every run while protocolAuditEnabled is true;
/// already-configured items are a no-op.
///
/// SECURITY EXCEPTION: This is the ONLY service in the agent that writes to
/// registry + event log config. The agent is otherwise a passive read-only
/// sensor. This deviation is acknowledged and scoped: it is opt-in per-org
/// via the portal, and only touches audit-related keys that cannot harm the
/// host's functional state.
/// </summary>
public static class ProtocolAuditService
{
    // Target sizes (bytes). 90-day retention for enterprise-grade analysis.
    private const long SecurityLogSize = 524_288_000;   // 500 MB
    private const long NtlmLogSize     = 314_572_800;   // 300 MB
    private const long SmbAuditLogSize = 314_572_800;   // 300 MB

    public static void Configure(bool verbose)
    {
        int ok = 0, failed = 0;

        // 1. NTLM inbound audit
        if (SetRegDword(
            @"SYSTEM\CurrentControlSet\Control\Lsa\MSV1_0",
            "AuditReceivingNTLMTraffic", 2, verbose)) ok++; else failed++;

        // 2. NTLM outbound audit
        if (SetRegDword(
            @"SYSTEM\CurrentControlSet\Control\Lsa\MSV1_0",
            "RestrictSendingNTLMTraffic", 1, verbose)) ok++; else failed++;

        // 3. SMB1 access audit
        if (SetRegDword(
            @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters",
            "AuditSmb1Access", 1, verbose)) ok++; else failed++;

        // 4. Security log size
        if (ResizeEventLog("Security", SecurityLogSize, verbose)) ok++; else failed++;

        // 5. NTLM Operational log size (enable first if disabled)
        if (ResizeEventLog("Microsoft-Windows-NTLM/Operational", NtlmLogSize, verbose)) ok++; else failed++;

        // 6. SMBServer Audit log size (enable first if disabled)
        if (ResizeEventLog("Microsoft-Windows-SMBServer/Audit", SmbAuditLogSize, verbose)) ok++; else failed++;

        if (verbose)
        {
            Console.WriteLine($"  [protocol-audit] {ok} OK / {failed} failed");
        }
    }

    /// <summary>
    /// Idempotent REG_DWORD write. Returns true if the value matches after
    /// the call (whether we wrote it or it was already correct).
    /// </summary>
    private static bool SetRegDword(string subKey, string valueName, int expected, bool verbose)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(subKey, writable: true);
            if (key is null)
            {
                if (verbose) Console.Error.WriteLine($"  [protocol-audit] cannot open/create {subKey}");
                return false;
            }

            var current = key.GetValue(valueName) as int?;
            if (current == expected)
            {
                if (verbose) Console.WriteLine($"  [protocol-audit] {valueName}={expected} already set");
                return true;
            }

            key.SetValue(valueName, expected, RegistryValueKind.DWord);
            if (verbose) Console.WriteLine($"  [protocol-audit] {valueName}={expected} written (was {current})");
            return true;
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"  [protocol-audit] {valueName} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Idempotent event log resize via EventLogConfiguration (native .NET,
    /// no wevtutil shell). Enables the log first if disabled.
    /// </summary>
    private static bool ResizeEventLog(string logName, long targetSize, bool verbose)
    {
        try
        {
            using var cfg = new EventLogConfiguration(logName);

            // Enable log if disabled (e.g. NTLM Operational is off by default)
            if (!cfg.IsEnabled)
            {
                cfg.IsEnabled = true;
                cfg.SaveChanges();
                if (verbose) Console.WriteLine($"  [protocol-audit] enabled {logName}");
                // Must reload config after SaveChanges to persist subsequent edits
                using var cfg2 = new EventLogConfiguration(logName);
                return ApplySize(cfg2, logName, targetSize, verbose);
            }

            return ApplySize(cfg, logName, targetSize, verbose);
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"  [protocol-audit] resize {logName} failed: {ex.Message}");
            return false;
        }
    }

    private static bool ApplySize(EventLogConfiguration cfg, string logName, long targetSize, bool verbose)
    {
        if (cfg.MaximumSizeInBytes >= targetSize)
        {
            if (verbose)
                Console.WriteLine($"  [protocol-audit] {logName} already >= target ({cfg.MaximumSizeInBytes / 1_048_576} MB)");
            return true;
        }

        // Round target to 64 KB boundary as Windows requires
        var rounded = (targetSize / 65_536) * 65_536;
        cfg.MaximumSizeInBytes = rounded;
        cfg.SaveChanges();
        if (verbose)
            Console.WriteLine($"  [protocol-audit] {logName} resized to {rounded / 1_048_576} MB");
        return true;
    }
}
