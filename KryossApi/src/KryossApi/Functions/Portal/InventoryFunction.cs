using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// Org-level hardware and software inventory endpoints.
/// </summary>
[RequirePermission("machines:read")]
public class InventoryFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public InventoryFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    // ── Hardware Inventory ──

    [Function("Inventory_Hardware")]
    public async Task<HttpResponseData> Hardware(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/inventory/hardware")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        Guid? orgId = Guid.TryParse(orgIdStr, out var parsed) ? parsed : _user.OrganizationId;
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        var machines = await _db.Machines
            .Where(m => m.OrganizationId == orgId.Value && m.IsActive)
            .OrderBy(m => m.Hostname)
            .Select(m => new HardwareItem
            {
                Id = m.Id,
                Hostname = m.Hostname,
                OsName = m.OsName,
                OsVersion = m.OsVersion,
                CpuName = m.CpuName,
                CpuCores = m.CpuCores,
                RamGb = m.RamGb,
                DiskType = m.DiskType,
                DiskSizeGb = m.DiskSizeGb,
                DiskFreeGb = m.DiskFreeGb,
                Manufacturer = m.Manufacturer,
                Model = m.Model,
                SerialNumber = m.SerialNumber,
                TpmPresent = m.TpmPresent,
                TpmVersion = m.TpmVersion,
                SecureBoot = m.SecureBoot,
                Bitlocker = m.Bitlocker,
                IpAddress = m.IpAddress,
                MacAddress = m.MacAddress,
                LastSeenAt = m.LastSeenAt
            })
            .ToListAsync();

        // Compute Win11 readiness for each machine
        foreach (var m in machines)
        {
            var blockers = new List<string>();
            if (m.TpmPresent != true || m.TpmVersion != "2.0")
                blockers.Add("TPM 2.0 required");
            if (m.SecureBoot != true)
                blockers.Add("Secure Boot required");
            if (m.RamGb is null || m.RamGb < 4)
                blockers.Add("RAM >= 4 GB required");
            if (m.DiskSizeGb is null || m.DiskSizeGb < 64)
                blockers.Add("Disk >= 64 GB required");

            m.Win11Ready = blockers.Count == 0;
            m.Win11Blockers = blockers;
        }

        var win11Ready = machines.Count(m => m.Win11Ready);
        var result = new
        {
            total = machines.Count,
            win11Ready,
            win11NotReady = machines.Count - win11Ready,
            items = machines
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    // ── Software Inventory ──

    [Function("Inventory_Software")]
    public async Task<HttpResponseData> Software(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/inventory/software")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        Guid? orgId = Guid.TryParse(orgIdStr, out var parsed) ? parsed : _user.OrganizationId;
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        // Get the latest run per machine, with RawPayload
        var machineIds = await _db.Machines
            .Where(m => m.OrganizationId == orgId.Value && m.IsActive)
            .Select(m => m.Id)
            .ToListAsync();

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Map: softwareKey -> aggregated data
        var softwareMap = new Dictionary<string, SoftwareAggregation>(StringComparer.OrdinalIgnoreCase);

        foreach (var machineId in machineIds)
        {
            var run = await _db.AssessmentRuns
                .Where(r => r.MachineId == machineId)
                .OrderByDescending(r => r.StartedAt)
                .Select(r => new { r.RawPayload, r.Machine.Hostname })
                .FirstOrDefaultAsync();

            if (run?.RawPayload is null) continue;

            AgentPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<AgentPayload>(run.RawPayload, opts);
            }
            catch
            {
                continue;
            }

            if (payload?.Software is null) continue;

            foreach (var sw in payload.Software)
            {
                if (string.IsNullOrWhiteSpace(sw.Name)) continue;

                var key = $"{sw.Name}|||{sw.Publisher}|||{sw.Version}";
                if (!softwareMap.TryGetValue(key, out var agg))
                {
                    agg = new SoftwareAggregation
                    {
                        Name = sw.Name,
                        Publisher = sw.Publisher,
                        Version = sw.Version,
                        Category = Categorize(sw.Name, sw.Publisher),
                        Machines = []
                    };
                    softwareMap[key] = agg;
                }
                if (!agg.Machines.Contains(run.Hostname, StringComparer.OrdinalIgnoreCase))
                    agg.Machines.Add(run.Hostname);
            }
        }

        var items = softwareMap.Values
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Name,
                s.Publisher,
                s.Version,
                machineCount = s.Machines.Count,
                s.Category,
                machines = s.Machines.OrderBy(h => h).ToList()
            })
            .ToList();

        var licensed = items.Count(i => i.Category == "licensed");
        var remoteAccess = items.Count(i => i.Category == "remote_access");
        var suspicious = items.Count(i => i.Category == "suspicious");

        var result = new
        {
            total = items.Count,
            licensed,
            remoteAccess,
            suspicious,
            items
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    // ── Categorization ──

    private static string Categorize(string name, string? publisher)
    {
        var combined = $"{name} {publisher}";

        if (MatchesAny(combined, SuspiciousPatterns))
            return "suspicious";
        if (MatchesAny(combined, RemoteAccessPatterns))
            return "remote_access";
        if (MatchesAny(combined, LicensedPatterns))
            return "licensed";

        return "standard";
    }

    private static bool MatchesAny(string text, string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static readonly string[] LicensedPatterns =
    [
        "Microsoft Office", "Microsoft 365", "Adobe", "AutoCAD", "Autodesk",
        "VMware", "Veeam", "Symantec", "McAfee", "Kaspersky", "ESET",
        "QuickBooks", "Sage", "SAP", "Oracle", "SQL Server",
        "Visual Studio", "JetBrains", "Slack", "Zoom", "Webex",
        "Citrix", "SonicWall", "Fortinet", "SolarWinds"
    ];

    private static readonly string[] RemoteAccessPatterns =
    [
        "TeamViewer", "AnyDesk", "LogMeIn", "Splashtop", "ConnectWise",
        "ScreenConnect", "UltraVNC", "TightVNC", "RealVNC", "Ammyy",
        "RemotePC", "Supremo", "Radmin", "DameWare", "BeyondTrust",
        "Bomgar", "GoToMyPC", "Chrome Remote Desktop"
    ];

    private static readonly string[] SuspiciousPatterns =
    [
        "BitTorrent", "uTorrent", "qBittorrent", "Vuze",
        "NiceHash", "XMRig", "PhoenixMiner", "CGMiner",
        "Wireshark", "Nmap", "Metasploit", "Burp Suite",
        "Cain", "John the Ripper", "Hashcat", "Mimikatz",
        "CCleaner", "IObit", "Avast Free", "AVG Free",
        "Hola VPN", "Hotspot Shield Free"
    ];

    // ── DTOs ──

    private class HardwareItem
    {
        public Guid Id { get; set; }
        public string Hostname { get; set; } = null!;
        public string? OsName { get; set; }
        public string? OsVersion { get; set; }
        public string? CpuName { get; set; }
        public short? CpuCores { get; set; }
        public short? RamGb { get; set; }
        public string? DiskType { get; set; }
        public int? DiskSizeGb { get; set; }
        public decimal? DiskFreeGb { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public bool? TpmPresent { get; set; }
        public string? TpmVersion { get; set; }
        public bool? SecureBoot { get; set; }
        public bool? Bitlocker { get; set; }
        public string? IpAddress { get; set; }
        public string? MacAddress { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public bool Win11Ready { get; set; }
        public List<string> Win11Blockers { get; set; } = [];
    }

    private class SoftwareAggregation
    {
        public string Name { get; set; } = null!;
        public string? Publisher { get; set; }
        public string? Version { get; set; }
        public string Category { get; set; } = "standard";
        public List<string> Machines { get; set; } = [];
    }
}
