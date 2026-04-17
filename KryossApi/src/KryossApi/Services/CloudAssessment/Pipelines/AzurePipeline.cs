using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services.CloudAssessment.Recommendations;
using KryossApi.Services.CopilotReadiness.Pipelines;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// Cloud Assessment (CA-6 Subsession B) Azure infrastructure pipeline.
///
/// Aggregates Azure Resource Manager (ARM) signals across one or more
/// subscriptions via parallel REST collectors:
///
///   * Resources inventory (Microsoft.Resources) — type + location
///     breakdown, seeds per-resource rows that type-specific collectors
///     enrich with properties + risk flags.
///   * Defender for Cloud — assessments (healthy / unhealthy /
///     notApplicable) and secure score.
///   * Public IP addresses.
///   * Network Security Groups — any/any/allow inbound rule detection.
///   * Storage accounts — public blob, HTTP enabled, missing soft-delete.
///   * Key Vault — soft-delete and purge-protection hygiene.
///   * Virtual Machines — OS-disk encryption and managed-identity presence.
///   * Azure Policy — non-compliant resource count (optional, tolerated).
///
/// Follows the resiliency contract from <c>EndpointPipeline</c>: each
/// collector catches its own exceptions (403 / 404 are treated as
/// non-fatal permission or RP-not-registered signals) and marks the
/// shared <c>CollectorErrorTracker</c>. Findings generation is deferred
/// to <see cref="AzureRecommendations"/> (Task B3).
///
/// The orchestrator (Task B4) constructs the ARM HttpClient once with
/// a bearer token scoped to <c>https://management.azure.com</c> and
/// passes it in. When the client is null or no subscriptions are
/// connected the pipeline returns <c>status = "skipped"</c> with no side
/// effects.
/// </summary>
public static class AzurePipeline
{
    private const string ArmBase = "https://management.azure.com";
    private const int ResourceListMaxPages = 20;

    // ARM resource type strings we explicitly count.
    private const string TypeVm = "Microsoft.Compute/virtualMachines";
    private const string TypeStorage = "Microsoft.Storage/storageAccounts";
    private const string TypeKeyVault = "Microsoft.KeyVault/vaults";
    private const string TypeNsg = "Microsoft.Network/networkSecurityGroups";
    private const string TypePublicIp = "Microsoft.Network/publicIPAddresses";
    private const string TypeSql = "Microsoft.Sql/servers/databases";

    public static async Task<PipelineResult> RunAsync(
        HttpClient? armHttp,
        IReadOnlyList<string> subscriptionIds,
        Guid scanId,
        KryossDbContext db,
        ILogger log,
        CancellationToken ct)
    {
        // --- Early exit: nothing to scan, no DB writes. ---------------------
        if (armHttp is null || subscriptionIds is null || subscriptionIds.Count == 0)
        {
            return new PipelineResult { PipelineName = "azure", Status = "skipped" };
        }

        var result = new PipelineResult { PipelineName = "azure", Status = "ok" };
        var ins = new AzureInsights();
        var err = new CollectorErrorTracker();
        // Single lock protects every AzureInsights mutation (counters + lists).
        // Resource counts are in the hundreds/thousands — contention is a
        // non-issue and the code stays simple.
        var insLock = new object();

        try
        {
            // Flat task list: subscription × collector. Each collector handles
            // its own exceptions and flags the shared tracker.
            var tasks = new List<Task>(subscriptionIds.Count * 8);
            foreach (var subId in subscriptionIds)
            {
                if (string.IsNullOrWhiteSpace(subId)) continue;
                tasks.Add(CollectResources(armHttp, subId, ins, err, insLock, log, ct));
                tasks.Add(CollectDefenderAssessments(armHttp, subId, ins, err, insLock, log, ct));
                tasks.Add(CollectPublicIps(armHttp, subId, ins, err, insLock, log, ct));
                tasks.Add(CollectNsgs(armHttp, subId, ins, err, insLock, log, ct));
                tasks.Add(CollectStorageAccounts(armHttp, subId, ins, err, insLock, log, ct));
                tasks.Add(CollectKeyVaults(armHttp, subId, ins, err, insLock, log, ct));
                tasks.Add(CollectVirtualMachines(armHttp, subId, ins, err, insLock, log, ct));
                tasks.Add(CollectPolicyCompliance(armHttp, subId, ins, err, insLock, log, ct));
            }

            await Task.WhenAll(tasks);

            ins.SubscriptionsScanned = subscriptionIds.Count;

            // Findings come from Task B3 — currently a stub returning [].
            result.Findings.AddRange(AzureRecommendations.Generate(ins));

            // --- Metrics (string-valued, snake_case). ------------------------
            var m = result.Metrics;
            var inv = CultureInfo.InvariantCulture;

            m["subscriptions_scanned"] = ins.SubscriptionsScanned.ToString(inv);
            m["resources_total"] = ins.ResourcesTotal.ToString(inv);
            m["resources_vm"] = ins.VmsCount.ToString(inv);
            m["resources_storage"] = ins.StorageAccountsCount.ToString(inv);
            m["resources_keyvault"] = ins.KeyVaultsCount.ToString(inv);
            m["resources_nsg"] = ins.NsgCount.ToString(inv);
            m["resources_public_ip"] = ins.PublicIpCount.ToString(inv);
            m["resources_sql"] = ins.SqlDatabasesCount.ToString(inv);

            m["assessments_healthy"] = ins.AssessmentsHealthy.ToString(inv);
            m["assessments_unhealthy"] = ins.AssessmentsUnhealthy.ToString(inv);
            m["assessments_not_applicable"] = ins.AssessmentsNotApplicable.ToString(inv);
            if (ins.SecureScorePct.HasValue)
            {
                m["secure_score_pct"] = ins.SecureScorePct.Value.ToString("F2", inv);
            }

            m["storage_public_blob"] = ins.StorageAccountsPublicBlob.ToString(inv);
            m["storage_http_enabled"] = ins.StorageAccountsHttpEnabled.ToString(inv);
            m["storage_no_soft_delete"] = ins.StorageAccountsNoSoftDelete.ToString(inv);
            m["public_ip_count"] = ins.PublicIpCount.ToString(inv);
            m["nsg_any_any_allow"] = ins.NsgAnyAnyAllowRules.ToString(inv);

            m["keyvaults_no_soft_delete"] = ins.KeyVaultsNoSoftDelete.ToString(inv);
            m["keyvaults_no_purge_protection"] = ins.KeyVaultsNoPurgeProtection.ToString(inv);

            m["vms_unencrypted_os_disk"] = ins.VmsUnencryptedOsDisk.ToString(inv);
            m["vms_no_managed_identity"] = ins.VmsWithoutManagedIdentity.ToString(inv);

            if (ins.PolicyNonCompliantResources.HasValue)
            {
                m["policy_non_compliant_resources"] =
                    ins.PolicyNonCompliantResources.Value.ToString(inv);
            }

            // --- Persist per-resource rows (single SaveChanges). -------------
            if (ins.Resources.Count > 0)
            {
                var now = DateTime.UtcNow;
                foreach (var row in ins.Resources)
                {
                    db.CloudAssessmentAzureResources.Add(new CloudAssessmentAzureResource
                    {
                        ScanId = scanId,
                        SubscriptionId = row.SubscriptionId,
                        ResourceType = row.ResourceType,
                        ResourceId = row.ResourceId,
                        Name = row.Name,
                        Location = row.Location,
                        Kind = row.Kind,
                        PropertiesJson = row.PropertiesJson,
                        RiskFlags = row.RiskFlags.Count > 0
                            ? JsonSerializer.Serialize(row.RiskFlags)
                            : null,
                        CreatedAt = now,
                    });
                }

                await db.SaveChangesAsync(ct);
            }

            // Partial when any collector errored. Keep findings / metrics.
            if (err.HadError)
            {
                return new PipelineResult
                {
                    PipelineName = result.PipelineName,
                    Status = "partial",
                    Findings = result.Findings,
                    Metrics = result.Metrics,
                    SharepointSites = result.SharepointSites,
                    ExternalUsers = result.ExternalUsers,
                };
            }

            return result;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Azure pipeline top-level failure");
            return new PipelineResult
            {
                PipelineName = "azure",
                Status = "failed",
                Error = ex.Message,
            };
        }
    }

    // ================================================================
    // Shared helper: per-collector error tracking
    // ================================================================
    private sealed class CollectorErrorTracker
    {
        private int _errorCount;
        public bool HadError => Volatile.Read(ref _errorCount) > 0;
        public void MarkError() => Interlocked.Increment(ref _errorCount);
    }

    // ================================================================
    // Collectors
    // ================================================================

    /// <summary>
    /// Inventory every resource in the subscription. Seeds the minimal
    /// <see cref="AzureResourceRow"/> rows that type-specific collectors
    /// enrich. Follows ARM nextLink paging up to
    /// <see cref="ResourceListMaxPages"/>.
    /// </summary>
    private static async Task CollectResources(
        HttpClient http, string subId, AzureInsights ins,
        CollectorErrorTracker err, object insLock,
        ILogger log, CancellationToken ct)
    {
        try
        {
            var subInsight = new AzureSubscriptionInsight { SubscriptionId = subId };

            string? url = $"{ArmBase}/subscriptions/{subId}/resources?api-version=2022-12-01";
            int pages = 0;

            while (!string.IsNullOrEmpty(url) && pages < ResourceListMaxPages)
            {
                pages++;
                using var resp = await http.GetAsync(url, ct);
                if (!HandleResponse(resp, $"resources[{subId}]", err, log)) return;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;

                if (root.TryGetProperty("value", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in items.EnumerateArray())
                    {
                        var id = r.TryGetProperty("id", out var i) ? i.GetString() : null;
                        var name = r.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var type = r.TryGetProperty("type", out var t) ? t.GetString() : null;
                        var location = r.TryGetProperty("location", out var loc) ? loc.GetString() : null;
                        var kind = r.TryGetProperty("kind", out var k) ? k.GetString() : null;

                        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(type))
                            continue;

                        // Subscription-scoped local rollup (not shared until
                        // we acquire the lock below).
                        subInsight.ResourceCount++;
                        if (!string.IsNullOrEmpty(location))
                        {
                            subInsight.LocationBreakdown.TryGetValue(location!, out var c);
                            subInsight.LocationBreakdown[location!] = c + 1;
                        }

                        lock (insLock)
                        {
                            ins.ResourcesTotal++;
                            IncrementTypeCounter(ins, type!);
                            ins.Resources.Add(new AzureResourceRow
                            {
                                SubscriptionId = subId,
                                ResourceType = type!,
                                ResourceId = id!,
                                Name = name,
                                Location = location,
                                Kind = kind,
                            });
                        }
                    }
                }

                url = root.TryGetProperty("nextLink", out var nl) ? nl.GetString() : null;
            }

            lock (insLock)
            {
                ins.Subscriptions.Add(subInsight);
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Azure resources collection failed for subscription {SubId}", subId);
        }
    }

    /// <summary>
    /// Defender for Cloud: security assessments + ascScore secure-score.
    /// </summary>
    private static async Task CollectDefenderAssessments(
        HttpClient http, string subId, AzureInsights ins,
        CollectorErrorTracker err, object insLock,
        ILogger log, CancellationToken ct)
    {
        // --- Assessments ---------------------------------------------------
        int subUnhealthy = 0;
        int healthy = 0, unhealthy = 0, notApplicable = 0;

        try
        {
            string? url = $"{ArmBase}/subscriptions/{subId}/providers/Microsoft.Security/assessments?api-version=2021-06-01";
            int pages = 0;

            while (!string.IsNullOrEmpty(url) && pages < ResourceListMaxPages)
            {
                pages++;
                using var resp = await http.GetAsync(url, ct);
                if (!HandleResponse(resp, $"assessments[{subId}]", err, log)) break;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;

                if (root.TryGetProperty("value", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in items.EnumerateArray())
                    {
                        var code = a.TryGetProperty("properties", out var p)
                            && p.TryGetProperty("status", out var s)
                            && s.TryGetProperty("code", out var c)
                                ? c.GetString()?.ToLowerInvariant() ?? ""
                                : "";
                        switch (code)
                        {
                            case "healthy": healthy++; break;
                            case "unhealthy": unhealthy++; subUnhealthy++; break;
                            case "notapplicable": notApplicable++; break;
                        }
                    }
                }

                url = root.TryGetProperty("nextLink", out var nl) ? nl.GetString() : null;
            }

            lock (insLock)
            {
                ins.AssessmentsHealthy += healthy;
                ins.AssessmentsUnhealthy += unhealthy;
                ins.AssessmentsNotApplicable += notApplicable;

                if (subUnhealthy > 0)
                {
                    var existing = ins.Subscriptions.Find(x =>
                        string.Equals(x.SubscriptionId, subId, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null)
                    {
                        existing.UnhealthyAssessments += subUnhealthy;
                    }
                    else
                    {
                        ins.Subscriptions.Add(new AzureSubscriptionInsight
                        {
                            SubscriptionId = subId,
                            UnhealthyAssessments = subUnhealthy,
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Azure Defender assessments collection failed for {SubId}", subId);
        }

        // --- Secure score (ascScore) — 404 is tolerated. -------------------
        try
        {
            var url = $"{ArmBase}/subscriptions/{subId}/providers/Microsoft.Security/secureScores/ascScore?api-version=2020-01-01";
            using var resp = await http.GetAsync(url, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                log.LogInformation("Azure secure score not available for {SubId} (404)", subId);
                return;
            }
            if (!HandleResponse(resp, $"secureScore[{subId}]", err, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("properties", out var props)
                && props.TryGetProperty("score", out var scoreObj))
            {
                decimal? pct = null;

                // Newer API: properties.score.percentage (0..100).
                if (scoreObj.ValueKind == JsonValueKind.Object
                    && scoreObj.TryGetProperty("percentage", out var percentEl)
                    && percentEl.ValueKind == JsonValueKind.Number
                    && percentEl.TryGetDecimal(out var percent))
                {
                    pct = percent;
                }
                // Older API: current / max.
                else if (scoreObj.ValueKind == JsonValueKind.Object
                    && scoreObj.TryGetProperty("current", out var curEl)
                    && scoreObj.TryGetProperty("max", out var maxEl)
                    && curEl.TryGetDecimal(out var cur)
                    && maxEl.TryGetDecimal(out var max)
                    && max > 0)
                {
                    pct = Math.Round(cur / max * 100m, 2);
                }

                if (pct.HasValue)
                {
                    lock (insLock)
                    {
                        // There is one ascScore per subscription. Across
                        // subs, keep the max observed as a coarse rollup.
                        if (!ins.SecureScorePct.HasValue || pct.Value > ins.SecureScorePct.Value)
                            ins.SecureScorePct = pct;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Azure secure score collection failed for {SubId}", subId);
        }
    }

    private static async Task CollectPublicIps(
        HttpClient http, string subId, AzureInsights ins,
        CollectorErrorTracker err, object insLock,
        ILogger log, CancellationToken ct)
    {
        try
        {
            var url = $"{ArmBase}/subscriptions/{subId}/providers/Microsoft.Network/publicIPAddresses?api-version=2023-09-01";
            using var resp = await http.GetAsync(url, ct);
            if (!HandleResponse(resp, $"publicIps[{subId}]", err, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)
                || items.ValueKind != JsonValueKind.Array) return;

            var count = items.GetArrayLength();
            lock (insLock)
            {
                ins.PublicIpCount += count;
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Azure public IPs collection failed for {SubId}", subId);
        }
    }

    /// <summary>
    /// Network Security Groups — detects the classic "any/any/allow inbound"
    /// footgun that exposes everything to the Internet.
    /// </summary>
    private static async Task CollectNsgs(
        HttpClient http, string subId, AzureInsights ins,
        CollectorErrorTracker err, object insLock,
        ILogger log, CancellationToken ct)
    {
        try
        {
            var url = $"{ArmBase}/subscriptions/{subId}/providers/Microsoft.Network/networkSecurityGroups?api-version=2023-09-01";
            using var resp = await http.GetAsync(url, ct);
            if (!HandleResponse(resp, $"nsgs[{subId}]", err, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)
                || items.ValueKind != JsonValueKind.Array) return;

            foreach (var nsg in items.EnumerateArray())
            {
                var nsgId = nsg.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(nsgId)) continue;

                bool anyAnyAllow = false;
                if (nsg.TryGetProperty("properties", out var props)
                    && props.TryGetProperty("securityRules", out var rules)
                    && rules.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rule in rules.EnumerateArray())
                    {
                        if (!rule.TryGetProperty("properties", out var rp)) continue;
                        var access = rp.TryGetProperty("access", out var a) ? a.GetString() : null;
                        var direction = rp.TryGetProperty("direction", out var d) ? d.GetString() : null;
                        var srcPrefix = rp.TryGetProperty("sourceAddressPrefix", out var sp) ? sp.GetString() : null;
                        var dstPort = rp.TryGetProperty("destinationPortRange", out var dp) ? dp.GetString() : null;

                        if (string.Equals(access, "Allow", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(direction, "Inbound", StringComparison.OrdinalIgnoreCase)
                            && IsAnySource(srcPrefix)
                            && string.Equals(dstPort, "*", StringComparison.OrdinalIgnoreCase))
                        {
                            anyAnyAllow = true;
                            break;
                        }
                    }
                }

                if (anyAnyAllow)
                {
                    lock (insLock)
                    {
                        ins.NsgAnyAnyAllowRules++;
                        MergeResourceRowLocked(ins, nsgId!, riskFlag: "any_any_allow");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Azure NSG collection failed for {SubId}", subId);
        }
    }

    private static async Task CollectStorageAccounts(
        HttpClient http, string subId, AzureInsights ins,
        CollectorErrorTracker err, object insLock,
        ILogger log, CancellationToken ct)
    {
        try
        {
            var url = $"{ArmBase}/subscriptions/{subId}/providers/Microsoft.Storage/storageAccounts?api-version=2023-01-01";
            using var resp = await http.GetAsync(url, ct);
            if (!HandleResponse(resp, $"storage[{subId}]", err, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)
                || items.ValueKind != JsonValueKind.Array) return;

            foreach (var sa in items.EnumerateArray())
            {
                var saId = sa.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(saId)) continue;

                bool publicBlob = false, httpEnabled = false, noSoftDelete = false;

                if (sa.TryGetProperty("properties", out var props))
                {
                    publicBlob = props.TryGetProperty("allowBlobPublicAccess", out var apba)
                        && apba.ValueKind == JsonValueKind.True;

                    // HTTPS-only flag inverted: absent or false ⇒ http allowed.
                    httpEnabled = !(props.TryGetProperty("supportsHttpsTrafficOnly", out var https)
                        && https.ValueKind == JsonValueKind.True);

                    // deleteRetentionPolicy.enabled — missing or false ⇒ flag.
                    bool softDeleteOn = false;
                    if (props.TryGetProperty("deleteRetentionPolicy", out var drp)
                        && drp.ValueKind == JsonValueKind.Object
                        && drp.TryGetProperty("enabled", out var en)
                        && en.ValueKind == JsonValueKind.True)
                    {
                        softDeleteOn = true;
                    }
                    noSoftDelete = !softDeleteOn;
                }

                if (!publicBlob && !httpEnabled && !noSoftDelete) continue;

                var flags = new List<string>();
                if (publicBlob) flags.Add("public_blob");
                if (httpEnabled) flags.Add("http_enabled");
                if (noSoftDelete) flags.Add("no_soft_delete");

                var properties = JsonSerializer.Serialize(new
                {
                    allowBlobPublicAccess = publicBlob,
                    supportsHttpsTrafficOnly = !httpEnabled,
                    deleteRetentionPolicyEnabled = !noSoftDelete,
                });

                lock (insLock)
                {
                    if (publicBlob) ins.StorageAccountsPublicBlob++;
                    if (httpEnabled) ins.StorageAccountsHttpEnabled++;
                    if (noSoftDelete) ins.StorageAccountsNoSoftDelete++;
                    MergeResourceRowLocked(ins, saId!, properties, flags);
                }
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Azure storage accounts collection failed for {SubId}", subId);
        }
    }

    private static async Task CollectKeyVaults(
        HttpClient http, string subId, AzureInsights ins,
        CollectorErrorTracker err, object insLock,
        ILogger log, CancellationToken ct)
    {
        try
        {
            var url = $"{ArmBase}/subscriptions/{subId}/providers/Microsoft.KeyVault/vaults?api-version=2023-07-01";
            using var resp = await http.GetAsync(url, ct);
            if (!HandleResponse(resp, $"keyvaults[{subId}]", err, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)
                || items.ValueKind != JsonValueKind.Array) return;

            foreach (var kv in items.EnumerateArray())
            {
                var kvId = kv.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(kvId)) continue;

                bool softDeleteOn = false, purgeProtectionOn = false;
                if (kv.TryGetProperty("properties", out var props))
                {
                    softDeleteOn = props.TryGetProperty("enableSoftDelete", out var sd)
                        && sd.ValueKind == JsonValueKind.True;
                    purgeProtectionOn = props.TryGetProperty("enablePurgeProtection", out var pp)
                        && pp.ValueKind == JsonValueKind.True;
                }

                var noSoftDelete = !softDeleteOn;
                var noPurgeProtection = !purgeProtectionOn;

                if (!noSoftDelete && !noPurgeProtection) continue;

                var flags = new List<string>();
                if (noSoftDelete) flags.Add("no_soft_delete");
                if (noPurgeProtection) flags.Add("no_purge_protection");

                var properties = JsonSerializer.Serialize(new
                {
                    enableSoftDelete = softDeleteOn,
                    enablePurgeProtection = purgeProtectionOn,
                });

                lock (insLock)
                {
                    if (noSoftDelete) ins.KeyVaultsNoSoftDelete++;
                    if (noPurgeProtection) ins.KeyVaultsNoPurgeProtection++;
                    MergeResourceRowLocked(ins, kvId!, properties, flags);
                }
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Azure Key Vault collection failed for {SubId}", subId);
        }
    }

    private static async Task CollectVirtualMachines(
        HttpClient http, string subId, AzureInsights ins,
        CollectorErrorTracker err, object insLock,
        ILogger log, CancellationToken ct)
    {
        try
        {
            var url = $"{ArmBase}/subscriptions/{subId}/providers/Microsoft.Compute/virtualMachines?api-version=2023-09-01";
            using var resp = await http.GetAsync(url, ct);
            if (!HandleResponse(resp, $"vms[{subId}]", err, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)
                || items.ValueKind != JsonValueKind.Array) return;

            foreach (var vm in items.EnumerateArray())
            {
                var vmId = vm.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(vmId)) continue;

                // --- OS disk encryption -----------------------------------
                bool osDiskUnencrypted = false;
                if (vm.TryGetProperty("properties", out var props)
                    && props.TryGetProperty("storageProfile", out var sp)
                    && sp.TryGetProperty("osDisk", out var osDisk))
                {
                    bool encSettingsEnabled = osDisk.TryGetProperty("encryptionSettings", out var es)
                        && es.TryGetProperty("enabled", out var esEnabled)
                        && esEnabled.ValueKind == JsonValueKind.True;

                    bool hasDes = osDisk.TryGetProperty("managedDisk", out var md)
                        && md.TryGetProperty("diskEncryptionSet", out var des)
                        && des.ValueKind == JsonValueKind.Object;

                    osDiskUnencrypted = !encSettingsEnabled && !hasDes;
                }

                // --- Managed identity -------------------------------------
                bool noManagedIdentity;
                if (vm.TryGetProperty("identity", out var identity)
                    && identity.ValueKind == JsonValueKind.Object
                    && identity.TryGetProperty("type", out var idType)
                    && !string.Equals(idType.GetString(), "None", StringComparison.OrdinalIgnoreCase))
                {
                    noManagedIdentity = false;
                }
                else
                {
                    noManagedIdentity = true;
                }

                if (!osDiskUnencrypted && !noManagedIdentity) continue;

                var flags = new List<string>();
                if (osDiskUnencrypted) flags.Add("os_disk_unencrypted");
                if (noManagedIdentity) flags.Add("no_managed_identity");

                var properties = JsonSerializer.Serialize(new
                {
                    osDiskUnencrypted,
                    noManagedIdentity,
                });

                lock (insLock)
                {
                    if (osDiskUnencrypted) ins.VmsUnencryptedOsDisk++;
                    if (noManagedIdentity) ins.VmsWithoutManagedIdentity++;
                    MergeResourceRowLocked(ins, vmId!, properties, flags);
                }
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Azure VMs collection failed for {SubId}", subId);
        }
    }

    /// <summary>
    /// Azure Policy compliance summary — optional. 403 / 404 are treated
    /// as "policy insights not available" and do NOT mark the collector
    /// errored.
    /// </summary>
    private static async Task CollectPolicyCompliance(
        HttpClient http, string subId, AzureInsights ins,
        CollectorErrorTracker err, object insLock,
        ILogger log, CancellationToken ct)
    {
        try
        {
            var url = $"{ArmBase}/subscriptions/{subId}/providers/Microsoft.PolicyInsights/policyStates/latest/summarize?api-version=2019-10-01";
            using var resp = await http.PostAsJsonAsync(url, new { }, ct);

            if (resp.StatusCode == HttpStatusCode.Forbidden
                || resp.StatusCode == HttpStatusCode.NotFound)
            {
                log.LogInformation(
                    "Azure policy compliance skipped for {SubId}: HTTP {Status}",
                    subId, (int)resp.StatusCode);
                return;
            }
            if (!HandleResponse(resp, $"policyCompliance[{subId}]", err, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            // Shape: { "value": [ { "results": { "nonCompliantResources": N } } ] }
            if (!root.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return;

            int total = 0;
            foreach (var entry in arr.EnumerateArray())
            {
                if (entry.TryGetProperty("results", out var results)
                    && results.TryGetProperty("nonCompliantResources", out var ncr)
                    && ncr.ValueKind == JsonValueKind.Number
                    && ncr.TryGetInt32(out var count))
                {
                    total += count;
                }
            }

            lock (insLock)
            {
                ins.PolicyNonCompliantResources =
                    (ins.PolicyNonCompliantResources ?? 0) + total;
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Azure policy compliance collection failed for {SubId}", subId);
        }
    }

    // ================================================================
    // Helpers
    // ================================================================

    /// <summary>
    /// Common HTTP-response triage. 200 → process; 403 / 404 → log at
    /// Warning and do NOT process (non-fatal — caller lacks permission or
    /// RP not registered). Any other non-success bumps the error tracker.
    /// </summary>
    private static bool HandleResponse(
        HttpResponseMessage resp, string endpoint,
        CollectorErrorTracker err, ILogger log)
    {
        if (resp.StatusCode == HttpStatusCode.OK) return true;

        if (resp.StatusCode == HttpStatusCode.Forbidden)
        {
            log.LogWarning(
                "Azure ARM ({Endpoint}): 403 - insufficient permissions (non-fatal)",
                endpoint);
            return false;
        }
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            log.LogWarning(
                "Azure ARM ({Endpoint}): 404 - provider not registered or not available",
                endpoint);
            return false;
        }

        err.MarkError();
        log.LogWarning("Azure ARM ({Endpoint}): HTTP {StatusCode}",
            endpoint, (int)resp.StatusCode);
        return false;
    }

    private static bool IsAnySource(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return false;
        return prefix is "*"
            || prefix.Equals("Any", StringComparison.OrdinalIgnoreCase)
            || prefix.Equals("0.0.0.0/0", StringComparison.Ordinal)
            || prefix.Equals("Internet", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bumps the per-type resource counter. Only the types the pipeline
    /// reports in the metrics dictionary are tracked — everything else
    /// still contributes to <see cref="AzureInsights.ResourcesTotal"/>.
    /// Caller must hold the insights lock.
    /// </summary>
    private static void IncrementTypeCounter(AzureInsights ins, string type)
    {
        if (type.Equals(TypeVm, StringComparison.OrdinalIgnoreCase)) ins.VmsCount++;
        else if (type.Equals(TypeStorage, StringComparison.OrdinalIgnoreCase)) ins.StorageAccountsCount++;
        else if (type.Equals(TypeKeyVault, StringComparison.OrdinalIgnoreCase)) ins.KeyVaultsCount++;
        else if (type.Equals(TypeNsg, StringComparison.OrdinalIgnoreCase)) ins.NsgCount++;
        else if (type.Equals(TypeSql, StringComparison.OrdinalIgnoreCase)) ins.SqlDatabasesCount++;
        // Public IPs are counted directly via CollectPublicIps — avoid double count.
        _ = TypePublicIp;
    }

    /// <summary>
    /// Merges <paramref name="properties"/> / risk flags into the existing
    /// <see cref="AzureResourceRow"/> for <paramref name="resourceId"/>.
    /// If no row exists (e.g. resource was paginated past the initial
    /// inventory cut-off) a minimal row is created so the finding isn't
    /// lost. Caller MUST hold the insights lock.
    /// </summary>
    private static void MergeResourceRowLocked(
        AzureInsights ins, string resourceId,
        string? properties = null, IEnumerable<string>? riskFlags = null,
        string? riskFlag = null)
    {
        var row = ins.Resources.Find(r =>
            string.Equals(r.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));

        if (row is null)
        {
            row = new AzureResourceRow { ResourceId = resourceId };
            ins.Resources.Add(row);
        }

        if (!string.IsNullOrEmpty(properties))
            row.PropertiesJson = properties;

        if (riskFlag is not null && !row.RiskFlags.Contains(riskFlag))
            row.RiskFlags.Add(riskFlag);

        if (riskFlags is not null)
        {
            foreach (var f in riskFlags)
            {
                if (!row.RiskFlags.Contains(f))
                    row.RiskFlags.Add(f);
            }
        }
    }
}
