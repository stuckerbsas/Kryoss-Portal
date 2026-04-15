# Reports — 4-Type Baseline Consolidation

**Date:** 2026-04-15
**Status:** Design approved via brainstorm, ready for implementation planning
**Supersedes:** The current 8-variant report catalog (see "Current state" below)

---

## Problem

The current Kryoss report catalog has grown to **8 variants** through iterative feature additions:

1. Technical (per-run)
2. Executive (per-run)
3. Presales (per-run)
4. Technical (org)
5. Executive (org)
6. Presales (org detailed)
7. Presales Opener (org)
8. Executive One-Pager (org)
9. Monthly Briefing (MRR) (org)

This violates the "principle of least choices for the user" — the MSP operator has to remember the differences between "Executive" and "Executive One-Pager" and "C-Level" before every export. It also multiplies maintenance cost: every new feature (Big 4 palette, running footer, bilingual support) must be replicated across 8 report builders.

## Decision

Consolidate to **4 report types**, each mapped 1:1 to an audience, all org-scoped, with 2 orthogonal filters:

| Type | Audience | Cadence | Length |
|---|---|---|---|
| **C-Level** | CEO / COO / CFO | Ad-hoc | Cover + 1 content page (2 A4) |
| **Technical Level** | SysAdmin / IT Manager | Ad-hoc (weekly backlog) | Cover + dynamic (5-15 A4) |
| **Monthly Progress** | Mixed exec + IT (MRR meeting) | Monthly | Cover + 3 content pages (4 A4) |
| **Preventas** | Prospect decision-maker + IT | 1×/sales cycle | Varies by tone param |

**Filters applied to all 4:**
- **Framework:** `all` · `NIST` · `CIS` · `HIPAA` · `ISO27001` · `PCI-DSS`
- **Language:** `English` · `Español`

**Scope:** always org-level. Per-run (single machine) reports are removed — that use case is covered by live drill-down in the portal's machine detail view.

**Visual baseline (already deployed — no redesign needed):**
- Cover: dark `#3D4043` with ribbon SVG behind white-filtered brand logo
- Header bar (`.ph`): dark `#3D4043` with ribbon gradient (visual continuity to cover)
- Content area (`.pb`): Big 4 Financial Audit light palette (`#FFFFFF` / `#F8F9FA` backgrounds, `#1E293B` / `#334155` typography, `#0F172A` navy accents, `#991B1B` brick red for critical alerts)
- Running footer on every page with current operator contact info
- A4 strict (296mm with 1mm safety buffer)
- Screen preview at 78% zoom with drop shadow for "floating paper" effect

---

## Report 1 — C-Level

### Purpose

Ad-hoc executive snapshot. The CEO opens it on a phone between meetings. Must be scannable in 2 minutes. NO historical trend (that is the Monthly Progress report's job). NO technical detail (that is Technical Level's job). Pure current-state business posture.

### Layout

Cover + 1 content page = **2 physical A4 pages**. Same structural pattern as the current `exec-onepager` but with a different content model.

### Content blocks

#### Block 1 — Semáforo de Supervivencia

Risk posture traffic light with **business-impact narrative**, not a numerical score. The CEO does not know if "score 70" is good or bad — but they do know what "operation at imminent risk, recovery >48h" means.

**Color mapping:**

- 🔴 **RED** — "Operation at imminent risk. Estimated recovery time from attack: >48h"
- 🟡 **YELLOW** — "Exposed to ransomware via technical debt. Recovery guaranteed but slow."
- 🟢 **GREEN** — "Solid posture. Controls active."

**Collapse logic (Capital Sins — force RED regardless of score):**

Four rules. Each maps to a specific narrative and auto-populates as the CTA #1 in Block 3 when it fires:

| # | Trigger | Narrative | Detection source |
|---|---|---|---|
| 1 | Active threats > 0 | "Invasión en curso" | `machine_threats` table |
| 2 | M365 admin account without MFA (*if M365 tenant connected*) | "Llave maestra sin protección" | `m365_findings` |
| 3 | LAPS coverage == 0% | "Sin barreras internas" | `hygiene.Findings` where `Status = 'NoLAPS'` |
| 4 | RDP exposed to internet on server-role hostname (`*-DC*`, `*-SRV*`, `*-APP*`) | "Puerta abierta a internet" | `machine_ports` + `machines.Hostname` pattern match |

**Fallback (no capital sin fires) — score-based:**

- Score ≥ 85 → GREEN
- Score 60–84 → YELLOW
- Score < 60 → RED

**Display format:** color pill + reason line + recovery time estimate.

**Edge case:** The M365 admin MFA rule only fires if the org has a connected M365 tenant (Phase 4 feature). Clients without M365 connected cannot trigger rule 2. This is documented as a limitation, not a bug. A future agent enhancement to detect local-admin MFA is out of scope.

#### Block 2 — 3 Business KPIs

Three KPIs, all expressed in business language (no technical jargon):

**KPI 1: Costo de Exposición** (static industry benchmark)

Shows a fixed industry benchmark rather than inventing a number. Format:

> **USD 1.2M**
> Impacto financiero estimado
>
> *Basado en IBM Cost of a Data Breach 2024 para PyMEs del sector. Su infraestructura actual presenta **{N} vectores críticos** que coinciden con los casos de estudio de este benchmark.*

The personalized tail ("su infraestructura presenta N vectores críticos") converts a generic benchmark into a specific warning without fabricating a per-client number. The value comes from the benchmark reference (IBM, Ponemon, or equivalent recognized source) — picking a commercial report is a write-time decision, not locked in the spec.

**KPI 2: Cobertura de Activos** ("Los 4 Fantásticos")

Average of 4 binary signals per machine:

```
coverage = avg(
    % of machines with BitLocker active,
    % with TPM present and enabled,
    % with LAPS deployed,
    % with Microsoft Defender running
)
```

Display: `"87% — 104 of 120 machines fully protected"`.

Explicit decision: **do not add more signals**. The Four Horsemen (BitLocker + TPM + LAPS + Defender) are the non-negotiable pillars of endpoint hygiene. Adding ASR, AppLocker, Credential Guard, etc. dilutes the metric and the number always reads as low, which frustrates the CEO.

**KPI 3: Evolución del Riesgo** (arrow)

Delta vs previous month's average score. Reuses the `previousMonthScore` loading logic from the current Monthly Briefing implementation (already in `GenerateOrgReportAsync`).

- `▲ +4.2 pts` (green) — improvement
- `▼ -2.1 pts` (red) — deterioration
- `= 0.0 pts` (gray) — stable
- `— BASELINE` (gray) — first reporting period (no historical data)

#### Block 3 — Executive Decisions Required

**Max 2 CTAs** shown, generated via **hybrid model Z**: auto-detected by rule engine + editable by the operator before export.

**Priority ranking:** Incidentes → Hardening → Budget → Risk. When multiple rules fire, this order decides which 2 are surfaced.

**12-rule auto-detection catalog:**

| # | Trigger | CTA text | Category |
|---|---|---|---|
| 1 | `active_threats > 0` | "Aprobar engagement de IR forense inmediata" | Incidentes |
| 2 | `LAPS_coverage < 50%` | "Aprobar rollout de LAPS en {N} equipos" | Hardening |
| 3 | `bitlocker_missing > 30%` of fleet | "Aprobar programa de cifrado de discos — {N} equipos exponen datos ante robo" | Hardening |
| 4 | `privileged_accounts > threshold` | "Aprobar revisión trimestral de cuentas privilegiadas ({N} actuales)" | Hardening |
| 5 | `password_never_expires > 10` | "Firmar política de rotación de contraseñas ({N} cuentas violan compliance)" | Hardening |
| 6 | `kerberoastable_accounts > 0` | "Aprobar remediación de cuentas Kerberoastables ({N})" | Hardening |
| 7 | `rdp_exposed_to_internet > 0` | "Aprobar VPN/RD Gateway mandatorio — {N} puertos RDP expuestos" | Hardening |
| 8 | `smbv1_or_ntlm_enabled > 10` machines | "Aprobar arranque de telemetría pasiva de 90 días para deprecación segura" | Hardening |
| 9 | `m365_mfa_coverage < 100%` (*if M365 connected*) | "Firmar enforcement de MFA obligatorio en administradores M365" | Hardening |
| 10 | `legacy_os_count > 0` (2008/2003/Win7/Vista) | "Aprobar budget de migración Azure de {N} máquinas con SO fuera de soporte" | Budget |
| 11 | `domain_functional_level IN [2003, 2008]` | "Aprobar upgrade del dominio AD a funcional 2016+" | Budget |
| 12 | `critical_cves > 5` (when CVE data available; optional) | "Aprobar sprint de parcheo crítico — {N} vulnerabilidades CVSS 9+" | Budget |

**Capital Sin → CTA linkage:** when a capital sin from Block 1 fires, the corresponding CTA is automatically promoted to position #1 regardless of general priority ordering. This ties the diagnosis to the action directly.

**Empty state** (zero rules fire, no capital sin, solid posture): show a single positive card:

> ✅ **Postura sólida — sin decisiones ejecutivas pendientes**
> Este mes no requiere acción del CEO. El programa de hardening continúa de forma rutinaria.

The block stays present with a positive tone so the report has a consistent visual closure regardless of state.

**Persistence:**

New table `executive_ctas` stores the generated + edited CTAs per org per reporting period:

```sql
CREATE TABLE executive_ctas (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id     UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    period_start        DATETIME2(2)     NOT NULL,
    auto_detected_rule  NVARCHAR(100)    NULL,   -- which rule fired, null if manual
    priority_category   VARCHAR(20)      NOT NULL, -- Incidentes|Hardening|Budget|Risk
    title               NVARCHAR(200)    NOT NULL,
    description         NVARCHAR(2000)   NOT NULL,
    is_suppressed       BIT              NOT NULL DEFAULT 0,
    is_manual           BIT              NOT NULL DEFAULT 0,
    -- audit columns
    created_by          UNIQUEIDENTIFIER NOT NULL,
    created_at          DATETIME2(2)     NOT NULL,
    modified_by         UNIQUEIDENTIFIER NULL,
    modified_at         DATETIME2(2)     NULL,
    deleted_by          UNIQUEIDENTIFIER NULL,
    deleted_at          DATETIME2(2)     NULL
);
```

**Portal UI:** new CTA preview tab in org detail view. When the operator clicks "Generate C-Level", a modal appears showing the auto-detected CTAs for the current reporting period. The operator can: edit the text, suppress individual CTAs (checkbox), add manual CTAs on top. On "Confirm", the C-Level PDF generates with the final set.

### Data dependencies

- `assessment_runs` — global score, grade, duration, timestamps
- `control_results` — critical / high fail count, per-control details
- `machines` — BitLocker, TPM, IP, OS name (legacy OS detection), hostname (RDP-on-server pattern match)
- `ad_hygiene` — LAPS coverage, kerberoastable, privileged count, password never expire, domain functional level
- `machine_ports` — RDP 3389 with risk label
- `machine_threats` — active threat signatures
- `m365_findings` — admin MFA status (optional, only if tenant connected)
- `executive_ctas` — new table, operator-edited CTAs per period

---

## Report 2 — Technical Level

### Purpose

Monday-morning backlog for the SysAdmin or IT Manager. Dry, instructive, data-driven. The "repair manual for the week". NO narrative framing, NO business impact language, NO CTAs — those belong to C-Level.

### Layout

Cover + dynamic content pages (5–15 total depending on fleet size and number of findings). Paginated where necessary (Asset Matrix paginates ~25 hosts per page).

### Content blocks

#### Block 1 — The Asset Matrix

Sortable table, one row per machine, ordered worst-to-best by individual score so the tech starts with the most urgent host.

**Columns:**

| Column | Source | Notes |
|---|---|---|
| Hostname | `machines.Hostname` | Monospace, bold |
| OS Status | `machines.OsName` | Label: Supported / Outdated / End-of-Life |
| Critical Findings | `control_results` count WHERE `severity IN (critical, high) AND status = 'fail'` grouped by `run_id` | Number |
| Agent Heartbeat | `machines.LastSeenAt` | "2h ago", "3 days ago", "12 days ago" — colored if >7 days |
| Individual Score | `assessment_runs.GlobalScore` for latest run | 0–100, color-coded |

**Note:** the column originally proposed as "Last Backup" is replaced by **Agent Heartbeat**. Kryoss does not integrate with backup tools (Veeam, Datto, Acronis, Azure Backup) — honestly representing what we track is better than showing an empty column. A machine silent for >7 days is operationally equivalent to a blind spot, which is what a tech cares about.

#### Block 2 — Top 10 Critical Findings

Pre-prioritized list of the 10 highest-impact findings across the fleet. Not 500. Not CVE-based — Kryoss does compliance control scanning, not vulnerability scanning.

**Columns:**

| Column | Source |
|---|---|
| Finding name | `control_defs.name` |
| Severity | `control_defs.severity` (critical / high) |
| Machines affected | distinct count of `control_results.run_id` where fail + same `control_def_id` |
| Fix instructions | `control_defs.remediation` (already populated in the seed data) |

**Ordering:** Critical > High, then by machine count descending.

**Fixed 1 page** (10 rows — if fewer than 10 findings exist, rows collapse).

**Explicit decision:** no CVE enrichment. Adding "CVE-2017-0144 (EternalBlue)" tags next to SMBv1 findings is a false-precision move — Kryoss is not a vulnerability scanner and should not pretend to be. A future project can add real CVE scanning as a separate integration; it is explicitly out of scope here.

#### Block 3 — Los 6 de Hierro (Hardening Audit)

Six thematic sub-lists, each showing the hostnames affected by that specific hygiene failure. Each sub-list is its own visual section with a colored header and icon. If a category has zero failures, the section shows `✅ Todos los equipos cumplen` instead of being hidden.

| # | Category | Signal | Source |
|---|---|---|---|
| 1 | 🔒 **Cifrado** | Equipos sin BitLocker activo | `machines.Bitlocker != true` |
| 2 | 📡 **Protocolos** | Equipos con SMBv1 o NTLMv1 habilitado | `control_results` pattern match |
| 3 | 💾 **Hardware** | Equipos sin TPM o con TPM desactivado | `machines.TpmPresent != true` |
| 4 | 🔑 **Identidad** | Equipos sin LAPS desplegado | `hygiene.Findings where Status = 'NoLAPS'` |
| 5 | 🧹 **Higiene** | Cuentas con "Password Never Expires" | `hygiene.PwdNeverExpire` |
| 6 | 🛡️ **Endpoint** | Equipos con Defender deshabilitado o sin protección activa | `machines.DefenderEnabled != true` or equivalent control |

The 6 categories intentionally overlap partially with the "4 Fantásticos" of the C-Level Block 2 (BitLocker, TPM, LAPS, Defender) but add SMBv1/NTLMv1 (Protocolos) and Password Never Expires (Higiene) which are technical details the CEO does not need to see but the tech does.

### Data dependencies

- `machines` — hostname, OS, last_seen_at, BitLocker, TPM, Defender flags
- `assessment_runs` — individual scores
- `control_results` + `control_defs` — failing controls with remediation text
- `ad_hygiene.Findings` — NoLAPS, PwdNeverExpire, domain info

---

## Report 3 — Monthly Progress

### Status: DEFERRED

**Reason:** the report depends on NinjaOne (or equivalent RMM) integration data that Kryoss does not currently have. Attempting to ship Monthly Progress with only Kryoss-native data produces a weaker report than the current "Monthly Briefing (MRR)" because it cannot cite things like "245 patches installed this month" or "14 health alerts resolved".

**Trigger to resume:** Monthly Progress brainstorm and implementation resumes once the NinjaOne RMM integration is live. At that point the report gains:

- Patches installed per device per month
- Scripts executed / maintenance tasks logged
- Alerts handled (count + mean time to resolution)
- Engineering hours billed
- True uptime / downtime tracking

**Target structure (not locked — revisit post-NinjaOne):**

- **Hoja 1 — Tablero de Evolución** — score trend sparkline, "Victory Lap" highlights, current semáforo (reused from C-Level)
- **Hoja 2 — Motor de Mantenimiento** — RMM activity summary, uptime/heartbeat, MSP work performed
- **Hoja 3 — Roadmap y Decisiones** — pending CTAs (reiteration from C-Level), next-month objectives

**Interim behavior:** the current `Monthly Briefing (MRR)` dropdown entry stays in place with its existing content (already updated to the Big 4 light palette in a prior turn). The success criteria list below describes the **final end state** after the NinjaOne-blocked rebuild ships; during the transition the dropdown will temporarily show 5 entries (the 4 new + the legacy Monthly Briefing). Once the Monthly Progress rebuild lands, the legacy entry is replaced and the dropdown collapses back to 4. MRR customers continue to receive the existing format throughout, without disruption.

---

## Report 4 — Preventas (consolidated)

### Status: consolidation of 2 existing variants — NO content change

Today's dropdown has two separate entries that both belong to the "prospect / sales" use case:

- `presales` — 6-page proposal with Executive Summary, Primary Threat Vectors, Technical Evidence (top 10 failures), Methodology (90-Day Safe Deprecation), Roadmap
- `presales-opener` — 2-page aggressive "door opener" with Risk Score, Top 4 Compromise Vectors, Frictionless Audit, 90-Day Safe Deprecation CTA

Both are already approved and working in production. The user confirmed in prior turns that the Presales reports are "bien" and not to touch their content. The only concern is that having 2 separate entries in the dropdown violates the 4-type baseline.

### Target state

Single dropdown entry `Preventas` with a **tone** sub-parameter:

- `Preventas · Opener` → renders the current `presales-opener` (2-page hook)
- `Preventas · Detailed` → renders the current `presales` (6-page proposal)

**Routing:** `/v2/reports/org/{orgId}?type=preventas&tone=opener|detailed&framework=...&lang=...`

**Portal UI:** when `type=preventas` is selected, a secondary dropdown for `tone` appears next to it. Default tone = `opener` (faster to generate, lower commitment — matches how sales typically introduces the product).

**Backward compatibility:** the old `type=presales` and `type=presales-opener` values remain accepted as aliases for 1–2 deploy cycles to avoid breaking any bookmarked URLs or external integrations.

### Content dependencies

No change. Both renderings use the same data sources they already use today (allResults, frameworkScores, enrichment, hygiene, machines, etc).

---

## Cross-cutting concerns

### Bilingual content

All 4 reports accept `?lang=en|es` query parameter.

- C-Level and Preventas already support EN/ES via dictionary-based i18n (`_enStrings` / `_esStrings` in `ReportService.cs`).
- Technical Level currently does not — needs a new dictionary block added (~30-40 keys).
- Date/number formatting respects locale (e.g., `MMMM dd, yyyy` for EN, `dd de MMMM de yyyy` for ES).
- Fallback: if a key is missing in ES, show the EN string. Never hide content.

### Framework filter

All 4 reports accept `?framework=NIST|CIS|HIPAA|ISO27001|PCI-DSS|all` query parameter.

- When a specific framework is selected, `allResults` and `frameworkScores` are filtered to that framework's controls only.
- Report title adapts: "C-Level — HIPAA Compliance Briefing" instead of just "C-Level Security Briefing".
- Data computations (critical counts, coverage, capital sins) operate on the filtered subset.

### Per-run reports deprecation

The per-run report endpoints and builders are removed:

- `BuildTechnicalReport(run, ...)` — DELETED
- `BuildExecutiveReport(run, ...)` — DELETED
- `BuildPresalesReport(run, ...)` — DELETED
- `GET /v2/reports/{runId}` — endpoint returns `410 Gone` with a brief deprecation message pointing to the org-level alternative

Rationale: the per-run use case ("diagnose one specific machine") is better served by the portal's live machine detail view with drill-down into control results. A one-time generated PDF for a single machine is a rare workflow and does not justify maintaining three duplicate report builders.

---

## Migration approach (high-level)

This is the strategic sequence. The detailed implementation plan will be produced by the `writing-plans` skill in the next phase.

1. **Build C-Level from scratch** — new builder, new CTA rule engine, new `executive_ctas` table, new portal UI for CTA preview/edit
2. **Rebuild Technical Level** — replace the body of the existing `BuildOrgTechnicalReport` method with the new 3-block structure (Asset Matrix, Top 10 Findings, Los 6 de Hierro). Preserves the switch case name, preserves the dropdown entry label, only the content changes.
3. **Consolidate Preventas** — refactor routing + dropdown, no content changes. Keep legacy aliases for 2 deploys.
4. **Delete per-run builders** — remove code paths, deprecate the `{runId}` endpoint with a 410 response.
5. **Defer Monthly Progress rebuild** — explicit status marker in the codebase and CLAUDE.md so future sessions know Monthly is waiting for NinjaOne.

Phases 1 through 4 are independent and can be worked in parallel or sequence. Phase 1 has the largest scope (new table, new rule engine, new portal UI) and is the only one that requires a SQL migration. Phase 2 is pure refactor within a single C# file. Phase 3 is ~1 turn. Phase 4 is cleanup, last.

---

## Success criteria

1. Portal dropdown shows exactly 4 entries: **C-Level**, **Technical Level**, **Monthly Progress** (disabled/placeholder until NinjaOne), **Preventas**.
2. Each report generates with valid data from the existing Kryoss DB (plus `executive_ctas` for C-Level).
3. C-Level traffic light correctly collapses to RED on any of the 4 capital sins.
4. C-Level never shows more than 2 CTAs; empty state shows the positive closure card.
5. Technical Level renders complete Asset Matrix (paginated), Top 10 Findings with `remediation` text, and all 6 iron categories (with `✅ Todos los equipos cumplen` for empty categories).
6. Preventas works via both `?tone=opener` and `?tone=detailed`. Legacy `?type=presales` and `?type=presales-opener` still resolve during the deprecation window.
7. All 4 reports respect the `?framework=` and `?lang=` filters.
8. Running footer shows current operator contact info on every page of every report.
9. Big 4 light palette applied consistently (no dark-mode leakage).
10. The per-run `GET /v2/reports/{runId}` endpoint returns `410 Gone`.
11. Zero breakage for existing `Monthly Briefing (MRR)` users during the transition — the current Monthly Briefing entry stays available and rendering correctly until Phase 5.

---

## Out of scope

Documented here to prevent scope creep during implementation:

- **NinjaOne / RMM integration** — separate project, gates the Monthly Progress rebuild
- **CVE scanner integration** — separate project. Technical Level's Top 10 is control-based, not CVE-based
- **Backup tool integration** (Veeam, Datto, Acronis, Azure Backup) — separate project. The "Agent Heartbeat" column in Technical Level is the honest stand-in
- **True uptime tracking** — separate project. Agent heartbeat is the proxy
- **Local admin MFA detection without M365 tenant** — requires agent payload enhancement. For now, the C-Level capital sin #2 only fires if M365 is connected
- **Benchmark comparison with industry peers** — requires external data feed
- **Server-side PDF generation** — reports stay as HTML + browser Ctrl+P. A server-side Playwright or QuestPDF implementation was discussed and deferred
- **Scheduled report delivery via email** — separate project
- **Per-run (single machine) reports** — explicitly deprecated by this spec
