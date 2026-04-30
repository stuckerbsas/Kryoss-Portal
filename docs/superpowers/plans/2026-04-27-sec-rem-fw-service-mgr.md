# SEC-REM-FW + SVC-MGR Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden the remediation pipeline with per-task HMAC signatures, immutable audit logging, protected service auto-healing, and add a full service management UI + machine activity timeline to the portal.

**Architecture:** Three pillars built in order: (1) Security hardening — task HMAC signatures using machine_secret, INSERT-only remediation_log, protected services block list with auto-heal + org-configurable priority services. (2) Service management — agent collects full service inventory via ServiceController, API persists in machine_services, portal Services tab with Start/Stop/Restart buttons and Priority toggle. (3) Machine activity log — unified timeline tab combining actlog + remediation_log entries per machine.

**Tech Stack:** .NET 8 (agent + API), EF Core 8 + Azure SQL, React 18 + TypeScript + shadcn/ui + TanStack Query (portal), HMAC-SHA256 for task signing.

---

## File Structure

### Agent — New Files
| File | Responsibility |
|------|---------------|
| `KryossAgent/src/KryossAgent/Services/ServiceHealer.cs` | Auto-heal protected + priority services every heartbeat cycle |
| `KryossAgent/src/KryossAgent/Services/ServiceInventory.cs` | Collect full service list via ServiceController.GetServices() |

### Agent — Modified Files
| File | Changes |
|------|---------|
| `Services/RemediationExecutor.cs` | Add ProtectedServices block list, signature validation, new action types (restart/stop/set_startup) |
| `Services/ServiceWorker.cs` | Call ServiceHealer + ServiceInventory in HeartbeatLoop |
| `Models/RemediationModels.cs` | Add Signature + ApprovedAt to PendingRemediationTask, PriorityServices to AgentRemoteConfig, ServiceInfo model |
| `Models/JsonContext.cs` | Register ServiceInfo, List\<ServiceInfo\> |
| `Services/ApiClient.cs` | Add SubmitServiceInventoryAsync method |
| `KryossAgent.csproj` | Version 2.8.1 → 2.9.0 |

### API — New Files
| File | Responsibility |
|------|---------------|
| `Functions/Portal/ServiceManagementFunction.cs` | GET services list, POST service action, PATCH priority-services |
| `Functions/Agent/ServiceInventoryFunction.cs` | POST /v1/services — agent submits inventory |
| `Data/Entities/RemediationLog.cs` | RemediationLog entity |
| `Data/Entities/MachineService.cs` | MachineService entity |
| `Services/RemediationLogService.cs` | INSERT-only helper for remediation_log |

### API — Modified Files
| File | Changes |
|------|---------|
| `Functions/Agent/HeartbeatFunction.cs` | Sign pending tasks with HMAC, deliver PriorityServices in config, write service_heal events to remediation_log |
| `Functions/Portal/RemediationFunction.cs` | Write to remediation_log on create/rollback/cancel, validate protected/priority service tiers |
| `Functions/Agent/TaskResultFunction.cs` | Write to remediation_log on result |
| `Functions/Portal/MachinesFunction.cs` | Add activity endpoint |
| `Data/KryossDbContext.cs` | Register MachineService + RemediationLog DbSets |
| `Data/Entities/Organization.cs` | Add PriorityServicesJson property |
| `Data/Entities/Remediation.cs` | Add SignatureHash to RemediationTask |
| `KryossApi.csproj` | Version 1.30.1 → 1.31.0 |

### Portal — New Files
| File | Responsibility |
|------|---------------|
| `src/components/machines/ServicesTab.tsx` | Services tab with table, action buttons, priority toggle |
| `src/components/machines/ActivityTab.tsx` | Unified activity timeline |
| `src/api/services.ts` | API hooks for services + activity |

### Portal — Modified Files
| File | Changes |
|------|---------|
| `src/components/machines/MachineDetail.tsx` | Add Services + Activity tab triggers and content |
| `src/api/machines.ts` | Extend MachineDetail type if needed |
| `package.json` | Version 1.17.0 → 1.18.0 |

### SQL
| File | Responsibility |
|------|---------------|
| `KryossApi/sql/077_remediation_hardening.sql` | All schema changes in one migration |

---

## Task 1: SQL Migration

**Files:**
- Create: `KryossApi/sql/077_remediation_hardening.sql`

- [ ] **Step 1: Write the migration file**

```sql
-- 077_remediation_hardening.sql
-- SEC-REM-FW + SVC-MGR: task signatures, immutable audit trail, service inventory, priority services

-- 1A: Task signature column
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

-- 2B: Expand remediation action types
ALTER TABLE remediation_actions DROP CONSTRAINT chk_rem_action_type;
ALTER TABLE remediation_actions ADD CONSTRAINT chk_rem_action_type
    CHECK (action_type IN ('set_registry','enable_service','disable_service',
        'restart_service','stop_service','set_service_startup',
        'set_audit_policy','set_account_policy'));

ALTER TABLE remediation_tasks DROP CONSTRAINT chk_rem_task_action;
ALTER TABLE remediation_tasks ADD CONSTRAINT chk_rem_task_action
    CHECK (action_type IN ('set_registry','enable_service','disable_service',
        'restart_service','stop_service','set_service_startup',
        'set_audit_policy','set_account_policy'));
```

- [ ] **Step 2: Commit**

```bash
git add KryossApi/sql/077_remediation_hardening.sql
git commit -m "feat(sql): 077 remediation hardening — audit trail, service inventory, task signatures"
```

---

## Task 2: API Entities + DbContext + RemediationLogService

**Files:**
- Create: `KryossApi/src/KryossApi/Data/Entities/RemediationLog.cs`
- Create: `KryossApi/src/KryossApi/Data/Entities/MachineService.cs`
- Create: `KryossApi/src/KryossApi/Services/RemediationLogService.cs`
- Modify: `KryossApi/src/KryossApi/Data/KryossDbContext.cs`
- Modify: `KryossApi/src/KryossApi/Data/Entities/Remediation.cs`
- Modify: `KryossApi/src/KryossApi/Data/Entities/Organization.cs`

- [ ] **Step 1: Create RemediationLog entity**

Create `KryossApi/src/KryossApi/Data/Entities/RemediationLog.cs`:

```csharp
namespace KryossApi.Data.Entities;

public class RemediationLog
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public Guid MachineId { get; set; }
    public Guid OrganizationId { get; set; }
    public string EventType { get; set; } = null!;
    public Guid? ActorId { get; set; }
    public string ActionType { get; set; } = null!;
    public int? ControlDefId { get; set; }
    public string? ServiceName { get; set; }
    public string? ParamsHash { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SignatureHash { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; }
}
```

- [ ] **Step 2: Create MachineService entity**

Create `KryossApi/src/KryossApi/Data/Entities/MachineService.cs`:

```csharp
namespace KryossApi.Data.Entities;

public class MachineService
{
    public long Id { get; set; }
    public Guid MachineId { get; set; }
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Status { get; set; } = null!;
    public string StartupType { get; set; } = null!;
    public DateTime UpdatedAt { get; set; }

    public Machine Machine { get; set; } = null!;
}
```

- [ ] **Step 3: Add SignatureHash to RemediationTask**

In `KryossApi/src/KryossApi/Data/Entities/Remediation.cs`, add to `RemediationTask` class after the `CompletedAt` property (line 34):

```csharp
    public string? SignatureHash { get; set; }
```

- [ ] **Step 4: Add PriorityServicesJson to Organization**

In `KryossApi/src/KryossApi/Data/Entities/Organization.cs`, add after `ScanWindowEnd` property (line 34):

```csharp
    public string? PriorityServicesJson { get; set; }
```

- [ ] **Step 5: Register DbSets in KryossDbContext**

In `KryossApi/src/KryossApi/Data/KryossDbContext.cs`, add after the RemediationTasks DbSet (around line 157):

```csharp
    public DbSet<RemediationLog> RemediationLogs => Set<RemediationLog>();
    public DbSet<MachineService> MachineServices => Set<MachineService>();
```

- [ ] **Step 6: Create RemediationLogService**

Create `KryossApi/src/KryossApi/Services/RemediationLogService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using KryossApi.Data;
using KryossApi.Data.Entities;

namespace KryossApi.Services;

public interface IRemediationLogService
{
    Task LogAsync(long taskId, Guid machineId, Guid organizationId, string eventType,
        string actionType, int? controlDefId = null, string? serviceName = null,
        string? paramsJson = null, string? previousValue = null, string? newValue = null,
        string? errorMessage = null, string? signatureHash = null,
        Guid? actorId = null, string? ipAddress = null);
}

public class RemediationLogService : IRemediationLogService
{
    private readonly KryossDbContext _db;

    public RemediationLogService(KryossDbContext db) => _db = db;

    public async Task LogAsync(long taskId, Guid machineId, Guid organizationId, string eventType,
        string actionType, int? controlDefId = null, string? serviceName = null,
        string? paramsJson = null, string? previousValue = null, string? newValue = null,
        string? errorMessage = null, string? signatureHash = null,
        Guid? actorId = null, string? ipAddress = null)
    {
        _db.RemediationLogs.Add(new RemediationLog
        {
            TaskId = taskId,
            MachineId = machineId,
            OrganizationId = organizationId,
            EventType = eventType,
            ActorId = actorId,
            ActionType = actionType,
            ControlDefId = controlDefId,
            ServiceName = serviceName,
            ParamsHash = paramsJson is not null
                ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paramsJson))).ToLowerInvariant()
                : null,
            PreviousValue = previousValue?.Length > 500 ? previousValue[..500] : previousValue,
            NewValue = newValue?.Length > 500 ? newValue[..500] : newValue,
            ErrorMessage = errorMessage?.Length > 500 ? errorMessage[..500] : errorMessage,
            SignatureHash = signatureHash,
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 7: Register RemediationLogService in DI**

In `KryossApi/src/KryossApi/Program.cs`, add alongside the other service registrations:

```csharp
services.AddScoped<IRemediationLogService, RemediationLogService>();
```

- [ ] **Step 8: Build and verify**

```bash
cd KryossApi && dotnet build src/KryossApi/KryossApi.csproj --no-restore -v q
```
Expected: 0 errors.

- [ ] **Step 9: Commit**

```bash
git add KryossApi/src/KryossApi/Data/Entities/RemediationLog.cs KryossApi/src/KryossApi/Data/Entities/MachineService.cs KryossApi/src/KryossApi/Services/RemediationLogService.cs KryossApi/src/KryossApi/Data/KryossDbContext.cs KryossApi/src/KryossApi/Data/Entities/Remediation.cs KryossApi/src/KryossApi/Data/Entities/Organization.cs KryossApi/src/KryossApi/Program.cs
git commit -m "feat(api): entities + RemediationLogService for SEC-REM-FW"
```

---

## Task 3: HeartbeatFunction — Task Signing + Priority Services Config

**Files:**
- Modify: `KryossApi/src/KryossApi/Functions/Agent/HeartbeatFunction.cs`

- [ ] **Step 1: Add task signing + priority services delivery**

Replace the pending tasks query and response building section (lines 148-170) with:

```csharp
        // Query pending tasks
        var pendingTasks = await _db.RemediationTasks
            .Where(t => t.MachineId == machine.Id && t.Status == "approved")
            .Select(t => new
            {
                t.Id,
                t.ActionType,
                t.Params,
                t.ControlDefId,
                controlId = t.ControlDef.ControlId,
                t.ApprovedAt,
            })
            .ToListAsync();

        // Sign each task with machine_secret (HMAC-SHA256)
        List<object>? signedTasks = null;
        if (pendingTasks.Count > 0 && !string.IsNullOrEmpty(machine.MachineSecret))
        {
            var remLogService = req.FunctionContext.InstanceServices.GetRequiredService<IRemediationLogService>();
            signedTasks = new List<object>();
            foreach (var t in pendingTasks)
            {
                var signingString = $"{t.Id}|{t.ActionType}|{t.Params}|{t.ApprovedAt:O}";
                var keyBytes = Encoding.UTF8.GetBytes(machine.MachineSecret);
                var signature = Convert.ToHexString(
                    System.Security.Cryptography.HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(signingString))
                ).ToLowerInvariant();

                signedTasks.Add(new
                {
                    t.Id,
                    t.ActionType,
                    t.Params,
                    t.ControlDefId,
                    t.controlId,
                    approvedAt = t.ApprovedAt,
                    signature,
                });

                // Log first dispatch
                try
                {
                    await remLogService.LogAsync(t.Id, machine.Id, machine.OrganizationId,
                        "dispatched", t.ActionType, t.ControlDefId, paramsJson: t.Params,
                        signatureHash: signature);
                }
                catch { }
            }
        }
        else if (pendingTasks.Count > 0)
        {
            // Legacy agents without machine_secret — send unsigned
            signedTasks = pendingTasks.Select(t => (object)new
            {
                t.Id, t.ActionType, t.Params, t.ControlDefId, t.controlId,
            }).ToList();
        }

        // Persist service_heal events from agent error queue to remediation_log
        if (body?.Errors is { Count: > 0 })
        {
            var remLogService = req.FunctionContext.InstanceServices.GetRequiredService<IRemediationLogService>();
            foreach (var err in body.Errors.Where(e => e.Phase == "service_heal"))
            {
                try
                {
                    await remLogService.LogAsync(0, machine.Id, machine.OrganizationId,
                        "service_heal", "heal_service", serviceName: err.Target,
                        errorMessage: err.Message);
                }
                catch { }
            }
        }

        // Load org for priority services
        var org = await _db.Organizations.AsNoTracking()
            .Where(o => o.Id == machine.OrganizationId)
            .Select(o => new { o.PriorityServicesJson })
            .FirstOrDefaultAsync();

        List<string>? priorityServices = null;
        if (!string.IsNullOrEmpty(org?.PriorityServicesJson))
        {
            try { priorityServices = JsonSerializer.Deserialize<List<string>>(org.PriorityServicesJson); }
            catch { }
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        var config = new
        {
            complianceIntervalHours = machine.ConfigComplianceIntervalHours,
            snmpIntervalMinutes = machine.ConfigSnmpIntervalMinutes,
            enableNetworkScan = machine.ConfigEnableNetworkScan,
            networkScanIntervalHours = machine.ConfigNetworkScanIntervalHours,
            enablePassiveDiscovery = machine.ConfigEnablePassiveDiscovery,
            priorityServices,
        };

        await ok.WriteAsJsonAsync(new
        {
            ack = true,
            pendingTasks = signedTasks,
            newMachineSecret,
            newSessionKey,
            newSessionKeyExpiresAt,
            config,
            forceScan,
        });
        return ok;
```

Also add at the top of the file:
```csharp
using System.Text;
```

- [ ] **Step 2: Build and verify**

```bash
cd KryossApi && dotnet build src/KryossApi/KryossApi.csproj --no-restore -v q
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Functions/Agent/HeartbeatFunction.cs
git commit -m "feat(api): sign remediation tasks with HMAC + deliver priority services in heartbeat"
```

---

## Task 4: RemediationFunction + TaskResultFunction — Audit Trail Writes

**Files:**
- Modify: `KryossApi/src/KryossApi/Functions/Portal/RemediationFunction.cs`
- Modify: `KryossApi/src/KryossApi/Functions/Agent/TaskResultFunction.cs`

- [ ] **Step 1: Add remediation_log writes to RemediationFunction**

Add `IRemediationLogService` to constructor:

```csharp
    private readonly IRemediationLogService _remLog;

    public RemediationFunction(KryossDbContext db, ICurrentUserService user, IActlogService actlog, IRemediationLogService remLog)
    {
        _db = db;
        _actlog = actlog;
        _user = user;
        _remLog = remLog;
    }
```

**In CreateTask** — after `await _db.SaveChangesAsync()` (line 102), add:

```csharp
        try
        {
            await _remLog.LogAsync(task.Id, task.MachineId, task.OrganizationId,
                "created", task.ActionType, task.ControlDefId,
                paramsJson: task.Params, actorId: _user.UserId, ipAddress: _user.IpAddress);
        }
        catch { }
```

**Add protected service validation** — before task creation (after machine lookup, ~line 86), add:

```csharp
        // Validate service actions against protected/priority tiers
        if (action.ActionType is "enable_service" or "disable_service" or "restart_service"
            or "stop_service" or "set_service_startup")
        {
            var svcParams = body.Params ?? action.ParamsTemplate;
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

                // Priority services: block disable only
                var org = await _db.Organizations.AsNoTracking()
                    .Where(o => o.Id == machine.OrganizationId)
                    .Select(o => o.PriorityServicesJson)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrEmpty(org) && action.ActionType == "disable_service")
                {
                    try
                    {
                        var psList = JsonSerializer.Deserialize<List<string>>(org);
                        if (psList?.Any(s => s.Equals(serviceName, StringComparison.OrdinalIgnoreCase)) == true)
                        {
                            var forbidden = req.CreateResponse(HttpStatusCode.BadRequest);
                            await forbidden.WriteAsJsonAsync(new { error = $"Service '{serviceName}' is a priority service and cannot be disabled" });
                            return forbidden;
                        }
                    }
                    catch { }
                }
            }
        }
```

**In Rollback** — after `await _db.SaveChangesAsync()` (line 201), add:

```csharp
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
```

- [ ] **Step 2: Add remediation_log writes to TaskResultFunction**

Add DI for `IRemediationLogService`:

```csharp
    private readonly KryossDbContext _db;
    private readonly IRemediationLogService _remLog;

    public TaskResultFunction(KryossDbContext db, IRemediationLogService remLog)
    {
        _db = db;
        _remLog = remLog;
    }
```

After `await _db.SaveChangesAsync()` (line 61), add:

```csharp
        try
        {
            await _remLog.LogAsync(task.Id, task.MachineId, task.OrganizationId,
                body.Success ? "completed" : "failed", task.ActionType, task.ControlDefId,
                previousValue: body.PreviousValue, newValue: body.NewValue,
                errorMessage: body.ErrorMessage, signatureHash: task.SignatureHash);
        }
        catch { }
```

- [ ] **Step 3: Build and verify**

```bash
cd KryossApi && dotnet build src/KryossApi/KryossApi.csproj --no-restore -v q
```

- [ ] **Step 4: Commit**

```bash
git add KryossApi/src/KryossApi/Functions/Portal/RemediationFunction.cs KryossApi/src/KryossApi/Functions/Agent/TaskResultFunction.cs
git commit -m "feat(api): audit trail writes + protected service validation in remediation pipeline"
```

---

## Task 5: Agent Models — Signature, PriorityServices, ServiceInfo

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Models/RemediationModels.cs`
- Modify: `KryossAgent/src/KryossAgent/Models/JsonContext.cs`

- [ ] **Step 1: Extend PendingRemediationTask with signature + approvedAt**

In `RemediationModels.cs`, add to `PendingRemediationTask` (after line 66):

```csharp
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    [JsonPropertyName("approvedAt")]
    public DateTime? ApprovedAt { get; set; }
```

- [ ] **Step 2: Add PriorityServices to AgentRemoteConfig**

In `RemediationModels.cs`, add to `AgentRemoteConfig` (after line 48):

```csharp
    [JsonPropertyName("priorityServices")]
    public List<string>? PriorityServices { get; set; }
```

- [ ] **Step 3: Add ServiceInfo model**

In `RemediationModels.cs`, add at the end of the file:

```csharp
public class ServiceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("startupType")]
    public string StartupType { get; set; } = null!;
}
```

- [ ] **Step 4: Register new types in JsonContext**

In `JsonContext.cs`, add before the `[JsonSourceGenerationOptions]` line:

```csharp
[JsonSerializable(typeof(ServiceInfo))]
[JsonSerializable(typeof(List<ServiceInfo>))]
```

- [ ] **Step 5: Build and verify**

```bash
cd KryossAgent && dotnet build src/KryossAgent/KryossAgent.csproj --no-restore -v q
```

- [ ] **Step 6: Commit**

```bash
git add KryossAgent/src/KryossAgent/Models/RemediationModels.cs KryossAgent/src/KryossAgent/Models/JsonContext.cs
git commit -m "feat(agent): models for task signatures, priority services, service inventory"
```

---

## Task 6: Agent RemediationExecutor — Signature Validation + Protected Services + New Actions

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Services/RemediationExecutor.cs`

- [ ] **Step 1: Add ProtectedServices, signature validation, new action types**

Replace the full `RemediationExecutor.cs` content with:

```csharp
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using KryossAgent.Config;
using KryossAgent.Models;
using Microsoft.Win32;

namespace KryossAgent.Services;

public static class RemediationExecutor
{
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "set_registry",
        "enable_service",
        "disable_service",
        "restart_service",
        "stop_service",
        "set_service_startup",
        "set_audit_policy",
        "set_account_policy",
    };

    private static readonly string[] AllowedRegistryPrefixes =
    {
        @"HKLM\SYSTEM\CurrentControlSet\Services\",
        @"HKLM\SOFTWARE\Policies\",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\",
    };

    internal static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM",
        "SamSs", "lsass", "services", "wininit",
        "CryptSvc", "TrustedInstaller", "WinDefend",
        "EventLog", "Winmgmt", "BFE", "mpssvc",
        "KryossAgent",
    };

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Kryoss", "Remediation");

    public static async Task ExecuteTasksAsync(
        ApiClient apiClient,
        IReadOnlyList<PendingRemediationTask> tasks,
        bool verbose = false)
    {
        Directory.CreateDirectory(LogDir);
        var config = AgentConfig.Load();

        foreach (var task in tasks)
        {
            // Signature validation (v2.9.0+)
            if (task.Signature is not null && !string.IsNullOrEmpty(config.MachineSecret))
            {
                var signingString = $"{task.Id}|{task.ActionType}|{task.Params}|{task.ApprovedAt:O}";
                var keyBytes = Encoding.UTF8.GetBytes(config.MachineSecret);
                var expected = Convert.ToHexString(
                    HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(signingString))
                ).ToLowerInvariant();

                if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected),
                    Encoding.UTF8.GetBytes(task.Signature.ToLowerInvariant())))
                {
                    AgentLogger.Error("REMEDIATE", $"Task {task.Id}: REJECTED — invalid signature");
                    await apiClient.ReportTaskResultAsync(new TaskResultPayload
                    {
                        TaskId = task.Id,
                        Success = false,
                        ErrorMessage = "rejected_invalid_signature",
                        ExecutedAt = DateTime.UtcNow,
                    });
                    continue;
                }
            }
            else if (task.Signature is null && !string.IsNullOrEmpty(config.MachineSecret))
            {
                // Unsigned task on a v2+ agent — reject
                AgentLogger.Error("REMEDIATE", $"Task {task.Id}: REJECTED — unsigned task");
                await apiClient.ReportTaskResultAsync(new TaskResultPayload
                {
                    TaskId = task.Id,
                    Success = false,
                    ErrorMessage = "rejected_unsigned_task",
                    ExecutedAt = DateTime.UtcNow,
                });
                continue;
            }

            if (!AllowedActions.Contains(task.ActionType))
            {
                if (verbose) Console.WriteLine($"[REMEDIATE] REJECTED unknown action: {task.ActionType} (task {task.Id})");
                await apiClient.ReportTaskResultAsync(new TaskResultPayload
                {
                    TaskId = task.Id,
                    Success = false,
                    ErrorMessage = $"Action type '{task.ActionType}' not in agent whitelist",
                    ExecutedAt = DateTime.UtcNow,
                });
                continue;
            }

            // Protected service check
            if (task.ActionType is "enable_service" or "disable_service" or "restart_service"
                or "stop_service" or "set_service_startup")
            {
                var svcName = ExtractServiceName(task.Params);
                if (!string.IsNullOrEmpty(svcName) && ProtectedServices.Contains(svcName))
                {
                    AgentLogger.Error("REMEDIATE", $"Task {task.Id}: REJECTED — '{svcName}' is a protected service");
                    await apiClient.ReportTaskResultAsync(new TaskResultPayload
                    {
                        TaskId = task.Id,
                        Success = false,
                        ErrorMessage = $"Service '{svcName}' is protected",
                        ExecutedAt = DateTime.UtcNow,
                    });
                    continue;
                }
            }

            if (verbose) Console.WriteLine($"[REMEDIATE] Executing task {task.Id}: {task.ActionType}");

            var result = task.ActionType.ToLowerInvariant() switch
            {
                "set_registry" => ExecuteSetRegistry(task),
                "enable_service" => ExecuteServiceAction(task, enable: true),
                "disable_service" => ExecuteServiceAction(task, enable: false),
                "restart_service" => ExecuteRestartService(task),
                "stop_service" => ExecuteStopService(task),
                "set_service_startup" => ExecuteSetServiceStartup(task),
                "set_audit_policy" => ExecuteSetAuditPolicy(task),
                "set_account_policy" => ExecuteSetAccountPolicy(task),
                _ => Fail(task, "Unhandled action type"),
            };

            LogResult(task, result);
            await apiClient.ReportTaskResultAsync(result);
            AgentLogger.Log("REMEDIATE", $"Task {task.Id}: {(result.Success ? "OK" : "FAILED")} {result.ErrorMessage}");
        }
    }

    private static string? ExtractServiceName(string? paramsJson)
    {
        if (string.IsNullOrEmpty(paramsJson)) return null;
        try
        {
            var p = JsonSerializer.Deserialize<ServiceParams>(paramsJson);
            return p?.ServiceName;
        }
        catch { return null; }
    }

    private static TaskResultPayload ExecuteRestartService(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize<ServiceParams>(task.Params ?? "{}");
            if (p is null || string.IsNullOrEmpty(p.ServiceName))
                return Fail(task, "Invalid service params");

            using var sc = new ServiceController(p.ServiceName);
            var previousStatus = sc.Status.ToString();

            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

            sc.Refresh();
            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = JsonSerializer.Serialize(new { serviceName = p.ServiceName, status = previousStatus }),
                NewValue = JsonSerializer.Serialize(new { serviceName = p.ServiceName, status = sc.Status.ToString() }),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    private static TaskResultPayload ExecuteStopService(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize<ServiceParams>(task.Params ?? "{}");
            if (p is null || string.IsNullOrEmpty(p.ServiceName))
                return Fail(task, "Invalid service params");

            using var sc = new ServiceController(p.ServiceName);
            var previousStatus = sc.Status.ToString();

            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }

            sc.Refresh();
            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = JsonSerializer.Serialize(new { serviceName = p.ServiceName, status = previousStatus }),
                NewValue = JsonSerializer.Serialize(new { serviceName = p.ServiceName, status = sc.Status.ToString() }),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    private static TaskResultPayload ExecuteSetServiceStartup(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize<ServiceStartupParams>(task.Params ?? "{}");
            if (p is null || string.IsNullOrEmpty(p.ServiceName) || string.IsNullOrEmpty(p.StartupType))
                return Fail(task, "Invalid service startup params");

            int startValue = p.StartupType.ToLowerInvariant() switch
            {
                "automatic" => 2,
                "manual" => 3,
                "disabled" => 4,
                _ => -1,
            };
            if (startValue < 0) return Fail(task, $"Invalid startup type: {p.StartupType}");

            using var sc = new ServiceController(p.ServiceName);
            var previousStartType = sc.StartType.ToString();

            using var regKey = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{p.ServiceName}", writable: true);
            if (regKey is null) return Fail(task, $"Service registry key not found: {p.ServiceName}");
            regKey.SetValue("Start", startValue, RegistryValueKind.DWord);

            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = JsonSerializer.Serialize(new { serviceName = p.ServiceName, startupType = previousStartType }),
                NewValue = JsonSerializer.Serialize(new { serviceName = p.ServiceName, startupType = p.StartupType }),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    // ── Existing methods (unchanged) ─────────────────────────────────────

    private static TaskResultPayload ExecuteSetRegistry(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize<RegistryParams>(task.Params ?? "{}");
            if (p is null || string.IsNullOrEmpty(p.Path) || string.IsNullOrEmpty(p.ValueName))
                return Fail(task, "Invalid registry params");

            if (!IsPathAllowed(p.Path))
            {
                AgentLogger.Error("REMEDIATE", $"REJECTED registry path outside whitelist: {p.Path}");
                return Fail(task, $"Registry path not in allowed prefixes: {p.Path}");
            }

            var (hive, subPath) = ParseRegistryPath(p.Path);
            if (hive is null) return Fail(task, $"Unsupported hive in path: {p.Path}");

            using var key = hive.OpenSubKey(subPath, writable: true)
                ?? hive.CreateSubKey(subPath);

            var previousValue = key.GetValue(p.ValueName)?.ToString();
            var valueKind = (p.ValueType?.ToUpperInvariant()) switch
            {
                "DWORD" => RegistryValueKind.DWord,
                "QWORD" => RegistryValueKind.QWord,
                "SZ" => RegistryValueKind.String,
                "EXPAND_SZ" => RegistryValueKind.ExpandString,
                "MULTI_SZ" => RegistryValueKind.MultiString,
                _ => RegistryValueKind.String,
            };

            object writeValue = valueKind switch
            {
                RegistryValueKind.DWord => int.Parse(p.ValueData ?? "0"),
                RegistryValueKind.QWord => long.Parse(p.ValueData ?? "0"),
                _ => p.ValueData ?? "",
            };

            key.SetValue(p.ValueName, writeValue, valueKind);
            var newValue = key.GetValue(p.ValueName)?.ToString();

            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = JsonSerializer.Serialize(new { path = p.Path, valueName = p.ValueName, value = previousValue }),
                NewValue = JsonSerializer.Serialize(new { path = p.Path, valueName = p.ValueName, value = newValue }),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    private static TaskResultPayload ExecuteServiceAction(PendingRemediationTask task, bool enable)
    {
        try
        {
            var p = JsonSerializer.Deserialize<ServiceParams>(task.Params ?? "{}");
            if (p is null || string.IsNullOrEmpty(p.ServiceName))
                return Fail(task, "Invalid service params");

            using var sc = new ServiceController(p.ServiceName);
            var previousStatus = sc.Status.ToString();
            var previousStartType = sc.StartType.ToString();

            if (enable)
            {
                using var regKey = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{p.ServiceName}", writable: true);
                if (regKey is not null)
                    regKey.SetValue("Start", 2, RegistryValueKind.DWord);

                sc.Refresh();
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                }
            }
            else
            {
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }
                using var regKey = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{p.ServiceName}", writable: true);
                if (regKey is not null)
                    regKey.SetValue("Start", 4, RegistryValueKind.DWord);
            }

            sc.Refresh();
            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = JsonSerializer.Serialize(new { serviceName = p.ServiceName, status = previousStatus, startType = previousStartType }),
                NewValue = JsonSerializer.Serialize(new { serviceName = p.ServiceName, status = sc.Status.ToString(), startType = sc.StartType.ToString() }),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    private static TaskResultPayload ExecuteSetAuditPolicy(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize<AuditPolicyParams>(task.Params ?? "{}");
            if (p is null || string.IsNullOrEmpty(p.Subcategory))
                return Fail(task, "Invalid audit policy params");

            var auditRegPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Audit";
            using var key = Registry.LocalMachine.OpenSubKey(auditRegPath, writable: true)
                ?? Registry.LocalMachine.CreateSubKey(auditRegPath);

            var previousValue = key.GetValue(p.Subcategory)?.ToString();
            int settingValue = (p.Setting?.ToLowerInvariant()) switch
            {
                "success" => 1,
                "failure" => 2,
                "successandfailure" => 3,
                "none" => 0,
                _ => 3,
            };

            key.SetValue(p.Subcategory, settingValue, RegistryValueKind.DWord);

            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = JsonSerializer.Serialize(new { subcategory = p.Subcategory, value = previousValue }),
                NewValue = JsonSerializer.Serialize(new { subcategory = p.Subcategory, value = settingValue.ToString() }),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    private static TaskResultPayload ExecuteSetAccountPolicy(PendingRemediationTask task) =>
        Fail(task, "set_account_policy not yet implemented — use GPO");

    private static bool IsPathAllowed(string path)
    {
        var normalized = path.Replace("HKEY_LOCAL_MACHINE\\", "HKLM\\")
            .Replace("HKLM:", "HKLM")
            .TrimStart('\\');
        return AllowedRegistryPrefixes.Any(prefix =>
            normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static (RegistryKey? hive, string subPath) ParseRegistryPath(string path)
    {
        var normalized = path.Replace("HKLM:", "HKLM").Replace("HKCU:", "HKCU").TrimStart('\\');
        if (normalized.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, normalized[5..]);
        if (normalized.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, normalized[5..]);
        if (normalized.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, normalized[19..]);
        if (normalized.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, normalized[18..]);
        return (null, path);
    }

    private static TaskResultPayload Fail(PendingRemediationTask task, string error) => new()
    {
        TaskId = task.Id,
        Success = false,
        ErrorMessage = error,
        ExecutedAt = DateTime.UtcNow,
    };

    private static void LogResult(PendingRemediationTask task, TaskResultPayload result)
    {
        try
        {
            var logFile = Path.Combine(LogDir, $"{task.Id}.json");
            var log = JsonSerializer.Serialize(new
            {
                task.Id, task.ActionType, task.Params,
                result.Success, result.PreviousValue, result.NewValue,
                result.ErrorMessage, result.ExecutedAt,
            });
            File.WriteAllText(logFile, log);
        }
        catch { }
    }
}

internal class RegistryParams
{
    public string? Path { get; set; }
    public string? ValueName { get; set; }
    public string? ValueData { get; set; }
    public string? ValueType { get; set; }
}

internal class ServiceParams
{
    public string? ServiceName { get; set; }
}

internal class ServiceStartupParams
{
    public string? ServiceName { get; set; }
    public string? StartupType { get; set; }
}

internal class AuditPolicyParams
{
    public string? Subcategory { get; set; }
    public string? Setting { get; set; }
}
```

- [ ] **Step 2: Build and verify**

```bash
cd KryossAgent && dotnet build src/KryossAgent/KryossAgent.csproj --no-restore -v q
```

- [ ] **Step 3: Commit**

```bash
git add KryossAgent/src/KryossAgent/Services/RemediationExecutor.cs
git commit -m "feat(agent): signature validation + protected services + restart/stop/set_startup actions"
```

---

## Task 7: Agent ServiceHealer + ServiceInventory

**Files:**
- Create: `KryossAgent/src/KryossAgent/Services/ServiceHealer.cs`
- Create: `KryossAgent/src/KryossAgent/Services/ServiceInventory.cs`

- [ ] **Step 1: Create ServiceHealer**

Create `KryossAgent/src/KryossAgent/Services/ServiceHealer.cs`:

```csharp
using System.ServiceProcess;
using KryossAgent.Models;

namespace KryossAgent.Services;

public static class ServiceHealer
{
    public static List<AgentError> HealProtectedServices(List<string>? priorityServices)
    {
        var errors = new List<AgentError>();
        var healTargets = new HashSet<string>(RemediationExecutor.ProtectedServices, StringComparer.OrdinalIgnoreCase);

        if (priorityServices is { Count: > 0 })
            foreach (var s in priorityServices) healTargets.Add(s);

        // Skip self — we're running
        healTargets.Remove("KryossAgent");

        foreach (var serviceName in healTargets)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                // Test if service exists by reading Status
                var status = sc.Status;
                if (status == ServiceControllerStatus.Running) continue;

                var isProtected = RemediationExecutor.ProtectedServices.Contains(serviceName);
                var severity = isProtected ? "ERROR" : "WARN";

                AgentLogger.Log("HEAL", $"{serviceName} is {status} — attempting start");

                var started = false;
                for (int retry = 0; retry < 3 && !started; retry++)
                {
                    try
                    {
                        if (retry > 0) Thread.Sleep(5000);
                        sc.Refresh();
                        if (sc.Status == ServiceControllerStatus.Running) { started = true; break; }
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                        started = true;
                    }
                    catch (Exception ex)
                    {
                        AgentLogger.Error("HEAL", $"{serviceName} start attempt {retry + 1}/3 failed: {ex.Message}");
                    }
                }

                var msg = started
                    ? $"Auto-healed {serviceName} (was {status})"
                    : $"Failed to heal {serviceName} (was {status})";
                AgentLogger.Log("HEAL", msg);

                errors.Add(new AgentError
                {
                    Phase = "service_heal",
                    Message = msg,
                    Timestamp = DateTime.UtcNow,
                    Target = serviceName,
                    IsTimeout = false,
                });
            }
            catch (InvalidOperationException)
            {
                // Service doesn't exist on this machine — skip silently
            }
            catch (Exception ex)
            {
                AgentLogger.Error("HEAL", $"Error checking {serviceName}: {ex.Message}");
            }
        }

        return errors;
    }
}
```

- [ ] **Step 2: Create ServiceInventory**

Create `KryossAgent/src/KryossAgent/Services/ServiceInventory.cs`:

```csharp
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using KryossAgent.Models;

namespace KryossAgent.Services;

public static class ServiceInventory
{
    private static string? _lastHash;

    public static (List<ServiceInfo> services, bool changed) Collect()
    {
        var services = new List<ServiceInfo>();

        try
        {
            foreach (var sc in ServiceController.GetServices())
            {
                try
                {
                    services.Add(new ServiceInfo
                    {
                        Name = sc.ServiceName,
                        DisplayName = sc.DisplayName,
                        Status = sc.Status.ToString(),
                        StartupType = sc.StartType.ToString(),
                    });
                }
                catch { }
                finally { sc.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            AgentLogger.Error("SERVICES", $"Failed to enumerate services: {ex.Message}");
        }

        services.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var json = JsonSerializer.Serialize(services, KryossJsonContext.Default.ListServiceInfo);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var changed = hash != _lastHash;
        _lastHash = hash;

        return (services, changed);
    }
}
```

- [ ] **Step 3: Build and verify**

```bash
cd KryossAgent && dotnet build src/KryossAgent/KryossAgent.csproj --no-restore -v q
```

- [ ] **Step 4: Commit**

```bash
git add KryossAgent/src/KryossAgent/Services/ServiceHealer.cs KryossAgent/src/KryossAgent/Services/ServiceInventory.cs
git commit -m "feat(agent): ServiceHealer auto-heal + ServiceInventory collection"
```

---

## Task 8: Agent ServiceWorker + ApiClient — Wire Healer + Inventory

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Services/ServiceWorker.cs`
- Modify: `KryossAgent/src/KryossAgent/Services/ApiClient.cs`

- [ ] **Step 1: Add SubmitServiceInventoryAsync to ApiClient**

In `ApiClient.cs`, add a new method (follow the pattern of existing submit methods like `ReportTaskResultAsync`):

```csharp
    public async Task SubmitServiceInventoryAsync(List<ServiceInfo> services)
    {
        var json = JsonSerializer.Serialize(services, KryossJsonContext.Default.ListServiceInfo);
        await PostSignedAsync("/v1/services", json);
    }
```

Where `PostSignedAsync` is the existing method that handles HMAC signing. If there's no generic `PostSignedAsync`, find the existing pattern — look at `ReportTaskResultAsync` and replicate:

```csharp
    public async Task SubmitServiceInventoryAsync(List<ServiceInfo> services)
    {
        var body = JsonSerializer.Serialize(services, KryossJsonContext.Default.ListServiceInfo);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await SendSignedAsync(HttpMethod.Post, "/v1/services", content);
        response.EnsureSuccessStatusCode();
    }
```

Adapt to match the exact HTTP sending pattern used by `ReportTaskResultAsync` in the codebase.

- [ ] **Step 2: Wire ServiceHealer + ServiceInventory into HeartbeatLoop**

In `ServiceWorker.cs`, in the `HeartbeatLoop` method, add service healing BEFORE the heartbeat POST, and service inventory AFTER:

After `SetLoopStatus("heartbeat", "running");` (line 111), before building `ApiClient`, add:

```csharp
                // Auto-heal protected + priority services
                List<string>? priorityServices = null;
                try
                {
                    var savedConfig = AgentConfig.Load();
                    priorityServices = savedConfig.PriorityServices;
                    var healErrors = ServiceHealer.HealProtectedServices(priorityServices);
                    foreach (var he in healErrors)
                    {
                        while (_errorQueue.Count >= MaxErrorQueueSize) _errorQueue.TryDequeue(out _);
                        _errorQueue.Enqueue(he);
                    }
                }
                catch (Exception ex)
                {
                    AgentLogger.Error("HEARTBEAT", $"Service healer failed: {ex.Message}");
                }
```

After the remediation executor call (after `await RemediationExecutor.ExecuteTasksAsync(...)`, ~line 153), add:

```csharp
                // Submit service inventory (only when changed)
                try
                {
                    var (services, changed) = ServiceInventory.Collect();
                    if (changed && services.Count > 0)
                    {
                        await client.SubmitServiceInventoryAsync(services);
                        AgentLogger.Log("HEARTBEAT", $"Service inventory submitted ({services.Count} services)");
                    }
                }
                catch (Exception ex)
                {
                    AgentLogger.Error("HEARTBEAT", $"Service inventory submit failed: {ex.Message}");
                }
```

Also: when heartbeat response contains config with PriorityServices, save to AgentConfig. After config update logic (if response has config), add:

```csharp
                // Save priority services from heartbeat config
                if (response?.Config?.PriorityServices is not null)
                {
                    var cfg = AgentConfig.Load();
                    cfg.PriorityServices = response.Config.PriorityServices;
                    cfg.Save();
                }
```

Note: This requires AgentConfig to have a `PriorityServices` property and `Save()` method. Check `AgentConfig.cs` — if PriorityServices isn't stored in registry, store as JSON string in a registry value `PriorityServices`. The simplest path: save the heartbeat config PriorityServices in a field on ServiceWorker or read from last heartbeat response each heal cycle.

Simpler approach — pass config from last heartbeat response directly. Store as a field:

In ServiceWorker, add a field:
```csharp
    private volatile List<string>? _priorityServices;
```

When heartbeat response arrives with config:
```csharp
                if (response?.Config?.PriorityServices is not null)
                    _priorityServices = response.Config.PriorityServices;
```

In the heal section at top of loop, use `_priorityServices` instead of loading from config:
```csharp
                var healErrors = ServiceHealer.HealProtectedServices(_priorityServices);
```

- [ ] **Step 3: Build and verify**

```bash
cd KryossAgent && dotnet build src/KryossAgent/KryossAgent.csproj --no-restore -v q
```

- [ ] **Step 4: Commit**

```bash
git add KryossAgent/src/KryossAgent/Services/ServiceWorker.cs KryossAgent/src/KryossAgent/Services/ApiClient.cs
git commit -m "feat(agent): wire ServiceHealer + ServiceInventory into HeartbeatLoop"
```

---

## Task 9: API ServiceInventoryFunction + ServiceManagementFunction

**Files:**
- Create: `KryossApi/src/KryossApi/Functions/Agent/ServiceInventoryFunction.cs`
- Create: `KryossApi/src/KryossApi/Functions/Portal/ServiceManagementFunction.cs`

- [ ] **Step 1: Create ServiceInventoryFunction (agent endpoint)**

Create `KryossApi/src/KryossApi/Functions/Agent/ServiceInventoryFunction.cs`:

```csharp
using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Agent;

public class ServiceInventoryFunction
{
    private readonly KryossDbContext _db;

    public ServiceInventoryFunction(KryossDbContext db) => _db = db;

    [Function("ServiceInventory")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/services")] HttpRequestData req)
    {
        var agentIdHeader = req.Headers.TryGetValues("X-Agent-Id", out var vals)
            ? vals.FirstOrDefault() : null;
        if (!Guid.TryParse(agentIdHeader, out var agentId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "X-Agent-Id header required" });
            return bad;
        }

        var machine = await _db.Machines.AsNoTracking()
            .Where(m => m.AgentId == agentId)
            .Select(m => new { m.Id })
            .FirstOrDefaultAsync();
        if (machine is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        List<ServiceEntry>? services = null;
        var context = req.FunctionContext;
        if (context.Items.TryGetValue("RequestBodyBytes", out var rawObj) && rawObj is byte[] rawBytes && rawBytes.Length > 0)
            services = JsonSerializer.Deserialize<List<ServiceEntry>>(rawBytes);
        else if (req.Body.CanSeek)
        {
            req.Body.Position = 0;
            services = await req.ReadFromJsonAsync<List<ServiceEntry>>();
        }

        if (services is null or { Count: 0 })
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "empty service list" });
            return bad;
        }

        var existing = await _db.MachineServices
            .Where(s => s.MachineId == machine.Id)
            .ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        foreach (var svc in services)
        {
            if (string.IsNullOrEmpty(svc.Name)) continue;

            if (existing.TryGetValue(svc.Name, out var row))
            {
                row.DisplayName = svc.DisplayName;
                row.Status = svc.Status ?? "Unknown";
                row.StartupType = svc.StartupType ?? "Unknown";
                row.UpdatedAt = now;
            }
            else
            {
                _db.MachineServices.Add(new MachineService
                {
                    MachineId = machine.Id,
                    Name = svc.Name,
                    DisplayName = svc.DisplayName,
                    Status = svc.Status ?? "Unknown",
                    StartupType = svc.StartupType ?? "Unknown",
                    UpdatedAt = now,
                });
            }
        }

        // Remove services no longer present
        var currentNames = new HashSet<string>(services.Where(s => !string.IsNullOrEmpty(s.Name)).Select(s => s.Name!), StringComparer.OrdinalIgnoreCase);
        foreach (var kv in existing)
        {
            if (!currentNames.Contains(kv.Key))
                _db.MachineServices.Remove(kv.Value);
        }

        await _db.SaveChangesAsync();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { ack = true, count = services.Count });
        return ok;
    }
}

internal class ServiceEntry
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Status { get; set; }
    public string? StartupType { get; set; }
}
```

- [ ] **Step 2: Create ServiceManagementFunction (portal endpoints)**

Create `KryossApi/src/KryossApi/Functions/Portal/ServiceManagementFunction.cs`:

```csharp
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

    public ServiceManagementFunction(KryossDbContext db, ICurrentUserService user, IRemediationLogService remLog)
    {
        _db = db;
        _user = user;
        _remLog = remLog;
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
            .OrderBy(s => s.Name)
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

        // Get org priority services
        var orgId = await _db.Machines.Where(m => m.Id == mid).Select(m => m.OrganizationId).FirstOrDefaultAsync();
        var orgPsJson = await _db.Organizations.Where(o => o.Id == orgId).Select(o => o.PriorityServicesJson).FirstOrDefaultAsync();
        var prioritySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(orgPsJson))
        {
            try { var ps = JsonSerializer.Deserialize<List<string>>(orgPsJson); if (ps is not null) foreach (var s in ps) prioritySet.Add(s); }
            catch { }
        }

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

        // Check priority services for stop/disable
        if (actionType == "stop_service")
        {
            var orgId = await _db.Machines.Where(m => m.Id == mid).Select(m => m.OrganizationId).FirstOrDefaultAsync();
            var orgPsJson = await _db.Organizations.Where(o => o.Id == orgId).Select(o => o.PriorityServicesJson).FirstOrDefaultAsync();
            if (!string.IsNullOrEmpty(orgPsJson))
            {
                try
                {
                    var ps = JsonSerializer.Deserialize<List<string>>(orgPsJson);
                    if (ps?.Any(s => s.Equals(serviceName, StringComparison.OrdinalIgnoreCase)) == true)
                    {
                        var forbidden = req.CreateResponse(HttpStatusCode.BadRequest);
                        await forbidden.WriteAsJsonAsync(new { error = $"Service '{serviceName}' is a priority service and cannot be stopped" });
                        return forbidden;
                    }
                }
                catch { }
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
        catch { }

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
        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId);
        if (org is null) return req.CreateResponse(HttpStatusCode.NotFound);

        var current = new List<string>();
        if (!string.IsNullOrEmpty(org.PriorityServicesJson))
        {
            try { current = JsonSerializer.Deserialize<List<string>>(org.PriorityServicesJson) ?? new(); }
            catch { }
        }

        if (body.Enable)
        {
            if (!current.Any(s => s.Equals(body.ServiceName, StringComparison.OrdinalIgnoreCase)))
                current.Add(body.ServiceName);
        }
        else
        {
            current.RemoveAll(s => s.Equals(body.ServiceName, StringComparison.OrdinalIgnoreCase));
        }

        org.PriorityServicesJson = current.Count > 0 ? JsonSerializer.Serialize(current) : null;
        await _db.SaveChangesAsync();

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
```

- [ ] **Step 3: Build and verify**

```bash
cd KryossApi && dotnet build src/KryossApi/KryossApi.csproj --no-restore -v q
```

- [ ] **Step 4: Commit**

```bash
git add KryossApi/src/KryossApi/Functions/Agent/ServiceInventoryFunction.cs KryossApi/src/KryossApi/Functions/Portal/ServiceManagementFunction.cs
git commit -m "feat(api): service inventory + service management + priority toggle endpoints"
```

---

## Task 10: API MachinesFunction — Activity Endpoint

**Files:**
- Modify: `KryossApi/src/KryossApi/Functions/Portal/MachinesFunction.cs`

- [ ] **Step 1: Add activity endpoint**

Add a new function method to MachinesFunction:

```csharp
    [Function("Machine_Activity")]
    [RequirePermission("machines:read")]
    public async Task<HttpResponseData> Activity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/machines/{id}/activity")] HttpRequestData req,
        string id)
    {
        if (!Guid.TryParse(id, out var machineId))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var page = int.TryParse(qs["page"], out var p) ? p : 1;
        var pageSize = int.TryParse(qs["pageSize"], out var ps) ? Math.Min(ps, 100) : 50;
        var typeFilter = qs["type"];
        var severityFilter = qs["severity"];

        // Source 1: actlog entries for this machine
        var actlogQuery = _db.Actlog
            .Where(a => a.EntityType == "machine" && a.EntityId == machineId.ToString())
            .Select(a => new ActivityEntry
            {
                Timestamp = a.Timestamp,
                Type = a.Action,
                Severity = a.Severity,
                Action = a.Message ?? a.Action,
                ActorEmail = a.ActorEmail,
                Source = "actlog",
            });

        // Source 2: remediation_log entries for this machine
        var remlogQuery = _db.RemediationLogs
            .Where(r => r.MachineId == machineId)
            .Select(r => new ActivityEntry
            {
                Timestamp = r.Timestamp,
                Type = r.EventType,
                Severity = r.EventType == "rejected" ? "ERR"
                    : r.EventType == "failed" ? "WARN"
                    : r.EventType == "service_heal" ? "WARN"
                    : "INFO",
                Action = r.ServiceName != null
                    ? $"{r.EventType}: {r.ActionType} on {r.ServiceName}"
                    : $"{r.EventType}: {r.ActionType}",
                ActorEmail = null,
                Source = "remediation",
                ServiceName = r.ServiceName,
                ErrorMessage = r.ErrorMessage,
            });

        // Union and sort
        var combined = actlogQuery.Union(remlogQuery);

        if (!string.IsNullOrEmpty(typeFilter))
            combined = combined.Where(a => a.Type == typeFilter);
        if (!string.IsNullOrEmpty(severityFilter))
            combined = combined.Where(a => a.Severity == severityFilter);

        var total = await combined.CountAsync();
        var items = await combined
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { total, page, pageSize, items });
        return ok;
    }
```

Add the ActivityEntry class at the bottom of the file (or inside the class as a private class):

```csharp
internal class ActivityEntry
{
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string? ActorEmail { get; set; }
    public string Source { get; set; } = null!;
    public string? ServiceName { get; set; }
    public string? ErrorMessage { get; set; }
}
```

Note: EF Core may not support Union across different entity types in a single LINQ query. If the build fails on the Union, split into two separate queries and merge in-memory:

```csharp
        var actlogItems = await _db.Actlog
            .Where(a => a.EntityType == "machine" && a.EntityId == machineId.ToString())
            .OrderByDescending(a => a.Timestamp)
            .Take(pageSize * 2)
            .Select(a => new ActivityEntry
            {
                Timestamp = a.Timestamp,
                Type = a.Action,
                Severity = a.Severity,
                Action = a.Message ?? a.Action,
                ActorEmail = a.ActorEmail,
                Source = "actlog",
            })
            .ToListAsync();

        var remlogItems = await _db.RemediationLogs
            .Where(r => r.MachineId == machineId)
            .OrderByDescending(r => r.Timestamp)
            .Take(pageSize * 2)
            .Select(r => new ActivityEntry
            {
                Timestamp = r.Timestamp,
                Type = r.EventType,
                Severity = r.EventType == "rejected" ? "ERR"
                    : r.EventType == "failed" ? "WARN"
                    : r.EventType == "service_heal" ? "WARN"
                    : "INFO",
                Action = r.ServiceName != null
                    ? $"{r.EventType}: {r.ActionType} on {r.ServiceName}"
                    : $"{r.EventType}: {r.ActionType}",
                Source = "remediation",
                ServiceName = r.ServiceName,
                ErrorMessage = r.ErrorMessage,
            })
            .ToListAsync();

        var combined = actlogItems.Concat(remlogItems)
            .OrderByDescending(a => a.Timestamp);

        if (!string.IsNullOrEmpty(typeFilter))
            combined = combined.Where(a => a.Type == typeFilter).OrderByDescending(a => a.Timestamp);
        if (!string.IsNullOrEmpty(severityFilter))
            combined = combined.Where(a => a.Severity == severityFilter).OrderByDescending(a => a.Timestamp);

        var allItems = combined.ToList();
        var total = allItems.Count;
        var items = allItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();
```

- [ ] **Step 2: Build and verify**

```bash
cd KryossApi && dotnet build src/KryossApi/KryossApi.csproj --no-restore -v q
```

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Functions/Portal/MachinesFunction.cs
git commit -m "feat(api): machine activity timeline endpoint (actlog + remediation_log union)"
```

---

## Task 11: Portal API Hooks

**Files:**
- Create: `KryossPortal/src/api/services.ts`
- Modify: `KryossPortal/src/api/machines.ts`

- [ ] **Step 1: Create services.ts API hooks**

Create `KryossPortal/src/api/services.ts`:

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface ServiceItem {
  name: string;
  displayName: string | null;
  status: string;
  startupType: string;
  updatedAt: string;
  isProtected: boolean;
  isPriority: boolean;
}

interface ServicesResponse {
  total: number;
  items: ServiceItem[];
}

export function useMachineServices(machineId: string | undefined) {
  return useQuery({
    queryKey: ['machine-services', machineId],
    queryFn: () => apiFetch<ServicesResponse>(`/v2/machines/${machineId}/services`),
    enabled: !!machineId,
    refetchInterval: 30_000,
  });
}

export function useServiceAction(machineId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ serviceName, action }: { serviceName: string; action: 'start' | 'stop' | 'restart' }) =>
      apiFetch<{ taskId: number }>(`/v2/machines/${machineId}/services/${encodeURIComponent(serviceName)}/action`, {
        method: 'POST',
        body: JSON.stringify({ action }),
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['machine-services', machineId] });
      qc.invalidateQueries({ queryKey: ['machine-activity', machineId] });
    },
  });
}

export function useTogglePriority(machineId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ serviceName, enable }: { serviceName: string; enable: boolean }) =>
      apiFetch<{ priorityServices: string[] }>(`/v2/machines/${machineId}/priority-services`, {
        method: 'PATCH',
        body: JSON.stringify({ serviceName, enable }),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['machine-services', machineId] }),
  });
}

export interface ActivityItem {
  timestamp: string;
  type: string;
  severity: string;
  action: string;
  actorEmail: string | null;
  source: string;
  serviceName: string | null;
  errorMessage: string | null;
}

interface ActivityResponse {
  total: number;
  page: number;
  pageSize: number;
  items: ActivityItem[];
}

export function useMachineActivity(machineId: string | undefined, page = 1) {
  return useQuery({
    queryKey: ['machine-activity', machineId, page],
    queryFn: () => apiFetch<ActivityResponse>(`/v2/machines/${machineId}/activity?page=${page}&pageSize=50`),
    enabled: !!machineId,
    refetchInterval: 30_000,
  });
}
```

- [ ] **Step 2: Build and verify**

```bash
cd KryossPortal && npx tsc --noEmit 2>&1 | head -20
```

- [ ] **Step 3: Commit**

```bash
git add KryossPortal/src/api/services.ts
git commit -m "feat(portal): API hooks for services + activity endpoints"
```

---

## Task 12: Portal ServicesTab Component

**Files:**
- Create: `KryossPortal/src/components/machines/ServicesTab.tsx`

- [ ] **Step 1: Create ServicesTab**

Create `KryossPortal/src/components/machines/ServicesTab.tsx`:

```tsx
import { useState } from 'react';
import { toast } from 'sonner';
import { Search, Lock, Star, Play, Square, RotateCw } from 'lucide-react';
import { useMachineServices, useServiceAction, useTogglePriority } from '@/api/services';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { Switch } from '@/components/ui/switch';
import {
  Table, TableHeader, TableRow, TableHead, TableBody, TableCell,
} from '@/components/ui/table';
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from '@/components/ui/alert-dialog';

interface Props {
  machineId: string | undefined;
  hostname: string;
}

export function ServicesTab({ machineId, hostname }: Props) {
  const { data, isLoading } = useMachineServices(machineId);
  const serviceAction = useServiceAction(machineId);
  const togglePriority = useTogglePriority(machineId);
  const [search, setSearch] = useState('');
  const [confirm, setConfirm] = useState<{ name: string; displayName: string | null; action: 'start' | 'stop' | 'restart' } | null>(null);

  const items = data?.items ?? [];
  const filtered = items.filter(s =>
    s.name.toLowerCase().includes(search.toLowerCase()) ||
    (s.displayName?.toLowerCase().includes(search.toLowerCase()) ?? false)
  );

  const stoppedFirst = [...filtered].sort((a, b) => {
    if (a.status === 'Stopped' && b.status !== 'Stopped') return -1;
    if (a.status !== 'Stopped' && b.status === 'Stopped') return 1;
    if (a.isPriority && !b.isPriority) return -1;
    if (!a.isPriority && b.isPriority) return 1;
    return a.name.localeCompare(b.name);
  });

  function handleAction(name: string, displayName: string | null, action: 'start' | 'stop' | 'restart') {
    setConfirm({ name, displayName, action });
  }

  function executeAction() {
    if (!confirm) return;
    serviceAction.mutate(
      { serviceName: confirm.name, action: confirm.action },
      {
        onSuccess: () => toast.success(`${confirm.action} queued for ${confirm.displayName ?? confirm.name}`),
        onError: (e: any) => toast.error(e?.message ?? 'Failed'),
      }
    );
    setConfirm(null);
  }

  if (isLoading) return <div className="text-muted-foreground text-sm p-4">Loading services...</div>;

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <div className="relative flex-1">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input placeholder="Search services..." value={search} onChange={e => setSearch(e.target.value)} className="pl-8" />
        </div>
        <Badge variant="outline">{items.length} services</Badge>
      </div>

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Startup</TableHead>
              <TableHead>Priority</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {stoppedFirst.map(s => (
              <TableRow key={s.name}>
                <TableCell>
                  <div className="flex items-center gap-2">
                    {s.isProtected && <Lock className="h-3.5 w-3.5 text-muted-foreground" />}
                    <div>
                      <div className="font-medium text-sm">{s.displayName ?? s.name}</div>
                      <div className="text-xs text-muted-foreground">{s.name}</div>
                    </div>
                  </div>
                </TableCell>
                <TableCell>
                  <Badge variant="outline" className={
                    s.status === 'Running' ? 'bg-green-100 text-green-800' :
                    s.status === 'Stopped' ? 'bg-red-100 text-red-800' :
                    'bg-amber-100 text-amber-800'
                  }>
                    <span className={`mr-1.5 inline-block h-2 w-2 rounded-full ${
                      s.status === 'Running' ? 'bg-green-500' :
                      s.status === 'Stopped' ? 'bg-red-500' :
                      'bg-amber-500'
                    }`} />
                    {s.status}
                  </Badge>
                </TableCell>
                <TableCell className="text-sm">{s.startupType}</TableCell>
                <TableCell>
                  {s.isProtected ? (
                    <Badge variant="secondary"><Lock className="h-3 w-3 mr-1" />Protected</Badge>
                  ) : (
                    <Switch
                      checked={s.isPriority}
                      onCheckedChange={(checked) =>
                        togglePriority.mutate(
                          { serviceName: s.name, enable: checked },
                          { onError: (e: any) => toast.error(e?.message ?? 'Failed') }
                        )
                      }
                    />
                  )}
                </TableCell>
                <TableCell className="text-right">
                  {s.isProtected ? (
                    <span className="text-xs text-muted-foreground">Protected</span>
                  ) : (
                    <div className="flex gap-1 justify-end">
                      {s.status === 'Stopped' && (
                        <Button size="sm" variant="outline" onClick={() => handleAction(s.name, s.displayName, 'start')}>
                          <Play className="h-3.5 w-3.5 mr-1" />Start
                        </Button>
                      )}
                      {s.status === 'Running' && (
                        <>
                          <Button size="sm" variant="outline" onClick={() => handleAction(s.name, s.displayName, 'restart')}>
                            <RotateCw className="h-3.5 w-3.5 mr-1" />Restart
                          </Button>
                          <Button size="sm" variant="outline" onClick={() => handleAction(s.name, s.displayName, 'stop')}>
                            <Square className="h-3.5 w-3.5 mr-1" />Stop
                          </Button>
                        </>
                      )}
                    </div>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      <AlertDialog open={!!confirm} onOpenChange={() => setConfirm(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Confirm Service Action</AlertDialogTitle>
            <AlertDialogDescription>
              {confirm?.action} service <strong>{confirm?.displayName ?? confirm?.name}</strong> on <strong>{hostname}</strong>?
              This action will be queued and executed on the next agent heartbeat.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={executeAction}>Confirm</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
```

- [ ] **Step 2: Build and verify**

```bash
cd KryossPortal && npx tsc --noEmit 2>&1 | head -20
```

- [ ] **Step 3: Commit**

```bash
git add KryossPortal/src/components/machines/ServicesTab.tsx
git commit -m "feat(portal): ServicesTab with Start/Stop/Restart + Priority toggle"
```

---

## Task 13: Portal ActivityTab Component

**Files:**
- Create: `KryossPortal/src/components/machines/ActivityTab.tsx`

- [ ] **Step 1: Create ActivityTab**

Create `KryossPortal/src/components/machines/ActivityTab.tsx`:

```tsx
import { useState } from 'react';
import { Pulse, ShieldCheck, Wrench, Cog, AlertTriangle, Settings, ChevronLeft, ChevronRight } from 'lucide-react';
import { useMachineActivity } from '@/api/services';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';

interface Props {
  machineId: string | undefined;
}

const TYPE_ICONS: Record<string, typeof Pulse> = {
  'post.heartbeat': Pulse,
  heartbeat: Pulse,
  'post.results': ShieldCheck,
  compliance: ShieldCheck,
  service_heal: Cog,
  service_action: Cog,
  created: Wrench,
  dispatched: Wrench,
  executed: Wrench,
  completed: Wrench,
  failed: AlertTriangle,
  rejected: AlertTriangle,
  rolled_back: Wrench,
};

const SEVERITY_COLORS: Record<string, string> = {
  INFO: 'bg-gray-100 text-gray-700',
  WARN: 'bg-amber-100 text-amber-800',
  ERR: 'bg-red-100 text-red-800',
  ERROR: 'bg-red-100 text-red-800',
  CRIT: 'bg-red-200 text-red-900',
};

function formatTimestamp(ts: string): string {
  return new Date(ts).toLocaleString(undefined, {
    month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  });
}

export function ActivityTab({ machineId }: Props) {
  const [page, setPage] = useState(1);
  const [severityFilter, setSeverityFilter] = useState<string>('all');
  const { data, isLoading } = useMachineActivity(machineId, page);

  const items = data?.items ?? [];
  const total = data?.total ?? 0;
  const totalPages = Math.ceil(total / 50);

  if (isLoading) return <div className="text-muted-foreground text-sm p-4">Loading activity...</div>;

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Select value={severityFilter} onValueChange={v => { setSeverityFilter(v); setPage(1); }}>
          <SelectTrigger className="w-[140px]">
            <SelectValue placeholder="Severity" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All</SelectItem>
            <SelectItem value="INFO">Info</SelectItem>
            <SelectItem value="WARN">Warning</SelectItem>
            <SelectItem value="ERR">Error</SelectItem>
          </SelectContent>
        </Select>
        <Badge variant="outline">{total} events</Badge>
      </div>

      <div className="space-y-1">
        {items.length === 0 && (
          <div className="text-center text-muted-foreground py-8">No activity found</div>
        )}
        {items.map((item, i) => {
          const Icon = TYPE_ICONS[item.type] ?? Settings;
          const sevClass = SEVERITY_COLORS[item.severity] ?? SEVERITY_COLORS.INFO;
          return (
            <div key={`${item.timestamp}-${i}`} className="flex items-start gap-3 py-2 px-3 rounded-md hover:bg-muted/50 border-b last:border-0">
              <div className="mt-0.5">
                <Icon className="h-4 w-4 text-muted-foreground" />
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <Badge variant="outline" className={`text-xs ${sevClass}`}>{item.severity}</Badge>
                  <span className="text-sm font-medium truncate">{item.action}</span>
                </div>
                {item.errorMessage && (
                  <div className="text-xs text-red-600 mt-0.5 truncate">{item.errorMessage}</div>
                )}
                {item.actorEmail && (
                  <div className="text-xs text-muted-foreground mt-0.5">{item.actorEmail}</div>
                )}
              </div>
              <div className="text-xs text-muted-foreground whitespace-nowrap">
                {formatTimestamp(item.timestamp)}
              </div>
            </div>
          );
        })}
      </div>

      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-2">
          <Button size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <span className="text-sm text-muted-foreground">Page {page} of {totalPages}</span>
          <Button size="sm" variant="outline" disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}>
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Build and verify**

```bash
cd KryossPortal && npx tsc --noEmit 2>&1 | head -20
```

- [ ] **Step 3: Commit**

```bash
git add KryossPortal/src/components/machines/ActivityTab.tsx
git commit -m "feat(portal): ActivityTab — unified machine timeline"
```

---

## Task 14: Portal MachineDetail — Wire New Tabs

**Files:**
- Modify: `KryossPortal/src/components/machines/MachineDetail.tsx`

- [ ] **Step 1: Add imports**

At the top of MachineDetail.tsx, add:

```typescript
import { Cog, ScrollText } from 'lucide-react';
import { ServicesTab } from './ServicesTab';
import { ActivityTab } from './ActivityTab';
```

- [ ] **Step 2: Add tab triggers**

After the `history` TabsTrigger (around line 1047), add:

```tsx
          <TabsTrigger value="services" className="gap-1.5">
            <Cog className="h-4 w-4" />Services
          </TabsTrigger>
          <TabsTrigger value="activity" className="gap-1.5">
            <ScrollText className="h-4 w-4" />Activity
          </TabsTrigger>
```

- [ ] **Step 3: Add tab content**

After the last TabsContent (history), add:

```tsx
        <TabsContent value="services">
          <ServicesTab machineId={machine?.id} hostname={machine?.hostname ?? ''} />
        </TabsContent>
        <TabsContent value="activity">
          <ActivityTab machineId={machine?.id} />
        </TabsContent>
```

- [ ] **Step 4: Build and verify**

```bash
cd KryossPortal && npm run build 2>&1 | tail -10
```
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add KryossPortal/src/components/machines/MachineDetail.tsx
git commit -m "feat(portal): wire Services + Activity tabs into MachineDetail"
```

---

## Task 15: Version Bumps + CLAUDE.md + ROADMAP

**Files:**
- Modify: `KryossAgent/src/KryossAgent/KryossAgent.csproj` — version 2.8.1 → 2.9.0
- Modify: `KryossApi/src/KryossApi/KryossApi.csproj` — version 1.30.1 → 1.31.0
- Modify: `KryossPortal/package.json` — version 1.17.0 → 1.18.0
- Modify: `CLAUDE.md` — version table + decision log entry
- Modify: `KryossAgent/CLAUDE.md` — header version + ServiceHealer/ServiceInventory in folder layout
- Modify: `KryossApi/CLAUDE.md` — new endpoints + entities
- Modify: `docs/superpowers/plans/ROADMAP.md` — shipped session entry + snapshot

- [ ] **Step 1: Bump all versions**

Agent csproj: `<Version>2.8.1</Version>` → `<Version>2.9.0</Version>`
API csproj: `<Version>1.30.1</Version>` → `<Version>1.31.0</Version>`
Portal package.json: `"version": "1.17.0"` → `"version": "1.18.0"`

- [ ] **Step 2: Update CLAUDE.md version table**

```
| **API** | 1.31.0 | ...
| **Portal** | 1.18.0 | ...
| **Agent** | 2.9.0 | ...
```

- [ ] **Step 3: Add decision log entry to CLAUDE.md**

```
| 2026-04-27 | SEC-REM-FW + SVC-MGR: Secure Remediation Framework + Service Management | Per-task HMAC-SHA256 signatures (machine_secret), INSERT-only `remediation_log` audit trail, ProtectedServices block list (16 OS core services) + auto-heal every 15min, org-configurable Priority Services with auto-heal, service inventory via `machine_services` table, portal Services tab (Start/Stop/Restart + Priority toggle), Machine Activity timeline tab (actlog + remediation_log union). 3 new action types (restart_service, stop_service, set_service_startup). SQL: `077_remediation_hardening.sql`. API 1.31.0, Agent 2.9.0, Portal 1.18.0. |
```

- [ ] **Step 4: Update ROADMAP shipped section**

Add to shipped entries and update snapshot versions.

- [ ] **Step 5: Build all 3 components**

```bash
cd KryossAgent && dotnet build src/KryossAgent/KryossAgent.csproj --no-restore -v q
cd KryossApi && dotnet build src/KryossApi/KryossApi.csproj --no-restore -v q
cd KryossPortal && npm run build
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: SEC-REM-FW + SVC-MGR — task signatures, audit trail, service management (Agent 2.9.0, API 1.31.0, Portal 1.18.0)"
```

---

## Self-Review

**Spec coverage check:**
- ✅ 1A Task Signatures — Task 3 (HeartbeatFunction signing) + Task 6 (agent validation)
- ✅ 1B Immutable Audit Trail — Task 2 (entities + service) + Task 4 (write points)
- ✅ 1C Protected Services — Task 6 (block list + validation) + Task 7 (ServiceHealer auto-heal)
- ✅ 1C Priority Services — Task 3 (config delivery) + Task 8 (ServiceWorker wiring) + Task 9 (API toggle)
- ✅ 2A Agent Service Inventory — Task 7 (ServiceInventory.cs) + Task 8 (wired into HeartbeatLoop)
- ✅ 2B API Service Endpoints — Task 9 (ServiceInventoryFunction + ServiceManagementFunction)
- ✅ 2C Portal Services Tab — Task 12 (ServicesTab.tsx) + Task 14 (wired into MachineDetail)
- ✅ 3A API Activity Endpoint — Task 10 (MachinesFunction.Activity)
- ✅ 3B Portal Activity Tab — Task 13 (ActivityTab.tsx) + Task 14 (wired into MachineDetail)
- ✅ SQL Migration — Task 1 (077_remediation_hardening.sql)
- ✅ Version Bumps — Task 15
- ✅ Backward Compatibility — Task 3 (unsigned tasks for legacy agents) + Task 6 (signature=null handling)

**Placeholder scan:** No TBD/TODO found.

**Type consistency check:**
- `ServiceInfo` (agent model) matches `ServiceEntry` (API deserialization) fields: name, displayName, status, startupType ✅
- `PendingRemediationTask` gains `Signature` + `ApprovedAt` — used consistently in Tasks 3, 5, 6 ✅
- `RemediationLog` entity matches SQL schema ✅
- `ActivityEntry` fields match portal `ActivityItem` type ✅
- `ProtectedServices` HashSet identical in agent (RemediationExecutor.cs) and API (ServiceManagementFunction.cs) ✅
