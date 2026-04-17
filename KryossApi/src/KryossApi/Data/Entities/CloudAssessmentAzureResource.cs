namespace KryossApi.Data.Entities;

/// <summary>
/// Per-scan cache of Azure ARM resources collected by the CA-6 Subsession B
/// Azure pipeline. Holds the slice of properties and the list of detected risk
/// flags that recommendations consume downstream. Rows are tied to a scan and
/// cascade-delete with it.
/// </summary>
public class CloudAssessmentAzureResource
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string SubscriptionId { get; set; } = null!;
    public string ResourceType { get; set; } = null!;
    public string ResourceId { get; set; } = null!;
    public string? Name { get; set; }
    public string? Location { get; set; }
    public string? Kind { get; set; }
    public string? PropertiesJson { get; set; }
    public string? RiskFlags { get; set; }
    public DateTime CreatedAt { get; set; }

    public CloudAssessmentScan Scan { get; set; } = null!;
}
