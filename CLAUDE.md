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
├── KryossPortal\                   <- Frontend (Vue? status unclear — not mapped yet)
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
| Agent | .NET 8 Native AOT (win-x64, single-file, ~68 MB self-contained) | ✅ Compiles, has 11 engines (registry, secedit, auditpol, firewall, service, netaccount, command, eventlog, certstore, bitlocker, tpm); still needs `raw_*` payload enrichment (deferred) |
| Portal | Unknown — **not yet mapped** | ❓ |
| Auth | Agent: API Key + HMAC-SHA256. Portal: Entra ID Bearer token | ✅ Implemented in middleware |
| Crypto | RSA-2048 + AES-256-GCM for payload encryption (agent → API) | ⚠️ Agent has `CryptoService.cs` but current `ApiClient.cs` only uses HMAC — payload encryption may not be wired up end-to-end yet |
| RLS | SQL Session Context, applied per request via `RlsMiddleware` | ✅ Implemented |

---

## Control catalog state (as of 2026-04-08)

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

Platform scope (Phase 1):
  W10, W11 → 647 controls linked each
  MS19/22/25, DC19/22/25 → 0 (Phase 2 roadmap)
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

### 🟠 Important — blocks rich reports

**Agent payload is v1.0, schema is v1.1.** The agent only populates
`HardwareInfo` (4 fields: cpu, ramGb, diskType, tpm) and a flat software
list. The schema v1.1 (in `agent-payload-schema.md`) expects 5 rich raw
blocks: `raw_hardware`, `raw_security_posture` (with new `mfa`/`event_logs`/`backup_posture`
sub-blocks), `raw_software`, `raw_network`, `raw_users`.

**Impact:** The portal can evaluate the 630 controls (because the agent
does send `control_results[]`), but can't build rich hardware/network/user
reports until the agent is enriched.

**Fix:** Add 4 new collectors to the agent:
`NetworkCollector`, `UserCollector`, `SecurityPostureCollector`,
`MfaCollector` (the last one covers the HIPAA refinement blocks).
All registry + shell based to stay AOT-compatible.

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
