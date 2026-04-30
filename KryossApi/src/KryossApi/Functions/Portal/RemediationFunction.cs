using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Portal;

[RequirePermission("admin:write")]
public class RemediationFunction
{
    private static readonly string[] AllowedRegistryPrefixes =
    {
        @"HKLM\SYSTEM\CurrentControlSet\Services\",
        @"HKLM\SOFTWARE\Policies\",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\",
    };

    private static readonly HashSet<string> OperationalActionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows_update",
    };

    private static readonly HashSet<string> AllowedWuParamKeys = new(StringComparer.Ordinal)
    {
        "mode", "reboot", "deadlineUtc",
    };

    private static readonly HashSet<string> AllowedWuModes = new(StringComparer.Ordinal)
    {
        "security_only", "all",
    };

    private static readonly HashSet<string> AllowedWuReboot = new(StringComparer.Ordinal)
    {
        "none", "if_required", "force",
    };

    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IActlogService _actlog;
    private readonly IRemediationLogService _remLog;
    private readonly ILogger<RemediationFunction> _logger;

    public RemediationFunction(KryossDbContext db, ICurrentUserService user, IActlogService actlog, IRemediationLogService remLog, ILogger<RemediationFunction> logger)
    {
        _db = db;
        _actlog = actlog;
        _user = user;
        _remLog = remLog;
        _logger = logger;
    }

    [Function("Remediation_CreateTask")]
    public async Task<HttpResponseData> CreateTask(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/remediation/tasks")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<CreateRemediationTaskRequest>();
        if (body is null || body.MachineId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "machineId required" });
            return bad;
        }

        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.Id == body.MachineId);
        if (machine is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        bool isOperational = !string.IsNullOrEmpty(body.ActionType)
            && OperationalActionTypes.Contains(body.ActionType);

        if (!isOperational && body.ControlDefId == 0)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "controlDefId required (or pass actionType for operational tasks)" });
            return bad;
        }

        // Operational task path (windows_update, etc.)
        if (isOperational)
        {
            var result = await CreateOperationalTask(req, body, machine);
            return result;
        }

        // Control-based task path (existing logic)
        var action = await _db.RemediationActions
            .FirstOrDefaultAsync(a => a.ControlDefId == body.ControlDefId && a.IsActive);
        if (action is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "No remediation action available for this control" });
            return notFound;
        }

        string? actionParamsJson = null;
        if (body.Params is null)
        {
            var actionParams = await _db.RemediationActionParams
                .Where(p => p.RemediationActionId == action.Id)
                .ToListAsync();
            if (actionParams.Count > 0)
                actionParamsJson = JsonSerializer.Serialize(actionParams.ToDictionary(p => p.ParamName, p => p.ParamValue));
        }

        if (action.ActionType.Equals("set_registry", StringComparison.OrdinalIgnoreCase))
        {
            var paramsJson = body.Params ?? actionParamsJson;
            if (!string.IsNullOrEmpty(paramsJson))
            {
                try
                {
                    var regParams = JsonSerializer.Deserialize<JsonElement>(paramsJson);
                    if (regParams.TryGetProperty("Path", out var pathProp) || regParams.TryGetProperty("path", out pathProp))
                    {
                        var regPath = pathProp.GetString() ?? "";
                        var normalized = regPath.Replace("HKEY_LOCAL_MACHINE\\", "HKLM\\")
                            .Replace("HKLM:", "HKLM").TrimStart('\\');
                        if (!AllowedRegistryPrefixes.Any(p => normalized.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                        {
                            try { await _actlog.LogAsync("WARN", "remediation", "path_rejected",
                                message: $"Registry path rejected: {regPath}"); } catch (Exception ex) { _logger.LogWarning(ex, "Actlog write failed for path_rejected"); }
                            var forbidden = req.CreateResponse(HttpStatusCode.BadRequest);
                            await forbidden.WriteAsJsonAsync(new { error = "Registry path not in allowed prefixes" });
                            return forbidden;
                        }
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to parse registry params for validation"); }
            }
        }

        if (action.ActionType is "enable_service" or "disable_service" or "restart_service"
            or "stop_service" or "set_service_startup")
        {
            var svcParams = body.Params ?? actionParamsJson;
            string? serviceName = null;
            if (!string.IsNullOrEmpty(svcParams))
            {
                try
                {
                    var svcJson = JsonSerializer.Deserialize<JsonElement>(svcParams);
                    if (svcJson.TryGetProperty("serviceName", out var sn) || svcJson.TryGetProperty("ServiceName", out sn))
                        serviceName = sn.GetString();
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to parse service params"); }
            }

            if (!string.IsNullOrEmpty(serviceName))
            {
                var protectedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM",
                    "SamSs", "lsass", "services", "wininit",
                    "CryptSvc", "TrustedInstaller", "WinDefend",
                    "EventLog", "Winmgmt", "BFE", "mpssvc",
                    "KryossAgent"
                };

                if (protectedServices.Contains(serviceName))
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.BadRequest);
                    await forbidden.WriteAsJsonAsync(new { error = $"Service '{serviceName}' is protected and cannot be modified" });
                    return forbidden;
                }

                if (action.ActionType == "disable_service")
                {
                    var isPriority = await _db.OrgPriorityServices
                        .AnyAsync(ps => ps.OrganizationId == machine.OrganizationId && ps.ServiceName == serviceName);
                    if (isPriority)
                    {
                        var forbidden = req.CreateResponse(HttpStatusCode.BadRequest);
                        await forbidden.WriteAsJsonAsync(new { error = $"Service '{serviceName}' is a priority service and cannot be disabled" });
                        return forbidden;
                    }
                }
            }
        }

        var task = new RemediationTask
        {
            OrganizationId = machine.OrganizationId,
            MachineId = body.MachineId,
            ControlDefId = body.ControlDefId,
            ActionId = action.Id,
            ActionType = action.ActionType,
            Params = body.Params ?? actionParamsJson,
            Status = "approved",
            ScheduledFor = body.ScheduledFor,
            CreatedBy = _user.UserId,
            ApprovedBy = _user.UserId,
            ApprovedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        _db.RemediationTasks.Add(task);
        await _db.SaveChangesAsync();

        try
        {
            await _remLog.LogAsync(task.Id, task.MachineId, task.OrganizationId,
                "created", task.ActionType, task.ControlDefId,
                paramsJson: task.Params, actorId: _user.UserId, ipAddress: _user.IpAddress);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Remediation log write failed"); }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            task.Id,
            task.MachineId,
            task.ControlDefId,
            task.ActionType,
            task.Params,
            task.Status,
            task.ScheduledFor,
            riskLevel = action.RiskLevel,
            description = action.Description,
        });
        return response;
    }

    private async Task<HttpResponseData> CreateOperationalTask(
        HttpRequestData req, CreateRemediationTaskRequest body, Data.Entities.Machine machine)
    {
        if (body.ActionType == "windows_update")
        {
            if (Environment.GetEnvironmentVariable("ENABLE_WINDOWS_UPDATE_REMEDIATION") != "true")
            {
                var off = req.CreateResponse(HttpStatusCode.BadRequest);
                await off.WriteAsJsonAsync(new { error = "windows_update remediation is not enabled" });
                return off;
            }

            var wuParams = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(body.Params))
            {
                try
                {
                    var raw = JsonSerializer.Deserialize<JsonElement>(body.Params);
                    if (raw.ValueKind != JsonValueKind.Object)
                    {
                        var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                        await bad.WriteAsJsonAsync(new { error = "params must be a JSON object" });
                        return bad;
                    }

                    foreach (var prop in raw.EnumerateObject())
                    {
                        if (!AllowedWuParamKeys.Contains(prop.Name))
                        {
                            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                            await bad.WriteAsJsonAsync(new { error = $"Unknown param key '{prop.Name}'. Allowed: mode, reboot, deadlineUtc" });
                            return bad;
                        }
                        wuParams[prop.Name] = prop.Value.GetString() ?? "";
                    }
                }
                catch (JsonException)
                {
                    var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                    await bad.WriteAsJsonAsync(new { error = "Invalid JSON in params" });
                    return bad;
                }
            }

            if (!wuParams.ContainsKey("mode"))
                wuParams["mode"] = "security_only";
            if (!wuParams.ContainsKey("reboot"))
                wuParams["reboot"] = "if_required";

            if (!AllowedWuModes.Contains(wuParams["mode"]))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = $"Invalid mode '{wuParams["mode"]}'. Allowed: security_only, all" });
                return bad;
            }

            if (!AllowedWuReboot.Contains(wuParams["reboot"]))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = $"Invalid reboot '{wuParams["reboot"]}'. Allowed: none, if_required, force" });
                return bad;
            }

            if (wuParams.TryGetValue("deadlineUtc", out var deadline))
            {
                if (!DateTime.TryParse(deadline, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                    || dt.Kind != DateTimeKind.Utc)
                {
                    var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                    await bad.WriteAsJsonAsync(new { error = "deadlineUtc must be ISO 8601 UTC (e.g. 2026-05-01T03:00:00Z)" });
                    return bad;
                }
            }

            var finalParams = JsonSerializer.Serialize(wuParams);

            var task = new RemediationTask
            {
                OrganizationId = machine.OrganizationId,
                MachineId = body.MachineId,
                ActionType = "windows_update",
                Params = finalParams,
                Status = "approved",
                ScheduledFor = body.ScheduledFor,
                CreatedBy = _user.UserId,
                ApprovedBy = _user.UserId,
                ApprovedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
            _db.RemediationTasks.Add(task);
            await _db.SaveChangesAsync();

            try
            {
                await _remLog.LogAsync(task.Id, task.MachineId, task.OrganizationId,
                    "created", task.ActionType, controlDefId: null,
                    paramsJson: finalParams, actorId: _user.UserId, ipAddress: _user.IpAddress);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Remediation log write failed"); }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                task.Id, task.MachineId, task.ActionType,
                task.Params, task.Status, task.ScheduledFor,
                riskLevel = "high",
            });
            return response;
        }

        var unsupported = req.CreateResponse(HttpStatusCode.BadRequest);
        await unsupported.WriteAsJsonAsync(new { error = $"Unsupported operational actionType '{body.ActionType}'" });
        return unsupported;
    }

    [Function("Remediation_ListTasks")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> ListTasks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/remediation/tasks")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var machineIdStr = query["machineId"];
        var orgIdStr = query["organizationId"];

        IQueryable<RemediationTask> q = _db.RemediationTasks;

        if (Guid.TryParse(machineIdStr, out var machineId))
            q = q.Where(t => t.MachineId == machineId);
        else if (Guid.TryParse(orgIdStr, out var orgId))
            q = q.Where(t => t.OrganizationId == orgId);
        else if (_user.OrganizationId.HasValue)
            q = q.Where(t => t.OrganizationId == _user.OrganizationId.Value);
        else
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var tasks = await q
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .Select(t => new
            {
                t.Id,
                t.MachineId,
                t.ControlDefId,
                controlId = t.ControlDef != null ? t.ControlDef.ControlId : (string?)null,
                controlName = t.ControlDef != null ? t.ControlDef.Name : (string?)null,
                t.ActionType,
                t.Status,
                t.PreviousValue,
                t.NewValue,
                t.ErrorMessage,
                t.ScheduledFor,
                t.ApprovedAt,
                t.ExecutedAt,
                t.CompletedAt,
                t.CreatedAt,
            })
            .ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { tasks });
        return response;
    }

    [Function("Remediation_Rollback")]
    public async Task<HttpResponseData> Rollback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/remediation/tasks/{taskId}/rollback")] HttpRequestData req,
        long taskId)
    {
        var original = await _db.RemediationTasks
            .Include(t => t.Action)
            .FirstOrDefaultAsync(t => t.Id == taskId);
        if (original is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        if (original.Status != "completed" || string.IsNullOrEmpty(original.PreviousValue))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Can only rollback completed tasks with a previous value" });
            return bad;
        }

        if (OperationalActionTypes.Contains(original.ActionType))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Operational tasks (windows_update) cannot be rolled back" });
            return bad;
        }

        var rollbackTask = new RemediationTask
        {
            OrganizationId = original.OrganizationId,
            MachineId = original.MachineId,
            ControlDefId = original.ControlDefId,
            ActionId = original.ActionId,
            ActionType = original.ActionType,
            Params = original.PreviousValue,
            Status = "approved",
            CreatedBy = _user.UserId,
            ApprovedBy = _user.UserId,
            ApprovedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        _db.RemediationTasks.Add(rollbackTask);

        original.Status = "rolled_back";
        await _db.SaveChangesAsync();

        try
        {
            await _remLog.LogAsync(rollbackTask.Id, rollbackTask.MachineId, rollbackTask.OrganizationId,
                "created", rollbackTask.ActionType, rollbackTask.ControlDefId,
                paramsJson: rollbackTask.Params, actorId: _user.UserId, ipAddress: _user.IpAddress);
            await _remLog.LogAsync(original.Id, original.MachineId, original.OrganizationId,
                "rolled_back", original.ActionType, original.ControlDefId,
                actorId: _user.UserId, ipAddress: _user.IpAddress);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Remediation log write failed"); }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { rollbackTaskId = rollbackTask.Id, originalTaskId = taskId });
        return response;
    }

    [Function("Remediation_CancelTask")]
    [RequirePermission("machines:write")]
    public async Task<HttpResponseData> CancelTask(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/remediation/tasks/{taskId}/cancel")] HttpRequestData req,
        long taskId)
    {
        var task = await _db.RemediationTasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        if (task.Status is not ("approved" or "pending"))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Only pending/approved tasks can be cancelled" });
            return bad;
        }

        task.Status = "cancelled";
        task.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { task.Id, task.Status });
        return response;
    }

    [Function("Remediation_History")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> History(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/remediation/history")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        Guid? orgId = Guid.TryParse(query["organizationId"], out var parsed) ? parsed : _user.OrganizationId;
        if (orgId is null)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var history = await _db.RemediationTasks
            .Where(t => t.OrganizationId == orgId.Value)
            .OrderByDescending(t => t.CreatedAt)
            .Take(200)
            .Select(t => new
            {
                t.Id,
                t.MachineId,
                hostname = t.Machine.Hostname,
                controlId = t.ControlDef != null ? t.ControlDef.ControlId : (string?)null,
                controlName = t.ControlDef != null ? t.ControlDef.Name : (string?)null,
                t.ActionType,
                t.Status,
                t.CreatedBy,
                t.ApprovedBy,
                t.ScheduledFor,
                t.ApprovedAt,
                t.ExecutedAt,
                t.CompletedAt,
                t.ErrorMessage,
                t.CreatedAt,
            })
            .ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { history });
        return response;
    }

    [Function("Remediation_Reschedule")]
    [RequirePermission("machines:write")]
    public async Task<HttpResponseData> Reschedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/remediation/tasks/{taskId}/reschedule")] HttpRequestData req,
        long taskId)
    {
        var task = await _db.RemediationTasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        if (task.Status is not ("approved" or "pending"))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Only pending/approved tasks can be rescheduled" });
            return bad;
        }

        var body = await req.ReadFromJsonAsync<RescheduleRequest>();
        task.ScheduledFor = body?.ScheduledFor;
        await _db.SaveChangesAsync();

        try
        {
            await _remLog.LogAsync(task.Id, task.MachineId, task.OrganizationId,
                "rescheduled", task.ActionType, task.ControlDefId,
                actorId: _user.UserId, ipAddress: _user.IpAddress);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Remediation log write failed"); }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { task.Id, task.ScheduledFor, task.Status });
        return response;
    }

    [Function("Remediation_Catalog")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> GetCatalog(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/remediation/catalog")] HttpRequestData req)
    {
        var actions = await _db.RemediationActions
            .Where(a => a.IsActive)
            .Select(a => new
            {
                a.Id,
                a.ControlDefId,
                controlId = a.ControlDef.ControlId,
                controlName = a.ControlDef.Name,
                a.ActionType,
                a.RiskLevel,
                a.Description,
            })
            .ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { actions });
        return response;
    }
}

internal class CreateRemediationTaskRequest
{
    public Guid MachineId { get; set; }
    public int ControlDefId { get; set; }
    public string? ActionType { get; set; }
    public string? Params { get; set; }
    public DateTime? ScheduledFor { get; set; }
}

internal class RescheduleRequest
{
    public DateTime? ScheduledFor { get; set; }
}
