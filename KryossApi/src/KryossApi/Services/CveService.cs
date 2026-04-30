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

    public CveService(KryossDbContext db) => _db = db;

    public async Task ScanMachineAsync(Guid machineId, Guid organizationId, Guid? runId)
    {
        var old = await _db.MachineCveFindings
            .Where(f => f.MachineId == machineId)
            .ToListAsync();
        _db.MachineCveFindings.RemoveRange(old);

        // Path A: CveProductMap bridge table (populated by RebuildProductMapAsync)
        var mappedFindings = await (
            from msi in _db.MachineSoftware
            join cpm in _db.CveProductMaps on msi.SoftwareId equals cpm.SoftwareId
            join cve in _db.CveEntries on cpm.CveEntryId equals cve.Id
            where msi.MachineId == machineId && msi.RemovedAt == null
            select new { msi, cpm, cve, softwareName = msi.Software.Name }
        ).ToListAsync();

        foreach (var f in mappedFindings)
        {
            if (!IsVersionAffected(f.msi.Version, f.cpm.AffectedBelow, f.cpm.FixedVersion))
                continue;

            _db.MachineCveFindings.Add(new MachineCveFinding
            {
                MachineId = machineId,
                OrganizationId = organizationId,
                RunId = runId,
                CveId = f.cve.CveId,
                SoftwareName = f.softwareName,
                SoftwareVersion = f.msi.Version,
                InstalledVersion = f.msi.Version,
                FixedVersion = f.cpm.FixedVersion,
                Severity = f.cve.Severity,
                CvssScore = f.cve.CvssScore,
                Description = f.cve.Description,
            });
        }

        // Path B: Direct matching via Software.CpeVendor/CpeProduct → cve_entries.vendor/product
        if (mappedFindings.Count == 0)
        {
            var machineSw = await _db.MachineSoftware
                .Include(ms => ms.Software)
                .Where(ms => ms.MachineId == machineId && ms.RemovedAt == null
                    && ms.Software.CpeVendor != null && ms.Software.CpeProduct != null)
                .ToListAsync();

            if (machineSw.Count > 0)
            {
                var vendors = machineSw.Select(ms => ms.Software.CpeVendor!).Distinct().ToList();
                var cves = await _db.CveEntries
                    .Where(c => c.Vendor != null && vendors.Contains(c.Vendor))
                    .ToListAsync();

                foreach (var ms in machineSw)
                {
                    var sw = ms.Software;
                    var matched = cves.Where(c =>
                        string.Equals(c.Vendor, sw.CpeVendor, StringComparison.OrdinalIgnoreCase)
                        && c.Product != null
                        && sw.CpeProduct != null
                        && c.Product.Contains(sw.CpeProduct, StringComparison.OrdinalIgnoreCase));

                    foreach (var cve in matched)
                    {
                        if (!IsVersionAffected(ms.Version, cve.AffectedBelow, cve.FixedVersion))
                            continue;

                        _db.MachineCveFindings.Add(new MachineCveFinding
                        {
                            MachineId = machineId,
                            OrganizationId = organizationId,
                            RunId = runId,
                            CveId = cve.CveId,
                            SoftwareName = sw.Name,
                            SoftwareVersion = ms.Version,
                            InstalledVersion = ms.Version,
                            FixedVersion = cve.FixedVersion,
                            Severity = cve.Severity,
                            CvssScore = cve.CvssScore,
                            Description = cve.Description,
                        });
                    }
                }
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

    internal static bool IsVersionAffected(string? installedVersion, string? affectedBelow, string? fixedVersion)
    {
        if (installedVersion is null) return true;
        var target = affectedBelow ?? fixedVersion;
        if (target is null) return true;
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
        var v = version.Trim();
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
        var parts = v.Split('.', '-', '_', ' ');
        var result = new List<long>();
        foreach (var part in parts)
        {
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
}
