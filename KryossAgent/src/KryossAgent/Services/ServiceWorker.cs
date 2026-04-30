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
    private volatile List<string>? _priorityServices;
    private DateTime? _lastComplianceScan = LoadLastComplianceScan();
    private PassiveListener? _passiveListener;

    // Per-loop status (read by HeartbeatLoop to build LoopStatus snapshot)
    private readonly ConcurrentDictionary<string, LoopStatusDto> _loopStatus = new();

    // AU-01/02/03: Version handshake + interruptible update loop
    private volatile bool _updateAvailable;
    private volatile bool _updateMandatory;
    private volatile bool _heartbeatHasRun;
    private CancellationTokenSource? _selfUpdateWakeCts;
    private readonly object _wakeCtsLock = new();
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    // Remediation task queue: heartbeat enqueues, RemediationLoop dequeues
    private readonly ConcurrentQueue<PendingRemediationTask> _remediationQueue = new();
    private readonly ConcurrentDictionary<long, byte> _remediationSeen = new();

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
                RemediationLoop(stoppingToken),
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

    // ── Loop 1: Self-Update (6h interval, interruptible by heartbeat dev-mode signal) ──

    private async Task SelfUpdateLoop(CancellationToken ct)
    {
        SetLoopStatus("update", "idle");
        // First iteration runs immediately (no wait)
        var firstRun = true;

        while (!ct.IsCancellationRequested)
        {
            if (!firstRun)
            {
                // Interruptible wait: heartbeat can cancel _selfUpdateWakeCts to wake us
                CancellationTokenSource wakeCts;
                lock (_wakeCtsLock)
                {
                    _selfUpdateWakeCts?.Dispose();
                    _selfUpdateWakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    wakeCts = _selfUpdateWakeCts;
                }

                try { await Task.Delay(TimeSpan.FromHours(6), wakeCts.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    AgentLogger.Log("UPDATE", "Woken early by heartbeat dev-mode signal");
                }
            }
            firstRun = false;

            // AU-03: skip if heartbeat says no update needed (but run if heartbeat hasn't run yet)
            if (_heartbeatHasRun && !_updateAvailable && !_updateMandatory)
            {
                AgentLogger.Log("UPDATE", "Skipped — heartbeat reports no update available");
                SetLoopStatus("update", "idle");
                continue;
            }

            if (!await _updateLock.WaitAsync(0, ct))
            {
                AgentLogger.Log("UPDATE", "Skipped — another update run in progress");
                continue;
            }

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
            finally
            {
                _updateLock.Release();
            }
        }
    }

    private void WakeSelfUpdateLoop()
    {
        lock (_wakeCtsLock)
        {
            try { _selfUpdateWakeCts?.Cancel(); }
            catch (ObjectDisposedException) { }
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

                // Auto-heal protected + priority services
                try
                {
                    var healErrors = ServiceHealer.HealProtectedServices(_priorityServices);
                    foreach (var he in healErrors)
                    {
                        while (_errorQueue.Count >= MaxErrorQueueSize) _errorQueue.TryDequeue(out _);
                        _errorQueue.Enqueue(he);
                    }
                }
                catch (Exception ex)
                {
                    AgentLogger.Error("HEARTBEAT", $"Service healer failed: {ex.Message}");
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
                if (response is not null)
                    AgentLogger.Log("HEARTBEAT", $"ok — ack={response.Ack}, errors_reported={errorCount}");
                else
                    AgentLogger.Log("HEARTBEAT", "FAILED — null response (check agent log for HTTP status)");

                if (response?.NewMachineSecret is not null)
                    AgentLogger.Log("HEARTBEAT", "Received machine keys — upgraded to auth v2");

                // AU-01: Parse version handshake flags
                _heartbeatHasRun = true;
                if (response is not null)
                {
                    var currentVersion = GetVersion();
                    _updateAvailable = response.LatestAgentVersion is not null
                        && Version.TryParse(response.LatestAgentVersion, out var latest)
                        && Version.TryParse(currentVersion, out var current)
                        && latest > current;
                    _updateMandatory = response.MinAgentVersion is not null
                        && Version.TryParse(response.MinAgentVersion, out var minVer)
                        && Version.TryParse(currentVersion, out var cur2)
                        && cur2 < minVer;

                    if (_updateAvailable)
                        AgentLogger.Log("HEARTBEAT", $"Update available: {currentVersion} → {response.LatestAgentVersion}" +
                            (_updateMandatory ? " (MANDATORY)" : ""));

                    // AU-02: Wake updater immediately in dev mode
                    if ((_updateAvailable || _updateMandatory) && response.ModeDev)
                    {
                        AgentLogger.Log("HEARTBEAT", "Dev mode — signaling immediate update");
                        WakeSelfUpdateLoop();
                    }
                }

                if (response?.ForceScan == true)
                {
                    AgentLogger.Log("HEARTBEAT", "Portal requested immediate compliance scan");
                    _forceScanRequested = true;
                }

                if (response?.Config?.PriorityServices is not null)
                    _priorityServices = response.Config.PriorityServices;

                if (response?.PendingTasks is { Count: > 0 })
                {
                    var inlineMode = Environment.GetEnvironmentVariable("KRYOSS_INLINE_REMEDIATION") == "true";
                    if (inlineMode)
                    {
                        AgentLogger.Log("SERVICE", $"{response.PendingTasks.Count} pending task(s) — executing inline (legacy)");
                        await RemediationExecutor.ExecuteTasksAsync(client, response.PendingTasks, verbose: false);
                    }
                    else
                    {
                        var enqueued = 0;
                        foreach (var task in response.PendingTasks)
                        {
                            if (_remediationSeen.TryAdd(task.Id, 0))
                            {
                                _remediationQueue.Enqueue(task);
                                enqueued++;
                            }
                        }
                        if (enqueued > 0)
                            AgentLogger.Log("HEARTBEAT", $"Enqueued {enqueued} remediation task(s)");
                    }
                }

                // Submit service inventory (only when changed)
                try
                {
                    var (services, changed) = ServiceInventory.Collect();
                    if (changed && services.Count > 0)
                    {
                        await client.SubmitServiceInventoryAsync(services);
                        AgentLogger.Log("HEARTBEAT", $"Service inventory submitted ({services.Count} services)");
                    }
                }
                catch (Exception ex)
                {
                    AgentLogger.Error("HEARTBEAT", $"Service inventory submit failed: {ex.Message}");
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

                    // WUC-03: submit available updates (empty list clears stale pending on server)
                    try
                    {
                        var wuUpdates = scanResult.AvailableUpdates ?? [];
                        await apiClient.SubmitAvailableUpdatesAsync(wuUpdates);
                        if (wuUpdates.Count > 0)
                            AgentLogger.Log("COMPLIANCE", $"Available updates submitted ({wuUpdates.Count})");
                    }
                    catch (Exception ex)
                    {
                        AgentLogger.Error("COMPLIANCE", $"Available updates upload failed: {ex.Message}");
                    }

                    SaveLastComplianceScan(DateTime.UtcNow);
                }
                else
                {
                    AgentLogger.Error("COMPLIANCE", $"Scan failed: {scanResult.Error}");
                    EnqueueError("compliance", scanResult.Error ?? "unknown");
                    SaveLastComplianceScan(DateTime.UtcNow - complianceInterval + TimeSpan.FromMinutes(30));
                }

                SetLoopStatus("compliance", "idle", durationMs: (int)sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                EnqueueError("compliance", $"Timed out after 30 min", isTimeout: true);
                AgentLogger.Error("COMPLIANCE", "Timed out after 30 min");
                SetLoopStatus("compliance", "error", durationMs: (int)sw.ElapsedMilliseconds, lastError: "timeout");
                SaveLastComplianceScan(DateTime.UtcNow - complianceInterval + TimeSpan.FromMinutes(30));
            }
            catch (Exception ex)
            {
                EnqueueError("compliance", ex.Message);
                AgentLogger.Error("COMPLIANCE", ex.Message);
                SetLoopStatus("compliance", "error", durationMs: (int)sw.ElapsedMilliseconds, lastError: ex.Message);
                SaveLastComplianceScan(DateTime.UtcNow - complianceInterval + TimeSpan.FromMinutes(30));
            }

            // Short sleep so forceScan and config changes are picked up quickly;
            // _lastComplianceScan check at top of loop handles the real interval.
            await WaitAsync(TimeSpan.FromMinutes(1), ct);
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

    // ── Loop 6: Remediation (queue-driven, per-task timeout) ───────────

    private async Task RemediationLoop(CancellationToken ct)
    {
        SetLoopStatus("remediation", "idle");
        await WaitAsync(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            if (_remediationQueue.IsEmpty)
            {
                await WaitAsync(TimeSpan.FromSeconds(5), ct);
                continue;
            }

            var config = AgentConfig.Load();
            if (!config.IsEnrolled)
            {
                await WaitAsync(TimeSpan.FromSeconds(30), ct);
                continue;
            }

            var batch = new List<PendingRemediationTask>();
            while (_remediationQueue.TryDequeue(out var task))
                batch.Add(task);

            if (batch.Count == 0) continue;

            SetLoopStatus("remediation", "running");
            var sw = Stopwatch.StartNew();
            AgentLogger.Log("REMEDIATE", $"Processing {batch.Count} task(s)");

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromHours(2));

                using var apiClient = new ApiClient(config);
                await RemediationExecutor.ExecuteTasksAsync(apiClient, batch, verbose: false, ct: cts.Token);

                SetLoopStatus("remediation", "idle", durationMs: (int)sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                EnqueueError("remediation", "Remediation timed out after 2h", isTimeout: true);
                AgentLogger.Error("REMEDIATE", "Timed out after 2h");
                SetLoopStatus("remediation", "error", durationMs: (int)sw.ElapsedMilliseconds, lastError: "timeout");
            }
            catch (Exception ex)
            {
                EnqueueError("remediation", ex.Message);
                AgentLogger.Error("REMEDIATE", ex.Message);
                SetLoopStatus("remediation", "error", durationMs: (int)sw.ElapsedMilliseconds, lastError: ex.Message);
            }
            finally
            {
                foreach (var task in batch)
                    _remediationSeen.TryRemove(task.Id, out _);
            }

            await WaitAsync(TimeSpan.FromSeconds(2), ct);
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

    private static readonly string LastScanFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Kryoss", "last_compliance.txt");

    private static DateTime? LoadLastComplianceScan()
    {
        try { return File.Exists(LastScanFile) && DateTime.TryParse(File.ReadAllText(LastScanFile).Trim(), out var dt) ? dt : null; }
        catch { return null; }
    }

    private void SaveLastComplianceScan(DateTime dt)
    {
        _lastComplianceScan = dt;
        try
        {
            var dir = Path.GetDirectoryName(LastScanFile)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(LastScanFile, dt.ToString("O"));
        }
        catch { }
    }

    private static string GetVersion() =>
        typeof(ServiceWorker).Assembly.GetName().Version?.ToString(3) ?? "2.8.0";

    private static async Task WaitAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { }
    }
}
