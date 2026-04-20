# Cloud Assessment Platform — Master Plan (ARCHIVED 2026-04-20)

> **⚠ ARCHIVED — see `ROADMAP.md` for current source of truth.** This file kept for historical context (detailed DB schemas, decisions log, original phase specs). Execution decisions + orchestrator prompts now live in `docs/superpowers/plans/ROADMAP.md`.

> **Context:** New parallel feature alongside Copilot Readiness Assessment (shipped 2026-04-16). Cloud Assessment covers full Microsoft Cloud scope (M365, Azure, Entra, Power BI, Intune, Compliance Manager, etc). Copilot Readiness stays intact as specialized slice — no rename, no refactor.

**Status:** Copilot Readiness complete and deployed. 6 pipelines (Entra, Defender, M365, Purview, Power Platform, SharePoint Deep), 169 service plan checks, bilingual report, portal dashboard.

**Architecture strategy:** Option 3 (parallel feature).
- Copilot Readiness: existing tables (`copilot_readiness_*`), existing service, existing tab. Untouched.
- Cloud Assessment: NEW tables (`cloud_assessment_*`), NEW service (`CloudAssessmentService`), NEW top-level portal tab.
- Some pipeline code reuse possible (Entra/Defender/Purview pipelines could be shared via abstraction), but NOT required — duplication OK initially.
- Customer can run BOTH independently: Copilot Readiness for pre-Copilot-deploy gate, Cloud Assessment for ongoing MSP health check.

---

## Cloud Assessment — Architecture

### New DB schema (`cloud_assessment_*` tables)

```
cloud_assessment_scans (parent — one scan covers multiple areas)
├── id UNIQUEIDENTIFIER PK
├── organization_id FK
├── tenant_id FK (m365_tenants)
├── azure_subscription_ids NVARCHAR(MAX) — JSON array of connected subs
├── status (running|completed|partial|failed)
├── overall_score DECIMAL(3,2)
├── area_scores NVARCHAR(MAX) — JSON: {identity:3.5, endpoint:4.2, data:2.8, azure:3.0, ...}
├── verdict NVARCHAR(20)
├── pipeline_status NVARCHAR(MAX)
├── started_at, completed_at, created_at

cloud_assessment_findings
├── id BIGINT PK
├── scan_id FK
├── area NVARCHAR(30) — identity|endpoint|data|productivity|azure|powerbi|compliance|cost
├── service NVARCHAR(30) — entra|intune|defender|purview|m365|azure|powerbi|etc
├── feature NVARCHAR(200)
├── status, priority, observation, recommendation, link_text, link_url

cloud_assessment_metrics
├── scan_id, area, metric_key, metric_value

cloud_assessment_azure_subscriptions
├── organization_id, subscription_id, display_name, state, connected_at, consent_state

cloud_assessment_licenses (new, separate from adoption)
├── scan_id, sku_part_number, friendly_name, purchased, assigned, available

cloud_assessment_adoption
├── scan_id, area, service_name, licensed_count, active_30d, adoption_rate

cloud_assessment_wasted_licenses
├── scan_id, user_principal, display_name, sku, last_sign_in, days_inactive, estimated_cost_year

cloud_assessment_finding_status (state carries across scans)
├── organization_id, area, service, feature, status, owner_user_id, notes, updated_at
```

### New backend namespace

```
Services/CloudAssessment/
├── ICloudAssessmentService.cs
├── CloudAssessmentService.cs (orchestrator — parallel pipelines)
├── CloudScoringEngine.cs (multi-area scoring, flexible weights)
├── CloudAssessmentReportBuilder.cs (new report type "cloud-assessment")
├── Pipelines/
│   ├── IdentityPipeline.cs (Entra deep — can share code with CopilotReadiness/EntraPipeline)
│   ├── EndpointPipeline.cs (Intune + Defender for Endpoint)
│   ├── DataPipeline.cs (Purview + labels + DLP)
│   ├── ProductivityPipeline.cs (M365 usage + adoption)
│   ├── AzurePipeline.cs (NEW — ARM)
│   ├── PowerBiPipeline.cs (NEW)
│   └── ComplianceManagerPipeline.cs (NEW)
├── Recommendations/
│   └── (per-area recommendation engines)
└── Models/
    └── (area-specific DTOs)
```

### New functions

```
Functions/Portal/CloudAssessmentFunction.cs
├── POST /v2/cloud-assessment/scan
├── GET  /v2/cloud-assessment?organizationId={id}
├── GET  /v2/cloud-assessment/{scanId}
├── GET  /v2/cloud-assessment/history?organizationId={id}
├── GET  /v2/cloud-assessment/compare?scanAId=X&scanBId=Y
├── GET  /v2/cloud-assessment/licenses?organizationId={id}
├── PATCH /v2/cloud-assessment/findings/status
├── POST /v2/cloud-assessment/azure/connect (start Azure sub consent flow)
└── GET  /v2/cloud-assessment/azure/subscriptions?organizationId={id}

Functions/Timer/CloudAssessmentTimerFunction.cs
├── Weekly scan (CRON 0 0 3 * * 0 — offset 1hr from Copilot Readiness timer)
└── Reaper (CRON 0 */5 * * * *)
```

### New portal

Top-level left-menu item: **"Cloud Assessment"** (separate from M365 tab which keeps Copilot Readiness sub-tab).

```
Cloud Assessment page
├── Overview (area scores radar chart + overall verdict + timeline)
├── Identity & Access (Entra deep)
├── Endpoint Security (Intune + Defender endpoint)
├── Data Protection (Purview + labels + DLP)
├── Productivity (M365 adoption)
├── Azure Infrastructure (subs + resources + security center)
├── Power BI Governance
├── Compliance (framework mapping)
├── Licenses & Cost
└── Remediation Tracker
```

### New report type

Report dropdown entry: `cloud-assessment` → full Cloud Assessment report (separate from `m365` Copilot Readiness report).

---

## Phase 1 — Licenses & Adoption Detail
**Priority:** HIGHEST — MSP sales tool, data already captured
**Effort:** 1 session, ~4 tasks

### Backend
- SQL migration 030:
  - `copilot_readiness_licenses` (scan_id, sku_part_number, friendly_name, purchased, assigned, available, created_at)
  - `copilot_readiness_adoption` (scan_id, service_name, licensed_count, active_30d_count, adoption_rate, created_at)
- Expand `M365Pipeline` in `Services/CopilotReadiness/Pipelines/M365Pipeline.cs`:
  - After `/subscribedSkus`, persist per-SKU counts
  - Compute per-service adoption rates from usage reports already called
- New endpoint `GET /v2/copilot-readiness/licenses?organizationId={id}` in `Functions/Portal/CopilotReadinessFunction.cs`
  - Returns: summary KPIs, SKUs array, adoption per service, friendly names
- Friendly SKU name mapping dictionary in `Services/CopilotReadiness/SkuFriendlyNames.cs`
  - Map common SKU part numbers (e.g. `SPE_E5` → `Microsoft 365 E5`) — 50+ entries

### Portal
- New sub-tab inside `CopilotReadinessTab.tsx` (or separate `LicensesTab.tsx`)
- 4 cards:
  1. KPIs row (Total purchased / Assigned / Available / Utilization %)
  2. SKU table with utilization progress bars
  3. Service adoption bar chart (recharts, horizontal bars)
  4. Feature heatmap (SKUs × service plans, color-coded status)
- New hook `useCopilotReadinessLicenses(orgId)` in `api/copilotReadiness.ts`

### Report
- Expand section 4 "License Inventory" in `CopilotReadinessReportBuilder.cs`:
  - Per-SKU utilization bars
  - Adoption bar chart (SVG)
  - Friendly names throughout

---

## Phase 2 — Wasted Licenses (Cost Optimization)
**Priority:** HIGH — direct $ sales argument
**Effort:** 1 session, ~3 tasks

### Backend
- SQL migration 031:
  - `copilot_readiness_wasted_licenses` (scan_id, user_principal, display_name, sku, last_sign_in, days_inactive, estimated_cost_per_year)
  - `organization_sku_costs` (organization_id, sku_part_number, cost_per_seat_monthly) — MSP enters manually
- Expand `M365Pipeline`:
  - Call `/users?$select=id,displayName,userPrincipalName,assignedLicenses,signInActivity&$top=999`
  - Detect users with licenses but no sign-in 30d → wasted
- Endpoint `GET /v2/copilot-readiness/licenses/wasted?organizationId={id}`
- Endpoint `PATCH /v2/organizations/{id}/sku-costs` (MSP sets cost per seat)

### Portal
- Add "Wasted Licenses" card in Licenses tab
- Table: user | last sign-in | days inactive | sku | estimated savings
- SKU cost config modal (MSP-only, manual $ input per SKU)
- Total estimated annual savings banner

### Report
- New subsection "License Optimization" in report section 4
- Table with top N wasted licenses + total $ savings estimate
- Recommendation: "Reassign N dormant licenses = $X/year"

---

## Phase 3 — Score Timeline & Drift Tracking
**Priority:** MEDIUM — shows progress to client, justifies retainer
**Effort:** 1 session, ~2 tasks

### Backend
- History endpoint already exists — expand to include dimension deltas
- New endpoint `GET /v2/copilot-readiness/compare?scanAId={id}&scanBId={id}`
  - Returns side-by-side dimension scores, resolved findings, new findings

### Portal
- Timeline card (line chart, last 6 months overall score) — recharts
- Per-dimension delta badges (↑ 0.5 vs last scan)
- "Compare" button → modal with 2 scan picker → side-by-side table

### Report
- New section 5 "Progress Since Last Scan" (only if ≥2 scans exist)
- Show: dimension deltas, N resolved, N new findings

---

## Phase 4 — Remediation Tracker
**Priority:** HIGH — tech workflow tool, shows MSP value
**Effort:** 1 session, ~3 tasks

### Backend
- SQL migration 032:
  - `cloud_assessment_finding_status` (organization_id, service, feature, status: open|in_progress|resolved|deferred, owner_user_id, notes, updated_at, updated_by)
  - Unique on `(organization_id, service, feature)` — state carries across scans
- Endpoint `PATCH /v2/copilot-readiness/findings/status`
  - Body: `{organizationId, service, feature, status, notes}`
- Modify `CopilotReadinessService.GetScanDetailAsync` to LEFT JOIN finding_status, return status per finding
- Auto-resolve: when finding disappears between scans (was Action Required → now Success), mark resolved automatically

### Portal
- Add status dropdown per finding in accordion
- Filter by status (Open / In Progress / Resolved / Deferred / All)
- Progress counter card: "N open / N resolved / N deferred"
- Owner assignment (from org users)

### Report
- New section "Remediation Progress" 
- Breakdown by status
- Resolved findings list (show wins)

---

## Phase 5 — Azure Infrastructure Pipeline (NEW AREA)
**Priority:** HIGH — huge scope expansion beyond M365
**Effort:** 2-3 sessions, 10+ tasks

### Permissions (CRITICAL)
- **Separate consent required** — Azure ARM not covered by Graph consent
- Customer admin needs to:
  1. Assign **"Reader"** RBAC role to Kryoss service principal at subscription level
  2. Optionally "Security Reader" for Defender for Cloud data
- New portal flow: "Connect Azure Subscription" button
  - Shows instructions + Azure CLI command to run
  - Detects when role assignment complete (polling ARM)

### Backend
- SQL migration 033:
  - `cloud_assessment_azure_subscriptions` (organization_id, subscription_id, display_name, state, tenant_id, connected_at)
  - Expand `cloud_assessment_findings` with `area` column (copilot|azure|powerbi|etc)
- New `AzurePipeline.cs` + `AzureRecommendations.cs`:
  - `GET /subscriptions?api-version=2022-12-01` (ARM)
  - Per subscription:
    - Resources: `/subscriptions/{id}/resources`
    - Policies: `/subscriptions/{id}/providers/Microsoft.PolicyInsights/policyStates`
    - Security Center assessments: `/subscriptions/{id}/providers/Microsoft.Security/assessments`
    - Public IPs: `/subscriptions/{id}/providers/Microsoft.Network/publicIPAddresses`
    - Storage accounts with public blob: filter `allowBlobPublicAccess=true`
    - Key Vaults: soft-delete + purge protection check
    - NSGs with Any-Any allow rules
- New dimensions: A1 Identity, A2 Network Posture, A3 Data Protection, A4 Compliance Policies
- Separate scoring weights per area

### Portal
- New sub-tab "Azure Infrastructure"
- Subscriptions table
- Resources breakdown by type (donut)
- Security Center assessments (Healthy / Unhealthy / NotApplicable)
- Public exposure alerts card (public storage, public IPs, open NSGs)

### Report
- New major section "Azure Infrastructure" — covers all findings + subscription-level breakdown

---

## Phase 6 — Power BI Governance Pipeline
**Priority:** MEDIUM — common customer blindspot
**Effort:** 1-2 sessions, 5 tasks

### Permissions
- Power BI REST API scope: `https://analysis.windows.net/powerbi/api/.default`
- **Customer admin must enable** "Service principals can use read-only admin APIs" in Power BI admin portal
- Add `Tenant.Read.All` (Power BI — distinct from Graph)

### Backend
- New `PowerBiPipeline.cs` + `PowerBiRecommendations.cs`:
  - Workspaces: `GET /admin/workspaces/modified?$top=5000`
  - Reports per workspace
  - Datasets per workspace
  - Dataflows
  - Data gateways: `GET /admin/datagateways`
  - Activities (last 30d): `GET /admin/activityevents?startDateTime=...&endDateTime=...`
  - Capacity usage: `GET /admin/capacities`

### Insights computed
- Orphaned workspaces (no owner)
- Personal workspaces with shared content (risk)
- Datasets with no refresh
- Gateway health
- RLS (row-level security) coverage
- External sharing settings

### Portal
- New sub-tab "Power BI"
- Workspaces table (active vs orphaned)
- Capacity usage gauge
- Risk findings (external sharing, RLS gaps)

### Report
- New section "Power BI Governance"

---

## Phase 7 — Intune Deep Dive
**Priority:** MEDIUM — already partial, expand
**Effort:** 1 session, 3-4 tasks

### Backend
- Expand `EntraPipeline` or split into `IntunePipeline`:
  - Device compliance policies: `/deviceManagement/deviceCompliancePolicies`
  - Configuration profiles: `/deviceManagement/deviceConfigurations`
  - App protection policies: `/deviceAppManagement/iosManagedAppProtections` + android
  - Managed apps: `/deviceAppManagement/mobileApps`
  - Enrollment restrictions: `/deviceManagement/deviceEnrollmentConfigurations`
  - Autopilot deployment profiles: `/deviceManagement/windowsAutopilotDeploymentProfiles`
  - Compliance stats: `/deviceManagement/managedDeviceOverview` (already have partial)
- Compute: policy coverage gaps, BYOD vs corporate split, conditional access dependency

### Portal
- Sub-tab "Endpoint Management"
- Device compliance rate gauge
- Policy coverage matrix
- App protection status
- Autopilot readiness

### Report
- New section "Endpoint Security"

---

## Phase 8 — Compliance Manager + Framework Mapping
**Priority:** MEDIUM-HIGH — differentiator for regulated industries
**Effort:** 1-2 sessions

### Backend
- Graph permission: `ComplianceManager.Read.All`
- New `ComplianceManagerPipeline.cs`:
  - `GET /compliance/complianceScore` (if tenant licensed)
  - `GET /compliance/improvementActions`
  - `GET /compliance/assessments`
- Static framework seed data:
  - Map Copilot findings → HIPAA / ISO 27001 / NIST CSF / SOC 2 / PCI DSS controls
  - Compliance score per framework (% of controls addressed)

### Portal
- Sub-tab "Compliance"
- Per-framework score cards (HIPAA 67% / NIST 82% / ISO 45%)
- Control mapping table (click framework → shows which Kryoss checks map)

### Report
- New section "Regulatory Compliance" with per-framework breakdown

---

## Phase 9 — Mail Flow & Email Security
**Priority:** MEDIUM — fits existing D5 Zero Trust
**Effort:** 1 session, 2 tasks

### Backend
- DNS queries (no new API consent):
  - SPF record per verified domain
  - DKIM records (`selector1._domainkey.domain.com`, `selector2._domainkey`)
  - DMARC record (`_dmarc.domain.com`)
  - MTA-STS (`_mta-sts.domain.com`)
- Exchange Online deep:
  - `/users/{id}/mailFolders/inbox/messageRules` per VIP user (forwarding rules)
  - Transport rules (requires EXO PowerShell — may skip or mark as "requires admin portal check")
  - Shared mailboxes: `/users?$filter=assignedPlans/any(p:p/service eq 'exchange' and p/capabilityStatus eq 'Enabled')`

### Portal
- Add to existing M365 Security Checks tab (section for email)
- Domain-by-domain DNS status table

### Report
- Add subsection to D5 Zero Trust: "Email Authentication"

---

## Phase 10 — Benchmarks & MSP Differentiator
**Priority:** LOW until 5+ tenants scanned
**Effort:** 1 session

### Backend
- New endpoint `GET /v2/copilot-readiness/benchmark?organizationId={id}`
- Query aggregates across franchise (avg D1-D6 scores, avg overall)
- Optional industry seed data (static JSON per sector)

### Portal
- Comparison chart "Your tenant vs franchise avg"
- Percentile rank badge

### Report
- New page "Benchmark Analysis"

---

## No Rename — Parallel Architecture

Copilot Readiness stays as-is forever. Cloud Assessment is separate product with separate tables, separate service, separate tab, separate report type.

### Code reuse strategy

Some pipelines share logic (Entra, Defender, Purview, M365). Approach:
1. Start with duplication — copy EntraPipeline.cs → IdentityPipeline.cs, adapt for `cloud_assessment_findings` schema
2. Once 2+ pipelines duplicated, extract shared abstraction (e.g. `BasePipeline<TInsights>` or static helpers)
3. Refactor only after clear pattern emerges — don't pre-optimize

---

## Overall Execution Strategy — Cloud Assessment (parallel feature)

**Decision (2026-04-16):** Option 3 — new parallel feature. Copilot Readiness stays intact. Cloud Assessment built from scratch alongside.

### Build order

**Foundation first (1-2 sessions):**

- **Phase CA-0: Scaffold** — new DB schema, entities, service skeleton, orchestrator shell, first API endpoint (POST scan returning 202), portal placeholder tab. No pipelines yet, just bones. Ships immediately.

**MVP scope (3-5 sessions):**

- **Phase CA-1: Identity Pipeline** — port/reuse Entra logic from CopilotReadiness/EntraPipeline, adapt for new `cloud_assessment_findings` schema. Identity area score + findings.
- **Phase CA-2: Endpoint Pipeline** — Intune deep (compliance policies, config profiles, app protection, Autopilot) + Defender for Endpoint (reuse from CopilotReadiness). Endpoint score + findings.
- **Phase CA-3: Data Pipeline** — Purview + labels + DLP (reuse + expand). Data score + findings.
- **Phase CA-4: Productivity Pipeline** — M365 usage + adoption detail + licenses + wasted licenses (merge Phase 1 + 2 of original plan). Productivity + licenses.
- **Phase CA-5: Overview + Timeline** — overview page with area radar chart, timeline of scans, compare mode (merge Phase 3 of original plan).

**High-value expansion (2-3 sessions each):**

- **Phase CA-6: Azure Pipeline** — subscriptions + resources + Security Center + public exposure. NEW consent flow (ARM role assignment). Major feature.
- **Phase CA-7: Remediation Tracker** — cross-scan state persistence, status workflow (merge Phase 4 of original plan).
- **Phase CA-8: Compliance Manager + Framework Mapping** — framework scores, control mapping.

**Lower priority:**

- **Phase CA-9: Power BI Pipeline**
- **Phase CA-10: Mail Flow & Email Security** (can be merged into Identity or Data)
- **Phase CA-11: Benchmarks** — ✅ COMPLETE (2026-04-18). Franchise peer (≥5 orgs), industry baseline (15 NAICS codes × 5 employee bands), and global Kryoss (≥50 orgs) dimensions. Migration `038_cloud_assessment_benchmarks.sql` + `seed_038_industry_benchmarks.sql`. `BenchmarkService` computes per-scan comparison rows (percentiles, verdicts) after each scan; nightly `Benchmark_RefreshAggregates` timer rebuilds franchise/global rollups. 4 API endpoints in `BenchmarkFunction.cs` (`GET /benchmarks/{scanId}`, `GET /benchmarks/industries`, `PATCH /organizations/{id}/industry`, `GET /benchmarks/franchise-summary`). Portal tab "Benchmarks" (`BenchmarksTab.tsx` + `BenchmarkMetricRow.tsx` + `IndustryPicker.tsx` + inline franchise leaderboard) with radar overlay, availability cards, per-metric verdict pills. Self-contained HTML report via `GET /benchmarks/{scanId}/report` (`BenchmarkReportBuilder.cs`). Privacy: sample gates respected (FranchiseMinSample=5, GlobalMinSample=50), franchise `benchmark_opt_in` excludes data from global pool, per-org numbers never leave franchise.
- **Phase CA-12: Unified Cloud Experience** — ✅ COMPLETE (2026-04-18). 4 tracks: (1) CopilotReadiness endpoints deprecated (410 Gone, sunset 2026-05-18), Copilot Lens tab reads D1-D6 from CA scan entity, new `GET /v2/cloud-assessment/copilot-lens/{scanId}`. (2) `ConsentOrchestrator.cs` + `GET /v2/cloud-assessment/connection-status` (graph/azure/pbi state + percentage), portal `ConnectCloudWizard.tsx` stepper modal (3 steps: M365 → Azure → PBI with skip buttons), `ConnectionBanner` on CloudAssessmentPage. (3) "M365 / Cloud" tab removed from OrgDetail nav, `/m365` route 301-redirects to `/cloud-assessment`, single "Run Scan" button. (4) Data migration = archive approach — legacy `copilot_readiness_*` tables kept read-only, CA computes D1-D6 from live pipeline data, no SQL migration needed.

---

## Cloud Assessment — Remaining Gaps (post-CA-12)

### CA-13: Intune Deep (verify + fill gaps)
**Priority:** MEDIUM — likely partial from CA-2
**Effort:** 0-1 session (verify first, may already be done)

Audit CA-2 (Endpoint pipeline) output vs original Phase 7 (Intune Deep) spec. Check coverage of:
- Device compliance policies (`/deviceManagement/deviceCompliancePolicies`)
- Config profiles (`/deviceManagement/deviceConfigurations`)
- App protection policies iOS + Android (`/deviceAppManagement/iosManagedAppProtections`, `/androidManagedAppProtections`)
- Managed apps inventory (`/deviceAppManagement/mobileApps`)
- Enrollment restrictions (`/deviceManagement/deviceEnrollmentConfigurations`)
- Autopilot deployment profiles (`/deviceManagement/windowsAutopilotDeploymentProfiles`)

If all present: mark CA-13 closed, no work.
If gaps: add missing collectors + recommendations to EndpointPipeline (no new area, just enrich).

### CA-14: Auto-Consent (Fabric Admin API + Delegated ARM)
**Priority:** HIGH — huge UX win, cuts onboarding from 3 steps to 1 click
**Effort:** 2 sessions

Two tracks:

**Track A — Auto-enable Power BI:**
- Delegated OAuth flow with Fabric Admin scope (`https://api.fabric.microsoft.com/.default`)
- Customer Fabric/PowerBI Admin signs in delegated
- Backend `FabricAdminService` calls `POST /v1/admin/tenantsettings/ServicePrincipalAccess` to enable setting
- Add Kryoss SPN to allowed list via `PATCH /v1/admin/security/spn-allowlist` (or tenant setting body)
- Verify with app-only token afterward
- Store delegated refresh token encrypted (optional, for re-verify)
- Preview diff before apply, explicit user confirm

**Track B — Auto-assign Azure RBAC:**
- Delegated OAuth flow with ARM admin scope (user must be Owner/User Access Administrator on subscription)
- Backend calls `PUT /subscriptions/{id}/providers/Microsoft.Authorization/roleAssignments/{guid}?api-version=2022-04-01` with Reader role + Kryoss SPN objectId
- Shows list of subs user has access to, lets user pick which to grant
- Verify via existing ARM call

Result: ConnectCloudWizard Step 2 (Azure) + Step 3 (PBI) change from instructions + verify to single "Enable automatically (sign in as admin)" button.

Fallback: existing manual instructions stay as "Configure manually" link.

### CA-15: Drift Alerts + Notifications
**Priority:** HIGH — MSP retention tool, surfaces problems between scans
**Effort:** 1 session

Triggers:
- Overall score drops ≥0.5 vs previous scan
- New Critical finding detected
- New High priority finding in regulated framework (HIPAA/PCI)
- Copilot license utilization drops (potential license loss)
- Compliance framework score drops below threshold

Delivery:
- Email to franchise admin (SendGrid/Azure Comms Services)
- Optional: webhook POST to configurable URL (for PSA integration)
- Digest mode: weekly roll-up of all orgs under franchise

New table `cloud_assessment_alert_rules` — MSP configures thresholds per franchise.
New service `AlertService` — runs after each scan, compares vs previous, fires alerts.
Portal: Alerts config page under franchise settings.

### CA-16: PSA / Ticketing Integration
**Priority:** MEDIUM — MSP workflow tool
**Effort:** 2 sessions (per PSA)

Connectors:
- ConnectWise Manage (REST API, OAuth2)
- Autotask PSA (REST API, API keys)
- HaloPSA (REST API, JWT)
- Generic webhook (escape hatch)

On trigger (finding status → "in_progress" or alert fires):
- Create ticket in PSA with finding details
- Include remediation steps from finding
- Link back to Kryoss finding URL
- On ticket close in PSA: optional sync → status=resolved in Kryoss (requires webhook or polling)

Config per franchise: which PSA, credentials, default board/queue, priority mapping.

### CA-17: Automated Remediation Library
**Priority:** MEDIUM-LOW — safety critical, per-fix careful review
**Effort:** 1 session per fix handler (modular)

CA-7 scaffolded fix button. Per-finding fix handlers:
- Enable MFA for user (Graph PATCH `/users/{id}/authentication/methods`)
- Apply sensitivity label to file (Graph PATCH `/drives/{id}/items/{itemId}`)
- Disable legacy auth (CA policy create or modify)
- Block external forwarding (Exchange transport rule create — requires EXO PowerShell or Graph equivalent)
- Revoke risky OAuth grant (Graph DELETE `/oauth2PermissionGrants/{id}`)
- Enable DKIM for domain (Graph action)
- Enable customer lockbox
- Add SPF record (customer DNS — can't do, generate instructions)

Each handler:
1. Preview endpoint — shows exact API call + risks
2. Apply endpoint — executes, logs actlog, returns diff
3. Rollback instructions embedded in success response
4. Audit trail with user who approved

Ship one handler per session, reviewed carefully for safety.

### CA-18: Benchmark Periodic Refresh
**Priority:** LOW — needs production data first
**Effort:** 1 session

Current benchmark seed = static estimates. After 100+ tenants scanned:
- Quarterly job computes actual percentiles from real aggregate data
- Update `cloud_assessment_industry_benchmarks` seed with refreshed numbers
- Publish new percentiles to customers (email notification)
- Track seed version history

### CA-19: Graph Connectors + Copilot Knowledge Sources
**Priority:** LOW — Copilot-specific depth
**Effort:** 1 session

Expand CA-4 Productivity or add dedicated sub-area:
- Full Graph Connectors inventory (`/external/connections` + status)
- Connected website content
- Custom connectors deployed
- External files indexed
- Copilot ground truth sources mapping
- Findings: stale connectors, failing index jobs, excluded-from-index sensitive content

### CA-20: Real-time Audit Log Deep Dive
**Priority:** LOW — data density issue for small tenants
**Effort:** 1 session

M365 unified audit log (`/auditLogs/directoryAudits`, `/auditLogs/signIns`):
- Sign-in risk timeline (last 30d)
- Admin action audit
- Exported file/share events
- Impossible travel detections
- Mass download events
- Privileged role elevations

Requires `AuditLog.Read.All` (already consented). New view in Cloud Assessment.

---

## Non-Cloud Roadmap (Kryoss core platform)

### Agent & Endpoint (KryossAgent)

**Agent v1.6+ features:**
- [ ] CVE scanner integration (Defender vuln data already, but standalone CVE lookup)
- [ ] Patch compliance tracking (WSUS / WUfB / NinjaOne status per machine)
- [ ] Backup verification (Veeam / Acronis / NinjaOne backup status signal)
- [ ] BitLocker escrow verification (key backed up to AAD/AD)
- [ ] Firewall rules inventory (not just posture — full rule list)
- [ ] Scheduled tasks inventory (persistence detection)
- [ ] Browser extensions inventory (shadow IT detection)
- [ ] Chrome/Edge/Firefox saved passwords risk (password hygiene)

**Domain Controller scope (Phase 2 deferred per CLAUDE.md):**
- [ ] DC19/22/25 platform codes + control mapping (~100 DC-specific controls)
- [ ] AD schema/replication health checks
- [ ] FSMO role distribution audit
- [ ] GPO inventory + inheritance analysis
- [ ] DNS zone audit (stale records, secure dynamic updates)
- [ ] DFS namespace health
- [ ] Domain functional level recommendations

**Attestation module (Phase 6 deferred per CLAUDE.md):**
- [ ] Administrative/physical safeguards not measurable by agent
- [ ] Policy documentation upload + version control
- [ ] Evidence artifacts (screenshots, signed attestations)
- [ ] Per-control attestation workflow (text/file evidence)
- [ ] SOC 2 Type II evidence collection
- [ ] Change log / audit trail for attestations

### RMM / PSA Integrations

**NinjaRMM:**
- [ ] Deploy agent via Ninja automation (deploy script exists, not end-to-end tested)
- [ ] Pull Ninja device inventory → auto-enroll missing machines
- [ ] Sync Ninja custom fields (backup status, patch level) into Kryoss
- [ ] Ticket auto-create from Kryoss findings

**Intune:**
- [ ] Deploy agent via Intune Win32 app (script exists in Scripts/Deploy/)
- [ ] Pull Intune compliance data already (CA-2) — verify no gaps
- [ ] Bi-directional: apply Intune compliance policy from Kryoss finding fix

**ConnectWise Manage:**
- [ ] Ticket auto-create (feeds CA-16)
- [ ] Asset sync (Ninja already does this better, maybe skip)
- [ ] Contract/billing integration

### Reports & Deliverables

**Monthly Progress Report (deferred per reports-4-type spec):**
- [ ] Trend charts over 6 months
- [ ] Score delta per framework
- [ ] Findings resolved / introduced
- [ ] Executive narrative (auto-generated)
- [ ] Requires NinjaOne data integration for ticket/activity context

**Additional report types:**
- [ ] SOC 2 Type II evidence report (pull from attestations + scans)
- [ ] Incident response preparedness report
- [ ] Vendor risk report (per-vendor SaaS inventory + risk)
- [ ] Insurance renewal report (cyber insurance questionnaire answers)
- [ ] Board-level report (C-level exec summary, higher abstraction than current C-Level)

### Assessment & Controls

**Control catalog expansion:**
- [ ] Custom control builder (MSP defines own controls)
- [ ] Control exceptions workflow (approved exceptions with expiry)
- [ ] Mapping to customer's own policies (upload policy → auto-map to controls)
- [ ] CIS benchmark full implementation (currently partial via NIST overlap)

**Framework additions:**
- [ ] ISO 27001:2022 full (90+ Annex A controls, currently partial)
- [ ] NIST 800-171 (CMMC predecessor)
- [ ] PCI DSS 4.0 full (250+ sub-requirements)
- [ ] Essential 8 (ACSC, Australia-relevant)
- [ ] CIS Critical Security Controls v8 (18 controls)

### Security Additional

**External attack surface (pentest expansion):**
- [ ] Already have external scans table + pipeline — verify coverage
- [ ] Port scan public IPs (feeds CA-6 Azure public IP findings)
- [ ] TLS cert expiration monitoring (per domain)
- [ ] Leaked credentials check (HaveIBeenPwned API integration)
- [ ] Shadow IT discovery (via Defender for Cloud Apps — CA-2 partial)
- [ ] DNS record audit (subdomain takeover risk)

**Dark web / threat intel:**
- [ ] Breach notification monitoring (customer domain in breach dumps)
- [ ] Employee email exposure in paste sites
- [ ] Typosquat domain detection
- [ ] Brand impersonation monitoring

**Phishing & training:**
- [ ] Phishing simulation launch + tracking (needs email sending infra)
- [ ] Security awareness training completion status (LMS integration)
- [ ] Reported phishing analytics (what users flag)

### CMDB & Inventory

**Asset discovery:**
- [ ] SNMP already partial — expand to full network topology map
- [ ] Printer audit (firmware, public print, credential default)
- [ ] Network device inventory (switches, APs, firewalls, NAS)
- [ ] IoT device detection (Defender for IoT partial, CA-6 covers Azure IoT)
- [ ] Network topology visual (D3.js or similar)

**Software inventory (beyond M365):**
- [ ] Adobe Creative Cloud license utilization
- [ ] QuickBooks / accounting SaaS audit
- [ ] Zoom/Slack/Teams account audit
- [ ] Third-party SaaS discovery (Defender for Cloud Apps expansion)
- [ ] License optimizer across SaaS portfolio

### Platform / Admin

**Multi-franchise features:**
- [ ] Franchise hierarchy (parent franchise + child reseller)
- [ ] Cross-franchise benchmarks (global Kryoss already does this)
- [ ] White-label customization per franchise (Brand 2025 partial)
- [ ] Franchise-level RBAC (sub-admins with limited scope)

**Billing / monetization:**
- [ ] Service catalog (ServiceCatalogItem table exists — surface in UI)
- [ ] Franchise service rates (FranchiseServiceRate exists — billing engine)
- [ ] Usage-based billing (scans per month × rate)
- [ ] Stripe integration
- [ ] Invoice generation
- [ ] Revenue dashboard for franchise admin

**API / external:**
- [ ] Public API with API key auth (for customer scripting)
- [ ] Webhooks (CA-15 partial — expand to all events)
- [ ] OData query endpoint (for Power BI / Excel pivot)
- [ ] GraphQL endpoint (nice-to-have)

### Mobile / UX

- [ ] Mobile web view optimization (portal responsive for tablet, not phone)
- [ ] Native iOS/Android app (view scores, receive alerts, approve fixes)
- [ ] Slack/Teams bot (query score via chat, receive alerts)
- [ ] Browser extension (score badge per customer)

---

## Infrastructure Assessment (IA) — New Product Line

Separate from Cloud Assessment. Customer use case: MSP delivers infrastructure relevamiento for clients with:
- Hybrid on-premise + cloud
- Multi-site (incl. remote/industrial: yacimientos, factories, warehouses)
- Capacity planning + optimization
- Cloud migration readiness analysis

Client deliverable expectations:
- Arquitectura actual mapped (on-prem + cloud)
- Capacity, performance, availability metrics
- Site-to-site connectivity health (WAN, VPN, SD-WAN, ExpressRoute)
- Optimization opportunities with $ estimates

### IA-0: Scaffold
**Effort:** 1 session

New DB schema (`infra_assessment_*`):
- `infra_assessment_scans` (org_id, scope, status, overall_health, scan_date)
- `infra_assessment_sites` (scan_id, site_name, location, type: hq|branch|remote|industrial|datacenter|cloud, device_count, user_count, connectivity_type)
- `infra_assessment_devices` (scan_id, site_id, hostname, device_type: server|switch|router|firewall|ap|printer|ups|hvac|plc|iot, vendor, model, role)
- `infra_assessment_connectivity` (scan_id, site_a, site_b, link_type: mpls|sdwan|ipsec|expressroute|leased|internet|cellular|satellite, bandwidth_mbps, latency_ms, uptime_pct)
- `infra_assessment_capacity` (scan_id, device_id, metric_key, current_value, peak_value, threshold, trend_direction)
- `infra_assessment_findings` (same shape as CA findings)

New service `Services/InfraAssessment/`.
New portal tab "Infrastructure Assessment".
New report type `infra-assessment`.

### IA-1: Server & Hypervisor Inventory
**Effort:** 2 sessions

Expand agent (KryossAgent) to collect hypervisor detection + VM inventory:
- VMware vCenter API integration (separate service principal / credentials)
- Hyper-V host enumeration (via agent on host)
- Proxmox detection (via API if present)
- Per-VM: CPU cores, RAM allocated, disk size, OS, power state, snapshot count, last backup
- Host resource utilization (CPU/RAM/storage allocation vs available)
- VM consolidation opportunities (idle VMs, over-provisioned resources)

New agent module: `KryossAgent.Hypervisor` — runs only if agent installed on hypervisor host.

### IA-2: Network Topology Discovery
**Effort:** 2 sessions

Expand SNMP collector (already partial):
- Full L2/L3 topology map via CDP/LLDP neighbor discovery
- Per-switch: interface utilization, error rates, VLAN config, STP topology
- Wireless: AP inventory, channel utilization, client associations, signal strength
- Firewall rules (where accessible via SNMP/NetConf/SSH API)
- Router config snapshot (read-only)

Visual: D3.js topology map in portal, drill-down per device.

Protocols supported:
- Cisco IOS/NX-OS (SSH + show commands via netmiko or python library ported to C#)
- Fortinet FortiOS (REST API)
- Palo Alto PAN-OS (REST API)
- pfSense/OPNsense (REST API)
- MikroTik RouterOS (REST API)
- SNMP v2c/v3 generic fallback

### IA-3: WAN & Site Connectivity Health
**Effort:** 2 sessions

Per-site connectivity assessment:
- Active probes from agent installed at each site
- Ping/traceroute to HQ + cloud endpoints
- Bandwidth utilization (polling switch interfaces via SNMP)
- Latency/jitter/packet loss over time
- DNS resolution time
- MTU/MSS discovery
- Path MTU to Microsoft endpoints (M365/Azure)

Per-link:
- Type classification (MPLS/SD-WAN/IPsec/ExpressRoute/Internet/Cellular/Satellite)
- Uptime SLA measured
- Cost per link (MSP inputs monthly)
- Utilization vs provisioned bandwidth
- Redundancy check (primary + backup link health)

Findings:
- Underutilized expensive links (MPLS 90% idle)
- Congested cheap links (Internet saturated during peak)
- Asymmetric routing
- Single-path risk (no redundancy)
- Satellite/cellular at remote sites (yacimientos) — latency/cost flags

### IA-4: Cloud Connectivity Audit
**Effort:** 1 session

Integrates with CA-6 Azure data + new collectors:
- ExpressRoute circuits (per-subscription, already partial via ARM)
- Virtual WAN hubs
- VPN Gateway sites (Site-to-Site + Point-to-Site)
- Network Watcher connection monitoring results
- NVA (Network Virtual Appliance) inventory

Per-connection:
- Throughput (Azure metrics)
- Circuit utilization
- BGP peering status
- Reachability to critical Azure services (SQL, Storage, Copilot endpoints)

AWS / GCP support (future IA phase).

### IA-5: Capacity Planning & Trends
**Effort:** 1 session

Time-series data from agent + SNMP:
- CPU/RAM/disk trends 90d per device
- Growth rate projection (linear regression)
- Hit date prediction (when disk full, when RAM saturated)
- Server consolidation score (idle VMs, over-provisioned)
- License utilization (M365 already in CA, expand to SQL Server, Windows Server, VMware, Citrix)

Report section: "Capacity Roadmap" with 6/12/18-month projections.

### IA-6: Collaboration & Unified Comms Audit
**Effort:** 1 session

Microinformática / colaboración scope:
- Teams Rooms inventory (devices, firmware, last meeting date) — Graph API partial
- Phone system config (Teams Calling, PSTN, auto-attendants)
- Meeting quality metrics (Call Quality Dashboard if accessible)
- Video conferencing endpoints (Poly, Cisco, Zoom Rooms)
- Collaboration app sprawl (Teams + Slack + Zoom duplication detection)

### IA-7: OT / Industrial Network Detection
**Effort:** 1-2 sessions

For yacimientos, factories, warehouses:
- Passive OT device discovery (ignoring IPs that respond to M365 probes = IT only)
- SCADA/PLC protocol detection (Modbus TCP 502, DNP3 20000, Ethernet/IP 44818, Siemens S7)
- Integration with Defender for IoT (CA-6 can enrich)
- Air-gapped network detection (hosts reachable only from specific jump boxes)
- Firmware version inventory (limited — most OT vendors don't expose via standard protocols)

Findings:
- OT on flat network (no segmentation from IT) = High risk
- Legacy protocols on internet-exposed interfaces = Critical
- Default credentials detected = Critical (via OT-safe probe)

### IA-8: Cloud Migration Readiness
**Effort:** 2 sessions

Per on-prem workload:
- Workload classification (web app, database, file share, print, domain controller, etc.)
- Cloud compatibility score (0-100)
- Target recommendation (lift-and-shift VM, PaaS replacement, SaaS replacement, refactor, retire)
- Azure/AWS/GCP cost estimate (per target, 3-year TCO)
- Dependency mapping (which workloads must move together)
- Migration complexity (low/med/high based on: dependencies, age, licensing, data volume)

Output: "Migration Wave" plan — recommended sequencing to move workloads in groups.

### IA-9: Availability & SLA Tracking
**Effort:** 1 session

Per-service uptime measurement:
- Active probes from agent (HTTP/TCP/ICMP checks)
- Historical uptime %
- MTTR / MTBF calculations
- Service dependency tree (if X down, what breaks)
- SLA compliance vs contractual targets

### IA-10: Infrastructure Assessment Report
**Effort:** 1 session

Dedicated report type `infra-assessment`. Sections:
1. Executive summary (health score, top risks, top opportunities $)
2. Arquitectura actual (on-prem + cloud + sites map)
3. Capacity analysis (current utilization, trends, 12-month projections)
4. Performance analysis (latency, throughput, error rates)
5. Availability analysis (uptime per service, SLA compliance)
6. Connectivity health (per-link analysis, redundancy gaps)
7. OT/Industrial networks (if applicable)
8. Cloud migration roadmap (wave plan, cost model)
9. Optimization recommendations ($ saved / performance gained)
10. Methodology

Deliverable matches customer agenda items:
- Contexto y objetivos → Cover + Exec summary
- Relevamiento arquitectura actual → Sections 2-6
- Oportunidades de mejora → Section 9
- Próximos pasos → Embedded in each section + final roadmap section

---

## Priority Matrix (2026-04-20) — Updated

| Tier | Features |
|------|----------|
| **P0** (next 1-3 sessions) | CA-13 (Intune verify), CA-14 (auto-consent), CA-15 (drift alerts), IA-0 (Infra scaffold) — unlock client meeting |
| **P1** (month 1) | IA-1 + IA-2 + IA-3 (server inv + topology + WAN health — core of client deliverable), CA-16 (ConnectWise PSA), DC scope (Phase 2) |
| **P2** (month 2-3) | IA-4 + IA-5 + IA-10 (cloud connectivity + capacity + report), CA-17 per-fix handlers, Monthly Progress report, Attestation basic |
| **P3** (month 3+) | IA-6 + IA-7 + IA-8 + IA-9 (collab + OT + migration + SLA), Phishing, dark web, multi-franchise |
| **Icebox** | Brand impersonation, mobile app, GraphQL, AWS/GCP migration, Slack/Teams bot |

**Rationale:** Customer meeting (60 min agenda) drives infrastructure assessment demand. IA-0 through IA-3 cover bulk of agenda ("arquitectura actual" + "relevamiento" + "conectividad entre sitios"). IA-10 report ships deliverable format matching their agenda sections.

### Sales demo path

To show prospect: run Cloud Assessment once → show Overview page with all area scores + wasted licenses $ savings + top findings. Compelling single-screen MSP pitch.

To show progress: Phase CA-5 timeline chart — "score went from X to Y after we fixed Z".

## Execution via subagent-driven development

Each phase = own brainstorm session (if unclear scope) OR direct plan writing (if clear) → subagent-driven execution.

Reuse patterns established:
- SQL migration numbered
- EF entities in `Data/Entities/`
- Pipeline in `Services/CopilotReadiness/Pipelines/` (or new folder `Services/CloudAssessment/Pipelines/`)
- Recommendations in `Services/*/Recommendations/`
- Function in `Functions/Portal/`
- Portal tab in `components/org-detail/` + hook in `api/`
- Report section added to existing report builder

---

## To resume in new session

Read this file + CLAUDE.md + existing specs:
- `docs/superpowers/specs/2026-04-16-copilot-readiness-assessment-design.md`
- `docs/superpowers/plans/2026-04-16-copilot-readiness-assessment.md`

Then pick Phase 1. Brainstorm if scope unclear, else go direct to implementation plan.

**Recommended start command in new session:**
> "Implement Phase CA-0 (Cloud Assessment Scaffold) from `docs/superpowers/plans/2026-04-16-cloud-assessment-platform.md`. This is a NEW parallel feature alongside Copilot Readiness — do NOT modify existing Copilot Readiness code. Use subagent-driven development. After Phase CA-0 foundation ships, continue with CA-1, CA-2, etc in order."

### Reference files to read in new session

- `CLAUDE.md` (master)
- `KryossApi/CLAUDE.md` (backend map)
- `docs/superpowers/specs/2026-04-16-copilot-readiness-assessment-design.md` (pattern reference)
- `docs/superpowers/plans/2026-04-16-copilot-readiness-assessment.md` (previous plan — patterns to reuse)
- This file (`2026-04-16-cloud-assessment-platform.md`)

### Key files for code pattern reuse

- `KryossApi/src/KryossApi/Services/CopilotReadiness/CopilotReadinessService.cs` — orchestrator pattern (background Task + scope factory)
- `KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/EntraPipeline.cs` — pipeline pattern
- `KryossApi/src/KryossApi/Functions/Portal/CopilotReadinessFunction.cs` — endpoint pattern (auth + org access check + actlog + try/catch)
- `KryossApi/src/KryossApi/Data/KryossDbContext.cs` — EF config pattern with `HasColumnName` snake_case mappings
