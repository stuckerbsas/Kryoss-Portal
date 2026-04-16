using KryossApi.Data;
using KryossApi.Services.CopilotReadiness;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Timer;

/// <summary>
/// Weekly automated Copilot Readiness scan for all connected M365 tenants.
/// Runs Sunday 02:00 UTC. Skips tenants scanned within the last 5 days.
/// </summary>
public class CopilotReadinessTimerFunction
{
    private readonly KryossDbContext _db;
    private readonly ICopilotReadinessService _service;
    private readonly ILogger<CopilotReadinessTimerFunction> _log;

    public CopilotReadinessTimerFunction(
        KryossDbContext db,
        ICopilotReadinessService service,
        ILogger<CopilotReadinessTimerFunction> log)
    {
        _db = db;
        _service = service;
        _log = log;
    }

    [Function("CopilotReadiness_WeeklyScan")]
    public async Task Run(
        [TimerTrigger("0 0 2 * * 0")] TimerInfo timer)
    {
        _log.LogInformation("Copilot Readiness weekly scan started");

        var activeTenants = await _db.M365Tenants
            .Where(t => t.Status == "active")
            .Select(t => new
            {
                t.Id,
                t.OrganizationId,
                t.TenantId
            })
            .ToListAsync();

        var fiveDaysAgo = DateTime.UtcNow.AddDays(-5);
        int scanned = 0;
        int skipped = 0;

        foreach (var tenant in activeTenants)
        {
            // Skip if recent scan exists
            var recentScan = await _db.CopilotReadinessScans
                .AnyAsync(s => s.TenantId == tenant.Id && s.CreatedAt > fiveDaysAgo);

            if (recentScan)
            {
                skipped++;
                continue;
            }

            try
            {
                await _service.StartScanAsync(tenant.OrganizationId, tenant.Id, tenant.TenantId);
                scanned++;
                _log.LogInformation(
                    "Copilot Readiness scan triggered for org {OrgId}, tenant {TenantId}",
                    tenant.OrganizationId, tenant.TenantId);
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Failed to trigger Copilot Readiness scan for org {OrgId}",
                    tenant.OrganizationId);
            }

            // 30s delay between tenants to avoid throttling
            if (scanned < activeTenants.Count)
                await Task.Delay(TimeSpan.FromSeconds(30));
        }

        _log.LogInformation(
            "Copilot Readiness weekly scan completed. Scanned={Scanned}, Skipped={Skipped}, Total={Total}",
            scanned, skipped, activeTenants.Count);
    }
}
