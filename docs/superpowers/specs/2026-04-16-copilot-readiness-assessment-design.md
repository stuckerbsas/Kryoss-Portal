# Copilot Readiness Assessment — Design Spec

**Date:** 2026-04-16
**Author:** Federico + Claude
**Status:** Draft
**Depends on:** M365 Phase 4 (consent flow, m365_tenants, M365ScannerService)

---

## 1. Overview

Full reimplementation of Microsoft's Copilot Readiness Assessment tool as a native Kryoss feature. Scans a customer's M365 tenant across 6 dimensions, scores readiness on a 1-5 scale, and generates a unified M365 report combining the existing 50 security checks with a comprehensive Copilot readiness analysis.

**Value proposition for MSPs:** Sell Copilot deployment services by showing customers exactly what infrastructure gaps they need to fix first. Works even without Copilot licenses — the assessment IS the sales tool.

---

## 2. Architecture

### Approach: Hybrid Single Service with Parallel Pipelines

Single `CopilotReadinessService` in backend. One Azure Function endpoint triggers the full scan. Internally, 6 pipelines run in parallel via `Task.WhenAll`. Results stored in new DB tables. Scoring computed server-side. Report builder in a separate static class.

```
CopilotReadinessService.RunScanAsync(orgId, tenantId)
├── Task.WhenAll(
│   ScanEntraPipeline(credential),
│   ScanDefenderPipeline(credential),
│   ScanM365Pipeline(credential),
│   ScanPurviewPipeline(credential),
│   ScanPowerPlatformPipeline(credential),
│   ScanSharePointDeepPipeline(credential)
│ )
├── ScoringEngine.ComputeScores(rawData)  → D1-D6 + overall + verdict
└── PersistResultsAsync(scan, scores, findings, details)
```

Each pipeline returns a `PipelineResult` with `findings[]`, `metrics{}`, `status` (ok/failed/partial). Failures caught per-pipeline — partial results persisted with `pipeline_status` JSON.

**Why not Durable Functions:** func-kryoss runs on App Service plan (30-min timeout). `Task.WhenAll` gives parallel speed without new infrastructure. If a pipeline fails, catch individually and persist partial results.

### Token Acquisition

Same app registration (M365Config.ClientId + M365Config.ClientSecret). Three token scopes for three API surfaces:

```csharp
// Graph API
var graphToken = credential.GetTokenAsync(new TokenRequestContext(
    new[] { "https://graph.microsoft.com/.default" }));

// Defender API (Microsoft Threat Protection + WindowsDefenderATP)
var defenderToken = credential.GetTokenAsync(new TokenRequestContext(
    new[] { "https://api.security.microsoft.com/.default" }));

// Power Platform API
var ppToken = credential.GetTokenAsync(new TokenRequestContext(
    new[] { "https://api.bap.microsoft.com/.default" }));
```

Same credential, different scopes. Customer admin consents to all three at once during initial consent flow.

### Graceful Degradation

If a specific API scope returns 403 (not consented or not provisioned):
- Pipeline marks status in `pipeline_status` JSON (e.g., `"defender_endpoint": "no_consent"`)
- Affected dimension scores computed from available data only
- Portal shows banner: "Defender deep scan unavailable — re-consent required"
- Report notes which pipelines ran and which were unavailable

---

## 3. Database Schema

### 3.1 copilot_readiness_scans

One row per scan. Stores dimension scores and overall verdict.

```sql
CREATE TABLE copilot_readiness_scans (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id     UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    tenant_id           UNIQUEIDENTIFIER NOT NULL REFERENCES m365_tenants(id) ON DELETE CASCADE,
    status              NVARCHAR(20)     NOT NULL DEFAULT 'running',  -- running|completed|partial|failed
    d1_score            DECIMAL(3,2),    -- 1.00-5.00
    d2_score            DECIMAL(3,2),
    d3_score            DECIMAL(3,2),
    d4_score            DECIMAL(3,2),
    d5_score            DECIMAL(3,2),
    d6_score            DECIMAL(3,2),
    overall_score       DECIMAL(3,2),    -- weighted formula
    verdict             NVARCHAR(20),    -- Ready|Nearly Ready|Not Ready
    pipeline_status     NVARCHAR(MAX),   -- JSON: {"entra":"ok","defender":"ok","purview":"failed:timeout",...}
    started_at          DATETIME2        NOT NULL,
    completed_at        DATETIME2,
    created_at          DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_copilot_scans_org_date
    ON copilot_readiness_scans(organization_id, created_at DESC);
```

### 3.2 copilot_readiness_metrics

Per-dimension raw metrics that fed the score. Used for trend analysis and report detail.

```sql
CREATE TABLE copilot_readiness_metrics (
    id                  BIGINT IDENTITY PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    dimension           NVARCHAR(10)     NOT NULL,  -- D1..D6
    metric_key          NVARCHAR(100)    NOT NULL,  -- e.g. "label_coverage_pct", "overshared_pct"
    metric_value        NVARCHAR(500)    NOT NULL,
    created_at          DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_copilot_metrics_scan ON copilot_readiness_metrics(scan_id);
```

### 3.3 copilot_readiness_findings

License/service plan analysis results. 169 possible checks across 6 service categories.

```sql
CREATE TABLE copilot_readiness_findings (
    id                  BIGINT IDENTITY PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    service             NVARCHAR(30)     NOT NULL,  -- entra|defender|purview|m365|power_platform|copilot_studio
    feature             NVARCHAR(200)    NOT NULL,  -- service plan friendly name
    status              NVARCHAR(30)     NOT NULL,  -- Critical|Action Required|Warning|Success|Disabled|PendingActivation|Insight|Not Licensed|Permission Required|Missing Prerequisite|PendingInput|PendingProvisioning
    priority            NVARCHAR(10)     NOT NULL DEFAULT '',  -- High|Medium|Low|""
    observation         NVARCHAR(MAX),
    recommendation      NVARCHAR(MAX),
    link_text           NVARCHAR(500),
    link_url            NVARCHAR(500),
    created_at          DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_copilot_findings_scan_svc ON copilot_readiness_findings(scan_id, service);
```

### 3.4 copilot_readiness_sharepoint

SharePoint site-level detail for D1 (label coverage) and D2 (oversharing).

```sql
CREATE TABLE copilot_readiness_sharepoint (
    id                  BIGINT IDENTITY PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    site_url            NVARCHAR(500)    NOT NULL,
    site_title          NVARCHAR(500),
    total_files         INT              NOT NULL DEFAULT 0,
    labeled_files       INT              NOT NULL DEFAULT 0,
    overshared_files    INT              NOT NULL DEFAULT 0,
    risk_level          NVARCHAR(10),    -- High|Medium|Low
    top_labels          NVARCHAR(MAX),   -- JSON array
    created_at          DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_copilot_sp_scan ON copilot_readiness_sharepoint(scan_id);
```

### 3.5 copilot_readiness_external_users

External/guest user detail for D3.

```sql
CREATE TABLE copilot_readiness_external_users (
    id                  BIGINT IDENTITY PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    user_principal      NVARCHAR(500)    NOT NULL,
    display_name        NVARCHAR(500),
    email_domain        NVARCHAR(200),
    last_sign_in        DATETIME2,
    risk_level          NVARCHAR(10),    -- High|Medium|Low
    sites_accessed      INT              NOT NULL DEFAULT 0,
    highest_permission  NVARCHAR(50),
    created_at          DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_copilot_ext_scan ON copilot_readiness_external_users(scan_id);
```

---

## 4. Scoring Engine

Ported 1:1 from the original Python `build_report.py`. Static class `ScoringEngine.cs`.

### 4.1 Dimension Formulas

**D1 — Information Protection (Weight: 25%)**
- Input: `label_coverage_pct` from SharePoint deep scan
- `>= 80%` → 5, `>= 60%` → 3, `>= 40%` → 2, `< 40%` → 1

**D2 — Data Sharing & Oversharing (Weight: 25%)**
- Input: `overshared_pct` = overshared items / total docs × 100
- `< 5%` → 5, `< 10%` → 3, `< 20%` → 2, `>= 20%` → 1

**D3 — External User Access (Weight: 20%)**
- Input: `high_risk_external_users`, `pending_invitations`
- `0 high + 0 pending` → 5, `0 high + <10 pending` → 4, `<10 high` → 3, `<50 high` → 2, `>=50 high` → 1

**D4 — Conditional Access (Weight: 15%)**
- Input: `ca_average_compat_score` (0-100%)
- `>= 90%` → 5, `>= 75%` → 4, `>= 60%` → 3, `>= 40%` → 2, `< 40%` → 1

**D5 — Zero Trust / Entra + Defender (Weight: 10%)**
- Input: Entra "Action Required"/"Warning" with Priority "High"/"Medium" → `n_entra`; Defender "Critical" → `n_def_crit` (×2); Defender "Warning"/"Action Required" with Priority "High"/"Medium" → `n_def_warn`
- Composite: `n5 = n_entra + (n_def_crit × 2) + n_def_warn`
- `0` → 5, `<= 2` → 4, `<= 5` → 3, `<= 8` → 2, `> 8` → 1

**D6 — Compliance & Governance / Purview (Weight: 5%)**
- Input: Purview findings with Status in [Disabled, Action Required, Warning] AND Priority == "High"
- `0` → 5, `<= 2` → 4, `<= 5` → 3, `<= 8` → 2, `> 8` → 1

### 4.2 Overall Score

```
overall = D1×0.25 + D2×0.25 + D3×0.20 + D4×0.15 + D5×0.10 + D6×0.05
```

### 4.3 Verdict

- `>= 4.0` → "Ready"
- `>= 3.0` → "Nearly Ready"
- `< 3.0` → "Not Ready"

---

## 5. Service Plan Mapping

Static dictionary mapping ~169 Microsoft service plan names to 6 categories. Ported from Python `SERVICE_PLAN_MAPPING`.

| Category | Count | Examples |
|---|---|---|
| entra | 14 | AAD_PREMIUM, AAD_PREMIUM_P2, MFA_PREMIUM, INTUNE_A, ENTRA_INTERNET_ACCESS, ENTRA_PRIVATE_ACCESS |
| defender | 17 | WINDEFATP, ATP_ENTERPRISE, MTP, ATA, ADALLOM_S_O365, SAFEDOCS, EOP_ENTERPRISE_PREMIUM |
| purview | 36 | AIP_P1, AIP_P2, COMMUNICATIONS_COMPLIANCE, EDISCOVERY, INSIDER_RISK_MANAGEMENT, M365_ADVANCED_AUDITING, RECORDS_MANAGEMENT, DLP, PREMIUM_ENCRYPTION, CUSTOMER_KEY |
| m365 | 85+ | EXCHANGE_S_ENTERPRISE, TEAMS1, SHAREPOINTENTERPRISE, ONEDRIVE_BASIC_P2, OFFICESUBSCRIPTION, M365_COPILOT_BUSINESS_CHAT, M365_COPILOT_APPS, MICROSOFT_LOOP, VIVA_INSIGHTS |
| power_platform | 17 | FLOW_O365_P3, POWERAPPS_O365_P3, CDS_O365_P1, BI_AZURE_P2, AI_BUILDER_MODELS, DLP_GOVERNANCE |
| copilot_studio | 11 | POWER_VIRTUAL_AGENTS, CDS_VIRTUAL_AGENT_USL, COPILOT_STUDIO_IN_COPILOT_FOR_M365, VIRTUAL_AGENT_BASE_MESSAGES |

---

## 6. Recommendation Engine

Each service plan has a recommendation handler. Two types:

### 6.1 License-Only Checks

Most plans (~120): check `provisioningStatus`. If "Success" → green observation. If not → recommendation to enable/purchase with appropriate priority.

### 6.2 Enriched Checks

~50 plans: license check PLUS live API data generates 2-10 sub-observations per feature.

**Entra enrichments:**
- CA policy coverage (total/enabled/MFA-requiring/Copilot-targeting/legacy-auth-blocking)
- MFA registration rate (thresholds: <50% High, <90% Medium, >=90% Success)
- Passwordless adoption (<10% action, <50% medium, >=50% success)
- PIM: permanent vs eligible Global Admins (role template `62e90394-69f5-4237-9190-012177145e10`)
- Access Reviews: active, recurring, scope (group/role/guest)
- Device compliance rate from Intune
- Guest users with licenses, B2B invite settings
- OAuth app consent settings, risky app permissions (high-privilege, unverified publishers)
- Sign-in log analysis (legacy auth, failed, risky sign-ins)
- Global Secure Access: filtering policies, forwarding profiles, private access connectors (beta endpoints)

**Defender enrichments:**
- Secure Score (current/max/%)
- Active incidents (total, high-severity)
- Device risk distribution (High/Medium/Low)
- Email threats (phishing, malware, spam)
- OAuth app risk (high-risk, over-privileged)
- Exposure score → risk level (0-30 Low, 30-60 Medium, 60+ High)
- 4 KQL advanced hunting queries:
  1. Copilot process events (DeviceProcessEvents, 30d, copilot/bing/edge keywords)
  2. Copilot network events (DeviceNetworkEvents, 30d, connections to openai/copilot/bing)
  3. Copilot file access (DeviceFileEvents, 30d, sensitivity-labeled Office files)
  4. Copilot email threats (EmailEvents, 30d, AI/copilot-themed phishing)

**Purview enrichments:**
- DLP policies (total/enabled)
- Sensitivity labels + label policies
- Information barriers, Insider Risk, Communication Compliance policies
- eDiscovery cases (total/active)
- Audit log configuration (unified audit, admin audit)
- Customer Lockbox enabled
- IRM (Azure RMS licensing enabled)
- Retention labels

**M365 enrichments:**
- Usage reports: email activity, Teams activity, SharePoint usage, OneDrive usage, Office activations, active users (all 30-day period)
- Copilot license adoption rate (assigned vs purchased)
- Graph Connectors deployment (external connections)

**Power Platform enrichments:**
- Environments (by type: production/sandbox/developer)
- Flows (cloud vs desktop RPA, Copilot plugin candidates via HTTP triggers)
- Apps (canvas vs model-driven, Teams integration)
- Connections (premium/standard/custom, enterprise connectors: SAP, Salesforce, ServiceNow, SQL)
- AI models (by type, trained/published status)
- DLP policies (Copilot extensibility blockers: HTTP connector blocked, custom connectors blocked)
- Capacity (database/file/log usage %)
- Solutions (managed/unmanaged, ALM readiness)

### 6.3 Composite Assessments

Three cross-cutting assessments that aggregate data from multiple pipelines:

**Copilot Security Posture** — 10-factor:
1. Exposure score, 2. Critical recommendations, 3. Compromised identities, 4. Email threats, 5. OAuth app risks, 6. Advanced hunting (suspicious processes/phishing), 7. Active high-severity incidents, 8. Vulnerable software, 9. High-risk devices, 10. Purview DLP+labels
→ NOT READY / AT RISK / NEEDS IMPROVEMENT / READY

**Copilot Data Governance** — 7-factor:
DLP policies + Sensitivity labels + Information barriers + Insider risk policies + Communication compliance + Retention labels + Unified audit logging
→ UNPROTECTED / AT RISK / NEEDS IMPROVEMENT / PROTECTED

**Copilot Threat Intelligence** — 6-factor:
Suspicious process activity + Unusual network activity + Sensitive file access patterns + AI-themed phishing + Compromised accounts using Copilot + High-risk OAuth apps

---

## 7. API Endpoints

### 7.1 New Endpoints (CopilotReadinessFunction)

| Method | Route | Auth | Body/Params | Response |
|---|---|---|---|---|
| `POST` | `/v2/copilot-readiness/scan` | Bearer, `assessment:create` | `{organizationId}` | `{scanId, status:"running"}` |
| `GET` | `/v2/copilot-readiness?organizationId={guid}` | Bearer, `assessment:read` | — | Latest scan: scores, verdict, pipeline_status, findings summary by service |
| `GET` | `/v2/copilot-readiness/{scanId}` | Bearer, `assessment:read` | — | Full detail: all findings, metrics, SharePoint detail, external users |
| `GET` | `/v2/copilot-readiness/history?organizationId={guid}` | Bearer, `assessment:read` | — | Array of `{id, date, overall_score, verdict}` for trend chart |

### 7.2 Modified Endpoint

| Method | Route | Change |
|---|---|---|
| `GET` | `/v2/reports/org/{orgId}?type=m365&lang=es` | New `m365` report type. Loads M365 findings (50 checks) + latest copilot readiness scan. Generates unified HTML report. |

### 7.3 Timer Trigger

`CopilotReadinessTimerFunction` — CRON: `0 0 2 * * 0` (Sunday 02:00 UTC).
Queries all `m365_tenants` with `status='active'`, runs `CopilotReadinessService.RunScanAsync` for each. Sequential per tenant with 30s delay to avoid Graph throttling.

---

## 8. Graph API Permissions

### 8.1 Current Permissions (already consented, ~18)

Policy.Read.All, User.Read.All, UserAuthenticationMethod.Read.All, Directory.Read.All, MailboxSettings.Read, Domain.Read.All, Application.Read.All, AuditLog.Read.All, SecurityEvents.Read.All, IdentityRiskEvent.Read.All, IdentityRiskyUser.Read.All, DeviceManagementManagedDevices.Read.All, DeviceManagementConfiguration.Read.All, SecurityAlert.Read.All, Sites.Read.All, Organization.Read.All

### 8.2 Additional Graph Permissions Needed

| Permission | Pipeline | Purpose |
|---|---|---|
| Reports.Read.All | M365 | Usage reports (email, Teams, SharePoint, OneDrive, Office activations) |
| Group.Read.All | Entra | Group-based licensing, dynamic groups, Copilot groups |
| AccessReview.Read.All | Entra | Access review definitions |
| RoleManagement.Read.Directory | Entra | PIM role assignments, eligibility schedules |
| CrossTenantInformation.ReadBasic.All | Entra | Cross-tenant access policy |
| DelegatedPermissionGrant.Read.All | Entra | OAuth permission grants |
| NetworkAccessPolicy.Read.All | Entra | Global Secure Access (beta endpoints) |
| SecurityIncident.Read.All | Defender | Security incidents |
| SecurityActions.Read.All | Defender | Secure score control profiles |

### 8.3 Defender API Permissions (via "Microsoft Threat Protection")

Incident.Read.All, AdvancedHunting.Read.All

### 8.4 Defender Endpoint API Permissions (via "WindowsDefenderATP")

Machine.Read.All, Vulnerability.Read.All, Software.Read.All, SecurityRecommendation.Read.All, Score.Read.All

### 8.5 Power Platform API Permissions (via "PowerApps Service")

Available delegated/application permissions for environment, flow, app, connector, DLP, capacity, solution reads.

### 8.6 Consent Flow

No code change to consent callback. Add new permissions to existing app registration in Azure portal. Existing customers re-consent when prompted. New customers get full permission set on first consent.

---

## 9. Portal UI

### 9.1 M365 Tab Sub-Navigation

```
M365 Tab
├── Security Checks    (existing — 50 findings table + KPI cards)
└── Copilot Readiness  (new)
```

### 9.2 Copilot Readiness Sub-Tab Layout

**Not scanned state:** Big "Run your first Copilot Readiness Assessment" button.

**Running state:** Progress spinner. Poll `GET /v2/copilot-readiness?organizationId=X` every 10s until `status != "running"`.

**Completed state:**
1. **Header:** "Run Assessment" button + last scan timestamp
2. **Overall score gauge:** Large badge — color-coded (1-2 red, 3 amber, 4-5 green) + verdict text
3. **6 dimension cards:** D1-D6 score cards, color-coded, clickable (scroll to findings)
4. **Copilot license status banner:** Present/absent/adoption rate
5. **Trend chart:** Line graph of `overall_score` from scan history (4-8 data points)
6. **Findings accordion:** Grouped by service (Entra/Defender/M365/Purview/PP/CS). Each group shows count + status breakdown. Expand → full table with Feature, Status badge, Priority pill, Observation, Recommendation, Link.
7. **Pipeline status:** Row of status icons per pipeline (ok/partial/failed/no_consent)
8. **Export Report button:** Opens unified M365 report in new tab

### 9.3 No-License Scenario

Assessment runs fully regardless of Copilot licensing. If no Copilot licenses detected:
- Warning banner: "No Microsoft 365 Copilot licenses detected"
- Context text: "This assessment shows what your tenant needs BEFORE deploying Copilot"
- Missing Copilot licenses listed
- Infrastructure readiness scores still shown (D1-D4 are license-independent)
- Action count: "N actions to complete before Copilot"

### 9.4 New Portal Files

```
KryossPortal/src/
├── components/org-detail/
│   ├── M365Tab.tsx                  -- modified: add sub-tab navigation
│   └── CopilotReadinessTab.tsx      -- new: full sub-tab component
├── api/
│   └── copilotReadiness.ts          -- new: React Query hooks
```

---

## 10. Unified M365 Report

New report type `m365` in the report dropdown. Bilingual EN/ES via `?lang=` parameter (default: `es`).

### 10.1 Report Sections

1. **Cover Page** — Client name, tenant ID, scan date, overall Copilot Readiness score badge, MSP branding
2. **Executive Summary** — 6 KPI boxes, D1-D6 scorecard bars, verdict banner, Copilot license status
3. **M365 Security Posture** — Existing 50 checks: category breakdown, pass/fail/warn counts, top 10 critical findings, per-category detail tables
4. **D1 — Information Protection** — Label coverage bar, label distribution, top unlabeled sites, recommendations
5. **D2 — Data Sharing & Oversharing** — Oversharing %, top 10 sites by overshared items, risk breakdown, recommendations
6. **D3 — External User Access** — KPI boxes (total/high-risk/pending/inactive), high-risk user table, recommendations
7. **D4 — Conditional Access** — Per-policy analysis (MFA/device/block/Copilot compat score), missing recommended policies, recommendations
8. **D5+D6 — Zero Trust & Compliance** — Entra + Defender findings tables, 5 governance pillars, full Purview status, Power Platform + Copilot Studio findings, 3 composite posture assessments
9. **License Inventory** — All detected SKUs grouped by category, color-coded status
10. **Remediation Roadmap** — Phase 1 Critical (High priority), Phase 2 Recommended (Medium), Phase 3 Ongoing (Low + monitoring), success criteria per phase
11. **Methodology** — Scoring formula table, data sources, scan timestamp, pipeline status

### 10.2 Builder Location

`CopilotReadinessReportBuilder.cs` — separate static class. Called from `ReportService.GenerateOrgReportAsync` via new `"m365"` case in switch expression at line 223. Uses shared helpers (AppendHtmlHead, AppendPageHeader, AppendFooter, etc.).

### 10.3 Brand 2025 Styling

Same as existing reports: dark header (#3D4043), Montserrat font, A4 `@page` rules, franchise color injection, inline SVG logos. PASS=#008852, WARN=#D97706, FAIL=#C0392B.

---

## 11. Backend File Structure

```
KryossApi/src/KryossApi/
├── Functions/Portal/
│   ├── CopilotReadinessFunction.cs      -- 4 endpoints + timer trigger
│   └── ReportsFunction.cs               -- modified: add "m365" case
├── Services/
│   ├── CopilotReadiness/
│   │   ├── CopilotReadinessService.cs   -- orchestrator
│   │   ├── ScoringEngine.cs             -- D1-D6 formula
│   │   ├── ServicePlanMapping.cs        -- 169-plan dictionary
│   │   ├── CopilotReadinessReportBuilder.cs  -- HTML report
│   │   ├── Pipelines/
│   │   │   ├── EntraPipeline.cs
│   │   │   ├── DefenderPipeline.cs
│   │   │   ├── M365Pipeline.cs
│   │   │   ├── PurviewPipeline.cs
│   │   │   ├── PowerPlatformPipeline.cs
│   │   │   └── SharePointDeepPipeline.cs
│   │   └── Recommendations/
│   │       ├── EntraRecommendations.cs
│   │       ├── DefenderRecommendations.cs
│   │       ├── PurviewRecommendations.cs
│   │       ├── M365Recommendations.cs
│   │       ├── PowerPlatformRecommendations.cs
│   │       └── CopilotStudioRecommendations.cs
│   └── ReportService.cs                 -- modified: add "m365" dispatch
├── Models/
│   └── CopilotReadiness/
│       ├── PipelineResult.cs
│       ├── CopilotReadinessFinding.cs
│       └── CopilotReadinessScan.cs
└── sql/
    └── 029_copilot_readiness.sql        -- all 5 tables + indexes
```

---

## 12. Graph API Endpoints Called (Complete List)

### 12.1 Microsoft Graph v1.0 (via GraphServiceClient)

| # | Endpoint | Pipeline |
|---|---|---|
| 1 | `GET /organization` | Entra |
| 2 | `GET /subscribedSkus` | All (license analysis) |
| 3 | `GET /directoryRoles` | Entra |
| 4 | `GET /identity/conditionalAccess/policies` | Entra, D4 |
| 5 | `GET /reports/authenticationMethods/userRegistrationDetails` | Entra |
| 6 | `GET /identityProtection/riskyUsers` | Entra, Defender |
| 7 | `GET /identityProtection/riskDetections` | Entra |
| 8 | `GET /roleManagement/directory/roleAssignments` | Entra |
| 9 | `GET /roleManagement/directory/roleEligibilitySchedules` | Entra |
| 10 | `GET /roleManagement/directory/roleAssignmentSchedules` | Entra |
| 11 | `GET /identityGovernance/accessReviews/definitions` | Entra |
| 12 | `GET /deviceManagement/managedDevices` | Entra |
| 13 | `GET /deviceManagement/deviceCompliancePolicies` | Entra |
| 14 | `GET /groups?$filter=assignedLicenses/$count ne 0` | Entra |
| 15 | `GET /users?$filter=userType eq 'Guest'` | Entra, D3 |
| 16 | `GET /users?$top=999&$select=id,displayName,assignedLicenses,accountEnabled` | M365 |
| 17 | `GET /policies/crossTenantAccessPolicy` | Entra |
| 18 | `GET /servicePrincipals` | Entra |
| 19 | `GET /oauth2PermissionGrants` | Entra, Defender |
| 20 | `GET /policies/permissionGrantPolicies` | Entra |
| 21 | `GET /policies/authorizationPolicy` | Entra |
| 22 | `GET /auditLogs/signIns?$filter=createdDateTime ge {7d}` | Entra |
| 23 | `GET /security/alerts_v2` | Defender |
| 24 | `GET /security/incidents` | Defender |
| 25 | `GET /security/secureScores` | Defender |
| 26 | `GET /security/secureScoreControlProfiles` | Defender |
| 27 | `GET /sites` | SharePoint Deep, M365 |
| 28 | `GET /sites/{id}/drives/{id}/root/children` | SharePoint Deep (D1+D2). Paginated, max 200 items/page. Cap at 500 files per site, 50 sites max. Sample-based for large tenants. |
| 29 | `GET /sites/{id}/permissions` | SharePoint Deep (D2). Check sharing links per site. |
| 30 | `GET /reports/getEmailActivityUserDetail(period='D30')` | M365 |
| 31 | `GET /reports/getTeamsUserActivityUserDetail(period='D30')` | M365 |
| 32 | `GET /reports/getSharePointSiteUsageDetail(period='D30')` | M365 |
| 33 | `GET /reports/getOneDriveUsageAccountDetail(period='D30')` | M365 |
| 34 | `GET /reports/getOffice365ActivationsUserDetail` | M365 |
| 35 | `GET /reports/getOffice365ActiveUserDetail(period='D30')` | M365 |
| 36 | `GET /external/connections` | M365 (Graph Connectors) |

### 12.2 Microsoft Graph Beta (via HttpClient)

| # | Endpoint | Pipeline |
|---|---|---|
| 37 | `GET /beta/networkAccess/filteringPolicies` | Entra (GSA) |
| 38 | `GET /beta/networkAccess/forwardingProfiles` | Entra (GSA) |
| 39 | `GET /beta/networkAccess/connectivity/remoteNetworks` | Entra (GSA) |
| 40 | `GET /beta/networkAccess/connectivity/branches` | Entra (GSA) |

### 12.3 Microsoft 365 Defender API (api.security.microsoft.com)

| # | Endpoint | Pipeline |
|---|---|---|
| 41 | `GET /api/incidents` | Defender |
| 42 | `GET /api/machines` | Defender |
| 43 | `GET /api/vulnerabilities` | Defender |
| 44 | `POST /api/advancedhunting/run` (4 KQL queries) | Defender |
| 45 | `GET /api/EmailPostDeliveryDetections` | Defender |
| 46 | `GET /api/recommendations` | Defender |
| 47 | `GET /api/Software` | Defender |
| 48 | `GET /api/exposureScore` | Defender |

### 12.4 Power Platform APIs

| # | Endpoint | Pipeline |
|---|---|---|
| 49 | `GET /providers/Microsoft.BusinessAppPlatform/scopes/admin/environments` | Power Platform |
| 50 | `GET .../environments/{env}/connections` | Power Platform |
| 51 | `GET .../environments/{env}/aiModels` | Power Platform |
| 52 | `GET .../environments/{env}/dlpPolicies` | Power Platform |
| 53 | `GET .../environments/{env}/capacity` | Power Platform |
| 54 | `GET .../environments/{env}/solutions` | Power Platform |
| 55 | `GET .../environments/{env}/v2/flows` | Power Platform |
| 56 | `GET https://{env}.environment.api.powerplatform.com/powerapps/apps` | Power Platform |

---

## 13. Status Values & Priority

### 13.1 Finding Status (severity order)

1. **Critical** — immediate security risk
2. **Action Required** — feature exists but misconfigured
3. **Warning** — suboptimal configuration
4. **Missing Prerequisite** — dependency not met
5. **PendingInput** — needs user action to activate
6. **PendingActivation** — provisioning in progress
7. **PendingProvisioning** — provisioning queued
8. **Disabled** — explicitly disabled by admin
9. **Not Licensed** — plan not present in tenant
10. **Permission Required** — API returned 403
11. **Insight** — informational only
12. **Success** — feature is active and healthy

### 13.2 Priority

- **High** — security-critical, blocks Copilot readiness
- **Medium** — governance gap, recommended before Copilot
- **Low** — informational, nice-to-have

---

## 14. Scan Trigger

### 14.1 On-Demand

User clicks "Run Assessment" in portal → `POST /v2/copilot-readiness/scan`.
Function creates scan row (status=running), fires `RunScanAsync` on background thread, returns immediately with `{scanId, status:"running"}`.
Portal polls `GET /v2/copilot-readiness?organizationId=X` every 10s.

### 14.2 Weekly Scheduled

`CopilotReadinessTimerFunction` runs Sunday 02:00 UTC.
Iterates all active m365_tenants, runs scan per tenant.
Sequential with 30s delay between tenants to avoid Graph throttling.
Skips tenants whose last scan is < 5 days old (avoid duplicate if manual scan ran recently).

---

## 15. Bilingual Support

All user-facing text in string tables. Two dictionaries: `StringsEn` and `StringsEs`.

Covers:
- Report section titles and labels
- Dimension names and descriptions
- Status labels
- Recommendation templates (parameterized)
- Verdict text
- Portal labels (via i18n in React)

Default language: `es`. Override via `?lang=en` on report endpoint.

---

## 16. No-License Handling

Assessment runs fully regardless of Copilot licensing.

**No Copilot license:**
- Warning banner in portal + report cover
- "This assessment shows what your tenant needs BEFORE deploying Copilot"
- All 169 checks run — Copilot-specific plans show as "Not Licensed"
- D1-D4 scores unaffected (infrastructure checks)
- D5-D6 may score low (Defender/Purview features may be unlicensed too)
- Action count: "N actions to complete before Copilot"

**Copilot licensed, low adoption:**
- Adoption banner: "50 purchased, 12 assigned (24%)"
- Recommendation to assign remaining licenses

**No Defender/Purview licenses:**
- Affected pipeline runs but most plans return "Not Licensed"
- Score reflects reality — MSP uses this to sell full E5 stack
