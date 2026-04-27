# Projecto Kryoss — Master Index

**Owner:** Federico / Geminis Computer S.A. / TeamLogic IT
**Purpose of this file:** Give any future Claude session enough context to work on the project **without re-exploring the filesystem**. Read this first, then the subfolder CLAUDE.md of whatever you're working on.

**IMPORTANT — if you update anything in this repo, update the relevant CLAUDE.md too.** The point is to not waste tokens rediscovering the same things.

---

## Version tracking

| Component | Current | Where | Endpoint |
|-----------|---------|-------|----------|
| **API** | 1.27.0 | `KryossApi.csproj` `<Version>` | `GET /v2/version` (no auth) |
| **Portal** | 1.17.0 | `KryossPortal/package.json` `"version"` | Sidebar footer "Powered by Kryoss vX.Y.Z" |
| **Agent** | 2.6.2 | `KryossAgent.csproj` `<Version>` | Registry `HKLM\SOFTWARE\Kryoss\Agent\Version` |

**MANDATORY — VERSION SYNC PROTOCOL:**

1. Every code change that modifies behavior (new endpoint, bug fix, new block, schema change) MUST bump the version of the affected component(s). Same version = no way to tell if deploy landed.
2. Bump BEFORE build/publish, not after.
3. **This version table MUST stay in sync with the actual values in csproj/package.json.** If you bump a version in source, update this table in the same edit session. Stale versions here waste future sessions re-discovering the real state.
4. When multiple components change in one session, bump ALL affected components.
5. The `/v2/version` endpoint returns the API version at runtime — use it to verify deploys landed.

- **Patch** (1.8.0 → 1.8.1): bug fix, minor tweak
- **Minor** (1.8.0 → 1.9.0): new feature, new endpoint, new report type
- **Major** (1.8.0 → 2.0.0): breaking API change, schema migration required

---

## What Kryoss is

A security assessment SaaS for MSPs (primary user: TeamLogic IT franchise, built by Geminis Computer). The platform has three pillars:

1. **Audit compliance** — run ~630 checks against Windows endpoints and score them against CIS / NIST / HIPAA / ISO 27001 / PCI-DSS
2. **Ticket reduction** — detect drift/misconfig early and feed findings into a helpdesk/PSA workflow
3. **Cloud workspace hardening** — M365 / Entra ID / Azure / Power BI via unified Cloud Assessment (CA-0..CA-12 SHIPPED, single tab + single scan)

**Business model:** Kryoss is the backend. Franchise portal = MSPs sign up, enroll client machines, generate reports, sell remediation.

---

## Repository layout (only the folders that matter)

```
Projecto Kryoss\
├── CLAUDE.md                       <- YOU ARE HERE (master index)
├── KryossApi\                      <- Backend (.NET 8 Azure Functions)
│   └── CLAUDE.md                   <- See this for full API map
├── KryossAgent\                    <- Windows agent (.NET 8, v1.5.1, ~12 MB trimmed)
│   └── CLAUDE.md                   <- See this for full agent map
├── KryossPortal\                   <- Frontend (React 18 + Vite + TS + shadcn/ui + MSAL)
│                                      Repo: github.com/stuckerbsas/Kryoss-Portal
│                                      Deployed: zealous-dune-0ac672d10.6.azurestaticapps.net
│                                      Auth: MSAL (@azure/msal-browser) + Entra ID Bearer token
│                                      Friendly URLs: /organizations/{slug}/machines/{hostname}
│                                      Spec: docs/superpowers/specs/2026-04-10-kryoss-portal-mvp-design.md
│                                      Plan: docs/superpowers/plans/2026-04-10-kryoss-portal-mvp.md
├── Scripts\                        <- PowerShell scripts & audit tools
│   └── CLAUDE.md                   <- MSP/TeamLogic IT PowerShell standards
│                                      (NinjaRMM/Intune deploy, brand, etc.)
├── docs\                           <- Specs, plans, roadmap
├── Procedimientos\                 <- Internal procedures docs
├── Propuesta\                      <- Proposals
├── Web\                            <- Web assets
└── archive\                        <- Dead/legacy folders (moved 2026-04-20)
    ├── Kryoss Partner Portal\, The Final portal\, New Portal\
    ├── Kryoss.Portal\, Kryoss\, Kryoss-main\
    ├── Assesment\, CopilotReadinessAssessment\, antigravity\
```

**Rule of thumb:** Active code lives in `KryossApi\`, `KryossAgent\`, `KryossPortal\`, `Scripts\`. Everything in `archive\` is dead — don't touch without asking.

---

## Tech stack summary

| Layer | Tech | Status |
|---|---|---|
| Backend API | .NET 8 Azure Functions + EF Core 8 + Azure SQL | ✅ Deployed (`func-kryoss`) |
| Database | Azure SQL `sql-kryoss.database.windows.net` / DB `KryossDb` | ✅ Seeded with ~767 active controls |
| Agent | .NET 8 v1.5.1 (win-x64, single-file trimmed, ~12 MB) | ✅ 12 engines (zero Process.Start) + DcEngine + network scanner + port scanner + AD hygiene + SNMP + network diagnostics + protocol audit + threat detection + binary patching sentinels |
| Portal | React 18 + Vite + TS + shadcn/ui + MSAL, deployed on Azure SWA | ✅ Deployed (`zealous-dune-0ac672d10.6.azurestaticapps.net`) |
| Auth | Agent: API Key + HMAC-SHA256 (`ApiKeyAuthMiddleware`). Portal: MSAL JWT via `BearerAuthMiddleware`. Easy Auth DISABLED on func-kryoss (platform.enabled=true, requireAuthentication=false, AllowAnonymous). All auth handled by custom middleware. | ✅ Implemented in middleware |
| Crypto | RSA-2048 + AES-256-GCM for payload encryption (agent → API) | ⚠️ Agent has `CryptoService.cs` but current `ApiClient.cs` only uses HMAC — payload encryption may not be wired up end-to-end yet |
| RLS | SQL Session Context, applied per request via `RlsMiddleware` | ✅ Implemented |

---

## Control catalog state (as of 2026-04-20)

This is the heart of the product. Memorize it:

```
control_defs total: 918  (738 prior + 80 SRV + 100 DC)
  Active:   827  (647 baseline + 80 SRV server + 100 DC domain controller)
  Inactive: 91   (legacy BL-XXX soft-deleted, superseded by BL-0XXX)

Active by engine (dispatch type):
  registry    371    command     188
  auditpol     34    firewall     21
  dc           27    service      18
  netaccount    5    secedit       2
  eventlog      4    certstore     6
  bitlocker     5    tpm           4

Framework coverage (active):
  NIST       827 (100.0%)
  CIS        ~810 (~98%)
  HIPAA      ~380 (~46%)
  ISO27001   ~240 (~29%)
  PCI-DSS     ~30 (~3.6%)

Platform scope (as of 2026-04-20):
  W10, W11 → 647 controls linked each
  MS19, MS22, MS25 → 727 controls each (647 baseline + 80 SRV via seed_010)
  DC19, DC22, DC25 → 827 controls each (647 baseline + 80 SRV + 100 DC via seed_013 + 047)
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
| 2026-04-17 | CA-6 Subsession A: Azure consent flow (Track 1 of Azure Infrastructure pipeline) | Separate consent model from Graph — customer admin assigns **Reader** RBAC role to Kryoss multi-tenant SPN at subscription scope. Migration `031_azure_consent_tracking.sql` adds `last_verified_at` + `error_message` to `cloud_assessment_azure_subscriptions` (consent_state, tenant_id, display_name, state already from 030). New `AzureConsentFunction.cs` exposes `POST /v2/cloud-assessment/azure/connect` (instructions + az CLI copy-paste + SPN object ID resolver via Graph with `az ad sp show` fallback), `POST /v2/cloud-assessment/azure/verify` (ARM `GET /subscriptions?api-version=2022-12-01` via `ClientSecretCredential` + management.azure.com scope, upserts rows matching `(orgId, subId)`, marks stale subs `consent_state=failed` never deletes), `GET /v2/cloud-assessment/azure/subscriptions`, `DELETE /.../{subscriptionId}`. Portal 6th tab "Azure" in `CloudAssessmentPage.tsx` with `ConnectAzureCard` (3-step: tenant ID → instructions → verify) + `AzureSubscriptionsList` (table + Remove/Re-verify/Connect-another). Hooks: `useAzureSubscriptions`, `useAzureConnect`, `useAzureVerify`, `useAzureDisconnect`. Subsession B will add `AzurePipeline` (resources + Defender for Cloud + public exposure + NSG + Key Vault + VM audits), ~20 recommendations, 5-area radar update, findings UI. |
| 2026-04-17 | CA-6 Subsession B: Azure Infrastructure pipeline | New `AzurePipeline` covers resources + Defender for Cloud + public exposure + NSG + Key Vault + VM audits. 20 recommendations. 5-area radar (azure added). Portal "Azure" tab gains rich infra view (exposure alerts, resource donut, Defender bar, findings table). Overall score weights revised to 5-area (28/25/22/15/10) when Azure present, 4-area fallback when no subs connected. Migration 032 adds `cloud_assessment_azure_resources` cache. Plan: `docs/superpowers/plans/2026-04-17-ca-6-subsession-b-azure-pipeline.md`. |
| 2026-04-18 | CA-10: Unified Cloud Connect | Single "Connect Cloud" button replaces 3 separate flows (M365/Azure/PBI). Auth code flow (`/authorize` with `prompt=admin_consent`) grants Graph+PBI app permissions AND returns delegated ARM token for auto-assigning Reader RBAC on Azure subscriptions. N/A states for services without access. `UnifiedCloudConnectFunction.cs` with `GET /v2/cloud/connect-url` + `GET /v2/cloud/connect-callback`. Portal: `CloudConnectCard` in OverviewTab, `ConnectProgressModal` after redirect, Power BI `unavailable` state. **Deploy requires**: App Registration add delegated `user_impersonation` on ARM + redirect URI `https://func-kryoss.azurewebsites.net/v2/cloud/connect-callback`. Plan: `docs/superpowers/plans/2026-04-18-unified-cloud-connect.md`. |
| 2026-04-18 | CA-11: Benchmarks | Three benchmark dimensions: franchise peers (≥5 orgs), industry baseline (15 NAICS codes × 5 employee bands), global Kryoss (≥50 orgs). Migration `038_cloud_assessment_benchmarks.sql` adds 4 tables + `organizations.industry_code/subcode/employee_count_band` + `franchises.benchmark_opt_in`. `BenchmarkService.ComputeAndPersistAsync` hooks into `CloudAssessmentService.RunScanInternalAsync` after suggestions block (non-fatal on failure). Nightly `Benchmark_RefreshAggregates` timer (03:00 UTC) rebuilds franchise + global rollups from latest completed scan per org. 4 API endpoints in `BenchmarkFunction.cs` + portal "Benchmarks" tab (radar overlay, per-metric verdict pills, franchise leaderboard) + self-contained HTML report `/benchmarks/{scanId}/report`. Privacy: sample gates (5/50) + franchise opt-out flag; per-org values never leave franchise. **Deploy requires**: apply `sql/038_cloud_assessment_benchmarks.sql` + `sql/seed_038_industry_benchmarks.sql`. |
| 2026-04-18 | CA-12: Unified Cloud Experience | **Track 1:** CopilotReadiness endpoints deprecated (410 Gone, sunset 2026-05-18). Copilot Lens tab inside Cloud Assessment reads D1-D6 from `cloud_assessment_scans` Copilot* fields. New `GET /v2/cloud-assessment/copilot-lens/{scanId}` endpoint. **Track 2:** `ConsentOrchestrator.cs` + `GET /v2/cloud-assessment/connection-status` returning graph/azure/powerbi state + percentage. Portal `ConnectCloudWizard.tsx` stepper (3 steps: M365 → Azure → PBI). `ConnectionBanner` on CloudAssessmentPage. **Track 3:** "M365 / Cloud" tab removed from OrgDetail nav, `/m365` route redirects to `/cloud-assessment`. Single "Run Scan" button on Overview. **Track 4:** Data migration decision = archive. Legacy `copilot_readiness_*` tables kept read-only; CA already computes D1-D6 from live pipeline data. No SQL migration needed. |
| 2026-04-19 | Unified Report System: 7 types, compositional blocks | Replaces monolithic ReportService with 17 blocks + 7 recipes via `ReportComposer`. Types: C-Level, Technical, Preventa Opener/Detailed, Framework Compliance, Business Proposal (auto-pricing from `service_catalog` + `franchise_service_rates`), Monthly Progress. Cloud integrated conditionally. SQL: `sql/039_service_catalog.sql` + `seed_039_service_catalog.sql`. Spec: `docs/superpowers/specs/2026-04-19-unified-report-system-design.md`. Plan: `docs/superpowers/plans/2026-04-19-unified-report-system.md`. |
| 2026-04-19 | Phase 2+3: DC vs Member Server detection | Agent detects `ProductType` via WMI `Win32_OperatingSystem` (1=workstation, 2=DC, 3=server), sends in enrollment + hardware payload. `PlatformResolver` resolves DC19/DC22/DC25 when ProductType=2. Migration `040_dc_platform_support.sql` adds `product_type` column + links 647 baseline controls to DC platforms. `seed_013_dc_controls.sql` adds 40 DC-only controls (DC-001..DC-040). **Deploy requires**: apply `sql/040_dc_platform_support.sql` + `sql/seed_013_dc_controls.sql`. |
| 2026-04-19 | Phase 5a: Network Diagnostics (no 3rd-party APIs) | Agent `NetworkDiagnostics.cs` runs speed test (HttpClient GET/POST against `/v1/speedtest`), internal latency (parallel ping sweep), route table (WMI `Win32_IP4RouteTable`), VPN detection (adapter type + keyword), bandwidth snapshot (IPv4Statistics delta), adapter inventory. Migration `041_network_diagnostics.sql` adds 3 tables (`machine_network_diag`, `machine_network_latency`, `machine_network_routes`). `EvaluationService` persists network diag data per run. `SpeedTestFunction` (GET/POST `/v1/speedtest`) serves random bytes for download + accepts upload. `NetworkDiagnosticsFunction` (GET `/v2/network-diagnostics`) portal endpoint. 50 network controls (NET-001..NET-050) in `seed_042_network_controls.sql`. `NetworkBlock` + `NetworkRecipe` add "network" report type (8th recipe). **Deploy requires**: apply `sql/041_network_diagnostics.sql` + `sql/seed_042_network_controls.sql`. |

| 2026-04-20 | IA-11: Network Sites + GeoIP + Leaflet Map + Speedtest History + SLA | Track A: `PublicIpTracker` captures public IP from X-Forwarded-For, `SiteClusterService` groups machines by IP → auto-derived sites, `NetworkSitesFunction` 4 endpoints, portal `NetworkSitesTab`. Migration `044_network_sites.sql`. Track B: Agent `NetworkDiagnostics.cs` gains cloud endpoint latency (6 M365 endpoints) + DNS resolution measurement. `ResultsFunction` returns `yourPublicIp`. Migration `045_network_diag_cloud_dns.sql`. Track C: `GeoIpService` (ip-api.com) enriches IP history + sites with geo/ISP/ASN/connectivity type. Portal gains Leaflet site map, speed history chart (recharts), SLA compliance badges, site detail drawer with device list. **Deploy requires**: `sql/044_network_sites.sql` + `sql/045_network_diag_cloud_dns.sql`. |
| 2026-04-20 | CA-14: Auto-consent (Fabric + ARM) | `AutoConsentFunction.cs` — 4 endpoints: PBI auto-enable URL/callback (delegated `Tenant.ReadWrite.All` scope, exchanges code → enables ServicePrincipalAccess via `FabricAdminService`) + Azure auto-assign URL/callback (delegated `user_impersonation`, auto-assigns Reader on all visible subs). Portal `ConnectCloudWizard` Azure + PBI steps gained "Enable automatically" primary button + collapsible manual fallback. `ConnectProgressModal` handles callback params. **Deploy requires**: App Registration add delegated Fabric + ARM scopes + 2 redirect URIs. |
| 2026-04-20 | IA-1: Server & Hypervisor Inventory | Migration `046_hypervisor_inventory.sql` (3 tables: `infra_hypervisor_configs`, `infra_hypervisors`, `infra_vms`). `HypervisorPipeline.cs` collects VMware vCenter REST + Proxmox REST (QEMU + LXC). 7 finding generators (idle/over-provisioned/stale snapshots/no backup/capacity/no HA/EOL OS). `HypervisorConfigFunction.cs` — CRUD + test + scan results. Portal "Servers & VMs" sub-tab with config manager + host table + VM table + findings. Hyper-V deferred to agent WMI module (IA-1b). **Deploy requires**: `sql/046_hypervisor_inventory.sql`. |
| 2026-04-20 | A-OFL: Offline Collection Mode | Machines without internet dump `OfflineCollectPayload` JSON to shared folder (`--offline --share \\server\path`). Collector machine uploads all via `--collect \\server\path` → `POST /v1/collect`. Server auto-enrolls unknown machines using embedded enrollment code. Agent: `OfflineCollectPayload.cs` model, `SaveCollectPayload` in OfflineStore, `SubmitCollectAsync` in ApiClient, `RunCollectMode` in Program.cs. API: `CollectFunction.cs` (auth via collector's API key, auto-enroll via `EnrollmentService.RedeemCodeAsync`, evaluate, track IP). |
| 2026-04-20 | DC-01: Domain Controller controls expansion (40→100) | New `DcEngine` (12th engine, type='dc') with 27 native check types via System.DirectoryServices/WMI/Registry/ServiceController. Migration `047_dc_controls_v2.sql`: converts 8 broken command-type DC controls to native types, deactivates 15 non-executable ones, adds 60 new controls (DC-041..DC-100) covering AD security, registry hardening, services, audit policies, hardware. All zero Process.Start. Agent dispatches by `CheckType` field. **Deploy requires**: `sql/047_dc_controls_v2.sql`. |
| 2026-04-20 | A-13: Server-side scan orchestrator (Phase 1) | Server assigns scan time slots per machine at enrollment. Agent v1.6.0: hourly check-in via `GET /v1/schedule`, replaces random 0-30min jitter. Org default window 2-6AM, uniform slot distribution (min 10s spacing, gap-filling). `ScanScheduleService` (slot assignment + schedule compute), `ScheduleFunction` (exempt from auth like /enroll). Agent uses `lastrun.txt` (not registry — gets wiped) to prevent double-run. NinjaOne deploy script v3.0: hourly trigger + 2h execution limit. Phase 2 adds portal UI for window config + redistribute. **Deploy requires**: `sql/049_scan_orchestrator.sql` + `sql/049b_backfill_scan_slots.sql`. |
| 2026-04-21 | CA-15: Drift Alerts + Notifications | `AlertService.EvaluateAndFireAsync` hooks post-scan in `CloudAssessmentService`. 4 rule types: `score_drop` (threshold), `new_critical`, `new_high_regulated`, `framework_below`. Per-franchise config via `cloud_assessment_alert_rules`. Delivery: webhook (POST JSON) + email (SendGrid placeholder). `AlertFunction.cs`: CRUD rules + history + test (6 endpoints). Migration `050_cloud_assessment_alerts.sql` (2 tables). **Deploy requires**: `sql/050_cloud_assessment_alerts.sql`. |
| 2026-04-21 | RP-06: Business Proposal report | Already implemented — `ProposalRecipe.cs` + `ServiceCatalogBlock.cs` (auto-pricing from `service_catalog` + `franchise_service_rates`). Portal dropdown added (`proposal` + `framework` types). |
| 2026-04-24 | SNMP dedup + enrichment | MAC-based device dedup (replaces IP-only unique constraint), HOST-RESOURCES-MIB (CPU/memory/disk/processes), ARP noise filter, machine correlation, stale marking, batched upload (50/batch). API 1.16.2 + Agent 1.7.4. Migrations: `056_snmp_host_resources.sql`, `057_snmp_dedup_enrich.sql`, `seed_054b_snmp_profiles_expanded.sql` (12 vendor profiles). |
| 2026-04-25 | Agent v2.0.0: Windows Service mode | `--install`/`--uninstall`/`--service` flags. `ServiceWorker` (BackgroundService) runs compliance scans every 24h, SNMP every 4h, heartbeat every 15min. P/Invoke service install (zero Process.Start). `ScanCycle.cs` extracts reusable scan logic. `POST /v1/heartbeat` endpoint. SQL: `061_agent_service_mode.sql`. Deploy: `Install-KryossService.ps1` + `Uninstall-KryossService.ps1`. |
| 2026-04-24 | IA-2: Network Topology Discovery (Phase 1) | `snmp_device_neighbors` table persists LLDP/CDP neighbor data (was discarded, only counts stored). `TopologyFunction.cs` returns graph (nodes + edges + phantom devices). Auto-resolve: matches remoteSysName/remoteChassisId/remoteIp to known devices. Portal: D3.js force-directed graph in Network → Topology sub-tab. Migration: `058_network_topology.sql`. API 1.17.0, Portal 1.10.0. |
| 2026-04-25 | Agent v2.1.0: Full Network Pipeline + Remediation | 9 blocks: (1) Windows Service, (2) Trial + auto-report, (3) Port banner grab, (4) Reverse DNS + ping enrichment, (5) WMI probe, (6) Passive discovery (NetBIOS/mDNS/SSDP), (7) Self-updater, (8) External exposure (server-side port scan + findings), (9) Closed-set remediation (whitelist catalog ~50 controls, tasks with rollback, agent executor). SQL: `061-066`. API 1.19.0, Agent 2.1.0. |
| 2026-04-25 | SH-KEY: Per-machine key rotation + rate limiting | Kerberos-inspired 3-layer auth: (1) enrollment code (one-time), (2) machine_secret (long-term CSPRNG 64-byte hex), (3) session_key (48h, rotated via heartbeat). HMAC validation chain: session_key → prev_session_key (24h grace) → machine_secret (reauth) → org ApiSecret (backward compat). Per-IP enrollment rate limit (5/15min). Per-org rate limit (200/min). `KeyRotationService` handles CSPRNG generation + rotation logic. Migration `067_machine_auth_keys.sql` adds 7 columns to machines. Backward compatible: pre-v2.2 agents keep using org ApiSecret. API 1.20.0, Agent 2.2.0. |
| 2026-04-25 | Agent Remote Configuration from Portal | Portal sets 5 config params per machine (compliance interval, SNMP interval, network scan on/off + interval, passive discovery on/off). DB columns in `machines` table (migration `069_agent_remote_config.sql`). API delivers config via heartbeat response → agent saves to registry → ServiceWorker reads each loop. Portal UI: `AgentConfigCard` in MachineDetail. PATCH `/v2/machines/{id}/agent-config`. API 1.21.0, Agent 2.3.0, Portal 1.11.0. |
| 2026-04-26 | IA-3: WAN & Site Connectivity Health | `WanHealthService` scores each network site 0-100 (weighted: latency 30%, jitter 20%, packet loss 25%, throughput 15%, DNS 10%). 11 finding rules (packet loss, jitter, latency, DNS, throughput, routing hops, SLA violations). Agent gains ICMP traceroute (incrementing TTL 1..30) + jitter/loss measurement. `MachineNetworkDiag` entity extended with JitterMs, PacketLossPct, HopCount, TracerouteTarget, TracerouteJson. `NetworkSitesFunction` gains `/wan-health` + `/{siteId}/traceroute` endpoints. Portal `WanHealthTab` in Network sub-tab. SQL: `072_wan_health.sql`. API 1.23.0, Agent 2.5.0, Portal 1.13.0. |
| 2026-04-26 | A-01: CVE Scanner | Server-side CVE matching engine (no agent changes). `CveService.ScanMachineAsync` loads software from `RawPayload`, matches against `cve_entries` patterns (SQL LIKE → sequential contains) with semantic version comparison. Hooked into `EvaluationService` post-save (non-fatal). ~60 built-in high-impact CVEs for common MSP software (Chrome, Firefox, 7-Zip, WinRAR, Adobe, Java, .NET, Zoom, TeamViewer, etc.). `CveFindingsFunction` (4 endpoints: list/rescan/dismiss/stats). Portal `CveFindingsTab` with severity filter, dismiss, rescan, top vulnerable software grid. SQL: `073_cve_scanner.sql` + `seed_073_cve_builtin.sql`. API 1.24.0, Portal 1.14.0. |
| 2026-04-26 | A-02: Patch Compliance (Track A) | Agent `PatchCollector.cs` collects Windows Update status via WMI `Win32_QuickFixEngineering` + registry (WSUS/WUfB config, reboot pending, last check/install times) + ServiceController (wuauserv, NinjaRMMAgent). Zero Process.Start. Data sent in `AssessmentPayload.PatchStatus`. Server persists in `machine_patch_status` (upsert) + `machine_patches` (hotfix history). Compliance score 0-100 (weighted: WU service status, reboot pending, update source, check recency, install recency). `PatchComplianceFunction` (2 endpoints: org summary + machine patches). Portal `PatchComplianceTab` with KPI cards (avg score, reporting, reboot pending, unmanaged, WU stopped), source distribution, per-machine table. SQL: `074_patch_compliance.sql`. Track B (patch deployment with rings + test plans via WUA COM) designed, deferred. API 1.25.0, Agent 2.6.0, Portal 1.15.0. |
| 2026-04-26 | DC-02+03: AD Schema/Replication + FSMO Health | Agent `DcHealthCollector.cs` collects via System.DirectoryServices (LDAP) + WMI `MSAD_ReplNeighbor`: AD schema version + label (objectVersion mapping Win2000→2025), forest/domain functional levels, 5 FSMO role holders (extract hostname from NTDS Settings DN), single-point-of-failure detection, site/subnet/DC/GC counts, per-partner replication status (last success/attempt, failure count, error, transport). Runs after AD hygiene on DCs only (ProductType=2). `POST /v1/dc-health` agent submit, `GET /v2/dc-health` portal read (latest snapshot + history). Portal `DcHealthTab` with KPI cards (schema version, DC count, sites, replication health, functional levels, FSMO distribution), domain info card, FSMO role table with single-point warning, replication partners table with status badges. SQL: `075_dc_health.sql` (2 tables: `dc_health_snapshots`, `dc_replication_partners` with CASCADE delete). API 1.26.0, Portal 1.16.0. |

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
5. **Default test enrollment code:** `<ENROLLMENT_CODE>` (see `KryossApi/sql/seed_100_test_data.sql` or DB)

---

## Reference documents (read these when relevant)

| Document | When to read it |
|---|---|
| **`docs/superpowers/plans/ROADMAP.md`** | **ORCHESTRATOR SOURCE OF TRUTH — read at start of every new session. Contains active queue, shipped phases, backlog, prompt library, priority tiers.** |
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


## Instrucciones
- Respuestas: máximo 1-2 líneas
- Código: comentarios solo si no es obvio
- Cambios: directo, sin explicar lógica
- Errors: reporta y sugiere fix, no narres
- Sin saludos, cierres, resúmenes
- NO mostrar código en las respuestas salvo que se esté discutiendo algo en particular. Solo reportar qué se hizo/cambió.