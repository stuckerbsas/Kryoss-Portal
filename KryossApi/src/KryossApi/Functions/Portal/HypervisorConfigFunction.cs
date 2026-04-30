using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using KryossApi.Services.InfraAssessment.Pipelines;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Portal;

public class HypervisorConfigFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IHypervisorPipeline _pipeline;
    private readonly ICryptoService _crypto;
    private readonly IActlogService _actlog;
    private readonly ILogger<HypervisorConfigFunction> _logger;

    public HypervisorConfigFunction(KryossDbContext db, ICurrentUserService user,
        IHypervisorPipeline pipeline, ICryptoService crypto, IActlogService actlog, ILogger<HypervisorConfigFunction> logger)
    {
        _db = db;
        _user = user;
        _pipeline = pipeline;
        _crypto = crypto;
        _actlog = actlog;
        _logger = logger;
    }

    private async Task<string?> GetOrgFingerprint(Guid orgId)
    {
        return await _db.OrgCryptoKeys
            .Where(k => k.OrganizationId == orgId && k.IsActive)
            .Select(k => k.Fingerprint)
            .FirstOrDefaultAsync();
    }

    [Function("HypervisorConfig_List")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/infra-assessment/hypervisor-configs")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var configs = await _db.InfraHypervisorConfigs
            .Where(c => c.OrganizationId == orgId)
            .OrderBy(c => c.DisplayName)
            .Select(c => new
            {
                c.Id,
                c.Platform,
                c.DisplayName,
                c.HostUrl,
                c.Username,
                c.VerifySsl,
                c.IsActive,
                c.LastTestedAt,
                c.LastTestOk,
                c.LastError,
                c.CreatedAt,
            })
            .ToListAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(configs);
        return res;
    }

    [Function("HypervisorConfig_Create")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/infra-assessment/hypervisor-configs")] HttpRequestData req)
    {
        var body = await JsonSerializer.DeserializeAsync<HypervisorConfigDto>(req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (body is null || string.IsNullOrWhiteSpace(body.Platform) || string.IsNullOrWhiteSpace(body.HostUrl))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "platform and hostUrl required" });
            return bad;
        }

        var config = new InfraHypervisorConfig
        {
            Id = Guid.NewGuid(),
            OrganizationId = body.OrganizationId,
            Platform = body.Platform.ToLower(),
            DisplayName = body.DisplayName,
            HostUrl = body.HostUrl,
            Username = body.Username,
            EncryptedPassword = null,
            ApiToken = body.ApiToken,
            VerifySsl = body.VerifySsl ?? true,
        };

        if (!string.IsNullOrEmpty(body.Password))
        {
            var fp = await GetOrgFingerprint(body.OrganizationId);
            if (fp != null)
                config.EncryptedPassword = Convert.ToBase64String(
                    _crypto.EncryptSymmetric(body.OrganizationId, fp, body.Password));
            else
                config.EncryptedPassword = body.Password;
        }

        _db.InfraHypervisorConfigs.Add(config);
        await _db.SaveChangesAsync();

        try { await _actlog.LogAsync("INFO", "hypervisor", "config_create",
            entityType: "infra_hypervisor_configs", entityId: config.Id.ToString(),
            message: $"Created config '{config.DisplayName}' ({config.Platform})"); }
        catch (Exception ex) { _logger.LogWarning(ex, "Actlog write failed"); }

        var res = req.CreateResponse(HttpStatusCode.Created);
        await res.WriteAsJsonAsync(new { config.Id, config.Platform, config.HostUrl, config.DisplayName });
        return res;
    }

    [Function("HypervisorConfig_Update")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/infra-assessment/hypervisor-configs/{configId}")] HttpRequestData req,
        string configId)
    {
        if (!Guid.TryParse(configId, out var id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            return bad;
        }

        var config = await _db.InfraHypervisorConfigs.FindAsync(id);
        if (config is null)
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            return nf;
        }

        var body = await JsonSerializer.DeserializeAsync<HypervisorConfigDto>(req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (body is null) return req.CreateResponse(HttpStatusCode.BadRequest);

        if (!string.IsNullOrWhiteSpace(body.DisplayName)) config.DisplayName = body.DisplayName;
        if (!string.IsNullOrWhiteSpace(body.HostUrl)) config.HostUrl = body.HostUrl;
        if (!string.IsNullOrWhiteSpace(body.Username)) config.Username = body.Username;
        if (!string.IsNullOrWhiteSpace(body.Password))
        {
            var fp = await GetOrgFingerprint(config.OrganizationId);
            if (fp != null)
                config.EncryptedPassword = Convert.ToBase64String(
                    _crypto.EncryptSymmetric(config.OrganizationId, fp, body.Password));
            else
                config.EncryptedPassword = body.Password;
        }
        if (!string.IsNullOrWhiteSpace(body.ApiToken)) config.ApiToken = body.ApiToken;
        if (body.VerifySsl.HasValue) config.VerifySsl = body.VerifySsl.Value;
        if (body.IsActive.HasValue) config.IsActive = body.IsActive.Value;
        config.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        try { await _actlog.LogAsync("INFO", "hypervisor", "config_update",
            entityType: "infra_hypervisor_configs", entityId: config.Id.ToString(),
            message: $"Updated config '{config.DisplayName}'"); }
        catch (Exception ex) { _logger.LogWarning(ex, "Actlog write failed"); }

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { config.Id, config.Platform, config.HostUrl, config.DisplayName, config.IsActive });
        return res;
    }

    [Function("HypervisorConfig_Delete")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v2/infra-assessment/hypervisor-configs/{configId}")] HttpRequestData req,
        string configId)
    {
        if (!Guid.TryParse(configId, out var id))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var config = await _db.InfraHypervisorConfigs.FindAsync(id);
        if (config is null) return req.CreateResponse(HttpStatusCode.NotFound);

        _db.InfraHypervisorConfigs.Remove(config);
        await _db.SaveChangesAsync();

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("HypervisorConfig_Test")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Test(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/infra-assessment/hypervisor-configs/{configId}/test")] HttpRequestData req,
        string configId)
    {
        if (!Guid.TryParse(configId, out var id))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var config = await _db.InfraHypervisorConfigs.FindAsync(id);
        if (config is null) return req.CreateResponse(HttpStatusCode.NotFound);

        string? password = null;
        if (!string.IsNullOrEmpty(config.EncryptedPassword))
        {
            try
            {
                var fp = await GetOrgFingerprint(config.OrganizationId);
                if (fp != null)
                    password = _crypto.DecryptSymmetric(config.OrganizationId, fp,
                        Convert.FromBase64String(config.EncryptedPassword));
                else
                    password = config.EncryptedPassword;
            }
            catch
            {
                password = config.EncryptedPassword; // pre-encryption legacy value
            }
        }

        if (!config.VerifySsl)
        {
            try { await _actlog.LogAsync("WARN", "hypervisor", "ssl_relaxed",
                entityType: "infra_hypervisor_configs", entityId: config.Id.ToString(),
                message: $"SSL verification relaxed for '{config.DisplayName}' ({config.HostUrl})"); }
            catch (Exception ex) { _logger.LogWarning(ex, "Actlog write failed"); }
        }

        bool success = false;
        string? error = null;

        try
        {
            using var handler = new HttpClientHandler();
            if (!config.VerifySsl)
                handler.ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None) return true;
                    // Allow self-signed (chain error only) but reject expired or wrong hostname
                    if (errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors
                        && cert is not null && cert.NotAfter > DateTime.UtcNow)
                        return true;
                    return false;
                };
            using var http = new HttpClient(handler) { BaseAddress = new Uri(config.HostUrl.TrimEnd('/')) };
            http.Timeout = TimeSpan.FromSeconds(10);

            if (config.Platform == "vmware")
            {
                var authReq = new HttpRequestMessage(HttpMethod.Post, "/api/session");
                authReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{config.Username}:{password}")));
                using var resp = await http.SendAsync(authReq);
                success = resp.IsSuccessStatusCode;
                if (!success) error = $"HTTP {(int)resp.StatusCode}";
            }
            else if (config.Platform == "proxmox")
            {
                if (!string.IsNullOrEmpty(config.ApiToken))
                {
                    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("PVEAPIToken", config.ApiToken);
                    using var resp = await http.GetAsync("/api2/json/version");
                    success = resp.IsSuccessStatusCode;
                    if (!success) error = $"HTTP {(int)resp.StatusCode}";
                }
                else
                {
                    var ticketBody = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("username", config.Username ?? ""),
                        new KeyValuePair<string, string>("password", password ?? ""),
                    });
                    using var resp = await http.PostAsync("/api2/json/access/ticket", ticketBody);
                    success = resp.IsSuccessStatusCode;
                    if (!success) error = $"HTTP {(int)resp.StatusCode}";
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
        }

        config.LastTestedAt = DateTime.UtcNow;
        config.LastTestOk = success;
        config.LastError = error;
        await _db.SaveChangesAsync();

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { success, error });
        return res;
    }

    [Function("Hypervisor_ScanResults")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> ScanResults(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/infra-assessment/hypervisors")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        // Get latest scan that has hypervisor data
        var latestScan = await _db.InfraAssessmentScans
            .Where(s => s.OrganizationId == orgId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestScan is null)
        {
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { hosts = Array.Empty<object>(), vms = Array.Empty<object>(), findings = Array.Empty<object>() });
            return res;
        }

        var hosts = await _db.InfraHypervisors
            .Where(h => h.ScanId == latestScan.Id)
            .ToListAsync();

        var vms = await _db.InfraVms
            .Where(v => v.ScanId == latestScan.Id)
            .ToListAsync();

        var findings = await _db.InfraAssessmentFindings
            .Where(f => f.ScanId == latestScan.Id && f.Service == "hypervisor")
            .ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            scanId = latestScan.Id,
            scannedAt = latestScan.CreatedAt,
            hosts = hosts.Select(h => new
            {
                h.Id,
                h.Platform,
                h.HostFqdn,
                h.Version,
                h.ClusterName,
                h.CpuCoresTotal,
                h.RamGbTotal,
                h.StorageGbTotal,
                h.CpuUsagePct,
                h.RamUsagePct,
                h.VmCount,
                h.VmRunning,
                h.HaEnabled,
                h.PowerState,
            }),
            vms = vms.Select(v => new
            {
                v.Id,
                v.HypervisorId,
                v.VmName,
                v.Os,
                v.PowerState,
                v.CpuCores,
                v.RamGb,
                v.DiskGb,
                v.CpuAvgPct,
                v.RamAvgPct,
                v.DiskUsedPct,
                v.SnapshotCount,
                v.OldestSnapshotDays,
                v.LastBackup,
                v.IpAddress,
                v.ToolsStatus,
                v.IsTemplate,
                v.IsIdle,
                v.Notes,
            }),
            findings = findings.Select(f => new
            {
                f.Area,
                f.Feature,
                f.Status,
                f.Priority,
                f.Observation,
                f.Recommendation,
            }),
        });
        return response;
    }
}

internal class HypervisorConfigDto
{
    public Guid OrganizationId { get; set; }
    public string Platform { get; set; } = "";
    public string? DisplayName { get; set; }
    public string HostUrl { get; set; } = "";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ApiToken { get; set; }
    public bool? VerifySsl { get; set; }
    public bool? IsActive { get; set; }
}
