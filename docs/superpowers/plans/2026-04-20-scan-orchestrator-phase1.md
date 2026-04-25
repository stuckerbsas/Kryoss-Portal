# A-13 Scan Orchestrator — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate 429 rate-limit errors by having the server assign each machine a unique scan time slot within the org's scan window.

**Architecture:** Server assigns `scan_slot_offset_sec` to each machine at enrollment. New `GET /v1/schedule` endpoint tells the agent when to run. Agent changes from daily 2AM task to hourly check-in that sleeps until its assigned slot. No portal UI in this phase — defaults only (2:00-6:00 AM window).

**Tech Stack:** .NET 8 Azure Functions (API), .NET 8 win-x64 agent, SQL Server, PowerShell (NinjaOne deploy script)

---

### Task 1: SQL Migration

**Files:**
- Create: `KryossApi/sql/049_scan_orchestrator.sql`

- [ ] **Step 1: Write migration script**

```sql
-- 049_scan_orchestrator.sql
-- A-13: Server-side scan orchestrator — scan windows + slot assignment

BEGIN TRANSACTION;

-- Org-level scan window (default 2AM-6AM)
ALTER TABLE organizations ADD
    scan_window_start TIME NOT NULL CONSTRAINT DF_org_scan_start DEFAULT '02:00',
    scan_window_end   TIME NOT NULL CONSTRAINT DF_org_scan_end   DEFAULT '06:00';

-- Per-machine slot offset (seconds from window_start) + check-in tracking
ALTER TABLE machines ADD
    scan_slot_offset_sec INT       NULL,
    last_checkin_at      DATETIME2 NULL;

COMMIT;
```

- [ ] **Step 2: Apply migration to database**

Run against `sql-kryoss.database.windows.net` / `KryossDb`.

- [ ] **Step 3: Verify columns exist**

```sql
SELECT scan_window_start, scan_window_end FROM organizations WHERE 1=0;
SELECT scan_slot_offset_sec, last_checkin_at FROM machines WHERE 1=0;
```

Expected: no error, 0 rows.

---

### Task 2: Entity Updates

**Files:**
- Modify: `KryossApi/src/KryossApi/Data/Entities/Organization.cs`
- Modify: `KryossApi/src/KryossApi/Data/Entities/Machine.cs`

- [ ] **Step 1: Add scan window properties to Organization entity**

In `Organization.cs`, after the `EmployeeCountBand` property (line ~26), add:

```csharp
// A-13: Scan orchestrator — org-level scan window
public TimeSpan ScanWindowStart { get; set; } = new TimeSpan(2, 0, 0);
public TimeSpan ScanWindowEnd { get; set; } = new TimeSpan(6, 0, 0);
```

- [ ] **Step 2: Add slot + checkin properties to Machine entity**

In `Machine.cs`, after the `AgentVersion` property (line ~58), add:

```csharp
// A-13: Scan orchestrator — assigned slot + check-in tracking
public int? ScanSlotOffsetSec { get; set; }
public DateTime? LastCheckinAt { get; set; }
```

- [ ] **Step 3: Add column mappings to KryossDbContext**

In `KryossDbContext.cs`, inside `OnModelCreating`, add mappings for the TIME columns (EF Core maps `TimeSpan` to `time` by default, which is correct). If there's no explicit mapping section for organizations, this step is a no-op — EF convention handles it.

Verify by building:

```bash
cd KryossApi/src/KryossApi && dotnet build --nologo -v q
```

Expected: 0 errors.

---

### Task 3: Slot Assignment Service

**Files:**
- Create: `KryossApi/src/KryossApi/Services/ScanScheduleService.cs`

- [ ] **Step 1: Create ScanScheduleService**

```csharp
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

        // Check gap before first offset
        if (sorted[0] >= MinSpacingSec)
            return 0;

        // Check gaps between consecutive offsets
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var gap = sorted[i + 1] - sorted[i];
            if (gap >= MinSpacingSec * 2)
                return sorted[i] + (gap / 2);
        }

        // Check gap after last offset
        var afterLast = sorted[^1] + MinSpacingSec;
        if (afterLast < windowDuration)
            return afterLast;

        // Window full — stack at end
        return windowDuration - 1;
    }

    public ScheduleResult ComputeSchedule(int slotOffsetSec, TimeSpan windowStart, TimeSpan windowEnd, DateTime? lastRunToday)
    {
        var now = DateTime.UtcNow;
        var todaySlot = now.Date.Add(windowStart).AddSeconds(slotOffsetSec);

        // If window crosses midnight (e.g., 22:00-02:00), adjust
        if (windowEnd < windowStart && now.TimeOfDay < windowEnd)
            todaySlot = todaySlot.AddDays(-1);

        bool ranToday = lastRunToday.HasValue && lastRunToday.Value.Date == now.Date;

        DateTime runAt;
        bool runNow;

        if (ranToday)
        {
            // Already ran today — schedule for tomorrow
            runAt = todaySlot.AddDays(1);
            runNow = false;
        }
        else if (now > todaySlot)
        {
            // Missed today's slot — run immediately (catch-up)
            runAt = todaySlot;
            runNow = true;
        }
        else
        {
            // Slot is in the future today
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
```

- [ ] **Step 2: Register in DI**

In `Program.cs`, add after existing service registrations:

```csharp
builder.Services.AddScoped<IScanScheduleService, ScanScheduleService>();
```

- [ ] **Step 3: Build and verify**

```bash
cd KryossApi/src/KryossApi && dotnet build --nologo -v q
```

Expected: 0 errors.

---

### Task 4: Slot Assignment in Enrollment

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/EnrollmentService.cs`

- [ ] **Step 1: Inject IScanScheduleService**

Add to constructor:

```csharp
private readonly IScanScheduleService _schedule;

public EnrollmentService(KryossDbContext db, ICurrentUserService user,
    IPlatformResolver platformResolver, IScanScheduleService schedule)
{
    _db = db;
    _user = user;
    _platformResolver = platformResolver;
    _schedule = schedule;
}
```

- [ ] **Step 2: Assign slot after machine creation**

In `RedeemCodeAsync`, after `await _db.SaveChangesAsync()` (the one that saves the machine + enrollment code), add:

```csharp
// A-13: Assign scan time slot
await _schedule.AssignSlotAsync(machine.Id, machine.OrganizationId);
```

This must go after `SaveChangesAsync` so the machine has an ID (for new machines).

- [ ] **Step 3: Build and verify**

```bash
cd KryossApi/src/KryossApi && dotnet build --nologo -v q
```

Expected: 0 errors.

---

### Task 5: Schedule Endpoint

**Files:**
- Create: `KryossApi/src/KryossApi/Functions/Agent/ScheduleFunction.cs`

- [ ] **Step 1: Create ScheduleFunction**

```csharp
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

        // Update check-in timestamp
        machine.LastCheckinAt = DateTime.UtcNow;

        // Auto-assign slot if missing (machines enrolled before orchestrator)
        if (machine.ScanSlotOffsetSec is null)
        {
            machine.ScanSlotOffsetSec = await _schedule.AssignSlotAsync(
                machine.Id, machine.OrganizationId);
        }

        // Check if machine has a successful run today
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
```

- [ ] **Step 2: Exempt /v1/schedule from rate limit in ApiKeyAuthMiddleware**

In `ApiKeyAuthMiddleware.cs`, update the public endpoint check (where `/enroll` and `/speedtest` are already exempted):

```csharp
if (path.EndsWith("/enroll", StringComparison.OrdinalIgnoreCase) ||
    path.EndsWith("/speedtest", StringComparison.OrdinalIgnoreCase) ||
    path.EndsWith("/schedule", StringComparison.OrdinalIgnoreCase))
{
    await next(context);
    return;
}
```

- [ ] **Step 3: Build and verify**

```bash
cd KryossApi/src/KryossApi && dotnet build --nologo -v q
```

Expected: 0 errors.

- [ ] **Step 4: Commit API changes**

```bash
git add KryossApi/sql/049_scan_orchestrator.sql \
    KryossApi/src/KryossApi/Data/Entities/Organization.cs \
    KryossApi/src/KryossApi/Data/Entities/Machine.cs \
    KryossApi/src/KryossApi/Services/ScanScheduleService.cs \
    KryossApi/src/KryossApi/Services/EnrollmentService.cs \
    KryossApi/src/KryossApi/Functions/Agent/ScheduleFunction.cs \
    KryossApi/src/KryossApi/Middleware/ApiKeyAuthMiddleware.cs \
    KryossApi/src/KryossApi/Program.cs
git commit -m "feat(api): A-13 scan orchestrator — schedule endpoint + slot assignment"
```

---

### Task 6: Agent — Schedule Response Model

**Files:**
- Create: `KryossAgent/src/KryossAgent/Models/ScheduleResponse.cs`
- Modify: `KryossAgent/src/KryossAgent/Models/JsonContext.cs`

- [ ] **Step 1: Create ScheduleResponse model**

```csharp
using System.Text.Json.Serialization;

namespace KryossAgent.Models;

public class ScheduleResponse
{
    [JsonPropertyName("runNow")]
    public bool RunNow { get; set; }

    [JsonPropertyName("runAt")]
    public DateTime RunAt { get; set; }

    [JsonPropertyName("windowStart")]
    public string WindowStart { get; set; } = "";

    [JsonPropertyName("windowEnd")]
    public string WindowEnd { get; set; } = "";

    [JsonPropertyName("slotOffsetSec")]
    public int SlotOffsetSec { get; set; }
}
```

- [ ] **Step 2: Register in JsonContext**

In `JsonContext.cs`, add before the `[JsonSourceGenerationOptions]` line:

```csharp
[JsonSerializable(typeof(ScheduleResponse))]
```

- [ ] **Step 3: Build and verify**

```bash
cd KryossAgent/src/KryossAgent && dotnet build --nologo -v q
```

Expected: 0 errors.

---

### Task 7: Agent — GetScheduleAsync in ApiClient

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Services/ApiClient.cs`

- [ ] **Step 1: Add GetScheduleAsync method**

Add after the `EnrollAsync` method (around line 97):

```csharp
    /// <summary>
    /// GET /v1/schedule — HMAC signed. Returns when this machine should run.
    /// </summary>
    public async Task<ScheduleResponse?> GetScheduleAsync()
    {
        var path = "/v1/schedule";
        var request = CreateSignedRequest(HttpMethod.Get, path);
        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync(KryossJsonContext.Default.ScheduleResponse);
    }
```

- [ ] **Step 2: Build and verify**

```bash
cd KryossAgent/src/KryossAgent && dotnet build --nologo -v q
```

Expected: 0 errors.

---

### Task 8: Agent — Orchestrated Check-in Flow in Program.cs

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Program.cs`
- Modify: `KryossAgent/src/KryossAgent/Config/AgentConfig.cs`

- [ ] **Step 1: Add LastRunDate to AgentConfig**

In `AgentConfig.cs`, add property after `SpkiPins` (line ~30):

```csharp
public string? LastRunDate { get; set; }
```

In `Load()`, after the SpkiPins block (line ~65), add:

```csharp
config.LastRunDate = key.GetValue("LastRunDate") as string;
```

In `Save()`, after the AssessmentName block (line ~95), add:

```csharp
if (LastRunDate is not null)
    key.SetValue("LastRunDate", LastRunDate);
```

- [ ] **Step 2: Replace jitter block with orchestrated check-in**

In `Program.cs`, replace the jitter block (lines ~88-93):

```csharp
// ── Jitter: spread scheduled runs over 30 min to avoid thundering herd ──
if (silent && !verbose && !args.Contains("--code", StringComparer.OrdinalIgnoreCase))
{
    var jitterMs = Random.Shared.Next(0, 30 * 60 * 1000);
    await Task.Delay(jitterMs);
}
```

With the orchestrated check-in:

```csharp
// ── A-13: Orchestrated scan scheduling ──
// In silent mode (scheduled task), check with server for assigned time slot.
// Falls back to immediate run if server unreachable.
if (silent && !args.Contains("--code", StringComparer.OrdinalIgnoreCase))
{
    var persistedConfig = AgentConfig.Load();
    if (persistedConfig.IsEnrolled)
    {
        // Skip if already ran today (prevents double-run on reboot)
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (persistedConfig.LastRunDate == today)
        {
            if (verbose) Console.Error.WriteLine($"[INFO] Already ran today ({today}). Exiting.");
            Environment.Exit(0);
        }

        try
        {
            using var scheduleClient = new ApiClient(persistedConfig);
            var schedule = await scheduleClient.GetScheduleAsync();
            if (schedule is not null && !schedule.RunNow)
            {
                var sleepMs = (int)(schedule.RunAt - DateTime.UtcNow).TotalMilliseconds;
                if (sleepMs > 0 && sleepMs <= 65 * 60 * 1000)
                {
                    if (verbose) Console.Error.WriteLine(
                        $"[INFO] Slot in {sleepMs / 1000}s (at {schedule.RunAt:HH:mm:ss} UTC). Sleeping...");
                    await Task.Delay(sleepMs);
                }
                else if (sleepMs > 65 * 60 * 1000)
                {
                    if (verbose) Console.Error.WriteLine(
                        $"[INFO] Slot too far ({sleepMs / 1000}s). Exiting — next hourly wake handles it.");
                    Environment.Exit(0);
                }
            }
            else if (verbose && schedule is not null)
            {
                Console.Error.WriteLine("[INFO] Server says runNow=true (catch-up).");
            }
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"[WARN] Schedule check failed, running immediately: {ex.Message}");
        }
    }
}
```

- [ ] **Step 3: Record LastRunDate after successful upload**

In `Program.cs`, after the successful upload block (where `Environment.Exit(0)` is called, around line 536), add before the exit:

```csharp
    // Record successful run date to prevent double-run
    try
    {
        using var runDateKey = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Kryoss\Agent");
        runDateKey.SetValue("LastRunDate", DateTime.UtcNow.ToString("yyyy-MM-dd"));
    }
    catch { }
```

Note: this must go BEFORE `AgentConfig.Wipe()` — but Wipe deletes the whole key. Since the orchestrator check reads `LastRunDate` from a fresh `AgentConfig.Load()` at the START of the next run, and Wipe only runs when the offline queue is empty, we need to keep `LastRunDate` even after wipe. Move it to a separate registry value outside the Kryoss\Agent subkey:

Actually, simpler approach — write `LastRunDate` to a small file instead of registry (since registry gets wiped):

Replace the above with: write to `C:\ProgramData\Kryoss\lastrun.txt`.

In the orchestrated check-in block (Step 2), replace the `LastRunDate` check with:

```csharp
        var lastRunFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Kryoss", "lastrun.txt");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (File.Exists(lastRunFile) && File.ReadAllText(lastRunFile).Trim() == today)
        {
            if (verbose) Console.Error.WriteLine($"[INFO] Already ran today ({today}). Exiting.");
            Environment.Exit(0);
        }
```

And after successful upload (before `Environment.Exit(0)`):

```csharp
    try
    {
        var kryossDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Kryoss");
        Directory.CreateDirectory(kryossDir);
        File.WriteAllText(Path.Combine(kryossDir, "lastrun.txt"),
            DateTime.UtcNow.ToString("yyyy-MM-dd"));
    }
    catch { }
```

Remove the `LastRunDate` property additions from `AgentConfig.cs` (Step 1 of this task) — not needed.

- [ ] **Step 4: Build and verify**

```bash
cd KryossAgent/src/KryossAgent && dotnet build --nologo -v q
```

Expected: 0 errors.

- [ ] **Step 5: Bump version to 1.6.0**

In `KryossAgent.csproj`, change:

```xml
<Version>1.6.0</Version>
```

(Note: current version is 1.5.3 from earlier this session. 1.6.0 because this is a significant behavior change.)

- [ ] **Step 6: Build final and verify**

```bash
cd KryossAgent/src/KryossAgent && dotnet build --nologo -v q
```

Expected: 0 errors.

- [ ] **Step 7: Commit agent changes**

```bash
git add KryossAgent/src/KryossAgent/Program.cs \
    KryossAgent/src/KryossAgent/Services/ApiClient.cs \
    KryossAgent/src/KryossAgent/Models/ScheduleResponse.cs \
    KryossAgent/src/KryossAgent/Models/JsonContext.cs \
    KryossAgent/src/KryossAgent/KryossAgent.csproj
git commit -m "feat(agent): A-13 orchestrated scan scheduling — hourly check-in + server-assigned slots

Agent v1.6.0: replaces random jitter with server-assigned time slots.
GET /v1/schedule returns when to run. Falls back to immediate on failure.
LastRunDate file prevents double-run on reboot."
```

---

### Task 9: Deploy Script — Hourly Trigger

**Files:**
- Modify: `Scripts/Deploy/Deploy-KryossNinja.ps1`

- [ ] **Step 1: Change scheduled task trigger from daily to hourly**

Replace the trigger line:

```powershell
$trigger  = New-ScheduledTaskTrigger -Daily -At "02:00"
```

With:

```powershell
$trigger  = New-ScheduledTaskTrigger -Once -At "00:00" `
    -RepetitionInterval (New-TimeSpan -Hours 1) `
    -RepetitionDuration ([TimeSpan]::MaxValue)
```

Update the description:

```powershell
-Description "Kryoss security assessment — hourly check-in, server-assigned scan slot" | Out-Null
```

Update the log message:

```powershell
Write-Log "Scheduled task '$TASK_NAME' created — hourly check-in (server assigns scan time)."
```

Update the ExecutionTimeLimit to allow for sleep + scan:

```powershell
-ExecutionTimeLimit (New-TimeSpan -Hours 2) `
```

- [ ] **Step 2: Update script version in header**

Change `Version: 2.0` to `Version: 3.0` and update `Modified:` date.

- [ ] **Step 3: Commit deploy script**

```bash
git add Scripts/Deploy/Deploy-KryossNinja.ps1
git commit -m "feat(deploy): A-13 change scheduled task from daily 2AM to hourly check-in"
```

---

### Task 10: Backfill Existing Machines

**Files:**
- Create: `KryossApi/sql/049b_backfill_scan_slots.sql`

- [ ] **Step 1: Write backfill script**

Assigns slots uniformly to all existing active machines that have NULL `scan_slot_offset_sec`:

```sql
-- 049b_backfill_scan_slots.sql
-- Backfill scan slots for machines enrolled before A-13.
-- Distributes uniformly within each org's scan window.

;WITH numbered AS (
    SELECT
        m.id,
        m.organization_id,
        ROW_NUMBER() OVER (PARTITION BY m.organization_id ORDER BY m.first_seen_at) - 1 AS rn,
        COUNT(*) OVER (PARTITION BY m.organization_id) AS total,
        DATEDIFF(SECOND, o.scan_window_start, o.scan_window_end) AS window_sec
    FROM machines m
    JOIN organizations o ON o.id = m.organization_id
    WHERE m.is_active = 1
      AND m.scan_slot_offset_sec IS NULL
      AND m.deleted_at IS NULL
)
UPDATE numbered
SET scan_slot_offset_sec = CASE
    WHEN total <= 1 THEN 0
    ELSE (rn * window_sec) / total
END
FROM machines
WHERE machines.id = numbered.id;
```

- [ ] **Step 2: Apply backfill**

Run against database. Verify:

```sql
SELECT TOP 10 hostname, scan_slot_offset_sec
FROM machines WHERE is_active = 1 AND scan_slot_offset_sec IS NOT NULL
ORDER BY organization_id, scan_slot_offset_sec;
```

Expected: each machine has a unique offset within its org.

- [ ] **Step 3: Commit backfill**

```bash
git add KryossApi/sql/049b_backfill_scan_slots.sql
git commit -m "data: backfill scan slots for existing machines (A-13)"
```

---

### Task 11: Update CLAUDE.md + Roadmap

**Files:**
- Modify: `CLAUDE.md` (root)
- Modify: `KryossAgent/CLAUDE.md`
- Modify: `KryossApi/CLAUDE.md`

- [ ] **Step 1: Add A-13 to root CLAUDE.md decision log**

Add entry to the decisions table:

```
| 2026-04-20 | A-13: Server-side scan orchestrator (Phase 1) | Server assigns scan time slots per machine. Agent hourly check-in via `GET /v1/schedule`. Org default window 2-6AM, uniform slot distribution. Replaces random jitter. Phase 2 adds portal UI. |
```

- [ ] **Step 2: Update agent CLAUDE.md**

Add `--schedule` behavior to CLI flags section. Update version to 1.6.0. Note hourly scheduled task.

- [ ] **Step 3: Update API CLAUDE.md**

Add `GET /v1/schedule` to the agent API endpoint table. Add `ScanScheduleService` to services section.

- [ ] **Step 4: Commit docs**

```bash
git add CLAUDE.md KryossAgent/CLAUDE.md KryossApi/CLAUDE.md
git commit -m "docs: A-13 scan orchestrator Phase 1 in CLAUDE.md files"
```
