using System.Globalization;
using KryossApi.Services.CloudAssessment.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;

namespace KryossApi.Services.CloudAssessment.Recommendations;

/// <summary>
/// Generates Cloud Assessment (CA-4) productivity recommendations from a
/// pre-computed <see cref="ProductivityInsights"/> bag.
///
/// Category A — License-only checks (~15, dynamic):
///   One finding per SKU in <see cref="ProductivityInsights.Licenses"/>.
///   Success = seats assigned; Warning = purchased but none assigned.
///
/// Category B — Enriched checks (10 fixed):
///   B1.  Microsoft 365 Copilot Adoption          (service = copilot)
///   B2.  Exchange Online Adoption                 (service = exchange)
///   B3.  Microsoft Teams Adoption                 (service = teams)
///   B4.  SharePoint Deployment                    (service = sharepoint)
///   B5.  OneDrive for Business Adoption           (service = onedrive)
///   B6.  Microsoft 365 Apps Desktop Adoption      (service = office)
///   B7.  Wasted Licenses                          (service = licensing)
///   B8.  Wasted Copilot Licenses                  (service = licensing, conditional)
///   B9.  External Guest Ratio                     (service = identity)
///   B10. Graph Connectors for Copilot             (service = copilot, conditional)
/// </summary>
public static class ProductivityRecommendations
{
    private const string ProductivitySvc = "productivity";
    private const string LicensingSvc    = "licensing";
    private const string ExchangeSvc     = "exchange";
    private const string TeamsSvc        = "teams";
    private const string SharePointSvc   = "sharepoint";
    private const string OneDriveSvc     = "onedrive";
    private const string OfficeSvc       = "office";
    private const string IdentitySvc     = "identity";
    private const string CopilotSvc      = "copilot";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ----------------------------------------------------------------
    //  Public entry point
    // ----------------------------------------------------------------

    public static List<RecommendationResult> Generate(ProductivityInsights ins)
    {
        var all = new List<RecommendationResult>();
        all.AddRange(GenerateLicenseChecks(ins));
        all.AddRange(GenerateCopilotAdoption(ins));
        all.AddRange(GenerateEmailAdoption(ins));
        all.AddRange(GenerateTeamsAdoption(ins));
        all.AddRange(GenerateSharePointDeployment(ins));
        all.AddRange(GenerateOneDriveAdoption(ins));
        all.AddRange(GenerateOfficeDesktopAdoption(ins));
        all.AddRange(GenerateWastedLicenses(ins));
        all.AddRange(GenerateWastedCopilotLicenses(ins));
        all.AddRange(GenerateGuestRatio(ins));
        all.AddRange(GenerateGraphConnectors(ins));
        return all;
    }

    // ================================================================
    //  Category A — License-only (one per SKU)
    // ================================================================

    private static List<RecommendationResult> GenerateLicenseChecks(ProductivityInsights ins)
    {
        const string linkText = "Microsoft 365 licensing";
        const string linkUrl  = "https://learn.microsoft.com/microsoft-365/commerce/licenses/";

        var list = new List<RecommendationResult>();

        foreach (var row in ins.Licenses)
        {
            if (row.Purchased == 0) continue;

            string friendlyName = string.IsNullOrWhiteSpace(row.FriendlyName)
                ? SkuFriendlyNames.Resolve(row.SkuPartNumber)
                : row.FriendlyName;

            if (row.Assigned > 0)
            {
                double util = (double)row.Assigned / row.Purchased * 100.0;
                list.Add(RecommendationResult.Success(LicensingSvc, $"{friendlyName} Licensing",
                    observation: $"{friendlyName} has {row.Assigned.ToString(Inv)}/{row.Purchased.ToString(Inv)} seats assigned ({util.ToString("F1", Inv)}% utilization).",
                    linkText: linkText,
                    linkUrl: linkUrl));
            }
            else
            {
                list.Add(RecommendationResult.Warning(LicensingSvc, $"{friendlyName} Licensing",
                    observation: $"{friendlyName}: {row.Purchased.ToString(Inv)} seats purchased but none assigned — unused license.",
                    recommendation: "Assign the license to users or return seats to reduce cost.",
                    linkText: linkText,
                    linkUrl: linkUrl));
            }
        }

        return list;
    }

    // ================================================================
    //  B1. Copilot adoption
    // ================================================================

    private static List<RecommendationResult> GenerateCopilotAdoption(ProductivityInsights ins)
    {
        const string feature  = "Microsoft 365 Copilot Adoption";
        const string linkText = "Microsoft 365 Copilot adoption";
        const string linkUrl  = "https://learn.microsoft.com/microsoft-365-copilot/microsoft-365-copilot-adoption";

        if (ins.CopilotLicensesPurchased == 0)
        {
            return
            [
                RecommendationResult.Success(CopilotSvc, feature,
                    observation: "No Copilot licenses purchased.",
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        double pct = (double)ins.CopilotLicensesAssigned / ins.CopilotLicensesPurchased * 100.0;

        if (pct >= 80.0)
        {
            return
            [
                RecommendationResult.Success(CopilotSvc, feature,
                    observation: $"Strong Copilot adoption: {ins.CopilotLicensesAssigned.ToString(Inv)}/{ins.CopilotLicensesPurchased.ToString(Inv)} seats assigned ({pct.ToString("F1", Inv)}%).",
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        if (pct >= 50.0)
        {
            return
            [
                RecommendationResult.Warning(CopilotSvc, feature,
                    observation: $"Copilot adoption at {pct.ToString("F1", Inv)}% — scale deployment to reach 80%+ target.",
                    recommendation: "Assign remaining Copilot seats to knowledge workers and drive enablement sessions.",
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        return
        [
            RecommendationResult.ActionRequired(CopilotSvc, feature, "high",
                observation: $"Copilot adoption is only {pct.ToString("F1", Inv)}% ({ins.CopilotLicensesAssigned.ToString(Inv)}/{ins.CopilotLicensesPurchased.ToString(Inv)}). Unused Copilot seats are expensive.",
                recommendation: "Assign remaining seats to active knowledge workers, run Champions program, and set a 90-day adoption target.",
                linkText: linkText,
                linkUrl: linkUrl),
        ];
    }

    // ================================================================
    //  B2. Email adoption gap
    // ================================================================

    private static List<RecommendationResult> GenerateEmailAdoption(ProductivityInsights ins)
    {
        const string feature  = "Exchange Online Adoption";
        const string linkText = "Exchange Online activity reports";
        const string linkUrl  = "https://learn.microsoft.com/microsoft-365/admin/activity-reports/email-activity";

        if (ins.EmailLicensedCount <= 0)
            return [];

        int inactive = ins.EmailLicensedCount - ins.EmailActive30d;
        double gap   = (double)inactive / ins.EmailLicensedCount * 100.0;

        if (gap > 20.0)
        {
            return
            [
                RecommendationResult.Warning(ExchangeSvc, feature,
                    observation: $"Email adoption gap: {inactive.ToString(Inv)} of {ins.EmailLicensedCount.ToString(Inv)} licensed users were inactive in the last 30 days ({gap.ToString("F1", Inv)}% gap).",
                    recommendation: "Run user training, verify licenses are assigned to active staff, offboard dormant accounts.",
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        return
        [
            RecommendationResult.Success(ExchangeSvc, feature,
                observation: $"Email adoption is healthy: {ins.EmailActive30d.ToString(Inv)}/{ins.EmailLicensedCount.ToString(Inv)} licensed users active in the last 30 days ({(100.0 - gap).ToString("F1", Inv)}%).",
                linkText: linkText,
                linkUrl: linkUrl),
        ];
    }

    // ================================================================
    //  B3. Teams adoption gap
    // ================================================================

    private static List<RecommendationResult> GenerateTeamsAdoption(ProductivityInsights ins)
    {
        const string feature  = "Microsoft Teams Adoption";
        const string linkText = "Teams adoption guide";
        const string linkUrl  = "https://adoption.microsoft.com/microsoft-teams/";

        if (ins.TeamsLicensedCount <= 0)
            return [];

        int inactive = ins.TeamsLicensedCount - ins.TeamsActive30d;
        double gap   = (double)inactive / ins.TeamsLicensedCount * 100.0;

        if (gap > 20.0)
        {
            return
            [
                RecommendationResult.Warning(TeamsSvc, feature,
                    observation: $"Teams adoption gap: {inactive.ToString(Inv)} of {ins.TeamsLicensedCount.ToString(Inv)} licensed users were inactive in the last 30 days ({gap.ToString("F1", Inv)}% gap).",
                    recommendation: "Run user training, verify licenses are assigned to active staff, offboard dormant accounts.",
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        return
        [
            RecommendationResult.Success(TeamsSvc, feature,
                observation: $"Teams adoption is healthy: {ins.TeamsActive30d.ToString(Inv)}/{ins.TeamsLicensedCount.ToString(Inv)} licensed users active in the last 30 days ({(100.0 - gap).ToString("F1", Inv)}%).",
                linkText: linkText,
                linkUrl: linkUrl),
        ];
    }

    // ================================================================
    //  B4. SharePoint site count
    // ================================================================

    private static List<RecommendationResult> GenerateSharePointDeployment(ProductivityInsights ins)
    {
        const string feature  = "SharePoint Deployment";
        const string linkText = "SharePoint adoption";
        const string linkUrl  = "https://adoption.microsoft.com/sharepoint/";

        if (ins.SharePointSiteCount == 0)
        {
            return
            [
                RecommendationResult.Warning(SharePointSvc, feature,
                    observation: "No SharePoint sites detected — tenant is not using SharePoint.",
                    recommendation: "Create team sites for departments and migrate shared drives to SharePoint to enable Copilot grounding over internal content.",
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        return
        [
            RecommendationResult.Success(SharePointSvc, feature,
                observation: $"{ins.SharePointSiteCount.ToString(Inv)} SharePoint sites active.",
                linkText: linkText,
                linkUrl: linkUrl),
        ];
    }

    // ================================================================
    //  B5. OneDrive adoption gap
    // ================================================================

    private static List<RecommendationResult> GenerateOneDriveAdoption(ProductivityInsights ins)
    {
        const string feature  = "OneDrive for Business Adoption";
        const string linkText = "OneDrive adoption";
        const string linkUrl  = "https://adoption.microsoft.com/onedrive/";

        if (ins.OneDriveLicensedCount <= 0)
            return [];

        int inactive = ins.OneDriveLicensedCount - ins.OneDriveActive30d;
        double gap   = (double)inactive / ins.OneDriveLicensedCount * 100.0;

        if (gap > 30.0)
        {
            return
            [
                RecommendationResult.Warning(OneDriveSvc, feature,
                    observation: $"OneDrive adoption gap: {inactive.ToString(Inv)}/{ins.OneDriveLicensedCount.ToString(Inv)} licensed accounts inactive in 30 days ({gap.ToString("F1", Inv)}%).",
                    recommendation: "Deploy OneDrive Known Folder Move, run adoption campaign.",
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        return
        [
            RecommendationResult.Success(OneDriveSvc, feature,
                observation: $"OneDrive adoption is healthy: {ins.OneDriveActive30d.ToString(Inv)}/{ins.OneDriveLicensedCount.ToString(Inv)} licensed accounts active in the last 30 days ({(100.0 - gap).ToString("F1", Inv)}%).",
                linkText: linkText,
                linkUrl: linkUrl),
        ];
    }

    // ================================================================
    //  B6. Office desktop activation gap
    // ================================================================

    private static List<RecommendationResult> GenerateOfficeDesktopAdoption(ProductivityInsights ins)
    {
        const string feature  = "Microsoft 365 Apps Desktop Adoption";
        const string linkText = "Office deployment";
        const string linkUrl  = "https://learn.microsoft.com/deployoffice/deployment-guide-microsoft-365-apps";

        double rate = ins.OfficeDesktopAdoptionRate;

        if (rate < 70.0)
        {
            return
            [
                RecommendationResult.Warning(OfficeSvc, feature,
                    observation: $"Only {rate.ToString("F1", Inv)}% of users have desktop Office activated. Desktop clients provide the richest Copilot integration.",
                    recommendation: "Deploy Microsoft 365 Apps via Intune or SCCM and ensure activation is tracked via the admin center.",
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        return
        [
            RecommendationResult.Success(OfficeSvc, feature,
                observation: $"Desktop Office activation rate is {rate.ToString("F1", Inv)}%.",
                linkText: linkText,
                linkUrl: linkUrl),
        ];
    }

    // ================================================================
    //  B7. Wasted licenses
    // ================================================================

    private static List<RecommendationResult> GenerateWastedLicenses(ProductivityInsights ins)
    {
        const string feature  = "Wasted Licenses";
        const string rec      = "Reassign or revoke these licenses to reduce cost.";
        const string linkText = "License optimization";
        const string linkUrl  = "https://learn.microsoft.com/microsoft-365/commerce/licenses/manage-licenses";

        if (ins.WastedLicenseCount > 5)
        {
            return
            [
                RecommendationResult.ActionRequired(LicensingSvc, feature, "high",
                    observation: $"{ins.WastedLicenseCount.ToString(Inv)} users have assigned licenses but have not signed in for 30+ days.",
                    recommendation: rec,
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        if (ins.WastedLicenseCount > 1)
        {
            return
            [
                RecommendationResult.Warning(LicensingSvc, feature,
                    observation: $"{ins.WastedLicenseCount.ToString(Inv)} users have assigned licenses but have not signed in for 30+ days.",
                    recommendation: rec,
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        return
        [
            RecommendationResult.Success(LicensingSvc, feature,
                observation: "No wasted licenses detected — all licensed users were active in the last 30 days.",
                linkText: linkText,
                linkUrl: linkUrl),
        ];
    }

    // ================================================================
    //  B8. Wasted Copilot licenses (conditional — skip if zero)
    // ================================================================

    private static List<RecommendationResult> GenerateWastedCopilotLicenses(ProductivityInsights ins)
    {
        const string feature  = "Wasted Copilot Licenses";
        const string linkText = "Copilot adoption";
        const string linkUrl  = "https://learn.microsoft.com/microsoft-365-copilot/microsoft-365-copilot-adoption";

        int wastedCopilot = ins.WastedLicenses
            .Count(w => w.Sku != null &&
                        w.Sku.Contains("copilot", StringComparison.OrdinalIgnoreCase));

        if (wastedCopilot == 0)
            return [];

        return
        [
            RecommendationResult.ActionRequired(LicensingSvc, feature, "critical",
                observation: $"{wastedCopilot.ToString(Inv)} Microsoft 365 Copilot seat(s) are assigned to users inactive for 30+ days — at ~$30/seat/month this is expensive unused spend.",
                recommendation: "Reassign Copilot seats to active knowledge workers or remove the licenses.",
                linkText: linkText,
                linkUrl: linkUrl),
        ];
    }

    // ================================================================
    //  B9. Guest user ratio
    // ================================================================

    private static List<RecommendationResult> GenerateGuestRatio(ProductivityInsights ins)
    {
        const string feature  = "External Guest Ratio";
        const string linkText = "Entra external collaboration";
        const string linkUrl  = "https://learn.microsoft.com/entra/external-id/external-identities-overview";

        if (ins.TotalUsers <= 0)
            return [];

        double ratio = (double)ins.GuestUserCount / ins.TotalUsers * 100.0;

        if (ratio > 20.0)
        {
            return
            [
                RecommendationResult.Warning(IdentitySvc, feature,
                    observation: $"{ins.GuestUserCount.ToString(Inv)} guest users out of {ins.TotalUsers.ToString(Inv)} total ({ratio.ToString("F1", Inv)}% guest ratio) — elevated external exposure.",
                    recommendation: "Run access review, enforce quarterly recertification, restrict guest invite permissions.",
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        return
        [
            RecommendationResult.Success(IdentitySvc, feature,
                observation: $"Guest ratio is {ratio.ToString("F1", Inv)}% ({ins.GuestUserCount.ToString(Inv)} guest(s) out of {ins.TotalUsers.ToString(Inv)} total users).",
                linkText: linkText,
                linkUrl: linkUrl),
        ];
    }

    // ================================================================
    //  B10. Graph Connectors for Copilot (conditional — skip if no Copilot)
    // ================================================================

    private static List<RecommendationResult> GenerateGraphConnectors(ProductivityInsights ins)
    {
        const string feature  = "Graph Connectors for Copilot";
        const string linkText = "Graph Connectors documentation";
        const string linkUrl  = "https://learn.microsoft.com/graph/connecting-external-content-connectors-overview";

        if (ins.CopilotLicensesPurchased <= 0)
            return [];

        if (ins.GraphConnectorsCount == 0)
        {
            return
            [
                RecommendationResult.Warning(CopilotSvc, feature,
                    observation: "Copilot is licensed but zero Graph Connectors deployed — Copilot is limited to Microsoft 365 content.",
                    recommendation: "Deploy Graph Connectors to ServiceNow, Salesforce, Jira, or other line-of-business systems.",
                    linkText: linkText,
                    linkUrl: linkUrl),
            ];
        }

        return
        [
            RecommendationResult.Success(CopilotSvc, feature,
                observation: $"{ins.GraphConnectorsCount.ToString(Inv)} Graph Connector(s) deployed — Copilot can search external sources.",
                linkText: linkText,
                linkUrl: linkUrl),
        ];
    }
}
