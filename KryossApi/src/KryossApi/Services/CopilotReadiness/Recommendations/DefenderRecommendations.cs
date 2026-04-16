using KryossApi.Services.CopilotReadiness.Pipelines;

namespace KryossApi.Services.CopilotReadiness.Recommendations;

/// <summary>
/// Generates all Microsoft Defender recommendations from pre-computed
/// <see cref="DefenderInsights"/>.
/// Ports 17 Python recommendation modules (14 license checks + 3 composite
/// assessments):
///   WINDEFATP, ATP_ENTERPRISE, MTP, ATA, ADALLOM_S_DISCOVERY,
///   ADALLOM_S_O365, ADALLOM_S_STANDALONE, EOP_ENTERPRISE_PREMIUM, SAFEDOCS,
///   THREAT_INTELLIGENCE, DEFENDER_FOR_IOT, Defender_for_Iot_Enterprise,
///   DEFENDER_XDR_ACTIVATION, DEFENDER_ENDPOINT_ONBOARDING,
///   COPILOT_SECURITY_POSTURE, COPILOT_THREAT_INTELLIGENCE,
///   COPILOT_DATA_GOVERNANCE
/// </summary>
public static class DefenderRecommendations
{
    private const string Svc = "Defender";

    // ----------------------------------------------------------------
    //  Public entry point
    // ----------------------------------------------------------------

    public static List<RecommendationResult> Generate(DefenderInsights ins)
    {
        var all = new List<RecommendationResult>();

        // License-based checks (1-12)
        all.Add(GenerateWinDefAtp(ins));                  // 1.  Defender for Endpoint
        all.Add(GenerateAtpEnterprise(ins));              // 2.  Defender for Office 365 P1
        all.Add(GenerateMtp(ins));                        // 3.  Defender XDR
        all.Add(GenerateAta(ins));                        // 4.  Defender for Identity
        all.Add(GenerateAdallomDiscovery(ins));           // 5.  Cloud Apps Discovery
        all.Add(GenerateAdallomO365(ins));                // 6.  Cloud Apps for O365
        all.Add(GenerateAdallomStandalone(ins));          // 7.  Cloud Apps standalone
        all.Add(GenerateEopEnterprisePremium(ins));       // 8.  EOP P1
        all.Add(GenerateSafeDocs(ins));                   // 9.  Safe Documents
        all.Add(GenerateThreatIntelligence(ins));         // 10. Threat Intelligence (P2)
        all.Add(GenerateDefenderForIot(ins));             // 11. Defender for IoT
        all.Add(GenerateDefenderForIotEnterprise(ins));   // 12. Defender for IoT Enterprise

        // Operational checks (13-14)
        all.Add(GenerateXdrActivation(ins));              // 13. XDR activation check
        all.Add(GenerateEndpointOnboarding(ins));         // 14. Device onboarding check

        // Composite assessments (15-17)
        all.Add(GenerateCopilotSecurityPosture(ins));     // 15. 10-factor composite
        all.Add(GenerateCopilotThreatIntelligence(ins));  // 16. 6-factor composite
        all.Add(GenerateCopilotDataGovernance(ins));      // 17. Data governance composite

        return all;
    }

    // ================================================================
    //  1. WINDEFATP - Defender for Endpoint
    // ================================================================

    private static RecommendationResult GenerateWinDefAtp(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender for Endpoint";

        if (!ins.Available)
            return RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{feature} data unavailable, leaving AI-enabled devices vulnerable to attacks",
                recommendation: $"Enable {feature} to protect the devices where employees use Copilot and agents. Endpoint security is critical because compromised devices could be used to inject malicious prompts, steal AI-generated sensitive data, or manipulate agent responses.",
                linkText: "Defender for Endpoint",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender-endpoint/");

        var metrics = new List<string>();
        string? rec = null;

        if (ins.IncidentsHighSeverity > 0)
        {
            metrics.Add($"{ins.IncidentsActive} incidents ({ins.IncidentsHighSeverity} high-severity)");
            rec = $"Investigate {ins.IncidentsHighSeverity} high-severity incident(s)";
        }
        else if (ins.IncidentsActive > 0)
        {
            metrics.Add($"{ins.IncidentsActive} active incidents");
            rec = $"Review {ins.IncidentsActive} active incident(s)";
        }

        if (ins.DevicesHighRisk > 0)
        {
            metrics.Add($"{ins.DevicesHighRisk} high-risk devices");
            rec ??= $"Secure {ins.DevicesHighRisk} high-risk device(s)";
        }

        var obs = metrics.Count > 0
            ? $"{feature} is active, protecting Copilot workloads. {string.Join(", ", metrics)}"
            : $"{feature} is active, protecting Copilot workloads. No security incidents detected";

        return rec is not null
            ? RecommendationResult.Warning(Svc, feature, observation: obs, recommendation: rec,
                linkText: "Defender for Endpoint",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender-endpoint/")
            : RecommendationResult.Success(Svc, feature, observation: obs,
                linkText: "Defender for Endpoint",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender-endpoint/");
    }

    // ================================================================
    //  2. ATP_ENTERPRISE - Defender for Office 365 (Plan 1)
    // ================================================================

    private static RecommendationResult GenerateAtpEnterprise(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender for Office 365 (Plan 1)";

        if (!ins.Available)
            return RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{feature} data unavailable, exposing Copilot to malicious email and unsafe links",
                recommendation: $"Enable {feature} to provide Safe Links and Safe Attachments protection for content Copilot accesses.",
                linkText: "Defender for Office 365",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/office-365-security/mdo-about");

        if (ins.PhishingAlerts > 0 || ins.MalwareAlerts > 0)
        {
            var metrics = new List<string>();
            if (ins.PhishingAlerts > 0) metrics.Add($"{ins.PhishingAlerts} phishing alerts");
            if (ins.MalwareAlerts > 0) metrics.Add($"{ins.MalwareAlerts} malware alerts");
            return RecommendationResult.Warning(Svc, feature,
                observation: $"{feature} is active, protecting Copilot workloads. {string.Join(", ", metrics)}",
                recommendation: "Review email threats targeting Copilot users",
                linkText: "Defender for Office 365",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/office-365-security/mdo-about");
        }

        return RecommendationResult.Success(Svc, feature,
            observation: $"{feature} is active, protecting Copilot workloads. No email threats detected in last 30 days",
            linkText: "Defender for Office 365",
            linkUrl: "https://learn.microsoft.com/microsoft-365/security/office-365-security/mdo-about");
    }

    // ================================================================
    //  3. MTP - Defender XDR
    // ================================================================

    private static RecommendationResult GenerateMtp(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender XDR";

        if (!ins.Available)
            return RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{feature} data unavailable, limiting visibility into Copilot security threats",
                recommendation: $"Enable {feature} to gain unified visibility and automated response across all M365 Copilot touchpoints.",
                linkText: "Defender XDR",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender/");

        var metrics = new List<string>();
        string? rec = null;

        if (ins.IncidentsHighSeverity > 0)
        {
            metrics.Add($"{ins.IncidentsActive} incidents ({ins.IncidentsHighSeverity} high-severity)");
            rec = $"Investigate {ins.IncidentsHighSeverity} high-severity incident(s)";
        }
        else if (ins.IncidentsActive > 0)
        {
            metrics.Add($"{ins.IncidentsActive} active incidents");
            rec = $"Review {ins.IncidentsActive} active incident(s)";
        }

        if (ins.ConfirmedCompromised > 0)
        {
            metrics.Add($"{ins.ConfirmedCompromised} compromised users");
            rec ??= $"Revoke access for {ins.ConfirmedCompromised} compromised account(s)";
        }

        var obs = metrics.Count > 0
            ? $"{feature} is active, protecting Copilot workloads. {string.Join(", ", metrics)}"
            : $"{feature} is active, protecting Copilot workloads. No security incidents detected";

        return rec is not null
            ? RecommendationResult.Warning(Svc, feature, observation: obs, recommendation: rec,
                linkText: "Defender XDR",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender/")
            : RecommendationResult.Success(Svc, feature, observation: obs,
                linkText: "Defender XDR",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender/");
    }

    // ================================================================
    //  4. ATA - Defender for Identity
    // ================================================================

    private static RecommendationResult GenerateAta(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender for Identity";

        if (!ins.Available)
            return RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{feature} data unavailable, missing detection of compromised accounts using Copilot",
                recommendation: $"Enable {feature} to detect when stolen credentials are used to abuse Copilot for data theft.",
                linkText: "Defender for Identity",
                linkUrl: "https://learn.microsoft.com/defender-for-identity/what-is");

        if (ins.RiskyUsersHigh > 0 || ins.ConfirmedCompromised > 0 || ins.RiskySignInsHigh > 0)
        {
            var metrics = new List<string>();
            string? rec = null;

            if (ins.ConfirmedCompromised > 0)
            {
                metrics.Add($"{ins.ConfirmedCompromised} compromised users");
                rec = $"Revoke access for {ins.ConfirmedCompromised} compromised account(s)";
            }
            else if (ins.RiskyUsersHigh > 0)
            {
                metrics.Add($"{ins.RiskyUsersHigh} high-risk users");
                rec = $"Review {ins.RiskyUsersHigh} high-risk identity(ies)";
            }

            if (ins.RiskySignInsHigh > 0)
                metrics.Add($"{ins.RiskySignInsHigh} high-risk sign-ins");

            return RecommendationResult.Warning(Svc, feature,
                observation: $"{feature} is active, protecting Copilot workloads. {string.Join(", ", metrics)}",
                recommendation: rec,
                linkText: "Defender for Identity",
                linkUrl: "https://learn.microsoft.com/defender-for-identity/what-is");
        }

        return RecommendationResult.Success(Svc, feature,
            observation: $"{feature} is active, protecting Copilot workloads. No risky users or sign-ins detected",
            linkText: "Defender for Identity",
            linkUrl: "https://learn.microsoft.com/defender-for-identity/what-is");
    }

    // ================================================================
    //  5. ADALLOM_S_DISCOVERY - Cloud Apps Discovery
    // ================================================================

    private static RecommendationResult GenerateAdallomDiscovery(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender for Cloud Apps Discovery";

        // License-only check, no enrichment in the Python source
        return RecommendationResult.Success(Svc, feature,
            observation: $"{feature} is active, monitoring for unauthorized app integrations with Copilot",
            linkText: "Control Third-Party AI Apps",
            linkUrl: "https://learn.microsoft.com/defender-cloud-apps/");
    }

    // ================================================================
    //  6. ADALLOM_S_O365 - Cloud Apps for Office 365
    // ================================================================

    private static RecommendationResult GenerateAdallomO365(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender for Cloud Apps for Office 365";

        if (ins.HighRiskApps > 0 || ins.OverPrivilegedApps > 0)
        {
            var metrics = new List<string>();
            string? rec = null;
            if (ins.HighRiskApps > 0)
            {
                metrics.Add($"{ins.HighRiskApps} high-risk OAuth apps");
                rec = $"Review {ins.HighRiskApps} high-risk app permissions";
            }
            if (ins.OverPrivilegedApps > 0)
                metrics.Add($"{ins.OverPrivilegedApps} over-privileged apps");

            return RecommendationResult.Warning(Svc, feature,
                observation: $"{feature} is active, protecting Copilot workloads. {string.Join(", ", metrics)}",
                recommendation: rec,
                linkText: "Cloud Apps",
                linkUrl: "https://learn.microsoft.com/defender-cloud-apps/");
        }

        return RecommendationResult.Success(Svc, feature,
            observation: $"{feature} is active, protecting Copilot workloads. No high-risk OAuth apps detected",
            linkText: "Cloud Apps",
            linkUrl: "https://learn.microsoft.com/defender-cloud-apps/");
    }

    // ================================================================
    //  7. ADALLOM_S_STANDALONE - Cloud Apps standalone
    // ================================================================

    private static RecommendationResult GenerateAdallomStandalone(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender for Cloud Apps";

        if (ins.HighRiskApps > 0 || ins.OverPrivilegedApps > 0)
        {
            var metrics = new List<string>();
            string? rec = null;
            if (ins.HighRiskApps > 0)
            {
                metrics.Add($"{ins.HighRiskApps} high-risk OAuth apps");
                rec = $"Review {ins.HighRiskApps} high-risk app permissions";
            }
            if (ins.OverPrivilegedApps > 0)
                metrics.Add($"{ins.OverPrivilegedApps} over-privileged apps");

            return RecommendationResult.Warning(Svc, feature,
                observation: $"{feature} is active, protecting Copilot workloads. {string.Join(", ", metrics)}",
                recommendation: rec,
                linkText: "Cloud Apps",
                linkUrl: "https://learn.microsoft.com/defender-cloud-apps/what-is-defender-for-cloud-apps");
        }

        return RecommendationResult.Success(Svc, feature,
            observation: $"{feature} is active, protecting Copilot workloads. No high-risk OAuth apps detected",
            linkText: "Cloud Apps",
            linkUrl: "https://learn.microsoft.com/defender-cloud-apps/what-is-defender-for-cloud-apps");
    }

    // ================================================================
    //  8. EOP_ENTERPRISE_PREMIUM - Defender for Office 365 (Plan 1) / EOP
    // ================================================================

    private static RecommendationResult GenerateEopEnterprisePremium(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender for Office 365 (Plan 1)";

        if (ins.PhishingAlerts > 0 || ins.MalwareAlerts > 0)
        {
            var metrics = new List<string>();
            if (ins.PhishingAlerts > 0) metrics.Add($"{ins.PhishingAlerts} phishing alerts");
            if (ins.MalwareAlerts > 0) metrics.Add($"{ins.MalwareAlerts} malware alerts");
            return RecommendationResult.Warning(Svc, feature,
                observation: $"{feature} is active, protecting Copilot workloads. {string.Join(", ", metrics)}",
                recommendation: "Review email threats targeting Copilot users",
                linkText: "Defender for Office 365",
                linkUrl: "https://learn.microsoft.com/defender-office-365/");
        }

        return RecommendationResult.Success(Svc, feature,
            observation: $"{feature} is active, protecting Copilot workloads. No email threats detected in last 30 days",
            linkText: "Defender for Office 365",
            linkUrl: "https://learn.microsoft.com/defender-office-365/");
    }

    // ================================================================
    //  9. SAFEDOCS - Safe Documents
    // ================================================================

    private static RecommendationResult GenerateSafeDocs(DefenderInsights ins)
    {
        const string feature = "Safe Documents";

        if (ins.MalwareAlerts > 0)
            return RecommendationResult.Warning(Svc, feature,
                observation: $"{feature} is active, protecting Copilot workloads. {ins.MalwareAlerts} malware alerts",
                recommendation: "Review malware detections in documents accessed by Copilot",
                linkText: "Safe Documents",
                linkUrl: "https://learn.microsoft.com/defender-office-365/safe-documents-in-e5-plus-security-about/");

        return RecommendationResult.Success(Svc, feature,
            observation: $"{feature} is active, protecting Copilot workloads. No malware detected in last 30 days",
            linkText: "Safe Documents",
            linkUrl: "https://learn.microsoft.com/defender-office-365/safe-documents-in-e5-plus-security-about/");
    }

    // ================================================================
    //  10. THREAT_INTELLIGENCE - Defender for Office 365 (Plan 2)
    // ================================================================

    private static RecommendationResult GenerateThreatIntelligence(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender for Office 365 (Plan 2)";

        if (!ins.Available)
            return RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{feature} data unavailable, exposing Copilot to malicious content in emails and files",
                recommendation: $"Enable {feature} to protect M365 Copilot from processing malicious attachments, phishing attempts, and unsafe links.",
                linkText: "Defender for Office 365",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/office-365-security/mdo-about");

        var metrics = new List<string>();
        string? rec = null;

        if (ins.PhishingAlerts > 0) metrics.Add($"{ins.PhishingAlerts} phishing alerts");
        if (ins.MalwareAlerts > 0) metrics.Add($"{ins.MalwareAlerts} malware alerts");
        if (ins.IncidentsHighSeverity > 0)
        {
            metrics.Add($"{ins.IncidentsHighSeverity} high-severity incidents");
            rec = $"Investigate {ins.IncidentsHighSeverity} high-severity incident(s)";
        }

        var obs = metrics.Count > 0
            ? $"{feature} is active, protecting Copilot workloads. {string.Join(", ", metrics)}"
            : $"{feature} is active, protecting Copilot workloads. No advanced threats detected in last 30 days";

        return rec is not null
            ? RecommendationResult.Warning(Svc, feature, observation: obs, recommendation: rec,
                linkText: "Defender for Office 365",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/office-365-security/mdo-about")
            : RecommendationResult.Success(Svc, feature, observation: obs,
                linkText: "Defender for Office 365",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/office-365-security/mdo-about");
    }

    // ================================================================
    //  11. DEFENDER_FOR_IOT
    // ================================================================

    private static RecommendationResult GenerateDefenderForIot(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender for IoT";

        return RecommendationResult.Success(Svc, feature,
            observation: $"{feature} is active, protecting IoT/OT environments with Security Copilot integration",
            linkText: "IoT Security with AI Analysis",
            linkUrl: "https://learn.microsoft.com/defender-for-iot/");
    }

    // ================================================================
    //  12. Defender_for_Iot_Enterprise
    // ================================================================

    private static RecommendationResult GenerateDefenderForIotEnterprise(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender for IoT";

        return RecommendationResult.Success(Svc, feature,
            observation: $"{feature} is active, securing IoT devices in environments where Copilot may access operational data",
            linkText: "Microsoft 365 Documentation",
            linkUrl: "https://learn.microsoft.com/microsoft-365/");
    }

    // ================================================================
    //  13. DEFENDER_XDR_ACTIVATION
    // ================================================================

    private static RecommendationResult GenerateXdrActivation(DefenderInsights ins)
    {
        const string feature = "Microsoft Defender XDR";

        if (ins.ActivationNeeded && !ins.DefenderApiAvailable)
            return RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: "Microsoft Defender XDR not activated - Security APIs unavailable",
                recommendation: "Activate Microsoft Defender XDR in the Security portal (https://security.microsoft.com) to enable unified security monitoring, threat detection, and incident response across your Microsoft 365 environment. This is required to access security data through APIs and protect Copilot workloads.",
                linkText: "Activate Microsoft Defender XDR",
                linkUrl: "https://security.microsoft.com");

        if (!ins.Available)
            return RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{feature} license not found - current SKU has limited Defender capabilities without unified XDR",
                recommendation: "Upgrade to Microsoft 365 E5 or add Microsoft Defender plan to get XDR capabilities. XDR provides unified security monitoring across endpoints, identities, email, and cloud apps essential for protecting Copilot workloads.",
                linkText: "Microsoft Defender XDR Licensing",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender/microsoft-365-defender");

        // XDR is licensed and APIs working
        return RecommendationResult.Success(Svc, feature,
            observation: $"{feature} is active and operational",
            linkText: "Defender XDR",
            linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender/");
    }

    // ================================================================
    //  14. DEFENDER_ENDPOINT_ONBOARDING
    // ================================================================

    private static RecommendationResult GenerateEndpointOnboarding(DefenderInsights ins)
    {
        const string feature = "Defender for Endpoint - Device Onboarding";

        if (!ins.DefenderApiAvailable && ins.ActivationNeeded)
            return RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: "Defender for Endpoint API unavailable (403 Forbidden). No devices are onboarded to Defender for Endpoint. The API requires at least one device reporting to the service.",
                recommendation: "Onboard at least one device to Defender for Endpoint: Go to https://security.microsoft.com > Settings > Endpoints > Onboarding. Select deployment method and deploy to Windows devices. Re-run this tool after 5-10 minutes.",
                linkText: "Onboard devices to Defender for Endpoint",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender-endpoint/onboarding");

        if (ins.DevicesTotal == 0)
            return RecommendationResult.Warning(Svc, feature,
                observation: "Defender for Endpoint is active but no devices are reporting. Device inventory is empty, limiting threat detection capabilities.",
                recommendation: "Onboard devices to Defender for Endpoint to enable full security monitoring. See Settings > Endpoints > Onboarding in the security portal.",
                linkText: "Device onboarding guide",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender-endpoint/onboarding");

        if (ins.DevicesTotal < 10)
            return RecommendationResult.Success(Svc, feature,
                observation: $"{ins.DevicesTotal} device(s) onboarded to Defender for Endpoint. API is fully functional. Consider onboarding additional devices for comprehensive security coverage.",
                linkText: "Scale Defender for Endpoint deployment",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender-endpoint/deployment-phases");

        return RecommendationResult.Success(Svc, feature,
            observation: $"{ins.DevicesTotal} devices onboarded to Defender for Endpoint. API is fully operational.",
            linkText: "Monitor device health",
            linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender-endpoint/device-health-sensor-health-os");
    }

    // ================================================================
    //  15. COPILOT_SECURITY_POSTURE (10-factor composite)
    // ================================================================

    private static RecommendationResult GenerateCopilotSecurityPosture(DefenderInsights ins)
    {
        const string feature = "Copilot Security Posture";
        var riskFactors = new List<string>();
        var strengths = new List<string>();
        var criticalGaps = new List<string>();

        // Factor 1: Exposure Score
        if (ins.DefenderApiAvailable && ins.ExposureScore > 0)
        {
            if (ins.ExposureScore > 60)
            {
                riskFactors.Add($"High exposure score ({ins.ExposureScore:F0}/100)");
                criticalGaps.Add("Reduce attack surface through patch management and configuration hardening");
            }
            else if (ins.ExposureScore > 30)
            {
                riskFactors.Add($"Medium exposure score ({ins.ExposureScore:F0}/100)");
            }
            else
            {
                strengths.Add($"Low exposure score ({ins.ExposureScore:F0}/100)");
            }
        }

        // Factor 2: Critical Security Recommendations
        if (ins.RecommendationsCritical > 0)
        {
            riskFactors.Add($"{ins.RecommendationsCritical} critical security recommendations unimplemented");
            criticalGaps.Add($"Address {ins.RecommendationsCritical} critical security gaps before Copilot deployment");
        }
        if (ins.CopilotRelevantRecommendations > 0)
            riskFactors.Add($"{ins.CopilotRelevantRecommendations} Copilot-related recommendations pending");

        // Factor 3: Compromised Identities
        if (ins.ConfirmedCompromised > 0)
        {
            riskFactors.Add($"{ins.ConfirmedCompromised} confirmed compromised accounts with potential Copilot access");
            criticalGaps.Add($"Immediately revoke access for {ins.ConfirmedCompromised} compromised accounts and enforce MFA");
        }
        if (ins.RiskyUsersHigh > 0)
            riskFactors.Add($"{ins.RiskyUsersHigh} high-risk users");

        // Factor 4: Email Threats
        if (ins.PhishingAlerts > 10)
            riskFactors.Add($"{ins.PhishingAlerts} phishing attempts detected (Copilot may process malicious content)");
        if (ins.MalwareAlerts > 0)
            riskFactors.Add($"{ins.MalwareAlerts} malware detections in emails");

        // Factor 5: OAuth App Risks
        if (ins.HighRiskApps > 0)
        {
            riskFactors.Add($"{ins.HighRiskApps} high-risk third-party apps with excessive permissions");
            var oauthAction = $"Review {ins.HighRiskApps} third-party apps with excessive permissions";
            if (ins.OverPrivilegedApps > 0)
                oauthAction += $". Prioritize {ins.OverPrivilegedApps} over-privileged apps first";
            criticalGaps.Add(oauthAction);
        }

        // Factor 6: Advanced Hunting
        if (ins.CopilotProcessEvents > 10)
            riskFactors.Add($"{ins.CopilotProcessEvents} suspicious Copilot-related process events detected");
        if (ins.AiPhishingEmails > 0)
        {
            riskFactors.Add($"{ins.AiPhishingEmails} phishing attempts mentioning Copilot/AI keywords");
            criticalGaps.Add("Educate users about AI-themed social engineering attacks");
        }
        if (ins.CopilotFileAccessEvents > 50)
            riskFactors.Add($"{ins.CopilotFileAccessEvents} sensitive files accessed before Copilot use (potential data exposure)");

        // Factor 7: Active High-Severity Incidents
        if (ins.IncidentsHighSeverity > 0)
        {
            riskFactors.Add($"{ins.IncidentsHighSeverity} high-severity security incidents active");
            criticalGaps.Add($"Resolve {ins.IncidentsHighSeverity} high-severity incidents before Copilot deployment");
        }

        // Factor 8: Vulnerable Software
        if (ins.VulnerableApps > 10)
            riskFactors.Add($"{ins.VulnerableApps} applications with known vulnerabilities");

        // Factor 9: Device Risks
        if (ins.DevicesHighRisk > 0 && ins.DevicesTotal > 0)
        {
            var riskPct = (double)ins.DevicesHighRisk / ins.DevicesTotal * 100;
            if (riskPct > 10)
            {
                riskFactors.Add($"{ins.DevicesHighRisk} high-risk devices ({riskPct:F0}% of fleet)");
                criticalGaps.Add("Implement conditional access to block high-risk devices from Copilot");
            }
        }

        // Factor 10: DLP/Labels -- needs purview data, handled via placeholder
        // (Purview pipeline not implemented yet, skip)

        // Calculate posture
        string status, posture, priority;
        if (criticalGaps.Count > 0) { status = "action_required"; posture = "NOT READY"; priority = "high"; }
        else if (riskFactors.Count > 5) { status = "warning"; posture = "AT RISK"; priority = "high"; }
        else if (riskFactors.Count > 2) { status = "warning"; posture = "NEEDS IMPROVEMENT"; priority = "medium"; }
        else { status = "success"; posture = "READY"; priority = "none"; }

        // Build observation
        var obs = $"Copilot Security Posture: {posture}";
        if (criticalGaps.Count > 0)
            obs += $". {criticalGaps.Count} critical gap(s): {string.Join("; ", criticalGaps.Take(3))}";
        if (riskFactors.Count > 0)
            obs += $". {riskFactors.Count} risk factor(s) detected";
        if (strengths.Count > 0)
            obs += $". {strengths.Count} strength(s)";

        // Build recommendation
        string rec;
        if (criticalGaps.Count > 0)
            rec = $"Address {criticalGaps.Count} critical security gaps before broad Copilot deployment. Consider phased rollout to pilot group while remediating critical gaps.";
        else if (riskFactors.Count > 5)
            rec = $"Mitigate {riskFactors.Count} security risks before full-scale rollout. Safe to pilot with IT/security team while improving overall posture.";
        else if (riskFactors.Count > 2)
            rec = $"Address {riskFactors.Count} moderate risks. Copilot deployment can proceed with enhanced monitoring.";
        else
            rec = "Security posture is strong for Copilot deployment. Continue reviewing Defender recommendations monthly.";

        return new RecommendationResult
        {
            Service = Svc,
            Feature = feature,
            Status = status,
            Priority = priority,
            Observation = obs,
            Recommendation = rec,
            LinkText = "Copilot Security",
            LinkUrl = "https://learn.microsoft.com/microsoft-365-copilot/microsoft-365-copilot-security"
        };
    }

    // ================================================================
    //  16. COPILOT_THREAT_INTELLIGENCE (6-factor composite)
    // ================================================================

    private static RecommendationResult GenerateCopilotThreatIntelligence(DefenderInsights ins)
    {
        const string feature = "Copilot Threat Intelligence";

        if (!ins.Available)
            return RecommendationResult.Warning(Svc, feature,
                observation: "Unable to generate threat intelligence - Defender data not available.",
                recommendation: "Ensure Microsoft Defender license is assigned and service principal has required permissions.",
                linkText: "Microsoft Defender",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender/");

        var threats = new List<string>();
        int threatCount = 0;

        // Factor 1: Suspicious process activity
        if (ins.CopilotProcessEvents > 0)
        {
            threatCount++;
            threats.Add($"Suspicious process activity: {ins.CopilotProcessEvents} Copilot-related process events detected");
        }

        // Factor 2: Unusual network activity
        if (ins.CopilotNetworkEvents > 100)
        {
            threatCount++;
            threats.Add($"Unusual network activity: {ins.CopilotNetworkEvents} high-volume connections to Copilot/AI services");
        }

        // Factor 3: Sensitive file access
        if (ins.CopilotFileAccessEvents > 50)
        {
            threatCount++;
            threats.Add($"Sensitive file access: {ins.CopilotFileAccessEvents} sensitive files accessed before/during Copilot usage");
        }

        // Factor 4: AI-themed phishing
        if (ins.AiPhishingEmails > 0)
        {
            threatCount++;
            threats.Add($"AI-themed phishing: {ins.AiPhishingEmails} phishing attempts mentioning Copilot/AI keywords");
        }

        // Factor 5: Compromised accounts
        if (ins.ConfirmedCompromised > 0)
        {
            threatCount++;
            threats.Add($"Compromised accounts: {ins.ConfirmedCompromised} confirmed compromised accounts with potential Copilot access");
        }

        // Factor 6: Risky OAuth apps
        if (ins.HighRiskApps > 0)
        {
            threatCount++;
            threats.Add($"Third-party app risks: {ins.HighRiskApps} high-risk OAuth apps with excessive permissions");
        }

        if (threatCount == 0)
        {
            var cleanObs = "No significant Copilot-related threats detected in available data sources.";
            if (ins.DefenderApiAvailable)
                cleanObs += " Advanced Hunting analysis shows clean telemetry for process execution, network activity, file access, email threats, identities, and OAuth apps.";
            else
                cleanObs += " Limited to cloud-side threat detection without device telemetry. For complete analysis, onboard devices to Defender for Endpoint.";

            return RecommendationResult.Success(Svc, feature,
                observation: cleanObs,
                linkText: "Advanced Hunting for Copilot",
                linkUrl: "https://learn.microsoft.com/microsoft-365/security/defender/advanced-hunting-microsoft-365-copilot");
        }

        string status = threatCount >= 3 ? "action_required" : "warning";
        string priority = "high";
        var obs = $"{threatCount} threat(s) involving Copilot: {string.Join("; ", threats.Take(3))}";
        var rec = threatCount >= 3
            ? "Multiple active threats targeting Copilot detected. Investigate suspicious activity and review Advanced Hunting logs."
            : "Investigate identified threat(s) and review Advanced Hunting logs for Copilot-related events.";

        return new RecommendationResult
        {
            Service = Svc,
            Feature = feature,
            Status = status,
            Priority = priority,
            Observation = obs,
            Recommendation = rec,
            LinkText = "Advanced Hunting for Copilot",
            LinkUrl = "https://learn.microsoft.com/microsoft-365/security/defender/advanced-hunting-microsoft-365-copilot"
        };
    }

    // ================================================================
    //  17. COPILOT_DATA_GOVERNANCE (Purview-dependent composite)
    // ================================================================

    private static RecommendationResult GenerateCopilotDataGovernance(DefenderInsights ins)
    {
        const string feature = "Copilot Data Governance";

        // Without Purview data, provide a warning-level recommendation.
        // When PurviewPipeline is implemented, this will accept PurviewInsights
        // as a parameter and perform the full 7-assessment analysis
        // (DLP, labels, barriers, insider risk, comm compliance, retention, audit).

        var gaps = new List<string>();
        var strengths = new List<string>();

        // Use what Defender data we have for DLP-related insights
        if (ins.Available)
        {
            var dlpAlerts = 0;
            ins.AlertsByCategory.TryGetValue("DataLossPrevention", out dlpAlerts);
            if (dlpAlerts > 0)
                gaps.Add($"{dlpAlerts} DLP violations detected - users attempting to share sensitive data through Copilot");
        }

        // Provide the standard "no Purview" response, enriched with any DLP alert data
        if (gaps.Count > 0)
        {
            return new RecommendationResult
            {
                Service = Svc,
                Feature = feature,
                Status = "warning",
                Priority = "high",
                Observation = $"Copilot Data Governance Posture: AT RISK. Purview data not available for full assessment. {string.Join(". ", gaps)}",
                Recommendation = "Enable Microsoft Purview to assess and enforce data governance for Copilot. Deploy DLP policies, sensitivity labels, and retention policies. Run Purview data collection for complete analysis.",
                LinkText = "Purview for AI",
                LinkUrl = "https://learn.microsoft.com/purview/ai-microsoft-purview"
            };
        }

        return RecommendationResult.Warning(Svc, feature,
            observation: "Copilot Data Governance Posture: UNKNOWN. Purview data not available - unable to assess DLP policies, sensitivity labels, information barriers, insider risk, or retention policies.",
            recommendation: "Enable Microsoft Purview to assess and enforce data governance for Copilot. Includes DLP, sensitivity labels, retention policies, and information barriers. Run Purview data collection for complete assessment.",
            linkText: "Purview for AI",
            linkUrl: "https://learn.microsoft.com/purview/ai-microsoft-purview-copilot");
    }
}
