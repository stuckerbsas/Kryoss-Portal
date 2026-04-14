using System.Diagnostics;
using KryossAgent.Config;

namespace KryossAgent.Services;

/// <summary>
/// Result of scanning a single network target (discovery + port scan).
/// </summary>
public record ScanResult(
    string Name,
    string Address,
    string Status,  // "Scanned" or "Unreachable"
    string? Error,
    int DurationMs,
    List<PortScanner.PortResult>? OpenPorts = null);

/// <summary>
/// Orchestrates network discovery, port scanning, and AD hygiene reporting.
/// v1.3.0: Remote deployment removed — discovery + ports + hygiene only.
/// </summary>
public static class NetworkScanner
{
    private static readonly object ConsoleLock = new();

    public static async Task<int> RunAsync(string[] args, bool silent)
    {
        // ── Parse arguments ──
        int threads = ParseIntArg(args, "--threads") ?? 10;

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

        // ── Port scan each target in parallel ──
        var results = new ScanResult[targets.Count];
        int completed = 0;
        var semaphore = new SemaphoreSlim(threads, threads);

        var tasks = targets.Select((target, index) => Task.Run(async () =>
        {
            await semaphore.WaitAsync();
            try
            {
                var result = await ScanTarget(target);
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
        await UploadHygieneReport();

        // ── Upload port scan results to API ──
        await UploadPortResults(results);

        // Return non-zero if any targets were unreachable
        bool anyFailed = results.Any(r => r.Status == "Unreachable");
        return anyFailed ? 2 : 0;
    }

    /// <summary>
    /// Ping + port scan a single target. No remote deployment.
    /// </summary>
    private static async Task<ScanResult> ScanTarget(TargetDiscovery.ScanTarget target)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // ── Quick ping to skip offline machines fast ──
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(target.Address, 1500);
                if (reply.Status != System.Net.NetworkInformation.IPStatus.Success)
                {
                    sw.Stop();
                    return new ScanResult(target.Hostname, target.Address,
                        "Unreachable", "Offline (ping failed)", (int)sw.ElapsedMilliseconds);
                }
            }
            catch
            {
                sw.Stop();
                return new ScanResult(target.Hostname, target.Address,
                    "Unreachable", "Offline (ping failed)", (int)sw.ElapsedMilliseconds);
            }

            // ── Port scan ──
            List<PortScanner.PortResult>? openPorts = null;
            try
            {
                openPorts = await PortScanner.ScanTcpAsync(target.Address, concurrency: 100, timeoutMs: 500);
            }
            catch { /* non-critical */ }

            sw.Stop();
            return new ScanResult(target.Hostname, target.Address,
                "Scanned", null, (int)sw.ElapsedMilliseconds, openPorts);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ScanResult(target.Hostname, target.Address,
                "Unreachable", ex.Message, (int)sw.ElapsedMilliseconds);
        }
    }

    private static void PrintProgress(int seq, int total, ScanResult result, bool silent)
    {
        if (silent) return;

        var duration = result.DurationMs / 1000;
        var portCount = result.OpenPorts?.Count ?? 0;
        var detail = result.Status switch
        {
            "Scanned" => $"{portCount} open port(s)",
            _ => result.Error ?? result.Status,
        };

        lock (ConsoleLock)
        {
            var color = result.Status switch
            {
                "Scanned" => ConsoleColor.Green,
                _ => ConsoleColor.Red,
            };

            Console.ForegroundColor = color;
            Console.WriteLine($"[{seq}/{total}]  {result.Name,-20} {result.Status,-14} {detail} -- {duration}s");
            Console.ResetColor();
        }
    }

    private static void PrintSummary(ScanResult[] results, bool silent, TimeSpan elapsed)
    {
        int scanned = results.Count(r => r.Status == "Scanned");
        int unreachable = results.Count(r => r.Status == "Unreachable");

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ══════════════════════════════════════════");
        Console.WriteLine("    Kryoss Network Scan Complete");
        Console.WriteLine("  ══════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine($"    Targets:       {results.Length}");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"    Scanned:       {scanned}");
        Console.ResetColor();
        if (unreachable > 0) Console.WriteLine($"    Unreachable:   {unreachable}");
        Console.WriteLine();

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

    private static async Task UploadHygieneReport()
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
        var machinesWithPorts = results
            .Where(r => r.OpenPorts is { Count: > 0 })
            .ToList();

        if (machinesWithPorts.Count == 0) return;

        try
        {
            var config = Config.AgentConfig.Load();
            if (!config.IsEnrolled) return;

            using var client = new ApiClient(config);
            int totalUploaded = 0;

            foreach (var machine in machinesWithPorts)
            {
                try
                {
                    await client.SubmitPortResultsAsync(new
                    {
                        machineHostname = machine.Name,
                        ports = machine.OpenPorts!.Select(p => new
                        {
                            port = p.Port,
                            protocol = p.Protocol,
                            status = p.Status,
                            service = p.Service,
                            risk = p.Risk
                        }).ToList()
                    });
                    totalUploaded += machine.OpenPorts!.Count;
                }
                catch { /* skip individual machine failures */ }
            }

            Console.WriteLine($"  Port Scan: {totalUploaded} open ports across {machinesWithPorts.Count} machines uploaded to portal");
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
