using System.Net;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Portal;

public class CveSyncFunction
{
    private readonly ICveSyncService _syncService;
    private readonly KryossDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CveSyncFunction> _logger;

    public CveSyncFunction(ICveSyncService syncService, KryossDbContext db,
        IServiceScopeFactory scopeFactory, ILogger<CveSyncFunction> logger)
    {
        _syncService = syncService;
        _db = db;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [Function("CveSync_Daily")]
    public async Task RunDaily([TimerTrigger("0 0 3 * * *")] TimerInfo timer)
    {
        await _syncService.SyncAsync(fullRebuild: false);
    }

    [Function("CveSync_Runner")]
    public async Task RunPending([TimerTrigger("0 */2 * * * *")] TimerInfo timer)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KryossDbContext>();

        var job = await db.CveSyncLogs
            .Where(l => l.Status == "pending" || l.Status == "running")
            .OrderBy(l => l.SyncedAt)
            .FirstOrDefaultAsync();

        if (job is null) return;

        if (job.Status == "pending")
        {
            job.Status = "running";
            await db.SaveChangesAsync();
        }

        var syncService = scope.ServiceProvider.GetRequiredService<ICveSyncService>();
        await syncService.ProcessNextVendorAsync(job);
    }

    [Function("CveSync_Manual")]
    [RequirePermission("admin:write")]
    public async Task<HttpResponseData> RunManual(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/cve-sync")] HttpRequestData req)
    {
        var full = req.Query["full"] == "true";

        var alreadyRunning = await _db.CveSyncLogs
            .AnyAsync(l => l.Status == "pending" || l.Status == "running");

        if (alreadyRunning)
        {
            var resp409 = req.CreateResponse(HttpStatusCode.Conflict);
            await resp409.WriteAsJsonAsync(new { error = "Sync already in progress" });
            return resp409;
        }

        _db.CveSyncLogs.Add(new CveSyncLog
        {
            SyncedAt = DateTime.UtcNow,
            Source = full ? "manual_full" : "manual_incremental",
            Status = "pending",
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation("CVE sync queued: full={Full}", full);

        var resp = req.CreateResponse(HttpStatusCode.Accepted);
        await resp.WriteAsJsonAsync(new { status = "queued", full });
        return resp;
    }

    [Function("CveSync_Status")]
    [RequirePermission("admin:read")]
    public async Task<HttpResponseData> GetStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cve-sync/status")] HttpRequestData req)
    {
        var lastSync = await _db.CveSyncLogs
            .Where(l => l.Status == "success" || l.Status == "error")
            .OrderByDescending(l => l.SyncedAt)
            .FirstOrDefaultAsync();

        var isRunning = await _db.CveSyncLogs
            .AnyAsync(l => l.Status == "pending" || l.Status == "running");

        var totalCves = await _db.CveEntries.CountAsync();
        var kevCount = await _db.CveEntries.CountAsync(e => e.IsKnownExploited);
        var mapCount = await _db.CveProductMaps.CountAsync();
        var totalFindings = await _db.MachineCveFindings.CountAsync(f => f.Status == "open");
        var softwareWithCpe = await _db.Software.CountAsync(s => s.CpeVendor != null);
        var totalSoftware = await _db.Software.CountAsync();

        var recentSyncs = await _db.CveSyncLogs
            .OrderByDescending(l => l.SyncedAt)
            .Take(10)
            .Select(l => new
            {
                l.SyncedAt,
                l.Status,
                l.EntriesAdded,
                l.EntriesUpdated,
                l.Source,
                l.ErrorMessage,
            })
            .ToListAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            lastSyncAt = lastSync?.SyncedAt,
            lastSyncStatus = lastSync?.Status,
            lastSyncError = lastSync?.ErrorMessage,
            isRunning,
            totalCves,
            knownExploited = kevCount,
            productMappings = mapCount,
            totalFindings,
            softwareWithCpe,
            totalSoftware,
            recentSyncs,
        });
        return resp;
    }

    [Function("CveSync_Products")]
    [RequirePermission("admin:read")]
    public async Task<HttpResponseData> GetProducts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cve-sync/products")] HttpRequestData req)
    {
        var mapped = await _db.Software
            .Where(s => s.CpeVendor != null)
            .GroupBy(s => new { s.CpeVendor, s.CpeProduct })
            .Select(g => new
            {
                vendor = g.Key.CpeVendor,
                product = g.Key.CpeProduct,
                softwareCount = g.Count(),
            })
            .OrderBy(x => x.vendor)
            .ThenBy(x => x.product)
            .ToListAsync();

        var cveCountByVendor = await _db.CveEntries
            .Where(e => e.Vendor != null)
            .GroupBy(e => e.Vendor)
            .Select(g => new { vendor = g.Key, cveCount = g.Count() })
            .ToDictionaryAsync(x => x.vendor!, x => x.cveCount);

        var findingCountByVendor = await (
            from f in _db.MachineCveFindings
            join c in _db.CveEntries on f.CveId equals c.CveId
            where f.Status == "open" && c.Vendor != null
            group f by c.Vendor into g
            select new { vendor = g.Key, findingCount = g.Count() }
        ).ToDictionaryAsync(x => x.vendor!, x => x.findingCount);

        var products = mapped.Select(m => new
        {
            m.vendor,
            m.product,
            m.softwareCount,
            cveCount = m.vendor != null && cveCountByVendor.TryGetValue(m.vendor, out var cc) ? cc : 0,
            openFindings = m.vendor != null && findingCountByVendor.TryGetValue(m.vendor, out var fc) ? fc : 0,
        }).ToList();

        var unmappedCount = await _db.Software.CountAsync(s => s.CpeVendor == null);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { products, unmappedCount });
        return resp;
    }
}
