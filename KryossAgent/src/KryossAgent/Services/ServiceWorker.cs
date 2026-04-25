using System.Diagnostics;
using KryossAgent.Config;
using KryossAgent.Models;
using Microsoft.Extensions.Hosting;

namespace KryossAgent.Services;

public class ServiceWorker : BackgroundService
{
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private DateTime? _lastComplianceScan;
    private DateTime? _lastSnmpScan;
    private PassiveListener? _passiveListener;
    private DateTime? _lastUpdateCheck;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[SERVICE] Kryoss Agent v2.2.1 started as Windows Service");

        try
        {
            _passiveListener = new PassiveListener(verbose: false);
            _passiveListener.Start();
            Console.WriteLine("[SERVICE] Passive discovery started (NetBIOS/mDNS/SSDP)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SERVICE] Passive discovery failed to start: {ex.Message}");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = AgentConfig.Load();

            if (!config.IsEnrolled)
            {
                Console.Error.WriteLine("[SERVICE] Not enrolled — waiting 60s to retry");
                await WaitAsync(TimeSpan.FromSeconds(60), stoppingToken);
                continue;
            }

            var now = DateTime.UtcNow;
            var complianceInterval = TimeSpan.FromHours(config.ComplianceIntervalHours);
            var snmpInterval = TimeSpan.FromMinutes(config.ScanIntervalMinutes);


            if (_lastComplianceScan is null || now - _lastComplianceScan >= complianceInterval)
            {
                Console.WriteLine($"[SERVICE] Starting compliance scan at {now:HH:mm:ss} UTC");
                try
                {
                    var scanResult = await ScanCycle.RunComplianceScanAsync(config, silent: true, verbose: false);
                    if (scanResult.Success && scanResult.Payload is not null)
                    {
                        using var apiClient = new ApiClient(config);
                        var response = await ScanCycle.UploadPayloadAsync(apiClient, scanResult.Payload, silent: true);
                        if (response is not null)
                        {
                            Console.WriteLine(
                                $"RESULT: OK | {Environment.MachineName} | {response.Score}% {response.Grade} | P:{response.PassCount} W:{response.WarnCount} F:{response.FailCount}");
                            await ScanCycle.RunAdHygieneAsync(apiClient, scanResult.HardwareInfo!, silent: true, verbose: false);
                        }
                        _lastComplianceScan = DateTime.UtcNow;
                    }
                    else
                    {
                        Console.Error.WriteLine($"[SERVICE] Scan failed: {scanResult.Error}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[SERVICE] Compliance scan error: {ex.Message}");
                }
            }

            if (_lastSnmpScan is null || now - _lastSnmpScan >= snmpInterval)
            {
                Console.WriteLine($"[SERVICE] Starting SNMP scan at {DateTime.UtcNow:HH:mm:ss} UTC");
                try
                {
                    config = AgentConfig.Load();
                    if (config.IsEnrolled)
                    {
                        NetworkDiagResult? netDiag = null;
                        try { netDiag = await NetworkDiagnostics.RunAllAsync(config.ApiUrl, verbose: false); }
                        catch { }

                        var passiveIps = _passiveListener?.DrainDiscoveredIps();
                        if (passiveIps is { Count: > 0 })
                            Console.WriteLine($"[SERVICE] Passive discovery contributed {passiveIps.Count} IPs");

                        using var apiClient = new ApiClient(config);
                        await ScanCycle.RunSnmpScanAsync(apiClient, netDiag, silent: true, verbose: false, extraTargets: passiveIps);
                        _lastSnmpScan = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[SERVICE] SNMP scan error: {ex.Message}");
                }
            }

            await SendHeartbeatAsync(config);

            // Self-update check every 6h
            if (_lastUpdateCheck is null || now - _lastUpdateCheck >= TimeSpan.FromHours(6))
            {
                try
                {
                    var updated = await SelfUpdater.CheckAndUpdateAsync(config, verbose: false);
                    if (updated) return; // Service will be restarted by update script
                    _lastUpdateCheck = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[SERVICE] Update check error: {ex.Message}");
                    _lastUpdateCheck = DateTime.UtcNow;
                }
            }

            var nextCompliance = _lastComplianceScan.HasValue
                ? _lastComplianceScan.Value + complianceInterval - DateTime.UtcNow
                : TimeSpan.Zero;
            var nextSnmp = _lastSnmpScan.HasValue
                ? _lastSnmpScan.Value + snmpInterval - DateTime.UtcNow
                : TimeSpan.Zero;

            var sleepTime = TimeSpan.FromMinutes(15);
            if (nextCompliance > TimeSpan.Zero && nextCompliance < sleepTime)
                sleepTime = nextCompliance;
            if (nextSnmp > TimeSpan.Zero && nextSnmp < sleepTime)
                sleepTime = nextSnmp;

            if (sleepTime < TimeSpan.FromMinutes(1))
                sleepTime = TimeSpan.FromMinutes(1);

            Console.WriteLine($"[SERVICE] Next wake in {sleepTime.TotalMinutes:0.#} min");
            await WaitAsync(sleepTime, stoppingToken);
        }

        _passiveListener?.Dispose();
        Console.WriteLine("[SERVICE] Shutting down gracefully");
    }

    private async Task SendHeartbeatAsync(AgentConfig config)
    {
        try
        {
            using var client = new ApiClient(config);
            var heartbeat = new HeartbeatPayload
            {
                AgentId = config.AgentId,
                Version = typeof(ServiceWorker).Assembly.GetName().Version?.ToString(3) ?? "2.0.0",
                UptimeSeconds = (long)_uptime.Elapsed.TotalSeconds,
                LastScanAt = _lastComplianceScan,
                Mode = "service",
            };
            var response = await client.SendHeartbeatAsync(heartbeat);
            Console.WriteLine($"[HEARTBEAT] sent — ack={response?.Ack}");

            if (response?.NewMachineSecret is not null)
                Console.WriteLine("[HEARTBEAT] Received machine keys — upgraded to auth v2");

            if (response?.PendingTasks is { Count: > 0 })
            {
                Console.WriteLine($"[SERVICE] {response.PendingTasks.Count} pending remediation task(s)");
                await RemediationExecutor.ExecuteTasksAsync(client, response.PendingTasks, verbose: false);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HEARTBEAT] failed: {ex.Message}");
        }
    }

    private static async Task WaitAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { }
    }
}
