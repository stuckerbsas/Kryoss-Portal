using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Agent;

public class ServiceInventoryFunction
{
    private readonly KryossDbContext _db;

    public ServiceInventoryFunction(KryossDbContext db) => _db = db;

    [Function("ServiceInventory")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/services")] HttpRequestData req)
    {
        var agentIdHeader = req.Headers.TryGetValues("X-Agent-Id", out var vals)
            ? vals.FirstOrDefault() : null;
        if (!Guid.TryParse(agentIdHeader, out var agentId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "X-Agent-Id header required" });
            return bad;
        }

        var machine = await _db.Machines.AsNoTracking()
            .Where(m => m.AgentId == agentId)
            .Select(m => new { m.Id })
            .FirstOrDefaultAsync();
        if (machine is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        List<ServiceEntry>? services = null;
        var context = req.FunctionContext;
        if (context.Items.TryGetValue("RequestBodyBytes", out var rawObj) && rawObj is byte[] rawBytes && rawBytes.Length > 0)
            services = JsonSerializer.Deserialize<List<ServiceEntry>>(rawBytes);
        else if (req.Body.CanSeek)
        {
            req.Body.Position = 0;
            services = await req.ReadFromJsonAsync<List<ServiceEntry>>();
        }

        if (services is null or { Count: 0 })
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "empty service list" });
            return bad;
        }

        var existing = await _db.MachineServices
            .Where(s => s.MachineId == machine.Id)
            .ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        foreach (var svc in services)
        {
            if (string.IsNullOrEmpty(svc.Name)) continue;

            if (existing.TryGetValue(svc.Name, out var row))
            {
                row.DisplayName = svc.DisplayName;
                row.Status = svc.Status ?? "Unknown";
                row.StartupType = svc.StartupType ?? "Unknown";
                row.UpdatedAt = now;
            }
            else
            {
                _db.MachineServices.Add(new MachineService
                {
                    MachineId = machine.Id,
                    Name = svc.Name,
                    DisplayName = svc.DisplayName,
                    Status = svc.Status ?? "Unknown",
                    StartupType = svc.StartupType ?? "Unknown",
                    UpdatedAt = now,
                });
            }
        }

        var currentNames = new HashSet<string>(
            services.Where(s => !string.IsNullOrEmpty(s.Name)).Select(s => s.Name!),
            StringComparer.OrdinalIgnoreCase);
        foreach (var kv in existing)
        {
            if (!currentNames.Contains(kv.Key))
                _db.MachineServices.Remove(kv.Value);
        }

        await _db.SaveChangesAsync();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { ack = true, count = services.Count });
        return ok;
    }
}

internal class ServiceEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string? Status { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("startupType")]
    public string? StartupType { get; set; }
}
