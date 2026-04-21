using System.Net;
using KryossApi.Data;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Agent;

public class ScheduleFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IScanScheduleService _schedule;
    private readonly ILogger<ScheduleFunction> _logger;

    public ScheduleFunction(KryossDbContext db, ICurrentUserService user,
        IScanScheduleService schedule, ILogger<ScheduleFunction> logger)
    {
        _db = db;
        _user = user;
        _schedule = schedule;
        _logger = logger;
    }

    [Function("Schedule")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/schedule")] HttpRequestData req)
    {
        var agentIdStr = req.Headers.TryGetValues("X-Agent-Id", out var vals)
            ? vals.FirstOrDefault() : null;

        if (string.IsNullOrEmpty(agentIdStr) || !Guid.TryParse(agentIdStr, out var agentId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "X-Agent-Id header required" });
            return bad;
        }

        var machine = await _db.Machines
            .Include(m => m.Organization)
            .FirstOrDefaultAsync(m => m.AgentId == agentId && m.IsActive);

        if (machine is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Machine not found" });
            return notFound;
        }

        machine.LastCheckinAt = DateTime.UtcNow;

        if (machine.ScanSlotOffsetSec is null)
        {
            machine.ScanSlotOffsetSec = await _schedule.AssignSlotAsync(
                machine.Id, machine.OrganizationId);
        }

        var todayStart = DateTime.UtcNow.Date;
        var lastRunToday = await _db.AssessmentRuns
            .Where(r => r.MachineId == machine.Id && r.StartedAt >= todayStart)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => (DateTime?)r.StartedAt)
            .FirstOrDefaultAsync();

        var org = machine.Organization;
        var result = _schedule.ComputeSchedule(
            machine.ScanSlotOffsetSec!.Value,
            org.ScanWindowStart,
            org.ScanWindowEnd,
            lastRunToday);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Schedule for {Machine}: runNow={RunNow} runAt={RunAt} slot={Slot}s",
            machine.Hostname, result.RunNow, result.RunAtUtc, result.SlotOffsetSec);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            result.RunNow,
            runAt = result.RunAtUtc.ToString("o"),
            result.WindowStart,
            result.WindowEnd,
            result.SlotOffsetSec
        });
        return resp;
    }
}
