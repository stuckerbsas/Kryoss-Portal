using System.Diagnostics;
using KryossAgent.Config;
using KryossAgent.Engines;
using KryossAgent.Models;
using KryossAgent.Services;

// ── Kryoss Security Agent ──
// v1.0.0

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

var sw = Stopwatch.StartNew();
var silent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase);
var verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);
if (verbose) Environment.SetEnvironmentVariable("KRYOSS_VERBOSE", "1");
var aloneMode = args.Contains("--alone", StringComparer.OrdinalIgnoreCase);
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
        Console.WriteLine("║       Kryoss Security Agent v1.2.0      ║");
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
    var hardware = PlatformDetector.DetectHardware();

    if (!silent) Console.WriteLine($"  Enrolling {hostname}...");

    try
    {
        using var enrollClient = new ApiClient(config);
        var enrollment = await enrollClient.EnrollAsync(code, hostname, platform, hardware);
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
    if (verbose) Console.WriteLine("  Downloading control definitions...");
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
if (verbose) Console.WriteLine();
if (verbose) Console.WriteLine("  Collecting system information...");
var platformInfo = PlatformDetector.DetectPlatform();
var hardwareInfo = PlatformDetector.DetectHardware();
List<SoftwareItem> softwareList;
try
{
    softwareList = SoftwareInventory.Enumerate();
    if (verbose) Console.WriteLine($"  {softwareList.Count} software packages detected");
}
catch (Exception ex)
{
    if (verbose) Console.Error.WriteLine($"  [WARN] Software enumeration failed: {ex.Message}");
    softwareList = [];
}

// ── Execute checks by engine ──
if (verbose) Console.WriteLine();
if (verbose) Console.WriteLine("  Running security assessment...");

ShellEngine.Verbose = verbose;
ICheckEngine[] engines =
[
    new RegistryEngine(),
    new SeceditEngine(),
    new AuditpolEngine(),
    new FirewallEngine(),
    new ServiceEngine(),
    new NetAccountsEngine(),
    new ShellEngine(),
    new EventLogEngine(),
    new CertStoreEngine(),
    new BitLockerEngine(),
    new TpmEngine()
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

var engineTasks = engines
    .Where(e => controlsByType.ContainsKey(e.Type))
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
            return (engine.Type, Results: engineResults, Count: controls.Count);
        }
        catch (Exception ex)
        {
            engineSw.Stop();
            LogLine($"  [{engine.Type}] FAILED after {engineSw.ElapsedMilliseconds} ms: {ex.Message}");
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

if (verbose)
{
    Console.WriteLine();
    Console.WriteLine($"  Assessment completed in {durationMs / 1000.0:F1}s ({allResults.Count} checks)");
}

// ── Build and upload payload ──
var payload = new AssessmentPayload
{
    AgentId = config.AgentId,
    AgentVersion = "1.1.2",
    Timestamp = DateTime.UtcNow,
    DurationMs = durationMs,
    Platform = platformInfo,
    Hardware = hardwareInfo,
    Software = softwareList,
    Results = allResults
};

try
{
    if (verbose) Console.WriteLine("  Uploading results...");

    var response = await apiClient.SubmitResultsAsync(payload);
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

    // ── Network scan: runs by default unless --alone or --silent ──
    if (!aloneMode && !silent && !scanMode)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ═══════════════════════════════════════");
        Console.WriteLine("  Starting network scan...");
        Console.WriteLine("  ═══════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();

        try
        {
            var scanArgs = new List<string> { "--scan" };
            if (enrollCode is not null) { scanArgs.Add("--code"); scanArgs.Add(enrollCode); }
            // Pass through reenroll if it was specified
            if (forceReenroll) scanArgs.Add("--reenroll");
            var netResult = await NetworkScanner.RunAsync(scanArgs.ToArray(), silent);
        }
        catch (Exception scanEx)
        {
            Console.Error.WriteLine($"[WARN] Network scan failed: {scanEx.Message}");
        }
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
