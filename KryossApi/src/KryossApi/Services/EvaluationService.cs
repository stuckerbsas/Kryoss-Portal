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
            // Lifecycle
            machine.SystemAgeDays = payload.Hardware?.SystemAgeDays;
            machine.LastBootAt = payload.Hardware?.LastBootAt;

            // Recompute platform scope if the OS string actually changed
            // (e.g. in-place upgrade from Windows 10 to Windows 11).
            if (!string.Equals(prevOsName, machine.OsName, StringComparison.Ordinal))
            {
                machine.PlatformId = await _platformResolver.ResolveIdAsync(
                    machine.OsName, machine.OsVersion, machine.OsBuild);
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
