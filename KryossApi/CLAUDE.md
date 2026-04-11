# KryossApi — Backend (.NET 8 Azure Functions)

**Read `../CLAUDE.md` first.** This file is the detailed map of the backend only.

---

## Stack

- **.NET 8** Azure Functions v4 (isolated worker)
- **EF Core 8** + SQL Server provider, snake_case naming
- **Auth:** Agent = API Key + HMAC-SHA256 (`ApiKeyAuthMiddleware`) | Portal = Entra ID Bearer via MSAL (`BearerAuthMiddleware`). Easy Auth on func-kryoss is pass-through only (AllowAnonymous).
- **Crypto:** RSA-2048 + AES-256-GCM (`CryptoService`)
- **Observability:** Application Insights
- **Deploy target:** Function App `func-kryoss` in RG `rg-kryoss` (Central US) + Azure SQL `sql-kryoss.database.windows.net`
- **Single project:** `src/KryossApi/KryossApi.csproj`

---

## HTTP endpoints

### Agent API (v1) — API Key + HMAC

| Method | Path | Function | Purpose |
|---|---|---|---|
| POST | `/v1/enroll` | `EnrollFunction.Run` | First-run enrollment with code |
| GET | `/v1/controls?assessmentId=X` | `ControlsFunction.Run` | Agent pulls catalog subset |
| POST | `/v1/results` | `ResultsFunction.Run` | Agent ships encrypted payload |

### Portal API (v2) — Bearer token + RBAC + RLS

| Method | Path | Function | Purpose |
|---|---|---|---|
| GET/POST/DEL | `/v2/enrollment-codes[/id]` | `EnrollmentCodesFunction` | Manage enrollment codes |
| GET | `/v2/machines` | `MachinesFunction.List` | Fleet list (filterable by org), includes ipAddress + domainStatus |
| GET | `/v2/machines/{id}` | `MachinesFunction.Detail` | Single machine (all ~30 fields) + latest snapshot |
| GET | `/v2/machines/{id}/history` | `MachinesFunction.History` | Assessment history, includes frameworkScores[] per run |
| GET | `/v2/machines/{id}/software` | `MachinesFunction.Software` | Software inventory (parsed from RawPayload) |
| GET/POST/PATCH/DEL | `/v2/controls[/id]` | `ControlDefsFunction` | Catalog CRUD (paginated, includes `check_json`) |
| GET | `/v2/catalog/controls?platform=W11&framework=HIPAA` | `CatalogControlsFunction.List` | Read-only catalog view for portal (flat, no `check_json`, with platform + framework tags) |
| GET | `/v2/dashboard/fleet` | `DashboardFunction.Fleet` | KPIs, grade distribution, aggregated frameworkScores per fleet |
| GET | `/v2/dashboard/machine/{id}` | `DashboardFunction.Machine` | Per-machine dashboard |
| GET/POST | `/v2/assessment-profiles` | `AssessmentProfilesFunction` | Assessment templates |
| GET | `/v2/reports/{runId}?type=technical\|executive\|presales` | `ReportsFunction.Generate` | Per-run HTML report |
| GET | `/v2/reports/org/{orgId}?type=...` | `ReportsFunction.GenerateOrg` | Org-wide report |

---

## Folder layout

```
src/KryossApi/
├── Program.cs                       <- Host + DI + middleware pipeline
├── Functions/
│   ├── Agent/                       <- v1 endpoints
│   │   ├── EnrollFunction.cs
│   │   ├── ControlsFunction.cs
│   │   └── ResultsFunction.cs
│   └── Portal/                      <- v2 endpoints
│       ├── MachinesFunction.cs
│       ├── EnrollmentCodesFunction.cs
│       ├── ControlDefsFunction.cs
│       ├── CatalogControlsFunction.cs  <- read-only /v2/catalog/controls for portal
│       ├── DashboardFunction.cs
│       ├── AssessmentProfilesFunction.cs
│       └── ReportsFunction.cs
├── Middleware/
│   ├── ApiKeyAuthMiddleware.cs      <- Agent HMAC validation (timestamp+method+path+bodyHash)
│   ├── BearerAuthMiddleware.cs      <- Portal JWT validation
│   ├── RbacMiddleware.cs            <- Permission check ([RequirePermission("...")])
│   ├── RlsMiddleware.cs             <- Sets SESSION_CONTEXT for RLS
│   └── ActlogMiddleware.cs          <- Request logging to actlog table
├── Services/
│   ├── CryptoService.cs             <- RSA keygen, AES-GCM decrypt
│   ├── EnrollmentService.cs         <- Code redemption, machine registration
│   ├── EvaluationService.cs         <- SERVER-SIDE PASS/FAIL eval vs check_json
│   ├── ReportService.cs             <- HTML report rendering
│   ├── ActlogService.cs             <- Audit logging
│   ├── CurrentUserService.cs        <- Request-scoped user context
│   └── AuditInterceptor.cs          <- EF Core CreatedBy/UpdatedBy interceptor
├── Data/
│   ├── KryossDbContext.cs           <- All DbSets
│   └── Entities/
│       ├── AssessmentRun.cs
│       ├── ControlDef.cs
│       ├── Enrollment.cs
│       ├── Franchise.cs
│       ├── Machine.cs
│       ├── Organization.cs
│       └── Auth.cs
└── Models/                          <- (empty — DTOs inline in functions)
```

---

## Middleware order (Program.cs pipeline)

1. `ApiKeyAuthMiddleware` — resolves API Key → OrgId from `auth_api_keys`
2. `BearerAuthMiddleware` — validates JWT if no API key
3. `RbacMiddleware` — enforces `[RequirePermission(...)]` attributes
4. `RlsMiddleware` — sets `SESSION_CONTEXT('org_id', ...)` on SQL connection
5. `ActlogMiddleware` — writes every request to `actlog`

---

## Key services (what they do)

- **`CryptoService`** — Generates RSA-2048 keypairs per org (private → Key Vault, public → `org_crypto_keys`). Decrypts agent payload envelopes (AES-GCM with RSA-wrapped key).
- **`EnrollmentService`** — Validates enrollment code, creates `machines` row, assigns API key, returns public key to agent. Supports `maxUses` on enrollment codes (multi-use). Auto-creates a default assessment if the org doesn't have one. Handles re-enrollment: reuses existing machine row by hostname match.
- **`EvaluationService`** — **This is the scoring engine.** Takes `control_results[]` + raw snapshot, loads `control_defs.check_json` for each, evaluates PASS/WARN/FAIL, writes `control_results` rows, computes and persists per-framework scores in `run_framework_scores`. Now also persists all ~25 hardware fields from agent payload.
- **`ReportService`** — Renders the 3 report types (technical/executive/presales) using `Scripts/Audit/Kryoss-Report-Template.html` as the base (logo embedded base64, ribbon, Montserrat). Self-contained HTML, no external deps.

---

## Database

**Connection:** env var `SqlConnectionString` → Azure SQL `sql-kryoss.database.windows.net` / `KryossDb`

**Migration layout:** 14 numbered schema files + seed files (see `sql/` folder). Key tables:

| Domain | Tables |
|---|---|
| Auth/RBAC | `modules`, `actions`, `permissions`, `roles`, `role_permissions`, `users`, `actlog` |
| Org | `franchises`, `organizations`, `auth_api_keys`, `org_crypto_keys` |
| CMDB | `machines`, `machine_snapshots` (with `raw_*` JSON cols), `machine_software`, `machine_users` |
| Catalog | `control_categories`, `control_defs`, `frameworks`, `platforms`, `control_frameworks`, `control_platforms` |
| Assessment | `assessments`, `assessment_controls`, `assessment_runs`, `control_results`, `run_framework_scores` |
| Enrollment | `enrollment_codes` |
| CRM/Tickets | `crm_*`, `tickets_*` (tables exist, features Phase 5+) |

**Schema files to read when DB-adjacent changes are needed:**
- `sql/004_assessment.sql` — core catalog + assessment tables
- `sql/014_snapshot_rawdata.sql` — `raw_*` columns added to `machine_snapshots`
- `sql/009_rls.sql` — RLS predicates

**Seed files (apply in order):**
- `seed_001_roles_permissions.sql` — RBAC baseline
- `seed_002_frameworks_platforms.sql` — frameworks + 8 platforms + categories
- `seed_003_crm_tickets.sql` — CRM schema
- `seed_004_controls.sql` — 605 auto-extracted controls (SC+BL 4-digit)
- `seed_005_controls_patch.sql` — HIPAA refinements (BL-0445..0469) + CIS/NIST tag fixes
- `seed_006b_deactivate_legacy.sql` — soft-deletes 91 legacy BL-XXX
- `seed_007_platform_scope_workstation.sql` + `seed_007b_prune_inactive_platforms.sql` — W10/W11 scope
- `seed_007c_platform_scope_server.sql` — links 647 controls to MS19/MS22/MS25

**Verification:** `sql/check_catalog_health.sql` — 14-section read-only health check.

---

## Deploy

**Scripts:** `deploy/Setup-Azure.ps1`, `deploy/Remove-Azure.ps1`
**Config:** `deploy/deploy-config.json`
**Resources:**
- RG `rg-kryoss` (centralus)
- SQL `sql-kryoss.database.windows.net` / DB `KryossDb`
- Function App `func-kryoss`
- App Insights `ai-kryoss`
- Storage `stkryoss4031`
- Key Vault (name in deploy-config.json)

---

## Test data

`test-data/` has canned request/response pairs:
- `01_enroll_request.json` / `02_enroll_response.json`
- `03_controls_response.json`
- `04_results_request.json` / `05_results_response.json`
- `Setup-LocalTest.ps1` — local dev bootstrap
- `Test-AgentFlow.ps1` — end-to-end flow tester

---

## Docs

- `docs/agent-payload-schema.md` — **v1.1** contract between agent and `/v1/results` (630 controls, 7 engines, 5 raw blocks, 3 new HIPAA refinement blocks: `mfa`, `event_logs`, `backup_posture`). Size ~175 KB per run.
- `docs/phase-roadmap.md` — 6-phase roadmap (workstation → server → DC → cloud → network → attestation) + decision log + current metrics snapshot.

---

## Known gaps (see `../CLAUDE.md` for details)

1. ✅ `check_json` casing — fixed by `seed_005b_fix_casing.sql`
2. 🟠 Agent payload partially enriched — HardwareInfo expanded (~25 fields), but `raw_*` blocks (network, users, security posture) still not populated
3. ✅ Platform scope — server resolves from OS string, agent sends `X-Agent-Id`; MS19/MS22/MS25 now have 647 controls linked (seed_007c)

## Auth config (Easy Auth on func-kryoss)

Easy Auth is configured with `platform.enabled=true` but `requireAuthentication=false`
and `unauthenticatedClientAction=AllowAnonymous`. This means Azure passes through
`X-MS-CLIENT-PRINCIPAL` headers when present but does NOT block unauthenticated
requests. All actual auth is handled by custom middleware:
- `ApiKeyAuthMiddleware` — agent HMAC validation
- `BearerAuthMiddleware` — portal JWT validation (reads `X-MS-CLIENT-PRINCIPAL` or `Authorization` header)

## Utility SQL scripts

- `sql/cleanup_all_scan_data.sql` — disables triggers, wipes all scan data (assessment_runs, control_results, run_framework_scores, machine_snapshots). For dev/test reset only.
