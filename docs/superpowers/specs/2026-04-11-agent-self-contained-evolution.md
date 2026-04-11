# KryossAgent — Self-Contained Evolution

**Date:** 2026-04-11
**Status:** Approved design
**Supersedes:** 2026-04-10-agent-binary-patching-and-remote-scan.md (incorporated + expanded)
**Scope:** Transform the agent into a single self-contained .exe that replaces all PS1 scripts

---

## Overview

One `.exe` does everything. The portal generates it per-org with embedded config. The MSP tech drops it on a machine and it auto-detects what to do:

```
Double-click (embedded config) → auto-enroll + scan this machine + upload
--scan                         → discover network + deploy self to all PCs + scan all
--scan --reenroll              → force re-enrollment on all targets
Manual (no embed, no flags)    → interactive prompt for enrollment code
```

**Eliminates:** `Invoke-KryossDeployment.ps1`, `Clear-KryossEnrollment.ps1`, `Get-NetworkDevices.ps1`

---

## Feature 1: Binary Sentinels (Compile-Time Placeholders)

### 1.1 Sentinel Definitions

Six fixed-length ASCII sentinel strings embedded as `const string` in `Program.cs`:

| Sentinel | Bytes | Purpose | Example value |
|---|---|---|---|
| `@@KRYOSS_ENROLL:...@@` | 64 | Enrollment code | `YL3T-BZM3-4BZS-9B25` |
| `@@KRYOSS_APIURL:...@@` | 256 | API base URL | `https://func-kryoss.azurewebsites.net` |
| `@@KRYOSS_ORGNAM:...@@` | 128 | Organization name | `Cox Science Museum` |
| `@@KRYOSS_MSPNAM:...@@` | 128 | MSP/franchise name | `TeamLogic IT` |
| `@@KRYOSS_CLRPRI:...@@` | 16 | Primary color hex | `#008852` |
| `@@KRYOSS_CLRACC:...@@` | 16 | Accent color hex | `#A2C564` |

**Format:** Each sentinel is `@@KRYOSS_XXXXX:` (prefix) + payload area (padded with `_PLACEHOLDER_VALUE_...`) + `@@` (suffix). Total byte count is fixed per sentinel.

**Detection:** If the payload area contains `PLACEHOLDER`, the sentinel is unpatched (generic binary). Otherwise, it contains the real value right-padded with `\0`.

### 1.2 Sentinel Reader (Agent Side)

New static class `EmbeddedConfig`:

```csharp
static class EmbeddedConfig
{
    public static string? EnrollmentCode => ReadSentinel(ENROLLMENT_PLACEHOLDER);
    public static string? ApiUrl => ReadSentinel(APIURL_PLACEHOLDER);
    public static string? OrgName => ReadSentinel(ORGNAME_PLACEHOLDER);
    public static string? MspName => ReadSentinel(MSPNAME_PLACEHOLDER);
    public static string? PrimaryColor => ReadSentinel(PRIMARY_COLOR_PLACEHOLDER);
    public static string? AccentColor => ReadSentinel(ACCENT_COLOR_PLACEHOLDER);

    public static bool IsPatched => EnrollmentCode != null;

    private static string? ReadSentinel(string raw)
    {
        if (raw.Contains("PLACEHOLDER")) return null;
        // Extract between prefix ":" and suffix "@@", trim nulls
        var start = raw.IndexOf(':') + 1;
        var end = raw.LastIndexOf("@@");
        return raw[start..end].TrimEnd('\0', '_').Trim();
    }
}
```

### 1.3 Server-Side Patching (API Endpoint)

New Azure Function: `AgentDownloadFunction`

```
GET /v2/agent/download?orgId={guid}
Authorization: Bearer <token>
Response: application/octet-stream (Content-Disposition: attachment)
```

Flow:
1. Authenticate caller via Bearer token
2. Load org + franchise + brand from DB
3. Get or create multi-use enrollment code for the org (30-day expiry, maxUses=999)
4. Read template binary from Azure Blob Storage (`kryoss-agent-templates/{version}/KryossAgent.exe`)
5. Byte-scan for each sentinel prefix, replace payload with real value (null-padded to exact length)
6. Stream patched binary as download

**Template upload:** Part of the agent CI/CD — on each build, upload the compiled `.exe` to blob storage.

### 1.4 Portal UI

"Download Agent" button on Organization detail page:
- Visible to users with `agents:download` permission
- Calls `GET /v2/agent/download?orgId={orgId}`
- Downloads `KryossAgent-{org-slug}.exe`

---

## Feature 2: Auto-Detect Flow

### 2.1 Program.cs Decision Tree

```
1. Load EmbeddedConfig sentinels
2. Load AgentConfig from registry (existing enrollment)
3. Parse CLI args (--scan, --code, --reenroll, --silent, --threads, etc.)

4. IF --scan:
     → Network Scan Mode (Feature 3)
     → Exit

5. IF EmbeddedConfig.IsPatched AND NOT config.IsEnrolled:
     → Auto-enroll silently using embedded code + URL
     → Scan this machine
     → Upload results
     → Exit 0

6. IF EmbeddedConfig.IsPatched AND config.IsEnrolled:
     → Scan this machine (already enrolled, skip enrollment)
     → Upload results
     → Exit 0

7. IF --code provided:
     → Enroll with provided code (existing behavior)
     → Scan + upload
     → Exit

8. ELSE (no embed, no --code, no --scan):
     → Interactive prompt for enrollment code
     → Scan + upload
     → Exit
```

### 2.2 Branded Banner

When embedded:
```
╔══════════════════════════════════════════╗
║   TeamLogic IT — Security Assessment    ║
║          Cox Science Museum             ║
╚══════════════════════════════════════════╝
```

When generic (unpatched):
```
╔══════════════════════════════════════════╗
║       Kryoss Security Agent v1.0.0      ║
║         Security Assessment              ║
╚══════════════════════════════════════════╝
```

Console colors use embedded brand colors when available (primary for PASS/headers, accent for info).

---

## Feature 3: Network Scan Mode (--scan)

### 3.1 CLI Interface

```
KryossAgent.exe --scan [discovery] [options]

Discovery (combinable, results deduplicated):
  --targets host1,host2,host3       Explicit comma-separated list
  --targets-file machines.txt       One hostname/IP per line
  --discover-ad [OU=path,DC=...]    LDAP query for computer objects
  --discover-arp                    Parse local ARP table
  --discover-subnet 192.168.1.0/24  TCP 445 probe across CIDR range

Options:
  --credential                      Prompt for domain\user + password
  --threads N                       Parallel concurrency (default: 5)
  --silent                          No confirmation prompt
  --reenroll                        Force re-enrollment on all targets
  --code XXXX-XXXX-XXXX-XXXX        Enrollment code (if not embedded)
```

**Default discovery:** If no discovery flag is provided, uses `--discover-ad` with fallback to `--discover-arp`.

### 3.2 PsExec Embedded Resource

PsExec64.exe (~1MB) embedded as a .NET embedded resource:

```xml
<!-- KryossAgent.csproj -->
<ItemGroup>
  <EmbeddedResource Include="Resources\PsExec64.exe" LogicalName="PsExec64.exe" />
</ItemGroup>
```

Extracted to `%TEMP%\KryossAgent_PsExec64_{pid}.exe` on `--scan` startup. Deleted on exit (finally block). Process ID in filename prevents conflicts with concurrent scans.

### 3.3 Execution Flow Per Target

```
For each target (parallel, up to --threads):

1. Connectivity: TCP 445 probe (2s timeout)
   FAIL → mark "unreachable", skip

2. Push: Copy self to \\target\C$\Windows\Temp\KryossAgent.exe
   Uses current Windows identity or --credential creds
   Via File.Copy with UNC path (AOT-safe, no WMI)

3. Execute: PsExec \\target -s -h -accepteula
   C:\Windows\Temp\KryossAgent.exe --silent [--reenroll --code X]
   If embedded: agent auto-enrolls using embedded code
   If --code: passed through
   Timeout: 5 minutes per machine

4. Collect: Parse stdout for RESULT: line
   Extract status (OK/SKIP/OFFLINE/ERROR), score, grade

5. Cleanup: Delete \\target\C$\Windows\Temp\KryossAgent.exe

6. Display: [3/15] DC01... OK (82% B)  or  [3/15] DC01... FAILED (timeout)
```

### 3.4 Credential Handling

- **Default:** Current Windows identity (tech running as domain admin)
- **`--credential`:** Prompts for `domain\user` + password (masked). Stored as `NetworkCredential` in memory only.
- **SMB auth:** `net use \\target\C$ /user:X` for file copy, PsExec `-u` for execution.

### 3.5 Parallel Execution

Default concurrency: 5 threads. Configurable via `--threads N`.

Implementation: `SemaphoreSlim(N)` + `Task.WhenAll` over all targets. Each target runs in its own task, acquires the semaphore before starting PsExec.

Progress output thread-safe via lock:
```
[1/24]  DC01            scanning...
[2/24]  CENTSVR-1       scanning...
[1/24]  DC01            OK (85% B) — 28s
[3/24]  FRONTDESK1      scanning...
[2/24]  CENTSVR-1       OK (72% C) — 34s
...
```

### 3.6 Console Summary

```
══════════════════════════════════════════
  Kryoss Network Scan Complete
══════════════════════════════════════════
  Targets:    24
  Scanned:    22    (18 OK, 3 Warn, 1 Fail)
  Offline:    18
  Unreachable: 4

  Machine          Score  Grade  Status
  ───────────────  ─────  ─────  ──────
  DC01             85%    B      OK
  CENTSVR-1        72%    C      OK
  STUDENT11        91%    A      OK
  SERVER2          --     --     Unreachable (no SMB)
  ...
══════════════════════════════════════════
```

### 3.7 Individual Enrollment

Each remote machine enrolls individually using the embedded code (or `--code`). Each gets its own `AgentId` and appears as a separate machine in the portal. The scanning workstation is a deployer only.

---

## Feature 4: Discovery Layer

### 4.1 AD Discovery

```csharp
// System.DirectoryServices (AOT-compatible on Windows)
var searcher = new DirectorySearcher("(objectClass=computer)");
// Filter: Windows OS, enabled, logged in last 30 days
// Returns: hostname, DNS name, OS, last logon
```

Scoped to specified OU or domain root. Fallback if AD module not available.

### 4.2 ARP Discovery

```csharp
// Process.Start("arp", "-a") → parse output
// Filter: skip multicast, broadcast, own IP
// Returns: IP addresses (reverse DNS best-effort)
```

### 4.3 Subnet Discovery

```csharp
// Parse CIDR → enumerate IPs
// Parallel TCP connect to port 445 (SMB), 2s timeout
// Hosts that respond = Windows machines with file sharing
```

### 4.4 Deduplication

All sources merge into `List<ScanTarget>`. Deduplicated by resolved hostname (case-insensitive). Multiple source tags preserved for the report.

### 4.5 Confirmation

Unless `--silent`, print discovered list and prompt:
```
Found 24 targets:
  DC01            192.168.1.10   [ad]
  CENTSVR-1       192.168.1.11   [ad, arp]
  STUDENT11       192.168.1.50   [subnet]
  ...
Proceed? [Y/n]
```

---

## Phase 2 (Future): Network Device Discovery + Speedtest

Deferred to a separate spec. Same `.exe`, new flag `--discovery`:

```
KryossAgent.exe --discovery [--subnet 192.168.1.0/24]
```

Will cover:
- SNMP sweep for switches/routers/APs
- HTTP probe for web-managed devices (printers, NAS, UPS)
- NetBIOS scan for legacy devices
- Speedtest (download/upload/latency to a reference endpoint)
- JSON report output (compatible with Kryoss Portal import)

Not in scope for this spec.

---

## Implementation Notes

### AOT Compatibility

All features are AOT-safe:
- `System.DirectoryServices` — AOT-compatible on Windows
- `File.Copy` with UNC paths — no P/Invoke needed
- `Process.Start` for PsExec — already used by ShellEngine
- `TcpClient` for port probes — standard .NET
- Embedded resources — standard .NET API

### Binary Size

Current: ~65MB (self-contained single-file)
After adding PsExec resource: ~66MB (+1MB)
Sentinels: negligible (<1KB)

### Security

- Embedded enrollment code is cleartext in the binary — acceptable since codes are short-lived and limited-use
- Credentials from `--credential` held in memory only, never persisted
- PsExec extracted to %TEMP% and deleted after use
- Agent .exe copied to `C:\Windows\Temp` on targets and deleted after execution
- HMAC signing on all API calls (existing)

### Files to Create/Modify

**Agent (KryossAgent):**
- `Program.cs` — auto-detect flow, --scan orchestration
- `Config/EmbeddedConfig.cs` — sentinel reader (NEW)
- `Services/NetworkScanner.cs` — discovery + scan orchestration (NEW)
- `Services/TargetDiscovery.cs` — AD, ARP, subnet discovery (NEW)
- `Services/PsExecRunner.cs` — extract + run PsExec (NEW)
- `Resources/PsExec64.exe` — embedded resource (NEW)
- `KryossAgent.csproj` — EmbeddedResource entry

**Backend (KryossApi):**
- `Functions/Portal/AgentDownloadFunction.cs` — download endpoint (NEW)
- `Services/BinaryPatcher.cs` — byte-level sentinel replacement (NEW)

**Portal (KryossPortal):**
- "Download Agent" button on org detail page
