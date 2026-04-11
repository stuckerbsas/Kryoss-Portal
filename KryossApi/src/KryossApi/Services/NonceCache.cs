using System.Collections.Concurrent;

namespace KryossApi.Services;

/// <summary>
/// Short-lived replay cache for HMAC signatures. The signature itself is the
/// nonce — every signed request produces a unique value because the canonical
/// string includes the timestamp (±5 min window enforced elsewhere), so an
/// attacker replaying the same bytes within the window will hit an identical
/// signature and be rejected here.
///
/// <para>
/// This is an in-process <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed
/// by <c>org_id:signature</c>. That scope is deliberate: two different orgs
/// will never produce the same signature anyway, but prefixing keeps lookups
/// O(1) and makes per-org eviction trivial if that's ever needed.
/// </para>
///
/// <para>
/// On a multi-instance Function App this cache is per-instance, which means
/// an attacker COULD in theory replay a signature against a different worker.
/// Mitigation: (a) the ±300 s timestamp window already bounds the replay
/// horizon to 5 minutes, (b) the likelihood of the same signature landing
/// on a different worker within that window is low because the Function App
/// normally runs 1–3 warm instances, (c) the backlog includes a Redis
/// implementation (see security-baseline.md §Implementation backlog — P2).
/// For the current single-consumption-plan deployment this is good enough.
/// </para>
///
/// <para>
/// Eviction: a lightweight sweep runs on every access when the dictionary
/// exceeds 10k entries. Expired entries are also filtered at lookup time,
/// so stale entries never cause false positives even between sweeps.
/// </para>
/// </summary>
public interface INonceCache
{
    /// <summary>
    /// Attempts to register <paramref name="signature"/> as a fresh nonce for
    /// <paramref name="organizationId"/>. Returns <c>true</c> if this is the
    /// first time we've seen it within the TTL window (request is OK to
    /// process), <c>false</c> if it's a replay (caller must reject 401).
    /// </summary>
    bool TryRegister(Guid organizationId, string signature);
}

public sealed class NonceCache : INonceCache
{
    // TTL must be >= HMAC max timestamp skew, so that a request still inside
    // the allowed window cannot bypass the cache by landing just after a
    // sweep. 10 minutes gives a 2x safety margin over the 5-minute skew.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private const int SweepThreshold = 10_000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private int _sweepTick;

    public bool TryRegister(Guid organizationId, string signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        // Amortized sweep — do not hold any locks. Worst case a concurrent
        // writer races a sweep and its entry survives, which is harmless.
        if (Interlocked.Increment(ref _sweepTick) % 1024 == 0 && _seen.Count > SweepThreshold)
            SweepExpired();

        var key = $"{organizationId:N}:{signature}";
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now + Ttl;

        // First insert wins. If the key already exists AND is still fresh,
        // this is a replay. If the existing entry is expired, refresh it
        // and accept the request.
        var inserted = _seen.TryAdd(key, expiresAt);
        if (inserted)
            return true;

        if (_seen.TryGetValue(key, out var existing) && existing > now)
            return false; // Replay within the window.

        // Expired entry — overwrite and accept.
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
