using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using KryossApi.Services.CopilotReadiness.Recommendations;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CopilotReadiness.Pipelines;

/// <summary>
/// Pre-computed Power Platform deployment insights.
/// Populated from the Power Platform admin APIs (api.bap.microsoft.com and api.flow.microsoft.com).
/// Requires Power Platform Administrator or Global Administrator role — gracefully degrades on 403.
/// </summary>
public class PowerPlatformInsights
{
    // --- Environments ---
    public int EnvironmentsTotal { get; set; }
    public int EnvironmentsProd { get; set; }
    public int EnvironmentsSandbox { get; set; }
    public int EnvironmentsDev { get; set; }
    public bool EnvironmentsAvailable { get; set; }

    // --- Flows (Power Automate) ---
    public int FlowsCloud { get; set; }
    public int FlowsDesktop { get; set; }
    public int FlowsCopilotPluginCandidates { get; set; }   // flows with HTTP trigger
    public int FlowsSuspended { get; set; }
    public bool FlowsAvailable { get; set; }

    // --- Canvas / Model-Driven Apps ---
    public int AppsCanvas { get; set; }
    public int AppsModelDriven { get; set; }
    public int AppsTeamsIntegrated { get; set; }
    public bool AppsAvailable { get; set; }

    // --- Connections ---
    public int ConnectionsPremium { get; set; }
    public int ConnectionsStandard { get; set; }
    public int EnterpriseConnectors { get; set; }   // SAP + Salesforce + ServiceNow + SQL count
    public bool HasSap { get; set; }
    public bool HasSalesforce { get; set; }
    public bool HasServiceNow { get; set; }
    public bool HasSql { get; set; }
    public bool ConnectionsAvailable { get; set; }

    // --- AI Builder Models ---
    public int AiModelsAiBuilder { get; set; }
    public int AiModelsAzureMl { get; set; }
    public int AiModelsCustom { get; set; }
    public int AiModelsTotal { get; set; }
    public bool AiModelsAvailable { get; set; }

    // --- DLP Policies ---
    public int DlpPoliciesTenantWide { get; set; }
    public int DlpPoliciesEnvLevel { get; set; }
    public bool DlpHttpBlocked { get; set; }
    public bool DlpCustomConnectorsBlocked { get; set; }
    public bool DlpPremiumBlocked { get; set; }
    public List<string> DlpRestrictedConnectors { get; set; } = [];
    public bool DlpAvailable { get; set; }

    // --- Capacity ---
    public double CapacityUsagePct { get; set; }
    public bool CapacityAvailable { get; set; }

    // --- Access status ---
    public string AccessStatus { get; set; } = "unknown"; // ok | permission_denied | error
}

/// <summary>
/// Power Platform pipeline: collects environment, flow, app, connector, AI model,
/// and DLP data from the Power Platform admin APIs, then generates Copilot readiness
/// recommendations. Gracefully degrades when the caller lacks Power Platform admin role.
/// </summary>
public static class PowerPlatformPipeline
{
    private const string BapBase = "https://api.bap.microsoft.com";
    private const string FlowBase = "https://api.flow.microsoft.com";

    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph,
        HttpClient bapHttp,
        HttpClient flowHttp,
        ILogger log,
        CancellationToken ct)
    {
        var insights = new PowerPlatformInsights();

        // SKU plan dictionary for license checks.
        var planStatuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Run license collection and Power Platform data in parallel.
        await Task.WhenAll(
            CollectSubscribedSkus(graph, planStatuses, log, ct),
            CollectPowerPlatformData(bapHttp, flowHttp, insights, log, ct)
        );

        // Determine final status before constructing result (Status is init-only).
        var finalStatus = insights.AccessStatus == "permission_denied" ? "partial" : "ok";
        var result = new PipelineResult { PipelineName = "power_platform", Status = finalStatus };

        // Generate recommendations.
        result.Findings.AddRange(PowerPlatformRecommendations.Generate(planStatuses, insights));

        // Emit metrics.
        result.Metrics["environments_total"] = insights.EnvironmentsTotal.ToString();
        result.Metrics["environments_prod"] = insights.EnvironmentsProd.ToString();
        result.Metrics["flows_cloud"] = insights.FlowsCloud.ToString();
        result.Metrics["flows_desktop"] = insights.FlowsDesktop.ToString();
        result.Metrics["flows_copilot_plugin_candidates"] = insights.FlowsCopilotPluginCandidates.ToString();
        result.Metrics["apps_canvas"] = insights.AppsCanvas.ToString();
        result.Metrics["apps_model_driven"] = insights.AppsModelDriven.ToString();
        result.Metrics["connections_premium"] = insights.ConnectionsPremium.ToString();
        result.Metrics["enterprise_connectors"] = insights.EnterpriseConnectors.ToString();
        result.Metrics["ai_models_total"] = insights.AiModelsTotal.ToString();
        result.Metrics["dlp_policies_tenant_wide"] = insights.DlpPoliciesTenantWide.ToString();
        result.Metrics["dlp_http_blocked"] = insights.DlpHttpBlocked.ToString().ToLowerInvariant();
        result.Metrics["access_status"] = insights.AccessStatus;
        result.Metrics["sku_plans_total"] = planStatuses.Count.ToString();

        return result;
    }

    // ================================================================
    // 1. Subscribed SKUs
    // ================================================================
    private static async Task CollectSubscribedSkus(
        GraphServiceClient graph,
        Dictionary<string, string> planStatuses,
        ILogger log, CancellationToken ct)
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
                    planStatuses.TryAdd(name, status);
                }
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Power Platform subscribed SKUs: 403");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Power Platform subscribed SKUs collection failed");
        }
    }

    // ================================================================
    // 2. Power Platform data (environments, flows, apps, connectors,
    //    AI models, DLP) — all via BAP / Flow admin APIs
    // ================================================================
    private static async Task CollectPowerPlatformData(
        HttpClient bap, HttpClient flow,
        PowerPlatformInsights ins, ILogger log, CancellationToken ct)
    {
        // First: get environments (required to form per-env URLs).
        string? environmentName = null;
        try
        {
            var envResp = await bap.GetAsync(
                "/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments?api-version=2023-06-01", ct);

            if (envResp.StatusCode == HttpStatusCode.Forbidden)
            {
                ins.AccessStatus = "permission_denied";
                log.LogInformation("Power Platform: 403 — Power Platform Administrator role required");
                return;
            }
            if (!envResp.IsSuccessStatusCode)
            {
                ins.AccessStatus = "error";
                log.LogWarning("Power Platform environments: HTTP {Status}", envResp.StatusCode);
                return;
            }

            var envJson = await envResp.Content.ReadAsStringAsync(ct);
            using var envDoc = JsonDocument.Parse(envJson);

            ins.AccessStatus = "ok";
            ins.EnvironmentsAvailable = true;

            if (!envDoc.RootElement.TryGetProperty("value", out var envArr))
                return;

            ins.EnvironmentsTotal = envArr.GetArrayLength();

            foreach (var env in envArr.EnumerateArray())
            {
                var props = env.TryGetProperty("properties", out var p) ? p : default;
                var envType = (props.ValueKind != JsonValueKind.Undefined &&
                               props.TryGetProperty("environmentType", out var et)) ? et.GetString() ?? "" : "";

                if (envType.Contains("Production", StringComparison.OrdinalIgnoreCase))
                    ins.EnvironmentsProd++;
                else if (envType.Contains("Sandbox", StringComparison.OrdinalIgnoreCase))
                    ins.EnvironmentsSandbox++;
                else if (envType.Contains("Developer", StringComparison.OrdinalIgnoreCase) ||
                         envType.Contains("Dev", StringComparison.OrdinalIgnoreCase))
                    ins.EnvironmentsDev++;

                // Prefer default env, then production, then first.
                if (environmentName is null || envType.Contains("Default", StringComparison.OrdinalIgnoreCase))
                    environmentName = env.TryGetProperty("name", out var n) ? n.GetString() : null;
                else if (environmentName is null && envType.Contains("Production", StringComparison.OrdinalIgnoreCase))
                    environmentName = env.TryGetProperty("name", out var n2) ? n2.GetString() : null;
            }

            // Pick any env if nothing matched above.
            if (environmentName is null && ins.EnvironmentsTotal > 0)
            {
                foreach (var env in envArr.EnumerateArray())
                {
                    environmentName = env.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (environmentName is not null) break;
                }
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            ins.AccessStatus = "permission_denied";
            log.LogInformation("Power Platform: 403 on environments — role required");
            return;
        }
        catch (Exception ex)
        {
            ins.AccessStatus = "error";
            log.LogWarning(ex, "Power Platform environments collection failed");
            return;
        }

        if (environmentName is null)
        {
            log.LogWarning("Power Platform: no environment found — skipping per-env calls");
            return;
        }

        // Now fetch all per-environment resources in parallel.
        var tasks = new List<Task>
        {
            CollectFlows(flow, environmentName, ins, log, ct),
            CollectConnections(bap, environmentName, ins, log, ct),
            CollectAiModels(bap, environmentName, ins, log, ct),
            CollectDlpPolicies(bap, environmentName, ins, log, ct),
            CollectCapacity(bap, environmentName, ins, log, ct),
        };

        await Task.WhenAll(tasks);
    }

    // ================================================================
    // 3. Flows (Power Automate) — Flow API
    // ================================================================
    private static async Task CollectFlows(
        HttpClient flowHttp, string envName,
        PowerPlatformInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var url = $"/providers/Microsoft.ProcessSimple/scopes/admin/environments/{envName}/v2/flows?api-version=2016-11-01-beta";
            var resp = await flowHttp.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogInformation("Power Platform flows: HTTP {Status}", resp.StatusCode);
                return;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            ins.FlowsAvailable = true;
            if (!doc.RootElement.TryGetProperty("value", out var arr)) return;

            foreach (var flow in arr.EnumerateArray())
            {
                var props = flow.TryGetProperty("properties", out var p) ? p : default;
                if (props.ValueKind == JsonValueKind.Undefined) continue;

                var flowType = props.TryGetProperty("flowType", out var ft) ? ft.GetString() ?? "" : "";
                var state = props.TryGetProperty("state", out var st) ? st.GetString() ?? "" : "";

                if (flowType.Contains("Desktop", StringComparison.OrdinalIgnoreCase) ||
                    flowType.Contains("Rpa", StringComparison.OrdinalIgnoreCase))
                    ins.FlowsDesktop++;
                else
                    ins.FlowsCloud++;

                if (state.Contains("Suspended", StringComparison.OrdinalIgnoreCase) ||
                    state.Contains("Stopped", StringComparison.OrdinalIgnoreCase))
                    ins.FlowsSuspended++;

                // Check for HTTP/Request trigger = Copilot plugin candidate.
                if (props.TryGetProperty("definitionSummary", out var def) &&
                    def.TryGetProperty("triggers", out var triggers) &&
                    triggers.GetArrayLength() > 0)
                {
                    var triggerType = triggers[0].TryGetProperty("type", out var tt)
                        ? tt.GetString() ?? "" : "";
                    if (triggerType.Contains("Http", StringComparison.OrdinalIgnoreCase) ||
                        triggerType.Contains("Request", StringComparison.OrdinalIgnoreCase))
                        ins.FlowsCopilotPluginCandidates++;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Power Platform flows collection failed");
        }
    }

    // ================================================================
    // 4. Connections (connectors) — BAP API
    // ================================================================
    private static async Task CollectConnections(
        HttpClient bap, string envName,
        PowerPlatformInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var url = $"/providers/Microsoft.PowerApps/scopes/admin/environments/{envName}/connections?api-version=2016-11-01";
            var resp = await bap.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogInformation("Power Platform connections: HTTP {Status}", resp.StatusCode);
                return;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            ins.ConnectionsAvailable = true;
            if (!doc.RootElement.TryGetProperty("value", out var arr)) return;

            var premiumKeywords = new[] { "sap", "salesforce", "servicenow", "dynamics", "oracle", "http", "custom" };

            foreach (var conn in arr.EnumerateArray())
            {
                var apiId = "";
                if (conn.TryGetProperty("properties", out var props) &&
                    props.TryGetProperty("apiId", out var api))
                    apiId = api.GetString()?.ToLowerInvariant() ?? "";

                bool isPremium = premiumKeywords.Any(k => apiId.Contains(k));
                if (isPremium) ins.ConnectionsPremium++;
                else ins.ConnectionsStandard++;

                if (apiId.Contains("sap")) { ins.HasSap = true; ins.EnterpriseConnectors++; }
                if (apiId.Contains("salesforce")) { ins.HasSalesforce = true; ins.EnterpriseConnectors++; }
                if (apiId.Contains("servicenow")) { ins.HasServiceNow = true; ins.EnterpriseConnectors++; }
                if (apiId.Contains("sql")) { ins.HasSql = true; ins.EnterpriseConnectors++; }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Power Platform connections collection failed");
        }
    }

    // ================================================================
    // 5. AI Builder Models — BAP API
    // ================================================================
    private static async Task CollectAiModels(
        HttpClient bap, string envName,
        PowerPlatformInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var url = $"/providers/Microsoft.PowerApps/environments/{envName}/aiModels?api-version=2024-05-01";
            var resp = await bap.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogInformation("Power Platform AI models: HTTP {Status}", resp.StatusCode);
                return;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            ins.AiModelsAvailable = true;
            if (!doc.RootElement.TryGetProperty("value", out var arr)) return;

            foreach (var model in arr.EnumerateArray())
            {
                var modelType = "";
                if (model.TryGetProperty("properties", out var props) &&
                    props.TryGetProperty("modelType", out var mt))
                    modelType = mt.GetString()?.ToLowerInvariant() ?? "";

                if (modelType.Contains("aibuilder")) ins.AiModelsAiBuilder++;
                else if (modelType.Contains("azureml")) ins.AiModelsAzureMl++;
                else ins.AiModelsCustom++;

                ins.AiModelsTotal++;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Power Platform AI models collection failed");
        }
    }

    // ================================================================
    // 6. DLP Policies — BAP API
    // ================================================================
    private static async Task CollectDlpPolicies(
        HttpClient bap, string envName,
        PowerPlatformInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var url = $"/providers/Microsoft.BusinessAppPlatform/environments/{envName}/dlpPolicies?api-version=2024-05-01";
            var resp = await bap.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogInformation("Power Platform DLP policies: HTTP {Status}", resp.StatusCode);
                return;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            ins.DlpAvailable = true;
            if (!doc.RootElement.TryGetProperty("value", out var arr)) return;

            foreach (var policy in arr.EnumerateArray())
            {
                var scope = "";
                if (policy.TryGetProperty("properties", out var props) &&
                    props.TryGetProperty("scope", out var sc))
                    scope = sc.GetString()?.ToLowerInvariant() ?? "";

                if (scope.Contains("tenant") || scope == "/")
                    ins.DlpPoliciesTenantWide++;
                else
                    ins.DlpPoliciesEnvLevel++;

                // Check blocked connectors (HBI group = "Blocked").
                if (policy.TryGetProperty("properties", out var p2) &&
                    p2.TryGetProperty("connectorGroups", out var groups) &&
                    groups.TryGetProperty("hbi", out var hbi) &&
                    hbi.TryGetProperty("classification", out var blocked))
                {
                    foreach (var conn in blocked.EnumerateArray())
                    {
                        var id = conn.TryGetProperty("id", out var cid)
                            ? cid.GetString()?.ToLowerInvariant() ?? "" : "";

                        if (id.Contains("http")) { ins.DlpHttpBlocked = true; ins.DlpRestrictedConnectors.Add("HTTP"); }
                        if (id.Contains("custom")) { ins.DlpCustomConnectorsBlocked = true; ins.DlpRestrictedConnectors.Add("Custom Connectors"); }
                        if (id.Contains("premium") || id.Contains("salesforce") ||
                            id.Contains("sap") || id.Contains("servicenow"))
                        {
                            ins.DlpPremiumBlocked = true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Power Platform DLP policies collection failed");
        }
    }

    // ================================================================
    // 7. Capacity — BAP API
    // ================================================================
    private static async Task CollectCapacity(
        HttpClient bap, string envName,
        PowerPlatformInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var url = $"/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments/{envName}/capacity?api-version=2020-10-01";
            var resp = await bap.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            ins.CapacityAvailable = true;

            if (doc.RootElement.TryGetProperty("storage", out var storage))
            {
                var dbCap = storage.TryGetProperty("databaseCapacity", out var dc) ? dc.GetDouble() : 0;
                var dbUsed = storage.TryGetProperty("databaseUsed", out var du) ? du.GetDouble() : 0;
                if (dbCap > 0)
                    ins.CapacityUsagePct = Math.Round(dbUsed * 100.0 / dbCap, 1);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Power Platform capacity collection failed");
        }
    }
}
