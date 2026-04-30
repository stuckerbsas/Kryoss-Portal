# A-13: Server-Side Scan Orchestrator

**Date:** 2026-04-20
**Status:** Approved
**Priority:** P0 — blocks production release

## Problem

All machines in an org share one API key. Even with per-machine rate limiting (15 req/min via X-Agent-Id) and 0-30 min client-side jitter, bursts cause 429 errors. At 37 machines this is painful; at 1000+ it's unusable.

## Solution

Hybrid approach: server assigns scan time slots at enrollment, org-level scan window configurable from portal with auto-calculated defaults. Agent scheduled task changes from daily to hourly check-in.

## Data Model

### Organizations — new columns

```sql
ALTER TABLE organizations ADD
    scan_window_start TIME NOT NULL DEFAULT '02:00',
    scan_window_end   TIME NOT NULL DEFAULT '06:00';
```

### Machines — new columns

```sql
ALTER TABLE machines ADD
    scan_slot_offset_sec INT         NULL,
    last_checkin_at      DATETIME2   NULL;
```

`scan_slot_offset_sec` = seconds from `scan_window_start`. Example: org with 2:00-6:00 window (14400s), 37 machines → each ~389s apart (~6.5 min).

## Schedule Endpoint

**`GET /v1/schedule`** — authenticated (API key + HMAC), lightweight.

Request headers: `X-Agent-Id`, `X-Api-Key`, `X-Signature`, `X-Timestamp`

Response:
```json
{
  "runNow": true,
  "runAt": "2026-04-21T02:17:30Z",
  "windowStart": "02:00",
  "windowEnd": "06:00",
  "slotOffsetSec": 1050
}
```

Server logic:
1. Look up machine by `X-Agent-Id` → get `scan_slot_offset_sec`
2. If no offset assigned → calculate and assign (auto-slot on first check-in)
3. Update `last_checkin_at = UTC NOW`
4. Calculate `runAt` = today's `window_start + offset`
5. If `runAt` already passed AND no successful run today → `runNow = true` (catch-up for machines that were off)
6. If machine already ran today → `runAt` = tomorrow's slot

Rate limit: exempt (like `/v1/speedtest`) — 1 req/hour/machine, negligible load.

## Slot Assignment Algorithm

**On enrollment:**
```
window_duration = (window_end - window_start) in seconds
existing_offsets = all active machines' scan_slot_offset_sec for this org
next_offset = smallest gap that fits ≥ min_spacing
```

**Min spacing:** floor of 10 seconds. Two machines never scheduled <10s apart.

**Capacity:** 4h window / 10s min spacing = 1440 machines max. Orgs exceeding this → warn MSP to widen window.

**On machine deactivation:** gap remains, filled by next enrollment.

**Redistribute:** `POST /v2/organizations/{id}/redistribute-slots` recalculates all slots uniformly. Called automatically when MSP changes scan window, or manually from portal.

## Agent Changes

### Scheduled task

Changes from daily 2AM to hourly repeat:
```powershell
New-ScheduledTaskTrigger -Once -At "00:00" `
    -RepetitionInterval (New-TimeSpan -Hours 1) `
    -RepetitionDuration ([TimeSpan]::MaxValue)
```

### Agent flow (new)

```
1. Hourly wake → GET /v1/schedule
2. If runNow == false:
   a. sleep = runAt - now
   b. If 0 < sleep < 65 min → sleep, then run
   c. If sleep > 65 min → exit (next hourly wake)
3. If runNow == true → run immediately
4. Normal flow: controls → scan → upload
```

### Registry additions

- `LastRunDate` (string, YYYY-MM-DD): skip if already ran today, prevents double-run on reboot.

### Fallback

If `/v1/schedule` fails (timeout, 5xx, 401) → run immediately. Orchestrator failure never causes a missed scan.

### Backward compatibility

Old agents (pre-orchestrator) still work: daily 2AM + jitter, rate limit as safety net. No breaking change.

## Portal UI

### Org Settings → Scan Schedule

- Time pickers for `window_start` and `window_end`
- Display: "37 machines, ~6.5 min between scans"
- Table: each machine's assigned slot time, last check-in, last run

### Dashboard enhancement

- "Next scan wave" countdown
- Machine status: ran today / pending / missed / offline (no check-in >25h)

### Endpoints

- `PATCH /v2/organizations/{id}` — existing, add `scanWindowStart`, `scanWindowEnd`
- `GET /v2/organizations/{id}/scan-schedule` — machine list with slots, check-in, status
- `POST /v2/organizations/{id}/redistribute-slots` — uniform redistribution
- `PATCH /v2/machines/{id}` — accept `scanSlotOffsetSec` for manual override

## Error Handling

| Scenario | Behavior |
|---|---|
| Server down during check-in | Agent runs immediately (fallback) |
| Clock skew >5 min | Log warning, run at calculated local time |
| Agent killed mid-scan | Offline queue handles. Next check-in → `runNow: true` |
| Window changed while agents sleeping | Old-window agents run once at old time, get new slot on next check-in. `redistribute-slots` recalculates. |
| Stale machine (no check-in >7d) | Excluded from slot spacing calculations. Portal shows "Offline" |
| Concurrent check-ins at deploy | Lightweight GET, fast DB lookup. No contention. |

## Migration

```sql
-- 049_scan_orchestrator.sql
ALTER TABLE organizations ADD
    scan_window_start TIME NOT NULL DEFAULT '02:00',
    scan_window_end   TIME NOT NULL DEFAULT '06:00';

ALTER TABLE machines ADD
    scan_slot_offset_sec INT       NULL,
    last_checkin_at      DATETIME2 NULL;
```

## Deploy Requirements

1. Apply `sql/049_scan_orchestrator.sql`
2. Deploy API (new `ScheduleFunction.cs` + enrollment changes)
3. Publish agent v1.6.0 (hourly check-in + schedule flow)
4. Update NinjaOne deploy script (hourly trigger)
5. Portal deploy (scan schedule UI)

## Future: Windows Service

Hourly check-in is one step away from a persistent Windows Service with heartbeat. When the product needs real-time push commands, live status, or remote remediation, the agent converts to a service. This orchestrator design is forward-compatible — the `/v1/schedule` endpoint becomes the heartbeat, the response can include commands beyond "run scan".
