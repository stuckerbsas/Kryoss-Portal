using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KryossApi.Data.Entities;

[Table("service_catalog")]
public class ServiceCatalogItem
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("category_code")]
    [MaxLength(30)]
    public string CategoryCode { get; set; } = "";

    [Column("name_en")]
    [MaxLength(100)]
    public string NameEn { get; set; } = "";

    [Column("name_es")]
    [MaxLength(100)]
    public string NameEs { get; set; } = "";

    [Column("unit_type")]
    [MaxLength(20)]
    public string UnitType { get; set; } = "";

    [Column("base_hours")]
    public decimal BaseHours { get; set; }

    [Column("trigger_source")]
    [MaxLength(50)]
    public string TriggerSource { get; set; } = "";

    [Column("trigger_filter")]
    [MaxLength(500)]
    public string? TriggerFilter { get; set; }

    [Column("severity")]
    [MaxLength(10)]
    public string Severity { get; set; } = "medium";

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;
}
