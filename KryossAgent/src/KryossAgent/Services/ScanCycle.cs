using System.Diagnostics;
using KryossAgent.Config;
using KryossAgent.Engines;
using KryossAgent.Models;

namespace KryossAgent.Services;

public static class ScanCycle
{
    public static async Task<ComplianceScanResult> RunComplianceScanAsync(AgentConfig config, bool silent, bool verbose)
    {
        var sw = Stopwatch.StartNew();

        List<ControlDef> checks;
        using var apiClient = new ApiClient(config);

        try
        {
            if (!silent) Console.WriteLine("  Downloading control definitions...");
            var controlsResponse = await apiClient.GetControlsAsync(config.AssessmentId!.Value);
            checks = controlsResponse?.Checks ?? [];
            if (verbose) Console.WriteLine($"  {checks.Count} controls loaded (v{controlsResponse?.Version})");
        }
        catch (Exception ex)
        {
            return ComplianceScanResult.Failed($"Cannot reach API: {ex.Message}");
        }

        if (checks.Count == 0)
        {
            var osName = PlatformDetector.DetectPlatform()?.Os ?? "Unknown OS";
            return ComplianceScanResult.Skipped($"No controls for this platform ({osName})");
        }

        if (!silent) Console.WriteLine("  Scanning local machine...");
        if (!silent) Console.Write("    Collecting system info...");
        var platformInfo = PlatformDetector.DetectPlatform();
        var hardwareInfo = PlatformDetector.DetectHardware();
        List<SoftwareItem> softwareList;
        try { softwareList = SoftwareInventory.Enumerate(); }
        catch { softwareList = []; }
        if (!silent) Console.WriteLine(" done");

        if (!silent) Console.Write("    Scanning for threats...");
        List<ThreatFinding> threats;
        try { threats = ThreatDetector.ScanAll(); }
        catch { threats = []; }
        if (!silent) Console.WriteLine($" {threats.Count} found");

        if (!silent) Console.Write("    Running security checks...");

        var securityPolicyEngine = new SecurityPolicyEngine();
        ICheckEngine[] engines =
        [
            new RegistryEngine(),
            securityPolicyEngine,
            new AuditpolEngine(),
            new FirewallEngine(),
            new ServiceEngine(),
            new NetAccountCompatEngine(securityPolicyEngine),
            new NativeCommandEngine(),
            new EventLogEngine(),
            new CertStoreEngine(),
            new BitLockerEngine(),
            new TpmEngine(),
            new DcEngine()
        ];

        var allResults = new List<CheckResult>();
        var controlsByType = checks
            .GroupBy(c => c.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ControlDef>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        var logLock = new object();
        void LogLine(string msg)
        {
            if (!verbose) return;
            lock (logLock) { Console.WriteLine(msg); Console.Out.Flush(); }
        }

        var activeEngines = engines.Where(e => controlsByType.ContainsKey(e.Type)).ToArray();
        var totalEngines = activeEngines.Length;
        var completedEngines = 0;

        var engineTasks = activeEngines
            .Select(engine => Task.Run(() =>
            {
                var controls = controlsByType[engine.Type];
                var engineSw = Stopwatch.StartNew();
                LogLine($"  [{engine.Type}] starting ({controls.Count} checks)");
                try
                {
                    var engineResults = engine.Execute(controls);
                    engineSw.Stop();
                    LogLine($"  [{engine.Type}] done in {engineSw.ElapsedMilliseconds} ms");
                    var done = Interlocked.Increment(ref completedEngines);
                    if (!silent && !verbose)
                        lock (logLock)
                        {
                            Console.Write($"\r    Running security checks... {done}/{totalEngines} engines   ");
                            Console.Out.Flush();
                        }
                    return engineResults;
                }
                catch (Exception ex)
                {
                    LogLine($"  [{engine.Type}] FAILED: {ex.Message}");
                    Interlocked.Increment(ref completedEngines);
                    return new List<CheckResult>();
                }
            }))
            .ToArray();

        var engineResults = await Task.WhenAll(engineTasks);
        foreach (var results in engineResults)
            allResults.AddRange(results);

        sw.Stop();
        var durationMs = (int)sw.ElapsedMilliseconds;
        if (!silent)
            Console.WriteLine($"\r    Security checks complete — {allResults.Count} checks in {durationMs / 1000.0:F1}s          ");

        hardwareInfo.Threats = threats;

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

        if (!silent) Console.Write("    Enumerating local administrators...");
        List<LocalAdminItem> localAdmins;
        try { localAdmins = PlatformDetector.EnumerateLocalAdmins(); }
        catch { localAdmins = []; }
        if (!silent) Console.WriteLine($" {localAdmins.Count} found");

        var payload = new AssessmentPayload
        {
            AgentId = config.AgentId,
            AgentVersion = typeof(ScanCycle).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            Timestamp = DateTime.UtcNow,
            DurationMs = durationMs,
            Platform = platformInfo,
            Hardware = hardwareInfo,
            Software = softwareList,
            Results = allResults,
            NetworkDiag = networkDiag,
            LocalAdmins = localAdmins.Count > 0 ? localAdmins : null,
        };

        return new ComplianceScanResult
        {
            Success = true,
            Payload = payload,
            HardwareInfo = hardwareInfo,
            NetworkDiag = networkDiag,
            CheckCount = allResults.Count,
        };
    }

    public static async Task RunSnmpScanAsync(ApiClient apiClient, NetworkDiagResult? networkDiag, bool silent, bool verbose, IReadOnlyCollection<string>? extraTargets = null)
    {
        try
        {
            var snmpCreds = await apiClient.GetSnmpCredentialsAsync();
            if (snmpCreds == null) return;

            if (!silent) Console.Write("    Running SNMP infrastructure scan...");
            var snmpTargets = snmpCreds.Targets ?? new List<string>();

            if (snmpTargets.Count == 0)
            {
                var discovered = new HashSet<string>();
                if (networkDiag?.GatewayIp != null)
                    discovered.Add(networkDiag.GatewayIp);
                if (networkDiag?.RouteTable != null)
                    foreach (var r in networkDiag.RouteTable)
                        if (!string.IsNullOrEmpty(r.NextHop) && r.NextHop != "0.0.0.0"
                            && System.Net.IPAddress.TryParse(r.NextHop, out _))
                            discovered.Add(r.NextHop);
                if (networkDiag?.InternalLatency != null)
                    foreach (var p in networkDiag.InternalLatency.Where(p => p.Reachable))
                        discovered.Add(p.Host);

                var subnetIps = await SnmpScanner.DiscoverSubnetAsync(snmpCreds, verbose);
                foreach (var ip in subnetIps) discovered.Add(ip);
                if (extraTargets is { Count: > 0 })
                    foreach (var ip in extraTargets) discovered.Add(ip);
                snmpTargets = discovered.ToList();
            }

            if (snmpTargets.Count == 0)
            {
                if (!silent) Console.WriteLine(" skipped (no targets)");
                return;
            }

            var snmpResult = await SnmpScanner.ScanAsync(snmpCreds, snmpTargets, verbose);

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
                        if (dev.DeviceType == "unknown") dev.DeviceType = oui.Value.Category;
                    }
                }
            }

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
                    Ip = ip, MacAddress = normalMac,
                    Vendor = oui?.Vendor, DeviceType = oui?.Category ?? "unknown",
                });
            }

            var needDns = arpDevices.Concat(snmpResult.Devices.Where(d => d.SysName == null)).ToList();
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
            Console.WriteLine($"  [SNMP] {snmpResult.Devices.Count} devices found ({arpEntries.Count} ARP)");

            var devicesWithSysOid = snmpResult.Devices.Where(d => !string.IsNullOrEmpty(d.SysObjectId)).ToList();
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
                            var profile = profiles.Profiles.FirstOrDefault(p => dev.SysObjectId!.StartsWith(p.OidPrefix));
                            if (profile == null) continue;
                            var vendorData = await SnmpScanner.ScanVendorOidsAsync(dev.Ip, profile.Oids, snmpCreds, verbose);
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

            if (snmpResult.Devices.Count > 0)
            {
                await apiClient.SubmitSnmpResultsAsync(snmpResult);
                Console.WriteLine($"  [SNMP] Uploaded {snmpResult.Devices.Count} device(s)");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [WARN] SNMP scan failed: {ex.Message}");
        }
    }

    public static async Task RunNetworkScanAsync(ApiClient apiClient, bool silent, bool verbose)
    {
        try
        {
            if (!silent) Console.Write("  Discovering network targets...");
            var targets = await TargetDiscovery.DiscoverAsync(Array.Empty<string>());
            if (targets.Count == 0)
            {
                if (!silent) Console.WriteLine(" no targets found");
                return;
            }
            if (!silent) Console.WriteLine($" {targets.Count} target(s)");

            if (!silent) Console.Write("  Scanning ports...");
            var portPayload = new PortBulkPayload();
            var sem = new SemaphoreSlim(10);
            await Task.WhenAll(targets.Select(async t =>
            {
                await sem.WaitAsync();
                try
                {
                    var ports = await PortScanner.ScanTcpAsync(t.Address, timeoutMs: 1000);
                    if (ports.Count > 0)
                        lock (portPayload.Machines)
                            portPayload.Machines.Add(new PortPayload
                            {
                                MachineHostname = t.Hostname,
                                Ports = ports.Select(p => new PortEntry
                                {
                                    Port = p.Port, Protocol = p.Protocol,
                                    Status = p.Status, Service = p.Service,
                                    Banner = p.Banner, ServiceVersion = p.ServiceVersion,
                                }).ToList()
                            });
                }
                finally { sem.Release(); }
            }));

            var totalPorts = portPayload.Machines.Sum(h => h.Ports.Count);
            if (!silent) Console.WriteLine($" {totalPorts} open port(s) on {portPayload.Machines.Count} host(s)");

            if (portPayload.Machines.Count > 0)
                await apiClient.SubmitPortResultsBulkAsync(portPayload);

            var hw = PlatformDetector.DetectHardware();
            if (hw.ProductType == 2)
                await RunAdHygieneAsync(apiClient, hw, silent, verbose);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [WARN] Network scan failed: {ex.Message}");
        }
    }

    public static async Task RunAdHygieneAsync(ApiClient apiClient, HardwareInfo hardwareInfo, bool silent, bool verbose)
    {
        if (hardwareInfo.ProductType != 2) return;

        if (!silent) Console.Write("  Running AD hygiene audit (DC detected)...");
        try
        {
            TargetDiscovery.DiscoverAd(null);
            var report = TargetDiscovery.LastHygieneReport;
            if (report is null) { if (!silent) Console.WriteLine(" no findings"); return; }

            var findings = new List<HygieneFinding>();
            void Add(IEnumerable<TargetDiscovery.AdHygieneItem> items, string objType, string? statusOverride = null)
            {
                foreach (var i in items)
                    findings.Add(new HygieneFinding
                    {
                        Name = i.Name, ObjectType = objType,
                        Status = statusOverride ?? i.Status,
                        DaysInactive = i.DaysInactive, Detail = i.Detail
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

            if (findings.Count == 0) { if (!silent) Console.WriteLine(" no findings"); return; }

            var payload = new HygienePayload
            {
                ScannedBy = Environment.MachineName,
                TotalMachines = report.StaleMachines.Count + report.DormantMachines.Count
                    + TargetDiscovery.LastDiscoveredActiveCount,
                TotalUsers = report.StaleUsers.Count + report.DormantUsers.Count
                    + report.DisabledUsers.Count + report.NeverExpirePasswords.Count
                    + TargetDiscovery.LastDiscoveredActiveUserCount,
                Findings = findings
            };

            await apiClient.SubmitHygieneAsync(payload);
            if (!silent) Console.WriteLine($" done ({findings.Count} findings)");
            else Console.WriteLine($"RESULT: HYGIENE | {Environment.MachineName} | {findings.Count} findings");
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"  [WARN] AD hygiene failed: {ex.Message}");
            if (!silent) Console.WriteLine(" skipped");
        }
    }

    public static async Task<ResultsResponse?> UploadPayloadAsync(
        ApiClient apiClient, AssessmentPayload payload, bool silent)
    {
        if (!silent) Console.Write("  Uploading results...");
        var response = await apiClient.SubmitResultsAsync(payload);
        if (!silent) Console.Write("\r                          \r");

        if (response is not null && !silent)
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

        return response;
    }
}

public class ComplianceScanResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool WasSkipped { get; set; }
    public AssessmentPayload? Payload { get; set; }
    public HardwareInfo? HardwareInfo { get; set; }
    public NetworkDiagResult? NetworkDiag { get; set; }
    public int CheckCount { get; set; }

    public static ComplianceScanResult Failed(string error) => new() { Error = error };
    public static ComplianceScanResult Skipped(string reason) => new() { WasSkipped = true, Error = reason };
}
