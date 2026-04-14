# Agent Security Refactor — Remove PsExec + Native .NET Engines

## Context

The Kryoss agent (v1.2.2) embeds PsExec for remote deployment and uses `Process.Start` to shell out to ~30 Windows executables for security checks. This is a security antipattern:

1. **PsExec** — flagged by AV/EDR, passes credentials on command line (visible in process audit logs), requires admin shares. A security product should not use hacker tools.
2. **Shell commands** — `Process.Start` is interceptable, command-line args are logged in Event ID 4688, external executables can be tampered with (DLL hijacking, path injection).

**Goal:** The agent becomes a passive sensor with zero remote execution capability, deployed by enterprise infrastructure (GPO/NinjaOne/Intune), using only native .NET/Win32 APIs.

---

## Phase A: Remove PsExec + Remote Deployment (v1.3.0)

### What to remove

| File | Action |
|------|--------|
| `Services/PsExecRunner.cs` | DELETE entirely |
| `Services/NetworkScanner.cs` | STRIP remote deployment (keep port scan + hygiene upload) |
| `Program.cs` lines 422-445 | Remove auto-network-scan after local assessment |
| `--scan` mode | KEEP but rewrite to only do: discovery + port scan + AD hygiene (no PsExec) |
| `--credential` flag | REMOVE (no longer needed) |
| Embedded PsExec resource | REMOVE from .csproj |

### What to keep

| Component | Why |
|-----------|-----|
| `TargetDiscovery.cs` | AD/ARP/subnet discovery is valuable inventory data |
| `PortScanner.cs` | Port scanning is a legitimate security check |
| AD Hygiene in `TargetDiscovery.cs` | Privileged accounts, stale objects, LAPS — all read-only |
| `--alone` flag | Becomes the default behavior (always local-only) |
| `--scan` flag | Redefine: network discovery + port scan + AD hygiene (no deployment) |

### NetworkScanner.cs rewrite

Strip to:
```
1. Discover targets (AD/ARP/subnet) — KEEP
2. Port scan discovered targets — KEEP
3. Upload port results to API — KEEP
4. AD Hygiene report — KEEP
5. Deploy agent via PsExec — REMOVE
6. Credential prompt — REMOVE
7. SMB copy — REMOVE
8. Remote execution — REMOVE
```

### Deployment instead: GPO / NinjaOne / Intune

Create deployment packages in `Scripts/Deploy/`:

1. **GPO deployment:**
   - `Deploy-KryossAgent.ps1` — copies .exe to `\\NETLOGON\Kryoss\` or SYSVOL
   - GPO Computer Startup Script or Scheduled Task that runs the agent
   - Agent runs with `--silent --code XXXX` (same as today's remote execution, but initiated by GPO)

2. **NinjaOne/RMM:**
   - Upload .exe as NinjaOne script payload
   - NinjaOne runs it with `--silent --code XXXX`
   - Agent output (`RESULT:` lines) captured by NinjaOne

3. **Intune:**
   - Package as Win32 app (.intunewin)
   - Detection rule: `HKLM\SOFTWARE\Kryoss\Agent\AgentId` exists
   - Install command: `KryossAgent.exe --silent --code XXXX`

---

## Phase B: Convert Batch Engines to Native .NET (v1.4.0)

### Engine conversion plan

#### 1. SeceditEngine → NativeSecurityPolicyEngine

**Today:** `secedit.exe /export /cfg temp.inf` → parse INF file
**Native:** Direct Win32 P/Invoke

| API | What it reads |
|-----|---------------|
| `LsaOpenPolicy` + `LsaQueryInformationPolicy` | Account lockout, password policy |
| `NetUserModalsGet` (level 0, 1, 3) | Password age, min length, history, lockout threshold/duration |

This also replaces `NetAccountsEngine` (which calls `net accounts`).

**Merge:** `SeceditEngine` + `NetAccountsEngine` → `SecurityPolicyEngine` (native)

~31 controls (26 secedit + 5 netaccount)

#### 2. AuditpolEngine → NativeAuditPolicyEngine

**Today:** `auditpol /get /category:* /r` → parse CSV
**Native:** `AuditQuerySystemPolicy` + `AuditQueryGlobalSacl` P/Invoke from `advapi32.dll`

```csharp
[DllImport("advapi32.dll")]
static extern bool AuditQuerySystemPolicy(
    Guid[] pSubCategoryGuids,
    uint dwPolicyCount,
    out IntPtr ppAuditPolicy);
```

~24 controls

#### 3. BitLockerEngine → NativeBitLockerEngine

**Today:** `manage-bde -status` → parse text output
**Native:** WMI/CIM `Win32_EncryptableVolume` class (namespace: `root\CIMV2\Security\MicrosoftVolumeEncryption`)

```csharp
using var searcher = new ManagementObjectSearcher(
    @"root\CIMV2\Security\MicrosoftVolumeEncryption",
    "SELECT * FROM Win32_EncryptableVolume");
```

Fields: `ProtectionStatus`, `ConversionStatus`, `EncryptionMethod`, `LockStatus`

~4 controls

**Note:** WMI is needed here — no pure registry/P/Invoke alternative for BitLocker status. BUT this is a read-only WMI query, not command execution. AOT-compatible via `System.Management`.

#### 4. TpmEngine → NativeTpmEngine

**Today:** registry + `tpmtool getdeviceinformation` → parse text
**Native:** 
- Registry: `HKLM\SYSTEM\CurrentControlSet\Services\TPM` (already done)
- WMI: `Win32_Tpm` class (namespace: `root\CIMV2\Security\MicrosoftTpm`)
- Alternatively: `TBS_CONTEXT` API via P/Invoke (lower level)

```csharp
using var searcher = new ManagementObjectSearcher(
    @"root\CIMV2\Security\MicrosoftTpm",
    "SELECT * FROM Win32_Tpm");
// .IsEnabled_InitialValue, .IsActivated_InitialValue, .SpecVersion
```

~3 controls

#### 5. ShellEngine → Multiple Specialized Engines

The 211 "command" controls break down as:

**Actually using executables (~10 controls from seed_005):**

| Executable | Controls | Native replacement |
|-----------|----------|-------------------|
| `wevtutil.exe` | 2 (BL-0458, 0459) | Already covered by `EventLogEngine` — merge |
| `wbadmin.exe` | 1 (BL-0460) | WMI `Win32_ShadowCopy` + registry `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WindowsBackup` |
| `vssadmin.exe` | 2 (BL-0461, 0462) | WMI `Win32_ShadowCopy` + `Win32_ShadowProvider` |
| `dsregcmd.exe` | 1 (BL-0449) | Registry `HKLM\SYSTEM\CurrentControlSet\Control\CloudDomainJoin` + `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WorkplaceJoin` |
| `cmd.exe` | 1 (BL-0450) | `Directory.Exists` / `File.Exists` in .NET |
| `citool.exe` | 1 (BL-0339) | Registry `HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy` |

**New native engines needed:**

| Engine | Replaces | Controls | Tech |
|--------|----------|----------|------|
| `BackupEngine` | wbadmin, vssadmin | 3 | WMI `Win32_ShadowCopy` + registry |
| `DomainJoinEngine` | dsregcmd | 1 | Registry (CloudDomainJoin, WorkplaceJoin) |
| `WdacEngine` | citool | 1 | Registry (Code Integrity) |

**Legacy `function` field controls (~200 from seed_004):**

These reference PowerShell functions like `Test-SMBv1`, `Test-LLMNR`, etc. They were auto-extracted from legacy scripts. Most likely ShellEngine SKIPS these because they don't have `executable` set — need to verify.

**Action:** Audit which of the 200 legacy controls actually execute vs skip. If they skip, they're dead controls that should be:
1. Converted to registry/native checks if the setting CAN be read natively
2. Deactivated if they can't be checked without PowerShell

---

## Phase C: Harden Remaining Process.Start (if any)

If any controls genuinely REQUIRE external executables (unlikely after Phase B), harden:

1. **Full path only** — `C:\Windows\System32\foo.exe`, never just `foo.exe`
2. **Hash verification** — SHA-256 of the executable must match a known-good hash compiled into the agent
3. **No shell** — `UseShellExecute = false`, `CreateNoWindow = true` (already done)
4. **Argument sanitization** — no user input, no string interpolation in args
5. **Timeout** — already honored (`TimeoutSeconds` field)

---

## Migration path

| Version | What changes | Breaking? |
|---------|-------------|-----------|
| v1.3.0 | Remove PsExec, `--scan` = discovery+ports only, add GPO/RMM deploy scripts | No — local scan identical |
| v1.4.0 | Native engines replace shell engines | No — same controls, same results, different tech |
| v1.5.0 | Audit + deactivate legacy `function` controls, remove ShellEngine if empty | Maybe — some controls may be deactivated |

## Files to modify (v1.3.0)

| File | Change |
|------|--------|
| `Services/PsExecRunner.cs` | DELETE |
| `Services/NetworkScanner.cs` | Strip to discovery + port scan + hygiene |
| `Program.cs` | Remove auto-network-scan, simplify CLI flags |
| `KryossAgent.csproj` | Remove PsExec embedded resource |
| `Scripts/Deploy/Deploy-KryossGPO.ps1` | NEW — GPO deployment package |
| `Scripts/Deploy/Deploy-KryossNinja.ps1` | NEW — NinjaOne deployment script |

## Files to modify (v1.4.0)

| File | Change |
|------|--------|
| `Engines/SeceditEngine.cs` | Rewrite → `SecurityPolicyEngine.cs` (P/Invoke) |
| `Engines/NetAccountsEngine.cs` | MERGE into SecurityPolicyEngine |
| `Engines/AuditpolEngine.cs` | Rewrite → `NativeAuditPolicyEngine.cs` (P/Invoke) |
| `Engines/BitLockerEngine.cs` | Rewrite → WMI query |
| `Engines/TpmEngine.cs` | Rewrite → WMI + registry (drop tpmtool) |
| `Engines/ShellEngine.cs` | Phase out → new specialized engines |
| `Engines/BackupEngine.cs` | NEW — WMI shadow copies + backup registry |
| `Engines/DomainJoinEngine.cs` | NEW — registry-based join detection |
| `Engines/WdacEngine.cs` | NEW — registry-based WDAC policy |

## Verification

- All 647 active controls must produce the same PASS/FAIL results before and after
- Run agent v1.3.0 and v1.2.2 side by side on same machine, compare `AssessmentPayload.Results`
- Zero `Process.Start` calls remaining after v1.4.0 (grep verification)
- AV/EDR should not flag the new agent (no PsExec, no suspicious process spawning)
