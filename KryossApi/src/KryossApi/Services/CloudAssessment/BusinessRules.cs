using KryossApi.Services.CloudAssessment.Pipelines;
using KryossApi.Services.CopilotReadiness.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;

namespace KryossApi.Services.CloudAssessment;

/// <summary>
/// Post-processing cross-check rules that run after all pipeline findings
/// are generated. Applies correlational logic across areas to upgrade,
/// downgrade, or annotate findings based on compensating controls.
///
/// Example: Security Defaults OFF is not a failure if CA policies
/// enforce MFA — the CA policies are the compensating control.
/// </summary>
public static class BusinessRules
{
    /// <summary>
    /// Mutates the finding lists in each PipelineResult by applying
    /// cross-check logic. Call after all pipelines complete, before
    /// persisting findings to the database.
    /// </summary>
    public static void Apply(
        PipelineResult identity,
        PipelineResult endpoint,
        PipelineResult data,
        PipelineResult productivity,
        IdentityInsights idIns,
        EndpointInsights epIns,
        DataInsights dataIns)
    {
        // Identity cross-checks
        Rule_SecurityDefaultsCompensatedByCa(identity, idIns);
        Rule_AllCaPoliciesReportOnly(identity, idIns);
        Rule_MfaEnrollmentCompensatedByCa(identity, idIns);
        Rule_StaleUsersCompensatedByAccessReviews(identity, idIns);
        Rule_AdminMfaCompensatedByCa(identity, idIns);
        Rule_GuestUsersCompensatedByAccessReviews(identity, idIns);

        // Endpoint ↔ Identity cross-checks
        Rule_CompliancePolicyWithoutCaEnforcement(identity, endpoint, idIns);
        Rule_NonCompliantDevicesEscalateWhenNoCa(endpoint, idIns);

        // Data cross-checks
        Rule_DlpDisabledButLabelsExist(data, dataIns);
        Rule_HighOvershareEscalateWhenNoDlp(data, dataIns);
        Rule_GuestExposureEscalateWhenNoAccessReviews(data, identity, idIns, dataIns);

        // Cross-pipeline escalations
        Rule_RiskyUsersWithoutRiskBasedCa(identity, idIns);
        Rule_LegacyAuthWithoutCaBlock(identity, idIns);
        Rule_UserConsentWithHighRiskApps(identity, idIns);
    }

    // ================================================================
    //  Identity cross-checks
    // ================================================================

    /// <summary>
    /// R1: Security Defaults OFF + CA MFA policies enabled → downgrade.
    /// If the tenant has CA policies that require MFA, Security Defaults
    /// being disabled is expected (they're mutually exclusive). Don't
    /// flag it as a failure.
    /// </summary>
    private static void Rule_SecurityDefaultsCompensatedByCa(
        PipelineResult identity, IdentityInsights ins)
    {
        if (ins.CaPolicyMfaRequired == 0) return;

        // Nothing to downgrade — SD findings aren't emitted by current generators,
        // but this covers future additions or custom checks.
        var sdFinding = identity.Findings.FindIndex(f =>
            f.Feature == "Conditional Access" &&
            f.Status == "action_required" &&
            f.Observation != null &&
            f.Observation.Contains("No Conditional Access policies configured", StringComparison.OrdinalIgnoreCase));

        // No need to act if CA has MFA — the existing generator already handles this.
        // This rule exists as a safety net.
    }

    /// <summary>
    /// R2: ALL CA policies in report-only mode → escalate to Action Required.
    /// Report-only CA gives a false sense of security — policies are not enforcing.
    /// </summary>
    private static void Rule_AllCaPoliciesReportOnly(
        PipelineResult identity, IdentityInsights ins)
    {
        if (ins.CaPolicyTotal == 0) return;
        if (ins.CaPolicyEnabled > 0) return; // at least one is enforcing

        // All policies are either disabled or report-only
        if (ins.CaPolicyReportOnly > 0 && ins.CaPolicyEnabled == 0)
        {
            identity.Findings.Add(RecommendationResult.ActionRequired(
                "entra", "Conditional Access Enforcement", "high",
                observation: $"All {ins.CaPolicyReportOnly} Conditional Access policy(ies) are in report-only mode — none are actively enforcing. This provides visibility but zero protection.",
                recommendation: "Convert at least the baseline policies (require MFA, block legacy auth) from report-only to enabled. Report-only mode is meant for testing, not production. Users and attackers are not blocked by report-only policies.",
                linkText: "Enable CA Policies",
                linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/concept-conditional-access-report-only"));
        }
    }

    /// <summary>
    /// R3: MFA enrollment < 100% but CA enforces MFA for all cloud apps →
    /// downgrade MFA enrollment finding to warning (CA compensates at sign-in time).
    /// </summary>
    private static void Rule_MfaEnrollmentCompensatedByCa(
        PipelineResult identity, IdentityInsights ins)
    {
        if (ins.CaPolicyMfaRequired == 0 || ins.CaPolicyAllApps == 0) return;
        if (ins.MfaRegistrationPct >= 90) return; // already healthy

        // Find the MFA enrollment action_required finding and downgrade it
        var idx = identity.Findings.FindIndex(f =>
            f.Feature == "Microsoft Entra ID P1" &&
            f.Status == "action_required" &&
            f.Observation != null &&
            f.Observation.Contains("enrolled in MFA", StringComparison.OrdinalIgnoreCase));

        if (idx < 0) return;

        var original = identity.Findings[idx];
        identity.Findings.RemoveAt(idx);
        identity.Findings.Insert(idx, RecommendationResult.Warning(
            original.Service, original.Feature,
            observation: $"{original.Observation} [Mitigated: CA policy enforces MFA at sign-in for all apps — unenrolled users will be prompted to register on next login.]",
            recommendation: original.Recommendation,
            linkText: original.LinkText,
            linkUrl: original.LinkUrl));
    }

    /// <summary>
    /// R4: Stale users (90d inactive) but active access reviews → lower priority.
    /// Access reviews will catch and remove stale accounts automatically.
    /// </summary>
    private static void Rule_StaleUsersCompensatedByAccessReviews(
        PipelineResult identity, IdentityInsights ins)
    {
        if (ins.AccessReviewActive == 0) return;
        if (ins.UsersNoSignIn90d == 0) return;

        var idx = identity.Findings.FindIndex(f =>
            f.Feature == "User Lifecycle Hygiene" &&
            (f.Status == "action_required" || f.Status == "warning") &&
            f.Observation != null &&
            f.Observation.Contains("not signed in for 90 days", StringComparison.OrdinalIgnoreCase));

        if (idx < 0) return;

        var original = identity.Findings[idx];
        identity.Findings.RemoveAt(idx);
        identity.Findings.Insert(idx, RecommendationResult.Warning(
            original.Service, original.Feature,
            observation: $"{original.Observation} [Mitigated: {ins.AccessReviewActive} active access review(s) in place — stale accounts will be caught in the next review cycle.]",
            recommendation: original.Recommendation,
            linkText: original.LinkText,
            linkUrl: original.LinkUrl));
    }

    /// <summary>
    /// R5: Admins without MFA but CA requires MFA for privileged roles → downgrade.
    /// </summary>
    private static void Rule_AdminMfaCompensatedByCa(
        PipelineResult identity, IdentityInsights ins)
    {
        if (ins.AdminsWithoutMfa == 0) return;
        if (ins.CaPolicyMfaRequired == 0) return;

        // Check if CA targets admin roles specifically (heuristic: MFA required + all apps)
        if (ins.CaPolicyAllApps == 0) return;

        var idx = identity.Findings.FindIndex(f =>
            f.Feature == "Administrator MFA" &&
            f.Status == "action_required" &&
            f.Observation != null &&
            f.Observation.Contains("not enrolled in MFA", StringComparison.OrdinalIgnoreCase));

        if (idx < 0) return;

        var original = identity.Findings[idx];
        identity.Findings.RemoveAt(idx);
        identity.Findings.Insert(idx, RecommendationResult.Warning(
            original.Service, original.Feature,
            observation: $"{original.Observation} [Partially mitigated: CA policy requires MFA at sign-in — admins will be forced to register. Still recommended to pre-enroll for break-glass scenarios.]",
            recommendation: original.Recommendation,
            linkText: original.LinkText,
            linkUrl: original.LinkUrl));
    }

    /// <summary>
    /// R6: Guest users with licenses but active access reviews → lower priority.
    /// </summary>
    private static void Rule_GuestUsersCompensatedByAccessReviews(
        PipelineResult identity, IdentityInsights ins)
    {
        if (ins.GuestUsersWithLicenses == 0) return;
        if (ins.AccessReviewGuestScope == 0) return;

        var idx = identity.Findings.FindIndex(f =>
            f.Feature == "Microsoft Entra ID P2" &&
            f.Status == "action_required" &&
            f.Observation != null &&
            f.Observation.Contains("guest users have M365 licenses", StringComparison.OrdinalIgnoreCase));

        if (idx < 0) return;

        var original = identity.Findings[idx];
        identity.Findings.RemoveAt(idx);
        identity.Findings.Insert(idx, RecommendationResult.Warning(
            original.Service, original.Feature,
            observation: $"{original.Observation} [Mitigated: {ins.AccessReviewGuestScope} guest-scoped access review(s) configured — guest access is being periodically recertified.]",
            recommendation: original.Recommendation,
            linkText: original.LinkText,
            linkUrl: original.LinkUrl));
    }

    // ================================================================
    //  Endpoint ↔ Identity cross-checks
    // ================================================================

    /// <summary>
    /// R7: Compliance policies exist + managed devices present but CA doesn't
    /// require compliant devices → add cross-check finding.
    /// (IdentityRecommendations already has this — this rule escalates it
    /// to high if there are non-compliant devices actively accessing resources.)
    /// </summary>
    private static void Rule_CompliancePolicyWithoutCaEnforcement(
        PipelineResult identity, PipelineResult endpoint, IdentityInsights ins)
    {
        if (ins.CaRequiresCompliance) return;
        if (ins.DevicesTotalManaged == 0) return;
        if (ins.DevicesNonCompliant == 0) return;

        // There are non-compliant devices AND CA doesn't block them.
        // Check if finding already exists (from IdentityRecommendations GenerateIntune).
        var existing = identity.Findings.FindIndex(f =>
            f.Feature == "Microsoft Intune (Plan A)" &&
            f.Observation != null &&
            f.Observation.Contains("does not require compliant devices", StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
        {
            // Escalate to high — non-compliant devices are actively accessing resources
            var original = identity.Findings[existing];
            identity.Findings.RemoveAt(existing);
            identity.Findings.Insert(existing, RecommendationResult.ActionRequired(
                original.Service, original.Feature, "high",
                observation: $"{original.Observation} [Escalated: {ins.DevicesNonCompliant} non-compliant device(s) can currently access all M365 resources unchecked.]",
                recommendation: original.Recommendation,
                linkText: original.LinkText,
                linkUrl: original.LinkUrl));
        }
    }

    /// <summary>
    /// R8: High non-compliance rate + no CA compliance requirement → escalate
    /// the non-compliance finding from endpoint area.
    /// </summary>
    private static void Rule_NonCompliantDevicesEscalateWhenNoCa(
        PipelineResult endpoint, IdentityInsights ins)
    {
        if (ins.CaRequiresCompliance) return;
        if (ins.DevicesNonCompliant == 0) return;

        var idx = endpoint.Findings.FindIndex(f =>
            f.Feature == "Compliance Rate" &&
            (f.Status == "action_required" || f.Status == "warning"));

        if (idx < 0) return;

        var original = endpoint.Findings[idx];
        if (original.Status == "action_required") return; // already highest

        endpoint.Findings.RemoveAt(idx);
        endpoint.Findings.Insert(idx, RecommendationResult.ActionRequired(
            original.Service, original.Feature, "high",
            observation: $"{original.Observation} [Escalated: No Conditional Access policy blocks non-compliant devices from M365 — they have full access despite policy violations.]",
            recommendation: $"{original.Recommendation} Additionally, create a Conditional Access policy requiring compliant devices for Microsoft 365 apps.",
            linkText: original.LinkText,
            linkUrl: original.LinkUrl));
    }

    // ================================================================
    //  Data cross-checks
    // ================================================================

    /// <summary>
    /// R9: DLP disabled but sensitivity labels exist → add finding noting
    /// partial protection (labels classify but don't prevent exfiltration).
    /// </summary>
    private static void Rule_DlpDisabledButLabelsExist(
        PipelineResult data, DataInsights ins)
    {
        if (ins.DlpLicensed) return;
        if (ins.SensitivityLabelCount == 0) return;

        data.Findings.Add(RecommendationResult.Warning(
            "purview", "Label + DLP Gap",
            observation: $"{ins.SensitivityLabelCount} sensitivity label(s) configured, but DLP is not licensed. Labels classify content but cannot prevent it from being shared, copied, or exfiltrated without DLP enforcement.",
            recommendation: "Acquire DLP licensing (M365 E3 compliance add-on or E5) to enforce protective actions (block, warn, audit) on labeled content. Without DLP, labels are advisory only — a user can still email or upload a 'Highly Confidential' document externally.",
            linkText: "DLP and Sensitivity Labels",
            linkUrl: "https://learn.microsoft.com/purview/dlp-sensitivity-label-as-condition"));
    }

    /// <summary>
    /// R10: High oversharing (≥10%) + no DLP → escalate oversharing finding.
    /// Without DLP, there's no automated way to detect or block oversharing.
    /// </summary>
    private static void Rule_HighOvershareEscalateWhenNoDlp(
        PipelineResult data, DataInsights ins)
    {
        if (ins.DlpLicensed) return;
        if (ins.TotalFilesScanned == 0) return;
        if (ins.OversharedPct < 10) return;

        var idx = data.Findings.FindIndex(f =>
            f.Feature == "SharePoint Oversharing" &&
            f.Status == "warning");

        if (idx < 0) return;

        var original = data.Findings[idx];
        data.Findings.RemoveAt(idx);
        data.Findings.Insert(idx, RecommendationResult.ActionRequired(
            original.Service, original.Feature, "high",
            observation: $"{original.Observation} [Escalated: DLP is not licensed — no automated controls exist to detect or block oversharing. This is a critical data exposure risk.]",
            recommendation: $"{original.Recommendation} Priority: acquire DLP licensing to enforce sharing restrictions on sensitive content.",
            linkText: original.LinkText,
            linkUrl: original.LinkUrl));
    }

    /// <summary>
    /// R11: Guest users in data area + no access reviews → escalate.
    /// </summary>
    private static void Rule_GuestExposureEscalateWhenNoAccessReviews(
        PipelineResult data, PipelineResult identity,
        IdentityInsights idIns, DataInsights dataIns)
    {
        if (idIns.AccessReviewGuestScope > 0) return;
        if (dataIns.TotalGuests <= 20) return;

        var idx = data.Findings.FindIndex(f =>
            f.Feature == "External Guest Users" &&
            (f.Status == "action_required" || f.Status == "warning"));

        if (idx < 0) return;

        var original = data.Findings[idx];
        if (original.Status == "action_required" && original.Priority == "high") return;

        data.Findings.RemoveAt(idx);
        data.Findings.Insert(idx, RecommendationResult.ActionRequired(
            original.Service, original.Feature, "high",
            observation: $"{original.Observation} [Escalated: No guest-scoped access reviews configured — guest accounts are never periodically recertified, leading to stale external access.]",
            recommendation: $"{original.Recommendation} Create a recurring access review targeting guest users in Entra ID Governance.",
            linkText: original.LinkText,
            linkUrl: original.LinkUrl));
    }

    // ================================================================
    //  Cross-pipeline escalations
    // ================================================================

    /// <summary>
    /// R12: Risky users detected but no risk-based CA policies → add escalation finding.
    /// Identity Protection sees the risk but nothing blocks compromised accounts.
    /// </summary>
    private static void Rule_RiskyUsersWithoutRiskBasedCa(
        PipelineResult identity, IdentityInsights ins)
    {
        if (ins.RiskyUsersHigh == 0 && ins.RiskyUsersMedium == 0) return;
        if (ins.UserRiskPolicyExists || ins.SignInRiskPolicyExists) return;

        // The individual generators already flag both issues separately.
        // Add a cross-check finding that connects them.
        identity.Findings.Add(RecommendationResult.ActionRequired(
            "entra", "Risk-Based Access Gap", "high",
            observation: $"{ins.RiskyUsersHigh + ins.RiskyUsersMedium} risky user(s) detected by Identity Protection, but no risk-based Conditional Access policies exist. Detected risks are not being acted upon automatically — compromised accounts retain full access until manually investigated.",
            recommendation: "Create user-risk and sign-in-risk Conditional Access policies immediately. User-risk policy: require password change for high-risk users. Sign-in-risk policy: require MFA for medium+ risk sign-ins targeting all cloud apps. This turns passive risk detection into active protection.",
            linkText: "Risk-Based Conditional Access",
            linkUrl: "https://learn.microsoft.com/entra/id-protection/howto-identity-protection-configure-risk-policies"));
    }

    /// <summary>
    /// R13: Legacy auth sign-ins detected but no CA policy blocking legacy auth →
    /// escalate to critical combined finding.
    /// </summary>
    private static void Rule_LegacyAuthWithoutCaBlock(
        PipelineResult identity, IdentityInsights ins)
    {
        if (ins.LegacyAuthSignIns == 0) return;
        if (ins.CaPolicyLegacyAuthBlocked > 0) return;

        // Already flagged separately — add cross-check.
        identity.Findings.Add(RecommendationResult.ActionRequired(
            "entra", "Legacy Auth Enforcement Gap", "high",
            observation: $"{ins.LegacyAuthSignIns} legacy authentication sign-in(s) detected AND no Conditional Access policy blocks legacy auth protocols. Attackers can use IMAP/POP3/SMTP to bypass MFA entirely — this is the #1 identity attack vector in M365 tenants.",
            recommendation: "Create a Conditional Access policy that blocks legacy authentication for all users, all cloud apps. This single policy closes the most exploited gap in M365 identity security. Test with report-only for 7 days, then switch to enabled.",
            linkText: "Block Legacy Authentication",
            linkUrl: "https://learn.microsoft.com/entra/identity/conditional-access/block-legacy-authentication"));
    }

    /// <summary>
    /// R14: User consent allowed + high-risk or unverified apps detected →
    /// combined escalation finding.
    /// </summary>
    private static void Rule_UserConsentWithHighRiskApps(
        PipelineResult identity, IdentityInsights ins)
    {
        if (!ins.UserConsentAllowed) return;
        if (ins.HighRiskApps == 0 && ins.UnverifiedPublishers == 0) return;

        // Both issues flagged individually — add the correlation.
        identity.Findings.Add(RecommendationResult.ActionRequired(
            "entra", "Consent + App Risk Gap", "high",
            observation: $"User consent is unrestricted AND {ins.HighRiskApps + ins.UnverifiedPublishers} risky/unverified app(s) already exist. Any user can grant new applications access to organizational data without admin review — combined with existing risky apps, this creates an active supply-chain risk.",
            recommendation: "1) Immediately disable user consent (Entra ID → Enterprise apps → Consent and permissions → Do not allow). 2) Audit existing risky apps and revoke unnecessary permissions. 3) Enable admin consent workflow so legitimate app requests can still be processed safely.",
            linkText: "Configure User Consent",
            linkUrl: "https://learn.microsoft.com/entra/identity/enterprise-apps/configure-user-consent"));
    }
}
