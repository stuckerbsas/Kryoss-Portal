using System.Security.Cryptography;
using System.Text;
using KryossApi.Data;
using KryossApi.Data.Entities;

namespace KryossApi.Services;

public interface IRemediationLogService
{
    Task LogAsync(long taskId, Guid machineId, Guid organizationId, string eventType,
        string actionType, int? controlDefId = null, string? serviceName = null,
        string? paramsJson = null, string? previousValue = null, string? newValue = null,
        string? errorMessage = null, string? signatureHash = null,
        Guid? actorId = null, string? ipAddress = null);
}

public class RemediationLogService : IRemediationLogService
{
    private readonly KryossDbContext _db;

    public RemediationLogService(KryossDbContext db) => _db = db;

    public async Task LogAsync(long taskId, Guid machineId, Guid organizationId, string eventType,
        string actionType, int? controlDefId = null, string? serviceName = null,
        string? paramsJson = null, string? previousValue = null, string? newValue = null,
        string? errorMessage = null, string? signatureHash = null,
        Guid? actorId = null, string? ipAddress = null)
    {
        _db.RemediationLogs.Add(new RemediationLog
        {
            TaskId = taskId,
            MachineId = machineId,
            OrganizationId = organizationId,
            EventType = eventType,
            ActorId = actorId,
            ActionType = actionType,
            ControlDefId = controlDefId,
            ServiceName = serviceName,
            ParamsHash = paramsJson is not null
                ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paramsJson))).ToLowerInvariant()
                : null,
            PreviousValue = previousValue?.Length > 500 ? previousValue[..500] : previousValue,
            NewValue = newValue?.Length > 500 ? newValue[..500] : newValue,
            ErrorMessage = errorMessage?.Length > 500 ? errorMessage[..500] : errorMessage,
            SignatureHash = signatureHash,
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }
}
