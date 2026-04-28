using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

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

    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IActlogService _actlog;
    private readonly IRemediationLogService _remLog;

    public RemediationFunction(KryossDbContext db, ICurrentUserService user, IActlogService actlog, IRemediationLogService remLog)
    {
        _db = db;
        _actlog = actlog;
        _user = user;
        _remLog = remLog;
    }

    [Function("Remediation_CreateTask")]
    public async Task<HttpResponseData> CreateTask(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/remediation/tasks")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<CreateRemediationTaskRequest>();
        if (body is null || body.MachineId == Guid.Empty || body.ControlDefId == 0)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "machineId and controlDefId required" });
            return bad;
        }

        var action = await _db.RemediationActions
            .FirstOrDefaultAsync(a => a.ControlDefId == body.ControlDefId && a.IsActive);
        if (action is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "No remediation action available for this control" });
            return notFound;
        }

        // Reconstruct params template from normalized table
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
                                message: $"Registry path rejected: {regPath}"); } catch { }
                            var forbidden = req.CreateResponse(HttpStatusCode.BadRequest);
                            await forbidden.WriteAsJsonAsync(new { error = "Registry path not in allowed prefixes" });
                            return forbidden;
                        }
                    }
                }
                catch { }
            }
        }

        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.Id == body.MachineId);
        if (machine is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

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
                catch { }
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
        catch { }

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
                controlId = t.ControlDef.ControlId,
                controlName = t.ControlDef.Name,
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
        catch { }

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
                controlId = t.ControlDef.ControlId,
                controlName = t.ControlDef.Name,
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
        catch { }

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
    public string? Params { get; set; }
    public DateTime? ScheduledFor { get; set; }
}

internal class RescheduleRequest
{
    public DateTime? ScheduledFor { get; set; }
}
