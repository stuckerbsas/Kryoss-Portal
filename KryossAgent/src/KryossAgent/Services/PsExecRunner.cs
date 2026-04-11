using System.Diagnostics;
using System.Reflection;

namespace KryossAgent.Services;

/// <summary>
/// Result of a PsExec remote execution.
/// </summary>
public sealed record PsExecResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>
    /// Finds the first line starting with "RESULT:" in stdout and returns its value,
    /// or null if no such line exists.
    /// </summary>
    public string? ParseResultLine()
    {
        if (string.IsNullOrEmpty(Stdout))
            return null;

        using var reader = new StringReader(Stdout);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("RESULT:", StringComparison.OrdinalIgnoreCase))
                return line.Substring("RESULT:".Length).Trim();
        }

        return null;
    }
}

/// <summary>
/// Extracts PsExec64.exe from embedded resources and runs it against remote targets.
/// Implements IDisposable to clean up the extracted temporary file.
/// </summary>
public sealed class PsExecRunner : IDisposable
{
    private readonly string _psExecPath;
    private bool _disposed;

    public PsExecRunner()
    {
        var pid = Environment.ProcessId;
        _psExecPath = Path.Combine(Path.GetTempPath(), $"KryossAgent_PsExec64_{pid}.exe");

        ExtractPsExec();
    }

    private void ExtractPsExec()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("PsExec64.exe")
            ?? throw new InvalidOperationException("Embedded resource 'PsExec64.exe' not found in assembly.");

        using var fs = new FileStream(_psExecPath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.CopyTo(fs);
    }

    /// <summary>
    /// Runs the KryossAgent binary on a remote machine via PsExec.
    /// </summary>
    /// <param name="target">Remote hostname or IP.</param>
    /// <param name="remoteExePath">Full path to the executable on the remote machine.</param>
    /// <param name="agentArgs">Arguments to pass to the remote executable (space-separated string).</param>
    /// <param name="username">Optional username for authentication.</param>
    /// <param name="password">Optional password for authentication.</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default 5 minutes).</param>
    /// <returns>A <see cref="PsExecResult"/> with exit code, stdout, and stderr.</returns>
    public async Task<PsExecResult> RunRemoteAsync(
        string target,
        string remoteExePath,
        string agentArgs,
        string? username = null,
        string? password = null,
        int timeoutMs = 180_000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var psi = new ProcessStartInfo
        {
            FileName = _psExecPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Build argument list using ArgumentList (safe, no string concatenation)
        psi.ArgumentList.Add($@"\\{target}");

        if (!string.IsNullOrEmpty(username))
        {
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(username);
        }

        if (!string.IsNullOrEmpty(password))
        {
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(password);
        }

        psi.ArgumentList.Add("-s");       // Run as SYSTEM
        psi.ArgumentList.Add("-h");       // Elevated
        psi.ArgumentList.Add("-n");       // Connection timeout
        psi.ArgumentList.Add("30");
        psi.ArgumentList.Add("-accepteula");

        psi.ArgumentList.Add(remoteExePath);

        // Split agentArgs by space and add each as a separate argument
        if (!string.IsNullOrWhiteSpace(agentArgs))
        {
            foreach (var arg in agentArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                psi.ArgumentList.Add(arg);
            }
        }

        var stdoutBuilder = new System.Text.StringBuilder();
        var stderrBuilder = new System.Text.StringBuilder();

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdoutTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdoutBuilder.AppendLine(e.Data);
            else
                stdoutTcs.TrySetResult(true);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderrBuilder.AppendLine(e.Data);
            else
                stderrTcs.TrySetResult(true);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new PsExecResult(
                ExitCode: -1,
                Stdout: "",
                Stderr: $"Failed to start PsExec process: {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = process.WaitForExit(timeoutMs);

        if (!exited)
        {
            // Kill the process tree on timeout
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort kill
            }

            return new PsExecResult(
                ExitCode: -1,
                Stdout: stdoutBuilder.ToString(),
                Stderr: stderrBuilder.ToString() + "\n[TIMEOUT] PsExec exceeded " + timeoutMs + "ms");
        }

        // Wait for async output streams to finish flushing
        var flushTimeout = Task.Delay(5000);
        await Task.WhenAny(Task.WhenAll(stdoutTcs.Task, stderrTcs.Task), flushTimeout);

        return new PsExecResult(
            ExitCode: process.ExitCode,
            Stdout: stdoutBuilder.ToString(),
            Stderr: stderrBuilder.ToString());
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (File.Exists(_psExecPath))
                File.Delete(_psExecPath);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
