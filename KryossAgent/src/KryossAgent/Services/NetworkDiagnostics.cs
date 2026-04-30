using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using KryossAgent.Models;

namespace KryossAgent.Services;

public static class NetworkDiagnostics
{
    private const int PingTimeoutMs = 2000;
    private const int PingCount = 5;
    private const int SpeedTestStreams = 12;
    private const int SpeedTestDurationSeconds = 12;
    private const int SpeedTestWarmupSeconds = 2;
    private const int SpeedTestUploadPerStream = 8 * 1024 * 1024;
    private const int SpeedTestLatencyPings = 10;

    public static async Task<NetworkDiagResult> RunAllAsync(
        string apiBaseUrl, bool verbose = false, CancellationToken ct = default)
    {
        var result = new NetworkDiagResult();

        using var globalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        globalCts.CancelAfter(TimeSpan.FromMinutes(5));
        var gct = globalCts.Token;

        var tasks = new List<Task>
        {
            Task.Run(() => result.RouteTable = GetRouteTable(), gct),
            Task.Run(() => result.Adapters = GetAdapterDetails(), gct),
            Task.Run(() => result.VpnInterfaces = DetectVpnInterfaces(), gct),
            Task.Run(async () =>
            {
                var (down, up, latency) = await MeasureInternetSpeedAsync(apiBaseUrl, gct);
                result.DownloadMbps = down;
                result.UploadMbps = up;
                result.InternetLatencyMs = latency;
            }, gct),
            Task.Run(async () => result.CloudEndpointLatency = await MeasureCloudEndpointLatencyAsync(gct), gct),
            Task.Run(async () => result.DnsResolutionMs = await MeasureDnsResolutionAsync(gct), gct),
            Task.Run(() => result.HostsFileEntryCount = CountHostsFileEntries(), gct),
            Task.Run(() => result.NtpConfigured = CheckNtpConfigured(), gct),
            Task.Run(() => result.WpadEnabled = CheckWpadEnabled(), gct),
            Task.Run(() =>
            {
                result.LlmnrEnabled = CheckLlmnrEnabled();
                result.NetbiosEnabled = CheckNetbiosEnabled();
            }, gct),
            Task.Run(() => result.ListeningPortCount = CountListeningPorts(), gct),
            Task.Run(() => result.DisconnectedWithIpCount = CountDisconnectedWithIp(), gct),
            Task.Run(() => result.NicTeamingDetected = DetectNicTeaming(), gct),
        };

        try { await Task.WhenAll(tasks); }
        catch { /* individual results will be null/0 */ }

        // Gateway latency (ping default gateway — true LAN latency indicator)
        try
        {
            var gw = GetDefaultGateway();
            if (gw != null)
            {
                result.GatewayIp = gw;
                var gwResult = await PingHostAsync(gw, gct);
                if (gwResult.Reachable)
                    result.GatewayLatencyMs = gwResult.AvgMs;
            }
        }
        catch { /* non-critical */ }

        // WAN link latency: ping unique next-hops from route table (non-local gateways)
        try
        {
            result.LinkLatency = await MeasureLinkLatencyAsync(result.RouteTable, result.GatewayIp, gct);
        }
        catch { /* non-critical */ }

        // Traceroute to API endpoint
        try
        {
            var apiHost = new Uri(apiBaseUrl).Host;
            result.TracerouteTarget = apiHost;
            result.Traceroute = await RunTracerouteAsync(apiHost, gct);
        }
        catch { /* non-critical */ }

        // Classify adapter counts
        if (result.Adapters != null)
        {
            result.WifiCount = result.Adapters.Count(a => a.Category == "wifi");
            result.VpnCount = result.Adapters.Count(a => a.Category == "vpn");
            result.EthCount = result.Adapters.Count(a => a.Category == "ethernet");
        }

        // Internal latency uses discovered hosts from ARP
        try
        {
            var arpHosts = GetArpHosts();
            result.ArpEntryCount = arpHosts.Count;
            if (arpHosts.Count > 0)
                result.InternalLatency = await MeasureInternalLatencyAsync(arpHosts, gct);
        }
        catch { /* non-critical */ }

        // Bandwidth utilization snapshot
        try { result.BandwidthSnapshot = GetBandwidthSnapshot(); }
        catch { /* non-critical */ }

        // IA-3: Aggregate jitter + packet loss from link latency probes
        var allProbes = new List<LatencyResult>();
        if (result.LinkLatency is { Count: > 0 }) allProbes.AddRange(result.LinkLatency);
        if (result.InternalLatency is { Count: > 0 }) allProbes.AddRange(result.InternalLatency);
        var reachable = allProbes.Where(p => p.Reachable && p.TotalSent > 0).ToList();
        if (reachable.Count > 0)
        {
            result.JitterMs = Math.Round(reachable.Average(p => p.JitterMs), 1);
            result.PacketLossPct = Math.Round(
                reachable.Average(p => (decimal)p.PacketLoss / p.TotalSent * 100m), 1);
        }

        return result;
    }

    public static async Task<(decimal downloadMbps, decimal uploadMbps, decimal latencyMs)>
        MeasureInternetSpeedAsync(string apiBaseUrl, CancellationToken ct)
    {
        decimal downloadMbps = 0, uploadMbps = 0, latencyMs = 0;

        try
        {
            var baseUri = new Uri(apiBaseUrl.TrimEnd('/'));

            // Phase 1: Latency — 10 sequential HTTP pings, trimmed mean
            var rtts = new List<double>();
            using (var pingHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                var pingUrl = new Uri(baseUri, "/v1/speedtest/ping");
                for (int i = 0; i < SpeedTestLatencyPings; i++)
                {
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        using var resp = await pingHttp.GetAsync(pingUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                        sw.Stop();
                        rtts.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    catch { /* skip failed pings */ }
                }
            }

            if (rtts.Count > 0)
            {
                rtts.Sort();
                var trimmed = rtts.Count >= 4
                    ? rtts.Skip(1).Take(rtts.Count - 2).ToList()
                    : rtts;
                latencyMs = Math.Round((decimal)trimmed.Average(), 1);
            }

            // Try blob-based speed test (v2.10+), fall back to legacy function-based
            var (dlMbps, ulMbps) = await MeasureViaBlobAsync(baseUri, ct);
            if (dlMbps > 0)
            {
                downloadMbps = dlMbps;
                uploadMbps = ulMbps;
            }
            else
            {
                (downloadMbps, uploadMbps) = await MeasureViaFunctionFallbackAsync(baseUri, ct);
            }
        }
        catch { /* speed test failed — return zeros */ }

        return (downloadMbps, uploadMbps, latencyMs);
    }

    private static async Task<(decimal dlMbps, decimal ulMbps)> MeasureViaBlobAsync(
        Uri baseUri, CancellationToken ct)
    {
        decimal dlMbps = 0, ulMbps = 0;

        try
        {
            // Get SAS tokens from server
            using var sasHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var sasUrl = new Uri(baseUri, "/v1/speedtest/sas");
            var sasJson = await sasHttp.GetStringAsync(sasUrl, ct);

            // Minimal JSON parse (no reflection for AOT)
            var downloadUrl = ExtractJsonString(sasJson, "downloadUrl");
            var uploadUrl = ExtractJsonString(sasJson, "uploadUrl");
            if (string.IsNullOrEmpty(downloadUrl)) return (0, 0);

            long downloadSizeBytes = 100 * 1024 * 1024; // 100 MB
            var sizeStr = ExtractJsonString(sasJson, "downloadSizeBytes");
            if (long.TryParse(sizeStr, out var parsed) && parsed > 0) downloadSizeBytes = parsed;

            // Phase 2: Multi-stream download — time-based, discard warmup
            using (var dlHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(SpeedTestDurationSeconds + 10) })
            {
                long totalBytes = 0;
                var warmupBytes = new long[1];
                var warmupDone = new TaskCompletionSource<bool>();
                var globalSw = Stopwatch.StartNew();

                // Schedule warmup marker
                _ = Task.Run(async () =>
                {
                    await Task.Delay(SpeedTestWarmupSeconds * 1000, ct);
                    warmupBytes[0] = Interlocked.Read(ref totalBytes);
                    warmupDone.TrySetResult(true);
                }, ct);

                var deadline = TimeSpan.FromSeconds(SpeedTestDurationSeconds);
                long chunkSize = downloadSizeBytes / SpeedTestStreams;

                var dlTasks = Enumerable.Range(0, SpeedTestStreams).Select(async i =>
                {
                    try
                    {
                        long from = i * chunkSize;
                        long to = (i == SpeedTestStreams - 1) ? downloadSizeBytes - 1 : from + chunkSize - 1;

                        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);

                        using var resp = await dlHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                        if (!resp.IsSuccessStatusCode) return;

                        using var stream = await resp.Content.ReadAsStreamAsync(ct);
                        var buf = new byte[256 * 1024]; // 256 KB read buffer
                        int read;
                        while ((read = await stream.ReadAsync(buf, ct)) > 0)
                        {
                            Interlocked.Add(ref totalBytes, read);
                            if (globalSw.Elapsed >= deadline) break;
                        }
                    }
                    catch { /* individual stream failure OK */ }
                }).ToArray();

                // Wait for streams or timeout
                var allDone = Task.WhenAll(dlTasks);
                await Task.WhenAny(allDone, Task.Delay(deadline + TimeSpan.FromSeconds(2), ct));
                globalSw.Stop();

                await warmupDone.Task.WaitAsync(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                var warmup = warmupBytes[0];
                var usefulBytes = Interlocked.Read(ref totalBytes) - warmup;
                var usefulSeconds = (decimal)(globalSw.ElapsedMilliseconds - SpeedTestWarmupSeconds * 1000) / 1000m;

                if (usefulBytes > 0 && usefulSeconds > 0.5m)
                {
                    dlMbps = Math.Round(usefulBytes * 8m / usefulSeconds / 1_000_000m, 2);
                }
            }

            // Phase 3: Multi-stream upload — time-based against blob SAS
            if (!string.IsNullOrEmpty(uploadUrl))
            {
                using var ulHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(SpeedTestDurationSeconds + 10) };
                var uploadPayload = new byte[SpeedTestUploadPerStream];
                Random.Shared.NextBytes(uploadPayload);
                long totalUploaded = 0;
                var sw3 = Stopwatch.StartNew();

                var ulTasks = Enumerable.Range(0, SpeedTestStreams).Select(async i =>
                {
                    try
                    {
                        using var content = new ByteArrayContent(uploadPayload);
                        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
                        request.Content = content;
                        request.Headers.Add("x-ms-blob-type", "BlockBlob");
                        using var resp = await ulHttp.SendAsync(request, ct);
                        if (resp.IsSuccessStatusCode)
                            Interlocked.Add(ref totalUploaded, uploadPayload.Length);
                    }
                    catch { /* individual stream failure OK */ }
                }).ToArray();

                try { await Task.WhenAll(ulTasks); }
                catch { /* partial results OK */ }
                sw3.Stop();

                if (totalUploaded > 0 && sw3.ElapsedMilliseconds > 100)
                {
                    ulMbps = Math.Round(totalUploaded * 8m / ((decimal)sw3.ElapsedMilliseconds / 1000m) / 1_000_000m, 2);
                }
            }
        }
        catch { /* blob speed test failed, caller will try fallback */ }

        return (dlMbps, ulMbps);
    }

    private static async Task<(decimal dlMbps, decimal ulMbps)> MeasureViaFunctionFallbackAsync(
        Uri baseUri, CancellationToken ct)
    {
        decimal dlMbps = 0, ulMbps = 0;
        int legacyPerStream = 10 * 1024 * 1024; // 10 MB per stream for fallback

        // Download fallback
        using (var dlHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
        {
            var downloadUrl = new Uri(baseUri, $"/v1/speedtest?size={legacyPerStream}");
            long totalDownloaded = 0;
            var sw = Stopwatch.StartNew();

            var dlTasks = Enumerable.Range(0, 8).Select(async _ =>
            {
                try
                {
                    using var resp = await dlHttp.SendAsync(
                        new HttpRequestMessage(HttpMethod.Get, downloadUrl),
                        HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!resp.IsSuccessStatusCode) return;
                    using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    var buf = new byte[128 * 1024];
                    int read;
                    while ((read = await stream.ReadAsync(buf, ct)) > 0)
                        Interlocked.Add(ref totalDownloaded, read);
                }
                catch { }
            }).ToArray();

            try { await Task.WhenAll(dlTasks); } catch { }
            sw.Stop();

            if (totalDownloaded > 0 && sw.ElapsedMilliseconds > 100)
                dlMbps = Math.Round(totalDownloaded * 8m / ((decimal)sw.ElapsedMilliseconds / 1000m) / 1_000_000m, 2);
        }

        // Upload fallback
        using (var ulHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
        {
            var uploadUrl = new Uri(baseUri, "/v1/speedtest");
            var payload = new byte[SpeedTestUploadPerStream];
            Random.Shared.NextBytes(payload);
            long totalUploaded = 0;
            var sw = Stopwatch.StartNew();

            var ulTasks = Enumerable.Range(0, 8).Select(async _ =>
            {
                try
                {
                    using var content = new ByteArrayContent(payload);
                    using var resp = await ulHttp.PostAsync(uploadUrl, content, ct);
                    if (resp.IsSuccessStatusCode)
                        Interlocked.Add(ref totalUploaded, payload.Length);
                }
                catch { }
            }).ToArray();

            try { await Task.WhenAll(ulTasks); } catch { }
            sw.Stop();

            if (totalUploaded > 0 && sw.ElapsedMilliseconds > 100)
                ulMbps = Math.Round(totalUploaded * 8m / ((decimal)sw.ElapsedMilliseconds / 1000m) / 1_000_000m, 2);
        }

        return (dlMbps, ulMbps);
    }

    private static string? ExtractJsonString(string json, string key)
    {
        var needle = $"\"{key}\":\"";
        var idx = json.IndexOf(needle, StringComparison.Ordinal);
        if (idx < 0)
        {
            // Try numeric value (no quotes)
            needle = $"\"{key}\":";
            idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return null;
            var start = idx + needle.Length;
            var end = json.IndexOfAny(new[] { ',', '}' }, start);
            return end > start ? json[start..end].Trim() : null;
        }
        else
        {
            var start = idx + needle.Length;
            var end = json.IndexOf('"', start);
            return end > start ? json[start..end] : null;
        }
    }

    public static async Task<List<LatencyResult>> MeasureInternalLatencyAsync(
        List<string> hosts, CancellationToken ct)
    {
        var results = new List<LatencyResult>();
        var semaphore = new SemaphoreSlim(20); // max 20 parallel pings

        var tasks = hosts.Select(async host =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var lr = await PingHostAsync(host, ct);
                lock (results) results.Add(lr);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
        return results.OrderBy(r => r.Host).ToList();
    }

    private static async Task<LatencyResult> PingHostAsync(string host, CancellationToken ct)
    {
        var result = new LatencyResult { Host = host };
        var rtts = new List<long>();

        using var ping = new Ping();
        for (int i = 0; i < PingCount; i++)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var reply = await ping.SendPingAsync(host, PingTimeoutMs);
                if (reply.Status == IPStatus.Success)
                    rtts.Add(reply.RoundtripTime);
                else
                    result.PacketLoss++;
            }
            catch { result.PacketLoss++; }
        }

        result.TotalSent = PingCount;
        if (rtts.Count > 0)
        {
            result.AvgMs = Math.Round((decimal)rtts.Average(), 1);
            result.MinMs = rtts.Min();
            result.MaxMs = rtts.Max();
            // Jitter = average absolute difference between consecutive RTTs
            if (rtts.Count > 1)
            {
                var diffs = new List<long>();
                for (int i = 1; i < rtts.Count; i++)
                    diffs.Add(Math.Abs(rtts[i] - rtts[i - 1]));
                result.JitterMs = Math.Round((decimal)diffs.Average(), 1);
            }
        }
        result.Reachable = rtts.Count > 0;

        // Detect subnet from host IP
        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            result.Subnet = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
        }

        return result;
    }

    public static List<RouteEntry> GetRouteTable()
    {
        var routes = new List<RouteEntry>();
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Destination, Mask, NextHop, InterfaceIndex, Metric1, Type, Protocol FROM Win32_IP4RouteTable");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                routes.Add(new RouteEntry
                {
                    Destination = obj["Destination"]?.ToString() ?? "",
                    Mask = obj["Mask"]?.ToString() ?? "",
                    NextHop = obj["NextHop"]?.ToString() ?? "",
                    InterfaceIndex = Convert.ToInt32(obj["InterfaceIndex"] ?? 0),
                    Metric = Convert.ToInt32(obj["Metric1"] ?? 0),
                    Type = Convert.ToInt32(obj["Type"] ?? 0),
                    Protocol = Convert.ToInt32(obj["Protocol"] ?? 0),
                });
                obj.Dispose();
            }
        }
        catch { /* non-critical */ }
        return routes;
    }

    public static List<VpnInterface> DetectVpnInterfaces()
    {
        var vpns = new List<VpnInterface>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                bool isVpn = nic.NetworkInterfaceType is
                    NetworkInterfaceType.Ppp or
                    NetworkInterfaceType.Tunnel or
                    NetworkInterfaceType.GenericModem;

                // Also detect by name patterns
                var name = nic.Name;
                var desc = nic.Description;
                if (!isVpn)
                {
                    isVpn = ContainsVpnKeyword(name) || ContainsVpnKeyword(desc);
                }

                if (isVpn)
                {
                    var props = nic.GetIPProperties();
                    var ipv4 = props.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

                    vpns.Add(new VpnInterface
                    {
                        Name = name,
                        Description = desc,
                        Type = nic.NetworkInterfaceType.ToString(),
                        IpAddress = ipv4?.Address.ToString(),
                        Speed = nic.Speed > 0 ? nic.Speed / 1_000_000 : null, // Mbps
                    });
                }
            }
        }
        catch { /* non-critical */ }
        return vpns;
    }

    private static bool ContainsVpnKeyword(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string[] keywords = [
            "VPN", "Cisco AnyConnect", "GlobalProtect", "WireGuard",
            "OpenVPN", "Fortinet", "SonicWall", "Pulse Secure",
            "Juniper", "NordVPN", "Tailscale", "ZeroTier",
            "TAP-Windows", "TUN", "PPP", "SSTP", "L2TP", "IKEv2"
        ];
        return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public static List<AdapterInfo> GetAdapterDetails()
    {
        var adapters = new List<AdapterInfo>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                var props = nic.GetIPProperties();
                var ipv4 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                var gateway = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                var dns = props.DnsAddresses
                    .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                    .Select(d => d.ToString()).ToList();

                var mac = nic.GetPhysicalAddress().ToString();
                if (mac.Length == 12)
                    mac = string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));

                var category = "ethernet";
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Wireless80211)
                    category = "wifi";
                else if (nic.NetworkInterfaceType is NetworkInterfaceType.Ppp or
                    NetworkInterfaceType.Tunnel or NetworkInterfaceType.GenericModem
                    || ContainsVpnKeyword(nic.Name) || ContainsVpnKeyword(nic.Description))
                    category = "vpn";

                int? mtu = null;
                try { mtu = props.GetIPv4Properties()?.Mtu; } catch { }

                var ipv6 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6
                        && !a.Address.IsIPv6LinkLocal);

                adapters.Add(new AdapterInfo
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    Type = nic.NetworkInterfaceType.ToString(),
                    Category = category,
                    Status = nic.OperationalStatus.ToString(),
                    SpeedMbps = nic.Speed > 0 ? nic.Speed / 1_000_000 : null,
                    MacAddress = mac,
                    IpAddress = ipv4?.Address.ToString(),
                    SubnetMask = ipv4?.IPv4Mask?.ToString(),
                    Gateway = gateway?.Address.ToString(),
                    DnsServers = dns,
                    DhcpEnabled = props.GetIPv4Properties()?.IsDhcpEnabled,
                    Mtu = mtu,
                    Ipv6Address = ipv6?.Address.ToString(),
                });
            }
        }
        catch { /* non-critical */ }
        return adapters;
    }

    public static BandwidthInfo? GetBandwidthSnapshot()
    {
        try
        {
            // Get total bytes across all active interfaces at two points 1 second apart
            var ifaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                    && n.NetworkInterfaceType is not NetworkInterfaceType.Tunnel)
                .ToList();

            if (ifaces.Count == 0) return null;

            long sent1 = 0, recv1 = 0;
            foreach (var nic in ifaces)
            {
                var stats = nic.GetIPv4Statistics();
                sent1 += stats.BytesSent;
                recv1 += stats.BytesReceived;
            }

            Thread.Sleep(1000);

            long sent2 = 0, recv2 = 0;
            foreach (var nic in ifaces)
            {
                var stats = nic.GetIPv4Statistics();
                sent2 += stats.BytesSent;
                recv2 += stats.BytesReceived;
            }

            return new BandwidthInfo
            {
                SendRateMbps = Math.Round((decimal)(sent2 - sent1) * 8 / 1_000_000, 2),
                RecvRateMbps = Math.Round((decimal)(recv2 - recv1) * 8 / 1_000_000, 2),
                TotalSentBytes = sent2,
                TotalRecvBytes = recv2,
            };
        }
        catch { return null; }
    }

    private static readonly string[] CloudEndpoints =
    [
        "outlook.office.com",
        "teams.microsoft.com",
        "sharepoint.com",
        "graph.microsoft.com",
        "login.microsoftonline.com",
        "admin.microsoft.com",
    ];

    public static async Task<List<CloudEndpointLatency>> MeasureCloudEndpointLatencyAsync(CancellationToken ct)
    {
        var results = new List<CloudEndpointLatency>();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var tasks = CloudEndpoints.Select(async endpoint =>
        {
            var entry = new CloudEndpointLatency { Endpoint = endpoint };
            try
            {
                var sw = Stopwatch.StartNew();
                using var req = new HttpRequestMessage(HttpMethod.Head, $"https://{endpoint}/");
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                sw.Stop();
                entry.LatencyMs = Math.Round((decimal)sw.Elapsed.TotalMilliseconds, 1);
                entry.Reachable = true;
            }
            catch
            {
                entry.Reachable = false;
            }
            lock (results) results.Add(entry);
        });

        try { await Task.WhenAll(tasks); }
        catch { /* partial results OK */ }
        return results.OrderBy(e => e.Endpoint).ToList();
    }

    public static async Task<decimal?> MeasureDnsResolutionAsync(CancellationToken ct)
    {
        string[] testHosts = ["outlook.office.com", "graph.microsoft.com", "login.microsoftonline.com"];
        var times = new List<double>();

        foreach (var host in testHosts)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var sw = Stopwatch.StartNew();
                await Dns.GetHostAddressesAsync(host, ct);
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            catch { /* skip failed lookups */ }
        }

        return times.Count > 0 ? Math.Round((decimal)times.Average(), 1) : null;
    }

    private static string? GetDefaultGateway()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = nic.GetIPProperties();
                var gw = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                if (gw != null && gw.Address.ToString() != "0.0.0.0")
                    return gw.Address.ToString();
            }
        }
        catch { }
        return null;
    }

    private static async Task<List<LatencyResult>> MeasureLinkLatencyAsync(
        List<RouteEntry>? routes, string? defaultGw, CancellationToken ct)
    {
        var results = new List<LatencyResult>();
        if (routes == null || routes.Count == 0) return results;

        var hops = routes
            .Where(r => r.NextHop != "0.0.0.0" && r.NextHop != "127.0.0.1"
                && r.Destination != "0.0.0.0" && r.Destination != "127.0.0.0"
                && r.NextHop != defaultGw)
            .Select(r => r.NextHop)
            .Distinct()
            .Take(10)
            .ToList();

        var semaphore = new SemaphoreSlim(5);
        var tasks = hops.Select(async hop =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var lr = await PingHostAsync(hop, ct);
                lr.Subnet = "wan-link";
                lock (results) results.Add(lr);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
        return results.OrderBy(r => r.Host).ToList();
    }

    private static int CountHostsFileEntries()
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
            if (!File.Exists(path)) return 0;
            return File.ReadAllLines(path)
                .Count(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'));
        }
        catch { return 0; }
    }

    private static bool CheckNtpConfigured()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\W32Time\Parameters");
            var ntpServer = key?.GetValue("NtpServer")?.ToString();
            return !string.IsNullOrWhiteSpace(ntpServer);
        }
        catch { return false; }
    }

    private static bool CheckWpadEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\WinHttp");
            var val = key?.GetValue("DisableWpad");
            return val is null or 0 or (int)0;
        }
        catch { return true; }
    }

    private static bool CheckLlmnrEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient");
            var val = key?.GetValue("EnableMulticast");
            return val is null || (val is int i && i != 0);
        }
        catch { return true; }
    }

    private static bool CheckNetbiosEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters");
            var val = key?.GetValue("NodeType");
            return val is null || (val is int i && i != 2);
        }
        catch { return true; }
    }

    private static int CountListeningPorts()
    {
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            return props.GetActiveTcpListeners().Length + props.GetActiveUdpListeners().Length;
        }
        catch { return 0; }
    }

    private static int CountDisconnectedWithIp()
    {
        int count = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;
                if (nic.OperationalStatus == OperationalStatus.Up) continue;
                try
                {
                    var props = nic.GetIPProperties();
                    if (props.UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                        count++;
                }
                catch { }
            }
        }
        catch { }
        return count;
    }

    private static bool DetectNicTeaming()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}");
            if (key == null) return false;
            foreach (var sub in key.GetSubKeyNames())
            {
                try
                {
                    using var sk = key.OpenSubKey(sub);
                    var teamNicId = sk?.GetValue("TeamNicID");
                    if (teamNicId != null) return true;
                }
                catch { }
            }
        }
        catch { }
        return false;
    }

    private static List<string> GetArpHosts()
    {
        var hosts = new List<string>();
        try
        {
            // Get local IP to determine subnet
            string? localIp = null;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = nic.GetIPProperties();
                if (props.GatewayAddresses.Count == 0) continue;
                var ipv4 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 != null) { localIp = ipv4.Address.ToString(); break; }
            }

            if (localIp == null) return hosts;
            var parts = localIp.Split('.');
            if (parts.Length != 4) return hosts;
            var prefix = $"{parts[0]}.{parts[1]}.{parts[2]}.";

            // Quick ARP probe: ping sweep a small range to populate ARP table
            // Then read ARP via WMI
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT IPAddress, MACAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True");
            // Fallback: try to ping nearby hosts and collect responding ones
            var pingTasks = new List<Task>();
            var found = new System.Collections.Concurrent.ConcurrentBag<string>();
            using var pinger = new SemaphoreSlim(50);

            for (int i = 1; i <= 254; i++)
            {
                var ip = prefix + i;
                if (ip == localIp) continue;
                pingTasks.Add(Task.Run(async () =>
                {
                    await pinger.WaitAsync();
                    try
                    {
                        using var p = new Ping();
                        var reply = await p.SendPingAsync(ip, 500);
                        if (reply.Status == IPStatus.Success)
                            found.Add(ip);
                    }
                    catch { }
                    finally { pinger.Release(); }
                }));
            }

            Task.WaitAll(pingTasks.ToArray(), TimeSpan.FromSeconds(15));
            hosts.AddRange(found);
        }
        catch { /* non-critical */ }
        return hosts;
    }

    public static async Task<List<TracerouteHop>> RunTracerouteAsync(
        string host, CancellationToken ct, int maxHops = 30, int timeoutMs = 1500)
    {
        var hops = new List<TracerouteHop>();
        using var ping = new Ping();
        var options = new PingOptions(1, true);

        for (int ttl = 1; ttl <= maxHops; ttl++)
        {
            if (ct.IsCancellationRequested) break;
            options.Ttl = ttl;
            try
            {
                var reply = await ping.SendPingAsync(host, timeoutMs, new byte[32], options);
                if (reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.Success)
                {
                    hops.Add(new TracerouteHop
                    {
                        Hop = ttl,
                        Address = reply.Address?.ToString(),
                        RttMs = reply.RoundtripTime,
                    });
                    if (reply.Status == IPStatus.Success) break;
                }
                else
                {
                    hops.Add(new TracerouteHop { Hop = ttl, TimedOut = true });
                }
            }
            catch
            {
                hops.Add(new TracerouteHop { Hop = ttl, TimedOut = true });
            }
        }
        return hops;
    }
}
