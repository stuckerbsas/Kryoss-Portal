using KryossApi.Services.CloudAssessment.Pipelines;
using KryossApi.Services.CopilotReadiness.Pipelines;

namespace KryossApi.Services.CloudAssessment;

public static class FeatureInventoryBuilder
{
    public static List<FeatureInventoryEntry> Build(
        PipelineResult identity,
        PipelineResult endpoint,
        PipelineResult data,
        PipelineResult productivity,
        PipelineResult azure,
        PipelineResult powerbi,
        PipelineResult mailFlow,
        bool graphConnected,
        bool azureConnected,
        bool pbiConnected)
    {
        var list = new List<FeatureInventoryEntry>();

        // ── Level 1: Connections ──
        list.Add(new("connections", "M365 Tenant (Graph)", graphConnected, graphConnected, null, null));
        list.Add(new("connections", "Azure (ARM)", azureConnected, azureConnected, null, null));
        list.Add(new("connections", "Power BI", pbiConnected, pbiConnected, null, null));

        // ── Level 2+3: Identity ──
        var idIns = identity.Insights as IdentityInsights;
        if (idIns is not null)
        {
            list.Add(Entry("identity", "Conditional Access", "Entra ID P1+",
                idIns.CaPolicyTotal > 0, idIns.CaPolicyEnabled > 0,
                idIns.CaPolicyTotal > 0 ? Pct(idIns.CaPolicyEnabled, idIns.CaPolicyTotal) : null,
                idIns.CaPolicyTotal > 0 ? $"{idIns.CaPolicyEnabled} enabled / {idIns.CaPolicyTotal} total" : null));

            list.Add(Entry("identity", "MFA", "Entra ID P1+",
                idIns.TotalUsers > 0 && idIns.MfaRegistered >= 0, idIns.MfaRegistered > 0,
                idIns.TotalUsers > 0 ? Pct(idIns.MfaRegistered, idIns.TotalUsers) : null,
                idIns.TotalUsers > 0 ? $"{idIns.MfaRegistered} / {idIns.TotalUsers} users" : null));

            list.Add(Entry("identity", "Risky Users", "Entra ID P2",
                idIns.RiskDetectionsTotal >= 0 && (idIns.RiskyUsersHigh > 0 || idIns.RiskyUsersMedium > 0 || idIns.RiskyUsersLow > 0 || idIns.RiskDetectionsTotal == 0),
                idIns.UserRiskPolicyExists || idIns.SignInRiskPolicyExists,
                null,
                idIns.RiskyUsersHigh + idIns.RiskyUsersMedium > 0
                    ? $"{idIns.RiskyUsersHigh} high, {idIns.RiskyUsersMedium} medium risk"
                    : "No risky users detected"));

            list.Add(Entry("identity", "PIM", "Entra ID P2 / Governance",
                idIns.EligibleAssignments >= 0 && (idIns.EligibleAssignments > 0 || idIns.PermanentAssignmentsTotal > 0),
                idIns.EligibleAssignments > 0,
                idIns.PermanentAssignmentsTotal + idIns.EligibleAssignments > 0
                    ? Pct(idIns.EligibleAssignments, idIns.PermanentAssignmentsTotal + idIns.EligibleAssignments) : null,
                $"{idIns.EligibleAssignments} eligible, {idIns.PermanentAssignmentsTotal} permanent"));

            list.Add(Entry("identity", "Access Reviews", "Entra ID P2 / Governance",
                idIns.AccessReviewTotal >= 0, idIns.AccessReviewTotal > 0,
                null,
                idIns.AccessReviewTotal > 0
                    ? $"{idIns.AccessReviewActive} active / {idIns.AccessReviewTotal} total, {idIns.AccessReviewRecurring} recurring"
                    : null));

            list.Add(Entry("identity", "Sign-in Logs", "Entra ID P1/P2",
                idIns.LegacyAuthSignIns >= 0 || idIns.FailedSignIns >= 0,
                true,
                null,
                $"{idIns.FailedSignIns} failed, {idIns.LegacyAuthSignIns} legacy auth"));

            bool deviceMgmtAvailable = idIns.DevicesTotalManaged > 0 || idIns.CompliancePoliciesCount > 0;
            list.Add(Entry("identity", "Device Management", "Intune",
                deviceMgmtAvailable, idIns.DevicesTotalManaged > 0,
                idIns.DevicesTotalManaged > 0 ? Pct(idIns.DevicesCompliant, idIns.DevicesTotalManaged) : null,
                idIns.DevicesTotalManaged > 0 ? $"{idIns.DevicesCompliant} compliant / {idIns.DevicesTotalManaged} managed" : null));

            list.Add(Entry("identity", "Global Secure Access", null,
                idIns.FilteringPolicies.HasValue, idIns.FilteringPolicies.GetValueOrDefault() > 0,
                null,
                idIns.FilteringPolicies.HasValue
                    ? $"{idIns.FilteringPolicies} policies, {idIns.ForwardingProfilesCount} profiles"
                    : null));
        }

        // ── Level 2+3: Endpoint ──
        var epIns = endpoint.Insights as EndpointInsights;
        if (epIns is not null)
        {
            list.Add(Entry("endpoint", "Intune Compliance", "Intune",
                epIns.IntuneAvailable, epIns.DeviceCompliancePolicyCount > 0,
                epIns.DevicesTotal > 0 ? Pct(epIns.DevicesCompliant, epIns.DevicesTotal) : null,
                epIns.IntuneAvailable ? $"{epIns.DevicesCompliant} compliant / {epIns.DevicesTotal} devices, {epIns.DeviceCompliancePolicyCount} policies" : null));

            list.Add(Entry("endpoint", "Config Profiles", "Intune",
                epIns.IntuneAvailable && epIns.DeviceConfigProfileCount >= 0,
                epIns.DeviceConfigProfileCount > 0,
                null,
                epIns.DeviceConfigProfileCount > 0 ? $"{epIns.DeviceConfigProfileCount} profiles" : null));

            list.Add(Entry("endpoint", "Config Profile Drift", "Intune",
                epIns.ConfigProfilesAssigned > 0, epIns.ConfigProfilesAssigned > 0,
                epIns.ConfigProfilesAssigned > 0 ? Pct(epIns.ConfigProfilesSucceeded, epIns.ConfigProfilesAssigned) : null,
                epIns.ConfigProfilesAssigned > 0
                    ? $"{epIns.ConfigProfilesSucceeded} ok, {epIns.ConfigProfilesFailed} failed, {epIns.ConfigProfilesConflict} conflict"
                    : null));

            list.Add(Entry("endpoint", "App Protection", "Intune",
                epIns.IntuneAvailable,
                epIns.AppProtectionPoliciesIOS > 0 || epIns.AppProtectionPoliciesAndroid > 0,
                null,
                epIns.IntuneAvailable ? $"iOS: {epIns.AppProtectionPoliciesIOS}, Android: {epIns.AppProtectionPoliciesAndroid}" : null));

            list.Add(Entry("endpoint", "Autopilot", "Intune",
                epIns.IntuneAvailable, epIns.AutopilotProfileCount > 0,
                null,
                epIns.AutopilotProfileCount > 0 ? $"{epIns.AutopilotProfileCount} profiles" : null));

            list.Add(Entry("endpoint", "Defender for Endpoint", "Defender",
                epIns.DefenderEndpointAvailable, epIns.MachinesTotal > 0,
                null,
                epIns.DefenderEndpointAvailable
                    ? $"{epIns.MachinesTotal} machines ({epIns.MachinesHighRisk} high risk), {epIns.VulnCritical} critical vulns"
                    : null));
        }

        // ── Level 2+3: Data ──
        var dataIns = data.Insights as DataInsights;
        if (dataIns is not null)
        {
            list.Add(Entry("data", "Purview / DLP", "M365 E5 / Purview",
                dataIns.PurviewAvailable, dataIns.SensitivityLabelCount > 0 || dataIns.DlpLicensed,
                dataIns.TotalFilesScanned > 0 ? Pct(dataIns.LabeledFiles, dataIns.TotalFilesScanned) : null,
                dataIns.PurviewAvailable
                    ? $"{dataIns.SensitivityLabelCount} labels, {dataIns.LabelPoliciesCount} policies"
                    : null));

            list.Add(Entry("data", "SharePoint", "SharePoint",
                dataIns.SharePointAvailable, dataIns.TotalSitesScanned > 0,
                null,
                dataIns.SharePointAvailable
                    ? $"{dataIns.TotalSitesScanned} sites, {dataIns.HighRiskSites} high risk"
                    : null));

            list.Add(Entry("data", "OneDrive", null,
                dataIns.OneDriveAvailable, dataIns.OneDriveActiveAccounts > 0,
                null,
                dataIns.OneDriveAvailable
                    ? $"{dataIns.OneDriveActiveAccounts} active, {dataIns.OneDriveTotalGB:F0} GB"
                    : null));
        }

        // ── Level 2+3: Productivity ──
        var prodIns = productivity.Insights as ProductivityInsights;
        if (prodIns is not null)
        {
            list.Add(Entry("productivity", "Exchange Online", null,
                prodIns.EmailReportAvailable, prodIns.EmailActive30d > 0,
                prodIns.EmailLicensedCount > 0 ? Pct(prodIns.EmailActive30d, prodIns.EmailLicensedCount) : null,
                prodIns.EmailReportAvailable ? $"{prodIns.EmailActive30d} active / {prodIns.EmailLicensedCount} licensed" : null));

            list.Add(Entry("productivity", "Teams", null,
                prodIns.TeamsReportAvailable, prodIns.TeamsActive30d > 0,
                prodIns.TeamsLicensedCount > 0 ? Pct(prodIns.TeamsActive30d, prodIns.TeamsLicensedCount) : null,
                prodIns.TeamsReportAvailable ? $"{prodIns.TeamsActive30d} active / {prodIns.TeamsLicensedCount} licensed" : null));

            list.Add(Entry("productivity", "SharePoint (Productivity)", null,
                prodIns.SharePointReportAvailable, prodIns.SharePointActive30d > 0,
                prodIns.SharePointLicensedCount > 0 ? Pct(prodIns.SharePointActive30d, prodIns.SharePointLicensedCount) : null,
                null));

            list.Add(Entry("productivity", "OneDrive (Productivity)", null,
                prodIns.OneDriveReportAvailable, prodIns.OneDriveActive30d > 0,
                prodIns.OneDriveLicensedCount > 0 ? Pct(prodIns.OneDriveActive30d, prodIns.OneDriveLicensedCount) : null,
                null));

            list.Add(Entry("productivity", "Copilot", "Copilot license",
                prodIns.CopilotLicensesPurchased > 0, prodIns.CopilotLicensesAssigned > 0,
                prodIns.CopilotLicensesPurchased > 0 ? Pct(prodIns.CopilotLicensesAssigned, prodIns.CopilotLicensesPurchased) : null,
                prodIns.CopilotLicensesPurchased > 0
                    ? $"{prodIns.CopilotLicensesAssigned} assigned / {prodIns.CopilotLicensesPurchased} purchased"
                    : null));
        }

        // ── Level 2+3: Mail Flow ──
        var mfIns = mailFlow.Insights as MailFlowInsights;
        if (mfIns is not null)
        {
            int domainsTotal = mfIns.DomainResults.Count;
            int domainsWithDmarc = mfIns.DomainResults.Count(d => d.DmarcValid);
            int domainsWithSpf = mfIns.DomainResults.Count(d => d.SpfValid);
            list.Add(Entry("mail_flow", "Email Security (SPF/DKIM/DMARC)", null,
                domainsTotal > 0, domainsWithDmarc > 0,
                domainsTotal > 0 ? Pct(domainsWithDmarc, domainsTotal) : null,
                domainsTotal > 0 ? $"{domainsWithSpf} SPF ok, {domainsWithDmarc} DMARC ok / {domainsTotal} domains" : null));
        }

        // ── Level 2+3: Azure ──
        if (azure.Insights is AzureInsights azIns && azure.Status != "skipped")
        {
            list.Add(Entry("azure", "Azure Resources", "Azure subscription",
                true, azIns.ResourcesTotal > 0,
                null,
                $"{azIns.ResourcesTotal} resources across {azIns.SubscriptionsScanned} subscriptions"));

            list.Add(Entry("azure", "Defender for Cloud", "Defender for Cloud",
                azIns.SecureScorePct.HasValue, azIns.AssessmentsHealthy + azIns.AssessmentsUnhealthy > 0,
                azIns.SecureScorePct.HasValue ? (int)azIns.SecureScorePct.Value : null,
                azIns.SecureScorePct.HasValue
                    ? $"Score {azIns.SecureScorePct:F0}%, {azIns.AssessmentsUnhealthy} unhealthy"
                    : null));
        }

        // ── Level 2+3: Power BI ──
        if (powerbi.Status != "skipped")
        {
            var pbiMetrics = powerbi.Metrics;
            int.TryParse(pbiMetrics.GetValueOrDefault("workspace_count", "0"), out var wsCount);
            int.TryParse(pbiMetrics.GetValueOrDefault("dataset_count", "0"), out var dsCount);
            int.TryParse(pbiMetrics.GetValueOrDefault("gateway_count", "0"), out var gwCount);

            list.Add(Entry("powerbi", "Power BI Governance", "Power BI Pro+",
                true, wsCount > 0,
                null,
                $"{wsCount} workspaces, {dsCount} datasets, {gwCount} gateways"));
        }

        // Mark entries whose pipeline data was absent (403/skipped) as not_licensed
        MarkSkippedAsNotLicensed(list, idIns, epIns, dataIns);

        return list;
    }

    private static void MarkSkippedAsNotLicensed(
        List<FeatureInventoryEntry> list,
        IdentityInsights? idIns,
        EndpointInsights? epIns,
        DataInsights? dataIns)
    {
        if (idIns is null) return;

        // If PIM data wasn't collected (all zeros + no permanent), assume no license
        if (idIns.PermanentAssignmentsTotal == 0 && idIns.EligibleAssignments == 0 && idIns.PimSkippedReason is not null)
            SetNotLicensed(list, "identity", "PIM");

        if (idIns.AccessReviewTotal == 0 && idIns.AccessReviewActive == 0)
        {
            // Could be "has license but no reviews" or "no license" — we check if the insights has any signal
            // Access reviews with 0 total but no skip reason means license exists, just unused
        }

        if (epIns is not null && !epIns.IntuneAvailable)
        {
            SetNotLicensed(list, "endpoint", "Intune Compliance");
            SetNotLicensed(list, "endpoint", "Config Profiles");
            SetNotLicensed(list, "endpoint", "Config Profile Drift");
            SetNotLicensed(list, "endpoint", "App Protection");
            SetNotLicensed(list, "endpoint", "Autopilot");
        }
        if (epIns is not null && !epIns.DefenderEndpointAvailable)
            SetNotLicensed(list, "endpoint", "Defender for Endpoint");

        if (dataIns is not null && !dataIns.PurviewAvailable)
            SetNotLicensed(list, "data", "Purview / DLP");
        if (dataIns is not null && !dataIns.SharePointAvailable)
            SetNotLicensed(list, "data", "SharePoint");
    }

    private static void SetNotLicensed(List<FeatureInventoryEntry> list, string area, string feature)
    {
        var entry = list.FirstOrDefault(e => e.Area == area && e.Feature == feature);
        if (entry is not null)
        {
            entry.Licensed = false;
            entry.Implemented = false;
            entry.AdoptionPct = null;
            entry.Detail = null;
        }
    }

    private static FeatureInventoryEntry Entry(
        string area, string feature, string? licenseRequired,
        bool licensed, bool implemented,
        int? adoptionPct, string? detail) =>
        new(area, feature, licensed, implemented, adoptionPct, detail) { LicenseRequired = licenseRequired };

    private static int Pct(int numerator, int denominator) =>
        denominator > 0 ? (int)Math.Round(100.0 * numerator / denominator) : 0;
}

public class FeatureInventoryEntry
{
    public string Area { get; set; }
    public string Feature { get; set; }
    public bool Licensed { get; set; }
    public bool Implemented { get; set; }
    public int? AdoptionPct { get; set; }
    public string? Detail { get; set; }
    public string? LicenseRequired { get; set; }

    public FeatureInventoryEntry(string area, string feature, bool licensed, bool implemented, int? adoptionPct, string? detail)
    {
        Area = area;
        Feature = feature;
        Licensed = licensed;
        Implemented = implemented;
        AdoptionPct = adoptionPct;
        Detail = detail;
    }
}
