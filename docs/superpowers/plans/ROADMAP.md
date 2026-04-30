# Kryoss Master Roadmap — Orchestrator

> **Role:** Active queue + current state. What to execute NOW.
> **Companion files:** `docs/BACKLOG.md` (all pending work), `docs/BITACORA.md` (decisions + history), `docs/FEATURES-SHIPPED.md` (what's built)
>
> **Last updated:** 2026-04-28
> **Owner:** Federico
> **Orchestrator:** Claude (caveman mode default)

---

## Current State (2026-04-28)

| Component | Version | Key Metric |
|-----------|---------|------------|
| API | 1.34.3 | 163 endpoints, 41 report blocks, 16 recipes, 7 CA pipelines, ~42 feature inventory entries |
| Agent | 2.10.0 | 13 engines, 22 services, 918 controls (827 active), zero Process.Start |
| Portal | 1.18.3 | 8 pages, 18 org tabs, 7 CA tabs, 20 API modules |
| DB | 84 migrations | 154+ tables, 24 seed files |

**Last session (2026-04-28):** CA-LH Lighthouse baseline gap closure (15 new checks), CA-EXO Exchange Online REST API (3 InvokeCommand checks), CA-TIER license tier detection (22 features), PBI consent re-enabled, Feature Inventory ~42 entries with `licenseTier`.

**In progress:** Nothing.

---

## Active Queue v3 — Report & Data Gap Closure

| # | Phase | Priority | Est | Status | What |
|---|-------|----------|-----|--------|------|
| 1 | **RP-RECIPE-OVERHAUL** | P1 | 0.5s | 🟡 6/9 done | Remaining: SNMP->Network/Inventory, Topology->Network, Hypervisor->Inventory |
| 2 | **GAP-REM** Remediation Action Plan | P0 | 1-2s | ⚪ | Map Control_ID->PowerShell fix, inject into Technical Report |
| 3 | **RP-DC** AD & Domain Health Report | P1 | 1s | ⚪ | New recipe: 100 DC controls + replication + FSMO |
| 4 | **RP-WAN** WAN & Network Health Reports | P1 | 1s | ⚪ | SNMP + topology + WAN into NetworkRecipe |
| 5 | **RP-VULN** Vulnerability & Patch Report | P1 | 1s | ⚪ | CVE + patch + external scan standalone recipe |
| 6 | **SEC-SHADOW** Shadow Admin Audit | P1.5 | 1s | ⚪ | Domain root ACLs + AdminSDHolder |
| 7 | **SEC-BACKUP** Backup Isolation Check | P1.5 | 1s | ⚪ | Ransomware resilience probe |
| 8 | **CA-COPILOT** Copilot Exposure Lens | P1.5 | 1s | ⚪ | SharePoint "Everyone" perms audit |
| 9 | **SEC-EGRESS** Egress Filtering Audit | P1.5 | 1s | ⚪ | Non-standard outbound from servers |
| 10 | **RP-INSURE** Cyber Insurance Readiness | P1.5 | 1s | ⚪ | Insurance questionnaire score from existing data |
| 11 | **CA-OAUTH** SaaS/OAuth Governance | P1.5 | 1s | ⚪ | M365 3rd-party excessive permissions |
| 12 | **DASH-RISK** Risk Cost Dashboard | P2 | 1-2s | ⚪ | Financial weight per finding |
| 13 | **RP-TREND** Historical Trend Tracking | P2 | 1s | ⚪ | `report_history` table, score over time |
| 14 | **RP-INFRA** Infrastructure Report (IA-10) | P2 | 1s | ⚪ | Hypervisors + SNMP + topology + WAN |

**From Queue v2 remaining:**
| Phase | Priority | Status | What |
|-------|----------|--------|------|
| **CA-17** Auto-remediation handlers | P1 | ⚪ | MFA enable + disable legacy auth + Customer Lockbox |
| **SH-HARDEN** Security final | P1-P2 | ⚪ | SPKI enforce (SH-07) + anomaly scoring (SH-10) |
| **CA-16A** ConnectWise PSA | 🔴 suspended | — | Vendor not decided |

**Dependencies:** RP-DC needs DC-02+03 (shipped). RP-WAN needs SNMP+topology+WAN (shipped). RP-INFRA needs IA-1 (shipped). SEC-SHADOW needs DcEngine (shipped). CA-COPILOT needs Graph consent (shipped).

**Design philosophy:** Every report answers: "What's wrong? How much? How to fix?" Shift Zero = FAIL control shows inline PowerShell fix in Technical Report.

---

## Queue Completion Log

| Queue | Items | Shipped |
|-------|-------|---------|
| v1 (10 items) | IA-0, IA-11, CA-13, CA-14, IA-1, IA-2 P1, CA-15, A-SVC, A-NET, RP-EXPANSION | All shipped 2026-04-26 |
| v2 (10 items) | SH-02, SH-BATCH, IA-3, A-01, A-02, DC-02+03, SEC-REM-FW, Ookla speed test | 8/10 shipped. CA-16A suspended, CA-17+SH-HARDEN pending |
| v3 (16 items) | GAP-VUL, RP-RECIPE-OVERHAUL partial, SEC-REM-FW, CA-LH, CA-EXO, CA-TIER | 6/16 shipped. 10 remaining |

---

## Session Start Checklist

1. Read `CLAUDE.md` (master) + relevant sub-CLAUDE.md
2. Read this file (ROADMAP.md) — check Active Queue top item
3. If needed: read `docs/BACKLOG.md` for full pending list
4. If needed: read `docs/FEATURES-SHIPPED.md` for implementation details
5. Execute top queue item
6. On completion: update this file (status + date) + `docs/BITACORA.md` (decisions + session summary) + `docs/FEATURES-SHIPPED.md` (new feature details)
