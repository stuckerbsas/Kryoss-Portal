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
/// Phase 1 returns W10 / W11 / MS19 / MS22 / MS25. DC detection is NOT
/// done here — Phase 2+ will add a second pass using ProductType or AD
/// role. For Phase 1, any server receives an empty control list from
/// <see cref="Functions.Agent.ControlsFunction"/>, which is the desired
/// behavior (agent exits with code 2 → NinjaRMM reports "warning").
/// </summary>
public interface IPlatformResolver
{
    /// <summary>
    /// Pure string parser. Returns the platform code (e.g. "W11") or null
    /// if the OS name does not match any supported pattern.
    /// </summary>
    string? ResolveCode(string? osName, string? osVersion = null, string? osBuild = null);

    /// <summary>
    /// Resolves the platform code and looks up its int id in the
    /// <c>platforms</c> table. Returns null if the code cannot be mapped
    /// or the lookup fails (e.g. the platform row does not exist or is
    /// soft-deleted).
    /// </summary>
    Task<int?> ResolveIdAsync(string? osName, string? osVersion = null, string? osBuild = null, CancellationToken ct = default);
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

    public string? ResolveCode(string? osName, string? osVersion = null, string? osBuild = null)
    {
        if (string.IsNullOrWhiteSpace(osName))
            return null;

        // Case-insensitive contains. Order matters: "Windows 11" before
        // "Windows 10", and the server SKUs before the workstations so
        // a future "Windows 11 Server" (doesn't exist today) still hits
        // the right branch. Today the ordering below is safe.
        if (osName.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
            return "W11";

        if (osName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            // Defensive: Microsoft never updated ProductName on Windows 11,
            // so the registry still says "Windows 10 Pro" on Win11 hosts.
            // Old agents (pre-2026-04-08) pass the string through untouched.
            // Use the build number as authoritative discriminator: >=22000 = Win11.
            if (int.TryParse(osBuild?.Split('.')[0], out var buildMajor) && buildMajor >= 22000)
                return "W11";

            return "W10";
        }

        // Server SKUs. Phase 1 treats all server roles as "member server"
        // (MS*) — DC detection is deferred to Phase 2+.
        if (osName.Contains("Server 2025", StringComparison.OrdinalIgnoreCase))
            return "MS25";

        if (osName.Contains("Server 2022", StringComparison.OrdinalIgnoreCase))
            return "MS22";

        if (osName.Contains("Server 2019", StringComparison.OrdinalIgnoreCase))
            return "MS19";

        return null;
    }

    public async Task<int?> ResolveIdAsync(
        string? osName,
        string? osVersion = null,
        string? osBuild = null,
        CancellationToken ct = default)
    {
        var code = ResolveCode(osName, osVersion, osBuild);
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
