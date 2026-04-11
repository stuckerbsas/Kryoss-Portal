using KryossAgent.Models;
using Microsoft.Win32;

namespace KryossAgent.Engines;

/// <summary>
/// Reads Windows Firewall profile settings via registry.
/// Avoids COM interop (INetFwPolicy2) for AOT compatibility.
/// Registry path: HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy
/// </summary>
public class FirewallEngine : ICheckEngine
{
    public string Type => "firewall";

    private const string FwBasePath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";

    private static readonly Dictionary<string, string> ProfilePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Domain"] = $@"{FwBasePath}\DomainProfile",
        ["Private"] = $@"{FwBasePath}\StandardProfile",
        ["Public"] = $@"{FwBasePath}\PublicProfile"
    };

    // Map property names to registry value names
    private static readonly Dictionary<string, string> PropertyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enabled"] = "EnableFirewall",
        ["EnableFirewall"] = "EnableFirewall",
        ["DefaultInboundAction"] = "DefaultInboundAction",
        ["DefaultOutboundAction"] = "DefaultOutboundAction",
        ["DisableNotifications"] = "DisableNotifications",
        ["LogFilePath"] = "LogFilePath",
        ["LogFileSize"] = "LogFileSize",
        ["LogDroppedPackets"] = "LogDroppedPackets",
        ["LogSuccessfulConnections"] = "LogSuccessfulConnections"
    };

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        foreach (var control in controls)
        {
            results.Add(ReadFirewallProperty(control));
        }

        return results;
    }

    private static CheckResult ReadFirewallProperty(ControlDef control)
    {
        var result = new CheckResult { Id = control.Id };

        try
        {
            var profile = control.Profile ?? "Domain";
            var property = control.Property ?? "Enabled";

            if (!ProfilePaths.TryGetValue(profile, out var registryPath))
            {
                result.Value = $"ERROR: Unknown profile '{profile}'";
                return result;
            }

            // Map the property name to the actual registry value name
            var valueName = PropertyMap.TryGetValue(property, out var mapped) ? mapped : property;

            // Logging properties are under a sub-key
            var isLogProp = property.StartsWith("Log", StringComparison.OrdinalIgnoreCase);
            var fullPath = isLogProp ? $@"{registryPath}\Logging" : registryPath;

            using var key = Registry.LocalMachine.OpenSubKey(fullPath);
            if (key is null)
            {
                result.Exists = false;
                return result;
            }

            var value = key.GetValue(valueName);
            if (value is null)
            {
                result.Exists = false;
                return result;
            }

            result.Exists = true;

            // Convert numeric values to friendly names for common properties
            if (valueName == "EnableFirewall")
                result.Value = value is int i && i == 1 ? "True" : "False";
            else if (valueName is "DefaultInboundAction" or "DefaultOutboundAction")
                result.Value = value is int i2 && i2 == 1 ? "Block" : "Allow";
            else
                result.Value = value.ToString();
        }
        catch (Exception ex)
        {
            result.Value = $"ERROR: {ex.Message}";
        }

        return result;
    }
}
