namespace KryossApi.Services.CloudAssessment;

public interface ICloudAssessmentService
{
    Task<Guid> StartScanAsync(Guid organizationId, Guid? tenantId);
    Task<object?> GetLatestScanAsync(Guid organizationId);
    Task<object?> GetScanDetailAsync(Guid scanId);
    Task<List<object>> GetScanHistoryAsync(Guid organizationId, int limit = 20);
    Task<object?> CompareScansAsync(Guid scanAId, Guid scanBId);
}
