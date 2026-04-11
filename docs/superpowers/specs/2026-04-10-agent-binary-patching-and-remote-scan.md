# KryossAgent — Binary Patching & Multi-Computer Scan

**Date:** 2026-04-10
**Status:** Approved design
**Scope:** Two new agent features for MSP deployment and network-wide assessment

---

## Feature 1: Binary Patching (Portal-Served Pre-Configured Agent)

### Problem

MSP techs deploying the agent must pass `--code` and `--api-url` manually or via RMM script. This adds friction. The portal should serve a one-click .exe download that auto-enrolls on first run.

### Design

#### 1.1 Compile-Time Sentinels

Embed fixed-length ASCII sentinel strings in `Program.cs`:

```csharp
// Exactly 64 ASCII chars — enrollment code (format: XXXX-XXXX-XXXX-XXXX = 19 chars max)
const string ENROLLMENT_PLACEHOLDER =
    "@@KRYOSS_ENROLL:__PLACEHOLDER_VALUE_000000000000000000000000000@@";
//  |--- 16 prefix ---|--- 45 payload ---|--- 3 suffix ---|  = 64

// Exactly 256 ASCII chars — API URL (typical: ~50 chars)
const string APIURL_PLACEHOLDER =
    "@@KRYOSS_APIURL:__PLACEHOLDER_VALUE_" + new string('0', 214) + "@@";
//  |--- 16 prefix ---|--- 237 payload ---|--- 3 suffix ---|  = 256
```

**Rules:**
- Sentinels are `const string` — the compiler embeds them as UTF-8 bytes in the PE
- Enrollment sentinel: exactly 64 bytes. Payload area: 45 bytes (enrollment code is 19 chars `XXXX-XXXX-XXXX-XXXX`, right-padded with `\0`)
- API URL sentinel: exactly 256 bytes. Payload area: 237 bytes (ample for any Azure Function URL)
- Byte search uses the prefix (`@@KRYOSS_ENROLL:` / `@@KRYOSS_APIURL:`) — unique and unlikely to appear elsewhere in the PE
- Replacement values are right-padded with null bytes (`\0`) to maintain exact byte length
- Sentinel prefix (`@@KRYOSS_ENROLL:` / `@@KRYOSS_APIURL:`) is used for byte search

#### 1.2 Agent Startup Logic

In `Program.cs`, before the existing enrollment flow:

```
1. Read ENROLLMENT_PLACEHOLDER const
2. If it does NOT contain "PLACEHOLDER":
   a. Extract the enrollment code (trim null padding)
   b. Extract the API URL (trim null padding)
   c. Set --silent mode implicitly
   d. Proceed to auto-enroll (same as --code path)
   e. Skip interactive prompt entirely
3. If it DOES contain "PLACEHOLDER":
   a. Normal flow (--code flag or interactive prompt)
```

This is a compile-time constant check — zero runtime overhead when not patched.

#### 1.3 Server-Side Patching Endpoint

New Azure Function: `AgentDownloadFunction`

```
GET /v2/agent/download
Headers: Authorization: Bearer <token> (portal auth)
Query: orgId={guid} (optional — defaults to caller's org)
Response: application/octet-stream, Content-Disposition: attachment; filename="KryossAgent.exe"
```

**Flow:**
1. Authenticate caller via Bearer token (portal user)
2. Resolve the organization's enrollment code and API URL
3. Read template binary from Azure Blob Storage (`kryoss-agent-templates/{version}/KryossAgent.exe`)
4. Byte-scan for sentinel markers
5. Replace with real values (right-padded with `\0` to match original byte length)
6. Stream patched binary to response (no disk write)

**Storage:** Template .exe uploaded to Azure Blob Storage on each agent build. Container: `kryoss-agent-templates`, blob path: `{version}/KryossAgent.exe`.

#### 1.4 Portal UI

"Download Agent" button on the Organization detail page:
- Calls `GET /v2/agent/download?orgId={orgId}`
- Browser downloads the patched .exe
- Button text: "Download Agent (.exe)"
- Tooltip: "Pre-configured for this organization. Run on any Windows machine to auto-enroll."

#### 1.5 Code Signing

Not implemented in Phase 1. Binary is unsigned. When EV code signing is added (Phase 2+), the flow becomes: patch binary → sign with Azure SignTool → serve.

---

## Feature 2: Multi-Computer Remote Scan (Push & Run)

### Problem

MSP techs doing initial client assessments need to scan multiple machines. Currently requires running the agent on each machine individually or writing a PowerShell wrapper. The agent should handle this natively.

### Design

#### 2.1 CLI Interface

```
KryossAgent.exe --scan [discovery flags] [options]

Discovery (combinable, results are deduplicated):
  --targets host1,host2,host3        Explicit comma-separated list
  --targets-file machines.txt        One hostname/IP per line
  --discover-arp                     Parse local ARP table (arp -a)
  --discover-ad [OU=path,DC=...]     LDAP query for computer objects
  --discover-subnet 192.168.1.0/24   TCP 445 probe across CIDR range

Options:
  --credential                       Prompt for domain\user + password
  --threads N                        Parallel scan concurrency (default: 5)
  --silent                           No confirmation prompt, no interactive output
  --code XXXX-XXXX-XXXX-XXXX         Enrollment code for remote machines
  --api-url https://...              API URL for remote machines
```

**Note:** `--code` and `--api-url` are required when remote machines are not yet enrolled. If the binary is patched (Feature 1), those values come from the embedded sentinels.

#### 2.2 Discovery Layer

All discovery sources produce a `List<ScanTarget>`:

```csharp
record ScanTarget(string Host, string Source); // Source = "explicit", "arp", "ad", "subnet"
```

**ARP discovery (`--discover-arp`):**
- Run `arp -a`, parse output for IP addresses
- Filter: skip 255.255.255.255, multicast, own IP
- Reverse-DNS optional (best-effort)

**AD discovery (`--discover-ad`):**
- `System.DirectoryServices.DirectorySearcher` with `(objectClass=computer)`
- Scoped to specified OU or domain root
- Returns `dNSHostName` or `cn` for each computer object
- AOT note: `System.DirectoryServices` is AOT-compatible on Windows

**Subnet discovery (`--discover-subnet`):**
- Parse CIDR notation into IP range
- Parallel TCP connect to port 445 (SMB) with 2-second timeout
- Hosts that respond = Windows machines with file sharing enabled
- Faster and more reliable than ICMP ping (often blocked by firewall)

**Deduplication:** Merge all sources by resolved hostname (case-insensitive). If same host found by multiple sources, keep all source tags for the report.

**Confirmation:** Unless `--silent`, print the discovered list and prompt:
```
Found 12 targets:
  192.168.1.10  (DC01)      [ad, arp]
  192.168.1.11  (WS-ACCT01) [subnet]
  ...
Proceed with scan? [Y/n]
```

#### 2.3 Execution Flow (Per Target)

```
For each target (up to --threads in parallel):

1. STATUS: "Connecting to {host}..."
2. Connectivity check:
   - TCP 445 (SMB) — required for file copy
   - TCP 5985 (WinRM HTTP) — required for remote execution
   - If either fails → mark as "unreachable", log reason, skip
3. Push:
   - Copy self (KryossAgent.exe) to \\{host}\C$\Windows\Temp\KryossAgent.exe
   - Via SMB using current creds or --credential creds
4. Execute:
   - WinRM remote command:
     C:\Windows\Temp\KryossAgent.exe --silent --code {code} --api-url {url}
   - Implementation: Process.Start("powershell", "-NoProfile -Command
     Invoke-Command -ComputerName {host} -ScriptBlock {
       & C:\Windows\Temp\KryossAgent.exe --silent --code ... --api-url ...
     }")
   - If --credential: add -Credential parameter
   - Timeout: 5 minutes per machine (engines usually finish in <30s)
5. Collect:
   - Parse exit code (0=success, 1=error, 2=deferred)
   - Capture stdout for score/grade if available
6. Cleanup:
   - Delete \\{host}\C$\Windows\Temp\KryossAgent.exe via SMB
7. STATUS: "[3/12] DC01 ... OK (score: 82/B)" or "[3/12] DC01 ... FAILED (WinRM timeout)"
```

#### 2.4 Credential Handling

- **Default (no `--credential`):** Use current Windows identity. Tech must be running as domain admin or account with local admin on all targets.
- **With `--credential`:** Prompt for `domain\username` and password (masked input via `Console.ReadKey`). Store as `NetworkCredential` in memory only — never persisted. Used for both SMB copy and WinRM `Invoke-Command -Credential`.

#### 2.5 Results & Enrollment

Each remote machine:
- Enrolls itself individually via `POST /v1/enroll` (using the provided enrollment code)
- Runs the full assessment locally
- Uploads results via `POST /v1/results` (HMAC-signed with its own credentials)
- Appears as a separate machine in the portal dashboard

The scanning workstation is a deployer only — it doesn't aggregate or proxy results.

#### 2.6 Console Summary

After all targets complete:

```
=== Kryoss Network Scan Complete ===
  Targets found:    12
  Scanned:          10
  Unreachable:       2  (WS-FRONT02: no WinRM, WS-LAB05: no SMB)

  Results:
    Pass (A/B):      6
    Warning (C):     3
    Fail (D/F):      1

  Details:
    DC01          92/A    WS-ACCT01     78/B
    WS-HR01       65/C    WS-DEV03      45/F
    ...
```

#### 2.7 Prerequisites

For the remote scan to work, target machines need:
- **WinRM enabled** (standard in domain environments, `winrm quickconfig` or GPO)
- **SMB accessible** (port 445, admin share `C$` available)
- **Firewall allowing inbound 5985** (WinRM HTTP)

If a target fails connectivity, the agent reports exactly which port/service is missing.

---

## Shared Concerns

### Enrollment Code Lifecycle

Both features use the same enrollment code. The portal generates one code per organization. Each machine that enrolls with it gets a unique `AgentId` and credentials. The code can be single-use or multi-use depending on portal config (existing behavior).

### AOT Compatibility

- Feature 1: No AOT impact — just const strings and byte comparison
- Feature 2: `System.DirectoryServices` (for AD discovery) is AOT-compatible on Windows. `Process.Start` for WinRM is already used by ShellEngine. SMB file copy via `File.Copy` with UNC paths is AOT-safe.

### Security Considerations

- The patched binary contains the enrollment code in cleartext — acceptable since enrollment codes are short-lived and single/limited-use
- Remote scan requires admin-level access — this is inherent to the use case (MSP tech assessing a client network)
- Credentials from `--credential` are held in memory only, never written to disk or registry
- The agent .exe is copied to `C:\Windows\Temp` and deleted after execution — minimal footprint

---

## Out of Scope (Phase 2+)

- Code signing after binary patching
- Permanent agent deployment via `--scan` (install as service on targets)
- Linux/macOS agent support
- Network device scanning (switches, firewalls)
- Agent auto-update mechanism
