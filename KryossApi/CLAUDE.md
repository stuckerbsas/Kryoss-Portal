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
| GET | `/v2/catalog/controls?platform=W11&framework=HIPAA` | `CatalogControlsFunction.List` | Read-only catalog view for portal |
| GET | `/v2/dashboard/fleet` | `DashboardFunction.Fleet` | KPIs, grade distribution, aggregated frameworkScores per fleet |
| GET | `/v2/dashboard/machine/{id}` | `DashboardFunction.Machine` | Per-machine dashboard |
| GET/POST | `/v2/assessment-profiles` | `AssessmentProfilesFunction` | Assessment templates |
| GET | `/v2/reports/{runId}` | `ReportsFunction.Generate` | **DEPRECATED** — returns HTTP 410 since 2026-04-15 |
| GET | `/v2/reports/org/{orgId}?type=c-level\|technical\|executive\|preventas&tone=opener\|detailed&framework=...&lang=en\|es` | `ReportsFunction.GenerateOrg` | Org-wide HTML report (4-type baseline, Brand 2025, Big 4 light) |
| GET | `/v2/inventory/hardware?organizationId=X` | `InventoryFunction.Hardware` | Org-level hardware inventory (all machines) |
| GET | `/v2/inventory/software?organizationId=X` | `InventoryFunction.Software` | Org-level software inventory (600+ commercial app detection) |
| GET | `/v2/hygiene?organizationId=X` | `HygieneFunction.Get` | AD hygiene findings for portal |
| GET | `/v2/agent/download?orgId=X&enrollmentCodeId=Y` | `AgentDownloadFunction` | Download org-patched agent binary |
| POST | `/v2/m365/connect` | `M365Function.Connect` | Connect M365 tenant + initial scan |
| POST | `/v2/m365/scan` | `M365Function.Scan` | Re-run M365 security scan |
| GET | `/v2/m365?organizationId=X` | `M365Function.Get` | Latest M365 scan results |
| DELETE | `/v2/m365/disconnect` | `M365Function.Disconnect` | Remove M365 tenant connection |

### Agent API (v1) — additional endpoints

| Method | Path | Function | Purpose |
|---|---|---|---|
| POST | `/v1/hygiene` | `HygieneFunction.Submit` | Agent submits AD hygiene findings |
| POST | `/v1/ports` | (in MachinesFunction or InventoryFunction) | Agent submits port scan results |

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
│       ├── ReportsFunction.cs          <- Brand 2025 redesign with AD hygiene section
│       ├── InventoryFunction.cs        <- /v2/inventory/hardware + /v2/inventory/software (600+ apps)
│       ├── HygieneFunction.cs          <- POST /v1/hygiene (agent) + GET /v2/hygiene (portal)
│       ├── AgentDownloadFunction.cs    <- /v2/agent/download (patched binary)
│       ├── M365Function.cs            <- M365/Entra ID: connect, scan, get, disconnect
│       ├── OrganizationsFunction.cs
│       ├── MeFunction.cs
│       └── RecycleBinFunction.cs
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
│   ├── ReportService.cs             <- HTML report rendering (Brand 2025, framework gauges, AD hygiene)
│   ├── BinaryPatcher.cs             <- UTF-16LE sentinel replacement in agent .exe binary
│   ├── ActlogService.cs             <- Audit logging
│   ├── CurrentUserService.cs        <- Request-scoped user context
│   ├── M365ScannerService.cs         <- Graph API client: 30 M365/Entra ID security checks
│   └── AuditInterceptor.cs          <- EF Core CreatedBy/UpdatedBy interceptor
├── Data/
│   ├── KryossDbContext.cs           <- All DbSets
│   └── Entities/
│       ├── AssessmentRun.cs
│       ├── ControlDef.cs
│       ├── Enrollment.cs
│       ├── Franchise.cs
│       ├── Machine.cs
│       ├── MachineDisk.cs           <- per-drive inventory (drive letter, size, free, type)
│       ├── MachinePort.cs           <- open ports per host (port, protocol, state, service)
│       ├── AdHygiene.cs             <- AD hygiene findings (stale objects, security issues)
│       ├── Brand.cs                 <- Brand/MSP customization
│       ├── M365Tenant.cs            <- M365Tenant + M365Finding entities
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
- **`ReportService`** — Renders the 3 report types (technical/executive/presales) with Brand 2025 redesign: framework score gauges, AD hygiene section, hardware/software summary. Self-contained HTML, Montserrat font, no external deps.
- **`BinaryPatcher`** — Replaces UTF-16LE sentinel strings in the compiled agent .exe to produce org-specific binaries. Sentinels: `@@KRYOSS_ENROLL:` (64 chars), `@@KRYOSS_APIURL:` (256 chars), `@@KRYOSS_ORGNAM:` (128 chars), `@@KRYOSS_MSPNAM:` (128 chars), `@@CLRPRI:` (32 chars), `@@CLRACC:` (32 chars). Called by `AgentDownloadFunction`.
- **`InventoryFunction`** — Org-level hardware and software inventory. Software endpoint includes 600+ commercial application detection list for normalizing `DisplayName` registry values into recognized products.
- **CloudAssessment pipelines** — `Services/CloudAssessment/Pipelines/{Identity,Endpoint,Data,Productivity,Azure}Pipeline.cs` + `Services/CloudAssessment/Recommendations/{Identity,Endpoint,Data,Productivity,Azure}Recommendations.cs`. CA-6 Subsession B added `Services/CloudAssessment/Pipelines/AzurePipeline.cs` (+ `AzureInsights.cs`) and `Services/CloudAssessment/Recommendations/AzureRecommendations.cs` for Azure infrastructure auditing (resources, Defender for Cloud, public exposure, NSG, Key Vault, VM audits).

---

## Database

**Connection:** env var `SqlConnectionString` → Azure SQL `sql-kryoss.database.windows.net` / `KryossDb`

**Migration layout:** 14 numbered schema files + seed files (see `sql/` folder). Key tables:

| Domain | Tables |
|---|---|
| Auth/RBAC | `modules`, `actions`, `permissions`, `roles`, `role_permissions`, `users`, `actlog` |
| Org | `franchises`, `organizations`, `auth_api_keys`, `org_crypto_keys` |
| CMDB | `machines`, `machine_snapshots` (with `raw_*` JSON cols), `machine_software`, `machine_users`, `machine_disks` (per-drive), `machine_ports` (open ports) |
| AD Hygiene | `ad_hygiene` (stale machines/users, privileged accounts, kerberoastable, delegation, LAPS, domain info) |
| Catalog | `control_categories`, `control_defs`, `frameworks`, `platforms`, `control_frameworks`, `control_platforms` |
| Assessment | `assessments`, `assessment_controls`, `assessment_runs`, `control_results`, `run_framework_scores` |
| Enrollment | `enrollment_codes` |
| CRM/Tickets | `crm_*`, `tickets_*` (tables exist, features Phase 5+) |
| M365/Cloud | `m365_tenants`, `m365_findings` (Phase 4: Entra ID / M365 security checks) |

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
2. ✅ Platform scope — server resolves from OS string, agent sends `X-Agent-Id`; MS19/MS22/MS25 now have 647 controls linked (seed_007c)
3. ✅ Hardware inventory — ~25 fields + multi-disk in `machine_disks` table
4. ✅ Software inventory — 600+ commercial app detection, org-level endpoint
5. ✅ Port scanning — `machine_ports` table, TCP top 100 + UDP top 20
6. ✅ AD Hygiene — `ad_hygiene` table, full security audit (privileged, kerberoastable, LAPS, delegation, domain level)
7. ✅ Binary patching — `BinaryPatcher` + `AgentDownloadFunction` for org-specific .exe
8. ✅ Report redesign — Brand 2025 layout with framework gauges, AD hygiene section
9. 🟡 `raw_*` blocks — `raw_users`, `raw_network`, `raw_security_posture` not populated as structured raw blocks in payload (data available via dedicated endpoints)

## Auth config (Easy Auth on func-kryoss)

Easy Auth is configured with `platform.enabled=true` but `requireAuthentication=false`
and `unauthenticatedClientAction=AllowAnonymous`. This means Azure passes through
`X-MS-CLIENT-PRINCIPAL` headers when present but does NOT block unauthenticated
requests. All actual auth is handled by custom middleware:
- `ApiKeyAuthMiddleware` — agent HMAC validation
- `BearerAuthMiddleware` — portal JWT validation (reads `X-MS-CLIENT-PRINCIPAL` or `Authorization` header)

## Utility SQL scripts

- `sql/cleanup_all_scan_data.sql` — disables triggers, wipes all scan data (assessment_runs, control_results, run_framework_scores, machine_snapshots). For dev/test reset only.
