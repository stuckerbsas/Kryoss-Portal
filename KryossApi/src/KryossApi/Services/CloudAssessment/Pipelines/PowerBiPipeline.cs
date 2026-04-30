using System.Globalization;
using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services.CloudAssessment.Recommendations;
using KryossApi.Services.CopilotReadiness.Pipelines;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// CA-9: Power BI Governance pipeline. Calls PBI Admin REST API to enumerate
/// workspaces, gateways, capacities, activity events, and dataset health.
/// Throttled to respect PBI admin API limits (~200 req/hr).
/// </summary>
public static class PowerBiPipeline
{
    private const int MaxWorkspaces = 500;
    private const int MaxActivityEvents = 10000;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static async Task<PipelineResult> RunAsync(
        HttpClient? pbiHttp,
        Guid scanId,
        KryossDbContext db,
        ILogger log,
        CancellationToken ct)
    {
        if (pbiHttp is null)
        {
            return new PipelineResult { PipelineName = "powerbi", Status = "skipped" };
        }

        var ins = new PowerBiInsights { ApiAccessible = true };
        string pipelineStatus = "ok";

        try
        {
            // 1. Probe — test API access.
            using var probeResp = await pbiHttp.GetAsync("/v1.0/myorg/admin/groups?$top=1", ct);
            if (!probeResp.IsSuccessStatusCode)
            {
                ins.ApiAccessible = false;
                ins.ConnectionError = $"PBI Admin API returned {(int)probeResp.StatusCode}";
                log.LogWarning("PBI probe failed: {Status}", (int)probeResp.StatusCode);

                return new PipelineResult
                {
                    PipelineName = "powerbi",
                    Status = "skipped",
                    Findings = PowerBiRecommendations.Generate(ins)
                };
            }

            // 2. Workspaces — GET /admin/groups?$top=5000
            await CollectWorkspaces(pbiHttp, ins, log, ct);

            // 3. Capacities
            await CollectCapacities(pbiHttp, ins, log, ct);

            // 4. Gateways
            await CollectGateways(pbiHttp, ins, log, ct);

            // 5. Activity events (last 30d, sampled)
            await CollectActivityEvents(pbiHttp, ins, log, ct);

            // 6. Datasets health
            await CollectDatasetsHealth(pbiHttp, ins, log, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "PBI pipeline partial failure");
            pipelineStatus = "partial";
        }

        // Persist collected data.
        var now = DateTime.UtcNow;
        foreach (var ws in ins.Workspaces)
        {
            db.CloudAssessmentPowerBiWorkspaces.Add(new CloudAssessmentPowerBiWorkspace
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                WorkspaceId = ws.WorkspaceId,
                Name = ws.Name,
                Type = ws.Type,
                State = ws.State,
                IsOnDedicatedCapacity = ws.IsOnDedicatedCapacity,
                CapacityId = ws.CapacityId,
                HasWorkspaceLevelSettings = ws.HasWorkspaceLevelSettings,
                MemberCount = ws.MemberCount,
                AdminCount = ws.AdminCount,
                ExternalUserCount = ws.ExternalUserCount,
                DatasetCount = ws.DatasetCount,
                ReportCount = ws.ReportCount,
                DashboardCount = ws.DashboardCount,
                DataflowCount = ws.DataflowCount,
                LastUpdatedDate = ws.LastUpdatedDate,
                CreatedAt = now
            });
        }

        foreach (var gw in ins.Gateways)
        {
            db.CloudAssessmentPowerBiGateways.Add(new CloudAssessmentPowerBiGateway
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                GatewayId = gw.GatewayId,
                Name = gw.Name,
                Type = gw.Type,
                PublicKeyValid = gw.PublicKeyValid,
                Status = gw.Status,
                Version = gw.Version,
                ContactInformation = gw.ContactInformation,
                CreatedAt = now
            });
        }

        foreach (var cap in ins.Capacities)
        {
            db.CloudAssessmentPowerBiCapacities.Add(new CloudAssessmentPowerBiCapacity
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                CapacityId = cap.CapacityId,
                DisplayName = cap.DisplayName,
                Sku = cap.Sku,
                Region = cap.Region,
                State = cap.State,
                UsagePct = cap.UsagePct,
                AdminCount = cap.AdminCount,
                CreatedAt = now
            });
        }

        if (ins.ActivitySummary is not null)
        {
            db.CloudAssessmentPowerBiActivitySummaries.Add(new CloudAssessmentPowerBiActivitySummary
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                ActivitiesTotal = ins.ActivitySummary.ActivitiesTotal,
                UniqueUsers = ins.ActivitySummary.UniqueUsers,
                ViewReportCount = ins.ActivitySummary.ViewReportCount,
                EditReportCount = ins.ActivitySummary.EditReportCount,
                CreateDatasetCount = ins.ActivitySummary.CreateDatasetCount,
                DeleteCount = ins.ActivitySummary.DeleteCount,
                ShareExternalCount = ins.ActivitySummary.ShareExternalCount,
                ExportCount = ins.ActivitySummary.ExportCount,
                PeriodDays = ins.ActivitySummary.PeriodDays,
                CreatedAt = now
            });
        }

        // Generate findings + metrics.
        var findings = PowerBiRecommendations.Generate(ins);
        var metrics = new Dictionary<string, string>
        {
            ["workspaces_total"] = ins.WorkspaceCount.ToString(Inv),
            ["workspaces_personal"] = ins.PersonalWorkspaceCount.ToString(Inv),
            ["workspaces_orphaned"] = ins.OrphanedWorkspaceCount.ToString(Inv),
            ["datasets_total"] = ins.TotalDatasets.ToString(Inv),
            ["datasets_stale_30d"] = ins.DatasetsStale30d.ToString(Inv),
            ["gateways_total"] = ins.GatewayCount.ToString(Inv),
            ["gateways_offline"] = ins.GatewaysOffline.ToString(Inv),
            ["capacities_total"] = ins.CapacityCount.ToString(Inv),
            ["activities_30d"] = ins.Activities30d.ToString(Inv),
            ["unique_users_30d"] = ins.UniqueActiveUsers30d.ToString(Inv),
            ["external_shares_30d"] = ins.ExternalShares30d.ToString(Inv),
            ["reports_total"] = ins.TotalReports.ToString(Inv)
        };

        return new PipelineResult
        {
            PipelineName = "powerbi",
            Status = pipelineStatus,
            Findings = findings,
            Metrics = metrics
        };
    }

    // ── Collectors ──

    private static async Task CollectWorkspaces(
        HttpClient http, PowerBiInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync("/v1.0/myorg/admin/groups?$top=5000", ct);
            if (!resp.IsSuccessStatusCode) { log.LogWarning("PBI workspaces: {Status}", (int)resp.StatusCode); return; }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            int count = 0;
            foreach (var ws in items.EnumerateArray())
            {
                if (count >= MaxWorkspaces) break;

                var row = new PowerBiWorkspaceRow
                {
                    WorkspaceId = ws.GetProperty("id").GetString() ?? "",
                    Name = ws.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Type = ws.TryGetProperty("type", out var t) ? t.GetString() : null,
                    State = ws.TryGetProperty("state", out var s) ? s.GetString() : null,
                    IsOnDedicatedCapacity = ws.TryGetProperty("isOnDedicatedCapacity", out var dc) ? dc.GetBoolean() : null,
                    CapacityId = ws.TryGetProperty("capacityId", out var ci) ? ci.GetString() : null,
                    HasWorkspaceLevelSettings = ws.TryGetProperty("hasWorkspaceLevelSettings", out var hwls) ? hwls.GetBoolean() : null,
                };

                // Count users from the expanded users array if present.
                if (ws.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array)
                {
                    row.MemberCount = users.GetArrayLength();
                    int admins = 0, external = 0;
                    foreach (var u in users.EnumerateArray())
                    {
                        var role = u.TryGetProperty("groupUserAccessRight", out var r) ? r.GetString() : null;
                        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) admins++;
                        var principal = u.TryGetProperty("emailAddress", out var e) ? e.GetString() : null;
                        if (principal != null && principal.Contains("#EXT#", StringComparison.OrdinalIgnoreCase)) external++;
                    }
                    row.AdminCount = admins;
                    row.ExternalUserCount = external;
                }

                if (ws.TryGetProperty("datasets", out var ds) && ds.ValueKind == JsonValueKind.Array)
                    row.DatasetCount = ds.GetArrayLength();
                if (ws.TryGetProperty("reports", out var rp) && rp.ValueKind == JsonValueKind.Array)
                    row.ReportCount = rp.GetArrayLength();
                if (ws.TryGetProperty("dashboards", out var db) && db.ValueKind == JsonValueKind.Array)
                    row.DashboardCount = db.GetArrayLength();
                if (ws.TryGetProperty("dataflows", out var df) && df.ValueKind == JsonValueKind.Array)
                    row.DataflowCount = df.GetArrayLength();

                ins.Workspaces.Add(row);
                count++;
            }

            ins.WorkspaceCount = ins.Workspaces.Count;
            ins.PersonalWorkspaceCount = ins.Workspaces.Count(w =>
                string.Equals(w.Type, "PersonalGroup", StringComparison.OrdinalIgnoreCase));
            ins.OrphanedWorkspaceCount = ins.Workspaces.Count(w => w.AdminCount == 0);
            ins.WorkspacesOnDedicatedCapacity = ins.Workspaces.Count(w => w.IsOnDedicatedCapacity == true);
            ins.WorkspacesWithExternalUsers = ins.Workspaces.Count(w => w.ExternalUserCount > 0);
            ins.TotalDatasets = ins.Workspaces.Sum(w => w.DatasetCount);
            ins.TotalReports = ins.Workspaces.Sum(w => w.ReportCount);
            ins.TotalDashboards = ins.Workspaces.Sum(w => w.DashboardCount);
            ins.TotalDataflows = ins.Workspaces.Sum(w => w.DataflowCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "PBI workspace collection failed");
        }
    }

    private static async Task CollectCapacities(
        HttpClient http, PowerBiInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync("/v1.0/myorg/admin/capacities", ct);
            if (!resp.IsSuccessStatusCode) { log.LogWarning("PBI capacities: {Status}", (int)resp.StatusCode); return; }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            foreach (var cap in items.EnumerateArray())
            {
                var row = new PowerBiCapacityRow
                {
                    CapacityId = cap.TryGetProperty("id", out var id) ? id.GetString() : null,
                    DisplayName = cap.TryGetProperty("displayName", out var dn) ? dn.GetString() : null,
                    Sku = cap.TryGetProperty("sku", out var sku) ? sku.GetString() : null,
                    Region = cap.TryGetProperty("region", out var r) ? r.GetString() : null,
                    State = cap.TryGetProperty("state", out var s) ? s.GetString() : null,
                };

                if (cap.TryGetProperty("admins", out var admins) && admins.ValueKind == JsonValueKind.Array)
                    row.AdminCount = admins.GetArrayLength();

                ins.Capacities.Add(row);
            }

            ins.CapacityCount = ins.Capacities.Count;
            ins.CapacitiesOverThreshold = ins.Capacities.Count(c => c.UsagePct > 85);
            if (ins.Capacities.Count > 0)
            {
                var withUsage = ins.Capacities.Where(c => c.UsagePct.HasValue).ToList();
                if (withUsage.Count > 0)
                    ins.AvgCapacityUsagePct = Math.Round(withUsage.Average(c => c.UsagePct!.Value), 1);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "PBI capacity collection failed");
        }
    }

    private static async Task CollectGateways(
        HttpClient http, PowerBiInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync("/v1.0/myorg/gateways", ct);
            if (resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.NotFound)
            {
                log.LogInformation("PBI gateways endpoint not accessible ({Status}), skipping", (int)resp.StatusCode);
                return;
            }
            if (!resp.IsSuccessStatusCode) { log.LogWarning("PBI gateways: {Status}", (int)resp.StatusCode); return; }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            foreach (var gw in items.EnumerateArray())
            {
                var row = new PowerBiGatewayRow
                {
                    GatewayId = gw.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Name = gw.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Type = gw.TryGetProperty("type", out var t) ? t.GetString() : null,
                    Status = gw.TryGetProperty("status", out var s) ? s.GetString() : null,
                    Version = gw.TryGetProperty("gatewayVersion", out var v) ? v.GetString() : null,
                    ContactInformation = gw.TryGetProperty("contactInformation", out var ci) ? ci.GetString() : null,
                };

                if (gw.TryGetProperty("publicKey", out var pk) && pk.ValueKind == JsonValueKind.Object)
                    row.PublicKeyValid = true;

                ins.Gateways.Add(row);
            }

            ins.GatewayCount = ins.Gateways.Count;
            ins.GatewaysOffline = ins.Gateways.Count(g =>
                !string.Equals(g.Status, "Live", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(g.Status, "Online", StringComparison.OrdinalIgnoreCase) &&
                g.Status != null);
            ins.PersonalGatewayCount = ins.Gateways.Count(g =>
                string.Equals(g.Type, "Personal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(g.Type, "OnPremisesPersonal", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "PBI gateway collection failed");
        }
    }

    private static async Task CollectActivityEvents(
        HttpClient http, PowerBiInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var endDate = DateTime.UtcNow.Date;
            var startDate = endDate.AddDays(-30);
            var start = startDate.ToString("yyyy-MM-dd'T'00:00:00", Inv);
            var end = endDate.ToString("yyyy-MM-dd'T'23:59:59", Inv);

            var activityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var uniqueUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalEvents = 0;

            string? continuationUri = $"/v1.0/myorg/admin/activityevents?startDateTime='{start}'&endDateTime='{end}'";

            while (continuationUri != null && totalEvents < MaxActivityEvents)
            {
                using var resp = await http.GetAsync(continuationUri, ct);
                if (!resp.IsSuccessStatusCode) break;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                if (doc.RootElement.TryGetProperty("activityEventEntities", out var events) &&
                    events.ValueKind == JsonValueKind.Array)
                {
                    foreach (var evt in events.EnumerateArray())
                    {
                        if (totalEvents >= MaxActivityEvents) break;
                        totalEvents++;

                        var activity = evt.TryGetProperty("Activity", out var a) ? a.GetString() ?? "" : "";
                        activityCounts[activity] = activityCounts.GetValueOrDefault(activity) + 1;

                        if (evt.TryGetProperty("UserId", out var uid) && uid.GetString() is string userId)
                            uniqueUsers.Add(userId);
                    }
                }

                continuationUri = doc.RootElement.TryGetProperty("continuationUri", out var cu)
                    ? cu.GetString()
                    : null;
            }

            ins.Activities30d = totalEvents;
            ins.UniqueActiveUsers30d = uniqueUsers.Count;
            ins.ExternalShares30d = activityCounts.GetValueOrDefault("ShareReport") +
                                    activityCounts.GetValueOrDefault("ShareDashboard");
            ins.Exports30d = activityCounts.GetValueOrDefault("ExportReport") +
                             activityCounts.GetValueOrDefault("ExportVisualData") +
                             activityCounts.GetValueOrDefault("ExportDataflow");
            ins.Deletes30d = activityCounts.GetValueOrDefault("DeleteReport") +
                             activityCounts.GetValueOrDefault("DeleteDashboard") +
                             activityCounts.GetValueOrDefault("DeleteDataset");

            ins.ActivitySummary = new PowerBiActivitySummaryRow
            {
                ActivitiesTotal = totalEvents,
                UniqueUsers = uniqueUsers.Count,
                ViewReportCount = activityCounts.GetValueOrDefault("ViewReport"),
                EditReportCount = activityCounts.GetValueOrDefault("EditReport"),
                CreateDatasetCount = activityCounts.GetValueOrDefault("CreateDataset"),
                DeleteCount = ins.Deletes30d,
                ShareExternalCount = ins.ExternalShares30d,
                ExportCount = ins.Exports30d,
                PeriodDays = 30
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "PBI activity events collection failed");
        }
    }

    private static async Task CollectDatasetsHealth(
        HttpClient http, PowerBiInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync("/v1.0/myorg/admin/datasets", ct);
            if (!resp.IsSuccessStatusCode) { log.LogWarning("PBI datasets: {Status}", (int)resp.StatusCode); return; }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            var staleThreshold = DateTime.UtcNow.AddDays(-30);
            int neverRefreshed = 0, stale = 0;

            foreach (var ds in items.EnumerateArray())
            {
                var isRefreshable = ds.TryGetProperty("isRefreshable", out var ir) && ir.GetBoolean();
                if (!isRefreshable) continue;

                if (ds.TryGetProperty("lastRefreshTime", out var lrt) && lrt.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(lrt.GetString(), Inv, DateTimeStyles.RoundtripKind, out var refreshTime))
                    {
                        if (refreshTime < staleThreshold) stale++;
                    }
                }
                else
                {
                    neverRefreshed++;
                }
            }

            ins.DatasetsNeverRefreshed = neverRefreshed;
            ins.DatasetsStale30d = stale;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "PBI datasets health collection failed");
        }
    }
}
