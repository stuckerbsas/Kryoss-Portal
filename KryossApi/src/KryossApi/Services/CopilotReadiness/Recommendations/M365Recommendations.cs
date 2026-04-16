using KryossApi.Services.CopilotReadiness.Pipelines;

namespace KryossApi.Services.CopilotReadiness.Recommendations;

/// <summary>
/// Generates M365 service-plan recommendations from <see cref="M365Insights"/> and
/// the flat SKU-plan dictionary built by <see cref="M365Pipeline"/>.
///
/// Covers 85+ service plans mapped to ServiceCategory.M365 in ServicePlanMapping.
/// ~10 plans are enriched with usage-activity metrics; the rest use the
/// license-only helper that avoids repeating the same pattern 75 times.
/// </summary>
public static class M365Recommendations
{
    private const string Svc = "m365";

    // ----------------------------------------------------------------
    // Friendly names for service plan codes
    // ----------------------------------------------------------------
    private static readonly Dictionary<string, string> FriendlyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Exchange
            { "EXCHANGE_S_ENTERPRISE",              "Exchange Online (Plan 2)" },
            { "EXCHANGE_S_STANDARD",                "Exchange Online (Plan 1)" },
            { "EXCHANGE_S_FOUNDATION",              "Exchange Online Foundation" },
            { "EXCHANGE_S_DESKLESS",                "Exchange Online Kiosk" },
            { "EXCHANGE_S_ARCHIVE_ADDON",           "Exchange Online Archiving" },
            { "EXCHANGE_ANALYTICS",                 "Exchange Analytics" },
            { "EXCHANGEDESKLESS",                   "Exchange Online Deskless" },
            { "EXCHANGEDESKLESS_GOV",               "Exchange Online Deskless (Gov)" },

            // Teams
            { "TEAMS1",                             "Microsoft Teams" },
            { "TEAMSPRO_CUST",                      "Teams Premium - Customer Service" },
            { "TEAMSPRO_MGMT",                      "Teams Premium - Management" },
            { "TEAMSPRO_PROTECTION",                "Teams Premium - Protection" },
            { "TEAMSPRO_VIRTUALAPPT",               "Teams Premium - Virtual Appointments" },
            { "TEAMSPRO_WEBINAR",                   "Teams Premium - Webinars" },
            { "TEAMS_PREMIUM_CUSTOMER",             "Teams Premium" },

            // SharePoint / OneDrive
            { "SHAREPOINTENTERPRISE",               "SharePoint (Plan 2)" },
            { "SHAREPOINTSTANDARD",                 "SharePoint (Plan 1)" },
            { "SHAREPOINTWAC",                      "Office Online" },
            { "ONEDRIVE_BASIC_P2",                  "OneDrive for Business (Plan 2)" },

            // Office Apps
            { "OFFICESUBSCRIPTION",                 "Microsoft 365 Apps for Enterprise" },
            { "OFFICEMOBILE_SUBSCRIPTION",          "Microsoft 365 Apps for Mobile" },
            { "EXCEL_PREMIUM",                      "Excel Advanced Features" },

            // Copilot
            { "M365_COPILOT_APPS",                  "Microsoft 365 Copilot Apps" },
            { "M365_COPILOT_BUSINESS_CHAT",         "Microsoft 365 Copilot - Business Chat" },
            { "M365_COPILOT_CONNECTORS",            "Microsoft 365 Copilot - Connectors" },
            { "M365_COPILOT_INTELLIGENT_SEARCH",    "Microsoft 365 Copilot - Intelligent Search" },
            { "M365_COPILOT_SHAREPOINT",            "Microsoft 365 Copilot for SharePoint" },
            { "M365_COPILOT_TEAMS",                 "Microsoft 365 Copilot for Teams" },
            { "GRAPH_CONNECTORS_COPILOT",           "Graph Connectors for Copilot" },
            { "GRAPH_CONNECTORS_SEARCH_INDEX",      "Graph Connectors Search Index" },

            // Productivity & Collaboration
            { "Bing_Chat_Enterprise",               "Microsoft Copilot (Bing Chat Enterprise)" },
            { "MICROSOFT_LOOP",                     "Microsoft Loop" },
            { "MICROSOFT_SEARCH",                   "Microsoft Search" },
            { "CLIPCHAMP",                          "Microsoft Clipchamp" },
            { "SWAY",                               "Microsoft Sway" },
            { "FORMS_PLAN_E1",                      "Microsoft Forms (E1)" },
            { "FORMS_PLAN_E5",                      "Microsoft Forms (E5)" },
            { "BPOS_S_TODO_3",                      "Microsoft To Do" },
            { "PROJECTWORKMANAGEMENT",              "Microsoft Planner" },
            { "PROJECTWORKMANAGEMENT_PLANNER",      "Microsoft Planner (Premium)" },
            { "PROJECT_O365_P1",                    "Project for Microsoft 365 (P1)" },
            { "PROJECT_O365_P3",                    "Project for Microsoft 365 (P3)" },
            { "DESKLESS",                           "Microsoft 365 F1 (Frontline)" },
            { "MICROSOFTBOOKINGS",                  "Microsoft Bookings" },
            { "MICROSOFT_ECDN",                     "Microsoft eCDN" },
            { "MICROSOFT_PLACES",                   "Microsoft Places" },

            // Viva & Analytics
            { "MICROSOFT_MYANALYTICS_FULL",         "Microsoft Viva Insights" },
            { "INSIGHTS_BY_MYANALYTICS",            "Viva Insights (Personal)" },
            { "MYANALYTICS_P1",                     "Viva Insights (P1)" },
            { "MYANALYTICS_P2",                     "Viva Insights (P2)" },
            { "MYANALYTICS_P3",                     "Viva Insights (P3)" },
            { "NUCLEUS",                            "Viva Connections" },
            { "PEOPLE_SKILLS_FOUNDATION",           "Viva Skills" },
            { "PLACES_CORE",                        "Microsoft Places Core" },
            { "PLACES_ENHANCED",                    "Microsoft Places Enhanced" },
            { "VIVA_GOALS",                         "Viva Goals" },
            { "VIVA_INSIGHTS_BACKEND",              "Viva Insights Backend" },
            { "VIVA_INSIGHTS_MYANALYTICS_FULL",     "Viva Insights (Full)" },
            { "VIVA_LEARNING_SEEDED",               "Viva Learning" },
            { "VIVAENGAGE_CORE",                    "Viva Engage" },
            { "WORKPLACE_ANALYTICS_INSIGHTS_BACKEND", "Workplace Analytics Backend" },
            { "WORKPLACE_ANALYTICS_INSIGHTS_USER",  "Workplace Analytics" },
            { "TEAMWORK_ANALYTICS",                 "Teamwork Analytics" },

            // Stream / Media
            { "STREAM_O365_E5",                     "Microsoft Stream (E5)" },
            { "WHITEBOARD_PLAN3",                   "Microsoft Whiteboard" },
            { "QUEUES_APP",                         "Microsoft Queues" },
            { "UNIVERSAL_PRINT_01",                 "Universal Print" },

            // Mesh & Immersive
            { "MESH",                               "Microsoft Mesh" },
            { "MESH_AVATARS_ADDITIONAL_FOR_TEAMS",  "Mesh Avatars (Additional)" },
            { "MESH_AVATARS_FOR_TEAMS",             "Mesh Avatars for Teams" },
            { "MESH_IMMERSIVE",                     "Mesh Immersive Spaces" },
            { "MESH_IMMERSIVE_FOR_TEAMS",           "Mesh Immersive Spaces for Teams" },

            // Voice / Telephony
            { "MCOEV",                              "Teams Phone System" },
            { "MCOEV_VIRTUALUSER",                  "Teams Phone (Virtual User)" },
            { "MCOIMP",                             "Teams Phone Standard" },
            { "MCOMEETADV",                         "Microsoft Teams Audio Conferencing" },
            { "MCOSTANDARD",                        "Skype for Business Online (Plan 2)" },
            { "MCOSTANDARD_GOV",                    "Skype for Business Online Gov" },
            { "MCO_VIRTUAL_APPT",                   "Virtual Appointments" },
            { "MCO_VIRTUAL_APPT_PREMIUM",           "Virtual Appointments Premium" },

            // Intune
            { "INTUNE_O365",                        "Intune for Microsoft 365" },
            { "INTUNE_SUITE",                       "Microsoft Intune Suite" },

            // Windows
            { "WIN10_PRO_ENT_SUB",                  "Windows 10/11 Enterprise" },
            { "WIN10_VDA_E3",                       "Windows 10/11 VDA (E3)" },
            { "WINDOWSUPDATEFORBUSINESS_DEPLOYMENTSERVICE", "Windows Autopatch (Deployment Service)" },
            { "WINDOWS_AUTOPATCH",                  "Windows Autopatch" },

            // Misc
            { "YAMMER_ENTERPRISE",                  "Viva Engage (Enterprise)" },
            { "KAIZALA_STANDALONE",                 "Microsoft Kaizala" },
            { "M365_LIGHTHOUSE_CUSTOMER_PLAN1",     "Microsoft 365 Lighthouse" },
        };

    // ----------------------------------------------------------------
    // Links for enriched and important plans
    // ----------------------------------------------------------------
    private static readonly Dictionary<string, (string text, string url)> PlanLinks =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "EXCHANGE_S_ENTERPRISE",      ("Intelligent Email with Copilot", "https://learn.microsoft.com/exchange/exchange-online") },
            { "EXCHANGE_S_STANDARD",        ("Exchange Online Documentation", "https://learn.microsoft.com/exchange/exchange-online") },
            { "TEAMS1",                     ("Teams as the Hub for AI Collaboration", "https://learn.microsoft.com/microsoftteams/teams-overview") },
            { "SHAREPOINTENTERPRISE",       ("SharePoint as Copilot Knowledge Base", "https://learn.microsoft.com/sharepoint/") },
            { "SHAREPOINTSTANDARD",         ("SharePoint Documentation", "https://learn.microsoft.com/sharepoint/") },
            { "ONEDRIVE_BASIC_P2",          ("OneDrive as Copilot Data Source", "https://learn.microsoft.com/onedrive/onedrive") },
            { "OFFICESUBSCRIPTION",         ("Copilot in Office Applications", "https://learn.microsoft.com/deployoffice/about-microsoft-365-apps") },
            { "M365_COPILOT_BUSINESS_CHAT", ("Business Chat for Enterprise Search", "https://learn.microsoft.com/microsoft-365-copilot/microsoft-365-copilot-chat") },
            { "M365_COPILOT_APPS",          ("Copilot Apps Documentation", "https://learn.microsoft.com/microsoft-365-copilot/") },
            { "M365_COPILOT_SHAREPOINT",    ("Copilot for SharePoint", "https://learn.microsoft.com/sharepoint/") },
            { "M365_COPILOT_TEAMS",         ("Copilot for Teams", "https://learn.microsoft.com/microsoftteams/teams-overview") },
            { "M365_COPILOT_CONNECTORS",    ("Copilot Connectors", "https://learn.microsoft.com/microsoft-365-copilot/extensibility/") },
            { "M365_COPILOT_INTELLIGENT_SEARCH", ("Copilot Intelligent Search", "https://learn.microsoft.com/microsoft-search/") },
            { "GRAPH_CONNECTORS_COPILOT",   ("Graph Connectors Documentation", "https://learn.microsoft.com/graph/connecting-external-content-connectors-overview") },
            { "GRAPH_CONNECTORS_SEARCH_INDEX", ("Graph Connectors Search Index", "https://learn.microsoft.com/graph/connecting-external-content-connectors-overview") },
            { "MICROSOFT_SEARCH",           ("Microsoft Search Documentation", "https://learn.microsoft.com/microsoft-search/") },
            { "MICROSOFT_LOOP",             ("Microsoft Loop Documentation", "https://learn.microsoft.com/microsoft-365/loop/") },
            { "VIVA_LEARNING_SEEDED",       ("Viva Learning Documentation", "https://learn.microsoft.com/viva/learning/") },
            { "VIVAENGAGE_CORE",            ("Viva Engage Documentation", "https://learn.microsoft.com/viva/engage/") },
            { "VIVA_GOALS",                 ("Viva Goals Documentation", "https://learn.microsoft.com/viva/goals/") },
            { "NUCLEUS",                    ("Viva Connections", "https://learn.microsoft.com/viva/connections/") },
            { "MCOEV",                      ("Teams Phone System", "https://learn.microsoft.com/microsoftteams/what-is-phone-system-in-office-365") },
            { "MCOMEETADV",                 ("Teams Audio Conferencing", "https://learn.microsoft.com/microsoftteams/audio-conferencing-in-office-365") },
            { "INTUNE_O365",                ("Intune Documentation", "https://learn.microsoft.com/mem/intune/") },
            { "INTUNE_SUITE",               ("Intune Suite Documentation", "https://learn.microsoft.com/mem/intune/") },
            { "WIN10_PRO_ENT_SUB",          ("Windows Enterprise", "https://learn.microsoft.com/windows/") },
            { "WINDOWS_AUTOPATCH",          ("Windows Autopatch", "https://learn.microsoft.com/windows/deployment/windows-autopatch/") },
        };

    // ----------------------------------------------------------------
    // Public entry point
    // ----------------------------------------------------------------

    public static List<RecommendationResult> Generate(
        M365Insights ins,
        Dictionary<string, string> skuPlans)
    {
        var all = new List<RecommendationResult>();

        // --- Enriched checks (10) ---
        all.AddRange(CheckExchangeEnterprise(ins, skuPlans));
        all.AddRange(CheckExchangeStandard(ins, skuPlans));
        all.AddRange(CheckTeams(ins, skuPlans));
        all.AddRange(CheckSharePointEnterprise(ins, skuPlans));
        all.AddRange(CheckSharePointStandard(ins, skuPlans));
        all.AddRange(CheckOneDrive(ins, skuPlans));
        all.AddRange(CheckOfficeSubscription(ins, skuPlans));
        all.AddRange(CheckCopilotBusinessChat(ins, skuPlans));
        all.AddRange(CheckCopilotApps(ins, skuPlans));
        all.AddRange(CheckGraphConnectors(ins, skuPlans));

        // --- License-only checks (75+) ---
        // Track enriched plans already emitted so we do not double-emit
        var enriched = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EXCHANGE_S_ENTERPRISE", "EXCHANGE_S_STANDARD", "TEAMS1",
            "SHAREPOINTENTERPRISE", "SHAREPOINTSTANDARD", "ONEDRIVE_BASIC_P2",
            "OFFICESUBSCRIPTION", "M365_COPILOT_BUSINESS_CHAT",
            "M365_COPILOT_APPS", "GRAPH_CONNECTORS_COPILOT",
        };

        foreach (var (plan, status) in skuPlans)
        {
            // Only emit M365-category plans
            if (!ServicePlanMapping.All.TryGetValue(plan, out var cat) ||
                cat != ServiceCategory.M365)
                continue;

            if (enriched.Contains(plan))
                continue; // already handled above

            var friendly = FriendlyNames.TryGetValue(plan, out var fn) ? fn : plan;
            var (linkText, linkUrl) = PlanLinks.TryGetValue(plan, out var lnk)
                ? lnk : DefaultLink(plan);

            all.Add(LicenseOnly(friendly, status, linkText, linkUrl));
        }

        return all;
    }

    // ================================================================
    //  Enriched checks
    // ================================================================

    // --- Exchange Online (Plan 2) ---
    private static List<RecommendationResult> CheckExchangeEnterprise(
        M365Insights ins, Dictionary<string, string> plans)
    {
        const string plan = "EXCHANGE_S_ENTERPRISE";
        const string feature = "Exchange Online (Plan 2)";
        const string linkText = "Intelligent Email with Copilot";
        const string linkUrl = "https://learn.microsoft.com/exchange/exchange-online";

        if (!plans.TryGetValue(plan, out var status)) return [];
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling Copilot to assist with email management and calendar intelligence.",
                linkText, linkUrl));

            if (ins.EmailReportAvailable)
            {
                list.Add(RecommendationResult.Success(Svc, $"{feature} - Activity Baseline",
                    $"Email activity (30-day): {ins.EmailActiveUsers} active users, avg {ins.EmailSendAvg:F1} sent / {ins.EmailReceiveAvg:F1} received per user. Baseline established for measuring Copilot email impact.",
                    "Exchange Activity Reports",
                    "https://learn.microsoft.com/microsoft-365/admin/activity-reports/email-activity"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, limiting Copilot email and scheduling capabilities.",
                $"Enable {feature} to let Copilot draft professional responses, summarize email threads, suggest meeting times, and extract action items from conversations.",
                linkText, linkUrl));
        }

        return list;
    }

    // --- Exchange Online (Plan 1) ---
    private static List<RecommendationResult> CheckExchangeStandard(
        M365Insights ins, Dictionary<string, string> plans)
    {
        const string plan = "EXCHANGE_S_STANDARD";
        const string feature = "Exchange Online (Plan 1)";
        const string linkText = "Exchange Online Documentation";
        const string linkUrl = "https://learn.microsoft.com/exchange/exchange-online";

        if (!plans.TryGetValue(plan, out var status)) return [];
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, providing email services accessible to Copilot.",
                linkText, linkUrl));

            if (ins.EmailReportAvailable)
            {
                list.Add(RecommendationResult.Success(Svc, $"{feature} - Activity Baseline",
                    $"Email activity (30-day): {ins.EmailActiveUsers} active users, avg {ins.EmailSendAvg:F1} sent per user.",
                    "Exchange Activity Reports",
                    "https://learn.microsoft.com/microsoft-365/admin/activity-reports/email-activity"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, limiting Copilot email capabilities.",
                $"Enable {feature} to provide email services for Copilot assistance.",
                linkText, linkUrl));
        }

        return list;
    }

    // --- Microsoft Teams ---
    private static List<RecommendationResult> CheckTeams(
        M365Insights ins, Dictionary<string, string> plans)
    {
        const string plan = "TEAMS1";
        const string feature = "Microsoft Teams";
        const string linkText = "Teams as the Hub for AI Collaboration";
        const string linkUrl = "https://learn.microsoft.com/microsoftteams/teams-overview";

        if (!plans.TryGetValue(plan, out var status)) return [];
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, providing the collaboration platform for Copilot and agents.",
                linkText, linkUrl));

            if (ins.TeamsReportAvailable)
            {
                string obs;
                string? rec = null;
                string statusSuffix;

                if (ins.TeamsActiveUsers > 100 && ins.TeamsMeetingAvg >= 5)
                {
                    obs = $"Strong Teams engagement: {ins.TeamsActiveUsers} active users averaging {ins.TeamsMeetingAvg:F1} meetings/user, {ins.TeamsChatAvg:F1} messages/user. High collaboration activity is ideal for Copilot adoption.";
                    statusSuffix = "success";
                }
                else if (ins.TeamsActiveUsers > 20)
                {
                    obs = $"Moderate Teams usage: {ins.TeamsActiveUsers} active users, {ins.TeamsMeetingAvg:F1} avg meetings/user. Collaboration is happening but could expand.";
                    rec = "Increase Teams adoption before Copilot rollout. Higher meeting and chat activity maximizes Copilot meeting intelligence (summaries, action items, Q&A) at scale.";
                    statusSuffix = "warning";
                }
                else
                {
                    obs = $"Low Teams adoption: only {ins.TeamsActiveUsers} active users. Limited collaboration reduces Copilot meeting intelligence value.";
                    rec = "Address low Teams adoption before deploying Copilot. Run an adoption campaign, migrate from legacy tools, enable meeting transcripts, and train users on chat vs email.";
                    statusSuffix = "warning";
                }

                if (statusSuffix == "success")
                    list.Add(RecommendationResult.Success(Svc, $"{feature} - Collaboration Activity",
                        obs, "Teams Analytics",
                        "https://learn.microsoft.com/microsoftteams/teams-analytics-and-reports/teams-reporting-reference"));
                else
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - Collaboration Activity",
                        obs, rec,
                        "Teams Adoption Guide",
                        "https://adoption.microsoft.com/microsoft-teams/"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, preventing access to the primary Copilot collaboration interface.",
                $"Enable {feature} — it is where Copilot generates meeting summaries, where users interact with custom agents, and where context-aware AI assistance happens. Essential infrastructure for agent adoption.",
                linkText, linkUrl));
        }

        return list;
    }

    // --- SharePoint (Plan 2) ---
    private static List<RecommendationResult> CheckSharePointEnterprise(
        M365Insights ins, Dictionary<string, string> plans)
    {
        const string plan = "SHAREPOINTENTERPRISE";
        const string feature = "SharePoint (Plan 2)";
        const string linkText = "SharePoint as Copilot Knowledge Base";
        const string linkUrl = "https://learn.microsoft.com/sharepoint/";

        if (!plans.TryGetValue(plan, out var status)) return [];
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, providing enterprise content management for Copilot.",
                linkText, linkUrl));

            if (ins.SharePointReportAvailable)
            {
                string obs;
                string? rec = null;
                bool isWarning;

                if (ins.SharePointActivityRate >= 50 && ins.SharePointTotalFiles > 1000)
                {
                    obs = $"Strong SharePoint engagement: {ins.SharePointActiveSites} active sites with {ins.SharePointTotalFiles:N0} files ({ins.SharePointActivityRate:F1}% activity rate). Rich content foundation ready for Copilot.";
                    isWarning = false;
                }
                else if (ins.SharePointActivityRate >= 25)
                {
                    obs = $"Moderate SharePoint activity: {ins.SharePointActiveSites} active sites with {ins.SharePointTotalFiles:N0} files ({ins.SharePointActivityRate:F1}% activity rate). Content available but engagement could be higher.";
                    rec = "Increase SharePoint adoption to maximize Copilot effectiveness. Encourage teams to use SharePoint for document collaboration, reduce email attachments in favor of SharePoint links, and ensure content is regularly updated.";
                    isWarning = false;
                }
                else
                {
                    obs = $"Low SharePoint activity: only {ins.SharePointActivityRate:F1}% activity rate across {ins.SharePointActiveSites} sites with {ins.SharePointTotalFiles:N0} files. Limited engagement reduces Copilot value.";
                    rec = "Address low SharePoint adoption to unlock Copilot potential. Audit content freshness, run adoption campaigns, provide training, and migrate active file shares to SharePoint.";
                    isWarning = true;
                }

                if (isWarning)
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - Usage Activity", obs, rec,
                        "SharePoint Adoption", "https://adoption.microsoft.com/sharepoint/"));
                else if (rec is not null)
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - Usage Activity", obs, rec,
                        "Drive SharePoint Adoption", "https://adoption.microsoft.com/sharepoint/"));
                else
                    list.Add(RecommendationResult.Success(Svc, $"{feature} - Usage Activity", obs,
                        "SharePoint Activity Reports",
                        "https://learn.microsoft.com/microsoft-365/admin/activity-reports/sharepoint-site-usage"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, limiting Copilot access to organizational content.",
                $"Enable {feature} to provide the content foundation for M365 Copilot. SharePoint stores the organizational knowledge that Copilot retrieves and reasons over — policies, procedures, project documentation, and institutional knowledge.",
                linkText, linkUrl));
        }

        return list;
    }

    // --- SharePoint (Plan 1) ---
    private static List<RecommendationResult> CheckSharePointStandard(
        M365Insights ins, Dictionary<string, string> plans)
    {
        const string plan = "SHAREPOINTSTANDARD";
        const string feature = "SharePoint (Plan 1)";
        const string linkText = "SharePoint Documentation";
        const string linkUrl = "https://learn.microsoft.com/sharepoint/";

        if (!plans.TryGetValue(plan, out var status)) return [];
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, providing collaborative content management for Copilot.",
                linkText, linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, limiting Copilot content access.",
                $"Enable {feature} to ensure Copilot can access team sites and document libraries.",
                linkText, linkUrl));
        }

        return list;
    }

    // --- OneDrive for Business (Plan 2) ---
    private static List<RecommendationResult> CheckOneDrive(
        M365Insights ins, Dictionary<string, string> plans)
    {
        const string plan = "ONEDRIVE_BASIC_P2";
        const string feature = "OneDrive for Business (Plan 2)";
        const string linkText = "OneDrive as Copilot Data Source";
        const string linkUrl = "https://learn.microsoft.com/onedrive/onedrive";

        if (!plans.TryGetValue(plan, out var status)) return [];
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling Copilot to access personal file storage.",
                linkText, linkUrl));

            if (ins.OneDriveReportAvailable)
            {
                if (ins.OneDriveAdoptionRate >= 70)
                {
                    list.Add(RecommendationResult.Success(Svc, $"{feature} - Adoption",
                        $"Strong OneDrive adoption: {ins.OneDriveAdoptionRate:F1}% of {ins.OneDriveTotalAccounts} accounts active, {ins.OneDriveTotalFilesGB:F1} GB stored. Personal files accessible to Copilot.",
                        "OneDrive Reports",
                        "https://learn.microsoft.com/microsoft-365/admin/activity-reports/onedrive-for-business-usage"));
                }
                else if (ins.OneDriveAdoptionRate >= 30)
                {
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - Adoption",
                        $"Moderate OneDrive adoption: {ins.OneDriveAdoptionRate:F1}% active ({ins.OneDriveActiveAccounts}/{ins.OneDriveTotalAccounts} accounts). Some personal content accessible to Copilot.",
                        "Increase OneDrive adoption for better Copilot personal file access. Migrate My Documents and Desktop to OneDrive. More files in OneDrive means better Copilot context.",
                        "OneDrive Adoption",
                        "https://adoption.microsoft.com/onedrive/"));
                }
                else
                {
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - Adoption",
                        $"Low OneDrive adoption: only {ins.OneDriveAdoptionRate:F1}% active. Limited personal files for Copilot access.",
                        "Deploy OneDrive folder redirection and Known Folder Move to increase adoption. Low usage limits Copilot personal context.",
                        "OneDrive Deployment",
                        "https://learn.microsoft.com/onedrive/redirect-known-folders"));
                }
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, restricting Copilot access to personal documents.",
                $"Enable {feature} to ensure Copilot can find personal drafts, templates, and working documents. Plan 2 provides unlimited storage and version history.",
                linkText, linkUrl));
        }

        return list;
    }

    // --- Microsoft 365 Apps for Enterprise ---
    private static List<RecommendationResult> CheckOfficeSubscription(
        M365Insights ins, Dictionary<string, string> plans)
    {
        const string plan = "OFFICESUBSCRIPTION";
        const string feature = "Microsoft 365 Apps for Enterprise";
        const string linkText = "Copilot in Office Applications";
        const string linkUrl = "https://learn.microsoft.com/deployoffice/about-microsoft-365-apps";

        if (!plans.TryGetValue(plan, out var status)) return [];
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, providing Word, Excel, PowerPoint, and Outlook with Copilot integration.",
                linkText, linkUrl));

            if (ins.OfficeActivationsReportAvailable)
            {
                string obs;
                string? rec = null;
                bool warn;

                if (ins.OfficeDesktopAdoptionRate >= 70)
                {
                    obs = $"Strong desktop Office adoption: {ins.OfficeDesktopAdoptionRate:F1}% of {ins.OfficeTotalUsersWithActivations} users have desktop activations (Windows: {ins.OfficeWindowsActivations}, Mac: {ins.OfficeMacActivations}). Optimal for Copilot in-app experiences.";
                    warn = false;
                }
                else if (ins.OfficeDesktopAdoptionRate >= 40)
                {
                    obs = $"Mixed Office deployment: {ins.OfficeDesktopAdoptionRate:F1}% desktop adoption (Win: {ins.OfficeWindowsActivations}, Mac: {ins.OfficeMacActivations}), {ins.OfficeMobileActivations} mobile-only users.";
                    rec = "Encourage desktop Office adoption for full Copilot experience. Desktop apps provide richer AI integration than web or mobile. Promote installations, especially for knowledge workers and content creators.";
                    warn = false;
                }
                else
                {
                    obs = $"Low desktop Office adoption: only {ins.OfficeDesktopAdoptionRate:F1}% have desktop apps (Win: {ins.OfficeWindowsActivations}, Mac: {ins.OfficeMacActivations}), {ins.OfficeMobileActivations} mobile-only users. Limited Copilot capabilities.";
                    rec = "Deploy desktop Office apps to maximize Copilot value. Web and mobile have limited AI capabilities. Target 70%+ desktop adoption for an effective Copilot program.";
                    warn = true;
                }

                if (warn)
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - Desktop Adoption", obs, rec,
                        "Office Deployment Guide",
                        "https://learn.microsoft.com/deployoffice/deployment-guide-microsoft-365-apps"));
                else if (rec is not null)
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - Desktop Adoption", obs, rec,
                        "Deploy Office Desktop Apps",
                        "https://learn.microsoft.com/deployoffice/deployment-guide-microsoft-365-apps"));
                else
                    list.Add(RecommendationResult.Success(Svc, $"{feature} - Desktop Adoption", obs,
                        "Office Activations Report",
                        "https://learn.microsoft.com/microsoft-365/admin/activity-reports/microsoft-office-activations"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, preventing Copilot integration in productivity applications.",
                $"Enable {feature} to access the latest versions of Word, Excel, PowerPoint, Outlook, and OneNote with built-in Copilot capabilities. Without current Office apps, users cannot leverage Copilot in-app assistance.",
                linkText, linkUrl));
        }

        return list;
    }

    // --- M365 Copilot Business Chat ---
    private static List<RecommendationResult> CheckCopilotBusinessChat(
        M365Insights ins, Dictionary<string, string> plans)
    {
        const string plan = "M365_COPILOT_BUSINESS_CHAT";
        const string feature = "Microsoft 365 Copilot - Business Chat";
        const string linkText = "Business Chat for Enterprise Search";
        const string linkUrl = "https://learn.microsoft.com/microsoft-365-copilot/microsoft-365-copilot-chat";

        if (!plans.TryGetValue(plan, out var status)) return [];
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling cross-application AI conversations about organizational data.",
                linkText, linkUrl));

            // Enriched: knowledge-graph context
            if (ins.SharePointTotalFiles > 0 || ins.TeamsTotalMeetings > 0)
            {
                var obs = $"Business Chat deployment context: {ins.TotalUsers} users, {ins.SharePointTotalFiles:N0} files, {ins.TeamsTotalMeetings:N0} meetings. Business Chat synthesizes across emails, chats, documents, and meetings — the more content, the smarter the responses.";

                if (ins.SharePointTotalFiles >= 200 && ins.TeamsTotalMeetings >= 150)
                {
                    list.Add(RecommendationResult.Success(Svc, $"{feature} - Knowledge Graph",
                        $"Rich knowledge graph: {ins.SharePointTotalFiles:N0} files and {ins.TeamsTotalMeetings:N0} meetings create strong Business Chat value. Train users on cross-app queries: 'Summarize decisions from last month's meetings', 'Find all customer feedback about feature X'.",
                        "Business Chat Best Practices",
                        "https://learn.microsoft.com/microsoft-365-copilot/microsoft-365-copilot-chat"));
                }
                else if (ins.SharePointTotalFiles >= 50 || ins.TeamsTotalMeetings >= 50)
                {
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - Knowledge Graph",
                        obs,
                        "Pilot Business Chat with teams that have rich cross-app workflows — project managers, sales, executives. As content grows, Business Chat becomes more valuable for knowledge discovery.",
                        "Pilot Business Chat",
                        "https://learn.microsoft.com/microsoft-365-copilot/microsoft-365-copilot-adoption"));
                }
            }

            // Copilot adoption rate observation
            if (ins.TotalUsers > 0)
            {
                if (ins.CopilotAdoptionPct < 10)
                {
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - Adoption",
                        $"Copilot adoption: {ins.CopilotLicensesAssigned} of {ins.TotalUsers} users licensed ({ins.CopilotAdoptionPct:F1}%).",
                        "Start Copilot pilot with 10-20 power users from different departments. Focus on users who create lots of content, attend many meetings, or need to synthesize information from multiple sources.",
                        "Copilot Adoption Guide",
                        "https://learn.microsoft.com/microsoft-365-copilot/microsoft-365-copilot-adoption"));
                }
                else if (ins.CopilotAdoptionPct < 30)
                {
                    list.Add(RecommendationResult.Warning(Svc, $"{feature} - Adoption",
                        $"Copilot adoption at {ins.CopilotAdoptionPct:F1}% ({ins.CopilotLicensesAssigned}/{ins.TotalUsers} users). Pilot phase is validating value.",
                        "Expand Copilot deployment. Gather feedback, identify use cases with highest ROI, then scale to similar user profiles. Target 20-30% adoption within 6 months for meaningful organizational impact.",
                        "Copilot Rollout",
                        "https://learn.microsoft.com/microsoft-365-copilot/microsoft-365-copilot-adoption"));
                }
                else
                {
                    list.Add(RecommendationResult.Success(Svc, $"{feature} - Adoption",
                        $"Solid Copilot adoption: {ins.CopilotAdoptionPct:F1}% ({ins.CopilotLicensesAssigned}/{ins.TotalUsers} users).",
                        "Copilot Adoption Dashboard",
                        "https://learn.microsoft.com/microsoft-365-copilot/microsoft-365-copilot-adoption"));
                }
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, limiting unified AI-powered search and synthesis capabilities.",
                $"Enable {feature} to provide employees with a ChatGPT-like experience that works across all Microsoft 365 data — emails, chats, documents, meetings, and calendar. Business Chat is the central hub for AI-powered knowledge work.",
                linkText, linkUrl));
        }

        return list;
    }

    // --- M365 Copilot Apps ---
    private static List<RecommendationResult> CheckCopilotApps(
        M365Insights ins, Dictionary<string, string> plans)
    {
        const string plan = "M365_COPILOT_APPS";
        const string feature = "Microsoft 365 Copilot Apps";
        const string linkText = "Copilot Apps Documentation";
        const string linkUrl = "https://learn.microsoft.com/microsoft-365-copilot/";

        if (!plans.TryGetValue(plan, out var status)) return [];
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling Copilot integration across Word, Excel, PowerPoint, and Outlook.",
                linkText, linkUrl));
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, preventing Copilot from assisting within Office applications.",
                $"Enable {feature} to unlock in-app Copilot: document drafting in Word, formula generation in Excel, presentation creation in PowerPoint, and email assistance in Outlook.",
                linkText, linkUrl));
        }

        return list;
    }

    // --- Graph Connectors for Copilot ---
    private static List<RecommendationResult> CheckGraphConnectors(
        M365Insights ins, Dictionary<string, string> plans)
    {
        const string plan = "GRAPH_CONNECTORS_COPILOT";
        const string feature = "Graph Connectors for Copilot";
        const string linkText = "Graph Connectors Documentation";
        const string linkUrl = "https://learn.microsoft.com/graph/connecting-external-content-connectors-overview";

        if (!plans.TryGetValue(plan, out var status)) return [];
        var list = new List<RecommendationResult>();

        if (status == "Success")
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                $"{feature} is active, enabling Copilot to access external knowledge sources.",
                linkText, linkUrl));

            if (ins.GraphConnectorsAvailable)
            {
                if (ins.GraphConnectorsCount > 0)
                {
                    var names = string.Join(", ", ins.GraphConnectorNames.Take(3));
                    if (ins.GraphConnectorsCount > 3)
                        names += $" (and {ins.GraphConnectorsCount - 3} more)";

                    list.Add(RecommendationResult.Success(Svc, "Graph Connectors Deployment",
                        $"{ins.GraphConnectorsCount} Graph Connector(s) deployed and indexing external data: {names}.",
                        "Manage Connectors",
                        "https://admin.microsoft.com/Adminportal/Home#/MicrosoftSearch/connectors"));
                }
                else
                {
                    list.Add(RecommendationResult.ActionRequired(Svc, "Graph Connectors Deployment",
                        priority: "High",
                        observation: "ZERO Graph Connectors deployed — Copilot cannot access any external data sources.",
                        recommendation: "Deploy Graph Connectors to connect Copilot to external systems: ServiceNow, Salesforce, Jira, custom databases, and file shares. Without connectors, Copilot is limited to Microsoft 365 content only.",
                        linkText: "Deploy Connectors Now",
                        linkUrl: "https://learn.microsoft.com/graph/connecting-external-content-connectors-overview"));
                }
            }

            // Usage context based on SharePoint content volume
            if (ins.SharePointTotalSites > 50)
            {
                list.Add(RecommendationResult.Success(Svc, "Graph Connectors - Content Context",
                    $"Your tenant has {ins.SharePointTotalSites} SharePoint sites. This significant content base creates strong opportunities for Graph Connectors to integrate external data and enable unified Copilot answers across Microsoft 365 and third-party systems.",
                    "Explore Graph Connectors",
                    "https://learn.microsoft.com/microsoft-365-copilot/extensibility/overview-graph-connector"));
            }
            else if (ins.SharePointTotalSites >= 10)
            {
                list.Add(RecommendationResult.Warning(Svc, "Graph Connectors - Content Context",
                    $"Your tenant has {ins.SharePointTotalSites} SharePoint sites. Consider deploying Graph Connectors to extend Copilot beyond Microsoft 365 to ServiceNow, Salesforce, or custom databases.",
                    $"Evaluate Graph Connectors for key external systems. With {ins.SharePointTotalSites} SharePoint sites, adding external data sources will enable comprehensive Copilot answers spanning both Microsoft 365 and third-party platforms.",
                    "Explore Graph Connectors",
                    "https://learn.microsoft.com/microsoft-365-copilot/extensibility/overview-graph-connector"));
            }
        }
        else
        {
            list.Add(RecommendationResult.Disabled(Svc, feature,
                $"{feature} is {status}, limiting Copilot to only Microsoft 365 content.",
                $"Enable {feature} to connect Copilot to external data sources like ServiceNow, Salesforce, Jira, custom databases, and file shares. Without this, Copilot misses critical business data stored in third-party systems.",
                linkText, linkUrl));
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
            $"Enable {feature} for full Copilot integration.",
            linkText, linkUrl);
    }

    private static (string text, string url) DefaultLink(string plan)
    {
        return ("Microsoft 365 Documentation",
                "https://learn.microsoft.com/microsoft-365/");
    }
}
