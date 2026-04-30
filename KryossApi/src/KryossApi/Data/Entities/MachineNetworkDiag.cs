namespace KryossApi.Data.Entities;

public class MachineNetworkDiag
{
    public int Id { get; set; }
    public Guid MachineId { get; set; }
    public Guid? RunId { get; set; }
    public decimal? DownloadMbps { get; set; }
    public decimal? UploadMbps { get; set; }
    public decimal? InternetLatencyMs { get; set; }
    public decimal? GatewayLatencyMs { get; set; }
    public string? GatewayIp { get; set; }
    public int? RouteCount { get; set; }
    public bool VpnDetected { get; set; }
    public string? VpnAdapters { get; set; }
    public int? AdapterCount { get; set; }
    public int WifiCount { get; set; }
    public int VpnAdapterCount { get; set; }
    public int EthCount { get; set; }
    public decimal? BandwidthSendMbps { get; set; }
    public decimal? BandwidthRecvMbps { get; set; }
    public decimal? DnsResolutionMs { get; set; }
    public int? CloudEndpointCount { get; set; }
    public decimal? CloudEndpointAvgMs { get; set; }
    public bool TriggeredByIpChange { get; set; }
    public decimal? JitterMs { get; set; }
    public decimal? PacketLossPct { get; set; }
    public int? HopCount { get; set; }
    public string? TracerouteTarget { get; set; }
    public DateTime ScannedAt { get; set; }

    public Machine Machine { get; set; } = null!;
    public AssessmentRun? Run { get; set; }
    public ICollection<MachineNetworkLatency> LatencyPeers { get; set; } = [];
    public ICollection<MachineNetworkRoute> Routes { get; set; } = [];
    public ICollection<MachineTracerouteHop> TracerouteHops { get; set; } = [];
}

public class MachineNetworkLatency
{
    public int Id { get; set; }
    public int DiagId { get; set; }
    public string Host { get; set; } = null!;
    public string? Subnet { get; set; }
    public bool Reachable { get; set; }
    public decimal? AvgMs { get; set; }
    public int? MinMs { get; set; }
    public int? MaxMs { get; set; }
    public decimal? JitterMs { get; set; }
    public short? PacketLoss { get; set; }
    public short? TotalSent { get; set; }

    public MachineNetworkDiag Diag { get; set; } = null!;
}

public class MachineNetworkRoute
{
    public int Id { get; set; }
    public int DiagId { get; set; }
    public string Destination { get; set; } = null!;
    public string Mask { get; set; } = null!;
    public string NextHop { get; set; } = null!;
    public int? InterfaceIndex { get; set; }
    public int? Metric { get; set; }
    public short? RouteType { get; set; }
    public short? Protocol { get; set; }

    public MachineNetworkDiag Diag { get; set; } = null!;
}
