using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface ICveService
{
    Task ScanMachineAsync(Guid machineId, Guid organizationId, Guid? runId);
    Task ScanOrganizationAsync(Guid organizationId);
}

public class CveService : ICveService
{
    private readonly KryossDbContext _db;
    private List<CveEntry>? _cachedEntries;

    public CveService(KryossDbContext db) => _db = db;

    public async Task ScanMachineAsync(Guid machineId, Guid organizationId, Guid? runId)
    {
        var entries = await GetCveEntriesAsync();
        if (entries.Count == 0) return;

        var run = await _db.AssessmentRuns
            .Where(r => r.MachineId == machineId)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new { r.Id, r.RawPayload })
            .FirstOrDefaultAsync();

        if (run?.RawPayload is null) return;

        List<SoftwareDto>? software;
        try
        {
            var payload = JsonSerializer.Deserialize<PayloadDto>(run.RawPayload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            software = payload?.Software;
        }
        catch { return; }

        if (software is null || software.Count == 0) return;

        // Remove old findings for this machine
        var old = await _db.MachineCveFindings
            .Where(f => f.MachineId == machineId)
            .ToListAsync();
        _db.MachineCveFindings.RemoveRange(old);

        foreach (var sw in software)
        {
            if (string.IsNullOrWhiteSpace(sw.Name)) continue;

            foreach (var cve in entries)
            {
                if (!MatchesProduct(sw.Name, cve.ProductPattern)) continue;
                if (!IsVersionAffected(sw.Version, cve.AffectedBelow, cve.FixedVersion)) continue;

                _db.MachineCveFindings.Add(new MachineCveFinding
                {
                    MachineId = machineId,
                    OrganizationId = organizationId,
                    RunId = runId ?? run.Id,
                    CveId = cve.CveId,
                    SoftwareName = sw.Name,
                    SoftwareVersion = sw.Version,
                    InstalledVersion = sw.Version,
                    FixedVersion = cve.FixedVersion,
                    Severity = cve.Severity,
                    CvssScore = cve.CvssScore,
                    Description = cve.Description,
                });
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task ScanOrganizationAsync(Guid organizationId)
    {
        var machineIds = await _db.Machines
            .Where(m => m.OrganizationId == organizationId && m.IsActive)
            .Select(m => m.Id)
            .ToListAsync();

        foreach (var id in machineIds)
            await ScanMachineAsync(id, organizationId, null);
    }

    private async Task<List<CveEntry>> GetCveEntriesAsync()
    {
        _cachedEntries ??= await _db.CveEntries.AsNoTracking().ToListAsync();
        return _cachedEntries;
    }

    private static bool MatchesProduct(string softwareName, string pattern)
    {
        // Convert SQL LIKE pattern to simple contains matching
        // Pattern format: %keyword1%keyword2%
        var parts = pattern.Split('%', StringSplitOptions.RemoveEmptyEntries);
        var remaining = softwareName;
        foreach (var part in parts)
        {
            var idx = remaining.IndexOf(part, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            remaining = remaining[(idx + part.Length)..];
        }
        return true;
    }

    internal static bool IsVersionAffected(string? installedVersion, string? affectedBelow, string? fixedVersion)
    {
        if (installedVersion is null) return true; // unknown version = assume vulnerable

        var target = affectedBelow ?? fixedVersion;
        if (target is null) return true; // no version constraint = all versions affected

        return CompareVersions(installedVersion, target) < 0;
    }

    internal static int CompareVersions(string a, string b)
    {
        var partsA = NormalizeVersion(a);
        var partsB = NormalizeVersion(b);

        var len = Math.Max(partsA.Length, partsB.Length);
        for (int i = 0; i < len; i++)
        {
            var va = i < partsA.Length ? partsA[i] : 0;
            var vb = i < partsB.Length ? partsB[i] : 0;
            if (va < vb) return -1;
            if (va > vb) return 1;
        }
        return 0;
    }

    private static long[] NormalizeVersion(string version)
    {
        // Strip common prefixes/suffixes
        var v = version.Trim();
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];

        // Extract numeric parts
        var parts = v.Split('.', '-', '_', ' ');
        var result = new List<long>();
        foreach (var part in parts)
        {
            // Extract leading digits from each part
            var digits = "";
            foreach (var c in part)
            {
                if (char.IsDigit(c)) digits += c;
                else break;
            }
            if (digits.Length > 0 && long.TryParse(digits, out var num))
                result.Add(num);
        }
        return result.Count > 0 ? result.ToArray() : [0];
    }

    private class PayloadDto
    {
        public List<SoftwareDto>? Software { get; set; }
    }

    private class SoftwareDto
    {
        public string Name { get; set; } = null!;
        public string? Version { get; set; }
        public string? Publisher { get; set; }
    }
}
