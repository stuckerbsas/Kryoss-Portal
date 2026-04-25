using System.Globalization;
using KryossApi.Services.CloudAssessment.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;

namespace KryossApi.Services.CloudAssessment.Recommendations;

public static class PowerBiRecommendations
{
    private const string Svc = "powerbi";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static List<RecommendationResult> Generate(PowerBiInsights ins)
    {
        var all = new List<RecommendationResult>();

        // ── Connection ──
        if (!ins.ApiAccessible)
        {
            all.Add(RecommendationResult.ActionRequired(Svc, "admin-api-access",
                observation: ins.ConnectionError ?? "Power BI Admin API is not accessible.",
                recommendation: "Enable 'Service principals can use read-only admin APIs' in Power BI Admin Portal → Tenant settings → Developer settings, and add the Kryoss SPN to the allow-list.",
                priority: "Critical"));
            return all; // No other checks possible.
        }

        all.Add(RecommendationResult.Success(Svc, "admin-api-access",
            observation: "Power BI Admin API is accessible."));

        // ── Workspaces ──
        if (ins.OrphanedWorkspaceCount > 0)
        {
            all.Add(RecommendationResult.ActionRequired(Svc, "orphaned-workspaces",
                observation: $"{ins.OrphanedWorkspaceCount} workspace(s) have no active admin.",
                recommendation: "Assign an admin to orphaned workspaces or remove them to reduce governance risk.",
                priority: "Medium"));
        }
        else
        {
            all.Add(RecommendationResult.Success(Svc, "orphaned-workspaces",
                observation: "No orphaned workspaces found."));
        }

        if (ins.PersonalWorkspaceCount > 0)
        {
            all.Add(RecommendationResult.Warning(Svc, "personal-workspaces",
                observation: $"{ins.PersonalWorkspaceCount} personal workspace(s) detected.",
                recommendation: "Personal workspaces with shared content risk data leakage. Migrate shared content to organizational workspaces."));
        }
        else
        {
            all.Add(RecommendationResult.Success(Svc, "personal-workspaces",
                observation: "No personal workspaces detected."));
        }

        if (ins.WorkspacesWithExternalUsers > 0)
        {
            all.Add(RecommendationResult.Warning(Svc, "external-workspace-users",
                observation: $"{ins.WorkspacesWithExternalUsers} workspace(s) have external users.",
                recommendation: "Review external user access to ensure sensitive data is not exposed to unauthorized parties."));
        }
        else
        {
            all.Add(RecommendationResult.Success(Svc, "external-workspace-users",
                observation: "No external users found in workspaces."));
        }

        // ── Capacities ──
        if (ins.CapacityCount == 0)
        {
            all.Add(RecommendationResult.Insight(Svc, "no-premium-capacity",
                observation: "No Power BI Premium or Fabric capacities detected.",
                recommendation: "Consider Premium capacity for enterprise-grade governance, large datasets, and paginated reports."));
        }
        else
        {
            if (ins.CapacitiesOverThreshold > 0)
            {
                all.Add(RecommendationResult.ActionRequired(Svc, "capacity-overload",
                    observation: $"{ins.CapacitiesOverThreshold} capacity/ies exceeding 85% usage threshold.",
                    recommendation: "Scale up or redistribute workloads to prevent performance degradation.",
                    priority: "High"));
            }
            else
            {
                all.Add(RecommendationResult.Success(Svc, "capacity-usage",
                    observation: $"{ins.CapacityCount} capacit(y/ies) within healthy usage range."));
            }
        }

        // ── Datasets ──
        if (ins.DatasetsNeverRefreshed > 0)
        {
            all.Add(RecommendationResult.Warning(Svc, "datasets-never-refreshed",
                observation: $"{ins.DatasetsNeverRefreshed} dataset(s) have never been refreshed.",
                recommendation: "Configure scheduled refresh or remove unused datasets."));
        }

        if (ins.DatasetsStale30d > 0)
        {
            all.Add(RecommendationResult.Warning(Svc, "datasets-stale",
                observation: $"{ins.DatasetsStale30d} dataset(s) have not been refreshed in 30+ days.",
                recommendation: "Review stale datasets — they may contain outdated data leading to incorrect decisions."));
        }

        if (ins.DatasetsNeverRefreshed == 0 && ins.DatasetsStale30d == 0 && ins.TotalDatasets > 0)
        {
            all.Add(RecommendationResult.Success(Svc, "dataset-freshness",
                observation: "All refreshable datasets are up-to-date."));
        }

        // ── Gateways ──
        if (ins.GatewayCount > 0)
        {
            if (ins.GatewaysOffline > 0)
            {
                all.Add(RecommendationResult.ActionRequired(Svc, "gateway-offline",
                    observation: $"{ins.GatewaysOffline} gateway(s) are offline.",
                    recommendation: "Investigate and restore offline gateways to prevent data refresh failures.",
                    priority: "High"));
            }
            else
            {
                all.Add(RecommendationResult.Success(Svc, "gateway-status",
                    observation: $"All {ins.GatewayCount} gateway(s) are online."));
            }

            if (ins.PersonalGatewayCount > 0 && ins.GatewayCount == ins.PersonalGatewayCount)
            {
                all.Add(RecommendationResult.Warning(Svc, "personal-gateways-only",
                    observation: $"Only personal gateways detected ({ins.PersonalGatewayCount}). No enterprise gateway.",
                    recommendation: "Personal gateways are a single point of failure and user-owned. Deploy an enterprise gateway for production workloads."));
            }
        }
        else
        {
            all.Add(RecommendationResult.Insight(Svc, "no-gateways",
                observation: "No data gateways detected.",
                recommendation: "If using on-premises data sources, deploy an enterprise gateway for secure connectivity."));
        }

        // ── Activity ──
        if (ins.ExternalShares30d > 10)
        {
            all.Add(RecommendationResult.Warning(Svc, "external-sharing-volume",
                observation: $"{ins.ExternalShares30d} external shares detected in the last 30 days.",
                recommendation: "Review external sharing governance policies to ensure sensitive data is not over-shared."));
        }
        else
        {
            all.Add(RecommendationResult.Success(Svc, "external-sharing",
                observation: ins.ExternalShares30d > 0
                    ? $"{ins.ExternalShares30d} external share(s) in 30 days — within normal range."
                    : "No external sharing activity in 30 days."));
        }

        if (ins.Exports30d > 50)
        {
            all.Add(RecommendationResult.Insight(Svc, "high-export-volume",
                observation: $"{ins.Exports30d} export events in 30 days.",
                recommendation: "High export volume may indicate users bypassing governance. Consider DLP policies for Power BI."));
        }

        if (ins.Deletes30d > 20)
        {
            all.Add(RecommendationResult.Warning(Svc, "high-delete-activity",
                observation: $"{ins.Deletes30d} delete events in 30 days.",
                recommendation: "Elevated delete activity may indicate destructive actions. Review audit logs."));
        }

        return all;
    }
}
