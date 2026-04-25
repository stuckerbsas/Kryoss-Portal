using System.Text.Json.Serialization;

namespace KryossAgent.Models;

public class NetworkDiagResult
{
    [JsonPropertyName("downloadMbps")]
    public decimal DownloadMbps { get; set; }

    [JsonPropertyName("uploadMbps")]
    public decimal UploadMbps { get; set; }

    [JsonPropertyName("internetLatencyMs")]
    public decimal InternetLatencyMs { get; set; }

    [JsonPropertyName("gatewayLatencyMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? GatewayLatencyMs { get; set; }

    [JsonPropertyName("gatewayIp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GatewayIp { get; set; }

    [JsonPropertyName("linkLatency")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LatencyResult>? LinkLatency { get; set; }

    [JsonPropertyName("internalLatency")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LatencyResult>? InternalLatency { get; set; }

    [JsonPropertyName("routeTable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RouteEntry>? RouteTable { get; set; }

    [JsonPropertyName("vpnInterfaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<VpnInterface>? VpnInterfaces { get; set; }

    [JsonPropertyName("adapters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AdapterInfo>? Adapters { get; set; }

    [JsonPropertyName("bandwidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BandwidthInfo? BandwidthSnapshot { get; set; }

    [JsonPropertyName("cloudEndpointLatency")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CloudEndpointLatency>? CloudEndpointLatency { get; set; }

    [JsonPropertyName("dnsResolutionMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? DnsResolutionMs { get; set; }

    [JsonPropertyName("triggeredByIpChange")]
    public bool TriggeredByIpChange { get; set; }

    [JsonPropertyName("wifiCount")]
    public int WifiCount { get; set; }

    [JsonPropertyName("vpnCount")]
    public int VpnCount { get; set; }

    [JsonPropertyName("ethCount")]
    public int EthCount { get; set; }

    [JsonPropertyName("hostsFileEntryCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? HostsFileEntryCount { get; set; }

    [JsonPropertyName("ntpConfigured")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NtpConfigured { get; set; }

    [JsonPropertyName("wpadEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WpadEnabled { get; set; }

    [JsonPropertyName("llmnrEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LlmnrEnabled { get; set; }

    [JsonPropertyName("netbiosEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NetbiosEnabled { get; set; }

    [JsonPropertyName("openWifiCount")]
    public int OpenWifiCount { get; set; }

    [JsonPropertyName("arpEntryCount")]
    public int ArpEntryCount { get; set; }

    [JsonPropertyName("listeningPortCount")]
    public int ListeningPortCount { get; set; }

    [JsonPropertyName("disconnectedWithIpCount")]
    public int DisconnectedWithIpCount { get; set; }

    [JsonPropertyName("nicTeamingDetected")]
    public bool NicTeamingDetected { get; set; }
}

public class CloudEndpointLatency
{
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = null!;

    [JsonPropertyName("latencyMs")]
    public decimal LatencyMs { get; set; }

    [JsonPropertyName("reachable")]
    public bool Reachable { get; set; }
}

public class LatencyResult
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = null!;

    [JsonPropertyName("subnet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subnet { get; set; }

    [JsonPropertyName("reachable")]
    public bool Reachable { get; set; }

    [JsonPropertyName("avgMs")]
    public decimal AvgMs { get; set; }

    [JsonPropertyName("minMs")]
    public long MinMs { get; set; }

    [JsonPropertyName("maxMs")]
    public long MaxMs { get; set; }

    [JsonPropertyName("jitterMs")]
    public decimal JitterMs { get; set; }

    [JsonPropertyName("packetLoss")]
    public int PacketLoss { get; set; }

    [JsonPropertyName("totalSent")]
    public int TotalSent { get; set; }
}

public class RouteEntry
{
    [JsonPropertyName("destination")]
    public string Destination { get; set; } = null!;

    [JsonPropertyName("mask")]
    public string Mask { get; set; } = null!;

    [JsonPropertyName("nextHop")]
    public string NextHop { get; set; } = null!;

    [JsonPropertyName("interfaceIndex")]
    public int InterfaceIndex { get; set; }

    [JsonPropertyName("metric")]
    public int Metric { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("protocol")]
    public int Protocol { get; set; }
}

public class VpnInterface
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("ipAddress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IpAddress { get; set; }

    [JsonPropertyName("speed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Speed { get; set; }
}

public class AdapterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("category")]
    public string Category { get; set; } = "ethernet"; // ethernet, wifi, vpn

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("speedMbps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SpeedMbps { get; set; }

    [JsonPropertyName("macAddress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MacAddress { get; set; }

    [JsonPropertyName("ipAddress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IpAddress { get; set; }

    [JsonPropertyName("subnetMask")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubnetMask { get; set; }

    [JsonPropertyName("gateway")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Gateway { get; set; }

    [JsonPropertyName("dnsServers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DnsServers { get; set; }

    [JsonPropertyName("dhcpEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DhcpEnabled { get; set; }

    [JsonPropertyName("mtu")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Mtu { get; set; }

    [JsonPropertyName("ipv6Address")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ipv6Address { get; set; }
}

public class BandwidthInfo
{
    [JsonPropertyName("sendRateMbps")]
    public decimal SendRateMbps { get; set; }

    [JsonPropertyName("recvRateMbps")]
    public decimal RecvRateMbps { get; set; }

    [JsonPropertyName("totalSentBytes")]
    public long TotalSentBytes { get; set; }

    [JsonPropertyName("totalRecvBytes")]
    public long TotalRecvBytes { get; set; }
}
