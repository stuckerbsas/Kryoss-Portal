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
        bool skipPorts = args.Contains("--no-ports", StringComparer.OrdinalIgnoreCase);
        bool skipAd = args.Contains("--no-ad", StringComparer.OrdinalIgnoreCase);

        var totalSw = Stopwatch.StartNew();

        // ── Discover targets ──
        if (!silent) Console.WriteLine("  Discovering targets...");
        List<TargetDiscovery.ScanTarget> targets;
        try
        {
            targets = await TargetDiscovery.DiscoverAsync(args, skipAd: skipAd);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Target discovery failed: {ex.Message}");
            return 1;
        }

        // Cross-check stale machines with ARP (add alive ones to scan)
        if (!skipAd)
        {
            var staleRecovered = await TargetDiscovery.CrossCheckStaleWithArp();
            if (staleRecovered.Count > 0)
            {
                var existingNames = new HashSet<string>(targets.Select(t => t.Hostname), StringComparer.OrdinalIgnoreCase);
                foreach (var s in staleRecovered)
                {
                    if (existingNames.Add(s.Hostname))
                        targets.Add(s);
                }
            }
        }

        if (targets.Count == 0)
        {
            if (!silent) Console.Error.WriteLine("[WARN] No scan targets found.");
            return skipPorts ? 0 : 1;
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
                var result = await ScanTarget(target, skipPorts);
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
        if (!skipAd)
        {
            if (!silent) PrintHygieneReport();
            await UploadHygieneReport();
        }

        // ── Upload port scan results to API ──
        if (!skipPorts)
            await UploadPortResults(results);

        // ── SNMP auto-scan: probe all reachable targets with SNMPv2c "public" ──
        bool skipSnmp = args.Contains("--no-snmp", StringComparer.OrdinalIgnoreCase);
        if (!skipSnmp)
            await RunSnmpScan(results, silent);

        // Return non-zero if any targets were unreachable
        bool anyFailed = results.Any(r => r.Status == "Unreachable");
        return anyFailed ? 2 : 0;
    }

    /// <summary>
    /// Ping + port scan a single target. No remote deployment.
    /// </summary>
    private static async Task<ScanResult> ScanTarget(TargetDiscovery.ScanTarget target, bool skipPorts = false)
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
            if (!skipPorts)
            {
                try
                {
                    openPorts = await PortScanner.ScanTcpAsync(target.Address, concurrency: 100, timeoutMs: 500);
                }
                catch { /* non-critical */ }
            }

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
            Console.WriteLine($"\n    Risky Open Ports: {allRiskyPorts.Count}");
            Console.ResetColor();
            if (!silent)
            {
                foreach (var rp in allRiskyPorts.OrderByDescending(p => p.Risk == "critical" ? 3 : p.Risk == "high" ? 2 : 1))
                {
                    var riskColor = rp.Risk == "critical" ? ConsoleColor.Red : ConsoleColor.Yellow;
                    Console.ForegroundColor = riskColor;
                    Console.Write($"      [{rp.Risk?.ToUpperInvariant()}]");
                    Console.ResetColor();
                    Console.WriteLine($" {rp.Host,-20} :{rp.Port,-6} {rp.Service}");
                }
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

        var findings = new List<Models.HygieneFinding>();
        void Add(IEnumerable<TargetDiscovery.AdHygieneItem> items, string objType, string? statusOverride = null, int defaultDays = 0)
        {
            foreach (var i in items)
                findings.Add(new Models.HygieneFinding
                {
                    Name = i.Name, ObjectType = objType,
                    Status = statusOverride ?? i.Status,
                    DaysInactive = statusOverride is "Disabled" or "PwdNeverExpires" ? defaultDays : i.DaysInactive,
                    Detail = i.Detail
                });
        }

        Add(report.StaleMachines, "Computer", "Stale");
        Add(report.DormantMachines, "Computer", "Dormant");
        Add(report.StaleUsers, "User");
        Add(report.DormantUsers, "User", "Dormant");
        Add(report.DisabledUsers, "User", "Disabled");
        Add(report.NeverExpirePasswords, "User", "PwdNeverExpires");
        Add(report.PrivilegedAccounts, "Security", "PrivilegedAccount");
        Add(report.KerberoastableAccounts, "Security", "Kerberoastable");
        Add(report.UnconstrainedDelegation, "Security", "UnconstrainedDelegation");
        Add(report.AdminCountResidual, "Security", "AdminCountResidue");
        Add(report.NoLaps, "Security", "NoLAPS");
        Add(report.DomainInfo, "Config");

        if (findings.Count == 0) return;

        try
        {
            var config = Config.AgentConfig.Load();
            if (!config.IsEnrolled) return;

            var totalMachines = report.StaleMachines.Count + report.DormantMachines.Count
                + TargetDiscovery.LastDiscoveredActiveCount;
            var totalUsers = report.StaleUsers.Count + report.DormantUsers.Count
                + report.DisabledUsers.Count + report.NeverExpirePasswords.Count
                + TargetDiscovery.LastDiscoveredActiveUserCount;

            using var client = new ApiClient(config);
            await client.SubmitHygieneAsync(new Models.HygienePayload
            {
                ScannedBy = Environment.MachineName,
                TotalMachines = totalMachines,
                TotalUsers = totalUsers,
                Findings = findings
            });
            Console.WriteLine($"  AD Hygiene: {findings.Count} findings uploaded to portal");
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

            var bulkPayload = new Models.PortBulkPayload
            {
                Machines = machinesWithPorts.Select(m => new Models.PortPayload
                {
                    MachineHostname = m.Name,
                    Ports = m.OpenPorts!.Select(p => new Models.PortEntry
                    {
                        Port = p.Port,
                        Protocol = p.Protocol,
                        Status = p.Status,
                        Service = p.Service,
                        Risk = p.Risk,
                        Banner = p.Banner?.Length > 512 ? p.Banner[..512] : p.Banner,
                        ServiceName = p.ServiceName,
                        ServiceVersion = p.ServiceVersion
                    }).ToList()
                }).ToList()
            };

            var (saved, skipped) = await client.SubmitPortResultsBulkAsync(bulkPayload);
            var totalPorts = machinesWithPorts.Sum(m => m.OpenPorts!.Count);
            Console.WriteLine($"  Port Scan: {totalPorts} ports across {saved} machines uploaded ({skipped} skipped — not enrolled)");
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

    private static async Task RunSnmpScan(ScanResult[] results, bool silent)
    {
        var reachableIps = results
            .Where(r => r.Status == "Scanned")
            .Select(r => r.Address)
            .Distinct()
            .ToList();

        try
        {
            var creds = new Models.SnmpCredentials { Version = 2, Community = "public" };

            // Also try to fetch org-specific SNMP config from server
            try
            {
                var config = Config.AgentConfig.Load();
                if (config.IsEnrolled)
                {
                    using var apiClient = new ApiClient(config);
                    var serverCreds = await apiClient.GetSnmpCredentialsAsync();
                    if (serverCreds is not null) creds = serverCreds;
                }
            }
            catch { /* use defaults */ }

            // Subnet SNMP sweep — discovers switches, routers, APs, printers not in AD/ARP
            if (!silent)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n  SNMP: subnet sweep for network devices...");
                Console.ResetColor();
            }

            var subnetDevices = await SnmpScanner.DiscoverSubnetAsync(creds, verbose: !silent);

            // Merge subnet discoveries with reachable IPs (dedup)
            var allIps = new HashSet<string>(reachableIps);
            int added = 0;
            foreach (var ip in subnetDevices)
            {
                if (allIps.Add(ip)) added++;
            }

            if (!silent && added > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  SNMP: {added} new device(s) found via subnet sweep (not in AD/ARP)");
                Console.ResetColor();
            }

            if (allIps.Count == 0) return;

            if (!silent)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  SNMP: full scan on {allIps.Count} target(s)...");
                Console.ResetColor();
            }

            var snmpResult = await SnmpScanner.ScanAsync(creds, allIps.ToList(), verbose: !silent);

            if (!silent)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  SNMP: {snmpResult.Devices.Count} device(s) responded, {snmpResult.Unreachable.Count} silent");
                Console.ResetColor();

                foreach (var dev in snmpResult.Devices)
                {
                    var ifCount = dev.Interfaces.Count;
                    var lldpCount = dev.LldpNeighbors.Count;
                    var cdpCount = dev.CdpNeighbors.Count;
                    var neighborInfo = (lldpCount + cdpCount) > 0
                        ? $", {lldpCount} LLDP + {cdpCount} CDP neighbors"
                        : "";
                    Console.WriteLine($"    {dev.Ip,-16} {dev.SysName ?? "?",-20} {ifCount} ifaces{neighborInfo}");

                    foreach (var n in dev.LldpNeighbors)
                        Console.WriteLine($"      {n.LocalPort,-14} → {n.RemoteSysName ?? n.RemoteChassisId ?? "?"} ({n.RemotePortId})");
                    foreach (var n in dev.CdpNeighbors)
                        Console.WriteLine($"      {n.LocalPort,-14} → {n.RemoteDeviceId ?? "?"} ({n.RemotePortId}) [{n.RemoteIp}]");
                }
            }

            // Enrich SNMP devices with MAC from interfaces + OUI
            foreach (var dev in snmpResult.Devices)
            {
                if (dev.MacAddress == null)
                {
                    var ifMac = dev.Interfaces.FirstOrDefault(i => !string.IsNullOrEmpty(i.MacAddress))?.MacAddress;
                    if (ifMac != null) dev.MacAddress = ifMac;
                }
                if (dev.MacAddress != null)
                {
                    var oui = OuiLookup.Lookup(dev.MacAddress);
                    if (oui != null)
                    {
                        dev.Vendor = oui.Value.Vendor;
                        if (dev.DeviceType == "unknown")
                            dev.DeviceType = oui.Value.Category;
                    }
                }
            }

            // Merge ARP-only devices (seen on network but no SNMP response)
            var seenIps = new HashSet<string>(snmpResult.Devices.Select(d => d.Ip));
            var arpEntries = TargetDiscovery.ReadNativeArpTable();
            var arpDevices = new List<Models.SnmpDeviceResult>();
            foreach (var (ip, mac) in arpEntries)
            {
                if (!seenIps.Add(ip)) continue;
                if (mac is "ff-ff-ff-ff-ff-ff" or "00-00-00-00-00-00") continue;
                var normalMac = mac.Replace('-', ':').ToUpperInvariant();
                var oui = OuiLookup.Lookup(normalMac);
                arpDevices.Add(new Models.SnmpDeviceResult
                {
                    Ip = ip,
                    MacAddress = normalMac,
                    Vendor = oui?.Vendor,
                    DeviceType = oui?.Category ?? "unknown",
                });
            }

            // Reverse DNS for ARP-only + SNMP devices without sysName
            var needDns = arpDevices.Cast<Models.SnmpDeviceResult>()
                .Concat(snmpResult.Devices.Where(d => d.SysName == null)).ToList();
            if (needDns.Count > 0)
            {
                var dnsSem = new SemaphoreSlim(20);
                await Task.WhenAll(needDns.Select(async dev =>
                {
                    await dnsSem.WaitAsync();
                    try
                    {
                        var entry = await System.Net.Dns.GetHostEntryAsync(dev.Ip);
                        if (!string.IsNullOrEmpty(entry.HostName) && entry.HostName != dev.Ip)
                            dev.SysName = entry.HostName;
                    }
                    catch { }
                    finally { dnsSem.Release(); }
                }));
            }

            snmpResult.Devices.AddRange(arpDevices);

            // Upload to API — always log result (same as ports/hygiene)
            if (snmpResult.Devices.Count > 0)
            {
                try
                {
                    var config = Config.AgentConfig.Load();
                    if (config.IsEnrolled)
                    {
                        using var apiClient = new ApiClient(config);
                        await apiClient.SubmitSnmpResultsAsync(snmpResult);
                        var arpOnly = snmpResult.Devices.Count(d => d.SysName == null && d.MacAddress != null);
                        Console.WriteLine($"  SNMP: {snmpResult.Devices.Count} device(s) uploaded ({added} subnet, {arpOnly} ARP-only)");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  [WARN] SNMP upload failed: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"  SNMP: 0 devices responded (scanned {allIps.Count} IPs)");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [WARN] SNMP scan failed: {ex.Message}");
        }
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
