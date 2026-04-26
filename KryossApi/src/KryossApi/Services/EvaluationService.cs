using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface IEvaluationService
{
    Task<AssessmentRun> EvaluateAsync(Guid machineId, Guid organizationId, AgentPayload payload);
}

/// <summary>
/// Server-side evaluation: compare agent raw values against expected values in control_defs.check_json.
/// Agent is "dumb" — only reports what it found. This service determines PASS/FAIL/WARN.
/// </summary>
public class EvaluationService : IEvaluationService
{
    private readonly KryossDbContext _db;
    private readonly IPlatformResolver _platformResolver;

    public EvaluationService(KryossDbContext db, IPlatformResolver platformResolver)
    {
        _db = db;
        _platformResolver = platformResolver;
    }

    public async Task<AssessmentRun> EvaluateAsync(Guid machineId, Guid organizationId, AgentPayload payload)
    {
        var run = new AssessmentRun
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            MachineId = machineId,
            AgentVersion = payload.AgentVersion,
            DurationMs = payload.DurationMs,
            StartedAt = payload.Timestamp,
            CompletedAt = DateTime.UtcNow,
            RawPayload = JsonSerializer.Serialize(payload)
        };

        // Load all active control definitions
        var controlIds = payload.Results.Select(r => r.Id).ToList();
        var controlDefs = await _db.ControlDefs
            .Where(c => controlIds.Contains(c.ControlId) && c.IsActive)
            .ToDictionaryAsync(c => c.ControlId);

        int passCount = 0, warnCount = 0, failCount = 0;
        int totalPoints = 0, earnedPoints = 0;
        var results = new List<ControlResult>();

        foreach (var agentResult in payload.Results)
        {
            if (!controlDefs.TryGetValue(agentResult.Id, out var controlDef))
                continue;

            var checkSpec = JsonSerializer.Deserialize<CheckSpec>(controlDef.CheckJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (checkSpec is null) continue;

            var (status, score, maxScore, finding) = Evaluate(agentResult, checkSpec, controlDef);

            results.Add(new ControlResult
            {
                RunId = run.Id,
                ControlDefId = controlDef.Id,
                Status = status,
                Score = (short)score,
                MaxScore = (short)maxScore,
                Finding = finding,
                ActualValue = agentResult.Value?.ToString()
            });

            totalPoints += maxScore;
            earnedPoints += score;

            switch (status)
            {
                case "pass": passCount++; break;
                case "warn": warnCount++; break;
                case "fail": failCount++; break;
            }
        }

        // ── Evaluate network_diag controls server-side ──
        if (payload.NetworkDiag is { } netDiag)
        {
            var netMachine = await _db.Machines.FindAsync(machineId);
            var platformId = netMachine?.PlatformId;
            if (platformId != null)
            {
                var netControls = await _db.ControlDefs
                    .Where(c => c.IsActive && c.Type == "network_diag")
                    .Where(c => _db.ControlPlatforms.Any(cp => cp.ControlDefId == c.Id && cp.PlatformId == platformId))
                    .ToListAsync();

                var netValues = ExtractNetworkDiagValues(netDiag, payload.Hardware);

                foreach (var ctrl in netControls)
                {
                    var spec = JsonSerializer.Deserialize<CheckSpec>(ctrl.CheckJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (spec is null) continue;

                    var (status, score, maxScore, finding) = EvaluateNetworkControl(spec, ctrl, netValues);

                    results.Add(new ControlResult
                    {
                        RunId = run.Id,
                        ControlDefId = ctrl.Id,
                        Status = status,
                        Score = (short)score,
                        MaxScore = (short)maxScore,
                        Finding = finding,
                        ActualValue = netValues.TryGetValue(spec.Field ?? "", out var actual) ? actual?.ToString() : null
                    });

                    totalPoints += maxScore;
                    earnedPoints += score;
                    switch (status)
                    {
                        case "pass": passCount++; break;
                        case "warn": warnCount++; break;
                        case "fail": failCount++; break;
                    }
                }
            }
        }

        run.PassCount = (short)passCount;
        run.WarnCount = (short)warnCount;
        run.FailCount = (short)failCount;
        run.TotalPoints = (short)totalPoints;
        run.EarnedPoints = (short)earnedPoints;
        run.GlobalScore = totalPoints > 0 ? Math.Round((decimal)earnedPoints / totalPoints * 100, 2) : 0;
        run.Grade = CalculateGrade(run.GlobalScore.Value);

        _db.AssessmentRuns.Add(run);
        _db.ControlResults.AddRange(results);

        // ── Compute per-framework scores ──
        // Load framework mappings for all evaluated controls
        var evaluatedControlDefIds = results.Select(r => r.ControlDefId).ToHashSet();
        var frameworkMappings = await _db.ControlFrameworks
            .Where(cf => evaluatedControlDefIds.Contains(cf.ControlDefId))
            .Select(cf => new { cf.ControlDefId, cf.FrameworkId })
            .ToListAsync();

        // Build a lookup: controlDefId → list of frameworkIds
        var controlToFrameworks = frameworkMappings
            .GroupBy(m => m.ControlDefId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.FrameworkId).ToList());

        // Accumulate per-framework stats
        var frameworkStats = new Dictionary<int, (int pass, int warn, int fail, int earned, int total)>();
        foreach (var cr in results)
        {
            if (!controlToFrameworks.TryGetValue(cr.ControlDefId, out var fwIds))
                continue;
            foreach (var fwId in fwIds)
            {
                var (p, w, f, e, t) = frameworkStats.GetValueOrDefault(fwId);
                switch (cr.Status)
                {
                    case "pass": p++; break;
                    case "warn": w++; break;
                    case "fail": f++; break;
                }
                frameworkStats[fwId] = (p, w, f, e + cr.Score, t + cr.MaxScore);
            }
        }

        foreach (var (fwId, (p, w, f, e, t)) in frameworkStats)
        {
            _db.RunFrameworkScores.Add(new RunFrameworkScore
            {
                RunId = run.Id,
                FrameworkId = fwId,
                Score = t > 0 ? Math.Round((decimal)e / t * 100, 2) : 0,
                PassCount = (short)p,
                WarnCount = (short)w,
                FailCount = (short)f
            });
        }

        await _db.SaveChangesAsync();

        // Update machine last_seen_at + OS drift detection
        var machine = await _db.Machines.FindAsync(machineId);
        if (machine is not null)
        {
            var prevOsName = machine.OsName;

            machine.LastSeenAt = DateTime.UtcNow;
            machine.AgentVersion = payload.AgentVersion;
            // OS
            machine.OsName = payload.Platform?.Os;
            machine.OsVersion = payload.Platform?.Version;
            machine.OsBuild = payload.Platform?.Build;
            // Hardware
            machine.CpuName = payload.Hardware?.Cpu;
            machine.CpuCores = payload.Hardware?.CpuCores;
            machine.RamGb = payload.Hardware?.RamGb;
            machine.DiskType = payload.Hardware?.DiskType;
            machine.DiskSizeGb = payload.Hardware?.DiskSizeGb;
            machine.DiskFreeGb = payload.Hardware?.DiskFreeGb;
            machine.Manufacturer = payload.Hardware?.Manufacturer;
            machine.Model = payload.Hardware?.Model;
            machine.SerialNumber = payload.Hardware?.SerialNumber;
            // Security
            machine.TpmPresent = payload.Hardware?.TpmPresent;
            machine.TpmVersion = payload.Hardware?.TpmVersion;
            machine.SecureBoot = payload.Hardware?.SecureBoot;
            machine.Bitlocker = payload.Hardware?.Bitlocker;
            // Network
            machine.IpAddress = payload.Hardware?.IpAddress;
            machine.MacAddress = payload.Hardware?.MacAddress;
            // Identity
            machine.DomainStatus = payload.Hardware?.DomainStatus;
            machine.DomainName = payload.Hardware?.DomainName;
            if (payload.Hardware?.ProductType is > 0)
                machine.ProductType = (short)payload.Hardware.ProductType;
            // Lifecycle
            machine.SystemAgeDays = payload.Hardware?.SystemAgeDays;
            machine.LastBootAt = payload.Hardware?.LastBootAt;
            // Local admins
            if (payload.LocalAdmins is { Count: > 0 })
                machine.LocalAdminsJson = System.Text.Json.JsonSerializer.Serialize(payload.LocalAdmins);

            if (!string.Equals(prevOsName, machine.OsName, StringComparison.Ordinal)
                || machine.PlatformId is null)
            {
                machine.PlatformId = await _platformResolver.ResolveIdAsync(
                    machine.OsName, machine.OsVersion, machine.OsBuild, machine.ProductType);
            }

            // Persist individual disk inventory
            if (payload.Hardware?.Disks is { Count: > 0 })
            {
                var existingDisks = await _db.MachineDisks
                    .Where(d => d.MachineId == machineId)
                    .ToDictionaryAsync(d => d.DriveLetter);

                foreach (var disk in payload.Hardware.Disks)
                {
                    if (string.IsNullOrEmpty(disk.DriveLetter)) continue;
                    var letter = disk.DriveLetter[..1].ToUpperInvariant();

                    if (existingDisks.TryGetValue(letter, out var existing))
                    {
                        existing.Label = disk.Label;
                        existing.DiskType = disk.DiskType;
                        existing.TotalGb = disk.TotalGb;
                        existing.FreeGb = disk.FreeGb;
                        existing.FileSystem = disk.FileSystem;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        _db.MachineDisks.Add(new Data.Entities.MachineDisk
                        {
                            MachineId = machineId,
                            DriveLetter = letter,
                            Label = disk.Label,
                            DiskType = disk.DiskType,
                            TotalGb = disk.TotalGb,
                            FreeGb = disk.FreeGb,
                            FileSystem = disk.FileSystem,
                            UpdatedAt = DateTime.UtcNow,
                        });
                    }
                }
            }

            // Persist threat detection findings
            if (payload.Hardware?.Threats is { Count: > 0 })
            {
                var oldThreats = await _db.MachineThreats.Where(t => t.MachineId == machineId).ToListAsync();
                _db.MachineThreats.RemoveRange(oldThreats);

                foreach (var threat in payload.Hardware.Threats)
                {
                    _db.MachineThreats.Add(new Data.Entities.MachineThreat
                    {
                        MachineId = machineId,
                        ThreatName = threat.ThreatName,
                        Category = threat.Category,
                        Severity = threat.Severity,
                        Vector = threat.Vector,
                        Detail = threat.Detail,
                        DetectedAt = DateTime.UtcNow,
                    });
                }
            }

            // Persist network diagnostics
            if (payload.NetworkDiag is { } nd)
            {
                var oldDiag = await _db.MachineNetworkDiags
                    .Include(d => d.LatencyPeers)
                    .Include(d => d.Routes)
                    .FirstOrDefaultAsync(d => d.MachineId == machineId && d.RunId == run.Id);

                if (oldDiag != null)
                {
                    _db.MachineNetworkLatencies.RemoveRange(oldDiag.LatencyPeers);
                    _db.MachineNetworkRoutes.RemoveRange(oldDiag.Routes);
                    _db.MachineNetworkDiags.Remove(oldDiag);
                }

                var diag = new Data.Entities.MachineNetworkDiag
                {
                    MachineId = machineId,
                    RunId = run.Id,
                    DownloadMbps = nd.DownloadMbps,
                    UploadMbps = nd.UploadMbps,
                    InternetLatencyMs = nd.InternetLatencyMs,
                    GatewayLatencyMs = nd.GatewayLatencyMs,
                    GatewayIp = nd.GatewayIp,
                    RouteCount = nd.RouteTable?.Count ?? 0,
                    VpnDetected = nd.VpnInterfaces is { Count: > 0 },
                    VpnAdapters = nd.VpnInterfaces is { Count: > 0 }
                        ? System.Text.Json.JsonSerializer.Serialize(nd.VpnInterfaces.Select(v => v.Name))
                        : null,
                    AdapterCount = nd.Adapters?.Count ?? 0,
                    WifiCount = nd.WifiCount,
                    VpnAdapterCount = nd.VpnCount,
                    EthCount = nd.EthCount,
                    BandwidthSendMbps = nd.Bandwidth?.SendRateMbps,
                    BandwidthRecvMbps = nd.Bandwidth?.RecvRateMbps,
                    DnsResolutionMs = nd.DnsResolutionMs,
                    CloudEndpointCount = nd.CloudEndpointLatency?.Count(e => e.Reachable),
                    CloudEndpointAvgMs = nd.CloudEndpointLatency?.Where(e => e.Reachable).Select(e => e.LatencyMs).DefaultIfEmpty(0).Average(),
                    TriggeredByIpChange = nd.TriggeredByIpChange,
                    RawData = System.Text.Json.JsonSerializer.Serialize(nd),
                    ScannedAt = DateTime.UtcNow,
                };

                if (nd.LinkLatency is { Count: > 0 })
                {
                    foreach (var peer in nd.LinkLatency)
                    {
                        diag.LatencyPeers.Add(new Data.Entities.MachineNetworkLatency
                        {
                            Host = peer.Host,
                            Subnet = "wan-link",
                            Reachable = peer.Reachable,
                            AvgMs = peer.AvgMs,
                            MinMs = (int)peer.MinMs,
                            MaxMs = (int)peer.MaxMs,
                            JitterMs = peer.JitterMs,
                            PacketLoss = (short)peer.PacketLoss,
                            TotalSent = (short)peer.TotalSent,
                        });
                    }
                }

                if (nd.InternalLatency is { Count: > 0 })
                {
                    foreach (var peer in nd.InternalLatency)
                    {
                        diag.LatencyPeers.Add(new Data.Entities.MachineNetworkLatency
                        {
                            Host = peer.Host,
                            Subnet = peer.Subnet,
                            Reachable = peer.Reachable,
                            AvgMs = peer.AvgMs,
                            MinMs = (int)peer.MinMs,
                            MaxMs = (int)peer.MaxMs,
                            JitterMs = peer.JitterMs,
                            PacketLoss = (short)peer.PacketLoss,
                            TotalSent = (short)peer.TotalSent,
                        });
                    }
                }

                if (nd.RouteTable is { Count: > 0 })
                {
                    foreach (var route in nd.RouteTable)
                    {
                        diag.Routes.Add(new Data.Entities.MachineNetworkRoute
                        {
                            Destination = route.Destination,
                            Mask = route.Mask,
                            NextHop = route.NextHop,
                            InterfaceIndex = route.InterfaceIndex,
                            Metric = route.Metric,
                            RouteType = (short)route.Type,
                            Protocol = (short)route.Protocol,
                        });
                    }
                }

                _db.MachineNetworkDiags.Add(diag);
            }

            await _db.SaveChangesAsync();
        }

        return run;
    }

    private static (string status, int score, int maxScore, string? finding) Evaluate(
        AgentCheckResult result, CheckSpec spec, ControlDef def)
    {
        int maxScore = def.Severity switch
        {
            "critical" => 10,
            "high" => 7,
            "medium" => 4,
            "low" => 2,
            _ => 4
        };

        // Collect-only controls (data-gathering commands like dsregcmd,
        // wevtutil gl, wbadmin get versions). No expected value = nothing
        // to compare. Record as "info" and grant full points so they
        // don't drag down the global score.
        if (spec.Expected is null && string.IsNullOrEmpty(spec.ExpectedStartType))
        {
            var hasData = result.Exists == true
                || !string.IsNullOrEmpty(result.Stdout)
                || result.Value is not null
                || result.ExitCode == 0;
            return hasData
                ? ("info", maxScore, maxScore, "Data collected")
                : ("warn", maxScore / 2, maxScore, "No data collected");
        }

        // Optional checks (e.g. backup services that may not be installed):
        // a missing service is not a failure, it's a "not applicable".
        if (spec.Optional == true && (result.Exists == false || result.Value is null))
        {
            return ("info", maxScore, maxScore, "Optional component not present");
        }

        // Service-type checks use StartType rather than Value for
        // comparison when ExpectedStartType is present.
        if (!string.IsNullOrEmpty(spec.ExpectedStartType))
        {
            if (result.Exists == false)
            {
                var mb = spec.MissingBehavior ?? "fail";
                return mb switch
                {
                    "pass" => ("pass", maxScore, maxScore, "Service not installed (expected)"),
                    "warn" => ("warn", maxScore / 2, maxScore, "Service not installed"),
                    _ => ("fail", 0, maxScore, "Service not installed (required)")
                };
            }

            var actualStart = result.StartType ?? "";
            var op2 = spec.Operator ?? "eq";
            bool svcPassed = op2 switch
            {
                "in" when spec.Expected is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array
                    => je.EnumerateArray().Any(v => string.Equals(v.GetString(), actualStart, StringComparison.OrdinalIgnoreCase)),
                _ => string.Equals(actualStart, spec.ExpectedStartType, StringComparison.OrdinalIgnoreCase)
            };

            return svcPassed
                ? ("pass", maxScore, maxScore, null)
                : ("fail", 0, maxScore, $"Expected StartType '{spec.ExpectedStartType}', got '{actualStart}'");
        }

        // Handle missing value (key doesn't exist)
        if (result.Exists == false || result.Value is null)
        {
            var missingBehavior = spec.MissingBehavior ?? "fail";
            return missingBehavior switch
            {
                "pass" => ("pass", maxScore, maxScore, "Key not present (expected)"),
                "warn" => ("warn", maxScore / 2, maxScore, "Key not present"),
                _ => ("fail", 0, maxScore, "Key not present (required)")
            };
        }

        // Compare actual vs expected
        var actual = result.Value?.ToString() ?? "";
        var expected = spec.Expected?.ToString() ?? "";
        var op = spec.Operator ?? "eq";

        bool passed = op switch
        {
            "eq" => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            "neq" => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            "gte" => decimal.TryParse(actual, out var a) && decimal.TryParse(expected, out var e) && a >= e,
            "lte" => decimal.TryParse(actual, out var a2) && decimal.TryParse(expected, out var e2) && a2 <= e2,
            "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "not_contains" => !actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "exists" => result.Exists == true,
            "not_exists" => result.Exists != true,
            "in" => spec.Expected is System.Text.Json.JsonElement je
                    && je.ValueKind == System.Text.Json.JsonValueKind.Array
                    && je.EnumerateArray().Any(v => string.Equals(v.GetString(), actual, StringComparison.OrdinalIgnoreCase)),
            _ => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
        };

        if (passed)
            return ("pass", maxScore, maxScore, null);

        return ("fail", 0, maxScore, $"Expected {op} '{expected}', got '{actual}'");
    }

    private static string CalculateGrade(decimal score) => score switch
    {
        >= 97 => "A+",
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 60 => "D",
        _ => "F"
    };

    private static Dictionary<string, object?> ExtractNetworkDiagValues(NetworkDiagDto net, HardwareInfo? hw = null)
    {
        var adapters = net.Adapters ?? [];
        var routes = net.RouteTable ?? [];
        var publicDns = new HashSet<string> { "8.8.8.8", "8.8.4.4", "1.1.1.1", "1.0.0.1", "9.9.9.9", "208.67.222.222", "208.67.220.220" };

        var vals = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Speed & latency (NET-001..004, 031..033) ──
            ["downloadMbps"] = net.DownloadMbps,
            ["uploadMbps"] = net.UploadMbps,
            ["internetLatencyMs"] = net.InternetLatencyMs,

            // ── VPN (NET-009) ──
            ["vpnDetected"] = net.VpnInterfaces is { Count: > 0 },
            ["vpnCount"] = net.VpnInterfaces?.Count ?? 0,

            // ── Routing (NET-010, 046..048) ──
            ["routeCount"] = routes.Count,
            ["loopbackRouteCount"] = routes.Count(r => r.Destination.StartsWith("127.")),
            ["persistentRouteCount"] = routes.Count(r => r.Protocol == 3),
            ["routeMetricConflictCount"] = routes
                .GroupBy(r => (r.Destination, r.Metric))
                .Count(g => g.Select(r => r.NextHop).Distinct().Count() > 1),

            // ── Adapters (NET-011..016, 025, 034..035, 037..040) ──
            ["adapterCount"] = adapters.Count,
            ["defaultGatewayCount"] = adapters.Count(a => !string.IsNullOrEmpty(a.Gateway)),
            ["hasDnsConfigured"] = adapters.Any(a => a.DnsServers is { Count: > 0 }),
            ["hasDnsServers"] = adapters.Any(a => a.DnsServers is { Count: > 0 }),
            ["hasGateway"] = adapters.Any(a => !string.IsNullOrEmpty(a.Gateway)),
            ["dnsServerCount"] = adapters.SelectMany(a => a.DnsServers ?? []).Distinct().Count(),
            ["apipaCount"] = adapters.Count(a => a.IpAddress?.StartsWith("169.254") == true),
            ["invalidSubnetMask"] = adapters.Any(a => a.SubnetMask is "255.255.255.255" or "0.0.0.0"),
            ["multiGatewayAdapterCount"] = 0,
            ["macAddresses"] = string.Join(", ", adapters.Where(a => !string.IsNullOrEmpty(a.MacAddress)).Select(a => a.MacAddress)),
            ["primaryLinkSpeedMbps"] = adapters
                .Where(a => a.Category is "ethernet" or null && a.Type?.Contains("Wireless", StringComparison.OrdinalIgnoreCase) != true)
                .Select(a => a.SpeedMbps ?? 0).DefaultIfEmpty(0).Max(),
            ["wifiSpeedMbps"] = adapters
                .Where(a => a.Category == "wifi" || a.Type?.Contains("Wireless", StringComparison.OrdinalIgnoreCase) == true)
                .Select(a => a.SpeedMbps ?? 0).DefaultIfEmpty(0).Max(),
            ["disconnectedWithIpCount"] = net.DisconnectedWithIpCount ?? 0,

            // ── DHCP (NET-015) ──
            ["dhcpEnabled"] = adapters.Any(a => a.DhcpEnabled == true),

            // ── Bandwidth (NET-017..018, 043) ──
            ["bandwidthSendMbps"] = net.Bandwidth?.SendRateMbps,
            ["bandwidthRecvMbps"] = net.Bandwidth?.RecvRateMbps,

            // ── Agent-collected fields (null until agent v1.6.5+) ──
            ["hostsFileEntryCount"] = net.HostsFileEntryCount,
            ["ntpConfigured"] = net.NtpConfigured,
            ["wpadEnabled"] = net.WpadEnabled,
            ["llmnrNetbiosEnabled"] = net.LlmnrEnabled == true || net.NetbiosEnabled == true,
            ["openWifiCount"] = net.OpenWifiCount ?? 0,
            ["arpEntryCount"] = net.ArpEntryCount ?? 0,
            ["listeningPortCount"] = net.ListeningPortCount ?? 0,
            ["ipv6Enabled"] = adapters.Any(a => !string.IsNullOrEmpty(a.Ipv6Address)),
            ["nicTeamingDetected"] = net.NicTeamingDetected ?? false,
            ["nonStandardMtuCount"] = adapters.Count(a => a.Mtu is not null and not 1500),

            // ── IPv6 (NET-021) ──
            ["ipv6Status"] = adapters.Any(a => !string.IsNullOrEmpty(a.Ipv6Address)) ? "enabled" : "disabled",
        };

        // ── NET-014: public DNS on domain-joined ──
        if (hw?.DomainStatus?.Contains("Domain", StringComparison.OrdinalIgnoreCase) == true)
        {
            vals["publicDnsOnDomainJoined"] = adapters
                .SelectMany(a => a.DnsServers ?? [])
                .Any(d => publicDns.Contains(d));
        }
        else
        {
            vals["publicDnsOnDomainJoined"] = false;
        }

        // ── NET-036: VPN split tunnel detection ──
        var defaultRoute = routes.FirstOrDefault(r => r.Destination == "0.0.0.0" && r.Mask == "0.0.0.0");
        var vpnIps = (net.VpnInterfaces ?? []).Select(v => v.IpAddress).Where(ip => ip != null).ToHashSet();
        vals["vpnSplitTunnel"] = net.VpnInterfaces is { Count: > 0 } && defaultRoute != null && !vpnIps.Contains(defaultRoute.NextHop);

        // ── Bandwidth saturation (NET-043) ──
        var primarySpeed = adapters
            .Where(a => !string.IsNullOrEmpty(a.Gateway))
            .Select(a => a.SpeedMbps ?? 0).DefaultIfEmpty(0).Max();
        if (primarySpeed > 0 && net.Bandwidth != null)
        {
            var maxBw = Math.Max(net.Bandwidth.SendRateMbps, net.Bandwidth.RecvRateMbps);
            vals["bandwidthSaturationPct"] = Math.Round(maxBw / primarySpeed * 100, 1);
        }
        else
        {
            vals["bandwidthSaturationPct"] = 0m;
        }

        // ── Peer analysis (NET-005..008, 026..028, 044) ──
        if (net.InternalLatency is { Count: > 0 } peers)
        {
            var reachable = peers.Where(p => p.Reachable).ToList();
            vals["peerCount"] = peers.Count;
            vals["reachablePeerCount"] = reachable.Count;
            vals["unreachablePeerCount"] = peers.Count - reachable.Count;
            vals["avgPeerLatencyMs"] = reachable.Count > 0 ? reachable.Average(p => p.AvgMs) : 0m;
            vals["maxPeerLatencyMs"] = reachable.Count > 0 ? reachable.Max(p => p.MaxMs) : 0L;
            vals["maxPeerJitterMs"] = reachable.Count > 0 ? reachable.Max(p => p.JitterMs) : 0m;
            vals["maxPeerPacketLoss"] = peers.Max(p => p.PacketLoss);
            vals["avgPeerPacketLoss"] = (decimal)peers.Average(p => p.PacketLoss);

            var distinctSubnets = peers.Where(p => p.Subnet != null).Select(p => p.Subnet).Distinct().ToList();
            vals["distinctSubnetCount"] = distinctSubnets.Count;

            var localIp = adapters.FirstOrDefault(a => !string.IsNullOrEmpty(a.IpAddress))?.IpAddress;
            var localSub = localIp != null ? $"{string.Join('.', localIp.Split('.').Take(3))}.0/24" : null;
            var crossSubnet = peers.Where(p => p.Subnet != null && p.Subnet != localSub).ToList();
            vals["maxCrossSubnetLatencyMs"] = crossSubnet.Where(p => p.Reachable).Select(p => p.AvgMs).DefaultIfEmpty(0).Max();
            vals["crossSubnetUnreachableCount"] = crossSubnet.Count(p => !p.Reachable);
            vals["duplicateIpCount"] = 0;
        }
        else
        {
            vals["peerCount"] = 0;
            vals["reachablePeerCount"] = 0;
            vals["unreachablePeerCount"] = 0;
            vals["avgPeerLatencyMs"] = 0m;
            vals["maxPeerLatencyMs"] = 0L;
            vals["maxPeerJitterMs"] = 0m;
            vals["maxPeerPacketLoss"] = 0;
            vals["avgPeerPacketLoss"] = 0m;
            vals["distinctSubnetCount"] = 0;
            vals["maxCrossSubnetLatencyMs"] = 0m;
            vals["crossSubnetUnreachableCount"] = 0;
            vals["duplicateIpCount"] = 0;
        }

        // ── Site topology (NET-045, info) ──
        vals["siteTopology"] = string.Join(", ", (net.InternalLatency ?? [])
            .Where(p => p.Subnet != null).Select(p => p.Subnet).Distinct().OrderBy(s => s));

        // ── NET-050: composite health score ──
        vals["networkHealthScore"] = CalculateNetworkHealthScore(vals);

        return vals;
    }

    private static decimal CalculateNetworkHealthScore(Dictionary<string, object?> vals)
    {
        decimal score = 100;

        if (ValDecimal(vals, "downloadMbps") < 10) score -= 15;
        else if (ValDecimal(vals, "downloadMbps") < 50) score -= 5;

        if (ValDecimal(vals, "uploadMbps") < 5) score -= 10;

        if (ValDecimal(vals, "internetLatencyMs") > 100) score -= 15;
        else if (ValDecimal(vals, "internetLatencyMs") > 50) score -= 5;

        if (ValInt(vals, "maxPeerPacketLoss") > 5) score -= 15;
        if (ValInt(vals, "unreachablePeerCount") > 0) score -= 10;
        if (ValInt(vals, "apipaCount") > 0) score -= 20;
        if (ValInt(vals, "defaultGatewayCount") > 1) score -= 10;
        if (ValBool(vals, "invalidSubnetMask")) score -= 10;
        if (!ValBool(vals, "hasDnsServers")) score -= 20;
        if (!ValBool(vals, "hasGateway")) score -= 20;
        if (ValDecimal(vals, "bandwidthSaturationPct") > 80) score -= 10;

        return Math.Max(0, Math.Round(score, 0));
    }

    private static decimal ValDecimal(Dictionary<string, object?> d, string k) =>
        d.TryGetValue(k, out var v) && v != null ? Convert.ToDecimal(v) : 0;
    private static int ValInt(Dictionary<string, object?> d, string k) =>
        d.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v) : 0;
    private static bool ValBool(Dictionary<string, object?> d, string k) =>
        d.TryGetValue(k, out var v) && v is true;

    private static (string status, int score, int maxScore, string? finding) EvaluateNetworkControl(
        CheckSpec spec, ControlDef def, Dictionary<string, object?> values)
    {
        int maxScore = def.Severity switch
        {
            "critical" => 10,
            "high" => 7,
            "medium" => 4,
            "low" => 2,
            _ => 4
        };

        var field = spec.Field;
        if (string.IsNullOrEmpty(field))
            return ("info", maxScore, maxScore, "No field specified");

        var op = spec.Operator ?? "info";

        if (op == "info")
            return ("info", maxScore, maxScore, values.TryGetValue(field, out var v) ? v?.ToString() : null);

        if (!values.TryGetValue(field, out var actualObj) || actualObj is null)
            return ("warn", maxScore / 2, maxScore, $"Field '{field}' not available");

        var actualDecimal = actualObj is bool ab ? (ab ? 1m : 0m) : Convert.ToDecimal(actualObj);
        var expectedDecimal = spec.Expected is JsonElement je
            ? je.ValueKind == JsonValueKind.Number ? je.GetDecimal()
              : je.ValueKind == JsonValueKind.True ? 1m
              : je.ValueKind == JsonValueKind.False ? 0m
              : decimal.TryParse(je.GetString(), out var p) ? p : 0m
            : decimal.TryParse(spec.Expected?.ToString(), out var p2) ? p2 : 0m;

        bool passed = op switch
        {
            "gte" => actualDecimal >= expectedDecimal,
            "lte" => actualDecimal <= expectedDecimal,
            "gt" => actualDecimal > expectedDecimal,
            "lt" => actualDecimal < expectedDecimal,
            "eq" => actualDecimal == expectedDecimal,
            "neq" => actualDecimal != expectedDecimal,
            _ => true
        };

        if (passed)
            return ("pass", maxScore, maxScore, null);

        return ("fail", 0, maxScore, $"Expected {field} {op} {expectedDecimal}, got {actualDecimal}");
    }
}

// ── Agent payload models ──

public class AgentPayload
{
    public Guid AgentId { get; set; }
    public string? AgentVersion { get; set; }
    public DateTime Timestamp { get; set; }
    public int DurationMs { get; set; }
    public PlatformInfo? Platform { get; set; }
    public HardwareInfo? Hardware { get; set; }
    public List<SoftwareInfo> Software { get; set; } = [];
    public List<AgentCheckResult> Results { get; set; } = [];
    public NetworkDiagDto? NetworkDiag { get; set; }
    public List<LocalAdminDto>? LocalAdmins { get; set; }
}

public class NetworkDiagDto
{
    public decimal DownloadMbps { get; set; }
    public decimal UploadMbps { get; set; }
    public decimal InternetLatencyMs { get; set; }
    public decimal? GatewayLatencyMs { get; set; }
    public string? GatewayIp { get; set; }
    public List<LatencyPeerDto>? LinkLatency { get; set; }
    public List<LatencyPeerDto>? InternalLatency { get; set; }
    public List<RoutEntryDto>? RouteTable { get; set; }
    public List<VpnInterfaceDto>? VpnInterfaces { get; set; }
    public List<AdapterDto>? Adapters { get; set; }
    public BandwidthDto? Bandwidth { get; set; }
    public List<CloudEndpointLatencyDto>? CloudEndpointLatency { get; set; }
    public decimal? DnsResolutionMs { get; set; }
    public bool TriggeredByIpChange { get; set; }
    public int WifiCount { get; set; }
    public int VpnCount { get; set; }
    public int EthCount { get; set; }
    public int? HostsFileEntryCount { get; set; }
    public bool? NtpConfigured { get; set; }
    public bool? WpadEnabled { get; set; }
    public bool? LlmnrEnabled { get; set; }
    public bool? NetbiosEnabled { get; set; }
    public int? OpenWifiCount { get; set; }
    public int? ArpEntryCount { get; set; }
    public int? ListeningPortCount { get; set; }
    public int? DisconnectedWithIpCount { get; set; }
    public bool? NicTeamingDetected { get; set; }
}

public class CloudEndpointLatencyDto
{
    public string Endpoint { get; set; } = null!;
    public decimal LatencyMs { get; set; }
    public bool Reachable { get; set; }
}

public class LatencyPeerDto
{
    public string Host { get; set; } = null!;
    public string? Subnet { get; set; }
    public bool Reachable { get; set; }
    public decimal AvgMs { get; set; }
    public long MinMs { get; set; }
    public long MaxMs { get; set; }
    public decimal JitterMs { get; set; }
    public int PacketLoss { get; set; }
    public int TotalSent { get; set; }
}

public class RoutEntryDto
{
    public string Destination { get; set; } = null!;
    public string Mask { get; set; } = null!;
    public string NextHop { get; set; } = null!;
    public int InterfaceIndex { get; set; }
    public int Metric { get; set; }
    public int Type { get; set; }
    public int Protocol { get; set; }
}

public class VpnInterfaceDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public string? IpAddress { get; set; }
    public long? Speed { get; set; }
}

public class AdapterDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public string? Category { get; set; }
    public string? Status { get; set; }
    public long? SpeedMbps { get; set; }
    public string? MacAddress { get; set; }
    public string? IpAddress { get; set; }
    public string? SubnetMask { get; set; }
    public string? Gateway { get; set; }
    public List<string>? DnsServers { get; set; }
    public bool? DhcpEnabled { get; set; }
    public int? Mtu { get; set; }
    public string? Ipv6Address { get; set; }
}

public class BandwidthDto
{
    public decimal SendRateMbps { get; set; }
    public decimal RecvRateMbps { get; set; }
    public long TotalSentBytes { get; set; }
    public long TotalRecvBytes { get; set; }
}

public class PlatformInfo
{
    public string? Os { get; set; }
    public string? Build { get; set; }
    public string? Version { get; set; }
}

public class HardwareInfo
{
    public string? Cpu { get; set; }
    public short? CpuCores { get; set; }
    public short? RamGb { get; set; }
    public string? DiskType { get; set; }
    public int? DiskSizeGb { get; set; }
    public decimal? DiskFreeGb { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public bool? TpmPresent { get; set; }
    public string? TpmVersion { get; set; }
    public bool? SecureBoot { get; set; }
    public bool? Bitlocker { get; set; }
    public string? IpAddress { get; set; }
    public string? MacAddress { get; set; }
    public string? DomainStatus { get; set; }
    public string? DomainName { get; set; }
    public int? ProductType { get; set; }
    public int? SystemAgeDays { get; set; }
    public DateTime? LastBootAt { get; set; }
    public string? Tpm { get; set; } // legacy compat
    public List<DiskInfo>? Disks { get; set; }
    public List<ThreatFinding>? Threats { get; set; }
}

public class DiskInfo
{
    public string DriveLetter { get; set; } = null!;
    public string? Label { get; set; }
    public string? DiskType { get; set; }
    public int? TotalGb { get; set; }
    public decimal? FreeGb { get; set; }
    public string? FileSystem { get; set; }
}

public class ThreatFinding
{
    public string ThreatName { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string Vector { get; set; } = null!;
    public string? Detail { get; set; }
}

public class SoftwareInfo
{
    public string Name { get; set; } = null!;
    public string? Version { get; set; }
    public string? Publisher { get; set; }
}

public class LocalAdminDto
{
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Source { get; set; } = null!;
}

public class AgentCheckResult
{
    public string Id { get; set; } = null!; // BL-001
    public bool? Exists { get; set; }
    public object? Value { get; set; }
    public string? RegType { get; set; }
    public string? StartType { get; set; }
    public string? Status { get; set; }
    public int? ExitCode { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
}

/// <summary>
/// Server-side check specification from control_defs.check_json.
/// Contains both agent instructions AND expected values for evaluation.
/// </summary>
public class CheckSpec
{
    // Agent instructions (sent to agent via GET /controls)
    public string? CheckType { get; set; }          // engine sub-variant
    public string? Hive { get; set; }
    public string? Path { get; set; }
    public string? ValueName { get; set; }
    public string? Subcategory { get; set; }
    public string? Profile { get; set; }
    public string? Property { get; set; }
    public string? SettingName { get; set; }
    public string? ServiceName { get; set; }
    public string? Field { get; set; }
    public string? Executable { get; set; }
    public string? Arguments { get; set; }
    public string? Display { get; set; }
    public int? TimeoutSeconds { get; set; }        // ShellEngine override
    public string? Parent { get; set; }             // dependency control id

    // Engine-specific instructions
    public string? LogName { get; set; }            // EventLogEngine
    public string? StoreName { get; set; }          // CertStoreEngine
    public string? StoreLocation { get; set; }      // CertStoreEngine
    public string? Drive { get; set; }              // BitLockerEngine

    // Server-side evaluation (NOT sent to agent)
    public object? Expected { get; set; }
    public string? Operator { get; set; } // eq, neq, gte, lte, contains, not_contains, exists, not_exists, in
    public string? MissingBehavior { get; set; } // pass, warn, fail
    public string? ExpectedStartType { get; set; } // service-type checks: Disabled/Manual/Automatic
    public bool? Optional { get; set; }            // data-gathering / not-present-is-ok
}
