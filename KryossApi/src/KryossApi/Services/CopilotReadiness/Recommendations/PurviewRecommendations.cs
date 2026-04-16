using KryossApi.Services.CopilotReadiness.Pipelines;

namespace KryossApi.Services.CopilotReadiness.Recommendations;

/// <summary>
/// Generates Purview compliance recommendations from license status (36 plans)
/// and the limited Graph API data available (sensitivity labels, DLP alerts,
/// eDiscovery cases, audit config).
///
/// Most checks are license-only because full Purview deployment data requires
/// Exchange Online Management cmdlets (not available from the backend).
/// Six plans are enriched where Graph data is available.
/// </summary>
public static class PurviewRecommendations
{
    private const string Svc = "purview";

    // ----------------------------------------------------------------
    // Friendly names for service plan codes
    // ----------------------------------------------------------------
    private static readonly Dictionary<string, string> FriendlyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "AIP_P1",                                     "Azure Information Protection Premium P1" },
            { "AIP_P2",                                     "Azure Information Protection Premium P2" },
            { "COMMUNICATIONS_COMPLIANCE",                  "Communication Compliance" },
            { "COMMUNICATIONS_DLP",                         "Communications DLP" },
            { "ContentExplorer_Standard",                   "Content Explorer (Standard)" },
            { "CONTENTEXPLORER_STANDARD_ACTIVITY",          "Content Explorer Activity" },
            { "Content_Explorer",                           "Content Explorer" },
            { "CustomerLockboxA_Enterprise",                "Customer Lockbox (Enterprise A)" },
            { "CUSTOMER_KEY",                               "Customer Key" },
            { "DATAINVESTIGATION",                          "Data Investigations" },
            { "DATA_INVESTIGATIONS",                        "Microsoft Purview Data Investigations" },
            { "EDISCOVERY",                                 "Microsoft Purview eDiscovery" },
            { "EQUIVIO_ANALYTICS",                          "eDiscovery Analytics" },
            { "EQUIVIO_ANALYTICS_EDM",                      "eDiscovery Analytics (EDM)" },
            { "INFORMATION_BARRIERS",                       "Information Barriers" },
            { "INFORMATION_PROTECTION_ANALYTICS",           "Information Protection Analytics" },
            { "INFORMATION_PROTECTION_COMPLIANCE_PREMIUM",  "Information Protection for Office 365 - Premium" },
            { "INFO_GOVERNANCE",                            "Information Governance" },
            { "INSIDER_RISK",                               "Insider Risk Management (Base)" },
            { "INSIDER_RISK_MANAGEMENT",                    "Insider Risk Management" },
            { "LOCKBOX_ENTERPRISE",                         "Customer Lockbox (Enterprise)" },
            { "M365_ADVANCED_AUDITING",                     "Microsoft 365 Advanced Auditing" },
            { "M365_AUDIT_PLATFORM",                        "Microsoft 365 Audit Platform" },
            { "MICROSOFTENDPOINTDLP",                       "Microsoft Endpoint DLP" },
            { "MICROSOFT_COMMUNICATION_COMPLIANCE",         "Microsoft Communication Compliance" },
            { "MIP_S_CLP1",                                 "MIP Sensitivity Labels CLP1" },
            { "MIP_S_CLP2",                                 "MIP Sensitivity Labels CLP2" },
            { "MIP_S_Exchange",                             "MIP Sensitivity Labels for Exchange" },
            { "ML_CLASSIFICATION",                          "Trainable Classifiers (ML Classification)" },
            { "PAM_ENTERPRISE",                             "Privileged Access Management (Enterprise)" },
            { "PREMIUM_ENCRYPTION",                         "Premium Encryption" },
            { "PURVIEW_DISCOVERY",                          "Microsoft Purview Discovery" },
            { "RECORDS_MANAGEMENT",                         "Records Management" },
            { "RMS_S_ENTERPRISE",                           "Azure Rights Management (Enterprise)" },
            { "RMS_S_PREMIUM",                              "Azure Rights Management Premium" },
            { "RMS_S_PREMIUM2",                             "Azure Rights Management Premium P2" },
        };

    // ----------------------------------------------------------------
    // Plans that have enriched checks — excluded from generic loop
    // ----------------------------------------------------------------
    private static readonly HashSet<string> EnrichedPlans = new(StringComparer.OrdinalIgnoreCase)
    {
        "AIP_P1",
        "AIP_P2",
        "COMMUNICATIONS_COMPLIANCE",
        "MICROSOFT_COMMUNICATION_COMPLIANCE",
        "EDISCOVERY",
        "PURVIEW_DISCOVERY",
        "DATA_INVESTIGATIONS",
        "DATAINVESTIGATION",
        "INFORMATION_BARRIERS",
        "INSIDER_RISK_MANAGEMENT",
        "M365_ADVANCED_AUDITING",
        "CustomerLockboxA_Enterprise",
        "LOCKBOX_ENTERPRISE",
    };

    // ----------------------------------------------------------------
    // Public entry point
    // ----------------------------------------------------------------
    public static List<RecommendationResult> Generate(
        Dictionary<string, string> planStatuses,
        PurviewInsights ins)
    {
        var all = new List<RecommendationResult>();

        // --- Enriched checks ---
        TryAdd(all, CheckAipP1(planStatuses, ins));
        TryAdd(all, CheckAipP2(planStatuses, ins));
        TryAdd(all, CheckCommunicationsCompliance(planStatuses, ins));
        TryAdd(all, CheckMicrosoftCommunicationCompliance(planStatuses, ins));
        TryAdd(all, CheckEDiscovery(planStatuses, ins));
        TryAdd(all, CheckPurviewDiscovery(planStatuses, ins));
        TryAdd(all, CheckDataInvestigations(planStatuses, ins));
        TryAdd(all, CheckDataInvestigation(planStatuses, ins));
        TryAdd(all, CheckInformationBarriers(planStatuses, ins));
        TryAdd(all, CheckInsiderRiskManagement(planStatuses, ins));
        TryAdd(all, CheckAdvancedAuditing(planStatuses, ins));
        TryAdd(all, CheckCustomerLockboxA(planStatuses, ins));
        TryAdd(all, CheckLockboxEnterprise(planStatuses, ins));

        // --- License-only for the remaining Purview plans ---
        foreach (var (plan, status) in planStatuses)
        {
            if (!ServicePlanMapping.All.TryGetValue(plan, out var cat) ||
                cat != ServiceCategory.Purview)
                continue;

            if (EnrichedPlans.Contains(plan))
                continue;

            var friendly = FriendlyNames.TryGetValue(plan, out var fn) ? fn : plan;
            all.Add(LicenseOnly(friendly, status,
                "Microsoft Purview Documentation",
                "https://learn.microsoft.com/purview/"));
        }

        return all;
    }

    private static void TryAdd(List<RecommendationResult> list, List<RecommendationResult>? items)
    {
        if (items is not null) list.AddRange(items);
    }

    // ================================================================
    // Enriched checks
    // ================================================================

    // --- AIP P1 ---
    private static List<RecommendationResult>? CheckAipP1(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "AIP_P1";
        const string feature = "Azure Information Protection Premium P1";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling label-aware Copilot operations on classified content.",
                "Information Protection for AI",
                "https://learn.microsoft.com/azure/information-protection/"));

            if (ins.LabelsAvailable)
            {
                if (ins.LabelCount > 0)
                {
                    var names = string.Join(", ", ins.LabelNames.Take(3));
                    if (ins.LabelCount > 3) names += $" (+{ins.LabelCount - 3} more)";
                    list.Add(RecommendationResult.Success(Svc, $"{feature} - Label Deployment",
                        $"{ins.LabelCount} sensitivity labels configured: {names}. Verify that Confidential labels prevent AI summarization and Highly Confidential labels enforce encryption on AI-generated outputs.",
                        "Sensitivity Labels for AI",
                        "https://learn.microsoft.com/purview/sensitivity-labels"));
                }
                else
                {
                    list.Add(RecommendationResult.ActionRequired(Svc, $"{feature} - Label Deployment",
                        priority: "High",
                        observation: "AIP P1 license active but NO sensitivity labels configured.",
                        recommendation: "Deploy sensitivity labels to control Copilot handling of classified content: Confidential (restrict AI summarization), Internal (allow internal AI use only), Public (full AI access), Highly Confidential (encryption required). Without labels, Copilot treats all content equally, potentially exposing sensitive data through AI responses.",
                        linkText: "Create Sensitivity Labels",
                        linkUrl: "https://learn.microsoft.com/purview/create-sensitivity-labels"));
                }
            }
            else
            {
                list.Add(RecommendationResult.Warning(Svc, $"{feature} - Label Deployment",
                    observation: "Label deployment status could not be verified via Graph API. Deeper Purview inspection requires admin portal.",
                    recommendation: "Verify sensitivity label deployment in Microsoft Purview portal > Information protection > Labels. Ensure labels control Copilot behavior for classified content.",
                    linkText: "Purview Compliance Portal",
                    linkUrl: "https://compliance.microsoft.com"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, missing classification controls for AI content handling.",
                "Enable AIP P1 to apply sensitivity labels that govern how Copilot handles information. Labels can restrict whether Copilot can include labeled content in responses, prevent AI from processing highly confidential data, and enforce encryption on Copilot-generated outputs containing sensitive information.",
                "Information Protection for AI",
                "https://learn.microsoft.com/azure/information-protection/"));
        }

        return list;
    }

    // --- AIP P2 ---
    private static List<RecommendationResult>? CheckAipP2(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "AIP_P2";
        const string feature = "Azure Information Protection Premium P2";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling automatic classification of Copilot-generated content.",
                "Advanced Protection for AI Content",
                "https://learn.microsoft.com/azure/information-protection/"));

            if (ins.LabelsAvailable)
            {
                if (ins.LabelCount > 0)
                {
                    list.Add(RecommendationResult.Success(Svc, $"{feature} - Auto-Classification",
                        $"{ins.LabelCount} sensitivity labels available for auto-classification. Configure automatic labeling conditions: auto-label documents containing PII patterns, apply Confidential to AI responses containing financial keywords, detect trade secrets in Copilot outputs.",
                        "Auto-Labeling for AI Content",
                        "https://learn.microsoft.com/purview/apply-sensitivity-label-automatically"));
                }
                else
                {
                    list.Add(RecommendationResult.ActionRequired(Svc, $"{feature} - Auto-Classification",
                        priority: "High",
                        observation: "AIP P2 license active but NO sensitivity labels configured for auto-classification.",
                        recommendation: "Deploy sensitivity labels with automatic classification rules to detect PII in Copilot responses, financial data in AI summaries, and trade secrets. Without auto-labeling, users manually classify Copilot outputs — often incorrectly.",
                        linkText: "Configure Auto-Labeling",
                        linkUrl: "https://learn.microsoft.com/purview/apply-sensitivity-label-automatically"));
                }
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, missing automatic classification for AI workflows.",
                "Enable AIP P2 to automatically classify and protect content that Copilot creates or accesses. AIP P2 applies labels based on content inspection (keywords, patterns, regex), ensuring Copilot-generated summaries inherit appropriate protections automatically.",
                "Advanced Protection for AI Content",
                "https://learn.microsoft.com/azure/information-protection/"));
        }

        return list;
    }

    // --- Communication Compliance ---
    private static List<RecommendationResult>? CheckCommunicationsCompliance(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "COMMUNICATIONS_COMPLIANCE";
        const string feature = "Communication Compliance";
        if (!plans.TryGetValue(plan, out var status)) return null;

        return BuildCommunicationComplianceRec(feature, status);
    }

    private static List<RecommendationResult>? CheckMicrosoftCommunicationCompliance(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "MICROSOFT_COMMUNICATION_COMPLIANCE";
        const string feature = "Microsoft Communication Compliance";
        if (!plans.TryGetValue(plan, out var status)) return null;

        return BuildCommunicationComplianceRec(feature, status);
    }

    private static List<RecommendationResult> BuildCommunicationComplianceRec(string feature, string status)
    {
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, monitoring Copilot conversations for compliance risks.",
                "Monitor AI Conversations for Compliance",
                "https://learn.microsoft.com/purview/communication-compliance"));

            list.Add(RecommendationResult.Warning(Svc, $"{feature} - Policy Deployment",
                observation: "Deeper policy deployment status requires Purview compliance portal. Verify policies are configured to monitor Copilot interactions.",
                recommendation: "Deploy Communication Compliance policies to monitor Copilot usage: (1) detect confidential data keywords in prompts, (2) monitor for inappropriate language in AI conversations, (3) detect attempts to use Copilot for prohibited purposes, (4) flag cross-barrier communication via AI. Configure in Purview > Communication compliance.",
                linkText: "Communication Compliance Policies",
                linkUrl: "https://learn.microsoft.com/purview/communication-compliance-policies"));
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, leaving Copilot usage unmonitored for compliance violations.",
                "Enable Communication Compliance to monitor M365 Copilot conversations for regulatory violations, inappropriate use cases, and sensitive data exposure. Detect when employees attempt to bypass information barriers through AI or share confidential information in Copilot chats.",
                "Monitor AI Conversations for Compliance",
                "https://learn.microsoft.com/purview/communication-compliance"));
        }

        return list;
    }

    // --- eDiscovery ---
    private static List<RecommendationResult>? CheckEDiscovery(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "EDISCOVERY";
        const string feature = "Microsoft Purview eDiscovery";
        if (!plans.TryGetValue(plan, out var status)) return null;

        return BuildEDiscoveryRec(feature, status, ins);
    }

    private static List<RecommendationResult>? CheckPurviewDiscovery(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "PURVIEW_DISCOVERY";
        if (!plans.TryGetValue(plan, out var status)) return null;

        return BuildEDiscoveryRec("Microsoft Purview Discovery", status, ins);
    }

    private static List<RecommendationResult>? CheckDataInvestigations(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "DATA_INVESTIGATIONS";
        if (!plans.TryGetValue(plan, out var status)) return null;

        return BuildEDiscoveryRec("Microsoft Purview Data Investigations", status, ins);
    }

    private static List<RecommendationResult>? CheckDataInvestigation(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "DATAINVESTIGATION";
        if (!plans.TryGetValue(plan, out var status)) return null;

        return BuildEDiscoveryRec("Data Investigations", status, ins);
    }

    private static List<RecommendationResult> BuildEDiscoveryRec(
        string feature, string status, PurviewInsights ins)
    {
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling legal hold and eDiscovery of data including Copilot interactions.",
                "Legal Discovery for AI Content",
                "https://learn.microsoft.com/purview/ediscovery/"));

            if (ins.EDiscoveryAvailable)
            {
                if (ins.EDiscoveryCasesActive > 0)
                {
                    list.Add(RecommendationResult.Success(Svc, $"{feature} - Case Status",
                        $"{ins.EDiscoveryCasesActive} active eDiscovery case(s) configured. Ensure cases include Copilot interaction logs (CopilotInteraction, PromptSubmitted audit events), documents accessed or generated by Copilot, and meeting transcripts where Copilot was used.",
                        "eDiscovery Best Practices",
                        "https://learn.microsoft.com/purview/ediscovery-standard"));
                }
                else
                {
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - Readiness",
                        observation: "No eDiscovery cases currently active. Prepare readiness for Copilot-related legal matters.",
                        recommendation: "Document procedures for collecting Copilot interaction logs, train legal/IT teams on searching AI-generated content, test case creation and data collection workflow. When litigation occurs, Copilot conversation histories and AI-accessed documents may be discoverable.",
                        linkText: "eDiscovery Planning",
                        linkUrl: "https://learn.microsoft.com/purview/ediscovery-plan"));
                }
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, limiting legal discovery capabilities for AI interactions.",
                "Enable eDiscovery to preserve, search, and export content for legal matters including Copilot interactions. Critical for organizations facing litigation where AI-assisted work product may be subject to discovery requests.",
                "Legal Discovery for AI Content",
                "https://learn.microsoft.com/purview/ediscovery/"));
        }

        return list;
    }

    // --- Information Barriers ---
    private static List<RecommendationResult>? CheckInformationBarriers(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "INFORMATION_BARRIERS";
        const string feature = "Information Barriers";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, preventing Copilot from crossing compliance boundaries.",
                "Information Barriers for AI Compliance",
                "https://learn.microsoft.com/purview/information-barriers"));

            list.Add(RecommendationResult.Warning(Svc, $"{feature} - Policy Deployment",
                observation: "Policy deployment status requires Purview compliance portal (Exchange Online Management). Verify that Information Barrier policies are configured and active.",
                recommendation: "Deploy Information Barrier policies for Copilot compliance. Define segments and barriers: M&A teams on competing deals, research/trading divisions, legal teams on opposing cases. Without barriers, Copilot becomes an information leak vector across restricted groups. Configure in Purview > Information barriers.",
                linkText: "Configure Information Barriers",
                linkUrl: "https://learn.microsoft.com/purview/information-barriers-policies"));
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, risking compliance violations through AI data sharing.",
                "Enable Information Barriers to enforce ethical walls that Copilot must respect. In financial services, legal firms, and regulated industries, certain employees cannot share information. Information Barriers ensure Copilot only accesses data the user is permitted to see, preventing the AI from becoming a conduit for inappropriate information flow.",
                "Information Barriers for AI Compliance",
                "https://learn.microsoft.com/purview/information-barriers"));
        }

        return list;
    }

    // --- Insider Risk Management ---
    private static List<RecommendationResult>? CheckInsiderRiskManagement(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "INSIDER_RISK_MANAGEMENT";
        const string feature = "Insider Risk Management";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, monitoring for data exfiltration risks through Copilot usage.",
                "Detect Copilot Misuse with Insider Risk",
                "https://learn.microsoft.com/purview/insider-risk-management"));

            list.Add(RecommendationResult.Warning(Svc, $"{feature} - Policy Deployment",
                observation: "Policy deployment status requires Purview compliance portal. Verify that Insider Risk policies are configured to detect Copilot-specific risks.",
                recommendation: "Deploy Insider Risk policies for Copilot: (1) data exfiltration by departing employees (spike in Copilot queries + downloads before resignation), (2) unauthorized data access (using Copilot to explore sensitive areas beyond normal scope), (3) intellectual property theft (aggregating trade secrets via AI). Configure in Purview > Insider risk management.",
                linkText: "Create Insider Risk Policies",
                linkUrl: "https://learn.microsoft.com/purview/insider-risk-management-configure"));
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, missing detection of AI-assisted data theft.",
                "Enable Insider Risk Management to identify malicious use of Copilot for data gathering. Detect when departing employees use Copilot to quickly aggregate competitive intelligence, customer lists, or intellectual property before resignation. Copilot makes data theft easier and faster — Insider Risk detects the patterns.",
                "Detect Copilot Misuse with Insider Risk",
                "https://learn.microsoft.com/purview/insider-risk-management"));
        }

        return list;
    }

    // --- Advanced Auditing ---
    private static List<RecommendationResult>? CheckAdvancedAuditing(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "M365_ADVANCED_AUDITING";
        const string feature = "Microsoft 365 Advanced Auditing";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, logging detailed Copilot interactions and data access patterns.",
                "Audit Copilot Activity for Compliance",
                "https://learn.microsoft.com/purview/audit-solutions-overview"));

            // Audit config not readable via Graph — always surface guidance
            list.Add(RecommendationResult.Warning(Svc, $"{feature} - Copilot Event Tracking",
                observation: "Audit configuration (Unified Audit Log status, retention policies) requires Exchange Online Management or Purview compliance portal. Cannot be verified via Graph API.",
                recommendation: "Configure Advanced Auditing for Copilot in Microsoft Purview compliance portal: (1) Verify Unified Audit Log is enabled — run Set-AdminAuditLogConfig -UnifiedAuditLogIngestionEnabled $true if needed, (2) Set retention to 10 years for Copilot events (vs default 90 days), (3) Confirm Copilot event types are captured (CopilotInteraction, PromptSubmitted, SensitiveDataAccessed), (4) Create audit alerts for high-risk patterns, (5) Export to SIEM for correlation with security events.",
                linkText: "Configure Copilot Audit Policies",
                linkUrl: "https://learn.microsoft.com/purview/audit-log-retention-policies"));
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, preventing comprehensive audit trails of Copilot usage.",
                "Enable Advanced Auditing to capture extended audit logs (10-year retention) of all M365 Copilot activities — which files were accessed by AI, what prompts users entered, when sensitive data was retrieved, and who modified Copilot settings. Required for SOC 2, HIPAA, and financial services compliance.",
                "Audit Copilot Activity for Compliance",
                "https://learn.microsoft.com/purview/audit-solutions-overview"));
        }

        return list;
    }

    // --- Customer Lockbox (Enterprise A) ---
    private static List<RecommendationResult>? CheckCustomerLockboxA(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "CustomerLockboxA_Enterprise";
        const string feature = "Customer Lockbox (Enterprise A)";
        if (!plans.TryGetValue(plan, out var status)) return null;

        return BuildCustomerLockboxRec(feature, status, ins);
    }

    // --- Customer Lockbox (Enterprise) ---
    private static List<RecommendationResult>? CheckLockboxEnterprise(
        Dictionary<string, string> plans, PurviewInsights ins)
    {
        const string plan = "LOCKBOX_ENTERPRISE";
        const string feature = "Customer Lockbox (Enterprise)";
        if (!plans.TryGetValue(plan, out var status)) return null;

        return BuildCustomerLockboxRec(feature, status, ins);
    }

    private static List<RecommendationResult> BuildCustomerLockboxRec(
        string feature, string status, PurviewInsights ins)
    {
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, requiring approval for Microsoft access to Copilot-indexed content.",
                "Control Microsoft Data Access",
                "https://learn.microsoft.com/purview/customer-lockbox-requests"));

            if (ins.CustomerLockboxAvailable)
            {
                if (ins.CustomerLockboxEnabled)
                {
                    list.Add(RecommendationResult.Success(Svc, $"{feature} - Configuration",
                        "Customer Lockbox is ENABLED. Microsoft support requires your approval before accessing tenant data. Ensure designated approvers are configured and the escalation path is defined for urgent support needs.",
                        "Manage Lockbox Requests",
                        "https://learn.microsoft.com/purview/customer-lockbox-requests"));
                }
                else
                {
                    list.Add(RecommendationResult.ActionRequired(Svc, $"{feature} - Configuration",
                        priority: "High",
                        observation: "Customer Lockbox license active but DISABLED — Microsoft support can access data without approval.",
                        recommendation: "Enable Customer Lockbox in Microsoft 365 admin center: Settings > Security & privacy > Customer lockbox > Edit > Enable. Once enabled, Microsoft engineers must request approval before accessing your tenant data including Copilot conversation histories.",
                        linkText: "Enable Customer Lockbox",
                        linkUrl: "https://learn.microsoft.com/purview/customer-lockbox-requests#enable-customer-lockbox"));
                }
            }
            else
            {
                list.Add(RecommendationResult.Warning(Svc, $"{feature} - Configuration",
                    observation: "Customer Lockbox configuration status cannot be verified via Graph API. Verify in Microsoft 365 admin center.",
                    recommendation: "Confirm Customer Lockbox is enabled in Microsoft 365 admin center > Settings > Security & privacy > Customer lockbox. With Copilot processing sensitive business information, Lockbox ensures Microsoft cannot view AI interactions without explicit approval.",
                    linkText: "Verify Customer Lockbox",
                    linkUrl: "https://learn.microsoft.com/purview/customer-lockbox-requests"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, missing control over Microsoft's access to AI training data.",
                "Enable Customer Lockbox to require explicit approval before Microsoft engineers can access your organization's data during support operations. With Copilot processing sensitive business information, Lockbox ensures Microsoft cannot view AI interactions, prompts, or Copilot-generated content without permission. Critical for regulated industries.",
                "Control Microsoft Data Access",
                "https://learn.microsoft.com/purview/customer-lockbox-requests"));
        }

        return list;
    }

    // ================================================================
    // License-only helper
    // ================================================================
    private static RecommendationResult LicenseOnly(
        string feature, string status, string linkText, string linkUrl)
    {
        if (status == "Success")
            return RecommendationResult.Success(Svc, feature,
                $"{feature} is active.",
                linkText, linkUrl);

        return RecommendationResult.Disabled(Svc, feature,
            $"{feature} is {status}.",
            $"Enable {feature} for full Copilot compliance integration.",
            linkText, linkUrl);
    }
}
