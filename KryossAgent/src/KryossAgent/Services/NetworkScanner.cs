using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using KryossAgent.Config;

namespace KryossAgent.Services;

/// <summary>
/// Result of scanning a single remote target.
/// </summary>
public record ScanResult(
    string Name,
    string Address,
    string Status,
    string? Error,
    string? ResultLine,
    int DurationMs,
    List<PortScanner.PortResult>? OpenPorts = null);

/// <summary>
/// Orchestrates remote network scans: discovers targets, deploys the agent
/// binary via SMB, executes it remotely via PsExec, and collects results.
/// Replaces the legacy Invoke-KryossDeployment.ps1 PowerShell script.
/// </summary>
public static class NetworkScanner
{
    private static readonly object ConsoleLock = new();

    public static async Task<int> RunAsync(string[] args, bool silent)
    {
        // ── Parse arguments ──
        int threads = ParseIntArg(args, "--threads") ?? 10;
        bool reenroll = HasFlag(args, "--reenroll");
        bool wantCredential = HasFlag(args, "--credential");

        string? enrollCode = GetArg(args, "--code") ?? EmbeddedConfig.EnrollmentCode;
        if (string.IsNullOrEmpty(enrollCode))
        {
            Console.Error.WriteLine("[ERROR] No enrollment code available. Use --code <code> or patch the binary.");
            return 1;
        }

        // ── Credential prompt ──
        // Always prompt for credentials in interactive mode (admin share requires them).
        // In --silent mode, only prompt if --credential is explicitly passed.
        string? username = null;
        string? password = null;

        if (wantCredential || !silent)
        {
            Console.WriteLine("  Credentials required for remote admin access.");
            Console.Write("  Username (domain\\user): ");
            username = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(username))
            {
                Console.Write("  Password: ");
                password = ReadMaskedPassword();
                Console.WriteLine();
            }

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  [WARN] No credentials provided — will try current Windows identity.");
                Console.ResetColor();
                username = null;
                password = null;
            }
        }

        var totalSw = Stopwatch.StartNew();

        // ── Discover targets ──
        if (!silent) Console.WriteLine("  Discovering targets...");
        List<TargetDiscovery.ScanTarget> targets;
        try
        {
            targets = await TargetDiscovery.DiscoverAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Target discovery failed: {ex.Message}");
            return 1;
        }

        // Cross-check stale machines with ARP (add alive ones to scan)
        var staleRecovered = await TargetDiscovery.CrossCheckStaleWithArp();
        if (staleRecovered.Count > 0)
        {
            // Dedup against existing targets
            var existingNames = new HashSet<string>(targets.Select(t => t.Hostname), StringComparer.OrdinalIgnoreCase);
            foreach (var s in staleRecovered)
            {
                if (existingNames.Add(s.Hostname))
                    targets.Add(s);
            }
        }

        if (targets.Count == 0)
        {
            Console.Error.WriteLine("[ERROR] No scan targets found.");
            return 1;
        }

        if (!silent)
        {
            Console.WriteLine();
            Console.WriteLine($"  Found {targets.Count} target(s):");
            foreach (var t in targets)
                Console.WriteLine($"    {t.Hostname,-20} {t.Address,-16} ({t.Source})");
            Console.WriteLine();
        }

        // ── Build agent args for remote execution ──
        var remoteArgs = "--silent";
        if (reenroll) remoteArgs += " --reenroll";
        remoteArgs += $" --code {enrollCode}";

        // Pass through --api-url if specified
        var apiUrl = GetArg(args, "--api-url") ?? EmbeddedConfig.ApiUrl;
        if (!string.IsNullOrEmpty(apiUrl))
            remoteArgs += $" --api-url {apiUrl}";

        // ── Execute scans in parallel ──
        var selfPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine own executable path.");

        var results = new ScanResult[targets.Count];
        int completed = 0;
        var semaphore = new SemaphoreSlim(threads, threads);

        PsExecRunner psExec;
        try
        {
            psExec = new PsExecRunner();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Failed to initialize PsExec: {ex.Message}");
            return 1;
        }

        using var _ = psExec;

        var tasks = targets.Select((target, index) => Task.Run(async () =>
        {
            await semaphore.WaitAsync();
            try
            {
                var result = await ScanSingleTarget(
                    target, selfPath, remoteArgs,
                    username, password, psExec);

                results[index] = result;
                var seq = Interlocked.Increment(ref completed);
                PrintProgress(seq, targets.Count, result, silent);
            }
            finally
            {
                semaphore.Release();
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        totalSw.Stop();

        // ── Summary ──
        PrintSummary(results, silent, totalSw.Elapsed);

        // ── AD Hygiene Report ──
        if (!silent) PrintHygieneReport();

        // ── Upload hygiene to API ──
        await UploadHygieneReport(enrollCode!);

        // ── Upload port scan results to API ──
        await UploadPortResults(results);

        // Return non-zero if any targets failed or were unreachable
        bool anyFailed = results.Any(r =>
            r.Status is "ScanFailed" or "Unreachable" or "DeployFailed");
        return anyFailed ? 2 : 0;
    }

    private static async Task<ScanResult> ScanSingleTarget(
        TargetDiscovery.ScanTarget target,
        string selfPath,
        string remoteArgs,
        string? username,
        string? password,
        PsExecRunner psExec)
    {
        var sw = Stopwatch.StartNew();
        string remotePath = @"C:\Windows\Temp\KryossAgent.exe";
        string uncShare = $@"\\{target.Address}\C$";
        string uncPath = $@"\\{target.Address}\C$\Windows\Temp\KryossAgent.exe";
        bool netUseConnected = false;
        bool smbWasStarted = false;

        try
        {
            // ── Step 0: Quick ping to skip offline machines fast ──
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(target.Address, 1500);
                if (reply.Status != System.Net.NetworkInformation.IPStatus.Success)
                {
                    sw.Stop();
                    return new ScanResult(target.Hostname, target.Address,
                        "Unreachable", "Offline (ping failed)", null, (int)sw.ElapsedMilliseconds);
                }
            }
            catch
            {
                sw.Stop();
                return new ScanResult(target.Hostname, target.Address,
                    "Unreachable", "Offline (ping failed)", null, (int)sw.ElapsedMilliseconds);
            }

            // ── Step 1: Connect admin share (net use with creds, or direct) ──
            if (username is not null && password is not null)
            {
                // Disconnect stale mapping first
                await RunProcess("net", $"use {uncShare} /delete /y", 5_000);

                var netResult = await RunProcess("net",
                    $"use {uncShare} /user:{username} \"{password}\" /y", 15_000);
                if (netResult.ExitCode != 0)
                {
                    sw.Stop();
                    return new ScanResult(target.Hostname, target.Address,
                        "Unreachable", $"Access denied (net use exit {netResult.ExitCode})",
                        null, (int)sw.ElapsedMilliseconds);
                }
                netUseConnected = true;
            }

            // ── Step 2: Copy agent binary (with SMB service recovery) ──
            try
            {
                File.Copy(selfPath, uncPath, overwrite: true);
            }
            catch (Exception)
            {
                // SMB might be disabled — try starting it remotely via RPC (port 135)
                var scStart = await RunProcess("sc", $@"\\{target.Address} start LanmanServer", 10_000);
                if (scStart.ExitCode == 0)
                {
                    smbWasStarted = true;
                    await Task.Delay(3000); // Wait for service to start

                    // Re-map if we had credentials
                    if (username is not null && password is not null)
                    {
                        await RunProcess("net", $"use {uncShare} /delete /y", 5_000);
                        await RunProcess("net", $"use {uncShare} /user:{username} \"{password}\" /y", 15_000);
                    }

                    // Retry copy
                    try
                    {
                        File.Copy(selfPath, uncPath, overwrite: true);
                    }
                    catch (Exception retryEx)
                    {
                        sw.Stop();
                        return new ScanResult(target.Hostname, target.Address,
                            "DeployFailed", $"Copy failed after SMB start: {retryEx.Message}",
                            null, (int)sw.ElapsedMilliseconds);
                    }
                }
                else
                {
                    // Try WinRM as last resort to start SMB
                    var winrmResult = await RunProcess("powershell",
                        $"-NoProfile -Command \"Invoke-Command -ComputerName {target.Address} -ScriptBlock {{ Start-Service LanmanServer }}\"",
                        15_000);
                    if (winrmResult.ExitCode == 0)
                    {
                        smbWasStarted = true;
                        await Task.Delay(3000);

                        if (username is not null && password is not null)
                        {
                            await RunProcess("net", $"use {uncShare} /delete /y", 5_000);
                            await RunProcess("net", $"use {uncShare} /user:{username} \"{password}\" /y", 15_000);
                        }

                        try
                        {
                            File.Copy(selfPath, uncPath, overwrite: true);
                        }
                        catch (Exception winrmRetryEx)
                        {
                            sw.Stop();
                            return new ScanResult(target.Hostname, target.Address,
                                "DeployFailed", $"Copy failed after WinRM SMB start: {winrmRetryEx.Message}",
                                null, (int)sw.ElapsedMilliseconds);
                        }
                    }
                    else
                    {
                        sw.Stop();
                        return new ScanResult(target.Hostname, target.Address,
                            "Unreachable", "SMB disabled, RPC and WinRM failed to start it",
                            null, (int)sw.ElapsedMilliseconds);
                    }
                }
            }

            // ── Step 4: Run via PsExec ──
            var psResult = await psExec.RunRemoteAsync(
                target.Address, remotePath, remoteArgs, username, password);

            var resultLine = psResult.ParseResultLine();

            // Extract a short error from PsExec stderr (first meaningful line)
            var shortError = ExtractShortError(psResult.Stderr);

            string status;
            string? error = null;

            if (psResult.ExitCode == 0 && resultLine is not null && resultLine.Contains("OK", StringComparison.OrdinalIgnoreCase))
            {
                status = "OK";
            }
            else if (psResult.ExitCode == 2 || (resultLine is not null &&
                (resultLine.Contains("OFFLINE", StringComparison.OrdinalIgnoreCase) ||
                 resultLine.Contains("SKIP", StringComparison.OrdinalIgnoreCase))))
            {
                status = "Partial";
                error = resultLine ?? shortError;
            }
            else
            {
                status = "ScanFailed";
                error = resultLine ?? shortError;
                if (string.IsNullOrWhiteSpace(error))
                    error = $"PsExec exit {psResult.ExitCode}";
            }

            // Port scan (only for successful scans)
            List<PortScanner.PortResult>? openPorts = null;
            if (status == "OK" || status == "Partial")
            {
                try
                {
                    openPorts = await PortScanner.ScanTcpAsync(target.Address, concurrency: 100, timeoutMs: 500);
                }
                catch { /* non-critical */ }
            }

            sw.Stop();
            return new ScanResult(target.Hostname, target.Address,
                status, error, resultLine is not null ? $"RESULT: {resultLine}" : null,
                (int)sw.ElapsedMilliseconds, openPorts);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ScanResult(target.Hostname, target.Address,
                "ScanFailed", ex.Message, null, (int)sw.ElapsedMilliseconds);
        }
        finally
        {
            // ── Cleanup ──
            try { File.Delete(uncPath); } catch { /* best-effort */ }

            // Stop SMB service if we started it (leave the machine as we found it)
            if (smbWasStarted)
            {
                try
                {
                    await RunProcess("sc", $@"\\{target.Address} stop LanmanServer", 10_000);
                }
                catch { /* best-effort */ }
            }

            if (netUseConnected)
            {
                try
                {
                    await RunProcess("net", $"use {uncShare} /delete /y", 10_000);
                }
                catch { /* best-effort */ }
            }
        }
    }

    /// <summary>Extract the first meaningful error line from PsExec stderr.</summary>
    private static string ExtractShortError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return "";
        foreach (var line in stderr.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.StartsWith("PsExec", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("Copyright", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("Sysinternals", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("Connecting to", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("Starting PSEXESVC", StringComparison.OrdinalIgnoreCase)) continue;
            // Found a meaningful line
            return trimmed.Length > 80 ? trimmed[..80] + "..." : trimmed;
        }
        return stderr.Trim().Length > 80 ? stderr.Trim()[..80] + "..." : stderr.Trim();
    }

    private static async Task<bool> ProbeTcp445(string address)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(address, 445, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcess(
        string fileName, string arguments, int timeoutMs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();

        var exited = proc.WaitForExit(timeoutMs);
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return (-1, stdout, stderr + $"\n[TIMEOUT] exceeded {timeoutMs}ms");
        }

        return (proc.ExitCode, stdout, stderr);
    }

    private static void PrintProgress(int seq, int total, ScanResult result, bool silent)
    {
        if (silent) return;

        var duration = result.DurationMs / 1000;
        var detail = result.Status switch
        {
            "OK" => result.ResultLine ?? "OK",
            "Partial" => result.ResultLine ?? result.Error ?? "Partial",
            _ => result.Error ?? result.Status,
        };

        lock (ConsoleLock)
        {
            var color = result.Status switch
            {
                "OK" => ConsoleColor.Green,
                "Partial" => ConsoleColor.Yellow,
                _ => ConsoleColor.Red,
            };

            Console.ForegroundColor = color;
            Console.WriteLine($"[{seq}/{total}]  {result.Name,-20} {result.Status,-14} {detail} -- {duration}s");
            Console.ResetColor();
        }
    }

    private static void PrintSummary(ScanResult[] results, bool silent, TimeSpan elapsed)
    {
        int ok = results.Count(r => r.Status == "OK");
        int partial = results.Count(r => r.Status == "Partial");
        int failed = results.Count(r => r.Status is "ScanFailed" or "DeployFailed" or "AccessDenied");
        int unreachable = results.Count(r => r.Status == "Unreachable");

        // Compute fleet averages from OK results
        var okResults = results.Where(r => r.ResultLine is not null && r.Status == "OK").ToArray();
        double avgScore = 0;
        int fastestMs = 0, slowestMs = 0, avgMs = 0;
        if (okResults.Length > 0)
        {
            // Parse scores from RESULT: OK | HOST | 17.01% F | P:25 W:189 F:424
            var scores = new List<double>();
            foreach (var r in okResults)
            {
                var parts = r.ResultLine!.Split('|');
                if (parts.Length >= 3)
                {
                    var scorePart = parts[2].Trim().Split('%')[0].Trim();
                    if (double.TryParse(scorePart, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var s))
                        scores.Add(s);
                }
            }
            if (scores.Count > 0) avgScore = scores.Average();

            fastestMs = okResults.Min(r => r.DurationMs);
            slowestMs = okResults.Max(r => r.DurationMs);
            avgMs = (int)okResults.Average(r => r.DurationMs);
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ══════════════════════════════════════════");
        Console.WriteLine("    Kryoss Network Scan Complete");
        Console.WriteLine("  ══════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine($"    Targets:       {results.Length}");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"    Scanned OK:    {ok}");
        Console.ResetColor();
        if (partial > 0) { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine($"    Partial:       {partial}"); Console.ResetColor(); }
        if (failed > 0) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"    Failed:        {failed}"); Console.ResetColor(); }
        if (unreachable > 0) Console.WriteLine($"    Unreachable:   {unreachable}");
        Console.WriteLine();

        if (okResults.Length > 0)
        {
            Console.WriteLine($"    Avg Score:     {avgScore:F1}%");
            Console.WriteLine($"    Scan Time:     fastest {fastestMs / 1000}s / avg {avgMs / 1000}s / slowest {slowestMs / 1000}s");
        }

        // Port scan summary — show risky open ports across all targets
        var allRiskyPorts = results
            .Where(r => r.OpenPorts is not null)
            .SelectMany(r => r.OpenPorts!
                .Where(p => p.Risk is not null)
                .Select(p => new { Host = r.Name, p.Port, p.Service, p.Risk }))
            .ToList();

        if (allRiskyPorts.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n    Risky Open Ports ({allRiskyPorts.Count}):");
            Console.ResetColor();
            foreach (var rp in allRiskyPorts.OrderByDescending(p => p.Risk == "critical" ? 3 : p.Risk == "high" ? 2 : 1))
            {
                var riskColor = rp.Risk == "critical" ? ConsoleColor.Red : ConsoleColor.Yellow;
                Console.ForegroundColor = riskColor;
                Console.Write($"      [{rp.Risk?.ToUpperInvariant()}]");
                Console.ResetColor();
                Console.WriteLine($" {rp.Host,-20} :{rp.Port,-6} {rp.Service}");
            }
        }

        var totalMin = elapsed.TotalMinutes;
        Console.WriteLine($"    Total Time:    {(totalMin >= 1 ? $"{totalMin:F1} min" : $"{elapsed.TotalSeconds:F0}s")}");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ══════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static async Task UploadHygieneReport(string enrollCode)
    {
        var report = TargetDiscovery.LastHygieneReport;
        if (report is null) return;

        var allFindings = new List<object>();
        foreach (var m in report.StaleMachines)
            allFindings.Add(new { m.Name, objectType = "Computer", status = "Stale", m.DaysInactive, m.Detail });
        foreach (var m in report.DormantMachines)
            allFindings.Add(new { m.Name, objectType = "Computer", status = "Dormant", m.DaysInactive, m.Detail });
        foreach (var u in report.StaleUsers)
            allFindings.Add(new { u.Name, objectType = "User", status = u.Status, u.DaysInactive, u.Detail });
        foreach (var u in report.DormantUsers)
            allFindings.Add(new { u.Name, objectType = "User", status = "Dormant", u.DaysInactive, u.Detail });
        foreach (var u in report.DisabledUsers)
            allFindings.Add(new { u.Name, objectType = "User", status = "Disabled", daysInactive = 0, u.Detail });
        foreach (var u in report.NeverExpirePasswords)
            allFindings.Add(new { u.Name, objectType = "User", status = "PwdNeverExpires", daysInactive = 0, u.Detail });
        // Security findings
        foreach (var p in report.PrivilegedAccounts)
            allFindings.Add(new { p.Name, objectType = "Security", status = "PrivilegedAccount", daysInactive = 0, p.Detail });
        foreach (var k in report.KerberoastableAccounts)
            allFindings.Add(new { k.Name, objectType = "Security", status = "Kerberoastable", daysInactive = 0, k.Detail });
        foreach (var u in report.UnconstrainedDelegation)
            allFindings.Add(new { u.Name, objectType = "Security", status = "UnconstrainedDelegation", daysInactive = 0, u.Detail });
        foreach (var a in report.AdminCountResidual)
            allFindings.Add(new { a.Name, objectType = "Security", status = "AdminCountResidue", daysInactive = 0, a.Detail });
        foreach (var l in report.NoLaps)
            allFindings.Add(new { l.Name, objectType = "Security", status = "NoLAPS", daysInactive = 0, l.Detail });
        foreach (var d in report.DomainInfo)
            allFindings.Add(new { d.Name, objectType = "Config", status = d.Status, daysInactive = 0, d.Detail });

        if (allFindings.Count == 0) return;

        try
        {
            var config = Config.AgentConfig.Load();
            if (!config.IsEnrolled) return;

            // Calculate real totals (active + stale + dormant = all AD objects)
            var totalMachines = report.StaleMachines.Count + report.DormantMachines.Count
                + (TargetDiscovery.LastDiscoveredActiveCount);
            var totalUsers = report.StaleUsers.Count + report.DormantUsers.Count
                + report.DisabledUsers.Count + report.NeverExpirePasswords.Count
                + (TargetDiscovery.LastDiscoveredActiveUserCount);

            using var client = new ApiClient(config);
            await client.SubmitHygieneAsync(new
            {
                scannedBy = Environment.MachineName,
                totalMachines,
                totalUsers,
                findings = allFindings
            });
            Console.WriteLine($"  AD Hygiene: {allFindings.Count} findings uploaded to portal");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [WARN] Hygiene upload failed: {ex.Message}");
        }
    }

    private static async Task UploadPortResults(ScanResult[] results)
    {
        var portFindings = results
            .Where(r => r.OpenPorts is { Count: > 0 })
            .SelectMany(r => r.OpenPorts!.Select(p => new
            {
                host = r.Name,
                address = r.Address,
                port = p.Port,
                protocol = p.Protocol,
                status = p.Status,
                service = p.Service,
                risk = p.Risk
            }))
            .ToList();

        if (portFindings.Count == 0) return;

        try
        {
            var config = Config.AgentConfig.Load();
            if (!config.IsEnrolled) return;

            using var client = new ApiClient(config);
            await client.SubmitPortResultsAsync(new
            {
                scannedBy = Environment.MachineName,
                scannedAt = DateTime.UtcNow,
                totalPorts = portFindings.Count,
                findings = portFindings
            });
            Console.WriteLine($"  Port Scan: {portFindings.Count} open ports uploaded to portal");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [WARN] Port scan upload failed: {ex.Message}");
        }
    }

    private static void PrintHygieneReport()
    {
        var report = TargetDiscovery.LastHygieneReport;
        if (report is null) return;

        var totalFindings = report.StaleMachines.Count + report.DormantMachines.Count +
            report.StaleUsers.Count + report.DormantUsers.Count +
            report.DisabledUsers.Count + report.NeverExpirePasswords.Count +
            report.PrivilegedAccounts.Count + report.KerberoastableAccounts.Count +
            report.UnconstrainedDelegation.Count + report.AdminCountResidual.Count +
            report.NoLaps.Count + report.DomainInfo.Count;

        if (totalFindings == 0) return;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ══════════════════════════════════════════");
        Console.WriteLine("    AD Hygiene Report");
        Console.WriteLine("  ══════════════════════════════════════════");
        Console.ResetColor();

        if (report.DormantMachines.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n    Dormant Machines (>60 days, remove from AD):");
            Console.ResetColor();
            foreach (var m in report.DormantMachines.OrderByDescending(m => m.DaysInactive))
                Console.WriteLine($"      {m.Name,-22} {m.DaysInactive,4}d  {m.Detail}");
        }

        if (report.StaleMachines.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n    Stale Machines (30-60 days, verify status):");
            Console.ResetColor();
            foreach (var m in report.StaleMachines.OrderByDescending(m => m.DaysInactive))
                Console.WriteLine($"      {m.Name,-22} {m.DaysInactive,4}d  {m.Detail}");
        }

        if (report.DormantUsers.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n    Dormant Users (>60 days, remove from AD):");
            Console.ResetColor();
            foreach (var u in report.DormantUsers.OrderByDescending(u => u.DaysInactive))
                Console.WriteLine($"      {u.Name,-22} {u.DaysInactive,4}d  {u.Detail}");
        }

        if (report.StaleUsers.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n    Stale Users (30-60 days or old password):");
            Console.ResetColor();
            foreach (var u in report.StaleUsers.OrderByDescending(u => u.DaysInactive))
                Console.WriteLine($"      {u.Name,-22} {u.DaysInactive,4}d  {u.Detail}");
        }

        if (report.DisabledUsers.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n    Disabled Users (still in AD, consider removing):");
            Console.ResetColor();
            foreach (var u in report.DisabledUsers)
                Console.WriteLine($"      {u.Name,-22}       {u.Detail}");
        }

        if (report.NeverExpirePasswords.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n    Password Never Expires (security risk):");
            Console.ResetColor();
            foreach (var u in report.NeverExpirePasswords)
                Console.WriteLine($"      {u.Name,-22}       {u.Detail}");
        }

        // ── Security findings ──
        if (report.DomainInfo.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n    Domain Configuration:");
            Console.ResetColor();
            foreach (var d in report.DomainInfo)
                Console.WriteLine($"      {d.Detail}");
        }

        if (report.PrivilegedAccounts.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n    Privileged Accounts ({report.PrivilegedAccounts.Count}):");
            Console.ResetColor();
            foreach (var p in report.PrivilegedAccounts)
                Console.WriteLine($"      {p.Name,-22}       {p.Detail}");
        }

        if (report.KerberoastableAccounts.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n    Kerberoastable Accounts ({report.KerberoastableAccounts.Count}):");
            Console.ResetColor();
            foreach (var k in report.KerberoastableAccounts)
                Console.WriteLine($"      {k.Name,-22}       {k.Detail}");
        }

        if (report.UnconstrainedDelegation.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n    Unconstrained Delegation ({report.UnconstrainedDelegation.Count}):");
            Console.ResetColor();
            foreach (var u in report.UnconstrainedDelegation)
                Console.WriteLine($"      {u.Name,-22}       {u.Detail}");
        }

        if (report.AdminCountResidual.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n    AdminCount Residual ({report.AdminCountResidual.Count}):");
            Console.ResetColor();
            foreach (var a in report.AdminCountResidual)
                Console.WriteLine($"      {a.Name,-22}       {a.Detail}");
        }

        if (report.NoLaps.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n    No LAPS ({report.NoLaps.Count} machines):");
            Console.ResetColor();
            // Only show first 10 to keep output manageable
            foreach (var l in report.NoLaps.Take(10))
                Console.WriteLine($"      {l.Name,-22}       {l.Detail}");
            if (report.NoLaps.Count > 10)
                Console.WriteLine($"      ... and {report.NoLaps.Count - 10} more");
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  ══════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static string ReadMaskedPassword()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write('*');
            }
        }
        return password.ToString();
    }

    // ── CLI helpers (local to NetworkScanner) ──

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                var next = args[i + 1];
                if (!next.StartsWith("--"))
                    return next;
            }
        }
        return null;
    }

    private static bool HasFlag(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static int? ParseIntArg(string[] args, string name)
    {
        var val = GetArg(args, name);
        if (val is not null && int.TryParse(val, out var n))
            return n;
        return null;
    }
}
