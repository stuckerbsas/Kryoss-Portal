# ServiceWorker v3 — Parallel Loops Architecture

**Date:** 2026-04-27
**Component:** Agent (KryossAgent) + API (HeartbeatFunction)
**Version:** Agent 2.8.0, API 1.30.0
**Status:** Draft

---

## Problem

ServiceWorker v2 runs heartbeat, compliance, SNMP, network scan, and self-update **sequentially** in a single loop. If any phase hangs (e.g., NetworkDiagnostics on a DC with 200+ ARP hosts, or SNMP on an unresponsive device), heartbeat stops — the agent appears dead in the portal even though the service is running. No local logging exists; errors are lost to `Console.Error`.

**Evidence:** RIV-DC-01 (DC, v2.6.0, service mode, 7+ days uptime) — heartbeat dead for 17h while `last_seen_at` shows scans still ran.

---

## Design

### Architecture: 5 Independent Task Loops

```
ExecuteAsync(CancellationToken stoppingToken):
  ├── ApplyStagedUpdate()                          // once, at startup
  ├── AgentLogger.Initialize()                     // once, at startup
  │
  ├── Task.WhenAll(
  │     SelfUpdateLoop(stoppingToken),             // 6h interval, 5 min timeout
  │     HeartbeatLoop(stoppingToken),              // 15 min interval, 30s timeout
  │     ComplianceLoop(stoppingToken),             // 24h interval (configurable), 30 min timeout
  │     SnmpLoop(stoppingToken),                   // 4h interval (configurable), 15 min timeout
  │     NetworkScanLoop(stoppingToken),            // configurable interval, 20 min timeout
  │   )
```

Each loop:
1. Is an independent `async Task` with its own `while (!ct.IsCancellationRequested)` loop
2. Creates its own `ApiClient` per cycle (disposable)
3. Reloads `AgentConfig` at the start of each cycle (picks up portal config changes)
4. Wraps work in `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter(timeout)`
5. On error: catches, logs to file via `AgentLogger`, pushes to shared `ConcurrentQueue<AgentError>`
6. Sleeps its own interval, never blocked by other loops

### Loop Execution Order (Startup)

On first iteration after service start, loops fire in this order (staggered to avoid thundering herd):

| Order | Loop | Initial Delay | Rationale |
|-------|------|--------------|-----------|
| 1 | SelfUpdateLoop | 0s | Update first before any work |
| 2 | HeartbeatLoop | 5s | Establish contact, get config |
| 3 | ComplianceLoop | 30s | Wait for heartbeat to deliver config |
| 4 | SnmpLoop | 60s | After compliance uploads |
| 5 | NetworkScanLoop | 90s | Last priority, most network-intensive |

After first iteration, each loop runs on its own interval independently.

### Loop Details

#### 1. SelfUpdateLoop (6h, timeout 5min)

```
while not cancelled:
  wait initial delay (0s first time, 6h after)
  try:
    check latest version from blob storage
    if newer: download, stage, exit(0) for restart
  catch → log + error queue
  sleep 6h
```

Runs first so agent gets latest code before doing any work.

#### 2. HeartbeatLoop (15min, timeout 30s)

```
while not cancelled:
  wait initial delay (5s first time, 15min after)
  try:
    build HeartbeatPayload (including drained errors from queue)
    POST /v1/heartbeat
    process response: keys, config, forceScan, pendingTasks
    if forceScan → set shared flag
    if pendingTasks → execute remediation
    save config if changed
  catch → log locally (don't push to error queue — that's us)
  sleep 15min
```

**Critical:** HeartbeatLoop NEVER calls any scan method. It only sends heartbeat + processes response.

#### 3. ComplianceLoop (configurable, default 24h, timeout 30min)

```
while not cancelled:
  wait initial delay (30s first time, interval after)
  reload config
  if forceScan flag set → clear flag, skip interval check
  if not enrolled → skip, sleep 60s
  try with timeout(30min):
    ScanCycle.RunComplianceScanAsync()
    ScanCycle.UploadPayloadAsync()
    ScanCycle.RunAdHygieneAsync()    // only on DCs
  catch OperationCanceledException → log timeout
  catch → log + error queue
  update lastComplianceScan
  sleep config.ComplianceIntervalHours
```

#### 4. SnmpLoop (configurable, default 4h, timeout 15min)

```
while not cancelled:
  wait initial delay (60s first time, interval after)
  reload config
  if not enrolled → skip
  try with timeout(15min):
    collect NetworkDiagnostics (with per-device parallelism)
    drain passive discovery IPs
    discover SNMP targets
    scan devices IN PARALLEL (semaphore 10, per-device timeout 60s)
    upload results in batches of 50
  catch OperationCanceledException → log timeout
  catch → log + error queue
  sleep config.ScanIntervalMinutes
```

#### 5. NetworkScanLoop (configurable, disabled by default, timeout 20min)

```
while not cancelled:
  wait initial delay (90s first time, interval after)
  reload config
  if not config.EnableNetworkScan → sleep 5min, continue
  try with timeout(20min):
    discover targets (AD/ARP/subnet)
    scan targets IN PARALLEL (semaphore 20, per-target timeout 2min):
      port scan + banner grab
      WMI probe (if Windows)
    upload port results
    if DC → run AD hygiene + DC health
  catch OperationCanceledException → log timeout
  catch → log + error queue
  sleep config.NetworkScanIntervalHours
```

---

### Per-Device Parallelism (Level 2)

#### SNMP Scan — Per Device

```csharp
var semaphore = new SemaphoreSlim(maxParallelSnmp); // 10
var tasks = targets.Select(async target =>
{
    await semaphore.WaitAsync(loopCt);
    try
    {
        using var deviceCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
        deviceCts.CancelAfter(TimeSpan.FromSeconds(60));
        var result = await SnmpScanner.ScanDeviceAsync(target, creds, deviceCts.Token);
        // ... enrich, collect
    }
    catch (OperationCanceledException) { errorQueue.Enqueue(timeout error for target); }
    catch (Exception ex) { errorQueue.Enqueue(error for target); }
    finally { semaphore.Release(); }
});
await Task.WhenAll(tasks);
// batch upload all results
```

#### Network Scan — Per Target

```csharp
var semaphore = new SemaphoreSlim(maxParallelNetwork); // 20
var tasks = targets.Select(async target =>
{
    await semaphore.WaitAsync(loopCt);
    try
    {
        using var targetCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
        targetCts.CancelAfter(TimeSpan.FromMinutes(2));
        var ports = await PortScanner.ScanTcpAsync(target.Address, timeoutMs: 1000, ct: targetCts.Token);
        // ... banner, WMI probe
    }
    catch (OperationCanceledException) { errorQueue.Enqueue(timeout error for target); }
    catch (Exception ex) { errorQueue.Enqueue(error for target); }
    finally { semaphore.Release(); }
});
await Task.WhenAll(tasks);
// batch upload
```

#### NetworkDiagnostics — Per Host (Internal Latency)

Already has `SemaphoreSlim(20)` in `MeasureInternalLatencyAsync`. Add CancellationToken propagation + per-host timeout (30s).

---

### Shared State

```csharp
// In ServiceWorker — shared across all loops
private readonly ConcurrentQueue<AgentError> _errorQueue = new();
private volatile bool _forceScanRequested;
private readonly Stopwatch _uptime = Stopwatch.StartNew();
private DateTime? _lastComplianceScan;
private PassiveListener? _passiveListener;
```

All other state is local to each loop. `AgentConfig` is reloaded from registry at each cycle start (not shared instance).

#### AgentError Model

```csharp
public class AgentError
{
    public string Phase { get; set; }      // "compliance", "snmp", "network", "heartbeat", "update"
    public string Message { get; set; }    // ex.Message (truncated to 500 chars)
    public DateTime Timestamp { get; set; }
    public string? Target { get; set; }    // hostname/IP for per-device errors, null for loop-level
    public bool IsTimeout { get; set; }    // true if OperationCanceledException
}
```

---

### HeartbeatPayload Extension (Agent → Server)

Add `errors` field to existing HeartbeatPayload:

```csharp
// HeartbeatPayload.cs — new field
[JsonPropertyName("errors")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public List<AgentErrorDto>? Errors { get; set; }

// Also add loop status for portal visibility
[JsonPropertyName("loopStatus")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public Dictionary<string, LoopStatusDto>? LoopStatus { get; set; }
```

```csharp
public class AgentErrorDto
{
    [JsonPropertyName("phase")] public string Phase { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; }
    [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
    [JsonPropertyName("target")] public string? Target { get; set; }
    [JsonPropertyName("isTimeout")] public bool IsTimeout { get; set; }
}

public class LoopStatusDto
{
    [JsonPropertyName("lastRunAt")] public DateTime? LastRunAt { get; set; }
    [JsonPropertyName("lastDurationMs")] public int? LastDurationMs { get; set; }
    [JsonPropertyName("lastError")] public string? LastError { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } // "idle", "running", "error"
}
```

HeartbeatLoop drains `_errorQueue` (max 20 per heartbeat to cap payload size) and includes `loopStatus` snapshot for portal visibility.

### HeartbeatFunction Changes (Server)

Server persists agent errors to `actlog` table:

```
For each error in heartbeat.errors:
  INSERT actlog (severity='WARN', module='agent', action=error.phase,
                 entity_type='machine', entity_id=machine.id,
                 message=error.message, actor_ip=request.ip)
```

Also update `machines` table with new columns:

```sql
ALTER TABLE machines ADD
    loop_status_json NVARCHAR(MAX) NULL,   -- raw loopStatus from heartbeat
    last_error_at    DATETIME2 NULL,
    last_error_phase NVARCHAR(50) NULL,
    last_error_msg   NVARCHAR(500) NULL;
```

---

### File Logging — AgentLogger

New static class `Services/AgentLogger.cs`:

```
Location: C:\ProgramData\Kryoss\Logs\agent-YYYY-MM-DD.log
Format:   2026-04-27T15:30:42Z [COMPLIANCE] Scan completed — 630 checks, 12.3s
          2026-04-27T15:31:01Z [SNMP] ERROR device 10.0.1.1 timeout after 60s
          2026-04-27T15:45:00Z [HEARTBEAT] sent — ack=true, errors_reported=2
Rotation: Delete files older than 7 days (checked once at startup)
Thread safety: ConcurrentQueue<string> + dedicated writer task (flush every 1s or 100 lines)
Max file size: 10 MB — rotate to agent-YYYY-MM-DD.1.log (keep 2 per day max)
```

```csharp
public static class AgentLogger
{
    public static void Initialize();                    // create dir, start writer task, prune old files
    public static void Log(string phase, string msg);   // [phase] msg
    public static void Error(string phase, string msg); // [phase] ERROR msg
    public static void Shutdown();                      // flush + stop writer
}
```

All `Console.WriteLine`/`Console.Error.WriteLine` in ServiceWorker replaced with `AgentLogger.Log`/`AgentLogger.Error`. Console output kept for `--verbose` mode only (one-shot mode unchanged).

---

### Timeout Matrix

| Loop | Interval | Max Duration | Per-Device Timeout | Semaphore |
|------|----------|-------------|-------------------|-----------|
| SelfUpdate | 6h | 5 min | — | — |
| Heartbeat | 15 min | 30s | — | — |
| Compliance | 24h (config) | 30 min | — | — |
| SNMP | 4h (config) | 15 min | 60s/device | 10 |
| NetworkScan | config | 20 min | 2 min/target | 20 |
| NetworkDiag | (inside SNMP) | 5 min | 30s/host | 20 |

All timeouts use `CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)` + `CancelAfter()` so service stop cancels everything cleanly.

---

### Passive Discovery Integration

PassiveListener stays as-is (started once in ExecuteAsync). SnmpLoop drains discovered IPs via `_passiveListener?.DrainDiscoveredIps()` at the start of each SNMP cycle.

---

### What Changes

| File | Change |
|------|--------|
| `Services/ServiceWorker.cs` | **Rewrite** — 5 independent loop methods, shared error queue, staggered startup |
| `Services/AgentLogger.cs` | **New** — file logging with rotation |
| `Models/HeartbeatPayload.cs` | Add `Errors` + `LoopStatus` fields |
| `Models/AgentError.cs` | **New** — error + loop status DTOs |
| `Models/JsonContext.cs` | Add new types to source-gen context |
| `Services/NetworkDiagnostics.cs` | Propagate `CancellationToken` to all sub-methods, add global 5min timeout |
| `Services/SnmpScanner.cs` | Add per-device `CancellationToken` support to `ScanAsync` |
| `Services/PortScanner.cs` | Add `CancellationToken` parameter to `ScanTcpAsync` |
| `Services/ScanCycle.cs` | Add `CancellationToken` parameter to all `Run*Async` methods |
| `KryossAgent.csproj` | Version → 2.8.0 |
| **API side:** | |
| `Functions/Agent/HeartbeatFunction.cs` | Persist errors to actlog, save loop_status_json |
| `Data/Entities/Machine.cs` | Add 4 new columns |
| `sql/076_agent_loop_status.sql` | **New** — migration for machine columns |
| `KryossApi.csproj` | Version → 1.30.0 |

### What Does NOT Change

- `Program.cs` (one-shot mode) — untouched, keeps sequential flow
- `ScanCycle` methods — signatures gain `CancellationToken` parameter, internal logic unchanged
- `ApiClient` — unchanged
- `SelfUpdater` — unchanged
- `RemediationExecutor` — unchanged
- All engines — unchanged

---

### Backward Compatibility

- HeartbeatPayload `errors` and `loopStatus` are `[JsonIgnore(WhenWritingNull)]` — pre-v2.8 agents send null, server ignores.
- Server HeartbeatFunction: if `body.Errors` is null, skip actlog writes. No breaking change.
- New machine columns are nullable — existing rows unaffected.

---

### Risk Assessment

| Risk | Mitigation |
|------|------------|
| Race condition on `AgentConfig.Save()` | Only HeartbeatLoop saves config (receives portal updates). Other loops only read. |
| ApiClient not thread-safe | Each loop creates its own `using var apiClient = new ApiClient(config)`. No sharing. |
| Memory from error queue growth | Heartbeat drains max 20 per cycle. Queue capped at 100 (oldest dropped). |
| Stale config in long-running scan | Acceptable — scan uses config at cycle start. Next cycle picks up changes. |
| Multiple loops writing to same log file | AgentLogger uses ConcurrentQueue + single writer task. Thread-safe by design. |
| Service stop with active scans | LinkedTokenSource propagates cancellation. Each loop catches OperationCanceledException gracefully. |
