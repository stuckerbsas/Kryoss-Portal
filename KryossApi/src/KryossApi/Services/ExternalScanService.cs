using System.Net;
using System.Net.Sockets;
using System.Text;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services;

public class ExternalScanService
{
    private readonly KryossDbContext _db;
    private readonly ILogger<ExternalScanService> _log;

    // Top 25 ports to scan externally
    private static readonly (int Port, string Service, string DefaultRisk)[] TargetPorts =
    [
        (21,    "FTP",         "high"),
        (22,    "SSH",         "info"),
        (23,    "Telnet",      "critical"),
        (25,    "SMTP",        "high"),
        (53,    "DNS",         "info"),
        (80,    "HTTP",        "medium"),
        (110,   "POP3",        "medium"),
        (143,   "IMAP",        "medium"),
        (443,   "HTTPS",       "info"),
        (445,   "SMB",         "critical"),
        (465,   "SMTPS",       "info"),
        (587,   "Submission",  "info"),
        (993,   "IMAPS",       "info"),
        (995,   "POP3S",       "info"),
        (1433,  "MSSQL",       "critical"),
        (1723,  "PPTP",        "high"),
        (3306,  "MySQL",       "critical"),
        (3389,  "RDP",         "critical"),
        (5432,  "PostgreSQL",  "critical"),
        (5900,  "VNC",         "critical"),
        (8080,  "HTTP-Proxy",  "medium"),
        (8443,  "HTTPS-Alt",   "info"),
        (8888,  "HTTP-Alt",    "medium"),
        (9090,  "WebConsole",  "medium"),
        (27017, "MongoDB",     "critical"),
    ];

    public ExternalScanService(KryossDbContext db, ILogger<ExternalScanService> log)
    {
        _db = db;
        _log = log;
    }

    // ── CRIT-02: SSRF protection — block private/reserved IP ranges ──
    /// <summary>
    /// Returns true if the IP is in a private, reserved, loopback, link-local,
    /// multicast, or cloud metadata range. These MUST NOT be scanned externally
    /// to prevent SSRF attacks against internal infrastructure, Azure IMDS, etc.
    /// </summary>
    private static bool IsPrivateOrReserved(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            if (bytes[0] == 10) return true;                                    // 10.0.0.0/8
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true; // 172.16.0.0/12
            if (bytes[0] == 192 && bytes[1] == 168) return true;                // 192.168.0.0/16
            if (bytes[0] == 127) return true;                                    // 127.0.0.0/8 loopback
            if (bytes[0] == 169 && bytes[1] == 254) return true;                // 169.254.0.0/16 link-local + Azure IMDS
            if (bytes[0] == 0) return true;                                      // 0.0.0.0/8
            if (bytes[0] >= 224) return true;                                    // 224.0.0.0+ multicast + reserved
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true; // 100.64.0.0/10 CGN (RFC 6598)
            if (bytes[0] == 168 && bytes[1] == 63 && bytes[2] == 129 && bytes[3] == 16) return true; // Azure wireserver
        }
        // Block all IPv6 for now — external scans should target public IPv4
        if (bytes.Length == 16) return true;
        return false;
    }

    /// <summary>
    /// Run a full external scan: resolve DNS, scan ports on each IP, save results.
    /// </summary>
    public async Task<ExternalScan> RunScanAsync(Guid scanId)
    {
        var scan = await _db.ExternalScans.FindAsync(scanId)
            ?? throw new InvalidOperationException($"Scan {scanId} not found");

        scan.Status = "running";
        scan.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        try
        {
            // 1. Resolve DNS records to collect unique IPs
            var dnsInfo = await ResolveDnsAsync(scan.Target);
            var allIps = dnsInfo.AllIps.Distinct().ToList();

            // CRIT-02: Filter out private/reserved IPs to prevent SSRF
            var uniqueIps = new List<string>();
            foreach (var ipStr in allIps)
            {
                if (IPAddress.TryParse(ipStr, out var ip) && IsPrivateOrReserved(ip))
                {
                    _log.LogWarning("External scan {ScanId}: skipping private/reserved IP {Ip} (SSRF protection)",
                        scanId, ipStr);
                    continue;
                }
                uniqueIps.Add(ipStr);
            }

            _log.LogInformation("External scan {ScanId}: target={Target}, IPs found={Count}",
                scanId, scan.Target, uniqueIps.Count);

            if (uniqueIps.Count == 0)
            {
                scan.Status = "completed";
                scan.CompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return scan;
            }

            // 2. Scan all IPs in parallel (20 concurrent connections max)
            var results = new List<ExternalScanResult>();
            var semaphore = new SemaphoreSlim(20);
            var tasks = new List<Task<List<ExternalScanResult>>>();

            foreach (var ip in uniqueIps)
            {
                tasks.Add(ScanIpAsync(scanId, ip, semaphore));
            }

            var allResults = await Task.WhenAll(tasks);
            foreach (var batch in allResults)
                results.AddRange(batch);

            // 3. Save results
            _db.ExternalScanResults.AddRange(results);
            scan.Status = "completed";
            scan.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Reload with results
            await _db.Entry(scan).Collection(s => s.Results).LoadAsync();
            return scan;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "External scan {ScanId} failed", scanId);
            scan.Status = "failed";
            scan.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>
    /// Scan all target ports on a single IP.
    /// </summary>
    private async Task<List<ExternalScanResult>> ScanIpAsync(Guid scanId, string ip, SemaphoreSlim semaphore)
    {
        var results = new List<ExternalScanResult>();
        var portTasks = new List<Task<ExternalScanResult>>();

        foreach (var (port, service, defaultRisk) in TargetPorts)
        {
            portTasks.Add(ScanPortAsync(scanId, ip, port, service, defaultRisk, semaphore));
        }

        var portResults = await Task.WhenAll(portTasks);
        results.AddRange(portResults);
        return results;
    }

    /// <summary>
    /// Scan a single port on a single IP. Returns a result regardless of open/closed.
    /// </summary>
    private async Task<ExternalScanResult> ScanPortAsync(
        Guid scanId, string ip, int port, string serviceName,
        string defaultRisk, SemaphoreSlim semaphore)
    {
        await semaphore.WaitAsync();
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

            try
            {
                await client.ConnectAsync(ip, port, cts.Token);

                // Port is open — try to grab banner
                string? banner = null;
                try
                {
                    banner = await GrabBannerAsync(client, port);
                }
                catch
                {
                    // Banner grab failed, that's fine
                }

                return new ExternalScanResult
                {
                    ScanId = scanId,
                    IpAddress = ip,
                    Port = port,
                    Protocol = "TCP",
                    Status = "open",
                    Service = serviceName,
                    Risk = defaultRisk,
                    Banner = banner?.Length > 500 ? banner[..500] : banner,
                    Detail = $"Port {port}/{serviceName} is open on {ip}",
                };
            }
            catch (OperationCanceledException)
            {
                return new ExternalScanResult
                {
                    ScanId = scanId,
                    IpAddress = ip,
                    Port = port,
                    Protocol = "TCP",
                    Status = "filtered",
                    Service = serviceName,
                };
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                return new ExternalScanResult
                {
                    ScanId = scanId,
                    IpAddress = ip,
                    Port = port,
                    Protocol = "TCP",
                    Status = "closed",
                    Service = serviceName,
                };
            }
            catch
            {
                return new ExternalScanResult
                {
                    ScanId = scanId,
                    IpAddress = ip,
                    Port = port,
                    Protocol = "TCP",
                    Status = "filtered",
                    Service = serviceName,
                };
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Try to grab a service banner from an open port.
    /// HTTP ports get a HEAD request; others just read whatever the server sends.
    /// </summary>
    private static async Task<string?> GrabBannerAsync(TcpClient client, int port)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stream = client.GetStream();

        // For HTTP ports, send a HEAD request to get a response
        if (port is 80 or 8080 or 8888 or 443 or 8443 or 9090)
        {
            var headRequest = "HEAD / HTTP/1.0\r\nHost: target\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(headRequest, cts.Token);
        }
        else if (port is 21 or 25 or 110 or 143 or 587)
        {
            // FTP, SMTP, POP3, IMAP: server speaks first, just read
        }
        else
        {
            // Send a single byte to provoke a response
            await stream.WriteAsync(new byte[] { 0x0A }, cts.Token);
        }

        var buf = new byte[256];
        var read = await stream.ReadAsync(buf, cts.Token);
        if (read == 0) return null;

        var banner = Encoding.ASCII.GetString(buf, 0, read).Trim();
        // Strip control chars
        banner = new string(banner.Where(c => !char.IsControl(c) || c is '\r' or '\n').ToArray());
        return string.IsNullOrWhiteSpace(banner) ? null : banner;
    }

    /// <summary>
    /// Resolve DNS A records, MX records, and NS records for a target.
    /// If the target is an IP address, returns it directly.
    /// </summary>
    private async Task<DnsInfo> ResolveDnsAsync(string target)
    {
        var info = new DnsInfo();

        // Check if target is already an IP
        if (IPAddress.TryParse(target, out var directIp))
        {
            info.AllIps.Add(directIp.ToString());
            return info;
        }

        // Clean domain (remove protocol prefix if user pasted a URL)
        var domain = target
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        if (domain.Contains('/'))
            domain = domain[..domain.IndexOf('/')];

        try
        {
            // A records
            var hostEntry = await Dns.GetHostEntryAsync(domain);
            foreach (var addr in hostEntry.AddressList.Where(a => a.AddressFamily == AddressFamily.InterNetwork))
            {
                var ip = addr.ToString();
                info.ARecords.Add(ip);
                info.AllIps.Add(ip);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "DNS A record resolution failed for {Domain}", domain);
        }

        try
        {
            // MX records — resolve via nslookup-style: Dns.GetHostEntry on MX hosts
            // .NET doesn't have native MX lookup, so we try common mail subdomains
            var mxPrefixes = new[] { $"mail.{domain}", $"mx.{domain}", $"smtp.{domain}" };
            foreach (var mx in mxPrefixes)
            {
                try
                {
                    var mxEntry = await Dns.GetHostEntryAsync(mx);
                    foreach (var addr in mxEntry.AddressList.Where(a => a.AddressFamily == AddressFamily.InterNetwork))
                    {
                        var ip = addr.ToString();
                        info.MxRecords.Add((mx, ip));
                        info.AllIps.Add(ip);
                    }
                }
                catch { /* MX subdomain doesn't exist, skip */ }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "DNS MX resolution failed for {Domain}", domain);
        }

        try
        {
            // NS records — try common NS subdomains
            var nsPrefixes = new[] { $"ns1.{domain}", $"ns2.{domain}", $"dns.{domain}" };
            foreach (var ns in nsPrefixes)
            {
                try
                {
                    var nsEntry = await Dns.GetHostEntryAsync(ns);
                    foreach (var addr in nsEntry.AddressList.Where(a => a.AddressFamily == AddressFamily.InterNetwork))
                    {
                        info.NsRecords.Add((ns, addr.ToString()));
                    }
                }
                catch { /* NS subdomain doesn't exist, skip */ }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "DNS NS resolution failed for {Domain}", domain);
        }

        return info;
    }

    public class DnsInfo
    {
        public List<string> ARecords { get; set; } = [];
        public List<(string Host, string Ip)> MxRecords { get; set; } = [];
        public List<(string Host, string Ip)> NsRecords { get; set; } = [];
        public HashSet<string> AllIps { get; set; } = [];
    }
}
