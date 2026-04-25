# SH-KEY: Agent Key Rotation + Expiration + Rate Limiting

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kerberos-inspired per-machine auth with automatic key rotation via heartbeat, covering SH-01 (rate limiting), SH-03 (key expiration), and SH-09 (key rotation) from the roadmap.

**Architecture:** Three-layer credential model: (1) enrollment code (one-time), (2) machine_secret (long-term, set at enrollment, used for reauth), (3) session_key (short-lived 48h, rotated via heartbeat). The org's `api_key` remains an identifier (like a Kerberos realm). HMAC signing moves from org-level secret to per-machine session key. Middleware validates against machine keys first, falls back to org secret for backward compat with pre-v2.2 agents. Rate limiting enhanced with enrollment throttle (5/15min per IP) and per-org aggregate limit.

**Tech Stack:** .NET 8, EF Core 8, Azure Functions v4 isolated worker, System.Security.Cryptography (CSPRNG), source-generated JSON (AOT)

**Covers:** SH-01, SH-03, SH-09 from roadmap. Version bump: API 1.20.0, Agent 2.2.0.

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `KryossApi/sql/067_machine_auth_keys.sql` | Migration: auth columns on machines |
| Create | `KryossApi/src/KryossApi/Services/KeyRotationService.cs` | Generate, rotate, validate per-machine keys |
| Modify | `KryossApi/src/KryossApi/Data/KryossDbContext.cs` | Column mappings for new machine fields |
| Modify | `KryossApi/src/KryossApi/Middleware/ApiKeyAuthMiddleware.cs` | Machine-level HMAC + enrollment rate limit |
| Modify | `KryossApi/src/KryossApi/Functions/Agent/EnrollFunction.cs` | Return per-machine credentials |
| Modify | `KryossApi/src/KryossApi/Functions/Agent/HeartbeatFunction.cs` | Key rotation in heartbeat response |
| Modify | `KryossApi/src/KryossApi/Program.cs` | Register KeyRotationService in DI |
| Modify | `KryossAgent/src/KryossAgent/Config/AgentConfig.cs` | Store machine_secret + session_key in registry |
| Modify | `KryossAgent/src/KryossAgent/Services/ApiClient.cs` | Sign with session_key, handle rotation |
| Modify | `KryossAgent/src/KryossAgent/Models/RemediationModels.cs` | Extend HeartbeatResponse with key fields |
| Modify | `KryossAgent/src/KryossAgent/Models/JsonContext.cs` | AOT source gen for new types |
| Modify | `KryossApi/src/KryossApi/KryossApi.csproj` | Version 1.20.0 |
| Modify | `KryossAgent/src/KryossAgent/KryossAgent.csproj` | Version 2.2.0 |

---

## Security Model

```
┌─────────────────────────────────────────────────────┐
│                   ENROLLMENT                         │
│  Agent presents: enrollment_code + hostname + hwid   │
│  Server returns: machine_secret + session_key (48h)  │
│  (one-time, code is consumed)                        │
└─────────────────────┬───────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────┐
│               NORMAL OPERATION                       │
│  Agent signs with: session_key (short-lived)         │
│  Server validates: machine.session_key HMAC          │
│  Heartbeat returns: new session_key when >50% aged   │
└─────────────────────┬───────────────────────────────┘
                      │ session_key expired?
                      ▼
┌─────────────────────────────────────────────────────┐
│                  REAUTH                              │
│  Agent signs with: machine_secret (long-term)        │
│  Server detects: session expired, secret valid       │
│  Heartbeat returns: fresh session_key                │
└─────────────────────────────────────────────────────┘
```

**Validation chain in middleware:**
1. Try `machine.session_key` → if valid, accept
2. Try `machine.prev_session_key` (grace period 24h) → if valid, accept
3. Try `machine.machine_secret` (reauth) → if valid, accept + flag for forced rotation
4. Try `org.api_secret` (backward compat for agents < v2.2) → if valid, accept
5. All fail → 401

**Rate limiting layers:**
- Enrollment: 5 attempts / 15 min per source IP (new)
- Per-machine: 15 requests / 1 min per X-Agent-Id (existing, keep)
- Per-org: 200 requests / 1 min per org (new aggregate cap)

---

### Task 1: SQL Migration

**Files:**
- Create: `KryossApi/sql/067_machine_auth_keys.sql`

- [ ] **Step 1: Create migration file**

```sql
-- 067: Per-machine auth keys for Kerberos-inspired rotation
-- Adds machine_secret (long-term), session_key (48h), prev_session_key (grace)

ALTER TABLE machines ADD
    machine_secret       NVARCHAR(128)  NULL,
    session_key          NVARCHAR(128)  NULL,
    session_key_expires_at DATETIME2    NULL,
    prev_session_key     NVARCHAR(128)  NULL,
    prev_key_expires_at  DATETIME2      NULL,
    key_rotated_at       DATETIME2      NULL,
    auth_version         INT            NOT NULL DEFAULT 1;

-- auth_version: 1 = legacy org-key, 2 = per-machine session keys
-- Agents < v2.2 stay at auth_version=1, middleware falls back to org secret

-- Index for middleware lookup (session_key is checked on every request)
CREATE NONCLUSTERED INDEX IX_machines_session_key
    ON machines (session_key)
    WHERE session_key IS NOT NULL;
```

- [ ] **Step 2: Apply migration in SSMS**

Run `067_machine_auth_keys.sql` against KryossDb. Verify with:

```sql
SELECT 'machines.machine_secret' AS [check],
    CASE WHEN COL_LENGTH('machines','machine_secret') IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS status
UNION ALL
SELECT 'machines.session_key',
    CASE WHEN COL_LENGTH('machines','session_key') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'machines.session_key_expires_at',
    CASE WHEN COL_LENGTH('machines','session_key_expires_at') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'machines.auth_version',
    CASE WHEN COL_LENGTH('machines','auth_version') IS NOT NULL THEN 'OK' ELSE 'MISSING' END;
```

Expected: all OK.

- [ ] **Step 3: Commit**

```
git add KryossApi/sql/067_machine_auth_keys.sql
git commit -m "feat(db): 067 per-machine auth key columns for rotation"
```

---

### Task 2: Entity + DbContext

**Files:**
- Modify: `KryossApi/src/KryossApi/Data/KryossDbContext.cs` (machine entity config section)

- [ ] **Step 1: Add properties to Machine entity**

Find the `Machine` entity class in `KryossApi/src/KryossApi/Data/Entities/Machine.cs`. Add these properties alongside existing fields like `LastHeartbeatAt`, `AgentMode`, etc:

```csharp
public string? MachineSecret { get; set; }
public string? SessionKey { get; set; }
public DateTime? SessionKeyExpiresAt { get; set; }
public string? PrevSessionKey { get; set; }
public DateTime? PrevKeyExpiresAt { get; set; }
public DateTime? KeyRotatedAt { get; set; }
public int AuthVersion { get; set; } = 1;
```

- [ ] **Step 2: Add column mappings in DbContext**

Find the `modelBuilder.Entity<Machine>` configuration block in `KryossDbContext.cs`. Add alongside existing `HasColumnName` calls:

```csharp
e.Property(x => x.MachineSecret).HasColumnName("machine_secret");
e.Property(x => x.SessionKey).HasColumnName("session_key");
e.Property(x => x.SessionKeyExpiresAt).HasColumnName("session_key_expires_at");
e.Property(x => x.PrevSessionKey).HasColumnName("prev_session_key");
e.Property(x => x.PrevKeyExpiresAt).HasColumnName("prev_key_expires_at");
e.Property(x => x.KeyRotatedAt).HasColumnName("key_rotated_at");
e.Property(x => x.AuthVersion).HasColumnName("auth_version");
```

- [ ] **Step 3: Build to verify**

```
cd KryossApi/src/KryossApi && dotnet build
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add KryossApi/src/KryossApi/Data/Entities/Machine.cs KryossApi/src/KryossApi/Data/KryossDbContext.cs
git commit -m "feat(entity): Machine auth key properties + column mappings"
```

---

### Task 3: KeyRotationService

**Files:**
- Create: `KryossApi/src/KryossApi/Services/KeyRotationService.cs`

- [ ] **Step 1: Create the service**

```csharp
using System.Security.Cryptography;
using KryossAgent.Data;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface IKeyRotationService
{
    (string machineSecret, string sessionKey, DateTime expiresAt) GenerateInitialKeys();
    (string newSessionKey, DateTime expiresAt)? TryRotate(
        string? currentSessionKey, DateTime? expiresAt, out string? prevKey, out DateTime? prevExpiry);
    bool ValidateHmac(string signingString, string signature, string key);
}

public class KeyRotationService : IKeyRotationService
{
    private static readonly TimeSpan SessionKeyLifetime = TimeSpan.FromHours(48);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);
    private static readonly double RotationThreshold = 0.5; // rotate when >50% of lifetime elapsed

    public (string machineSecret, string sessionKey, DateTime expiresAt) GenerateInitialKeys()
    {
        var secret = GenerateKey();
        var session = GenerateKey();
        var expiresAt = DateTime.UtcNow.Add(SessionKeyLifetime);
        return (secret, session, expiresAt);
    }

    public (string newSessionKey, DateTime expiresAt)? TryRotate(
        string? currentSessionKey, DateTime? expiresAt, out string? prevKey, out DateTime? prevExpiry)
    {
        prevKey = null;
        prevExpiry = null;

        if (currentSessionKey is null || expiresAt is null)
            return null;

        var elapsed = DateTime.UtcNow - (expiresAt.Value - SessionKeyLifetime);
        if (elapsed < SessionKeyLifetime * RotationThreshold)
            return null; // not yet time to rotate

        prevKey = currentSessionKey;
        prevExpiry = DateTime.UtcNow.Add(GracePeriod);

        var newKey = GenerateKey();
        var newExpiry = DateTime.UtcNow.Add(SessionKeyLifetime);
        return (newKey, newExpiry);
    }

    public bool ValidateHmac(string signingString, string signature, string key)
    {
        using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(key));
        var computed = Convert.ToHexStringLower(hmac.ComputeHash(
            System.Text.Encoding.UTF8.GetBytes(signingString)));
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(computed),
            System.Text.Encoding.UTF8.GetBytes(signature));
    }

    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToHexStringLower(bytes);
    }
}
```

- [ ] **Step 2: Register in DI**

In `KryossApi/src/KryossApi/Program.cs`, find the services registration section (near other `AddSingleton`/`AddScoped` calls). Add:

```csharp
services.AddSingleton<IKeyRotationService, KeyRotationService>();
```

- [ ] **Step 3: Build**

```
cd KryossApi/src/KryossApi && dotnet build
```

- [ ] **Step 4: Commit**

```
git add KryossApi/src/KryossApi/Services/KeyRotationService.cs KryossApi/src/KryossApi/Program.cs
git commit -m "feat(auth): KeyRotationService — CSPRNG key generation + rotation logic"
```

---

### Task 4: Enrollment — Return Per-Machine Credentials

**Files:**
- Modify: `KryossApi/src/KryossApi/Functions/Agent/EnrollFunction.cs`
- Modify: `KryossApi/src/KryossApi/Services/EnrollmentService.cs` (find via grep for `RedeemCodeAsync`)

- [ ] **Step 1: Update EnrollmentService.RedeemCodeAsync**

Find `EnrollmentService.cs`. Locate the method that creates the machine row (look for where `machine.AgentId` is set). After the machine row is created/updated, add key generation:

```csharp
// After machine row is set up, before SaveChangesAsync:
var keyService = /* inject IKeyRotationService via constructor */;
var (machineSecret, sessionKey, expiresAt) = keyService.GenerateInitialKeys();
machine.MachineSecret = machineSecret;
machine.SessionKey = sessionKey;
machine.SessionKeyExpiresAt = expiresAt;
machine.AuthVersion = 2;
machine.KeyRotatedAt = DateTime.UtcNow;
```

Add `IKeyRotationService` to the constructor injection of `EnrollmentService`.

- [ ] **Step 2: Extend enrollment response**

In the enrollment response object (returned from `RedeemCodeAsync` or built in `EnrollFunction.cs`), add:

```csharp
MachineSecret = machine.MachineSecret,
SessionKey = machine.SessionKey,
SessionKeyExpiresAt = machine.SessionKeyExpiresAt,
```

Find the anonymous object or DTO in `EnrollFunction.cs` that builds the response (the one that currently includes `agentId`, `apiKey`, `apiSecret`, `publicKey`). Add the three new fields alongside.

- [ ] **Step 3: Add enrollment rate limiting**

In `EnrollFunction.cs`, at the top of the `Run` method (before any business logic), add IP-based throttle:

```csharp
private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _enrollLimits = new();
private const int MaxEnrollPerWindow = 5;
private static readonly TimeSpan EnrollWindow = TimeSpan.FromMinutes(15);

// Inside Run method, first lines:
var clientIp = req.Headers.TryGetValues("X-Forwarded-For", out var fwdVals)
    ? fwdVals.First().Split(',')[0].Trim()
    : "unknown";

var now = DateTime.UtcNow;
var (count, windowStart) = _enrollLimits.GetOrAdd(clientIp, _ => (0, now));
if (now - windowStart > EnrollWindow)
{
    count = 0;
    windowStart = now;
}
if (count >= MaxEnrollPerWindow)
{
    return new HttpResponseData(req) { StatusCode = HttpStatusCode.TooManyRequests };
    // Use req.CreateResponse(HttpStatusCode.TooManyRequests) — match existing pattern
}
_enrollLimits[clientIp] = (count + 1, windowStart);
```

Adapt to match the exact response creation pattern used in the file (look at how other error responses are created — likely `req.CreateResponse(statusCode)` + `WriteAsJsonAsync`).

- [ ] **Step 4: Build + verify**

```
cd KryossApi/src/KryossApi && dotnet build
```

- [ ] **Step 5: Commit**

```
git add KryossApi/src/KryossApi/Functions/Agent/EnrollFunction.cs KryossApi/src/KryossApi/Services/EnrollmentService.cs
git commit -m "feat(auth): per-machine credentials at enrollment + enrollment rate limit (5/15min)"
```

---

### Task 5: Middleware — Machine-Level HMAC Validation

**Files:**
- Modify: `KryossApi/src/KryossApi/Middleware/ApiKeyAuthMiddleware.cs`

This is the most critical task. The middleware currently validates HMAC with `org.ApiSecret`. We need to add a machine-level validation chain.

- [ ] **Step 1: Add machine key lookup**

In `ApiKeyAuthMiddleware.cs`, find the HMAC validation section (where it reads `org.ApiSecret` and computes HMAC). The current flow is:

1. Extract `X-Api-Key` → lookup org
2. If `org.ApiSecret` is set → validate HMAC

Change to:

```csharp
// After org lookup and HMAC requirement check, BEFORE computing HMAC against org secret:

var agentIdHeader = req.Headers.TryGetValues("X-Agent-Id", out var agentVals) ? agentVals.FirstOrDefault() : null;
Machine? machine = null;
bool machineKeyUsed = false;

if (agentIdHeader is not null && Guid.TryParse(agentIdHeader, out var agentGuid))
{
    machine = await db.Machines
        .Where(m => m.AgentId == agentGuid && m.OrganizationId == org.Id)
        .Select(m => new { m.SessionKey, m.SessionKeyExpiresAt, m.PrevSessionKey, m.PrevKeyExpiresAt, m.MachineSecret, m.AuthVersion })
        .FirstOrDefaultAsync();

    if (machine?.AuthVersion == 2 && machine.SessionKey is not null)
    {
        var keyRotation = serviceProvider.GetRequiredService<IKeyRotationService>();
        var signingString = $"{timestamp}{method}{path}{agentIdHeader}{bodyHash}";

        // Try session_key (current)
        if (machine.SessionKeyExpiresAt > DateTime.UtcNow
            && keyRotation.ValidateHmac(signingString, signature, machine.SessionKey))
        {
            machineKeyUsed = true;
        }
        // Try prev_session_key (grace period)
        else if (machine.PrevSessionKey is not null
            && machine.PrevKeyExpiresAt > DateTime.UtcNow
            && keyRotation.ValidateHmac(signingString, signature, machine.PrevSessionKey))
        {
            machineKeyUsed = true;
        }
        // Try machine_secret (reauth — session expired)
        else if (machine.MachineSecret is not null
            && keyRotation.ValidateHmac(signingString, signature, machine.MachineSecret))
        {
            machineKeyUsed = true;
            // Flag for forced rotation in heartbeat
            httpContext.Items["ForceKeyRotation"] = true;
        }
    }
}

if (!machineKeyUsed)
{
    // Fall back to org-level ApiSecret (backward compat for agents < v2.2)
    // ... existing HMAC validation code stays here ...
}
```

- [ ] **Step 2: Add per-org aggregate rate limit**

In the rate limiting section of the middleware, add an org-level check alongside the existing per-machine check:

```csharp
// Existing: per-machine rate limit (15/min by X-Agent-Id)
// Add: per-org rate limit (200/min by orgId)
private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _orgLimits = new();
private const int MaxOrgRequestsPerMin = 200;

// In the rate limit check section:
var orgLimitKey = $"org:{org.Id:N}";
var orgNow = DateTime.UtcNow;
var (orgCount, orgWindowStart) = _orgLimits.GetOrAdd(orgLimitKey, _ => (0, orgNow));
if (orgNow - orgWindowStart > TimeSpan.FromMinutes(1))
{
    orgCount = 0;
    orgWindowStart = orgNow;
}
if (orgCount >= MaxOrgRequestsPerMin)
{
    httpContext.Response.StatusCode = 429;
    await httpContext.Response.WriteAsJsonAsync(new { error = "org_rate_limit_exceeded" });
    return;
}
_orgLimits[orgLimitKey] = (orgCount + 1, orgWindowStart);
```

- [ ] **Step 3: Build**

```
cd KryossApi/src/KryossApi && dotnet build
```

- [ ] **Step 4: Commit**

```
git add KryossApi/src/KryossApi/Middleware/ApiKeyAuthMiddleware.cs
git commit -m "feat(auth): machine-level HMAC validation chain + per-org rate limit (200/min)"
```

---

### Task 6: Heartbeat — Key Rotation Response

**Files:**
- Modify: `KryossApi/src/KryossApi/Functions/Agent/HeartbeatFunction.cs`

- [ ] **Step 1: Add key rotation to heartbeat**

In `HeartbeatFunction.cs`, after the existing machine update logic (where `LastHeartbeatAt` is set) and before building the response:

```csharp
// Key rotation: check if session key needs rotation
string? newSessionKey = null;
DateTime? newSessionKeyExpiresAt = null;

var forceRotation = httpContext.Items.ContainsKey("ForceKeyRotation");

if (machine.AuthVersion == 2 && machine.SessionKey is not null)
{
    var keyService = serviceProvider.GetRequiredService<IKeyRotationService>();
    var rotation = keyService.TryRotate(
        machine.SessionKey, machine.SessionKeyExpiresAt,
        out var prevKey, out var prevExpiry);

    if (rotation is not null || forceRotation)
    {
        if (rotation is null && forceRotation)
        {
            // Force rotation: generate new key regardless of age
            var freshKey = /* call GenerateInitialKeys and use only session part */;
            prevKey = machine.SessionKey;
            prevExpiry = DateTime.UtcNow.AddHours(24);
            rotation = (freshKey.sessionKey, freshKey.expiresAt);
        }

        machine.PrevSessionKey = prevKey;
        machine.PrevKeyExpiresAt = prevExpiry;
        machine.SessionKey = rotation.Value.newSessionKey;
        machine.SessionKeyExpiresAt = rotation.Value.expiresAt;
        machine.KeyRotatedAt = DateTime.UtcNow;

        newSessionKey = rotation.Value.newSessionKey;
        newSessionKeyExpiresAt = rotation.Value.expiresAt;
    }
}
```

- [ ] **Step 2: Extend the response**

Find where the heartbeat response is built (the anonymous object or DTO with `ack = true` and `pendingTasks`). Add:

```csharp
var response = new
{
    ack = true,
    pendingTasks = pendingTasks,
    // Key rotation fields (null when no rotation needed)
    newSessionKey = newSessionKey,
    newSessionKeyExpiresAt = newSessionKeyExpiresAt,
};
```

- [ ] **Step 3: Build**

```
cd KryossApi/src/KryossApi && dotnet build
```

- [ ] **Step 4: Commit**

```
git add KryossApi/src/KryossApi/Functions/Agent/HeartbeatFunction.cs
git commit -m "feat(auth): heartbeat rotates session key when >50% expired or forced"
```

---

### Task 7: Agent — Config Changes

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Config/AgentConfig.cs`

- [ ] **Step 1: Add new registry fields**

In `AgentConfig.cs`, add properties:

```csharp
public string? MachineSecret { get; set; }
public string? SessionKey { get; set; }
public DateTime? SessionKeyExpiresAt { get; set; }
```

- [ ] **Step 2: Update Load()**

In the `Load()` method, after existing registry reads (like `ApiKey`, `ApiSecret`), add:

```csharp
MachineSecret = key.GetValue("MachineSecret") as string,
SessionKey = key.GetValue("SessionKey") as string,
SessionKeyExpiresAt = key.GetValue("SessionKeyExpiresAt") is string expStr
    ? DateTime.TryParse(expStr, out var exp) ? exp : null
    : null,
```

Match the exact pattern used for other string fields in `Load()`.

- [ ] **Step 3: Update Save()**

In the `Save()` method, after existing registry writes, add:

```csharp
if (MachineSecret is not null)
    key.SetValue("MachineSecret", MachineSecret);
if (SessionKey is not null)
    key.SetValue("SessionKey", SessionKey);
if (SessionKeyExpiresAt is not null)
    key.SetValue("SessionKeyExpiresAt", SessionKeyExpiresAt.Value.ToString("O"));
```

- [ ] **Step 4: Build**

```
cd KryossAgent/src/KryossAgent && dotnet build
```

- [ ] **Step 5: Commit**

```
git add KryossAgent/src/KryossAgent/Config/AgentConfig.cs
git commit -m "feat(agent): store MachineSecret + SessionKey in registry"
```

---

### Task 8: Agent — ApiClient Signing + Key Swap

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Services/ApiClient.cs`
- Modify: `KryossAgent/src/KryossAgent/Models/RemediationModels.cs`
- Modify: `KryossAgent/src/KryossAgent/Models/JsonContext.cs`

- [ ] **Step 1: Extend HeartbeatResponse**

In `RemediationModels.cs`, add to `HeartbeatResponse`:

```csharp
[JsonPropertyName("newSessionKey")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public string? NewSessionKey { get; set; }

[JsonPropertyName("newSessionKeyExpiresAt")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public DateTime? NewSessionKeyExpiresAt { get; set; }
```

- [ ] **Step 2: Update signing in ApiClient**

In `ApiClient.cs`, find the `CreateSignedRequest` method (the private method that adds `X-Signature` headers). Change the HMAC key selection:

```csharp
// Current code uses: _config.ApiSecret
// Replace with session key priority:
var signingKey = _config.SessionKey   // prefer session key (v2.2+)
    ?? _config.MachineSecret          // fallback: reauth with long-term secret
    ?? _config.ApiSecret;             // fallback: legacy org secret (pre-v2.2)

if (signingKey is null) return; // no signing possible

// Rest of HMAC computation stays the same, just use signingKey instead of _config.ApiSecret
```

- [ ] **Step 3: Handle key rotation in SendHeartbeatAsync**

In `ApiClient.cs`, find `SendHeartbeatAsync`. After deserializing the response, add key swap logic:

```csharp
if (response?.NewSessionKey is not null)
{
    _config.SessionKey = response.NewSessionKey;
    _config.SessionKeyExpiresAt = response.NewSessionKeyExpiresAt;
    _config.Save();
    if (verbose)
        Console.WriteLine($"[AUTH] Session key rotated, expires {response.NewSessionKeyExpiresAt:u}");
}
```

Note: `_config` is the `AgentConfig` instance. Check if the constructor stores it as a field. If `ApiClient` takes config as constructor param, it should be mutable (it already is — `Save()` exists).

- [ ] **Step 4: Handle enrollment response**

In `ApiClient.cs`, find `EnrollAsync` (the method that calls POST `/v1/enroll`). After deserializing the enrollment response, save the new per-machine credentials:

```csharp
// After existing config.ApiKey and config.ApiSecret assignment:
if (enrollResponse.MachineSecret is not null)
{
    config.MachineSecret = enrollResponse.MachineSecret;
    config.SessionKey = enrollResponse.SessionKey;
    config.SessionKeyExpiresAt = enrollResponse.SessionKeyExpiresAt;
}
```

Check the actual field names in the enrollment response deserialization — the response may be an anonymous type or a specific DTO. Match the exact property names.

- [ ] **Step 5: Update JsonContext for AOT**

In `JsonContext.cs`, verify `HeartbeatResponse` is already registered (it should be from v2.1.0). The new nullable string/DateTime properties will be picked up automatically by source gen since they're on an existing registered type.

If `HeartbeatResponse` is NOT in the `[JsonSerializable]` list, add:

```csharp
[JsonSerializable(typeof(HeartbeatResponse))]
```

- [ ] **Step 6: Build both projects**

```
cd KryossAgent/src/KryossAgent && dotnet build
cd KryossApi/src/KryossApi && dotnet build
```

- [ ] **Step 7: Commit**

```
git add KryossAgent/src/KryossAgent/Services/ApiClient.cs KryossAgent/src/KryossAgent/Models/RemediationModels.cs KryossAgent/src/KryossAgent/Models/JsonContext.cs
git commit -m "feat(agent): sign with session key, handle heartbeat key rotation"
```

---

### Task 9: Version Bump + Documentation

**Files:**
- Modify: `KryossApi/src/KryossApi/KryossApi.csproj` — Version 1.19.0 → 1.20.0
- Modify: `KryossAgent/src/KryossAgent/KryossAgent.csproj` — Version 2.1.0 → 2.2.0
- Modify: `KryossAgent/src/KryossAgent/Services/ServiceWorker.cs` — version string in log line

- [ ] **Step 1: Bump API version**

In `KryossApi.csproj`, change:

```xml
<Version>1.20.0</Version>
```

- [ ] **Step 2: Bump Agent version**

In `KryossAgent.csproj`, change:

```xml
<Version>2.2.0</Version>
```

- [ ] **Step 3: Update ServiceWorker version string**

In `ServiceWorker.cs`, find the startup log line (currently `"[SERVICE] Kryoss Agent v2.1.0 started as Windows Service"`). Change to:

```csharp
Console.WriteLine("[SERVICE] Kryoss Agent v2.2.0 started as Windows Service");
```

- [ ] **Step 4: Update master CLAUDE.md version table**

In `CLAUDE.md` at the project root, update the version table:

```
| **API** | 1.20.0 | ...
| **Agent** | 2.2.0 | ...
```

- [ ] **Step 5: Update ROADMAP.md**

In `docs/superpowers/plans/ROADMAP.md`:

1. In Current State Snapshot, update agent version line to v2.2.0
2. In Active Queue, add shipped entry for SH-KEY
3. In Decision Log, add:

```
| 2026-04-25 | SH-KEY: Per-machine Kerberos-inspired auth (3-layer: enrollment code → machine_secret → session_key 48h) | Covers SH-01 (enrollment rate limit 5/15min/IP), SH-03 (session key expires 48h), SH-09 (automatic rotation via heartbeat). Backward compat: agents < v2.2 fall back to org-level ApiSecret |
```

4. In Agent table, mark SH-01, SH-03, SH-09 as shipped
5. In Security Hardening table, mark SH-01, SH-03, SH-09 as shipped

- [ ] **Step 6: Build both**

```
cd KryossApi/src/KryossApi && dotnet build
cd KryossAgent/src/KryossAgent && dotnet build
```

- [ ] **Step 7: Commit**

```
git add KryossApi/src/KryossApi/KryossApi.csproj KryossAgent/src/KryossAgent/KryossAgent.csproj KryossAgent/src/KryossAgent/Services/ServiceWorker.cs CLAUDE.md docs/superpowers/plans/ROADMAP.md
git commit -m "chore: version bump API 1.20.0, Agent 2.2.0 — SH-KEY auth rotation"
```

---

## Verification Checklist

After all tasks complete:

- [ ] `dotnet build` passes for both API and Agent (0 errors)
- [ ] Migration 067 applied to KryossDb (6 new columns on machines)
- [ ] New agent enrollment returns `machineSecret` + `sessionKey` + `sessionKeyExpiresAt`
- [ ] Agent signs requests with `sessionKey` (check via `--verbose` flag)
- [ ] Heartbeat response includes `newSessionKey` when key is >24h old
- [ ] Agent swaps keys on heartbeat and persists to registry
- [ ] If session key expires, agent falls back to `machineSecret` and gets fresh session key
- [ ] Pre-v2.2 agents still work (org-level ApiSecret fallback)
- [ ] Enrollment endpoint rejects >5 attempts from same IP in 15 minutes
- [ ] Per-org rate limit triggers at 200 req/min

## Backward Compatibility Matrix

| Agent Version | Auth Method | Key Rotation | Rate Limited |
|---|---|---|---|
| < v2.2 | org ApiSecret (legacy) | No | Yes (existing per-machine) |
| >= v2.2 | per-machine session_key | Yes (48h via heartbeat) | Yes (per-machine + per-org) |
| >= v2.2 (offline >48h) | machine_secret reauth | Forced on next heartbeat | Yes |

## Rollback Plan

If issues arise:
1. Server: middleware fallback to org-level ApiSecret is always active — no agent breaks
2. Agent: `sessionKey ?? machineSecret ?? apiSecret` chain means any layer can be null
3. Migration: all new columns are nullable, `auth_version` defaults to 1
4. To revert: set `machine.auth_version = 1` in DB, agent falls back to org secret
