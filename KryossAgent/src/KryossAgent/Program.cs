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
    // Hardware detection deferred to after controls download — not needed for
    // enrollment (EnrollAsync only uses OS strings from platform).

    if (!silent) Console.WriteLine($"  Enrolling {hostname}...");

    try
    {
        using var enrollClient = new ApiClient(config);
        var enrollment = await enrollClient.EnrollAsync(code, hostname, platform);
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
    new TpmEngine()                              // WMI Win32_Tpm (no tpmtool.exe)
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

// ── Build and upload payload ──
var payload = new AssessmentPayload
{
    AgentId = config.AgentId,
    AgentVersion = "1.3.0",
    Timestamp = DateTime.UtcNow,
    DurationMs = durationMs,
    Platform = platformInfo,
    Hardware = hardwareInfo,
    Software = softwareList,
    Results = allResults,
};

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
        }
        else
        {
            Console.WriteLine($"RESULT: OK | {Environment.MachineName} | {response.Score}% {response.Grade} | P:{response.PassCount} W:{response.WarnCount} F:{response.FailCount}");
        }
    }

    // ── v1.3.0: Stateless cycle ──
    // Wipe credentials from registry after successful upload IF no offline
    // queue items remain. Next run will re-enroll (cheap, <1s) and use fresh
    // credentials. Zero on-disk state when idle = minimal attack surface.
    // If offline queue has items, KEEP credentials — next run needs them to
    // re-sign and upload the queued payloads.
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
    Console.Error.WriteLine($"[WARN] Upload failed: {ex.Message}");
    Console.Error.WriteLine("  Saving results offline for later upload...");
    Console.WriteLine($"RESULT: OFFLINE | {Environment.MachineName} | {allResults.Count} checks | Upload failed: {ex.Message}");
    OfflineStore.SavePayload(payload);
    Environment.Exit(2);
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
  KryossAgent.exe --scan --threads 20          Network discovery + port scan
  KryossAgent.exe --reenroll --code XXXX       Re-enroll and scan
  KryossAgent.exe --scan --discover-subnet 10.0.0.0/24
  KryossAgent.exe --verbose                    Full detail output

EXIT CODES:
  0   Success
  1   Fatal error (enrollment failed, no controls, etc.)
  2   Partial (upload deferred, some targets unreachable)
  99  Unhandled exception
");
}
