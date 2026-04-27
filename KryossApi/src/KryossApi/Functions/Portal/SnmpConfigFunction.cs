using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class SnmpConfigFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public SnmpConfigFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    [Function("SnmpConfig_Get")]
    public async Task<HttpResponseData> GetConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/snmp-config")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgId = query["organizationId"];
        if (string.IsNullOrEmpty(orgId) || !Guid.TryParse(orgId, out var orgGuid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("{\"error\":\"organizationId required\"}");
            return bad;
        }

        var config = await _db.SnmpConfigs.FirstOrDefaultAsync(c => c.OrganizationId == orgGuid);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        if (config == null)
        {
            await resp.WriteAsJsonAsync(new { configured = false });
        }
        else
        {
            await resp.WriteAsJsonAsync(new
            {
                configured = true,
                config.Enabled,
                version = (int)config.SnmpVersion,
                community = config.Community != null ? "***" : null,
                config.Username,
                config.AuthProtocol,
                config.PrivProtocol,
                targets = string.IsNullOrEmpty(config.Targets) ? null : JsonSerializer.Deserialize<List<string>>(config.Targets),
                config.UpdatedAt,
            });
        }
        return resp;
    }

    [Function("SnmpConfig_Save")]
    public async Task<HttpResponseData> SaveConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v2/snmp-config")] HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        if (string.IsNullOrEmpty(body))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var dto = JsonSerializer.Deserialize<SnmpConfigDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (dto == null || dto.OrganizationId == Guid.Empty)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var existing = await _db.SnmpConfigs.FirstOrDefaultAsync(c => c.OrganizationId == dto.OrganizationId);
        if (existing != null)
        {
            existing.SnmpVersion = dto.Version;
            existing.Community = dto.Community;
            existing.Username = dto.Username;
            existing.AuthProtocol = dto.AuthProtocol;
            if (dto.AuthPassword != null) existing.AuthPassword = dto.AuthPassword;
            existing.PrivProtocol = dto.PrivProtocol;
            if (dto.PrivPassword != null) existing.PrivPassword = dto.PrivPassword;
            existing.Targets = dto.Targets != null ? JsonSerializer.Serialize(dto.Targets) : null;
            existing.Enabled = dto.Enabled;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.SnmpConfigs.Add(new SnmpConfig
            {
                OrganizationId = dto.OrganizationId,
                SnmpVersion = dto.Version,
                Community = dto.Community,
                Username = dto.Username,
                AuthProtocol = dto.AuthProtocol,
                AuthPassword = dto.AuthPassword,
                PrivProtocol = dto.PrivProtocol,
                PrivPassword = dto.PrivPassword,
                Targets = dto.Targets != null ? JsonSerializer.Serialize(dto.Targets) : null,
                Enabled = dto.Enabled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { saved = true });
        return resp;
    }

    [Function("SnmpDevices_List")]
    public async Task<HttpResponseData> ListDevices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/snmp-devices")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgId = query["organizationId"];
        if (string.IsNullOrEmpty(orgId) || !Guid.TryParse(orgId, out var orgGuid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("{\"error\":\"organizationId required\"}");
            return bad;
        }

        var devices = await _db.SnmpDevices
            .Where(d => d.OrganizationId == orgGuid)
            .OrderBy(d => d.IpAddress)
            .Select(d => new
            {
                d.Id,
                d.IpAddress,
                d.MacAddress,
                d.Vendor,
                d.SysName,
                d.SysDescr,
                uptimeDays = d.SysUptimeSec.HasValue ? d.SysUptimeSec.Value / 86400 : (long?)null,
                d.SysLocation,
                d.DeviceType,
                d.EntityModel,
                d.EntitySerial,
                d.EntityMfg,
                d.EntityFirmware,
                d.InterfaceCount,
                d.LldpNeighborCount,
                d.CdpNeighborCount,
                d.PageCount,
                cpuLoadPct = d.CpuLoadPct,
                memoryTotalMb = d.MemoryTotalMb,
                memoryUsedMb = d.MemoryUsedMb,
                diskTotalGb = d.DiskTotalGb,
                diskUsedGb = d.DiskUsedGb,
                processCount = d.ProcessCount,
                d.FirstSeenAt,
                d.IsStale,
                d.MachineId,
                d.ScanSource,
                d.SecondaryIps,
                d.ScannedAt,
                d.RawData,
                d.VendorData,
                interfaces = d.Interfaces.Select(i => new
                {
                    i.IfIndex,
                    i.Name,
                    i.Description,
                    i.IfType,
                    i.SpeedMbps,
                    i.MacAddress,
                    i.AdminStatus,
                    i.OperStatus,
                    i.InErrors,
                    i.OutErrors,
                }),
                supplies = d.Supplies.Select(s => new
                {
                    s.Description,
                    s.SupplyType,
                    s.Color,
                    s.LevelPercent,
                    s.MaxCapacity,
                    s.CurrentLevel,
                }),
            })
            .ToListAsync();

        var result = devices.Select(d =>
        {
            JsonElement? lldpNeighbors = null;
            JsonElement? cdpNeighbors = null;
            if (!string.IsNullOrEmpty(d.RawData))
            {
                try
                {
                    using var doc = JsonDocument.Parse(d.RawData);
                    if (doc.RootElement.TryGetProperty("lldpNeighbors", out var lldp))
                        lldpNeighbors = JsonSerializer.Deserialize<JsonElement>(lldp.GetRawText());
                    if (doc.RootElement.TryGetProperty("cdpNeighbors", out var cdp))
                        cdpNeighbors = JsonSerializer.Deserialize<JsonElement>(cdp.GetRawText());
                }
                catch { }
            }
            return new
            {
                d.Id, d.IpAddress, d.MacAddress, d.Vendor, d.SysName, d.SysDescr, d.uptimeDays,
                d.SysLocation, d.DeviceType, d.EntityModel, d.EntitySerial, d.EntityMfg, d.EntityFirmware,
                d.InterfaceCount, d.LldpNeighborCount, d.CdpNeighborCount,
                d.PageCount, d.cpuLoadPct, d.memoryTotalMb, d.memoryUsedMb,
                d.diskTotalGb, d.diskUsedGb, d.processCount,
                d.FirstSeenAt, d.IsStale, d.MachineId, d.ScanSource, d.SecondaryIps,
                d.ScannedAt, d.interfaces, d.supplies,
                lldpNeighbors, cdpNeighbors,
                vendorData = !string.IsNullOrEmpty(d.VendorData)
                    ? (JsonElement?)JsonSerializer.Deserialize<JsonElement>(d.VendorData) : null,
            };
        });

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(result);
        return resp;
    }
}

internal class SnmpConfigDto
{
    public Guid OrganizationId { get; set; }
    public short Version { get; set; } = 2;
    public string? Community { get; set; }
    public string? Username { get; set; }
    public string? AuthProtocol { get; set; }
    public string? AuthPassword { get; set; }
    public string? PrivProtocol { get; set; }
    public string? PrivPassword { get; set; }
    public List<string>? Targets { get; set; }
    public bool Enabled { get; set; } = true;
}
