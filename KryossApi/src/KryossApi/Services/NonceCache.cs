using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace KryossApi.Services;

public interface INonceCache
{
    bool TryRegister(Guid organizationId, string signature);
}

public sealed class InMemoryNonceCache : INonceCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private const int SweepThreshold = 10_000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private int _sweepTick;

    public bool TryRegister(Guid organizationId, string signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        if (Interlocked.Increment(ref _sweepTick) % 1024 == 0 && _seen.Count > SweepThreshold)
            SweepExpired();

        var key = $"{organizationId:N}:{signature}";
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now + Ttl;

        var inserted = _seen.TryAdd(key, expiresAt);
        if (inserted)
            return true;

        if (_seen.TryGetValue(key, out var existing) && existing > now)
            return false;

        _seen[key] = expiresAt;
        return true;
    }

    private void SweepExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _seen)
        {
            if (kvp.Value <= now)
                _seen.TryRemove(kvp);
        }
    }
}

public sealed class RedisNonceCache : INonceCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisNonceCache> _logger;
    private readonly InMemoryNonceCache _fallback = new();

    public RedisNonceCache(IConnectionMultiplexer redis, ILogger<RedisNonceCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public bool TryRegister(Guid organizationId, string signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        try
        {
            var db = _redis.GetDatabase();
            var key = $"nonce:{organizationId:N}:{signature}";
            var wasSet = db.StringSet(key, "1", Ttl, When.NotExists);
            return wasSet;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis nonce cache unavailable, falling back to in-memory");
            return _fallback.TryRegister(organizationId, signature);
        }
    }
}
