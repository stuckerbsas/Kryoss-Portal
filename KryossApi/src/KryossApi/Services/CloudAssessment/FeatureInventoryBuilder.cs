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
        bool pbiConnected,
        Dictionary<string, string>? skuPlans = null)
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

            list.Add(Entry("identity", "Entra Device Join", null,
                idIns.DeviceJoinConfigured, idIns.DeviceJoinConfigured,
                null,
                idIns.DeviceJoinConfigured
                    ? $"Scope: {idIns.DeviceJoinScope ?? "unknown"}"
                    : "Device join policy not configured"));

            list.Add(Entry("identity", "SSPR", null,
                idIns.TotalUsers > 0, idIns.SsprEnabled,
                idIns.TotalUsers > 0 ? Pct(idIns.SsprRegisteredUsers, idIns.TotalUsers) : null,
                idIns.SsprEnabled
                    ? $"{idIns.SsprRegisteredUsers} / {idIns.TotalUsers} registered"
                    : "Self-service password reset not enabled"));

            list.Add(Entry("identity", "Break-Glass Accounts", null,
                true, idIns.BreakGlassAccountCount > 0,
                null,
                idIns.BreakGlassAccountCount > 0
                    ? $"{idIns.BreakGlassAccountCount} emergency access accounts detected"
                    : "No break-glass accounts found in Global Admin role"));

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

            list.Add(Entry("endpoint", "Defender Antivirus Policy", "Intune",
                epIns.IntuneAvailable, epIns.HasAntivirusPolicy,
                null,
                epIns.HasAntivirusPolicy ? "Antivirus policy configured" : "No antivirus policy found"));

            list.Add(Entry("endpoint", "Defender Firewall Policy", "Intune",
                epIns.IntuneAvailable, epIns.HasFirewallPolicy,
                null,
                epIns.HasFirewallPolicy ? "Firewall policy configured" : "No firewall policy found"));

            list.Add(Entry("endpoint", "Attack Surface Reduction", "Intune",
                epIns.IntuneAvailable, epIns.HasAsrPolicy,
                null,
                epIns.HasAsrPolicy ? "ASR policy configured" : "No ASR policy found"));

            list.Add(Entry("endpoint", "Edge Profile", "Intune",
                epIns.IntuneAvailable, epIns.HasEdgeProfile,
                null,
                epIns.HasEdgeProfile ? "Edge profile configured" : "No Edge config profile found"));

            list.Add(Entry("endpoint", "OneDrive Policy", "Intune",
                epIns.IntuneAvailable, epIns.HasOneDrivePolicy,
                null,
                epIns.HasOneDrivePolicy ? "OneDrive policy configured" : "No OneDrive policy found"));

            list.Add(Entry("endpoint", "Windows Update Policy", "Intune",
                epIns.IntuneAvailable, epIns.HasWindowsUpdatePolicy,
                null,
                epIns.HasWindowsUpdatePolicy ? "Update policy configured" : "No Windows Update policy found"));

            list.Add(Entry("endpoint", "Noncompliant Notifications", "Intune",
                epIns.IntuneAvailable, epIns.NotificationTemplateCount > 0,
                null,
                epIns.NotificationTemplateCount > 0
                    ? $"{epIns.NotificationTemplateCount} notification templates"
                    : "No noncompliant device notification templates"));

            list.Add(Entry("endpoint", "Endpoint Analytics", "Intune",
                epIns.IntuneAvailable, epIns.EndpointAnalyticsEnabled,
                null,
                epIns.EndpointAnalyticsEnabled ? "Endpoint Analytics enabled" : "Endpoint Analytics not enabled"));

            list.Add(Entry("endpoint", "Defender Auto-Onboard", "Intune + Defender",
                epIns.IntuneAvailable, epIns.DefenderAutoOnboard,
                null,
                epIns.DefenderAutoOnboard ? "Auto-onboard via Intune connector active" : "No Defender auto-onboard connector"));
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

            list.Add(Entry("mail_flow", "Unified Audit Logs", "Exchange Online",
                mfIns.ExchangeAvailable, mfIns.UnifiedAuditLogEnabled,
                null,
                mfIns.ExchangeAvailable
                    ? (mfIns.UnifiedAuditLogEnabled ? "Unified audit log ingestion enabled" : "Unified audit log NOT enabled")
                    : null));

            list.Add(Entry("mail_flow", "Safe Attachments", "Defender for Office 365",
                mfIns.ExchangeAvailable, mfIns.HasSafeAttachmentPolicy,
                null,
                mfIns.ExchangeAvailable
                    ? (mfIns.HasSafeAttachmentPolicy ? $"{mfIns.SafeAttachmentPolicyCount} safe attachment policies" : "No safe attachment policies configured")
                    : null));

            list.Add(Entry("mail_flow", "EOP/MDO Protection", "Exchange Online Protection",
                mfIns.ExchangeAvailable, mfIns.HasEopStandardProtection,
                null,
                mfIns.ExchangeAvailable
                    ? $"Standard: {(mfIns.HasEopStandardProtection ? "active" : "inactive")}, Strict: {(mfIns.HasEopStrictProtection ? "active" : "inactive")}"
                    : null));
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

        // If not implemented, adoption percentage is meaningless (e.g. default-compliant devices)
        foreach (var entry in list)
        {
            if (!entry.Implemented && entry.AdoptionPct.HasValue)
                entry.AdoptionPct = null;
        }

        // Resolve license tiers from subscribedSkus service plans
        if (skuPlans is { Count: > 0 })
            ResolveLicenseTiers(list, skuPlans);

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
            SetNotLicensed(list, "endpoint", "Defender Antivirus Policy");
            SetNotLicensed(list, "endpoint", "Defender Firewall Policy");
            SetNotLicensed(list, "endpoint", "Attack Surface Reduction");
            SetNotLicensed(list, "endpoint", "Edge Profile");
            SetNotLicensed(list, "endpoint", "OneDrive Policy");
            SetNotLicensed(list, "endpoint", "Windows Update Policy");
            SetNotLicensed(list, "endpoint", "Noncompliant Notifications");
            SetNotLicensed(list, "endpoint", "Endpoint Analytics");
            SetNotLicensed(list, "endpoint", "Defender Auto-Onboard");
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

    // ── License tier resolution from subscribedSkus service plans ──

    private static readonly (string Feature, string[] BasicPlans, string[] PremiumPlans)[] TierRules = new[]
    {
        // Identity
        ("Conditional Access",
            new[] { "AAD_PREMIUM" },
            new[] { "AAD_PREMIUM_P2" }),
        ("MFA",
            new[] { "AAD_PREMIUM", "MFA_PREMIUM" },
            new[] { "AAD_PREMIUM_P2" }),
        ("Risky Users",
            Array.Empty<string>(),
            new[] { "AAD_PREMIUM_P2" }),
        ("PIM",
            Array.Empty<string>(),
            new[] { "AAD_PREMIUM_P2" }),
        ("Access Reviews",
            Array.Empty<string>(),
            new[] { "AAD_PREMIUM_P2" }),
        ("Sign-in Logs",
            new[] { "AAD_PREMIUM" },
            new[] { "AAD_PREMIUM_P2" }),
        ("Device Management",
            new[] { "INTUNE_A" },
            new[] { "INTUNE_A_P2" }),
        ("SSPR",
            new[] { "AAD_PREMIUM" },
            Array.Empty<string>()),
        ("Global Secure Access",
            new[] { "AAD_PREMIUM" },
            new[] { "M365_NETWORK_ACCESS" }),

        // Endpoint
        ("Intune Compliance",
            new[] { "INTUNE_A" },
            new[] { "INTUNE_A_P2" }),
        ("Config Profiles",
            new[] { "INTUNE_A" },
            new[] { "INTUNE_A_P2" }),
        ("App Protection",
            new[] { "INTUNE_A" },
            new[] { "INTUNE_A_P2" }),
        ("Autopilot",
            new[] { "INTUNE_A" },
            Array.Empty<string>()),
        ("Defender for Endpoint",
            new[] { "WINDEFATP" },
            new[] { "MDE_SMB", "DEFENDER_ENDPOINT_P2" }),
        ("Defender Antivirus Policy",
            new[] { "INTUNE_A" },
            Array.Empty<string>()),
        ("Defender Firewall Policy",
            new[] { "INTUNE_A" },
            Array.Empty<string>()),
        ("Attack Surface Reduction",
            new[] { "INTUNE_A" },
            Array.Empty<string>()),
        ("Windows Update Policy",
            new[] { "INTUNE_A" },
            new[] { "INTUNE_A_P2" }),

        // Data
        ("Purview / DLP",
            new[] { "MIP_S_CDA", "CONTENT_EXPLORER", "DLP_ANALYTICS_REPORTS_BASIC" },
            new[] { "INFORMATION_BARRIERS", "CUSTOMER_KEY_DATACENTER", "MIP_S_CDA2", "M365_ADVANCED_AUDITING" }),

        // Mail Flow / Exchange
        ("Safe Attachments",
            new[] { "ATP_ENTERPRISE" },
            new[] { "THREAT_INTELLIGENCE" }),
        ("EOP/MDO Protection",
            new[] { "EOP_ENTERPRISE" },
            new[] { "ATP_ENTERPRISE", "THREAT_INTELLIGENCE" }),

        // Productivity
        ("Copilot",
            new[] { "Microsoft_365_Copilot" },
            Array.Empty<string>()),
    };

    private static void ResolveLicenseTiers(List<FeatureInventoryEntry> list, Dictionary<string, string> plans)
    {
        bool Has(string planName) =>
            plans.TryGetValue(planName, out var status) &&
            !string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase);

        foreach (var (featureName, basicPlans, premiumPlans) in TierRules)
        {
            var entry = list.FirstOrDefault(e => e.Feature == featureName);
            if (entry is null) continue;

            bool hasPremium = premiumPlans.Length > 0 && premiumPlans.Any(Has);
            bool hasBasic = basicPlans.Length > 0 && basicPlans.Any(Has);

            if (hasPremium)
                entry.LicenseTier = "premium";
            else if (hasBasic)
                entry.LicenseTier = "standard";
            else if (basicPlans.Length > 0 || premiumPlans.Length > 0)
                entry.LicenseTier = "none";
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
    /// <summary>
    /// Detected license tier from subscribedSkus service plans.
    /// null = not determinable, "none" = plan missing, "basic"/"standard"/"premium".
    /// </summary>
    public string? LicenseTier { get; set; }

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
