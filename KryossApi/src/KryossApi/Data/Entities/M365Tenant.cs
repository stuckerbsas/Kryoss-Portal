namespace KryossApi.Data.Entities;

public class M365Tenant
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string TenantId { get; set; } = null!;
    public string? TenantName { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? LastScanAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public ICollection<M365Finding> Findings { get; set; } = [];
}

public class M365Finding
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public string CheckId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Finding { get; set; }
    public string? ActualValue { get; set; }
    public DateTime ScannedAt { get; set; }

    public M365Tenant Tenant { get; set; } = null!;
}
