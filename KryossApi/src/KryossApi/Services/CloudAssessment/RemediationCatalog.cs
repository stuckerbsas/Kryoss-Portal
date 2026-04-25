namespace KryossApi.Services.CloudAssessment;

public static class RemediationCatalog
{
    public record RemediationEntry(
        string Area,
        string Service,
        string Feature,
        string ServicePackage,
        decimal EstimatedHours,
        string RemediationSummary,
        string? LicenseRequired = null,
        decimal? MonthlyCostPerUser = null);

    public static readonly string[] PackageOrder =
    [
        "Identity Hardening",
        "Endpoint Management",
        "Data Protection",
        "Email Security",
        "Productivity Optimization",
        "Azure Infrastructure",
        "Power BI Governance"
    ];

    private static readonly Dictionary<string, RemediationEntry> _catalog = BuildCatalog();

    public static RemediationEntry? Get(string area, string service, string feature)
    {
        var key = $"{area}|{service}|{feature}";
        return _catalog.GetValueOrDefault(key);
    }

    public static IReadOnlyDictionary<string, RemediationEntry> All => _catalog;

    private static Dictionary<string, RemediationEntry> BuildCatalog()
    {
        var entries = new RemediationEntry[]
        {
            // ── IDENTITY (area=identity, service=entra) ───────────────
            E("identity", "entra", "Microsoft Entra ID P1",              "Identity Hardening", 2.0m,  "Activate P1 trial/license, configure baseline CA policies",                            "Entra ID P1", 6.00m),
            E("identity", "entra", "Microsoft Entra ID P2",              "Identity Hardening", 3.0m,  "Activate P2 license, enable Identity Protection risk policies",                         "Entra ID P2", 9.00m),
            E("identity", "entra", "Conditional Access",                 "Identity Hardening", 4.0m,  "Design and deploy CA policy set (MFA, compliant device, location, risk-based)"),
            E("identity", "entra", "Entra Identity Protection",          "Identity Hardening", 2.0m,  "Configure user/sign-in risk policies, remediation flows",                               "Entra ID P2"),
            E("identity", "entra", "Microsoft Intune (Plan A)",          "Endpoint Management", 3.0m, "Activate Intune, configure enrollment, deploy compliance policies",                     "Intune Plan 1", 8.00m),
            E("identity", "entra", "Microsoft Entra ID Governance",      "Identity Hardening", 3.0m,  "Configure access reviews, entitlement management, lifecycle workflows",                 "Entra ID Governance", 7.00m),
            E("identity", "entra", "Microsoft Entra Internet Access",    "Identity Hardening", 2.0m,  "Configure Global Secure Access for internet traffic",                                   "Entra Internet Access"),
            E("identity", "entra", "Microsoft Entra Private Access",     "Identity Hardening", 3.0m,  "Configure private network connectors and app segments",                                 "Entra Private Access"),
            E("identity", "entra", "Service Principal Credentials",      "Identity Hardening", 1.5m,  "Audit and rotate expiring/expired app credentials"),
            E("identity", "entra", "B2B Cross-Tenant Access",           "Identity Hardening", 1.0m,  "Review and restrict cross-tenant access settings"),
            E("identity", "entra", "PIM Activation Policies",           "Identity Hardening", 2.0m,  "Configure PIM roles, activation policies, access reviews",                              "Entra ID P2"),
            E("identity", "entra", "User Lifecycle Hygiene",            "Identity Hardening", 1.5m,  "Disable/remove stale accounts, configure automated offboarding"),
            E("identity", "entra", "Administrator MFA",                 "Identity Hardening", 1.0m,  "Enforce MFA on all admin accounts, register security keys"),
            E("identity", "entra", "OAuth Consent",                     "Identity Hardening", 1.0m,  "Restrict user consent, configure admin consent workflow"),
            E("identity", "entra", "Device Security Posture",           "Identity Hardening", 2.0m,  "Configure device compliance requirements in CA policies"),
            E("identity", "entra", "Microsoft Entra ID Multi-Factor Authentication", "Identity Hardening", 1.0m, "Enable per-user MFA or Security Defaults for all users"),

            // ── ENDPOINT (area=endpoint, service=intune) ──────────────
            E("endpoint", "intune", "Device Compliance Policies",        "Endpoint Management", 3.0m, "Create compliance policies per OS (Win/iOS/Android)"),
            E("endpoint", "intune", "Compliance Rate",                   "Endpoint Management", 2.0m, "Investigate non-compliant devices, push remediation"),
            E("endpoint", "intune", "BYOD App Protection",              "Endpoint Management", 2.0m, "Deploy MAM policies for unmanaged devices"),
            E("endpoint", "intune", "Windows Autopilot",                "Endpoint Management", 4.0m, "Configure Autopilot profiles and deployment"),
            E("endpoint", "intune", "Device Encryption",                "Endpoint Management", 1.5m, "Deploy BitLocker/FileVault policy via Intune"),
            E("endpoint", "intune", "Enrollment Restrictions",          "Endpoint Management", 1.0m, "Configure device type and limit restrictions"),
            E("endpoint", "intune", "Windows Compliance",               "Endpoint Management", 1.0m, "Review and fix Windows compliance violations"),
            E("endpoint", "intune", "iOS Compliance",                   "Endpoint Management", 1.0m, "Review and fix iOS compliance violations"),
            E("endpoint", "intune", "Android Compliance",               "Endpoint Management", 1.0m, "Review and fix Android compliance violations"),
            E("endpoint", "intune", "iOS App Protection",               "Endpoint Management", 1.5m, "Deploy iOS MAM policies"),
            E("endpoint", "intune", "Android App Protection",           "Endpoint Management", 1.5m, "Deploy Android MAM policies"),

            // ── ENDPOINT (area=endpoint, service=defender-endpoint) ───
            E("endpoint", "defender-endpoint", "Defender for Endpoint",       "Endpoint Management", 3.0m, "Onboard devices to MDE, configure security baseline",                "Defender for Endpoint P1/P2", 2.50m),
            E("endpoint", "defender-endpoint", "Exposure Score",              "Endpoint Management", 2.0m, "Remediate top exposure recommendations"),
            E("endpoint", "defender-endpoint", "Vulnerability Posture",       "Endpoint Management", 2.0m, "Patch critical vulnerabilities identified by TVM"),
            E("endpoint", "defender-endpoint", "High Vulnerabilities",        "Endpoint Management", 2.0m, "Prioritize and remediate high-severity CVEs"),
            E("endpoint", "defender-endpoint", "Software Inventory",          "Endpoint Management", 0.5m, "Review and validate software inventory"),
            E("endpoint", "defender-endpoint", "High-Risk Machines",          "Endpoint Management", 2.0m, "Investigate and remediate high-risk devices"),

            // ── DATA (area=data, service=purview) ─────────────────────
            E("data", "purview", "AIP/DLP Licensing",                   "Data Protection", 1.0m, "Activate AIP/Purview licensing",                                                       "Microsoft Purview", 5.00m),
            E("data", "purview", "Sensitivity Label Deployment",        "Data Protection", 4.0m, "Design label taxonomy, publish labels, train users"),
            E("data", "purview", "DLP Policy Posture",                  "Data Protection", 4.0m, "Design and deploy DLP policies for Exchange, SharePoint, Teams"),
            E("data", "purview", "eDiscovery",                          "Data Protection", 2.0m, "Configure eDiscovery cases and retention"),
            E("data", "purview", "Advanced Audit",                      "Data Protection", 1.0m, "Enable advanced audit logging",                                                         "M365 E5/A5"),
            E("data", "purview", "Retention Labels",                    "Data Protection", 3.0m, "Design and deploy retention label policies"),
            E("data", "purview", "Customer Lockbox",                    "Data Protection", 0.5m, "Enable Customer Lockbox feature"),
            E("data", "purview", "Information Barriers",                "Data Protection", 3.0m, "Configure information barrier policies and segments"),
            E("data", "purview", "Insider Risk Management",             "Data Protection", 4.0m, "Configure insider risk policies and alerts",                                            "M365 E5/A5"),

            // ── DATA (area=data, service=sharepoint) ──────────────────
            E("data", "sharepoint", "Sensitivity Label Coverage",       "Data Protection", 2.0m, "Apply sensitivity labels to unlabeled SharePoint content"),
            E("data", "sharepoint", "SharePoint Oversharing",           "Data Protection", 3.0m, "Review and restrict overshared sites, reduce org-wide links"),
            E("data", "sharepoint", "High-Risk SharePoint Sites",       "Data Protection", 2.0m, "Remediate high-risk sites (permissions, external access)"),
            E("data", "sharepoint", "External Guest Users",             "Data Protection", 2.0m, "Run access review, remove stale guests, restrict invite perms"),
            E("data", "sharepoint", "Unlabeled Sensitive Content",      "Data Protection", 2.0m, "Apply auto-labeling or manual labels to sensitive content"),

            // ── DATA (area=data, service=onedrive) ────────────────────
            E("data", "onedrive", "OneDrive Storage Hoarding",          "Data Protection", 1.0m, "Review large accounts, set storage quotas, migrate to SharePoint"),

            // ── PRODUCTIVITY ──────────────────────────────────────────
            E("productivity", "copilot",   "Microsoft 365 Copilot Adoption",       "Productivity Optimization", 2.0m, "Run adoption campaign, assign licenses to active users",     "Microsoft 365 Copilot", 30.00m),
            E("productivity", "exchange",  "Exchange Online Adoption",              "Productivity Optimization", 1.0m, "Train inactive users, verify mailbox provisioning"),
            E("productivity", "teams",     "Microsoft Teams Adoption",              "Productivity Optimization", 1.0m, "Run Teams adoption campaign"),
            E("productivity", "sharepoint","SharePoint Deployment",                 "Productivity Optimization", 2.0m, "Create team sites, migrate shared drives"),
            E("productivity", "onedrive",  "OneDrive for Business Adoption",        "Productivity Optimization", 1.5m, "Deploy Known Folder Move, run adoption campaign"),
            E("productivity", "office",    "Microsoft 365 Apps Desktop Adoption",   "Productivity Optimization", 2.0m, "Deploy Office via Intune/SCCM, track activation"),
            E("productivity", "licensing", "Wasted Licenses",                       "Productivity Optimization", 1.0m, "Reassign or revoke unused licenses"),
            E("productivity", "licensing", "Wasted Copilot Licenses",               "Productivity Optimization", 0.5m, "Reassign Copilot seats to active knowledge workers"),
            E("productivity", "identity",  "External Guest Ratio",                  "Productivity Optimization", 1.0m, "Run guest access review, restrict invite permissions"),
            E("productivity", "copilot",   "Graph Connectors for Copilot",          "Productivity Optimization", 4.0m, "Deploy connectors to ServiceNow, Jira, Salesforce"),

            // ── AZURE ─────────────────────────────────────────────────
            E("azure", "arm",            "Subscription Coverage",                   "Azure Infrastructure", 0.5m, "Connect remaining subscriptions to Kryoss"),
            E("azure", "arm",            "Resource Density",                        "Azure Infrastructure", 0.5m, "Review resource distribution, consolidate if needed"),
            E("azure", "arm",            "Regional Footprint",                      "Azure Infrastructure", 0.5m, "Document multi-region strategy and compliance implications"),
            E("azure", "arm",            "Public exposure posture",                 "Azure Infrastructure", 2.0m, "Audit public endpoints, restrict or add WAF/FrontDoor"),
            E("azure", "defender-cloud", "Defender for Cloud Activation",           "Azure Infrastructure", 2.0m, "Enable Defender plans per subscription",                         "Defender for Cloud", 15.00m),
            E("azure", "defender-cloud", "Defender Unhealthy Ratio",                "Azure Infrastructure", 3.0m, "Remediate unhealthy Defender recommendations"),
            E("azure", "defender-cloud", "Defender Assessment Posture",             "Azure Infrastructure", 2.0m, "Review and act on Defender assessments"),
            E("azure", "defender-cloud", "Secure Score",                            "Azure Infrastructure", 2.0m, "Implement top Secure Score recommendations"),
            E("azure", "defender-cloud", "Secure Score Healthy",                    "Azure Infrastructure", 1.0m, "Maintain Secure Score health over time"),
            E("azure", "storage",        "Public Blob Access",                      "Azure Infrastructure", 1.0m, "Disable anonymous blob access on all storage accounts"),
            E("azure", "storage",        "Secure Transfer Required",                "Azure Infrastructure", 0.5m, "Enforce HTTPS on all storage accounts"),
            E("azure", "storage",        "Blob Soft-Delete",                        "Azure Infrastructure", 0.5m, "Enable soft-delete for blob recovery"),
            E("azure", "keyvault",       "Key Vault Soft-Delete",                   "Azure Infrastructure", 0.5m, "Enable soft-delete on Key Vaults"),
            E("azure", "keyvault",       "Key Vault Purge Protection",              "Azure Infrastructure", 0.5m, "Enable purge protection on Key Vaults"),
            E("azure", "network",        "Public IP Sprawl",                        "Azure Infrastructure", 1.5m, "Audit and deallocate unused public IPs"),
            E("azure", "network",        "NSG Unrestricted Inbound",               "Azure Infrastructure", 2.0m, "Restrict any/any NSG inbound rules"),
            E("azure", "compute",        "VM OS Disk Encryption",                   "Azure Infrastructure", 1.5m, "Enable Azure Disk Encryption on VMs"),
            E("azure", "compute",        "VM Managed Identity",                     "Azure Infrastructure", 1.0m, "Assign managed identities, remove stored credentials"),
            E("azure", "policy",         "Policy Compliance",                       "Azure Infrastructure", 2.0m, "Remediate non-compliant Azure Policy assignments"),

            // ── POWER BI ──────────────────────────────────────────────
            E("powerbi", "powerbi", "admin-api-access",                 "Power BI Governance", 0.5m, "Grant Kryoss service admin API access"),
            E("powerbi", "powerbi", "orphaned-workspaces",              "Power BI Governance", 1.0m, "Assign owners to orphaned workspaces or archive"),
            E("powerbi", "powerbi", "personal-workspaces",              "Power BI Governance", 1.0m, "Migrate content from personal to shared workspaces"),
            E("powerbi", "powerbi", "external-workspace-users",         "Power BI Governance", 1.0m, "Audit and remove unnecessary external users"),
            E("powerbi", "powerbi", "no-premium-capacity",              "Power BI Governance", 0.5m, "Evaluate Premium capacity need (informational)"),
            E("powerbi", "powerbi", "capacity-overload",                "Power BI Governance", 1.0m, "Scale capacity or optimize heavy workloads"),
            E("powerbi", "powerbi", "capacity-usage",                   "Power BI Governance", 0.5m, "Monitor capacity utilization"),
            E("powerbi", "powerbi", "datasets-never-refreshed",         "Power BI Governance", 1.0m, "Configure scheduled refresh for stale datasets"),
            E("powerbi", "powerbi", "datasets-stale",                   "Power BI Governance", 1.0m, "Fix failed refreshes, update connection strings"),
            E("powerbi", "powerbi", "dataset-freshness",                "Power BI Governance", 0.5m, "Verify refresh schedules are current"),
            E("powerbi", "powerbi", "gateway-offline",                  "Power BI Governance", 1.5m, "Troubleshoot and restore offline gateways"),
            E("powerbi", "powerbi", "gateway-status",                   "Power BI Governance", 0.5m, "Monitor gateway health"),
            E("powerbi", "powerbi", "personal-gateways-only",           "Power BI Governance", 2.0m, "Deploy enterprise gateway, migrate from personal"),
            E("powerbi", "powerbi", "no-gateways",                      "Power BI Governance", 0.5m, "Evaluate gateway need (informational)"),
            E("powerbi", "powerbi", "external-sharing-volume",          "Power BI Governance", 1.0m, "Review external sharing policies and recipients"),
            E("powerbi", "powerbi", "external-sharing",                 "Power BI Governance", 0.5m, "Monitor external sharing compliance"),
            E("powerbi", "powerbi", "high-export-volume",               "Power BI Governance", 0.5m, "Review export activity (informational)"),
            E("powerbi", "powerbi", "high-delete-activity",             "Power BI Governance", 1.0m, "Investigate unusual delete activity"),

            // ── MAIL FLOW ─────────────────────────────────────────────
            E("mail_flow", "email",     "SPF",                          "Email Security", 0.5m, "Create/update SPF record in DNS"),
            E("mail_flow", "email",     "DKIM",                         "Email Security", 1.0m, "Configure DKIM signing in Exchange + publish DNS records"),
            E("mail_flow", "email",     "DMARC",                        "Email Security", 1.0m, "Publish DMARC policy, configure reporting"),
            E("mail_flow", "email",     "MTA-STS",                      "Email Security", 1.0m, "Configure MTA-STS policy and DNS TXT record"),
            E("mail_flow", "email",     "BIMI",                         "Email Security", 1.5m, "Create SVG logo, obtain VMC certificate, publish DNS"),
            E("mail_flow", "mail_flow", "External Mailbox Forwarding",  "Email Security", 0.5m, "Disable external forwarding via transport rule"),
            E("mail_flow", "mail_flow", "Stealth Forwarding Rules",     "Email Security", 1.0m, "Identify and remove hidden forwarding rules"),
            E("mail_flow", "mail_flow", "Shared Mailbox Sign-in",       "Email Security", 0.5m, "Block direct sign-in on shared mailboxes"),
        };

        var dict = new Dictionary<string, RemediationEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
            dict[$"{e.Area}|{e.Service}|{e.Feature}"] = e;
        return dict;
    }

    private static RemediationEntry E(string area, string service, string feature,
        string package, decimal hours, string summary,
        string? license = null, decimal? costPerUser = null) =>
        new(area, service, feature, package, hours, summary, license, costPerUser);
}
