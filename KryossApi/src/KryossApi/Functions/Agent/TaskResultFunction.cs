using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Agent;

public class TaskResultFunction
{
    private readonly KryossDbContext _db;
    private readonly IRemediationLogService _remLog;
    private readonly ILogger<TaskResultFunction> _logger;

    public TaskResultFunction(KryossDbContext db, IRemediationLogService remLog, ILogger<TaskResultFunction> logger)
    {
        _db = db;
        _remLog = remLog;
        _logger = logger;
    }

    [Function("TaskResult")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/task-result")] HttpRequestData req)
    {
        var agentIdHeader = req.Headers.TryGetValues("X-Agent-Id", out var vals)
            ? vals.FirstOrDefault() : null;
        if (!Guid.TryParse(agentIdHeader, out var agentId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "X-Agent-Id header required" });
            return bad;
        }

        TaskResultRequest? body = null;
        var context = req.FunctionContext;
        if (context.Items.TryGetValue("RequestBodyBytes", out var rawObj) && rawObj is byte[] rawBytes && rawBytes.Length > 0)
            body = JsonSerializer.Deserialize<TaskResultRequest>(rawBytes);
        else if (req.Body.CanSeek)
        {
            req.Body.Position = 0;
            body = await req.ReadFromJsonAsync<TaskResultRequest>();
        }
        if (body is null || body.TaskId == 0)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "taskId required" });
            return bad;
        }

        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.AgentId == agentId);
        if (machine is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        var task = await _db.RemediationTasks
            .FirstOrDefaultAsync(t => t.Id == body.TaskId && t.MachineId == machine.Id);
        if (task is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        if (task.ActionType == "windows_update"
            && Environment.GetEnvironmentVariable("ENABLE_WINDOWS_UPDATE_REMEDIATION") != "true")
        {
            _logger.LogWarning("TaskResult for windows_update task {TaskId} rejected — kill switch off", task.Id);
            task.Status = "rejected";
            task.ErrorMessage = "windows_update disabled server-side";
            task.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            var rejected = req.CreateResponse(HttpStatusCode.OK);
            await rejected.WriteAsJsonAsync(new { ack = true, rejected = true });
            return rejected;
        }

        task.Status = body.Success ? "completed" : "failed";
        task.PreviousValue = body.PreviousValue;
        task.NewValue = body.NewValue;
        task.ErrorMessage = body.ErrorMessage;
        task.ExecutedAt = body.ExecutedAt ?? DateTime.UtcNow;
        task.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        try
        {
            await _remLog.LogAsync(task.Id, task.MachineId, task.OrganizationId,
                body.Success ? "completed" : "failed", task.ActionType, task.ControlDefId,
                previousValue: body.PreviousValue, newValue: body.NewValue,
                errorMessage: body.ErrorMessage, signatureHash: task.SignatureHash);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to write remediation log for task {TaskId}", task.Id); }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { ack = true });
        return ok;
    }
}

public class TaskResultRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("taskId")]
    public long TaskId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("success")]
    public bool Success { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("previousValue")]
    public string? PreviousValue { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("newValue")]
    public string? NewValue { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("executedAt")]
    public DateTime? ExecutedAt { get; set; }
}
