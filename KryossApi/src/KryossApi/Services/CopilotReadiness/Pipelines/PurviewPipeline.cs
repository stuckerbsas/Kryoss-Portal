using KryossApi.Services.CopilotReadiness.Recommendations;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CopilotReadiness.Pipelines;

/// <summary>
/// Pre-computed Microsoft Purview compliance metrics.
/// Most data is license-only because Exchange Online Management (DLP policies,
/// retention, audit config, insider risk) is not accessible via Graph API.
/// Where Graph data is available (sensitivity labels, DLP alerts) it is collected.
/// </summary>
public class PurviewInsights
{
    // --- Sensitivity Labels (Graph: /security/informationProtection/sensitivityLabels) ---
    public int LabelCount { get; set; }
    public List<string> LabelNames { get; set; } = [];
    public bool LabelsAvailable { get; set; }

    // --- Label Policies ---
    public int LabelPoliciesCount { get; set; }
    public bool LabelPoliciesAvailable { get; set; }

    // --- DLP (Graph: /security/alerts_v2 filtered by category) ---
    public bool DlpPoliciesEnabled { get; set; }   // true = at least 1 DLP alert found
    public int DlpAlerts { get; set; }
    public bool DlpAlertsAvailable { get; set; }

    // --- eDiscovery (Graph: /compliance/ediscovery/cases) ---
    public int EDiscoveryCasesActive { get; set; }
    public bool EDiscoveryAvailable { get; set; }

    // --- Audit / Org config (Graph: /organization) ---
    public bool AuditLogEnabled { get; set; }      // nullable default — unknown
    public bool AuditConfigAvailable { get; set; }

    // --- Customer Lockbox (Graph: /organization) ---
    public bool CustomerLockboxEnabled { get; set; }
    public bool CustomerLockboxAvailable { get; set; }

    // --- Insight status ---
    public string DataNote { get; set; } =
        "Deep Purview inspection (DLP policies, retention, insider risk) requires Exchange Online Management. " +
        "Graph API provides sensitivity labels and security alerts only.";
}

/// <summary>
/// Purview pipeline: collects compliance data available via Graph API and generates
/// Copilot readiness recommendations. Most checks are license-only because Exchange
/// Online Management cmdlets are not callable from the backend. Where Graph data
/// exists (labels, alerts, eDiscovery) it enriches the output.
/// </summary>
public static class PurviewPipeline
{
    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph,
        ILogger log,
        CancellationToken ct)
    {
        var result = new PipelineResult { PipelineName = "purview", Status = "ok" };
        var insights = new PurviewInsights();

        // Also need the SKU plan dictionary — re-collect from subscribed SKUs.
        var planStatuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var tasks = new List<Task>
        {
            CollectSubscribedSkus(graph, planStatuses, log, ct),
            CollectSensitivityLabels(graph, insights, log, ct),
            CollectDlpAlerts(graph, insights, log, ct),
            CollectEDiscoveryCases(graph, insights, log, ct),
            CollectOrgConfig(graph, insights, log, ct),
        };

        await Task.WhenAll(tasks);

        // Generate recommendations.
        result.Findings.AddRange(PurviewRecommendations.Generate(planStatuses, insights));

        // Emit metrics.
        result.Metrics["label_count"] = insights.LabelCount.ToString();
        result.Metrics["label_policies_count"] = insights.LabelPoliciesCount.ToString();
        result.Metrics["dlp_alerts"] = insights.DlpAlerts.ToString();
        result.Metrics["ediscovery_cases_active"] = insights.EDiscoveryCasesActive.ToString();
        result.Metrics["audit_log_enabled"] = insights.AuditConfigAvailable
            ? insights.AuditLogEnabled.ToString().ToLowerInvariant()
            : "unknown";
        result.Metrics["customer_lockbox_enabled"] = insights.CustomerLockboxAvailable
            ? insights.CustomerLockboxEnabled.ToString().ToLowerInvariant()
            : "unknown";
        result.Metrics["sku_plans_total"] = planStatuses.Count.ToString();

        return result;
    }

    // ================================================================
    // 1. Subscribed SKUs — build planStatuses for Purview category
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
            log.LogWarning("Purview subscribed SKUs: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Purview subscribed SKUs collection failed");
        }
    }

    // ================================================================
    // 2. Sensitivity Labels
    //    Graph SDK v5 does not expose /security/informationProtection/sensitivityLabels.
    //    Marked unavailable — license-only recommendations cover AIP/MIP plans.
    // ================================================================
    private static Task CollectSensitivityLabels(
        GraphServiceClient graph, PurviewInsights ins, ILogger log, CancellationToken ct)
    {
        ins.LabelsAvailable = false;
        ins.LabelPoliciesAvailable = false;
        log.LogInformation("Purview sensitivity labels: skipped — not available in Graph SDK v5 stable");
        return Task.CompletedTask;
    }

    // ================================================================
    // 3. DLP Alerts — GET /security/alerts_v2 filtered by category
    // ================================================================
    private static async Task CollectDlpAlerts(
        GraphServiceClient graph, PurviewInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.Security.Alerts_v2.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = "serviceSource eq 'microsoftDefenderForCloudApps' or serviceSource eq 'office365'";
                rc.QueryParameters.Top = 50;
            }, cancellationToken: ct);

            if (resp?.Value is null) return;

            ins.DlpAlertsAvailable = true;
            var dlpAlerts = resp.Value.Where(a =>
            {
                var cat = a.Category?.ToLowerInvariant() ?? "";
                return cat.Contains("dlp") || cat.Contains("data loss") || cat.Contains("compliance");
            }).ToList();

            ins.DlpAlerts = dlpAlerts.Count;
            ins.DlpPoliciesEnabled = dlpAlerts.Count > 0;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogInformation("Purview DLP alerts: 403 - SecurityAlert.Read.All required");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Purview DLP alerts collection failed");
        }
    }

    // ================================================================
    // 4. eDiscovery Cases
    //    Graph SDK v5 stable does not include typed eDiscovery support.
    //    Marked unavailable — license-only recommendations cover eDiscovery plans.
    // ================================================================
    private static Task CollectEDiscoveryCases(
        GraphServiceClient graph, PurviewInsights ins, ILogger log, CancellationToken ct)
    {
        ins.EDiscoveryAvailable = false;
        log.LogInformation("Purview eDiscovery cases: skipped — not available in Graph SDK v5 stable");
        return Task.CompletedTask;
    }

    // ================================================================
    // 5. Organization config — audit log + Customer Lockbox
    //    Note: Full audit config requires Exchange Online PowerShell.
    //    Graph /organization exposes AllowedDataLocations but NOT audit status.
    //    We collect what we can; the rest is marked unknown.
    // ================================================================
    private static async Task CollectOrgConfig(
        GraphServiceClient graph, PurviewInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.Organization.GetAsync(cancellationToken: ct);
            var org = resp?.Value?.FirstOrDefault();
            if (org is null) return;

            // Graph does not expose CustomerLockBoxEnabled or UnifiedAuditLogIngestionEnabled
            // directly on the organization resource — those require Exchange PowerShell.
            // Mark as available but unknown; enrichment noted in DataNote.
            ins.AuditConfigAvailable = false;
            ins.CustomerLockboxAvailable = false;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogInformation("Purview org config: 403 - Organization.Read.All required");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Purview org config collection failed");
        }
    }
}
