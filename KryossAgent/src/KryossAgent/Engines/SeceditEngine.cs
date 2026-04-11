using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Reads security policy settings via secedit.exe export.
/// Runs secedit ONCE, parses the INF file, matches multiple controls.
/// ~2 seconds for a single export that covers all 26 secedit checks.
/// </summary>
public class SeceditEngine : ICheckEngine
{
    public string Type => "secedit";

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        // Export security policy to temp file
        var settings = ExportSecurityPolicy();

        foreach (var control in controls)
        {
            var result = new CheckResult { Id = control.Id };

            if (settings is null)
            {
                result.Value = "ERROR: secedit export failed";
                results.Add(result);
                continue;
            }

            if (control.SettingName is not null &&
                settings.TryGetValue(control.SettingName, out var value))
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
    /// Run secedit /export /cfg tempfile and parse the INF result.
    /// Returns dictionary of SettingName → Value.
    /// </summary>
    private static Dictionary<string, string>? ExportSecurityPolicy()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"kryoss_secedit_{Guid.NewGuid():N}.inf");

        try
        {
            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var exe = Path.Combine(systemRoot, "secedit.exe");

            var run = ProcessHelper.RunCaptured(
                exePath: exe,
                arguments: $"/export /cfg \"{tempFile}\"",
                timeoutSeconds: 10,
                stdoutCapBytes: 4096,
                stderrCapBytes: 1024);

            if (!run.Started || run.TimedOut) return null;
            if (!File.Exists(tempFile)) return null;

            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadLines(tempFile))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('[') || trimmed.StartsWith(';'))
                    continue;

                var eqIndex = trimmed.IndexOf('=');
                if (eqIndex <= 0) continue;

                var key = trimmed[..eqIndex].Trim();
                var value = trimmed[(eqIndex + 1)..].Trim();

                // Clean up common secedit value formats
                // e.g., "MinimumPasswordLength = 14" or "PasswordComplexity = 1"
                settings[key] = value;
            }

            return settings;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }
}
