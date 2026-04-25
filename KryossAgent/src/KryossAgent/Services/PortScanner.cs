using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace KryossAgent.Services;

/// <summary>
/// Scans TCP and UDP ports on a target host. TCP uses parallel TcpClient connections.
/// UDP sends empty datagrams and checks for ICMP port-unreachable responses.
/// </summary>
public static class PortScanner
{
    // Top TCP ports (covers 95%+ of services found in enterprise networks)
    // Based on Nmap's top 1000 ports, condensed to the most common 100
    public static readonly int[] TopTcpPorts = [
        21, 22, 23, 25, 53, 80, 81, 88, 110, 111,
        119, 135, 139, 143, 161, 389, 443, 445, 464, 465,
        514, 515, 548, 554, 587, 593, 631, 636, 873, 902,
        993, 995, 1025, 1026, 1027, 1028, 1029, 1080, 1110, 1433,
        1434, 1521, 1723, 1883, 2049, 2082, 2083, 2086, 2087, 2096,
        2222, 2375, 2376, 3268, 3269, 3306, 3389, 3690, 4000, 4443,
        4444, 4567, 4899, 5000, 5001, 5003, 5004, 5060, 5061, 5222,
        5432, 5555, 5601, 5672, 5900, 5901, 5985, 5986, 6000, 6379,
        6443, 6588, 6667, 7001, 7002, 7070, 7071, 7080, 7443, 8000,
        8008, 8080, 8081, 8083, 8088, 8443, 8880, 8888, 9000, 9001,
        9090, 9091, 9200, 9300, 9443, 9999, 10000, 11211, 15672, 27017,
    ];

    // Top UDP ports
    public static readonly int[] TopUdpPorts = [
        53, 67, 68, 69, 88, 123, 137, 138, 161, 162,
        389, 445, 500, 514, 520, 631, 1194, 1434, 1900, 4500,
    ];

    // Well-known service names
    private static readonly Dictionary<int, string> TcpServiceNames = new()
    {
        [21] = "FTP", [22] = "SSH", [23] = "Telnet", [25] = "SMTP",
        [53] = "DNS", [80] = "HTTP", [81] = "HTTP-Alt", [88] = "Kerberos",
        [110] = "POP3", [111] = "RPCBind", [119] = "NNTP", [135] = "RPC",
        [139] = "NetBIOS", [143] = "IMAP", [161] = "SNMP", [389] = "LDAP",
        [443] = "HTTPS", [445] = "SMB", [464] = "Kerberos-PW", [465] = "SMTPS",
        [514] = "Syslog", [515] = "LPD", [548] = "AFP", [554] = "RTSP",
        [587] = "Submission", [593] = "HTTP-RPC", [631] = "IPP", [636] = "LDAPS",
        [873] = "Rsync", [902] = "VMware", [993] = "IMAPS", [995] = "POP3S",
        [1080] = "SOCKS", [1433] = "MSSQL", [1434] = "MSSQL-UDP", [1521] = "Oracle",
        [1723] = "PPTP", [1883] = "MQTT", [2049] = "NFS", [2222] = "SSH-Alt",
        [2375] = "Docker", [2376] = "Docker-TLS", [3268] = "LDAP-GC", [3269] = "LDAPS-GC",
        [3306] = "MySQL", [3389] = "RDP", [3690] = "SVN", [4443] = "HTTPS-Alt",
        [4444] = "Metasploit", [4899] = "Radmin", [5000] = "UPnP", [5001] = "Synology",
        [5060] = "SIP", [5061] = "SIP-TLS", [5222] = "XMPP", [5432] = "PostgreSQL",
        [5555] = "ADB", [5601] = "Kibana", [5672] = "AMQP", [5900] = "VNC",
        [5901] = "VNC-1", [5985] = "WinRM", [5986] = "WinRM-S", [6000] = "X11",
        [6379] = "Redis", [6443] = "K8s-API", [6667] = "IRC", [7001] = "WebLogic",
        [7070] = "RealServer", [7080] = "HTTP-Alt", [7443] = "HTTPS-Alt",
        [8000] = "HTTP-Alt", [8008] = "HTTP-Alt", [8080] = "HTTP-Proxy",
        [8081] = "HTTP-Alt", [8083] = "HTTP-Alt", [8088] = "HTTP-Alt",
        [8443] = "HTTPS-Alt", [8880] = "HTTP-Alt", [8888] = "HTTP-Alt",
        [9000] = "HTTP-Alt", [9001] = "HTTP-Alt", [9090] = "WebConsole",
        [9200] = "Elasticsearch", [9300] = "ES-Transport", [9443] = "HTTPS-Alt",
        [9999] = "HTTP-Alt", [10000] = "Webmin", [11211] = "Memcached",
        [15672] = "RabbitMQ", [27017] = "MongoDB",
    };

    // Risk levels for open ports
    private static readonly Dictionary<int, string> RiskyPorts = new()
    {
        [21] = "high",     // FTP - cleartext
        [23] = "critical", // Telnet - cleartext remote access
        [25] = "medium",   // SMTP - open relay risk
        [135] = "medium",  // RPC - lateral movement
        [139] = "high",    // NetBIOS - info leak
        [445] = "high",    // SMB - lateral movement, EternalBlue
        [1433] = "high",   // MSSQL - DB exposed
        [1434] = "high",   // MSSQL Browser
        [3306] = "high",   // MySQL exposed
        [3389] = "high",   // RDP - brute force target
        [4444] = "critical", // Metasploit default
        [4899] = "high",   // Radmin
        [5432] = "high",   // PostgreSQL exposed
        [5555] = "high",   // ADB - Android debug
        [5900] = "high",   // VNC - often weak auth
        [5901] = "high",   // VNC
        [6379] = "critical", // Redis - often no auth
        [6667] = "medium", // IRC - C2 channel
        [11211] = "high",  // Memcached - DDoS amplification
        [27017] = "critical", // MongoDB - often no auth
    };

    public record PortResult(
        int Port, string Protocol, string Status, string? Service, string? Risk,
        string? Banner = null, string? ServiceName = null, string? ServiceVersion = null);

    private static readonly HashSet<int> HttpPorts = [80, 81, 443, 4443, 7080, 7443, 8000, 8008, 8080, 8081, 8083, 8088, 8443, 8880, 8888, 9000, 9001, 9090, 9443, 9999, 10000];
    private static readonly HashSet<int> BannerGrabPorts = [21, 22, 25, 110, 143, 389, 587, 993, 995, 1433, 3306, 3389, 5432, 5900, 6379, 11211, 27017];

    /// <summary>
    /// Scan TCP ports in parallel. 100 concurrent connections, 500ms timeout.
    /// </summary>
    public static async Task<List<PortResult>> ScanTcpAsync(string host, int concurrency = 100, int timeoutMs = 500, bool grabBanners = true)
    {
        var results = new List<PortResult>();
        var semaphore = new SemaphoreSlim(concurrency);

        var tasks = TopTcpPorts.Select(async port =>
        {
            await semaphore.WaitAsync();
            try
            {
                using var tcp = new TcpClient();
                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    await tcp.ConnectAsync(host, port, cts.Token);
                    var service = TcpServiceNames.TryGetValue(port, out var s) ? s : null;
                    var risk = RiskyPorts.TryGetValue(port, out var r) ? r : null;

                    string? banner = null;
                    string? svcName = null;
                    string? svcVersion = null;

                    if (grabBanners)
                    {
                        banner = await GrabBannerAsync(tcp, host, port);
                        if (banner is not null)
                            (svcName, svcVersion) = ParseBanner(banner, port);
                    }

                    return new PortResult(port, "TCP", "open", service, risk, banner, svcName, svcVersion);
                }
                catch
                {
                    return null;
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        var scanResults = await Task.WhenAll(tasks);
        results.AddRange(scanResults.Where(r => r is not null)!);
        return results.OrderBy(r => r.Port).ToList();
    }

    /// <summary>
    /// Scan UDP ports. Sends empty datagram and checks for ICMP unreachable.
    /// UDP scanning is inherently unreliable — no response = open|filtered.
    /// </summary>
    public static async Task<List<PortResult>> ScanUdpAsync(string host, int timeoutMs = 2000)
    {
        var results = new List<PortResult>();

        foreach (var port in TopUdpPorts)
        {
            try
            {
                using var udp = new UdpClient();
                udp.Connect(host, port);
                var data = new byte[1];
                await udp.SendAsync(data, 1);

                var receiveTask = udp.ReceiveAsync();
                if (await Task.WhenAny(receiveTask, Task.Delay(timeoutMs)) == receiveTask)
                {
                    // Got a response — port is open
                    var service = TcpServiceNames.TryGetValue(port, out var s) ? s : null;
                    results.Add(new PortResult(port, "UDP", "open", service, null));
                }
                // No response = open|filtered, we only report confirmed open
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // ICMP port unreachable = closed (don't report)
            }
            catch
            {
                // Other error — skip
            }
        }

        return results.OrderBy(r => r.Port).ToList();
    }

    /// <summary>
    /// Full scan: TCP + UDP in parallel.
    /// </summary>
    public static async Task<List<PortResult>> ScanAllAsync(string host)
    {
        var tcpTask = ScanTcpAsync(host);
        var udpTask = ScanUdpAsync(host);

        await Task.WhenAll(tcpTask, udpTask);

        var all = new List<PortResult>();
        all.AddRange(tcpTask.Result);
        all.AddRange(udpTask.Result);
        return all;
    }

    /// <summary>Get the service name for a port.</summary>
    public static string? GetServiceName(int port) =>
        TcpServiceNames.TryGetValue(port, out var s) ? s : null;

    /// <summary>Get the risk level for a port.</summary>
    public static string? GetRiskLevel(int port) =>
        RiskyPorts.TryGetValue(port, out var r) ? r : null;

    private static async Task<string?> GrabBannerAsync(TcpClient tcp, string host, int port)
    {
        try
        {
            tcp.ReceiveTimeout = 3000;
            tcp.SendTimeout = 3000;
            var stream = tcp.GetStream();
            var buf = new byte[512];

            if (HttpPorts.Contains(port))
            {
                var probe = Encoding.ASCII.GetBytes($"HEAD / HTTP/1.0\r\nHost: {host}\r\n\r\n");
                await stream.WriteAsync(probe);
                using var cts = new CancellationTokenSource(3000);
                var total = 0;
                while (total < buf.Length)
                {
                    var n = await stream.ReadAsync(buf.AsMemory(total), cts.Token);
                    if (n == 0) break;
                    total += n;
                    if (total > 4 && Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n")) break;
                }
                return total > 0 ? Encoding.ASCII.GetString(buf, 0, total).Trim() : null;
            }

            if (BannerGrabPorts.Contains(port))
            {
                using var cts = new CancellationTokenSource(3000);
                var n = await stream.ReadAsync(buf, cts.Token);
                return n > 0 ? Encoding.ASCII.GetString(buf, 0, n).Trim() : null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static (string? Name, string? Version) ParseBanner(string banner, int port)
    {
        // HTTP Server header
        var serverMatch = Regex.Match(banner, @"Server:\s*(.+)", RegexOptions.IgnoreCase);
        if (serverMatch.Success)
        {
            var server = serverMatch.Groups[1].Value.Trim();
            var verMatch = Regex.Match(server, @"^([^\s/]+)[/\s]+(\S+)");
            if (verMatch.Success)
                return (verMatch.Groups[1].Value, verMatch.Groups[2].Value);
            return (server, null);
        }

        // SSH: SSH-2.0-OpenSSH_8.9p1
        if (banner.StartsWith("SSH-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = banner.Split('-', 3);
            if (parts.Length >= 3)
            {
                var impl = parts[2].Split(' ')[0];
                var verMatch = Regex.Match(impl, @"^([A-Za-z]+)[_](.+)");
                if (verMatch.Success)
                    return (verMatch.Groups[1].Value, verMatch.Groups[2].Value);
                return (impl, null);
            }
        }

        // FTP/SMTP: 220 Microsoft FTP Service or 220 mail.example.com ESMTP Postfix
        if (banner.StartsWith("220"))
        {
            var line = banner.Split('\n')[0][3..].Trim().TrimStart('-');
            var verMatch = Regex.Match(line, @"([\w.-]+)\s+([\d.]+)");
            if (verMatch.Success)
                return (verMatch.Groups[1].Value, verMatch.Groups[2].Value);
            return (line.Length > 60 ? line[..60] : line, null);
        }

        // Generic: first line, extract name/version pattern
        var firstLine = banner.Split('\n')[0].Trim();
        var generic = Regex.Match(firstLine, @"^[+*\s]*([A-Za-z][\w.-]*)\s+([\d]+[\d.]*\w*)");
        if (generic.Success)
            return (generic.Groups[1].Value, generic.Groups[2].Value);

        return (null, null);
    }
}
