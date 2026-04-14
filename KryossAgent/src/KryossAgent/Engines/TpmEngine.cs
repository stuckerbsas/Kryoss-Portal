using System.Management;
using Microsoft.Win32;
using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Detects TPM presence, version, manufacturer, and ready state.
/// v1.4.0: Replaced tpmtool.exe shell-out with WMI Win32_Tpm query.
/// Registry reads kept as-is for fallback detection.
///
/// Supported <c>CheckType</c> values:
///   present       -> bool
///   version       -> string ("2.0" / "1.2" / "none")
///   spec_version  -> string (full spec version)
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

        // ---- Registry signals (fast, always available) ----
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

        // ---- WMI Win32_Tpm query (replaces tpmtool.exe) ----
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2\Security\MicrosoftTpm",
                "SELECT * FROM Win32_Tpm");

            foreach (ManagementObject tpm in searcher.Get())
            {
                // IsEnabled_InitialValue
                var isEnabled = GetBool(tpm, "IsEnabled_InitialValue");
                if (isEnabled.HasValue)
                {
                    info.Enabled = isEnabled.Value;
                    info.Present = true;
                }

                // IsActivated_InitialValue
                var isActivated = GetBool(tpm, "IsActivated_InitialValue");

                // SpecVersion: e.g. "2.0, 0, 1.38" or "1.2, 2.0, ..."
                var specVersionRaw = tpm["SpecVersion"]?.ToString();
                if (!string.IsNullOrEmpty(specVersionRaw))
                {
                    info.SpecVersion = specVersionRaw;

                    // Extract major version (first segment before comma)
                    var firstPart = specVersionRaw.Split(',')[0].Trim();
                    info.Version = firstPart; // "2.0" or "1.2"
                }

                // ManufacturerVersion
                var mfgVersion = tpm["ManufacturerVersion"]?.ToString();

                // ManufacturerId — uint32, convert to 4-char ASCII manufacturer code
                var mfgId = tpm["ManufacturerId"];
                if (mfgId != null)
                {
                    try
                    {
                        var mfgUint = Convert.ToUInt32(mfgId);
                        // TPM manufacturer IDs are 4 ASCII chars packed into uint32
                        var chars = new char[4];
                        chars[0] = (char)((mfgUint >> 24) & 0xFF);
                        chars[1] = (char)((mfgUint >> 16) & 0xFF);
                        chars[2] = (char)((mfgUint >> 8) & 0xFF);
                        chars[3] = (char)(mfgUint & 0xFF);
                        var mfgStr = new string(chars).Trim('\0').Trim();
                        if (!string.IsNullOrEmpty(mfgStr))
                            info.Manufacturer = mfgStr;
                    }
                    catch { /* fallback: no manufacturer */ }
                }

                // If ManufacturerId decode failed, try ManufacturerIdTxt if available
                if (info.Manufacturer is null)
                {
                    var mfgTxt = tpm["ManufacturerIdTxt"]?.ToString();
                    if (!string.IsNullOrEmpty(mfgTxt))
                        info.Manufacturer = mfgTxt;
                }

                // Ready state: call IsReady_InitialValue or check IsActivated + IsEnabled
                var readyForStorage = isEnabled == true && isActivated == true;

                // Try calling IsReady method for more accurate state
                try
                {
                    var outParams = tpm.InvokeMethod("IsReady", null, null);
                    if (outParams != null)
                    {
                        var isReady = outParams["IsReady"];
                        if (isReady != null)
                            readyForStorage = Convert.ToBoolean(isReady);
                    }
                }
                catch { /* method may not exist on all versions */ }

                info.ReadyState = info.Present
                    ? (readyForStorage ? "Ready" : "NotReady")
                    : "Unknown";

                break; // Only one TPM instance expected
            }
        }
        catch (ManagementException)
        {
            // WMI namespace may not be available (e.g. no TPM hardware)
            // Fall through to registry-only detection
        }
        catch (Exception ex)
        {
            info.Error = $"WMI query failed: {ex.Message}";
        }

        // If WMI didn't find a TPM and registry didn't either
        if (!info.Present && !info.Enabled)
        {
            info.Present = false;
            info.Version ??= "none";
            info.ReadyState ??= "Unknown";
        }

        // If registry said enabled but WMI didn't run, mark present
        if (info.Enabled && !info.Present)
        {
            info.Present = true;
            info.ReadyState ??= "Unknown";
        }

        return info;
    }

    private static bool? GetBool(ManagementObject obj, string propertyName)
    {
        try
        {
            var val = obj[propertyName];
            if (val == null) return null;
            return Convert.ToBoolean(val);
        }
        catch
        {
            return null;
        }
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
