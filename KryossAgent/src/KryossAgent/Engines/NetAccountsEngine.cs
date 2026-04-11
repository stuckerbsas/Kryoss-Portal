using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Reads net accounts settings (password policy, lockout policy).
/// Uses 'net accounts' command output parsing.
/// Fields: MinimumPasswordLength, MaximumPasswordAge, MinimumPasswordAge,
///         PasswordHistoryLength, LockoutThreshold, LockoutDuration, LockoutWindow
/// </summary>
public class NetAccountsEngine : ICheckEngine
{
    public string Type => "netaccount";

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        // Run net accounts once
        var settings = GetNetAccountSettings();

        foreach (var control in controls)
        {
            var result = new CheckResult { Id = control.Id };

            if (settings is null)
            {
                result.Value = "ERROR: net accounts failed";
                results.Add(result);
                continue;
            }

            if (control.Field is not null &&
                settings.TryGetValue(control.Field, out var value))
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

    private static Dictionary<string, string>? GetNetAccountSettings()
    {
        try
        {
            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var exe = Path.Combine(systemRoot, "net.exe");

            var run = ProcessHelper.RunCaptured(
                exePath: exe,
                arguments: "accounts",
                timeoutSeconds: 5,
                stdoutCapBytes: 8192,
                stderrCapBytes: 1024);

            if (!run.Started || run.TimedOut) return null;

            var output = run.Stdout;
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Map "net accounts" output lines to our field names
            // Output format: "Force user logoff how long after time expires?:       Never"
            var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Minimum password length"] = "MinimumPasswordLength",
                ["Maximum password age (days)"] = "MaximumPasswordAge",
                ["Minimum password age (days)"] = "MinimumPasswordAge",
                ["Length of password history maintained"] = "PasswordHistoryLength",
                ["Lockout threshold"] = "LockoutThreshold",
                ["Lockout duration (minutes)"] = "LockoutDuration",
                ["Lockout observation window (minutes)"] = "LockoutWindow",
                ["Force user logoff how long after time expires?"] = "ForceLogoff"
            };

            foreach (var line in output.Split('\n'))
            {
                var colonIndex = line.LastIndexOf(':');
                if (colonIndex <= 0) continue;

                var label = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();

                foreach (var (pattern, fieldName) in fieldMap)
                {
                    if (label.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        settings[fieldName] = value;
                        break;
                    }
                }
            }

            return settings;
        }
        catch
        {
            return null;
        }
    }
}
