using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface IWanHealthService
{
    Task EvaluateAsync(Guid organizationId);
}

public class WanHealthService : IWanHealthService
{
    private readonly KryossDbContext _db;

    public WanHealthService(KryossDbContext db) => _db = db;

    public async Task EvaluateAsync(Guid organizationId)
    {
        var sites = await _db.NetworkSites
            .Where(s => s.OrganizationId == organizationId && s.PublicIp != null)
            .ToListAsync();

        // Remove old findings
        var oldFindings = await _db.WanFindings
            .Where(f => f.OrganizationId == organizationId)
            .ToListAsync();
        _db.WanFindings.RemoveRange(oldFindings);

        var cutoff30d = DateTime.UtcNow.AddDays(-30);

        foreach (var site in sites)
        {
            var machineIds = await _db.Machines
                .Where(m => m.OrganizationId == organizationId && m.LastPublicIp == site.PublicIp)
                .Select(m => m.Id)
                .ToListAsync();

            if (machineIds.Count == 0) continue;

            var recentDiags = await _db.MachineNetworkDiags
                .Where(d => machineIds.Contains(d.MachineId) && d.ScannedAt >= cutoff30d)
                .OrderByDescending(d => d.ScannedAt)
                .Take(100)
                .ToListAsync();

            if (recentDiags.Count == 0) continue;

            // Aggregate WAN metrics across machines at this site
            var jitters = recentDiags.Where(d => d.JitterMs.HasValue).Select(d => d.JitterMs!.Value).ToList();
            var losses = recentDiags.Where(d => d.PacketLossPct.HasValue).Select(d => d.PacketLossPct!.Value).ToList();
            var hops = recentDiags.Where(d => d.HopCount.HasValue).Select(d => d.HopCount!.Value).ToList();
            var downloads = recentDiags.Where(d => d.DownloadMbps.HasValue).Select(d => d.DownloadMbps!.Value).ToList();
            var uploads = recentDiags.Where(d => d.UploadMbps.HasValue).Select(d => d.UploadMbps!.Value).ToList();
            var latencies = recentDiags.Where(d => d.InternetLatencyMs.HasValue).Select(d => d.InternetLatencyMs!.Value).ToList();
            var dnsValues = recentDiags.Where(d => d.DnsResolutionMs.HasValue).Select(d => d.DnsResolutionMs!.Value).ToList();

            var avgJitter = jitters.Count > 0 ? jitters.Average() : (decimal?)null;
            var avgLoss = losses.Count > 0 ? losses.Average() : (decimal?)null;
            var avgHops = hops.Count > 0 ? (int)hops.Average() : (int?)null;
            var avgDown = downloads.Count > 0 ? downloads.Average() : (decimal?)null;
            var avgUp = uploads.Count > 0 ? uploads.Average() : (decimal?)null;
            var avgLatency = latencies.Count > 0 ? latencies.Average() : (decimal?)null;
            var avgDns = dnsValues.Count > 0 ? dnsValues.Average() : (decimal?)null;

            site.AvgJitterMs = avgJitter;
            site.AvgPacketLossPct = avgLoss;
            site.HopCount = avgHops;
            site.AvgDownMbps = avgDown;
            site.AvgUpMbps = avgUp;
            site.AvgLatencyMs = avgLatency;

            // Compute WAN score (0-100)
            site.WanScore = ComputeScore(avgLatency, avgJitter, avgLoss, avgDown, avgDns);
            site.UpdatedAt = DateTime.UtcNow;

            // Generate findings
            GenerateFindings(organizationId, site.Id, avgLatency, avgJitter, avgLoss, avgDown, avgUp, avgDns, avgHops, site.ContractedDownMbps, site.ContractedUpMbps);
        }

        await _db.SaveChangesAsync();
    }

    private static decimal ComputeScore(decimal? latency, decimal? jitter, decimal? loss, decimal? download, decimal? dns)
    {
        // Weighted scoring: latency 30%, jitter 20%, packet loss 25%, throughput 15%, DNS 10%
        decimal score = 100;

        if (latency.HasValue)
        {
            // <30ms = 100, 30-80ms = 80-100, 80-150ms = 50-80, >150ms = 0-50
            score -= Math.Min(30, latency.Value * 0.2m);
        }

        if (jitter.HasValue)
        {
            // <5ms = 100, 5-20ms = 50-100, >20ms = 0-50
            score -= Math.Min(20, jitter.Value * 1.0m);
        }

        if (loss.HasValue)
        {
            // 0% = 100, 1% = 75, 3% = 25, >5% = 0
            score -= Math.Min(25, loss.Value * 5m);
        }

        if (download.HasValue)
        {
            // >100Mbps = 100, 50-100 = 90, 10-50 = 70, <10 = 50
            var throughputPenalty = download.Value switch
            {
                > 100 => 0m,
                > 50 => 3m,
                > 10 => 8m,
                > 1 => 12m,
                _ => 15m
            };
            score -= throughputPenalty;
        }

        if (dns.HasValue)
        {
            // <20ms = 100, 20-100ms = 50-100, >100ms = 0-50
            score -= Math.Min(10, dns.Value * 0.1m);
        }

        return Math.Max(0, Math.Min(100, Math.Round(score, 1)));
    }

    private void GenerateFindings(Guid orgId, Guid siteId, decimal? latency, decimal? jitter, decimal? loss,
        decimal? download, decimal? upload, decimal? dns, int? hops,
        decimal? contractedDown, decimal? contractedUp)
    {
        void Add(string severity, string category, string title, string? detail, decimal? value, decimal? threshold) =>
            _db.WanFindings.Add(new WanFinding
            {
                OrganizationId = orgId,
                SiteId = siteId,
                Severity = severity,
                Category = category,
                Title = title,
                Detail = detail,
                MetricValue = value,
                MetricThreshold = threshold,
            });

        if (loss is > 3)
            Add("critical", "packet-loss", "High packet loss", $"Average packet loss {loss:F1}% exceeds 3% threshold", loss, 3);
        else if (loss is > 1)
            Add("warning", "packet-loss", "Elevated packet loss", $"Average packet loss {loss:F1}% exceeds 1% threshold", loss, 1);

        if (jitter is > 30)
            Add("critical", "jitter", "Severe jitter", $"Average jitter {jitter:F1}ms exceeds 30ms (VoIP unusable)", jitter, 30);
        else if (jitter is > 10)
            Add("warning", "jitter", "Elevated jitter", $"Average jitter {jitter:F1}ms exceeds 10ms threshold", jitter, 10);

        if (latency is > 150)
            Add("critical", "latency", "Very high internet latency", $"Average latency {latency:F0}ms exceeds 150ms", latency, 150);
        else if (latency is > 80)
            Add("warning", "latency", "Elevated internet latency", $"Average latency {latency:F0}ms exceeds 80ms", latency, 80);

        if (dns is > 100)
            Add("warning", "dns", "Slow DNS resolution", $"Average DNS resolution {dns:F0}ms exceeds 100ms", dns, 100);

        if (download is < 10)
            Add("warning", "throughput", "Low download speed", $"Average download {download:F1} Mbps below 10 Mbps", download, 10);

        if (upload is < 5)
            Add("warning", "throughput", "Low upload speed", $"Average upload {upload:F1} Mbps below 5 Mbps", upload, 5);

        if (hops is > 20)
            Add("info", "routing", "High hop count", $"Traceroute shows {hops} hops (above 20)", hops, 20);

        // SLA violation: actual throughput below contracted
        if (contractedDown.HasValue && download.HasValue && download < contractedDown * 0.7m)
            Add("critical", "sla", "Download below 70% of contracted speed",
                $"Measured {download:F1} Mbps vs contracted {contractedDown:F0} Mbps", download, contractedDown * 0.7m);

        if (contractedUp.HasValue && upload.HasValue && upload < contractedUp * 0.7m)
            Add("critical", "sla", "Upload below 70% of contracted speed",
                $"Measured {upload:F1} Mbps vs contracted {contractedUp:F0} Mbps", upload, contractedUp * 0.7m);
    }
}
