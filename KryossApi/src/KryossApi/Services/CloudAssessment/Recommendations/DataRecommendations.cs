using System.Globalization;
using KryossApi.Services.CloudAssessment.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;

namespace KryossApi.Services.CloudAssessment.Recommendations;

/// <summary>
/// Generates Cloud Assessment (CA-3) data-protection recommendations from a
/// pre-computed <see cref="DataInsights"/> bag.
///
/// Covers four service surfaces:
///
///   * Microsoft Purview (service = "purview") — AIP/DLP licensing,
///     sensitivity label deployment, DLP policy posture, eDiscovery,
///     Advanced Audit, retention labels, Customer Lockbox, Information
///     Barriers and Insider Risk Management.
///
///   * SharePoint (service = "sharepoint") — label coverage across
///     scanned files, org-wide oversharing, high-risk sites, external
///     guest exposure and unlabeled sensitive content.
///
///   * OneDrive (service = "onedrive") — storage hoarding thresholds
///     across active accounts.
///
///   * Microsoft 365 (service = "m365") — reserved for cross-surface
///     findings (none emitted in CA-3).
///
/// Reuses <see cref="RecommendationResult"/> from CopilotReadiness so
/// downstream report rendering / pipeline result processing remains
/// shared. Observation / recommendation text is in English — bilingual
/// rendering happens downstream.
/// </summary>
public static class DataRecommendations
{
    private const string PurviewSvc = "purview";
    private const string SharePointSvc = "sharepoint";
    private const string OneDriveSvc = "onedrive";
    private const string M365Svc = "m365";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ----------------------------------------------------------------
    //  Public entry point
    // ----------------------------------------------------------------

    public static List<RecommendationResult> Generate(DataInsights ins)
    {
        var all = new List<RecommendationResult>();
        all.AddRange(GenerateAipDlpLicensing(ins));
        all.AddRange(GenerateSensitivityLabelDeployment(ins));
        all.AddRange(GenerateLabelCoverage(ins));
        all.AddRange(GenerateOversharing(ins));
        all.AddRange(GenerateHighRiskSites(ins));
        all.AddRange(GenerateDlpPosture(ins));
        all.AddRange(GenerateGuestUserAccess(ins));
        all.AddRange(GenerateEDiscovery(ins));
        all.AddRange(GenerateAdvancedAudit(ins));
        all.AddRange(GenerateRetentionLabels(ins));
        all.AddRange(GenerateCustomerLockbox(ins));
        all.AddRange(GenerateInformationBarriers(ins));
        all.AddRange(GenerateInsiderRisk(ins));
        all.AddRange(GenerateUnlabeledContent(ins));
        all.AddRange(GenerateOneDriveHoarding(ins));
        return all;
    }

    // ================================================================
    //  Purview — AIP / DLP licensing
    // ================================================================

    private static List<RecommendationResult> GenerateAipDlpLicensing(DataInsights ins)
    {
        const string feature = "AIP/DLP Licensing";
        const string linkText = "Microsoft Purview Information Protection licensing";
        const string linkUrl = "https://learn.microsoft.com/purview/information-protection";
        var list = new List<RecommendationResult>();

        if (!ins.AipP1Licensed && !ins.AipP2Licensed)
        {
            list.Add(RecommendationResult.ActionRequired(PurviewSvc, feature, "high",
                observation: "No Azure Information Protection license detected — cannot classify or protect sensitive data.",
                recommendation: "Acquire AIP P1 or M365 E5 to enable sensitivity labels and data protection.",
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else if (!ins.DlpLicensed)
        {
            list.Add(RecommendationResult.Warning(PurviewSvc, feature,
                observation: "DLP licensing absent — outbound data-loss prevention policies cannot be enforced on Exchange / Teams / SharePoint.",
                recommendation: "Add M365 E3 compliance add-on or E5 for endpoint DLP coverage.",
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Success(PurviewSvc, feature,
                observation: "AIP + DLP licenses present.",
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }

    // ================================================================
    //  Purview — Sensitivity label deployment
    // ================================================================

    private static List<RecommendationResult> GenerateSensitivityLabelDeployment(DataInsights ins)
    {
        const string feature = "Sensitivity Label Deployment";
        const string rec = "Create at least one baseline label set (Public / Internal / Confidential / Highly Confidential) so SharePoint + Copilot can enforce handling rules.";
        const string linkText = "Create sensitivity labels";
        const string linkUrl = "https://learn.microsoft.com/purview/create-sensitivity-labels";
        var list = new List<RecommendationResult>();

        if (ins.SensitivityLabelCount == 0)
        {
            list.Add(RecommendationResult.ActionRequired(PurviewSvc, feature, "high",
                observation: "No sensitivity labels configured in Purview.",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else if (ins.SensitivityLabelCount < 3)
        {
            list.Add(RecommendationResult.Warning(PurviewSvc, feature,
                observation: $"Only {ins.SensitivityLabelCount.ToString(Inv)} sensitivity label(s) defined — a baseline taxonomy usually needs 3-4 tiers.",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Success(PurviewSvc, feature,
                observation: $"{ins.SensitivityLabelCount.ToString(Inv)} sensitivity labels configured.",
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }

    // ================================================================
    //  SharePoint — Sensitivity label coverage
    // ================================================================

    private static List<RecommendationResult> GenerateLabelCoverage(DataInsights ins)
    {
        const string feature = "Sensitivity Label Coverage";
        const string rec = "Enable auto-labeling policies and Purview training wheels to drive coverage above 80%.";
        const string linkText = "Auto-labeling policies";
        const string linkUrl = "https://learn.microsoft.com/purview/apply-sensitivity-label-automatically";
        var list = new List<RecommendationResult>();

        if (ins.TotalFilesScanned == 0) return list;

        double pct = ins.LabelCoveragePct;

        if (pct < 40)
        {
            list.Add(RecommendationResult.ActionRequired(SharePointSvc, feature, "high",
                observation: $"Only {pct.ToString("F1", Inv)}% of scanned files carry a sensitivity label.",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else if (pct < 60)
        {
            list.Add(RecommendationResult.Warning(SharePointSvc, feature,
                observation: $"Label coverage is {pct.ToString("F1", Inv)}%.",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else if (pct >= 80)
        {
            list.Add(RecommendationResult.Success(SharePointSvc, feature,
                observation: $"Label coverage is healthy ({pct.ToString("F1", Inv)}%).",
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Warning(SharePointSvc, feature,
                observation: $"Label coverage is {pct.ToString("F1", Inv)}% — approaching target, still room to improve.",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }

    // ================================================================
    //  SharePoint — Oversharing
    // ================================================================

    private static List<RecommendationResult> GenerateOversharing(DataInsights ins)
    {
        const string feature = "SharePoint Oversharing";
        const string rec = "Review org-wide sharing links and shift to least-privilege groups. Disable 'Everyone except external' on sensitive libraries.";
        const string linkText = "SharePoint sharing and permissions";
        const string linkUrl = "https://learn.microsoft.com/sharepoint/external-sharing-overview";
        var list = new List<RecommendationResult>();

        if (ins.TotalFilesScanned == 0) return list;

        double pct = ins.OversharedPct;

        if (pct >= 20)
        {
            list.Add(RecommendationResult.ActionRequired(SharePointSvc, feature, "high",
                observation: $"{pct.ToString("F1", Inv)}% of scanned files are shared at organization or public scope ({ins.OversharedFiles.ToString(Inv)} files).",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else if (pct >= 10)
        {
            list.Add(RecommendationResult.Warning(SharePointSvc, feature,
                observation: $"{pct.ToString("F1", Inv)}% of scanned files are shared at organization or public scope ({ins.OversharedFiles.ToString(Inv)} files).",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else if (pct < 5)
        {
            list.Add(RecommendationResult.Success(SharePointSvc, feature,
                observation: $"Only {pct.ToString("F1", Inv)}% of files are overshared.",
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Warning(SharePointSvc, feature,
                observation: $"Oversharing at {pct.ToString("F1", Inv)}% — monitor trend.",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }

    // ================================================================
    //  SharePoint — High-risk sites
    // ================================================================

    private static List<RecommendationResult> GenerateHighRiskSites(DataInsights ins)
    {
        const string feature = "High-Risk SharePoint Sites";
        const string rec = "Prioritize the top 10 sites for a sharing audit and enforce time-bound sharing links.";
        const string linkText = "Site permissions management";
        const string linkUrl = "https://learn.microsoft.com/sharepoint/manage-site-permissions";
        var list = new List<RecommendationResult>();

        if (ins.HighRiskSites > 5)
        {
            list.Add(RecommendationResult.ActionRequired(SharePointSvc, feature, "high",
                observation: $"{ins.HighRiskSites.ToString(Inv)} sites exceed oversharing thresholds.",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else if (ins.HighRiskSites > 0)
        {
            list.Add(RecommendationResult.Warning(SharePointSvc, feature,
                observation: $"{ins.HighRiskSites.ToString(Inv)} site(s) exceed oversharing thresholds.",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }

    // ================================================================
    //  Purview — DLP policy posture
    // ================================================================

    private static List<RecommendationResult> GenerateDlpPosture(DataInsights ins)
    {
        const string feature = "DLP Policy Posture";
        const string linkText = "DLP policies";
        const string linkUrl = "https://learn.microsoft.com/purview/dlp-policy-reference";
        var list = new List<RecommendationResult>();

        if (!ins.DlpLicensed) return list;

        if (ins.DlpAlertsLast30d == 0 && ins.DlpLicensed)
        {
            list.Add(RecommendationResult.Warning(PurviewSvc, feature,
                observation: "DLP is licensed but no alerts were generated in the last 30 days — policies may be misconfigured or audit-only.",
                recommendation: "Review DLP policies in Purview portal and ensure enforcement mode is ON for the critical scenarios (credit card, SSN, keywords).",
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Success(PurviewSvc, feature,
                observation: $"{ins.DlpAlertsLast30d.ToString(Inv)} DLP alert(s) in last 30 days.",
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }

    // ================================================================
    //  SharePoint — External guest users
    // ================================================================

    private static List<RecommendationResult> GenerateGuestUserAccess(DataInsights ins)
    {
        const string feature = "External Guest Users";
        const string rec = "Run access reviews on all Guests, remove inactive accounts, and enforce quarterly recertification.";
        const string linkText = "Access reviews for guests";
        const string linkUrl = "https://learn.microsoft.com/entra/id-governance/access-reviews-overview";
        var list = new List<RecommendationResult>();

        int guests = ins.TotalGuests;
        if (guests <= 0) return list;

        if (guests > 50)
        {
            list.Add(RecommendationResult.ActionRequired(SharePointSvc, feature, "high",
                observation: $"{guests.ToString(Inv)} guest users with tenant access.",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else if (guests > 20)
        {
            list.Add(RecommendationResult.Warning(SharePointSvc, feature,
                observation: $"{guests.ToString(Inv)} guest users with tenant access.",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Success(SharePointSvc, feature,
                observation: $"{guests.ToString(Inv)} guest user(s) present.",
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }

    // ================================================================
    //  Purview — eDiscovery
    // ================================================================

    private static List<RecommendationResult> GenerateEDiscovery(DataInsights ins)
    {
        const string feature = "eDiscovery";
        const string linkText = "eDiscovery overview";
        const string linkUrl = "https://learn.microsoft.com/purview/ediscovery";
        var list = new List<RecommendationResult>();

        if (!ins.EDiscoveryLicensed)
        {
            list.Add(RecommendationResult.Warning(PurviewSvc, feature,
                observation: "eDiscovery not licensed — legal hold / search workflows unavailable.",
                recommendation: "Consider M365 E5 or Compliance add-on if subject to regulatory discovery obligations.",
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Success(PurviewSvc, feature,
                observation: "eDiscovery license present.",
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }

    // ================================================================
    //  Purview — Advanced Audit
    // ================================================================

    private static List<RecommendationResult> GenerateAdvancedAudit(DataInsights ins)
    {
        const string feature = "Advanced Audit";
        const string linkText = "Advanced Audit capabilities";
        const string linkUrl = "https://learn.microsoft.com/purview/audit-solutions-overview";
        var list = new List<RecommendationResult>();

        if (!ins.AdvancedAuditLicensed)
        {
            list.Add(RecommendationResult.Warning(PurviewSvc, feature,
                observation: "Advanced Audit not licensed — 10-year retention + Copilot audit events unavailable.",
                recommendation: "Required for Copilot evidence, investigation, and incident response. Consider M365 E5 Compliance.",
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Success(PurviewSvc, feature,
                observation: "Advanced Audit license present.",
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }

    // ================================================================
    //  Purview — Retention labels
    // ================================================================

    private static List<RecommendationResult> GenerateRetentionLabels(DataInsights ins)
    {
        const string feature = "Retention Labels";
        var list = new List<RecommendationResult>
        {
            RecommendationResult.Warning(PurviewSvc, feature,
                observation: "Retention label presence could not be verified via Graph (requires Purview portal or Exchange PowerShell).",
                recommendation: "Verify retention label publication and policies in Microsoft Purview > Data lifecycle management > Retention labels.",
                linkText: "Retention labels",
                linkUrl: "https://learn.microsoft.com/purview/retention"),
        };
        return list;
    }

    // ================================================================
    //  Purview — Customer Lockbox
    // ================================================================

    private static List<RecommendationResult> GenerateCustomerLockbox(DataInsights ins)
    {
        const string feature = "Customer Lockbox";
        var list = new List<RecommendationResult>
        {
            RecommendationResult.Warning(PurviewSvc, feature,
                observation: "Customer Lockbox status could not be verified via Graph.",
                recommendation: "Enable Customer Lockbox in M365 admin center > Settings > Security & privacy if subject to regulated compliance.",
                linkText: "Customer Lockbox",
                linkUrl: "https://learn.microsoft.com/purview/customer-lockbox-requests"),
        };
        return list;
    }

    // ================================================================
    //  Purview — Information Barriers
    // ================================================================

    private static List<RecommendationResult> GenerateInformationBarriers(DataInsights ins)
    {
        const string feature = "Information Barriers";
        var list = new List<RecommendationResult>();

        if (!ins.InsiderRiskLicensed) return list;

        list.Add(RecommendationResult.Warning(PurviewSvc, feature,
            observation: "Information Barriers licensed but deployment status not verifiable via Graph.",
            recommendation: "Configure Information Barrier policies if business units must not exchange data (legal ethical walls, trading floor).",
            linkText: "Information Barriers",
            linkUrl: "https://learn.microsoft.com/purview/information-barriers"));

        return list;
    }

    // ================================================================
    //  Purview — Insider Risk Management
    // ================================================================

    private static List<RecommendationResult> GenerateInsiderRisk(DataInsights ins)
    {
        const string feature = "Insider Risk Management";
        const string linkText = "Insider Risk Management";
        const string linkUrl = "https://learn.microsoft.com/purview/insider-risk-management";
        var list = new List<RecommendationResult>();

        if (!ins.InsiderRiskLicensed)
        {
            list.Add(RecommendationResult.Warning(PurviewSvc, feature,
                observation: "Insider Risk Management not licensed — Copilot usage exfiltration detection unavailable.",
                recommendation: "Consider M365 E5 Compliance for Insider Risk policies on Copilot misuse + data theft scenarios.",
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Warning(PurviewSvc, feature,
                observation: "Insider Risk licensed but deployment not verifiable via Graph.",
                recommendation: "Deploy Insider Risk policies in Purview portal — start with Data Leaks and Departing Employee templates.",
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }

    // ================================================================
    //  SharePoint — Unlabeled sensitive content
    // ================================================================

    private static List<RecommendationResult> GenerateUnlabeledContent(DataInsights ins)
    {
        const string feature = "Unlabeled Sensitive Content";
        var list = new List<RecommendationResult>();

        if (ins.TotalFilesScanned == 0) return list;

        if (ins.UnlabeledFiles > 0)
        {
            list.Add(RecommendationResult.Warning(SharePointSvc, feature,
                observation: $"{ins.UnlabeledFiles.ToString(Inv)} unlabeled file(s) scanned across {ins.TotalSitesScanned.ToString(Inv)} site(s).",
                recommendation: "Target unlabeled sites for auto-labeling rollout, prioritizing sites flagged High Risk.",
                linkText: "Auto-labeling for SharePoint",
                linkUrl: "https://learn.microsoft.com/purview/apply-sensitivity-label-automatically"));
        }

        return list;
    }

    // ================================================================
    //  OneDrive — Storage hoarding
    // ================================================================

    private static List<RecommendationResult> GenerateOneDriveHoarding(DataInsights ins)
    {
        const string feature = "OneDrive Storage Hoarding";
        const string linkText = "OneDrive storage management";
        const string linkUrl = "https://learn.microsoft.com/onedrive/manage-user-storage";
        var list = new List<RecommendationResult>();

        if (ins.OneDriveActiveAccounts == 0) return list;

        if (ins.OneDriveAvgGBPerUser > 100)
        {
            list.Add(RecommendationResult.Warning(OneDriveSvc, feature,
                observation: $"Average OneDrive consumption is {ins.OneDriveAvgGBPerUser.ToString("F1", Inv)} GB per active account across {ins.OneDriveActiveAccounts.ToString(Inv)} users.",
                recommendation: "Inspect heavy-storage accounts for data hoarding; enforce retention, archive or offboard stale data.",
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Success(OneDriveSvc, feature,
                observation: $"Average OneDrive usage is {ins.OneDriveAvgGBPerUser.ToString("F1", Inv)} GB per account.",
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }
}
