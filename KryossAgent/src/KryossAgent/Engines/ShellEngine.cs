using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Executes arbitrary allowlisted commands (bcdedit, gpresult, wbadmin, etc.)
/// and captures stdout/stderr. Used for checks that don't fit other engines.
/// Security: only executables from System32/SysWOW64/Windows are allowed.
/// Timeouts: honored via <see cref="ProcessHelper"/>, with hard process-tree kill.
/// </summary>
public class ShellEngine : ICheckEngine
{
    public string Type => "command";

    /// <summary>Set to true to enable per-command logging (--verbose).</summary>
    public static bool Verbose { get; set; }

    // Allowlisted executable directories (prevent arbitrary code execution).
    private static readonly string[] AllowedPaths =
    [
        @"C:\Windows\System32",
        @"C:\Windows\SysWOW64",
        @"C:\Windows"
    ];

    // ── C-3: Strict executable allowlist ──
    // Only these specific executables are permitted. This blocks LOLBins like
    // mshta.exe, regsvr32.exe, rundll32.exe, wscript.exe, cscript.exe,
    // bitsadmin.exe, msiexec.exe, etc. that could enable RCE via a
    // compromised API server or MitM attack.
    private static readonly HashSet<string> AllowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        // Audit/policy tools
        "auditpol.exe", "secedit.exe", "gpresult.exe",
        // Network
        "netsh.exe", "ipconfig.exe", "nslookup.exe", "arp.exe",
        // System info
        "systeminfo.exe", "wmic.exe", "hostname.exe", "whoami.exe",
        "bcdedit.exe", "manage-bde.exe", "tpmtool.exe",
        // Disk/file
        "fsutil.exe", "cipher.exe", "icacls.exe",
        // Event/service
        "wevtutil.exe", "sc.exe", "schtasks.exe",
        // Domain
        "dsregcmd.exe", "nltest.exe", "net.exe",
        // Backup
        "wbadmin.exe", "vssadmin.exe",
        // Shell (restricted — arguments are controlled by check_json)
        "cmd.exe", "powershell.exe",
        // IIS (server)
        "appcmd.exe",
        // Certificate
        "certutil.exe",
        // DNS (server)
        "dnscmd.exe",
        // Utilities
        "reg.exe", "where.exe", "findstr.exe", "tasklist.exe",
        "citool.exe",
    };

    // Executables that hang on certain systems (DCs, servers) and can't be killed.
    // These are skipped entirely — the control gets an "info" result.
    private static readonly HashSet<string> SkipExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "vssadmin.exe",     // Hangs on DCs, Kill() doesn't work on VSS processes
        "vssadmin",
    };

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);
        var total = controls.Count;
        var cmdSw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < total; i++)
        {
            var control = controls[i];
            try
            {
                if (Verbose)
                {
                    Console.Error.WriteLine($"  [command] [{i+1}/{total}] {control.Id}: {control.Executable} {control.Arguments}");
                    Console.Error.Flush();
                }
                var before = cmdSw.ElapsedMilliseconds;
                var result = ExecuteCommand(control);
                var elapsed = cmdSw.ElapsedMilliseconds - before;
                if (Verbose && elapsed > 5000)
                    Console.Error.WriteLine($"  [command] SLOW: {control.Id} took {elapsed}ms");
                results.Add(result);
                if (Verbose)
                {
                    Console.Error.Write($"\r  [command] progress: {i+1}/{total}   ");
                    Console.Error.Flush();
                }
            }
            catch (Exception ex)
            {
                if (Verbose)
                {
                    Console.Error.WriteLine($"  [command] ENGINE ERROR on {control.Id}: {ex.GetType().Name}: {ex.Message}");
                    Console.Error.Flush();
                }
                results.Add(new CheckResult
                {
                    Id = control.Id,
                    ExitCode = -99,
                    Stderr = $"Engine error: {ex.Message}"
                });
            }
        }
        return results;
    }

    private static CheckResult ExecuteCommand(ControlDef control)
    {
        var result = new CheckResult { Id = control.Id };

        try
        {
            if (string.IsNullOrEmpty(control.Executable))
            {
                result.ExitCode = -1;
                result.Stderr = "No executable specified";
                return result;
            }

            // Skip executables known to hang and resist Kill()
            var exeName = Path.GetFileName(control.Executable);
            if (SkipExecutables.Contains(exeName))
            {
                result.ExitCode = 0;
                result.Stdout = "Skipped (known to hang on some systems)";
                return result;
            }

            var execPath = ResolveExecutable(control.Executable);
            if (execPath is null)
            {
                result.ExitCode = -1;
                result.Stderr = $"Executable '{control.Executable}' not found in allowed paths";
                return result;
            }

            var run = ProcessHelper.RunCaptured(
                exePath: execPath,
                arguments: control.Arguments ?? string.Empty,
                timeoutSeconds: control.TimeoutSeconds ?? 10,
                stdoutCapBytes: 4000,
                stderrCapBytes: 1000);

            result.ExitCode = run.ExitCode;
            result.Stdout = run.Stdout.Length > 4000 ? run.Stdout[..4000] : run.Stdout;
            result.Stderr = string.IsNullOrWhiteSpace(run.Stderr) ? null : run.Stderr;
        }
        catch (Exception ex)
        {
            result.ExitCode = -1;
            result.Stderr = $"ERROR: {ex.Message}";
            if (Verbose)
            {
                Console.Error.WriteLine($"  [command] EXCEPTION on {control.Id} ({control.Executable}): {ex.GetType().Name}: {ex.Message}");
                Console.Error.Flush();
            }
        }

        return result;
    }

    private static string? ResolveExecutable(string executable)
    {
        // If already a full path, verify it's in an allowed directory.
        if (Path.IsPathRooted(executable))
        {
            var dir = Path.GetDirectoryName(executable) ?? "";
            if (AllowedPaths.Any(p => dir.StartsWith(p, StringComparison.OrdinalIgnoreCase)) &&
                File.Exists(executable))
            {
                // C-3: Also verify the filename is in the strict allowlist
                var fileName = Path.GetFileName(executable);
                if (!AllowedExecutables.Contains(fileName))
                    return null; // Not in allowlist — blocked
                return executable;
            }
            return null;
        }

        // Otherwise search the allowlisted directories.
        // C-3: Check filename against strict allowlist first
        var exeName = executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? executable : executable + ".exe";
        if (!AllowedExecutables.Contains(Path.GetFileName(exeName)) &&
            !AllowedExecutables.Contains(Path.GetFileName(executable)))
            return null; // Not in allowlist — blocked

        foreach (var basePath in AllowedPaths)
        {
            var fullPath = Path.Combine(basePath, executable);
            if (File.Exists(fullPath)) return fullPath;
        }

        return null;
    }
}
