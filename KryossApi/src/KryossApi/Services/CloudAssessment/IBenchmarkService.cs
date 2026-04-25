namespace KryossApi.Services.CloudAssessment;

public interface IBenchmarkService
{
    // Computes + persists all benchmark comparisons for a completed scan.
    Task ComputeAndPersistAsync(Guid scanId, CancellationToken ct);

    // Returns the comparison payload (per-metric rows + availability flags) for portal/report.
    Task<BenchmarkReport> GetBenchmarkReportAsync(Guid scanId, CancellationToken ct);

    // Nightly aggregate refresh jobs — triggered by BenchmarkRefreshFunction.
    Task<(int franchises, int metrics)> RefreshFranchiseAggregatesAsync(CancellationToken ct);
    Task<(int rows, int metrics)> RefreshGlobalAggregatesAsync(CancellationToken ct);

    // Industry dropdown population.
    Task<List<IndustryOption>> GetIndustryOptionsAsync();

    // Franchise leaderboard (MSP-side view of all their orgs by score).
    Task<FranchiseLeaderboard> GetFranchiseLeaderboardAsync(Guid franchiseId, CancellationToken ct);
}

public class BenchmarkReport
{
    public List<MetricComparison> Metrics { get; set; } = new();
    public BenchmarkAvailability Availability { get; set; } = new();
}

public class MetricComparison
{
    public string MetricKey { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Category { get; set; } = null!; // area | framework | metric | overall
    public decimal? OrgValue { get; set; }
    public decimal? FranchiseAvg { get; set; }
    public decimal? FranchisePercentile { get; set; }
    public int FranchiseSampleSize { get; set; }
    public decimal? IndustryBaseline { get; set; }
    public decimal? IndustryP25 { get; set; }
    public decimal? IndustryP50 { get; set; }
    public decimal? IndustryP75 { get; set; }
    public decimal? IndustryPercentile { get; set; }
    public decimal? GlobalAvg { get; set; }
    public decimal? GlobalPercentile { get; set; }
    public int GlobalSampleSize { get; set; }
    public string Verdict { get; set; } = "insufficient_data"; // above_peer | at_peer | below_peer | insufficient_data
}

public class BenchmarkAvailability
{
    public bool FranchiseBenchmarkAvailable { get; set; }
    public int FranchiseOrgCount { get; set; }
    public int FranchiseThreshold { get; set; } = 5;
    public bool IndustryBenchmarkAvailable { get; set; }
    public string? IndustryCode { get; set; }
    public bool GlobalBenchmarkAvailable { get; set; }
    public int GlobalOrgCount { get; set; }
    public int GlobalThreshold { get; set; } = 50;
}

public class IndustryOption
{
    public string Code { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Description { get; set; } = null!;
}

public class FranchiseLeaderboard
{
    public Guid FranchiseId { get; set; }
    public int OrgCount { get; set; }
    public bool Available { get; set; }
    public List<FranchiseLeaderboardRow> Rows { get; set; } = new();
}

public class FranchiseLeaderboardRow
{
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = null!;
    public decimal? OverallScore { get; set; }
    public string? TopArea { get; set; }
    public decimal? TopAreaScore { get; set; }
    public string? WeakestArea { get; set; }
    public decimal? WeakestAreaScore { get; set; }
    public DateTime? LastScanAt { get; set; }
}
