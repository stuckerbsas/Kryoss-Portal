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
        var entry = new Actlog
        {
            Timestamp = DateTime.UtcNow,
            ActorId = _user.UserId == Guid.Empty ? null : _user.UserId,
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
            Message = message
        };

        _db.Actlog.Add(entry);
        await _db.SaveChangesAsync();
    }
}
