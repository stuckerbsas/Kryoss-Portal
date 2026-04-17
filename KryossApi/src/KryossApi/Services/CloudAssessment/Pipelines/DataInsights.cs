namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// Pre-computed Microsoft Purview / SharePoint / OneDrive data-protection
/// metrics for the Cloud Assessment (CA-3) data pipeline.
///
/// Covers Purview license posture (AIP P1/P2, DLP, Insider Risk, eDiscovery,
/// Advanced Audit), sensitivity label inventory, SharePoint site label
/// coverage and oversharing, external / guest user exposure, Defender for
/// Cloud Apps / Office 365 DLP alerts and OneDrive usage footprint.
///
/// Built once per assessment run and consumed by
/// <c>DataRecommendations</c> (added in Task 2) to emit findings.
/// </summary>
public class DataInsights
{
    // ============================================================
    // Purview licensing
    // ============================================================
    public bool AipP1Licensed { get; set; }
    public bool AipP2Licensed { get; set; }
    public bool DlpLicensed { get; set; }
    public bool InsiderRiskLicensed { get; set; }
    public bool EDiscoveryLicensed { get; set; }
    public bool AdvancedAuditLicensed { get; set; }

    // ============================================================
    // Labels
    // ============================================================
    public int SensitivityLabelCount { get; set; }
    public int LabelPoliciesCount { get; set; }

    // ============================================================
    // SharePoint
    // ============================================================
    public int TotalSitesScanned { get; set; }
    public int TotalFilesScanned { get; set; }
    public int LabeledFiles { get; set; }
    public int UnlabeledFiles { get; set; }
    public double LabelCoveragePct { get; set; }
    public int OversharedFiles { get; set; }
    public double OversharedPct { get; set; }
    public int HighRiskSites { get; set; }

    // ============================================================
    // External users
    // ============================================================
    public int TotalGuests { get; set; }
    public int GuestsWithRecentActivity { get; set; }
    public int GuestsWithSiteAccess { get; set; }

    // ============================================================
    // DLP
    // ============================================================
    public int DlpAlertsLast30d { get; set; }
    public int DlpIncidentCount { get; set; }

    // ============================================================
    // OneDrive
    // ============================================================
    public int OneDriveActiveAccounts { get; set; }
    public double OneDriveTotalGB { get; set; }
    public double OneDriveAvgGBPerUser { get; set; }

    // ============================================================
    // Availability / state flags
    // ============================================================
    public bool PurviewAvailable { get; set; }
    public bool SharePointAvailable { get; set; }
    public bool OneDriveAvailable { get; set; }
    public bool Sampled { get; set; }

    public bool Available => PurviewAvailable || SharePointAvailable || OneDriveAvailable;
}
