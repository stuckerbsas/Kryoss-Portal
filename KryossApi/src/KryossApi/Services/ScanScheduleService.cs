using KryossApi.Data;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface IScanScheduleService
{
    Task<int> AssignSlotAsync(Guid machineId, Guid organizationId);
    ScheduleResult ComputeSchedule(int slotOffsetSec, TimeSpan windowStart, TimeSpan windowEnd, DateTime? lastRunToday);
}

public record ScheduleResult(bool RunNow, DateTime RunAtUtc, string WindowStart, string WindowEnd, int SlotOffsetSec);

public class ScanScheduleService : IScanScheduleService
{
    private readonly KryossDbContext _db;
    private const int MinSpacingSec = 10;

    public ScanScheduleService(KryossDbContext db) => _db = db;

    public async Task<int> AssignSlotAsync(Guid machineId, Guid organizationId)
    {
        var org = await _db.Organizations.FindAsync(organizationId);
        if (org is null) return 0;

        var windowDuration = (int)(org.ScanWindowEnd - org.ScanWindowStart).TotalSeconds;
        if (windowDuration <= 0)
            windowDuration = (int)(org.ScanWindowEnd.Add(TimeSpan.FromHours(24)) - org.ScanWindowStart).TotalSeconds;

        var existingOffsets = await _db.Machines
            .Where(m => m.OrganizationId == organizationId
                && m.IsActive
                && m.ScanSlotOffsetSec != null
                && m.Id != machineId)
            .Select(m => m.ScanSlotOffsetSec!.Value)
            .OrderBy(o => o)
            .ToListAsync();

        var offset = FindGap(existingOffsets, windowDuration);

        var machine = await _db.Machines.FindAsync(machineId);
        if (machine is not null)
        {
            machine.ScanSlotOffsetSec = offset;
            await _db.SaveChangesAsync();
        }

        return offset;
    }

    private static int FindGap(List<int> sorted, int windowDuration)
    {
        if (sorted.Count == 0)
            return 0;

        if (sorted[0] >= MinSpacingSec)
            return 0;

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var gap = sorted[i + 1] - sorted[i];
            if (gap >= MinSpacingSec * 2)
                return sorted[i] + (gap / 2);
        }

        var afterLast = sorted[^1] + MinSpacingSec;
        if (afterLast < windowDuration)
            return afterLast;

        return windowDuration - 1;
    }

    public ScheduleResult ComputeSchedule(int slotOffsetSec, TimeSpan windowStart, TimeSpan windowEnd, DateTime? lastRunToday)
    {
        var now = DateTime.UtcNow;
        var todaySlot = now.Date.Add(windowStart).AddSeconds(slotOffsetSec);

        if (windowEnd < windowStart && now.TimeOfDay < windowEnd)
            todaySlot = todaySlot.AddDays(-1);

        bool ranToday = lastRunToday.HasValue && lastRunToday.Value.Date == now.Date;

        DateTime runAt;
        bool runNow;

        if (ranToday)
        {
            runAt = todaySlot.AddDays(1);
            runNow = false;
        }
        else if (now > todaySlot)
        {
            runAt = todaySlot;
            runNow = true;
        }
        else
        {
            runAt = todaySlot;
            runNow = false;
        }

        return new ScheduleResult(
            runNow,
            runAt,
            windowStart.ToString(@"hh\:mm"),
            windowEnd.ToString(@"hh\:mm"),
            slotOffsetSec);
    }
}
