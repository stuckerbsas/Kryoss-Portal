namespace KryossApi.Data.Entities;

public class InfraAssessmentScan
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Status { get; set; } = "pending";
    public string? Scope { get; set; }
    public decimal? OverallHealth { get; set; }
    public int SiteCount { get; set; }
    public int DeviceCount { get; set; }
    public int FindingCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Organization Organization { get; set; } = null!;
    public ICollection<InfraAssessmentSite> Sites { get; set; } = new List<InfraAssessmentSite>();
    public ICollection<InfraAssessmentDevice> Devices { get; set; } = new List<InfraAssessmentDevice>();
    public ICollection<InfraAssessmentConnectivity> Connectivity { get; set; } = new List<InfraAssessmentConnectivity>();
    public ICollection<InfraAssessmentCapacity> Capacity { get; set; } = new List<InfraAssessmentCapacity>();
    public ICollection<InfraAssessmentFinding> Findings { get; set; } = new List<InfraAssessmentFinding>();
}

public class InfraAssessmentSite
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public string SiteName { get; set; } = null!;
    public string? Location { get; set; }
    public string SiteType { get; set; } = "branch";
    public int DeviceCount { get; set; }
    public int UserCount { get; set; }
    public string? ConnectivityType { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public InfraAssessmentScan Scan { get; set; } = null!;
}

public class InfraAssessmentDevice
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public Guid? SiteId { get; set; }
    public string? Hostname { get; set; }
    public string DeviceType { get; set; } = "server";
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string? Role { get; set; }
    public string? IpAddress { get; set; }
    public string? Os { get; set; }
    public string? Firmware { get; set; }
    public string? SerialNumber { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public InfraAssessmentScan Scan { get; set; } = null!;
    public InfraAssessmentSite? Site { get; set; }
}

public class InfraAssessmentConnectivity
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public Guid SiteAId { get; set; }
    public Guid SiteBId { get; set; }
    public string LinkType { get; set; } = "internet";
    public decimal? BandwidthMbps { get; set; }
    public decimal? LatencyMs { get; set; }
    public decimal? UptimePct { get; set; }
    public decimal? CostMonthlyUsd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public InfraAssessmentScan Scan { get; set; } = null!;
    public InfraAssessmentSite SiteA { get; set; } = null!;
    public InfraAssessmentSite SiteB { get; set; } = null!;
}

public class InfraAssessmentCapacity
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public Guid? DeviceId { get; set; }
    public string MetricKey { get; set; } = null!;
    public decimal? CurrentValue { get; set; }
    public decimal? PeakValue { get; set; }
    public decimal? Threshold { get; set; }
    public string TrendDirection { get; set; } = "stable";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public InfraAssessmentScan Scan { get; set; } = null!;
    public InfraAssessmentDevice? Device { get; set; }
}

public class InfraAssessmentFinding
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public string Area { get; set; } = null!;
    public string? Service { get; set; }
    public string? Feature { get; set; }
    public string Status { get; set; } = "warning";
    public string Priority { get; set; } = "medium";
    public string? Observation { get; set; }
    public string? Recommendation { get; set; }
    public string? LinkText { get; set; }
    public string? LinkUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public InfraAssessmentScan Scan { get; set; } = null!;
}
