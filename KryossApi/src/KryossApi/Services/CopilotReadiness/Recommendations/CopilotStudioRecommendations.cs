using KryossApi.Services.CopilotReadiness.Pipelines;

namespace KryossApi.Services.CopilotReadiness.Recommendations;

/// <summary>
/// Generates Copilot Studio recommendations from license status (11 plans).
/// All checks are license-only. PowerPlatformInsights is passed for context
/// when emitting deployment guidance (environment counts, flow counts, etc.)
/// </summary>
public static class CopilotStudioRecommendations
{
    private const string Svc = "copilot_studio";

    // ----------------------------------------------------------------
    // Friendly names
    // ----------------------------------------------------------------
    private static readonly Dictionary<string, string> FriendlyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "POWER_VIRTUAL_AGENTS",                   "Power Virtual Agents" },
            { "POWER_VIRTUAL_AGENTS_BASE",              "Power Virtual Agents (Base)" },
            { "POWER_VIRTUAL_AGENTS_O365_P3",           "Power Virtual Agents for Office 365" },
            { "CDS_VIRTUAL_AGENT_BASE_MESSAGES",        "CDS Virtual Agent Base Messages" },
            { "CDS_VIRTUAL_AGENT_USL",                  "CDS Virtual Agent USL" },
            { "COPILOT_STUDIO_IN_COPILOT_FOR_M365",     "Copilot Studio in Microsoft 365 Copilot" },
            { "FLOW_VIRTUAL_AGENT_BASE_MESSAGES",       "Flow Virtual Agent Base Messages" },
            { "FLOW_VIRTUAL_AGENT_USL",                 "Flow Virtual Agent USL" },
            { "VIRTUAL_AGENT_BASE_MESSAGES",            "Virtual Agent Base Messages" },
            { "VIRTUAL_AGENT_USL",                      "Virtual Agent USL" },
            { "CCIBOTS_PRIVPREV_VIRAL",                 "Copilot Studio (CCI Bots Viral)" },
        };

    // ----------------------------------------------------------------
    // Enriched checks (have context-aware deployment guidance)
    // ----------------------------------------------------------------
    private static readonly HashSet<string> EnrichedPlans = new(StringComparer.OrdinalIgnoreCase)
    {
        "POWER_VIRTUAL_AGENTS",
        "POWER_VIRTUAL_AGENTS_BASE",
        "POWER_VIRTUAL_AGENTS_O365_P3",
        "COPILOT_STUDIO_IN_COPILOT_FOR_M365",
        "CDS_VIRTUAL_AGENT_BASE_MESSAGES",
        "CCIBOTS_PRIVPREV_VIRAL",
    };

    // ----------------------------------------------------------------
    // Public entry point
    // ----------------------------------------------------------------
    public static List<RecommendationResult> Generate(
        Dictionary<string, string> planStatuses,
        PowerPlatformInsights? ppInsights)
    {
        var all = new List<RecommendationResult>();

        // --- Enriched checks ---
        TryAdd(all, CheckPowerVirtualAgents(planStatuses, ppInsights));
        TryAdd(all, CheckPowerVirtualAgentsBase(planStatuses, ppInsights));
        TryAdd(all, CheckPowerVirtualAgentsO365P3(planStatuses, ppInsights));
        TryAdd(all, CheckCopilotStudioInM365(planStatuses, ppInsights));
        TryAdd(all, CheckCdsVirtualAgentBaseMessages(planStatuses, ppInsights));
        TryAdd(all, CheckCciBots(planStatuses, ppInsights));

        // --- License-only for remaining plans ---
        foreach (var (plan, status) in planStatuses)
        {
            if (!ServicePlanMapping.All.TryGetValue(plan, out var cat) ||
                cat != ServiceCategory.CopilotStudio)
                continue;

            if (EnrichedPlans.Contains(plan))
                continue;

            var friendly = FriendlyNames.TryGetValue(plan, out var fn) ? fn : plan;
            all.Add(LicenseOnly(friendly, status,
                "Copilot Studio Documentation",
                "https://learn.microsoft.com/microsoft-copilot-studio/"));
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

    // --- Power Virtual Agents (standalone / full) ---
    private static List<RecommendationResult>? CheckPowerVirtualAgents(
        Dictionary<string, string> plans, PowerPlatformInsights? ins)
    {
        const string plan = "POWER_VIRTUAL_AGENTS";
        const string feature = "Power Virtual Agents";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} (Copilot Studio) is active, enabling enterprise conversational agent development with generative AI, multi-channel deployment, and Power Platform integration.",
                "Copilot Studio Overview",
                "https://learn.microsoft.com/microsoft-copilot-studio/fundamentals-what-is-power-virtual-agents"));

            list.Add(BuildDeploymentGuidance(feature, ins));
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, preventing enterprise conversational agent development.",
                "Enable Power Virtual Agents (Copilot Studio) to build intelligent agents for employee support (IT helpdesk, HR FAQs, facilities) and customer-facing scenarios. Agents use generative AI to answer questions from SharePoint knowledge bases, trigger Power Automate workflows, and deploy across Teams, web, and email channels.",
                "Copilot Studio Overview",
                "https://learn.microsoft.com/microsoft-copilot-studio/fundamentals-what-is-power-virtual-agents"));
        }

        return list;
    }

    // --- Power Virtual Agents (Base) ---
    private static List<RecommendationResult>? CheckPowerVirtualAgentsBase(
        Dictionary<string, string> plans, PowerPlatformInsights? ins)
    {
        const string plan = "POWER_VIRTUAL_AGENTS_BASE";
        const string feature = "Power Virtual Agents (Base)";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, providing the foundational Copilot Studio platform for agent development.",
                "Copilot Studio Platform",
                "https://learn.microsoft.com/microsoft-copilot-studio/"));

            list.Add(BuildDeploymentGuidance(feature, ins));
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}.",
                "Enable the Power Virtual Agents Base plan to access the Copilot Studio development platform.",
                "Copilot Studio Platform",
                "https://learn.microsoft.com/microsoft-copilot-studio/"));
        }

        return list;
    }

    // --- Power Virtual Agents for Office 365 ---
    private static List<RecommendationResult>? CheckPowerVirtualAgentsO365P3(
        Dictionary<string, string> plans, PowerPlatformInsights? ins)
    {
        const string plan = "POWER_VIRTUAL_AGENTS_O365_P3";
        const string feature = "Power Virtual Agents for Office 365";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling M365-integrated chatbot creation for employee support scenarios.",
                "Create Support Bots with Copilot Studio",
                "https://learn.microsoft.com/microsoft-copilot-studio/fundamentals-what-is-power-virtual-agents"));

            // Deployment guidance with Teams context.
            var envProd = ins?.EnvironmentsProd ?? 0;
            var appsTeams = ins?.AppsTeamsIntegrated ?? 0;

            if (envProd > 0)
            {
                list.Add(RecommendationResult.Success(Svc, $"{feature} - Teams Agent Deployment",
                    $"{envProd} production environment(s) available. Deploy specialized M365 support agents directly in Teams: IT Helpdesk (password resets, software requests), HR Agent (PTO policies, benefits), Facilities (room booking). Measure deflection: % of questions answered without @mentioning human support.",
                    "Deploy Agents to Teams",
                    "https://learn.microsoft.com/microsoft-copilot-studio/publication-add-bot-to-microsoft-teams"));
            }
            else
            {
                list.Add(RecommendationResult.Warning(Svc, $"{feature} - Deployment Planning",
                    observation: "M365-integrated agent capabilities ready. Deploy agents to Teams for IT helpdesk, HR, and facilities support to deflect repetitive questions.",
                    recommendation: "Plan M365-integrated agent strategy: identify repetitive support requests (IT, HR, facilities), build specialized agents for each domain, deploy to Teams channels. Focus on deflecting simple questions to free support staff for complex issues.",
                    linkText: "Agent Deployment Options",
                    linkUrl: "https://learn.microsoft.com/microsoft-copilot-studio/publication-fundamentals-publish-channels"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, missing conversational AI capabilities for employee support.",
                "Enable Power Virtual Agents for Office 365 to create conversational agents that handle common employee requests (IT support, HR questions, facilities) before escalating to humans. These agents complement M365 Copilot by providing specialized, task-oriented assistance.",
                "Create Support Bots with Copilot Studio",
                "https://learn.microsoft.com/microsoft-copilot-studio/fundamentals-what-is-power-virtual-agents"));
        }

        return list;
    }

    // --- Copilot Studio in Microsoft 365 Copilot ---
    private static List<RecommendationResult>? CheckCopilotStudioInM365(
        Dictionary<string, string> plans, PowerPlatformInsights? ins)
    {
        const string plan = "COPILOT_STUDIO_IN_COPILOT_FOR_M365";
        const string feature = "Copilot Studio in Microsoft 365 Copilot";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling agent building directly within the M365 Copilot experience. Users can create and share declarative agents from Microsoft 365 Copilot Chat without leaving the M365 ecosystem.",
                "Copilot Studio in M365 Copilot",
                "https://learn.microsoft.com/microsoft-copilot-studio/microsoft-365-copilot-studio"));

            var flowCandidates = ins?.FlowsCopilotPluginCandidates ?? 0;
            if (flowCandidates > 0)
            {
                list.Add(RecommendationResult.Success(Svc, $"{feature} - Plugin Candidates",
                    $"{flowCandidates} Power Automate flow(s) with HTTP triggers are ready to be registered as plugins in Copilot Studio. Register these flows as declarative agent actions to extend M365 Copilot with custom business processes.",
                    "Register Plugins",
                    "https://learn.microsoft.com/microsoft-copilot-studio/copilot-plugins-overview"));
            }
            else if (ins?.FlowsAvailable == true)
            {
                list.Add(RecommendationResult.Warning(Svc, $"{feature} - Plugin Development",
                    observation: "No Power Automate flows with HTTP triggers found — no Copilot plugin candidates available.",
                    recommendation: "Add HTTP Request triggers to Power Automate flows to create Copilot plugin candidates. Once flows have HTTP triggers, register them in Copilot Studio as agent actions. This extends M365 Copilot with your custom business processes — users can trigger actions via natural language in Copilot Chat.",
                    linkText: "Build Copilot Plugins",
                    linkUrl: "https://learn.microsoft.com/microsoft-copilot-studio/copilot-plugins-overview"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, preventing agent creation within M365 Copilot.",
                "Enable Copilot Studio in M365 Copilot to allow users to create and share declarative agents directly from Copilot Chat. This is the primary extensibility path for M365 Copilot — enabling custom agents for specific business scenarios without leaving the Microsoft 365 ecosystem.",
                "Copilot Studio in M365 Copilot",
                "https://learn.microsoft.com/microsoft-copilot-studio/microsoft-365-copilot-studio"));
        }

        return list;
    }

    // --- CDS Virtual Agent Base Messages ---
    private static List<RecommendationResult>? CheckCdsVirtualAgentBaseMessages(
        Dictionary<string, string> plans, PowerPlatformInsights? ins)
    {
        const string plan = "CDS_VIRTUAL_AGENT_BASE_MESSAGES";
        const string feature = "CDS Virtual Agent Base Messages";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, adding Dataverse-connected message capacity for entity-driven agent conversations.",
                "Dataverse Message Capacity",
                "https://learn.microsoft.com/microsoft-copilot-studio/requirements-quotas"));

            var envTotal = ins?.EnvironmentsTotal ?? 0;
            var modelApps = ins?.AppsModelDriven ?? 0;

            if (envTotal > 0 || modelApps > 0)
            {
                var context = new List<string>();
                if (envTotal > 0) context.Add($"{envTotal} environment(s)");
                if (modelApps > 0) context.Add($"{modelApps} model-driven app(s) (Dataverse entity access)");

                list.Add(RecommendationResult.Success(Svc, $"{feature} - Dataverse Message Planning",
                    $"Entity-driven agent capacity: {string.Join(", ", context)}. Prioritize capacity for entity-heavy agents (Dataverse queries consume more messages than simple FAQ). Monitor per-environment message usage.",
                    "Dataverse Messages",
                    "https://learn.microsoft.com/microsoft-copilot-studio/requirements-quotas"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}.",
                "Enable CDS Virtual Agent Base Messages to add message volume capacity for entity-driven agent conversations requiring Dataverse queries.",
                "Message Capacity",
                "https://learn.microsoft.com/microsoft-copilot-studio/requirements-quotas"));
        }

        return list;
    }

    // --- Copilot Studio (CCI Bots Viral) ---
    private static List<RecommendationResult>? CheckCciBots(
        Dictionary<string, string> plans, PowerPlatformInsights? ins)
    {
        const string plan = "CCIBOTS_PRIVPREV_VIRAL";
        const string feature = "Copilot Studio (CCI Bots Viral)";
        if (!plans.TryGetValue(plan, out var status)) return null;

        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, providing Copilot Studio access through viral/trial licensing for CCI bot scenarios.",
                "Copilot Studio Overview",
                "https://learn.microsoft.com/microsoft-copilot-studio/"));

            list.Add(RecommendationResult.Warning(Svc, $"{feature} - License Planning",
                observation: "Viral/trial Copilot Studio license detected. This may have usage and feature limitations compared to full commercial licensing.",
                recommendation: "Evaluate upgrading to full Copilot Studio licensing for production agent deployment. Trial licenses typically have message limits, restricted features, and lack SLA guarantees. Plan migration before agents reach production scale.",
                linkText: "Copilot Studio Licensing",
                linkUrl: "https://learn.microsoft.com/microsoft-copilot-studio/requirements-licensing"));
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}.",
                "Enable Copilot Studio CCI Bots plan to access conversational AI capabilities for agent scenarios.",
                "Copilot Studio Overview",
                "https://learn.microsoft.com/microsoft-copilot-studio/"));
        }

        return list;
    }

    // ================================================================
    // Helpers
    // ================================================================

    /// <summary>
    /// Build a deployment guidance recommendation that is context-aware
    /// based on available Power Platform insights (environments, flows, connections).
    /// </summary>
    private static RecommendationResult BuildDeploymentGuidance(
        string feature, PowerPlatformInsights? ins)
    {
        var envProd = ins?.EnvironmentsProd ?? 0;
        var envSandbox = ins?.EnvironmentsSandbox ?? 0;
        var flowsCloud = ins?.FlowsCloud ?? 0;
        var httpCandidates = ins?.FlowsCopilotPluginCandidates ?? 0;
        var hasSap = ins?.HasSap ?? false;
        var hasSalesforce = ins?.HasSalesforce ?? false;
        var hasServiceNow = ins?.HasServiceNow ?? false;

        var enterpriseSystems = new List<string>();
        if (hasSap) enterpriseSystems.Add("SAP");
        if (hasSalesforce) enterpriseSystems.Add("Salesforce");
        if (hasServiceNow) enterpriseSystems.Add("ServiceNow");

        if (envProd >= 1 && flowsCloud >= 5 && httpCandidates >= 2)
        {
            var obs = $"Strong deployment foundation: {envProd} production environment(s), {flowsCloud} cloud flows, {httpCandidates} Copilot plugin candidates.";
            if (enterpriseSystems.Count > 0)
                obs += $" Enterprise connectors active: {string.Join(", ", enterpriseSystems)}.";

            return RecommendationResult.Success(Svc, $"{feature} - Deployment Readiness",
                obs + " Scale Copilot Studio agents: add generative AI knowledge from SharePoint, extend with Power Automate actions, deploy to Teams.",
                "Scale Agents",
                "https://learn.microsoft.com/microsoft-copilot-studio/nlu-gpt-quickstart");
        }
        else if (envProd >= 1 || envSandbox >= 1)
        {
            var envDesc = envProd > 0 ? $"{envProd} production" : $"{envSandbox} sandbox";
            var obs = $"{envDesc} environment(s) available.";
            if (httpCandidates > 0) obs += $" {httpCandidates} flow(s) ready as Copilot actions.";

            return RecommendationResult.Warning(Svc, $"{feature} - Deployment Guidance",
                observation: obs,
                recommendation: "Build first agents: (1) IT Helpdesk — answers password reset and software request questions from SharePoint IT knowledge base, (2) HR Agent — PTO policies, benefits enrollment from SharePoint HR library, (3) Deploy to Teams channels for immediate employee access. Add Power Automate actions for ticket creation and approval workflows. Measure: conversations per week, deflection rate, user satisfaction.",
                linkText: "Build Your First Agent",
                linkUrl: "https://learn.microsoft.com/microsoft-copilot-studio/nlu-gpt-quickstart");
        }
        else
        {
            return RecommendationResult.Warning(Svc, $"{feature} - Getting Started",
                observation: "No Power Platform environments detected or access requires Power Platform Administrator role.",
                recommendation: "Get started with Copilot Studio: (1) Create a Power Platform environment in the admin center, (2) Build a pilot agent for a high-volume support topic, (3) Connect to SharePoint knowledge base for generative AI answers, (4) Deploy to Teams for employee testing. Copilot Studio agents reduce support ticket volume by deflecting repetitive questions.",
                linkText: "Get Started with Copilot Studio",
                linkUrl: "https://learn.microsoft.com/microsoft-copilot-studio/fundamentals-get-started");
        }
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
            $"Enable {feature} for full Copilot Studio capabilities.",
            linkText, linkUrl);
    }
}
