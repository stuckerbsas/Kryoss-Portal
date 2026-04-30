using System.ServiceProcess;
using KryossAgent.Models;

namespace KryossAgent.Services;

public static class ServiceHealer
{
    public static List<AgentError> HealProtectedServices(List<string>? priorityServices)
    {
        var errors = new List<AgentError>();
        var healTargets = new HashSet<string>(RemediationExecutor.ProtectedServices, StringComparer.OrdinalIgnoreCase);

        if (priorityServices is { Count: > 0 })
            foreach (var s in priorityServices) healTargets.Add(s);

        healTargets.Remove("KryossAgent");

        foreach (var serviceName in healTargets)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                var status = sc.Status;
                if (status == ServiceControllerStatus.Running) continue;

                // Skip on-demand services (Manual/Disabled start type) — they stop by design
                var startType = sc.StartType;
                if (startType != ServiceStartMode.Automatic)
                {
                    AgentLogger.Log("HEAL", $"{serviceName} is {status} but StartType={startType} — skipping");
                    continue;
                }

                AgentLogger.Log("HEAL", $"{serviceName} is {status} — attempting start");

                var started = false;
                for (int retry = 0; retry < 3 && !started; retry++)
                {
                    try
                    {
                        if (retry > 0) Thread.Sleep(5000);
                        sc.Refresh();
                        if (sc.Status == ServiceControllerStatus.Running) { started = true; break; }
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                        started = true;
                    }
                    catch (Exception ex)
                    {
                        AgentLogger.Error("HEAL", $"{serviceName} start attempt {retry + 1}/3 failed: {ex.Message}");
                    }
                }

                if (started)
                {
                    AgentLogger.Log("HEAL", $"Auto-healed {serviceName} (was {status})");
                }
                else
                {
                    AgentLogger.Error("HEAL", $"Failed to heal {serviceName} (was {status})");
                    errors.Add(new AgentError
                    {
                        Phase = "service_heal",
                        Message = $"Failed to heal {serviceName} (was {status})",
                        Timestamp = DateTime.UtcNow,
                        Target = serviceName,
                        IsTimeout = false,
                    });
                }
            }
            catch (InvalidOperationException)
            {
                // Service doesn't exist on this machine
            }
            catch (Exception ex)
            {
                AgentLogger.Error("HEAL", $"Error checking {serviceName}: {ex.Message}");
            }
        }

        return errors;
    }
}
