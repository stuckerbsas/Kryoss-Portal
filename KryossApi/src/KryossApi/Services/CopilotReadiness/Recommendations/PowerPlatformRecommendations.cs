using KryossApi.Services.CopilotReadiness.Pipelines;

namespace KryossApi.Services.CopilotReadiness.Recommendations;

/// <summary>
/// Generates Power Platform recommendations from license status (15 plans)
/// and deployment insights collected by <see cref="PowerPlatformPipeline"/>.
///
/// 15 plans are enriched with flow/app/connection data where available.
/// 2 pseudo-features (DLP_GOVERNANCE, AI_BUILDER_MODELS) run once per tenant
/// and are always appended regardless of specific plan presence.
/// </summary>
public static class PowerPlatformRecommendations
{
    private const string Svc = "power_platform";

    // ----------------------------------------------------------------
    // Friendly names
    // ----------------------------------------------------------------
    private static readonly Dictionary<string, string> FriendlyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "FLOW_O365_P3",           "Power Automate for Office 365 (Plan 3)" },
            { "FLOW_FREE",              "Power Automate (Free)" },
            { "FLOW_P2_VIRAL",          "Power Automate (P2 Viral)" },
            { "FLOW_CCI_BOTS",          "Power Automate for CCI Bots" },
            { "POWERAPPS_O365_P3",      "Power Apps for Office 365 (Plan 3)" },
            { "CDS_O365_P1",            "Common Data Service for Office 365 (P1)" },
            { "CDS_O365_P2",            "Common Data Service for Office 365 (P2)" },
            { "CDS_O365_P3",            "Common Data Service for Office 365 (P3)" },
            { "CDS_VIRAL",              "Common Data Service (Viral)" },
            { "DYN365_CDS_O365_P1",     "Dynamics 365 Common Data Service (O365 P1)" },
            { "DYN365_CDS_O365_P2",     "Dynamics 365 Common Data Service (O365 P2)" },
            { "DYN365_CDS_O365_P3",     "Dynamics 365 Common Data Service (O365 P3)" },
            { "DYN365_CDS_VIRAL",       "Dynamics 365 Common Data Service (Viral)" },
            { "DYN365_CDS_CCI_BOTS",    "Dynamics 365 Common Data Service for CCI Bots" },
            { "BI_AZURE_P2",            "Power BI Premium (Azure P2)" },
        };

    // ----------------------------------------------------------------
    // Plans with enriched checks
    // ----------------------------------------------------------------
    private static readonly HashSet<string> EnrichedPlans = new(StringComparer.OrdinalIgnoreCase)
    {
        "FLOW_O365_P3",
        "FLOW_FREE",
        "FLOW_P2_VIRAL",
        "FLOW_CCI_BOTS",
        "POWERAPPS_O365_P3",
        "BI_AZURE_P2",
    };

    // ----------------------------------------------------------------
    // Public entry point
    // ----------------------------------------------------------------
    public static List<RecommendationResult> Generate(
        Dictionary<string, string> planStatuses,
        PowerPlatformInsights ins)
    {
        var all = new List<RecommendationResult>();

        // --- Enriched checks ---
        TryAdd(all, CheckFlowO365P3(planStatuses, ins));
        TryAdd(all, CheckFlowFree(planStatuses, ins));
        TryAdd(all, CheckFlowP2Viral(planStatuses, ins));
        TryAdd(all, CheckFlowCciBots(planStatuses, ins));
        TryAdd(all, CheckPowerAppsO365P3(planStatuses, ins));
        TryAdd(all, CheckBiAzureP2(planStatuses, ins));

        // --- License-only for remaining Power Platform plans ---
        foreach (var (plan, status) in planStatuses)
        {
            if (!ServicePlanMapping.All.TryGetValue(plan, out var cat) ||
                cat != ServiceCategory.PowerPlatform)
                continue;

            if (EnrichedPlans.Contains(plan))
                continue;

            var friendly = FriendlyNames.TryGetValue(plan, out var fn) ? fn : plan;
            all.Add(LicenseOnly(friendly, status,
                "Power Platform Documentation",
                "https://learn.microsoft.com/power-platform/"));
        }

        // --- Pseudo-features (run once per tenant regardless of specific plan) ---
        // Only emit if at least one Power Platform plan is present.
        bool hasPpPlan = planStatuses.Any(kv =>
            ServicePlanMapping.All.TryGetValue(kv.Key, out var c) &&
            c == ServiceCategory.PowerPlatform);

        if (hasPpPlan)
        {
            all.AddRange(CheckDlpGovernance(ins));
            all.AddRange(CheckAiBuilderModels(ins));
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

    // --- Power Automate for Office 365 (Plan 3) ---
    private static List<RecommendationResult>? CheckFlowO365P3(
        Dictionary<string, string> plans, PowerPlatformInsights ins)
    {
        const string plan = "FLOW_O365_P3";
        const string feature = "Power Automate for Office 365 (Plan 3)";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling premium connector flows and RPA capabilities for Copilot integration.",
                "Power Automate for Copilot",
                "https://learn.microsoft.com/power-automate/"));

            if (ins.FlowsAvailable)
            {
                var cloud = ins.FlowsCloud;
                var desktop = ins.FlowsDesktop;
                var httpTriggers = ins.FlowsCopilotPluginCandidates;
                var suspended = ins.FlowsSuspended;

                if (cloud + desktop == 0)
                {
                    list.Add(RecommendationResult.ActionRequired(Svc, $"{feature} - Flow Deployment",
                        priority: "Medium",
                        observation: "No Power Automate flows deployed yet.",
                        recommendation: "Build Power Automate flows to extend Copilot capabilities: (1) Create flows with HTTP triggers as Copilot plugins, (2) Automate approval workflows triggered from Copilot prompts, (3) Connect enterprise systems (SharePoint, Teams, Dataverse) for Copilot orchestration. HTTP-triggered flows are the primary integration pattern for Copilot actions.",
                        linkText: "Power Automate for Copilot Actions",
                        linkUrl: "https://learn.microsoft.com/power-automate/get-started-with-copilot"));
                }
                else
                {
                    var obs = $"{cloud} cloud flows, {desktop} desktop flows deployed. {httpTriggers} flow(s) with HTTP triggers are Copilot plugin candidates.";
                    if (suspended > 0) obs += $" {suspended} suspended flow(s) need attention.";

                    if (httpTriggers >= 3)
                    {
                        list.Add(RecommendationResult.Success(Svc, $"{feature} - Copilot Plugin Candidates",
                            $"{obs} Strong plugin foundation: expose these flows as Copilot actions in Power Platform admin center.",
                            "Copilot Plugin Setup",
                            "https://learn.microsoft.com/microsoft-copilot-studio/copilot-plugins-overview"));
                    }
                    else if (httpTriggers >= 1)
                    {
                        list.Add(RecommendationResult.Warning(Svc, $"{feature} - Copilot Plugin Candidates",
                            obs,
                            "Add HTTP triggers to key automation flows to expose them as Copilot plugins. Target: at least 3-5 flows as Copilot actions covering core business processes (approvals, lookups, actions).",
                            "Add HTTP Triggers",
                            "https://learn.microsoft.com/power-automate/triggers-introduction"));
                    }
                    else
                    {
                        list.Add(RecommendationResult.Warning(Svc, $"{feature} - Copilot Plugin Candidates",
                            $"{cloud} cloud flows deployed but NONE have HTTP triggers — cannot be used as Copilot plugins.",
                            "Add HTTP Request triggers to your most important flows to expose them as Copilot actions. Users can then say 'Approve this request' or 'Look up customer X' and Copilot calls the flow automatically.",
                            "Enable Copilot Plugins",
                            "https://learn.microsoft.com/power-automate/triggers-introduction"));
                    }
                }
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, limiting premium automation and Copilot plugin development.",
                "Enable Power Automate Plan 3 to build premium-connector flows and expose them as Copilot plugins. HTTP-triggered flows are the primary extensibility pattern for Copilot actions connecting to enterprise systems.",
                "Power Automate for Copilot",
                "https://learn.microsoft.com/power-automate/"));
        }

        return list;
    }

    // --- Power Automate (Free) ---
    private static List<RecommendationResult>? CheckFlowFree(
        Dictionary<string, string> plans, PowerPlatformInsights ins)
    {
        const string plan = "FLOW_FREE";
        const string feature = "Power Automate (Free)";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, providing seeded automation capabilities included with M365 licenses.",
                "Power Automate Documentation",
                "https://learn.microsoft.com/power-automate/"));

            if (ins.FlowsAvailable && ins.FlowsCloud > 0)
            {
                list.Add(RecommendationResult.Success(Svc, $"{feature} - Flow Activity",
                    $"{ins.FlowsCloud} cloud flow(s) active. {ins.FlowsCopilotPluginCandidates} have HTTP triggers for Copilot integration.",
                    "Flows as Copilot Plugins",
                    "https://learn.microsoft.com/power-automate/get-started-with-copilot"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}.",
                $"Enable {feature} to provide basic automation capabilities for Copilot integration.",
                "Power Automate Documentation",
                "https://learn.microsoft.com/power-automate/"));
        }

        return list;
    }

    // --- Power Automate (P2 Viral) ---
    private static List<RecommendationResult>? CheckFlowP2Viral(
        Dictionary<string, string> plans, PowerPlatformInsights ins)
    {
        const string plan = "FLOW_P2_VIRAL";
        if (!plans.TryGetValue(plan, out var status)) return null;
        return new List<RecommendationResult>
        {
            LicenseOnly("Power Automate (P2 Viral)", status,
                "Power Automate Documentation",
                "https://learn.microsoft.com/power-automate/")
        };
    }

    // --- Power Automate for CCI Bots ---
    private static List<RecommendationResult>? CheckFlowCciBots(
        Dictionary<string, string> plans, PowerPlatformInsights ins)
    {
        const string plan = "FLOW_CCI_BOTS";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, "Power Automate for CCI Bots",
                "Power Automate for CCI Bots is active, enabling automation workflows triggered by Copilot Studio agents.",
                "Copilot Studio Actions",
                "https://learn.microsoft.com/microsoft-copilot-studio/advanced-flow"));
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, "Power Automate for CCI Bots",
                $"Power Automate for CCI Bots is {status}, limiting agent action capabilities.",
                "Enable this plan to allow Copilot Studio agents to trigger Power Automate flows for complex backend actions.",
                "Copilot Studio Actions",
                "https://learn.microsoft.com/microsoft-copilot-studio/advanced-flow"));
        }

        return list;
    }

    // --- Power Apps for Office 365 (Plan 3) ---
    private static List<RecommendationResult>? CheckPowerAppsO365P3(
        Dictionary<string, string> plans, PowerPlatformInsights ins)
    {
        const string plan = "POWERAPPS_O365_P3";
        const string feature = "Power Apps for Office 365 (Plan 3)";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling canvas and model-driven app development with Copilot integration.",
                "Power Apps with Copilot",
                "https://learn.microsoft.com/power-apps/"));

            if (ins.AppsAvailable)
            {
                if (ins.AppsCanvas + ins.AppsModelDriven == 0)
                {
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - App Deployment",
                        observation: "No Power Apps deployed yet.",
                        recommendation: "Build Power Apps with Copilot Control to embed AI directly in business apps. Canvas apps with Copilot Control let users query data in natural language. Start with a simple app — a ticket lookup, inventory check, or approval interface — and add the Copilot Control component.",
                        linkText: "Add Copilot to Canvas Apps",
                        linkUrl: "https://learn.microsoft.com/power-apps/maker/canvas-apps/add-ai-copilot"));
                }
                else
                {
                    var obs = $"{ins.AppsCanvas} canvas app(s), {ins.AppsModelDriven} model-driven app(s).";
                    if (ins.AppsTeamsIntegrated > 0) obs += $" {ins.AppsTeamsIntegrated} integrated in Teams.";

                    list.Add(RecommendationResult.Success(Svc, $"{feature} - App Inventory",
                        obs + " Consider adding Copilot Control to canvas apps for natural-language data querying.",
                        "Copilot Control in Power Apps",
                        "https://learn.microsoft.com/power-apps/maker/canvas-apps/add-ai-copilot"));
                }
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, preventing Power Apps development with Copilot integration.",
                "Enable Power Apps Plan 3 to build apps with Copilot Control — allowing users to query Dataverse and SharePoint data using natural language within your business applications.",
                "Power Apps with Copilot",
                "https://learn.microsoft.com/power-apps/"));
        }

        return list;
    }

    // --- Power BI Premium (Azure P2) ---
    private static List<RecommendationResult>? CheckBiAzureP2(
        Dictionary<string, string> plans, PowerPlatformInsights ins)
    {
        const string plan = "BI_AZURE_P2";
        const string feature = "Power BI Premium (Azure P2)";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, providing dedicated BI capacity and enabling Copilot in Power BI for natural-language analytics.",
                "Copilot in Power BI",
                "https://learn.microsoft.com/power-bi/copilot/copilot-introduction"));
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, limiting Power BI capacity and Copilot analytics capabilities.",
                "Enable Power BI Premium to unlock Copilot in Power BI — allowing users to ask questions about reports and dashboards in natural language, auto-generate executive summaries, and build DAX measures via conversation.",
                "Copilot in Power BI",
                "https://learn.microsoft.com/power-bi/copilot/copilot-introduction"));
        }

        return list;
    }

    // ================================================================
    // Pseudo-features
    // ================================================================

    // --- DLP Governance (runs once, tenant-wide) ---
    private static List<RecommendationResult> CheckDlpGovernance(PowerPlatformInsights ins)
    {
        var list = new List<RecommendationResult>();

        if (!ins.DlpAvailable)
        {
            list.Add(RecommendationResult.Warning(Svc, "DLP Governance - Assessment Needed",
                observation: "DLP policy assessment unavailable — requires Power Platform Administrator access.",
                recommendation: "Request Power Platform Administrator role to assess DLP policies: (1) Review if HTTP connector is blocked (prevents ALL plugin development), (2) Check custom connector restrictions, (3) Identify premium connector blocks, (4) Create a dedicated 'Copilot Extensibility' environment if tenant-wide policies are too restrictive. DLP governance is critical for Copilot adoption — overly restrictive policies block plugin development entirely.",
                linkText: "DLP Policy Review",
                linkUrl: "https://learn.microsoft.com/power-platform/admin/wp-data-loss-prevention"));
            return list;
        }

        int total = ins.DlpPoliciesTenantWide + ins.DlpPoliciesEnvLevel;

        if (total == 0)
        {
            list.Add(RecommendationResult.Warning(Svc, "DLP Governance - Copilot Extensibility",
                observation: "No DLP policies configured — maximum flexibility for Copilot plugin development, but no governance.",
                recommendation: "Implement DLP policies while preserving Copilot extensibility: (1) Create environment-specific policies (not tenant-wide) to isolate plugin development, (2) Allow HTTP and Custom Connectors in a dedicated 'Copilot Innovation' environment, (3) Restrict only high-risk connectors (social media, consumer storage), (4) Document governance exceptions for Copilot plugins. Balance innovation with data protection.",
                linkText: "DLP Policy Best Practices",
                linkUrl: "https://learn.microsoft.com/power-platform/admin/wp-data-loss-prevention"));
            return list;
        }

        // Has policies — check for blockers.
        if (ins.DlpHttpBlocked)
        {
            var restricted = ins.DlpRestrictedConnectors.Count > 0
                ? string.Join(", ", ins.DlpRestrictedConnectors.Distinct())
                : "HTTP";

            list.Add(RecommendationResult.ActionRequired(Svc, "DLP Governance - BLOCKER: HTTP Connector",
                priority: "High",
                observation: $"DLP policies block HTTP connector ({ins.DlpPoliciesTenantWide} tenant-wide) — BLOCKS Copilot plugin development entirely. Restricted: {restricted}.",
                recommendation: "CRITICAL: HTTP connector is blocked, preventing ALL Copilot plugin creation. Required actions: (1) Create dedicated 'Copilot Extensibility' environment exempt from HTTP blocking, (2) Move plugin development to this environment, (3) Update DLP policy to allow HTTP in this environment only, (4) Document plugin approval process before production deployment. Without HTTP connector, you CANNOT build custom Copilot plugins or actions. This is the #1 blocker for M365 Copilot extensibility.",
                linkText: "DLP Policy Configuration",
                linkUrl: "https://learn.microsoft.com/power-platform/admin/create-dlp-policy"));
        }
        else if (ins.DlpCustomConnectorsBlocked)
        {
            list.Add(RecommendationResult.Warning(Svc, "DLP Governance - WARNING: Custom Connectors",
                observation: $"DLP policies block custom connectors — limits Copilot extensibility to pre-built connectors only ({ins.DlpPoliciesTenantWide} tenant-wide policies).",
                recommendation: "Custom connectors are restricted, limiting Copilot extensibility options. Recommended: (1) Create 'Copilot Innovation' environment with custom connector access, (2) Implement approval workflow for custom connectors (security review + IT approval), (3) Allow custom connectors for approved internal APIs, (4) Maintain a list of approved custom connectors. Custom connectors enable Copilot to interact with proprietary systems — consider risk-based exceptions rather than blanket blocking.",
                linkText: "Custom Connector Governance",
                linkUrl: "https://learn.microsoft.com/power-platform/admin/dlp-custom-connector-parity"));
        }
        else if (ins.DlpPremiumBlocked)
        {
            list.Add(RecommendationResult.Warning(Svc, "DLP Governance - Premium Connector Restrictions",
                observation: $"DLP policies restrict some premium connectors — may limit enterprise system integrations for Copilot ({total} total policies).",
                recommendation: "Review premium connector restrictions: (1) Identify which premium connectors are needed for Copilot scenarios (SAP, Salesforce, ServiceNow, SQL), (2) Create exception policies for approved enterprise integrations, (3) Document business justification for each premium connector. Premium connectors enable Copilot to access enterprise systems — restricting them limits agent capabilities.",
                linkText: "Premium Connector Management",
                linkUrl: "https://learn.microsoft.com/power-platform/admin/dlp-connector-classification"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, "DLP Governance - Copilot Friendly",
                $"{total} DLP policy(ies) configured without blocking critical Copilot extensibility connectors — well-balanced governance. {ins.DlpPoliciesTenantWide} tenant-wide, {ins.DlpPoliciesEnvLevel} environment-level.",
                "DLP Policy Review",
                "https://learn.microsoft.com/power-platform/admin/dlp-policy-commands"));
        }

        return list;
    }

    // --- AI Builder Models (runs once, tenant-wide) ---
    private static List<RecommendationResult> CheckAiBuilderModels(PowerPlatformInsights ins)
    {
        var list = new List<RecommendationResult>();

        if (!ins.AiModelsAvailable)
        {
            list.Add(RecommendationResult.Warning(Svc, "AI Builder - Assessment Needed",
                observation: "AI Builder model inventory unavailable — requires Power Platform Administrator access.",
                recommendation: "Request Power Platform Administrator role to assess AI Builder models: (1) Inventory existing models (document processing, predictions, text classification), (2) Identify published models ready for Copilot integration, (3) Map model capabilities to Copilot scenarios, (4) Create Power Automate flows with HTTP triggers to expose models as Copilot plugins. AI Builder models significantly enhance Copilot intelligence.",
                linkText: "AI Builder Overview",
                linkUrl: "https://learn.microsoft.com/ai-builder/overview"));
            return list;
        }

        if (ins.AiModelsTotal == 0)
        {
            list.Add(RecommendationResult.Warning(Svc, "AI Builder - Get Started with Copilot AI",
                observation: "No AI Builder models deployed — opportunity to add intelligent automation to Copilot workflows.",
                recommendation: "Build AI Builder models to enhance Copilot: (1) Document processing (invoice extraction, receipt scanning) — integrate for 'Process this invoice' prompts, (2) Prediction models for business outcomes (lead scoring, churn prediction) — Copilot can query predictions in natural language, (3) Text classification for email/document categorization. Target: 2-3 production models in the first 90 days.",
                linkText: "AI Builder for Copilot",
                linkUrl: "https://learn.microsoft.com/ai-builder/overview"));
            return list;
        }

        // Has models.
        if (ins.AiModelsAiBuilder > 0)
        {
            list.Add(RecommendationResult.Success(Svc, "AI Builder - Model Inventory",
                $"{ins.AiModelsTotal} AI Builder model(s) deployed ({ins.AiModelsAiBuilder} AI Builder, {ins.AiModelsAzureMl} Azure ML, {ins.AiModelsCustom} custom). Integrate with Copilot: create Power Automate flows with HTTP triggers that invoke these models and expose them as Copilot plugins.",
                "AI Builder Integration Patterns",
                "https://learn.microsoft.com/ai-builder/use-in-flow-overview"));
        }
        else
        {
            list.Add(RecommendationResult.Warning(Svc, "AI Builder - Model Integration",
                observation: $"{ins.AiModelsTotal} AI model(s) found ({ins.AiModelsAzureMl} Azure ML, {ins.AiModelsCustom} custom). Consider AI Builder models for native Copilot integration.",
                recommendation: "Evaluate AI Builder for native Power Platform integration: AI Builder models integrate directly with Copilot Studio agents without custom API calls. Azure ML models require additional HTTP connector setup. AI Builder document processing and prediction models are the fastest path to Copilot intelligence augmentation.",
                linkText: "AI Builder vs Azure ML",
                linkUrl: "https://learn.microsoft.com/ai-builder/overview"));
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
            $"Enable {feature} for full Power Platform and Copilot integration.",
            linkText, linkUrl);
    }
}
