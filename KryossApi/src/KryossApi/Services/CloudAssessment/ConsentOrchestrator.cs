using KryossApi.Data;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services.CloudAssessment;

public interface IConsentOrchestrator
{
    Task<ConnectionStatusResult> GetConnectionStatusAsync(Guid orgId);
}

public class ConnectionStatusResult
{
    public string Graph { get; init; } = "not_connected";
    public string Azure { get; init; } = "not_connected";
    public string PowerBi { get; init; } = "not_connected";
    public int AzureSubscriptionCount { get; init; }
    public int ConnectionPercentage { get; init; }
}

public class ConsentOrchestrator : IConsentOrchestrator
{
    private readonly KryossDbContext _db;

    public ConsentOrchestrator(KryossDbContext db) => _db = db;

    public async Task<ConnectionStatusResult> GetConnectionStatusAsync(Guid orgId)
    {
        var graphConnected = await _db.M365Tenants
            .AnyAsync(t => t.OrganizationId == orgId && t.ConsentGrantedAt != null);

        var azureSubs = await _db.CloudAssessmentAzureSubscriptions
            .Where(s => s.OrganizationId == orgId)
            .Select(s => s.ConsentState)
            .ToListAsync();

        var pbiConnected = await _db.CloudAssessmentPowerBiConnections
            .AnyAsync(p => p.OrganizationId == orgId && p.ConnectionState == "connected");

        var azureStatus = azureSubs.Count == 0
            ? "not_connected"
            : azureSubs.All(s => s == "connected")
                ? "connected"
                : azureSubs.Any(s => s == "connected")
                    ? "partial"
                    : "not_connected";

        var azureSubCount = azureSubs.Count(s => s == "connected");

        // graph=34%, azure=33%, powerbi=33%
        var pct = 0;
        if (graphConnected) pct += 34;
        if (azureStatus is "connected" or "partial") pct += 33;
        if (pbiConnected) pct += 33;

        return new ConnectionStatusResult
        {
            Graph = graphConnected ? "connected" : "not_connected",
            Azure = azureStatus,
            PowerBi = pbiConnected ? "connected" : "not_connected",
            AzureSubscriptionCount = azureSubCount,
            ConnectionPercentage = pct
        };
    }
}
