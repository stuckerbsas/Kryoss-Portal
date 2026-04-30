using KryossApi.Data;
using KryossApi.Data.Entities;

namespace KryossApi.Services;

public interface IActlogService
{
    Task LogAsync(string severity, string module, string action, string? message = null,
        string? entityType = null, string? entityId = null,
        string? oldValues = null, string? newValues = null,
        short? responseCode = null, int? durationMs = null);
}

public class ActlogService : IActlogService
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public ActlogService(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    public async Task LogAsync(string severity, string module, string action,
        string? message = null, string? entityType = null, string? entityId = null,
        string? oldValues = null, string? newValues = null,
        short? responseCode = null, int? durationMs = null)
    {
        var userId = _user.UserId == Guid.Empty ? null : (Guid?)_user.UserId;
        var entry = new Actlog
        {
            Timestamp = DateTime.UtcNow,
            ActorId = userId,
            ActorEmail = _user.Email,
            ActorIp = _user.IpAddress,
            SessionId = _user.SessionId,
            Severity = severity,
            Module = module,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            ResponseCode = responseCode,
            DurationMs = durationMs,
            Message = message,
            MachineId = _user.MachineId,
            UserId = userId
        };

        _db.Actlog.Add(entry);
        await _db.SaveChangesAsync();
    }
}
