namespace KryossApi.Data.Entities;

public class CloudAssessmentIndustryBenchmark
{
    public Guid Id { get; set; }
    public string IndustryCode { get; set; } = null!;
    public string? EmployeeBand { get; set; }
    public string MetricKey { get; set; } = null!;
    public decimal BaselineValue { get; set; }
    public decimal? Percentile25 { get; set; }
    public decimal? Percentile50 { get; set; }
    public decimal? Percentile75 { get; set; }
    public int? SampleSize { get; set; }
    public string? Source { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CloudAssessmentBenchmarkComparison
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public string MetricKey { get; set; } = null!;
    public decimal? OrgValue { get; set; }
    public decimal? FranchiseAvg { get; set; }
    public decimal? FranchisePercentile { get; set; }
    public int? FranchiseSampleSize { get; set; }
    public decimal? IndustryBaseline { get; set; }
    public decimal? IndustryP25 { get; set; }
    public decimal? IndustryP50 { get; set; }
    public decimal? IndustryP75 { get; set; }
    public decimal? IndustryPercentile { get; set; }
    public decimal? GlobalAvg { get; set; }
    public decimal? GlobalPercentile { get; set; }
    public int? GlobalSampleSize { get; set; }
    public string? Verdict { get; set; }
    public DateTime ComputedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}

public class CloudAssessmentFranchiseAggregate
{
    public Guid Id { get; set; }
    public Guid FranchiseId { get; set; }
    public string MetricKey { get; set; } = null!;
    public decimal? AvgValue { get; set; }
    public decimal? Percentile25 { get; set; }
    public decimal? Percentile50 { get; set; }
    public decimal? Percentile75 { get; set; }
    public int? SampleSize { get; set; }
    public DateTime RefreshedAt { get; set; }

    public Franchise Franchise { get; set; } = null!;
}

public class CloudAssessmentGlobalAggregate
{
    public Guid Id { get; set; }
    public string MetricKey { get; set; } = null!;
    public string? IndustryCode { get; set; }
    public string? EmployeeBand { get; set; }
    public decimal? AvgValue { get; set; }
    public decimal? Percentile25 { get; set; }
    public decimal? Percentile50 { get; set; }
    public decimal? Percentile75 { get; set; }
    public int? SampleSize { get; set; }
    public DateTime RefreshedAt { get; set; }
}
