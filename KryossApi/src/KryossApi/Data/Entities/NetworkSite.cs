using System.ComponentModel.DataAnnotations.Schema;

namespace KryossApi.Data.Entities;

public class MachinePublicIpHistory
{
    public int Id { get; set; }
    public Guid MachineId { get; set; }
    public string PublicIp { get; set; } = null!;
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public string? GeoCountry { get; set; }
    public string? GeoRegion { get; set; }
    public string? GeoCity { get; set; }
    public decimal? GeoLat { get; set; }
    public decimal? GeoLon { get; set; }
    public string? Isp { get; set; }
    public int? Asn { get; set; }
    public string? AsnOrg { get; set; }
    public string? ConnType { get; set; }

    public Machine Machine { get; set; } = null!;
}

public class NetworkSite
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string SiteName { get; set; } = null!;
    public string? PublicIp { get; set; }
    public string? GeoCountry { get; set; }
    public string? GeoRegion { get; set; }
    public string? GeoCity { get; set; }
    public decimal? GeoLat { get; set; }
    public decimal? GeoLon { get; set; }
    public string? Isp { get; set; }
    public int? Asn { get; set; }
    public string? AsnOrg { get; set; }
    public string? ConnType { get; set; }
    public decimal? ContractedDownMbps { get; set; }
    public decimal? ContractedUpMbps { get; set; }
    public int AgentCount { get; set; }
    public int DeviceCount { get; set; }
    [Column("ip_changes_90d")]
    public int IpChanges90d { get; set; }
    public decimal? AvgDownMbps { get; set; }
    public decimal? AvgUpMbps { get; set; }
    public decimal? AvgLatencyMs { get; set; }
    public bool IsAutoDerived { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Organization Organization { get; set; } = null!;
}
