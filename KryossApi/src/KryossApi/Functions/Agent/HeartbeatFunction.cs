using System.Net;
using System.Text;
using System.Text.Json;
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

        HeartbeatRequest? body = null;
        var context = req.FunctionContext;
        if (context.Items.TryGetValue("RequestBodyBytes", out var rawObj) && rawObj is byte[] rawBytes && rawBytes.Length > 0)
            body = JsonSerializer.Deserialize<HeartbeatRequest>(rawBytes);
        else if (req.Body.CanSeek)
        {
            req.Body.Position = 0;
            body = await req.ReadFromJsonAsync<HeartbeatRequest>();
        }

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

        // On-demand scan: check if portal requested a forced scan
        bool forceScan = machine.ForceScanRequestedAt.HasValue;
        if (forceScan)
        {
            machine.ForceScanRequestedAt = null;
            machine.ForceScanRequestedBy = null;
        }

        // Persist agent errors to actlog
        if (body?.Errors is { Count: > 0 })
        {
            var actlogService = req.FunctionContext.InstanceServices.GetRequiredService<ActlogService>();
            foreach (var err in body.Errors)
            {
                await actlogService.LogAsync(
                    severity: err.IsTimeout ? "WARN" : "ERROR",
                    module: "agent",
                    action: err.Phase,
                    entityType: "machine",
                    entityId: machine.Id.ToString(),
                    message: err.Message);
            }

            var latest = body.Errors.OrderByDescending(e => e.Timestamp).First();
            machine.LastErrorAt = latest.Timestamp;
            machine.LastErrorPhase = latest.Phase;
            machine.LastErrorMsg = latest.Message.Length > 500 ? latest.Message[..500] : latest.Message;
        }

        // Save loop status → normalized table
        if (body?.LoopStatus is { Count: > 0 })
        {
            var existing = await _db.MachineLoopStatuses
                .Where(ls => ls.MachineId == machine.Id)
                .ToDictionaryAsync(ls => ls.LoopName);

            foreach (var ls in body.LoopStatus)
            {
                if (existing.TryGetValue(ls.Key, out var row))
                {
                    row.State = ls.Value.State ?? "idle";
                    row.LastRunAt = ls.Value.LastRunAt;
                    row.DurationMs = ls.Value.LastDurationMs;
                    row.LastError = ls.Value.LastError;
                }
                else
                {
                    _db.MachineLoopStatuses.Add(new Data.Entities.MachineLoopStatus
                    {
                        MachineId = machine.Id,
                        LoopName = ls.Key,
                        State = ls.Value.State ?? "idle",
                        LastRunAt = ls.Value.LastRunAt,
                        DurationMs = ls.Value.LastDurationMs,
                        LastError = ls.Value.LastError,
                    });
                }
            }
        }

        await _db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var pendingTasks = await _db.RemediationTasks
            .Where(t => t.MachineId == machine.Id && t.Status == "approved"
                && (t.ScheduledFor == null || t.ScheduledFor <= now))
            .Select(t => new
            {
                t.Id,
                t.ActionType,
                t.Params,
                t.ControlDefId,
                controlId = t.ControlDef.ControlId,
                t.ApprovedAt,
            })
            .ToListAsync();

        List<object>? signedTasks = null;
        if (pendingTasks.Count > 0 && !string.IsNullOrEmpty(machine.MachineSecret))
        {
            var remLogService = req.FunctionContext.InstanceServices.GetRequiredService<IRemediationLogService>();
            signedTasks = new List<object>();
            foreach (var t in pendingTasks)
            {
                var signingString = $"{t.Id}|{t.ActionType}|{t.Params}|{t.ApprovedAt:O}";
                var keyBytes = Encoding.UTF8.GetBytes(machine.MachineSecret);
                var signature = Convert.ToHexString(
                    System.Security.Cryptography.HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(signingString))
                ).ToLowerInvariant();

                signedTasks.Add(new
                {
                    t.Id, t.ActionType, t.Params, t.ControlDefId, t.controlId,
                    approvedAt = t.ApprovedAt, signature,
                });

                try
                {
                    await remLogService.LogAsync(t.Id, machine.Id, machine.OrganizationId,
                        "dispatched", t.ActionType, t.ControlDefId, paramsJson: t.Params,
                        signatureHash: signature);
                }
                catch { }
            }
        }
        else if (pendingTasks.Count > 0)
        {
            signedTasks = pendingTasks.Select(t => (object)new
            {
                t.Id, t.ActionType, t.Params, t.ControlDefId, t.controlId,
            }).ToList();
        }

        // Persist service_heal events from agent error queue to remediation_log
        if (body?.Errors is { Count: > 0 })
        {
            var remLogService = req.FunctionContext.InstanceServices.GetRequiredService<IRemediationLogService>();
            foreach (var err in body.Errors.Where(e => e.Phase == "service_heal"))
            {
                try
                {
                    await remLogService.LogAsync(0, machine.Id, machine.OrganizationId,
                        "service_heal", "heal_service", serviceName: err.Target,
                        errorMessage: err.Message);
                }
                catch { }
            }
        }

        var priorityServices = await _db.OrgPriorityServices
            .Where(ps => ps.OrganizationId == machine.OrganizationId)
            .Select(ps => ps.ServiceName)
            .ToListAsync();
        List<string>? prioritySvcList = priorityServices.Count > 0 ? priorityServices : null;

        var ok = req.CreateResponse(HttpStatusCode.OK);
        var config = new
        {
            complianceIntervalHours = machine.ConfigComplianceIntervalHours,
            snmpIntervalMinutes = machine.ConfigSnmpIntervalMinutes,
            enableNetworkScan = machine.ConfigEnableNetworkScan,
            networkScanIntervalHours = machine.ConfigNetworkScanIntervalHours,
            enablePassiveDiscovery = machine.ConfigEnablePassiveDiscovery,
            priorityServices = prioritySvcList,
        };

        await ok.WriteAsJsonAsync(new
        {
            ack = true,
            pendingTasks = signedTasks,
            newMachineSecret, newSessionKey, newSessionKeyExpiresAt,
            config, forceScan,
        });
        return ok;
    }
}

public class HeartbeatRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("agentId")]
    public Guid AgentId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string? Version { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("uptimeSeconds")]
    public long UptimeSeconds { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("lastScanAt")]
    public DateTime? LastScanAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("errors")]
    public List<AgentErrorEntry>? Errors { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("loopStatus")]
    public Dictionary<string, LoopStatusEntry>? LoopStatus { get; set; }
}

public class AgentErrorEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("phase")]
    public string Phase { get; set; } = null!;

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string Message { get; set; } = null!;

    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("target")]
    public string? Target { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("isTimeout")]
    public bool IsTimeout { get; set; }
}

public class LoopStatusEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("lastRunAt")]
    public DateTime? LastRunAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("lastDurationMs")]
    public int? LastDurationMs { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("lastError")]
    public string? LastError { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("state")]
    public string State { get; set; } = "idle";
}
