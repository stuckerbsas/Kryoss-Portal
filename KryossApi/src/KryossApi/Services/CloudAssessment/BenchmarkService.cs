using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services.CloudAssessment.Benchmarks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CloudAssessment;

public class BenchmarkService : IBenchmarkService
{
    private readonly KryossDbContext _db;
    private readonly ILogger<BenchmarkService> _log;

    private const int FranchiseMinSample = 5;
    private const int GlobalMinSample = 50;
    private const decimal VerdictThreshold = 0.3m; // absolute delta on 0-5 area scale; for 0-100 metrics scales proportionally via caller.

    public BenchmarkService(KryossDbContext db, ILogger<BenchmarkService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task ComputeAndPersistAsync(Guid scanId, CancellationToken ct)
    {
        var scan = await _db.CloudAssessmentScans
            .Include(s => s.Organization)
                .ThenInclude(o => o.Franchise)
            .Include(s => s.FrameworkScores)
            .Include(s => s.Metrics)
            .FirstOrDefaultAsync(s => s.Id == scanId, ct);

        if (scan == null || scan.Status != "completed")
        {
            _log.LogInformation("Benchmark compute skipped — scan {ScanId} not completed", scanId);
            return;
        }

        var frameworkCodes = await _db.CloudAssessmentFrameworks
            .ToDictionaryAsync(f => f.Id, f => f.Code, ct);

        var orgValues = ExtractOrgMetricValues(scan, frameworkCodes);
        if (orgValues.Count == 0)
        {
            _log.LogInformation("Benchmark compute skipped — scan {ScanId} has no metric values", scanId);
            return;
        }

        var franchiseId = scan.Organization.FranchiseId;
        var industryCode = scan.Organization.IndustryCode;
        var employeeBand = scan.Organization.EmployeeCountBand;

        var franchiseAggs = await _db.CloudAssessmentFranchiseAggregates
            .Where(a => a.FranchiseId == franchiseId)
            .ToDictionaryAsync(a => a.MetricKey, ct);

        var industryRows = industryCode != null
            ? await _db.CloudAssessmentIndustryBenchmarks
                .Where(b => b.IndustryCode == industryCode &&
                           (b.EmployeeBand == employeeBand || b.EmployeeBand == null))
                .ToListAsync(ct)
            : new List<CloudAssessmentIndustryBenchmark>();

        // Prefer exact employee-band match; fall back to null-band row.
        var industryByKey = industryRows
            .GroupBy(r => r.MetricKey)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.EmployeeBand == employeeBand ? 1 : 0).First());

        var globalAggs = await _db.CloudAssessmentGlobalAggregates
            .Where(a => (a.IndustryCode == industryCode || a.IndustryCode == null) &&
                       (a.EmployeeBand == employeeBand || a.EmployeeBand == null))
            .ToListAsync(ct);

        var globalByKey = globalAggs
            .GroupBy(a => a.MetricKey)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => (a.IndustryCode == industryCode ? 2 : 0) +
                                                (a.EmployeeBand == employeeBand ? 1 : 0))
                     .First());

        // Remove any prior comparison rows for this scan (idempotent re-run).
        var existing = _db.CloudAssessmentBenchmarkComparisons.Where(c => c.ScanId == scanId);
        _db.CloudAssessmentBenchmarkComparisons.RemoveRange(existing);

        var now = DateTime.UtcNow;
        foreach (var (key, orgVal) in orgValues)
        {
            var row = new CloudAssessmentBenchmarkComparison
            {
                ScanId = scanId,
                MetricKey = key,
                OrgValue = orgVal,
                ComputedAt = now,
            };

            if (franchiseAggs.TryGetValue(key, out var fa) && (fa.SampleSize ?? 0) >= FranchiseMinSample)
            {
                row.FranchiseAvg = fa.AvgValue;
                row.FranchiseSampleSize = fa.SampleSize;
                row.FranchisePercentile = Percentile(orgVal, fa.Percentile25, fa.Percentile50, fa.Percentile75,
                    BenchmarkMetrics.HigherIsBetter(key));
            }

            if (industryByKey.TryGetValue(key, out var ib))
            {
                row.IndustryBaseline = ib.BaselineValue;
                row.IndustryP25 = ib.Percentile25;
                row.IndustryP50 = ib.Percentile50;
                row.IndustryP75 = ib.Percentile75;
                row.IndustryPercentile = Percentile(orgVal, ib.Percentile25, ib.Percentile50, ib.Percentile75,
                    BenchmarkMetrics.HigherIsBetter(key));
            }

            if (globalByKey.TryGetValue(key, out var ga) && (ga.SampleSize ?? 0) >= GlobalMinSample)
            {
                row.GlobalAvg = ga.AvgValue;
                row.GlobalSampleSize = ga.SampleSize;
                row.GlobalPercentile = Percentile(orgVal, ga.Percentile25, ga.Percentile50, ga.Percentile75,
                    BenchmarkMetrics.HigherIsBetter(key));
            }

            row.Verdict = ComputeVerdict(orgVal, row.FranchiseAvg, row.FranchiseSampleSize ?? 0, key);

            _db.CloudAssessmentBenchmarkComparisons.Add(row);
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Persisted {Count} benchmark comparisons for scan {ScanId}", orgValues.Count, scanId);
    }

    public async Task<BenchmarkReport> GetBenchmarkReportAsync(Guid scanId, CancellationToken ct)
    {
        var scan = await _db.CloudAssessmentScans
            .Include(s => s.Organization)
            .FirstOrDefaultAsync(s => s.Id == scanId, ct)
            ?? throw new InvalidOperationException($"Scan {scanId} not found");

        var rows = await _db.CloudAssessmentBenchmarkComparisons
            .Where(c => c.ScanId == scanId)
            .OrderBy(c => c.MetricKey)
            .ToListAsync(ct);

        var franchiseId = scan.Organization.FranchiseId;
        var franchiseOrgCount = await CountScannedOrgsInFranchise(franchiseId, ct);
        var globalOrgCount = await CountGloballyScannedOrgs(ct);

        var avail = new BenchmarkAvailability
        {
            FranchiseOrgCount = franchiseOrgCount,
            FranchiseBenchmarkAvailable = franchiseOrgCount >= FranchiseMinSample,
            IndustryCode = scan.Organization.IndustryCode,
            IndustryBenchmarkAvailable = scan.Organization.IndustryCode != null
                && await _db.CloudAssessmentIndustryBenchmarks
                    .AnyAsync(b => b.IndustryCode == scan.Organization.IndustryCode, ct),
            GlobalOrgCount = globalOrgCount,
            GlobalBenchmarkAvailable = globalOrgCount >= GlobalMinSample,
        };

        var metrics = rows.Select(r => new MetricComparison
        {
            MetricKey = r.MetricKey,
            DisplayName = BenchmarkMetrics.DisplayName(r.MetricKey),
            Category = BenchmarkMetrics.Category(r.MetricKey),
            OrgValue = r.OrgValue,
            FranchiseAvg = avail.FranchiseBenchmarkAvailable ? r.FranchiseAvg : null,
            FranchisePercentile = avail.FranchiseBenchmarkAvailable ? r.FranchisePercentile : null,
            FranchiseSampleSize = r.FranchiseSampleSize ?? 0,
            IndustryBaseline = r.IndustryBaseline,
            IndustryP25 = r.IndustryP25,
            IndustryP50 = r.IndustryP50,
            IndustryP75 = r.IndustryP75,
            IndustryPercentile = r.IndustryPercentile,
            GlobalAvg = avail.GlobalBenchmarkAvailable ? r.GlobalAvg : null,
            GlobalPercentile = avail.GlobalBenchmarkAvailable ? r.GlobalPercentile : null,
            GlobalSampleSize = r.GlobalSampleSize ?? 0,
            Verdict = r.Verdict ?? "insufficient_data",
        }).ToList();

        return new BenchmarkReport { Metrics = metrics, Availability = avail };
    }

    public async Task<(int franchises, int metrics)> RefreshFranchiseAggregatesAsync(CancellationToken ct)
    {
        // Latest completed scan per org.
        var latestScans = await _db.CloudAssessmentScans
            .Where(s => s.Status == "completed")
            .GroupBy(s => s.OrganizationId)
            .Select(g => g.OrderByDescending(s => s.CompletedAt).First())
            .Include(s => s.Organization)
            .Include(s => s.FrameworkScores)
            .Include(s => s.Metrics)
            .ToListAsync(ct);

        var byFranchise = latestScans
            .GroupBy(s => s.Organization.FranchiseId)
            .Where(g => g.Count() >= FranchiseMinSample)
            .ToList();

        // Wipe stale franchise aggregates before recompute.
        var touchedFranchises = byFranchise.Select(g => g.Key).ToHashSet();
        if (touchedFranchises.Count > 0)
        {
            var stale = _db.CloudAssessmentFranchiseAggregates
                .Where(a => touchedFranchises.Contains(a.FranchiseId));
            _db.CloudAssessmentFranchiseAggregates.RemoveRange(stale);
            await _db.SaveChangesAsync(ct);
        }

        var totalMetrics = 0;
        var now = DateTime.UtcNow;
        foreach (var group in byFranchise)
        {
            var franchiseId = group.Key;
            var metricValues = CollectMetricValuesByKey(group.ToList());
            foreach (var (key, values) in metricValues)
            {
                if (values.Count < FranchiseMinSample) continue;
                var stats = ComputeStats(values);
                _db.CloudAssessmentFranchiseAggregates.Add(new CloudAssessmentFranchiseAggregate
                {
                    FranchiseId = franchiseId,
                    MetricKey = key,
                    AvgValue = stats.Avg,
                    Percentile25 = stats.P25,
                    Percentile50 = stats.P50,
                    Percentile75 = stats.P75,
                    SampleSize = values.Count,
                    RefreshedAt = now,
                });
                totalMetrics++;
            }
        }
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Refreshed franchise aggregates: {Franchises} franchises, {Metrics} metric rows",
            byFranchise.Count, totalMetrics);
        return (byFranchise.Count, totalMetrics);
    }

    public async Task<(int rows, int metrics)> RefreshGlobalAggregatesAsync(CancellationToken ct)
    {
        // Only include franchises that opted in.
        var optedOutFranchises = await _db.Franchises
            .Where(f => !f.BenchmarkOptIn)
            .Select(f => f.Id)
            .ToListAsync(ct);

        var latestScans = await _db.CloudAssessmentScans
            .Where(s => s.Status == "completed")
            .Include(s => s.Organization)
            .Include(s => s.FrameworkScores)
            .Include(s => s.Metrics)
            .ToListAsync(ct);

        // Deduplicate: one scan per org (latest).
        latestScans = latestScans
            .GroupBy(s => s.OrganizationId)
            .Select(g => g.OrderByDescending(s => s.CompletedAt).First())
            .Where(s => !optedOutFranchises.Contains(s.Organization.FranchiseId))
            .ToList();

        if (latestScans.Count < GlobalMinSample)
        {
            _log.LogInformation("Global aggregate refresh skipped — {Count} opted-in scans, need {Min}",
                latestScans.Count, GlobalMinSample);
            return (0, 0);
        }

        // Wipe + rebuild.
        _db.CloudAssessmentGlobalAggregates.RemoveRange(_db.CloudAssessmentGlobalAggregates);
        await _db.SaveChangesAsync(ct);

        var now = DateTime.UtcNow;
        var rowCount = 0;
        var metricSet = new HashSet<string>();

        // "All industries, all sizes" slice.
        AddSlice(latestScans, industry: null, band: null, now, ref rowCount, metricSet);

        // Per-industry slice (size-agnostic).
        foreach (var industryGroup in latestScans.Where(s => s.Organization.IndustryCode != null)
                                                  .GroupBy(s => s.Organization.IndustryCode!))
        {
            AddSlice(industryGroup.ToList(), industryGroup.Key, band: null, now, ref rowCount, metricSet);
        }

        // Per-industry + band slice.
        foreach (var g in latestScans
                     .Where(s => s.Organization.IndustryCode != null && s.Organization.EmployeeCountBand != null)
                     .GroupBy(s => new { s.Organization.IndustryCode, s.Organization.EmployeeCountBand }))
        {
            AddSlice(g.ToList(), g.Key.IndustryCode, g.Key.EmployeeCountBand, now, ref rowCount, metricSet);
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Refreshed global aggregates: {Rows} rows across {Metrics} metrics", rowCount, metricSet.Count);
        return (rowCount, metricSet.Count);
    }

    public Task<List<IndustryOption>> GetIndustryOptionsAsync()
    {
        var opts = IndustryCodes.All.Select(t => new IndustryOption
        {
            Code = t.Code,
            Label = t.Label,
            Description = t.Description,
        }).ToList();
        return Task.FromResult(opts);
    }

    public async Task<FranchiseLeaderboard> GetFranchiseLeaderboardAsync(Guid franchiseId, CancellationToken ct)
    {
        var orgs = await _db.Organizations
            .Where(o => o.FranchiseId == franchiseId)
            .Select(o => new { o.Id, o.Name })
            .ToListAsync(ct);

        var orgIds = orgs.Select(o => o.Id).ToList();
        var latestScans = await _db.CloudAssessmentScans
            .Where(s => orgIds.Contains(s.OrganizationId) && s.Status == "completed")
            .ToListAsync(ct);

        var latestByOrg = latestScans
            .GroupBy(s => s.OrganizationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CompletedAt).First());

        var rows = new List<FranchiseLeaderboardRow>();
        foreach (var o in orgs)
        {
            latestByOrg.TryGetValue(o.Id, out var scan);
            var row = new FranchiseLeaderboardRow
            {
                OrganizationId = o.Id,
                OrganizationName = o.Name,
                OverallScore = scan?.OverallScore,
                LastScanAt = scan?.CompletedAt,
            };
            if (scan?.AreaScores != null)
            {
                var areas = ParseAreaScores(scan.AreaScores);
                if (areas.Count > 0)
                {
                    var best = areas.OrderByDescending(kv => kv.Value).First();
                    var worst = areas.OrderBy(kv => kv.Value).First();
                    row.TopArea = best.Key;
                    row.TopAreaScore = best.Value;
                    row.WeakestArea = worst.Key;
                    row.WeakestAreaScore = worst.Value;
                }
            }
            rows.Add(row);
        }

        rows = rows.OrderByDescending(r => r.OverallScore ?? -1m).ToList();

        return new FranchiseLeaderboard
        {
            FranchiseId = franchiseId,
            OrgCount = rows.Count,
            Available = rows.Count(r => r.OverallScore != null) >= FranchiseMinSample,
            Rows = rows,
        };
    }

    // ── helpers ──

    private static Dictionary<string, decimal> ExtractOrgMetricValues(
        CloudAssessmentScan scan,
        Dictionary<Guid, string> frameworkCodes)
    {
        var result = new Dictionary<string, decimal>();

        if (scan.OverallScore.HasValue)
            result[BenchmarkMetrics.OverallScore] = scan.OverallScore.Value;

        if (!string.IsNullOrWhiteSpace(scan.AreaScores))
        {
            foreach (var kv in ParseAreaScores(scan.AreaScores))
                result[$"area.{kv.Key}"] = kv.Value;
        }

        foreach (var fs in scan.FrameworkScores)
        {
            if (frameworkCodes.TryGetValue(fs.FrameworkId, out var code))
                result[$"framework.{code}"] = fs.ScorePct;
        }

        foreach (var m in scan.Metrics)
        {
            var key = $"metric.{m.MetricKey}";
            if (BenchmarkMetrics.All.Contains(key) && decimal.TryParse(m.MetricValue,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
                result[key] = v;
        }

        return result;
    }

    private static Dictionary<string, decimal> ParseAreaScores(string json)
    {
        var result = new Dictionary<string, decimal>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out var d))
                    result[p.Name] = d;
            }
        }
        catch
        {
            // Malformed area_scores — ignore.
        }
        return result;
    }

    private Dictionary<string, List<decimal>> CollectMetricValuesByKey(List<CloudAssessmentScan> scans)
    {
        var frameworkCodes = _db.CloudAssessmentFrameworks
            .AsNoTracking()
            .ToDictionary(f => f.Id, f => f.Code);
        var d = new Dictionary<string, List<decimal>>();
        foreach (var scan in scans)
        {
            foreach (var kv in ExtractOrgMetricValues(scan, frameworkCodes))
            {
                if (!d.TryGetValue(kv.Key, out var list))
                    d[kv.Key] = list = new List<decimal>();
                list.Add(kv.Value);
            }
        }
        return d;
    }

    private static (decimal Avg, decimal P25, decimal P50, decimal P75) ComputeStats(List<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var avg = sorted.Average();
        return (Math.Round(avg, 2),
                Math.Round(Quantile(sorted, 0.25m), 2),
                Math.Round(Quantile(sorted, 0.50m), 2),
                Math.Round(Quantile(sorted, 0.75m), 2));
    }

    private static decimal Quantile(List<decimal> sorted, decimal q)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var pos = q * (sorted.Count - 1);
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        if (lo == hi) return sorted[lo];
        var frac = pos - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
    }

    /// <summary>
    /// Piecewise-linear percentile from p25/p50/p75 anchors. Returns 0-100.
    /// If higherIsBetter is false, inverts so a low value maps to a high percentile.
    /// </summary>
    private static decimal? Percentile(decimal orgValue, decimal? p25, decimal? p50, decimal? p75, bool higherIsBetter)
    {
        if (!p25.HasValue || !p50.HasValue || !p75.HasValue) return null;
        var v = orgValue;
        decimal pct;
        if (v <= p25.Value) pct = 12.5m;
        else if (v <= p50.Value)
        {
            var range = p50.Value - p25.Value;
            pct = range > 0 ? 25m + (v - p25.Value) / range * 25m : 37.5m;
        }
        else if (v <= p75.Value)
        {
            var range = p75.Value - p50.Value;
            pct = range > 0 ? 50m + (v - p50.Value) / range * 25m : 62.5m;
        }
        else pct = 87.5m;

        if (!higherIsBetter) pct = 100m - pct;
        return Math.Round(Math.Clamp(pct, 0m, 100m), 1);
    }

    private static string ComputeVerdict(decimal orgValue, decimal? franchiseAvg, int sample, string metricKey)
    {
        if (sample < FranchiseMinSample || franchiseAvg == null) return "insufficient_data";
        var delta = orgValue - franchiseAvg.Value;
        // Scale threshold: 0-5 scales use 0.3; 0-100 scales use 5.0.
        var threshold = metricKey.StartsWith("area.") || metricKey == BenchmarkMetrics.OverallScore
            ? VerdictThreshold
            : 5.0m;
        var higherIsBetter = BenchmarkMetrics.HigherIsBetter(metricKey);
        if (!higherIsBetter) delta = -delta;
        if (delta > threshold) return "above_peer";
        if (delta < -threshold) return "below_peer";
        return "at_peer";
    }

    private void AddSlice(List<CloudAssessmentScan> scans, string? industry, string? band,
                          DateTime now, ref int rowCount, HashSet<string> metricSet)
    {
        var values = CollectMetricValuesByKey(scans);
        foreach (var (key, list) in values)
        {
            if (list.Count < GlobalMinSample) continue;
            var stats = ComputeStats(list);
            _db.CloudAssessmentGlobalAggregates.Add(new CloudAssessmentGlobalAggregate
            {
                MetricKey = key,
                IndustryCode = industry,
                EmployeeBand = band,
                AvgValue = stats.Avg,
                Percentile25 = stats.P25,
                Percentile50 = stats.P50,
                Percentile75 = stats.P75,
                SampleSize = list.Count,
                RefreshedAt = now,
            });
            rowCount++;
            metricSet.Add(key);
        }
    }

    private async Task<int> CountScannedOrgsInFranchise(Guid franchiseId, CancellationToken ct)
    {
        return await _db.CloudAssessmentScans
            .Where(s => s.Status == "completed" && s.Organization.FranchiseId == franchiseId)
            .Select(s => s.OrganizationId)
            .Distinct()
            .CountAsync(ct);
    }

    private async Task<int> CountGloballyScannedOrgs(CancellationToken ct)
    {
        return await _db.CloudAssessmentScans
            .Where(s => s.Status == "completed")
            .Select(s => s.OrganizationId)
            .Distinct()
            .CountAsync(ct);
    }
}
