using System.Text.RegularExpressions;
using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Reads BitLocker drive encryption state via a single batched
/// <c>manage-bde.exe -status</c> invocation. The output is parsed into a
/// per-drive dictionary and each control selects the right field.
///
/// AOT-safe: no WMI, no reflection, no dynamic code. Shells to the
/// in-box system binary (System32\manage-bde.exe) once per engine run.
///
/// Supported <c>CheckType</c> values:
///   protection_status     -> "On" / "Off" / "Unknown"
///   encryption_method     -> e.g. "XTS-AES 256" or "None"
///   protector_types       -> comma-separated list ("TPM,RecoveryPassword")
///   encryption_percent    -> int 0-100
///   recovery_key_present  -> bool
///
/// Required ControlDef fields: <c>Drive</c> (e.g. "C:") or "*" for any
/// encrypted drive (returns first match where ProtectionStatus == On).
/// </summary>
public class BitLockerEngine : ICheckEngine
{
    public string Type => "bitlocker";

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        // Single batched manage-bde invocation for the whole engine run.
        Dictionary<string, DriveInfoBlock> drives;
        string? executionError;
        try
        {
            drives = RunManageBdeStatus(out executionError);
        }
        catch (Exception ex)
        {
            drives = new Dictionary<string, DriveInfoBlock>(StringComparer.OrdinalIgnoreCase);
            executionError = ex.Message;
        }

        foreach (var control in controls)
        {
            results.Add(ExecuteOne(control, drives, executionError));
        }
        return results;
    }

    private static CheckResult ExecuteOne(
        ControlDef control,
        Dictionary<string, DriveInfoBlock> drives,
        string? executionError)
    {
        var result = new CheckResult { Id = control.Id };

        if (executionError is not null)
        {
            result.Exists = null;
            result.Value = $"ERROR: manage-bde failed: {executionError}";
            return result;
        }

        if (string.IsNullOrEmpty(control.Drive))
        {
            result.Exists = false;
            result.Value = "ERROR: drive is required";
            return result;
        }

        DriveInfoBlock? info;
        if (control.Drive == "*")
        {
            info = drives.Values.FirstOrDefault(d =>
                string.Equals(d.ProtectionStatus, "On", StringComparison.OrdinalIgnoreCase));
            if (info is null)
            {
                result.Exists = false;
                result.Value = null;
                return result;
            }
        }
        else
        {
            var key = NormalizeDrive(control.Drive);
            if (!drives.TryGetValue(key, out info))
            {
                result.Exists = false;
                result.Value = null;
                return result;
            }
        }

        var checkType = control.CheckType ?? "protection_status";
        switch (checkType)
        {
            case "protection_status":
                result.Exists = true;
                result.Value = info.ProtectionStatus ?? "Unknown";
                break;
            case "encryption_method":
                result.Exists = true;
                result.Value = info.EncryptionMethod ?? "None";
                break;
            case "protector_types":
                result.Exists = true;
                result.Value = string.Join(",", info.ProtectorTypes);
                break;
            case "encryption_percent":
                result.Exists = true;
                result.Value = info.EncryptionPercent;
                break;
            case "recovery_key_present":
                result.Exists = true;
                result.Value = info.ProtectorTypes.Any(p =>
                    p.Contains("Recovery", StringComparison.OrdinalIgnoreCase));
                break;
            default:
                result.Exists = false;
                result.Value = $"ERROR: unknown checkType '{checkType}'";
                break;
        }

        return result;
    }

    private static string NormalizeDrive(string drive)
    {
        // Accept "C", "C:", "C:\\" — store as "C:".
        var d = drive.Trim().TrimEnd('\\');
        if (d.Length == 1) d += ":";
        return d.ToUpperInvariant();
    }

    private static Dictionary<string, DriveInfoBlock> RunManageBdeStatus(out string? error)
    {
        error = null;
        var result = new Dictionary<string, DriveInfoBlock>(StringComparer.OrdinalIgnoreCase);

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var exePath = Path.Combine(systemRoot, "manage-bde.exe");
        if (!File.Exists(exePath))
        {
            error = "manage-bde.exe not found";
            return result;
        }

        var run = ProcessHelper.RunCaptured(
            exePath: exePath,
            arguments: "-status",
            timeoutSeconds: 30,
            stdoutCapBytes: 32768,
            stderrCapBytes: 1024);

        if (!run.Started)
        {
            error = "failed to start manage-bde.exe";
            return result;
        }

        if (run.TimedOut)
        {
            error = "manage-bde.exe timed out";
            return result;
        }

        ParseStatusOutput(run.Stdout, result);
        return result;
    }

    // Parses manage-bde -status output into per-drive blocks.
    // Output shape (per drive):
    //
    //   Volume C: [OSDisk]
    //   [OS Volume]
    //
    //       Size:                 237.93 GB
    //       BitLocker Version:    2.0
    //       Conversion Status:    Fully Encrypted
    //       Percentage Encrypted: 100.0%
    //       Encryption Method:    XTS-AES 256
    //       Protection Status:    Protection On
    //       Lock Status:          Unlocked
    //       Identification Field: Unknown
    //       Key Protectors:
    //           TPM
    //           Numerical Password
    //
    private static void ParseStatusOutput(string stdout, Dictionary<string, DriveInfoBlock> drives)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return;

        var lines = stdout.Split('\n');
        DriveInfoBlock? current = null;
        string? currentKey = null;
        var inProtectors = false;

        var volumeRegex = new Regex(@"Volume\s+([A-Za-z]):", RegexOptions.IgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();

            var vm = volumeRegex.Match(trimmed);
            if (vm.Success && trimmed.StartsWith("Volume", StringComparison.OrdinalIgnoreCase))
            {
                // Commit previous
                if (current is not null && currentKey is not null)
                    drives[currentKey] = current;

                currentKey = vm.Groups[1].Value.ToUpperInvariant() + ":";
                current = new DriveInfoBlock();
                inProtectors = false;
                continue;
            }

            if (current is null) continue;

            if (trimmed.StartsWith("Key Protectors:", StringComparison.OrdinalIgnoreCase))
            {
                inProtectors = true;
                continue;
            }

            if (inProtectors)
            {
                // Protector list ends on blank line or a new "Label:" line at the section level.
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    inProtectors = false;
                    continue;
                }
                // Heuristic: protector list items are indented and do NOT contain ':'.
                if (!trimmed.Contains(':'))
                {
                    current.ProtectorTypes.Add(trimmed);
                    continue;
                }
                inProtectors = false;
                // fall through to key/value parsing
            }

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0) continue;
            var key = trimmed.Substring(0, colonIdx).Trim();
            var val = trimmed.Substring(colonIdx + 1).Trim();

            if (key.Equals("Protection Status", StringComparison.OrdinalIgnoreCase))
            {
                // "Protection On" / "Protection Off"
                current.ProtectionStatus = val.Contains("On", StringComparison.OrdinalIgnoreCase) ? "On"
                    : val.Contains("Off", StringComparison.OrdinalIgnoreCase) ? "Off"
                    : "Unknown";
            }
            else if (key.Equals("Encryption Method", StringComparison.OrdinalIgnoreCase))
            {
                current.EncryptionMethod = val;
            }
            else if (key.Equals("Percentage Encrypted", StringComparison.OrdinalIgnoreCase))
            {
                // e.g. "100.0%" or "100%"
                var numPart = val.Replace("%", "").Trim();
                if (double.TryParse(numPart, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pct))
                {
                    current.EncryptionPercent = (int)Math.Round(pct);
                }
            }
        }

        // Commit last
        if (current is not null && currentKey is not null)
            drives[currentKey] = current;
    }

    private sealed class DriveInfoBlock
    {
        public string? ProtectionStatus { get; set; }
        public string? EncryptionMethod { get; set; }
        public int EncryptionPercent { get; set; }
        public List<string> ProtectorTypes { get; } = new();
    }
}
