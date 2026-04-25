using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CloudAssessment;

public interface IFabricAdminService
{
    Task<FabricEnableResult> EnableServicePrincipalAccessAsync(string accessToken, string spnObjectId);
}

public class FabricEnableResult
{
    public bool Success { get; set; }
    public bool AlreadyEnabled { get; set; }
    public string? Error { get; set; }
}

public class FabricAdminService : IFabricAdminService
{
    private readonly ILogger<FabricAdminService> _log;

    public FabricAdminService(ILogger<FabricAdminService> log) => _log = log;

    public async Task<FabricEnableResult> EnableServicePrincipalAccessAsync(string accessToken, string spnObjectId)
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://api.fabric.microsoft.com") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Step 1: Check current tenant setting state
        var settingName = "ServicePrincipalAccess";
        bool alreadyEnabled = false;

        try
        {
            using var getResp = await http.GetAsync("/v1/admin/tenantsettings");
            if (getResp.IsSuccessStatusCode)
            {
                var json = await getResp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tenantsettings", out var settings) ||
                    doc.RootElement.TryGetProperty("tenantSettings", out settings))
                {
                    foreach (var setting in settings.EnumerateArray())
                    {
                        var name = setting.TryGetProperty("settingName", out var n) ? n.GetString() : null;
                        if (string.Equals(name, settingName, StringComparison.OrdinalIgnoreCase))
                        {
                            alreadyEnabled = setting.TryGetProperty("enabled", out var e) && e.GetBoolean();
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning("Failed to read Fabric tenant settings: {Error}", ex.Message);
        }

        // Step 2: Enable ServicePrincipalAccess if not already
        if (!alreadyEnabled)
        {
            try
            {
                var enableBody = JsonSerializer.Serialize(new
                {
                    enabled = true,
                    enabledSecurityGroups = Array.Empty<object>()
                });

                var patchUrl = $"/v1/admin/tenantsettings/{settingName}";
                using var patchReq = new HttpRequestMessage(HttpMethod.Patch, patchUrl)
                {
                    Content = new StringContent(enableBody, Encoding.UTF8, "application/json")
                };
                using var patchResp = await http.SendAsync(patchReq);

                if (!patchResp.IsSuccessStatusCode)
                {
                    var status = (int)patchResp.StatusCode;
                    var respBody = await patchResp.Content.ReadAsStringAsync();
                    _log.LogWarning("Fabric enable SPN access failed: HTTP {Status} — {Body}", status, respBody);

                    if (status == 403)
                        return new FabricEnableResult { Error = "Insufficient permissions. Must be Fabric Admin." };

                    return new FabricEnableResult { Error = $"Fabric API returned HTTP {status}" };
                }
            }
            catch (Exception ex)
            {
                return new FabricEnableResult { Error = $"Failed to enable: {ex.Message}" };
            }
        }

        // Step 3: Add SPN to the allowed security principals
        try
        {
            var addBody = JsonSerializer.Serialize(new
            {
                enabled = true,
                enabledSecurityGroups = new[]
                {
                    new { id = spnObjectId }
                }
            });

            var addUrl = $"/v1/admin/tenantsettings/{settingName}";
            using var addReq = new HttpRequestMessage(HttpMethod.Patch, addUrl)
            {
                Content = new StringContent(addBody, Encoding.UTF8, "application/json")
            };
            using var addResp = await http.SendAsync(addReq);

            if (!addResp.IsSuccessStatusCode && (int)addResp.StatusCode != 409)
            {
                var body = await addResp.Content.ReadAsStringAsync();
                _log.LogWarning("Fabric add SPN to allowlist failed: {Body}", body);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning("Failed to add SPN to Fabric allowlist: {Error}", ex.Message);
        }

        return new FabricEnableResult { Success = true, AlreadyEnabled = alreadyEnabled };
    }
}
