# Report Block Library Finalization — Task List

> **Scope:** Complete the ReportComposer block library. Audit existing blocks, add missing SVG chart helpers, add 9 new blocks, cleanup dropdown, and ship a test fixture that renders every block 30 times to stress-test A4 layout.
>
> **NO TOCAR:** `CoverBlock.cs` visual (audit only), `.ph` page header CSS, `.page-footer` CSS, A4 print CSS (`GetA4PrintCss`). These are frozen.
>
> **Created:** 2026-04-20
> **Status:** Queued, ready for subagent-driven execution

---

## Phase 1: Audit + Refactor Existing Blocks

25 existing blocks — most need just audit + documentation. A few need parameter expansion.

### Task 01 — CoverBlock audit (NO MODIFY VISUAL)
**Files:** `Services/Reports/Blocks/CoverBlock.cs`
**Action:** read only. Verify renders `.cover` + `.cover-ribbon` + `.cover-content` with `eyebrow/h1/h2/meta/score`. Document inputs: `{clientName, reportTitle, scanDate, scoreValue?, gradeCode?}`.
**If interface mismatch with `IReportBlock.Render`:** align signature only, no visual change.

### Task 02 — KpiBlock variants
**Files:** `Services/Reports/Blocks/KpiBlock.cs`, `Services/Reports/ReportOptions.cs`
**Action:** add `KpiVariant` enum `{ Exec, Business, Compact }` to `ReportOptions`.
- `Exec` — current tech framing (pass/fail/warn counts)
- `Business` — labels swapped to biz language ("users at risk" not "fails"), currency formatting
- `Compact` — smaller padding (6mm), max 6 items

**Data shape:**
```csharp
public record KpiItem(string Label, string Value, string Variant, string? Icon, string? TrendArrow, string? SubLabel);
```
**CSS reuse:** `.summary-grid`, `.stat`, `.pass-stat`, `.warn-stat`, `.fail-stat`, `.stat-value`, `.stat-label`.
**Test plan:** 4 KPIs, 6 KPIs, 8 KPIs row.

### Task 03 — SemaforoBlock audit
**Files:** `Services/Reports/Blocks/SemaforoBlock.cs`
**Action:** read, document visual. No modify.

### Task 04 — CtaBlock dual-mode
**Files:** `Services/Reports/Blocks/CtaBlock.cs`
**Action:** add `CtaMode` enum `{ Simple, Stepped }`.
- `Simple` — current `.cta-box`
- `Stepped` — `.next-steps` + `.step` + `.step-num` numbered list

**Data shape:**
```csharp
public record CtaData(CtaMode Mode, string Title, string? Body, List<CtaStep>? Steps);
public record CtaStep(int Num, string Title, string Desc);
```
**Test plan:** Simple + 3/5/10 steps.

### Task 05 — AssetMatrixBlock verify
**Files:** `Services/Reports/Blocks/AssetMatrixBlock.cs`
**Action:** verify renders `.fleet-table` with per-machine rows. Confirm columns: Hostname | OS | Compliance % | Critical | High | Medium | Last Scan. Verify pagination breaks cleanly.
**Test plan:** 5 machines, 30 machines, 100 machines (force A4 overflow to verify `page-break-inside:avoid`).

### Task 06 — TopFindingsBlock
**Files:** `Services/Reports/Blocks/TopFindingsBlock.cs`
**Action:** ensure uses `.results-table` + `.severity` + `.status-badge`. Each row: ID + Name + Severity badge + Affected count + Category.
**Test plan:** 5, 10, 20 findings.

### Task 07 — IronSixBlock audit
**Action:** read, document "6 de hierro" visual. Keep as-is.
**Test plan:** standard 6 items.

### Task 08 — RiskScoreBlock verify
**Action:** check if renders donut/gauge or just big number. If no donut → modify to use `SvgDonut` helper (Task 21).

### Task 09 — ThreatVectorsBlock audit
**Action:** document. Verify uses `.risk-card` style for threats.

### Task 10 — MethodologyBlock audit + audience variant
**Files:** `Services/Reports/Blocks/MethodologyBlock.cs`
**Action:** add `AudiencePerspective` enum `{ Technical, Audit }`. Audit variant adds: data sources list, framework version citations, scan timestamp, caveats/limitations section.

### Task 11 — CloudPostureBlock audit
**Action:** check if renders radar chart or bars. If bars only → add `SvgRadar` option (after Task 22).

### Task 12 — FrameworkGaugeBlock audit
**Action:** verify uses `.framework-bars` + `.fw-bar-row` per framework. Letter grade + %. Confirm both modes supported: single-framework (Framework recipe) vs all-frameworks (C-Level).

### Task 13 — GapAnalysisBlock audit
**Action:** document current rendering.

### Task 14 — ServiceCatalogBlock tier grid
**Files:** `Services/Reports/Blocks/ServiceCatalogBlock.cs`, `ReportStyles.cs`
**Action:** verify renders 3-tier engagement grid vs flat list. If flat → modify to 3-column grid.

**New CSS (add to `ReportStyles.cs`):**
```css
.tier-grid { display:grid; grid-template-columns:repeat(3,1fr); gap:14px; margin:16px 0; }
.tier-card { border:1px solid #E2E8F0; border-radius:6px; padding:18px; background:#FFFFFF; break-inside:avoid; }
.tier-card.highlight { border-width:2px; border-color:#15803D; box-shadow:0 4px 12px rgba(15,128,61,0.12); }
.tier-header { margin-bottom:10px; }
.tier-name { font-size:14px; font-weight:700; color:#0F172A; }
.tier-price { font-size:22px; font-weight:900; color:#15803D; margin:6px 0; }
.tier-duration { font-size:10px; color:#64748B; text-transform:uppercase; letter-spacing:0.08em; }
.tier-bullets { list-style:none; padding:0; margin:12px 0; font-size:11px; }
.tier-bullets li { padding:4px 0 4px 18px; position:relative; }
.tier-bullets li::before { content:'✓'; position:absolute; left:0; color:#15803D; font-weight:700; }
```

**Data shape:**
```csharp
public record ServiceTier(string Name, string Price, string Duration, List<string> Bullets, bool Highlight);
```

### Task 15 — TimelineBlock audit
**Action:** if duplicate with `.next-steps` impl, consolidate. One timeline impl only.

### Task 16 — ScoreTrendBlock + DeltaBlock consolidate
**Files:** `Services/Reports/Blocks/ScoreTrendBlock.cs`, `DeltaBlock.cs`
**Action:** merge into `ScoreTrendBlock` with delta inline. Use `SvgSparkline` helper (Task 23).
**Deprecate DeltaBlock** (delete or mark obsolete).
**Test plan:** 3 points, 6 points, 12 points, 24 points.

### Task 17 — CloudExecutiveBlock + CloudPostureBlock consolidate
**Action:** identify overlap. Consolidate into 1 block with variants (`Executive` vs `Posture`).

### Task 18 — ExecOnePagerBlock freeze
**Action:** NO TOCAR. Self-contained `.op-*` CSS. Document only.

### Task 19 — NetworkBlock split
**Files:** `Services/Reports/Blocks/NetworkBlock.cs`
**Action:** split into sub-variants passed via options:
- `NetworkSummary` — KPI row (sites count, bandwidth avg, issues count)
- `NetworkSitesTable` — per-site detail row using `.results-table`
- `NetworkMap` — placeholder div, hook to IA-11 static export later

### Task 20 — Legacy blocks audit
**Files:** `M365Block.cs`, `RiskSummaryBlock.cs`, `ComplianceScorecardBlock.cs`, `HygieneBlock.cs`, `InventoryBlock.cs`
**Action:** read each, document data shape + visual. Check overlap with Cloud Assessment equivalents. Mark deprecation candidates if redundant.

---

## Phase 2: New SVG Chart Helpers

Pure functions, return inline SVG strings, no external deps.

### Task 21 — SvgDonut helper
**Create:** `Services/Reports/Charts/SvgDonut.cs`

**API:**
```csharp
public static class SvgDonut
{
    public static string Render(
        List<DonutSegment> segments,
        int size = 160,
        double innerRadius = 0.65,
        string? centerLabel = null,
        bool showLegend = true);
}

public record DonutSegment(string Label, decimal Value, string Color);
```

**Visual:** ring with colored segments, center shows total or custom label, legend right-side 2-col.
**CSS wrap class:** `.chart-donut`

### Task 22 — SvgRadar helper
**Create:** `Services/Reports/Charts/SvgRadar.cs`

**API:**
```csharp
public static class SvgRadar
{
    public static string Render(
        List<string> axes,
        List<RadarSeries> series,
        decimal maxValue = 5m,
        int size = 300);
}

public record RadarSeries(string Name, List<decimal> Values, string Color, double FillOpacity);
```

**Visual:** 5-ring concentric polygon grid, axis labels around, series polygons overlaid with transparency.
**Use cases:** CA areas (5-7 axes), Benchmark overlay (tenant + peer + industry), Framework compliance overview.

### Task 23 — SvgSparkline helper
**Create:** `Services/Reports/Charts/SvgSparkline.cs`

**API:**
```csharp
public static class SvgSparkline
{
    public static string Render(
        List<(DateTime Date, decimal Value)> points,
        int width = 120,
        int height = 30,
        string color = "#2BB673",
        bool showDots = false,
        bool showEndValue = false);
}
```

**Visual:** mini line chart, no axes, in-line with text.
**Use cases:** TrendBenchmark, ScoreTrend, per-row table sparklines.

### Task 24 — SvgProgressCircle (optional, skip if unused)
**Create only if** any new block requires circular progress 0-100%. Current `.grade-badge` + big number suffice for most cases.

---

## Phase 3: New Blocks

### Task 25 — RiskRoiBlock
**Create:** `Services/Reports/Blocks/RiskRoiBlock.cs`

**Visual:**
- `.results-table` with columns: Rank | Risk | Likelihood | Impact $ | Mitigation Cost $ | ROI | Severity
- Sorted by ROI desc
- Rank numbered circle (reuse `.risk-num` style inside first cell)
- Top 3 rows get subtle red tint row class

**Data shape:**
```csharp
public record RiskRoiItem(int Rank, string Title, string Likelihood, decimal ImpactUsd, decimal MitigationUsd, decimal RoiRatio, string Severity, string? Narrative);
```

**Test plan:** 5 rows, 10 rows, 20 rows.

### Task 26 — DecisionsMatrixBlock
**Create:** `Services/Reports/Blocks/DecisionsMatrixBlock.cs`

**Visual:** 3-column grid (new CSS `.decisions-grid`). Headers: "Approved & Funded" (green), "Pending Approval" (amber), "Recommended Next" (blue). Per column: list items with title + 1-line ask + $ pill.

**New CSS (add to `ReportStyles.cs`):**
```css
.decisions-grid { display:grid; grid-template-columns:1fr 1fr 1fr; gap:12px; margin:14px 0; }
.decisions-col { border-top:4px solid; padding:12px; background:#F8F9FA; border-radius:4px; break-inside:avoid; }
.decisions-col.approved { border-color:#15803D; }
.decisions-col.pending { border-color:#B45309; }
.decisions-col.recommended { border-color:#2563EB; }
.decisions-col h4 { font-size:11px; text-transform:uppercase; letter-spacing:0.08em; font-weight:700; color:#0F172A; margin-bottom:10px; }
.decision-item { padding:8px 0; border-bottom:1px solid #E5E7EB; }
.decision-item:last-child { border:none; }
.decision-title { font-weight:600; font-size:12px; color:#0F172A; margin-bottom:2px; }
.decision-ask { font-size:11px; color:#64748B; line-height:1.4; }
.decision-cost { display:inline-block; background:#0F172A; color:#fff; padding:2px 8px; border-radius:4px; font-size:10px; font-weight:700; margin-top:4px; }
```

**Data shape:**
```csharp
public record DecisionsMatrixData(List<Decision> Approved, List<Decision> Pending, List<Decision> Recommended);
public record Decision(string Title, string Ask, decimal? CostUsd, string? Duration);
```

**Test plan:** 3/3/3 items, 5/5/5, 2/8/2 (unbalanced).

### Task 27 — ControlDetailBlock (Framework auditor)
**Create:** `Services/Reports/Blocks/ControlDetailBlock.cs`

**Visual:** per-control card with strict structure:
```
┌──[severity stripe left edge]────────────────────────────────┐
│ {FrameworkId} §{ControlCode} — {Title}      [STATUS pill]   │
│ ─────────────────────────────────────────────────────────── │
│ Requirement: {description, wraps 2-3 lines}                 │
│                                                             │
│ Kryoss Controls Mapped ({N}):                               │
│  ● {KryossId1} — {Name1}                  [pass/fail]       │
│  ● {KryossId2} — {Name2}                  [pass/fail]       │
│                                                             │
│ Evidence: {scanStamp} — {machineCount} machines tested      │
│ Last Tested: {date}                                         │
│ Remediation Owner: ______  Target: ______                   │
└─────────────────────────────────────────────────────────────┘
```

**New CSS:**
```css
.control-card { border:1px solid #E5E7EB; border-radius:6px; margin-bottom:10px; padding:14px 16px 14px 20px; position:relative; break-inside:avoid; background:#fff; }
.control-card::before { content:''; position:absolute; left:0; top:0; bottom:0; width:4px; border-radius:6px 0 0 6px; background:var(--sev-color); }
.control-card.critical { --sev-color:#7F1D1D; }
.control-card.high     { --sev-color:#C0392B; }
.control-card.medium   { --sev-color:#D97706; }
.control-card.low      { --sev-color:#2563EB; }
.control-head { display:flex; justify-content:space-between; align-items:baseline; margin-bottom:8px; gap:12px; }
.control-code { font-family:monospace; font-size:11px; color:#64748B; }
.control-title { font-weight:700; font-size:13px; color:#0F172A; flex:1; }
.control-req { font-size:12px; color:#334155; margin-bottom:10px; line-height:1.5; }
.control-mapped-label { font-size:10px; color:#64748B; margin-bottom:4px; text-transform:uppercase; letter-spacing:0.06em; font-weight:700; }
.mapped-list { list-style:none; padding:0; margin:0 0 10px; }
.mapped-list li { padding:4px 0; font-size:11px; display:flex; justify-content:space-between; border-bottom:1px dashed #E5E7EB; }
.mapped-list li:last-child { border:none; }
.control-meta { font-size:10px; color:#64748B; border-top:1px solid #E5E7EB; padding-top:6px; display:flex; justify-content:space-between; }
.control-remediation-fields { font-size:11px; color:#64748B; margin-top:4px; font-family:monospace; }
```

**Data shape:**
```csharp
public record ControlDetailData(
    string FrameworkId, string ControlCode, string Title, string Requirement,
    string Severity, string OverallStatus,
    List<MappedControl> MappedControls,
    DateTime ScanStamp, int MachineCount, DateTime LastTested,
    string OwnerPlaceholder, string TargetPlaceholder);

public record MappedControl(string KryossId, string Name, string Status);
```

**Test plan:** 10 controls, 30 controls, 60 controls (multi-page stress).

### Task 28 — EvidenceAppendixBlock
**Create:** `Services/Reports/Blocks/EvidenceAppendixBlock.cs`

**Visual:** per-control evidence snippet with:
- Control code header
- Evidence type (screenshot / registry / powershell / eventlog)
- Monospace code block (max 10 lines, truncate with "...")
- Timestamp + machine label

**New CSS:**
```css
.evidence-item { margin-bottom:16px; page-break-inside:avoid; }
.evidence-head { font-size:11px; font-weight:700; color:#0F172A; margin-bottom:4px; }
.evidence-meta { font-size:10px; color:#64748B; margin-bottom:6px; }
.evidence-code { background:#0F172A; color:#F1F5F9; font-family:Consolas,monospace; font-size:10px; padding:10px 12px; border-radius:4px; white-space:pre-wrap; max-height:140px; overflow:hidden; position:relative; line-height:1.5; }
.evidence-code.truncated::after { content:'...'; position:absolute; bottom:4px; right:8px; color:#94A3B8; }
```

**Data shape:**
```csharp
public record EvidenceItem(string ControlCode, string EvidenceType, string Content, string Machine, DateTime Timestamp, bool Truncated);
```

**Test plan:** 5 items (short), 20 items (mixed), 50 items (pagination stress).

### Task 29 — CategoryBreakdownBlock
**Create:** `Services/Reports/Blocks/CategoryBreakdownBlock.cs`

**Visual:** table per category with: category name, total controls, passing count, failing count, N/A count, % bar.
**CSS reuse:** `.cat-header`, `.cat-bar`, `.cat-bar-pass/warn/fail`, `.results-table`.

**Table form:**
```html
<table class="results-table">
  <tr><th>Category</th><th>Passing</th><th>Failing</th><th>N/A</th><th>Coverage</th></tr>
  <tr>
    <td>Access Control</td><td>12</td><td>3</td><td>2</td>
    <td>
      <div class="cat-bar">
        <div class="cat-bar-pass" style="width:70%"></div>
        <div class="cat-bar-fail" style="width:18%"></div>
      </div>
    </td>
  </tr>
</table>
```

**Data shape:**
```csharp
public record CategoryBreakdown(string Name, int Passing, int Failing, int NotApplicable);
```

**Test plan:** 5 categories, 15 categories, 30 categories.

### Task 30 — Top3RiskBlock (preventa opener)
**Create:** `Services/Reports/Blocks/Top3RiskBlock.cs`

**Visual:** 3-column card row (NOT `.risk-cards` vertical list). Each card: icon + title + 1-line body + red tint. Exactly 3 cards.

**New CSS:**
```css
.top-risk-grid { display:grid; grid-template-columns:repeat(3,1fr); gap:12px; margin:16px 0; }
.top-risk-card { padding:18px; background:#FEF2F2; border:1px solid #FECACA; border-left:4px solid #991B1B; border-radius:6px; break-inside:avoid; }
.top-risk-icon { font-size:24px; margin-bottom:8px; }
.top-risk-title { font-weight:700; font-size:13px; color:#450A0A; margin-bottom:6px; }
.top-risk-body { font-size:11px; color:#7F1D1D; line-height:1.5; }
.top-risk-cost { display:inline-block; margin-top:8px; padding:3px 8px; background:#991B1B; color:#fff; font-size:10px; font-weight:700; border-radius:3px; }
```

**Data shape:**
```csharp
public record TopRiskItem(string? Icon, string Title, string Body, string? CostEstimate);  // exactly 3
```

**Test plan:** 3 short body (1 line), 3 medium (2 lines), 3 long (3 lines).

### Task 31 — FrameworkCoverBlock (audit variant)
**Create:** `Services/Reports/Blocks/FrameworkCoverBlock.cs`

**Visual:** specialized cover for audit reports. Extends `.cover` base:
- Eyebrow: "COMPLIANCE EVIDENCE REPORT"
- H1: framework name + version ("ISO 27001:2022")
- H2: audit period + org scope ("Q1 2026 • Contoso Corp")
- Grade badge large centered
- Score % big number
- Meta table: authority, report date, auditor name placeholder

**CSS reuse:** `.cover`, `.cover-content`, `.grade-badge`, `.big-number`.

**New CSS (minor):**
```css
.cover-audit-meta { margin-top:30px; border-top:1px solid rgba(255,255,255,0.2); padding-top:16px; }
.cover-audit-meta dt { font-size:9px; text-transform:uppercase; letter-spacing:0.1em; color:rgba(255,255,255,0.6); margin-bottom:2px; }
.cover-audit-meta dd { font-size:13px; color:#fff; margin-bottom:10px; }
```

### Task 32 — NetworkMiniBlock (preventa detailed)
**Create:** `Services/Reports/Blocks/NetworkMiniBlock.cs`

**Visual:** split 2-col, left 60% map placeholder, right 40% stacked KPIs (sites, avg bandwidth, high-risk links, rogue devices).

**New CSS:**
```css
.network-mini-split { display:grid; grid-template-columns:3fr 2fr; gap:12px; margin:14px 0; break-inside:avoid; }
.network-mini-map { background:#F8F9FA; border:1px solid #E2E8F0; border-radius:6px; min-height:180px; position:relative; display:flex; align-items:center; justify-content:center; color:#64748B; font-size:11px; }
.network-mini-kpis { display:flex; flex-direction:column; gap:8px; }
.network-mini-kpis .stat { flex:1; padding:10px; }
```

**Note:** after IA-11 ships, swap `.network-mini-map` placeholder with real Leaflet static export.

### Task 33 — NextStepBlock (preventa opener CTA)
**Create:** `Services/Reports/Blocks/NextStepBlock.cs`

**Visual:** 3-region:
- Section "Lo que encontramos" — 5 bullets
- Section "Cómo te ayudamos" — 3 bullets
- CTA bottom (reuse `.cta-box`)

**CSS reuse:** `.next-steps`, `.step`, `.cta-box`, `.phase-list`.

**Data shape:**
```csharp
public record NextStepData(string HeaderFound, List<string> Found, string HeaderHelp, List<string> Help, string CtaText);
```

---

## Phase 4: Dropdown + Routing Cleanup

### Task 34 — ReportGenerator.tsx dropdown cleanup
**Files:** `KryossPortal/src/components/reports/ReportGenerator.tsx`

Remove:
- `executive` (redirect → c-level)
- `exec-onepager` (redirect → c-level OR keep if used by franchises — verify usage first)
- `compliance` (redirect → framework)
- `m365` (already deprecated via 410 since CA-12)

Split:
- `preventas` → `preventa-opener` + `preventa-detailed` (drop tone selector)

Add:
- `infra-assessment` (hook when IA-10 ships)
- `benchmarks` (CA-11 standalone delivery)

Final count: 15 → 13 types.

### Task 35 — Backend route updates
**Files:** `Functions/Portal/ReportsFunction.cs`, `Services/Reports/ReportComposer.cs`

Add switch cases:
- `executive` → return 301 to `?type=c-level`
- `exec-onepager` → check actual usage, either keep or redirect
- `compliance` → 301 to `?type=framework`
- `preventas` → 301 to `?type=preventa-detailed` (tone=detailed) or `?type=preventa-opener` (tone=opener)

Wire new recipes:
- `preventa-opener` → `PreventaOpenerRecipe` (already exists)
- `preventa-detailed` → `PreventaDetailedRecipe` (already exists)

---

## Phase 5: Test Report Fixture

### Task 36 — TestFixtureRecipe
**Create:** `Services/Reports/Recipes/TestFixtureRecipe.cs`

**Purpose:** renders EVERY block 30 times to stress-test A4 layout, text wrapping, page overflow.

**Structure:**
```csharp
public class TestFixtureRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions opts) => "Kryoss Block Library Test Fixture";
    public ReportDataNeeds DataNeeds => ReportDataNeeds.All;

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        var faker = new FakeDataBuilder(seed: 42);

        yield return new SectionHeaderBlock("Section 1: KpiBlock variants");
        for (int i = 0; i < 30; i++)
            yield return new KpiBlock(faker.KpiRow(variant: i % 3, lines: (i % 3) + 1));

        yield return new SectionHeaderBlock("Section 2: RiskRoiBlock");
        for (int i = 0; i < 30; i++)
            yield return new RiskRoiBlock(faker.RiskRoi(rows: 5, lineVariant: i % 3));

        yield return new SectionHeaderBlock("Section 3: DecisionsMatrixBlock");
        for (int i = 0; i < 30; i++)
            yield return new DecisionsMatrixBlock(faker.DecisionsMatrix(lineVariant: i % 3));

        yield return new SectionHeaderBlock("Section 4: ControlDetailBlock");
        for (int i = 0; i < 30; i++)
            yield return new ControlDetailBlock(faker.ControlDetail(reqLines: (i % 3) + 1));

        yield return new SectionHeaderBlock("Section 5: EvidenceAppendixBlock");
        for (int i = 0; i < 30; i++)
            yield return new EvidenceAppendixBlock(faker.Evidence(items: 3));

        // ... one section per block type, 30 instances each
        // Include: Top3Risk, NextStep, NetworkMini, CategoryBreakdown,
        // AssetMatrix (with 30/100 row variants), TopFindings, CtaBlock (both modes),
        // ScoreTrend (different lengths), FrameworkGauge, ServiceCatalog tier grid, etc.
    }
}
```

**Fake data generator:** `Services/Reports/Tests/FakeDataBuilder.cs`
- Lorem ipsum with LENGTH variants: `lines = 1 | 2 | 3` → short/medium/long body
- Numeric values with seeded randomization (stable output)
- Date ranges for timelines/trends

**Line-count variants:**
Free-text fields (observation, recommendation, risk body, control requirement, etc.) alternate:
- Instance 0 → 1-line text (~60 chars)
- Instance 1 → 2-line text (~120 chars)
- Instance 2 → 3-line text (~240 chars)
- Repeat pattern

**Endpoint:**
Route: add `test-fixture` to existing `?type=test-fixture` switch in `ReportsFunction.GenerateOrg`, gated by admin permission only. Or separate `GET /v2/reports/test-fixture` admin endpoint.

**Visual verification checklist post-render:**
1. Scroll through all sections
2. Verify:
   - [ ] No block spills across page break without `break-inside:avoid`
   - [ ] 3-line text variants stay within card bounds (no overflow, no clip)
   - [ ] Long tables (30-row AssetMatrix) paginate correctly
   - [ ] Footer stays at bottom of every A4 page
   - [ ] Header `.ph` consistent on every page
   - [ ] SVG charts render (donut / radar / sparkline)
   - [ ] Page count reasonable (~150-200 pages for 30 × ~25 blocks)
   - [ ] Chrome print preview matches screen rendering

**Success criteria:**
- 0 content clipping
- 0 orphan headers
- 0 content overflowing A4 296mm bounds
- Every block visually complete
- Print-preview matches screen

### Task 37 — Optional QA script
**Create:** `Scripts/Report/Test-Fixture.ps1` OR Playwright test

**Purpose:** automate verification:
1. Hit test-fixture endpoint with admin token
2. Save HTML to disk
3. Headless Chrome render → PDF (via `puppeteer` or similar)
4. Count pages via `pdftotext`
5. Scan for overflow violations
6. Generate report card: per-block status (OK / overflow on page N / clip detected)

Skip if effort > 30 min. Manual visual inspection acceptable for MVP.

---

## Summary Table

| Phase | Tasks | Est Effort |
|-------|-------|-----------|
| 1 Audit + refactor existing blocks | 01-20 | 1 session |
| 2 SVG chart helpers | 21-24 | 0.5 session |
| 3 New blocks | 25-33 | 1 session |
| 4 Dropdown + routing cleanup | 34-35 | 0.25 session |
| 5 Test fixture + optional QA | 36-37 | 0.5 session |
| **Total** | **37 tasks** | **~3.25 sessions** |

---

## Rules for Claude Code

1. **NO TOCAR (frozen):**
   - `CoverBlock.cs` visual rendering (audit only, confirm interface signature)
   - `.ph` page header CSS in `ReportStyles.cs`
   - `.page-footer` CSS in `ReportStyles.cs`
   - `GetA4PrintCss` block (A4 print discipline)

2. **Reuse first:** before creating new CSS class, check `ReportStyles.cs` for existing equivalent. List existing reuse in each new block's comments.

3. **Data adapters pure:** if a block needs transform of `ReportData`, create adapter function in `ReportHelpers.cs` or `Services/Reports/Adapters/{Block}Adapter.cs`. No transformation logic inside block render.

4. **Every new CSS class documented:** inline comment in `ReportStyles.cs` explaining use case and which block(s) use it.

5. **Test fixture last:** after ALL blocks done (Phase 1-4). Run manual visual inspection. Report fix list if issues found in any block.

6. **Commit cadence:** minimum 5 commits, one per phase. Plus final commit with test fixture output saved to `docs/report-fixtures/` for visual baseline reference.

7. **Build after every phase:** `dotnet build` + `npm run build`. Fail = stop, fix, continue.

8. **Deploy after phase 4:** `func azure functionapp publish func-kryoss` + portal deploy. Test fixture gets deployed in phase 5.

---

## Success Definition

- 25 existing blocks audited, documented, refactored where needed
- 3 SVG chart helpers (donut, radar, sparkline) available for any block to use
- 9 new blocks built: RiskRoi, DecisionsMatrix, ControlDetail, EvidenceAppendix, CategoryBreakdown, Top3Risk, FrameworkCover, NetworkMini, NextStep
- Dropdown cleaned from 15 → 13 types
- Test fixture renders 30 × ~25 blocks with line-count variants
- Visual inspection: 0 overflow, 0 clipping, A4 discipline respected

After this ships: every future report type = compose existing blocks, no new HTML/CSS code per report.
