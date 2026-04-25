using KryossApi.Data.Entities;

namespace KryossApi.Services.InfraAssessment;

public interface IInfraAssessmentService
{
    Task<InfraAssessmentScan> StartScanAsync(Guid organizationId, string? scope);
    Task<InfraAssessmentScan?> GetLatestAsync(Guid organizationId);
    Task<InfraAssessmentScan?> GetDetailAsync(Guid scanId);
    Task<List<InfraAssessmentScan>> GetHistoryAsync(Guid organizationId, int take = 20);
}
