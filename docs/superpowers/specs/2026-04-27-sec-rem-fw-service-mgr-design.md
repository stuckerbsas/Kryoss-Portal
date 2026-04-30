# SEC-REM-FW + SVC-MGR: Secure Remediation Framework & Service Management

> **Status:** Design  
> **Date:** 2026-04-27  
> **Components:** Agent 2.8.1 → 2.9.0, API 1.30.1 → 1.31.0, Portal 1.17.0 → 1.18.0  
> **Prerequisites:** SH-KEY (per-machine key rotation), ServiceWorker v3 (parallel loops), remediation pipeline (066_remediation.sql)

---

## Goal

Harden the remediation pipeline with cryptographic task integrity, immutable audit logging, and protected service auto-healing. Add service management UI to the portal. Surface all machine activity in a unified per-machine timeline.

## Architecture

Three pillars, built in order:

1. **Security hardening** — task signatures, immutable audit trail, protected services
2. **Service management** — agent service inventory, portal Services tab, Start/Stop/Restart/Priority
3. **Machine activity log** — unified timeline tab in MachineDetail showing all machine events

---

## Pillar 1: Security Hardening

### 1A. Task Signatures (HMAC-SHA256)

**Signing:** When HeartbeatFunction builds `pendingTasks[]` in the heartbeat response, compute per-task signature:

```
signingString = "{taskId}|{actionType}|{params}|{approvedAt:O}"
signature = HMAC-SHA256(machine.MachineSecret, signingString)
```

**Key choice:** `machine_secret` (not `session_key`). Session key rotates every 48h — pending tasks could outlive a session. Machine secret is long-lived, always available on agent.

**Re-signing:** HeartbeatFunction re-signs ALL pending tasks with CURRENT machine_secret every heartbeat. No stale signatures from pre-rotation.

**Agent validation (RemediationExecutor):**
```
expectedSig = HMAC-SHA256(machineSecret, "{task.Id}|{task.ActionType}|{task.Params}|{task.ApprovedAt:O}")
if expectedSig != task.Signature:
    reject task
    log to AgentLogger ("REJECT", "Task {id} invalid signature")
    enqueue AgentError (severity=ERROR, phase="remediation_reject")
    report to API via task-result with status="rejected_invalid_signature"
```

**Model changes:**

Agent `PendingRemediationTask` gains:
```csharp
[JsonPropertyName("signature")]
public string? Signature { get; set; }

[JsonPropertyName("approvedAt")]
public DateTime? ApprovedAt { get; set; }
```

API `remediation_tasks` table gains:
```sql
signature_hash VARCHAR(64) NULL
```

### 1B. Immutable Audit Trail

**New table `remediation_log`** — INSERT-only. No UPDATE, no DELETE.

```sql
CREATE TABLE remediation_log (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    task_id         BIGINT NOT NULL,
    machine_id      UNIQUEIDENTIFIER NOT NULL,
    organization_id UNIQUEIDENTIFIER NOT NULL,
    event_type      VARCHAR(30) NOT NULL,
    actor_id        UNIQUEIDENTIFIER NULL,
    action_type     VARCHAR(30) NOT NULL,
    control_def_id  INT NULL,
    service_name    VARCHAR(100) NULL,
    params_hash     VARCHAR(64) NULL,
    previous_value  NVARCHAR(500) NULL,
    new_value       NVARCHAR(500) NULL,
    error_message   NVARCHAR(500) NULL,
    signature_hash  VARCHAR(64) NULL,
    ip_address      VARCHAR(45) NULL,
    [timestamp]     DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_rem_log_machine ON remediation_log(machine_id, [timestamp] DESC);
CREATE INDEX ix_rem_log_org ON remediation_log(organization_id, [timestamp] DESC);
CREATE INDEX ix_rem_log_task ON remediation_log(task_id);
```

**Event types:**
- `created` — portal user creates task
- `approved` — task approved (currently auto, same as created)
- `dispatched` — HeartbeatFunction sends task to agent
- `executed` — agent executed task
- `completed` — task succeeded
- `failed` — task failed
- `rejected` — agent rejected invalid signature
- `rolled_back` — rollback executed
- `service_heal` — auto-heal of protected/priority service
- `service_action` — manual service Start/Stop/Restart from portal

**Write points:**
1. `RemediationFunction.CreateTask` → `created`
2. `HeartbeatFunction` first dispatch → `dispatched`
3. `TaskResultFunction` receives result → `executed` + `completed`/`failed`
4. Agent rejects signature → `rejected` (via task-result endpoint)
5. Agent auto-heals service → `service_heal` (via error queue in heartbeat)
6. Portal triggers service action → `service_action`

**`params_hash`** stores SHA256 of params JSON, not plaintext. Proves integrity without exposing registry paths in audit trail.

**Immutability enforcement:** Application-level only (API only INSERTs). Optional future: SQL DENY UPDATE/DELETE on app role.

### 1C. Protected Services (Block List + Auto-Heal)

**Three tiers:**

| Tier | Source | Remediation | Auto-Heal | Severity |
|------|--------|-------------|-----------|----------|
| **Protected** | Hardcoded in agent | BLOCKED | YES | ERROR |
| **Priority** | Per-org, server-delivered via heartbeat config | Restart YES, Disable NO | YES | WARN |
| **Normal** | Everything else | All actions (with signature + audit) | NO | — |

**Protected services (hardcoded, non-negotiable):**
```csharp
private static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
{
    // OS core
    "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM",
    "SamSs", "lsass", "services", "wininit",
    // Security
    "CryptSvc", "TrustedInstaller", "WinDefend",
    "EventLog", "Winmgmt", "BFE", "mpssvc",
    // Self-protection
    "KryossAgent"
};
```

**Priority services:** per-org, configured from portal. Stored in `organizations.priority_services_json` (JSON array). Delivered to agent via `HeartbeatResponse.Config.PriorityServices`.

**Auto-heal logic** — runs at start of HeartbeatLoop (every 15min):

```
healTargets = ProtectedServices ∪ orgPriorityServices
for each service in healTargets:
    if service exists on machine AND status != Running:
        attempt ServiceController.Start()
        log to AgentLogger
        enqueue event:
            Protected → AgentError(severity=ERROR, phase="service_heal")
            Priority → AgentError(severity=WARN, phase="service_heal")
        max 3 retries per service per cycle, 5s backoff
```

Skip services that don't exist on the machine (e.g., NTDS on workstations). Skip KryossAgent (already running if code executes).

**Remediation validation (agent + API):**
```
if service in ProtectedServices → REJECT all actions
if service in PriorityServices AND action is disable_service → REJECT
else → ALLOW (with signature + audit)
```

---

## Pillar 2: Service Management

### 2A. Agent Service Inventory

**New: `ServiceInventory.cs`** — collects full service list via `ServiceController.GetServices()`.

Per service:
```csharp
public class ServiceInfo
{
    public string Name { get; set; }        // short name
    public string DisplayName { get; set; } // friendly name
    public string Status { get; set; }      // Running, Stopped, Paused, etc.
    public string StartupType { get; set; } // Automatic, Manual, Disabled
}
```

**When collected:** During compliance scan (ComplianceLoop). Sent as part of assessment payload OR separate `POST /v1/services` endpoint.

**Recommended:** Separate endpoint `POST /v1/services`. Service inventory changes frequently — decouple from 24h compliance cycle. Collect every heartbeat (15min), send only if changed (hash comparison of previous snapshot).

### 2B. API: Service Endpoints

```
POST /v1/services                          — agent submits service inventory
GET  /v2/machines/{id}/services            — portal reads service list
POST /v2/machines/{id}/services/{name}/action  — portal triggers Start/Stop/Restart
PATCH /v2/machines/{id}/priority-services  — toggle priority flag on service
```

**Service action flow:**
1. Portal `POST /v2/machines/{id}/services/Spooler/action` with body `{"action": "restart"}`
2. API validates: not in ProtectedServices, not disable on Priority
3. API creates `remediation_task` with `action_type=restart_service`, `params={"serviceName":"Spooler"}`
4. Signs task, writes to `remediation_log` (`service_action` event)
5. Next heartbeat: agent receives task, validates signature, executes, reports result
6. All flows through existing remediation pipeline — secure by default

**DB: new table `machine_services`:**
```sql
CREATE TABLE machine_services (
    id          BIGINT IDENTITY(1,1) PRIMARY KEY,
    machine_id  UNIQUEIDENTIFIER NOT NULL,
    name        VARCHAR(100) NOT NULL,
    display_name NVARCHAR(256) NULL,
    status      VARCHAR(20) NOT NULL,
    startup_type VARCHAR(20) NOT NULL,
    updated_at  DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_machine_service UNIQUE (machine_id, name)
);

CREATE INDEX ix_machine_services_machine ON machine_services(machine_id);
```

Upsert on each service inventory submission. No history — current state only (history is in actlog/remediation_log).

**RBAC:**
- `GET /v2/machines/{id}/services` → `machines:read`
- `POST .../action` → `admin:write`
- `PATCH .../priority-services` → `admin:write` + role check (`superadmin` OR `org_manager`)

### 2C. Portal: Services Tab in MachineDetail

**New tab "Services"** in MachineDetail, after existing tabs.

**Layout:**
- Search bar (filter by service name/display name)
- Table columns: Name | Display Name | Status | Startup Type | Priority | Actions
- Status badges: Running (green dot), Stopped (red dot), Paused (yellow dot)
- Priority column: toggle switch (lock icon if ProtectedServices — not toggleable)
- Actions column: Start / Stop / Restart buttons (disabled if Protected, conditionally if Priority)
- Protected services: lock icon + "Protected" badge, no action buttons
- Sort by: Status (stopped first), Name, Priority

**Action buttons:**
- Start: enabled when Stopped
- Stop: enabled when Running (hidden for Protected, disabled for Priority)
- Restart: enabled when Running (hidden for Protected)
- Each triggers confirmation dialog: "Restart service {DisplayName} on {Hostname}?"
- After confirm: creates remediation task, shows toast "Service restart queued"

**Priority toggle:**
- Only visible to superadmin/org_manager
- Toggle on: `PATCH /v2/machines/{id}/priority-services` adds service name
- Toggle off: removes service name
- Applies to entire org (all machines), not per-machine

**Realtime:** service list refetches every 30s (consistent with other machine data).

---

## Pillar 3: Machine Activity Log

### 3A. API Endpoint

```
GET /v2/machines/{id}/activity?page=1&pageSize=50&type=&severity=
```

**Data sources (UNION query):**
1. `actlog` WHERE `entity_type='machine' AND entity_id={machineId}` — heartbeats, scans, errors, config changes
2. `remediation_log` WHERE `machine_id={machineId}` — all remediation + service events

**Response:**
```json
{
  "total": 234,
  "items": [
    {
      "timestamp": "2026-04-27T17:15:00Z",
      "type": "service_heal",
      "severity": "WARN",
      "action": "Auto-healed Spooler (was Stopped)",
      "actor": null,
      "details": { "serviceName": "Spooler", "previousStatus": "Stopped" }
    },
    {
      "timestamp": "2026-04-27T17:14:00Z",
      "type": "heartbeat",
      "severity": "INFO",
      "action": "Heartbeat received",
      "actor": null,
      "details": { "uptime": 65913, "loopStatus": {...} }
    }
  ]
}
```

**RBAC:** `machines:read`

### 3B. Portal: Activity Tab in MachineDetail

**New tab "Activity"** — unified timeline of all machine events.

**Layout:**
- Filter bar: severity (INFO/WARN/ERROR/ALL), type (heartbeat/scan/remediation/service/config/error)
- Timeline list: timestamp | severity badge | type icon | description | actor (user or "Agent")
- Severity colors: INFO=gray, WARN=amber, ERROR=red
- Pagination: 50 per page, load more button
- Auto-refresh: every 30s

**Event type icons:**
- Heartbeat: pulse icon
- Scan: shield-check icon
- Remediation: wrench icon
- Service: cog icon
- Config: settings icon
- Error: alert-triangle icon

---

## SQL Migration: `077_remediation_hardening.sql`

Single migration file covering all 3 pillars:

```sql
-- 1A: Task signature
ALTER TABLE remediation_tasks ADD signature_hash VARCHAR(64) NULL;

-- 1B: Immutable audit trail
CREATE TABLE remediation_log (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    task_id         BIGINT NOT NULL,
    machine_id      UNIQUEIDENTIFIER NOT NULL,
    organization_id UNIQUEIDENTIFIER NOT NULL,
    event_type      VARCHAR(30) NOT NULL,
    actor_id        UNIQUEIDENTIFIER NULL,
    action_type     VARCHAR(30) NOT NULL,
    control_def_id  INT NULL,
    service_name    VARCHAR(100) NULL,
    params_hash     VARCHAR(64) NULL,
    previous_value  NVARCHAR(500) NULL,
    new_value       NVARCHAR(500) NULL,
    error_message   NVARCHAR(500) NULL,
    signature_hash  VARCHAR(64) NULL,
    ip_address      VARCHAR(45) NULL,
    [timestamp]     DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_rem_log_machine ON remediation_log(machine_id, [timestamp] DESC);
CREATE INDEX ix_rem_log_org ON remediation_log(organization_id, [timestamp] DESC);
CREATE INDEX ix_rem_log_task ON remediation_log(task_id);

-- 1C: Priority services per org
ALTER TABLE organizations ADD priority_services_json NVARCHAR(MAX) NULL;

-- 1C: Expand allowed action types
-- Update CHECK constraint on remediation_tasks if needed for new action types
-- (restart_service, stop_service, set_service_startup)

-- 2B: Machine service inventory
CREATE TABLE machine_services (
    id           BIGINT IDENTITY(1,1) PRIMARY KEY,
    machine_id   UNIQUEIDENTIFIER NOT NULL,
    name         VARCHAR(100) NOT NULL,
    display_name NVARCHAR(256) NULL,
    status       VARCHAR(20) NOT NULL,
    startup_type VARCHAR(20) NOT NULL,
    updated_at   DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_machine_service UNIQUE (machine_id, name)
);

CREATE INDEX ix_machine_services_machine ON machine_services(machine_id);

-- 2B: Expand remediation_actions CHECK constraint for new action types
ALTER TABLE remediation_actions DROP CONSTRAINT chk_rem_action_type;
ALTER TABLE remediation_actions ADD CONSTRAINT chk_rem_action_type 
    CHECK (action_type IN ('set_registry','enable_service','disable_service','restart_service','stop_service','set_service_startup','set_audit_policy','set_account_policy'));

ALTER TABLE remediation_tasks DROP CONSTRAINT chk_rem_task_action;
ALTER TABLE remediation_tasks ADD CONSTRAINT chk_rem_task_action
    CHECK (action_type IN ('set_registry','enable_service','disable_service','restart_service','stop_service','set_service_startup','set_audit_policy','set_account_policy'));
```

---

## Version Bumps

| Component | From | To | Reason |
|-----------|------|----|--------|
| Agent | 2.8.1 | 2.9.0 | New feature: service inventory, auto-heal, signature validation, new action types |
| API | 1.30.1 | 1.31.0 | New endpoints: services, activity, remediation_log writes, task signing |
| Portal | 1.17.0 | 1.18.0 | New tabs: Services, Activity |

---

## Backward Compatibility

- **Pre-2.9.0 agents:** HeartbeatFunction sends `signature=null` for tasks dispatched to old agents. Old agents ignore unknown JSON fields (`[JsonIgnore(WhenWritingNull)]`). Tasks execute unsigned (existing behavior). No breakage.
- **Signature enforcement:** Agent 2.9.0+ rejects tasks without valid signature. Transition period: if `signature` is null AND agent version < 2.9.0, allow execution (logged as WARN).
- **New action types:** Old agents don't know `restart_service`/`stop_service`/`set_service_startup`. RemediationExecutor whitelist rejects unknown types (existing behavior). Tasks stay `approved` until agent upgrades.
- **Priority services config:** `HeartbeatResponse.Config.PriorityServices` is nullable. Old agents ignore unknown config fields.

---

## Security Properties (Post-Implementation)

| Property | Status |
|----------|--------|
| Task integrity (HMAC-SHA256 per task) | ✅ |
| Task origin verification (machine_secret binding) | ✅ |
| Immutable audit trail (INSERT-only remediation_log) | ✅ |
| Protected services (hardcoded block list, non-negotiable) | ✅ |
| Protected service auto-heal (15min cycle) | ✅ |
| Priority services (org-configurable, auto-heal) | ✅ |
| Service name validation (Protected=block, Priority=no-disable) | ✅ |
| RBAC on all portal endpoints | ✅ |
| Signature rejection reporting (actlog + remediation_log) | ✅ |
| Full machine activity timeline (unified view) | ✅ |
| Backward compatibility (graceful degradation for old agents) | ✅ |

---

## Files to Create/Modify

### Agent (new)
- `Services/ServiceInventory.cs` — full service list collection via ServiceController
- `Services/ServiceHealer.cs` — auto-heal logic for Protected + Priority services

### Agent (modify)
- `Services/RemediationExecutor.cs` — signature validation, new action types (restart/stop/set_startup), ProtectedServices block list
- `Services/ServiceWorker.cs` — call ServiceHealer in HeartbeatLoop, call ServiceInventory in ComplianceLoop
- `Models/RemediationModels.cs` — add Signature + ApprovedAt to PendingRemediationTask, add PriorityServices to AgentRemoteConfig
- `Models/JsonContext.cs` — add ServiceInfo serialization
- `Services/ApiClient.cs` — new SubmitServiceInventoryAsync method
- `KryossAgent.csproj` — version 2.8.1 → 2.9.0

### API (new)
- `Functions/Portal/ServiceManagementFunction.cs` — GET services, POST action, PATCH priority
- `Functions/Agent/ServiceInventoryFunction.cs` — POST /v1/services
- `Data/Entities/MachineService.cs` — entity
- `Data/Entities/RemediationLog.cs` — entity
- `Services/RemediationLogService.cs` — INSERT-only helper

### API (modify)
- `Functions/Agent/HeartbeatFunction.cs` — task signing, deliver PriorityServices in config, write service_heal events from error queue to remediation_log
- `Functions/Portal/RemediationFunction.cs` — write to remediation_log on create/rollback, validate service tiers
- `Functions/Agent/TaskResultFunction.cs` — write to remediation_log on result
- `Functions/Portal/MachinesFunction.cs` — add activity endpoint
- `Data/KryossDbContext.cs` — add MachineService + RemediationLog DbSets
- `Data/Entities/Organization.cs` — add PriorityServicesJson property
- `KryossApi.csproj` — version 1.30.1 → 1.31.0

### Portal (new)
- `src/components/machines/ServicesTab.tsx` — Services tab component
- `src/components/machines/ActivityTab.tsx` — Activity timeline component
- `src/api/services.ts` — API hooks for services + activity

### Portal (modify)
- `src/components/machines/MachineDetail.tsx` — add Services + Activity tabs
- `src/types/index.ts` — ServiceInfo type
- `package.json` — version 1.17.0 → 1.18.0

### SQL
- `sql/077_remediation_hardening.sql` — single migration (all tables + columns)
