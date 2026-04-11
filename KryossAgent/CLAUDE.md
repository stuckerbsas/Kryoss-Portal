# KryossAgent — Windows Assessment Agent (.NET 8 AOT)

**Read `../CLAUDE.md` first.** This file is the detailed map of the agent only.

---

## Stack

- **.NET 8** (`net8.0-windows`), **win-x64** only
- **Native AOT capable** (`PublishAot=true` in csproj, but `publish.ps1` currently overrides to false — see gap note)
- **Self-contained single-file** publish (~68 MB)
- **Zero external runtime deps** on target machines
- **NuGet:** `System.Text.Json` (source-gen), `System.ServiceProcess.ServiceController`, `Microsoft.Win32.Registry`, `System.Diagnostics.EventLog`
- **No WMI, no PowerShell SDK, no COM** — all AOT-safe

---

## Folder layout

```
KryossAgent/
├── KryossAgent.sln (implicit)
├── publish.ps1                       <- builds self-contained single-file exe
├── publish/
│   └── KryossAgent.exe               <- ~68.7 MB compiled binary
└── src/KryossAgent/
    ├── KryossAgent.csproj            <- net8.0-windows, PublishAot=true, SingleFile
    ├── Program.cs                    <- entry point, enrollment, dispatch, upload
    ├── 9e201df0-...._config.json    <- sample/test config (Azure RCM proxy ids)
    ├── Config/
    │   └── AgentConfig.cs            <- loads/saves HKLM\SOFTWARE\Kryoss\Agent
    ├── Engines/                      <- 12 files (11 types + interface)
    │   ├── ICheckEngine.cs           <- interface: Type, Execute(controls)
    │   ├── RegistryEngine.cs         <- HKLM/HKCU/HKU/HKCR, handles HKU user enum
    │   ├── SeceditEngine.cs          <- BATCH: secedit /export /cfg once, parse INF
    │   ├── AuditpolEngine.cs         <- BATCH: auditpol /get /category:* /r, parse CSV
    │   ├── NetAccountsEngine.cs      <- BATCH: 'net accounts', parse colon pairs
    │   ├── FirewallEngine.cs         <- registry-only (SharedAccess\...\FirewallPolicy)
    │   ├── ServiceEngine.cs          <- System.ServiceProcess.ServiceController
    │   ├── ShellEngine.cs            <- Type="command", allowlisted System32 exes, honors TimeoutSeconds (default 15s)
    │   ├── EventLogEngine.cs         <- EventLogConfiguration + EventLogReader (max_size/retention/last_cleared/event_count/latest_event)
    │   ├── CertStoreEngine.cs        <- X509Store (count_self_signed/count_expiring/count_weak_key/list_thumbprints)
    │   ├── BitLockerEngine.cs        <- BATCH: single manage-bde -status invocation, parsed per drive
    │   └── TpmEngine.cs              <- BATCH: registry + tpmtool getdeviceinformation
    ├── Models/
    │   ├── AssessmentPayload.cs      <- top-level POST /v1/results body (v1.0 shape)
    │   ├── ControlDef.cs             <- downloaded control definition from /v1/controls
    │   ├── CheckResult.cs            <- flexible result: object? Value + engine-specific fields
    │   └── JsonContext.cs            <- source-generated JSON context (AOT)
    ├── Services/
    │   ├── ApiClient.cs              <- HTTP client, HMAC signing
    │   ├── CryptoService.cs          <- RSA-OAEP + AES-GCM envelope (but NOT wired to ApiClient yet)
    │   ├── OfflineStore.cs           <- queue at C:\ProgramData\Kryoss\PendingResults\*.json
    │   ├── PlatformDetector.cs       <- OS/hw detection via registry only (no WMI)
    │   └── SoftwareInventory.cs      <- Uninstall registry keys (HKLM 64+32 bit)
    └── Helpers/                      <- (empty)
```

---

## Program.cs execution flow

1. Parse CLI args (`--silent`, `--code`, `--api-url`, `--reenroll`)
2. If `--reenroll`: delete `HKLM\SOFTWARE\Kryoss\Agent` registry key entirely
3. Load `AgentConfig` from `HKLM\SOFTWARE\Kryoss\Agent`
4. If not enrolled: take `--code` → `POST /v1/enroll` → save credentials (API URL prompt removed, uses compiled default)
5. Upload any pending offline payloads from `C:\ProgramData\Kryoss\PendingResults\`
6. `GET /v1/controls?assessmentId=X` → receive list of `ControlDef`
7. Detect platform + hardware (~25 fields) + enumerate software
8. Group controls by `Type` → run 11 engines **in parallel** (`Task.WhenAll`)
9. Build `AssessmentPayload` → `POST /v1/results` (HMAC signed)
10. Server responds with `{score, grade, passCount, warnCount, failCount}` — printed to console
11. Output `RESULT:` lines for deployment script parsing (`OK`, `SKIP`, `ERROR`, `OFFLINE`, `ENROLL_FAILED`)
12. On upload failure → save payload to offline store, exit code 2

---

## Engines — efficiency matrix

| Engine | Type string | Batch? | Tech | Notes |
|---|---|---|---|---|
| RegistryEngine | `registry` | Per-control | `Microsoft.Win32.Registry` | Fastest. Handles 4 hives + HKU user enumeration |
| SeceditEngine | `secedit` | ✅ Batch (1 call) | `secedit.exe /export /cfg` | ~2 sec for 26+ checks |
| AuditpolEngine | `auditpol` | ✅ Batch (1 call) | `auditpol /get /category:* /r` | ~1 sec for all subcats |
| NetAccountsEngine | `netaccount` | ✅ Batch (1 call) | `net accounts` | Password/lockout fields only |
| FirewallEngine | `firewall` | Per-control | Registry only (`SharedAccess\Parameters\FirewallPolicy`) | No netsh, no COM |
| ServiceEngine | `service` | Per-control | `ServiceController` | No sc.exe, no WMI |
| ShellEngine | `command` | Per-control | `Process.Start` | Honors `TimeoutSeconds` (default 15s), 4KB stdout cap, allowlist `System32/SysWOW64/Windows` |
| EventLogEngine | `eventlog` | Per-control | `System.Diagnostics.Eventing.Reader` | AOT-safe. CheckType: max_size, retention, last_cleared, event_count, latest_event |
| CertStoreEngine | `certstore` | Per-control | `X509Store` | AOT-safe. CheckType: count_self_signed, count_expiring, count_weak_key, list_thumbprints |
| BitLockerEngine | `bitlocker` | ✅ Batch (1 call) | `manage-bde -status` parse | Single batched invocation per engine run, per-drive dictionary |
| TpmEngine | `tpm` | ✅ Batch (1 call) | registry + `tpmtool getdeviceinformation` | Graceful fallback if tpmtool missing |

**Dispatch:** `Program.cs` groups controls by `c.Type` and runs the matching engine with ALL controls of that type in one call (`engine.Execute(IReadOnlyList<ControlDef>)`). Batch engines exploit this, per-control engines iterate internally.

---

## Data models

### `AssessmentPayload` (current v1.0 — partially enriched)

```csharp
class AssessmentPayload {
    Guid AgentId
    string AgentVersion  // "1.0.0"
    DateTime Timestamp
    int DurationMs
    PlatformInfo? Platform       // { os, build, version }
    HardwareInfo? Hardware       // ~20 fields (see PlatformDetector above)
    List<SoftwareItem> Software  // { name, version, publisher }
    List<CheckResult> Results
}
```

**Partial gap:** HardwareInfo expanded to ~20 fields (was 4). Backend
persists all of them. Still missing: `raw_*` blocks (network, users,
security posture) from schema v1.1.

### `ControlDef` (downloaded from `/v1/controls`)

Has fields: `Id, Type, CheckType, Hive, Path, ValueName, SettingName, Subcategory, Profile, Property, ServiceName, Field, Executable, Arguments, TimeoutSeconds, Parent, LogName, StoreName, StoreLocation, Drive, Display`

**All camelCase JSON** via `[JsonPropertyName]` and sourced through `JsonContext` for AOT.

**Fixed (seed_005b_fix_casing.sql):** The 25 HIPAA refinement controls
(BL-0445..0469) were originally seeded with snake_case keys; that
migration rewrites them to camelCase in place, idempotently.

### `CheckResult` (agent → server)

```csharp
class CheckResult {
    string Id
    bool? Exists
    object? Value           // flexible — int, string, bool
    string? RegType         // registry only
    string? StartType       // service only
    string? Status          // service only
    int? ExitCode           // command only
    string? Stdout          // command only (capped 4KB)
    string? Stderr          // command only
}
```

---

## Services

- **`ApiClient`** — HTTP client. `EnrollAsync` (no HMAC — public), `GetControlsAsync` (HMAC), `SubmitResultsAsync` (HMAC). HMAC = `HMAC-SHA256(secret, timestamp + method + path + sha256(body))`, sent as `X-Api-Key`, `X-Timestamp`, `X-Signature` headers.

- **`CryptoService`** — Provides `EncryptEnvelope(json, publicKeyPem)` → `{EncryptedKey, EncryptedPayload, Iv, Tag}` (RSA-OAEP-SHA256 wraps a random AES-256-GCM key, GCM encrypts the body, 128-bit auth tag). **⚠️ NOT called from ApiClient today** — current flow sends plaintext JSON + HMAC signature only. Payload encryption end-to-end appears to be dormant code.

- **`OfflineStore`** — `C:\ProgramData\Kryoss\PendingResults\result_YYYYMMDD_HHmmss_{guid}.json`. Plain JSON. Methods: `SavePayload`, `LoadPending`, `RemovePending`.

- **`PlatformDetector`** — `DetectPlatform()` returns `{os, version, build}` from `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`. `DetectHardware()` returns ~25 fields: cpu, cpuCores, ramGb, diskType, diskSizeGb, diskFreeGb, manufacturer, model, serialNumber, tpmPresent, tpmVersion, secureBoot, bitLockerStatus, ipAddress, macAddress, domainStatus, domainName, systemAgeDays, lastBootAt. All via registry + .NET APIs, AOT-safe. **Does NOT compute platform code (`W10`/`W11`/`MS22`) — server resolves this.**

- **`SoftwareInventory`** — Enumerates `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` and the WOW6432Node variant. Filters out updates/hotfixes/system components. Dedup by name.

---

## Config storage

- **Location:** `HKLM\SOFTWARE\Kryoss\Agent` registry key
- **Fields:** `ApiUrl`, `AgentId` (Guid), `ApiKey`, `ApiSecret`, `PublicKeyPem`, `AssessmentId`, `AssessmentName`
- **ACL:** SYSTEM-only in production (not enforced in dev)
- **No encryption at rest** — plaintext in registry

---

## How the agent is invoked

```powershell
# First run (interactive enrollment — API URL is compiled default: https://func-kryoss.azurewebsites.net)
KryossAgent.exe --code K7X9-M2P4-Q8R1-T5W3

# First run (silent, for RMM deployment)
KryossAgent.exe --silent --code K7X9-M2P4-Q8R1-T5W3

# Subsequent runs (already enrolled)
KryossAgent.exe          # interactive
KryossAgent.exe --silent # for scheduled task / RMM

# Re-enrollment (clears HKLM\SOFTWARE\Kryoss\Agent and re-enrolls)
KryossAgent.exe --reenroll --code NEW-CODE-HERE

# Custom API URL (override compiled default)
KryossAgent.exe --code K7X9-M2P4-Q8R1-T5W3 --api-url https://custom-url.azurewebsites.net
```

Exit codes: `0` = success, `1` = fatal error, `2` = warning/upload-deferred.

---

## Known gaps (details in `../CLAUDE.md`)

1. ✅ **camelCase vs snake_case** — fixed by `seed_005b_fix_casing.sql`
2. ✅ **Engine coverage** — EventLog / CertStore / BitLocker / TPM added (11 engines total)
3. ✅ **Platform scoping** — backend resolves `machines.platform_id` from OS string; agent sends `X-Agent-Id` header and receives a pre-filtered control list
4. ✅ **HardwareInfo expanded** — PlatformDetector now collects ~25 fields (was 4), backend persists all
5. ✅ **Default API URL** — compiled to `https://func-kryoss.azurewebsites.net`, interactive URL prompt removed
6. ✅ **Re-enrollment** — `--reenroll` flag clears registry and re-enrolls; server reuses machine row by hostname
7. ✅ **RESULT: output lines** — Program.cs outputs structured lines for deployment script parsing
8. 🟠 **Payload v1.0 vs v1.1** — still needs 3 new collectors for `raw_*` blocks: `NetworkCollector`, `UserCollector`, `SecurityPostureCollector`
9. 🟡 **`CryptoService` dormant** — defined but not used by `ApiClient`. Decide: wire it up or remove it
10. 🟡 **`publish.ps1` overrides `PublishAot=true`** — binary is 68 MB single-file-with-runtime instead of ~15 MB AOT native
11. 🟡 **No `Helpers/` content** — folder exists but empty
