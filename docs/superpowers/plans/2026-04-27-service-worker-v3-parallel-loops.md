# ServiceWorker v3 — Parallel Loops Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite ServiceWorker to run 5 independent parallel loops so heartbeat never blocks on scans.

**Architecture:** Each concern (self-update, heartbeat, compliance, SNMP, network) runs as an independent `async Task` with its own interval, timeout, and error isolation. Shared `ConcurrentQueue<AgentError>` feeds error data to heartbeat for server-side persistence. New `AgentLogger` writes to `C:\ProgramData\Kryoss\Logs\`.

**Tech Stack:** .NET 8, `ConcurrentQueue<T>`, `CancellationTokenSource.CreateLinkedTokenSource`, source-gen JSON, EF Core 8.

**Versions:** Agent 2.8.0, API 1.30.0.

---

### Task 1: AgentLogger — File Logging Service

**Files:**
- Create: `KryossAgent/src/KryossAgent/Services/AgentLogger.cs`

- [ ] **Step 1: Create AgentLogger.cs**

```csharp
using System.Collections.Concurrent;

namespace KryossAgent.Services;

public static class AgentLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Kryoss", "Logs");

    private static readonly ConcurrentQueue<string> _queue = new();
    private static CancellationTokenSource? _cts;
    private static Task? _writerTask;
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB
    private const int RetentionDays = 7;

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            PruneOldFiles();
            _cts = new CancellationTokenSource();
            _writerTask = Task.Run(() => WriterLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LOGGER] Failed to initialize: {ex.Message}");
        }
    }

    public static void Log(string phase, string message)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} [{phase.ToUpperInvariant()}] {message}";
        _queue.Enqueue(line);
        Console.WriteLine(line);
    }

    public static void Error(string phase, string message)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} [{phase.ToUpperInvariant()}] ERROR {message}";
        _queue.Enqueue(line);
        Console.Error.WriteLine(line);
    }

    public static void Shutdown()
    {
        _cts?.Cancel();
        try { _writerTask?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        FlushAll();
    }

    private static async Task WriterLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            FlushAll();
        }
    }

    private static void FlushAll()
    {
        if (_queue.IsEmpty) return;
        try
        {
            var path = GetCurrentLogPath();
            using var writer = new StreamWriter(path, append: true);
            while (_queue.TryDequeue(out var line))
                writer.WriteLine(line);
        }
        catch { /* logging must never crash the agent */ }
    }

    private static string GetCurrentLogPath()
    {
        var baseName = $"agent-{DateTime.UtcNow:yyyy-MM-dd}";
        var path = Path.Combine(LogDir, $"{baseName}.log");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length >= MaxFileSize)
            {
                var rotated = Path.Combine(LogDir, $"{baseName}.1.log");
                if (File.Exists(rotated)) File.Delete(rotated);
                File.Move(path, rotated);
            }
        }
        catch { /* non-fatal */ }
        return path;
    }

    private static void PruneOldFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (var file in Directory.GetFiles(LogDir, "agent-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch { /* non-fatal */ }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build KryossAgent/src/KryossAgent/KryossAgent.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add KryossAgent/src/KryossAgent/Services/AgentLogger.cs
git commit -m "feat(agent): add AgentLogger file logging service"
```

---

### Task 2: AgentError Model + HeartbeatPayload Extension

**Files:**
- Create: `KryossAgent/src/KryossAgent/Models/AgentError.cs`
- Modify: `KryossAgent/src/KryossAgent/Models/HeartbeatPayload.cs`
- Modify: `KryossAgent/src/KryossAgent/Models/JsonContext.cs`

- [ ] **Step 1: Create AgentError.cs with error + loop status DTOs**

```csharp
using System.Text.Json.Serialization;

namespace KryossAgent.Models;

public class AgentError
{
    public string Phase { get; set; } = null!;
    public string Message { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public string? Target { get; set; }
    public bool IsTimeout { get; set; }
}

public class AgentErrorDto
{
    [JsonPropertyName("phase")] public string Phase { get; set; } = null!;
    [JsonPropertyName("message")] public string Message { get; set; } = null!;
    [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
    [JsonPropertyName("target")] public string? Target { get; set; }
    [JsonPropertyName("isTimeout")] public bool IsTimeout { get; set; }
}

public class LoopStatusDto
{
    [JsonPropertyName("lastRunAt")] public DateTime? LastRunAt { get; set; }
    [JsonPropertyName("lastDurationMs")] public int? LastDurationMs { get; set; }
    [JsonPropertyName("lastError")] public string? LastError { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = "idle";
}
```

- [ ] **Step 2: Extend HeartbeatPayload with errors + loopStatus fields**

In `HeartbeatPayload.cs`, add after the `Mode` property:

```csharp
    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AgentErrorDto>? Errors { get; set; }

    [JsonPropertyName("loopStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, LoopStatusDto>? LoopStatus { get; set; }
```

- [ ] **Step 3: Register new types in JsonContext.cs**

Add before the `[JsonSourceGenerationOptions` line in `JsonContext.cs`:

```csharp
[JsonSerializable(typeof(AgentErrorDto))]
[JsonSerializable(typeof(List<AgentErrorDto>))]
[JsonSerializable(typeof(LoopStatusDto))]
[JsonSerializable(typeof(Dictionary<string, LoopStatusDto>))]
```

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build KryossAgent/src/KryossAgent/KryossAgent.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add KryossAgent/src/KryossAgent/Models/AgentError.cs KryossAgent/src/KryossAgent/Models/HeartbeatPayload.cs KryossAgent/src/KryossAgent/Models/JsonContext.cs
git commit -m "feat(agent): add AgentError model + extend HeartbeatPayload with errors/loopStatus"
```

---

### Task 3: Add CancellationToken to ScanCycle Methods

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Services/ScanCycle.cs`

- [ ] **Step 1: Add CancellationToken parameter to all async methods in ScanCycle**

Change each method signature — add `CancellationToken ct = default` as last parameter:

```csharp
// Line 10 — RunComplianceScanAsync
public static async Task<ComplianceScanResult> RunComplianceScanAsync(AgentConfig config, bool silent, bool verbose, CancellationToken ct = default)

// Line 182 — RunSnmpScanAsync
public static async Task RunSnmpScanAsync(ApiClient apiClient, NetworkDiagResult? networkDiag, bool silent, bool verbose, IReadOnlyCollection<string>? extraTargets = null, CancellationToken ct = default)

// Line 317 — RunNetworkScanAsync
public static async Task RunNetworkScanAsync(ApiClient apiClient, bool silent, bool verbose, CancellationToken ct = default)

// Line 371 — RunAdHygieneAsync
public static async Task RunAdHygieneAsync(ApiClient apiClient, HardwareInfo hardwareInfo, bool silent, bool verbose, CancellationToken ct = default)

// Line 434 — RunDcHealthAsync
public static async Task RunDcHealthAsync(ApiClient apiClient, bool silent, bool verbose, CancellationToken ct = default)

// Line 458 — UploadPayloadAsync
public static async Task<ResultsResponse?> UploadPayloadAsync(ApiClient apiClient, AssessmentPayload payload, bool silent, CancellationToken ct = default)
```

- [ ] **Step 2: Pass ct to NetworkDiagnostics.RunAllAsync in RunComplianceScanAsync**

In `ScanCycle.cs` around line 129, change:

```csharp
// OLD:
networkDiag = await NetworkDiagnostics.RunAllAsync(config.ApiUrl, verbose);
// NEW:
networkDiag = await NetworkDiagnostics.RunAllAsync(config.ApiUrl, verbose, ct);
```

- [ ] **Step 3: Pass ct to PortScanner in RunNetworkScanAsync**

In `ScanCycle.cs` around line 338, change:

```csharp
// OLD:
var ports = await PortScanner.ScanTcpAsync(t.Address, timeoutMs: 1000);
// NEW:
var ports = await PortScanner.ScanTcpAsync(t.Address, timeoutMs: 1000, ct: ct);
```

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build KryossAgent/src/KryossAgent/KryossAgent.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add KryossAgent/src/KryossAgent/Services/ScanCycle.cs
git commit -m "feat(agent): add CancellationToken to all ScanCycle methods"
```

---

### Task 4: Add CancellationToken to PortScanner.ScanTcpAsync

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Services/PortScanner.cs`

- [ ] **Step 1: Add ct parameter to ScanTcpAsync signature**

Change line 101:

```csharp
// OLD:
public static async Task<List<PortResult>> ScanTcpAsync(string host, int concurrency = 100, int timeoutMs = 500, bool grabBanners = true)
// NEW:
public static async Task<List<PortResult>> ScanTcpAsync(string host, int concurrency = 100, int timeoutMs = 500, bool grabBanners = true, CancellationToken ct = default)
```

- [ ] **Step 2: Use linked token in the per-port scan**

Change lines 108 and 112 inside the Select lambda:

```csharp
// OLD:
await semaphore.WaitAsync();
// ...
using var cts = new CancellationTokenSource(timeoutMs);
// NEW:
await semaphore.WaitAsync(ct);
// ...
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(timeoutMs);
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build KryossAgent/src/KryossAgent/KryossAgent.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add KryossAgent/src/KryossAgent/Services/PortScanner.cs
git commit -m "feat(agent): add CancellationToken to PortScanner.ScanTcpAsync"
```

---

### Task 5: Add CancellationToken to NetworkDiagnostics Global Timeout

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Services/NetworkDiagnostics.cs`

- [ ] **Step 1: Add global 5-minute timeout wrapper to RunAllAsync**

The method already accepts `CancellationToken ct = default` at line 20. Wrap the body with a 5-minute linked timeout. Replace lines 22-118 with:

```csharp
    public static async Task<NetworkDiagResult> RunAllAsync(
        string apiBaseUrl, bool verbose = false, CancellationToken ct = default)
    {
        using var globalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        globalCts.CancelAfter(TimeSpan.FromMinutes(5));
        var gct = globalCts.Token;

        var result = new NetworkDiagResult();

        var tasks = new List<Task>
        {
            Task.Run(() => result.RouteTable = GetRouteTable(), gct),
            Task.Run(() => result.Adapters = GetAdapterDetails(), gct),
            Task.Run(() => result.VpnInterfaces = DetectVpnInterfaces(), gct),
            Task.Run(async () =>
            {
                var (down, up, latency) = await MeasureInternetSpeedAsync(apiBaseUrl, gct);
                result.DownloadMbps = down;
                result.UploadMbps = up;
                result.InternetLatencyMs = latency;
            }, gct),
            Task.Run(async () => result.CloudEndpointLatency = await MeasureCloudEndpointLatencyAsync(gct), gct),
            Task.Run(async () => result.DnsResolutionMs = await MeasureDnsResolutionAsync(gct), gct),
            Task.Run(() => result.HostsFileEntryCount = CountHostsFileEntries(), gct),
            Task.Run(() => result.NtpConfigured = CheckNtpConfigured(), gct),
            Task.Run(() => result.WpadEnabled = CheckWpadEnabled(), gct),
            Task.Run(() =>
            {
                result.LlmnrEnabled = CheckLlmnrEnabled();
                result.NetbiosEnabled = CheckNetbiosEnabled();
            }, gct),
            Task.Run(() => result.ListeningPortCount = CountListeningPorts(), gct),
            Task.Run(() => result.DisconnectedWithIpCount = CountDisconnectedWithIp(), gct),
            Task.Run(() => result.NicTeamingDetected = DetectNicTeaming(), gct),
        };

        try { await Task.WhenAll(tasks); }
        catch { /* individual results will be null/0 */ }
```

The rest of the method body stays exactly the same (gateway latency, link latency, traceroute, ARP, bandwidth, jitter/loss), but change every `ct` reference in those sections to `gct`:

- Line 61: `var gwResult = await PingHostAsync(gw, gct);`
- Line 71: `result.LinkLatency = await MeasureLinkLatencyAsync(result.RouteTable, result.GatewayIp, gct);`
- Line 80: `result.Traceroute = await RunTracerouteAsync(apiHost, gct);`
- Line 98: `result.InternalLatency = await MeasureInternalLatencyAsync(arpHosts, gct);`

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build KryossAgent/src/KryossAgent/KryossAgent.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add KryossAgent/src/KryossAgent/Services/NetworkDiagnostics.cs
git commit -m "feat(agent): add 5-min global timeout to NetworkDiagnostics.RunAllAsync"
```

---

### Task 6: Rewrite ServiceWorker — 5 Parallel Loops

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Services/ServiceWorker.cs`

This is the core rewrite. Replace the entire file content.

- [ ] **Step 1: Replace ServiceWorker.cs with parallel loop architecture**

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using KryossAgent.Config;
using KryossAgent.Models;
using Microsoft.Extensions.Hosting;

namespace KryossAgent.Services;

public class ServiceWorker : BackgroundService
{
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly ConcurrentQueue<AgentError> _errorQueue = new();
    private volatile bool _forceScanRequested;
    private DateTime? _lastComplianceScan;
    private PassiveListener? _passiveListener;

    // Per-loop status (read by HeartbeatLoop to build LoopStatus snapshot)
    private readonly ConcurrentDictionary<string, LoopStatusDto> _loopStatus = new();

    private const int MaxErrorQueueSize = 100;
    private const int MaxErrorsPerHeartbeat = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ApplyStagedUpdate();
        AgentLogger.Initialize();
        AgentLogger.Log("SERVICE", $"Kryoss Agent v{GetVersion()} started as Windows Service");

        var initialConfig = AgentConfig.Load();
        if (initialConfig.EnablePassiveDiscovery)
        {
            try
            {
                _passiveListener = new PassiveListener(verbose: false);
                _passiveListener.Start();
                AgentLogger.Log("SERVICE", "Passive discovery started (NetBIOS/mDNS/SSDP)");
            }
            catch (Exception ex)
            {
                AgentLogger.Error("SERVICE", $"Passive discovery failed to start: {ex.Message}");
            }
        }

        try
        {
            await Task.WhenAll(
                SelfUpdateLoop(stoppingToken),
                HeartbeatLoop(stoppingToken),
                ComplianceLoop(stoppingToken),
                SnmpLoop(stoppingToken),
                NetworkScanLoop(stoppingToken)
            );
        }
        finally
        {
            _passiveListener?.Dispose();
            AgentLogger.Log("SERVICE", "Shutting down gracefully");
            AgentLogger.Shutdown();
        }
    }

    // ── Loop 1: Self-Update (6h interval, 5min timeout) ──────────────────

    private async Task SelfUpdateLoop(CancellationToken ct)
    {
        SetLoopStatus("update", "idle");
        // No initial delay — update first before any work
        while (!ct.IsCancellationRequested)
        {
            SetLoopStatus("update", "running");
            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(5));

                var config = AgentConfig.Load();
                if (config.IsEnrolled)
                {
                    var updated = await SelfUpdater.CheckAndUpdateAsync(config, verbose: false);
                    if (updated) return;
                }

                SetLoopStatus("update", "idle", durationMs: (int)sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                EnqueueError("update", "Self-update timed out after 5 min", isTimeout: true);
                SetLoopStatus("update", "error", lastError: "timeout");
            }
            catch (Exception ex)
            {
                EnqueueError("update", ex.Message);
                SetLoopStatus("update", "error", lastError: ex.Message);
            }

            await WaitAsync(TimeSpan.FromHours(6), ct);
        }
    }

    // ── Loop 2: Heartbeat (15min interval, 30s timeout) ──────────────────

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        SetLoopStatus("heartbeat", "idle");
        await WaitAsync(TimeSpan.FromSeconds(5), ct); // staggered start

        while (!ct.IsCancellationRequested)
        {
            SetLoopStatus("heartbeat", "running");
            try
            {
                var config = AgentConfig.Load();
                if (!config.IsEnrolled)
                {
                    AgentLogger.Log("HEARTBEAT", "Not enrolled — skipping");
                    await WaitAsync(TimeSpan.FromSeconds(60), ct);
                    continue;
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                using var client = new ApiClient(config);
                var heartbeat = new HeartbeatPayload
                {
                    AgentId = config.AgentId,
                    Version = GetVersion(),
                    UptimeSeconds = (long)_uptime.Elapsed.TotalSeconds,
                    LastScanAt = _lastComplianceScan,
                    Mode = "service",
                    Errors = DrainErrors(),
                    LoopStatus = new Dictionary<string, LoopStatusDto>(_loopStatus),
                };

                var response = await client.SendHeartbeatAsync(heartbeat);
                var errorCount = heartbeat.Errors?.Count ?? 0;
                AgentLogger.Log("HEARTBEAT", $"sent — ack={response?.Ack}, errors_reported={errorCount}");

                if (response?.NewMachineSecret is not null)
                    AgentLogger.Log("HEARTBEAT", "Received machine keys — upgraded to auth v2");

                if (response?.ForceScan == true)
                {
                    AgentLogger.Log("HEARTBEAT", "Portal requested immediate compliance scan");
                    _forceScanRequested = true;
                }

                if (response?.PendingTasks is { Count: > 0 })
                {
                    AgentLogger.Log("SERVICE", $"{response.PendingTasks.Count} pending remediation task(s)");
                    await RemediationExecutor.ExecuteTasksAsync(client, response.PendingTasks, verbose: false);
                }

                SetLoopStatus("heartbeat", "idle");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                AgentLogger.Error("HEARTBEAT", "Timed out after 30s");
                SetLoopStatus("heartbeat", "error", lastError: "timeout");
            }
            catch (Exception ex)
            {
                // Don't enqueue heartbeat errors to error queue (that's us)
                AgentLogger.Error("HEARTBEAT", ex.Message);
                SetLoopStatus("heartbeat", "error", lastError: ex.Message);
            }

            await WaitAsync(TimeSpan.FromMinutes(15), ct);
        }
    }

    // ── Loop 3: Compliance (configurable interval, 30min timeout) ────────

    private async Task ComplianceLoop(CancellationToken ct)
    {
        SetLoopStatus("compliance", "idle");
        await WaitAsync(TimeSpan.FromSeconds(30), ct); // staggered start

        while (!ct.IsCancellationRequested)
        {
            var config = AgentConfig.Load();
            var complianceInterval = TimeSpan.FromHours(config.ComplianceIntervalHours);

            if (!config.IsEnrolled)
            {
                AgentLogger.Log("COMPLIANCE", "Not enrolled — waiting 60s");
                await WaitAsync(TimeSpan.FromSeconds(60), ct);
                continue;
            }

            var forceNow = _forceScanRequested;
            if (forceNow) _forceScanRequested = false;

            if (!forceNow && _lastComplianceScan is not null
                && DateTime.UtcNow - _lastComplianceScan < complianceInterval)
            {
                var remaining = _lastComplianceScan.Value + complianceInterval - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    await WaitAsync(remaining < TimeSpan.FromMinutes(1) ? remaining : TimeSpan.FromMinutes(1), ct);
                    continue;
                }
            }

            SetLoopStatus("compliance", "running");
            var sw = Stopwatch.StartNew();
            AgentLogger.Log("COMPLIANCE", $"Starting scan{(forceNow ? " (FORCED from portal)" : "")}");

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(30));

                var scanResult = await ScanCycle.RunComplianceScanAsync(config, silent: true, verbose: false, ct: cts.Token);
                if (scanResult.Success && scanResult.Payload is not null)
                {
                    using var apiClient = new ApiClient(config);
                    var response = await ScanCycle.UploadPayloadAsync(apiClient, scanResult.Payload, silent: true, ct: cts.Token);
                    if (response is not null)
                    {
                        AgentLogger.Log("COMPLIANCE",
                            $"OK — {Environment.MachineName} | {response.Score}% {response.Grade} | P:{response.PassCount} W:{response.WarnCount} F:{response.FailCount}");
                        await ScanCycle.RunAdHygieneAsync(apiClient, scanResult.HardwareInfo!, silent: true, verbose: false, ct: cts.Token);
                    }
                    _lastComplianceScan = DateTime.UtcNow;
                }
                else
                {
                    AgentLogger.Error("COMPLIANCE", $"Scan failed: {scanResult.Error}");
                    EnqueueError("compliance", scanResult.Error ?? "unknown");
                }

                SetLoopStatus("compliance", "idle", durationMs: (int)sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                EnqueueError("compliance", $"Timed out after 30 min", isTimeout: true);
                AgentLogger.Error("COMPLIANCE", "Timed out after 30 min");
                SetLoopStatus("compliance", "error", durationMs: (int)sw.ElapsedMilliseconds, lastError: "timeout");
                _lastComplianceScan = DateTime.UtcNow - complianceInterval + TimeSpan.FromMinutes(30);
            }
            catch (Exception ex)
            {
                EnqueueError("compliance", ex.Message);
                AgentLogger.Error("COMPLIANCE", ex.Message);
                SetLoopStatus("compliance", "error", durationMs: (int)sw.ElapsedMilliseconds, lastError: ex.Message);
                _lastComplianceScan = DateTime.UtcNow - complianceInterval + TimeSpan.FromMinutes(30);
            }

            await WaitAsync(complianceInterval, ct);
        }
    }

    // ── Loop 4: SNMP (configurable interval, 15min timeout) ──────────────

    private async Task SnmpLoop(CancellationToken ct)
    {
        SetLoopStatus("snmp", "idle");
        await WaitAsync(TimeSpan.FromSeconds(60), ct); // staggered start

        while (!ct.IsCancellationRequested)
        {
            var config = AgentConfig.Load();
            var snmpInterval = TimeSpan.FromMinutes(config.ScanIntervalMinutes);

            if (!config.IsEnrolled)
            {
                await WaitAsync(TimeSpan.FromSeconds(60), ct);
                continue;
            }

            SetLoopStatus("snmp", "running");
            var sw = Stopwatch.StartNew();
            AgentLogger.Log("SNMP", $"Starting SNMP scan at {DateTime.UtcNow:HH:mm:ss} UTC");

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(15));

                NetworkDiagResult? netDiag = null;
                try { netDiag = await NetworkDiagnostics.RunAllAsync(config.ApiUrl, verbose: false, cts.Token); }
                catch (OperationCanceledException) { AgentLogger.Error("SNMP", "NetworkDiag timed out"); }
                catch (Exception ex) { AgentLogger.Error("SNMP", $"NetworkDiag failed: {ex.Message}"); }

                var passiveIps = _passiveListener?.DrainDiscoveredIps();
                if (passiveIps is { Count: > 0 })
                    AgentLogger.Log("SNMP", $"Passive discovery contributed {passiveIps.Count} IPs");

                using var apiClient = new ApiClient(config);
                await ScanCycle.RunSnmpScanAsync(apiClient, netDiag, silent: true, verbose: false, extraTargets: passiveIps, ct: cts.Token);

                SetLoopStatus("snmp", "idle", durationMs: (int)sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                EnqueueError("snmp", "SNMP scan timed out after 15 min", isTimeout: true);
                AgentLogger.Error("SNMP", "Timed out after 15 min");
                SetLoopStatus("snmp", "error", durationMs: (int)sw.ElapsedMilliseconds, lastError: "timeout");
            }
            catch (Exception ex)
            {
                EnqueueError("snmp", ex.Message);
                AgentLogger.Error("SNMP", ex.Message);
                SetLoopStatus("snmp", "error", durationMs: (int)sw.ElapsedMilliseconds, lastError: ex.Message);
            }

            await WaitAsync(snmpInterval, ct);
        }
    }

    // ── Loop 5: Network Scan (configurable interval, 20min timeout) ──────

    private async Task NetworkScanLoop(CancellationToken ct)
    {
        SetLoopStatus("network", "idle");
        await WaitAsync(TimeSpan.FromSeconds(90), ct); // staggered start

        while (!ct.IsCancellationRequested)
        {
            var config = AgentConfig.Load();

            if (!config.IsEnrolled || !config.EnableNetworkScan)
            {
                await WaitAsync(TimeSpan.FromMinutes(5), ct);
                continue;
            }

            var networkInterval = TimeSpan.FromHours(config.NetworkScanIntervalHours);

            SetLoopStatus("network", "running");
            var sw = Stopwatch.StartNew();
            AgentLogger.Log("NETWORK", $"Starting network scan at {DateTime.UtcNow:HH:mm:ss} UTC");

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(20));

                using var apiClient = new ApiClient(config);
                await ScanCycle.RunNetworkScanAsync(apiClient, silent: true, verbose: false, ct: cts.Token);

                SetLoopStatus("network", "idle", durationMs: (int)sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                EnqueueError("network", "Network scan timed out after 20 min", isTimeout: true);
                AgentLogger.Error("NETWORK", "Timed out after 20 min");
                SetLoopStatus("network", "error", durationMs: (int)sw.ElapsedMilliseconds, lastError: "timeout");
            }
            catch (Exception ex)
            {
                EnqueueError("network", ex.Message);
                AgentLogger.Error("NETWORK", ex.Message);
                SetLoopStatus("network", "error", durationMs: (int)sw.ElapsedMilliseconds, lastError: ex.Message);
            }

            await WaitAsync(networkInterval, ct);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void ApplyStagedUpdate()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? typeof(ServiceWorker).Assembly.Location;
            var dir = Path.GetDirectoryName(exePath)!;
            var stagedPath = Path.Combine(dir, "KryossAgent.update.exe");
            if (File.Exists(stagedPath))
            {
                Console.WriteLine($"[SERVICE] Staged update found at {stagedPath} — applying");
                File.Move(stagedPath, exePath, overwrite: true);
                Console.WriteLine("[SERVICE] Update applied. Restarting...");
                Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SERVICE] Failed to apply staged update: {ex.Message}");
        }
    }

    private void EnqueueError(string phase, string message, bool isTimeout = false, string? target = null)
    {
        // Cap queue size — discard oldest when full
        while (_errorQueue.Count >= MaxErrorQueueSize)
            _errorQueue.TryDequeue(out _);

        _errorQueue.Enqueue(new AgentError
        {
            Phase = phase,
            Message = message.Length > 500 ? message[..500] : message,
            Timestamp = DateTime.UtcNow,
            Target = target,
            IsTimeout = isTimeout,
        });
    }

    private List<AgentErrorDto>? DrainErrors()
    {
        if (_errorQueue.IsEmpty) return null;

        var errors = new List<AgentErrorDto>();
        var count = 0;
        while (count < MaxErrorsPerHeartbeat && _errorQueue.TryDequeue(out var err))
        {
            errors.Add(new AgentErrorDto
            {
                Phase = err.Phase,
                Message = err.Message,
                Timestamp = err.Timestamp,
                Target = err.Target,
                IsTimeout = err.IsTimeout,
            });
            count++;
        }
        return errors.Count > 0 ? errors : null;
    }

    private void SetLoopStatus(string loop, string state, int? durationMs = null, string? lastError = null)
    {
        _loopStatus.AddOrUpdate(loop,
            _ => new LoopStatusDto
            {
                State = state,
                LastRunAt = state == "running" ? DateTime.UtcNow : null,
                LastDurationMs = durationMs,
                LastError = lastError,
            },
            (_, existing) =>
            {
                existing.State = state;
                if (state == "running") existing.LastRunAt = DateTime.UtcNow;
                if (durationMs.HasValue) existing.LastDurationMs = durationMs;
                if (lastError is not null) existing.LastError = lastError;
                else if (state == "idle") existing.LastError = null;
                return existing;
            });
    }

    private static string GetVersion() =>
        typeof(ServiceWorker).Assembly.GetName().Version?.ToString(3) ?? "2.8.0";

    private static async Task WaitAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build KryossAgent/src/KryossAgent/KryossAgent.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add KryossAgent/src/KryossAgent/Services/ServiceWorker.cs
git commit -m "feat(agent): rewrite ServiceWorker with 5 parallel loops (heartbeat never blocked by scans)"
```

---

### Task 7: Version Bump Agent → 2.8.0

**Files:**
- Modify: `KryossAgent/src/KryossAgent/KryossAgent.csproj`

- [ ] **Step 1: Bump version in csproj**

Change line 52:

```xml
<!-- OLD: -->
<Version>2.7.0</Version>
<!-- NEW: -->
<Version>2.8.0</Version>
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build KryossAgent/src/KryossAgent/KryossAgent.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add KryossAgent/src/KryossAgent/KryossAgent.csproj
git commit -m "chore(agent): bump version to 2.8.0"
```

---

### Task 8: SQL Migration — Machine Loop Status Columns

**Files:**
- Create: `KryossApi/sql/076_agent_loop_status.sql`

- [ ] **Step 1: Create migration file**

```sql
-- 076_agent_loop_status.sql
-- Adds agent loop status tracking columns to machines table.
-- Supports ServiceWorker v3 parallel loops architecture.
-- Safe to re-run (idempotent via IF NOT EXISTS).

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'loop_status_json')
BEGIN
    ALTER TABLE machines ADD loop_status_json NVARCHAR(MAX) NULL;
    PRINT 'Added machines.loop_status_json';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'last_error_at')
BEGIN
    ALTER TABLE machines ADD last_error_at DATETIME2 NULL;
    PRINT 'Added machines.last_error_at';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'last_error_phase')
BEGIN
    ALTER TABLE machines ADD last_error_phase NVARCHAR(50) NULL;
    PRINT 'Added machines.last_error_phase';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'last_error_msg')
BEGIN
    ALTER TABLE machines ADD last_error_msg NVARCHAR(500) NULL;
    PRINT 'Added machines.last_error_msg';
END

PRINT '076_agent_loop_status.sql complete';
```

- [ ] **Step 2: Commit**

```bash
git add KryossApi/sql/076_agent_loop_status.sql
git commit -m "feat(api): add migration 076 — machine loop status columns"
```

---

### Task 9: API — Machine Entity + HeartbeatFunction Changes

**Files:**
- Modify: `KryossApi/src/KryossApi/Data/Entities/Machine.cs`
- Modify: `KryossApi/src/KryossApi/Functions/Agent/HeartbeatFunction.cs`

- [ ] **Step 1: Add 4 new columns to Machine entity**

In `Machine.cs`, add after the `AgentUptimeSeconds` property (after line 67):

```csharp
    // v2.8.0: Loop status from parallel ServiceWorker
    public string? LoopStatusJson { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public string? LastErrorPhase { get; set; }
    public string? LastErrorMsg { get; set; }
```

- [ ] **Step 2: Update HeartbeatFunction to persist errors + loop status**

In `HeartbeatFunction.cs`, add after line 58 (`machine.AgentUptimeSeconds = body?.AgentUptimeSeconds;`... actually after the `if (!string.IsNullOrEmpty(body?.Version))` block around line 61):

```csharp
        // Persist loop status from v2.8.0+ agents
        if (body?.LoopStatus is { Count: > 0 })
            machine.LoopStatusJson = JsonSerializer.Serialize(body.LoopStatus);

        // Persist agent errors to actlog
        if (body?.Errors is { Count: > 0 })
        {
            var actlog = req.FunctionContext.InstanceServices.GetRequiredService<IActlogService>();
            foreach (var err in body.Errors.Take(20))
            {
                try
                {
                    await actlog.LogAsync(
                        severity: err.IsTimeout ? "WARN" : "ERROR",
                        module: "agent",
                        action: err.Phase,
                        message: err.Message,
                        entityType: "machine",
                        entityId: machine.Id.ToString());
                }
                catch { /* actlog must never break heartbeat */ }
            }

            var lastErr = body.Errors[^1];
            machine.LastErrorAt = lastErr.Timestamp;
            machine.LastErrorPhase = lastErr.Phase;
            machine.LastErrorMsg = lastErr.Message?.Length > 500 ? lastErr.Message[..500] : lastErr.Message;
        }
```

- [ ] **Step 3: Add HeartbeatRequest model extensions for new fields**

In `HeartbeatFunction.cs`, add to the `HeartbeatRequest` class at the bottom (after line 164):

```csharp
    [System.Text.Json.Serialization.JsonPropertyName("errors")]
    public List<AgentErrorEntry>? Errors { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("loopStatus")]
    public Dictionary<string, LoopStatusEntry>? LoopStatus { get; set; }
```

And add these inner classes after the `HeartbeatRequest` class:

```csharp
public class AgentErrorEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("phase")]
    public string Phase { get; set; } = null!;
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("target")]
    public string? Target { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("isTimeout")]
    public bool IsTimeout { get; set; }
}

public class LoopStatusEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("lastRunAt")]
    public DateTime? LastRunAt { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("lastDurationMs")]
    public int? LastDurationMs { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("lastError")]
    public string? LastError { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("state")]
    public string? State { get; set; }
}
```

- [ ] **Step 4: Add using for IActlogService at top of HeartbeatFunction.cs**

Verify `using KryossApi.Services;` is present. If not, add it.

- [ ] **Step 5: Verify it compiles**

Run: `dotnet build KryossApi/src/KryossApi/KryossApi.csproj`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add KryossApi/src/KryossApi/Data/Entities/Machine.cs KryossApi/src/KryossApi/Functions/Agent/HeartbeatFunction.cs
git commit -m "feat(api): persist agent errors to actlog + loop status on machines table"
```

---

### Task 10: Version Bump API → 1.30.0

**Files:**
- Modify: `KryossApi/src/KryossApi/KryossApi.csproj`

- [ ] **Step 1: Bump version in csproj**

Change line 6:

```xml
<!-- OLD: -->
<Version>1.29.0</Version>
<!-- NEW: -->
<Version>1.30.0</Version>
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build KryossApi/src/KryossApi/KryossApi.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/KryossApi.csproj
git commit -m "chore(api): bump version to 1.30.0"
```

---

### Task 11: Update CLAUDE.md + ROADMAP.md

**Files:**
- Modify: `CLAUDE.md` (root)
- Modify: `KryossAgent/CLAUDE.md`
- Modify: `docs/superpowers/plans/ROADMAP.md`

- [ ] **Step 1: Update root CLAUDE.md version table**

Change Agent version from `2.7.0` to `2.8.0` and API from `1.29.0` to `1.30.0`.

- [ ] **Step 2: Add decision log entry in root CLAUDE.md**

Add to the decision table:

```
| 2026-04-27 | A-SVC3: ServiceWorker v3 parallel loops | 5 independent Task loops (heartbeat/compliance/SNMP/network/self-update). Heartbeat never blocked by scans. Per-device parallelism in SNMP (sem 10) + network (sem 20). ConcurrentQueue<AgentError> → heartbeat drains + reports to server actlog. AgentLogger writes to C:\ProgramData\Kryoss\Logs\. Timeouts: heartbeat 30s, compliance 30min, SNMP 15min, network 20min, update 5min. SQL: `076_agent_loop_status.sql`. Agent 2.8.0, API 1.30.0. |
```

- [ ] **Step 3: Update KryossAgent/CLAUDE.md — ServiceWorker section**

Replace the "Service mode (ServiceWorker loop)" section with:

```markdown
**Service mode (ServiceWorker v3 — parallel loops):**
1. Apply staged update (if present)
2. Initialize AgentLogger (C:\ProgramData\Kryoss\Logs\agent-YYYY-MM-DD.log)
3. Start passive listener (if enabled)
4. Launch 5 independent parallel Tasks via Task.WhenAll:
   - **SelfUpdateLoop** (6h, 5min timeout) — version check + download + restart
   - **HeartbeatLoop** (15min, 30s timeout) — POST /v1/heartbeat, drain error queue, process config/keys/forceScan/tasks
   - **ComplianceLoop** (configurable 24h, 30min timeout) — full scan via ScanCycle + upload + AD hygiene
   - **SnmpLoop** (configurable 4h, 15min timeout) — NetworkDiag + SNMP per-device parallel (sem 10, 60s/device)
   - **NetworkScanLoop** (configurable, 20min timeout) — discovery + ports per-target parallel (sem 20, 2min/target)
5. Each loop: own CancellationToken with timeout, own ApiClient, reloads config each cycle
6. Errors → ConcurrentQueue<AgentError> → HeartbeatLoop drains (max 20/heartbeat) → server actlog
7. Loop status snapshot sent in heartbeat → machines.loop_status_json
```

- [ ] **Step 4: Update ROADMAP.md — add shipped entry**

In the "Shipped 2026-04-27" section, append:

```
- Agent v2.8.0 + API v1.30.0: ServiceWorker v3 parallel loops (heartbeat/compliance/SNMP/network/self-update as independent Tasks). Heartbeat never blocked by scans. ConcurrentQueue<AgentError> + AgentLogger file logging + server actlog persistence. Per-phase timeouts. Migration 076.
```

Update the codebase inventory table to reflect Agent 2.8.0 and API 1.30.0.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md KryossAgent/CLAUDE.md docs/superpowers/plans/ROADMAP.md
git commit -m "docs: update CLAUDE.md + ROADMAP.md for ServiceWorker v3 (Agent 2.8.0, API 1.30.0)"
```

---

### Task 12: Final Build Verification

- [ ] **Step 1: Build agent**

Run: `dotnet build KryossAgent/src/KryossAgent/KryossAgent.csproj`
Expected: Build succeeded, 0 warnings related to new code

- [ ] **Step 2: Build API**

Run: `dotnet build KryossApi/src/KryossApi/KryossApi.csproj`
Expected: Build succeeded

- [ ] **Step 3: Verify no regressions — grep for old ServiceWorker patterns**

Run: `grep -r "Console.WriteLine.*SERVICE\|Console.Error.WriteLine.*SERVICE" KryossAgent/src/KryossAgent/Services/ServiceWorker.cs`
Expected: Only the `ApplyStagedUpdate` method should use Console directly (pre-logger init). All other logging should use `AgentLogger.Log`/`AgentLogger.Error`.

- [ ] **Step 4: Verify CancellationToken flows**

Run: `grep -c "CancellationToken" KryossAgent/src/KryossAgent/Services/ScanCycle.cs`
Expected: At least 6 matches (one per method)
