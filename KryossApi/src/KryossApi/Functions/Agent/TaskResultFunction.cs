using System.Net;
using KryossApi.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Agent;

public class TaskResultFunction
{
    private readonly KryossDbContext _db;

    public TaskResultFunction(KryossDbContext db) => _db = db;

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

        var body = await req.ReadFromJsonAsync<TaskResultRequest>();
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

        task.Status = body.Success ? "completed" : "failed";
        task.PreviousValue = body.PreviousValue;
        task.NewValue = body.NewValue;
        task.ErrorMessage = body.ErrorMessage;
        task.ExecutedAt = body.ExecutedAt ?? DateTime.UtcNow;
        task.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { ack = true });
        return ok;
    }
}

public class TaskResultRequest
{
    public long TaskId { get; set; }
    public bool Success { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? ExecutedAt { get; set; }
}
