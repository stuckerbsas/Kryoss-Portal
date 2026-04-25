using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace KryossAgent.Services;

public record PassiveDiscovery(string Ip, string? Hostname, string? DeviceType, string? Manufacturer, string Protocol);

public class PassiveListener : IDisposable
{
    private readonly ConcurrentDictionary<string, PassiveDiscovery> _discovered = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly bool _verbose;
    private Task? _listenTask;

    public PassiveListener(bool verbose = false) => _verbose = verbose;

    public IReadOnlyCollection<PassiveDiscovery> Discovered => _discovered.Values.ToList();

    public void Start()
    {
        _listenTask = Task.Run(() => ListenAllAsync(_cts.Token));
    }

    private async Task ListenAllAsync(CancellationToken ct)
    {
        var tasks = new List<Task>
        {
            ListenUdpAsync(137, ParseNetBios, ct),
            ListenUdpAsync(5353, ParseMdns, ct),
            ListenUdpAsync(1900, ParseSsdp, ct),
        };
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
    }

    private async Task ListenUdpAsync(int port, Func<byte[], IPEndPoint, PassiveDiscovery?> parser, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));

            if (port == 5353)
            {
                udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.251"));
            }
            else if (port == 1900)
            {
                udp.JoinMulticastGroup(IPAddress.Parse("239.255.255.250"));
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var receiveTask = udp.ReceiveAsync(ct);
                    var result = await receiveTask;
                    var discovery = parser(result.Buffer, result.RemoteEndPoint);
                    if (discovery != null)
                    {
                        _discovered.TryAdd(discovery.Ip, discovery);
                        if (_verbose)
                            Console.WriteLine($"  [Passive] {discovery.Protocol}: {discovery.Ip} = {discovery.Hostname ?? "?"} ({discovery.DeviceType ?? "unknown"})");
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }
        catch (SocketException ex)
        {
            if (_verbose) Console.Error.WriteLine($"  [Passive] Port {port} bind failed: {ex.Message}");
        }
    }

    private static PassiveDiscovery? ParseNetBios(byte[] data, IPEndPoint remote)
    {
        // NetBIOS Name Service response: name at offset 57, 15 bytes padded
        if (data.Length < 57 + 15) return null;
        try
        {
            // Simple: look for name registration/response packets
            var name = Encoding.ASCII.GetString(data, 57, 15).Trim('\0', ' ');
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2) return null;
            return new PassiveDiscovery(remote.Address.ToString(), name, null, null, "NetBIOS");
        }
        catch { return null; }
    }

    private static PassiveDiscovery? ParseMdns(byte[] data, IPEndPoint remote)
    {
        // mDNS: extract hostname from answer section
        if (data.Length < 12) return null;
        try
        {
            var text = Encoding.UTF8.GetString(data);
            string? hostname = null;
            string? deviceType = null;
            string? manufacturer = null;

            // Look for .local names in the packet
            var localIdx = text.IndexOf(".local", StringComparison.OrdinalIgnoreCase);
            if (localIdx > 0)
            {
                var start = text.LastIndexOfAny(['\0', (char)0x0c, (char)0x01], localIdx - 1);
                if (start >= 0 && start < localIdx)
                    hostname = text[(start + 1)..localIdx].Trim('\0', ' ', '\t');
            }

            // Parse TXT records for device info
            if (text.Contains("model=", StringComparison.OrdinalIgnoreCase))
            {
                var modelIdx = text.IndexOf("model=", StringComparison.OrdinalIgnoreCase);
                var end = text.IndexOf('\0', modelIdx);
                if (end < 0) end = Math.Min(modelIdx + 50, text.Length);
                deviceType = text[(modelIdx + 6)..end].Trim();
            }

            if (text.Contains("manufacturer=", StringComparison.OrdinalIgnoreCase))
            {
                var mfgIdx = text.IndexOf("manufacturer=", StringComparison.OrdinalIgnoreCase);
                var end = text.IndexOf('\0', mfgIdx);
                if (end < 0) end = Math.Min(mfgIdx + 50, text.Length);
                manufacturer = text[(mfgIdx + 13)..end].Trim();
            }

            // Derive type from service names
            if (text.Contains("_ipp._tcp", StringComparison.OrdinalIgnoreCase))
                deviceType ??= "printer";
            else if (text.Contains("_airplay._tcp", StringComparison.OrdinalIgnoreCase))
                deviceType ??= "media";
            else if (text.Contains("_smb._tcp", StringComparison.OrdinalIgnoreCase))
                deviceType ??= "computer";
            else if (text.Contains("_http._tcp", StringComparison.OrdinalIgnoreCase))
                deviceType ??= "web-device";

            if (hostname == null && deviceType == null) return null;
            return new PassiveDiscovery(remote.Address.ToString(), hostname, deviceType, manufacturer, "mDNS");
        }
        catch { return null; }
    }

    private static PassiveDiscovery? ParseSsdp(byte[] data, IPEndPoint remote)
    {
        // SSDP: HTTP-like headers
        try
        {
            var text = Encoding.UTF8.GetString(data);
            if (!text.Contains("HTTP/", StringComparison.OrdinalIgnoreCase)) return null;

            string? deviceType = null;
            string? manufacturer = null;
            string? hostname = null;

            // ST or NT header often contains device type
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("ST:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("NT:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
                    if (val.Contains("MediaRenderer", StringComparison.OrdinalIgnoreCase))
                        deviceType = "media";
                    else if (val.Contains("Printer", StringComparison.OrdinalIgnoreCase))
                        deviceType = "printer";
                    else if (val.Contains("InternetGateway", StringComparison.OrdinalIgnoreCase))
                        deviceType = "router";
                    else if (val.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                        deviceType ??= "iot";
                }
                else if (trimmed.StartsWith("SERVER:", StringComparison.OrdinalIgnoreCase))
                {
                    manufacturer = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
                    if (manufacturer.Length > 80) manufacturer = manufacturer[..80];
                }
                else if (trimmed.StartsWith("USN:", StringComparison.OrdinalIgnoreCase))
                {
                    var usn = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
                    if (usn.Contains("uuid:", StringComparison.OrdinalIgnoreCase))
                        hostname ??= usn;
                }
            }

            if (deviceType == null && manufacturer == null) return null;
            return new PassiveDiscovery(remote.Address.ToString(), hostname, deviceType, manufacturer, "SSDP");
        }
        catch { return null; }
    }

    public List<string> DrainDiscoveredIps()
    {
        var ips = _discovered.Keys.ToList();
        return ips;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
