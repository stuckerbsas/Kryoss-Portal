namespace KryossApi.Data.Entities;

public class Franchise : IAuditable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Country { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string Status { get; set; } = "active";

    // Branding (white-label)
    public string? BrandName { get; set; }
    public string? BrandLogoUrl { get; set; }
    public string? BrandColorPrimary { get; set; }
    public string? BrandColorAccent { get; set; }

    // Audit
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation
    public ICollection<Organization> Organizations { get; set; } = [];
}
