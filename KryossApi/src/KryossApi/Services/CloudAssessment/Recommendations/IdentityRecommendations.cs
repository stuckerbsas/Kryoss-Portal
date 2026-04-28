using KryossApi.Services.CloudAssessment.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;

namespace KryossApi.Services.CloudAssessment.Recommendations;

/// <summary>
/// Generates Cloud Assessment (CA-1) identity recommendations from a
/// pre-computed <see cref="IdentityInsights"/> bag.
///
/// Reuses <see cref="RecommendationResult"/> from CopilotReadiness so
/// downstream report rendering / pipeline result processing remains
/// shared. The 11 ported generators mirror
/// <c>EntraRecommendations</c> verbatim (same observation text,
/// recommendation text, link URLs and priority ladder), but emit the
/// service tag <c>"entra"</c> (lowercase) so Cloud Assessment findings
/// stay distinguishable from Copilot Readiness findings in the
/// aggregated finding stream.
///
/// In addition to the 11 ported generators, 7 new generators leverage
/// the broader MSP-grade fields added in <see cref="IdentityInsights"/>:
/// service principal credential hygiene, B2B trust posture, PIM audit
/// maturity, user lifecycle hygiene, admin MFA gap, OAuth consent
/// surface and device security posture (BitLocker / compliance gaps).
///
/// Total: 18 generator methods. A fully populated tenant produces well
/// over 20 findings across the 7 service-tagged feature buckets.
/// </summary>
public static class IdentityRecommendations
{
    private const string Svc = "entra";

    // ----------------------------------------------------------------
    //  Public entry point
    // ----------------------------------------------------------------

    public static List<RecommendationResult> Generate(IdentityInsights ins)
    {
        var all = new List<RecommendationResult>();

        // 11 ported Entra-plan generators (mirror EntraRecommendations).
        all.AddRange(GenerateAadPremiumP1(ins));
        all.AddRange(GenerateAadPremiumP2(ins));
        all.AddRange(GenerateConditionalAccess(ins));
        all.AddRange(GenerateIdentityProtection(ins));
        all.AddRange(GenerateIntune(ins));
        all.Add(GenerateMfaPremium(ins));
        all.AddRange(GenerateGovernance(ins));
        all.AddRange(GenerateInternetAccess(ins));
        all.Add(GenerateInternetAccessFrontline(ins));
        all.AddRange(GeneratePrivateAccess(ins));
        all.Add(GeneratePrivateAccessCa(ins));

        // 7 Cloud-Assessment-only generators.
        all.AddRange(GenerateServicePrincipalHygiene(ins));
        all.AddRange(GenerateB2bTrustPosture(ins));
        all.AddRange(GeneratePimAuditMaturity(ins));
        all.AddRange(GenerateUserLifecycleHygiene(ins));
        all.AddRange(GenerateAdminMfaCoverage(ins));
        all.AddRange(GenerateConsentSurface(ins));
        all.AddRange(GenerateDeviceSecurityPosture(ins));

        return all;
    }

    // ================================================================
    //  1. AAD_PREMIUM / AAD_PREMIUM_P1  (5 enriched observations)
    // ================================================================

    private static List<RecommendationResult> GenerateAadPremiumP1(IdentityInsights ins)
    {
        const string feature = "Microsoft Entra ID P1";
        var list = new List<RecommendationResult>();

        // --- Obs 1: CA policy coverage ---
        if (ins.CaPolicyTotal == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: "No Conditional Access policies configured, leaving Copilot access unprotected",
                recommendation: "Implement Conditional Access policies to protect Copilot access. Start with requiring MFA for all users, blocking legacy authentication, and enforcing device compliance. CA policies are essential for preventing unauthorized AI usage and protecting sensitive data accessed through Copilot.",
                linkText: "Configure Conditional Access",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/overview"));
        }
        else if (ins.CaPolicyTargetM365 == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: $"{ins.CaPolicyTotal} Conditional Access policy(ies) configured, but none specifically target Copilot applications",
                recommendation: "Create Copilot-specific Conditional Access policies targeting Microsoft 365 Copilot and Graph Connector applications. Apply stricter controls for AI access: require compliant devices, trusted locations, and MFA. Consider blocking Copilot access from unmanaged devices to prevent data exfiltration.",
                linkText: "Conditional Access for Copilot",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/concept-conditional-access-cloud-apps"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"{ins.CaPolicyTargetM365} Conditional Access policy(ies) protecting Copilot applications",
                linkText: "Conditional Access Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/plan-conditional-access"));
        }

        // --- Obs 2: MFA enrollment ---
        var mfaPct = ins.MfaRegistrationPct;
        if (mfaPct < 50)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"Only {ins.MfaRegistered} of {ins.TotalUsers} users ({mfaPct:F1}%) enrolled in MFA, exposing Copilot to credential theft",
                recommendation: "Enforce MFA registration for all users accessing Copilot. Use Conditional Access to require MFA for Microsoft 365 apps. Compromised accounts without MFA can access Copilot to exfiltrate organizational data through AI prompts. Aim for 100% MFA coverage.",
                linkText: "Configure MFA Requirements",
                linkUrl: "https://learn.microsoft.com/entra/identity/authentication/howto-mfa-getstarted"));
        }
        else if (mfaPct < 90)
        {
            var remaining = ins.TotalUsers - ins.MfaRegistered;
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: $"{ins.MfaRegistered} of {ins.TotalUsers} users ({mfaPct:F1}%) enrolled in MFA, approaching secure coverage for Copilot access",
                recommendation: $"Continue rolling out MFA to remaining {remaining} users. Use Conditional Access to require MFA for all Copilot and Microsoft 365 access. Target 100% MFA coverage to fully protect AI services from compromised credentials.",
                linkText: "MFA Deployment Guide",
                linkUrl: "https://learn.microsoft.com/entra/identity/authentication/howto-mfa-getstarted"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"{ins.MfaRegistered} of {ins.TotalUsers} users ({mfaPct:F1}%) enrolled in MFA, strongly protecting Copilot access",
                linkText: "MFA Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/identity/authentication/concept-mfa-howitworks"));
        }

        // --- Obs 3: Legacy authentication ---
        if (ins.LegacyAuthSignIns > 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{ins.LegacyAuthSignIns} legacy authentication sign-in(s) detected in the past 7 days, bypassing MFA and CA protections",
                recommendation: "Block legacy authentication protocols (IMAP, POP3, SMTP AUTH) using Conditional Access. Legacy auth bypasses MFA and cannot be protected by Conditional Access policies, creating a backdoor for attackers to access Copilot. Migrate apps to modern authentication (OAuth 2.0) and block legacy protocols tenant-wide.",
                linkText: "Block Legacy Authentication",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/block-legacy-authentication"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "No legacy authentication sign-ins detected, all access uses modern authentication with full security controls",
                linkText: "Modern Authentication Overview",
                linkUrl: "https://learn.microsoft.com/microsoft-365/enterprise/hybrid-modern-auth-overview"));
        }

        // --- Obs 4: Passwordless adoption ---
        var fido2 = ins.AuthMethodBreakdown.GetValueOrDefault("fido2");
        var hello = ins.AuthMethodBreakdown.GetValueOrDefault("windowsHello");
        var authenticator = ins.AuthMethodBreakdown.GetValueOrDefault("microsoftAuthenticator");
        var totalPl = fido2 + hello + authenticator;
        var plPct = ins.PasswordlessPct;

        var methodParts = new List<string>();
        if (fido2 > 0) methodParts.Add($"{fido2} FIDO2");
        if (hello > 0) methodParts.Add($"{hello} Windows Hello");
        if (authenticator > 0) methodParts.Add($"{authenticator} Authenticator");
        var methodText = methodParts.Count > 0 ? $" ({string.Join(", ", methodParts)})" : "";

        if (plPct < 10)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: $"Only {plPct:F1}% of users ({totalPl}{methodText}) use passwordless authentication, leaving Copilot vulnerable to password attacks",
                recommendation: "Deploy passwordless authentication (FIDO2 security keys, Windows Hello for Business, or Microsoft Authenticator app) to eliminate password-based attacks targeting Copilot access. Passwordless methods are phishing-resistant and significantly reduce credential theft risks. Use Conditional Access to require passwordless auth for high-value resources like Copilot. Start with pilot groups and expand.",
                linkText: "Deploy Passwordless Authentication",
                linkUrl: "https://learn.microsoft.com/entra/identity/authentication/concept-authentication-passwordless"));
        }
        else if (plPct < 50)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: $"{plPct:F1}% of users ({totalPl}{methodText}) use passwordless authentication, making progress toward phishing-resistant Copilot access",
                recommendation: "Continue passwordless rollout to reach 80%+ adoption. Focus on eliminating password-based authentication for Copilot users. Use Temporary Access Pass (TAP) to onboard users to Windows Hello or FIDO2. Consider requiring passwordless for executives and high-risk users accessing sensitive AI capabilities.",
                linkText: "Passwordless Deployment Guide",
                linkUrl: "https://learn.microsoft.com/entra/identity/authentication/howto-authentication-passwordless-deployment"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"{plPct:F1}% of users ({totalPl}{methodText}) use passwordless authentication, providing phishing-resistant protection for Copilot",
                linkText: "Passwordless Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/identity/authentication/concept-authentication-passwordless"));
        }

        // --- Obs 5: Group-based licensing ---
        if (ins.CopilotLicenseGroups == 0 && ins.GroupsWithLicenses == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: "No group-based licensing configured - Copilot licenses likely assigned manually to individual users",
                recommendation: "Implement group-based licensing to automate Copilot license assignment and improve governance. Create security groups for Copilot users and assign M365 licenses to groups. Use dynamic groups with attribute-based rules to automatically assign/remove licenses. This enables self-service access, improves license reclamation, and provides a clear governance model.",
                linkText: "Group-Based Licensing",
                linkUrl: "https://learn.microsoft.com/entra/identity/users/licensing-groups-assign"));
        }
        else if (ins.CopilotLicenseGroups == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: $"{ins.GroupsWithLicenses} group(s) use license assignment, but none configured for Copilot licenses",
                recommendation: $"Extend group-based licensing to Copilot license management. You have {ins.DynamicGroups} dynamic group(s) - leverage these for automated Copilot access. Create dedicated groups for Copilot users by department, role, or project for automatic license assignment and removal.",
                linkText: "Assign Copilot Licenses to Groups",
                linkUrl: "https://learn.microsoft.com/entra/identity/users/licensing-groups-assign"));
        }
        else if (ins.GroupsWithErrors > 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{ins.CopilotLicenseGroups} group(s) configured for Copilot licensing, but {ins.GroupsWithErrors} group(s) have license assignment errors",
                recommendation: $"Resolve license assignment errors in {ins.GroupsWithErrors} group(s) to ensure users receive Copilot access. Common errors: insufficient licenses, conflicting service plans, duplicate assignments, or missing usage location.",
                linkText: "Troubleshoot Group Licensing",
                linkUrl: "https://learn.microsoft.com/entra/identity/users/licensing-groups-resolve-problems"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"{ins.CopilotLicenseGroups} group(s) manage Copilot license assignment ({ins.DynamicGroups} dynamic), automating access governance",
                linkText: "Group-Based Licensing Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/identity/users/licensing-groups-assign"));
        }

        return list;
    }

    // ================================================================
    //  2. AAD_PREMIUM_P2  (9 enriched observations)
    // ================================================================

    private static List<RecommendationResult> GenerateAadPremiumP2(IdentityInsights ins)
    {
        const string feature = "Microsoft Entra ID P2";
        var list = new List<RecommendationResult>();

        // --- Obs 1: PIM ---
        if (ins.PermanentAssignmentsTotal > 0 && ins.EligibleAssignments == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{ins.PermanentAssignmentsTotal} admin(s) have permanent elevated privileges, increasing risk for Copilot administrative actions",
                recommendation: "Configure Privileged Identity Management (PIM) to require just-in-time activation for admin roles managing Copilot services. This reduces the attack surface for AI workload administration.",
                linkText: "Configure PIM for Copilot Admins",
                linkUrl: "https://learn.microsoft.com/entra/id-governance/privileged-identity-management/pim-configure"));
        }
        else if (ins.EligibleAssignments > 0)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"{ins.EligibleAssignments} eligible admin role(s) configured with PIM, enforcing just-in-time activation",
                linkText: "PIM Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/id-governance/privileged-identity-management/pim-configure"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "No permanent admin roles detected, all administrative access uses just-in-time activation",
                linkText: "PIM Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/id-governance/privileged-identity-management/pim-configure"));
        }

        // --- Obs 2: Access Reviews ---
        if (ins.AccessReviewTotal == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: "No active access reviews configured for user access governance",
                recommendation: "Implement periodic access reviews for users with Copilot licenses and admin roles. Regular reviews ensure only authorized users maintain access to AI assistants and prevent license waste.",
                linkText: "Configure Access Reviews",
                linkUrl: "https://learn.microsoft.com/entra/id-governance/access-reviews-overview"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"{ins.AccessReviewActive} active access review(s) monitoring user access to Copilot services",
                linkText: "Access Reviews Overview",
                linkUrl: "https://learn.microsoft.com/entra/id-governance/access-reviews-overview"));
        }

        // --- Obs 3: Identity Protection risk status ---
        var totalRisky = ins.RiskyUsersHigh + ins.RiskyUsersMedium;
        if (totalRisky > 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature,
                ins.RiskyUsersHigh > 0 ? "high" : "medium",
                observation: $"{totalRisky} risky user(s) detected ({ins.RiskyUsersHigh} high-risk, {ins.RiskyUsersMedium} medium-risk) who may have access to Copilot services",
                recommendation: "Review and remediate risky users immediately. Compromised accounts with Copilot access can lead to data exfiltration through AI prompts. Enable risk-based conditional access policies to block risky sign-ins automatically.",
                linkText: "Investigate Risky Users",
                linkUrl: "https://learn.microsoft.com/entra/id-protection/howto-identity-protection-investigate-risk"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "No risky users detected, Identity Protection is actively monitoring and protecting accounts",
                linkText: "Identity Protection Overview",
                linkUrl: "https://learn.microsoft.com/entra/id-protection/overview-identity-protection"));
        }

        // --- Obs 4: Guest user (B2B) access ---
        if (ins.GuestUsersWithLicenses > 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: $"{ins.GuestUsersWithLicenses} of {ins.GuestUsersTotal} guest users have M365 licenses, potentially accessing Copilot with sensitive organizational data",
                recommendation: $"Review {ins.GuestUsersWithLicenses} guest user(s) with M365 licenses to verify Copilot access is intentional and appropriate. Implement quarterly access reviews for guest users, restrict guest access to specific sites/teams, and use sensitivity labels to prevent Copilot from accessing highly confidential content shared with guests.",
                linkText: "Manage Guest Access",
                linkUrl: "https://learn.microsoft.com/entra/external-id/what-is-b2b"));
        }
        else if (ins.GuestUsersTotal > 10)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"{ins.GuestUsersTotal} guest users in tenant, none with M365 licenses - external collaboration isolated from Copilot access",
                linkText: "External Identities Overview",
                linkUrl: "https://learn.microsoft.com/entra/external-id/"));
        }

        // --- Obs 5: Guest invite setting ---
        var invite = ins.GuestInviteSetting;
        if (invite != "Unknown")
        {
            var isRestricted = invite.Contains("admin", StringComparison.OrdinalIgnoreCase)
                            || invite.Contains("limited", StringComparison.OrdinalIgnoreCase);
            if (!isRestricted)
            {
                list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                    observation: $"Guest user invitations are configured as '{invite}', allowing broad external access to Copilot-searchable content",
                    recommendation: "Restrict guest user invitations to admins and specific users only to control external access to content that Copilot can search. Unrestricted guest access risks Copilot exposing internal content to external users via AI summaries.",
                    linkText: "Configure External Collaboration",
                    linkUrl: "https://learn.microsoft.com/entra/external-id/external-collaboration-settings-configure"));
            }
            else
            {
                list.Add(RecommendationResult.Success(Svc, feature,
                    observation: $"Guest user invitations restricted to '{invite}', controlling external access to Copilot-searchable content",
                    linkText: "External Collaboration Settings",
                    linkUrl: "https://learn.microsoft.com/entra/external-id/external-collaboration-settings-configure"));
            }
        }

        // --- Obs 6: Cross-tenant access ---
        if (!ins.CrossTenantConfigured)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: "Cross-tenant access settings not configured, allowing unrestricted B2B collaboration with external organizations accessing Copilot content",
                recommendation: "Configure cross-tenant access policies to control B2B collaboration with specific partner organizations and restrict Copilot data exposure. Without cross-tenant controls, users from any external org can access your Copilot-searchable content via B2B.",
                linkText: "Cross-Tenant Access Settings",
                linkUrl: "https://learn.microsoft.com/entra/external-id/cross-tenant-access-overview"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "Cross-tenant access policies configured, governing external access to Copilot content",
                linkText: "Cross-Tenant Access Settings",
                linkUrl: "https://learn.microsoft.com/entra/external-id/cross-tenant-access-overview"));
        }

        // --- Obs 7: App consent settings ---
        if (ins.UserConsentAllowed && !ins.AdminConsentRequired)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: "User consent enabled for applications, allowing users to grant apps access to Copilot-generated content and M365 data without admin review",
                recommendation: "Disable user consent and require admin approval for all application permissions to prevent unauthorized apps from accessing Copilot data. Configure consent settings to 'Do not allow user consent' and enable admin consent workflow so IT can review app permissions before granting access.",
                linkText: "Configure User Consent",
                linkUrl: "https://learn.microsoft.com/entra/identity/enterprise-apps/configure-user-consent"));
        }
        else if (ins.AdminConsentRequired)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "Admin consent required for applications, protecting Copilot data from unauthorized app access",
                linkText: "Admin Consent Workflow",
                linkUrl: "https://learn.microsoft.com/entra/identity/enterprise-apps/configure-admin-consent-workflow"));
        }
        else
        {
            list.Add(RecommendationResult.Warning(Svc, feature,
                observation: "Application consent settings could not be verified - review tenant configuration to ensure proper app governance for Copilot data access",
                recommendation: "Review application consent policies in Entra ID to ensure user consent is appropriately restricted. Verify that users cannot consent to apps accessing organizational data without admin review.",
                linkText: "Configure User Consent",
                linkUrl: "https://learn.microsoft.com/entra/identity/enterprise-apps/configure-user-consent"));
        }

        // --- Obs 8: Risky application permissions ---
        if (ins.HighRiskApps > 0 || ins.UnverifiedPublishers > 0)
        {
            var riskParts = new List<string>();
            if (ins.HighRiskApps > 0) riskParts.Add($"{ins.HighRiskApps} with high-privilege permissions");
            if (ins.UnverifiedPublishers > 0) riskParts.Add($"{ins.UnverifiedPublishers} from unverified publishers");
            if (ins.AppsWithGraphAccess > 0) riskParts.Add($"{ins.AppsWithGraphAccess} accessing Graph API (Copilot data)");

            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"Risky applications detected: {string.Join(", ", riskParts)} - potential unauthorized access to Copilot-generated content",
                recommendation: $"Audit and restrict {ins.HighRiskApps + ins.UnverifiedPublishers} risky application(s) with access to M365 data and Copilot content. Review each app's permissions in Entra ID > Enterprise Applications > Permissions, revoke excessive permissions, and remove unnecessary apps. Implement app governance policies to detect risky permission grants automatically.",
                linkText: "App Governance",
                linkUrl: "https://learn.microsoft.com/defender-cloud-apps/app-governance-manage-app-governance"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "No high-risk applications detected, all apps accessing M365 data have appropriate permissions and verified publishers",
                linkText: "App Governance",
                linkUrl: "https://learn.microsoft.com/defender-cloud-apps/app-governance-manage-app-governance"));
        }

        // --- Obs 9: Access Reviews detailed (recurring, group scope) ---
        if (ins.AccessReviewTotal > 0)
        {
            if (ins.AccessReviewRecurring == 0)
            {
                list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                    observation: $"{ins.AccessReviewTotal} access review(s) configured, but none are recurring - Copilot access not continuously governed",
                    recommendation: $"Convert {ins.AccessReviewTotal} one-time access review(s) to recurring campaigns for continuous governance. Update review configurations to recur quarterly for group memberships, monthly for privileged roles, and semi-annually for guest access.",
                    linkText: "Create Recurring Reviews",
                    linkUrl: "https://learn.microsoft.com/entra/id-governance/create-access-review"));
            }
            else if (ins.AccessReviewGroupScope == 0)
            {
                list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                    observation: $"{ins.AccessReviewTotal} access review(s) active ({ins.AccessReviewRecurring} recurring), but no group membership reviews for Copilot license governance",
                    recommendation: "Add access reviews for groups that assign Copilot licenses to ensure only authorized users maintain access. Target groups with Copilot license assignments, assign managers as reviewers, and enable auto-removal of denied users.",
                    linkText: "Review Group Memberships",
                    linkUrl: "https://learn.microsoft.com/entra/id-governance/create-access-review"));
            }
            else
            {
                var parts = new List<string>();
                if (ins.AccessReviewGroupScope > 0) parts.Add($"{ins.AccessReviewGroupScope} group");
                if (ins.AccessReviewRoleScope > 0) parts.Add($"{ins.AccessReviewRoleScope} role");
                if (ins.AccessReviewGuestScope > 0) parts.Add($"{ins.AccessReviewGuestScope} guest");

                list.Add(RecommendationResult.Success(Svc, feature,
                    observation: $"{ins.AccessReviewActive} active access review(s) ({string.Join(", ", parts)}), {ins.AccessReviewRecurring} recurring - continuous governance for Copilot access",
                    linkText: "Access Review Best Practices",
                    linkUrl: "https://learn.microsoft.com/entra/id-governance/deploy-access-reviews"));
            }
        }

        return list;
    }

    // ================================================================
    //  3. AAD_PREMIUM_CONDITIONAL_ACCESS
    // ================================================================

    private static List<RecommendationResult> GenerateConditionalAccess(IdentityInsights ins)
    {
        const string feature = "Conditional Access";
        var list = new List<RecommendationResult>();

        // License-level observation
        list.Add(RecommendationResult.Success(Svc, feature,
            observation: "Conditional Access is available, enabling context-aware access policies for Copilot",
            linkText: "Policy-Based Copilot Access",
            linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/"));

        // Policy coverage
        if (ins.CaPolicyTotal == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: "No Conditional Access policies configured, leaving Copilot accessible from any device, location, or risk level",
                recommendation: "Create baseline Conditional Access policies for Microsoft 365 apps to protect Copilot: require MFA for all cloud apps, block legacy authentication, require compliant or hybrid-joined devices, and block access from high-risk locations. Start with report-only mode.",
                linkText: "CA Policies for Copilot",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/concept-conditional-access-cloud-apps"));
        }
        else if (ins.CaPolicyMfaRequired == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: $"{ins.CaPolicyEnabled} Conditional Access policy(ies) configured, but none require MFA for Microsoft 365 apps",
                recommendation: "Update Conditional Access policies to require MFA for all Microsoft 365 applications, including Copilot. Without MFA enforcement, stolen passwords grant full access to AI assistants that can retrieve sensitive organizational data.",
                linkText: "Require MFA with CA",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/howto-conditional-access-policy-all-users-mfa"));
        }
        else
        {
            var detail = $"{ins.CaPolicyEnabled} enabled, {ins.CaPolicyMfaRequired} requiring MFA";
            if (ins.CaPolicyTargetM365 > 0) detail += $", {ins.CaPolicyTargetM365} targeting M365";
            if (ins.CaPolicyLegacyAuthBlocked > 0) detail += $", {ins.CaPolicyLegacyAuthBlocked} blocking legacy auth";

            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"Conditional Access protecting Microsoft 365: {detail}",
                linkText: "CA Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/plan-conditional-access"));
        }

        // Copilot targeting check
        if (ins.CaPolicyTotal > 0 && ins.CaPolicyTargetM365 == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: $"{ins.CaPolicyTotal} Conditional Access policy(ies) configured, but none specifically target Copilot applications",
                recommendation: "Create Copilot-specific Conditional Access policies targeting Microsoft 365 Copilot and Graph Connector applications. Apply stricter controls for AI access: require compliant devices, trusted locations, and MFA.",
                linkText: "Conditional Access for Copilot",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/concept-conditional-access-cloud-apps"));
        }

        return list;
    }

    // ================================================================
    //  4. AAD_PREMIUM_IDENTITY_PROTECTION
    // ================================================================

    private static List<RecommendationResult> GenerateIdentityProtection(IdentityInsights ins)
    {
        const string feature = "Entra Identity Protection";
        var list = new List<RecommendationResult>();

        // License-level
        list.Add(RecommendationResult.Success(Svc, feature,
            observation: "Identity Protection is active, detecting risky Copilot access patterns and compromised accounts",
            linkText: "Protect Against AI Misuse",
            linkUrl: "https://learn.microsoft.com/entra/id-protection/"));

        // Risky users
        if (ins.ConfirmedCompromised > 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{ins.ConfirmedCompromised} confirmed compromised user(s) detected - immediate security incident requiring Copilot access revocation",
                recommendation: $"URGENT: {ins.ConfirmedCompromised} user account(s) confirmed as compromised with potential Copilot access. Revoke all sessions, reset passwords, require MFA registration, review Copilot audit logs for data exfiltration, and block Copilot access until security clearance.",
                linkText: "Respond to Compromised Accounts",
                linkUrl: "https://learn.microsoft.com/entra/id-protection/howto-identity-protection-investigate-risk"));
        }
        else if (ins.AtRisk > 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: $"{ins.AtRisk} at-risk user(s) detected with suspicious sign-in patterns - potential Copilot security threat",
                recommendation: $"Investigate {ins.AtRisk} at-risk user account(s) for potential compromise. Review risk detections, check recent Copilot activity, require MFA re-authentication, and consider temporary Copilot access restriction until risk is remediated.",
                linkText: "Investigate Risk Detections",
                linkUrl: "https://learn.microsoft.com/entra/id-protection/howto-identity-protection-investigate-risk"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "No risky users detected, Identity Protection is actively monitoring and protecting accounts",
                linkText: "Identity Protection Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/id-protection/overview-identity-protection"));
        }

        // Risk-based CA policies
        if (!ins.UserRiskPolicyExists && !ins.SignInRiskPolicyExists)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: "Identity Protection enabled but no risk-based Conditional Access policies configured to protect Copilot",
                recommendation: "Create risk-based Conditional Access policies to automatically block or require re-authentication for risky Copilot access: user risk policy for high-risk users, sign-in risk policy targeting 'Office 365' application.",
                linkText: "Risk-Based Conditional Access",
                linkUrl: "https://learn.microsoft.com/entra/id-protection/howto-identity-protection-configure-risk-policies"));
        }

        return list;
    }

    // ================================================================
    //  5. INTUNE_A  (device compliance + CA integration)
    // ================================================================

    private static List<RecommendationResult> GenerateIntune(IdentityInsights ins)
    {
        const string feature = "Microsoft Intune (Plan A)";
        var list = new List<RecommendationResult>();

        // License-level
        list.Add(RecommendationResult.Success(Svc, feature,
            observation: "Intune is active, managing devices that access M365 Copilot with comprehensive security policies",
            linkText: "Microsoft 365 Documentation",
            linkUrl: "https://learn.microsoft.com/microsoft-365/"));

        // Device compliance state
        if (ins.DevicesTotalManaged == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: "No managed devices detected - users may access Copilot from unmanaged BYOD devices",
                recommendation: "Enroll devices in Intune to enforce compliance policies for Copilot access. Start with company-owned devices, then implement MAM for BYOD scenarios to protect Copilot within managed apps without full device control.",
                linkText: "Device Enrollment",
                linkUrl: "https://learn.microsoft.com/mem/intune/enrollment/"));
        }
        else if (ins.DevicesNonCompliant > 0)
        {
            var compRate = ins.DevicesTotalManaged > 0
                ? ins.DevicesCompliant * 100.0 / ins.DevicesTotalManaged : 0;
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{ins.DevicesNonCompliant} of {ins.DevicesTotalManaged} managed devices ({100 - compRate:F1}%) are non-compliant - may access Copilot despite policy violations",
                recommendation: $"Block Copilot access from {ins.DevicesNonCompliant} non-compliant device(s) using Conditional Access. Create CA policy targeting Microsoft 365 requiring device to be marked as compliant. Review non-compliance reasons in Intune console and remediate.",
                linkText: "Require Compliant Devices",
                linkUrl: "https://learn.microsoft.com/mem/intune/protect/device-compliance-get-started"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"{ins.DevicesCompliant} of {ins.DevicesTotalManaged} managed devices (100%) are compliant, enforcing security policies for Copilot access",
                linkText: "Device Compliance Best Practices",
                linkUrl: "https://learn.microsoft.com/mem/intune/protect/device-compliance-get-started"));
        }

        // CA integration check
        if (ins.DevicesTotalManaged > 0 && !ins.CaRequiresCompliance)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: $"{ins.DevicesTotalManaged} devices managed by Intune, but Conditional Access does not require compliant devices for Copilot",
                recommendation: "Create Conditional Access policy to require compliant devices for Microsoft 365 apps (including Copilot). This ensures only encrypted, malware-protected, policy-compliant devices can access AI capabilities.",
                linkText: "Device Compliance with CA",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/howto-conditional-access-policy-compliant-device"));
        }
        else if (ins.DevicesTotalManaged > 0 && ins.CaRequiresCompliance)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "Conditional Access requires compliant devices for Microsoft 365 apps, enforcing Intune policies for Copilot access",
                linkText: "Device Compliance with CA",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/howto-conditional-access-policy-compliant-device"));
        }

        return list;
    }

    // ================================================================
    //  6. MFA_PREMIUM  (license check only)
    // ================================================================

    private static RecommendationResult GenerateMfaPremium(IdentityInsights ins)
    {
        return RecommendationResult.Success(Svc, "Microsoft Entra ID Multi-Factor Authentication",
            observation: "MFA is active, protecting Copilot sessions with additional authentication factors",
            linkText: "MFA for Copilot Security",
            linkUrl: "https://learn.microsoft.com/entra/identity/authentication/concept-mfa-howitworks");
    }

    // ================================================================
    //  7. AAD_GOVERNANCE  (PIM + access reviews + consent + risky apps)
    // ================================================================

    private static List<RecommendationResult> GenerateGovernance(IdentityInsights ins)
    {
        const string feature = "Microsoft Entra ID Governance";
        var list = new List<RecommendationResult>();

        // License-level
        list.Add(RecommendationResult.Success(Svc, feature,
            observation: "Entra ID Governance is active, enabling automated access governance for Copilot administration",
            linkText: "Govern Copilot Access Rights",
            linkUrl: "https://learn.microsoft.com/entra/id-governance/"));

        // PIM configuration
        if (ins.PermanentAssignmentsTotal > 0 && ins.EligibleAssignments == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{ins.PermanentAssignmentsTotal} permanent admin role assignment(s) detected - standing privileges can override Copilot security controls",
                recommendation: $"Convert {ins.PermanentAssignmentsTotal} permanent admin role(s) to time-bound eligible assignments using PIM. Implement just-in-time activation with MFA and business justification for admin roles affecting Copilot.",
                linkText: "Configure PIM for Admins",
                linkUrl: "https://learn.microsoft.com/entra/id-governance/privileged-identity-management/pim-configure"));
        }
        else if (ins.EligibleAssignments > 0)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"PIM configured with {ins.EligibleAssignments} eligible admin role assignment(s), enforcing just-in-time access",
                linkText: "PIM Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/id-governance/privileged-identity-management/pim-configure"));
        }

        // Access reviews
        if (ins.AccessReviewTotal == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: "No access reviews configured for admin roles or Copilot license assignments",
                recommendation: "Implement quarterly access reviews to certify admin role assignments and Copilot license assignments. Prevents privilege creep and reduces security/licensing costs.",
                linkText: "Configure Access Reviews",
                linkUrl: "https://learn.microsoft.com/entra/id-governance/access-reviews-overview"));
        }
        else
        {
            var detail = $"{ins.AccessReviewTotal} access review definition(s) configured";
            if (ins.AccessReviewActive > 0) detail += $", {ins.AccessReviewActive} currently active";
            if (ins.AccessReviewRoleScope > 0) detail += $", {ins.AccessReviewRoleScope} reviewing admin roles";

            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"Access governance active: {detail}",
                linkText: "Access Reviews Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/id-governance/access-reviews-overview"));
        }

        // App consent (same logic as P2 obs 7)
        if (ins.UserConsentAllowed && !ins.AdminConsentRequired)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: "User consent enabled for applications, allowing users to grant apps access to Copilot-generated content without admin review",
                recommendation: "Disable user consent and require admin approval for all application permissions to prevent unauthorized apps from accessing Copilot data.",
                linkText: "Configure User Consent",
                linkUrl: "https://learn.microsoft.com/entra/identity/enterprise-apps/configure-user-consent"));
        }
        else if (ins.AdminConsentRequired)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "Admin consent required for applications, protecting Copilot data from unauthorized app access",
                linkText: "Admin Consent Workflow",
                linkUrl: "https://learn.microsoft.com/entra/identity/enterprise-apps/configure-admin-consent-workflow"));
        }

        // Risky apps (same logic as P2 obs 8)
        if (ins.HighRiskApps > 0 || ins.UnverifiedPublishers > 0)
        {
            var riskParts = new List<string>();
            if (ins.HighRiskApps > 0) riskParts.Add($"{ins.HighRiskApps} with high-privilege permissions");
            if (ins.UnverifiedPublishers > 0) riskParts.Add($"{ins.UnverifiedPublishers} from unverified publishers");
            if (ins.AppsWithGraphAccess > 0) riskParts.Add($"{ins.AppsWithGraphAccess} accessing Graph API (Copilot data)");

            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"Risky applications detected: {string.Join(", ", riskParts)} - potential unauthorized access to Copilot-generated content",
                recommendation: $"Audit and restrict {ins.HighRiskApps + ins.UnverifiedPublishers} risky application(s). Review permissions in Entra ID > Enterprise Applications, revoke excessive permissions, and implement app governance policies.",
                linkText: "App Governance",
                linkUrl: "https://learn.microsoft.com/defender-cloud-apps/app-governance-manage-app-governance"));
        }
        else
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "No high-risk applications detected, app permissions appropriately scoped for Copilot data protection",
                linkText: "App Permission Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/identity/enterprise-apps/manage-application-permissions"));
        }

        return list;
    }

    // ================================================================
    //  8. ENTRA_INTERNET_ACCESS  (web filtering + traffic forwarding)
    // ================================================================

    private static List<RecommendationResult> GenerateInternetAccess(IdentityInsights ins)
    {
        const string feature = "Microsoft Entra Internet Access";
        var list = new List<RecommendationResult>();

        // Handle GSA status
        if (ins.GsaStatus == "NotLicensed")
        {
            list.Add(RecommendationResult.NotLicensed(Svc, feature,
                observation: "Global Secure Access (Entra Internet Access) is not enabled in your tenant",
                recommendation: "Enable Global Secure Access to protect against Shadow AI and unauthorized cloud service usage. Provides web content filtering to block unauthorized AI platforms, traffic forwarding for DLP inspection, and real-time visibility into AI service access.",
                linkText: "What is Global Secure Access?",
                linkUrl: "https://learn.microsoft.com/entra/global-secure-access/overview-what-is-global-secure-access"));
            return list;
        }
        if (ins.GsaStatus == "PermissionDenied")
        {
            return list;
        }
        if (ins.GsaStatus == "Error" || ins.GsaStatus is null)
        {
            // No data available, skip
            return list;
        }

        // License-level
        list.Add(RecommendationResult.Success(Svc, feature,
            observation: "Entra Internet Access is active, providing secure web gateway with AI service visibility and threat protection",
            linkText: "Secure Internet Gateway for AI",
            linkUrl: "https://learn.microsoft.com/entra/global-secure-access/"));

        // Web content filtering
        var filterCount = ins.FilteringPolicies ?? 0;
        if (filterCount == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: "Global Secure Access deployed, but no web content filtering policies configured to block unauthorized AI platforms",
                recommendation: "Configure web content filtering policies to control AI service access and prevent shadow AI usage. Block consumer AI platforms, create FQDN allow/block lists, and use web category filtering for AI/ML categories.",
                linkText: "Configure Web Content Filtering",
                linkUrl: "https://learn.microsoft.com/entra/global-secure-access/how-to-configure-web-content-filtering"));
        }
        else
        {
            var detail = $"{filterCount} web filtering policy(ies) configured";
            if ((ins.FqdnRules ?? 0) > 0) detail += $", {ins.FqdnRules} FQDN rule(s)";
            if ((ins.WebCategoryRules ?? 0) > 0) detail += $", {ins.WebCategoryRules} web category rule(s)";

            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"Web content filtering active: {detail}, controlling access to AI platforms and detecting shadow AI usage",
                linkText: "Web Filtering Best Practices",
                linkUrl: "https://learn.microsoft.com/entra/global-secure-access/how-to-configure-web-content-filtering"));
        }

        // Traffic forwarding
        var fwdCount = ins.ForwardingProfilesCount ?? 0;
        if (fwdCount == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: "Global Secure Access deployed, but no traffic forwarding profiles configured",
                recommendation: "Configure traffic forwarding profiles to route internet and M365 traffic through the secure gateway for AI security monitoring. Deploy Global Secure Access client to endpoints.",
                linkText: "Configure Traffic Forwarding",
                linkUrl: "https://learn.microsoft.com/entra/global-secure-access/how-to-manage-forwarding-profiles"));
        }
        else
        {
            var fwdParts = new List<string>();
            if (ins.M365ForwardingEnabled == true) fwdParts.Add("M365 traffic");
            if (ins.InternetForwardingEnabled == true) fwdParts.Add("internet traffic");

            if (fwdParts.Count > 0)
            {
                list.Add(RecommendationResult.Success(Svc, feature,
                    observation: $"Traffic forwarding active: {string.Join(" and ", fwdParts)} routed through secure gateway for AI security monitoring",
                    linkText: "Monitor AI Traffic",
                    linkUrl: "https://learn.microsoft.com/entra/global-secure-access/how-to-view-traffic-logs"));
            }
        }

        return list;
    }

    // ================================================================
    //  9. ENTRA_INTERNET_ACCESS_FRONTLINE (license check only)
    // ================================================================

    private static RecommendationResult GenerateInternetAccessFrontline(IdentityInsights ins)
    {
        // Frontline is a license-level check; if GSA is available it is implicitly available.
        if (ins.GsaStatus == "Success")
        {
            return RecommendationResult.Success(Svc,
                "Entra Internet Access for Frontline",
                observation: "Frontline secure access is available, securing frontline worker access to organizational agents",
                linkText: "Frontline Secure Access",
                linkUrl: "https://learn.microsoft.com/entra/global-secure-access/");
        }

        return RecommendationResult.NotLicensed(Svc,
            "Entra Internet Access for Frontline",
            observation: "Entra Internet Access for Frontline is not available, missing secure web access for frontline agent interactions",
            recommendation: "Enable Entra Internet Access to provide secure internet connectivity for frontline workers accessing organizational agents and basic AI services. Ensures agent interactions remain secure and comply with organizational policies.",
            linkText: "Frontline Secure Access",
            linkUrl: "https://learn.microsoft.com/entra/global-secure-access/");
    }

    // ================================================================
    //  10. ENTRA_PRIVATE_ACCESS
    // ================================================================

    private static List<RecommendationResult> GeneratePrivateAccess(IdentityInsights ins)
    {
        const string feature = "Microsoft Entra Private Access";
        var list = new List<RecommendationResult>();

        // Handle status
        if (ins.PrivateAccessStatus == "NotLicensed")
        {
            list.Add(RecommendationResult.NotLicensed(Svc, feature,
                observation: "Global Secure Access (Entra Private Access) is not enabled in your tenant",
                recommendation: "Enable Global Secure Access Private Access to provide secure zero-trust connectivity to internal applications for Copilot and custom agents.",
                linkText: "What is Global Secure Access?",
                linkUrl: "https://learn.microsoft.com/entra/global-secure-access/overview-what-is-global-secure-access"));
            return list;
        }
        if (ins.PrivateAccessStatus == "PermissionDenied")
        {
            return list;
        }
        if (ins.PrivateAccessStatus != "Success")
            return list;

        // License-level
        list.Add(RecommendationResult.Success(Svc, feature,
            observation: "Entra Private Access is active, enabling secure zero-trust access to private resources for AI workloads",
            linkText: "Zero-Trust Network for AI",
            linkUrl: "https://learn.microsoft.com/entra/global-secure-access/"));

        // Connector deployment
        var connectors = ins.PrivateAccessConnectors ?? 0;
        if (connectors > 0)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: $"Private Access configured: {connectors} connector(s) deployed ({ins.PrivateAccessActiveConnectors ?? 0} active), {ins.PrivateAccessAppSegments ?? 0} application segment(s) published for zero-trust AI access",
                linkText: "Manage Private Access",
                linkUrl: "https://learn.microsoft.com/entra/global-secure-access/how-to-configure-connectors"));
        }
        else
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: "Private Access licensed but no connectors deployed to enable AI access to internal resources",
                recommendation: "Deploy Private Access connectors to enable Copilot and custom agents to securely access on-premises applications, databases, and APIs without VPN.",
                linkText: "Deploy Private Access Connectors",
                linkUrl: "https://learn.microsoft.com/entra/global-secure-access/how-to-configure-connectors"));
        }

        return list;
    }

    // ================================================================
    //  11. ENTRA_PRIVATE_ACCESS_CA (license check only)
    // ================================================================

    private static RecommendationResult GeneratePrivateAccessCa(IdentityInsights ins)
    {
        if (ins.PrivateAccessStatus == "Success")
        {
            return RecommendationResult.Success(Svc,
                "Conditional Access for Entra Private Access",
                observation: "Conditional Access for Private Access is active, enforcing access policies for agent connections to internal apps",
                linkText: "Private Access Policies",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/");
        }

        return RecommendationResult.NotLicensed(Svc,
            "Conditional Access for Entra Private Access",
            observation: "Conditional Access for Private Access is not available, missing policy enforcement for agent access to internal systems",
            recommendation: "Enable Conditional Access for Private Access to apply policies when agents connect to internal applications. Enforce device compliance, authentication strength, and session controls for zero-trust agent architecture.",
            linkText: "Private Access Policies",
            linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/");
    }

    // ================================================================
    //  Cloud-Assessment-only generators (12-18)
    // ================================================================

    // ----------------------------------------------------------------
    //  12. Service Principal credential hygiene
    // ----------------------------------------------------------------
    private static List<RecommendationResult> GenerateServicePrincipalHygiene(IdentityInsights ins)
    {
        const string feature = "Service Principal Credentials";
        const string link = "https://learn.microsoft.com/entra/identity-platform/howto-create-service-principal-portal";
        var list = new List<RecommendationResult>();

        if (ins.ServicePrincipalCredentialsExpired > 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{ins.ServicePrincipalCredentialsExpired} service principal credential(s) are expired, leaving automation and integrations broken or relying on stale secrets",
                recommendation: "Rotate or remove expired service principal credentials immediately. Audit each owning application, replace passwords/certificates with fresh material, and remove credentials that are no longer in use to reduce the attack surface.",
                linkText: "Manage Service Principal Credentials",
                linkUrl: link));
        }

        if (ins.ServicePrincipalCredentialsExpiring30d > 0)
        {
            list.Add(RecommendationResult.Warning(Svc, feature,
                observation: $"{ins.ServicePrincipalCredentialsExpiring30d} service principal credential(s) are scheduled to expire within the next 30 days",
                recommendation: "Plan a rotation window for credentials expiring within 30 days. Coordinate with application owners, schedule the rotation off-hours, and verify the dependent integrations resume after the new secret is deployed.",
                linkText: "Manage Service Principal Credentials",
                linkUrl: link));
        }

        if (ins.ServicePrincipalCredentialsOlderThan2Years > 0)
        {
            list.Add(RecommendationResult.Warning(Svc, feature,
                observation: $"{ins.ServicePrincipalCredentialsOlderThan2Years} service principal credential(s) have not been rotated in more than 2 years",
                recommendation: "Adopt a maximum 1-year rotation policy for service principal secrets and certificates. Long-lived credentials accumulate exposure across logs, backups and developer workstations - rotate now and document the cadence.",
                linkText: "Manage Service Principal Credentials",
                linkUrl: link));
        }

        if (list.Count == 0)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "All service principal credentials are within rotation policy (no expired, no expiring in 30 days, none older than 2 years)",
                linkText: "Manage Service Principal Credentials",
                linkUrl: link));
        }

        return list;
    }

    // ----------------------------------------------------------------
    //  13. B2B cross-tenant trust posture
    // ----------------------------------------------------------------
    private static List<RecommendationResult> GenerateB2bTrustPosture(IdentityInsights ins)
    {
        const string feature = "B2B Cross-Tenant Access";
        const string link = "https://learn.microsoft.com/entra/external-id/cross-tenant-access-overview";
        var list = new List<RecommendationResult>();

        if (ins.B2bInboundTrustMfa == "not_accepted")
        {
            list.Add(RecommendationResult.Warning(Svc, feature,
                observation: "Partner MFA not trusted - users re-prompted unnecessarily",
                recommendation: "Enable inbound MFA trust in cross-tenant access settings for trusted partner tenants so guest users can satisfy MFA requirements with the credentials they already use at home, instead of being prompted twice.",
                linkText: "Cross-Tenant Inbound Trust",
                linkUrl: link));
        }

        if (ins.B2bInboundTrustCompliantDevice == "not_accepted" && ins.DevicesTotalManaged > 0)
        {
            list.Add(RecommendationResult.Warning(Svc, feature,
                observation: "Partner compliant-device claim not trusted - guest users on managed partner devices treated as untrusted",
                recommendation: "Accept the compliant-device claim from trusted partner tenants in cross-tenant inbound trust. This lets device-compliance Conditional Access policies pass when partners present evidence from their own MDM.",
                linkText: "Cross-Tenant Inbound Trust",
                linkUrl: link));
        }

        if (ins.B2bAllowedDomains == 0 && ins.B2bBlockedDomains == 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "medium",
                observation: "No cross-tenant access controls defined",
                recommendation: "Define explicit allow/block lists at the cross-tenant access policy level to limit which external organizations can collaborate via B2B. Without any allow or block lists, every external Entra tenant is treated as trusted.",
                linkText: "Cross-Tenant Access Settings",
                linkUrl: link));
        }

        if (list.Count == 0)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "Cross-tenant access posture configured: trust claims aligned with partner expectations and explicit allow/block domain lists in place",
                linkText: "Cross-Tenant Access Settings",
                linkUrl: link));
        }

        return list;
    }

    // ----------------------------------------------------------------
    //  14. PIM activation maturity
    // ----------------------------------------------------------------
    private static List<RecommendationResult> GeneratePimAuditMaturity(IdentityInsights ins)
    {
        const string feature = "PIM Activation Policies";
        const string link = "https://learn.microsoft.com/entra/id-governance/privileged-identity-management/pim-configure";
        var list = new List<RecommendationResult>();

        if (ins.PimAuditEntriesLast30d == 0 && ins.EligibleAssignments > 0)
        {
            list.Add(RecommendationResult.Warning(Svc, feature,
                observation: $"Eligible PIM assignments exist ({ins.EligibleAssignments}) but no activations logged in the last 30 days - review utilization",
                recommendation: "Investigate why eligible PIM assignments are not being activated. Either users have alternative standing access (defeating the purpose of PIM) or the assignments are unused - in which case remove them via an access review.",
                linkText: "PIM Activation Policies",
                linkUrl: link));
        }

        if (ins.PimRolesWithoutMfaRequirement > 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{ins.PimRolesWithoutMfaRequirement} PIM role(s) do not require MFA at activation",
                recommendation: "Enable MFA at activation for every PIM-managed role. Privileged roles must require strong authentication every time they are activated, regardless of the user's recent MFA history.",
                linkText: "Configure PIM Role Settings",
                linkUrl: link));
        }

        if (ins.PimRolesWithoutJustificationRequirement > 0)
        {
            list.Add(RecommendationResult.Warning(Svc, feature,
                observation: $"{ins.PimRolesWithoutJustificationRequirement} PIM role(s) do not require business justification at activation",
                recommendation: "Require a written business justification on every PIM activation. Justifications are essential for audit trails and detecting privilege misuse during incident response.",
                linkText: "Configure PIM Role Settings",
                linkUrl: link));
        }

        if (list.Count == 0)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "PIM activation policies enforce MFA and business justification across all roles, with healthy activation activity",
                linkText: "PIM Activation Policies",
                linkUrl: link));
        }

        return list;
    }

    // ----------------------------------------------------------------
    //  15. User lifecycle hygiene
    // ----------------------------------------------------------------
    private static List<RecommendationResult> GenerateUserLifecycleHygiene(IdentityInsights ins)
    {
        const string feature = "User Lifecycle Hygiene";
        const string link = "https://learn.microsoft.com/entra/identity/users/users-bulk-restore";
        var list = new List<RecommendationResult>();

        if (ins.UsersPasswordNeverExpires > 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{ins.UsersPasswordNeverExpires} user(s) configured with passwords that never expire - non-compliant with any modern security framework (CIS, NIST, ISO 27001, HIPAA, PCI)",
                recommendation: "Remove the DisablePasswordExpiration flag from all user accounts and instead pursue passwordless or phishing-resistant authentication. Password-never-expires accounts violate every modern security baseline and frequently end up as the weakest link in a tenant.",
                linkText: "User Password Policies",
                linkUrl: "https://learn.microsoft.com/entra/identity/authentication/concept-sspr-policy"));
        }

        if (ins.UsersNoSignIn90d > 0)
        {
            list.Add(RecommendationResult.Warning(Svc, feature,
                observation: $"{ins.UsersNoSignIn90d} enabled user(s) have not signed in for 90 days - candidates for disable/offboard",
                recommendation: "Disable or offboard accounts inactive for 90 days. Stale-but-enabled accounts inflate your license cost, your attack surface, and blur audit trails. Use Lifecycle Workflows or an access review to automate the cleanup.",
                linkText: "Manage Inactive Users",
                linkUrl: link));
        }

        if (list.Count == 0)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "User lifecycle hygiene is healthy: no never-expire passwords and no enabled accounts inactive for 90 days",
                linkText: "Identity Lifecycle Best Practices",
                linkUrl: link));
        }

        return list;
    }

    // ----------------------------------------------------------------
    //  16. Administrator MFA coverage
    // ----------------------------------------------------------------
    private static List<RecommendationResult> GenerateAdminMfaCoverage(IdentityInsights ins)
    {
        const string feature = "Administrator MFA";
        const string link = "https://learn.microsoft.com/entra/identity/role-based-access-control/best-practices";
        var list = new List<RecommendationResult>();

        if (ins.AdminsWithoutMfa > 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"{ins.AdminsWithoutMfa} privileged user(s) are not enrolled in MFA",
                recommendation: "Enroll every privileged user in MFA immediately and enforce it via a Conditional Access policy targeting privileged role members. An admin account without MFA is the single highest-impact identity risk in any tenant.",
                linkText: "Administrator Role Best Practices",
                linkUrl: link));
        }

        if (ins.PermanentGlobalAdmins > 5)
        {
            list.Add(RecommendationResult.Warning(Svc, feature,
                observation: $"More than 5 permanent Global Admins ({ins.PermanentGlobalAdmins}) - reduce via PIM",
                recommendation: "Reduce permanent Global Administrator count to 2-4 break-glass accounts and convert the remainder to PIM-eligible assignments. The accepted Microsoft baseline is no more than 5 permanent Global Administrators per tenant.",
                linkText: "Plan Privileged Roles",
                linkUrl: link));
        }

        if (list.Count == 0)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "All privileged users are MFA-enrolled and the permanent Global Admin count is within the recommended baseline",
                linkText: "Administrator Role Best Practices",
                linkUrl: link));
        }

        return list;
    }

    // ----------------------------------------------------------------
    //  17. OAuth consent surface
    // ----------------------------------------------------------------
    private static List<RecommendationResult> GenerateConsentSurface(IdentityInsights ins)
    {
        const string feature = "OAuth Consent";
        const string link = "https://learn.microsoft.com/entra/identity/enterprise-apps/configure-user-consent";
        var list = new List<RecommendationResult>();

        if (ins.OAuthConsentGrantsToAllUsers > 0)
        {
            list.Add(RecommendationResult.Warning(Svc, feature,
                observation: $"{ins.OAuthConsentGrantsToAllUsers} OAuth grant(s) issued to all users (consentType=AllPrincipals) - broad consent surface",
                recommendation: "Review every tenant-wide (AllPrincipals) OAuth grant. Each one allows the consenting application to act against every user account in the tenant - revoke the ones that don't have a clear business owner and a documented review cadence.",
                linkText: "Review OAuth Permission Grants",
                linkUrl: link));
        }

        if (ins.UserConsentAllowed)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: "User consent to third-party apps is unrestricted - block or restrict to verified publishers",
                recommendation: "Restrict user consent to apps from verified publishers requesting low-risk delegated permissions, or disable user consent entirely and route all requests through the admin consent workflow.",
                linkText: "Configure User Consent",
                linkUrl: link));
        }

        if (list.Count == 0)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "OAuth consent surface is controlled: no broad tenant-wide grants and user consent is restricted",
                linkText: "Configure User Consent",
                linkUrl: link));
        }

        return list;
    }

    // ----------------------------------------------------------------
    //  18. Device security posture (BitLocker + compliance gap)
    // ----------------------------------------------------------------
    private static List<RecommendationResult> GenerateDeviceSecurityPosture(IdentityInsights ins)
    {
        const string feature = "Device Security Posture";
        const string link = "https://learn.microsoft.com/mem/intune/protect/encrypt-devices";
        var list = new List<RecommendationResult>();

        if (ins.BitlockerCoveragePct < 80 && ins.DevicesTotalManaged > 0)
        {
            list.Add(RecommendationResult.ActionRequired(Svc, feature, "high",
                observation: $"BitLocker coverage is {ins.BitlockerCoveragePct}% - target ≥95%",
                recommendation: "Drive BitLocker coverage to at least 95% on managed Windows devices via an Intune disk-encryption policy. Unencrypted endpoints are a direct path to data loss on theft and almost always a finding in a regulated audit.",
                linkText: "Encrypt Windows Devices with Intune",
                linkUrl: link));
        }

        if (ins.DevicesWithoutCompliancePolicy > 0)
        {
            list.Add(RecommendationResult.Warning(Svc, feature,
                observation: $"{ins.DevicesWithoutCompliancePolicy} managed device(s) have no compliance policy assigned",
                recommendation: "Assign a baseline compliance policy to every managed device. Without an assigned policy a device cannot be evaluated as compliant, so Conditional Access policies that require device compliance silently fail open.",
                linkText: "Device Compliance Policies",
                linkUrl: "https://learn.microsoft.com/mem/intune/protect/device-compliance-get-started"));
        }

        if (list.Count == 0)
        {
            list.Add(RecommendationResult.Success(Svc, feature,
                observation: "Device security posture is healthy: BitLocker coverage ≥80% and every managed device has a compliance policy assigned",
                linkText: "Encrypt Windows Devices with Intune",
                linkUrl: link));
        }

        return list;
    }
}
