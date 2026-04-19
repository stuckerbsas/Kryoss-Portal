using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KryossApi.Data.Entities;

[Table("franchise_service_rates")]
public class FranchiseServiceRate
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("franchise_id")]
    public Guid FranchiseId { get; set; }

    [ForeignKey(nameof(FranchiseId))]
    public Franchise Franchise { get; set; } = null!;

    [Column("hourly_rate")]
    public decimal HourlyRate { get; set; } = 150.00m;

    [Column("currency")]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    [Column("margin_pct")]
    public decimal MarginPct { get; set; }

    [Column("effective_from")]
    public DateTime EffectiveFrom { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
