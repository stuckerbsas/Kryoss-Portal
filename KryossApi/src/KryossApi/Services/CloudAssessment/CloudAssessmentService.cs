using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CloudAssessment;

public class CloudAssessmentService : ICloudAssessmentService
{
    private readonly KryossDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CloudAssessmentService> _log;

    public CloudAssessmentService(
        KryossDbContext db,
        IServiceScopeFactory scopeFactory,
        ILogger<CloudAssessmentService> log)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    // ── StartScanAsync ──────────────────────────────────────────────

    public async Task<Guid> StartScanAsync(Guid organizationId, Guid? tenantId)
    {
        var scan = new CloudAssessmentScan
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            TenantId = tenantId,
            Status = "running",
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _db.CloudAssessmentScans.Add(scan);
        await _db.SaveChangesAsync();

        var scanId = scan.Id;

        // Fire-and-forget background stub (CA-0: no real pipelines yet).
        // Scaffold only — CA-1+ may switch to await-in-request like
        // CopilotReadinessService does, once pipelines make the run long-lived.
        _ = Task.Run(() => RunStubAsync(scanId));

        return scanId;
    }

    // ── Background stub runner ──────────────────────────────────────

    private async Task RunStubAsync(Guid scanId)
    {
        // Must create a NEW DbContext scope since this runs on a background thread
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KryossDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<CloudAssessmentService>>();

        try
        {
            var scan = await db.CloudAssessmentScans.FindAsync(scanId);
            if (scan is null) return;

            scan.Status = "completed";
            scan.OverallScore = null;
            scan.AreaScores = "{}";
            scan.Verdict = "scaffold";
            scan.PipelineStatus = "{}";
            scan.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            db.Actlog.Add(new Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "cloud-assessment",
                Action = "scan.completed",
                EntityType = "CloudAssessmentScan",
                EntityId = scanId.ToString(),
                Message = "CA-0 scaffold stub complete — no pipelines yet"
            });
            await db.SaveChangesAsync();

            log.LogInformation("Cloud Assessment scan {ScanId} stub completed (CA-0 scaffold)", scanId);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Cloud Assessment scan {ScanId} stub failed", scanId);

            try
            {
                // Clear change tracker — context may be poisoned with failed inserts
                db.ChangeTracker.Clear();

                // Use fresh fetch to get untracked scan row
                var scan = await db.CloudAssessmentScans.FindAsync(scanId);
                if (scan is not null)
                {
                    scan.Status = "failed";
                    scan.CompletedAt = DateTime.UtcNow;
                    scan.PipelineStatus = JsonSerializer.Serialize(new { error = ex.Message });
                    await db.SaveChangesAsync();
                }

                db.Actlog.Add(new Actlog
                {
                    Timestamp = DateTime.UtcNow,
                    Severity = "error",
                    Module = "cloud-assessment",
                    Action = "scan.failed",
                    EntityType = "CloudAssessmentScan",
                    EntityId = scanId.ToString(),
                    Message = $"Scan failed: {ex.Message}"
                        + (ex.InnerException is not null ? $" (inner: {ex.InnerException.Message})" : "")
                });
                await db.SaveChangesAsync();
            }
            catch (Exception innerEx)
            {
                log.LogError(innerEx, "Failed to mark Cloud Assessment scan {ScanId} as failed", scanId);
            }
        }
    }

    // ── GetLatestScanAsync ──────────────────────────────────────────

    public async Task<object?> GetLatestScanAsync(Guid organizationId)
    {
        var scan = await _db.CloudAssessmentScans
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (scan is null) return null;

        // Separate query for findings summary grouped by area
        var findingsSummary = await _db.CloudAssessmentFindings
            .Where(f => f.ScanId == scan.Id)
            .GroupBy(f => f.Area)
            .Select(g => new
            {
                Area = g.Key,
                Total = g.Count(),
                ActionRequired = g.Count(f => f.Status == "Action Required"),
                Warning = g.Count(f => f.Status == "Warning"),
                Success = g.Count(f => f.Status == "Success"),
                Disabled = g.Count(f => f.Status == "Disabled")
            })
            .ToListAsync();

        return new
        {
            scan.Id,
            scan.Status,
            scan.OverallScore,
            AreaScores = ParseJsonDict(scan.AreaScores),
            scan.Verdict,
            PipelineStatus = ParsePipelineStatus(scan.PipelineStatus),
            scan.StartedAt,
            scan.CompletedAt,
            scan.CreatedAt,
            scan.TenantId,
            FindingsSummary = findingsSummary
        };
    }

    // ── GetScanDetailAsync ──────────────────────────────────────────

    public async Task<object?> GetScanDetailAsync(Guid scanId)
    {
        var scan = await _db.CloudAssessmentScans
            .FirstOrDefaultAsync(s => s.Id == scanId);

        if (scan is null) return null;

        var findings = await _db.CloudAssessmentFindings
            .Where(f => f.ScanId == scanId)
            .OrderBy(f => f.Area).ThenBy(f => f.Service).ThenBy(f => f.Feature)
            .Select(f => new
            {
                f.Area, f.Service, f.Feature, f.Status, f.Priority,
                f.Observation, f.Recommendation, f.LinkText, f.LinkUrl
            })
            .ToListAsync();

        var metrics = await _db.CloudAssessmentMetrics
            .Where(m => m.ScanId == scanId)
            .Select(m => new { m.Area, m.MetricKey, m.MetricValue })
            .ToListAsync();

        var licenses = await _db.CloudAssessmentLicenses
            .Where(l => l.ScanId == scanId)
            .Select(l => new
            {
                l.SkuPartNumber, l.FriendlyName, l.Purchased, l.Assigned, l.Available
            })
            .ToListAsync();

        var adoption = await _db.CloudAssessmentAdoptions
            .Where(a => a.ScanId == scanId)
            .Select(a => new
            {
                a.Area, a.ServiceName, a.LicensedCount, a.Active30d, a.AdoptionRate
            })
            .ToListAsync();

        var wastedLicenses = await _db.CloudAssessmentWastedLicenses
            .Where(w => w.ScanId == scanId)
            .Select(w => new
            {
                w.UserPrincipal, w.DisplayName, w.Sku,
                w.LastSignIn, w.DaysInactive, w.EstimatedCostYear
            })
            .ToListAsync();

        return new
        {
            scan.Id, scan.OrganizationId, scan.TenantId,
            scan.Status, scan.OverallScore,
            AreaScores = ParseJsonDict(scan.AreaScores),
            scan.Verdict,
            PipelineStatus = ParsePipelineStatus(scan.PipelineStatus),
            scan.StartedAt, scan.CompletedAt, scan.CreatedAt,
            Findings = findings,
            Metrics = metrics,
            Licenses = licenses,
            Adoption = adoption,
            WastedLicenses = wastedLicenses
        };
    }

    // ── GetScanHistoryAsync ─────────────────────────────────────────

    public async Task<List<object>> GetScanHistoryAsync(Guid organizationId)
    {
        var history = await _db.CloudAssessmentScans
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(10)
            .Select(s => (object)new
            {
                s.Id,
                s.OverallScore,
                s.Verdict,
                s.Status,
                s.CreatedAt
            })
            .ToListAsync();

        return history;
    }

    // ── Private helpers ─────────────────────────────────────────────

    private static Dictionary<string, object>? ParseJsonDict(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parse pipeline_status JSON string to object so it ships to portal as JSON object,
    /// not a string (otherwise portal Object.entries iterates characters).
    /// </summary>
    private static Dictionary<string, string>? ParsePipelineStatus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch
        {
            return null;
        }
    }
}
