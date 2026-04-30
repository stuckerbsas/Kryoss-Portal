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
| POST | `/v1/collect` | `CollectFunction.Run` | Offline collection: collector uploads on behalf of machines without internet (auto-enrolls unknown machines) |

### Portal API (v2) — Bearer token + RBAC + RLS

| Method | Path | Function | Purpose |
|---|---|---|---|
| GET | `/v2/version` | `VersionFunction.Run` | API version, build time, runtime (no auth required) |
| GET | `/v2/reports/diagnose/{orgId}?type=all` | `ReportsFunction.Diagnose` | Diagnostic: runs all 15 recipes with timing + error per block |
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
| GET | `/v2/reports/org/{orgId}?type=c-level\|technical\|executive\|preventas\|framework\|proposal\|monthly&tone=opener\|detailed&framework=...&lang=en\|es` | `ReportsFunction.GenerateOrg` | Org-wide HTML report (7-type unified system via ReportComposer, legacy types fall back to ReportService) |
| GET | `/v2/service-catalog` | `ServiceCatalogFunction.List` | List active service catalog items (14 remediation categories) |
| GET | `/v2/franchise-rates/{franchiseId}` | `ServiceCatalogFunction.GetRate` | Get franchise hourly rate + margin |
| PATCH | `/v2/franchise-rates/{franchiseId}` | `ServiceCatalogFunction.SetRate` | Set franchise hourly rate + margin (admin:write) |
| GET | `/v2/inventory/hardware?organizationId=X` | `InventoryFunction.Hardware` | Org-level hardware inventory (all machines) |
| GET | `/v2/inventory/software?organizationId=X` | `InventoryFunction.Software` | Org-level software inventory (600+ commercial app detection) |
| GET | `/v2/hygiene?organizationId=X` | `HygieneFunction.Get` | AD hygiene findings for portal |
| GET | `/v2/agent/download?orgId=X&enrollmentCodeId=Y` | `AgentDownloadFunction` | Download org-patched agent binary |
| POST | `/v2/m365/connect` | `M365Function.Connect` | Connect M365 tenant + initial scan |
| POST | `/v2/m365/scan` | `M365Function.Scan` | Re-run M365 security scan |
| GET | `/v2/m365?organizationId=X` | `M365Function.Get` | Latest M365 scan results |
| DELETE | `/v2/m365/disconnect` | `M365Function.Disconnect` | Remove M365 tenant connection |
| GET | `/v2/cloud-assessment/connection-status?organizationId=X` | `CloudAssessmentFunction.ConnectionStatus` | Graph/Azure/PBI connection state + percentage (CA-12) |
| GET | `/v2/cloud-assessment/copilot-lens/{scanId}` | `CloudAssessmentFunction.CopilotLens` | D1-D6 Copilot Readiness scores + filtered findings from CA scan (CA-12) |
| * | `/v2/copilot-readiness/*` | `CopilotReadinessFunction` | **DEPRECATED** — returns HTTP 410 since CA-12 (sunset 2026-05-18). Use Cloud Assessment equivalents. |
| GET | `/v2/users` | `UsersFunction.List` | List users (franchise-scoped, admin:read) |
| GET | `/v2/users/{id}` | `UsersFunction.Get` | User detail (admin:read) |
| PATCH | `/v2/users/{id}` | `UsersFunction.Update` | Update role/franchise/org (admin:edit) |
| DELETE | `/v2/users/{id}` | `UsersFunction.Delete` | Soft-delete user (admin:delete) |
| GET | `/v2/roles` | `UsersFunction.ListRoles` | List available roles (admin:read) |
| GET | `/v2/me` | `MeFunction.Me` | Current user profile + permissions (no perm required) |
| PATCH | `/v2/me` | `MeFunction.Update` | Update own displayName/phone/jobTitle (no perm required) |

### Agent API (v1) — additional endpoints

| Method | Path | Function | Purpose |
|---|---|---|---|
| POST | `/v1/hygiene` | `HygieneFunction.Submit` | Agent submits AD hygiene findings |
| POST | `/v1/ports` | (in MachinesFunction or InventoryFunction) | Agent submits port scan results |
| GET | `/v1/speedtest` | `SpeedTestFunction.Download` | Returns 10 MB random bytes for agent speed test |
| POST | `/v1/speedtest` | `SpeedTestFunction.Upload` | Accepts agent upload for speed test measurement |
| GET | `/v1/schedule` | `ScheduleFunction.Run` | A-13: Returns assigned scan time slot for agent (exempt from auth, HMAC-signed) |
| POST | `/v1/collect` | `CollectFunction.Run` | Offline collection: upload on behalf of other machines |
| POST | `/v1/heartbeat` | `HeartbeatFunction.Run` | Agent heartbeat (updates last_seen, returns pendingTasks[]) |
| POST | `/v1/task-result` | `TaskResultFunction.Run` | Agent reports remediation task execution result |
| GET | `/v1/agent/latest-version` | `AgentVersionFunction.LatestVersion` | Check latest agent version (from blob storage) |
| GET | `/v1/agent/download` | `AgentVersionFunction.Download` | Download latest agent binary (from blob storage) |
| GET | `/v1/report?type=X&tone=Y` | `ReportDownloadFunction.Run` | Agent downloads HTML report (HMAC auth) |

### Portal API (v2) — External Exposure

| Method | Path | Function | Purpose |
|---|---|---|---|
| POST | `/v2/external-scan` | `ExternalExposureFunction.StartScan` | Start external port scan (requires consent) |
| GET | `/v2/external-scan` | `ExternalExposureFunction.GetLatest` | Latest completed scan with results + findings |
| GET | `/v2/external-scan/history` | `ExternalExposureFunction.History` | Last 20 external scans |
| PATCH | `/v2/organizations/{id}/external-scan` | `OrganizationsFunction` | Toggle external scan consent |

### Portal API (v2) — Remediation

| Method | Path | Function | Purpose |
|---|---|---|---|
| POST | `/v2/remediation/tasks` | `RemediationFunction.CreateTask` | Create remediation task for machine (admin:write) |
| GET | `/v2/remediation/tasks` | `RemediationFunction.ListTasks` | List tasks by machine or org |
| POST | `/v2/remediation/tasks/{id}/rollback` | `RemediationFunction.Rollback` | Rollback completed task |
| POST | `/v2/remediation/tasks/{id}/rollback` | `RemediationFunction.Rollback` | Rollback completed task |
| PATCH | `/v2/remediation/tasks/{id}/cancel` | `RemediationFunction.CancelTask` | Cancel pending/approved task |
| PATCH | `/v2/remediation/tasks/{id}/reschedule` | `RemediationFunction.Reschedule` | Reschedule pending/approved task |
| GET | `/v2/remediation/history` | `RemediationFunction.History` | Remediation audit trail by org |
| GET | `/v2/remediation/catalog` | `RemediationFunction.GetCatalog` | List available remediation actions |

### Agent API (v1) — Service Inventory

| Method | Path | Function | Purpose |
|---|---|---|---|
| POST | `/v1/services` | `ServiceInventoryFunction.Run` | Agent submits service inventory (upsert/remove stale) |

### Portal API (v2) — Service Management

| Method | Path | Function | Purpose |
|---|---|---|---|
| GET | `/v2/machines/{machineId}/services` | `ServiceManagementFunction.ListServices` | List services with isProtected/isPriority flags |
| POST | `/v2/machines/{machineId}/services/{serviceName}/action` | `ServiceManagementFunction.ServiceAction` | Queue start/stop/restart via remediation task |
| PATCH | `/v2/machines/{machineId}/priority-services` | `ServiceManagementFunction.TogglePriority` | Toggle org priority service |

### Portal API (v2) — Machine Activity

| Method | Path | Function | Purpose |
|---|---|---|---|
| GET | `/v2/machines/{id}/activity` | `MachinesFunction.Activity` | Unified timeline (actlog + remediation_log) |

### Portal API (v2) — Network diagnostics

| Method | Path | Function | Purpose |
|---|---|---|---|
| GET | `/v2/network-diagnostics?organizationId=X` | `NetworkDiagnosticsFunction.List` | Org-level network diagnostics with latency/routes |
| GET | `/v2/network-diagnostics/{machineId}` | `NetworkDiagnosticsFunction.Detail` | Per-machine latest network diagnostic detail |

### Portal API (v2) — Network Sites (IA-11)

| Method | Path | Function | Purpose |
|---|---|---|---|
| GET | `/v2/network-sites?organizationId=X` | `NetworkSitesFunction.List` | List auto-derived network sites |
| POST | `/v2/network-sites/rebuild` | `NetworkSitesFunction.Rebuild` | Rebuild sites from current machine IPs |
| PATCH | `/v2/network-sites/{siteId}` | `NetworkSitesFunction.Update` | Rename site or set contracted bandwidth |
| GET | `/v2/network-sites/ip-history?organizationId=X` | `NetworkSitesFunction.IpHistory` | Public IP change timeline |
| GET | `/v2/network-sites/{siteId}/speed-history` | `NetworkSitesFunction.SpeedHistory` | 90d speed/latency/DNS timeseries for site |
| GET | `/v2/network-sites/{siteId}/machines` | `NetworkSitesFunction.SiteMachines` | Machines at site with latest diag |
| GET | `/v2/network-sites/wan-health?organizationId=X` | `NetworkSitesFunction.WanHealth` | Org WAN score + per-site scores + findings |
| GET | `/v2/network-sites/{siteId}/traceroute` | `NetworkSitesFunction.SiteTraceroute` | Latest traceroute data per machine at site |

### Portal API (v2) — CVE Findings (A-01)

| Method | Path | Function | Purpose |
|---|---|---|---|
| GET | `/v2/cve-findings?organizationId=X&severity=` | `CveFindingsFunction.List` | CVE findings with summary + severity filter |
| POST | `/v2/cve-findings/rescan?organizationId=X` | `CveFindingsFunction.Rescan` | Re-scan all machines for CVEs |
| PATCH | `/v2/cve-findings/{id}/dismiss` | `CveFindingsFunction.Dismiss` | Dismiss a CVE finding |
| GET | `/v2/cve-findings/stats?organizationId=X` | `CveFindingsFunction.Stats` | Top CVEs by CVSS + top vulnerable software |

### Portal API (v2) — CVE Sync (HQ)

| Method | Path | Function | Purpose |
|---|---|---|---|
| GET | `/v2/cve-sync/status` | `CveSyncFunction.GetStatus` | CVE DB health: counts, last sync, coverage, recent syncs |
| GET | `/v2/cve-sync/products` | `CveSyncFunction.GetProducts` | Monitored product checklist with per-vendor stats |
| POST | `/v2/cve-sync` | `CveSyncFunction.RunManual` | Trigger manual sync (?full=true for rebuild) |
| Timer | `0 0 3 * * *` | `CveSyncFunction.RunDaily` | Daily incremental NVD + CISA KEV sync |

### Portal API (v2) — Patch Compliance (A-02)

| Method | Path | Function | Purpose |
|---|---|---|---|
| GET | `/v2/patch-compliance?organizationId=X` | `PatchComplianceFunction.Summary` | Org patch compliance summary + per-machine status |
| GET | `/v2/patch-compliance/{machineId}/patches` | `PatchComplianceFunction.MachinePatches` | Installed hotfixes for machine |

### Agent + Portal API — DC Health (DC-02+03)

| Method | Path | Function | Purpose |
|---|---|---|---|
| POST | `/v1/dc-health` | `DcHealthFunction.Submit` | Agent submits DC health snapshot (schema/FSMO/replication/sites) |
| GET | `/v2/dc-health?organizationId=X` | `DcHealthFunction.Get` | Latest DC health snapshot + history (last 20) |

### Portal API (v2) — Infrastructure Assessment

| Method | Path | Function | Purpose |
|---|---|---|---|
| POST | `/v2/infra-assessment/scan` | `InfraAssessmentFunction.Scan` | Start new IA scan (body: organizationId, scope?) |
| GET | `/v2/infra-assessment?organizationId=X` | `InfraAssessmentFunction.Latest` | Latest scan with all children |
| GET | `/v2/infra-assessment/{scanId}` | `InfraAssessmentFunction.Detail` | Single scan detail |
| GET | `/v2/infra-assessment/history?organizationId=X` | `InfraAssessmentFunction.History` | Scan history (summary, last 20) |

### Portal API (v2) — Drift Alerts (CA-15)

| Method | Path | Function | Purpose |
|---|---|---|---|
| GET | `/v2/alert-rules` | `AlertFunction.ListRules` | List alert rules for franchise |
| POST | `/v2/alert-rules` | `AlertFunction.CreateRule` | Create alert rule |
| PATCH | `/v2/alert-rules/{ruleId}` | `AlertFunction.UpdateRule` | Update alert rule |
| DELETE | `/v2/alert-rules/{ruleId}` | `AlertFunction.DeleteRule` | Delete alert rule |
| GET | `/v2/alerts/history` | `AlertFunction.History` | Alert history (last 100) |
| POST | `/v2/alert-rules/{ruleId}/test` | `AlertFunction.TestRule` | Test alert delivery |

### Portal API (v2) — Network Topology (IA-2)

| Method | Path | Function | Purpose |
|---|---|---|---|
| GET | `/v2/topology?organizationId=X` | `TopologyFunction.Get` | Network topology graph (nodes + edges from LLDP/CDP) |

---

## Folder layout

```
src/KryossApi/
├── Program.cs                       <- Host + DI + middleware pipeline
├── Functions/
│   ├── Agent/                       <- v1 endpoints
│   │   ├── EnrollFunction.cs
│   │   ├── ControlsFunction.cs
│   │   ├── ResultsFunction.cs
│   │   ├── HeartbeatFunction.cs      <- POST /v1/heartbeat (returns pendingTasks[])
│   │   ├── TaskResultFunction.cs     <- POST /v1/task-result (agent reports remediation results)
│   │   └── AgentVersionFunction.cs   <- GET /v1/agent/latest-version + /v1/agent/download
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
│       ├── NetworkSitesFunction.cs    <- IA-11: list, rebuild, update, IP history
│       ├── InfraAssessmentFunction.cs <- IA: start scan, latest, detail, history
│       ├── TopologyFunction.cs         <- /v2/topology (IA-2: network graph from LLDP/CDP)
│       ├── OrganizationsFunction.cs
│       ├── ExternalExposureFunction.cs <- POST/GET /v2/external-scan (consent-gated port scan)
│       ├── RemediationFunction.cs     <- /v2/remediation/* (create task, list, rollback, history, catalog)
│       ├── PatchComplianceFunction.cs <- /v2/patch-compliance (org summary + machine patches)
│       ├── DcHealthFunction.cs        <- POST /v1/dc-health (agent) + GET /v2/dc-health (portal)
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
│   ├── ScanScheduleService.cs       <- A-13: Slot assignment (gap-filling) + schedule computation
│   ├── EnrollmentService.cs         <- Code redemption, machine registration + slot assignment
│   ├── EvaluationService.cs         <- SERVER-SIDE PASS/FAIL eval vs check_json
│   ├── ReportService.cs             <- Legacy HTML report rendering (executive, presales)
│   ├── Reports/                     <- Unified Report System (compositional blocks)
│   │   ├── IReportBlock.cs          <- IReportBlock + IReportRecipe interfaces
│   │   ├── ReportOptions.cs         <- Lang, FrameworkCode, Tone
│   │   ├── ReportData.cs            <- Unified data model (endpoint + cloud + hygiene + benchmarks)
│   │   ├── ReportHelpers.cs         <- 22 static HTML helpers extracted from ReportService
│   │   ├── ReportStyles.cs          <- CSS generation
│   │   ├── ReportDataLoader.cs      <- Loads all data for a report
│   │   ├── ReportComposer.cs        <- Orchestrator: resolves recipe, renders blocks
│   │   ├── Blocks/ (17 blocks)      <- CoverBlock, SemaforoBlock, KpiBlock, CtaBlock, AssetMatrixBlock,
│   │   │                               TopFindingsBlock, IronSixBlock, RiskScoreBlock, ThreatVectorsBlock,
│   │   │                               MethodologyBlock, CloudPostureBlock, FrameworkGaugeBlock,
│   │   │                               GapAnalysisBlock, ServiceCatalogBlock, TimelineBlock,
│   │   │                               ScoreTrendBlock, DeltaBlock
│   │   └── Recipes/ (7 recipes)     <- CLevelRecipe, TechnicalRecipe, PreventaOpenerRecipe,
│   │                                   PreventaDetailedRecipe, FrameworkRecipe, ProposalRecipe, MonthlyRecipe
│   ├── BinaryPatcher.cs             <- UTF-16LE sentinel replacement in agent .exe binary
│   ├── ActlogService.cs             <- Audit logging
│   ├── CurrentUserService.cs        <- Request-scoped user context
│   ├── M365ScannerService.cs         <- Graph API client: 30 M365/Entra ID security checks
│   ├── InfraAssessment/
│   │   ├── IInfraAssessmentService.cs <- Interface (start, latest, detail, history)
│   │   ├── InfraAssessmentService.cs  <- Stub orchestrator (creates scan row)
│   │   └── Pipelines/                 <- Future: site discovery, device audit, etc.
│   ├── ExternalScanner.cs            <- Server-side TCP port scan of public IPs (53 ports, banner grab, findings engine)
│   ├── WanHealthService.cs           <- IA-3: WAN health scoring (0-100) + 11 finding rules per site
│   ├── CveService.cs                 <- A-01: CVE matching engine (LIKE patterns + semantic version comparison)
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
│       ├── MachineNetworkDiag.cs    <- network diagnostics (speed, latency, routes, VPN, adapters)
│       ├── AdHygiene.cs             <- AD hygiene findings (stale objects, security issues)
│       ├── Brand.cs                 <- Brand/MSP customization
│       ├── M365Tenant.cs            <- M365Tenant + M365Finding entities
│       ├── InfraAssessment.cs       <- 6 entities: Scan, Site, Device, Connectivity, Capacity, Finding
│       ├── ExternalScan.cs          <- ExternalScan + ExternalScanResult + ExternalScanFinding
│       ├── Remediation.cs           <- RemediationAction + RemediationTask + OrgAutoRemediate
│       ├── CveEntry.cs              <- CveEntry + MachineCveFinding + CveSyncLog (A-01)
│       ├── PatchStatus.cs           <- MachinePatchStatus + MachinePatch (A-02)
│       ├── DcHealthSnapshot.cs      <- DcHealthSnapshot + DcReplicationPartner (DC-02+03)
│       ├── WanFinding.cs            <- WanFinding entity (IA-3, linked to NetworkSite)
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
- **`ReportService`** — Legacy renderer for executive/presales types. Being replaced by the Unified Report System.
- **`ReportComposer`** — **New unified report orchestrator.** Resolves recipe by type string, loads data via `ReportDataLoader`, renders 17 composable blocks into self-contained HTML. 7 report types: C-Level, Technical, Preventa Opener/Detailed, Framework Compliance, Business Proposal (auto-pricing from service catalog), Monthly Progress. Cloud sections conditional (omitted when no M365/Azure). Spec: `docs/superpowers/specs/2026-04-19-unified-report-system-design.md`.
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
| CMDB | `machines`, `machine_snapshots` (with `raw_*` JSON cols), `machine_software`, `machine_users`, `machine_disks` (per-drive), `machine_ports` (open ports), `machine_network_diag` + `machine_network_latency` + `machine_network_routes` (network diagnostics) |
| AD Hygiene | `ad_hygiene` (stale machines/users, privileged accounts, kerberoastable, delegation, LAPS, domain info) |
| Catalog | `control_categories`, `control_defs`, `frameworks`, `platforms`, `control_frameworks`, `control_platforms` |
| Assessment | `assessments`, `assessment_controls`, `assessment_runs`, `control_results`, `run_framework_scores` |
| Enrollment | `enrollment_codes` |
| CRM/Tickets | `crm_*`, `tickets_*` (tables exist, features Phase 5+) |
| M365/Cloud | `m365_tenants`, `m365_findings` (Phase 4: Entra ID / M365 security checks) |
| Network Sites | `network_sites`, `machine_public_ip_history` (IA-11: auto-derived sites from public IP clustering) |
| SNMP/Topology | `snmp_configs`, `snmp_devices`, `snmp_device_interfaces`, `snmp_device_supplies`, `snmp_device_neighbors` (IA-2), `snmp_device_profiles`, `snmp_profile_oids` |
| Infra Assessment | `infra_assessment_scans`, `_sites`, `_devices`, `_connectivity`, `_capacity`, `_findings` (IA-0 scaffold) |
| External Exposure | `external_scans`, `external_scan_results`, `external_scan_findings` (server-side port scan with consent) |
| Remediation | `remediation_actions` (whitelist catalog ~50 controls), `remediation_tasks` (per-machine work items + `signature_hash`), `remediation_log` (INSERT-only audit trail), `org_auto_remediate` (per-org auto-fix opt-in) |
| Service Mgmt | `machine_services` (per-machine service inventory: name, status, startupType) |
| CVE | `cve_entries` (product patterns + severity + CVSS), `machine_cve_findings` (per-machine findings), `cve_sync_log` (sync tracking) |
| Patch Compliance | `machine_patch_status` (per-machine WU status + compliance score), `machine_patches` (installed hotfixes) |
| DC Health | `dc_health_snapshots` (schema/FSMO/sites/replication summary), `dc_replication_partners` (per-partner status, CASCADE delete) |
| WAN Health | `wan_findings` (per-site WAN issues from `WanHealthService`), WAN fields on `network_sites` + `machine_network_diag` |

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
- `040_dc_platform_support.sql` — adds `product_type` column to machines + links 647 controls to DC19/DC22/DC25
- `seed_013_dc_controls.sql` — 40 DC-specific controls (DC-001..DC-040)

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
2. ✅ Platform scope — server resolves from OS string + ProductType, agent sends `X-Agent-Id`; MS19/MS22/MS25 + DC19/DC22/DC25 all have controls linked
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

---

## Changelog

### [1.38.6] - 2026-04-30
- **Added:** Azure AD tenant ID enrichment (DAT-01) — `aad_tenant_id` column on `machines` (migration 097). Agent extracts TenantId from `CloudDomainJoin\JoinInfo` registry. API persists + returns in machine detail. Portal shows friendly domain status labels + Tenant ID row.
- **Files:** `Machine.cs`, `EvaluationService.cs`, `MachinesFunction.cs`, `sql/097_machine_aad_tenant_id.sql`

### [1.38.5] - 2026-04-30
- **Added:** SNMP device hostname enrichment — `GET /v2/snmp-devices` now returns `machineName` (from linked Machine) so portal can show hostname for IP-matched SNMP devices instead of blank sysName (DAT-04).
- **Files:** `SnmpInfrastructure.cs`, `KryossDbContext.cs`, `SnmpConfigFunction.cs`

### [1.38.4] - 2026-04-30
- **Fixed:** Network Diagnostics empty — `GroupBy(MachineId).Select(g => g.OrderByDescending().First())` followed by navigation property access (`d.Machine.Hostname`) fails EF Core 8 SQL translation. Replaced with correlated subquery pattern (DAT-05).
- **Files:** `NetworkDiagnosticsFunction.cs`

### [1.38.3] - 2026-04-30
- **Fixed:** CVE product matching — `MatchesCpe` did exact product comparison so CPE slugs ("edge_chromium") never matched CVE display names ("Microsoft Edge (Chromium-based)"). Added `ProductMatches` with normalized bidirectional containment: splits underscores to spaces, strips noise words ("and"/"for"/"the"), checks substring both ways. Fixes Edge, Adobe Reader, VLC, Docker Desktop, AnyConnect, Veeam, Office, FileZilla — all now match their CVE entries. Chrome/Firefox unaffected (already exact match).
- **Files:** `CveSyncService.cs`

### [1.38.2] - 2026-04-30
- **Added:** EPSS integration — `epss_score` + `epss_percentile` columns on `cve_entries` (migration 095). `SyncEpssAsync` downloads bulk CSV from FIRST.org (gzipped, ~250K rows), matches against our CVEs, updates scores in 2000-row chunks. Runs daily as part of `CveSyncService` after KEV sync. EPSS = probability of exploitation in next 30 days (0.0-1.0).
- **Files:** `CveEntry.cs`, `CveSyncService.cs`, `sql/095_cve_epss_columns.sql`

### [1.38.1] - 2026-04-29
- **Fixed:** Loop status showed numbers (0-5) instead of names in portal — API returned `loopStatus` as array, portal expected `Record<string, LoopStatus>` dictionary keyed by `loopName`. Converted to `ToDictionary` with `lastDurationMs` field matching portal's `LoopStatus` interface.
- **Files:** `MachinesFunction.cs`

### [1.38.0] - 2026-04-29
- **Added:** Software normalization pipeline (DB-NORM 1B) — `EvaluationService` now persists `payload.Software` into `software` + `machine_software` tables during scan ingest. Upsert pattern: find-or-create Software catalog entry (with CPE mapping via `CpeMappingService.ResolveKnownCpe`), upsert `MachineSoftware` per machine, mark removed entries with `RemovedAt`. `CveService.ScanMachineAsync` adds direct matching fallback (Path B) when `CveProductMap` is empty — matches `Software.CpeVendor/CpeProduct` against `cve_entries.vendor/product`. `InventoryFunction.Software` and `MachinesFunction.GetSoftware` rewritten to query normalized tables instead of parsing `RawPayload` JSON.
- **Files:** `EvaluationService.cs`, `CpeMappingService.cs`, `CveService.cs`, `InventoryFunction.cs`, `MachinesFunction.cs`

### [1.37.5] - 2026-04-29
- **Fixed:** Heartbeat crash — `geo_country` column NVARCHAR(2) but geo API returns full names ("United States"). Widened to NVARCHAR(60) on both `machine_public_ip_history` and `network_sites`.
- **Files:** `sql/093_widen_geo_country.sql`

### [1.37.4] - 2026-04-29
- **Fixed:** SNMP `SubmitResults` crash — duplicate `IfIndex` in previous interfaces caused `ToDictionary` to throw `ArgumentException`. Switched to `GroupBy` + `First()` to deduplicate.
- **Files:** `SnmpFunction.cs`

### [1.37.3] - 2026-04-29
- **Fixed:** CVE ingestion pipeline deep fix — (1) Multi-field vendor resolution: checks ALL `affected[].vendor` entries then falls back to `providerMetadata.shortName` (was only checking `affected[0].vendor`). (2) Reduced allowlist from 106 to 20 MSP-focused vendors. (3) Product classification column (`product_class`: OS/PLATFORM/APPLICATION/LIBRARY). (4) `ShouldIngest` decision function combines state + vendor checks. (5) Product/version extraction now matches the resolved vendor's affected entry, not blindly `affected[0]`.
- **Files:** `CveSyncService.cs`, `CveEntry.cs`, `cve-vendors.json`, `sql/092_cve_product_class.sql`, `Scripts/Import-CveBulk.ps1`

### [1.37.2] - 2026-04-29
- **Fixed:** WAN Health empty — `X-Forwarded-For` header empty in Azure Functions isolated worker. Added fallback to `HttpContext.Connection.RemoteIpAddress` via ASP.NET Core integration. Fixed in both `ApiKeyAuthMiddleware` (agent) and `BearerAuthMiddleware` (portal). Also added comma-split for multi-proxy chains. Without IP, `PublicIpTracker` returned early → no `machine_public_ip_history` → no `network_sites` → no WAN health.
- **Files:** `ApiKeyAuthMiddleware.cs`, `BearerAuthMiddleware.cs`

### [1.37.1] - 2026-04-29
- **Fixed:** CVE vendor matching — vendor names from CVE.org (display names like "Palo Alto Networks") didn't match CPE-style slugs in whitelist ("paloaltonetworks"). Added `NormalizeVendor()` that strips spaces/underscores/hyphens before comparison. Same fix in `Import-CveBulk.ps1`.
- **Files:** `CveSyncService.cs`, `Scripts/Import-CveBulk.ps1`

### [1.37.0] - 2026-04-29
- **Added:** `GET /v2/local-admins?organizationId=X` — org-level local administrators endpoint. Returns all local admin group members across all machines, grouped by account name with machine list. Sorted by machineCount descending.
- **Files:** `MachinesFunction.cs` (new `LocalAdminsFunction` class)

### [1.36.0] - 2026-04-29
- **Added:** Available Updates pipeline (WUC-02/04) — `machine_available_updates` table (migration 091) stores pending Windows Updates per machine with history. Upsert logic: new KBs inserted, existing refreshed, missing-from-scan marked installed. Cross-references `machine_patches` (PatchCollector) to avoid contradictions. Agent endpoint: `POST /v1/available-updates`. Portal endpoints: `GET /v2/organizations/{orgId}/available-updates` (org aggregation with pending/installed counts per KB), `GET /v2/machines/{machineId}/available-updates` (per-machine list, `?status=all` for history).
- **Files:** `MachineAvailableUpdate.cs` (new), `AvailableUpdatesFunction.cs` (new), `KryossDbContext.cs`, `sql/091_machine_available_updates.sql` (new)

### [1.35.18] - 2026-04-29
- **Added:** WU-API-01 — `windows_update` ActionType for remediation tasks. Operational tasks bypass control_def/action lookup. Kill switch `ENABLE_WINDOWS_UPDATE_REMEDIATION` guards CreateTask, HeartbeatFunction dispatch, and TaskResult (defense-in-depth). Strict param validation: exact keys (`mode`, `reboot`, `deadlineUtc`), reject unknown. Server injects defaults (`security_only`, `if_required`). Nullable `ControlDefId`/`ActionId` on `RemediationTask` (migration 090). All queries null-safe for ControlDef nav property.
- **Files:** `Remediation.cs`, `RemediationFunction.cs`, `HeartbeatFunction.cs`, `TaskResultFunction.cs`, `sql/090_remediation_nullable_control.sql`

### [1.35.17] - 2026-04-29
- **Fixed:** 5 CVE engine quality improvements: (1) Vendor whitelist loaded from shared `cve-vendors.json` — single source of truth for C# and PS1, eliminates drift. (2) `MatchesCpe` now checks vendor+product, not just vendor — eliminates false positives (e.g., Microsoft Word CVE no longer maps to Microsoft Edge). Added `product` column to `cve_entries` (migration 089). (3) Batch `SaveChangesAsync` every 50 records in delta sync — was per-record. (4) CVSS enrichment increased from 50 to 500/run with API key (was 50 fixed). (5) `RebuildProductMapAsync` processes CVEs in chunks of 2000 — was loading all to memory.
- **Files:** `CveSyncService.cs`, `CveEntry.cs`, `KryossApi.csproj`, `cve-vendors.json`, `sql/089_cve_product_column.sql`, `Scripts/Import-CveBulk.ps1`

### [1.35.16] - 2026-04-29
- **Changed:** CVE sync rewritten: CVE.org delta.json as primary source (replaces NVD keyword search). Daily delta → parse CVE Record v5.2 → NVD CVSS enrichment (by cveId, max 50/run) → CISA KEV → product map → machine rescan. Added MSP vendor whitelist (~90 vendors) — filters out irrelevant CVEs from both delta sync and bulk import script.
- **Files:** `CveSyncService.cs`, `Scripts/Import-CveBulk.ps1`

### [1.35.15] - 2026-04-29
- **Changed:** CVE sync now processes one vendor per timer tick (every 2 min). POST queues job → Runner picks up → processes one vendor → saves progress JSON → exits. Next tick continues with next vendor. After all vendors: KEV + product map + rescan. Zero timeout risk. Each vendor logged to actlog with `[N/24]` progress.
- **Files:** `CveSyncFunction.cs`, `CveSyncService.cs`, `CveEntry.cs` (added Progress to CveSyncLog)

### [1.35.14] - 2026-04-29
- **Fixed:** CVE manual sync HTTP timeout — `POST /v2/cve-sync` now returns 202 immediately, queues work via `cve_sync_log` (status=pending). New `CveSync_Runner` timer (every 2 min) picks up pending jobs. Prevents Azure Functions 5-min HTTP timeout killing long syncs. Added `isRunning` to status endpoint. All vendors now logged to actlog (not just those with changes). HTTP errors per vendor logged to actlog.
- **Files:** `CveSyncFunction.cs`, `CveSyncService.cs`

### [1.35.13] - 2026-04-29
- **Fixed:** CVE sync crash after ingesting CVEs — post-sync stages (KEV, product map, machine rescan) now wrapped non-fatal so CVE data isn't lost. Inner exception logged to actlog for EF Core errors.
- **Files:** `CveSyncService.cs`

### [1.35.12] - 2026-04-29
- **Fixed:** CVE sync timeout — NVD responses for large vendors (Microsoft) exceed 2 min. HttpClient timeout raised to 5 min, page size reduced from 2000 → 500 to get faster responses.
- **Files:** `Program.cs`, `CveSyncService.cs`

### [1.35.11] - 2026-04-29
- **Added:** Actlog tracing for CVE sync — writes start/per-vendor/done/fail entries to actlog table for debugging sync issues. Replaced `Console.WriteLine` with `ILogger`.
- **Files:** `CveSyncService.cs`

### [1.35.10] - 2026-04-29
- **Fixed:** CVE NVD sync returned 0 results — `keywordSearch` used CPE vendor names (`igor_pavlov`, `git-scm`) which don't appear in CVE descriptions. Added `VendorSearchTerms` mapping (24 entries) to translate CPE vendors → effective NVD search terms (e.g. `rarlab` → `WinRAR`, `git-scm` → `Git`). Unknown vendors fall back to raw name.
- **Files:** `CveSyncService.cs`

### [1.35.9] - 2026-04-29
- **Fixed:** CVE sync `POST /v2/cve-sync` now returns 202 Accepted immediately, runs sync in background via `Task.Run` + new DI scope. Added diagnostic logging to `SyncVendorFromNvdAsync` — logs HTTP status on NVD failure, CVE count per vendor per page, and sync parameters at start. Previous sync returned 0 results silently because NVD errors were swallowed.
- **Files:** `CveSyncFunction.cs`, `CveSyncService.cs`

### [1.35.8] - 2026-04-29
- **Added:** Heartbeat response includes `latestAgentVersion`, `minAgentVersion`, `apiVersion`, `modeDev` fields (AU-01). Enables agent version handshake without extra API calls. Controlled via env vars `LatestAgentVersion`, `MinAgentVersion`, `AgentModeDev`.
- **Files:** `HeartbeatFunction.cs`

### [1.35.7] - 2026-04-29
- **Fixed:** CVE NVD sync never ran — `CpeMappingService` injected scoped `KryossDbContext` as singleton (DI crash). Now uses `IDbContextFactory`. Added 24 baseline vendors (microsoft, google, mozilla, adobe, cisco, etc.) so sync runs even with empty software table. CPE mapping failure is now non-fatal.
- **Files:** `CveSyncService.cs`, `CpeMappingService.cs`

### [1.35.6] - 2026-04-29
- **Fixed:** Network health score (NET-050) oscillated on boundary-crossing measurements. Continuous metrics (speed, latency, bandwidth saturation) now rounded to nearest 5-unit before threshold comparison, creating dead zones that absorb natural measurement variance.
- **Files:** `EvaluationService.cs`

### [1.35.5] - 2026-04-29
- **Fixed:** Patch compliance score used `DateTime.UtcNow` instead of agent scan timestamp — crossing 7d/14d/30d/60d boundaries between collection and evaluation caused non-deterministic scores. Now uses `payload.Timestamp` as reference time.
- **Files:** `EvaluationService.cs`

### [1.35.4] - 2026-04-29
- **Perf:** Dashboard_Fleet rewritten — uses denormalized `LatestRunId` on machines instead of GroupBy+Max+Any subquery. Top failing + framework scores use server-side subquery instead of IN clause with materialized ID list. Added `latest_run_id` column + index via SQL `087`.
- **Files:** `DashboardFunction.cs`, `Machine.cs`, `EvaluationService.cs`, `sql/087_machine_latest_run_id.sql`

### [1.35.3] - 2026-04-29
- **Fixed:** Services list ordered by DisplayName (A→Z) instead of internal Name.
- **Files:** `ServiceManagementFunction.cs`

### [1.35.2] - 2026-04-29
- **Added:** `GET /v2/cve-sync/products` — monitored product checklist with per-vendor CVE/finding counts + unmapped software count. Extended `GET /v2/cve-sync/status` with totalFindings, softwareWithCpe, totalSoftware, recentSyncs.
- **Files:** `CveSyncFunction.cs`

### [1.35.1] - 2026-04-29
- **Added:** Public IP tracking on heartbeat — `PublicIpTracker.TrackAsync` now called every 15 min (heartbeat) instead of only on compliance scan (24h). Populates `machines.last_public_ip` + `machine_public_ip_history` much faster.
- **Changed:** Network scan enabled by default (`config_enable_network_scan = true`, 12h interval). SQL migration `086` flips existing machines.
- **Files:** `HeartbeatFunction.cs`, `Machine.cs`, `sql/086_enable_network_scan_default.sql`

### [1.35.0] - 2026-04-28
- **Added:** `PATCH /v2/me` — users can update their own displayName, phone, and jobTitle. `GET /v2/me` now returns `phone` and `jobTitle` fields. Profile data available for report headers.
- **Files:** `MeFunction.cs`

### [1.34.12] - 2026-04-28
- **Fixed:** DcHealthFunction machine lookup used `m.Id == agentId` instead of `m.AgentId == agentId` — DC health submissions fell through to hostname fallback instead of matching by agent GUID. Caused empty `dc_health_snapshots` when hostname didn't match.
- **Files:** `DcHealthFunction.cs`

### [1.34.11] - 2026-04-28
- **Fixed:** Heartbeat crash — `actlog.severity` column is VARCHAR(4) but agent error logging wrote `"ERROR"` (5 chars), causing `DbUpdateException` on every heartbeat with errors. Changed to `"ERR"` matching existing convention.
- **Files:** `HeartbeatFunction.cs`

### [1.34.10] - 2026-04-28
- **Fixed:** RemediationLog table name mismatch — EF convention mapped to `remediation_logs` (plural) but SQL table created as `remediation_log` (singular). Added explicit `ToTable("remediation_log")` mapping.
- **Fixed:** HeartbeatFunction crash diagnostic — wrapped entire function in try-catch that logs exception to actlog (CRIT severity) and returns error details in response body for agent-side debugging.
- **Files:** `HeartbeatFunction.cs`, `KryossDbContext.cs`

### [1.34.9] - 2026-04-28
- **Fixed:** HeartbeatFunction DI crash — `GetRequiredService<ActlogService>()` resolved concrete class instead of `IActlogService` interface, causing HTTP 500 on every heartbeat with errors. Root cause of all machines showing `agent_mode=NULL`.
- **Fixed:** 21 empty `catch { }` blocks replaced with `ILogger.LogWarning` for App Insights visibility across all API functions and services.
- **Files:** `HeartbeatFunction.cs`, `TaskResultFunction.cs`, `RemediationFunction.cs`, `ServiceManagementFunction.cs`, `HypervisorConfigFunction.cs`, `ControlDefsFunction.cs`, `SnmpConfigFunction.cs`, `ReportsFunction.cs`, `AzurePipeline.cs`, `HypervisorPipeline.cs`, `ReportDataLoader.cs`

### [1.34.8] - 2026-04-28
- **Fixed:** Re-enrollment credential desync — `GenerateInitialKeys()` ran on every re-enrollment, generating new MachineSecret/SessionKey. If `SaveChangesAsync` failed, agent had new creds, DB had old ones → permanent auth failure. Now preserves existing credentials on re-enrollment.
- **Files:** `EnrollmentService.cs`

### [1.34.7] - 2026-04-28
- **Fixed:** Re-enrollment AgentId desync — generating new `Guid.NewGuid()` on re-enrollment meant if `SaveChangesAsync` failed, the agent had a different AgentId than the DB, breaking heartbeat forever. Now reuses existing AgentId on re-enrollment.
- **Files:** `EnrollmentService.cs`

### [1.34.6] - 2026-04-28
- **Fixed:** PBI radar showing score 5 when not configured — probe failure returned `"partial"` instead of `"skipped"`, so radar included PBI with a near-perfect default score. Now returns `"skipped"` when API is inaccessible.
- **Files:** `PowerBiPipeline.cs`

### [1.34.5] - 2026-04-28
- **Fixed:** License tier column empty — ProductivityPipeline wasn't setting `Insights` on PipelineResult, so `SkuPlans` was null and `ResolveLicenseTiers` never ran.
- **Files:** `ProductivityPipeline.cs`

### [1.34.4] - 2026-04-28
- **Added:** Power BI Service `Tenant.Read.All` to programmatic consent (OptionalApis). Automates step 1 of PBI setup — tenant admin portal setting (step 2) remains manual.
- **Files:** `UnifiedCloudConnectFunction.cs`

### [1.34.3] - 2026-04-28
- **Added:** License tier detection per feature — reads `subscribedSkus` service plans (already collected by ProductivityPipeline), maps 22 features to `none`/`standard`/`premium` tier. New `licenseTier` field on Feature Inventory entries. Covers Entra P1/P2, Intune P1/P2, Purview basic/advanced, Defender for O365 Plan 1/2, Defender for Endpoint, Copilot.
- **Files:** `FeatureInventoryBuilder.cs`, `CloudAssessmentService.cs`

### [1.34.2] - 2026-04-28
- **Added:** Exchange Online REST API integration — 3 checks via InvokeCommand pattern: Unified Audit Logs (`Get-AdminAuditLogConfig`), Safe Attachments (`Get-SafeAttachmentPolicy`), EOP/MDO Standard Protection (`Get-EOPProtectionPolicyRule`). Exchange.ManageAsApp + Exchange Admin role granted via programmatic consent. PBI `TryVerifyPowerBi` re-enabled in consent callback. Feature Inventory now ~42 entries.
- **Files:** `MailFlowPipeline.cs`, `MailFlowInsights.cs`, `MailFlowRecommendations.cs`, `CloudAssessmentService.cs`, `UnifiedCloudConnectFunction.cs`, `FeatureInventoryBuilder.cs`

### [1.34.1] - 2026-04-28
- **Added:** 3 more Lighthouse checks — Entra device join config (`/beta/policies/deviceRegistrationPolicy`), Endpoint Analytics (`/beta/deviceManagement/userExperienceAnalyticsOverview`), Defender auto-onboard via Intune MTD connectors. Feature Inventory now ~39 entries.
- **Files:** `IdentityInsights.cs`, `EndpointInsights.cs`, `IdentityPipeline.cs`, `EndpointPipeline.cs`, `FeatureInventoryBuilder.cs`

### [1.34.0] - 2026-04-28
- **Added:** Lighthouse baseline gap checks — 12 new checks across Identity and Endpoint pipelines: SSPR enablement/registration, break-glass account detection, Defender AV/Firewall/ASR policy presence, Edge profile, OneDrive policy, Windows Update policy, noncompliant device notification templates (via Settings Catalog + beta configurationPolicies + windowsFeatureUpdateProfiles). Feature Inventory expanded from ~25 to ~36 entries.
- **Fixed:** Adoption percentage now nulled when feature is not implemented (prevents misleading 100% on default-compliant states)
- **Files:** `IdentityInsights.cs`, `EndpointInsights.cs`, `IdentityPipeline.cs`, `EndpointPipeline.cs`, `FeatureInventoryBuilder.cs`

### [1.33.5] - 2026-04-28
- **Added:** Feature Inventory — per-scan license/implementation/adoption matrix (~25 features across 7 areas). Built from pipeline insights post-scan, persisted as JSON on `cloud_assessment_scans.feature_inventory`, exposed in `GetLatestScan` and `GetScanDetail` responses.
- **Files:** `FeatureInventoryBuilder.cs` (new), `CloudAssessmentService.cs`, `CloudAssessment.cs`, `KryossDbContext.cs`, `sql/084_feature_inventory.sql`

### [1.33.4] - 2026-04-28
- **Added:** Programmatic consent for optional APIs (Defender) after Graph consent. Uses Graph API to find service principal + assign app roles. Silently skips if API not in tenant. Requires removing Threat Protection from app registration.
- **Files:** `UnifiedCloudConnectFunction.cs`

### [1.33.3] - 2026-04-28
- **Fixed:** Cloud connect consent fails for tenants without Defender (AADSTS650052). Changed `prompt=consent` → `prompt=select_account` so Microsoft only evaluates requested Graph scope, not all app registration permissions
- **Files:** `UnifiedCloudConnectFunction.cs`, `AutoConsentFunction.cs`

### [1.33.2] - 2026-04-28
- **Fixed:** Suppress GSA (Global Secure Access) permission_required findings when NetworkAccessPolicy.Read.All not granted — SMBs don't use GSA, showing "grant permission" was noise
- **Files:** `IdentityRecommendations.cs`, `EntraRecommendations.cs`

### [1.33.1] - 2026-04-28
- **Added:** Remediation task scheduling — `scheduled_for` column, `PATCH /v2/remediation/tasks/{id}/reschedule` endpoint, heartbeat gate (only dispatches when `ScheduledFor <= UtcNow`)
- **Files:** `RemediationFunction.cs`, `HeartbeatFunction.cs`, `Remediation.cs`, `KryossDbContext.cs`, `sql/083_remediation_scheduled_for.sql`

### [1.33.0] - 2026-04-28
- **Added:** Blob-based speed test via SAS tokens (`GET /v1/speedtest/sas`), auto-seeds 100MB test file
- **Files:** `SpeedTestFunction.cs`

---

## Coding Principles

### 1. Think Before Coding
Don't assume. Don't hide confusion. Surface tradeoffs.

- State assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them — don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

### 2. Simplicity First
Minimum code that solves the problem. Nothing speculative.

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.
- Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

### 3. Surgical Changes
Touch only what you must. Clean up only your own mess.

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it — don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

### 4. Goal-Driven Execution
Define success criteria. Loop until verified.

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.
