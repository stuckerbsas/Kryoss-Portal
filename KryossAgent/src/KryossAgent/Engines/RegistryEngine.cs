using KryossAgent.Models;
using Microsoft.Win32;

namespace KryossAgent.Engines;

/// <summary>
/// Reads registry values. Handles HKLM and HKU (per-user) hives.
/// Fastest engine — pure registry reads, no process spawning.
/// </summary>
public class RegistryEngine : ICheckEngine
{
    public string Type => "registry";

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        foreach (var control in controls)
        {
            results.Add(ReadRegistryValue(control));
        }

        return results;
    }

    private static CheckResult ReadRegistryValue(ControlDef control)
    {
        var result = new CheckResult { Id = control.Id };

        try
        {
            var hive = control.Hive?.ToUpperInvariant() switch
            {
                "HKLM" => Registry.LocalMachine,
                "HKCU" => Registry.CurrentUser,
                "HKU" => Registry.Users,
                "HKCR" => Registry.ClassesRoot,
                _ => Registry.LocalMachine
            };

            // For HKU, enumerate user SIDs and check the first real user
            if (control.Hive?.ToUpperInvariant() == "HKU")
            {
                return ReadHkuValue(control);
            }

            using var key = hive.OpenSubKey(control.Path ?? "");
            if (key is null)
            {
                result.Exists = false;
                return result;
            }

            var value = key.GetValue(control.ValueName);
            if (value is null)
            {
                result.Exists = false;
                return result;
            }

            result.Exists = true;
            result.Value = value;
            result.RegType = key.GetValueKind(control.ValueName!) switch
            {
                RegistryValueKind.DWord => "REG_DWORD",
                RegistryValueKind.QWord => "REG_QWORD",
                RegistryValueKind.String => "REG_SZ",
                RegistryValueKind.ExpandString => "REG_EXPAND_SZ",
                RegistryValueKind.MultiString => "REG_MULTI_SZ",
                RegistryValueKind.Binary => "REG_BINARY",
                _ => "REG_UNKNOWN"
            };
        }
        catch (Exception ex)
        {
            result.Exists = null;
            result.Value = $"ERROR: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Read from HKU\{SID}\... for the first real user profile.
    /// Skips .DEFAULT, S-1-5-18 (SYSTEM), S-1-5-19 (LOCAL SERVICE), S-1-5-20 (NETWORK SERVICE).
    /// </summary>
    private static CheckResult ReadHkuValue(ControlDef control)
    {
        var result = new CheckResult { Id = control.Id };

        try
        {
            using var usersKey = Registry.Users;
            foreach (var sid in usersKey.GetSubKeyNames())
            {
                // Skip system accounts and _Classes hives
                if (sid.StartsWith(".") || sid.EndsWith("_Classes") ||
                    sid == "S-1-5-18" || sid == "S-1-5-19" || sid == "S-1-5-20" ||
                    !sid.StartsWith("S-1-5-21-"))
                    continue;

                var fullPath = $@"{sid}\{control.Path}";
                using var key = usersKey.OpenSubKey(fullPath);
                if (key is null) continue;

                var value = key.GetValue(control.ValueName);
                if (value is null) continue;

                result.Exists = true;
                result.Value = value;
                result.RegType = key.GetValueKind(control.ValueName!) switch
                {
                    RegistryValueKind.DWord => "REG_DWORD",
                    RegistryValueKind.QWord => "REG_QWORD",
                    RegistryValueKind.String => "REG_SZ",
                    _ => "REG_OTHER"
                };
                return result; // Return first real user's value
            }

            result.Exists = false;
        }
        catch (Exception ex)
        {
            result.Value = $"ERROR: {ex.Message}";
        }

        return result;
    }
}
