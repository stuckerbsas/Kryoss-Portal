using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface IExternalScanner
{
    Task<Guid> RunScanAsync(Guid orgId, string publicIp, Guid? userId = null);
}

public class ExternalScanner : IExternalScanner
{
    private readonly KryossDbContext _db;

    private static readonly int[] TopPorts = [
        21, 22, 23, 25, 53, 80, 110, 135, 139, 143,
        389, 443, 445, 465, 587, 636, 993, 995,
        1433, 1434, 1521, 1723, 2049, 2082, 2083,
        3306, 3389, 4443, 5000, 5060, 5222, 5432,
        5555, 5900, 5985, 5986, 6379, 6443, 7001,
        8000, 8008, 8080, 8081, 8443, 8888, 9000,
        9090, 9200, 9443, 10000, 11211, 27017,
    ];

    private static readonly HashSet<int> HttpPorts = [80, 443, 4443, 8000, 8008, 8080, 8081, 8443, 8888, 9000, 9090, 9443, 10000];

    public ExternalScanner(KryossDbContext db) => _db = db;

    public async Task<Guid> RunScanAsync(Guid orgId, string publicIp, Guid? userId = null)
    {
        var scan = new ExternalScan
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Target = publicIp,
            Status = "running",
            StartedAt = DateTime.UtcNow,
            CreatedBy = userId ?? Guid.Empty,
            CreatedAt = DateTime.UtcNow,
        };
        _db.ExternalScans.Add(scan);
        await _db.SaveChangesAsync();

        var results = new List<ExternalScanResult>();
        var sem = new SemaphoreSlim(20);

        var tasks = TopPorts.Select(async port =>
        {
            await sem.WaitAsync();
            try
            {
                using var tcp = new TcpClient();
                using var cts = new CancellationTokenSource(3000);
                try
                {
                    await tcp.ConnectAsync(publicIp, port, cts.Token);
                    var banner = await GrabBannerAsync(tcp, publicIp, port);
                    var (svcName, svcVersion) = banner != null ? ParseBanner(banner, port) : (null, null);

                    var result = new ExternalScanResult
                    {
                        ScanId = scan.Id,
                        IpAddress = publicIp,
                        Port = port,
                        Protocol = "TCP",
                        Status = "open",
                        Service = GetServiceName(port),
                        Risk = GetRiskLevel(port),
                        Banner = banner?.Length > 512 ? banner[..512] : banner,
                        ServiceName = svcName,
                        ServiceVersion = svcVersion,
                    };
                    lock (results) results.Add(result);
                }
                catch { /* closed/filtered port — expected */ }
            }
            finally { sem.Release(); }
        });

        await Task.WhenAll(tasks);

        _db.ExternalScanResults.AddRange(results);

        // Generate findings
        var findings = GenerateFindings(scan.Id, publicIp, results);
        _db.ExternalScanFindings.AddRange(findings);

        scan.Status = "completed";
        scan.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return scan.Id;
    }

    private static List<ExternalScanFinding> GenerateFindings(Guid scanId, string publicIp, List<ExternalScanResult> results)
    {
        var findings = new List<ExternalScanFinding>();

        foreach (var r in results)
        {
            var (severity, title, desc, remediation) = r.Port switch
            {
                3389 => ("critical", "RDP Exposed to Internet",
                    "Remote Desktop Protocol (TCP/3389) is accessible from the internet, making it a prime target for brute-force and BlueKeep-type attacks.",
                    "Block port 3389 at the firewall. Use VPN or Azure Bastion for remote access."),
                445 => ("critical", "SMB Exposed to Internet",
                    "Server Message Block (TCP/445) is accessible from the internet, enabling potential lateral movement and ransomware propagation (EternalBlue, WannaCry).",
                    "Block port 445 at the perimeter firewall immediately."),
                23 => ("critical", "Telnet Exposed to Internet",
                    "Telnet (TCP/23) transmits credentials in cleartext and is accessible from the internet.",
                    "Disable Telnet. Use SSH for remote administration."),
                21 => ("high", "FTP Exposed to Internet",
                    "FTP (TCP/21) transmits credentials in cleartext.",
                    "Replace FTP with SFTP or SCP. If FTP is required, restrict to specific IPs."),
                1433 => ("critical", "SQL Server Exposed to Internet",
                    "Microsoft SQL Server (TCP/1433) is directly accessible from the internet.",
                    "Block port 1433 at the firewall. Use VPN for database access."),
                3306 => ("critical", "MySQL Exposed to Internet",
                    "MySQL (TCP/3306) is directly accessible from the internet.",
                    "Block port 3306. Use SSH tunnels or VPN for database access."),
                5432 => ("critical", "PostgreSQL Exposed to Internet",
                    "PostgreSQL (TCP/5432) is directly accessible from the internet.",
                    "Block port 5432. Use SSH tunnels or VPN for database access."),
                6379 => ("critical", "Redis Exposed to Internet",
                    "Redis (TCP/6379) is accessible from the internet, often with no authentication.",
                    "Block port 6379. Never expose Redis to the internet."),
                27017 => ("critical", "MongoDB Exposed to Internet",
                    "MongoDB (TCP/27017) is accessible from the internet, often with default no-auth config.",
                    "Block port 27017. Enable authentication and restrict to internal access."),
                5900 or 5901 => ("high", "VNC Exposed to Internet",
                    "VNC remote access is exposed to the internet with often weak authentication.",
                    "Block VNC ports. Use VPN for remote access."),
                11211 => ("high", "Memcached Exposed to Internet",
                    "Memcached is exposed, which can be used for DDoS amplification attacks.",
                    "Block port 11211. Bind Memcached to localhost only."),
                135 => ("medium", "RPC Exposed to Internet",
                    "RPC endpoint mapper is accessible from the internet, potential lateral movement vector.",
                    "Block port 135 at the perimeter firewall."),
                139 => ("high", "NetBIOS Exposed to Internet",
                    "NetBIOS Session Service is accessible from the internet, leaking network information.",
                    "Block port 139 at the perimeter firewall."),
                25 => ("medium", "SMTP Exposed to Internet",
                    "SMTP is accessible. Verify it is not configured as an open relay.",
                    "Ensure SMTP authentication is required. Test for open relay."),
                _ => ((string?)null, (string?)null, (string?)null, (string?)null),
            };

            if (severity != null)
            {
                findings.Add(new ExternalScanFinding
                {
                    ScanId = scanId,
                    ScanResultId = null,
                    Severity = severity,
                    Title = title!,
                    Description = desc,
                    Remediation = remediation,
                    Port = r.Port,
                    PublicIp = publicIp,
                });
            }
        }

        // Check for HTTP without HTTPS redirect
        var hasHttp = results.Any(r => r.Port == 80);
        var hasHttps = results.Any(r => r.Port == 443);
        if (hasHttp && !hasHttps)
        {
            findings.Add(new ExternalScanFinding
            {
                ScanId = scanId,
                Severity = "medium",
                Title = "HTTP Without HTTPS",
                Description = "Port 80 (HTTP) is open but no HTTPS (443) is available. Traffic may be unencrypted.",
                Remediation = "Enable HTTPS with a valid certificate and redirect HTTP to HTTPS.",
                Port = 80,
                PublicIp = publicIp,
            });
        }

        // Unknown services on high ports
        foreach (var r in results.Where(r => r.Port > 1024 && r.Service == null))
        {
            findings.Add(new ExternalScanFinding
            {
                ScanId = scanId,
                Severity = "low",
                Title = $"Unknown Service on Port {r.Port}",
                Description = $"An unidentified service is listening on TCP/{r.Port} and is accessible from the internet.",
                Remediation = "Identify the service and determine if external access is necessary. Block if not required.",
                Port = r.Port,
                PublicIp = publicIp,
            });
        }

        return findings;
    }

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

            using var readCts = new CancellationTokenSource(3000);
            var read = await stream.ReadAsync(buf, readCts.Token);
            return read > 0 ? Encoding.ASCII.GetString(buf, 0, read).Trim() : null;
        }
        catch { return null; }
    }

    private static (string? Name, string? Version) ParseBanner(string banner, int port)
    {
        var serverMatch = Regex.Match(banner, @"Server:\s*(.+)", RegexOptions.IgnoreCase);
        if (serverMatch.Success)
        {
            var server = serverMatch.Groups[1].Value.Trim();
            var verMatch = Regex.Match(server, @"^([^\s/]+)[/\s]+(\S+)");
            return verMatch.Success ? (verMatch.Groups[1].Value, verMatch.Groups[2].Value) : (server, null);
        }
        if (banner.StartsWith("SSH-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = banner.Split('-', 3);
            if (parts.Length >= 3)
            {
                var impl = parts[2].Split(' ')[0];
                var verMatch = Regex.Match(impl, @"^([A-Za-z]+)[_](.+)");
                return verMatch.Success ? (verMatch.Groups[1].Value, verMatch.Groups[2].Value) : (impl, null);
            }
        }
        if (banner.StartsWith("220"))
        {
            var line = banner.Split('\n')[0][3..].Trim().TrimStart('-');
            var verMatch = Regex.Match(line, @"([\w.-]+)\s+([\d.]+)");
            return verMatch.Success ? (verMatch.Groups[1].Value, verMatch.Groups[2].Value) : (line.Length > 60 ? line[..60] : line, null);
        }
        return (null, null);
    }

    private static readonly Dictionary<int, string> ServiceNames = new()
    {
        [21] = "FTP", [22] = "SSH", [23] = "Telnet", [25] = "SMTP", [53] = "DNS",
        [80] = "HTTP", [110] = "POP3", [135] = "RPC", [139] = "NetBIOS", [143] = "IMAP",
        [389] = "LDAP", [443] = "HTTPS", [445] = "SMB", [465] = "SMTPS", [587] = "Submission",
        [636] = "LDAPS", [993] = "IMAPS", [995] = "POP3S", [1433] = "MSSQL", [1521] = "Oracle",
        [1723] = "PPTP", [3306] = "MySQL", [3389] = "RDP", [4443] = "HTTPS-Alt",
        [5060] = "SIP", [5222] = "XMPP", [5432] = "PostgreSQL", [5900] = "VNC",
        [5985] = "WinRM", [6379] = "Redis", [6443] = "K8s-API",
        [8080] = "HTTP-Proxy", [8443] = "HTTPS-Alt", [9200] = "Elasticsearch",
        [11211] = "Memcached", [27017] = "MongoDB",
    };

    private static string? GetServiceName(int port) => ServiceNames.GetValueOrDefault(port);

    private static string? GetRiskLevel(int port) => port switch
    {
        23 or 4444 or 6379 or 27017 => "critical",
        21 or 139 or 445 or 1433 or 1434 or 3306 or 3389 or 5432 or 5555 or 5900 or 5901 or 11211 => "high",
        25 or 135 => "medium",
        _ => null,
    };
}
