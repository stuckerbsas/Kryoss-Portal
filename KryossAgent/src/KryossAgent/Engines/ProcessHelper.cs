using System.Diagnostics;
using System.Text;

namespace KryossAgent.Engines;

/// <summary>
/// Deadlock-free synchronous process execution with hard timeout.
///
/// Why this exists:
///   <see cref="Process.StandardOutput"/>.ReadToEnd() followed by
///   <see cref="Process.StandardError"/>.ReadToEnd() is the classic
///   .NET pipe deadlock: if the child writes &gt; ~64 KB to stderr
///   before the parent starts reading it, the child blocks writing
///   to stderr while the parent blocks reading stdout. Both sides
///   wait forever and the timeout on WaitForExit is never honored.
///
/// This helper:
///   - drains BOTH streams in parallel via BeginOutputReadLine / BeginErrorReadLine
///   - caps captured output so we never leak unbounded memory
///   - enforces a hard deadline and on timeout calls Kill(entireProcessTree:true)
///   - returns a small value tuple so engines don't need to share state
///
/// AOT-safe: no reflection, no dynamic code, uses only System.Diagnostics.
/// </summary>
internal static class ProcessHelper
{
    public readonly record struct RunResult(
        int ExitCode,
        string Stdout,
        string Stderr,
        bool TimedOut,
        bool Started);

    /// <summary>
    /// Run a process synchronously, capturing stdout/stderr without deadlock.
    /// </summary>
    /// <param name="exePath">Full path to the executable (caller resolves + validates).</param>
    /// <param name="arguments">Command-line arguments.</param>
    /// <param name="timeoutSeconds">Hard deadline. On timeout the process tree is killed.</param>
    /// <param name="stdoutCapBytes">Maximum stdout characters captured. Default 4096.</param>
    /// <param name="stderrCapBytes">Maximum stderr characters captured. Default 1024.</param>
    public static RunResult RunCaptured(
        string exePath,
        string arguments,
        int timeoutSeconds,
        int stdoutCapBytes = 4096,
        int stderrCapBytes = 1024)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        var stdoutBuf = new StringBuilder(Math.Min(stdoutCapBytes, 4096));
        var stderrBuf = new StringBuilder(Math.Min(stderrCapBytes, 1024));
        var stdoutDone = new ManualResetEventSlim(false);
        var stderrDone = new ManualResetEventSlim(false);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { stdoutDone.Set(); return; }
            if (stdoutBuf.Length < stdoutCapBytes) stdoutBuf.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { stderrDone.Set(); return; }
            if (stderrBuf.Length < stderrCapBytes) stderrBuf.AppendLine(e.Data);
        };

        bool started;
        try
        {
            started = process.Start();
        }
        catch (Exception ex)
        {
            return new RunResult(-1, string.Empty, $"Failed to start: {ex.Message}", false, false);
        }

        if (!started)
            return new RunResult(-1, string.Empty, "Failed to start process", false, false);

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timeoutMs = Math.Max(1, timeoutSeconds) * 1000;
        var exited = process.WaitForExit(timeoutMs);

        if (!exited)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            try { process.WaitForExit(2000); } catch { /* best effort */ }

            return new RunResult(
                ExitCode: -2,
                Stdout: stdoutBuf.ToString(),
                Stderr: $"Command timed out after {timeoutSeconds}s and was killed",
                TimedOut: true,
                Started: true);
        }

        // Drain trailing async reads with a bounded wait. Never block indefinitely.
        stdoutDone.Wait(500);
        stderrDone.Wait(500);

        return new RunResult(
            ExitCode: process.ExitCode,
            Stdout: stdoutBuf.ToString(),
            Stderr: stderrBuf.ToString(),
            TimedOut: false,
            Started: true);
    }
}
