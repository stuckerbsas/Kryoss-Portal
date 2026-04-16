using System.Text.Json;
using KryossApi.Services.CopilotReadiness.Recommendations;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CopilotReadiness.Pipelines;

/// <summary>
/// Pre-computed M365 service usage and deployment metrics extracted from Graph API.
/// Built once per assessment run, shared across all recommendation generators.
/// </summary>
public class M365Insights
{
    // --- Users & Licensing ---
    public int TotalUsers { get; set; }
    public int EnabledUsers { get; set; }
    public int CopilotLicensesPurchased { get; set; }
    public int CopilotLicensesAssigned { get; set; }
    public double CopilotAdoptionPct { get; set; }

    // --- Email (Exchange) activity ---
    public double EmailSendAvg { get; set; }
    public double EmailReceiveAvg { get; set; }
    public int EmailActiveUsers { get; set; }
    public bool EmailReportAvailable { get; set; }

    // --- Teams activity ---
    public double TeamsChatAvg { get; set; }
    public double TeamsMeetingAvg { get; set; }
    public double TeamsCallAvg { get; set; }
    public int TeamsActiveUsers { get; set; }
    public int TeamsTotalMeetings { get; set; }
    public bool TeamsReportAvailable { get; set; }

    // --- SharePoint usage ---
    public int SharePointActiveSites { get; set; }
    public int SharePointTotalSites { get; set; }
    public double SharePointActivityRate { get; set; }
    public int SharePointAvgFiles { get; set; }
    public int SharePointTotalFiles { get; set; }
    public bool SharePointReportAvailable { get; set; }

    // --- OneDrive usage ---
    public int OneDriveActiveAccounts { get; set; }
    public int OneDriveTotalAccounts { get; set; }
    public double OneDriveAdoptionRate { get; set; }
    public double OneDriveTotalFilesGB { get; set; }
    public bool OneDriveReportAvailable { get; set; }

    // --- Office activations ---
    public int OfficeWindowsActivations { get; set; }
    public int OfficeMacActivations { get; set; }
    public int OfficeMobileActivations { get; set; }
    public double OfficeDesktopAdoptionRate { get; set; }
    public int OfficeTotalUsersWithActivations { get; set; }
    public bool OfficeActivationsReportAvailable { get; set; }

    // --- Active users (latest snapshot) ---
    public int ActiveUsersExchange { get; set; }
    public int ActiveUsersTeams { get; set; }
    public int ActiveUsersSharePoint { get; set; }
    public int ActiveUsersOneDrive { get; set; }
    public bool ActiveUsersReportAvailable { get; set; }

    // --- Graph Connectors ---
    public int GraphConnectorsCount { get; set; }
    public List<string> GraphConnectorNames { get; set; } = [];
    public bool GraphConnectorsAvailable { get; set; }

    // --- SKU plans: plan name -> provisioning status ---
    public Dictionary<string, string> SkuPlans { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// M365 pipeline: collects license, usage activity, and Graph Connector data from
/// Microsoft Graph API. Feeds the M365 recommendation engine with all 9 data sources.
/// </summary>
public static class M365Pipeline
{
    // SKU IDs for Microsoft 365 Copilot licenses
    private static readonly HashSet<string> CopilotSkuIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "c28afa23-5a37-4837-938f-7cc48d0cca5c", // M365 Copilot
        "f2b5e97e-f677-4bb5-8127-5c3ce7b6a64e", // M365 Copilot (User)
    };

    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph,
        ILogger log,
        CancellationToken ct)
    {
        var result = new PipelineResult { PipelineName = "m365", Status = "ok" };
        var insights = new M365Insights();

        // Run all data collectors in parallel; each catches its own errors.
        var tasks = new List<Task>
        {
            CollectSubscribedSkus(graph, insights, log, ct),
            CollectUsers(graph, insights, log, ct),
            CollectEmailActivity(graph, insights, log, ct),
            CollectTeamsActivity(graph, insights, log, ct),
            CollectSharePointUsage(graph, insights, log, ct),
            CollectOneDriveUsage(graph, insights, log, ct),
            CollectOfficeActivations(graph, insights, log, ct),
            CollectActiveUsers(graph, insights, log, ct),
            CollectGraphConnectors(graph, insights, log, ct),
        };

        await Task.WhenAll(tasks);

        // Generate recommendations from collected data.
        result.Findings.AddRange(M365Recommendations.Generate(insights, insights.SkuPlans));

        // Emit key metrics for scoring.
        result.Metrics["total_users"] = insights.TotalUsers.ToString();
        result.Metrics["enabled_users"] = insights.EnabledUsers.ToString();
        result.Metrics["copilot_licenses_assigned"] = insights.CopilotLicensesAssigned.ToString();
        result.Metrics["copilot_adoption_pct"] = insights.CopilotAdoptionPct.ToString("F1");
        result.Metrics["email_active_users"] = insights.EmailActiveUsers.ToString();
        result.Metrics["teams_active_users"] = insights.TeamsActiveUsers.ToString();
        result.Metrics["teams_total_meetings"] = insights.TeamsTotalMeetings.ToString();
        result.Metrics["sharepoint_active_sites"] = insights.SharePointActiveSites.ToString();
        result.Metrics["sharepoint_activity_rate"] = insights.SharePointActivityRate.ToString("F1");
        result.Metrics["onedrive_active_accounts"] = insights.OneDriveActiveAccounts.ToString();
        result.Metrics["onedrive_adoption_rate"] = insights.OneDriveAdoptionRate.ToString("F1");
        result.Metrics["office_desktop_adoption_rate"] = insights.OfficeDesktopAdoptionRate.ToString("F1");
        result.Metrics["graph_connectors_count"] = insights.GraphConnectorsCount.ToString();
        result.Metrics["sku_plans_total"] = insights.SkuPlans.Count.ToString();

        return result;
    }

    // ================================================================
    // 1. Subscribed SKUs — all SKUs with service plans + provisioning status
    // ================================================================
    private static async Task CollectSubscribedSkus(
        GraphServiceClient graph, M365Insights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.SubscribedSkus.GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;

            foreach (var sku in resp.Value)
            {
                if (sku.ServicePlans is null) continue;

                foreach (var plan in sku.ServicePlans)
                {
                    var name = plan.ServicePlanName;
                    var status = plan.ProvisioningStatus ?? "Unknown";
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Keep first occurrence (some plans appear in multiple SKUs)
                    ins.SkuPlans.TryAdd(name, status);
                }
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("M365 subscribed SKUs: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "M365 subscribed SKUs collection failed");
        }
    }

    // ================================================================
    // 2. Users — total, enabled, Copilot license assignment
    // ================================================================
    private static async Task CollectUsers(
        GraphServiceClient graph, M365Insights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Select = new[]
                {
                    "id", "displayName", "userPrincipalName",
                    "assignedLicenses", "accountEnabled"
                };
                rc.QueryParameters.Top = 999;
            }, cancellationToken: ct);

            if (resp?.Value is null) return;

            ins.TotalUsers = resp.Value.Count;
            int copilotAssigned = 0;

            foreach (var user in resp.Value)
            {
                if (user.AccountEnabled == true) ins.EnabledUsers++;

                if (user.AssignedLicenses is null) continue;
                foreach (var lic in user.AssignedLicenses)
                {
                    if (lic.SkuId.HasValue &&
                        CopilotSkuIds.Contains(lic.SkuId.Value.ToString()))
                    {
                        copilotAssigned++;
                        break; // count each user once
                    }
                }
            }

            ins.CopilotLicensesAssigned = copilotAssigned;
            if (ins.TotalUsers > 0)
                ins.CopilotAdoptionPct = copilotAssigned * 100.0 / ins.TotalUsers;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("M365 users: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "M365 users collection failed");
        }
    }

    // ================================================================
    // 3. Email Activity — GET /reports/getEmailActivityUserDetail(period='D30')
    // ================================================================
    private static async Task CollectEmailActivity(
        GraphServiceClient graph, M365Insights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var stream = await graph.Reports
                .GetEmailActivityUserDetailWithPeriod("D30")
                .GetAsync(cancellationToken: ct);

            if (stream is null) return;

            using var reader = new StreamReader(stream);
            // Skip header line
            var header = await reader.ReadLineAsync(ct);
            if (header is null) return;

            var cols = ParseCsvHeader(header);
            long totalSend = 0, totalReceive = 0;
            int rows = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = SplitCsvLine(line);
                rows++;

                totalSend += GetLongField(fields, cols, "Send Count");
                totalReceive += GetLongField(fields, cols, "Receive Count");
            }

            if (rows > 0)
            {
                ins.EmailActiveUsers = rows;
                ins.EmailSendAvg = Math.Round(totalSend / (double)rows, 1);
                ins.EmailReceiveAvg = Math.Round(totalReceive / (double)rows, 1);
                ins.EmailReportAvailable = true;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("M365 email activity: 403 - Reports.Read.All required");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "M365 email activity collection failed");
        }
    }

    // ================================================================
    // 4. Teams Activity — GET /reports/getTeamsUserActivityUserDetail(period='D30')
    // ================================================================
    private static async Task CollectTeamsActivity(
        GraphServiceClient graph, M365Insights ins, ILogger log, CancellationToken ct)
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

            var cols = ParseCsvHeader(header);
            long teamChat = 0, privateChat = 0, calls = 0, meetings = 0;
            int rows = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = SplitCsvLine(line);
                rows++;

                teamChat += GetLongField(fields, cols, "Team Chat Message Count");
                privateChat += GetLongField(fields, cols, "Private Chat Message Count");
                calls += GetLongField(fields, cols, "Call Count");
                meetings += GetLongField(fields, cols, "Meeting Count");
            }

            if (rows > 0)
            {
                ins.TeamsActiveUsers = rows;
                ins.TeamsTotalMeetings = (int)meetings;
                ins.TeamsChatAvg = Math.Round((teamChat + privateChat) / (double)rows, 1);
                ins.TeamsMeetingAvg = Math.Round(meetings / (double)rows, 1);
                ins.TeamsCallAvg = Math.Round(calls / (double)rows, 1);
                ins.TeamsReportAvailable = true;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("M365 Teams activity: 403 - Reports.Read.All required");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "M365 Teams activity collection failed");
        }
    }

    // ================================================================
    // 5. SharePoint Usage — GET /reports/getSharePointSiteUsageDetail(period='D30')
    // ================================================================
    private static async Task CollectSharePointUsage(
        GraphServiceClient graph, M365Insights ins, ILogger log, CancellationToken ct)
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

            var cols = ParseCsvHeader(header);
            long totalFiles = 0, pageViews = 0;
            int activeSites = 0, totalSites = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = SplitCsvLine(line);
                totalSites++;

                var views = GetLongField(fields, cols, "Page View Count");
                var files = GetLongField(fields, cols, "File Count");
                totalFiles += files;
                pageViews += views;
                if (views > 0) activeSites++;
            }

            if (totalSites > 0)
            {
                ins.SharePointTotalSites = totalSites;
                ins.SharePointActiveSites = activeSites;
                ins.SharePointTotalFiles = (int)totalFiles;
                ins.SharePointAvgFiles = (int)(totalFiles / totalSites);
                ins.SharePointActivityRate = Math.Round(activeSites * 100.0 / totalSites, 1);
                ins.SharePointReportAvailable = true;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("M365 SharePoint usage: 403 - Reports.Read.All required");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "M365 SharePoint usage collection failed");
        }
    }

    // ================================================================
    // 6. OneDrive Usage — GET /reports/getOneDriveUsageAccountDetail(period='D30')
    // ================================================================
    private static async Task CollectOneDriveUsage(
        GraphServiceClient graph, M365Insights ins, ILogger log, CancellationToken ct)
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
            int activeAccounts = 0, totalAccounts = 0;
            long totalFiles = 0;
            long storageBytes = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = SplitCsvLine(line);
                totalAccounts++;

                var files = GetLongField(fields, cols, "File Count");
                var storage = GetLongField(fields, cols, "Storage Used (Byte)");
                var isActive = GetStringField(fields, cols, "Is Active");

                totalFiles += files;
                storageBytes += storage;
                if (string.Equals(isActive, "True", StringComparison.OrdinalIgnoreCase) || files > 0)
                    activeAccounts++;
            }

            if (totalAccounts > 0)
            {
                ins.OneDriveTotalAccounts = totalAccounts;
                ins.OneDriveActiveAccounts = activeAccounts;
                ins.OneDriveAdoptionRate = Math.Round(activeAccounts * 100.0 / totalAccounts, 1);
                ins.OneDriveTotalFilesGB = Math.Round(storageBytes / (1024.0 * 1024 * 1024), 2);
                ins.OneDriveReportAvailable = true;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("M365 OneDrive usage: 403 - Reports.Read.All required");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "M365 OneDrive usage collection failed");
        }
    }

    // ================================================================
    // 7. Office Activations — GET /reports/getOffice365ActivationsUserDetail
    // ================================================================
    private static async Task CollectOfficeActivations(
        GraphServiceClient graph, M365Insights ins, ILogger log, CancellationToken ct)
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

            var cols = ParseCsvHeader(header);
            int windowsUsers = 0, macUsers = 0, mobileUsers = 0, totalUsers = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = SplitCsvLine(line);
                totalUsers++;

                if (GetLongField(fields, cols, "Windows") > 0) windowsUsers++;
                if (GetLongField(fields, cols, "Mac") > 0) macUsers++;
                if (GetLongField(fields, cols, "Android") > 0 ||
                    GetLongField(fields, cols, "iOS") > 0) mobileUsers++;
            }

            if (totalUsers > 0)
            {
                ins.OfficeTotalUsersWithActivations = totalUsers;
                ins.OfficeWindowsActivations = windowsUsers;
                ins.OfficeMacActivations = macUsers;
                ins.OfficeMobileActivations = mobileUsers;
                ins.OfficeDesktopAdoptionRate = Math.Round(
                    (windowsUsers + macUsers) * 100.0 / totalUsers, 1);
                ins.OfficeActivationsReportAvailable = true;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("M365 Office activations: 403 - Reports.Read.All required");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "M365 Office activations collection failed");
        }
    }

    // ================================================================
    // 8. Active Users — GET /reports/getOffice365ActiveUserDetail(period='D30')
    // ================================================================
    private static async Task CollectActiveUsers(
        GraphServiceClient graph, M365Insights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var stream = await graph.Reports
                .GetOffice365ActiveUserDetailWithPeriod("D30")
                .GetAsync(cancellationToken: ct);

            if (stream is null) return;

            using var reader = new StreamReader(stream);

            string? lastLine = null;
            string? header = await reader.ReadLineAsync(ct);
            if (header is null) return;

            var cols = ParseCsvHeader(header);

            // CSV is sorted by date; read all and keep the last row (most recent snapshot)
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (!string.IsNullOrWhiteSpace(line))
                    lastLine = line;
            }

            if (lastLine is not null)
            {
                var fields = SplitCsvLine(lastLine);
                ins.ActiveUsersExchange = (int)GetLongField(fields, cols, "Exchange");
                ins.ActiveUsersTeams = (int)GetLongField(fields, cols, "Microsoft Teams");
                ins.ActiveUsersSharePoint = (int)GetLongField(fields, cols, "SharePoint");
                ins.ActiveUsersOneDrive = (int)GetLongField(fields, cols, "OneDrive");
                ins.ActiveUsersReportAvailable = true;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("M365 active users: 403 - Reports.Read.All required");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "M365 active users collection failed");
        }
    }

    // ================================================================
    // 9. Graph Connectors — GET /external/connections
    // ================================================================
    private static async Task CollectGraphConnectors(
        GraphServiceClient graph, M365Insights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.External.Connections.GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;

            ins.GraphConnectorsAvailable = true;
            ins.GraphConnectorsCount = resp.Value.Count;
            foreach (var conn in resp.Value)
            {
                var name = conn.Name ?? conn.Id ?? "Unknown";
                ins.GraphConnectorNames.Add(name);
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("M365 Graph connectors: 403 - ExternalConnection.Read.All required");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "M365 Graph connectors collection failed");
        }
    }

    // ================================================================
    // CSV parsing helpers
    // ================================================================

    /// <summary>Parse header row into a column-name -> index map (BOM-aware).</summary>
    private static Dictionary<string, int> ParseCsvHeader(string header)
    {
        // Remove UTF-8 BOM if present
        header = header.TrimStart('\uFEFF');
        var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fields = header.Split(',');
        for (int i = 0; i < fields.Length; i++)
            cols[fields[i].Trim('"', ' ')] = i;
        return cols;
    }

    /// <summary>Split a CSV line on commas (simple, no quoted-field handling needed for Graph reports).</summary>
    private static string[] SplitCsvLine(string line) => line.Split(',');

    private static long GetLongField(string[] fields, Dictionary<string, int> cols, string name)
    {
        if (!cols.TryGetValue(name, out var idx) || idx >= fields.Length)
            return 0;
        return long.TryParse(fields[idx].Trim('"', ' '), out var v) ? v : 0;
    }

    private static string GetStringField(string[] fields, Dictionary<string, int> cols, string name)
    {
        if (!cols.TryGetValue(name, out var idx) || idx >= fields.Length)
            return string.Empty;
        return fields[idx].Trim('"', ' ');
    }
}
