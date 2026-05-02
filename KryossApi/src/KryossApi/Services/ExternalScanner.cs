using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services.CloudAssessment.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services;

public interface IExternalScanner
{
    Task<Guid> RunScanAsync(Guid orgId, string target, Guid? userId = null);
}

public class ExternalScanner : IExternalScanner
{
    private readonly KryossDbContext _db;
    private readonly IDnsLookup _dns;
    private readonly ILogger<ExternalScanner> _log;

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

    public ExternalScanner(KryossDbContext db, IDnsLookup dns, ILogger<ExternalScanner> log)
    {
        _db = db;
        _dns = dns;
        _log = log;
    }

    private static bool IsDomain(string target) =>
        target.Contains('.') && !IPAddress.TryParse(target, out _);

    public async Task<Guid> RunScanAsync(Guid orgId, string target, Guid? userId = null)
    {
        var isDomain = IsDomain(target);
        var scan = new ExternalScan
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Target = target,
            Status = "running",
            StartedAt = DateTime.UtcNow,
            CreatedBy = userId ?? Guid.Empty,
            CreatedAt = DateTime.UtcNow,
        };
        _db.ExternalScans.Add(scan);
        await _db.SaveChangesAsync();

        // Resolve IPs to scan
        var ips = new List<string>();
        if (isDomain)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(target);
                ips.AddRange(addresses.Select(a => a.ToString()));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "DNS resolution failed for {Domain}", target);
            }
        }
        else
        {
            ips.Add(target);
        }

        var results = new List<ExternalScanResult>();
        var sem = new SemaphoreSlim(20);

        foreach (var ip in ips.Distinct())
        {
            var portTasks = TopPorts.Select(async port =>
            {
                await sem.WaitAsync();
                try
                {
                    using var tcp = new TcpClient();
                    using var cts = new CancellationTokenSource(3000);
                    try
                    {
                        await tcp.ConnectAsync(ip, port, cts.Token);
                        var banner = await GrabBannerAsync(tcp, ip, port);
                        var (svcName, svcVersion) = banner != null ? ParseBanner(banner, port) : (null, null);

                        var result = new ExternalScanResult
                        {
                            ScanId = scan.Id,
                            IpAddress = ip,
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
            await Task.WhenAll(portTasks);
        }

        _db.ExternalScanResults.AddRange(results);

        // Generate port findings
        var findings = new List<ExternalScanFinding>();
        foreach (var ip in ips.Distinct())
        {
            var ipResults = results.Where(r => r.IpAddress == ip).ToList();
            findings.AddRange(GenerateFindings(scan.Id, ip, ipResults));
        }

        // Domain-specific checks
        if (isDomain)
        {
            findings.AddRange(await CheckTlsCertificateAsync(scan.Id, target));
            findings.AddRange(await CheckHttpSecurityHeadersAsync(scan.Id, target));
            findings.AddRange(await CheckWebSecurityAsync(scan.Id, target));
            findings.AddRange(await CheckMailRecordsAsync(scan.Id, target));
            findings.AddRange(await CheckDnsHealthAsync(scan.Id, target));
        }

        _db.ExternalScanFindings.AddRange(findings);

        scan.Status = "completed";
        scan.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return scan.Id;
    }

    // ── Domain-specific checks ──

    private async Task<List<ExternalScanFinding>> CheckTlsCertificateAsync(Guid scanId, string domain)
    {
        var findings = new List<ExternalScanFinding>();
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(5000);
            await tcp.ConnectAsync(domain, 443, cts.Token);
            using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            var tlsParams = new SslClientAuthenticationOptions
            {
                TargetHost = domain,
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            };
            await ssl.AuthenticateAsClientAsync(tlsParams);

            var cert = ssl.RemoteCertificate as X509Certificate2 ?? new X509Certificate2(ssl.RemoteCertificate!);
            var daysUntilExpiry = (cert.NotAfter - DateTime.UtcNow).TotalDays;

            if (daysUntilExpiry < 30)
            {
                findings.Add(new ExternalScanFinding
                {
                    ScanId = scanId,
                    Severity = daysUntilExpiry < 0 ? "critical" : "high",
                    Title = daysUntilExpiry < 0 ? "TLS Certificate Expired" : "TLS Certificate Expiring Soon",
                    Description = $"Certificate for {domain} expires on {cert.NotAfter:yyyy-MM-dd} ({Math.Abs((int)daysUntilExpiry)} days {(daysUntilExpiry < 0 ? "ago" : "remaining")}). Subject: {cert.Subject}, Issuer: {cert.Issuer}.",
                    Remediation = "Renew the TLS certificate before expiry.",
                    Port = 443, PublicIp = domain, Category = "tls",
                });
            }

            var keySize = 0;
            if (cert.PublicKey.GetRSAPublicKey() is { } rsa) keySize = rsa.KeySize;
            else if (cert.PublicKey.GetECDsaPublicKey() is { } ec) keySize = ec.KeySize;
            if (keySize > 0 && keySize < 2048)
            {
                findings.Add(new ExternalScanFinding
                {
                    ScanId = scanId, Severity = "medium",
                    Title = "TLS Weak Key Size",
                    Description = $"Certificate key size is {keySize} bits. Minimum recommended is 2048 bits (RSA) or 256 bits (ECDSA).",
                    Remediation = "Regenerate the certificate with a stronger key (RSA-2048+ or ECDSA-256+).",
                    Port = 443, PublicIp = domain, Category = "tls",
                });
            }

            if (ssl.SslProtocol is System.Security.Authentication.SslProtocols.Tls
                or System.Security.Authentication.SslProtocols.Tls11)
            {
                findings.Add(new ExternalScanFinding
                {
                    ScanId = scanId, Severity = "high",
                    Title = "Deprecated TLS Protocol",
                    Description = $"Server negotiated {ssl.SslProtocol}. TLS 1.0 and 1.1 are deprecated (RFC 8996) and vulnerable to BEAST/POODLE.",
                    Remediation = "Disable TLS 1.0 and 1.1. Require TLS 1.2 or 1.3 minimum.",
                    Port = 443, PublicIp = domain, Category = "tls",
                });
            }

            if (!cert.Subject.Contains(domain, StringComparison.OrdinalIgnoreCase))
            {
                var san = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
                var dnsNames = san?.EnumerateDnsNames().ToList() ?? [];
                if (!dnsNames.Any(n => n.Equals(domain, StringComparison.OrdinalIgnoreCase)
                    || (n.StartsWith("*.") && domain.EndsWith(n[1..], StringComparison.OrdinalIgnoreCase))))
                {
                    findings.Add(new ExternalScanFinding
                    {
                        ScanId = scanId, Severity = "high",
                        Title = "TLS Certificate Name Mismatch",
                        Description = $"Certificate CN/SAN does not match '{domain}'. Subject: {cert.Subject}. SANs: {string.Join(", ", dnsNames.Take(5))}.",
                        Remediation = "Obtain a certificate that covers the correct domain name.",
                        Port = 443, PublicIp = domain, Category = "tls",
                    });
                }
            }

            if (cert.NotBefore > DateTime.UtcNow)
            {
                findings.Add(new ExternalScanFinding
                {
                    ScanId = scanId, Severity = "high",
                    Title = "TLS Certificate Not Yet Valid",
                    Description = $"Certificate is not valid until {cert.NotBefore:yyyy-MM-dd}.",
                    Remediation = "Check certificate dates and replace if issued incorrectly.",
                    Port = 443, PublicIp = domain, Category = "tls",
                });
            }

            if (cert.Issuer == cert.Subject)
            {
                findings.Add(new ExternalScanFinding
                {
                    ScanId = scanId, Severity = "medium",
                    Title = "Self-Signed TLS Certificate",
                    Description = "The TLS certificate is self-signed and will not be trusted by browsers or clients.",
                    Remediation = "Replace with a certificate from a trusted Certificate Authority (e.g., Let's Encrypt).",
                    Port = 443, PublicIp = domain, Category = "tls",
                });
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "TLS check failed for {Domain}", domain);
        }
        return findings;
    }

    private async Task<List<ExternalScanFinding>> CheckHttpSecurityHeadersAsync(Guid scanId, string domain)
    {
        var findings = new List<ExternalScanFinding>();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KryossScanner/1.0");
            var resp = await http.GetAsync($"https://{domain}");
            var headers = resp.Headers.Concat(resp.Content.Headers);
            var headerDict = headers.ToDictionary(
                h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);

            void CheckMissing(string name, string title, string desc, string fix) {
                if (!headerDict.ContainsKey(name))
                    findings.Add(new ExternalScanFinding {
                        ScanId = scanId, Severity = "medium",
                        Title = title, Description = desc,
                        Remediation = fix, Port = 443, PublicIp = domain, Category = "header",
                    });
            }

            CheckMissing("Strict-Transport-Security",
                "Missing HSTS Header",
                "The Strict-Transport-Security header is not set, allowing potential downgrade attacks.",
                "Add 'Strict-Transport-Security: max-age=31536000; includeSubDomains' to all HTTPS responses.");
            CheckMissing("Content-Security-Policy",
                "Missing Content-Security-Policy",
                "No Content-Security-Policy header found, increasing XSS and injection risk.",
                "Define a Content-Security-Policy that restricts script and resource origins.");
            CheckMissing("X-Frame-Options",
                "Missing X-Frame-Options",
                "The X-Frame-Options header is not set, allowing clickjacking attacks.",
                "Add 'X-Frame-Options: DENY' or 'SAMEORIGIN'.");
            CheckMissing("X-Content-Type-Options",
                "Missing X-Content-Type-Options",
                "The X-Content-Type-Options header is not set, allowing MIME sniffing.",
                "Add 'X-Content-Type-Options: nosniff'.");
            CheckMissing("Referrer-Policy",
                "Missing Referrer-Policy",
                "No Referrer-Policy header found, potentially leaking URL information.",
                "Add 'Referrer-Policy: strict-origin-when-cross-origin'.");
            CheckMissing("Permissions-Policy",
                "Missing Permissions-Policy",
                "No Permissions-Policy header found. Browser features (camera, microphone, geolocation) are not restricted.",
                "Add 'Permissions-Policy: camera=(), microphone=(), geolocation=()' to restrict browser feature access.");

            if (headerDict.TryGetValue("Server", out var server) && server.Length > 0)
            {
                if (Regex.IsMatch(server, @"\d+\.\d+", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                    findings.Add(new ExternalScanFinding {
                        ScanId = scanId, Severity = "low",
                        Title = "Server Version Disclosed",
                        Description = $"Server header exposes version info: '{server}'. Attackers use this to find known vulnerabilities.",
                        Remediation = "Remove version information from the Server header.",
                        Port = 443, PublicIp = domain, Category = "header",
                    });
            }

            if (headerDict.TryGetValue("X-Powered-By", out var poweredBy))
                findings.Add(new ExternalScanFinding {
                    ScanId = scanId, Severity = "low",
                    Title = "X-Powered-By Header Disclosed",
                    Description = $"X-Powered-By header exposes technology stack: '{poweredBy}'.",
                    Remediation = "Remove the X-Powered-By header to reduce information disclosure.",
                    Port = 443, PublicIp = domain, Category = "header",
                });
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "HTTP header check failed for {Domain}", domain);
        }
        return findings;
    }

    private async Task<List<ExternalScanFinding>> CheckMailRecordsAsync(Guid scanId, string domain)
    {
        var findings = new List<ExternalScanFinding>();
        var ct = CancellationToken.None;
        try
        {
            // MX check
            var mxRecords = await _dns.GetMxRecordsAsync(domain, ct);

            // SPF check
            var txtRecords = await _dns.GetTxtRecordsAsync(domain, ct);
            var spf = txtRecords.FirstOrDefault(t => t.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase));
            if (spf == null)
            {
                findings.Add(new ExternalScanFinding
                {
                    ScanId = scanId, Severity = "high",
                    Title = "No SPF Record",
                    Description = "No SPF (Sender Policy Framework) TXT record found. Attackers can spoof emails from this domain.",
                    Remediation = "Add a TXT record with a valid SPF policy (e.g., 'v=spf1 include:_spf.google.com ~all').",
                    PublicIp = domain, Category = "mail",
                });
            }
            else if (spf.Contains("+all", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ExternalScanFinding
                {
                    ScanId = scanId, Severity = "high",
                    Title = "SPF Too Permissive",
                    Description = "SPF record uses '+all' which allows any server to send email on behalf of this domain.",
                    Remediation = "Change '+all' to '~all' (softfail) or '-all' (hardfail).",
                    PublicIp = domain, Category = "mail",
                });
            }

            var dmarcRecords = await _dns.GetTxtRecordsAsync($"_dmarc.{domain}", ct);
            var dmarc = dmarcRecords.FirstOrDefault(t => t.StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase));
            if (dmarc == null)
            {
                findings.Add(new ExternalScanFinding
                {
                    ScanId = scanId, Severity = "high",
                    Title = "No DMARC Record",
                    Description = "No DMARC record found at _dmarc." + domain + ". Email spoofing prevention is incomplete.",
                    Remediation = "Add a TXT record at _dmarc." + domain + " (e.g., 'v=DMARC1; p=quarantine; rua=mailto:dmarc@" + domain + "').",
                    PublicIp = domain, Category = "mail",
                });
            }
            else if (dmarc.Contains("p=none", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ExternalScanFinding
                {
                    ScanId = scanId, Severity = "medium",
                    Title = "DMARC Policy Set to None",
                    Description = "DMARC policy is 'none' — spoofed emails are only reported, not blocked.",
                    Remediation = "Change DMARC policy to 'quarantine' or 'reject' after monitoring reports.",
                    PublicIp = domain, Category = "mail",
                });
            }

            // DKIM check
            var dkimS1 = await _dns.GetCnameAsync($"selector1._domainkey.{domain}", ct);
            var dkimS2 = await _dns.GetCnameAsync($"selector2._domainkey.{domain}", ct);
            if (dkimS1 == null && dkimS2 == null)
            {
                var dkimTxt = await _dns.GetTxtRecordsAsync($"selector1._domainkey.{domain}", ct);
                if (!dkimTxt.Any(t => t.Contains("v=DKIM1", StringComparison.OrdinalIgnoreCase)))
                    findings.Add(new ExternalScanFinding
                    {
                        ScanId = scanId, Severity = "medium",
                        Title = "No DKIM Records Found",
                        Description = "No DKIM selector records found (selector1/selector2). Email authentication is incomplete without DKIM.",
                        Remediation = "Configure DKIM signing for your email domain in your mail provider.",
                        PublicIp = domain, Category = "mail",
                    });
            }

            // MTA-STS check
            var mtaSts = await _dns.GetTxtRecordsAsync($"_mta-sts.{domain}", ct);
            if (!mtaSts.Any(t => t.StartsWith("v=STSv1", StringComparison.OrdinalIgnoreCase)))
            {
                if (mxRecords.Count > 0)
                    findings.Add(new ExternalScanFinding
                    {
                        ScanId = scanId, Severity = "low",
                        Title = "No MTA-STS Record",
                        Description = "MTA-STS (RFC 8461) is not configured. Inbound mail transport may be susceptible to downgrade attacks.",
                        Remediation = "Publish an MTA-STS policy at _mta-sts." + domain + " and host the policy file at https://mta-sts." + domain + "/.well-known/mta-sts.txt.",
                        PublicIp = domain, Category = "mail",
                    });
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Mail record check failed for {Domain}", domain);
        }
        return findings;
    }

    private async Task<List<ExternalScanFinding>> CheckDnsHealthAsync(Guid scanId, string domain)
    {
        var findings = new List<ExternalScanFinding>();
        var ct = CancellationToken.None;
        try
        {
            var nsRecords = await _dns.GetNsRecordsAsync(domain, ct);
            if (nsRecords.Count == 0)
                findings.Add(new ExternalScanFinding {
                    ScanId = scanId, Severity = "critical",
                    Title = "No NS Records Found",
                    Description = $"No nameserver records found for {domain}. DNS resolution will fail.",
                    Remediation = "Configure at least two authoritative nameservers for the domain.",
                    PublicIp = domain, Category = "dns",
                });
            else if (nsRecords.Count == 1)
                findings.Add(new ExternalScanFinding {
                    ScanId = scanId, Severity = "high",
                    Title = "Single Nameserver",
                    Description = $"Only one NS record found ({nsRecords[0]}). Single point of failure for DNS resolution.",
                    Remediation = "Add at least one additional nameserver on a different network for redundancy.",
                    PublicIp = domain, Category = "dns",
                });

            var soa = await _dns.GetSoaRecordAsync(domain, ct);
            if (soa != null)
            {
                if (soa.Refresh < 3600)
                    findings.Add(new ExternalScanFinding {
                        ScanId = scanId, Severity = "low",
                        Title = "SOA Refresh Too Aggressive",
                        Description = $"SOA refresh interval is {soa.Refresh}s (< 1 hour). This may cause excessive zone transfer traffic.",
                        Remediation = "Set SOA refresh to at least 3600 seconds (1 hour).",
                        PublicIp = domain, Category = "dns",
                    });
                if (soa.Expire < 604800)
                    findings.Add(new ExternalScanFinding {
                        ScanId = scanId, Severity = "low",
                        Title = "SOA Expire Too Short",
                        Description = $"SOA expire value is {soa.Expire}s ({soa.Expire / 86400}d). If the primary NS goes down, secondaries will stop serving the zone quickly.",
                        Remediation = "Set SOA expire to at least 604800 seconds (7 days).",
                        PublicIp = domain, Category = "dns",
                    });
            }

            var caaRecords = await _dns.GetCaaRecordsAsync(domain, ct);
            if (caaRecords.Count == 0)
                findings.Add(new ExternalScanFinding {
                    ScanId = scanId, Severity = "medium",
                    Title = "No CAA Records",
                    Description = "No CAA (Certificate Authority Authorization) records found. Any CA can issue certificates for this domain.",
                    Remediation = "Add CAA records to restrict which CAs can issue certificates (e.g., '0 issue \"letsencrypt.org\"').",
                    PublicIp = domain, Category = "dns",
                });

            var hasDnssec = await _dns.CheckDnssecAsync(domain, ct);
            if (!hasDnssec)
                findings.Add(new ExternalScanFinding {
                    ScanId = scanId, Severity = "medium",
                    Title = "DNSSEC Not Enabled",
                    Description = "DNSSEC is not configured for this domain. DNS responses can be spoofed (cache poisoning).",
                    Remediation = "Enable DNSSEC at your domain registrar and DNS provider.",
                    PublicIp = domain, Category = "dns",
                });

            // Dangling CNAME check on common subdomains
            string[] subdomains = ["www", "mail", "autodiscover", "ftp", "vpn", "remote", "portal", "app"];
            foreach (var sub in subdomains)
            {
                var cname = await _dns.GetCnameAsync($"{sub}.{domain}", ct);
                if (cname != null)
                {
                    var resolved = await _dns.GetARecordsAsync(cname, ct);
                    if (resolved.Count == 0)
                        findings.Add(new ExternalScanFinding {
                            ScanId = scanId, Severity = "high",
                            Title = $"Dangling CNAME: {sub}.{domain}",
                            Description = $"{sub}.{domain} has a CNAME to {cname} which does not resolve. This may be vulnerable to subdomain takeover.",
                            Remediation = $"Remove the dangling CNAME record for {sub}.{domain} or point it to a valid target.",
                            PublicIp = domain, Category = "dns",
                        });
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "DNS health check failed for {Domain}", domain);
        }
        return findings;
    }

    private async Task<List<ExternalScanFinding>> CheckWebSecurityAsync(Guid scanId, string domain)
    {
        var findings = new List<ExternalScanFinding>();
        try
        {
            // HTTP → HTTPS redirect check
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KryossScanner/1.0");

            try
            {
                var httpResp = await http.GetAsync($"http://{domain}");
                if ((int)httpResp.StatusCode is not (>= 301 and <= 308))
                {
                    findings.Add(new ExternalScanFinding {
                        ScanId = scanId, Severity = "medium",
                        Title = "No HTTP to HTTPS Redirect",
                        Description = $"HTTP request to {domain} returns {(int)httpResp.StatusCode} instead of a redirect to HTTPS.",
                        Remediation = "Configure a 301 redirect from HTTP to HTTPS for all requests.",
                        PublicIp = domain, Category = "web",
                    });
                }
                else
                {
                    var location = httpResp.Headers.Location?.ToString() ?? "";
                    if (!location.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        findings.Add(new ExternalScanFinding {
                            ScanId = scanId, Severity = "medium",
                            Title = "HTTP Redirect Not to HTTPS",
                            Description = $"HTTP redirects to '{location}' instead of an HTTPS URL.",
                            Remediation = "Ensure HTTP redirects to the HTTPS version of the site.",
                            PublicIp = domain, Category = "web",
                        });
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "HTTP redirect check failed for {Domain}", domain);
            }

            // Cookie security on HTTPS
            try
            {
                using var httpsHandler = new HttpClientHandler { AllowAutoRedirect = true, UseCookies = true };
                using var https = new HttpClient(httpsHandler) { Timeout = TimeSpan.FromSeconds(10) };
                https.DefaultRequestHeaders.UserAgent.ParseAdd("KryossScanner/1.0");
                var resp = await https.GetAsync($"https://{domain}");
                if (resp.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    foreach (var cookie in cookies)
                    {
                        var parts = cookie.Split(';').Select(p => p.Trim()).ToList();
                        var name = parts[0].Split('=')[0];
                        var flags = parts.Skip(1).Select(p => p.ToLowerInvariant()).ToList();
                        var issues = new List<string>();
                        if (!flags.Contains("secure")) issues.Add("missing Secure flag");
                        if (!flags.Contains("httponly")) issues.Add("missing HttpOnly flag");
                        if (!flags.Any(f => f.StartsWith("samesite"))) issues.Add("missing SameSite attribute");
                        if (issues.Count > 0)
                            findings.Add(new ExternalScanFinding {
                                ScanId = scanId, Severity = "medium",
                                Title = $"Insecure Cookie: {name}",
                                Description = $"Cookie '{name}' has security issues: {string.Join(", ", issues)}.",
                                Remediation = "Set Secure, HttpOnly, and SameSite=Strict (or Lax) on all cookies.",
                                PublicIp = domain, Category = "web",
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Cookie check failed for {Domain}", domain);
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Web security check failed for {Domain}", domain);
        }
        return findings;
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
                    Category = "port",
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
                Category = "port",
            });
        }

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
                Category = "port",
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
