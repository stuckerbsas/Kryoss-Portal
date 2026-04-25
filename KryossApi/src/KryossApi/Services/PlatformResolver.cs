using KryossApi.Data;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

/// <summary>
/// Server-side OS-string → platform mapping.
///
/// Design principle: the agent stays dumb. It never sends a platformCode.
/// The backend parses <c>os_name</c> (and falls back to <c>os_version</c> /
/// <c>os_build</c> if needed) and resolves to one of the seeded platform
/// codes (W10, W11, MS19, MS22, MS25, DC19, DC22, DC25).
///
/// Phase 2: when <c>productType</c> is 2 (Domain Controller), server SKUs
/// resolve to DC19/DC22/DC25 instead of MS19/MS22/MS25.
/// </summary>
public interface IPlatformResolver
{
    string? ResolveCode(string? osName, string? osVersion = null, string? osBuild = null, int? productType = null);

    Task<int?> ResolveIdAsync(string? osName, string? osVersion = null, string? osBuild = null, int? productType = null, CancellationToken ct = default);
}

public class PlatformResolver : IPlatformResolver
{
    private readonly KryossDbContext _db;

    // Process-lifetime cache: code → id. Platforms are seed data and
    // effectively immutable at runtime (7 rows). Concurrent reads are
    // safe on Dictionary once the entry has been set; we only add.
    private static readonly Dictionary<string, int> _codeToId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);

    public PlatformResolver(KryossDbContext db)
    {
        _db = db;
    }

    public string? ResolveCode(string? osName, string? osVersion = null, string? osBuild = null, int? productType = null)
    {
        if (string.IsNullOrWhiteSpace(osName))
            return null;

        bool isDc = productType == 2;

        if (osName.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
            return "W11";

        if (osName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(osBuild?.Split('.')[0], out var buildMajor) && buildMajor >= 22000)
                return "W11";

            return "W10";
        }

        if (osName.Contains("Server 2025", StringComparison.OrdinalIgnoreCase))
            return isDc ? "DC25" : "MS25";

        if (osName.Contains("Server 2022", StringComparison.OrdinalIgnoreCase))
            return isDc ? "DC22" : "MS22";

        if (osName.Contains("Server 2019", StringComparison.OrdinalIgnoreCase))
            return isDc ? "DC19" : "MS19";

        return null;
    }

    public async Task<int?> ResolveIdAsync(
        string? osName,
        string? osVersion = null,
        string? osBuild = null,
        int? productType = null,
        CancellationToken ct = default)
    {
        var code = ResolveCode(osName, osVersion, osBuild, productType);
        if (code is null)
            return null;

        // Fast path: cached
        if (_codeToId.TryGetValue(code, out var cached))
            return cached;

        // Slow path: one-time DB lookup + cache
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_codeToId.TryGetValue(code, out cached))
                return cached;

            var id = await _db.Platforms
                .Where(p => p.Code == code && p.IsActive)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync(ct);

            if (id is not null)
                _codeToId[code] = id.Value;

            return id;
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}
