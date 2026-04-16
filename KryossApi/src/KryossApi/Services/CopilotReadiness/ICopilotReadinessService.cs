namespace KryossApi.Services.CopilotReadiness;

public interface ICopilotReadinessService
{
    Task<Guid> StartScanAsync(Guid organizationId, Guid tenantId, string customerTenantId);
    Task<object?> GetLatestScanAsync(Guid organizationId);
    Task<object?> GetScanDetailAsync(Guid scanId);
    Task<List<object>> GetScanHistoryAsync(Guid organizationId);
}
