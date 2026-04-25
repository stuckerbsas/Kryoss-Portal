using System.Net;
using KryossApi.Data;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Agent;

public class HeartbeatFunction
{
    private readonly KryossDbContext _db;
    private readonly ILogger<HeartbeatFunction> _logger;

    public HeartbeatFunction(KryossDbContext db, ILogger<HeartbeatFunction> logger)
    {
        _db = db;
        _logger = logger;
    }

    [Function("Heartbeat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/heartbeat")] HttpRequestData req)
    {
        var agentIdHeader = req.Headers.TryGetValues("X-Agent-Id", out var vals)
            ? vals.FirstOrDefault() : null;
        if (!Guid.TryParse(agentIdHeader, out var agentId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "X-Agent-Id header required" });
            return bad;
        }

        var body = await req.ReadFromJsonAsync<HeartbeatRequest>();

        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.AgentId == agentId);
        if (machine is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "unknown agent" });
            return notFound;
        }

        machine.LastHeartbeatAt = DateTime.UtcNow;
        machine.LastSeenAt = DateTime.UtcNow;
        machine.AgentMode = body?.Mode;
        machine.AgentUptimeSeconds = body?.UptimeSeconds;
        if (!string.IsNullOrEmpty(body?.Version))
            machine.AgentVersion = body.Version;

        // Key rotation: check ForceKeyRotation flag set by middleware (machine_secret reauth path)
        string? newSessionKey = null;
        DateTime? newSessionKeyExpiresAt = null;

        var forceRotation = req.FunctionContext.Items.ContainsKey("ForceKeyRotation");

        var keyService = req.FunctionContext.InstanceServices.GetRequiredService<IKeyRotationService>();
        string? newMachineSecret = null;

        if (machine.AuthVersion == 1 && machine.MachineSecret is null)
        {
            var initial = keyService.GenerateInitialKeys();
            machine.MachineSecret = initial.machineSecret;
            machine.SessionKey = initial.sessionKey;
            machine.SessionKeyExpiresAt = initial.expiresAt;
            machine.AuthVersion = 2;
            machine.KeyRotatedAt = DateTime.UtcNow;

            newMachineSecret = initial.machineSecret;
            newSessionKey = initial.sessionKey;
            newSessionKeyExpiresAt = initial.expiresAt;

            _logger.LogInformation("Auto-upgraded {Host} to auth_version=2", machine.Hostname);
        }
        else if (machine.AuthVersion >= 2 && machine.SessionKey is not null)
        {
            var rotation = keyService.TryRotate(
                machine.SessionKey, machine.SessionKeyExpiresAt,
                out var prevKey, out var prevExpiry);

            if (rotation is not null || forceRotation)
            {
                if (rotation is null && forceRotation)
                {
                    var fresh = keyService.GenerateInitialKeys();
                    prevKey = machine.SessionKey;
                    prevExpiry = DateTime.UtcNow.AddHours(24);
                    rotation = (fresh.sessionKey, fresh.expiresAt);
                }

                machine.PrevSessionKey = prevKey;
                machine.PrevKeyExpiresAt = prevExpiry;
                machine.SessionKey = rotation!.Value.newSessionKey;
                machine.SessionKeyExpiresAt = rotation.Value.expiresAt;
                machine.KeyRotatedAt = DateTime.UtcNow;

                newSessionKey = rotation.Value.newSessionKey;
                newSessionKeyExpiresAt = rotation.Value.expiresAt;
            }
        }

        await _db.SaveChangesAsync();

        var pendingTasks = await _db.RemediationTasks
            .Where(t => t.MachineId == machine.Id && t.Status == "approved")
            .Select(t => new
            {
                t.Id,
                t.ActionType,
                t.Params,
                t.ControlDefId,
                controlId = t.ControlDef.ControlId,
            })
            .ToListAsync();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { ack = true, pendingTasks, newMachineSecret, newSessionKey, newSessionKeyExpiresAt });
        return ok;
    }
}

public class HeartbeatRequest
{
    public Guid AgentId { get; set; }
    public string? Version { get; set; }
    public long UptimeSeconds { get; set; }
    public DateTime? LastScanAt { get; set; }
    public string? Mode { get; set; }
}
