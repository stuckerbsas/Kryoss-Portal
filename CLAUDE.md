# Projecto Kryoss — Master Index

**Owner:** Federico / Geminis Computer S.A. / TeamLogic IT
**Purpose of this file:** Give any future Claude session enough context to work on the project **without re-exploring the filesystem**. Read this first, then the subfolder CLAUDE.md of whatever you're working on.

**IMPORTANT — if you update anything in this repo, update the relevant CLAUDE.md too.** The point is to not waste tokens rediscovering the same things.

---

## What Kryoss is

A security assessment SaaS for MSPs (primary user: TeamLogic IT franchise, built by Geminis Computer). The platform has three pillars:

1. **Audit compliance** — run ~630 checks against Windows endpoints and score them against CIS / NIST / HIPAA / ISO 27001 / PCI-DSS
2. **Ticket reduction** — detect drift/misconfig early and feed findings into a helpdesk/PSA workflow
3. **Cloud workspace hardening** — eventually cover M365 / Entra ID / Google Workspace via connectors (Phase 4, not started)

**Business model:** Kryoss is the backend. Franchise portal = MSPs sign up, enroll client machines, generate reports, sell remediation.

---

## Repository layout (only the folders that matter)

```
Projecto Kryoss\
├── CLAUDE.md                       <- YOU ARE HERE (master index)
├── KryossApi\                      <- Backend (.NET 8 Azure Functions)
│   └── CLAUDE.md                   <- See this for full API map
├── KryossAgent\                    <- Windows agent (.NET 8 AOT)
│   └── CLAUDE.md                   <- See this for full agent map
├── KryossPortal\                   <- Frontend (React 18 + Vite + TS + shadcn/ui + MSAL)
│                                      Repo: github.com/stuckerbsas/Kryoss-Portal
│                                      Deployed: zealous-dune-0ac672d10.6.azurestaticapps.net
│                                      Auth: MSAL (@azure/msal-browser) + Entra ID Bearer token
│                                      Friendly URLs: /organizations/{slug}/machines/{hostname}
│                                      Spec: docs/superpowers/specs/2026-04-10-kryoss-portal-mvp-design.md
│                                      Plan: docs/superpowers/plans/2026-04-10-kryoss-portal-mvp.md
├── Scripts\                        <- Legacy PowerShell scripts & audit tools
│   └── CLAUDE.md                   <- MSP/TeamLogic IT PowerShell standards
│                                      (NinjaRMM/Intune deploy, brand, etc.)
├── Kryoss Partner Portal\          <- Old/different portal variant — UNUSED
├── The Final portal\               <- Legacy production Go+Vue CRM+auth portal
│                                      (see user memory: existing portal)
├── New Portal\                     <- Another variant — UNUSED / unclear
├── Kryoss.Portal\                  <- UNUSED / unclear
├── Kryoss\ / Kryoss-main\          <- UNUSED / legacy
└── docs\, Procedimientos\, Web\    <- Non-code: brand, docs, web assets
```

**Rule of thumb:** If you need code, look at `KryossApi\` (backend), `KryossAgent\` (agent), or `KryossPortal\` (frontend). The other "portal"-named folders are dead code — **don't touch them without asking first**.

---

## Tech stack summary

| Layer | Tech | Status |
|---|---|---|
| Backend API | .NET 8 Azure Functions + EF Core 8 + Azure SQL | ✅ Deployed (`func-kryoss`) |
| Database | Azure SQL `sql-kryoss.database.windows.net` / DB `KryossDb` | ✅ Seeded with ~647 active controls |
| Agent | .NET 8 v1.2.2 (win-x64, single-file, ~68 MB self-contained) | ✅ 11 engines + network scanner (AD/ARP/subnet discovery, PsExec remote deploy) + port scanner (TCP top 100 + UDP top 20) + AD hygiene audit + binary patching sentinels |
| Portal | React 18 + Vite + TS + shadcn/ui + MSAL, deployed on Azure SWA | ✅ Deployed (`zealous-dune-0ac672d10.6.azurestaticapps.net`) |
| Auth | Agent: API Key + HMAC-SHA256 (`ApiKeyAuthMiddleware`). Portal: MSAL JWT via `BearerAuthMiddleware`. Easy Auth DISABLED on func-kryoss (platform.enabled=true, requireAuthentication=false, AllowAnonymous). All auth handled by custom middleware. | ✅ Implemented in middleware |
| Crypto | RSA-2048 + AES-256-GCM for payload encryption (agent → API) | ⚠️ Agent has `CryptoService.cs` but current `ApiClient.cs` only uses HMAC — payload encryption may not be wired up end-to-end yet |
| RLS | SQL Session Context, applied per request via `RlsMiddleware` | ✅ Implemented |

---

## Control catalog state (as of 2026-04-10)

This is the heart of the product. Memorize it:

```
control_defs total: 738  (721 prior + 17 seed_008 new-engine controls)
  Active:   647  (161 SC scored + 469 BL baseline + 17 new-engine)
  Inactive:  91  (legacy BL-XXX soft-deleted, superseded by BL-0XXX)

Active by engine (dispatch type):
  registry    356    command     211
  auditpol     24    firewall     21
  service      11    netaccount    5
  secedit       2    eventlog      4
  certstore     6    bitlocker     4
  tpm           3

Framework coverage (active):
  NIST       647 (100.0%)
  CIS        ~637 (~98.5%)
  HIPAA      ~320 (~49.5%)
  ISO27001   ~179 (~27.7%)
  PCI-DSS     ~18 (~2.8%)

Platform scope (as of 2026-04-10):
  W10, W11 → 647 controls linked each
  MS19, MS22, MS25 → 647 controls linked each (seed_007c)
  DC19/22/25 → 0 (Phase 2 roadmap)
```

**Authoritative DB check script:** `KryossApi/sql/check_catalog_health.sql`
**Roadmap for phases 2-6:** `KryossApi/docs/phase-roadmap.md`
**Agent payload contract v1.1:** `KryossApi/docs/agent-payload-schema.md`

---

## Security baseline (READ BEFORE TOUCHING AUTH / CRYPTO / INGEST)

**Authoritative doc:** `KryossApi/docs/security-baseline.md`

Zero-trust defense-in-depth is now the stated architecture. Every change
to authentication, crypto, key management, or the ingest flow MUST be
audited against that file. Short version of the contract:

- **Envelope:** RSA-OAEP-256 wrap + AES-256-GCM, AAD binds ciphertext to
  `hwid` and `tokenId` so stolen envelopes can't be replayed from
  another machine.
- **Integrity:** HMAC-SHA256 over a frozen canonical request
  (`ts\nMETHOD\npath\nhwid\ntokenId\nhex(sha256(body))`), key derived
  via HKDF from the per-device shared secret.
- **Replay:** nonce cache (signature as nonce, 10 min TTL) + ±300 s
  timestamp window.
- **Identity:** TPM-bound hardware fingerprint, checked on every request.
- **Infra:** Managed Identity → Azure SQL (no connection strings),
  private RSA key in Key Vault HSM (never exportable), per-Function
  least-privilege SQL role.
- **App:** DTOs separate from EF entities (no mass assignment), global
  error sanitization middleware (no info leakage), SPKI-pinned HTTPS
  from the agent.

**Weakest link:** a compromised endpoint submitting crafted but
cryptographically valid payloads. Crypto cannot prevent this —
mitigation is server-side (TPM binding, rate limits, simultaneous-use
detection, anomaly scoring). The agent is a sensor, NOT an oracle.

**Backlog status (2026-04-09 — see `security-baseline.md` for full runbooks):**
- ✅ **P0 #1** Single-use enrollment — `used_by` set in same txn as machine row.
- ✅ **P0 #2** Managed Identity to Azure SQL via `AccessTokenCallback`.
- ✅ **P0 #3** Cross-tenant ingest hazard — `ResultsFunction` trusts the HMAC-signed `X-Agent-Id` header, scopes machine lookup by `OrganizationId`, rejects body/header `AgentId` mismatches.
- ✅ **P1 #4** Envelope encryption (RSA-OAEP-256 + AES-256-GCM) implemented and wired into `ApiClient.SubmitResultsAsync`.
- ✅ **P1 #5** `NonceCache` — in-process `ConcurrentDictionary`, 10-min TTL, checked in `ApiKeyAuthMiddleware` (Redis upgrade queued for multi-instance scale).
- ✅ **P1 #6** `ErrorSanitizationMiddleware` registered first in worker pipeline — frozen `{"error":"internal_error","traceId":...}` on all unhandled exceptions.
- ✅ **P1 #7** Hardware fingerprint — registry-based SHA-256 in agent, sent via `X-Hwid` on every request, backfilled + bound server-side in `HwidVerifier`. TPM EK attestation upgrade is Phase 2.
- ✅ **P2 #9** SPKI pinning in agent `HttpClient` (log-only until `SpkiPins` registry value is populated — see runbook).
- 🟡 **P2 #8** Key Vault key still `--kty RSA` — HSM promotion runbook documented, blocked on Key Vault Premium SKU spend approval.
- 🟡 **P2 #10** Rotation runbooks documented (RSA key, HMAC secret, hwid salt); automated timer trigger not implemented (low priority — manual steps anyway).

---

## Known gaps / open issues

These are concrete things we know are broken or missing, so **don't waste time rediscovering them**:

### ✅ Fixed — `check_json` case mismatch

The 25 HIPAA refinement controls (BL-0445..BL-0469) were seeded with
snake_case field names but the agent's `ControlDef.cs` deserializes
camelCase. Resolved via `seed_005b_fix_casing.sql`, an idempotent
`UPDATE ... REPLACE(...)` migration on those 25 rows.

### 🟠 Important — blocks rich reports (partially resolved)

**Agent payload is partially enriched.** `HardwareInfo` expanded to ~20 fields,
multi-disk inventory added (`machine_disks` table), software inventory with
600+ commercial app detection, port scanning (`machine_ports` table), and
AD hygiene audit all implemented. The `raw_*` schema v1.1 blocks
(`raw_security_posture` with `mfa`/`event_logs`/`backup_posture`) are still
not populated as raw blocks but data is captured through dedicated endpoints.

**Still missing:** `raw_users` and `raw_network` as structured raw blocks
in the payload. Data is available through inventory/hygiene endpoints instead.

### ✅ Fixed — platform scope enforcement

Resolved by keeping the **agent dumb** and parsing OS strings
**server-side**. Implementation:

1. `015_machine_platform_id.sql` adds `machines.platform_id` FK +
   backfills existing rows via `CASE` on `os_name LIKE`.
2. New `PlatformResolver` service maps `os_name` → platform code
   (`W10`/`W11`/`MS19`/`MS22`/`MS25`) and caches the id lookup.
3. `EnrollmentService` and `EvaluationService` call the resolver on
   create + on OS drift, respectively.
4. `ControlsFunction` reads `X-Agent-Id` header, resolves the caller's
   machine, and JOINs `control_platforms` filtered by the machine's
   `platform_id`. Unknown OS → empty list (Phase 1 policy).
5. Agent change is minimal: `ApiClient` sends `X-Agent-Id` on every
   signed request. No platform detection in the agent at all.

Phase 2 will split MS19/22/25 (member server) from DC19/22/25 using
`ProductType`/AD role info in a second resolver step.

### 🟡 Minor — publish script inconsistency

`KryossAgent/KryossAgent.csproj` sets `PublishAot=true`, but
`KryossAgent/publish.ps1` overrides with `PublishAot=false`. The
published binary is NOT AOT — it's a self-contained single-file .exe
with the full runtime (~68 MB instead of ~15 MB).

**Decision pending:** should we actually ship AOT, or is self-contained
single-file fine? AOT gives faster startup and smaller size but limits
some reflection. The engines don't use reflection, so AOT should work.

---

## Active decisions (decision log)

See `KryossApi/docs/phase-roadmap.md` → "Decision log" section for the
full list. Key ones:

| Date | Decision | Why |
|---|---|---|
| 2026-04-08 | Phase 1 = workstation only (W10/W11) | MVP focus, 70% of SMB client fleets are workstations |
| 2026-04-08 | Soft-delete legacy 91 controls, don't hard-delete | FK to `assessment_controls` blocks delete; soft-delete preserves history |
| 2026-04-08 | Tag all active controls with NIST (100%) | Max flexibility for the NIST framework report |
| 2026-04-08 | Don't over-tag HIPAA — only controls that map to §164.312 | Reports stay honest; administrative/physical safeguards need attestation module (Phase 6) |
| 2026-04-08 | Agent is dumb, server evaluates | Catalog can change without rescanning; raw state in `machine_snapshots.raw_*` |
| 2026-04-10 | Easy Auth disabled on func-kryoss | All auth handled by custom middleware (ApiKeyAuth + BearerAuth); Easy Auth caused double-auth issues |
| 2026-04-10 | Enrollment codes support multi-use (`maxUses`) | MSPs need to enroll multiple machines with one code |
| 2026-04-10 | Agent re-enrollment via `--reenroll` flag | Clears registry, re-enrolls; server reuses machine row by hostname |
| 2026-04-10 | Auto-create default assessment on enrollment | If org has no assessment, one is created automatically |
| 2026-04-10 | Server platforms (MS19/MS22/MS25) share W10/W11 controls | Same 647 controls linked via seed_007c; DC split deferred to Phase 2 |
| 2026-04-10 | Portal uses friendly slug URLs | Frontend-only slug resolution, no API changes needed |
| 2026-04-10 | Agent default API URL = `https://func-kryoss.azurewebsites.net` | Compiled into binary, no interactive prompt |
| 2026-04-11 | Agent v1.2.2: self-contained network scan replaces PowerShell deployment | Single .exe does local scan + network discovery + remote PsExec deploy |
| 2026-04-11 | Binary patching via UTF-16LE sentinel replacement | Server-side `BinaryPatcher` replaces sentinels in compiled .exe (enrollment code, API URL, org/MSP name, colors) |
| 2026-04-11 | Multi-disk inventory in `machine_disks` table | One row per drive letter, replaces single diskSize/diskFree fields |
| 2026-04-11 | Port scanning persisted in `machine_ports` table | TCP top 100 + UDP top 20 per discovered host |
| 2026-04-11 | AD Hygiene full security audit | Privileged accounts, kerberoastable, unconstrained delegation, LAPS, adminCount residual, domain functional level |
| 2026-04-11 | 600+ commercial software detection list | Server-side normalization in `InventoryFunction` |
| 2026-04-12 | Portal: Hardware + Software inventory tabs | Org-level inventory views with search, sort, export |
| 2026-04-12 | Reports redesigned with Brand 2025 | New layout with framework score gauges, AD hygiene section, Montserrat font |
| 2026-04-13 | HMAC error differentiation | Server returns specific errors: timestamp skew vs signature mismatch. Agent verbose HMAC logging. |
| 2026-04-13 | Auto-provision Entra ID users as viewer | BearerAuthMiddleware creates user on first login instead of 403 |
| 2026-04-13 | M365 Phase 4: admin consent flow | One-click "Connect M365" button → Microsoft admin consent → 50 checks. Multi-tenant app, no per-customer App Registration needed. |
| 2026-04-13 | M365 50 security checks (was 30) | Added: stale accounts, app registrations, Secure Score, Identity Protection, Intune, DLP, security alerts, SharePoint, org config |
| 2026-04-13 | Agent security refactor PLANNED (v1.3.0+) | Remove PsExec, deploy via GPO/NinjaOne/Intune, convert shell engines to native .NET P/Invoke. Plan: `docs/superpowers/plans/2026-04-13-agent-security-refactor.md` |
| 2026-04-14 | Agent v1.3.0 + v1.4.0 DONE: zero Process.Start | PsExec deleted, NetworkScanner stripped to discovery+ports+hygiene, all 5 batch engines converted to native (WMI/P/Invoke), NativeCommandEngine replaces ShellEngine, PlatformDetector uses MSFT_PhysicalDisk WMI. `grep Process.Start` in agent/src returns ZERO matches. Deploy via GPO/NinjaOne/Intune scripts in `Scripts/Deploy/`. |
| 2026-04-14 | Binary trimming: 67 MB → 11.9 MB (82% reduction) | `PublishTrimmed=true` partial mode + feature flags (`InvariantGlobalization`, `DebuggerSupport=false`, `BuiltInComInteropSupport=false`, `EnableCompressionInSingleFile`), TrimmerRootAssembly for WMI/DirectoryServices/EventLog/ServiceController, hand-written JSON writer in NativeCommandEngine (avoids `JsonSerializer.Serialize<T>` reflection). |
| 2026-04-14 | Agent stateless cycle | Registry `HKLM\SOFTWARE\Kryoss\Agent` wiped after every successful upload (when offline queue empty). ACL relaxed from SYSTEM-only → Administrators+SYSTEM. Re-testing works without ACL workarounds. |
| 2026-04-14 | v1.5.0: 52 legacy command controls now execute natively | Server `ControlsFunction` whitelist expanded + snake_case fallback. Agent `ControlDef` gained fields `CheckType, Protocol, Side, Privilege, ExpectedSidsOrAccounts, Collection, Expected, Operator, EventIds, Days, TopN, PayloadField, Notes, Label`. NativeCommandEngine routes by `CheckType`: TLS (16 SCHANNEL reads), UserRights (16 via P/Invoke LsaEnumerateAccountsWithUserRight in `UserRightsApi.cs`), AppLocker (5 SrpV2 registry), inline registry with `exists` operator (19), 5 custom (Xbox task, PS ModuleLogging wildcard, PS v2 disabled, DoH servers, Legacy JScript). |
| 2026-04-15 | Reports 4-type baseline consolidation | 8 variants collapsed into 4 clean types (C-Level, Technical Level, Monthly Progress [deferred], Preventas). All org-scoped. Per-run reports deprecated (HTTP 410). Technical Level rebuilt with Asset Matrix + Top 10 Findings + Los 6 de Hierro. Preventas consolidated with tone=opener\|detailed sub-param. Spec: `docs/superpowers/specs/2026-04-15-reports-4-type-baseline-design.md`. Plan: `docs/superpowers/plans/2026-04-15-reports-4-type-baseline.md` |
| 2026-04-14 | v1.5.1: Protocol Usage Audit (NTLM + SMBv1, 90-day retention) | Server-side toggle per org (`organizations.protocol_audit_enabled`, `PATCH /v2/organizations/{id}/protocol-audit`, `EnrollFunction` propagates the flag). Agent `ProtocolAuditService` (ONLY service that writes registry — audit opt-in scoped) configures `AuditReceivingNTLMTraffic=2`, `RestrictSendingNTLMTraffic=1`, `AuditSmb1Access=1` and resizes Security (500 MB), `Microsoft-Windows-NTLM/Operational` (300 MB), `Microsoft-Windows-SMBServer/Audit` (300 MB) via native `EventLogConfiguration` (no wevtutil). EventLogEngine gained `event_count` (array of EventIDs + days window) and `event_top_sources` (top N by EventData payload field, parsed via XmlReader). 12 new controls seeded in `sql/026_protocol_audit.sql`: AUDIT-001..004, NTLM-USE-001..004, SMB1-USE-001..002, SAFE-TO-DISABLE-NTLM/SMB1. Portal tab "Protocol Usage" under `/organizations/{slug}/protocol-usage` with confirmation dialog + 90-day retention progress bar. |

---

## Recent features (v1.2.x, 2026-04-11/12)

### Agent (v1.2.2)
- **Network scanner** — discovers targets via AD (LDAP), ARP table, or subnet probe; deploys agent binary to remote machines via SMB + PsExec (embedded resource); collects results
- **Port scanner** — TCP top 100 ports (parallel `TcpClient`) + UDP top 20 ports per target
- **AD Hygiene audit** — stale/dormant machines and users, disabled users, never-expire passwords, privileged accounts, kerberoastable accounts, unconstrained delegation, adminCount residual, LAPS coverage, domain functional level
- **Binary patching** — `EmbeddedConfig.cs` has fixed-length UTF-16LE sentinels (`@@KRYOSS_ENROLL:`, `@@KRYOSS_APIURL:`, `@@KRYOSS_ORGNAM:`, `@@KRYOSS_MSPNAM:`, `@@CLRPRI:`, `@@CLRACC:`) that the server's `BinaryPatcher` replaces to produce org-specific .exe files
- **Multi-disk detection** — `PlatformDetector.DetectHardware()` enumerates all fixed drives (drive letter, size, free, type)
- **CLI flags** — `--help`, `--alone` (skip network scan), `--scan` (network scan only), `--verbose`, `--silent`, `--credential`, `--threads N`, `--discover-ad`, `--discover-arp`, `--discover-subnet CIDR`, `--targets`, `--targets-file`

### API
- **Inventory endpoints** — `GET /v2/inventory/hardware`, `GET /v2/inventory/software` (org-level aggregation with 600+ commercial software normalization)
- **Hygiene endpoints** — `POST /v1/hygiene` (agent submits AD findings), `GET /v2/hygiene?orgId=` (portal reads)
- **Agent download** — `GET /v2/agent/download?orgId=&enrollmentCodeId=` via `AgentDownloadFunction` + `BinaryPatcher`
- **New tables** — `machine_disks` (per-drive inventory), `machine_ports` (open ports per host), `ad_hygiene` (AD security findings)
- **Report redesign** — Brand 2025 layout, framework score gauges, AD hygiene section, hardware/software summary

### Portal
- **Hardware Inventory tab** — org-level view of all machines with disk, TPM, BitLocker, SecureBoot, domain status
- **Software Inventory tab** — org-level view with 600+ commercial app detection, version tracking
- **Download Agent button** — generates patched binary from portal
- **Report redesign** — new report viewer with Brand 2025 styling

### Session 2026-04-13

**Bug fixes:**
- HMAC: Server now returns `"HMAC timestamp skew"` vs `"Invalid HMAC signature"` (was generic for both). Agent `--verbose` prints signing components.
- Scan order: Removed wasted `DetectHardware()` from enrollment (was called but never sent).
- Auto-provision: New Entra ID users auto-created as `viewer` role instead of 403.

**M365 Phase 4 (complete):**
- **Admin consent flow** — one-click "Connect M365" button in portal. Multi-tenant Entra app, customer admin just approves permissions. No per-customer App Registration needed.
- **Endpoints:** `GET /v2/m365/consent-url`, `GET /v2/m365/consent-callback` (browser redirect from Microsoft)
- **50 security checks** (was 30): M365-001..050 across 15 categories
- **21 Graph API permissions** (Application, read-only) across 4 tiers
- **Config:** `M365ScannerClientId`, `M365ScannerClientSecret`, `PortalBaseUrl` env vars on func-kryoss
- **SQL:** migration 025 adds `consent_granted_at`, `consent_granted_by` to `m365_tenants`
- **Portal:** One-click button + collapsible manual fallback + 15 category labels + callback handling
- **PowerShell:** `Scripts/M365/Register-KryossM365App.ps1` for enterprise per-tenant setup

**Agent security refactor (PLANNED, not implemented):**
- Plan at `docs/superpowers/plans/2026-04-13-agent-security-refactor.md`
- v1.3.0: Remove PsExec, deploy via GPO/NinjaOne/Intune
- v1.4.0: Convert 5 shell engines to native .NET P/Invoke (zero Process.Start)
- v1.5.0: Audit 200 legacy `function` controls, eliminate ShellEngine

---

## How to not waste tokens in future sessions

1. **Always read `CLAUDE.md` first** — this file, then the one in the
   subfolder you're editing.
2. **Before running a deep explore, check the CLAUDE.md** — if it's in
   there, you don't need to re-grep.
3. **When you add something new, add it here** — a new table, a new
   endpoint, a new seed, a new agent feature. 30 seconds to update
   saves 5 minutes of re-exploration next time.
4. **DB connection (for verification queries):**
   `Server: tcp:sql-kryoss.database.windows.net,1433`
   `DB: KryossDb`
   `User: kryossadmin` (password in user's local notes, don't hard-code)
5. **Default test enrollment code:** `K7X9-M2P4-Q8R1-T5W3`

---

## Reference documents (read these when relevant)

| Document | When to read it |
|---|---|
| `KryossApi/docs/security-baseline.md` | **Before any change to auth, crypto, key management, or the ingest flow. Non-negotiable.** |
| `KryossApi/docs/agent-payload-schema.md` | Before modifying the agent's output or the `/v1/results` handler |
| `KryossApi/docs/phase-roadmap.md` | Before adding any scope (server, DC, cloud, network) |
| `KryossApi/sql/check_catalog_health.sql` | To verify DB state after any control-catalog change |
| `KryossApi/CLAUDE.md` | Before touching the backend |
| `KryossAgent/CLAUDE.md` | Before touching the agent |
| `docs/superpowers/specs/2026-04-10-agent-binary-patching-and-remote-scan.md` | Before implementing binary patching or multi-computer scan |
| `Scripts/CLAUDE.md` | When writing any PowerShell script for RMM/Intune deploy |

---

## Brand & writing rules (condensed from Scripts/CLAUDE.md)

- Brand: **TeamLogic IT** (not "TLIT"), "Your Technology Advisor"
- Primary green: **#008852**, accent light green **#A2C564**, dark bg **#3D4043**
- Logo: `Scripts/assets/TLITLogo.svg` (3210×915, embed base64 in reports)
- Font: **Montserrat** (Google Fonts)
- Report template: `Scripts/Audit/Kryoss-Report-Template.html`
- PASS=#008852, WARN=#D97706, FAIL=#C0392B
- Code comments and variable names in **English**. User-facing copy in
  English or Spanish as needed (MSP is bilingual).
- Never use emojis in code/commits unless the user explicitly asks.
