# Reports Redesign — Brief for next session

## Context

The Kryoss platform now has 840 security controls + 50 M365 checks + AD hygiene
+ port scan + hardware/software inventory + protocol usage audit. The data
collection is enterprise-grade. **The reports are lagging.**

## What exists today

### Backend

- **`KryossApi/src/KryossApi/Services/ReportService.cs`** — **2399 lines**.
  Renders HTML reports with Brand 2025 styling (Montserrat, TeamLogic IT
  colors, framework score gauges, AD hygiene section). Self-contained HTML,
  no external deps.
- **`KryossApi/src/KryossApi/Functions/Portal/ReportsFunction.cs`** — 126 lines.
  Two endpoints:
  - `GET /v2/reports/{runId:guid}?type=technical|executive|presales` — per-run report
  - `GET /v2/reports/org/{orgId:guid}?type=...` — org-wide report

### Frontend

- `KryossPortal/src/components/org-detail/ReportsTab.tsx` — list + generate button
- `KryossPortal/src/components/reports/ReportGenerator.tsx` — generator UI

## Known pain points (to investigate first thing in the next session)

1. **Only 3 report types.** technical / executive / presales is too coarse for
   an MSP selling remediation packages.
2. **Framework scores are flat.** A client wants to see NIST 800-171 coverage,
   HIPAA §164.312 coverage, ISO 27001 A.12 coverage — with drill-down by
   control family.
3. **No trend / delta report.** "Your score went from 25% to 31% this month
   because we fixed X, Y, Z" is the conversation that keeps the MSP paid.
4. **Protocol Usage report does not exist.** The 90-day NTLM/SMB1 data we
   collect has no dedicated deliverable. Huge missed upsell — this is literally
   a $5K/machine engagement topic.
5. **M365 findings not in any report.** 50 checks run, zero hit the PDF.
6. **AD Hygiene section is a table dump.** No prioritization, no "fix
   these 5 things first" ranking.
7. **Hardware / Software inventory is not linked to controls.** e.g. "this
   machine fails BitLocker check AND the disk is HDD" — that's a buying
   signal for SSD upgrades but nothing connects them.
8. **No executive one-pager.** The "executive" type is basically the technical
   report with fewer rows. A real exec one-pager is 1 page: score, 3 top
   risks, 3 quick wins, estimated remediation hours.

## Starting steps for the next session

1. **Read before writing.** Open `ReportService.cs` (all 2399 lines) +
   `ReportsTab.tsx` + `ReportGenerator.tsx`. Understand the current
   templates, the data sources, the HTML layout patterns.
2. **Look at an actual rendered report.** Open the portal → any org with
   assessment runs → Reports tab → generate one of each type → read the
   HTML output. That is the "before" state.
3. **Ask Federico what he wants first.** Layout redesign or new report
   types? Slot info (AD hygiene priority, M365, protocol usage) or
   rebuild the existing sections? Remove sections nobody reads?
4. **Decide on scope** BEFORE touching code. 2399 lines of HTML rendering
   is easy to break and hard to test. Any redesign should be incremental
   with the existing types still working while new ones are added.

## Data available in the DB for reports

(As of 2026-04-14 after v1.5.1 deploy — reference for next session)

| Table | What it has |
|---|---|
| `assessment_runs` | Global score, grade, pass/warn/fail counts, started_at |
| `control_results` | Per-control verdict (pass/warn/fail/info) |
| `run_framework_scores` | Per-framework score (NIST/CIS/HIPAA/ISO27001/PCI-DSS) |
| `machine_snapshots` | ~25 hardware fields + raw JSON blocks |
| `machine_disks` | Per-drive inventory |
| `machine_ports` | Open ports with risk level |
| `ad_hygiene` | Stale/dormant/privileged/kerberoastable/LAPS findings |
| `m365_tenants` + `m365_findings` | 50 M365/Entra ID checks |
| `machine_software` | Installed apps (600+ commercial detection) |
| `organizations` | Including `protocol_audit_enabled` flag + timestamps |

## Useful file pointers

- Brand 2025 layout: `Scripts/Audit/Kryoss-Report-Template.html` (reference)
- Brand assets: `Scripts/assets/TLITLogo.svg`
- Decision log: `CLAUDE.md` "Active decisions" section

## Out of scope for next session (do NOT get distracted)

- Agent changes (v1.5.0 + v1.5.1 are done and stable)
- New check types (840 controls is enough)
- Portal auth / routing (working fine)
- SQL migrations unrelated to reports
