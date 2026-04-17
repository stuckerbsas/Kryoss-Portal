namespace KryossApi.Services.CloudAssessment;

/// <summary>
/// Maps Microsoft 365 / Entra SKU part numbers (as returned by Graph
/// /subscribedSkus skuPartNumber) to human-readable display names.
/// Used by the ProductivityPipeline when persisting cloud_assessment_licenses
/// rows and by ProductivityRecommendations for license-only checks.
/// </summary>
public static class SkuFriendlyNames
{
    private static readonly Dictionary<string, string> _map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Microsoft 365 suites
            { "SPE_E5",                          "Microsoft 365 E5" },
            { "SPE_E3",                          "Microsoft 365 E3" },
            { "SPE_F1",                          "Microsoft 365 F1" },
            { "SPE_F3",                          "Microsoft 365 F3" },

            // Office 365 suites
            { "ENTERPRISEPREMIUM",               "Office 365 E5" },
            { "ENTERPRISEPACK",                  "Office 365 E3" },
            { "STANDARDPACK",                    "Office 365 E1" },
            { "DESKLESSPACK",                    "Office 365 F3" },

            // Microsoft 365 Business
            { "O365_BUSINESS_PREMIUM",           "Microsoft 365 Business Premium" },
            { "O365_BUSINESS_ESSENTIALS",        "Microsoft 365 Business Basic" },
            { "O365_BUSINESS",                   "Microsoft 365 Apps for Business" },
            { "SMB_BUSINESS_PREMIUM",            "Microsoft 365 Business Premium (SMB)" },
            { "SMB_BUSINESS_ESSENTIALS",         "Microsoft 365 Business Basic (SMB)" },
            { "SMB_BUSINESS",                    "Microsoft 365 Apps for Business (SMB)" },

            // Exchange Online
            { "EXCHANGESTANDARD",                "Exchange Online (Plan 1)" },
            { "EXCHANGEENTERPRISE",              "Exchange Online (Plan 2)" },
            { "EXCHANGEARCHIVE_ADDON",           "Exchange Online Archiving for Exchange Online" },
            { "EXCHANGEDESKLESS",                "Exchange Online Kiosk" },
            { "EXCHANGETELCO",                   "Exchange Online POP" },

            // SharePoint / OneDrive
            { "SHAREPOINTSTANDARD",              "SharePoint Online (Plan 1)" },
            { "SHAREPOINTENTERPRISE",            "SharePoint Online (Plan 2)" },
            { "WACONEDRIVESTANDARD",             "OneDrive for Business (Plan 1)" },
            { "WACONEDRIVEENTERPRISE",           "OneDrive for Business (Plan 2)" },

            // Teams and calling
            { "MCOSTANDARD",                     "Skype for Business Online (Plan 2)" },
            { "MCOEV",                           "Microsoft Teams Phone Standard" },
            { "MCOMEETADV",                      "Microsoft Teams Audio Conferencing" },
            { "MCOCAP",                          "Microsoft Teams Shared Devices" },
            { "MCOPSTN1",                        "Microsoft Teams Domestic Calling Plan" },
            { "MCOPSTN2",                        "Microsoft Teams Domestic and International Calling Plan" },
            { "TEAMS_ESSENTIALS_AAD",            "Microsoft Teams Essentials" },
            { "TEAMS_EXPLORATORY",               "Microsoft Teams Exploratory" },

            // Project
            { "PROJECTPROFESSIONAL",             "Project Plan 3" },
            { "PROJECTPREMIUM",                  "Project Plan 5" },
            { "PROJECT_P1",                      "Project Plan 1" },

            // Visio
            { "VISIOCLIENT",                     "Visio Plan 2" },
            { "VISIOONLINE_PLAN1",               "Visio Plan 1" },

            // Power Platform
            { "POWER_BI_PRO",                    "Power BI Pro" },
            { "POWER_BI_PREMIUM_PER_USER",       "Power BI Premium (per user)" },
            { "POWER_BI_STANDARD",               "Power BI (free)" },
            { "FLOW_FREE",                       "Power Automate (free)" },
            { "POWERAPPS_VIRAL",                 "Power Apps (developer)" },
            { "POWER_VIRTUAL_AGENTS_VIRAL",      "Power Virtual Agents" },
            { "COPILOT_STUDIO",                  "Copilot Studio" },

            // Intune
            { "INTUNE_A",                        "Intune" },
            { "INTUNE_A_D",                      "Intune Device" },

            // Entra ID / Azure AD
            { "AAD_PREMIUM",                     "Entra ID P1" },
            { "AAD_PREMIUM_P2",                  "Entra ID P2" },

            // Enterprise Mobility + Security
            { "EMS",                             "Enterprise Mobility + Security E3" },
            { "EMSPREMIUM",                      "Enterprise Mobility + Security E5" },

            // Defender / Security
            { "ATA",                             "Microsoft Defender for Identity" },
            { "DEFENDER_ENDPOINT_P1",            "Microsoft Defender for Endpoint Plan 1" },
            { "DEFENDER_ENDPOINT_P2",            "Microsoft Defender for Endpoint Plan 2" },
            { "WIN_DEF_ATP",                     "Microsoft Defender for Endpoint (legacy)" },
            { "SECURITY_E3",                     "Microsoft 365 Security E3" },
            { "SECURITY_E5",                     "Microsoft 365 Security E5" },
            { "COMPLIANCE_E3",                   "Microsoft 365 Compliance E3" },
            { "COMPLIANCE_E5",                   "Microsoft 365 Compliance E5" },

            // Windows
            { "WINDOWS_STORE",                   "Windows Store for Business" },
            { "WIN10_PRO_ENT_SUB",               "Windows 10/11 Enterprise E3" },
            { "WIN10_VDA_E5",                    "Windows 10/11 Enterprise E5" },

            // Copilot
            { "COPILOT_M365",                    "Microsoft 365 Copilot" },
            { "Microsoft_365_Copilot",           "Microsoft 365 Copilot" },

            // Power Platform / RPA (tenant-observed variants)
            { "PBI_PREMIUM_PER_USER",            "Power BI Premium (per user)" },
            { "POWERAUTOMATE_ATTENDED_RPA",      "Power Automate Attended RPA" },
            { "POWERAPPS_PER_USER",              "Power Apps (per user)" },
            { "POWERAPPS_PER_APP",               "Power Apps (per app)" },
            { "POWERAPPS_DEV",                   "Power Apps Developer" },

            // Information Protection / SharePoint add-ons
            { "RMSBASIC",                        "Azure Information Protection (Basic)" },
            { "SharePoint_advanced_management_plan_1", "SharePoint Advanced Management" },

            // Teams / M365 Business tenant-specific variants (non-canonical names
            // as surfaced by /subscribedSkus on some tenants — keep both spaced
            // and un-spaced forms for robustness).
            { "Microsoft_Teams_Enterprise_New",              "Microsoft Teams Enterprise" },
            { "Microsoft_Teams_Trials",                      "Microsoft Teams Trial" },
            { "MS_TEAMS_IW",                                 "Microsoft Teams Trial (IW)" },
            { "Microsoft_365_Business_Premium_(no_Teams)",   "Microsoft 365 Business Premium (no Teams)" },
            { "Microsoft_365_ Business_ Premium_(no Teams)", "Microsoft 365 Business Premium (no Teams)" },
            { "Microsoft_365_E3_(no_Teams)",                 "Microsoft 365 E3 (no Teams)" },
            { "Microsoft_365_E5_(no_Teams)",                 "Microsoft 365 E5 (no Teams)" },
            { "O365_w/o_Teams_Bundle_E3",                    "Office 365 E3 (no Teams)" },
            { "O365_w/o_Teams_Bundle_E5",                    "Office 365 E5 (no Teams)" },
        };

    public static readonly IReadOnlyDictionary<string, string> Map = _map;

    public static string Resolve(string skuPartNumber)
        => Map.TryGetValue(skuPartNumber, out var v) ? v : skuPartNumber;
}
