using System.Globalization;
using System.Net;
using System.Text.Json;
using KryossApi.Services.CloudAssessment.Recommendations;
using KryossApi.Services.CopilotReadiness.Pipelines;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// Cloud Assessment (CA-3) data-protection pipeline.
///
/// Aggregates Purview, SharePoint and OneDrive signals via six parallel
/// collectors:
///
///   * Purview licensing (Graph SubscribedSkus): AIP P1/P2, DLP,
///     Insider Risk, eDiscovery, Advanced Audit.
///   * Sensitivity labels (Graph beta /security/informationProtection
///     /sensitivityLabels — skipped when no beta HTTP client is supplied).
///   * SharePoint site enumeration with per-site file paging: label
///     coverage + org-wide oversharing risk. Throttled via
///     <c>SemaphoreSlim(5)</c>, capped at 50 sites / 500 files each.
///   * External (guest) users + recent sign-in activity.
///   * Defender for Cloud Apps / Office 365 DLP alerts last 30 days.
///   * OneDrive usage (Reports API CSV) — active accounts and storage
///     footprint.
///
/// Follows the same resiliency contract as <see cref="IdentityPipeline"/>
/// and <see cref="EndpointPipeline"/>: each collector catches its own
/// exceptions and marks the shared <c>CollectorErrorTracker</c> when a
/// failure is license-agnostic. DLP alert 403 and OneDrive 403/404 are
/// treated as "not licensed" signals — they do NOT mark the pipeline
/// errored. Findings generation is deferred to <c>DataRecommendations</c>
/// (Task 2).
/// </summary>
public static class DataPipeline
{
    private const int MaxSitesPerRun  = 50;
    private const int MaxFilesPerSite = 500;
    private const int FilesPageSize   = 200;
    private const int SiteConcurrency = 5;

    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph,
        HttpClient? graphBetaHttp,
        ILogger log,
        CancellationToken ct)
    {
        var result = new PipelineResult { PipelineName = "data", Status = "ok" };
        var ins = new DataInsights();
        var err = new CollectorErrorTracker();

        try
        {
            var tasks = new List<Task>
            {
                CollectPurviewLicensing(graph, ins, err, log, ct),
                CollectSensitivityLabels(graphBetaHttp, ins, err, log, ct),
                CollectSharePointSites(graph, ins, err, log, ct),
                CollectExternalUsers(graph, ins, err, log, ct),
                CollectDlpAlerts(graph, ins, err, log, ct),
                CollectOneDriveUsage(graph, ins, err, log, ct),
            };

            await Task.WhenAll(tasks);

            result.Findings.AddRange(DataRecommendations.Generate(ins));

            // Metrics (string-valued, snake_case).
            var m = result.Metrics;
            var inv = CultureInfo.InvariantCulture;

            // Purview licensing
            m["aip_p1_licensed"] = ins.AipP1Licensed ? "true" : "false";
            m["aip_p2_licensed"] = ins.AipP2Licensed ? "true" : "false";
            m["dlp_licensed"] = ins.DlpLicensed ? "true" : "false";
            m["insider_risk_licensed"] = ins.InsiderRiskLicensed ? "true" : "false";
            m["ediscovery_licensed"] = ins.EDiscoveryLicensed ? "true" : "false";
            m["advanced_audit_licensed"] = ins.AdvancedAuditLicensed ? "true" : "false";

            // Labels
            m["sensitivity_label_count"] = ins.SensitivityLabelCount.ToString(inv);
            m["label_policies_count"] = ins.LabelPoliciesCount.ToString(inv);

            // SharePoint
            m["total_sites_scanned"] = ins.TotalSitesScanned.ToString(inv);
            m["total_files_scanned"] = ins.TotalFilesScanned.ToString(inv);
            m["labeled_files"] = ins.LabeledFiles.ToString(inv);
            m["unlabeled_files"] = ins.UnlabeledFiles.ToString(inv);
            m["label_coverage_pct"] = ins.LabelCoveragePct.ToString("F1", inv);
            m["overshared_files"] = ins.OversharedFiles.ToString(inv);
            m["overshared_pct"] = ins.OversharedPct.ToString("F1", inv);
            m["high_risk_sites"] = ins.HighRiskSites.ToString(inv);

            // External users
            m["total_guests"] = ins.TotalGuests.ToString(inv);
            m["guests_with_recent_activity"] = ins.GuestsWithRecentActivity.ToString(inv);
            m["guests_with_site_access"] = ins.GuestsWithSiteAccess.ToString(inv);

            // DLP
            m["dlp_alerts_last_30d"] = ins.DlpAlertsLast30d.ToString(inv);
            m["dlp_incident_count"] = ins.DlpIncidentCount.ToString(inv);

            // OneDrive
            m["onedrive_active_accounts"] = ins.OneDriveActiveAccounts.ToString(inv);
            m["onedrive_total_gb"] = ins.OneDriveTotalGB.ToString("F1", inv);
            m["onedrive_avg_gb_per_user"] = ins.OneDriveAvgGBPerUser.ToString("F1", inv);

            // Availability
            m["purview_available"] = ins.PurviewAvailable ? "true" : "false";
            m["sharepoint_available"] = ins.SharePointAvailable ? "true" : "false";
            m["onedrive_available"] = ins.OneDriveAvailable ? "true" : "false";
            m["sampled"] = ins.Sampled ? "true" : "false";

            // Final status: partial when any collector errored or no surface responded.
            if (err.HadError || !ins.Available)
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Data pipeline top-level failure");
            return new PipelineResult
            {
                PipelineName = "data",
                Status = "failed",
                Error = ex.Message,
            };
        }
    }

    // ================================================================
    // Helper: per-collector error tracking
    // ================================================================
    private sealed class CollectorErrorTracker
    {
        private int _errorCount;
        public bool HadError => Volatile.Read(ref _errorCount) > 0;
        public void MarkError() => Interlocked.Increment(ref _errorCount);
    }

    // ================================================================
    // 1. Purview licensing — Graph SubscribedSkus
    // ================================================================
    private static async Task CollectPurviewLicensing(
        GraphServiceClient graph, DataInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.SubscribedSkus.GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;

            ins.PurviewAvailable = true;

            foreach (var sku in resp.Value)
            {
                if (sku.ServicePlans is null) continue;
                foreach (var plan in sku.ServicePlans)
                {
                    var name = plan.ServicePlanName;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var status = plan.ProvisioningStatus ?? "";
                    if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (name.Equals("AIP_P1", StringComparison.OrdinalIgnoreCase))
                        ins.AipP1Licensed = true;
                    else if (name.Equals("AIP_P2", StringComparison.OrdinalIgnoreCase))
                        ins.AipP2Licensed = true;
                    else if (name.Equals("MICROSOFTENDPOINTDLP", StringComparison.OrdinalIgnoreCase)
                          || name.Equals("MIP_S_Exchange", StringComparison.OrdinalIgnoreCase))
                        ins.DlpLicensed = true;
                    else if (name.Equals("INSIDER_RISK_MANAGEMENT", StringComparison.OrdinalIgnoreCase)
                          || name.Equals("INSIDER_RISK", StringComparison.OrdinalIgnoreCase))
                        ins.InsiderRiskLicensed = true;
                    else if (name.Equals("EDISCOVERY", StringComparison.OrdinalIgnoreCase)
                          || name.Equals("PURVIEW_DISCOVERY", StringComparison.OrdinalIgnoreCase))
                        ins.EDiscoveryLicensed = true;
                    else if (name.Equals("M365_ADVANCED_AUDITING", StringComparison.OrdinalIgnoreCase))
                        ins.AdvancedAuditLicensed = true;
                }
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Data Purview licensing: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Data Purview licensing collection failed");
        }
    }

    // ================================================================
    // 2. Sensitivity labels — Graph beta only
    //    Graph SDK v5 stable does not expose sensitivity labels; use the
    //    beta HTTP client if present. Label policies endpoint is not
    //    reliably exposed in beta either — stays 0 with a TODO.
    // ================================================================
    private static async Task CollectSensitivityLabels(
        HttpClient? http, DataInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        if (http is null)
        {
            log.LogInformation(
                "Data sensitivity labels: skipped — no Graph beta HTTP client supplied");
            return;
        }

        try
        {
            using var resp = await http.GetAsync(
                "/beta/security/informationProtection/sensitivityLabels", ct);

            if (resp.StatusCode == HttpStatusCode.Forbidden)
            {
                log.LogInformation(
                    "Data sensitivity labels: 403 - InformationProtectionPolicy.Read.All required");
                return;
            }
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                log.LogInformation(
                    "Data sensitivity labels: 404 - endpoint not available in this tenant");
                return;
            }
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning(
                    "Data sensitivity labels: HTTP {StatusCode}", (int)resp.StatusCode);
                return;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("value", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                ins.SensitivityLabelCount = items.GetArrayLength();
                ins.PurviewAvailable = true;
            }

            // TODO CA-3+: label policies — /beta/security/informationProtection/labelPolicies
            // is not reliably exposed; leave LabelPoliciesCount = 0 until SDK gains typed support.
            log.LogInformation(
                "Data label policies: skipped — endpoint not exposed in stable beta");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Data sensitivity labels collection failed");
        }
    }

    // ================================================================
    // 3. SharePoint sites — label coverage + oversharing
    //    Cap at MaxSitesPerRun sites × MaxFilesPerSite files each, with
    //    SemaphoreSlim(SiteConcurrency) throttling per-site processing.
    // ================================================================
    private static async Task CollectSharePointSites(
        GraphServiceClient graph, DataInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var sitesResp = await graph.Sites.GetAsync(rc =>
            {
                rc.QueryParameters.Top = MaxSitesPerRun;
                rc.QueryParameters.Select = new[] { "id", "displayName", "webUrl" };
            }, cancellationToken: ct);

            var sites = sitesResp?.Value;
            if (sites is null || sites.Count == 0) return;

            ins.SharePointAvailable = true;

            if (sites.Count >= MaxSitesPerRun)
                ins.Sampled = true;

            var sem = new SemaphoreSlim(SiteConcurrency);
            var tasks = sites
                .Select(site => ProcessSiteAsync(graph, site, sem, log, ct))
                .ToList();

            var siteOutputs = await Task.WhenAll(tasks);

            int processed = 0;
            foreach (var (siteData, hitCap) in siteOutputs)
            {
                if (siteData is not { } d) continue;
                processed++;

                ins.TotalFilesScanned += d.TotalFiles;
                ins.LabeledFiles      += d.LabeledFiles;
                ins.UnlabeledFiles    += d.UnlabeledFiles;
                ins.OversharedFiles   += d.OversharedFiles;

                double siteOversharedPct = d.TotalFiles > 0
                    ? d.OversharedFiles * 100.0 / d.TotalFiles
                    : 0;
                if (d.OversharedFiles > 50 || siteOversharedPct > 30)
                    ins.HighRiskSites++;

                if (hitCap) ins.Sampled = true;
            }

            ins.TotalSitesScanned = processed;
            ins.LabelCoveragePct = ins.TotalFilesScanned > 0
                ? ins.LabeledFiles * 100.0 / ins.TotalFilesScanned
                : 0;
            ins.OversharedPct = ins.TotalFilesScanned > 0
                ? ins.OversharedFiles * 100.0 / ins.TotalFilesScanned
                : 0;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Data SharePoint sites: 403 - insufficient permissions");
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            log.LogInformation("Data SharePoint sites: 404 - no sites available (non-fatal)");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Data SharePoint sites collection failed");
        }
    }

    private readonly record struct SiteScanData(
        int TotalFiles,
        int LabeledFiles,
        int UnlabeledFiles,
        int OversharedFiles);

    private static async Task<(SiteScanData? data, bool hitCap)> ProcessSiteAsync(
        GraphServiceClient graph,
        Site site,
        SemaphoreSlim sem,
        ILogger log,
        CancellationToken ct)
    {
        await sem.WaitAsync(ct);
        try
        {
            var siteId = site.Id ?? "";
            if (string.IsNullOrEmpty(siteId))
                return (null, false);

            // Find the default document library drive.
            string? driveId = null;
            try
            {
                var drivesResp = await graph.Sites[siteId].Drives.GetAsync(cancellationToken: ct);
                var docLib = drivesResp?.Value?.FirstOrDefault(d =>
                    string.Equals(d.DriveType, "documentLibrary", StringComparison.OrdinalIgnoreCase));
                driveId = docLib?.Id;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
            {
                log.LogWarning("Data site {SiteId}: cannot access drives ({Code})",
                    siteId, ex.ResponseStatusCode);
                return (null, false);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Data site {SiteId}: drives enumeration failed", siteId);
                return (null, false);
            }

            if (driveId is null) return (null, false);

            int totalFiles = 0;
            int labeledFiles = 0;
            int unlabeledFiles = 0;
            int oversharedFiles = 0;
            bool hitCap = false;

            try
            {
                string? nextLink = null;
                bool firstPage = true;

                do
                {
                    DriveItemCollectionResponse? pageResp;

                    if (firstPage)
                    {
                        pageResp = await graph.Drives[driveId].Items["root"].Children.GetAsync(rc =>
                        {
                            rc.QueryParameters.Top = FilesPageSize;
                            rc.QueryParameters.Select = new[]
                            {
                                "id", "name", "file", "sensitivityLabel", "shared"
                            };
                        }, cancellationToken: ct);
                        firstPage = false;
                    }
                    else
                    {
                        var reqInfo = new Microsoft.Kiota.Abstractions.RequestInformation
                        {
                            HttpMethod = Microsoft.Kiota.Abstractions.Method.GET,
                            URI = new Uri(nextLink!),
                        };
                        pageResp = await graph.RequestAdapter.SendAsync(
                            reqInfo,
                            DriveItemCollectionResponse.CreateFromDiscriminatorValue,
                            cancellationToken: ct);
                    }

                    nextLink = pageResp?.OdataNextLink;
                    var items = pageResp?.Value;
                    if (items is null) break;

                    foreach (var item in items)
                    {
                        // Only process files (not folders).
                        if (item.File is null) continue;

                        totalFiles++;

                        // Sensitivity label presence via AdditionalData.
                        bool hasLabel = false;
                        if (item.AdditionalData?.TryGetValue("sensitivityLabel", out var labelObj) == true
                            && labelObj is not null)
                        {
                            hasLabel = true;
                        }
                        if (hasLabel) labeledFiles++;
                        else unlabeledFiles++;

                        // Oversharing via the shared facet.
                        if (item.Shared is not null)
                        {
                            var scope = item.Shared.Scope?.ToLowerInvariant() ?? "";
                            if (scope == "organization" || scope == "public")
                                oversharedFiles++;
                        }
                    }

                    if (totalFiles >= MaxFilesPerSite)
                    {
                        hitCap = true;
                        break;
                    }
                }
                while (!string.IsNullOrEmpty(nextLink));
            }
            catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
            {
                log.LogWarning("Data site {SiteId}: cannot list files ({Code})",
                    siteId, ex.ResponseStatusCode);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Data site {SiteId}: file listing failed", siteId);
            }

            return (new SiteScanData(totalFiles, labeledFiles, unlabeledFiles, oversharedFiles), hitCap);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Data site {SiteId}: unexpected error during processing", site.Id);
            return (null, false);
        }
        finally
        {
            sem.Release();
        }
    }

    // ================================================================
    // 4. External / guest users
    // ================================================================
    private static async Task CollectExternalUsers(
        GraphServiceClient graph, DataInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = "userType eq 'Guest'";
                rc.QueryParameters.Select = new[]
                {
                    "id", "displayName", "userPrincipalName",
                    "createdDateTime", "signInActivity",
                };
                rc.QueryParameters.Top = 999;
            }, cancellationToken: ct);

            var guests = resp?.Value;
            if (guests is null) return;

            ins.TotalGuests = guests.Count;

            var now = DateTimeOffset.UtcNow;
            foreach (var g in guests)
            {
                if (g.SignInActivity?.LastSignInDateTime is { } lastSignIn
                    && (now - lastSignIn).TotalDays <= 90)
                {
                    ins.GuestsWithRecentActivity++;
                }
            }

            // Per-user site enumeration is too expensive (N×M Graph calls across all
            // sites — see SharePointDeepPipeline). GuestsWithSiteAccess is left at 0
            // for CA-3; aggregate oversharing is already captured by HighRiskSites.
            ins.GuestsWithSiteAccess = 0;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Data external users: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Data external users collection failed");
        }
    }

    // ================================================================
    // 5. DLP alerts — Graph /security/alerts_v2 filtered to DLP/compliance
    //    403 here is treated as "Purview not entitled" and is NOT a pipeline
    //    error — the DLP signal is license-gated, absence is expected.
    // ================================================================
    private static async Task CollectDlpAlerts(
        GraphServiceClient graph, DataInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var thirtyDaysAgoIso = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var resp = await graph.Security.Alerts_v2.GetAsync(rc =>
            {
                rc.QueryParameters.Filter =
                    $"(serviceSource eq 'microsoftDefenderForCloudApps' or serviceSource eq 'office365') and createdDateTime ge {thirtyDaysAgoIso}";
                rc.QueryParameters.Top = 50;
            }, cancellationToken: ct);

            if (resp?.Value is null) return;

            int count = resp.Value.Count(a =>
            {
                var cat = a.Category?.ToLowerInvariant() ?? "";
                return cat.Contains("dlp")
                    || cat.Contains("data loss")
                    || cat.Contains("compliance");
            });

            ins.DlpAlertsLast30d = count;
            // DlpIncidentCount: Graph Security doesn't distinguish alerts from incidents at /alerts_v2. Kept at 0 until Defender incidents API (/incidents) is wired in a later task.
            ins.DlpIncidentCount = 0;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogInformation(
                "Data DLP alerts: 403 - Purview not entitled (non-fatal)");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Data DLP alerts collection failed");
        }
    }

    // ================================================================
    // 6. OneDrive usage — Reports API CSV (period D30)
    //    403/404 are treated as "Reports.Read.All missing or no OneDrive
    //    usage yet" — NOT a pipeline error.
    // ================================================================
    private static async Task CollectOneDriveUsage(
        GraphServiceClient graph, DataInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var stream = await graph.Reports
                .GetOneDriveUsageAccountDetailWithPeriod("D30")
                .GetAsync(cancellationToken: ct);

            if (stream is null) return;

            using var reader = new StreamReader(stream);
            var header = await reader.ReadLineAsync(ct);
            if (header is null) return;

            var cols = ParseCsvHeader(header);

            long storageBytes = 0;
            int activeAccounts = 0;
            var now = DateTimeOffset.UtcNow;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = SplitCsvLine(line);

                var storage = GetLongField(fields, cols, "Storage Used (Byte)");
                storageBytes += storage;

                var isDeleted = GetStringField(fields, cols, "Is Deleted");
                var lastActivityRaw = GetStringField(fields, cols, "Last Activity Date");

                if (!string.Equals(isDeleted, "False", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (DateTimeOffset.TryParse(
                        lastActivityRaw, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var lastActivity)
                    && (now - lastActivity).TotalDays <= 30)
                {
                    activeAccounts++;
                }
            }

            ins.OneDriveActiveAccounts = activeAccounts;
            ins.OneDriveTotalGB = storageBytes / 1024.0 / 1024.0 / 1024.0;
            ins.OneDriveAvgGBPerUser = activeAccounts > 0
                ? ins.OneDriveTotalGB / activeAccounts
                : 0;
            ins.OneDriveAvailable = true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
        {
            log.LogInformation(
                "Data OneDrive usage: {Code} - Reports.Read.All missing or no usage data (non-fatal)",
                ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Data OneDrive usage collection failed");
        }
    }

    // ================================================================
    // CSV parsing helpers (BOM-aware, quote-aware — OneDrive usage CSV
    // may contain quoted commas like "Smith, John" in display names)
    // ================================================================
    private static Dictionary<string, int> ParseCsvHeader(string header)
    {
        header = header.TrimStart('\uFEFF');
        var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fields = SplitCsvLine(header);
        for (int i = 0; i < fields.Length; i++)
            cols[fields[i]] = i;
        return cols;
    }

    /// <summary>
    /// Minimal quote-aware CSV line splitter. Treats <c>"</c> as a toggle for
    /// in-quote state and only splits on <c>,</c> when not in-quote. Trims
    /// leading/trailing quotes and spaces from each field. Returns a
    /// single empty field for null/empty input.
    /// </summary>
    private static string[] SplitCsvLine(string? line)
    {
        if (string.IsNullOrEmpty(line)) return new[] { string.Empty };

        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString().Trim().Trim('"').Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString().Trim().Trim('"').Trim());
        return result.ToArray();
    }

    private static long GetLongField(string[] fields, Dictionary<string, int> cols, string name)
    {
        if (!cols.TryGetValue(name, out var idx) || idx >= fields.Length)
            return 0;
        return long.TryParse(fields[idx], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static string GetStringField(string[] fields, Dictionary<string, int> cols, string name)
    {
        if (!cols.TryGetValue(name, out var idx) || idx >= fields.Length)
            return string.Empty;
        return fields[idx];
    }
}
