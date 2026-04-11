using KryossAgent.Models;
using Microsoft.Win32;

namespace KryossAgent.Services;

/// <summary>
/// Enumerates installed software from registry uninstall keys.
/// Reads both 64-bit and 32-bit (Wow6432Node) paths.
/// </summary>
public static class SoftwareInventory
{
    private static readonly string[] UninstallPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public static List<SoftwareItem> Enumerate()
    {
        var software = new Dictionary<string, SoftwareItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in UninstallPaths)
        {
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(path);
                if (baseKey is null) continue;

                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = baseKey.OpenSubKey(subKeyName);
                        if (subKey is null) continue;

                        var name = subKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        // Skip updates, hotfixes, and system components
                        var systemComponent = subKey.GetValue("SystemComponent");
                        if (systemComponent is int sc && sc == 1) continue;

                        var releaseType = subKey.GetValue("ReleaseType") as string;
                        if (releaseType is "Update" or "Hotfix" or "Security Update") continue;

                        var parentKeyName = subKey.GetValue("ParentKeyName") as string;
                        if (!string.IsNullOrEmpty(parentKeyName)) continue;

                        var key = name.ToLowerInvariant();
                        if (software.ContainsKey(key)) continue;

                        software[key] = new SoftwareItem
                        {
                            Name = name,
                            Version = subKey.GetValue("DisplayVersion") as string,
                            Publisher = subKey.GetValue("Publisher") as string
                        };
                    }
                    catch { /* skip individual entries */ }
                }
            }
            catch { /* skip path */ }
        }

        return software.Values
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
