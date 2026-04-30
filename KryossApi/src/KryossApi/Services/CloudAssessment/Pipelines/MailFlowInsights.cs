namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// CA-10 Mail Flow & Email Security insights container. Produced by the
/// MailFlowPipeline (Task 2) and consumed by MailFlowRecommendations.
/// Holds per-domain DNS posture (SPF/DKIM/DMARC/MTA-STS/BIMI), mailbox
/// forwarding and shared-mailbox risks, plus a consent flag for callers
/// that lack Mail.Read/MailboxSettings.Read permissions.
/// </summary>
public class MailFlowInsights
{
    public List<DomainInspectionResult> DomainResults { get; set; } = [];
    public List<MailboxRisk> ForwardingRisks { get; set; } = [];
    public List<SharedMailbox> SharedMailboxes { get; set; } = [];

    /// <summary>Number of users sampled for mailbox risk inspection.</summary>
    public int UsersSampled { get; set; }

    /// <summary>
    /// Set to false by the pipeline when Graph returns 403 on /messageRules —
    /// pipeline emits Info recommendation in that case.
    /// </summary>
    public bool MailReadConsented { get; set; } = true;

    // ── Exchange Online REST API checks (Lighthouse gaps) ──
    public bool ExchangeAvailable { get; set; }
    public bool UnifiedAuditLogEnabled { get; set; }
    public bool HasSafeAttachmentPolicy { get; set; }
    public int SafeAttachmentPolicyCount { get; set; }
    public bool HasEopStandardProtection { get; set; }
    public bool HasEopStrictProtection { get; set; }
}

public class DomainInspectionResult
{
    public string Domain { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsVerified { get; set; }
    // SPF
    public string? SpfRecord { get; set; }
    public bool SpfValid { get; set; }
    public string? SpfMechanism { get; set; }  // -all | ~all | ?all | +all | missing
    public int SpfLookupCount { get; set; }
    public List<string> SpfWarnings { get; set; } = [];
    // DKIM
    public bool DkimS1Present { get; set; }
    public bool DkimS2Present { get; set; }
    public List<string> DkimSelectors { get; set; } = [];
    // DMARC
    public string? DmarcRecord { get; set; }
    public bool DmarcValid { get; set; }
    public string? DmarcPolicy { get; set; }
    public string? DmarcSubdomainPolicy { get; set; }
    public int? DmarcPct { get; set; }
    public string? DmarcRua { get; set; }
    public string? DmarcRuf { get; set; }
    // MTA-STS
    public string? MtaStsRecord { get; set; }
    public string? MtaStsPolicy { get; set; }  // enforce | testing | none | missing
    // BIMI
    public bool BimiPresent { get; set; }
    // Aggregate
    public decimal Score { get; set; }
}

public class MailboxRisk
{
    public string UserPrincipalName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string RiskType { get; set; } = string.Empty;
    public string? RiskDetail { get; set; }
    public string? ForwardTarget { get; set; }
    public string? Severity { get; set; }
}

public class SharedMailbox
{
    public string MailboxUpn { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int? DelegatesCount { get; set; }
    public List<string> FullAccessUsers { get; set; } = [];
    public List<string> SendAsUsers { get; set; } = [];
    public bool HasPasswordEnabled { get; set; }
    public DateTime? LastActivity { get; set; }
}
