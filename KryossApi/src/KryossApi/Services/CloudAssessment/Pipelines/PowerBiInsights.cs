namespace KryossApi.Services.CloudAssessment.Pipelines;

public class PowerBiInsights
{
    public bool ApiAccessible { get; set; }
    public string? ConnectionError { get; set; }

    // Workspaces
    public int WorkspaceCount { get; set; }
    public int PersonalWorkspaceCount { get; set; }
    public int OrphanedWorkspaceCount { get; set; }
    public int WorkspacesOnDedicatedCapacity { get; set; }
    public int WorkspacesWithExternalUsers { get; set; }

    // Content
    public int TotalDatasets { get; set; }
    public int TotalReports { get; set; }
    public int TotalDashboards { get; set; }
    public int TotalDataflows { get; set; }
    public int DatasetsNeverRefreshed { get; set; }
    public int DatasetsStale30d { get; set; }

    // Capacities
    public int CapacityCount { get; set; }
    public decimal? AvgCapacityUsagePct { get; set; }
    public int CapacitiesOverThreshold { get; set; }

    // Gateways
    public int GatewayCount { get; set; }
    public int GatewaysOffline { get; set; }
    public int PersonalGatewayCount { get; set; }

    // Activity (30d)
    public int Activities30d { get; set; }
    public int UniqueActiveUsers30d { get; set; }
    public int ExternalShares30d { get; set; }
    public int Exports30d { get; set; }
    public int Deletes30d { get; set; }

    // Persisted workspace rows
    public List<PowerBiWorkspaceRow> Workspaces { get; } = new();
    public List<PowerBiGatewayRow> Gateways { get; } = new();
    public List<PowerBiCapacityRow> Capacities { get; } = new();
    public PowerBiActivitySummaryRow? ActivitySummary { get; set; }
}

public class PowerBiWorkspaceRow
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public bool? IsOnDedicatedCapacity { get; set; }
    public string? CapacityId { get; set; }
    public bool? HasWorkspaceLevelSettings { get; set; }
    public int MemberCount { get; set; }
    public int AdminCount { get; set; }
    public int ExternalUserCount { get; set; }
    public int DatasetCount { get; set; }
    public int ReportCount { get; set; }
    public int DashboardCount { get; set; }
    public int DataflowCount { get; set; }
    public DateTime? LastUpdatedDate { get; set; }
}

public class PowerBiGatewayRow
{
    public string GatewayId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public bool? PublicKeyValid { get; set; }
    public string? Status { get; set; }
    public string? Version { get; set; }
    public string? ContactInformation { get; set; }
}

public class PowerBiCapacityRow
{
    public string? CapacityId { get; set; }
    public string? DisplayName { get; set; }
    public string? Sku { get; set; }
    public string? Region { get; set; }
    public string? State { get; set; }
    public decimal? UsagePct { get; set; }
    public int AdminCount { get; set; }
}

public class PowerBiActivitySummaryRow
{
    public int ActivitiesTotal { get; set; }
    public int UniqueUsers { get; set; }
    public int ViewReportCount { get; set; }
    public int EditReportCount { get; set; }
    public int CreateDatasetCount { get; set; }
    public int DeleteCount { get; set; }
    public int ShareExternalCount { get; set; }
    public int ExportCount { get; set; }
    public int PeriodDays { get; set; }
}
