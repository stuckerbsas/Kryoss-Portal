using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KryossApi.Services.CopilotReadiness.Recommendations;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CopilotReadiness.Pipelines;

/// <summary>
/// Pre-computed Defender security metrics extracted from Graph Security API
/// and Microsoft 365 Defender REST API. Built once per assessment run,
/// shared across all recommendation generators.
/// </summary>
public class DefenderInsights
{
    // --- Alerts ---
    public Dictionary<string, int> AlertsByCategory { get; set; } = new();
    public int PhishingAlerts { get; set; }
    public int MalwareAlerts { get; set; }
    public int TotalAlerts { get; set; }
    public int HighSeverityAlerts { get; set; }

    // --- Incidents ---
    public int IncidentsTotal { get; set; }
    public int IncidentsActive { get; set; }
    public int IncidentsHighSeverity { get; set; }

    // --- Secure Score ---
    public double SecureScoreCurrent { get; set; }
    public double SecureScoreMax { get; set; }
    public double SecureScorePct { get; set; }
    public int IdentityControlsImplemented { get; set; }
    public int DataControlsImplemented { get; set; }
    public int CopilotRelevantControls { get; set; }

    // --- Risky Users ---
    public int RiskyUsersHigh { get; set; }
    public int ConfirmedCompromised { get; set; }
    public int RiskySignInsHigh { get; set; }

    // --- OAuth ---
    public int HighRiskApps { get; set; }
    public int OverPrivilegedApps { get; set; }

    // --- Defender Machines ---
    public int DevicesTotal { get; set; }
    public int DevicesHighRisk { get; set; }
    public int DevicesMediumRisk { get; set; }
    public int DevicesLowRisk { get; set; }

    // --- Vulnerabilities ---
    public int VulnCritical { get; set; }
    public int VulnHigh { get; set; }
    public int VulnMedium { get; set; }
    public int VulnLow { get; set; }

    // --- Email ---
    public int PhishingEmails { get; set; }
    public int MalwareEmails { get; set; }
    public int SpamEmails { get; set; }

    // --- Advanced Hunting ---
    public int CopilotProcessEvents { get; set; }
    public int CopilotNetworkEvents { get; set; }
    public int CopilotFileAccessEvents { get; set; }
    public int AiPhishingEmails { get; set; }

    // --- Exposure ---
    public double ExposureScore { get; set; }
    public string ExposureRisk { get; set; } = "Unknown";

    // --- Software ---
    public int TotalSoftwareApps { get; set; }
    public int VulnerableApps { get; set; }
    public int CopilotRelatedApps { get; set; }

    // --- Recommendations ---
    public int RecommendationsTotal { get; set; }
    public int RecommendationsCritical { get; set; }
    public int CopilotRelevantRecommendations { get; set; }

    // --- Availability flags ---
    public bool GraphSecurityAvailable { get; set; }
    public bool DefenderApiAvailable { get; set; }
    public bool Available => GraphSecurityAvailable || DefenderApiAvailable;
    public bool ActivationNeeded { get; set; }
}

/// <summary>
/// Defender pipeline: collects security data from Graph Security API
/// (via GraphServiceClient) and Microsoft 365 Defender REST API (via HttpClient).
/// Feeds D5 scoring (Zero Trust dimension).
/// </summary>
public static class DefenderPipeline
{
    private const string DefenderApiBase = "https://api.security.microsoft.com";

    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph,
        HttpClient defenderHttp,
        ILogger log,
        CancellationToken ct)
    {
        var result = new PipelineResult { PipelineName = "defender", Status = "ok" };
        var insights = new DefenderInsights();

        // Run Graph Security + Defender API collectors in parallel.
        var tasks = new List<Task>
        {
            CollectAlerts(graph, insights, log, ct),
            CollectIncidents(graph, insights, log, ct),
            CollectSecureScore(graph, insights, log, ct),
            CollectRiskyUsers(graph, insights, log, ct),
            CollectOAuthGrants(graph, insights, log, ct),
            CollectDefenderIncidents(defenderHttp, insights, log, ct),
            CollectDefenderMachines(defenderHttp, insights, log, ct),
            CollectDefenderVulnerabilities(defenderHttp, insights, log, ct),
            CollectAdvancedHunting(defenderHttp, insights, log, ct),
            CollectEmailThreats(defenderHttp, insights, log, ct),
            CollectRecommendations(defenderHttp, insights, log, ct),
            CollectSoftware(defenderHttp, insights, log, ct),
            CollectExposureScore(defenderHttp, insights, log, ct),
        };

        await Task.WhenAll(tasks);

        // If neither API surface returned data, mark partial.
        if (!insights.GraphSecurityAvailable && !insights.DefenderApiAvailable)
            result = new PipelineResult { PipelineName = "defender", Status = "partial" };

        // Generate recommendations from collected data.
        result.Findings.AddRange(DefenderRecommendations.Generate(insights));

        // Extract key metrics for scoring.
        result.Metrics["total_alerts"] = insights.TotalAlerts.ToString();
        result.Metrics["high_severity_alerts"] = insights.HighSeverityAlerts.ToString();
        result.Metrics["incidents_total"] = insights.IncidentsTotal.ToString();
        result.Metrics["incidents_active"] = insights.IncidentsActive.ToString();
        result.Metrics["incidents_high_severity"] = insights.IncidentsHighSeverity.ToString();
        result.Metrics["secure_score_pct"] = insights.SecureScorePct.ToString("F1");
        result.Metrics["risky_users_high"] = insights.RiskyUsersHigh.ToString();
        result.Metrics["confirmed_compromised"] = insights.ConfirmedCompromised.ToString();
        result.Metrics["high_risk_apps"] = insights.HighRiskApps.ToString();
        result.Metrics["devices_total"] = insights.DevicesTotal.ToString();
        result.Metrics["devices_high_risk"] = insights.DevicesHighRisk.ToString();
        result.Metrics["vuln_critical"] = insights.VulnCritical.ToString();
        result.Metrics["exposure_score"] = insights.ExposureScore.ToString("F1");
        result.Metrics["exposure_risk"] = insights.ExposureRisk;
        result.Metrics["copilot_process_events"] = insights.CopilotProcessEvents.ToString();
        result.Metrics["copilot_network_events"] = insights.CopilotNetworkEvents.ToString();
        result.Metrics["copilot_file_access_events"] = insights.CopilotFileAccessEvents.ToString();
        result.Metrics["ai_phishing_emails"] = insights.AiPhishingEmails.ToString();
        result.Metrics["recommendations_total"] = insights.RecommendationsTotal.ToString();
        result.Metrics["recommendations_critical"] = insights.RecommendationsCritical.ToString();

        return result;
    }

    // ================================================================
    // Graph Security API collectors
    // ================================================================

    // 1. Security Alerts v2
    private static async Task CollectAlerts(
        GraphServiceClient graph, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.Security.Alerts_v2.GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;

            ins.GraphSecurityAvailable = true;
            ins.TotalAlerts = resp.Value.Count;

            foreach (var alert in resp.Value)
            {
                var severity = alert.Severity?.ToString()?.ToLowerInvariant() ?? "";
                if (severity.Contains("high")) ins.HighSeverityAlerts++;

                var category = alert.Category ?? "Unknown";
                ins.AlertsByCategory.TryGetValue(category, out var count);
                ins.AlertsByCategory[category] = count + 1;

                if (category.Equals("Phishing", StringComparison.OrdinalIgnoreCase))
                    ins.PhishingAlerts++;
                if (category.Equals("Malware", StringComparison.OrdinalIgnoreCase))
                    ins.MalwareAlerts++;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Defender alerts: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender alerts collection failed");
        }
    }

    // 2. Security Incidents
    private static async Task CollectIncidents(
        GraphServiceClient graph, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.Security.Incidents.GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;

            ins.GraphSecurityAvailable = true;
            ins.IncidentsTotal = resp.Value.Count;

            foreach (var inc in resp.Value)
            {
                var status = inc.Status?.ToString()?.ToLowerInvariant() ?? "";
                if (status is "active" or "new" or "inprogress")
                    ins.IncidentsActive++;

                var severity = inc.Severity?.ToString()?.ToLowerInvariant() ?? "";
                if (severity.Contains("high"))
                    ins.IncidentsHighSeverity++;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Defender incidents: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender incidents collection failed");
        }
    }

    // 3. Secure Scores + Control Profiles
    private static async Task CollectSecureScore(
        GraphServiceClient graph, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            // Latest secure score
            var scoreResp = await graph.Security.SecureScores.GetAsync(cancellationToken: ct);
            if (scoreResp?.Value is { Count: > 0 })
            {
                ins.GraphSecurityAvailable = true;
                var latest = scoreResp.Value[0];
                ins.SecureScoreCurrent = latest.CurrentScore ?? 0;
                ins.SecureScoreMax = latest.MaxScore ?? 0;
                ins.SecureScorePct = ins.SecureScoreMax > 0
                    ? ins.SecureScoreCurrent / ins.SecureScoreMax * 100
                    : 0;
            }

            // Score control profiles
            var ctrlResp = await graph.Security.SecureScoreControlProfiles
                .GetAsync(cancellationToken: ct);
            if (ctrlResp?.Value is not null)
            {
                var identityKw = new[] { "identity", "authentication", "mfa", "conditional" };
                var dataKw = new[] { "data", "dlp", "encryption", "information" };
                var copilotKw = new[] { "copilot", "ai", "agent", "m365", "office365", "sharepoint", "teams" };

                foreach (var ctrl in ctrlResp.Value)
                {
                    var cat = ctrl.ControlCategory?.ToLowerInvariant() ?? "";
                    var name = ctrl.Title?.ToLowerInvariant() ?? "";
                    var status = (ctrl.AdditionalData?.TryGetValue("implementationStatus", out var s) == true
                        ? s?.ToString()?.ToLowerInvariant() : null) ?? "";

                    bool implemented = status == "implemented";

                    if (identityKw.Any(k => cat.Contains(k) || name.Contains(k)) && implemented)
                        ins.IdentityControlsImplemented++;

                    if (dataKw.Any(k => cat.Contains(k) || name.Contains(k)) && implemented)
                        ins.DataControlsImplemented++;

                    if (copilotKw.Any(k => name.Contains(k)))
                        ins.CopilotRelevantControls++;
                }
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Defender secure score: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender secure score collection failed");
        }
    }

    // 4. Risky Users (Identity Protection)
    private static async Task CollectRiskyUsers(
        GraphServiceClient graph, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.IdentityProtection.RiskyUsers
                .GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;

            ins.GraphSecurityAvailable = true;

            foreach (var u in resp.Value)
            {
                var level = u.RiskLevel?.ToString()?.ToLowerInvariant() ?? "";
                var state = u.RiskState?.ToString()?.ToLowerInvariant() ?? "";

                if (level.Contains("high")) ins.RiskyUsersHigh++;
                if (state.Contains("confirmedcompromised")) ins.ConfirmedCompromised++;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Defender risky users: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender risky users collection failed");
        }
    }

    // 5. OAuth Permission Grants
    private static async Task CollectOAuthGrants(
        GraphServiceClient graph, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.Oauth2PermissionGrants.GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;

            ins.GraphSecurityAvailable = true;

            var highPrivScopes = new[] { "mail.readwrite", "files.readwrite.all", "sites.readwrite.all", "user.readwrite.all" };
            var appsByClient = new Dictionary<string, HashSet<string>>();

            foreach (var g in resp.Value)
            {
                var clientId = g.ClientId ?? "unknown";
                var scope = (g.Scope ?? "").ToLowerInvariant();
                var scopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (!appsByClient.TryGetValue(clientId, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    appsByClient[clientId] = set;
                }
                foreach (var s in scopes) set.Add(s);
            }

            foreach (var (_, scopes) in appsByClient)
            {
                if (highPrivScopes.Any(hp => scopes.Contains(hp)))
                    ins.HighRiskApps++;
                if (scopes.Count > 10)
                    ins.OverPrivilegedApps++;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Defender OAuth grants: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender OAuth grants collection failed");
        }
    }

    // ================================================================
    // Microsoft 365 Defender REST API collectors
    // ================================================================

    // 6. Defender Incidents (more detailed)
    private static async Task CollectDefenderIncidents(
        HttpClient http, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync("/api/incidents", ct);
            if (!HandleDefenderResponse(resp, "incidents", ins, log)) return;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            ins.DefenderApiAvailable = true;

            // Use Defender incident count if Graph didn't provide data.
            if (ins.IncidentsTotal == 0)
                ins.IncidentsTotal = items.GetArrayLength();

            foreach (var item in items.EnumerateArray())
            {
                var status = item.TryGetProperty("status", out var st) ? st.GetString()?.ToLowerInvariant() ?? "" : "";
                if (status is "active" or "inprogress" or "new" && ins.IncidentsActive == 0)
                    ins.IncidentsActive++;

                var severity = item.TryGetProperty("severity", out var sev) ? sev.GetString()?.ToLowerInvariant() ?? "" : "";
                if (severity == "high" && ins.IncidentsHighSeverity == 0)
                    ins.IncidentsHighSeverity++;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender API incidents collection failed");
        }
    }

    // 7. Machines / Devices
    private static async Task CollectDefenderMachines(
        HttpClient http, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync("/api/machines", ct);
            if (!HandleDefenderResponse(resp, "machines", ins, log)) return;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            ins.DefenderApiAvailable = true;
            ins.DevicesTotal = items.GetArrayLength();

            foreach (var m in items.EnumerateArray())
            {
                var risk = m.TryGetProperty("riskScore", out var r) ? r.GetString() ?? "" : "";
                switch (risk)
                {
                    case "High": ins.DevicesHighRisk++; break;
                    case "Medium": ins.DevicesMediumRisk++; break;
                    case "Low": ins.DevicesLowRisk++; break;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender API machines collection failed");
        }
    }

    // 8. Vulnerabilities
    private static async Task CollectDefenderVulnerabilities(
        HttpClient http, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync("/api/vulnerabilities", ct);
            if (!HandleDefenderResponse(resp, "vulnerabilities", ins, log)) return;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            ins.DefenderApiAvailable = true;

            foreach (var v in items.EnumerateArray())
            {
                var severity = v.TryGetProperty("severity", out var s) ? s.GetString()?.ToLowerInvariant() ?? "" : "";
                switch (severity)
                {
                    case "critical": ins.VulnCritical++; break;
                    case "high": ins.VulnHigh++; break;
                    case "medium": ins.VulnMedium++; break;
                    case "low": ins.VulnLow++; break;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender API vulnerabilities collection failed");
        }
    }

    // 9. Advanced Hunting (4 KQL queries)
    private static async Task CollectAdvancedHunting(
        HttpClient http, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        // Query 1: Copilot process events
        const string processQuery = """
            DeviceProcessEvents
            | where Timestamp > ago(30d)
            | where ProcessCommandLine has_any ('copilot', 'bing chat', 'edge://copilot', 'teams copilot')
                or InitiatingProcessCommandLine has_any ('copilot', 'ai assistant')
            | where ActionType in ('ProcessCreated', 'ScriptExecution')
            | summarize TotalEvents=count(), UniqueDevices=dcount(DeviceName)
                by DeviceName, AccountName
            | where TotalEvents > 5
            | order by TotalEvents desc
            | limit 100
            """;

        // Query 2: Copilot network events
        const string networkQuery = """
            DeviceNetworkEvents
            | where Timestamp > ago(30d)
            | where RemoteUrl has_any ('openai.com', 'copilot', 'bing.com/chat', 'ai.azure.com')
                or InitiatingProcessFileName has_any ('Teams.exe', 'msedge.exe', 'powerpnt.exe', 'winword.exe', 'excel.exe')
            | where ActionType in ('ConnectionSuccess', 'ConnectionRequest')
            | summarize TotalConnections=count(), UniqueURLs=dcount(RemoteUrl),
                DataSent=sum(tolong(RemoteSentBytes))
                by DeviceName, InitiatingProcessFileName
            | extend DataSentMB = DataSent / 1024 / 1024
            | where DataSentMB > 10 or TotalConnections > 100
            | order by DataSentMB desc
            | limit 100
            """;

        // Query 3: Copilot file access
        const string fileQuery = """
            DeviceFileEvents
            | where Timestamp > ago(30d)
            | where InitiatingProcessFileName has_any ('Teams.exe', 'msedge.exe', 'OneDrive.exe')
            | where SensitivityLabel in ('Highly Confidential', 'Confidential', 'Secret')
                or FileName endswith_any ('.docx', '.xlsx', '.pptx', '.pdf')
            | where ActionType in ('FileCreated', 'FileModified', 'FileRenamed', 'FileCopied')
            | summarize TotalFileOperations=count(), UniqueSensitiveFiles=dcount(FileName)
                by DeviceName, AccountName, InitiatingProcessFileName
            | where TotalFileOperations > 20
            | order by UniqueSensitiveFiles desc
            | limit 100
            """;

        // Query 4: AI-themed phishing emails
        const string emailQuery = """
            EmailEvents
            | where Timestamp > ago(30d)
            | where Subject has_any ('copilot', 'AI assistant', 'chatgpt', 'openai', 'artificial intelligence')
                or EmailDirection == 'Inbound'
            | where ThreatTypes has_any ('Phish', 'Malware', 'Spam')
            | summarize TotalThreats=count(),
                PhishingAttempts=countif(ThreatTypes has 'Phish')
                by RecipientEmailAddress
            | order by TotalThreats desc
            | limit 100
            """;

        var queries = new (string Name, string Kql, Action<DefenderInsights, JsonElement> Apply)[]
        {
            ("copilot_processes", processQuery, (i, results) =>
            {
                foreach (var r in results.EnumerateArray())
                {
                    i.CopilotProcessEvents += r.TryGetProperty("TotalEvents", out var te) ? te.GetInt32() : 0;
                }
            }),
            ("copilot_network", networkQuery, (i, results) =>
            {
                foreach (var r in results.EnumerateArray())
                {
                    i.CopilotNetworkEvents += r.TryGetProperty("TotalConnections", out var tc) ? tc.GetInt32() : 0;
                }
            }),
            ("copilot_files", fileQuery, (i, results) =>
            {
                foreach (var r in results.EnumerateArray())
                {
                    i.CopilotFileAccessEvents += r.TryGetProperty("UniqueSensitiveFiles", out var uf) ? uf.GetInt32() : 0;
                }
            }),
            ("copilot_emails", emailQuery, (i, results) =>
            {
                foreach (var r in results.EnumerateArray())
                {
                    i.AiPhishingEmails += r.TryGetProperty("PhishingAttempts", out var pa) ? pa.GetInt32() : 0;
                }
            }),
        };

        var huntingTasks = queries.Select(q => RunHuntingQuery(http, q.Name, q.Kql, q.Apply, ins, log, ct));
        await Task.WhenAll(huntingTasks);
    }

    private static async Task RunHuntingQuery(
        HttpClient http, string name, string kql,
        Action<DefenderInsights, JsonElement> apply,
        DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var payload = new { Query = kql };
            var resp = await http.PostAsJsonAsync("/api/advancedhunting/run", payload, ct);
            if (!HandleDefenderResponse(resp, $"hunting_{name}", ins, log)) return;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("Results", out var results))
            {
                ins.DefenderApiAvailable = true;
                apply(ins, results);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Advanced Hunting query '{Name}' failed", name);
        }
    }

    // 10. Email post-delivery detections
    private static async Task CollectEmailThreats(
        HttpClient http, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var url = $"/api/EmailPostDeliveryDetections?$filter=DetectionTime ge {cutoff}";
            var resp = await http.GetAsync(url, ct);
            if (!HandleDefenderResponse(resp, "email_threats", ins, log)) return;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            ins.DefenderApiAvailable = true;

            foreach (var e in items.EnumerateArray())
            {
                var type = e.TryGetProperty("threatType", out var tt) ? tt.GetString()?.ToLowerInvariant() ?? "" : "";
                if (type.Contains("phish")) ins.PhishingEmails++;
                if (type.Contains("malware")) ins.MalwareEmails++;
                if (type.Contains("spam")) ins.SpamEmails++;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender API email threats collection failed");
        }
    }

    // 11. Security Recommendations
    private static async Task CollectRecommendations(
        HttpClient http, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync("/api/recommendations", ct);
            if (!HandleDefenderResponse(resp, "recommendations", ins, log)) return;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            ins.DefenderApiAvailable = true;
            ins.RecommendationsTotal = items.GetArrayLength();

            var copilotKw = new[] { "microsoft 365", "m365", "office", "teams", "sharepoint", "onedrive", "exchange", "identity", "authentication", "mfa" };

            foreach (var r in items.EnumerateArray())
            {
                var severity = r.TryGetProperty("severity", out var s) ? s.GetString()?.ToLowerInvariant() ?? "" : "";
                if (severity == "critical") ins.RecommendationsCritical++;

                var recName = r.TryGetProperty("recommendationName", out var rn) ? rn.GetString()?.ToLowerInvariant() ?? "" : "";
                var recCat = r.TryGetProperty("recommendationCategory", out var rc) ? rc.GetString()?.ToLowerInvariant() ?? "" : "";
                if (copilotKw.Any(kw => recName.Contains(kw) || recCat.Contains(kw)))
                    ins.CopilotRelevantRecommendations++;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender API recommendations collection failed");
        }
    }

    // 12. Software Inventory
    private static async Task CollectSoftware(
        HttpClient http, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync("/api/Software", ct);
            if (!HandleDefenderResponse(resp, "software", ins, log)) return;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            ins.DefenderApiAvailable = true;
            ins.TotalSoftwareApps = items.GetArrayLength();

            var copilotAppKw = new[] { "microsoft teams", "microsoft 365", "edge", "copilot", "office" };

            foreach (var app in items.EnumerateArray())
            {
                var name = app.TryGetProperty("name", out var n) ? n.GetString()?.ToLowerInvariant() ?? "" : "";
                var vendor = app.TryGetProperty("vendor", out var v) ? v.GetString()?.ToLowerInvariant() ?? "" : "";
                var weaknesses = app.TryGetProperty("numberOfWeaknesses", out var w) ? w.GetInt32() : 0;

                if (copilotAppKw.Any(kw => name.Contains(kw) || vendor.Contains(kw)))
                    ins.CopilotRelatedApps++;
                if (weaknesses > 0)
                    ins.VulnerableApps++;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender API software collection failed");
        }
    }

    // 13. Exposure Score
    private static async Task CollectExposureScore(
        HttpClient http, DefenderInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync("/api/exposureScore", ct);
            if (!HandleDefenderResponse(resp, "exposure_score", ins, log)) return;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            ins.DefenderApiAvailable = true;
            ins.ExposureScore = root.TryGetProperty("score", out var s) ? s.GetDouble() : 0;
            ins.ExposureRisk = ins.ExposureScore switch
            {
                <= 30 => "Low",
                <= 60 => "Medium",
                _ => "High"
            };
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Defender API exposure score collection failed");
        }
    }

    // ================================================================
    // Helpers
    // ================================================================

    /// <summary>
    /// Handles common HTTP error codes for Defender API responses.
    /// Returns true if the response is OK and should be processed.
    /// </summary>
    private static bool HandleDefenderResponse(
        HttpResponseMessage resp, string endpoint, DefenderInsights ins, ILogger log)
    {
        if (resp.StatusCode == HttpStatusCode.OK)
            return true;

        if (resp.StatusCode == HttpStatusCode.Forbidden)
        {
            ins.ActivationNeeded = true;
            log.LogWarning("Defender API ({Endpoint}): 403 - insufficient permissions or no devices onboarded", endpoint);
            return false;
        }

        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            log.LogWarning("Defender API ({Endpoint}): 404 - not available in current SKU", endpoint);
            return false;
        }

        log.LogWarning("Defender API ({Endpoint}): HTTP {StatusCode}", endpoint, (int)resp.StatusCode);
        return false;
    }
}
