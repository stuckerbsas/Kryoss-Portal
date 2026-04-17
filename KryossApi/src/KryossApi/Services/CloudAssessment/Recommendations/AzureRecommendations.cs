using System.Globalization;
using KryossApi.Services.CloudAssessment.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;

namespace KryossApi.Services.CloudAssessment.Recommendations;

/// <summary>
/// Generates Cloud Assessment (CA-6 Subsession B) Azure recommendations
/// from a pre-computed <see cref="AzureInsights"/> bag.
///
/// Covers seven service surfaces:
///
///   * <c>arm</c>             — subscription / resource inventory hygiene
///                              (empty subscriptions, multi-region sprawl,
///                              overall resource density, posture rollup).
///   * <c>defender-cloud</c>  — Microsoft Defender for Cloud assessments,
///                              secure score thresholds and unhealthy ratio.
///   * <c>storage</c>         — public blob exposure, HTTP-only endpoints,
///                              soft-delete retention on storage accounts.
///   * <c>keyvault</c>        — soft-delete + purge protection on Key Vault
///                              instances.
///   * <c>network</c>         — public IP sprawl and NSG rules that allow
///                              any source to any destination on any port.
///   * <c>compute</c>         — VM OS-disk encryption gaps and managed
///                              identity adoption.
///   * <c>policy</c>          — non-compliant resources across Azure Policy
///                              assignments.
///
/// Reuses <see cref="RecommendationResult"/> from CopilotReadiness so
/// downstream report rendering / pipeline result processing remains
/// shared. Observation / recommendation text is in English — bilingual
/// rendering happens downstream.
///
/// This module is pure computation: no HTTP calls, no reflection, no
/// <c>Process.Start</c>. Safe to invoke with a default-constructed
/// <see cref="AzureInsights"/> — it will emit the "no subscriptions"
/// insight and the positive-posture rollup without throwing.
/// </summary>
public static class AzureRecommendations
{
    private const string ArmSvc = "arm";
    private const string DefenderCloudSvc = "defender-cloud";
    private const string StorageSvc = "storage";
    private const string KeyVaultSvc = "keyvault";
    private const string NetworkSvc = "network";
    private const string ComputeSvc = "compute";
    private const string PolicySvc = "policy";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ----------------------------------------------------------------
    //  Public entry point
    // ----------------------------------------------------------------

    public static List<RecommendationResult> Generate(AzureInsights ins)
    {
        var all = new List<RecommendationResult>();

        // ==== arm (subscription + resource inventory) ====
        all.AddRange(GenerateNoSubscriptionsScanned(ins));
        all.AddRange(GenerateEmptySubscriptions(ins));
        all.AddRange(GenerateResourceSprawl(ins));
        all.AddRange(GenerateMultiRegionFootprint(ins));
        all.AddRange(GeneratePublicExposurePostureSuccess(ins));

        // ==== defender-cloud ====
        all.AddRange(GenerateDefenderNotEnabled(ins));
        all.AddRange(GenerateDefenderUnhealthyRatio(ins));
        all.AddRange(GenerateDefenderHealthyDominates(ins));
        all.AddRange(GenerateSecureScoreLow(ins));
        all.AddRange(GenerateSecureScoreGood(ins));

        // ==== storage ====
        all.AddRange(GenerateStoragePublicBlob(ins));
        all.AddRange(GenerateStorageHttpEnabled(ins));
        all.AddRange(GenerateStorageNoSoftDelete(ins));

        // ==== keyvault ====
        all.AddRange(GenerateKeyVaultNoSoftDelete(ins));
        all.AddRange(GenerateKeyVaultNoPurgeProtection(ins));

        // ==== network ====
        all.AddRange(GeneratePublicIpSprawl(ins));
        all.AddRange(GenerateNsgAnyAnyAllow(ins));

        // ==== compute ====
        all.AddRange(GenerateVmUnencryptedOsDisk(ins));
        all.AddRange(GenerateVmWithoutManagedIdentity(ins));

        // ==== policy ====
        all.AddRange(GeneratePolicyNonCompliant(ins));

        return all;
    }

    // ================================================================
    //  arm — No subscriptions scanned
    // ================================================================

    private static List<RecommendationResult> GenerateNoSubscriptionsScanned(AzureInsights ins)
    {
        const string feature = "Subscription Coverage";
        var list = new List<RecommendationResult>();

        if (ins.SubscriptionsScanned == 0)
        {
            list.Add(RecommendationResult.Insight(ArmSvc, feature,
                observation: "No Azure subscriptions were scanned — the customer has not yet connected a subscription or consent is missing.",
                recommendation: "Connect an Azure subscription via the portal Azure tab and assign the Kryoss service principal a Reader role at subscription scope.",
                linkText: "Assign Azure RBAC roles",
                linkUrl: "https://learn.microsoft.com/en-us/azure/role-based-access-control/role-assignments-portal"));
        }

        return list;
    }

    // ================================================================
    //  arm — Empty subscription (one finding per empty sub)
    // ================================================================

    private static List<RecommendationResult> GenerateEmptySubscriptions(AzureInsights ins)
    {
        var list = new List<RecommendationResult>();

        foreach (var sub in ins.Subscriptions)
        {
            if (sub.ResourceCount != 0) continue;

            string label = string.IsNullOrWhiteSpace(sub.DisplayName)
                ? sub.SubscriptionId
                : sub.DisplayName!;

            list.Add(RecommendationResult.Insight(ArmSvc, $"Empty subscription: {label}",
                observation: $"Subscription '{label}' contains no ARM resources.",
                recommendation: "Confirm the subscription is intentional — idle subscriptions still incur baseline cost and expand the blast radius of a compromised Reader role.",
                linkText: "Decommission unused subscriptions",
                linkUrl: "https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/cancel-azure-subscription"));
        }

        return list;
    }

    // ================================================================
    //  arm — Resource sprawl (avg > 500 per subscription)
    // ================================================================

    private static List<RecommendationResult> GenerateResourceSprawl(AzureInsights ins)
    {
        const string feature = "Resource Density";
        var list = new List<RecommendationResult>();

        if (ins.SubscriptionsScanned <= 0) return list;
        if (ins.ResourcesTotal <= 500 * ins.SubscriptionsScanned) return list;

        double avg = (double)ins.ResourcesTotal / ins.SubscriptionsScanned;
        string avgLabel = avg.ToString("F1", Inv);

        list.Add(RecommendationResult.Insight(ArmSvc, feature,
            observation: $"{ins.ResourcesTotal.ToString(Inv)} resources across {ins.SubscriptionsScanned.ToString(Inv)} subscription(s) (avg {avgLabel} per subscription).",
            recommendation: "Consider splitting workloads into separate subscriptions and applying management group-based governance to keep blast radius and quota headroom manageable.",
            linkText: "Azure subscription and service limits",
            linkUrl: "https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits"));

        return list;
    }

    // ================================================================
    //  arm — Multi-region footprint (>3 distinct locations)
    // ================================================================

    private static List<RecommendationResult> GenerateMultiRegionFootprint(AzureInsights ins)
    {
        const string feature = "Regional Footprint";
        var list = new List<RecommendationResult>();

        var distinctRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in ins.Subscriptions)
        {
            foreach (var region in sub.LocationBreakdown.Keys)
            {
                if (!string.IsNullOrWhiteSpace(region))
                {
                    distinctRegions.Add(region);
                }
            }
        }

        if (distinctRegions.Count <= 3) return list;

        list.Add(RecommendationResult.Insight(ArmSvc, feature,
            observation: $"Workloads are distributed across {distinctRegions.Count.ToString(Inv)} Azure regions.",
            recommendation: "Review the multi-region footprint for data-residency alignment and BC/DR intent — unplanned regional sprawl complicates compliance and cost tracking.",
            linkText: "Azure geographies and regions",
            linkUrl: "https://learn.microsoft.com/en-us/azure/reliability/availability-zones-overview"));

        return list;
    }

    // ================================================================
    //  arm — Positive posture rollup
    // ================================================================

    private static List<RecommendationResult> GeneratePublicExposurePostureSuccess(AzureInsights ins)
    {
        const string feature = "Public exposure posture";
        var list = new List<RecommendationResult>();

        if (ins.StorageAccountsPublicBlob == 0
            && ins.StorageAccountsHttpEnabled == 0
            && ins.NsgAnyAnyAllowRules == 0
            && ins.VmsUnencryptedOsDisk == 0)
        {
            // Success factory forces Priority="none" — override to "informational" per spec.
            list.Add(new RecommendationResult
            {
                Service = ArmSvc,
                Feature = feature,
                Status = "success",
                Priority = "informational",
                Observation = "No public exposure findings across storage, network, or compute.",
                LinkText = "Azure security best practices",
                LinkUrl = "https://learn.microsoft.com/en-us/azure/security/fundamentals/best-practices-and-patterns"
            });
        }

        return list;
    }

    // ================================================================
    //  defender-cloud — Not enabled
    // ================================================================

    private static List<RecommendationResult> GenerateDefenderNotEnabled(AzureInsights ins)
    {
        const string feature = "Defender for Cloud Activation";
        var list = new List<RecommendationResult>();

        int total = ins.AssessmentsHealthy + ins.AssessmentsUnhealthy + ins.AssessmentsNotApplicable;
        if (total != 0) return list;

        // Only emit when there's at least one subscription — otherwise #14 covers the empty case.
        if (ins.SubscriptionsScanned == 0) return list;

        list.Add(RecommendationResult.ActionRequired(DefenderCloudSvc, feature, "high",
            observation: "Microsoft Defender for Cloud returned zero assessments across all connected subscriptions — the service is not enabled or the caller lacks the required role.",
            recommendation: "Enable Microsoft Defender for Cloud on every subscription and grant Reader / Security Reader to the Kryoss service principal so posture can be read.",
            linkText: "Enable Microsoft Defender for Cloud",
            linkUrl: "https://learn.microsoft.com/en-us/azure/defender-for-cloud/get-started"));

        return list;
    }

    // ================================================================
    //  defender-cloud — High unhealthy ratio
    // ================================================================

    private static List<RecommendationResult> GenerateDefenderUnhealthyRatio(AzureInsights ins)
    {
        const string feature = "Defender Unhealthy Ratio";
        var list = new List<RecommendationResult>();

        int denom = ins.AssessmentsHealthy + ins.AssessmentsUnhealthy;
        if (denom < 10) return list;

        double ratio = (double)ins.AssessmentsUnhealthy / denom;
        if (ratio <= 0.30) return list;

        string ratioLabel = ratio.ToString("P1", Inv);

        list.Add(RecommendationResult.ActionRequired(DefenderCloudSvc, feature, "high",
            observation: $"{ins.AssessmentsUnhealthy.ToString(Inv)} of {denom.ToString(Inv)} scored Defender assessments are unhealthy ({ratioLabel}).",
            recommendation: "Work the Defender for Cloud recommendations backlog, starting with the highest-severity items, and retire or accept risk on stale ones.",
            linkText: "Review Defender for Cloud recommendations",
            linkUrl: "https://learn.microsoft.com/en-us/azure/defender-for-cloud/review-security-recommendations"));

        return list;
    }

    // ================================================================
    //  defender-cloud — Healthy dominates (positive reinforcement)
    // ================================================================

    private static List<RecommendationResult> GenerateDefenderHealthyDominates(AzureInsights ins)
    {
        const string feature = "Defender Assessment Posture";
        var list = new List<RecommendationResult>();

        if (ins.AssessmentsUnhealthy == 0 && ins.AssessmentsHealthy > 0)
        {
            // Success factory forces Priority="none" — override to "informational" per spec.
            list.Add(new RecommendationResult
            {
                Service = DefenderCloudSvc,
                Feature = feature,
                Status = "success",
                Priority = "informational",
                Observation = $"All {ins.AssessmentsHealthy.ToString(Inv)} scored Defender assessments are healthy.",
                LinkText = "Defender for Cloud secure score",
                LinkUrl = "https://learn.microsoft.com/en-us/azure/defender-for-cloud/secure-score-security-controls"
            });
        }

        return list;
    }

    // ================================================================
    //  defender-cloud — Secure score low (<50)
    // ================================================================

    private static List<RecommendationResult> GenerateSecureScoreLow(AzureInsights ins)
    {
        const string feature = "Secure Score";
        var list = new List<RecommendationResult>();

        if (ins.SecureScorePct is null) return list;
        decimal pct = ins.SecureScorePct.Value;
        if (pct >= 50m) return list;

        string pctLabel = pct.ToString("F1", Inv);

        list.Add(RecommendationResult.Warning(DefenderCloudSvc, feature,
            observation: $"Defender for Cloud secure score is {pctLabel}% — below the 50% threshold.",
            recommendation: "Focus on the top-weighted recommendations in Defender for Cloud to raise the secure score above 50% as a first milestone.",
            linkText: "Improve your secure score",
            linkUrl: "https://learn.microsoft.com/en-us/azure/defender-for-cloud/secure-score-access-and-track"));

        return list;
    }

    // ================================================================
    //  defender-cloud — Secure score good (>=70)
    // ================================================================

    private static List<RecommendationResult> GenerateSecureScoreGood(AzureInsights ins)
    {
        const string feature = "Secure Score Healthy";
        var list = new List<RecommendationResult>();

        if (ins.SecureScorePct is null) return list;
        decimal pct = ins.SecureScorePct.Value;
        if (pct < 70m) return list;

        string pctLabel = pct.ToString("F1", Inv);

        // Success factory forces Priority="none" — override to "informational" per spec.
        list.Add(new RecommendationResult
        {
            Service = DefenderCloudSvc,
            Feature = feature,
            Status = "success",
            Priority = "informational",
            Observation = $"Defender for Cloud secure score is {pctLabel}% — within the healthy band (>=70%).",
            LinkText = "Secure score in Defender for Cloud",
            LinkUrl = "https://learn.microsoft.com/en-us/azure/defender-for-cloud/secure-score-security-controls"
        });

        return list;
    }

    // ================================================================
    //  storage — Public Blob Access
    // ================================================================

    private static List<RecommendationResult> GenerateStoragePublicBlob(AzureInsights ins)
    {
        const string feature = "Public Blob Access";
        var list = new List<RecommendationResult>();

        if (ins.StorageAccountsPublicBlob <= 0) return list;

        list.Add(RecommendationResult.ActionRequired(StorageSvc, feature, "critical",
            observation: $"{ins.StorageAccountsPublicBlob.ToString(Inv)} storage account(s) allow anonymous public blob access.",
            recommendation: "Disable anonymous blob access at the account level and require SAS tokens or RBAC-authenticated access for all containers.",
            linkText: "Disable anonymous public read access",
            linkUrl: "https://learn.microsoft.com/en-us/azure/storage/blobs/anonymous-read-access-prevent"));

        return list;
    }

    // ================================================================
    //  storage — HTTP enabled (TLS not enforced)
    // ================================================================

    private static List<RecommendationResult> GenerateStorageHttpEnabled(AzureInsights ins)
    {
        const string feature = "Secure Transfer Required";
        var list = new List<RecommendationResult>();

        if (ins.StorageAccountsHttpEnabled <= 0) return list;

        list.Add(RecommendationResult.ActionRequired(StorageSvc, feature, "critical",
            observation: $"{ins.StorageAccountsHttpEnabled.ToString(Inv)} storage account(s) accept unencrypted HTTP traffic.",
            recommendation: "Enable 'Secure transfer required' on every storage account to force HTTPS and reject plain HTTP requests.",
            linkText: "Require secure transfer for a storage account",
            linkUrl: "https://learn.microsoft.com/en-us/azure/storage/common/storage-require-secure-transfer"));

        return list;
    }

    // ================================================================
    //  storage — No soft-delete on blobs
    // ================================================================

    private static List<RecommendationResult> GenerateStorageNoSoftDelete(AzureInsights ins)
    {
        const string feature = "Blob Soft-Delete";
        var list = new List<RecommendationResult>();

        if (ins.StorageAccountsNoSoftDelete <= 0) return list;

        list.Add(RecommendationResult.Warning(StorageSvc, feature,
            observation: $"{ins.StorageAccountsNoSoftDelete.ToString(Inv)} storage account(s) have blob soft-delete disabled.",
            recommendation: "Enable blob soft-delete with a 7-30 day retention window so accidental or malicious deletions can be recovered.",
            linkText: "Soft delete for blobs",
            linkUrl: "https://learn.microsoft.com/en-us/azure/storage/blobs/soft-delete-blob-overview"));

        return list;
    }

    // ================================================================
    //  keyvault — Soft-delete disabled
    // ================================================================

    private static List<RecommendationResult> GenerateKeyVaultNoSoftDelete(AzureInsights ins)
    {
        const string feature = "Key Vault Soft-Delete";
        var list = new List<RecommendationResult>();

        if (ins.KeyVaultsNoSoftDelete <= 0) return list;

        list.Add(RecommendationResult.ActionRequired(KeyVaultSvc, feature, "high",
            observation: $"{ins.KeyVaultsNoSoftDelete.ToString(Inv)} Key Vault(s) have soft-delete disabled.",
            recommendation: "Enable soft-delete on every Key Vault — this is a mandatory baseline and new vaults enforce it by default.",
            linkText: "Azure Key Vault soft-delete overview",
            linkUrl: "https://learn.microsoft.com/en-us/azure/key-vault/general/soft-delete-overview"));

        return list;
    }

    // ================================================================
    //  keyvault — Purge protection disabled
    // ================================================================

    private static List<RecommendationResult> GenerateKeyVaultNoPurgeProtection(AzureInsights ins)
    {
        const string feature = "Key Vault Purge Protection";
        var list = new List<RecommendationResult>();

        if (ins.KeyVaultsNoPurgeProtection <= 0) return list;

        list.Add(RecommendationResult.Warning(KeyVaultSvc, feature,
            observation: $"{ins.KeyVaultsNoPurgeProtection.ToString(Inv)} Key Vault(s) have purge protection disabled.",
            recommendation: "Enable purge protection so soft-deleted vaults and secrets cannot be permanently destroyed before the retention window expires.",
            linkText: "Purge protection in Key Vault",
            linkUrl: "https://learn.microsoft.com/en-us/azure/key-vault/general/soft-delete-overview#purge-protection"));

        return list;
    }

    // ================================================================
    //  network — Public IP sprawl (>5)
    // ================================================================

    private static List<RecommendationResult> GeneratePublicIpSprawl(AzureInsights ins)
    {
        const string feature = "Public IP Sprawl";
        var list = new List<RecommendationResult>();

        if (ins.PublicIpCount <= 5) return list;

        list.Add(RecommendationResult.Warning(NetworkSvc, feature,
            observation: $"{ins.PublicIpCount.ToString(Inv)} public IP addresses in scope — internet-reachable surface is above the 5-IP threshold.",
            recommendation: "Inventory each public IP, front internet workloads with Application Gateway / Front Door, and retire unused public IPs to shrink the attack surface.",
            linkText: "Public IP address overview",
            linkUrl: "https://learn.microsoft.com/en-us/azure/virtual-network/ip-services/public-ip-addresses"));

        return list;
    }

    // ================================================================
    //  network — NSG any/any/allow
    // ================================================================

    private static List<RecommendationResult> GenerateNsgAnyAnyAllow(AzureInsights ins)
    {
        const string feature = "NSG Unrestricted Inbound";
        var list = new List<RecommendationResult>();

        if (ins.NsgAnyAnyAllowRules <= 0) return list;

        list.Add(RecommendationResult.ActionRequired(NetworkSvc, feature, "critical",
            observation: $"{ins.NsgAnyAnyAllowRules.ToString(Inv)} NSG rule(s) allow traffic from any source to any destination on any port.",
            recommendation: "Replace any/any/allow rules with least-privilege source-scoped rules (service tags, specific IPs, ASGs) and gate management ports behind Bastion or Just-In-Time VM access.",
            linkText: "Network security group security rules",
            linkUrl: "https://learn.microsoft.com/en-us/azure/virtual-network/network-security-groups-overview"));

        return list;
    }

    // ================================================================
    //  compute — VM unencrypted OS disk
    // ================================================================

    private static List<RecommendationResult> GenerateVmUnencryptedOsDisk(AzureInsights ins)
    {
        const string feature = "VM OS Disk Encryption";
        var list = new List<RecommendationResult>();

        if (ins.VmsUnencryptedOsDisk <= 0) return list;

        list.Add(RecommendationResult.ActionRequired(ComputeSvc, feature, "high",
            observation: $"{ins.VmsUnencryptedOsDisk.ToString(Inv)} VM(s) have unencrypted OS disks.",
            recommendation: "Enable Azure Disk Encryption or server-side encryption with customer-managed keys on every VM OS disk — data-at-rest exposure on snapshot/export.",
            linkText: "Azure Disk Encryption overview",
            linkUrl: "https://learn.microsoft.com/en-us/azure/virtual-machines/disk-encryption-overview"));

        return list;
    }

    // ================================================================
    //  compute — VM without managed identity
    // ================================================================

    private static List<RecommendationResult> GenerateVmWithoutManagedIdentity(AzureInsights ins)
    {
        const string feature = "VM Managed Identity";
        var list = new List<RecommendationResult>();

        if (ins.VmsWithoutManagedIdentity <= 0) return list;

        // Warning factory forces Priority="medium" — override to "low" per spec.
        list.Add(new RecommendationResult
        {
            Service = ComputeSvc,
            Feature = feature,
            Status = "warning",
            Priority = "low",
            Observation = $"{ins.VmsWithoutManagedIdentity.ToString(Inv)} VM(s) have no managed identity assigned.",
            Recommendation = "Assign a system-assigned or user-assigned managed identity to each VM that needs to authenticate to Azure services — eliminates credential-in-config patterns.",
            LinkText = "Managed identities for Azure resources",
            LinkUrl = "https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/overview"
        });

        return list;
    }

    // ================================================================
    //  policy — Non-compliant resources
    // ================================================================

    private static List<RecommendationResult> GeneratePolicyNonCompliant(AzureInsights ins)
    {
        const string feature = "Policy Compliance";
        var list = new List<RecommendationResult>();

        if (ins.PolicyNonCompliantResources is null) return list;
        int count = ins.PolicyNonCompliantResources.Value;
        if (count <= 0) return list;

        list.Add(RecommendationResult.Warning(PolicySvc, feature,
            observation: $"{count.ToString(Inv)} resource(s) are non-compliant with one or more Azure Policy assignments.",
            recommendation: "Review non-compliant resources in Azure Policy and either remediate them, exempt with justification, or adjust the policy definition scope.",
            linkText: "Azure Policy compliance data",
            linkUrl: "https://learn.microsoft.com/en-us/azure/governance/policy/how-to/get-compliance-data"));

        return list;
    }
}
