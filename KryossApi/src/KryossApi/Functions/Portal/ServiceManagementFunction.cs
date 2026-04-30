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

public class ServiceManagementFunction
{
    private static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM",
        "SamSs", "lsass", "services", "wininit",
        "CryptSvc", "TrustedInstaller", "WinDefend",
        "EventLog", "Winmgmt", "BFE", "mpssvc",
        "KryossAgent",
    };

    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IRemediationLogService _remLog;
    private readonly ILogger<ServiceManagementFunction> _logger;

    public ServiceManagementFunction(KryossDbContext db, ICurrentUserService user, IRemediationLogService remLog, ILogger<ServiceManagementFunction> logger)
    {
        _db = db;
        _user = user;
        _remLog = remLog;
        _logger = logger;
    }

    [Function("Services_List")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> ListServices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/machines/{machineId}/services")] HttpRequestData req,
        string machineId)
    {
        if (!Guid.TryParse(machineId, out var mid))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var services = await _db.MachineServices
            .Where(s => s.MachineId == mid)
            .OrderBy(s => s.DisplayName)
            .Select(s => new
            {
                s.Name,
                s.DisplayName,
                s.Status,
                s.StartupType,
                s.UpdatedAt,
                isProtected = ProtectedServices.Contains(s.Name),
            })
            .ToListAsync();

        var orgId = await _db.Machines.Where(m => m.Id == mid).Select(m => m.OrganizationId).FirstOrDefaultAsync();
        var prioritySet = (await _db.OrgPriorityServices
            .Where(ps => ps.OrganizationId == orgId)
            .Select(ps => ps.ServiceName)
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = services.Select(s => new
        {
            s.Name, s.DisplayName, s.Status, s.StartupType, s.UpdatedAt, s.isProtected,
            isPriority = prioritySet.Contains(s.Name),
        });

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { total = services.Count, items = result });
        return ok;
    }

    [Function("Services_Action")]
    [RequirePermission("admin:write")]
    public async Task<HttpResponseData> ServiceAction(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/machines/{machineId}/services/{serviceName}/action")] HttpRequestData req,
        string machineId, string serviceName)
    {
        if (!Guid.TryParse(machineId, out var mid))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var body = await req.ReadFromJsonAsync<ServiceActionRequest>();
        if (body is null || string.IsNullOrEmpty(body.Action))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "action required (start, stop, restart)" });
            return bad;
        }

        if (ProtectedServices.Contains(serviceName))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.BadRequest);
            await forbidden.WriteAsJsonAsync(new { error = $"Service '{serviceName}' is protected" });
            return forbidden;
        }

        var actionType = body.Action.ToLowerInvariant() switch
        {
            "start" => "enable_service",
            "stop" => "stop_service",
            "restart" => "restart_service",
            _ => null,
        };
        if (actionType is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid action. Use: start, stop, restart" });
            return bad;
        }

        if (actionType == "stop_service")
        {
            var orgId = await _db.Machines.Where(m => m.Id == mid).Select(m => m.OrganizationId).FirstOrDefaultAsync();
            var isPriority = await _db.OrgPriorityServices
                .AnyAsync(ps => ps.OrganizationId == orgId && ps.ServiceName == serviceName);
            if (isPriority)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.BadRequest);
                await forbidden.WriteAsJsonAsync(new { error = $"Service '{serviceName}' is a priority service and cannot be stopped" });
                return forbidden;
            }
        }

        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.Id == mid);
        if (machine is null) return req.CreateResponse(HttpStatusCode.NotFound);

        var task = new RemediationTask
        {
            OrganizationId = machine.OrganizationId,
            MachineId = mid,
            ControlDefId = 0,
            ActionId = 0,
            ActionType = actionType,
            Params = JsonSerializer.Serialize(new { serviceName }),
            Status = "approved",
            CreatedBy = _user.UserId,
            ApprovedBy = _user.UserId,
            ApprovedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        _db.RemediationTasks.Add(task);
        await _db.SaveChangesAsync();

        try
        {
            await _remLog.LogAsync(task.Id, mid, machine.OrganizationId,
                "service_action", actionType, serviceName: serviceName,
                paramsJson: task.Params, actorId: _user.UserId, ipAddress: _user.IpAddress);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Service action log write failed"); }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { taskId = task.Id, actionType, serviceName, status = "approved" });
        return ok;
    }

    [Function("Services_PriorityToggle")]
    [RequirePermission("admin:write")]
    public async Task<HttpResponseData> TogglePriority(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/machines/{machineId}/priority-services")] HttpRequestData req,
        string machineId)
    {
        if (!Guid.TryParse(machineId, out var mid))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var body = await req.ReadFromJsonAsync<PriorityToggleRequest>();
        if (body is null || string.IsNullOrEmpty(body.ServiceName))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "serviceName required" });
            return bad;
        }

        var orgId = await _db.Machines.Where(m => m.Id == mid).Select(m => m.OrganizationId).FirstOrDefaultAsync();
        if (orgId == Guid.Empty) return req.CreateResponse(HttpStatusCode.NotFound);

        if (body.Enable)
        {
            var exists = await _db.OrgPriorityServices
                .AnyAsync(ps => ps.OrganizationId == orgId && ps.ServiceName == body.ServiceName);
            if (!exists)
            {
                _db.OrgPriorityServices.Add(new Data.Entities.OrgPriorityService
                {
                    OrganizationId = orgId,
                    ServiceName = body.ServiceName
                });
            }
        }
        else
        {
            var existing = await _db.OrgPriorityServices
                .FirstOrDefaultAsync(ps => ps.OrganizationId == orgId && ps.ServiceName == body.ServiceName);
            if (existing is not null)
                _db.OrgPriorityServices.Remove(existing);
        }

        await _db.SaveChangesAsync();

        var current = await _db.OrgPriorityServices
            .Where(ps => ps.OrganizationId == orgId)
            .Select(ps => ps.ServiceName)
            .ToListAsync();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { priorityServices = current });
        return ok;
    }
}

internal class ServiceActionRequest
{
    public string? Action { get; set; }
}

internal class PriorityToggleRequest
{
    public string? ServiceName { get; set; }
    public bool Enable { get; set; }
}
