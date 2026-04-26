# Security Remediation Plan — Kryoss Platform

**Date:** 2026-04-26
**Author:** Federico / Claude
**Status:** Approved
**Scope:** 30 findings across API, Agent, Portal, Scripts — 4 sprints, zero functionality removed

---

## Context

Full security audit of the 4 Kryoss pillars (API, Agent, Portal, Scripts/Deploy) identified 30 vulnerabilities: 8 CRITICAL, 13 HIGH, 9 MEDIUM, 8 LOW. This spec defines the remediation plan — every fix hardens security without removing any existing capability.

### Core Principle: Actlog as Source of Truth

The `actlog` table is the system's audit trail and debugging bible. It records **everything** the system does — not just errors. Every request, auth event, enrollment, config change, remediation execution, heartbeat, scan completion, report generation, consent flow, and CRUD operation gets an actlog entry.

Severities:
- **INFO** — normal operation (enrollment, scan complete, config change, heartbeat)
- **WARN** — suspicious but not failed (auth bypass attempt, expired key fallback, rejected remediation path)
- **ERR** — failure (unhandled exception, auth failure, validation error)

Every entry: `timestamp`, `module`, `action`, `actor` (agent_id/user_id/ip), `severity`, `message`, `trace_id`.

**Rule: if it's not in actlog, it didn't happen.**

For security remediation specifically:
- Error details (exception type, stack, inner) go to actlog severity=ERR, never to HTTP responses
- Auth events (success, failure, bypass attempt) go to actlog
- Config changes (remediation, agent config) go to actlog
- The HTTP response returns only `{ error, traceId }` — the traceId is the correlation key to find the full detail in actlog

---

## Sprint 1 — CRITICAL (8 findings)

### C1. ErrorSanitizationMiddleware Debug Mode

**File:** `KryossApi/src/KryossApi/Middleware/ErrorSanitizationMiddleware.cs`
**Risk:** Full stack traces, exception types, and inner messages returned in HTTP responses.
**Fix:**
1. Remove all `debug_*` fields from the JSON response
2. Before removing, write the full error detail to actlog via `ActlogService`:
   - `module = "error"`, `action = "unhandled_exception"`, `severity = "ERR"`
   - `message` = JSON with `{ exceptionType, message, stack, innerMessage, traceId }`
3. HTTP response becomes frozen shape: `{ "error": "internal_error", "traceId": "<guid>" }`
4. Application Insights continues to receive the same telemetry (backup)
5. Delete the `// TODO(SH): revert to frozen shape before go-live` comment

**Actlog entry example:**
```json
{
  "module": "error",
  "action": "unhandled_exception",
  "severity": "ERR",
  "message": "{\"type\":\"NullReferenceException\",\"message\":\"Object reference...\",\"stack\":\"at ...\",\"inner\":null}",
  "trace_id": "abc-123"
}
```

**Validation:** Call a non-existent endpoint or trigger a 500 — response must contain only `error` + `traceId`, never stack traces. Actlog must contain the full detail with matching traceId.

---

### C2. Hypervisor Passwords in Plaintext

**File:** `KryossApi/src/KryossApi/Functions/Portal/HypervisorConfigFunction.cs`
**Risk:** `EncryptedPassword` column stores plaintext despite the name.
**Fix:**
1. Use existing `CryptoService` to encrypt passwords with AES-256-GCM before DB write
2. Derive per-org encryption key from the org's existing RSA key in `org_crypto_keys`
3. Decrypt only when testing connection or running scan pipeline
4. API response for hypervisor configs never returns password field (omit entirely)
5. SQL migration: one-time script to encrypt existing plaintext values in `infra_hypervisor_configs.encrypted_password`
6. Log config creation/update to actlog: `module = "hypervisor"`, `action = "config_create|config_update"`, severity=INFO, message includes config ID (never password)

**Validation:** Query `infra_hypervisor_configs.encrypted_password` — values must be base64 ciphertext, not readable strings. Test connection must still work.

---

### C3. BearerAuthMiddleware Trusts Unvalidated Header

**File:** `KryossApi/src/KryossApi/Middleware/BearerAuthMiddleware.cs`
**Risk:** `X-MS-CLIENT-PRINCIPAL` header accepted without cryptographic validation. Direct Function App access = full impersonation.
**Fix:**
1. Add JWT validation using `Microsoft.Identity.Web` for the `Authorization: Bearer <token>` header
2. Validate token signature, issuer (`https://login.microsoftonline.com/{tenantId}/v2.0`), audience (client ID), and expiry
3. Keep `X-MS-CLIENT-PRINCIPAL` as secondary path but only when `Authorization` header is absent AND request comes from SWA (check `X-Forwarded-Host` matches SWA domain)
4. If neither path validates, return 401
5. Log all auth attempts to actlog: `module = "auth"`, `action = "bearer_validate"`, severity=INFO on success, severity=WARN on untrusted header attempt, severity=ERR on failure

**NuGet:** `Microsoft.Identity.Web` (already compatible with Azure Functions isolated worker)

**Validation:** Send crafted `X-MS-CLIENT-PRINCIPAL` header directly to Function App URL (not via SWA) — must return 401. Send valid Bearer token — must succeed.

---

### C4. SelfUpdater Uses cmd.exe + Batch File

**File:** `KryossAgent/src/KryossAgent/Services/SelfUpdater.cs`
**Risk:** `Process.Start("cmd.exe")` for service restart. TOCTOU race on batch file. Breaks zero-Process.Start contract.
**Fix:**
1. Replace batch file approach with:
   - `File.Move(tempPath, exePath, overwrite: true)` — atomic on NTFS
   - Restart own service via `ServiceController` API (already used in `ServiceEngine.cs`)
2. Sequence: download → verify hash → stop service → move binary → start service
3. Since the agent IS the service, use a helper approach: write a small restart script that the SCM executes, OR use `Environment.Exit(0)` with service recovery set to "Restart" (configured during `--install`)
4. Delete all `Process.Start` and batch file generation code
5. Verify `grep -r "Process.Start" KryossAgent/src/` returns zero matches after fix

**Preferred restart approach:** Configure service recovery to "Restart on failure" during `--install` (via `ChangeServiceConfig2` P/Invoke). Then `Environment.Exit(1)` triggers automatic restart by SCM with the new binary already in place.

**Validation:** Trigger update cycle — binary must be replaced and service must restart without any cmd.exe or .bat file creation.

---

### C5. Trial Mode UseShellExecute=true

**File:** `KryossAgent/src/KryossAgent/Program.cs` (line ~841)
**Risk:** Server-supplied file opened with default shell handler. RCE if server compromised returns .lnk/.exe/.msi.
**Fix:**
1. Validate downloaded file: must end in `.html` and file content must start with `<!DOCTYPE` or `<html`
2. Open explicitly with browser: `Process.Start(new ProcessStartInfo { FileName = reportPath, UseShellExecute = true })` is acceptable ONLY after validation
3. Alternative: `Process.Start("explorer.exe", reportPath)` — explorer respects file associations but is marginally safer
4. If validation fails, log path to console and skip auto-open — user opens manually
5. Best option: don't auto-open at all. Print path to console. User opens.

**Decision:** Print path, don't auto-open. Removes attack surface entirely, user experience impact is minimal (one extra click).

**Validation:** Ensure trial mode completes without any `UseShellExecute=true` call.

---

### C6. Hardcoded Enrollment Code

**Files:** `Setup-Azure.ps1`, `Deploy-KryossGPO.ps1`, `CLAUDE.md`
**Risk:** `K7X9-M2P4-Q8R1-T5W3` visible in version control, documentation, and scripts.
**Fix:**
1. Remove hardcoded code from all scripts — make it a required parameter
2. Replace in CLAUDE.md with `<ENROLLMENT_CODE>` placeholder
3. Remove from `deploy-config.json` if present
4. Rotate the code in production DB (`UPDATE enrollment_codes SET is_active = 0 WHERE code = 'K7X9-M2P4-Q8R1-T5W3'`)
5. Create new enrollment code per org via portal
6. Scripts use NinjaOne variables or environment variables only

**Validation:** `grep -r "K7X9-M2P4-Q8R1-T5W3"` across entire repo returns zero matches.

---

### C7. SQL Password Plaintext in Setup-Azure.ps1

**File:** `KryossApi/deploy/Setup-Azure.ps1`
**Risk:** SecureString converted to plaintext, passed as CLI argument (visible in process tree), stored in variable.
**Fix:**
1. Refactor to pipe password via stdin to `az sql server create` (Azure CLI supports `--admin-password` from stdin)
2. Never store plaintext password in a PowerShell variable
3. Remove password from `$deployConfig` JSON output
4. Add comment: production uses Managed Identity — this script is initial setup only
5. Note: this is a low-frequency script (run once per environment), not a recurring risk

**Validation:** Search script for plaintext password variables — must find none. `$deployConfig` JSON must not contain password fields.

---

### C8. Dynamic SQL in Migration Scripts

**Files:** `KryossApi/sql/018_prevent_hard_delete.sql`, `017_brands_and_org_updates.sql`
**Risk:** String concatenation of table names in `EXEC sp_executesql`. Source is `sys.tables` (safe) but sets bad precedent.
**Fix:**
1. Wrap all dynamic object names with `QUOTENAME()`:
   ```sql
   SET @sql = N'DROP TRIGGER ' + QUOTENAME('trg_' + @tableName + '_prevent_delete');
   ```
2. Add schema qualification: `QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name)`
3. Apply to both migration files
4. These are one-time migrations (already applied), so this is preventive for future reference

**Validation:** Review both files — all dynamic object references use `QUOTENAME()`.

---

## Sprint 2 — HIGH (13 findings)

### H1. /v1/schedule Endpoint Without Auth

**File:** `ScheduleFunction.cs` + `ApiKeyAuthMiddleware.cs`
**Fix:** Remove `/v1/schedule` from the auth-exempt whitelist. Require HMAC signature (agent already signs all requests). Log access to actlog: `module = "schedule"`, `action = "slot_check"`, severity=INFO.
**Validation:** Unauthenticated request to `/v1/schedule` returns 401.

### H2. M365 Secrets Not in Key Vault

**Files:** `M365Function.cs`, Function App configuration
**Fix:** Migrate `M365ScannerClientSecret` to Key Vault reference (`@Microsoft.KeyVault(SecretUri=...)`). Function App's Managed Identity gets `Key Vault Secrets User` role. Code reads from env var as before — only the storage changes.
**Validation:** `az functionapp config appsettings list` shows Key Vault reference syntax, not plaintext.

### H3. SSL Verification Bypass in HypervisorConfig

**File:** `HypervisorConfigFunction.cs`
**Fix:** When `VerifySsl=false`, still validate CA chain but accept self-signed certs. Replace `=> true` with custom callback that checks `SslPolicyErrors.RemoteCertificateChainErrors` only (allows self-signed, rejects expired/wrong-host). Log warning to actlog when SSL validation is relaxed.
**Validation:** Self-signed hypervisor cert connects successfully. Expired cert is rejected even with `VerifySsl=false`.

### H4. In-Memory Nonce Cache

**File:** `NonceCache.cs`
**Fix:** Implement `IDistributedNonceCache` with Redis backend (Azure Cache for Redis, Basic C0). Fallback to in-memory `ConcurrentDictionary` if Redis unavailable. Registration in DI: `services.AddSingleton<INonceCache>(sp => TryRedis() ?? new InMemoryNonceCache())`. Connection string via Key Vault reference.
**Validation:** Two requests with identical HMAC signature to different Function App instances — second is rejected.

### H5. SelfUpdater Hash Verification Optional

**File:** `SelfUpdater.cs`
**Fix:** Change `if (!string.IsNullOrEmpty(hash))` guard to mandatory: if hash is empty/null, reject the update and return false. Log rejection to console.
**Validation:** Mock server response with empty hash field — agent must not update.

### H6. SPKI Pinning Disabled by Default

**File:** `PinnedHttpHandler.cs`, `AgentConfig.cs`
**Fix:** Hardcode current `func-kryoss.azurewebsites.net` SPKI hash as compiled-in fallback. If registry `SpkiPins` is empty, use hardcoded pins (not log-only). Log-only mode requires explicit `KRYOSS_SPKI_LOGONLY=1` env var. During `--install`, write current pins to registry.
**Validation:** Agent without registry pins still validates SPKI against hardcoded hash. MitM with different cert fails.

### H7. Registry ACL Administrators+SYSTEM

**File:** `AgentConfig.cs`
**Fix:** Revert `Save()` ACL to SYSTEM-only. Add `--debug-acl` CLI flag that temporarily relaxes to Administrators+SYSTEM for manual testing (prints warning). Default = SYSTEM-only.
**Validation:** Non-SYSTEM admin cannot read `HKLM\SOFTWARE\Kryoss\Agent` after `Save()`.

### H8. RemediationExecutor Arbitrary Registry Paths

**File:** `RemediationExecutor.cs`
**Fix:** Add static whitelist of allowed registry path prefixes:
```csharp
private static readonly string[] AllowedPrefixes = {
    @"HKLM\SYSTEM\CurrentControlSet\Services\",
    @"HKLM\SOFTWARE\Policies\",
    @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\",
    @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\AuditPolicy\",
};
```
Reject any path not matching a prefix. Log rejections to actlog (server-side when task created) AND console (agent-side when executing): `module = "remediation"`, `action = "path_rejected"`, severity=WARN, message includes attempted path.
**Validation:** Create remediation task with path `HKLM\SAM\Domains` — task creation rejected server-side with actlog entry.

### H9. Portal Hardcoded Client ID

**File:** `KryossPortal/src/auth/msalConfig.ts`
**Fix:** Replace hardcoded values with `import.meta.env.VITE_AZURE_CLIENT_ID` and `import.meta.env.VITE_AZURE_AUTHORITY`. Add to `.env.production` and `.env.example`. Build fails if vars missing (Vite throws on undefined `import.meta.env` with required prefix).
**Validation:** Source code contains no GUID literals in msalConfig.ts.

### H10. Portal Error Handling Exposes Backend Messages

**Files:** `client.ts`, `UsersPage.tsx`, other catch blocks
**Fix:**
1. `apiFetch` in `client.ts`: map status codes to generic messages. Include `traceId` from response body if present.
   ```typescript
   const errorMessages: Record<number, string> = {
     400: 'Invalid request',
     401: 'Authentication required',
     403: 'Access denied',
     404: 'Not found',
     409: 'Conflict',
     429: 'Too many requests',
   };
   const msg = errorMessages[res.status] || 'Request failed';
   ```
2. Toast shows: `"Error — ref: {traceId}"` (if traceId available) or generic message
3. Replace all `catch (e: any)` with `catch (e: unknown)` + type guard
4. Backend actlog already has full detail keyed by traceId — portal never shows it

**Validation:** Trigger 500 from portal — toast shows "Request failed — ref: abc-123", never shows stack trace or exception message.

### H11. Binary Download Without Checksum

**File:** `Deploy-KryossNinja.ps1`
**Fix:** `version.txt` in blob storage gains a second line: SHA256 hash. Script downloads version.txt, parses hash, downloads binary, computes `Get-FileHash`, compares. Mismatch = abort + log.
**Validation:** Corrupt binary in blob — script refuses to install.

### H12. Predictable Temp File Path

**File:** `Deploy-KryossNinja.ps1`
**Fix:** `$tempPath = Join-Path $env:TEMP "KryossAgent_$(New-Guid).exe.tmp"`. Set restrictive ACL immediately after creation (SYSTEM + Administrators only).
**Validation:** Temp file path contains GUID, not predictable.

### H13. No HTTPS Enforcement on Blob URL

**File:** `Deploy-KryossNinja.ps1`
**Fix:** Validate `$BlobBaseUrl` starts with `https://`. Validate domain against whitelist: `stkryossagent.blob.core.windows.net`. Reject anything else.
**Validation:** Pass `http://evil.com/agent.exe` as blob URL — script exits with error.

---

## Sprint 3 — MEDIUM (9 findings)

### M1. Deserialize<object>() in SNMP Config
**Fix:** Replace with typed DTOs (`LldpNeighborDto`, `CdpNeighborDto`). Use `JsonElement` for vendor data with explicit property access.

### M2. AutoConsent TenantId Validation
**Fix:** Verify tenant ID belongs to an org the authenticated user has access to (join `m365_tenants` or `cloud_assessment_azure_subscriptions` by org_id).

### M3. HMAC Key Expiry Check
**Fix:** In `ApiClient.SignRequest`, check `SessionKeyExpiresAt < DateTime.UtcNow` before using SessionKey. If expired, fall back to MachineSecret. Log fallback to console as WARN.

### M4. Verbose Mode Credential Leakage
**Fix:** Remove `apiKeyPrefix` from verbose HMAC logging. Only log: algorithm, key source (session/machine/org), timestamp, path. Never log any part of any key.

### M5. Auth Failures to Event Log + Actlog
**Fix:** Agent-side: `EventLog.WriteEntry("KryossAgent", message, EventLogEntryType.Warning)` for 401/403/429. Server-side: already in actlog via `ActlogMiddleware` — verify severity=WARN for auth failures (not just INFO).

### M6. Client-Side Permission Enforcement
**Fix:** Add comment to `RequirePermission.tsx`: "UI guard only — backend enforces via [RequirePermission] attribute". No code change needed — backend already enforces. Add integration test: call `/v2/users` without admin permission → 403.

### M7. Logout via MSAL
**Fix:** Replace `window.location.href = '/.auth/logout'` with `instance.logoutRedirect({ postLogoutRedirectUri: '/' })`. Clears MSAL cache properly.

### M8. Service Runs as SYSTEM — Document
**Fix:** No code change. Create `docs/agent-service-hardening.md` documenting: why SYSTEM is required (WMI, protected registry, eventlog, service controller), what minimum permissions would be needed for a virtual account, and decision to stay with SYSTEM for now.

### M9. Enrollment Audit Logging
**Fix:** Server-side `EnrollmentService` already writes actlog on enrollment. Verify entry includes: `module = "enrollment"`, `action = "machine_enrolled"`, severity=INFO, message with machine hostname, code ID (hashed), agent version, IP. Add if missing.

---

## Sprint 4 — LOW (8 findings)

### L1. Credential Memory Wipe
**Fix:** After HMAC signing and after upload, zero-out byte arrays with `CryptographicOperations.ZeroMemory()`. Strings are immutable in .NET — document this limitation. Use `byte[]` for key material where possible.

### L2. Offline Payload Encryption
**Fix:** Encrypt payload JSON with DPAPI (`ProtectedData.Protect()`) before writing to `C:\ProgramData\Kryoss\PendingResults\`. Decrypt on load. DPAPI ties encryption to machine account — payload only readable on same machine.

### L3. Firewall Rules on Install
**Fix:** During `--install`, create outbound Windows Firewall rule: allow TCP 443 to `func-kryoss.azurewebsites.net` for `KryossAgent.exe`. Optional — document as recommended hardening, not mandatory.

### L4. Deploy Config Credential Cleanup
**Fix:** Remove enrollment code and any password-adjacent fields from `deploy-config.json`. Only infrastructure identifiers (resource group, server name, region).

### L5. Download Retry with Backoff
**Fix:** Wrap `Invoke-WebRequest` in retry loop: 3 attempts, 2/4/8 second delays. Already pattern in NinjaOne scripts — apply consistently.

### L6. URLSearchParams Consistency
**Fix:** Refactor all `apiFetch` calls in portal to use `URLSearchParams` for query string construction instead of template literal interpolation. Pattern from `ActivityLogPage.tsx` as reference.

### L7. Hardcoded API URL — Document
**Fix:** No code change. Add comment in `AgentConfig.cs` documenting this is intentional for cloud-hosted service. SPKI pinning (H6) provides additional protection against DNS poisoning.

### L8. Client-Side Enrollment Rate Limit
**Fix:** Add exponential backoff to `EnrollAsync` in `ApiClient.cs`: retry 3 times with 2/4/8 second delays on 429 or network failure. Server already enforces rate limits — this is client-side courtesy.

---

## Deployment Strategy

Each sprint deploys independently:
- **Sprint 1:** API deploy (C1, C2, C3, C7, C8) + Agent publish (C4, C5) + Scripts update (C6) — all CRITICAL
- **Sprint 2:** API deploy (H1-H4) + Agent publish (H5-H8) + Portal deploy (H9, H10) + Scripts (H11-H13)
- **Sprint 3:** API deploy (M1, M2, M5, M9) + Agent publish (M3, M4) + Portal deploy (M6, M7) + Docs (M8)
- **Sprint 4:** Agent publish (L1, L2, L8) + Scripts (L3, L5) + Portal (L6) + Docs/Config (L4, L7)

Version bumps: one patch bump per sprint per component (not per fix).

Backward compatibility: all agent changes are backward compatible — pre-2.3 agents continue working with existing auth chain.

---

## Verification Checklist (per sprint)

After each sprint:
1. `grep -r "Process.Start" KryossAgent/src/` — must return 0 matches (maintained from v1.4.0)
2. `grep -ri "password\|secret\|apikey" --include="*.json"` in deploy/ — must return 0 plaintext credentials
3. Trigger intentional 500 from portal — response must be `{ error, traceId }` only
4. Query actlog for last 24h — all severity levels present, no gaps in coverage
5. Run `check_catalog_health.sql` — no regressions
6. API `/v2/version` returns new version number
