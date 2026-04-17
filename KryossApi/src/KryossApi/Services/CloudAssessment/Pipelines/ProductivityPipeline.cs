using System.Globalization;
using KryossApi.Services.CloudAssessment.Recommendations;
using KryossApi.Services.CopilotReadiness.Pipelines;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;

namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// Cloud Assessment (CA-4) productivity pipeline.
///
/// Aggregates M365 license, user, and workload-activity signals via nine
/// parallel collectors:
///
///   * Subscribed SKUs — license inventory, Copilot detection, SKU plan map.
///   * Users — totals, enabled/disabled/guest counts, Copilot assignment,
///     wasted-license detection (inactive >= 30 d).
///   * Email activity (Reports D30) — licensed count, active users, averages.
///   * Teams activity (Reports D30) — licensed count, active users, averages.
///   * SharePoint site usage (Reports D30) — site count, active sites, avg files.
///   * OneDrive usage (Reports D30) — accounts, active, total storage.
///   * Office activations — Windows / Mac / mobile activations per user.
///   * Active users daily snapshot (Reports D30) — fallback LicensedCount for
///     Exchange / Teams / SharePoint / OneDrive when detail reports are absent.
///   * Graph Connectors — external connection count.
///
/// Follows the same resiliency contract as <see cref="DataPipeline"/>:
/// each collector catches its own exceptions. Reports.* 403/404 are treated
/// as "not licensed" — they do NOT mark the pipeline errored.
/// 403 on /subscribedSkus and /users IS an error.
/// Findings generation is deferred to <c>ProductivityRecommendations</c> (T3).
/// </summary>
public static class ProductivityPipeline
{
    // Copilot SKU IDs (case-insensitive Guid comparison).
    private static readonly HashSet<string> CopilotSkuIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "c28afa23-5a37-4837-938f-7cc48d0cca5c", // Microsoft 365 Copilot
            "f2b5e97e-f677-4bb5-8127-5c3ce7b6a64e", // Microsoft 365 Copilot (User)
        };

    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph,
        HttpClient? graphBetaHttp,
        ProductivityInsights ins,
        ILogger log,
        CancellationToken ct)
    {
        var result = new PipelineResult { PipelineName = "productivity", Status = "ok" };
        var err = new CollectorErrorTracker();

        try
        {
            var tasks = new List<Task>
            {
                CollectSubscribedSkus(graph, ins, err, log, ct),
                CollectUsers(graph, ins, err, log, ct),
                CollectEmailActivity(graph, ins, err, log, ct),
                CollectTeamsActivity(graph, ins, err, log, ct),
                CollectSharePointUsage(graph, ins, err, log, ct),
                CollectOneDriveUsage(graph, ins, err, log, ct),
                CollectOfficeActivations(graph, ins, err, log, ct),
                CollectActiveUsers(graph, ins, err, log, ct),
                CollectGraphConnectors(graph, ins, err, log, ct),
            };

            await Task.WhenAll(tasks);

            // Post-processing: Copilot assignment count. CollectSubscribedSkus and
            // CollectUsers run in parallel, so the authoritative Copilot GUID set
            // is only complete after both finish. Count each user once if any of
            // their assigned SKUs is a known Copilot SKU.
            int copilotAssigned = 0;
            foreach (var skuGuids in ins.UserSkuAssignments)
            {
                for (int i = 0; i < skuGuids.Length; i++)
                {
                    if (ins.CopilotSkuGuids.Contains(skuGuids[i]))
                    {
                        copilotAssigned++;
                        break;
                    }
                }
            }
            ins.CopilotLicensesAssigned = copilotAssigned;
            ins.CopilotAdoptionPct = ins.CopilotLicensesPurchased > 0
                ? copilotAssigned * 100.0 / ins.CopilotLicensesPurchased
                : 0;

            // Post-processing: resolve wasted-license SKU friendly names now that
            // both CollectSubscribedSkus and CollectUsers have completed.
            foreach (var row in ins.WastedLicenses)
            {
                if (row.Sku is null) continue;
                if (!Guid.TryParse(row.Sku, out var skuGuid)) continue;
                if (!ins.SkuIdToPartNumber.TryGetValue(skuGuid, out var partNumber)) continue;
                row.Sku = SkuFriendlyNames.Resolve(partNumber);
            }

            // Build adoption rows (6 services).
            ins.Adoptions.Add(new ProductivityAdoptionRow
            {
                ServiceName   = "email",
                LicensedCount = ins.EmailLicensedCount,
                Active30d     = ins.EmailActive30d,
                AdoptionRate  = (decimal)Math.Round(ins.EmailAdoptionRate, 2),
            });
            ins.Adoptions.Add(new ProductivityAdoptionRow
            {
                ServiceName   = "teams",
                LicensedCount = ins.TeamsLicensedCount,
                Active30d     = ins.TeamsActive30d,
                AdoptionRate  = (decimal)Math.Round(ins.TeamsAdoptionRate, 2),
            });
            ins.Adoptions.Add(new ProductivityAdoptionRow
            {
                ServiceName   = "sharepoint",
                LicensedCount = ins.SharePointLicensedCount,
                Active30d     = ins.SharePointActive30d,
                AdoptionRate  = (decimal)Math.Round(ins.SharePointAdoptionRate, 2),
            });
            ins.Adoptions.Add(new ProductivityAdoptionRow
            {
                ServiceName   = "onedrive",
                LicensedCount = ins.OneDriveLicensedCount,
                Active30d     = ins.OneDriveActive30d,
                AdoptionRate  = (decimal)Math.Round(ins.OneDriveAdoptionRate, 2),
            });
            ins.Adoptions.Add(new ProductivityAdoptionRow
            {
                ServiceName   = "office",
                LicensedCount = ins.OfficeWindowsActivations + ins.OfficeMacActivations + ins.OfficeMobileActivations,
                Active30d     = ins.OfficeWindowsActivations + ins.OfficeMacActivations,
                AdoptionRate  = (decimal)Math.Round(ins.OfficeDesktopAdoptionRate, 2),
            });
            ins.Adoptions.Add(new ProductivityAdoptionRow
            {
                ServiceName   = "copilot",
                LicensedCount = ins.CopilotLicensesPurchased,
                Active30d     = ins.CopilotLicensesAssigned,
                AdoptionRate  = (decimal)Math.Round(ins.CopilotAdoptionPct, 2),
            });

            result.Findings.AddRange(ProductivityRecommendations.Generate(ins));

            // Metrics.
            var m   = result.Metrics;
            var inv = CultureInfo.InvariantCulture;

            // Users
            m["total_users"]    = ins.TotalUsers.ToString(inv);
            m["enabled_users"]  = ins.EnabledUsers.ToString(inv);
            m["disabled_users"] = ins.DisabledUsers.ToString(inv);
            m["guest_user_count"] = ins.GuestUserCount.ToString(inv);

            // Copilot
            m["copilot_licenses_purchased"] = ins.CopilotLicensesPurchased.ToString(inv);
            m["copilot_licenses_assigned"]  = ins.CopilotLicensesAssigned.ToString(inv);
            m["copilot_adoption_pct"]       = ins.CopilotAdoptionPct.ToString("F1", inv);

            // Email
            m["email_licensed_count"] = ins.EmailLicensedCount.ToString(inv);
            m["email_active_30d"]     = ins.EmailActive30d.ToString(inv);
            m["email_adoption_rate"]  = ins.EmailAdoptionRate.ToString("F1", inv);
            m["email_send_avg"]       = ins.EmailSendAvg.ToString("F1", inv);
            m["email_receive_avg"]    = ins.EmailReceiveAvg.ToString("F1", inv);

            // Teams
            m["teams_licensed_count"] = ins.TeamsLicensedCount.ToString(inv);
            m["teams_active_30d"]     = ins.TeamsActive30d.ToString(inv);
            m["teams_adoption_rate"]  = ins.TeamsAdoptionRate.ToString("F1", inv);
            m["teams_chat_avg"]       = ins.TeamsChatAvg.ToString("F1", inv);
            m["teams_meeting_avg"]    = ins.TeamsMeetingAvg.ToString("F1", inv);
            m["teams_call_avg"]       = ins.TeamsCallAvg.ToString("F1", inv);

            // SharePoint
            m["sharepoint_licensed_count"] = ins.SharePointLicensedCount.ToString(inv);
            m["sharepoint_active_30d"]     = ins.SharePointActive30d.ToString(inv);
            m["sharepoint_adoption_rate"]  = ins.SharePointAdoptionRate.ToString("F1", inv);
            m["sharepoint_site_count"]     = ins.SharePointSiteCount.ToString(inv);
            m["sharepoint_avg_files"]      = ins.SharePointAvgFiles.ToString(inv);

            // OneDrive
            m["onedrive_licensed_count"] = ins.OneDriveLicensedCount.ToString(inv);
            m["onedrive_active_30d"]     = ins.OneDriveActive30d.ToString(inv);
            m["onedrive_adoption_rate"]  = ins.OneDriveAdoptionRate.ToString("F1", inv);
            m["onedrive_total_gb"]       = ins.OneDriveTotalGB.ToString("F1", inv);

            // Office
            m["office_windows_activations"]     = ins.OfficeWindowsActivations.ToString(inv);
            m["office_mac_activations"]         = ins.OfficeMacActivations.ToString(inv);
            m["office_mobile_activations"]      = ins.OfficeMobileActivations.ToString(inv);
            m["office_desktop_adoption_rate"]   = ins.OfficeDesktopAdoptionRate.ToString("F1", inv);

            // Wasted
            m["wasted_license_count"]       = ins.WastedLicenseCount.ToString(inv);
            m["wasted_license_total_seats"] = ins.WastedLicenseTotalSeats.ToString(inv);
            m["estimated_annual_waste"]     = ins.EstimatedAnnualWaste.HasValue
                ? ins.EstimatedAnnualWaste.Value.ToString("F2", inv)
                : string.Empty;

            // Graph Connectors / availability
            m["graph_connectors_count"]  = ins.GraphConnectorsCount.ToString(inv);
            m["productivity_available"]  = ins.Available ? "true" : "false";

            // Final status.
            if (err.HadError || !ins.Available)
            {
                return new PipelineResult
                {
                    PipelineName = result.PipelineName,
                    Status       = "partial",
                    Findings     = result.Findings,
                    Metrics      = result.Metrics,
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
            log.LogError(ex, "Productivity pipeline top-level failure");
            return new PipelineResult
            {
                PipelineName = "productivity",
                Status       = "failed",
                Error        = ex.Message,
            };
        }
    }

    // Matches tenant-specific Copilot SKU part numbers (e.g. "Microsoft_365_Copilot",
    // "COPILOT_M365"). Excludes "Copilot Studio" — it's a different product licensed
    // separately and would inflate the Copilot adoption metric if counted here.
    private static bool IsCopilotSkuPart(string partNumber) =>
        !string.IsNullOrEmpty(partNumber)
        && partNumber.Contains("Copilot", StringComparison.OrdinalIgnoreCase)
        && !partNumber.Contains("Studio", StringComparison.OrdinalIgnoreCase);

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
    // 1. Subscribed SKUs — license inventory, Copilot detection, SKU plan map
    //    403 → mark error (SubscribedSku.Read.All is required).
    // ================================================================
    private static async Task CollectSubscribedSkus(
        GraphServiceClient graph, ProductivityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.SubscribedSkus.GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;

            int copilotPurchased = 0;

            foreach (var sku in resp.Value)
            {
                var partNumber = sku.SkuPartNumber ?? string.Empty;
                var assigned   = sku.ConsumedUnits ?? 0;
                var purchased  = sku.PrepaidUnits?.Enabled ?? 0;

                ins.Licenses.Add(new ProductivityLicenseRow
                {
                    SkuPartNumber = partNumber,
                    FriendlyName  = SkuFriendlyNames.Resolve(partNumber),
                    Purchased     = purchased,
                    Assigned      = assigned,
                    // Bundled plans (e.g. Entra P2 inside M365 E5) may assign without a
                    // direct purchase, producing negative available. Clamp to 0.
                    Available     = Math.Max(0, purchased - assigned),
                });

                // SkuId → SkuPartNumber map for wasted-license post-processing.
                if (sku.SkuId.HasValue && !string.IsNullOrEmpty(partNumber))
                    ins.SkuIdToPartNumber.TryAdd(sku.SkuId.Value, partNumber);

                // Copilot SKU detection — match by hardcoded GUID (legacy) OR
                // SkuPartNumber containing "Copilot" (excluding Copilot Studio).
                bool isCopilotById = sku.SkuId.HasValue
                    && CopilotSkuIds.Contains(sku.SkuId.Value.ToString());
                bool isCopilotByPart = IsCopilotSkuPart(partNumber);

                if (isCopilotById || isCopilotByPart)
                {
                    copilotPurchased += purchased;
                    if (sku.SkuId.HasValue)
                        ins.CopilotSkuGuids.Add(sku.SkuId.Value);
                }

                // SKU service-plan map (first-wins across SKUs).
                if (sku.ServicePlans is not null)
                {
                    foreach (var plan in sku.ServicePlans)
                    {
                        var name = plan.ServicePlanName;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        ins.SkuPlans.TryAdd(name, plan.ProvisioningStatus ?? "Unknown");
                    }
                }
            }

            ins.CopilotLicensesPurchased = copilotPurchased;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Productivity subscribed SKUs: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Productivity subscribed SKUs collection failed");
        }
    }

    // ================================================================
    // 2. Users — totals, enabled/disabled/guests, Copilot assignment,
    //    wasted-license detection (never signed in OR inactive >= 30 d).
    //    NOTE: Runs in parallel with CollectSubscribedSkus; ins.Licenses
    //    may be empty here. SKU friendly-name resolution happens
    //    post-Task.WhenAll using ins.SkuIdToPartNumber.
    //    403 → mark error.
    // ================================================================
    private static async Task CollectUsers(
        GraphServiceClient graph, ProductivityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Select = new[]
                {
                    "id", "displayName", "userPrincipalName",
                    "assignedLicenses", "accountEnabled",
                    "signInActivity", "userType",
                };
                rc.QueryParameters.Top = 999;
            }, cancellationToken: ct);

            if (resp?.Value is null) return;

            int total    = 0;
            int enabled  = 0;
            int disabled = 0;
            int guests   = 0;
            int wastedSeats = 0;

            var now = DateTime.UtcNow;

            foreach (var user in resp.Value)
            {
                total++;
                if (user.AccountEnabled == true)  enabled++;
                else                               disabled++;
                if (string.Equals(user.UserType, "Guest", StringComparison.OrdinalIgnoreCase))
                    guests++;

                // Capture the user's assigned SKU GUIDs. CollectSubscribedSkus runs in
                // parallel and may not have populated CopilotSkuGuids yet — Copilot
                // assigned count is computed post-Task.WhenAll in RunAsync.
                if (user.AssignedLicenses is { Count: > 0 })
                {
                    var skuGuids = new Guid[user.AssignedLicenses.Count];
                    for (int i = 0; i < user.AssignedLicenses.Count; i++)
                        skuGuids[i] = user.AssignedLicenses[i].SkuId ?? Guid.Empty;
                    ins.UserSkuAssignments.Add(skuGuids);
                }

                // Wasted-license detection: any assigned license + inactive >= 30 d.
                if (user.AssignedLicenses is { Count: > 0 })
                {
                    DateTime? lastSignIn = user.SignInActivity?.LastSignInDateTime?.UtcDateTime;
                    bool isWasted = lastSignIn is null || (now - lastSignIn.Value).TotalDays > 30;

                    if (isWasted)
                    {
                        // Store the first assigned license's SkuId as a Guid string;
                        // post-processing resolves it to a friendly name.
                        var firstSkuId = user.AssignedLicenses[0].SkuId;
                        string? skuRaw = firstSkuId.HasValue
                            ? firstSkuId.Value.ToString()
                            : null;

                        int? daysInactive = lastSignIn.HasValue
                            ? (int)(now - lastSignIn.Value).TotalDays
                            : null;

                        ins.WastedLicenses.Add(new ProductivityWastedLicenseRow
                        {
                            UserPrincipal     = user.UserPrincipalName ?? string.Empty,
                            DisplayName       = user.DisplayName,
                            Sku               = skuRaw,
                            LastSignIn        = lastSignIn,
                            DaysInactive      = daysInactive,
                            EstimatedCostYear = null,
                        });

                        wastedSeats += user.AssignedLicenses.Count;
                    }
                }
            }

            ins.TotalUsers    = total;
            ins.EnabledUsers  = enabled;
            ins.DisabledUsers = disabled;
            ins.GuestUserCount = guests;

            // CopilotLicensesAssigned + CopilotAdoptionPct are computed post-Task.WhenAll
            // in RunAsync once CopilotSkuGuids is fully resolved by CollectSubscribedSkus.

            ins.WastedLicenseCount       = ins.WastedLicenses.Count;
            ins.WastedLicenseTotalSeats  = wastedSeats;
            ins.EstimatedAnnualWaste     = null;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Productivity users: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Productivity users collection failed");
        }
    }

    // ================================================================
    // 3. Email activity — Reports D30
    //    403/404 = not licensed, not an error.
    // ================================================================
    private static async Task CollectEmailActivity(
        GraphServiceClient graph, ProductivityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var stream = await graph.Reports
                .GetEmailActivityUserDetailWithPeriod("D30")
                .GetAsync(cancellationToken: ct);

            if (stream is null) return;

            using var reader = new StreamReader(stream);
            var header = await reader.ReadLineAsync(ct);
            if (header is null) return;

            var cols     = ParseCsvHeader(header);
            var now      = DateTimeOffset.UtcNow;
            int licensed = 0;
            int active   = 0;
            long totalSend    = 0;
            long totalReceive = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = SplitCsvLine(line);
                licensed++;

                var lastRaw = GetStringField(fields, cols, "Last Activity Date");
                if (DateTimeOffset.TryParse(lastRaw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var lastActivity)
                    && (now - lastActivity).TotalDays <= 30)
                {
                    active++;
                }

                totalSend    += GetLongField(fields, cols, "Send Count");
                totalReceive += GetLongField(fields, cols, "Receive Count");
            }

            if (licensed > 0)
            {
                ins.EmailLicensedCount    = licensed;
                ins.EmailActive30d        = active;
                ins.EmailAdoptionRate     = active * 100.0 / licensed;
                ins.EmailSendAvg          = Math.Round(totalSend    / (double)licensed, 1);
                ins.EmailReceiveAvg       = Math.Round(totalReceive / (double)licensed, 1);
                ins.EmailReportAvailable  = true;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
        {
            log.LogInformation(
                "Productivity email activity: {Code} - not licensed or no data (non-fatal)",
                ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Productivity email activity collection failed");
        }
    }

    // ================================================================
    // 4. Teams activity — Reports D30
    //    403/404 = not licensed, not an error.
    // ================================================================
    private static async Task CollectTeamsActivity(
        GraphServiceClient graph, ProductivityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var stream = await graph.Reports
                .GetTeamsUserActivityUserDetailWithPeriod("D30")
                .GetAsync(cancellationToken: ct);

            if (stream is null) return;

            using var reader = new StreamReader(stream);
            var header = await reader.ReadLineAsync(ct);
            if (header is null) return;

            var cols       = ParseCsvHeader(header);
            var now        = DateTimeOffset.UtcNow;
            int licensed   = 0;
            int active     = 0;
            long teamChat  = 0;
            long privChat  = 0;
            long calls     = 0;
            long meetings  = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = SplitCsvLine(line);
                licensed++;

                var lastRaw = GetStringField(fields, cols, "Last Activity Date");
                if (DateTimeOffset.TryParse(lastRaw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var lastActivity)
                    && (now - lastActivity).TotalDays <= 30)
                {
                    active++;
                }

                teamChat += GetLongField(fields, cols, "Team Chat Message Count");
                privChat += GetLongField(fields, cols, "Private Chat Message Count");
                calls    += GetLongField(fields, cols, "Call Count");
                meetings += GetLongField(fields, cols, "Meeting Count");
            }

            if (licensed > 0)
            {
                ins.TeamsLicensedCount   = licensed;
                ins.TeamsActive30d       = active;
                ins.TeamsAdoptionRate    = active * 100.0 / licensed;
                ins.TeamsChatAvg         = Math.Round((teamChat + privChat) / (double)licensed, 1);
                ins.TeamsMeetingAvg      = Math.Round(meetings / (double)licensed, 1);
                ins.TeamsCallAvg         = Math.Round(calls    / (double)licensed, 1);
                ins.TeamsReportAvailable = true;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
        {
            log.LogInformation(
                "Productivity Teams activity: {Code} - not licensed or no data (non-fatal)",
                ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Productivity Teams activity collection failed");
        }
    }

    // ================================================================
    // 5. SharePoint site usage — Reports D30
    //    LicensedCount = total site rows (sites, not users — per spec).
    //    Active30d = rows with Last Activity Date within 30d OR Page View Count > 0.
    //    403/404 = not licensed, not an error.
    // ================================================================
    private static async Task CollectSharePointUsage(
        GraphServiceClient graph, ProductivityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var stream = await graph.Reports
                .GetSharePointSiteUsageDetailWithPeriod("D30")
                .GetAsync(cancellationToken: ct);

            if (stream is null) return;

            using var reader = new StreamReader(stream);
            var header = await reader.ReadLineAsync(ct);
            if (header is null) return;

            var cols     = ParseCsvHeader(header);
            var now      = DateTimeOffset.UtcNow;
            int licensed = 0;
            int active   = 0;
            long totalFiles = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = SplitCsvLine(line);
                licensed++;

                var views   = GetLongField(fields, cols, "Page View Count");
                var files   = GetLongField(fields, cols, "File Count");
                totalFiles += files;

                var lastRaw = GetStringField(fields, cols, "Last Activity Date");
                bool recentActivity = DateTimeOffset.TryParse(lastRaw,
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                    out var lastActivity)
                    && (now - lastActivity).TotalDays <= 30;

                if (recentActivity || views > 0)
                    active++;
            }

            if (licensed > 0)
            {
                ins.SharePointLicensedCount   = licensed;
                ins.SharePointActive30d        = active;
                ins.SharePointAdoptionRate     = active * 100.0 / licensed;
                ins.SharePointSiteCount        = licensed;
                ins.SharePointAvgFiles         = (int)(totalFiles / licensed);
                ins.SharePointReportAvailable  = true;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
        {
            log.LogInformation(
                "Productivity SharePoint usage: {Code} - not licensed or no data (non-fatal)",
                ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Productivity SharePoint usage collection failed");
        }
    }

    // ================================================================
    // 6. OneDrive usage — Reports D30
    //    Active30d = "Is Active" == "True" OR Last Activity Date within 30d.
    //    403/404 = not licensed, not an error.
    // ================================================================
    private static async Task CollectOneDriveUsage(
        GraphServiceClient graph, ProductivityInsights ins,
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

            var cols       = ParseCsvHeader(header);
            var now        = DateTimeOffset.UtcNow;
            int licensed   = 0;
            int active     = 0;
            long storageBytes = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = SplitCsvLine(line);
                licensed++;

                storageBytes += GetLongField(fields, cols, "Storage Used (Byte)");

                var isActiveRaw = GetStringField(fields, cols, "Is Active");
                var lastRaw     = GetStringField(fields, cols, "Last Activity Date");

                bool isActive = string.Equals(isActiveRaw, "True",
                    StringComparison.OrdinalIgnoreCase);
                bool recentActivity = !isActive
                    && DateTimeOffset.TryParse(lastRaw, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var lastActivity)
                    && (now - lastActivity).TotalDays <= 30;

                if (isActive || recentActivity)
                    active++;
            }

            if (licensed > 0)
            {
                ins.OneDriveLicensedCount   = licensed;
                ins.OneDriveActive30d        = active;
                ins.OneDriveAdoptionRate     = active * 100.0 / licensed;
                ins.OneDriveTotalGB          = storageBytes / 1024.0 / 1024.0 / 1024.0;
                ins.OneDriveReportAvailable  = true;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
        {
            log.LogInformation(
                "Productivity OneDrive usage: {Code} - not licensed or no data (non-fatal)",
                ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Productivity OneDrive usage collection failed");
        }
    }

    // ================================================================
    // 7. Office activations — /reports/getOffice365ActivationsUserDetail
    //    Counts per-platform users (Windows / Mac / mobile).
    //    403/404 = not licensed, not an error.
    // ================================================================
    private static async Task CollectOfficeActivations(
        GraphServiceClient graph, ProductivityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var stream = await graph.Reports
                .GetOffice365ActivationsUserDetail
                .GetAsync(cancellationToken: ct);

            if (stream is null) return;

            using var reader = new StreamReader(stream);
            var header = await reader.ReadLineAsync(ct);
            if (header is null) return;

            var cols      = ParseCsvHeader(header);
            int winUsers  = 0;
            int macUsers  = 0;
            int mobUsers  = 0;
            int totalUsers = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = SplitCsvLine(line);
                totalUsers++;

                if (GetLongField(fields, cols, "Windows") > 0) winUsers++;
                if (GetLongField(fields, cols, "Mac") > 0) macUsers++;
                if (GetLongField(fields, cols, "Android") > 0
                 || GetLongField(fields, cols, "iOS") > 0) mobUsers++;
            }

            if (totalUsers > 0)
            {
                ins.OfficeWindowsActivations         = winUsers;
                ins.OfficeMacActivations             = macUsers;
                ins.OfficeMobileActivations          = mobUsers;
                ins.OfficeDesktopAdoptionRate        =
                    Math.Round((winUsers + macUsers) * 100.0 / totalUsers, 1);
                ins.OfficeActivationsReportAvailable = true;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
        {
            log.LogInformation(
                "Productivity Office activations: {Code} - not licensed or no data (non-fatal)",
                ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Productivity Office activations collection failed");
        }
    }

    // ================================================================
    // 8. Active users daily snapshot — Reports D30
    //    Fallback: sets LicensedCount for services where detail
    //    reports didn't populate it.  Keeps the last non-empty row.
    //    403/404 = not an error.
    // ================================================================
    private static async Task CollectActiveUsers(
        GraphServiceClient graph, ProductivityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var stream = await graph.Reports
                .GetOffice365ActiveUserDetailWithPeriod("D30")
                .GetAsync(cancellationToken: ct);

            if (stream is null) return;

            using var reader = new StreamReader(stream);
            var header = await reader.ReadLineAsync(ct);
            if (header is null) return;

            var cols     = ParseCsvHeader(header);
            string? lastLine = null;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (!string.IsNullOrWhiteSpace(line))
                    lastLine = line;
            }

            if (lastLine is null) return;

            var fields = SplitCsvLine(lastLine);

            // Fallback only — don't overwrite values already set by detail reports.
            if (ins.TeamsLicensedCount == 0)
            {
                var v = GetLongField(fields, cols, "Microsoft Teams");
                if (v > 0) ins.TeamsLicensedCount = (int)v;
            }
            if (ins.EmailLicensedCount == 0)
            {
                var v = GetLongField(fields, cols, "Exchange");
                if (v > 0) ins.EmailLicensedCount = (int)v;
            }
            if (ins.SharePointLicensedCount == 0)
            {
                var v = GetLongField(fields, cols, "SharePoint");
                if (v > 0) ins.SharePointLicensedCount = (int)v;
            }
            if (ins.OneDriveLicensedCount == 0)
            {
                var v = GetLongField(fields, cols, "OneDrive");
                if (v > 0) ins.OneDriveLicensedCount = (int)v;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
        {
            log.LogInformation(
                "Productivity active users: {Code} - not licensed or no data (non-fatal)",
                ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Productivity active users collection failed");
        }
    }

    // ================================================================
    // 9. Graph Connectors — /external/connections
    //    403 = ExternalConnection.Read.All missing, not an error.
    // ================================================================
    private static async Task CollectGraphConnectors(
        GraphServiceClient graph, ProductivityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.External.Connections.GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;

            ins.GraphConnectorsCount     = resp.Value.Count;
            ins.GraphConnectorsAvailable = true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogInformation(
                "Productivity Graph connectors: 403 - ExternalConnection.Read.All required (non-fatal)");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Productivity Graph connectors collection failed");
        }
    }

    // ================================================================
    // CSV parsing helpers — quote-aware splitter from DataPipeline pattern.
    // BOM-aware header parser; handles quoted commas in display names.
    // ================================================================
    private static Dictionary<string, int> ParseCsvHeader(string header)
    {
        header = header.TrimStart('\uFEFF');
        var cols   = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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

        var result   = new List<string>();
        var sb       = new System.Text.StringBuilder();
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
