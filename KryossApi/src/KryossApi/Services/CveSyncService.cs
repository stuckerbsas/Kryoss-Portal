using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services;

public interface ICveSyncService
{
    Task<CveSyncResult> SyncAsync(bool fullRebuild = false);
    Task ProcessNextVendorAsync(CveSyncLog job);
}

public class CveSyncResult
{
    public int CvesAdded { get; set; }
    public int CvesUpdated { get; set; }
    public int KevFlagged { get; set; }
    public int ProductMapsCreated { get; set; }
    public int MachinesScanned { get; set; }
    public int CvssEnriched { get; set; }
    public int EpssEnriched { get; set; }
    public string? Error { get; set; }
}

public class CveSyncProgress
{
    [JsonPropertyName("vendors")]
    public List<string> Vendors { get; set; } = [];
    [JsonPropertyName("vendorIndex")]
    public int VendorIndex { get; set; }
    [JsonPropertyName("added")]
    public int Added { get; set; }
    [JsonPropertyName("updated")]
    public int Updated { get; set; }
    [JsonPropertyName("fullRebuild")]
    public bool FullRebuild { get; set; }
    [JsonPropertyName("lastSync")]
    public DateTime? LastSync { get; set; }
    [JsonPropertyName("modifiedCveIds")]
    public List<int> ModifiedCveIds { get; set; } = [];
}

public class CveSyncService : ICveSyncService
{
    private readonly IDbContextFactory<KryossDbContext> _dbFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ICpeMappingService _cpeMappingService;
    private readonly ICveService _cveService;
    private readonly ILogger<CveSyncService> _logger;
    private readonly IActlogService _actlog;

    private const string CveDeltaUrl = "https://raw.githubusercontent.com/CVEProject/cvelistV5/main/cves/delta.json";
    private const string NvdBaseUrl = "https://services.nvd.nist.gov/rest/json/cves/2.0";
    private const string CisaKevUrl = "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json";

    // Fix 1: single source of truth — loaded from cve-vendors.json, with hardcoded fallback
    private static readonly Lazy<HashSet<string>> AllowedVendors = new(LoadAllowedVendors);

    private static readonly System.Text.RegularExpressions.Regex CorporateSuffix =
        new(@"(,?inc|corporation|corp|ltd|systems|technologies|incorporated|enterprise|software|foundation|group|gmbh|ag)$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string NormalizeVendor(string v)
    {
        var n = v.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "").Replace(".", "");
        n = CorporateSuffix.Replace(n, "");
        n = CorporateSuffix.Replace(n, "");
        return n;
    }

    private static HashSet<string> LoadAllowedVendors()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "cve-vendors.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var vendors = JsonSerializer.Deserialize<string[]>(json);
                if (vendors is { Length: > 0 })
                    return new HashSet<string>(vendors.Select(NormalizeVendor), StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { /* fall through to hardcoded */ }

        return new(StringComparer.OrdinalIgnoreCase)
        {
            "microsoft","apple","google","mozilla","oracle","cisco","vmware","adobe",
            "dell","hp","hpe","lenovo","intel","fortinet","sonicwall","sophos",
            "paloaltonetworks","watchguard","barracuda","ubiquiti","veeam","acronis",
            "qnap","synology","teamviewer","connectwise","zoom","citrix","broadcom",
        };
    }

    public CveSyncService(
        IDbContextFactory<KryossDbContext> dbFactory,
        IHttpClientFactory httpFactory,
        ICpeMappingService cpeMappingService,
        ICveService cveService,
        ILogger<CveSyncService> logger,
        IActlogService actlog)
    {
        _dbFactory = dbFactory;
        _httpFactory = httpFactory;
        _cpeMappingService = cpeMappingService;
        _cveService = cveService;
        _logger = logger;
        _actlog = actlog;
    }

    // ── Daily sync: CVE.org delta + NVD CVSS enrichment + CISA KEV ──

    public async Task<CveSyncResult> SyncAsync(bool fullRebuild = false)
    {
        var result = new CveSyncResult();
        await using var db = await _dbFactory.CreateDbContextAsync();

        try
        {
            try { await _cpeMappingService.ApplyMappingsToNewSoftwareAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "CPE mapping failed (non-fatal)"); }

            var client = _httpFactory.CreateClient("nvd");
            var modifiedCveIds = new HashSet<int>();

            if (fullRebuild)
            {
                await _actlog.LogAsync("INFO", "cve_sync", "start", message: "full rebuild: product map + rescan only");
            }
            else
            {
                // 1. Fetch CVE.org delta
                var deltaIds = await SyncFromCveDeltaAsync(db, client, result);
                foreach (var id in deltaIds) modifiedCveIds.Add(id);

                // 2. Enrich missing CVSS scores via NVD (up to 500 per daily run)
                try { result.CvssEnriched = await EnrichMissingCvssAsync(db, client); }
                catch (Exception ex) { _logger.LogWarning(ex, "CVSS enrichment failed (non-fatal)"); }

                await _actlog.LogAsync("INFO", "cve_sync", "delta",
                    message: $"+{result.CvesAdded} ~{result.CvesUpdated} cvss={result.CvssEnriched}");
            }

            // 3. CISA KEV
            try { result.KevFlagged = await SyncCisaKevAsync(db, client); }
            catch (Exception ex) { _logger.LogWarning(ex, "KEV sync failed (non-fatal)"); }

            // 3b. EPSS scores (bulk CSV from FIRST.org)
            try { result.EpssEnriched = await SyncEpssAsync(db, client); }
            catch (Exception ex) { _logger.LogWarning(ex, "EPSS sync failed (non-fatal)"); }

            // 4. Product map
            try
            {
                if (fullRebuild)
                    result.ProductMapsCreated = await RebuildProductMapAsync(db);
                else if (modifiedCveIds.Count > 0)
                    result.ProductMapsCreated = await UpdateProductMapAsync(db, modifiedCveIds);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Product map failed (non-fatal)"); }

            // 5. Machine rescan
            try { result.MachinesScanned = await RescanAffectedMachinesAsync(db, modifiedCveIds); }
            catch (Exception ex) { _logger.LogWarning(ex, "Machine rescan failed (non-fatal)"); }

            await _actlog.LogAsync("INFO", "cve_sync", "done",
                message: $"+{result.CvesAdded} ~{result.CvesUpdated} cvss={result.CvssEnriched} epss={result.EpssEnriched} kev={result.KevFlagged} maps={result.ProductMapsCreated} machines={result.MachinesScanned}");

            db.CveSyncLogs.Add(new CveSyncLog
            {
                SyncedAt = DateTime.UtcNow,
                EntriesAdded = result.CvesAdded,
                EntriesUpdated = result.CvesUpdated,
                Source = "cve.org+nvd+kev",
                Status = "success",
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException?.Message ?? "";
            var errMsg = $"{ex.Message} | inner: {inner}";
            result.Error = errMsg;
            _logger.LogError(ex, "CVE sync failed");
            await _actlog.LogAsync("ERR", "cve_sync", "fail",
                message: $"+{result.CvesAdded} ~{result.CvesUpdated} err={errMsg[..Math.Min(errMsg.Length, 200)]}");
            db.CveSyncLogs.Add(new CveSyncLog
            {
                SyncedAt = DateTime.UtcNow,
                Source = "cve.org+nvd+kev",
                Status = "error",
                ErrorMessage = ex.Message[..Math.Min(ex.Message.Length, 500)],
            });
            await db.SaveChangesAsync();
        }

        return result;
    }

    // ── CVE.org delta: fetch new + updated CVEs ──

    private async Task<List<int>> SyncFromCveDeltaAsync(KryossDbContext db, HttpClient client, CveSyncResult result)
    {
        var modifiedIds = new List<int>();

        var deltaResponse = await client.GetAsync(CveDeltaUrl);
        if (!deltaResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("CVE.org delta fetch failed: HTTP {Status}", (int)deltaResponse.StatusCode);
            return modifiedIds;
        }

        var delta = await deltaResponse.Content.ReadFromJsonAsync<CveDelta>();
        if (delta is null) return modifiedIds;

        var allEntries = new List<CveDeltaEntry>();
        if (delta.New is not null) allEntries.AddRange(delta.New);
        if (delta.Updated is not null) allEntries.AddRange(delta.Updated);

        _logger.LogInformation("CVE.org delta: {Count} changes ({New} new, {Updated} updated)",
            allEntries.Count, delta.New?.Count ?? 0, delta.Updated?.Count ?? 0);

        // Fix 3: batch upserts — collect records, save every 50
        var pendingRecords = new List<(CveParsedRecord record, CveEntry? existing)>();
        int batchCount = 0;

        foreach (var entry in allEntries)
        {
            if (string.IsNullOrEmpty(entry.GithubLink)) continue;

            try
            {
                var cveResponse = await client.GetAsync(entry.GithubLink);
                if (!cveResponse.IsSuccessStatusCode) continue;

                var json = await cveResponse.Content.ReadAsStringAsync();
                var record = ParseCveRecord(json);
                if (record is null) continue;

                var id = await UpsertCveEntryAsync(db, record, result, saveBatch: false);
                if (id > 0) modifiedIds.Add(id);

                batchCount++;
                if (batchCount >= 50)
                {
                    await db.SaveChangesAsync();
                    batchCount = 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to process {CveId}: {Err}", entry.CveId, ex.Message);
            }

            await Task.Delay(100);
        }

        if (batchCount > 0) await db.SaveChangesAsync();

        return modifiedIds;
    }

    private CveParsedRecord? ParseCveRecord(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!ShouldIngest(root)) return null;

        var meta = root.GetProperty("cveMetadata");
        var cveId = meta.GetProperty("cveId").GetString()!;
        var published = meta.TryGetProperty("datePublished", out var dp) ? dp.GetString() : null;

        var containers = root.GetProperty("containers");
        var cna = containers.GetProperty("cna");

        // Vendor resolution: affected[].vendor → providerMetadata.shortName
        var vendor = ResolveVendor(root);
        if (vendor is null) return null;

        // Description
        string? description = null;
        if (cna.TryGetProperty("descriptions", out var descs))
        {
            foreach (var d in descs.EnumerateArray())
            {
                if (d.TryGetProperty("lang", out var lang) && (lang.GetString()?.StartsWith("en") ?? false)
                    && d.TryGetProperty("value", out var val))
                {
                    description = val.GetString();
                    break;
                }
            }
            if (description is null && descs.GetArrayLength() > 0)
            {
                descs[0].TryGetProperty("value", out var val);
                description = val.GetString();
            }
        }

        // Product + versions from the matched vendor's affected entry
        string? product = null, affectedBelow = null;
        if (cna.TryGetProperty("affected", out var affected) && affected.GetArrayLength() > 0)
        {
            JsonElement? matchedEntry = null;
            foreach (var entry in affected.EnumerateArray())
            {
                var entryVendor = entry.TryGetProperty("vendor", out var ev) ? ev.GetString() : null;
                if (entryVendor is not null && string.Equals(NormalizeVendor(entryVendor), NormalizeVendor(vendor), StringComparison.OrdinalIgnoreCase))
                {
                    matchedEntry = entry;
                    break;
                }
            }
            // Fallback: if vendor came from providerMetadata, use first affected entry
            matchedEntry ??= affected[0];

            var me = matchedEntry.Value;
            product = me.TryGetProperty("product", out var p) ? p.GetString() : null;
            if (me.TryGetProperty("versions", out var versions))
            {
                foreach (var ver in versions.EnumerateArray())
                {
                    if (ver.TryGetProperty("lessThan", out var lt))
                    { affectedBelow = lt.GetString(); break; }
                    if (ver.TryGetProperty("lessThanOrEqual", out var lte))
                    { affectedBelow = lte.GetString(); break; }
                }
            }
        }

        var productClass = ClassifyProduct(product, vendor);

        // References
        string? refUrl = null;
        if (cna.TryGetProperty("references", out var refs) && refs.GetArrayLength() > 0)
        {
            refUrl = refs[0].TryGetProperty("url", out var u) ? u.GetString() : null;
        }

        // CWE
        string? cweId = null;
        if (cna.TryGetProperty("problemTypes", out var pts))
            cweId = ExtractCweId(pts);

        // CVSS — check ADP first, then CNA
        decimal? cvssScore = null;
        string? baseSeverity = null;

        if (containers.TryGetProperty("adp", out var adp))
        {
            foreach (var a in adp.EnumerateArray())
            {
                if (a.TryGetProperty("metrics", out var metrics))
                {
                    (cvssScore, baseSeverity) = ExtractCvss(metrics);
                    if (cvssScore.HasValue) break;
                }
                if (cweId is null && a.TryGetProperty("problemTypes", out var adpPts))
                    cweId = ExtractCweId(adpPts);
            }
        }

        if (!cvssScore.HasValue && cna.TryGetProperty("metrics", out var cnaMetrics))
        {
            (cvssScore, baseSeverity) = ExtractCvss(cnaMetrics);
        }

        var severity = MapSeverityFromScore(cvssScore) ?? MapSeverityFromString(baseSeverity) ?? "medium";

        return new CveParsedRecord
        {
            CveId = cveId,
            Vendor = vendor,
            Product = product,
            ProductClass = productClass,
            ProductPattern = vendor != null ? $"%{vendor}%" : null,
            Severity = severity,
            CvssScore = cvssScore,
            Description = description,
            CweId = cweId,
            PublishedAt = published != null ? DateTime.TryParse(published, out var dt) ? dt : null : null,
            AffectedBelow = affectedBelow,
            FixedVersion = affectedBelow,
            ReferencesUrl = refUrl?.Length > 500 ? refUrl[..500] : refUrl,
        };
    }

    private static string? ExtractCweId(JsonElement problemTypes)
    {
        foreach (var pt in problemTypes.EnumerateArray())
        {
            if (!pt.TryGetProperty("descriptions", out var descs)) continue;
            foreach (var d in descs.EnumerateArray())
            {
                if (d.TryGetProperty("cweId", out var cwe))
                    return cwe.GetString();
            }
        }
        return null;
    }

    private static (decimal? score, string? severity) ExtractCvss(JsonElement metrics)
    {
        if (metrics.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in metrics.EnumerateArray())
            {
                if (m.TryGetProperty("cvssV3_1", out var v31))
                {
                    var score = v31.TryGetProperty("baseScore", out var bs) ? bs.GetDecimal() : (decimal?)null;
                    var sev = v31.TryGetProperty("baseSeverity", out var sv) ? sv.GetString() : null;
                    if (score.HasValue) return (score, sev);
                }
                if (m.TryGetProperty("cvssV3_0", out var v30))
                {
                    var score = v30.TryGetProperty("baseScore", out var bs) ? bs.GetDecimal() : (decimal?)null;
                    var sev = v30.TryGetProperty("baseSeverity", out var sv) ? sv.GetString() : null;
                    if (score.HasValue) return (score, sev);
                }
            }
        }
        else if (metrics.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in metrics.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.TryGetProperty("cvssData", out var cvssData))
                        {
                            var score = cvssData.TryGetProperty("baseScore", out var bs) ? bs.GetDecimal() : (decimal?)null;
                            var sev = cvssData.TryGetProperty("baseSeverity", out var sv) ? sv.GetString() : null;
                            if (score.HasValue) return (score, sev);
                        }
                    }
                }
            }
        }
        return (null, null);
    }

    // Fix 3: saveBatch=false defers SaveChanges to caller for batching
    private async Task<int> UpsertCveEntryAsync(KryossDbContext db, CveParsedRecord record, CveSyncResult result, bool saveBatch = true)
    {
        var existing = await db.CveEntries.FirstOrDefaultAsync(e => e.CveId == record.CveId);

        if (existing != null)
        {
            existing.Description = record.Description ?? existing.Description;
            existing.Vendor = record.Vendor ?? existing.Vendor;
            existing.Product = record.Product ?? existing.Product;
            existing.ProductClass = record.ProductClass ?? existing.ProductClass;
            existing.ProductPattern = record.ProductPattern ?? existing.ProductPattern;
            if (record.CvssScore.HasValue)
            {
                existing.CvssScore = record.CvssScore;
                existing.Severity = record.Severity;
            }
            existing.CweId = record.CweId ?? existing.CweId;
            existing.AffectedBelow = record.AffectedBelow ?? existing.AffectedBelow;
            existing.FixedVersion = record.FixedVersion ?? existing.FixedVersion;
            existing.ReferencesUrl = record.ReferencesUrl ?? existing.ReferencesUrl;
            existing.UpdatedAt = DateTime.UtcNow;
            if (saveBatch) await db.SaveChangesAsync();
            result.CvesUpdated++;
            return existing.Id;
        }
        else
        {
            var entry = new CveEntry
            {
                CveId = record.CveId,
                Vendor = record.Vendor,
                Product = record.Product,
                ProductClass = record.ProductClass,
                ProductPattern = record.ProductPattern ?? "%unknown%",
                Severity = record.Severity,
                CvssScore = record.CvssScore,
                Description = record.Description,
                CweId = record.CweId,
                PublishedAt = record.PublishedAt,
                AffectedBelow = record.AffectedBelow,
                FixedVersion = record.FixedVersion,
                ReferencesUrl = record.ReferencesUrl,
                Source = "cve.org",
            };
            db.CveEntries.Add(entry);
            if (saveBatch) await db.SaveChangesAsync();
            result.CvesAdded++;
            return entry.Id;
        }
    }

    // ── NVD CVSS enrichment — Fix 4: up to 500 per run with proper rate limiting ──

    private async Task<int> EnrichMissingCvssAsync(KryossDbContext db, HttpClient client)
    {
        var apiKey = Environment.GetEnvironmentVariable("NvdApiKey");
        int delayMs = apiKey != null ? 600 : 6000;
        int batchSize = apiKey != null ? 500 : 50;

        var missing = await db.CveEntries
            .Where(e => e.CvssScore == null)
            .OrderByDescending(e => e.CreatedAt)
            .Take(batchSize)
            .Select(e => new { e.Id, e.CveId })
            .ToListAsync();

        if (missing.Count == 0) return 0;

        int enriched = 0;
        int saveCounter = 0;

        foreach (var item in missing)
        {
            try
            {
                var url = $"{NvdBaseUrl}?cveId={item.CveId}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (apiKey != null) request.Headers.Add("apiKey", apiKey);

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode) continue;

                var nvd = await response.Content.ReadFromJsonAsync<NvdResponse>();
                var vuln = nvd?.Vulnerabilities?.FirstOrDefault()?.Cve;
                if (vuln is null) continue;

                var cvss31 = vuln.Metrics?.CvssMetricV31?.FirstOrDefault()?.CvssData;
                var cvss2 = vuln.Metrics?.CvssMetricV2?.FirstOrDefault()?.CvssData;
                var score = cvss31?.BaseScore ?? cvss2?.BaseScore;
                var severity = cvss31?.BaseSeverity ?? cvss2?.BaseSeverity;

                if (score.HasValue)
                {
                    var entry = await db.CveEntries.FindAsync(item.Id);
                    if (entry is not null)
                    {
                        entry.CvssScore = score;
                        entry.Severity = MapSeverityFromScore(score) ?? MapSeverityFromString(severity) ?? entry.Severity;
                        entry.UpdatedAt = DateTime.UtcNow;
                        enriched++;
                        saveCounter++;

                        if (saveCounter >= 50)
                        {
                            await db.SaveChangesAsync();
                            saveCounter = 0;
                        }
                    }
                }

                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("NVD CVSS enrichment failed for {CveId}: {Err}", item.CveId, ex.Message);
            }
        }

        if (saveCounter > 0) await db.SaveChangesAsync();
        return enriched;
    }

    // ── Manual trigger (queue-based) ──

    public async Task ProcessNextVendorAsync(CveSyncLog job)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tracked = await db.CveSyncLogs.FindAsync(job.Id);
        if (tracked is null) return;

        var progress = tracked.Progress is not null
            ? JsonSerializer.Deserialize<CveSyncProgress>(tracked.Progress)!
            : null;

        if (progress is null)
        {
            await _actlog.LogAsync("INFO", "cve_sync", "start", message: "manual: KEV + product map rebuild + rescan");

            var client = _httpFactory.CreateClient("nvd");

            int kevFlagged = 0;
            try { kevFlagged = await SyncCisaKevAsync(db, client); }
            catch (Exception ex) { _logger.LogWarning(ex, "KEV sync failed"); }

            int maps = 0;
            try { maps = await RebuildProductMapAsync(db); }
            catch (Exception ex) { _logger.LogWarning(ex, "Product map rebuild failed"); }

            int machines = 0;
            try { machines = await RescanAffectedMachinesAsync(db, new HashSet<int>()); }
            catch (Exception ex) { _logger.LogWarning(ex, "Machine rescan failed"); }

            tracked.Status = "success";
            tracked.EntriesAdded = 0;
            tracked.EntriesUpdated = 0;
            await db.SaveChangesAsync();

            await _actlog.LogAsync("INFO", "cve_sync", "done",
                message: $"manual rebuild: kev={kevFlagged} maps={maps} machines={machines}");
            return;
        }

        tracked.Status = "success";
        await db.SaveChangesAsync();
    }

    // ── CISA KEV ──

    private async Task<int> SyncCisaKevAsync(KryossDbContext db, HttpClient client)
    {
        var response = await client.GetAsync(CisaKevUrl);
        if (!response.IsSuccessStatusCode) return 0;

        var kev = await response.Content.ReadFromJsonAsync<CisaKevResponse>();
        if (kev?.Vulnerabilities is null) return 0;

        var kevIds = kev.Vulnerabilities.ToDictionary(v => v.CveID, v => v);
        var dbEntries = await db.CveEntries
            .Where(e => kevIds.Keys.Contains(e.CveId))
            .ToListAsync();

        int flagged = 0;
        foreach (var entry in dbEntries)
        {
            if (!entry.IsKnownExploited && kevIds.TryGetValue(entry.CveId, out var kevEntry))
            {
                entry.IsKnownExploited = true;
                entry.KevAddedDate = kevEntry.DateAdded;
                entry.KevDueDate = kevEntry.DueDate;
                flagged++;
            }
        }

        await db.SaveChangesAsync();
        return flagged;
    }

    private async Task<int> SyncEpssAsync(KryossDbContext db, HttpClient client)
    {
        var response = await client.GetAsync("https://epss.cyentia.com/epss_scores-current.csv.gz");
        if (!response.IsSuccessStatusCode) return 0;

        await using var gzStream = await response.Content.ReadAsStreamAsync();
        await using var decompressed = new System.IO.Compression.GZipStream(gzStream, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(decompressed);

        var epssMap = new Dictionary<string, (decimal score, decimal percentile)>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("#") || line.StartsWith("cve,")) continue;
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            if (decimal.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var score)
                && decimal.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                epssMap[parts[0]] = (score, pct);
            }
        }

        if (epssMap.Count == 0) return 0;

        int enriched = 0;
        int skip = 0;
        const int chunk = 2000;

        while (true)
        {
            var batch = await db.CveEntries
                .Where(e => e.EpssScore == null)
                .OrderBy(e => e.Id)
                .Skip(skip)
                .Take(chunk)
                .ToListAsync();

            if (batch.Count == 0) break;

            foreach (var entry in batch)
            {
                if (epssMap.TryGetValue(entry.CveId, out var epss))
                {
                    entry.EpssScore = epss.score;
                    entry.EpssPercentile = epss.percentile;
                    enriched++;
                }
            }

            await db.SaveChangesAsync();
            skip += chunk;
        }

        return enriched;
    }

    // ── Product map — Fix 5: chunked processing ──

    private async Task<int> RebuildProductMapAsync(KryossDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM cve_product_map");

        var softwareCatalog = await db.Software
            .Where(s => s.CpeVendor != null)
            .Select(s => new { s.Id, s.CpeVendor, s.CpeProduct })
            .AsNoTracking()
            .ToListAsync();

        if (softwareCatalog.Count == 0) return 0;

        int count = 0;
        int skip = 0;
        const int chunkSize = 2000;

        while (true)
        {
            var cveChunk = await db.CveEntries
                .OrderBy(e => e.Id)
                .Skip(skip)
                .Take(chunkSize)
                .Select(e => new { e.Id, e.Vendor, e.Product, e.CpeMatchString, e.AffectedBelow, e.FixedVersion })
                .AsNoTracking()
                .ToListAsync();

            if (cveChunk.Count == 0) break;

            foreach (var cve in cveChunk)
            {
                foreach (var sw in softwareCatalog)
                {
                    if (MatchesCpe(cve.Vendor, cve.Product, cve.CpeMatchString, sw.CpeVendor, sw.CpeProduct))
                    {
                        db.CveProductMaps.Add(new CveProductMap
                        {
                            CveEntryId = cve.Id,
                            SoftwareId = sw.Id,
                            AffectedBelow = cve.AffectedBelow,
                            FixedVersion = cve.FixedVersion,
                        });
                        count++;
                    }
                }
            }

            await db.SaveChangesAsync();
            skip += chunkSize;
        }

        return count;
    }

    private async Task<int> UpdateProductMapAsync(KryossDbContext db, HashSet<int> modifiedCveIds)
    {
        var oldMaps = await db.CveProductMaps
            .Where(m => modifiedCveIds.Contains(m.CveEntryId))
            .ToListAsync();
        db.CveProductMaps.RemoveRange(oldMaps);

        var cves = await db.CveEntries
            .Where(e => modifiedCveIds.Contains(e.Id))
            .ToListAsync();

        var softwareCatalog = await db.Software
            .Where(s => s.CpeVendor != null)
            .Select(s => new { s.Id, s.CpeVendor, s.CpeProduct })
            .AsNoTracking()
            .ToListAsync();

        int count = 0;
        foreach (var cve in cves)
        {
            foreach (var sw in softwareCatalog)
            {
                if (MatchesCpe(cve.Vendor, cve.Product, cve.CpeMatchString, sw.CpeVendor, sw.CpeProduct))
                {
                    db.CveProductMaps.Add(new CveProductMap
                    {
                        CveEntryId = cve.Id,
                        SoftwareId = sw.Id,
                        AffectedBelow = cve.AffectedBelow,
                        FixedVersion = cve.FixedVersion,
                    });
                    count++;
                }
            }
        }

        await db.SaveChangesAsync();
        return count;
    }

    // ── Machine rescan ──

    private async Task<int> RescanAffectedMachinesAsync(KryossDbContext db, HashSet<int> modifiedCveIds)
    {
        if (modifiedCveIds.Count == 0) return 0;

        var affectedSoftwareIds = await db.CveProductMaps
            .Where(m => modifiedCveIds.Contains(m.CveEntryId))
            .Select(m => m.SoftwareId)
            .Distinct()
            .ToListAsync();

        var machineIds = await db.MachineSoftware
            .Where(ms => affectedSoftwareIds.Contains(ms.SoftwareId) && ms.RemovedAt == null)
            .Select(ms => ms.MachineId)
            .Distinct()
            .ToListAsync();

        foreach (var machineId in machineIds)
        {
            var machine = await db.Machines.FindAsync(machineId);
            if (machine is null) continue;
            await _cveService.ScanMachineAsync(machineId, machine.OrganizationId, null);
        }

        return machineIds.Count;
    }

    // ── Matching — vendor + normalized product comparison ──

    private static readonly System.Text.RegularExpressions.Regex MultiSpace =
        new(@"\s{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool MatchesCpe(string? cveVendor, string? cveProduct, string? cpeMatchString,
        string? swVendor, string? swProduct)
    {
        if (swVendor is null) return false;
        if (cveVendor is null && cpeMatchString is null) return false;

        bool vendorMatch = false;
        string? effectiveCveProduct = cveProduct;

        if (cveVendor != null && string.Equals(cveVendor, swVendor, StringComparison.OrdinalIgnoreCase))
        {
            vendorMatch = true;
        }
        else if (cpeMatchString != null)
        {
            var parts = cpeMatchString.Split(':');
            if (parts.Length >= 5)
            {
                if (string.Equals(parts[3], swVendor, StringComparison.OrdinalIgnoreCase))
                {
                    vendorMatch = true;
                    effectiveCveProduct ??= parts[4];
                }
            }
        }

        if (!vendorMatch) return false;

        if (effectiveCveProduct != null && swProduct != null)
            return ProductMatches(effectiveCveProduct, swProduct);

        if (swProduct is null) return true;
        return false;
    }

    private static bool ProductMatches(string cveProduct, string swProduct)
    {
        if (string.Equals(cveProduct, swProduct, StringComparison.OrdinalIgnoreCase))
            return true;

        var normCve = NormalizeProductName(cveProduct);
        var normSw = NormalizeProductName(swProduct);

        return normCve.Length > 0 && normSw.Length > 0 &&
               (normCve.Contains(normSw, StringComparison.Ordinal) ||
                normSw.Contains(normCve, StringComparison.Ordinal));
    }

    private static string NormalizeProductName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
            else
                sb.Append(' ');
        }
        var result = sb.ToString();
        result = result.Replace(" and ", " ").Replace(" for ", " ").Replace(" the ", " ");
        result = MultiSpace.Replace(result, " ");
        return result.Trim();
    }

    // ── Vendor Resolution (multi-field) ──

    private static string? ResolveVendor(JsonElement root)
    {
        if (!root.TryGetProperty("containers", out var containers)) return null;
        if (!containers.TryGetProperty("cna", out var cna)) return null;

        // Primary: affected[].vendor — check ALL entries, not just first
        if (cna.TryGetProperty("affected", out var affected))
        {
            foreach (var entry in affected.EnumerateArray())
            {
                if (entry.TryGetProperty("vendor", out var v))
                {
                    var vendor = v.GetString();
                    if (!string.IsNullOrEmpty(vendor) && AllowedVendors.Value.Contains(NormalizeVendor(vendor)))
                        return vendor;
                }
            }
        }

        // Fallback: providerMetadata.shortName
        if (cna.TryGetProperty("providerMetadata", out var pm) && pm.TryGetProperty("shortName", out var sn))
        {
            var shortName = sn.GetString();
            if (!string.IsNullOrEmpty(shortName) && AllowedVendors.Value.Contains(NormalizeVendor(shortName)))
                return shortName;
        }

        return null;
    }

    private static string ClassifyProduct(string? product, string? vendor)
    {
        if (string.IsNullOrEmpty(product)) return "APPLICATION";
        var p = product.ToLowerInvariant();

        if (p.Contains("windows") || p.Contains("macos") || p.Contains("ios") ||
            p.Contains("android") || p.Contains("chromeos") || p.Contains("linux") ||
            p.Contains("ubuntu") || p.Contains("debian") || p.Contains("red hat enterprise"))
            return "OS";

        if (p.Contains(".net") || p.Contains("java") || p.Contains("nodejs") ||
            p.Contains("node.js") || p.Contains("python") || p.Contains("php"))
            return "PLATFORM";

        if (p.Contains("openssl") || p.Contains("zlib") || p.Contains("curl") ||
            p.Contains("libxml") || p.Contains("sqlite") || p.Contains("libjpeg") ||
            p.Contains("libpng") || p.Contains("freetype") || p.Contains("expat"))
            return "LIBRARY";

        return "APPLICATION";
    }

    private static bool ShouldIngest(JsonElement root)
    {
        if (!root.TryGetProperty("cveMetadata", out var meta)) return false;
        var state = meta.TryGetProperty("state", out var s) ? s.GetString() : null;
        if (state != "PUBLISHED") return false;

        return ResolveVendor(root) is not null;
    }

    private static bool IsAllowedVendor(string? vendor)
    {
        if (string.IsNullOrEmpty(vendor)) return false;
        return AllowedVendors.Value.Contains(NormalizeVendor(vendor));
    }

    private static string? MapSeverityFromScore(decimal? score) => score switch
    {
        >= 9.0m => "critical",
        >= 7.0m => "high",
        >= 4.0m => "medium",
        > 0 => "low",
        _ => null,
    };

    private static string? MapSeverityFromString(string? s) => s?.ToLower() switch
    {
        "critical" => "critical",
        "high" => "high",
        "medium" => "medium",
        "low" => "low",
        _ => null,
    };

    // ── DTOs ──

    private class CveParsedRecord
    {
        public string CveId { get; set; } = null!;
        public string? Vendor { get; set; }
        public string? Product { get; set; }
        public string? ProductClass { get; set; }
        public string? ProductPattern { get; set; }
        public string Severity { get; set; } = "medium";
        public decimal? CvssScore { get; set; }
        public string? Description { get; set; }
        public string? CweId { get; set; }
        public DateTime? PublishedAt { get; set; }
        public string? AffectedBelow { get; set; }
        public string? FixedVersion { get; set; }
        public string? ReferencesUrl { get; set; }
    }

    private class CveDelta
    {
        [JsonPropertyName("new")]
        public List<CveDeltaEntry>? New { get; set; }
        [JsonPropertyName("updated")]
        public List<CveDeltaEntry>? Updated { get; set; }
    }

    private class CveDeltaEntry
    {
        [JsonPropertyName("cveId")]
        public string? CveId { get; set; }
        [JsonPropertyName("githubLink")]
        public string? GithubLink { get; set; }
    }

    // NVD DTOs (for CVSS enrichment)
    private class NvdResponse
    {
        [JsonPropertyName("vulnerabilities")]
        public List<NvdVulnerability>? Vulnerabilities { get; set; }
    }

    private class NvdVulnerability
    {
        [JsonPropertyName("cve")]
        public NvdCve? Cve { get; set; }
    }

    private class NvdCve
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("metrics")]
        public NvdMetrics? Metrics { get; set; }
    }

    private class NvdMetrics
    {
        [JsonPropertyName("cvssMetricV31")]
        public List<NvdCvssMetric>? CvssMetricV31 { get; set; }
        [JsonPropertyName("cvssMetricV2")]
        public List<NvdCvssMetric>? CvssMetricV2 { get; set; }
    }

    private class NvdCvssMetric
    {
        [JsonPropertyName("cvssData")]
        public NvdCvssData? CvssData { get; set; }
    }

    private class NvdCvssData
    {
        [JsonPropertyName("baseScore")]
        public decimal? BaseScore { get; set; }
        [JsonPropertyName("baseSeverity")]
        public string? BaseSeverity { get; set; }
    }

    private class CisaKevResponse
    {
        [JsonPropertyName("vulnerabilities")]
        public List<CisaKevEntry>? Vulnerabilities { get; set; }
    }

    private class CisaKevEntry
    {
        [JsonPropertyName("cveID")]
        public string CveID { get; set; } = null!;
        [JsonPropertyName("dateAdded")]
        public DateTime? DateAdded { get; set; }
        [JsonPropertyName("dueDate")]
        public DateTime? DueDate { get; set; }
    }
}
