# Kryoss Platform — Phase Roadmap

**Owner:** TeamLogic IT / Geminis Computer S.A.
**Last updated:** 2026-04-08
**Status:** Phase 1 catalog closed — ready to build portal

This document is the authoritative reference for what is in scope
per phase and what stays out. Whenever you're tempted to expand
scope mid-phase, re-read this file first.

---

## Guiding principle

> A real client has **workstations + servers + cloud + network**.
> To sell the full audit we eventually need all four sources.
> To ship the MVP we only need workstations, because that already
> proves value for ~70% of small MSP clients.

Ship narrow → validate → expand.

---

## Phase 1 — Workstation (CURRENT)

**Scope:** Windows 10 + Windows 11 endpoints only.
**Status:** Catalog closed. Portal in progress.

### What is included

- **647 active control_defs** in `control_defs` (SC-001..161, BL-0001..0486)
- Linked to platforms `W10` and `W11` only in `control_platforms`
- Framework coverage (active controls, approximate after seed_008):
  | Framework | Tagged | % |
  |---|---|---|
  | NIST     | 647 | 100.0 |
  | CIS      | ~637 | ~98.5 |
  | HIPAA    | ~320 | ~49.5 |
  | ISO27001 | ~179 | ~27.7 |
  | PCI-DSS  |  ~18 |  ~2.8 |
- 11 collector engines required:
  `registry` (356), `command` (211), `auditpol` (24),
  `firewall` (21), `service` (11), `netaccount` (5), `secedit` (2),
  `eventlog` (4), `certstore` (6), `bitlocker` (4), `tpm` (3)
- `machine_snapshots.raw_*` columns for raw data sink
- HIPAA §164.312 Technical Safeguards covered at 100% including
  the three refinement blocks added in seed_005:
  - MFA / Windows Hello for Business / Smart Card (BL-0445..0450)
  - Event log retention (BL-0451..0459)
  - Backup posture (BL-0460..0469)

### What is intentionally OUT of Phase 1

| Area | Why it's out |
|---|---|
| Windows Server (MS19/22/25) | Needs separate CIS Server benchmark + server-specific controls (IIS, SMB shares, Hyper-V). Out of Phase 1. |
| Domain Controllers (DC19/22/25) | Needs AD health checks (replication, FSMO, LDAPS, recycle bin) that don't exist in catalog yet. |
| Microsoft 365 / Entra ID | Requires Graph API connector, not the Windows agent. |
| Google Workspace | Requires Admin SDK connector. |
| Network perimeter (firewalls, switches, APs) | Requires per-vendor API or network scanner. |
| HIPAA §164.308 Administrative Safeguards | Non-technical. Belongs in portal as attestation forms. |
| HIPAA §164.310 Physical Safeguards | Non-technical. Portal attestation. |
| Vulnerability scanning (real CVE matching) | Requires CVE feed + scanner (Qualys/Nessus/Defender). We only detect presence of such agents. |

### Deliverables to close Phase 1

- [x] Catalog seeded (seed_004 + seed_005 + seed_006b + seed_007 + seed_007b)
- [x] DB health verified against Azure SQL
- [x] `docs/agent-payload-schema.md` updated with `raw_security_posture.mfa`, `.event_logs`, `.backup_posture` (schema v1.1 — agent-side implementation deferred until a portal consumer exists; the 25 HIPAA refinement controls BL-0445..BL-0469 already evaluate individually via the existing registry/service/command engines, so the rich `raw_*` blocks are a future holistic-report channel, not an evaluation blocker)
- [x] Backend catalog endpoint — `GET /api/v2/catalog/controls?platform=W11&framework=HIPAA` in `Functions/Portal/CatalogControlsFunction.cs` (2026-04-09). Note: the original roadmap entry said `/api/v1/...` but that predates the v1/v2 convention (v1 = agent routes, RBAC-skipped; v2 = portal routes, RBAC-enforced via `[RequirePermission("controls:read")]`). The endpoint correctly lives under v2.
- [ ] Portal MVP: enrollment → snapshot ingest → controls list → per-framework report

### Official backend catalog query

```sql
SELECT cd.*
FROM control_defs cd
INNER JOIN control_platforms cp ON cp.control_def_id = cd.id
INNER JOIN platforms p          ON p.id = cp.platform_id
WHERE cd.is_active = 1
  AND p.code = @platformCode    -- agent sends 'W10' or 'W11'
```

The agent detects platform at runtime via
`Get-CimInstance Win32_OperatingSystem` and reads `ProductType`:

| ProductType | Meaning | Phase 1 behavior |
|---|---|---|
| 1 | Workstation | Requests `W10` or `W11` → gets 630 controls |
| 2 | Domain Controller | Requests `DC*` → gets **0 controls** (Phase 3) |
| 3 | Server member | Requests `MS*` → gets **0 controls** (Phase 2) |

Phase 1 agents should detect ProductType != 1 and abort gracefully
with a "Server scope not enabled yet — contact MSP" message.

---

## Phase 2 — Windows Server Member

**Trigger:** Phase 1 portal MVP working end-to-end in production
with at least one real client.

**Scope:** Windows Server 2019 / 2022 / 2025 as member servers
(not domain controllers).

### Estimated work

1. **seed_008_platform_scope_server.sql**
   Map the ~450 existing controls that apply to servers to
   `MS19`, `MS22`, `MS25`. Exclude:
   - Office Hardening (78) — Office not installed on servers
   - Browser Hardening (43) — no interactive browser use
   - Xbox / Cortana / OneDrive consumer / Windows Ink / Personalization (~50)
   - Some workstation-only defender settings

2. **seed_009_controls_server_specific.sql**
   Add ~80 new control_defs for server-specific checks:
   - IIS hardening (if role present)
   - SMB share permissions enumeration
   - File Server role config
   - Print Server role config
   - DNS Server role config
   - DHCP Server role config
   - Hyper-V host hardening (if role present)
   - Remote Desktop Session Host
   - Windows Admin Center detection
   - Server Core detection (affects which checks are N/A)

3. **Agent update:** role detection via
   `Get-WindowsFeature | Where Installed` (Server SKU only) so
   the backend can return only the subset matching installed roles.

4. **New reports in portal:**
   - CIS Server 2022 L1 Member Server Benchmark
   - CIS Server 2019 L1 Member Server Benchmark

**Estimated effort:** 2–3 weeks once Phase 1 is shipped.

---

## Phase 3 — Domain Controllers

**Trigger:** At least one client with on-premise AD buys Phase 2
and asks for DC audit.

**Scope:** Windows Server DCs (DC19/DC22/DC25).

### Estimated work

1. **seed_010_platform_scope_dc.sql**
   Extend Phase 2 server mapping to DC platforms with additional
   DC-specific controls.

2. **seed_011_controls_dc_specific.sql**
   Add ~60 DC-only control_defs:
   - AD replication health (`repadmin /replsummary`)
   - FSMO role holders
   - LDAPS enforcement
   - LDAP signing and channel binding
   - AD recycle bin enabled
   - Protected Users group usage
   - Authentication policies and silos
   - Krbtgt password age
   - AD backup via System State
   - SYSVOL / NETLOGON permissions
   - Default domain policy vs local policy enforcement
   - Secure dynamic DNS updates
   - DCDiag key tests

3. **Agent update:** DC detection via `ProductType=2` — the agent
   must run as `DOMAIN\Administrator` or an account with
   `Replicate Directory Changes` to read some of these.

4. **New report:** CIS Microsoft Windows Server DC Benchmark

**Estimated effort:** 3–4 weeks.

---

## Phase 4 — Cloud Workspace Hardening

**Trigger:** Phase 1–3 shipped. Most of our clients are M365.

**Scope:** Microsoft 365 / Entra ID / Defender / Purview.
Optionally Google Workspace.

### Estimated work

1. **New connector service** (not the Windows agent):
   Go service that uses Microsoft Graph with app-only auth
   (client credentials flow) against each tenant.

2. **New control_defs source type:** `graph_api` engine.

3. **Controls to add (~120):**
   - Conditional Access policies inventory and evaluation
   - MFA enforcement per user / per group / tenant-wide
   - Legacy auth blocked
   - Security defaults enabled or replaced by CA
   - Privileged Identity Management (PIM) usage
   - Guest user policies
   - Sharing policies (SharePoint, OneDrive, Teams)
   - DLP policies (Purview)
   - Retention policies
   - Defender for Office 365 (Safe Links, Safe Attachments)
   - Defender for Endpoint enrollment
   - Defender for Cloud Apps
   - Intune compliance and configuration profiles
   - Unified audit log enabled
   - Mailbox auditing per user
   - External forwarding rules
   - Admin role assignments audit

4. **New report:** M365 Tenant Security Posture.

5. **Kryoss franchise portal update:** per-tenant connector
   enrollment UI with consent flow.

**Estimated effort:** 4–6 weeks.

---

## Phase 5 — Network / Perimeter

**Trigger:** Phase 4 shipped. Enterprise clients asking for
"full stack" audits.

**Scope:** On-premise network devices (firewalls, switches, APs).

### Approach

Hybrid: three possible data sources depending on vendor:

1. **Vendor API** (best): Fortinet FortiGate, Sophos, SonicWall,
   Cisco Meraki, Ubiquiti UniFi — all have REST APIs.

2. **SNMP v3** (fallback for managed switches and APs)

3. **SSH config dump parsing** (fallback for Cisco IOS / ASA)

This phase builds on the existing `NetworkDiscovery\` PowerShell
scripts and promotes them into a proper Go service.

### Controls to add (~80)

- Firewall rule base hygiene (any/any rules, orphaned rules)
- Admin interface exposure
- Firmware currency
- Log export configuration
- IDS/IPS enabled
- Guest VLAN isolation
- Wireless encryption (WPA3 preferred, WPA2 minimum)
- 802.1X on wired ports
- STP protection (BPDU guard, root guard)
- Port security
- SNMP community strings not default
- NTP source configured
- Syslog forwarding to SIEM

**Estimated effort:** 6–8 weeks.

---

## Phase 6 — Compliance attestation & manual controls

**Trigger:** Phases 1–5 live. Clients asking for audit-grade
HIPAA / SOC2 reports.

**Scope:** Non-technical controls that no agent can collect.

### What this adds

- **Attestation controls table:** checklist items the MSP marks
  manually during onboarding (policies written? training log?
  BAA signed? incident response plan tested?)
- **Evidence upload:** PDF / DOCX attachments per attestation
  control for audit trail
- **Review workflow:** technician fills → supervisor signs off
- **HIPAA §164.308 Administrative Safeguards** (full coverage)
- **HIPAA §164.310 Physical Safeguards** (full coverage)
- **SOC 2 Trust Services Criteria** (those not covered by
  technical controls)

### Report impact

Once this phase ships, HIPAA reports can honestly state:
"Technical Safeguards: X/Y automated. Administrative: A/B attested.
Physical: C/D attested. Overall compliance: Z%."

**Estimated effort:** 2–3 weeks (mostly UI, the data model is
small).

---

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-04-08 | Phase 1 limited to W10/W11 only | MVP focus, avoid scope creep, 70% of SMB client fleets are workstations |
| 2026-04-08 | Legacy BL-XXX (3-digit) controls soft-deleted not hard-deleted | FK to `assessment_controls` prevented delete; soft-delete preserves historical assessment data |
| 2026-04-08 | 25 new HIPAA refinement controls added in seed_005 | Cover MFA / event log retention / backup posture — the only real gaps in §164.312 technical coverage |
| 2026-04-08 | `control_platforms` is the scope enforcement mechanism | Lets us add servers in Phase 2 with a single `INSERT` seed, no catalog surgery |
| 2026-04-08 | Agent stays dumb, backend resolves `platform_id` from `os_name` | Zero agent refactor for scope enforcement; single source of truth in `PlatformResolver` |
| 2026-04-08 | Ship 4 new engines (eventlog/certstore/bitlocker/tpm) in same redeploy cycle | Avoid incremental redeploys; one binary covers all Phase 1 data needs |
| 2026-04-08 | BL-0458..0466 (legacy wevtutil command controls) left as-is | New `eventlog` engine used only for NEW controls to keep migration blast radius small |

---

## Collector (agent) gap backlog — post Phase 1 engines

As of 2026-04-08 the agent has 11 engines and the control catalog
has 647 active checks scoped to W10/W11. The **check execution**
side of the collector is complete. What still blocks rich reports
and full parity with the v1.1 payload schema is listed below, in
priority order.

> **Source of truth for payload shape:** `KryossApi/docs/agent-payload-schema.md`

### P1 — Payload enrichment (`raw_*` blocks, schema v1.0 → v1.1)

Today `AssessmentPayload` sends `Platform` (3 fields), `Hardware`
(4 fields: cpu/ramGb/diskType/tpm), a flat `Software` list, and the
`Results` array. The v1.1 schema expects 5 rich raw blocks with
dozens of fields each. Without them the portal can evaluate the 647
controls but cannot build Executive / Technical reports with
hardware lifecycle, network topology, user sprawl, backup freshness,
or MFA posture sections.

| Block | Missing data | New collector |
|---|---|---|
| `raw_hardware` | serial, model, manufacturer, BIOS version/date, UEFI/Legacy, SecureBoot, RAM slots occupied/free, per-volume disk (total/free/fs/BitLocker), GPU, battery | extend `PlatformDetector` or split into `HardwareCollector` |
| `raw_network` | adapters (name, MAC, IP/mask, gateway, DNS, DHCP), default route, WiFi profiles, open local ports (netstat), proxy config | **`NetworkCollector` (NEW)** |
| `raw_users` | local accounts (name, SID, enabled, lastLogon, passwordLastSet, flags), Administrators/Users group members, `C:\Users\*` profile sizes + lastWrite, active sessions | **`UserCollector` (NEW)** |
| `raw_software` | already collected but missing `installDate`, `installLocation`, `uninstallString`, `estimatedSize`, arch (x86/x64); dedupe should be by `(name, version)` not just `name` | extend `SoftwareInventory` |
| `raw_security_posture` | Defender (enabled, realtimeProtection, signatureDate, threats), SmartScreen, UAC, ASR rules, effective firewall profiles, LAPS, Credential Guard, DMA protection | **`SecurityPostureCollector` (NEW)** |
| `raw_security_posture.mfa` | WHfB provisioned state, NGC container populated, parsed dsregcmd output, TPM-bound credentials | **`MfaCollector` (NEW)** — may live inside `SecurityPostureCollector` |
| `raw_security_posture.event_logs` | last 10 critical events from System/App, parsed wevtutil gl (SDDL perms) | reuse `EventLogEngine` with new `checkType` or split collector |
| `raw_security_posture.backup_posture` | parsed wbadmin versions, VSS shadow list, 3rd-party agent detection (Veeam/Datto/Acronis/Carbonite/etc.) | **`BackupCollector` (NEW)** |

**Also consolidate:** the new `TpmEngine` collects spec_version,
manufacturer, ready_state — but those never cross over into
`HardwareInfo` on the payload. Hardware summary should pull from
the engine's batched TPM info.

**Effort:** 4–6 new collectors, all registry + shell based to stay
AOT-compatible. Estimate 1–2 days of focused work.

### P2 — `CryptoService` envelope wire-up (decision pending)

`Services/CryptoService.cs` already implements RSA-OAEP + AES-256-GCM
envelope encryption: `EncryptEnvelope(json, publicKeyPem)` →
`{EncryptedKey, EncryptedPayload, Iv, Tag}`. But
`ApiClient.SubmitResultsAsync` sends JSON in **plaintext** with only
an HMAC signature on top. The architecture promises "payload
encryption end-to-end" but that promise is not kept today.

**Decision required:**
- **A)** Wire envelope encryption into `ApiClient`, verify backend
  decrypts with private key from Key Vault end-to-end.
- **B)** Remove `CryptoService.cs` entirely and document "HMAC-SHA256
  over TLS 1.2+" as the Phase 1 security posture.

Not blocking, but must be resolved before any marketing material
claims "end-to-end encrypted payload."

### P3 — `publish.ps1` AOT override

The `.csproj` declares `PublishAot=true` but `publish.ps1` passes
`/p:PublishAot=false`. Result: shipped binary is self-contained
single-file with embedded runtime (~68 MB, ~800 ms startup), not
true AOT (~15 MB, sub-100 ms startup).

Impact:
- Slow startup hurts scheduled-task RMM runs
- 4x larger deploy size over NinjaRMM
- The 11 engines use zero reflection/dynamic code, so AOT *should*
  compile clean — but nobody has tested it since adding the new
  `System.Diagnostics.EventLog` dependency

**Action:** run `publish.ps1` with `PublishAot=true`, triage any
trim/AOT warnings, decide whether to ship AOT or keep self-contained.

### P4 — Missing agent modes (Phase 2 territory)

The original plan promised three run modes beyond the current
interactive/silent pair:

| Mode | Flag | Status | Blocks |
|---|---|---|---|
| Remote DC scanner | `--remote --targets PC1,PC2` | ❌ not implemented | Presales-AD flow ("technician at the DC scans every PC via WinRM") |
| Offline fallback with cached controls | automatic | ⚠️ partial — `OfflineStore` queues **results**, but agent cannot run without a fresh `/v1/controls` response | Site visits with no internet |
| Offline fallback (results) | automatic | ✅ implemented | — |

The remote mode needs a `RemoteScanner.cs` service that uses WinRM
(`System.Management.Automation.Remoting` is NOT AOT-safe — will
need to shell to `winrs` or `Invoke-Command` via PowerShell hosted
process, which itself is problematic in AOT). This is a **Phase 2
problem** aligned with server support.

### P5 — Housekeeping / cleanup

- `Helpers/` folder exists but is empty — populate or remove
- Test project `KryossAgent.Tests/` referenced in the plan was
  never created
- `SoftwareInventory` dedupes by `name` only, losing side-by-side
  version installs
- Sample/test config `9e201df0-...._config.json` should not be in
  `src/KryossAgent/` — move to a gitignored location
- `PlatformDetector.DetectHardware()` uses a TPM heuristic
  (service key presence); now that `TpmEngine` exists with real
  data, consolidate

### Recommended sequencing

1. **Before touching any of this:** apply the 3 SQL migrations
   (`seed_005b`, `015_machine_platform_id`, `seed_008`), publish
   the current agent, and run end-to-end against a real W11 VM.
   Verify the new engines work against real hardware before
   adding more collector code.
2. **Next sprint:** P1 — the 4 new collectors. This is the highest
   leverage work — unlocks all Phase 1 reports.
3. **Then:** P3 — decide AOT vs self-contained based on measured
   performance and package size.
4. **Then:** P2 — resolve the CryptoService fate.
5. **Deferred to Phase 2:** P4 — remote mode + offline controls
   cache, aligned with server support work.
6. **Continuous:** P5 — housekeeping as opportunities arise.

---

## Reference: current catalog metrics (post seed_008)

```
Total control_defs:              738
  Active:                        647
  Inactive (legacy soft-deleted): 91

Active by prefix:
  SC  161   (SC-001..SC-161)
  BL  486   (BL-0001..BL-0486)

Active by engine:
  registry     356
  command      211
  auditpol      24
  firewall      21
  service       11
  netaccount     5
  secedit        2
  eventlog       4    (seed_008)
  certstore      6    (seed_008)
  bitlocker      4    (seed_008)
  tpm            3    (seed_008)

Active by framework (approximate — seed_008 adds ~17 each where tagged):
  NIST       647 (100.0%)
  CIS       ~637 (~98.5%)
  HIPAA     ~320 (~49.5%)
  ISO27001  ~179 (~27.7%)
  PCI-DSS    ~18 ( ~2.8%)

Platform mapping:
  W10  →  647 active controls linked
  W11  →  647 active controls linked
  MS*  →  0 (Phase 2)
  DC*  →  0 (Phase 3)
```
