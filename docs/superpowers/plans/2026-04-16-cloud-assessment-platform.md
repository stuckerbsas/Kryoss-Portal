# Cloud Assessment Platform — Master Plan

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
- **Phase CA-11: Benchmarks**

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
