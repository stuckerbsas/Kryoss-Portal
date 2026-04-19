using KryossApi.Data.Entities;

namespace KryossApi.Services.Reports;

public class ReportData
{
    // --- Org context ---
    public Organization Org { get; set; } = null!;
    public ReportBranding Branding { get; set; } = null!;
    public ReportUserInfo UserInfo { get; set; } = null!;

    // --- Endpoint data ---
    public List<AssessmentRun> Runs { get; set; } = new();
    public List<OrgControlResult> ControlResults { get; set; } = new();
    public List<FrameworkScoreDto> FrameworkScores { get; set; } = new();
    public HygieneScanDto? Hygiene { get; set; }
    public OrgEnrichment Enrichment { get; set; } = new();
    public decimal? PreviousMonthScore { get; set; }

    // --- Cloud data (null = not connected) ---
    public CloudAssessmentScan? CloudScan { get; set; }
    public List<CloudAssessmentFinding>? CloudFindings { get; set; }
    public Dictionary<string, decimal>? AreaScores { get; set; }
    public List<CloudAssessmentFrameworkScore>? CloudFrameworkScores { get; set; }
    public BenchmarkData? Benchmarks { get; set; }

    // --- M365 ---
    public bool M365Connected { get; set; }
    public List<M365Finding> M365Findings { get; set; } = new();

    // --- CTAs ---
    public List<ExecutiveCta> SavedCtas { get; set; } = new();

    // --- Service catalog ---
    public List<ServiceCatalogItem> ServiceCatalog { get; set; } = new();
    public FranchiseServiceRate? Rate { get; set; }

    // --- Monthly ---
    public List<MonthlyScore>? ScoreHistory { get; set; }

    // --- Computed convenience ---
    public decimal AvgScore => Runs.Count > 0 ? Math.Round(Runs.Average(r => r.GlobalScore ?? 0), 1) : 0;
    public int TotalMachines => Runs.Count;
    public DateTime ScanDate => Runs.Count > 0 ? Runs.Max(r => r.CompletedAt ?? r.StartedAt) : DateTime.UtcNow;
    public bool HasCloudData => CloudScan != null;
}

public class BenchmarkData
{
    public Dictionary<string, decimal>? FranchisePeers { get; set; }
    public Dictionary<string, decimal>? IndustryBaseline { get; set; }
    public Dictionary<string, decimal>? GlobalKryoss { get; set; }
}

public class MonthlyScore
{
    public DateTime Month { get; set; }
    public decimal Score { get; set; }
}
