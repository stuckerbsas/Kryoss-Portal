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
