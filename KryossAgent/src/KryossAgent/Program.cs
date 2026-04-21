using System.Diagnostics;
using KryossAgent.Config;
using KryossAgent.Engines;
using KryossAgent.Models;
using KryossAgent.Services;

// ── Kryoss Security Agent ──
// v1.3.0

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

// ── Validate arguments ──
var knownFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "--silent", "--verbose", "--scan", "--reenroll",
    "--code", "--api-url", "--threads", "--targets", "--targets-file",
    "--discover-ad", "--discover-arp", "--discover-subnet",
    "--offline", "--share", "--collect",
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
var scanMode = args.Contains("--scan", StringComparer.OrdinalIgnoreCase);

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
        Console.WriteLine("║       Kryoss Security Agent v1.3.0      ║");
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
        config.Save();

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
if (!silent) Console.Write("    Scanning for threats...");
List<ThreatFinding> threats;
try
{
    threats = ThreatDetector.ScanAll();
}
catch
{
    threats = [];
}
if (!silent) Console.WriteLine($" {threats.Count} found");

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
try
{
    var snmpCreds = await apiClient.GetSnmpCredentialsAsync();
    if (snmpCreds != null)
    {
        if (!silent) Console.Write("    Running SNMP infrastructure scan...");
        var snmpTargets = snmpCreds.Targets ?? new List<string>();
        // Auto-discover from ARP/route if no explicit targets
        if (snmpTargets.Count == 0 && networkDiag?.InternalLatency != null)
            snmpTargets = networkDiag.InternalLatency
                .Where(p => p.Reachable)
                .Select(p => p.Host)
                .ToList();

        if (snmpTargets.Count > 0)
        {
            var snmpResult = await SnmpScanner.ScanAsync(snmpCreds, snmpTargets, verbose);
            if (!silent) Console.WriteLine($" done ({snmpResult.Devices.Count} devices, {snmpResult.Unreachable.Count} unreachable)");
            if (snmpResult.Devices.Count > 0)
                await apiClient.SubmitSnmpResultsAsync(snmpResult);
        }
        else if (!silent) Console.WriteLine(" skipped (no targets)");
    }
}
catch (Exception ex)
{
    if (verbose) Console.Error.WriteLine($"  [WARN] SNMP scan failed: {ex.Message}");
    if (!silent) Console.WriteLine(" skipped");
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
                var allFindings = new List<object>();
                foreach (var m in report.StaleMachines) allFindings.Add(new { m.Name, objectType = "Computer", status = "Stale", m.DaysInactive, m.Detail });
                foreach (var m in report.DormantMachines) allFindings.Add(new { m.Name, objectType = "Computer", status = "Dormant", m.DaysInactive, m.Detail });
                foreach (var u in report.StaleUsers) allFindings.Add(new { u.Name, objectType = "User", status = "Stale", u.DaysInactive, u.Detail });
                foreach (var u in report.DormantUsers) allFindings.Add(new { u.Name, objectType = "User", status = "Dormant", u.DaysInactive, u.Detail });
                foreach (var u in report.DisabledUsers) allFindings.Add(new { u.Name, objectType = "User", status = "Disabled", u.DaysInactive, u.Detail });
                foreach (var u in report.NeverExpirePasswords) allFindings.Add(new { u.Name, objectType = "User", status = "PwdNeverExpires", u.DaysInactive, u.Detail });
                foreach (var s in report.PrivilegedAccounts) allFindings.Add(new { s.Name, objectType = "Security", status = "PrivilegedAccount", s.DaysInactive, s.Detail });
                foreach (var s in report.KerberoastableAccounts) allFindings.Add(new { s.Name, objectType = "Security", status = "Kerberoastable", s.DaysInactive, s.Detail });
                foreach (var s in report.UnconstrainedDelegation) allFindings.Add(new { s.Name, objectType = "Security", status = "UnconstrainedDelegation", s.DaysInactive, s.Detail });
                foreach (var s in report.AdminCountResidual) allFindings.Add(new { s.Name, objectType = "Security", status = "AdminCountResidue", s.DaysInactive, s.Detail });
                foreach (var s in report.NoLaps) allFindings.Add(new { s.Name, objectType = "Security", status = "NoLAPS", s.DaysInactive, s.Detail });
                foreach (var s in report.DomainInfo) allFindings.Add(new { s.Name, objectType = "Config", status = s.Status, s.DaysInactive, s.Detail });

                var totalMachines = TargetDiscovery.LastDiscoveredActiveCount;
                var totalUsers = TargetDiscovery.LastDiscoveredActiveUserCount;

                await apiClient.SubmitHygieneAsync(new
                {
                    scannedBy = Environment.MachineName,
                    totalMachines,
                    totalUsers,
                    findings = allFindings
                });

                if (!silent) Console.WriteLine($" done ({allFindings.Count} findings)");
                else Console.WriteLine($"RESULT: HYGIENE | {Environment.MachineName} | {allFindings.Count} findings");
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
        config.Save();
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
Kryoss Security Agent v1.3.0
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

  Scan mode:
    --scan               Network discovery + port scan + AD hygiene (no remote deployment)

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
  KryossAgent.exe                              Scan this machine (default)
  KryossAgent.exe --offline --share \\server\kryoss  Scan + save to share
  KryossAgent.exe --collect \\server\kryoss    Upload all pending from share
  KryossAgent.exe --scan --threads 20          Network discovery + port scan
  KryossAgent.exe --reenroll --code XXXX       Re-enroll and scan
  KryossAgent.exe --verbose                    Full detail output

EXIT CODES:
  0   Success
  1   Fatal error (enrollment failed, no controls, etc.)
  2   Partial (upload deferred, some targets unreachable)
  99  Unhandled exception
");
}
