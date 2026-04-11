using Microsoft.Win32;
using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Detects TPM presence, version, manufacturer, and ready state.
/// AOT-safe: registry reads + a single batched shell-out to
/// <c>tpmtool.exe getdeviceinformation</c>. Falls back gracefully
/// if tpmtool is missing or fails.
///
/// Supported <c>CheckType</c> values:
///   present       -> bool
///   version       -> string ("2.0" / "1.2" / "none")
///   spec_version  -> string (full spec version reported by tpmtool)
///   manufacturer  -> string (INTC, IFX, STM, AMD, etc.)
///   ready_state   -> "Ready" / "NotReady" / "Unknown"
///   enabled       -> bool
/// </summary>
public class TpmEngine : ICheckEngine
{
    public string Type => "tpm";

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        // Collect TPM info once for the whole batch.
        TpmInfo info;
        try
        {
            info = CollectTpmInfo();
        }
        catch (Exception ex)
        {
            info = new TpmInfo { Error = ex.Message };
        }

        foreach (var control in controls)
        {
            results.Add(ExecuteOne(control, info));
        }
        return results;
    }

    private static CheckResult ExecuteOne(ControlDef control, TpmInfo info)
    {
        var result = new CheckResult { Id = control.Id };

        if (info.Error is not null && !info.Present)
        {
            // If collection itself failed but we can still answer "present=false",
            // treat as unknown hardware rather than hard error.
            result.Exists = false;
            result.Value = info.Error;
            return result;
        }

        var checkType = control.CheckType ?? "present";
        switch (checkType)
        {
            case "present":
                result.Exists = true;
                result.Value = info.Present;
                break;
            case "version":
                result.Exists = true;
                result.Value = info.Version ?? "none";
                break;
            case "spec_version":
                result.Exists = info.SpecVersion is not null;
                result.Value = info.SpecVersion;
                break;
            case "manufacturer":
                result.Exists = info.Manufacturer is not null;
                result.Value = info.Manufacturer;
                break;
            case "ready_state":
                result.Exists = true;
                result.Value = info.ReadyState ?? "Unknown";
                break;
            case "enabled":
                result.Exists = true;
                result.Value = info.Enabled;
                break;
            default:
                result.Exists = false;
                result.Value = $"ERROR: unknown checkType '{checkType}'";
                break;
        }
        return result;
    }

    private static TpmInfo CollectTpmInfo()
    {
        var info = new TpmInfo();

        // ─── Registry signals ──────────────────────────────────────────────
        // TPM base service presence
        try
        {
            using var svc = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\TPM");
            if (svc is not null)
            {
                info.Enabled = true;
            }
        }
        catch { /* ignore */ }

        // WBEM TPM provider keys (device instance) — best-effort, optional
        try
        {
            using var wmi = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\Tpm\Parameters");
            // Some systems expose nothing here; that's fine.
            _ = wmi;
        }
        catch { /* ignore */ }

        // ─── tpmtool.exe getdeviceinformation ─────────────────────────────
        var tpmtoolOutput = TryRunTpmtool();
        if (!string.IsNullOrWhiteSpace(tpmtoolOutput))
        {
            ParseTpmtoolOutput(tpmtoolOutput, info);
        }
        else if (!info.Enabled)
        {
            // Neither registry nor tpmtool told us anything — probably no TPM.
            info.Present = false;
            info.Version = "none";
            info.ReadyState = "Unknown";
        }

        return info;
    }

    private static string? TryRunTpmtool()
    {
        try
        {
            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var exe = Path.Combine(systemRoot, "tpmtool.exe");
            if (!File.Exists(exe)) return null;

            var run = ProcessHelper.RunCaptured(
                exePath: exe,
                arguments: "getdeviceinformation",
                timeoutSeconds: 15,
                stdoutCapBytes: 8192,
                stderrCapBytes: 1024);

            if (!run.Started || run.TimedOut) return null;
            return run.Stdout;
        }
        catch
        {
            return null;
        }
    }

    // Sample tpmtool output:
    //   -TPM Present: True
    //   -TPM Version: 2.0
    //   -TPM Manufacturer ID: INTC
    //   -TPM Manufacturer Full Name: Intel
    //   -TPM Manufacturer Version: 403.1.0.0
    //   -PPI Version: 1.3
    //   -Is Initialized: True
    //   -Ready For Storage: True
    //   -Ready For Attestation: True
    //   -Maintenance Task Complete: True
    //   -TPM Spec Version: 1.38
    private static void ParseTpmtoolOutput(string stdout, TpmInfo info)
    {
        var readyForStorage = false;
        var readyForAttestation = false;

        foreach (var rawLine in stdout.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('-').Trim();
            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;
            var key = line.Substring(0, colonIdx).Trim();
            var val = line.Substring(colonIdx + 1).Trim();

            if (key.Equals("TPM Present", StringComparison.OrdinalIgnoreCase))
            {
                info.Present = val.Equals("True", StringComparison.OrdinalIgnoreCase);
            }
            else if (key.Equals("TPM Version", StringComparison.OrdinalIgnoreCase))
            {
                info.Version = val;
            }
            else if (key.Equals("TPM Spec Version", StringComparison.OrdinalIgnoreCase))
            {
                info.SpecVersion = val;
            }
            else if (key.Equals("TPM Manufacturer ID", StringComparison.OrdinalIgnoreCase))
            {
                info.Manufacturer = val;
            }
            else if (key.Equals("Ready For Storage", StringComparison.OrdinalIgnoreCase))
            {
                readyForStorage = val.Equals("True", StringComparison.OrdinalIgnoreCase);
            }
            else if (key.Equals("Ready For Attestation", StringComparison.OrdinalIgnoreCase))
            {
                readyForAttestation = val.Equals("True", StringComparison.OrdinalIgnoreCase);
            }
        }

        info.ReadyState = info.Present
            ? (readyForStorage && readyForAttestation ? "Ready" : "NotReady")
            : "Unknown";

        if (info.Present && !info.Enabled)
            info.Enabled = true;
    }

    private sealed class TpmInfo
    {
        public bool Present { get; set; }
        public bool Enabled { get; set; }
        public string? Version { get; set; }
        public string? SpecVersion { get; set; }
        public string? Manufacturer { get; set; }
        public string? ReadyState { get; set; }
        public string? Error { get; set; }
    }
}
