using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Reads audit policy settings via auditpol.exe /get /category:*.
/// Runs auditpol ONCE, parses CSV output, matches multiple controls.
/// ~1 second for all audit subcategories.
/// </summary>
public class AuditpolEngine : ICheckEngine
{
    public string Type => "auditpol";

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        // Get all audit policies in one shot
        var policies = GetAuditPolicies();

        foreach (var control in controls)
        {
            var result = new CheckResult { Id = control.Id };

            if (policies is null)
            {
                result.Value = "ERROR: auditpol failed";
                results.Add(result);
                continue;
            }

            if (control.Subcategory is not null &&
                policies.TryGetValue(control.Subcategory, out var value))
            {
                result.Exists = true;
                result.Value = value;
            }
            else
            {
                result.Exists = false;
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Run auditpol /get /category:* /r and parse CSV output.
    /// Returns dictionary of Subcategory → "Success", "Failure", "Success and Failure", "No Auditing".
    /// </summary>
    private static Dictionary<string, string>? GetAuditPolicies()
    {
        try
        {
            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var exe = Path.Combine(systemRoot, "auditpol.exe");

            var run = ProcessHelper.RunCaptured(
                exePath: exe,
                arguments: "/get /category:* /r",
                timeoutSeconds: 10,
                stdoutCapBytes: 32768,
                stderrCapBytes: 1024);

            if (!run.Started || run.TimedOut) return null;

            var output = run.Stdout;
            var policies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // CSV format: Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split(',');
                if (parts.Length < 5) continue;

                var subcategory = parts[2].Trim();
                var setting = parts[4].Trim();

                if (string.IsNullOrEmpty(subcategory) || subcategory == "Subcategory")
                    continue;

                policies[subcategory] = setting;
            }

            return policies;
        }
        catch
        {
            return null;
        }
    }
}
