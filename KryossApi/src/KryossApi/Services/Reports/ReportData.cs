using KryossApi.Data.Entities;

namespace KryossApi.Services.Reports;

// ======================================================================
// DTOs (moved from ReportService.cs)
// ======================================================================

public class OrgControlResult
{
    public int ControlDefId { get; set; }
    public Guid RunId { get; set; }
    public string ControlId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Finding { get; set; }
    public string? Remediation { get; set; }
}

public class ReportBranding
{
    public string CompanyName { get; set; } = "TeamLogic IT";
    public string PrimaryColor { get; set; } = "#006536";
    public string AccentColor { get; set; } = "#A2C564";
    public string? LogoUrl { get; set; }
}

public class FrameworkScoreDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public double Score { get; set; }
    public short PassCount { get; set; }
    public short FailCount { get; set; }
}

public class HygieneScanDto
{
    public DateTime ScannedAt { get; set; }
    public int TotalMachines { get; set; }
    public int TotalUsers { get; set; }
    public int StaleMachines { get; set; }
    public int DormantMachines { get; set; }
    public int StaleUsers { get; set; }
    public int DormantUsers { get; set; }
    public int DisabledUsers { get; set; }
    public int PwdNeverExpire { get; set; }
    public List<AdHygieneFinding> Findings { get; set; } = [];
}

public class OrgEnrichment
{
    public List<MachineDisk> Disks { get; set; } = [];
    public List<MachinePort> Ports { get; set; } = [];
    public List<MachineThreat> Threats { get; set; } = [];
}

/// <summary>
/// Identity of the portal user that generated the report. Shown in the
/// Executive One-Pager footer so the C-level reader knows who to call back.
/// </summary>
public class ReportUserInfo
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
    public string? CompanyName { get; set; }
}

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

    // --- Network diagnostics ---
    public List<MachineNetworkDiag> NetworkDiags { get; set; } = new();
    public bool HasNetworkData => NetworkDiags.Count > 0;

    // --- Vulnerability data ---
    public List<MachineCveFinding> CveFindings { get; set; } = new();
    public List<MachinePatchStatus> PatchStatuses { get; set; } = new();
    public ExternalScan? LatestExternalScan { get; set; }
    public bool HasCveData => CveFindings.Count > 0;
    public bool HasPatchData => PatchStatuses.Count > 0;
    public bool HasExternalScanData => LatestExternalScan != null;

    // --- DC Health ---
    public DcHealthSnapshot? DcHealth { get; set; }
    public bool HasDcHealthData => DcHealth != null;

    // --- WAN / Sites ---
    public List<NetworkSite> NetworkSites { get; set; } = new();
    public List<WanFinding> WanFindings { get; set; } = new();
    public bool HasWanData => NetworkSites.Count > 0;

    // --- Remediation ---
    public List<RemediationTask> RemediationTasks { get; set; } = new();
    public bool HasRemediationData => RemediationTasks.Count > 0;

    // --- Computed convenience ---
    public decimal AvgScore => Runs.Count > 0 ? Math.Round(Runs.Average(r => r.GlobalScore ?? 0), 1) : 0;
    public int TotalMachines => Runs.Count;
    public DateTime ScanDate => Runs.Count > 0 ? Runs.Max(r => r.CompletedAt ?? r.StartedAt) : DateTime.UtcNow;
    public bool HasCloudData => CloudScan != null;
    public string OrgGrade => ReportHelpers.GetGrade(AvgScore);
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
