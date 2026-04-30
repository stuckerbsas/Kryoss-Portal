using System.Globalization;
using KryossApi.Services.CloudAssessment.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;

namespace KryossApi.Services.CloudAssessment.Recommendations;

/// <summary>
/// CA-10 Mail Flow findings generator. Turns a populated
/// <see cref="MailFlowInsights"/> bag into a flat list of
/// <see cref="RecommendationResult"/>s. Service codes: "email" for per-domain
/// DNS posture, "mail_flow" for mailbox/tenant surface.
/// </summary>
public static class MailFlowRecommendations
{
    private const string EmailSvc = "email";
    private const string MailFlowSvc = "mail_flow";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const string SpfLink = "https://learn.microsoft.com/microsoft-365/security/office-365-security/email-authentication-anti-spoofing";
    private const string DkimLink = "https://learn.microsoft.com/defender-office-365/email-authentication-dkim-configure";
    private const string DmarcLink = "https://learn.microsoft.com/defender-office-365/email-authentication-dmarc-configure";
    private const string MtaStsLink = "https://learn.microsoft.com/defender-office-365/mta-sts";
    private const string BimiLink = "https://learn.microsoft.com/defender-office-365/bimi-howto";
    private const string ForwardingLink = "https://learn.microsoft.com/defender-office-365/outbound-spam-policies-external-email-forwarding";
    private const string SharedMbxLink = "https://learn.microsoft.com/exchange/recipients-in-exchange-online/manage-user-mailboxes/disable-shared-mailbox-signin";

    public static List<RecommendationResult> Generate(MailFlowInsights ins)
    {
        var all = new List<RecommendationResult>();

        foreach (var d in ins.DomainResults)
        {
            all.AddRange(GenerateSpf(d));
            all.AddRange(GenerateDkim(d));
            all.AddRange(GenerateDmarc(d));
            all.AddRange(GenerateMtaSts(d));
            all.AddRange(GenerateBimi(d));
        }

        all.AddRange(GenerateForwardingFindings(ins));
        all.AddRange(GenerateSharedMailboxFindings(ins));
        all.AddRange(GenerateConsentFinding(ins));
        all.AddRange(GenerateExchangeFindings(ins));

        return all;
    }

    // ── SPF ────────────────────────────────────────────────────────────

    private static List<RecommendationResult> GenerateSpf(DomainInspectionResult d)
    {
        const string feature = "SPF";
        var list = new List<RecommendationResult>();

        if (!d.SpfValid)
        {
            list.Add(RecommendationResult.ActionRequired(EmailSvc, feature, "high",
                observation: $"Domain {d.Domain} has no SPF record — spoofed mail cannot be rejected at the SMTP edge.",
                recommendation: "Publish a TXT record starting with 'v=spf1 include:spf.protection.outlook.com -all'.",
                linkText: "SPF configuration", linkUrl: SpfLink));
            return list;
        }

        if (d.SpfLookupCount > 10)
        {
            list.Add(RecommendationResult.Warning(EmailSvc, feature,
                observation: $"Domain {d.Domain} SPF exceeds 10 DNS lookups ({d.SpfLookupCount.ToString(Inv)}) — evaluators will return PermError.",
                recommendation: "Flatten nested includes or replace third-party includes with ip4/ip6 literals.",
                linkText: "SPF configuration", linkUrl: SpfLink));
            return list;
        }

        if (d.SpfMechanism == "+all" || d.SpfMechanism == "?all")
        {
            list.Add(RecommendationResult.Warning(EmailSvc, feature,
                observation: $"Domain {d.Domain} SPF ends with '{d.SpfMechanism}' — permits or ignores unauthorized senders.",
                recommendation: "Change terminator to '-all' (hard fail) once all legitimate senders are included.",
                linkText: "SPF configuration", linkUrl: SpfLink));
            return list;
        }

        if (d.SpfMechanism == "-all")
        {
            list.Add(RecommendationResult.Success(EmailSvc, feature,
                observation: $"Domain {d.Domain} SPF hard-fails unauthorized senders ({d.SpfLookupCount.ToString(Inv)} lookups).",
                linkText: "SPF configuration", linkUrl: SpfLink));
        }
        else if (d.SpfMechanism == "~all")
        {
            list.Add(RecommendationResult.Warning(EmailSvc, feature,
                observation: $"Domain {d.Domain} SPF uses soft-fail ('~all') — unauthorized mail is marked but delivered.",
                recommendation: "Tighten to '-all' once RUA reports confirm no legitimate failures.",
                linkText: "SPF configuration", linkUrl: SpfLink));
        }

        return list;
    }

    // ── DKIM ───────────────────────────────────────────────────────────

    private static List<RecommendationResult> GenerateDkim(DomainInspectionResult d)
    {
        const string feature = "DKIM";
        var list = new List<RecommendationResult>();

        var both = d.DkimS1Present && d.DkimS2Present;
        var either = d.DkimS1Present || d.DkimS2Present;

        if (!either)
        {
            list.Add(RecommendationResult.ActionRequired(EmailSvc, feature, "high",
                observation: $"Domain {d.Domain} has no DKIM selectors published — outbound mail cannot be cryptographically signed.",
                recommendation: "Enable DKIM in Defender Portal for this domain and publish selector1/selector2 CNAMEs.",
                linkText: "Enable DKIM", linkUrl: DkimLink));
        }
        else if (!both)
        {
            list.Add(RecommendationResult.Warning(EmailSvc, feature,
                observation: $"Domain {d.Domain} has only one DKIM selector ({string.Join(",", d.DkimSelectors)}) — key rotation will break signing.",
                recommendation: "Publish both selector1 and selector2 CNAME records so M365 can rotate keys without outage.",
                linkText: "Enable DKIM", linkUrl: DkimLink));
        }
        else
        {
            list.Add(RecommendationResult.Success(EmailSvc, feature,
                observation: $"Domain {d.Domain} has both DKIM selectors published.",
                linkText: "Enable DKIM", linkUrl: DkimLink));
        }

        return list;
    }

    // ── DMARC ──────────────────────────────────────────────────────────

    private static List<RecommendationResult> GenerateDmarc(DomainInspectionResult d)
    {
        const string feature = "DMARC";
        var list = new List<RecommendationResult>();

        if (!d.DmarcValid)
        {
            list.Add(RecommendationResult.ActionRequired(EmailSvc, feature, "high",
                observation: $"Domain {d.Domain} has no _dmarc TXT record — no aggregate feedback and no policy enforcement.",
                recommendation: "Publish 'v=DMARC1; p=none; rua=mailto:dmarc@yourdomain; pct=100' to begin monitoring.",
                linkText: "DMARC configuration", linkUrl: DmarcLink));
            return list;
        }

        if (d.DmarcPolicy == "none")
        {
            list.Add(RecommendationResult.Warning(EmailSvc, feature,
                observation: $"Domain {d.Domain} DMARC is monitor-only (p=none) — spoofed mail is reported but still delivered.",
                recommendation: "Graduate to p=quarantine after 30 days of clean RUA aggregate reports, then p=reject.",
                linkText: "DMARC configuration", linkUrl: DmarcLink));
            return list;
        }

        if ((d.DmarcPolicy == "quarantine" || d.DmarcPolicy == "reject") && d.DmarcPct == 100)
        {
            list.Add(RecommendationResult.Success(EmailSvc, feature,
                observation: $"Domain {d.Domain} enforces DMARC p={d.DmarcPolicy} at 100%.",
                linkText: "DMARC configuration", linkUrl: DmarcLink));
        }
        else if (d.DmarcPct.HasValue && d.DmarcPct < 100)
        {
            list.Add(RecommendationResult.Warning(EmailSvc, feature,
                observation: $"Domain {d.Domain} DMARC p={d.DmarcPolicy} applies to only {d.DmarcPct.Value.ToString(Inv)}% of mail.",
                recommendation: "Raise pct= to 100 once RUA reports show zero legitimate-failure cases.",
                linkText: "DMARC configuration", linkUrl: DmarcLink));
        }

        return list;
    }

    // ── MTA-STS ────────────────────────────────────────────────────────

    private static List<RecommendationResult> GenerateMtaSts(DomainInspectionResult d)
    {
        const string feature = "MTA-STS";
        var list = new List<RecommendationResult>();

        if (d.MtaStsPolicy == "missing")
        {
            list.Add(RecommendationResult.Warning(EmailSvc, feature,
                observation: $"Domain {d.Domain} has no MTA-STS TXT record — inbound SMTP has no enforced TLS policy.",
                recommendation: "Publish '_mta-sts.{domain}' TXT with 'v=STSv1; id=...' and host an mta-sts.txt policy over HTTPS.",
                linkText: "MTA-STS overview", linkUrl: MtaStsLink));
        }
        else if (d.MtaStsPolicy == "testing")
        {
            list.Add(RecommendationResult.Insight(EmailSvc, feature,
                observation: $"Domain {d.Domain} has an MTA-STS TXT published (mode inferred as 'testing' — HTTPS policy not verified).",
                recommendation: "Fetch mta-sts.{domain}/.well-known/mta-sts.txt and confirm 'mode: enforce' before treating as enforcing.",
                linkText: "MTA-STS overview", linkUrl: MtaStsLink));
        }
        else if (d.MtaStsPolicy == "enforce")
        {
            list.Add(RecommendationResult.Success(EmailSvc, feature,
                observation: $"Domain {d.Domain} enforces MTA-STS.",
                linkText: "MTA-STS overview", linkUrl: MtaStsLink));
        }

        return list;
    }

    // ── BIMI ───────────────────────────────────────────────────────────

    private static List<RecommendationResult> GenerateBimi(DomainInspectionResult d)
    {
        const string feature = "BIMI";
        var list = new List<RecommendationResult>();

        if (d.BimiPresent)
        {
            list.Add(RecommendationResult.Insight(EmailSvc, feature,
                observation: $"Domain {d.Domain} publishes a BIMI record — logo appears in supporting clients when DMARC is enforced.",
                linkText: "BIMI setup", linkUrl: BimiLink));
        }

        return list;
    }

    // ── Forwarding / tenant surface ────────────────────────────────────

    private static List<RecommendationResult> GenerateForwardingFindings(MailFlowInsights ins)
    {
        var list = new List<RecommendationResult>();

        var external = ins.ForwardingRisks.Count(r => r.RiskType == "external_forward");
        var stealth = ins.ForwardingRisks.Count(r => r.RiskType == "stealth_forward");

        if (external > 0)
        {
            list.Add(RecommendationResult.ActionRequired(MailFlowSvc, "External Mailbox Forwarding", "high",
                observation: $"{external.ToString(Inv)} inbox rule(s) forward messages to external addresses — common data-exfiltration vector.",
                recommendation: "Disable user-defined auto-forwarding via outbound spam policy and audit the flagged mailboxes.",
                linkText: "Outbound forwarding controls", linkUrl: ForwardingLink));
        }
        else if (ins.MailReadConsented && ins.UsersSampled > 0)
        {
            list.Add(RecommendationResult.Success(MailFlowSvc, "External Mailbox Forwarding",
                observation: $"No external forwarding rules found across {ins.UsersSampled.ToString(Inv)} sampled mailboxes.",
                linkText: "Outbound forwarding controls", linkUrl: ForwardingLink));
        }

        if (stealth > 0)
        {
            list.Add(RecommendationResult.ActionRequired(MailFlowSvc, "Stealth Forwarding Rules", "high",
                observation: $"{stealth.ToString(Inv)} rule(s) forward AND auto-delete — hides exfiltration from the user.",
                recommendation: "Investigate affected mailboxes for BEC compromise and remove the rule immediately.",
                linkText: "Outbound forwarding controls", linkUrl: ForwardingLink));
        }

        return list;
    }

    private static List<RecommendationResult> GenerateSharedMailboxFindings(MailFlowInsights ins)
    {
        var list = new List<RecommendationResult>();

        if (ins.SharedMailboxes.Count > 0)
        {
            list.Add(RecommendationResult.Insight(MailFlowSvc, "Shared Mailbox Sign-in",
                observation: $"{ins.SharedMailboxes.Count.ToString(Inv)} unlicensed mailbox(es) detected — likely shared mailboxes.",
                recommendation: "Verify via Exchange Online PowerShell that sign-in is blocked for each (Set-User -AccountDisabled $true).",
                linkText: "Block shared-mailbox sign-in", linkUrl: SharedMbxLink));
        }

        return list;
    }

    private static List<RecommendationResult> GenerateConsentFinding(MailFlowInsights ins)
    {
        var list = new List<RecommendationResult>();

        if (!ins.MailReadConsented)
        {
            list.Add(RecommendationResult.PermissionRequired(MailFlowSvc, "Mailbox Rule Inspection",
                observation: "Forwarding-rule inspection skipped — Graph returned 403 on mailFolders/inbox/messageRules.",
                recommendation: "Grant 'MailboxSettings.Read' (or 'Mail.Read') application permission in the consent flow and re-scan.",
                linkText: "Graph permissions", linkUrl: "https://learn.microsoft.com/graph/permissions-reference#mailboxsettings-permissions"));
        }

        return list;
    }

    private const string AuditLogLink = "https://learn.microsoft.com/purview/audit-log-enable-disable";
    private const string SafeAttachLink = "https://learn.microsoft.com/defender-office-365/safe-attachments-about";
    private const string EopLink = "https://learn.microsoft.com/defender-office-365/preset-security-policies";

    private static List<RecommendationResult> GenerateExchangeFindings(MailFlowInsights ins)
    {
        var list = new List<RecommendationResult>();
        if (!ins.ExchangeAvailable) return list;

        if (!ins.UnifiedAuditLogEnabled)
        {
            list.Add(RecommendationResult.ActionRequired(MailFlowSvc, "Unified Audit Logs", "high",
                observation: "Unified audit log ingestion is disabled. Microsoft 365 audit events are not being recorded.",
                recommendation: "Enable unified audit log ingestion in the compliance portal or via Exchange PowerShell (Set-AdminAuditLogConfig -UnifiedAuditLogIngestionEnabled $true).",
                linkText: "Enable audit logging", linkUrl: AuditLogLink));
        }
        else
        {
            list.Add(RecommendationResult.Success(MailFlowSvc, "Unified Audit Logs",
                observation: "Unified audit log ingestion is enabled.",
                linkText: "Audit logging", linkUrl: AuditLogLink));
        }

        if (!ins.HasSafeAttachmentPolicy)
        {
            list.Add(RecommendationResult.ActionRequired(MailFlowSvc, "Safe Attachments", "medium",
                observation: "No Safe Attachments policies are configured. Email attachments are not scanned for malware in a sandbox.",
                recommendation: "Configure a Safe Attachments policy in Microsoft Defender for Office 365 to scan inbound attachments.",
                linkText: "Safe Attachments", linkUrl: SafeAttachLink));
        }
        else
        {
            list.Add(RecommendationResult.Success(MailFlowSvc, "Safe Attachments",
                observation: $"{ins.SafeAttachmentPolicyCount} Safe Attachments policy(ies) configured.",
                linkText: "Safe Attachments", linkUrl: SafeAttachLink));
        }

        if (!ins.HasEopStandardProtection)
        {
            list.Add(RecommendationResult.ActionRequired(MailFlowSvc, "EOP Standard Protection", "medium",
                observation: "EOP/MDO Standard preset protection policy is not active. Basic email protection may not be enforced consistently.",
                recommendation: "Enable the Standard preset security policy in Microsoft Defender for Office 365 portal.",
                linkText: "Preset security policies", linkUrl: EopLink));
        }
        else
        {
            list.Add(RecommendationResult.Success(MailFlowSvc, "EOP Standard Protection",
                observation: $"EOP preset protection active — Standard: yes, Strict: {(ins.HasEopStrictProtection ? "yes" : "no")}.",
                linkText: "Preset security policies", linkUrl: EopLink));
        }

        return list;
    }
}
