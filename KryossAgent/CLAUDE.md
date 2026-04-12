# KryossAgent v1.2.2 — Windows Assessment Agent (.NET 8)

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
    ├── Program.cs                    <- entry point, enrollment, dispatch, upload, network scan
    ├── Config/
    │   ├── AgentConfig.cs            <- loads/saves HKLM\SOFTWARE\Kryoss\Agent
    │   └── EmbeddedConfig.cs         <- binary patching sentinels (UTF-16LE, fixed-length)
    ├── Engines/                      <- 12 files (11 types + interface)
    │   ├── ICheckEngine.cs           <- interface: Type, Execute(controls)
    │   ├── RegistryEngine.cs         <- HKLM/HKCU/HKU/HKCR, handles HKU user enum
    │   ├── SeceditEngine.cs          <- BATCH: secedit /export /cfg once, parse INF
    │   ├── AuditpolEngine.cs         <- BATCH: auditpol /get /category:* /r, parse CSV
    │   ├── NetAccountsEngine.cs      <- BATCH: 'net accounts', parse colon pairs
    │   ├── FirewallEngine.cs         <- registry-only (SharedAccess\...\FirewallPolicy)
    │   ├── ServiceEngine.cs          <- System.ServiceProcess.ServiceController
    │   ├── ShellEngine.cs            <- Type="command", allowlisted System32 exes, honors TimeoutSeconds
    │   ├── EventLogEngine.cs         <- EventLogConfiguration + EventLogReader
    │   ├── CertStoreEngine.cs        <- X509Store
    │   ├── BitLockerEngine.cs        <- BATCH: single manage-bde -status invocation, parsed per drive
    │   └── TpmEngine.cs              <- BATCH: registry + tpmtool getdeviceinformation
    ├── Models/
    │   ├── AssessmentPayload.cs      <- top-level POST /v1/results body
    │   ├── ControlDef.cs             <- downloaded control definition from /v1/controls
    │   ├── CheckResult.cs            <- flexible result: object? Value + engine-specific fields
    │   └── JsonContext.cs            <- source-generated JSON context (AOT)
    ├── Services/
    │   ├── ApiClient.cs              <- HTTP client, HMAC signing
    │   ├── CryptoService.cs          <- RSA-OAEP + AES-GCM envelope (dormant)
    │   ├── HardwareFingerprint.cs    <- registry-based SHA-256 hardware ID
    │   ├── NetworkScanner.cs         <- orchestrates remote scan: discover + deploy + collect
    │   ├── OfflineStore.cs           <- queue at C:\ProgramData\Kryoss\PendingResults\*.json
    │   ├── PinnedHttpHandler.cs      <- SPKI pinning for HTTPS
    │   ├── PlatformDetector.cs       <- OS/hw/multi-disk detection via registry (no WMI)
    │   ├── PortScanner.cs            <- TCP top 100 + UDP top 20 port scanning
    │   ├── PsExecRunner.cs           <- PsExec embedded resource, remote execution
    │   ├── SecurityService.cs        <- security-related helpers
    │   ├── SoftwareInventory.cs      <- Uninstall registry keys (HKLM 64+32 bit)
    │   └── TargetDiscovery.cs        <- AD/ARP/subnet/explicit target discovery + AD hygiene
    └── Helpers/                      <- (empty)
```

---

## Program.cs execution flow

1. Parse CLI args (`--silent`, `--verbose`, `--alone`, `--scan`, `--code`, `--api-url`, `--reenroll`, `--credential`, `--threads`, discovery flags)
2. Show branded banner (uses `EmbeddedConfig.MspName`/`OrgName` if patched, otherwise generic Kryoss banner)
3. If `--scan`: delegate to `NetworkScanner.RunAsync()` and exit
4. If patched binary or `--reenroll`: wipe `HKLM\SOFTWARE\Kryoss\Agent` for clean enrollment
5. Load `AgentConfig` from `HKLM\SOFTWARE\Kryoss\Agent`
6. If not enrolled: resolve code from CLI > embedded > interactive prompt → `POST /v1/enroll` → save credentials
7. Upload any pending offline payloads from `C:\ProgramData\Kryoss\PendingResults\`
8. `GET /v1/controls?assessmentId=X` → receive list of `ControlDef`
9. Detect platform + hardware (~25 fields, multi-disk) + enumerate software
10. Group controls by `Type` → run 11 engines **in parallel** (`Task.WhenAll`)
11. Build `AssessmentPayload` (v1.2.2) → `POST /v1/results` (HMAC signed)
12. Server responds with `{score, grade, passCount, warnCount, failCount}` — printed to console
13. Unless `--alone` or `--silent`: auto-run network scan (discovery + remote deploy + port scan + AD hygiene)
14. Output `RESULT:` lines for deployment script parsing (`OK`, `SKIP`, `ERROR`, `OFFLINE`, `ENROLL_FAILED`)
15. On upload failure → save payload to offline store, exit code 2

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

### `AssessmentPayload` (v1.2.2)

```csharp
class AssessmentPayload {
    Guid AgentId
    string AgentVersion  // "1.2.2"
    DateTime Timestamp
    int DurationMs
    PlatformInfo? Platform       // { os, build, version }
    HardwareInfo? Hardware       // ~25 fields + multi-disk (see PlatformDetector)
    List<SoftwareItem> Software  // { name, version, publisher }
    List<CheckResult> Results
}
```

**HardwareInfo** includes ~25 fields: cpu, cpuCores, ramGb, manufacturer, model,
serialNumber, tpmPresent, tpmVersion, secureBoot, bitLockerStatus, ipAddress,
macAddress, domainStatus, domainName, systemAgeDays, lastBootAt, plus
per-drive disk info (drive letter, sizeGb, freeGb, type).

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

- **`CryptoService`** — Provides `EncryptEnvelope(json, publicKeyPem)` → `{EncryptedKey, EncryptedPayload, Iv, Tag}` (RSA-OAEP-SHA256 wraps a random AES-256-GCM key, GCM encrypts the body, 128-bit auth tag). **Dormant** — current flow sends plaintext JSON + HMAC signature only.

- **`HardwareFingerprint`** — Registry-based SHA-256 hardware ID, sent via `X-Hwid` header on every request.

- **`NetworkScanner`** — Orchestrates remote network scans. Discovers targets (via `TargetDiscovery`), deploys the agent binary to remote machines via SMB (`\\host\admin$\Temp\`), executes remotely via `PsExecRunner` with `--silent` flag, collects `RESULT:` lines. Also runs port scanning and AD hygiene audit on discovered targets. Submits port results and hygiene findings to API.

- **`OfflineStore`** — `C:\ProgramData\Kryoss\PendingResults\result_YYYYMMDD_HHmmss_{guid}.json`. Plain JSON. Methods: `SavePayload`, `LoadPending`, `RemovePending`.

- **`PinnedHttpHandler`** — SPKI pinning for HTTPS connections (log-only until `SpkiPins` registry value populated).

- **`PlatformDetector`** — `DetectPlatform()` returns `{os, version, build}`. `DetectHardware()` returns ~25 fields + multi-disk inventory (enumerates all fixed `DriveInfo` drives). All via registry + .NET APIs, AOT-safe. **Does NOT compute platform code — server resolves this.**

- **`PortScanner`** — Scans TCP top 100 ports (parallel `TcpClient` with configurable timeout) + UDP top 20 ports per target. Returns list of `PortResult(port, protocol, state, service)`.

- **`PsExecRunner`** — Extracts PsExec from embedded resources, runs it against remote targets. Used by `NetworkScanner` for remote agent deployment.

- **`SecurityService`** — Security-related helpers.

- **`SoftwareInventory`** — Enumerates `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` and the WOW6432Node variant. Filters out updates/hotfixes/system components. Dedup by name.

- **`TargetDiscovery`** — Discovers scan targets via multiple methods:
  - `--targets host1,host2` (explicit list)
  - `--targets-file machines.txt` (file)
  - `--discover-ad [OU]` (Active Directory LDAP query)
  - `--discover-arp` (ARP table)
  - `--discover-subnet CIDR` (TCP probe)
  - Default: tries AD first, falls back to ARP
  - Also contains `AdHygieneReport` generation: stale/dormant machines, stale/dormant/disabled users, never-expire passwords, privileged accounts, kerberoastable accounts, unconstrained delegation, adminCount residual, LAPS coverage, domain functional level.

---

## Config storage

- **Location:** `HKLM\SOFTWARE\Kryoss\Agent` registry key
- **Fields:** `ApiUrl`, `AgentId` (Guid), `ApiKey`, `ApiSecret`, `PublicKeyPem`, `AssessmentId`, `AssessmentName`
- **ACL:** SYSTEM-only in production (not enforced in dev)
- **No encryption at rest** — plaintext in registry

---

## How the agent is invoked

```powershell
# Default: scan local + entire network (AD discovery + remote deploy)
KryossAgent.exe --code K7X9-M2P4-Q8R1-T5W3

# Scan only this machine (skip network scan)
KryossAgent.exe --alone

# Network scan only (skip local assessment)
KryossAgent.exe --scan --threads 20

# Silent mode (for RMM/PsExec remote execution)
KryossAgent.exe --silent --code K7X9-M2P4-Q8R1-T5W3

# Re-enrollment
KryossAgent.exe --reenroll --code NEW-CODE-HERE

# Subnet discovery scan
KryossAgent.exe --scan --discover-subnet 10.0.0.0/24

# Verbose (show engine + command detail)
KryossAgent.exe --verbose

# Custom API URL
KryossAgent.exe --code K7X9-M2P4-Q8R1-T5W3 --api-url https://custom-url.azurewebsites.net
```

Exit codes: `0` = success, `1` = fatal error, `2` = warning/upload-deferred, `99` = unhandled exception.

---

## Known gaps (details in `../CLAUDE.md`)

1. ✅ **camelCase vs snake_case** — fixed by `seed_005b_fix_casing.sql`
2. ✅ **Engine coverage** — EventLog / CertStore / BitLocker / TPM added (11 engines total)
3. ✅ **Platform scoping** — backend resolves `machines.platform_id` from OS string
4. ✅ **HardwareInfo expanded** — ~25 fields + multi-disk inventory
5. ✅ **Default API URL** — compiled to `https://func-kryoss.azurewebsites.net`
6. ✅ **Re-enrollment** — `--reenroll` flag; server reuses machine row by hostname
7. ✅ **RESULT: output lines** — structured lines for deployment/RMM parsing
8. ✅ **Network scanner** — self-contained: AD/ARP/subnet discovery + PsExec remote deploy
9. ✅ **Port scanner** — TCP top 100 + UDP top 20 per target
10. ✅ **AD Hygiene audit** — privileged, kerberoastable, LAPS, delegation, domain level
11. ✅ **Binary patching** — EmbeddedConfig sentinels for server-side org-specific .exe generation
12. ✅ **Multi-disk detection** — per-drive inventory (letter, size, free, type)
13. ✅ **CLI flags** — `--help`, `--alone`, `--scan`, `--verbose`, `--credential`, `--threads`, discovery flags
14. 🟡 **`CryptoService` dormant** — defined but not used by `ApiClient`. Decide: wire it up or remove it
15. 🟡 **`publish.ps1` overrides `PublishAot=true`** — binary is 68 MB single-file-with-runtime instead of ~15 MB AOT native
16. 🟡 **`raw_*` blocks** — `raw_users`, `raw_network`, `raw_security_posture` still not populated as structured raw blocks in payload (data available via dedicated endpoints instead)
