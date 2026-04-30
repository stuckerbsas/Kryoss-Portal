using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using KryossAgent.Models;

namespace KryossAgent.Services;

public static class ServiceInventory
{
    private static string? _lastHash;

    public static (List<ServiceInfo> services, bool changed) Collect()
    {
        var services = new List<ServiceInfo>();

        try
        {
            foreach (var sc in ServiceController.GetServices())
            {
                try
                {
                    services.Add(new ServiceInfo
                    {
                        Name = sc.ServiceName,
                        DisplayName = sc.DisplayName,
                        Status = sc.Status.ToString(),
                        StartupType = sc.StartType.ToString(),
                    });
                }
                catch { }
                finally { sc.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            AgentLogger.Error("SERVICES", $"Failed to enumerate services: {ex.Message}");
        }

        services.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var json = JsonSerializer.Serialize(services, KryossJsonContext.Default.ListServiceInfo);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var changed = hash != _lastHash;
        _lastHash = hash;

        return (services, changed);
    }
}
