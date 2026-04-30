# Unified Report System — 7-Type Compositional Architecture

**Date:** 2026-04-19
**Status:** Design approved, pending implementation
**Supersedes:** `2026-04-15-reports-4-type-baseline-design.md` (extends, does not delete)

---

## Problem

The current report system has grown organically into fragmented pieces:

- `ReportService.cs` handles 4 endpoint report types (C-Level, Technical, Preventas opener/detailed) as a monolithic switch-case (~2000+ lines)
- `CloudAssessmentReportService.cs` generates cloud reports separately
- `BenchmarkReportBuilder.cs` handles benchmark reports independently
- No business proposal generation exists
- No framework-specific compliance dashboard exists
- Monthly Progress is deferred pending NinjaOne (unnecessary — Kryoss has enough data for v1)
- Cloud and endpoint data never appear in the same report

This fragmentation means an MSP must generate 3-4 separate documents to tell a complete story to a single audience.

## Decision

Rebuild the report system using a **compositional block architecture**:

- **18 reusable content blocks** that each render one visual section
- **7 report recipes** that compose blocks into complete documents
- **1 unified data loader** that fetches all data once (endpoint + cloud + hygiene + benchmarks)
- **1 service catalog** for auto-generated remediation proposals with pricing

All 7 reports are org-scoped. Cloud sections appear automatically when M365/Azure is connected; omitted silently when not. Every report supports `?framework=` filter and `?lang=en|es`.

---

## The 7 Reports

| # | Type | Audience | Cycle Moment | Pages | Priority |
|---|------|----------|-------------|-------|----------|
| 1 | **C-Level** | CEO/COO/CFO | Operation / Renewal | 2-3 A4 | P1 |
| 2 | **Technical** | SysAdmin / IT Manager | Onboarding / Operation | 5-20 A4 | P1 |
| 3 | **Preventa Opener** | Prospect decision-maker | Prospecting | 2 A4 | P1 |
| 4 | **Preventa Detailed** | Prospect decision-maker + IT | Prospecting | 6-8 A4 | P1 |
| 5 | **Framework** | Compliance officer / IT Manager | Compliance review | 4-8 A4 | P1 |
| 6 | **Proposal** | Prospect / Existing client | Upsell / Renewal | 6-10 A4 | P1 |
| 7 | **Monthly** | Mixed exec + IT (MRR meeting) | Monthly operation | 4 A4 | P2 (last) |

---

## Architecture

### Folder Structure

```
Services/Reports/
├── IReportBlock.cs              — common block interface
├── ReportComposer.cs            — orchestrator: receives type → assembles blocks
├── ReportDataLoader.cs          — loads ALL data once (endpoint+cloud+hygiene+benchmarks)
├── ReportData.cs                — unified data model
├── ReportOptions.cs             — lang, framework, tone, branding
├── Blocks/
│   ├── CoverBlock.cs            — cover page (brand, org, date, report type)
│   ├── SemaforoBlock.cs         — survival traffic light + capital sins
│   ├── KpiBlock.cs              — 3 executive KPIs (cost, coverage, evolution)
│   ├── CtaBlock.cs              — executive decisions (12 auto-rules + manual)
│   ├── AssetMatrixBlock.cs      — machine-by-machine table, paginated
│   ├── TopFindingsBlock.cs      — top N critical findings cross-fleet
│   ├── IronSixBlock.cs          — los 6 de hierro (encryption, protocols, etc)
│   ├── FrameworkGaugeBlock.cs   — visual gauge per framework
│   ├── GapAnalysisBlock.cs      — failed controls grouped by framework
│   ├── CloudPostureBlock.cs     — 5-area radar + cloud findings integrated
│   ├── ScoreTrendBlock.cs       — monthly evolution sparkline
│   ├── DeltaBlock.cs            — resolved vs new findings this period
│   ├── RiskScoreBlock.cs        — aggressive score for pre-sales
│   ├── ThreatVectorsBlock.cs    — top compromise vectors
│   ├── MethodologyBlock.cs      — 90-day safe deprecation
│   ├── ServiceCatalogBlock.cs   — catalog + auto-calculated pricing
│   └── TimelineBlock.cs         — visual remediation roadmap
└── Recipes/
    ├── CLevelRecipe.cs
    ├── TechnicalRecipe.cs
    ├── PreventaOpenerRecipe.cs
    ├── PreventaDetailedRecipe.cs
    ├── MonthlyRecipe.cs
    ├── FrameworkRecipe.cs
    └── ProposalRecipe.cs
```

### Block Interface

```csharp
public interface IReportBlock
{
    string Render(ReportData data, ReportOptions options);
}
```

Each block returns an HTML string. Blocks are stateless — all state lives in `ReportData`.

### ReportData — Unified Data Model

Single object loaded once by `ReportDataLoader`, contains:

**Endpoint data:**
- `List<AssessmentRun> Runs` — latest run per machine
- `List<Machine> Machines` — full fleet with hardware fields
- `List<OrgControlResult> ControlResults` — all results across latest runs
- `List<AdHygiene> HygieneFindings` — LAPS, kerberoastable, privileged, pwd-never-expire
- `List<MachinePort> Ports` — open ports (RDP exposure detection)
- `List<MachineThreat> Threats` — active threats

**Cloud data (null when not connected):**
- `CloudAssessmentScan? CloudScan` — latest completed scan
- `List<CloudAssessmentFinding>? CloudFindings`
- `Dictionary<string, decimal>? AreaScores` — identity, endpoint, data, productivity, azure
- `List<CloudFrameworkScore>? CloudFrameworkScores`
- `BenchmarkData? Benchmarks` — franchise/industry/global comparisons

**Org context:**
- `Organization Org` — includes brand, franchise
- `decimal? PreviousMonthScore` — for delta calculation
- `List<ServiceCatalogItem> ServiceCatalog` — for proposal pricing
- `FranchiseServiceRate? Rate` — hourly rate + margin
- `List<ExecutiveCta> Ctas` — auto-detected + manual

### ReportOptions

```csharp
public record ReportOptions(
    string Lang = "en",           // en | es
    string? FrameworkCode = null,  // NIST, CIS, HIPAA, ISO27001, PCI-DSS
    string? Tone = null            // opener | detailed (preventa only)
);
```

### Recipe Pattern

Each recipe is a class that returns an ordered list of blocks:

```csharp
public class CLevelRecipe : IReportRecipe
{
    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock();
        yield return new SemaforoBlock();
        yield return new KpiBlock();
        if (data.CloudScan != null)
            yield return new CloudPostureBlock();
        yield return new CtaBlock();
    }
}
```

Cloud blocks conditionally included based on data availability.

---

## Recipe Definitions

### 1. C-Level

```
Cover → Semáforo → KPI → CloudPosture* → CTA
```

**Semáforo:** unchanged from current spec (4 capital sins → force RED, score-based fallback).

**KPI:** 3 business KPIs (Costo de Exposición, Cobertura de Activos "4 Fantásticos", Evolución del Riesgo). When cloud connected, adds 4th KPI: Cloud Security Score (overall area average).

**CloudPosture:** 5-area radar (identity/endpoint/data/productivity/azure) + top 3 critical cloud findings. Compact view — detail lives in Technical.

**CTA:** 12-rule auto-detection engine (unchanged from current spec) + manual CTAs. Max 2 shown.

### 2. Technical

```
Cover → AssetMatrix → TopFindings → IronSix → CloudPosture* → GapAnalysis
```

**AssetMatrix:** one row per machine, sorted worst-to-best. Columns: Hostname, OS Status, Critical Findings count, Agent Heartbeat, Individual Score.

**TopFindings:** top 10 critical/high findings cross-fleet with remediation text and affected machine count.

**IronSix:** 6 thematic sub-lists (Cifrado, Protocolos, Hardware, Identidad, Higiene, Endpoint). Zero-state shows checkmark.

**CloudPosture (full):** detailed radar + all findings grouped by area + recommendations. Technical audience gets the complete picture.

**GapAnalysis:** controls failing grouped by framework, with remediation instructions. Only appears when framework filter is applied, or shows all frameworks in summary mode.

### 3. Preventa Opener

```
Cover → RiskScore → ThreatVectors → Methodology
```

Aggressive 2-page door opener. Unchanged content from current `presales-opener`. RiskScore uses fear-based framing. ThreatVectors shows top 4 compromise paths. Methodology introduces the 90-day safe deprecation approach.

When cloud data available: RiskScore incorporates cloud findings into the narrative (e.g., "admin accounts without MFA" as a vector).

### 4. Preventa Detailed

```
Cover → RiskScore → ThreatVectors → TopFindings → Methodology → ServiceCatalog → Timeline
```

6-8 page proposal. Extends Opener with evidence (TopFindings), service catalog preview (what we'd fix), and timeline (when). The ServiceCatalog block here shows categories + hours but NOT pricing — that's the Proposal report's job. This is "what's wrong and what we'd do", not "how much it costs".

### 5. Monthly (P2 — v1 with Kryoss data only)

```
Cover → ScoreTrend → Delta → TopFindings(resolved+new) → CloudPosture* → KPI
```

**ScoreTrend:** sparkline of org-average score over last 6 months. Data source: `assessment_runs` grouped by month.

**Delta:** side-by-side comparison — findings resolved since last month vs new findings appeared. Grouped by severity.

**TopFindings variant:** split into two sub-sections: "Resolved This Month" (green, celebration) and "New This Month" (red, attention needed).

**KPI:** same 3 KPIs as C-Level but with month-over-month deltas emphasized.

NinjaOne integration later adds: patches installed, scripts executed, engineering hours, uptime tracking. These become additional blocks injected into the recipe without changing existing blocks.

### 6. Framework

```
Cover → FrameworkGauge → GapAnalysis → CloudPosture(filtered)* → Timeline
```

**Framework parameter is required** for this report type. API returns 400 if `?framework=` is missing.

**FrameworkGauge:** large visual gauge showing score for the selected framework. Below: control count (pass/fail/total), grade letter, comparison to previous period.

**GapAnalysis:** every failing control for this framework, grouped by category, with severity, affected machines, and remediation text. This is the meat of the report.

**CloudPosture (filtered):** only cloud findings that map to the selected framework (e.g., HIPAA filter shows only HIPAA-mapped cloud controls). Uses `cloud_assessment_framework_controls` mapping.

**Timeline:** suggested remediation sequence for closing the gaps, prioritized by severity and effort.

### 7. Proposal (Propuesta Comercial)

```
Cover → Semáforo → TopFindings → GapAnalysis → ServiceCatalog → Timeline
```

**ServiceCatalog (full):** auto-generated pricing table. For each service category with findings > 0:

| Categoría | Máquinas afectadas | Horas estimadas | Costo |
|---|---|---|---|
| Cifrado de discos | 47 | 23.5h | $3,525 |
| LAPS deployment | 92 | 23h | $3,450 |
| ... | ... | ... | ... |
| **Total** | | **156h** | **$23,400** |

Pricing formula: `machines_affected × base_hours × hourly_rate × (1 + margin_pct/100)`

**Timeline:** Gantt-style visual. Phases ordered by severity (critical first). Each phase shows: category, duration estimate, dependency.

---

## Service Catalog — Data Model

### Table: `service_catalog`

```sql
CREATE TABLE service_catalog (
    id              INT IDENTITY PRIMARY KEY,
    category_code   VARCHAR(30)    NOT NULL UNIQUE,
    name_en         NVARCHAR(100)  NOT NULL,
    name_es         NVARCHAR(100)  NOT NULL,
    unit_type       VARCHAR(20)    NOT NULL,  -- per_machine, per_domain, per_tenant, per_account, per_cert, per_subscription
    base_hours      DECIMAL(5,2)   NOT NULL,
    trigger_source  VARCHAR(50)    NOT NULL,
    trigger_filter  NVARCHAR(500)  NULL,
    severity        VARCHAR(10)    NOT NULL DEFAULT 'medium',
    sort_order      INT            NOT NULL DEFAULT 0,
    is_active       BIT            NOT NULL DEFAULT 1
);
```

### Seed data (14 categories)

| category_code | name_en | unit_type | base_hours | trigger_source | severity |
|---|---|---|---|---|---|
| `disk_encryption` | Disk Encryption Rollout | per_machine | 0.50 | machines.Bitlocker | critical |
| `laps_deploy` | LAPS Deployment | per_machine | 0.25 | ad_hygiene.NoLAPS | critical |
| `protocol_hardening` | Protocol Hardening (SMBv1/NTLMv1) | per_machine | 1.00 | control_results.SMBv1/NTLM | high |
| `endpoint_protection` | Endpoint Protection Enablement | per_machine | 0.50 | machines.DefenderEnabled | critical |
| `password_policy` | Password Policy Enforcement | per_domain | 2.00 | ad_hygiene.PwdNeverExpire | high |
| `privileged_access` | Privileged Access Review | per_account | 0.50 | ad_hygiene.PrivilegedAccounts | high |
| `patch_management` | Legacy OS Migration | per_machine | 1.50 | machines.OsName (legacy) | critical |
| `firewall_hardening` | Firewall Hardening | per_machine | 0.75 | control_results.firewall | medium |
| `audit_logging` | Audit & Logging Configuration | per_domain | 3.00 | control_results.auditpol | medium |
| `rdp_hardening` | RDP Hardening | per_machine | 0.50 | machine_ports.3389 | high |
| `m365_security` | M365 Security Hardening | per_tenant | 4.00 | cloud_assessment_findings.identity | high |
| `azure_hardening` | Azure Infrastructure Hardening | per_subscription | 6.00 | cloud_assessment_findings.azure | high |
| `cert_hygiene` | Certificate Remediation | per_cert | 0.25 | control_results.certstore | medium |
| `ad_restructuring` | AD Infrastructure Upgrade | per_domain | 8.00 | ad_hygiene.DomainFunctionalLevel | medium |

### Table: `franchise_service_rates`

```sql
CREATE TABLE franchise_service_rates (
    id              INT IDENTITY PRIMARY KEY,
    franchise_id    UNIQUEIDENTIFIER NOT NULL REFERENCES franchises(id),
    hourly_rate     DECIMAL(10,2)  NOT NULL DEFAULT 150.00,
    currency        VARCHAR(3)     NOT NULL DEFAULT 'USD',
    margin_pct      DECIMAL(5,2)   NOT NULL DEFAULT 0,
    effective_from  DATETIME2(2)   NOT NULL DEFAULT GETUTCDATE(),
    created_at      DATETIME2(2)   NOT NULL DEFAULT GETUTCDATE()
);
```

### Table: `executive_ctas` (unchanged from prior spec)

```sql
CREATE TABLE executive_ctas (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id     UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    period_start        DATETIME2(2)     NOT NULL,
    auto_detected_rule  NVARCHAR(100)    NULL,
    priority_category   VARCHAR(20)      NOT NULL,
    title               NVARCHAR(200)    NOT NULL,
    description         NVARCHAR(2000)   NOT NULL,
    is_suppressed       BIT              NOT NULL DEFAULT 0,
    is_manual           BIT              NOT NULL DEFAULT 0,
    created_by          UNIQUEIDENTIFIER NOT NULL,
    created_at          DATETIME2(2)     NOT NULL,
    modified_by         UNIQUEIDENTIFIER NULL,
    modified_at         DATETIME2(2)     NULL,
    deleted_by          UNIQUEIDENTIFIER NULL,
    deleted_at          DATETIME2(2)     NULL
);
```

---

## API Design

### Unified report endpoint

```
GET /v2/reports/org/{orgId}?type=c-level|technical|preventa|monthly|framework|proposal
                           &tone=opener|detailed    (preventa only)
                           &framework=NIST|CIS|HIPAA|ISO27001|PCI-DSS  (required for framework type, optional filter for others)
                           &lang=en|es
```

Returns: `text/html` self-contained report.

**Backward compatibility aliases (2 deploy cycles, then 410):**
- `type=executive` → `type=c-level`
- `type=presales` → `type=preventa&tone=detailed`
- `type=presales-opener` → `type=preventa&tone=opener`

**Validation:**
- `type=framework` without `?framework=` → 400
- `tone` param ignored for non-preventa types
- Unknown `type` → 400

### Service catalog endpoints

```
GET    /v2/service-catalog                        — list all active catalog items
PATCH  /v2/franchise-rates/{franchiseId}          — set hourly rate + margin
GET    /v2/franchise-rates/{franchiseId}          — get current rate
```

### CTA management endpoints

```
GET    /v2/reports/org/{orgId}/ctas               — preview auto-detected CTAs for current period
PATCH  /v2/reports/org/{orgId}/ctas/{id}          — edit text / suppress
POST   /v2/reports/org/{orgId}/ctas               — add manual CTA
```

### Cloud assessment report endpoint — DEPRECATED

`CloudAssessmentReportService.GenerateAsync` is superseded by the unified report system. Cloud data flows through `ReportDataLoader` into any recipe that includes `CloudPostureBlock`. The standalone cloud report endpoints return 410 after migration.

---

## Visual Baseline (unchanged)

All 7 reports use the existing Brand 2025 visual system:

- **Cover:** dark `#3D4043` with ribbon SVG behind white-filtered brand logo
- **Header bar:** dark `#3D4043` with ribbon gradient
- **Content area:** Big 4 Financial Audit light palette (`#FFFFFF` / `#F8F9FA`, `#1E293B` / `#334155` typography, `#0F172A` navy accents, `#991B1B` brick red for critical)
- **Running footer:** current operator contact info on every page
- **A4 strict:** 296mm with 1mm safety buffer
- **Screen preview:** 78% zoom with drop shadow
- **Font:** Montserrat (Google Fonts)
- **Colors:** PASS=#008852, WARN=#D97706, FAIL=#C0392B

---

## Cross-Cutting Rules

1. **Cloud sections are conditional:** CloudPostureBlock, cloud-filtered GapAnalysis, and cloud KPIs only render when `data.CloudScan != null`. No empty sections, no "connect M365 to see this" placeholders.

2. **Framework filter applies globally:** when `?framework=HIPAA` is set, ALL blocks filter their data to HIPAA-mapped controls only. Report title adapts (e.g., "Technical Report — HIPAA Compliance").

3. **Bilingual:** `?lang=en|es` applies to all blocks via `ReportOptions`. Dictionary-based i18n. Missing ES key → fallback to EN. Date/number formatting respects locale.

4. **Benchmarks injected when available:** KpiBlock and FrameworkGaugeBlock incorporate benchmark data (franchise peers, industry baseline, global) when `data.Benchmarks != null`. Shows percentile positioning.

5. **Empty states:** every block handles the case where its data source has zero items. Blocks show a positive checkmark message rather than hiding entirely (maintains consistent visual structure).

---

## Migration Path

### Phase 1 — Infrastructure (blocks + loader)

1. Create `Services/Reports/` folder structure
2. Implement `IReportBlock`, `ReportData`, `ReportOptions`, `ReportDataLoader`
3. Implement `ReportComposer` (recipe → HTML assembly with shared CSS/header/footer)
4. SQL migrations: `service_catalog`, `franchise_service_rates`, `executive_ctas`
5. Seed: 14 service catalog items

### Phase 2 — Migrate existing reports to blocks

6. Extract existing C-Level logic into `SemaforoBlock`, `KpiBlock`, `CtaBlock`
7. Extract Technical logic into `AssetMatrixBlock`, `TopFindingsBlock`, `IronSixBlock`
8. Extract Preventa logic into `RiskScoreBlock`, `ThreatVectorsBlock`, `MethodologyBlock`
9. Create `CoverBlock` (shared)
10. Wire `CLevelRecipe`, `TechnicalRecipe`, `PreventaOpenerRecipe`, `PreventaDetailedRecipe`
11. Verify output parity with current reports

### Phase 3 — New blocks

12. Implement `CloudPostureBlock` (merge cloud report logic)
13. Implement `FrameworkGaugeBlock` + `GapAnalysisBlock`
14. Implement `ServiceCatalogBlock` + `TimelineBlock`
15. Implement `ScoreTrendBlock` + `DeltaBlock`

### Phase 4 — New recipes

16. Wire `FrameworkRecipe`
17. Wire `ProposalRecipe`
18. Wire `MonthlyRecipe` (P2, last)

### Phase 5 — Cleanup

19. Deprecate old `ReportService.cs` (delete after verification)
20. Deprecate `CloudAssessmentReportService.cs`
21. Update `ReportsFunction.cs` to route through `ReportComposer`
22. Remove backward-compat aliases after 2 deploy cycles

---

## Implementation Priority

| Order | What | Why |
|---|---|---|
| 1 | Infrastructure + CoverBlock | Foundation for everything |
| 2 | C-Level blocks + recipe | Highest business value, validates architecture |
| 3 | Technical blocks + recipe | Largest block count, exercises pagination |
| 4 | Preventa blocks + recipes | Mostly extraction from existing code |
| 5 | CloudPostureBlock | Enables cloud integration across all recipes |
| 6 | Framework recipe + blocks | New capability |
| 7 | Proposal recipe + ServiceCatalog | New capability, depends on service catalog seed |
| 8 | Monthly recipe | P2, uses blocks already built (ScoreTrend, Delta, KPI) |

---

## Success Criteria

1. Portal dropdown shows 7 report types (Monthly may be disabled/placeholder initially)
2. Single unified endpoint handles all 7 types
3. Cloud sections appear automatically when M365/Azure connected, absent when not
4. Framework filter works across all report types
5. Proposal auto-generates pricing from service catalog + franchise rate
6. All reports bilingual (EN/ES)
7. Visual parity with current Brand 2025 baseline
8. Old endpoint aliases work during deprecation window
9. No regression in existing C-Level, Technical, or Preventa output
10. Each block independently renders valid HTML (testable in isolation)

---

## Out of Scope

- **NinjaOne / RMM integration** — Monthly report v1 uses Kryoss data only. NinjaOne adds enrichment blocks later
- **Server-side PDF generation** — reports stay HTML + browser print. Playwright/QuestPDF deferred
- **Scheduled email delivery** — separate project
- **Per-run reports** — deprecated since 2026-04-15, stay 410
- **Custom report builder** (drag-and-drop blocks) — future feature, architecture supports it but not building the UI
- **CVE scanning integration** — TopFindings is control-based, not CVE-based
- **Editable service catalog via portal UI** — v1 is seed-only, portal CRUD deferred
