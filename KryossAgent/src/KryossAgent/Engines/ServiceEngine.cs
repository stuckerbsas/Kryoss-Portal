using System.ServiceProcess;
using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Reads Windows service status and start type.
/// Uses ServiceController (no WMI, no process spawning).
/// </summary>
public class ServiceEngine : ICheckEngine
{
    public string Type => "service";

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        foreach (var control in controls)
        {
            results.Add(ReadServiceInfo(control));
        }

        return results;
    }

    private static CheckResult ReadServiceInfo(ControlDef control)
    {
        var result = new CheckResult { Id = control.Id };

        try
        {
            if (string.IsNullOrEmpty(control.ServiceName))
            {
                result.Exists = false;
                return result;
            }

            using var sc = new ServiceController(control.ServiceName);

            result.Exists = true;
            result.Status = sc.Status.ToString(); // Running, Stopped, Paused, etc.
            result.StartType = sc.StartType.ToString(); // Automatic, Manual, Disabled, Boot, System
        }
        catch (InvalidOperationException)
        {
            // Service not found
            result.Exists = false;
        }
        catch (Exception ex)
        {
            result.Value = $"ERROR: {ex.Message}";
        }

        return result;
    }
}
