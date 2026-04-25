using System.Globalization;
using KryossApi.Services.CloudAssessment.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;

namespace KryossApi.Services.CloudAssessment.Recommendations;

/// <summary>
/// Generates Cloud Assessment (CA-2) endpoint recommendations from a
/// pre-computed <see cref="EndpointInsights"/> bag.
///
/// Covers two service surfaces:
///
///   * Intune (service = "intune") — compliance policy coverage, per-platform
///     gaps (Windows / iOS / Android), non-compliance rate thresholds, BYOD
///     App Protection, Autopilot, device encryption and enrollment
///     restrictions.
///
///   * Defender for Endpoint (service = "defender-endpoint") — activation /
///     licensing, exposure score risk thresholds, critical and high
///     vulnerability counts, unpatched software inventory and high-risk
///     machines.
///
/// Reuses <see cref="RecommendationResult"/> from CopilotReadiness so
/// downstream report rendering / pipeline result processing remains
/// shared. Observation / recommendation text is in English — bilingual
/// rendering happens downstream.
/// </summary>
public static class EndpointRecommendations
{
    private const string IntuneSvc = "intune";
    private const string DefenderSvc = "defender-endpoint";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ----------------------------------------------------------------
    //  Public entry point
    // ----------------------------------------------------------------

    public static List<RecommendationResult> Generate(EndpointInsights ins)
    {
        var all = new List<RecommendationResult>();

        // Intune checks.
        all.AddRange(GenerateCompliancePolicyCoverage(ins));
        all.AddRange(GeneratePerPlatformComplianceGaps(ins));
        all.AddRange(GenerateNonComplianceRate(ins));
        all.AddRange(GenerateBYODAppProtection(ins));
        all.AddRange(GeneratePerPlatformAppProtection(ins));
        all.AddRange(GenerateAutopilot(ins));
        all.AddRange(GenerateConfigProfileDrift(ins));
        all.AddRange(GenerateDeviceEncryption(ins));
        all.AddRange(GenerateEnrollmentRestrictions(ins));

        // Defender for Endpoint checks.
        all.AddRange(GenerateDefenderActivation(ins));
        all.AddRange(GenerateExposureScore(ins));
        all.AddRange(GenerateCriticalVulnerabilities(ins));
        all.AddRange(GenerateHighVulnerabilities(ins));
        all.AddRange(GenerateUnpatchedSoftware(ins));
        all.AddRange(GenerateHighRiskMachines(ins));

        return all;
    }

    // ================================================================
    //  Intune — Compliance policy coverage
    // ================================================================

    private static List<RecommendationResult> GenerateCompliancePolicyCoverage(EndpointInsights ins)
    {
        const string feature = "Device Compliance Policies";
        var list = new List<RecommendationResult>();

        if (ins.DeviceCompliancePolicyCount == 0)
        {
            list.Add(RecommendationResult.ActionRequired(IntuneSvc, feature, "high",
                observation: "No device compliance policies configured — all managed devices are unassessed.",
                recommendation: "Create at least one compliance policy per platform (Windows, iOS, Android) to enforce baseline security posture.",
                linkText: "Intune compliance policies",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/protect/device-compliance-get-started"));
        }

        return list;
    }

    // ================================================================
    //  Intune — Per-platform compliance gaps
    // ================================================================

    private static List<RecommendationResult> GeneratePerPlatformComplianceGaps(EndpointInsights ins)
    {
        var list = new List<RecommendationResult>();

        // Windows compliance gap.
        if (ins.DevicesWindows > 0 && ins.DeviceCompliancePolicyCount == 0)
        {
            list.Add(RecommendationResult.Warning(IntuneSvc, "Windows Compliance",
                observation: $"{ins.DevicesWindows.ToString(Inv)} Windows devices enrolled without compliance policies.",
                recommendation: "Assign a Windows compliance policy to all enrolled Windows endpoints.",
                linkText: "Windows compliance settings",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/protect/compliance-policy-create-windows"));
        }

        // iOS compliance gap.
        if (ins.DevicesIOS > 0 && ins.DeviceCompliancePolicyCount == 0)
        {
            list.Add(RecommendationResult.Warning(IntuneSvc, "iOS Compliance",
                observation: $"{ins.DevicesIOS.ToString(Inv)} iOS devices enrolled without compliance policies.",
                recommendation: "Assign an iOS compliance policy to all enrolled iOS endpoints.",
                linkText: "iOS compliance settings",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/protect/compliance-policy-create-ios"));
        }

        // Android compliance gap.
        if (ins.DevicesAndroid > 0 && ins.DeviceCompliancePolicyCount == 0)
        {
            list.Add(RecommendationResult.Warning(IntuneSvc, "Android Compliance",
                observation: $"{ins.DevicesAndroid.ToString(Inv)} Android devices enrolled without compliance policies.",
                recommendation: "Assign an Android compliance policy to all enrolled Android endpoints.",
                linkText: "Android compliance settings",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/protect/compliance-policy-create-android"));
        }

        return list;
    }

    // ================================================================
    //  Intune — Non-compliance rate thresholds
    // ================================================================

    private static List<RecommendationResult> GenerateNonComplianceRate(EndpointInsights ins)
    {
        const string feature = "Compliance Rate";
        var list = new List<RecommendationResult>();

        if (ins.DevicesTotal < 10) return list;

        double rate = ins.DevicesTotal > 0
            ? (double)ins.DevicesNonCompliant / ins.DevicesTotal
            : 0;

        string rateLabel = rate.ToString("P1", Inv);
        string healthyLabel = (1 - rate).ToString("P1", Inv);

        if (rate > 0.15)
        {
            list.Add(RecommendationResult.ActionRequired(IntuneSvc, feature, "high",
                observation: $"Non-compliance rate is {rateLabel} ({ins.DevicesNonCompliant.ToString(Inv)} of {ins.DevicesTotal.ToString(Inv)} devices) — critical threshold exceeded.",
                recommendation: "Investigate non-compliant devices and remediate policy failures. Focus on the most common failure reasons first (OS version, encryption, password).",
                linkText: "Monitor device compliance",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/protect/compliance-policy-monitor"));
        }
        else if (rate > 0.05)
        {
            list.Add(RecommendationResult.Warning(IntuneSvc, feature,
                observation: $"Non-compliance rate is {rateLabel}.",
                recommendation: "Review non-compliant devices and remediate policy failures to reach <5% non-compliance.",
                linkText: "Monitor device compliance",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/protect/compliance-policy-monitor"));
        }
        else
        {
            list.Add(RecommendationResult.Success(IntuneSvc, feature,
                observation: $"Compliance rate is healthy ({healthyLabel}).",
                linkText: "Monitor device compliance",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/protect/compliance-policy-monitor"));
        }

        return list;
    }

    // ================================================================
    //  Intune — BYOD App Protection
    // ================================================================

    private static List<RecommendationResult> GenerateBYODAppProtection(EndpointInsights ins)
    {
        const string feature = "BYOD App Protection";
        var list = new List<RecommendationResult>();

        if (ins.DevicesBYOD > 0
            && ins.AppProtectionPoliciesIOS == 0
            && ins.AppProtectionPoliciesAndroid == 0)
        {
            list.Add(RecommendationResult.ActionRequired(IntuneSvc, feature, "high",
                observation: $"{ins.DevicesBYOD.ToString(Inv)} BYOD devices enrolled with no App Protection policies in place — corporate data is unprotected on personal hardware.",
                recommendation: "Configure App Protection policies (iOS + Android) to enforce container encryption, copy-paste restrictions, and wipe on unenroll.",
                linkText: "App Protection policies",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/apps/app-protection-policy"));
        }

        return list;
    }

    // ================================================================
    //  Intune — Per-platform App Protection gaps
    // ================================================================

    private static List<RecommendationResult> GeneratePerPlatformAppProtection(EndpointInsights ins)
    {
        var list = new List<RecommendationResult>();

        if (ins.DevicesIOS > 0 && ins.AppProtectionPoliciesIOS == 0)
        {
            list.Add(RecommendationResult.Warning(IntuneSvc, "iOS App Protection",
                observation: $"{ins.DevicesIOS.ToString(Inv)} iOS devices enrolled with no iOS App Protection policy.",
                recommendation: "Create an iOS App Protection policy to enforce container encryption, PIN requirements and copy-paste restrictions on managed apps.",
                linkText: "Create an iOS App Protection policy",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/apps/app-protection-policies"));
        }

        if (ins.DevicesAndroid > 0 && ins.AppProtectionPoliciesAndroid == 0)
        {
            list.Add(RecommendationResult.Warning(IntuneSvc, "Android App Protection",
                observation: $"{ins.DevicesAndroid.ToString(Inv)} Android devices enrolled with no Android App Protection policy.",
                recommendation: "Create an Android App Protection policy to enforce container encryption, PIN requirements and copy-paste restrictions on managed apps.",
                linkText: "Create an Android App Protection policy",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/apps/app-protection-policies"));
        }

        return list;
    }

    // ================================================================
    //  Intune — Windows Autopilot
    // ================================================================

    private static List<RecommendationResult> GenerateAutopilot(EndpointInsights ins)
    {
        const string feature = "Windows Autopilot";
        var list = new List<RecommendationResult>();

        if (ins.DevicesWindows < 5) return list;

        if (ins.AutopilotProfileCount == 0)
        {
            list.Add(RecommendationResult.Warning(IntuneSvc, feature,
                observation: $"{ins.DevicesWindows.ToString(Inv)} Windows devices enrolled with no Autopilot deployment profiles — manual provisioning burden remains.",
                recommendation: "Configure Windows Autopilot deployment profiles to automate zero-touch device enrollment and reduce IT overhead.",
                linkText: "Windows Autopilot overview",
                linkUrl: "https://learn.microsoft.com/en-us/autopilot/overview"));
        }
        else
        {
            list.Add(RecommendationResult.Success(IntuneSvc, feature,
                observation: $"{ins.AutopilotProfileCount.ToString(Inv)} Autopilot deployment profile(s) configured for zero-touch provisioning.",
                linkText: "Windows Autopilot overview",
                linkUrl: "https://learn.microsoft.com/en-us/autopilot/overview"));
        }

        return list;
    }

    // ================================================================
    //  Intune — Configuration profile drift
    // ================================================================

    private static List<RecommendationResult> GenerateConfigProfileDrift(EndpointInsights ins)
    {
        const string feature = "Configuration Profile Drift";
        var list = new List<RecommendationResult>();

        if (ins.ConfigProfilesAssigned == 0) return list;

        int driftCount = ins.ConfigProfilesFailed + ins.ConfigProfilesConflict;
        double driftRate = (double)driftCount / ins.ConfigProfilesAssigned;
        string driftLabel = driftRate.ToString("P1", Inv);

        if (driftRate > 0.15)
        {
            list.Add(RecommendationResult.ActionRequired(IntuneSvc, feature, "high",
                observation: $"{driftCount.ToString(Inv)} devices ({driftLabel}) have failed or conflicting configuration profiles — settings drift from intended baseline.",
                recommendation: "Review failed/conflicting profile assignments in Intune. Resolve conflicts between overlapping profiles and remediate failed deployments.",
                linkText: "Monitor device configuration profiles",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/configuration/device-profile-monitor"));
        }
        else if (driftRate > 0.05)
        {
            list.Add(RecommendationResult.Warning(IntuneSvc, feature,
                observation: $"{driftCount.ToString(Inv)} devices ({driftLabel}) have failed or conflicting configuration profiles.",
                recommendation: "Investigate profile assignment failures and conflicts. Target <5% drift rate for consistent endpoint posture.",
                linkText: "Monitor device configuration profiles",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/configuration/device-profile-monitor"));
        }
        else
        {
            list.Add(RecommendationResult.Success(IntuneSvc, feature,
                observation: $"Configuration profile compliance is healthy — {ins.ConfigProfilesSucceeded.ToString(Inv)} of {ins.ConfigProfilesAssigned.ToString(Inv)} devices have succeeded.",
                linkText: "Monitor device configuration profiles",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/configuration/device-profile-monitor"));
        }

        return list;
    }

    // ================================================================
    //  Intune — Device encryption gap
    // ================================================================

    private static List<RecommendationResult> GenerateDeviceEncryption(EndpointInsights ins)
    {
        const string feature = "Device Encryption";
        var list = new List<RecommendationResult>();

        if (ins.DevicesTotal <= 0) return list;

        int unencrypted = ins.DevicesTotal - ins.DevicesEncrypted;
        if (unencrypted <= 0) return list;

        double pct = (double)unencrypted / ins.DevicesTotal;
        string pctLabel = pct.ToString("P1", Inv);

        if (ins.DevicesTotal >= 10 && pct > 0.2)
        {
            list.Add(RecommendationResult.ActionRequired(IntuneSvc, feature, "high",
                observation: $"{unencrypted.ToString(Inv)} of {ins.DevicesTotal.ToString(Inv)} devices unencrypted ({pctLabel}) — data-at-rest exposure on theft/loss.",
                recommendation: "Enforce BitLocker (Windows) and FileVault (macOS) via Intune configuration profiles. Block non-compliant devices from corporate resources until remediated.",
                linkText: "Encrypt devices with Intune",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/protect/encrypt-devices"));
        }
        else
        {
            list.Add(RecommendationResult.Warning(IntuneSvc, feature,
                observation: $"{unencrypted.ToString(Inv)} of {ins.DevicesTotal.ToString(Inv)} devices unencrypted ({pctLabel}).",
                recommendation: "Enforce BitLocker (Windows) and FileVault (macOS) via Intune configuration profiles on all managed endpoints.",
                linkText: "Encrypt devices with Intune",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/protect/encrypt-devices"));
        }

        return list;
    }

    // ================================================================
    //  Intune — Enrollment restrictions
    // ================================================================

    private static List<RecommendationResult> GenerateEnrollmentRestrictions(EndpointInsights ins)
    {
        const string feature = "Enrollment Restrictions";
        var list = new List<RecommendationResult>();

        if (ins.EnrollmentRestrictionCount == 0)
        {
            list.Add(RecommendationResult.Warning(IntuneSvc, feature,
                observation: "No enrollment restrictions configured — any user or device type may enroll.",
                recommendation: "Define enrollment restrictions to block unsupported platforms and personal devices where policy requires corporate-only.",
                linkText: "Enrollment restrictions",
                linkUrl: "https://learn.microsoft.com/en-us/mem/intune/enrollment/enrollment-restrictions-set"));
        }

        return list;
    }

    // ================================================================
    //  Defender for Endpoint — Activation / licensing
    // ================================================================

    private static List<RecommendationResult> GenerateDefenderActivation(EndpointInsights ins)
    {
        const string feature = "Defender for Endpoint";
        var list = new List<RecommendationResult>();

        if (ins.ActivationNeeded && !ins.DefenderEndpointAvailable)
        {
            list.Add(RecommendationResult.NotLicensed(DefenderSvc, feature,
                observation: "Defender for Endpoint API returned 403/404 — either not licensed or no devices onboarded.",
                recommendation: "Onboard Windows endpoints to Defender and ensure Microsoft 365 Defender API permissions are consented.",
                linkText: "Onboard devices to Defender for Endpoint",
                linkUrl: "https://learn.microsoft.com/en-us/defender-endpoint/onboarding"));
        }

        return list;
    }

    // ================================================================
    //  Defender for Endpoint — Exposure score thresholds
    // ================================================================

    private static List<RecommendationResult> GenerateExposureScore(EndpointInsights ins)
    {
        const string feature = "Exposure Score";
        var list = new List<RecommendationResult>();

        if (!ins.DefenderEndpointAvailable) return list;

        string scoreLabel = ins.ExposureScore.ToString("F1", Inv);
        const string rec = "Review top security recommendations in the Defender portal and apply mitigations in priority order.";
        const string linkText = "Exposure score in Defender Vulnerability Management";
        const string linkUrl = "https://learn.microsoft.com/en-us/defender-vulnerability-management/tvm-exposure-score";

        if (ins.ExposureScore > 60)
        {
            list.Add(RecommendationResult.ActionRequired(DefenderSvc, feature, "high",
                observation: $"Exposure score is {scoreLabel} (High risk).",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else if (ins.ExposureScore > 30)
        {
            list.Add(RecommendationResult.Warning(DefenderSvc, feature,
                observation: $"Exposure score is {scoreLabel} (Medium risk).",
                recommendation: rec,
                linkText: linkText,
                linkUrl: linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Success(DefenderSvc, feature,
                observation: $"Exposure score is {scoreLabel} (Low risk).",
                linkText: linkText,
                linkUrl: linkUrl));
        }

        return list;
    }

    // ================================================================
    //  Defender for Endpoint — Critical vulnerabilities
    // ================================================================

    private static List<RecommendationResult> GenerateCriticalVulnerabilities(EndpointInsights ins)
    {
        const string feature = "Vulnerability Posture";
        var list = new List<RecommendationResult>();

        if (ins.DefenderEndpointAvailable && ins.VulnCritical > 0)
        {
            list.Add(RecommendationResult.ActionRequired(DefenderSvc, feature, "high",
                observation: $"{ins.VulnCritical.ToString(Inv)} critical vulnerabilities across the fleet.",
                recommendation: "Patch or mitigate critical CVEs immediately — prioritize internet-facing and privileged endpoints.",
                linkText: "Defender vulnerability management — weaknesses",
                linkUrl: "https://learn.microsoft.com/en-us/defender-vulnerability-management/tvm-weaknesses"));
        }

        return list;
    }

    // ================================================================
    //  Defender for Endpoint — High-severity vulnerabilities
    // ================================================================

    private static List<RecommendationResult> GenerateHighVulnerabilities(EndpointInsights ins)
    {
        const string feature = "High Vulnerabilities";
        var list = new List<RecommendationResult>();

        if (ins.DefenderEndpointAvailable && ins.VulnHigh > 10)
        {
            list.Add(RecommendationResult.Warning(DefenderSvc, feature,
                observation: $"{ins.VulnHigh.ToString(Inv)} high-severity vulnerabilities pending.",
                recommendation: "Schedule patch cycles for high-severity CVEs; prioritize by asset exposure and exploit availability.",
                linkText: "Defender vulnerability management — weaknesses",
                linkUrl: "https://learn.microsoft.com/en-us/defender-vulnerability-management/tvm-weaknesses"));
        }

        return list;
    }

    // ================================================================
    //  Defender for Endpoint — Unpatched software inventory
    // ================================================================

    private static List<RecommendationResult> GenerateUnpatchedSoftware(EndpointInsights ins)
    {
        const string feature = "Software Inventory";
        var list = new List<RecommendationResult>();

        if (ins.DefenderEndpointAvailable && ins.SoftwareVulnerable > 0)
        {
            list.Add(RecommendationResult.Warning(DefenderSvc, feature,
                observation: $"{ins.SoftwareVulnerable.ToString(Inv)} applications have known weaknesses across the fleet.",
                recommendation: "Update or remove vulnerable applications; prioritize those with public exploits.",
                linkText: "Software inventory in Defender",
                linkUrl: "https://learn.microsoft.com/en-us/defender-vulnerability-management/tvm-software-inventory"));
        }

        return list;
    }

    // ================================================================
    //  Defender for Endpoint — High-risk machines
    // ================================================================

    private static List<RecommendationResult> GenerateHighRiskMachines(EndpointInsights ins)
    {
        const string feature = "High-Risk Machines";
        var list = new List<RecommendationResult>();

        if (ins.DefenderEndpointAvailable && ins.MachinesHighRisk > 0)
        {
            list.Add(RecommendationResult.ActionRequired(DefenderSvc, feature, "high",
                observation: $"{ins.MachinesHighRisk.ToString(Inv)} machines marked High risk by Defender.",
                recommendation: "Investigate and remediate immediately — consider isolating high-risk machines pending investigation.",
                linkText: "Investigate devices in the Defender portal",
                linkUrl: "https://learn.microsoft.com/en-us/defender-endpoint/investigate-machines"));
        }

        return list;
    }
}
