namespace KryossApi.Data.Entities;

public class ExternalScan
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Target { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public List<ExternalScanResult> Results { get; set; } = [];
    public List<ExternalScanFinding> Findings { get; set; } = [];
}

public class ExternalScanResult
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string IpAddress { get; set; } = null!;
    public int Port { get; set; }
    public string Protocol { get; set; } = "TCP";
    public string Status { get; set; } = null!;
    public string? Service { get; set; }
    public string? Risk { get; set; }
    public string? Banner { get; set; }
    public string? ServiceName { get; set; }
    public string? ServiceVersion { get; set; }
    public string? Detail { get; set; }

    public ExternalScan Scan { get; set; } = null!;
}

public class ExternalScanFinding
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public long? ScanResultId { get; set; }
    public string Severity { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? Remediation { get; set; }
    public int? Port { get; set; }
    public string? PublicIp { get; set; }
    public string? Category { get; set; }

    public ExternalScan Scan { get; set; } = null!;
    public ExternalScanResult? ScanResult { get; set; }
}
