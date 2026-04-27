using System.Diagnostics;
using KryossAgent.Config;
using KryossAgent.Engines;
using KryossAgent.Models;
using KryossAgent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ── Kryoss Security Agent ──
var _agentVer = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

// Global exception handler — NEVER let the agent die silently
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"[FATAL] Unhandled exception: {e.ExceptionObject}");
    Console.ResetColor();
    Console.WriteLine($"RESULT: ERROR | {Environment.MachineName} | FATAL: {e.ExceptionObject}");
};

try
{

// ── Help ──
if (args.Any(a => a is "/?" or "-?" or "--help" or "-h" or "/h"))
{
    PrintHelp();
    Environment.Exit(0);
    return;
}

// ── Service management ──
if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
{
    ServiceInstaller.Install();
    Environment.Exit(0);
    return;
}
if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
{
    ServiceInstaller.Uninstall();
    Environment.Exit(0);
    return;
}
if (args.Contains("--service", StringComparer.OrdinalIgnoreCase))
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddHostedService<ServiceWorker>();
    builder.Services.AddWindowsService(o => o.ServiceName = "KryossAgent");
    var host = builder.Build();
    await host.RunAsync();
    return;
}

// ── Validate arguments ──
var knownFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "--silent", "--verbose", "--alone", "--scan", "--reenroll",
    "--no-network", "--no-ports", "--no-ad", "--no-threats", "--no-snmp",
    "--code", "--api-url", "--threads", "--targets", "--targets-file",
    "--discover-ad", "--discover-arp", "--discover-subnet",
    "--offline", "--share", "--collect",
    "--install", "--uninstall", "--service", "--trial", "--debug-acl", "--enroll-only",
};
foreach (var arg in args)
{
    if (arg.StartsWith("--") || arg.StartsWith("/") || arg.StartsWith("-"))
    {
        // Skip values that look like flags (e.g. --code XXXX)
        if (arg.StartsWith("--") && !knownFlags.Contains(arg))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Unknown argument: {arg}");
            Console.ResetColor();
            Console.WriteLine();
            PrintHelp();
            Environment.Exit(1);
            return;
        }
    }
}

var sw = Stopwatch.StartNew();
var silent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase);
var verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);
if (verbose) Environment.SetEnvironmentVariable("KRYOSS_VERBOSE", "1");
var aloneMode = args.Contains("--alone", StringComparer.OrdinalIgnoreCase);
var scanMode = args.Contains("--scan", StringComparer.OrdinalIgnoreCase);
var noNetwork = aloneMode || args.Contains("--no-network", StringComparer.OrdinalIgnoreCase);
var noPorts = args.Contains("--no-ports", StringComparer.OrdinalIgnoreCase);
var noAd = args.Contains("--no-ad", StringComparer.OrdinalIgnoreCase);
var noThreats = args.Contains("--no-threats", StringComparer.OrdinalIgnoreCase);
var trialMode = args.Contains("--trial", StringComparer.OrdinalIgnoreCase);
var debugAcl = args.Contains("--debug-acl", StringComparer.OrdinalIgnoreCase);
if (trialMode) aloneMode = true; // trial = local scan only, no network

// ── Banner (always show first, even in --scan mode) ──
if (!silent)
{
    Console.ForegroundColor = ConsoleColor.Green;
    if (EmbeddedConfig.MspName is not null && EmbeddedConfig.OrgName is not null)
    {
        var mspLine = $"  {EmbeddedConfig.MspName} — Security Assessment";
        var orgLine = $"  {EmbeddedConfig.OrgName}";
        var width = Math.Max(42, Math.Max(mspLine.Length, orgLine.Length) + 2);
        var border = new string('═', width);
        Console.WriteLine($"╔{border}╗");
        Console.WriteLine($"║{mspLine.PadRight(width)}║");
        Console.WriteLine($"║{orgLine.PadRight(width)}║");
        Console.WriteLine($"╚{border}╝");
    }
    else
    {
        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine($"║     Kryoss Security Agent v{_agentVer,-12}║");
        Console.WriteLine("║         TeamLogic IT Assessment          ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
    }
    Console.ResetColor();
    Console.WriteLine();
}

// ── A-13: Orchestrated scan scheduling ──
if (silent && !args.Contains("--code", StringComparer.OrdinalIgnoreCase))
{
    var persistedConfig = AgentConfig.Load();
    if (persistedConfig.IsEnrolled)
    {
        var lastRunFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Kryoss", "lastrun.txt");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (File.Exists(lastRunFile) && File.ReadAllText(lastRunFile).Trim() == today)
        {
            if (verbose) Console.Error.WriteLine($"[INFO] Already ran today ({today}). Exiting.");
            Environment.Exit(0);
        }

        try
        {
            using var scheduleClient = new ApiClient(persistedConfig);
            var schedule = await scheduleClient.GetScheduleAsync();
            if (schedule is not null && !schedule.RunNow)
            {
                var sleepMs = (int)(schedule.RunAt - DateTime.UtcNow).TotalMilliseconds;
                if (sleepMs > 0 && sleepMs <= 65 * 60 * 1000)
                {
                    if (verbose) Console.Error.WriteLine(
                        $"[INFO] Slot in {sleepMs / 1000}s (at {schedule.RunAt:HH:mm:ss} UTC). Sleeping...");
                    await Task.Delay(sleepMs);
                }
                else if (sleepMs > 65 * 60 * 1000)
                {
                    if (verbose) Console.Error.WriteLine(
                        $"[INFO] Slot too far ({sleepMs / 1000}s). Exiting — next hourly wake handles it.");
                    Environment.Exit(0);
                }
            }
            else if (verbose && schedule is not null)
            {
                Console.Error.WriteLine("[INFO] Server says runNow=true (catch-up).");
            }
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"[WARN] Schedule check failed, running immediately: {ex.Message}");
        }
    }
}

// ── Scan mode: delegate to NetworkScanner and exit ──
if (scanMode)
{
    try
    {
        var scanExitCode = await NetworkScanner.RunAsync(args, silent);
        Environment.Exit(scanExitCode);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[ERROR] Network scan failed: {ex.Message}");
        Console.WriteLine($"RESULT: ERROR | {Environment.MachineName} | Network scan: {ex.Message}");
        Environment.Exit(2);
    }
    return;
}

// ── Collect mode: upload offline payloads from shared folder to API ──
var collectPath = GetArg(args, "--collect");
if (collectPath is not null)
{
    await RunCollectMode(collectPath, args, silent, verbose);
    return;
}

// ── Parse CLI arguments ──
var cliCode = GetArg(args, "--code");
var cliApiUrl = GetArg(args, "--api-url");
var forceReenroll = args.Contains("--reenroll", StringComparer.OrdinalIgnoreCase);

// ── Patched binary: ALWAYS start clean ──
// A patched .exe is meant to be dropped on any machine and just work.
// Wipe any stale enrollment from a previous org/run so it re-enrolls
// with the embedded code every time.
if (EmbeddedConfig.IsPatched || forceReenroll)
{
    try
    {
        Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Kryoss\Agent", throwOnMissingSubKey: false);
    }
    catch { /* non-critical */ }
}

// ── Load config (will be empty after wipe, or existing if unpatched) ──
var config = AgentConfig.Load();

// ── Apply embedded/CLI overrides ──
if (EmbeddedConfig.ApiUrl is not null) config.ApiUrl = EmbeddedConfig.ApiUrl;
if (!string.IsNullOrEmpty(cliApiUrl)) config.ApiUrl = cliApiUrl;

// ── Resolve enrollment code: CLI > embedded > interactive ──
var enrollCode = cliCode ?? EmbeddedConfig.EnrollmentCode;

// ── Enrollment (first run) ──
if (!config.IsEnrolled)
{
    if (silent && string.IsNullOrEmpty(enrollCode))
    {
        Console.Error.WriteLine("[ERROR] Agent not enrolled. Provide --code <enrollment-code> or run interactively.");
        Environment.Exit(1);
        return;
    }

    string? code;
    if (!string.IsNullOrEmpty(enrollCode))
    {
        code = enrollCode;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  No configuration found. Entering enrollment mode.");
        Console.ResetColor();
        Console.WriteLine();

        Console.Write("  Enter enrollment code: ");
        code = Console.ReadLine()?.Trim();
    }

    if (string.IsNullOrEmpty(code))
    {
        Console.Error.WriteLine("[ERROR] Enrollment code is required.");
        Environment.Exit(1);
        return;
    }

    // API URL: use --api-url if provided, otherwise use embedded or compiled default.

    var hostname = Environment.MachineName;
    var platform = PlatformDetector.DetectPlatform();
    var earlyHw = PlatformDetector.DetectHardware();

    if (!silent) Console.WriteLine($"  Enrolling {hostname}...");

    try
    {
        using var enrollClient = new ApiClient(config);
        var enrollment = await enrollClient.EnrollAsync(code, hostname, platform, earlyHw.ProductType ?? 0);
        if (enrollment is null)
        {
            Console.Error.WriteLine("[ERROR] Enrollment returned null.");
            Environment.Exit(1);
            return;
        }

        config.AgentId = enrollment.AgentId;
        config.ApiKey = enrollment.ApiKey;
        config.ApiSecret = enrollment.ApiSecret;
        config.PublicKeyPem = enrollment.PublicKey;
        config.AssessmentId = enrollment.AssessmentId;
        config.AssessmentName = enrollment.AssessmentName;
        // Save per-machine auth credentials (v2.2+)
        config.MachineSecret = enrollment.MachineSecret;
        config.SessionKey = enrollment.SessionKey;
        config.SessionKeyExpiresAt = enrollment.SessionKeyExpiresAt;
        config.Save(debugAcl);

        if (silent)
        {
            Console.WriteLine($"Kryoss: Enrolled {hostname} — Assessment: {enrollment.AssessmentName} ({enrollment.AssessmentId})");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Enrolled successfully as {hostname}");
            Console.WriteLine($"  Assessment: {enrollment.AssessmentName ?? "Default"} ({enrollment.AssessmentId})");
            Console.ResetColor();
            Console.WriteLine();
        }

        // v1.5.1: Protocol Usage Audit — opt-in per-org via portal toggle.
        // Configures NTLM+SMB1 audit and resizes event logs on this machine.
        if (enrollment.ProtocolAuditEnabled)
        {
            if (!silent) Console.WriteLine("  Configuring protocol usage audit (NTLM + SMBv1)...");
            try
            {
                ProtocolAuditService.Configure(verbose);
                if (!silent && !verbose) Console.WriteLine("  Protocol audit configured.");
            }
            catch (Exception paEx)
            {
                if (!silent) Console.Error.WriteLine($"  [WARN] Protocol audit config failed: {paEx.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] Enrollment failed: {ex.Message}");
        Console.ResetColor();
        Console.WriteLine($"RESULT: ENROLL_FAILED | {Environment.MachineName} | {ex.Message}");
        Environment.Exit(1);
        return;
    }
}

// ── Enroll-only mode: exit after enrollment (used by NinjaOne deploy) ──
if (args.Contains("--enroll-only", StringComparer.OrdinalIgnoreCase))
{
    if (!silent) Console.WriteLine("  Enrollment complete (--enroll-only). Exiting.");
    Environment.Exit(0);
    return;
}

// ── Upload pending offline results ──
await UploadPendingResults(config, silent);

// ── Download controls ──
if (!config.AssessmentId.HasValue)
{
    Console.Error.WriteLine("[ERROR] No assessment ID configured.");
    Environment.Exit(1);
    return;
}

List<ControlDef> checks;
using var apiClient = new ApiClient(config);

try
{
    if (!silent) Console.WriteLine("  Downloading control definitions...");
    var controlsResponse = await apiClient.GetControlsAsync(config.AssessmentId.Value);
    checks = controlsResponse?.Checks ?? [];
    if (verbose) Console.WriteLine($"  {checks.Count} controls loaded (v{controlsResponse?.Version})");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ERROR] Cannot reach API: {ex.Message}");
    Console.WriteLine($"RESULT: ERROR | {Environment.MachineName} | Cannot reach API: {ex.Message}");
    Environment.Exit(2);
    return;
}

if (checks.Count == 0)
{
    var osName = PlatformDetector.DetectPlatform()?.Os ?? "Unknown OS";
    var reason = "No controls for this platform (may be unsupported OS)";
    Console.Error.WriteLine($"[WARN] {reason}");
    Console.WriteLine($"RESULT: SKIP | {Environment.MachineName} | {osName} | {reason}");
    Environment.Exit(2);
    return;
}

// ── Detect platform & inventory ──
if (!silent) Console.WriteLine("  Scanning local machine...");
if (!silent) Console.Write("    Collecting system info...");
var platformInfo = PlatformDetector.DetectPlatform();
var hardwareInfo = PlatformDetector.DetectHardware();
List<SoftwareItem> softwareList;
try
{
    softwareList = SoftwareInventory.Enumerate();
}
catch (Exception ex)
{
    if (verbose) Console.Error.WriteLine($"  [WARN] Software enumeration failed: {ex.Message}");
    softwareList = [];
}
if (!silent) Console.WriteLine(" done");

// ── Threat detection ──
List<ThreatFinding> threats;
if (noThreats)
{
    threats = [];
}
else
{
    if (!silent) Console.Write("    Scanning for threats...");
    try
    {
        threats = ThreatDetector.ScanAll();
    }
    catch
    {
        threats = [];
    }
    if (!silent) Console.WriteLine($" {threats.Count} found");
}

if (!silent) Console.Write("    Running security checks...");

var securityPolicyEngine = new SecurityPolicyEngine();
ICheckEngine[] engines =
[
    new RegistryEngine(),
    securityPolicyEngine,                        // Type="secedit" — P/Invoke (replaces SeceditEngine)
    new AuditpolEngine(),                        // P/Invoke AuditQuerySystemPolicy (no auditpol.exe)
    new FirewallEngine(),
    new ServiceEngine(),
    new NetAccountCompatEngine(securityPolicyEngine), // Type="netaccount" — delegates to SecurityPolicyEngine
    new NativeCommandEngine(),                   // Type="command" — WMI/registry (replaces ShellEngine, zero Process.Start)
    new EventLogEngine(),
    new CertStoreEngine(),
    new BitLockerEngine(),                       // WMI Win32_EncryptableVolume (no manage-bde.exe)
    new TpmEngine(),                             // WMI Win32_Tpm (no tpmtool.exe)
    new DcEngine()                               // Type="dc" — DirectoryServices/WMI/registry DC checks
];

var allResults = new List<CheckResult>();

// Group controls by engine type for batch execution
var controlsByType = checks
    .GroupBy(c => c.Type, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(g => g.Key, g => (IReadOnlyList<ControlDef>)g.ToList(), StringComparer.OrdinalIgnoreCase);

// Execute engines in parallel. Log start + finish of each engine immediately
// (flushing after each write) so that if one engine hangs we can see which.
var logLock = new object();
void LogLine(string msg)
{
    if (!verbose) return;
    lock (logLock)
    {
        Console.WriteLine(msg);
        Console.Out.Flush();
    }
}

var activeEngines = engines.Where(e => controlsByType.ContainsKey(e.Type)).ToArray();
var totalEngines = activeEngines.Length;
var completedEngines = 0;

var engineTasks = activeEngines
    .Select(engine => Task.Run(() =>
    {
        var controls = controlsByType[engine.Type];
        var engineSw = System.Diagnostics.Stopwatch.StartNew();
        LogLine($"  [{engine.Type}] starting ({controls.Count} checks)");

        try
        {
            var engineResults = engine.Execute(controls);
            engineSw.Stop();
            LogLine($"  [{engine.Type}] done in {engineSw.ElapsedMilliseconds} ms");

            // Progress update (non-verbose)
            var done = Interlocked.Increment(ref completedEngines);
            if (!silent && !verbose)
            {
                lock (logLock)
                {
                    Console.Write($"\r    Running security checks... {done}/{totalEngines} engines   ");
                    Console.Out.Flush();
                }
            }

            return (engine.Type, Results: engineResults, Count: controls.Count);
        }
        catch (Exception ex)
        {
            engineSw.Stop();
            LogLine($"  [{engine.Type}] FAILED after {engineSw.ElapsedMilliseconds} ms: {ex.Message}");
            Interlocked.Increment(ref completedEngines);
            return (engine.Type, Results: new List<CheckResult>(), Count: controls.Count);
        }
    }))
    .ToArray();

var engineResults = await Task.WhenAll(engineTasks);

foreach (var (_, results, _) in engineResults)
{
    allResults.AddRange(results);
}

sw.Stop();
var durationMs = (int)sw.ElapsedMilliseconds;

if (!silent)
{
    Console.WriteLine($"\r    Security checks complete — {allResults.Count} checks in {durationMs / 1000.0:F1}s          ");
}

// ── Attach threats to hardware info ──
hardwareInfo.Threats = threats;

// ── Network diagnostics ──
NetworkDiagResult? networkDiag = null;
if (!silent) Console.Write("    Running network diagnostics...");
try
{
    networkDiag = await NetworkDiagnostics.RunAllAsync(config.ApiUrl, verbose);
    if (!silent)
    {
        var vpnCount = networkDiag.VpnInterfaces?.Count ?? 0;
        var peerCount = networkDiag.InternalLatency?.Count ?? 0;
        var cloudCount = networkDiag.CloudEndpointLatency?.Count(e => e.Reachable) ?? 0;
        var dnsMs = networkDiag.DnsResolutionMs?.ToString("0.#") ?? "n/a";
        Console.WriteLine($" done ({networkDiag.DownloadMbps:0.#} Mbps down, {peerCount} peers, {vpnCount} VPNs, {cloudCount} cloud, DNS {dnsMs}ms)");
    }
}
catch (Exception ex)
{
    if (verbose) Console.Error.WriteLine($"  [WARN] Network diagnostics failed: {ex.Message}");
    if (!silent) Console.WriteLine(" skipped");
}

// ── SNMP infrastructure scan ──
var noSnmp = aloneMode || silent || args.Contains("--no-snmp", StringComparer.OrdinalIgnoreCase);
if (!noSnmp)
{
    try
    {
        var snmpCreds = await apiClient.GetSnmpCredentialsAsync();
        if (snmpCreds != null)
        {
            if (!silent) Console.Write("    Running SNMP infrastructure scan...");
            var snmpTargets = snmpCreds.Targets ?? new List<string>();

            if (snmpTargets.Count == 0)
            {
                var discovered = new HashSet<string>();
                // Gateways
                if (networkDiag?.GatewayIp != null)
                    discovered.Add(networkDiag.GatewayIp);
                if (networkDiag?.RouteTable != null)
                    foreach (var r in networkDiag.RouteTable)
                        if (!string.IsNullOrEmpty(r.NextHop) && r.NextHop != "0.0.0.0"
                            && System.Net.IPAddress.TryParse(r.NextHop, out _))
                            discovered.Add(r.NextHop);
                // Latency sweep peers
                if (networkDiag?.InternalLatency != null)
                    foreach (var p in networkDiag.InternalLatency.Where(p => p.Reachable))
                        discovered.Add(p.Host);

                // Full subnet SNMP sweep — catches switches, APs, printers not in ARP/AD
                var subnetIps = await SnmpScanner.DiscoverSubnetAsync(snmpCreds, verbose);
                foreach (var ip in subnetIps) discovered.Add(ip);

                snmpTargets = discovered.ToList();
            }

            if (snmpTargets.Count > 0)
            {
                var snmpResult = await SnmpScanner.ScanAsync(snmpCreds, snmpTargets, verbose);

                // Enrich SNMP devices with MAC from first non-empty interface + OUI lookup
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
                var arpDevices = new List<SnmpDeviceResult>();
                foreach (var (ip, mac) in arpEntries)
                {
                    if (!seenIps.Add(ip)) continue;
                    if (mac is "ff-ff-ff-ff-ff-ff" or "00-00-00-00-00-00") continue;
                    var normalMac = mac.Replace('-', ':').ToUpperInvariant();
                    var oui = OuiLookup.Lookup(normalMac);
                    arpDevices.Add(new SnmpDeviceResult
                    {
                        Ip = ip,
                        MacAddress = normalMac,
                        Vendor = oui?.Vendor,
                        DeviceType = oui?.Category ?? "unknown",
                    });
                }

                // Reverse DNS for all discovered devices
                snmpResult.Devices.AddRange(arpDevices);
                var allDevices = snmpResult.Devices;
                {
                    var dnsSem = new SemaphoreSlim(20);
                    await Task.WhenAll(allDevices.Select(async dev =>
                    {
                        await dnsSem.WaitAsync();
                        try
                        {
                            using var cts = new CancellationTokenSource(2000);
                            var entry = await System.Net.Dns.GetHostEntryAsync(dev.Ip, cts.Token);
                            if (!string.IsNullOrEmpty(entry.HostName) && entry.HostName != dev.Ip)
                            {
                                dev.ReverseDns = entry.HostName;
                                if (dev.SysName == null) dev.SysName = entry.HostName;
                            }
                        }
                        catch { }
                        finally { dnsSem.Release(); }
                    }));
                    var resolved = allDevices.Count(d => d.ReverseDns != null);
                    if (resolved > 0 && verbose)
                        Console.WriteLine($"  [SNMP] Reverse DNS resolved {resolved}/{allDevices.Count} hostnames");
                }

                // Large-packet ping: latency + jitter + packet loss
                {
                    var pingSem = new SemaphoreSlim(20);
                    await Task.WhenAll(allDevices.Select(async dev =>
                    {
                        await pingSem.WaitAsync();
                        try
                        {
                            using var ping = new System.Net.NetworkInformation.Ping();
                            var buf = new byte[1472]; // MTU test
                            var rtts = new List<long>();
                            int sent = 5, received = 0;
                            for (int i = 0; i < sent; i++)
                            {
                                try
                                {
                                    var reply = await ping.SendPingAsync(dev.Ip, 2000, buf);
                                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                                    {
                                        rtts.Add(reply.RoundtripTime);
                                        received++;
                                    }
                                }
                                catch { }
                            }
                            if (received > 0)
                            {
                                dev.PingLatencyMs = rtts.Average();
                                dev.PingLossPct = Math.Round((1.0 - (double)received / sent) * 100, 1);
                                if (rtts.Count >= 2)
                                {
                                    var diffs = new List<double>();
                                    for (int i = 1; i < rtts.Count; i++)
                                        diffs.Add(Math.Abs(rtts[i] - rtts[i - 1]));
                                    dev.PingJitterMs = Math.Round(diffs.Average(), 2);
                                }
                            }
                        }
                        catch { }
                        finally { pingSem.Release(); }
                    }));
                    var pinged = allDevices.Count(d => d.PingLatencyMs != null);
                    if (pinged > 0 && verbose)
                        Console.WriteLine($"  [SNMP] Ping: {pinged}/{allDevices.Count} reachable");
                }

                Console.WriteLine(
                    $"  [SNMP] {snmpResult.Devices.Count} devices found ({arpEntries.Count} ARP), {snmpResult.Unreachable.Count} unreachable");

                // Pass 2: vendor-specific OIDs for devices with sysObjectId
                var devicesWithSysOid = snmpResult.Devices
                    .Where(d => !string.IsNullOrEmpty(d.SysObjectId))
                    .ToList();
                if (devicesWithSysOid.Count > 0)
                {
                    try
                    {
                        var sysOids = devicesWithSysOid.Select(d => d.SysObjectId!).Distinct().ToList();
                        var profiles = await apiClient.GetSnmpProfilesAsync(sysOids);
                        if (profiles?.Profiles.Count > 0)
                        {
                            if (!silent) Console.WriteLine($"  [SNMP] Pass 2: {profiles.Profiles.Count} vendor profile(s) matched");
                            foreach (var dev in devicesWithSysOid)
                            {
                                var profile = profiles.Profiles.FirstOrDefault(
                                    p => dev.SysObjectId!.StartsWith(p.OidPrefix));
                                if (profile == null) continue;
                                var vendorData = await SnmpScanner.ScanVendorOidsAsync(
                                    dev.Ip, profile.Oids, snmpCreds, verbose);
                                if (vendorData.Count > 0)
                                {
                                    dev.VendorData = vendorData;
                                    if (dev.Vendor == null) dev.Vendor = profile.Vendor;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (verbose) Console.Error.WriteLine($"  [WARN] SNMP pass 2 failed: {ex.Message}");
                    }
                }

                // WMI probe: unenrolled Windows machines (no SNMP data or sysDescr contains "Windows")
                var wmiCandidates = snmpResult.Devices
                    .Where(d => d.SysDescr == null || d.SysDescr.Contains("Windows", StringComparison.OrdinalIgnoreCase))
                    .Where(d => d.SysObjectId == null) // no SNMP = ARP-only or Windows
                    .Select(d => d.Ip)
                    .ToList();

                if (wmiCandidates.Count > 0)
                {
                    try
                    {
                        if (!silent) Console.Write($"  [WMI] Probing {wmiCandidates.Count} potential Windows hosts...");
                        var wmiDevices = await WmiProbe.ProbeAsync(wmiCandidates, verbose);
                        if (wmiDevices.Count > 0)
                        {
                            // Merge WMI data into existing device entries
                            var devByIp = snmpResult.Devices.ToDictionary(d => d.Ip);
                            foreach (var wmi in wmiDevices)
                            {
                                if (devByIp.TryGetValue(wmi.Ip, out var existing))
                                {
                                    existing.SysName = wmi.SysName ?? existing.SysName;
                                    existing.SysDescr = wmi.SysDescr ?? existing.SysDescr;
                                    existing.MacAddress = wmi.MacAddress ?? existing.MacAddress;
                                    existing.DeviceType = "computer";
                                    existing.HostResources = wmi.HostResources;
                                    if (wmi.VendorData != null)
                                    {
                                        existing.VendorData ??= new Dictionary<string, string>();
                                        foreach (var kv in wmi.VendorData)
                                            existing.VendorData[kv.Key] = kv.Value;
                                    }
                                }
                            }
                            if (!silent) Console.WriteLine($" {wmiDevices.Count} enriched");
                        }
                        else if (!silent) Console.WriteLine(" none reachable");
                    }
                    catch (Exception ex)
                    {
                        if (verbose) Console.Error.WriteLine($"  [WARN] WMI probe failed: {ex.Message}");
                        if (!silent) Console.WriteLine(" skipped");
                    }
                }

                if (snmpResult.Devices.Count > 0)
                {
                    await apiClient.SubmitSnmpResultsAsync(snmpResult);
                    Console.WriteLine($"  [SNMP] Uploaded {snmpResult.Devices.Count} device(s)");
                }
            }
            else
            {
                if (!silent) Console.WriteLine(" skipped (no targets)");
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  [WARN] SNMP scan failed: {ex.Message}");
    }
}

// ── Build payload ──
var payload = new AssessmentPayload
{
    AgentId = config.AgentId,
    AgentVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
    Timestamp = DateTime.UtcNow,
    DurationMs = durationMs,
    Platform = platformInfo,
    Hardware = hardwareInfo,
    Software = softwareList,
    Results = allResults,
    NetworkDiag = networkDiag,
};

// ── Offline mode: dump to shared folder instead of uploading ──
var offlineMode = args.Contains("--offline", StringComparer.OrdinalIgnoreCase);
var sharePath = GetArg(args, "--share");

if (offlineMode || sharePath is not null)
{
    var targetDir = sharePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Kryoss", "OfflineCollect");
    OfflineStore.SaveCollectPayload(payload, config, targetDir, silent);
    Console.WriteLine($"RESULT: OFFLINE | {Environment.MachineName} | {allResults.Count} checks | Saved to shared folder");
    Environment.Exit(0);
    return;
}

// ── Upload payload ──
try
{
    if (!silent) Console.Write("  Uploading results...");

    var response = await apiClient.SubmitResultsAsync(payload);
    if (!silent) Console.Write("\r                          \r");
    if (response is not null)
    {
        if (!silent)
        {
            Console.WriteLine();
            Console.ForegroundColor = response.Grade is "A+" or "A" ? ConsoleColor.Green
                : response.Grade is "B" or "C" ? ConsoleColor.Yellow
                : ConsoleColor.Red;

            Console.WriteLine($"  ╔══════════════════════════════════════╗");
            Console.WriteLine($"  ║  Score: {response.Score,6}%   Grade: {response.Grade,-4}     ║");
            Console.WriteLine($"  ║  Pass: {response.PassCount,4}  Warn: {response.WarnCount,4}  Fail: {response.FailCount,4} ║");
            Console.WriteLine($"  ╚══════════════════════════════════════╝");
            Console.ResetColor();
            if (!string.IsNullOrEmpty(response.YourPublicIp))
                Console.WriteLine($"  Public IP: {response.YourPublicIp}");
        }
        else
        {
            Console.WriteLine($"RESULT: OK | {Environment.MachineName} | {response.Score}% {response.Grade} | P:{response.PassCount} W:{response.WarnCount} F:{response.FailCount}");
        }
    }

    // ── Trial mode: download report, open in browser, clean up ──
    if (trialMode && response is not null)
    {
        if (!silent) Console.Write("  Generating trial report...");
        var html = await apiClient.DownloadReportAsync("preventas", "detailed");
        if (html is not null)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var reportPath = Path.Combine(desktop, $"Kryoss-Trial-Report-{DateTime.Now:yyyyMMdd}.html");
            File.WriteAllText(reportPath, html);
            if (!silent) Console.WriteLine($" saved to {reportPath}");

            Console.WriteLine($"  Report: {reportPath}");

            AgentConfig.Wipe();
            if (!silent)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  Trial complete — report opened in browser. No data retained.");
                Console.ResetColor();
            }
            Console.WriteLine($"RESULT: TRIAL | {Environment.MachineName} | {response.Score}% {response.Grade} | Report saved");
            Environment.Exit(0);
            return;
        }
        else
        {
            if (!silent) Console.WriteLine(" failed (report unavailable)");
        }
    }

    // ── Auto AD hygiene for Domain Controllers ──
    if (hardwareInfo.ProductType == 2)
    {
        if (!silent) Console.Write("  Running AD hygiene audit (DC detected)...");
        try
        {
            TargetDiscovery.DiscoverAd(null);
            var report = TargetDiscovery.LastHygieneReport;
            if (report is not null)
            {
                var hygienePayload = BuildHygienePayload(report);
                if (hygienePayload.Findings.Count > 0)
                {
                    await apiClient.SubmitHygieneAsync(hygienePayload);
                    if (!silent) Console.WriteLine($" done ({hygienePayload.Findings.Count} findings)");
                    else Console.WriteLine($"RESULT: HYGIENE | {Environment.MachineName} | {hygienePayload.Findings.Count} findings");
                }
                else if (!silent) Console.WriteLine(" no findings");
            }
            else if (!silent) Console.WriteLine(" no findings");
        }
        catch (Exception hyEx)
        {
            if (verbose) Console.Error.WriteLine($"  [WARN] AD hygiene failed: {hyEx.Message}");
            if (!silent) Console.WriteLine(" skipped");
        }
    }

    try
    {
        var kryossDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Kryoss");
        Directory.CreateDirectory(kryossDir);
        File.WriteAllText(Path.Combine(kryossDir, "lastrun.txt"),
            DateTime.UtcNow.ToString("yyyy-MM-dd"));
    }
    catch { }

    // ── Network scan: runs by default, skip with --no-network, --alone, or --silent ──
    if (!noNetwork && !scanMode && !silent)
    {
        if (!silent)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  ═══════════════════════════════════════");
            Console.WriteLine("  Starting network scan...");
            Console.WriteLine("  ═══════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine();
        }

        try
        {
            var scanArgs = new List<string> { "--scan" };
            if (enrollCode is not null) { scanArgs.Add("--code"); scanArgs.Add(enrollCode); }
            if (forceReenroll) scanArgs.Add("--reenroll");
            if (noPorts) scanArgs.Add("--no-ports");
            if (noAd) scanArgs.Add("--no-ad");
            if (silent) scanArgs.Add("--silent");
            await NetworkScanner.RunAsync(scanArgs.ToArray(), silent);
        }
        catch (Exception scanEx)
        {
            if (!silent) Console.Error.WriteLine($"[WARN] Network scan failed: {scanEx.Message}");
        }
    }

    var pendingCount = OfflineStore.LoadPending().Count;
    if (pendingCount == 0)
    {
        AgentConfig.Wipe();
        if (verbose) Console.WriteLine("  [state] Registry wiped (stateless cycle complete)");
    }
    else if (verbose)
    {
        Console.WriteLine($"  [state] Registry kept ({pendingCount} offline payloads pending)");
    }

    // Auto-install as service if not already installed (continuous monitoring)
    if (!scanMode && !ServiceInstaller.IsInstalled())
    {
        try
        {
            if (!silent) Console.WriteLine("\n  Installing Kryoss Agent as Windows Service...");
            ServiceInstaller.Install();
            if (!silent) Console.WriteLine("  Agent will now run continuously with heartbeat every 15 min.");
        }
        catch (Exception svcEx)
        {
            if (verbose) Console.Error.WriteLine($"  [WARN] Auto-install service failed: {svcEx.Message}");
        }
    }

    Environment.Exit(0);
}
catch (Exception ex)
{
    // If share path configured, fall back to offline dump instead of local queue
    if (sharePath is not null)
    {
        OfflineStore.SaveCollectPayload(payload, config, sharePath, silent);
        Console.WriteLine($"RESULT: OFFLINE | {Environment.MachineName} | {allResults.Count} checks | Fallback to shared folder");
        Environment.Exit(2);
    }
    else
    {
        Console.Error.WriteLine($"[WARN] Upload failed: {ex.Message}");
        Console.Error.WriteLine("  Saving results offline for later upload...");
        Console.WriteLine($"RESULT: OFFLINE | {Environment.MachineName} | {allResults.Count} checks | Upload failed: {ex.Message}");
        OfflineStore.SavePayload(payload);
        Environment.Exit(2);
    }
}

}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"[FATAL] {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    Console.ResetColor();
    Console.WriteLine($"RESULT: ERROR | {Environment.MachineName} | FATAL: {ex.Message}");
    Environment.Exit(99);
}

// ── Helper: parse named CLI argument ──
static string? GetArg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }
    return null;
}

// ── Helper: upload pending offline results ──
static async Task UploadPendingResults(AgentConfig config, bool silent)
{
    var pending = OfflineStore.LoadPending();
    if (pending.Count == 0) return;

    if (!silent) Console.WriteLine($"  Found {pending.Count} pending offline result(s). Uploading...");

    using var client = new ApiClient(config);
    foreach (var (path, payload) in pending)
    {
        try
        {
            await client.SubmitResultsAsync(payload);
            OfflineStore.RemovePending(path);
            if (!silent) Console.WriteLine($"  Uploaded pending: {Path.GetFileName(path)}");
        }
        catch
        {
            if (!silent) Console.WriteLine($"  Still offline, will retry: {Path.GetFileName(path)}");
        }
    }
}

// ── Collect mode: read JSONs from shared folder, POST to /v1/collect ──
static async Task RunCollectMode(string collectPath, string[] args, bool silent, bool verbose)
{
    if (!Directory.Exists(collectPath))
    {
        Console.Error.WriteLine($"[ERROR] Collect path does not exist: {collectPath}");
        Environment.Exit(1);
        return;
    }

    var cliCode = GetArg(args, "--code");
    var cliApiUrl = GetArg(args, "--api-url");

    var config = AgentConfig.Load();
    if (EmbeddedConfig.ApiUrl is not null) config.ApiUrl = EmbeddedConfig.ApiUrl;
    if (!string.IsNullOrEmpty(cliApiUrl)) config.ApiUrl = cliApiUrl;

    var enrollCode = cliCode ?? EmbeddedConfig.EnrollmentCode;

    // Collector must be enrolled to authenticate
    if (!config.IsEnrolled)
    {
        if (string.IsNullOrEmpty(enrollCode))
        {
            Console.Error.WriteLine("[ERROR] Collector not enrolled. Provide --code <enrollment-code>.");
            Environment.Exit(1);
            return;
        }
        var hostname = Environment.MachineName;
        var platform = PlatformDetector.DetectPlatform();
        var earlyHw = PlatformDetector.DetectHardware();
        if (!silent) Console.WriteLine($"  Enrolling collector {hostname}...");

        using var enrollClient = new ApiClient(config);
        var enrollment = await enrollClient.EnrollAsync(enrollCode, hostname, platform, earlyHw.ProductType ?? 0);
        if (enrollment is null)
        {
            Console.Error.WriteLine("[ERROR] Collector enrollment failed.");
            Environment.Exit(1);
            return;
        }
        config.AgentId = enrollment.AgentId;
        config.ApiKey = enrollment.ApiKey;
        config.ApiSecret = enrollment.ApiSecret;
        config.PublicKeyPem = enrollment.PublicKey;
        config.AssessmentId = enrollment.AssessmentId;
        // Save per-machine auth credentials (v2.2+)
        config.MachineSecret = enrollment.MachineSecret;
        config.SessionKey = enrollment.SessionKey;
        config.SessionKeyExpiresAt = enrollment.SessionKeyExpiresAt;
        config.Save(args.Contains("--debug-acl", StringComparer.OrdinalIgnoreCase));
        if (!silent) Console.WriteLine($"  Collector enrolled.");
    }

    var files = Directory.GetFiles(collectPath, "collect_*.json");
    if (files.Length == 0)
    {
        if (!silent) Console.WriteLine("  No pending collect files found.");
        Environment.Exit(0);
        return;
    }

    if (!silent) Console.WriteLine($"  Found {files.Length} collect file(s). Uploading...");

    var donePath = Path.Combine(collectPath, "done");
    Directory.CreateDirectory(donePath);

    using var client = new ApiClient(config);
    var successCount = 0;
    var failCount = 0;

    foreach (var file in files)
    {
        try
        {
            var json = File.ReadAllText(file);
            var envelope = System.Text.Json.JsonSerializer.Deserialize(json,
                KryossJsonContext.Default.OfflineCollectPayload);
            if (envelope is null)
            {
                if (!silent) Console.Error.WriteLine($"  [WARN] Skipping corrupt file: {Path.GetFileName(file)}");
                failCount++;
                continue;
            }

            await client.SubmitCollectAsync(envelope);
            File.Move(file, Path.Combine(donePath, Path.GetFileName(file)), overwrite: true);
            successCount++;
            if (verbose) Console.WriteLine($"  Uploaded: {Path.GetFileName(file)} ({envelope.Hostname})");
        }
        catch (Exception ex)
        {
            failCount++;
            Console.Error.WriteLine($"  [WARN] Failed {Path.GetFileName(file)}: {ex.Message}");
        }
    }

    if (!silent) Console.WriteLine($"  Collect complete: {successCount} uploaded, {failCount} failed.");
    Console.WriteLine($"RESULT: COLLECT | {Environment.MachineName} | {successCount} uploaded, {failCount} failed");
    Environment.Exit(failCount > 0 ? 2 : 0);
}

// ── Help ──
static void PrintHelp()
{
    Console.WriteLine(@"
Kryoss Security Agent v" + (typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0") + @"
TeamLogic IT — Security Assessment Tool

USAGE:
  KryossAgent.exe [options]

DEFAULT BEHAVIOR (no flags):
  Enrolls this machine, runs security controls, uploads results.

OPTIONS:

  Enrollment:
    --code CODE          Enrollment code (if not embedded in binary)
    --api-url URL        API endpoint (default: https://func-kryoss.azurewebsites.net)
    --reenroll           Clear existing enrollment and re-enroll

  Service:
    --install            Install as Windows Service (auto-start)
    --uninstall          Stop and remove Windows Service
    --service            Run as Windows Service (used by SCM)

  Modes:
    --alone              Scan only this machine (skip network scan entirely)
    --scan               Network discovery + port scan + AD hygiene only (no local assessment)
    --trial              Trial mode: scan, generate report to Desktop, open in browser, wipe

  Disable specific collectors:
    --no-network         Skip network scan phase (same as --alone)
    --no-ports           Skip port scanning
    --no-ad              Skip AD hygiene audit
    --no-threats         Skip threat detection
    --no-snmp            Skip SNMP device discovery

  Offline collection:
    --offline            Save results to shared folder (skip upload)
    --share PATH         Shared folder path for offline results
    --collect PATH       Upload all collect_*.json files from PATH to API

  Network discovery (used with --scan):
    --discover-ad [OU]   Discover machines via Active Directory
    --discover-arp       Discover machines via ARP table
    --discover-subnet CIDR  Probe subnet (e.g. 192.168.1.0/24)
    --targets H1,H2,H3  Explicit target list (comma-separated)
    --targets-file FILE  Read targets from file (one per line)
    --threads N          Parallel scan threads (default: 10)

  Output:
    --silent             No console output (for scheduled/automated execution)
    --verbose            Show detailed engine output and command progress

  Help:
    --help, -?, /?       Show this help message

EXAMPLES:
  KryossAgent.exe --install                    Install as Windows Service
  KryossAgent.exe --uninstall                  Remove Windows Service
  KryossAgent.exe                              Scan this machine (default, one-shot)
  KryossAgent.exe --offline --share \\server\kryoss  Scan + save to share
  KryossAgent.exe --collect \\server\kryoss    Upload all pending from share
  KryossAgent.exe --scan --threads 20          Network discovery + port scan
  KryossAgent.exe --reenroll --code XXXX       Re-enroll and scan
  KryossAgent.exe --trial --code XXXX           Trial: scan + report + clean up
  KryossAgent.exe --verbose                    Full detail output

EXIT CODES:
  0   Success
  1   Fatal error (enrollment failed, no controls, etc.)
  2   Partial (upload deferred, some targets unreachable)
  99  Unhandled exception
");
}

static HygienePayload BuildHygienePayload(TargetDiscovery.AdHygieneReport report)
{
    var findings = new List<HygieneFinding>();
    void Add(IEnumerable<TargetDiscovery.AdHygieneItem> items, string objType, string? statusOverride = null)
    {
        foreach (var i in items)
            findings.Add(new HygieneFinding
            {
                Name = i.Name, ObjectType = objType,
                Status = statusOverride ?? i.Status,
                DaysInactive = i.DaysInactive,
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

    return new HygienePayload
    {
        ScannedBy = Environment.MachineName,
        TotalMachines = report.StaleMachines.Count + report.DormantMachines.Count
            + TargetDiscovery.LastDiscoveredActiveCount,
        TotalUsers = report.StaleUsers.Count + report.DormantUsers.Count
            + report.DisabledUsers.Count + report.NeverExpirePasswords.Count
            + TargetDiscovery.LastDiscoveredActiveUserCount,
        Findings = findings
    };
}
