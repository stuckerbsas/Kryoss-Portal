# Cloud Assessment Platform — Master Plan

> **Context:** Extending Copilot Readiness Assessment (shipped 2026-04-16) into a full Microsoft Cloud Assessment platform. Each phase ships independently, no big refactor. Each phase = new pipeline + new tab + new report section + SQL migration.

**Status:** Copilot Readiness feature complete and deployed. 6 pipelines (Entra, Defender, M365, Purview, Power Platform, SharePoint Deep), 169 service plan checks, bilingual report, portal dashboard.

**Architecture strategy:** Option 2 (incremental). Keep `CopilotReadinessService` as specialized slice. Add new area-specific pipelines independently. Each ships without blocking others.

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

## Rename Consideration

Phase 1-4 keep `copilot_readiness_*` table names (existing infra).

For Phase 5+ expanding beyond Copilot:
- Option A: New `cloud_assessment_*` tables parallel to existing
- Option B: Rename `copilot_readiness_*` → `cloud_assessment_*` (migration) + keep Copilot slice as a "dimension"
- **Decision:** Defer rename until Phase 5. Start Phase 5 with new table names (`cloud_assessment_azure_*`, `cloud_assessment_findings` as superset). Gradually migrate.

---

## Overall Execution Strategy

1. **Phase 1 first** (data ready, high value, low risk)
2. **Phase 2** parallel or next (sales tool)
3. **Phase 3 + 4** sequential (tracking + workflow)
4. **Phase 5** major — separate new session, 2-3 subsessions
5. **Phase 6-10** on demand, prioritize by client asks

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
> "Implement Phase 1 (Licenses & Adoption Detail) from `docs/superpowers/plans/2026-04-16-cloud-assessment-platform.md`. Use subagent-driven development."
