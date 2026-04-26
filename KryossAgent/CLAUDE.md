# KryossAgent v2.2.0 — Windows Assessment Agent (.NET 8)

**Read `../CLAUDE.md` first.** This file is the detailed map of the agent only.

---

## Stack

- **.NET 8** (`net8.0-windows`), **win-x64** only
- **Native AOT capable** (`PublishAot=true` in csproj, but `publish.ps1` currently overrides to false — see gap note)
- **Self-contained single-file** publish (~12 MB trimmed, was ~68 MB pre-v1.4.0)
- **Zero external runtime deps** on target machines
- **NuGet:** `System.Text.Json` (source-gen), `System.ServiceProcess.ServiceController`, `Microsoft.Win32.Registry`, `System.Diagnostics.EventLog`, `System.Management` (WMI), `System.DirectoryServices`, `Lextm.SharpSnmpLib`, `Microsoft.Extensions.Hosting.WindowsServices`
- **Zero Process.Start** since v1.4.0 — all engines use registry, WMI, P/Invoke, or .NET APIs
- **Dual-mode** since v2.0.0 — runs as Windows Service (`--service`) or one-shot (default)

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
    ├── Program.cs                    <- entry point, dual-mode (one-shot + service), enrollment, dispatch
    │                                    --install/--uninstall/--service for Windows Service mode
    ├── Config/
    │   ├── AgentConfig.cs            <- loads/saves HKLM\SOFTWARE\Kryoss\Agent
    │   └── EmbeddedConfig.cs         <- binary patching sentinels (UTF-16LE, fixed-length)
    ├── Engines/                      <- v1.5.1: 14 files (12 engine types + interface + P/Invoke)
    │   ├── ICheckEngine.cs           <- interface: Type, Execute(controls)
    │   ├── RegistryEngine.cs         <- HKLM/HKCU/HKU/HKCR, handles HKU user enum
    │   ├── SecurityPolicyEngine.cs   <- P/Invoke NetUserModalsGet + registry (replaces SeceditEngine + NetAccountsEngine)
    │   ├── NetAccountCompatEngine.cs <- Backward compat wrapper → SecurityPolicyEngine
    │   ├── AuditpolEngine.cs         <- P/Invoke AuditQuerySystemPolicy (advapi32.dll), no auditpol.exe
    │   ├── FirewallEngine.cs         <- registry-only (SharedAccess\...\FirewallPolicy)
    │   ├── ServiceEngine.cs          <- System.ServiceProcess.ServiceController
    │   ├── NativeCommandEngine.cs    <- Routes by CheckType: TLS, UserRights, AppLocker, inline registry, custom
    │   ├── UserRightsApi.cs          <- P/Invoke LsaEnumerateAccountsWithUserRight for 16 user-rights controls
    │   ├── EventLogEngine.cs         <- EventLogConfiguration + EventLogReader + event_count + event_top_sources
    │   ├── CertStoreEngine.cs        <- X509Store
    │   ├── BitLockerEngine.cs        <- WMI Win32_EncryptableVolume (replaces manage-bde.exe)
    │   ├── TpmEngine.cs              <- WMI Win32_Tpm + registry (replaces tpmtool.exe)
    │   └── DcEngine.cs              <- Domain Controller checks via LDAP/WMI/Registry/ServiceController (27 check types)
    ├── Models/
    │   ├── AssessmentPayload.cs      <- top-level POST /v1/results body
    │   ├── ControlDef.cs             <- downloaded control definition from /v1/controls
    │   ├── CheckResult.cs            <- flexible result: object? Value + engine-specific fields
    │   ├── RemediationModels.cs     <- HeartbeatResponse, PendingRemediationTask, TaskResultPayload
    │   └── JsonContext.cs            <- source-generated JSON context (AOT)
    ├── Services/
    │   ├── ScanCycle.cs              <- extracted compliance scan logic (reused by one-shot + service)
    │   ├── ServiceWorker.cs          <- BackgroundService for Windows Service mode
    │   ├── ServiceInstaller.cs       <- P/Invoke service install/uninstall (zero Process.Start)
    │   ├── ApiClient.cs              <- HTTP client, HMAC signing
    │   ├── CryptoService.cs          <- RSA-OAEP + AES-GCM envelope (dormant)
    │   ├── HardwareFingerprint.cs    <- registry-based SHA-256 hardware ID
    │   ├── NetworkScanner.cs         <- orchestrates remote scan: discover + deploy + collect
    │   ├── NetworkDiagnostics.cs     <- speed test, latency sweep, route table, VPN detection, adapters
    │   ├── OfflineStore.cs           <- queue at C:\ProgramData\Kryoss\PendingResults\*.json
    │   ├── PinnedHttpHandler.cs      <- SPKI pinning for HTTPS
    │   ├── PlatformDetector.cs       <- OS/hw/multi-disk/ProductType detection via WMI MSFT_PhysicalDisk
    │   ├── PortScanner.cs            <- TCP top 100 + UDP top 20 port scanning
    │   ├── ProtocolAuditService.cs   <- NTLM/SMBv1 audit (configures event logs, opt-in per org)
    │   ├── SecurityService.cs        <- security-related helpers
    │   ├── SnmpScanner.cs            <- SNMP device discovery (IF-MIB, ENTITY-MIB, etc.)
    │   ├── SoftwareInventory.cs      <- Uninstall registry keys (HKLM 64+32 bit)
    │   ├── ThreatDetector.cs         <- Endpoint threat detection
    │   ├── TargetDiscovery.cs        <- AD/ARP/subnet/explicit target discovery + AD hygiene
    │   ├── WmiProbe.cs               <- WMI remote probe for unenrolled Windows machines
    │   ├── PassiveListener.cs        <- UDP listeners (NetBIOS/mDNS/SSDP) for passive discovery
    │   ├── SelfUpdater.cs            <- Auto-update from blob storage (version check + download + service restart)
    │   └── RemediationExecutor.cs    <- Executes whitelisted remediation tasks (registry/service/audit), reports results
    └── Helpers/                      <- (empty)
```

---

## Program.cs execution flow

**v2.0.0 dual-mode:**
- `--install`: installs as Windows Service via P/Invoke (CreateService), starts it
- `--uninstall`: stops + removes Windows Service via P/Invoke
- `--service`: runs as Windows Service using `Host.CreateApplicationBuilder()` + `ServiceWorker` (BackgroundService)
- No flags / one-shot: original behavior (below)

**One-shot mode:**
1. Parse CLI args (`--silent`, `--verbose`, `--alone`, `--scan`, `--code`, `--api-url`, `--reenroll`, etc.)
2. Show branded banner
3. **A-13 Orchestrated scheduling** (silent mode only): check `lastrun.txt` → if already ran today, exit. Call `GET /v1/schedule` → sleep until assigned slot.
4. If `--scan`: delegate to `NetworkScanner.RunAsync()` and exit
5. If patched binary or `--reenroll`: wipe registry for clean enrollment
6. Load `AgentConfig` from `HKLM\SOFTWARE\Kryoss\Agent`
7. If not enrolled: resolve code → `POST /v1/enroll` → save credentials
8. Upload any pending offline payloads
9. `GET /v1/controls` → run 12 engines in parallel → build payload → `POST /v1/results`
10. Server responds with score/grade → printed to console
11. Write `lastrun.txt`, run network scan (unless `--alone`/`--silent`), wipe registry

**Service mode (ServiceWorker loop):**
1. Load config, check enrollment
2. Every `ComplianceIntervalHours` (default 24h): run full compliance scan via `ScanCycle`
3. Every `ScanIntervalMinutes` (default 240 = 4h): run SNMP scan
4. Every 15 min: send heartbeat (`POST /v1/heartbeat`)
5. Sleep until next task, graceful shutdown on stop

---

## Engines — efficiency matrix (v1.4.0 — zero Process.Start)

| Engine | Type string | Tech | Notes |
|---|---|---|---|
| RegistryEngine | `registry` | `Microsoft.Win32.Registry` | HKLM/HKCU/HKU/HKCR + user enum |
| SecurityPolicyEngine | `secedit` | P/Invoke `NetUserModalsGet` + registry | Replaces SeceditEngine + NetAccountsEngine. 31 controls |
| NetAccountCompatEngine | `netaccount` | Delegates to SecurityPolicyEngine | Backward compat wrapper |
| AuditpolEngine | `auditpol` | P/Invoke `AuditQuerySystemPolicy` (advapi32.dll) | 24 controls, no auditpol.exe |
| FirewallEngine | `firewall` | Registry only | No netsh, no COM |
| ServiceEngine | `service` | `ServiceController` | No sc.exe, no WMI |
| NativeCommandEngine | `command` | Registry + WMI + .NET APIs | Replaces ShellEngine. Routes by `parent` field (Test-DeviceJoinStatus, Test-WHfBProvisioned, Test-EventLogRetention, Test-BackupPosture, Test-WdacPolicies). Unknown parents return "info" |
| EventLogEngine | `eventlog` | `System.Diagnostics.Eventing.Reader` | AOT-safe |
| CertStoreEngine | `certstore` | `X509Store` | AOT-safe |
| BitLockerEngine | `bitlocker` | WMI `Win32_EncryptableVolume` (root\CIMV2\Security\MicrosoftVolumeEncryption) | Replaces manage-bde.exe parsing |
| TpmEngine | `tpm` | WMI `Win32_Tpm` (root\CIMV2\Security\MicrosoftTpm) + registry | Replaces tpmtool.exe |
| DcEngine | `dc` | System.DirectoryServices (LDAP) + WMI + Registry + ServiceController | 27 check types: krbtgt_age, asrep_roastable, protected_users, recycle_bin, schema_admins, preauth_disabled, unconstrained_deleg, kerberoastable, stale_computers, pwd_never_expire, inactive_admins, domain_level, laps_coverage, admin_count_orphan, gpo_count, tombstone_lifetime, dsrm_password_set, ntds_service, replication_queue, time_source, dns_forwarders, print_spooler, smb_signing, ldap_signing, secure_channel, ntlm_restrict, crl_validity. Returns SkipNotDc on non-DC machines. |

**v1.4.0 security contract:** `grep Process.Start` in `KryossAgent/src/` returns **ZERO** matches. The agent is a pure passive sensor — all data collection via registry, WMI read queries, P/Invoke, and .NET APIs. No external executables, no shell commands, no process spawning. PlatformDetector also converted to WMI `MSFT_PhysicalDisk` (replaces `Get-PhysicalDisk` PowerShell calls).

**Dispatch:** `Program.cs` groups controls by `c.Type` and runs the matching engine with ALL controls of that type in one call (`engine.Execute(IReadOnlyList<ControlDef>)`). Batch engines exploit this, per-control engines iterate internally.

---

## Data models

### `AssessmentPayload` (v1.5.1)

```csharp
class AssessmentPayload {
    Guid AgentId
    string AgentVersion  // "1.5.1"
    DateTime Timestamp
    int DurationMs
    PlatformInfo? Platform       // { os, build, version }
    HardwareInfo? Hardware       // ~25 fields + multi-disk (see PlatformDetector)
    List<SoftwareItem> Software  // { name, version, publisher }
    List<CheckResult> Results
}
```

**HardwareInfo** includes ~26 fields: cpu, cpuCores, ramGb, manufacturer, model,
serialNumber, tpmPresent, tpmVersion, secureBoot, bitLockerStatus, ipAddress,
macAddress, domainStatus, domainName, productType (1=workstation, 2=DC, 3=server),
systemAgeDays, lastBootAt, plus per-drive disk info (drive letter, sizeGb, freeGb, type).

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

- **`PlatformDetector`** — `DetectPlatform()` returns `{os, version, build}`. `DetectHardware()` returns ~26 fields + multi-disk inventory (enumerates all fixed `DriveInfo` drives). Includes `ProductType` detection via WMI `Win32_OperatingSystem` (1=workstation, 2=DC, 3=server). **Does NOT compute platform code — server resolves this using OS string + ProductType.**

- **`PortScanner`** — Scans TCP top 100 ports (parallel `TcpClient` with configurable timeout) + UDP top 20 ports per target. Returns list of `PortResult(port, protocol, state, service)`.

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
# Install as Windows Service (auto-start, runs continuously)
KryossAgent.exe --install

# Uninstall Windows Service
KryossAgent.exe --uninstall

# Default: one-shot scan local + entire network
KryossAgent.exe --code <ENROLLMENT_CODE>

# Scan only this machine (skip network scan)
KryossAgent.exe --alone

# Network scan only (skip local assessment)
KryossAgent.exe --scan --threads 20

# Silent mode (for RMM/PsExec remote execution)
KryossAgent.exe --silent --code <ENROLLMENT_CODE>

# Offline collection (machines without internet)
KryossAgent.exe --offline --share \\fileserver\kryoss-collect
KryossAgent.exe --collect \\fileserver\kryoss-collect --code <ENROLLMENT_CODE>
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
13. ✅ **CLI flags** — `--help`, `--alone`, `--scan`, `--verbose`, `--credential`, `--threads`, discovery flags, `--offline`, `--share`, `--collect`
14. 🟡 **`CryptoService` dormant** — defined but not used by `ApiClient`. Decide: wire it up or remove it
15. 🟡 **`publish.ps1` overrides `PublishAot=true`** — binary is 68 MB single-file-with-runtime instead of ~15 MB AOT native
16. 🟡 **`raw_*` blocks** — `raw_users`, `raw_network`, `raw_security_posture` still not populated as structured raw blocks in payload (data available via dedicated endpoints instead)
