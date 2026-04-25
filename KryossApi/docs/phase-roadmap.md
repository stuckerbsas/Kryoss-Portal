# Kryoss Platform — Phase Roadmap

**Owner:** TeamLogic IT / Geminis Computer S.A.
**Last updated:** 2026-04-19
**Status:** Phase 1 COMPLETE, Phase 4 COMPLETE (exceeded scope), Phases 2/3/5/6 pending

This document is the authoritative reference for what is in scope
per phase and what stays out. Whenever you're tempted to expand
scope mid-phase, re-read this file first.

---

## Guiding principle

> A real client has **workstations + servers + cloud + network**.
> To sell the full audit we eventually need all four sources.
> Phase 1 (workstations) and Phase 4 (cloud) shipped first because
> they cover ~90% of SMB client attack surface.

Ship narrow → validate → expand.

---

## Phase 1 — Workstation ✅ COMPLETE

**Scope:** Windows 10 + Windows 11 endpoints only.
**Status:** Shipped. Agent v1.5.1, Portal deployed, 4-type reports live.

### What shipped

- **647 active control_defs** (SC-001..161, BL-0001..0486)
- Linked to platforms `W10`, `W11`, `MS19`, `MS22`, `MS25`
- 11 collector engines: `registry` (356), `command` (211), `auditpol` (24),
  `firewall` (21), `service` (11), `netaccount` (5), `secedit` (2),
  `eventlog` (4), `certstore` (6), `bitlocker` (4), `tpm` (3)
- Framework coverage (endpoint controls):
  | Framework | Tagged | % |
  |---|---|---|
  | NIST     | 647 | 100.0 |
  | CIS      | ~637 | ~98.5 |
  | HIPAA    | ~320 | ~49.5 |
  | ISO27001 | ~179 | ~27.7 |
  | PCI-DSS  |  ~18 |  ~2.8 |

### Agent (v1.5.1)

| Feature | Version | Status |
|---|---|---|
| 11 collector engines (647 controls) | v1.0 | ✅ |
| Network discovery (AD/ARP/subnet) + port scan | v1.2.2 | ✅ |
| AD Hygiene audit (stale, kerberoastable, delegation, LAPS) | v1.2.2 | ✅ |
| Binary patching (org-specific .exe via sentinels) | v1.2.2 | ✅ |
| Multi-disk inventory | v1.2.2 | ✅ |
| Hardware enrichment (~25 fields) | v1.2.2 | ✅ |
| Software inventory (600+ commercial apps) | v1.2.2 | ✅ |
| PsExec removed, deploy via GPO/NinjaOne/Intune | v1.3.0 | ✅ |
| All 5 batch engines converted to native .NET P/Invoke | v1.4.0 | ✅ |
| Zero `Process.Start` — `NativeCommandEngine` | v1.4.0 | ✅ |
| Binary trimmed 67 MB → 11.9 MB (82% reduction) | v1.4.0 | ✅ |
| 52 legacy command controls execute natively (TLS, UserRights, AppLocker) | v1.5.0 | ✅ |
| Protocol Usage Audit (NTLM + SMBv1, 90-day retention) | v1.5.1 | ✅ |
| Stateless cycle (registry wiped after successful upload) | v1.4.0 | ✅ |
| Envelope encryption (RSA-OAEP-256 + AES-256-GCM) | v1.4.0 | ✅ |
| SPKI pinning (log-only until pins populated) | v1.4.0 | ✅ |

### Portal (React + Vite + shadcn/ui)

| Feature | Status |
|---|---|
| MSAL auth + auto-provision Entra ID users | ✅ |
| Org/machine fleet management | ✅ |
| Machine detail (30+ fields, snapshot history) | ✅ |
| Hardware Inventory tab (org-level) | ✅ |
| Software Inventory tab (org-level, 600+ apps) | ✅ |
| Agent download (patched binary from portal) | ✅ |
| Protocol Usage tab (NTLM/SMBv1 audit toggle) | ✅ |
| Dashboard (KPIs, grade distribution, framework scores) | ✅ |
| 4-type reports (C-Level, Technical, Preventas, Monthly Progress) | ✅ |
| Brand 2025 layout (Montserrat, framework gauges, AD hygiene) | ✅ |

### Backend

| Feature | Status |
|---|---|
| .NET 8 Azure Functions + EF Core 8 | ✅ |
| API Key + HMAC (agent) / Bearer JWT (portal) auth | ✅ |
| RLS via SESSION_CONTEXT | ✅ |
| Server-side evaluation engine | ✅ |
| Managed Identity → Azure SQL | ✅ |
| Error sanitization middleware | ✅ |
| Nonce cache (replay protection) | ✅ |
| Hardware fingerprint (HWID binding) | ✅ |
| PlatformResolver (OS string → platform code) | ✅ |
| Enrollment (multi-use codes, re-enrollment) | ✅ |

---

## Phase 2 — Windows Server Member ✅ COMPLETE

**Trigger:** Client demand for server audits.
**Status:** Shipped. 80 server-specific controls + DC/Member detection + platform linkage.

### What shipped (2026-04-19)

- **80 server-specific controls** (`seed_010_server_controls.sql`, SRV-001..SRV-080):
  IIS hardening, SMB security, RDP hardening, DNS Server, DHCP Server,
  Hyper-V security, Print Server, File Server, WSUS, Server Core detection,
  general server hardening
- `product_type` column on `machines` (migration `040_dc_platform_support.sql`)
- Agent detects `ProductType` via WMI `Win32_OperatingSystem.ProductType`
- Agent sends `productType` in enrollment request and hardware payload
- `PlatformResolver` resolves DC19/DC22/DC25 when ProductType=2
- All baseline + SRV controls linked to DC platforms via `040_dc_platform_support.sql`

### Remaining work

1. **Agent:** role detection via WMI `Win32_ServerFeature` (conditional checks)
2. **Reports:** CIS Server 2022/2019 L1 Member Server Benchmark

---

## Phase 3 — Domain Controllers ✅ COMPLETE

**Trigger:** Client with on-prem AD requests DC audit.
**Status:** Shipped. 40 DC-specific controls + DC detection + full platform linkage.

### What shipped (2026-04-19)

- DC detection via `ProductType=2` in PlatformResolver
- **40 DC-only controls** (`seed_013_dc_controls.sql`, DC-001..DC-040):
  - AD replication & health (6): repadmin, SYSVOL DFS-R, FSMO, DCDiag, DNS, NTDS integrity
  - LDAP security (5): signing, channel binding, integrity, LDAPS, machine account passwords
  - Kerberos security (6): krbtgt age, ticket lifetime, pre-auth, AES-only, ZeroLogon
  - DC hardening (13): AD Recycle Bin, Protected Users, fine-grained PWD policies, Print Spooler,
    unconstrained delegation, internet access, auth policies/silos, NTLM audit, AdminSDHolder,
    time sync, tombstone lifetime, domain functional level
  - DNS security (5): secure dynamic updates, scavenging, forwarders, DC firewall, DNSSEC
  - DC audit (5): DS Access, DS Changes, DS Replication, Kerberos Auth, Kerberos Service Ticket
- DC controls linked to DC19/DC22/DC25 only (not workstations or member servers)
- All 647 baseline + 80 SRV controls also linked to DC platforms
- AD Hygiene audit (agent v1.2.2) covers runtime AD security posture

### Control totals post-Phase 2+3

| Platform | Controls |
|---|---|
| W10/W11 | 647 (baseline) |
| MS19/MS22/MS25 | 727 (647 baseline + 80 SRV) |
| DC19/DC22/DC25 | 767 (647 baseline + 80 SRV + 40 DC) |

### Deploy order

1. `sql/seed_010_server_controls.sql` — 80 SRV controls → MS19/MS22/MS25
2. `sql/seed_013_dc_controls.sql` — 40 DC controls → DC19/DC22/DC25
3. `sql/040_dc_platform_support.sql` — product_type column + copies MS19 controls to DC platforms

---

## Phase 4 — Cloud Workspace Hardening ✅ COMPLETE

**Original scope:** M365 / Entra ID / Defender / Purview (~120 Graph controls).
**Actual delivery:** Massively exceeded original scope. Full Cloud Assessment platform.

### What shipped

#### Cloud Assessment Platform (CA-1 through CA-12)

| Feature | Ticket | Status |
|---|---|---|
| M365 admin consent flow (multi-tenant app) | CA-1 | ✅ |
| 50 M365/Entra ID security checks (was 30) | CA-1 | ✅ |
| Cloud Assessment scan engine (7 pipelines) | CA-4 | ✅ |
| Identity pipeline (Entra ID P1/P2, CA, PIM, MFA, lifecycle) | CA-4 | ✅ |
| Endpoint pipeline (Intune compliance, Defender for Endpoint) | CA-4 | ✅ |
| Data pipeline (Purview, DLP, sensitivity labels, SharePoint) | CA-4 | ✅ |
| Productivity pipeline (adoption, licensing, Copilot readiness) | CA-4 | ✅ |
| Azure consent flow (Reader RBAC via SPN) | CA-6a | ✅ |
| Azure Infrastructure pipeline (resources, Defender for Cloud, NSG, KeyVault, VMs) | CA-6b | ✅ |
| Power BI governance pipeline (workspaces, capacity, gateways, datasets) | CA-7 | ✅ |
| Compliance framework scoring (8 frameworks) | CA-8 | ✅ |
| Compliance drilldown (per-control finding status) | CA-8 | ✅ |
| Mail flow pipeline (SPF, DKIM, DMARC, MTA-STS, BIMI, forwarding) | CA-9 | ✅ |
| Unified Cloud Connect (single button for M365+Azure+PBI) | CA-10 | ✅ |
| Industry benchmarks (franchise/industry/global) | CA-11 | ✅ |
| Unified Cloud Experience (deprecated CopilotReadiness standalone) | CA-12 | ✅ |
| Kryoss Recommended Baseline framework (95 controls, 108 mappings) | CA-12 | ✅ |
| Compliance recompute endpoint (backfill without re-scan) | CA-12 | ✅ |
| Copilot Lens (D1-D6 from CA scan data) | CA-12 | ✅ |
| Connection status orchestrator | CA-12 | ✅ |

#### Cloud Assessment numbers

| Metric | Count |
|---|---|
| Pipelines | 7 (Identity, Endpoint, Data, Productivity, Azure, PowerBI, MailFlow) |
| Unique findings | ~95 across all pipelines |
| Recommendation generators | 7 (.cs files) |
| Compliance frameworks | 8 (HIPAA, ISO27001, NIST CSF, SOC2, PCI DSS, CIS M365, CMMC L2, KRYOSS Baseline) |
| KRYOSS Baseline controls | 95 (100% finding coverage by design) |
| Graph API permissions | 21 (Application, read-only) |
| Azure checks | 19 (ARM, Defender, Storage, KeyVault, Network, Compute, Policy) |
| Power BI checks | 12 (workspaces, capacity, datasets, gateways, sharing, activity) |
| Mail flow checks | 8 (SPF, DKIM, DMARC, MTA-STS, BIMI, forwarding, stealth rules, shared mailbox) |

#### Portal — Cloud Assessment UI

| Feature | Status |
|---|---|
| Overview tab (area scores, radar chart, top findings) | ✅ |
| Identity/Endpoint/Data/Productivity findings tabs | ✅ |
| Azure Infrastructure tab (exposure alerts, resource donut, Defender bar) | ✅ |
| Power BI tab (workspaces, capacity, datasets, gateways) | ✅ |
| Compliance tab (framework cards, drilldown table) | ✅ |
| Benchmarks tab (radar overlay, verdict pills, leaderboard) | ✅ |
| Remediation tab (status tracking, recommended actions) | ✅ |
| Copilot Lens tab (D1-D6 radar, SharePoint sites, external users) | ✅ |
| Connect Cloud wizard (M365 → Azure → PBI stepper) | ✅ |
| Connection status banner | ✅ |
| Loading spinners on all tabs | ✅ |
| Case-insensitive status/priority badges with proper labels | ✅ |
| Service name formatting (24 services) | ✅ |
| Self-contained HTML benchmark report | ✅ |

#### License handling

All 403/401 errors from Graph API are non-fatal warnings. Scan continues,
findings show "Not Licensed" status. Covers: Entra P2, PIM, Governance,
Internet Access, Private Access, Intune, Defender for Endpoint, Purview,
Power BI admin API.

---

## Phase 5a — Network Diagnostics (no 3rd-party APIs) ✅ COMPLETE

**Status:** Shipped 2026-04-19.

### What shipped

- **Agent `NetworkDiagnostics.cs`** — speed test (HttpClient against `/v1/speedtest`), internal latency (parallel ping sweep, 5 pings per host), route table (WMI `Win32_IP4RouteTable`), VPN detection (adapter type + keyword matching), bandwidth snapshot (IPv4Statistics 1-sec delta), adapter inventory
- **`SpeedTestFunction`** — `GET /v1/speedtest` returns 10 MB random bytes, `POST /v1/speedtest` accepts upload
- **Migration `041_network_diagnostics.sql`** — 3 tables: `machine_network_diag`, `machine_network_latency`, `machine_network_routes`
- **`EvaluationService`** — persists NetworkDiag payload to 3 tables per run
- **`NetworkDiagnosticsFunction`** — `GET /v2/network-diagnostics?organizationId=X` (org list) + `GET /v2/network-diagnostics/{machineId}` (detail)
- **50 network controls** — `seed_042_network_controls.sql` (NET-001..NET-050): speed, latency, VPN, routing, DNS, WiFi, bandwidth, WPAD, NTP, APIPA, hosts file, LLMNR
- **Network Assessment report** — `NetworkBlock` + `NetworkRecipe` (8th report type "network"): speed summary, latency matrix, VPN topology, subnet map, KPIs

## Phase 5b — Network / Perimeter (vendor APIs) (PENDING)

**Trigger:** Enterprise clients asking for full-stack audits.
**Status:** Phase 5a (native diagnostics) done. Vendor API integration not started.

### Remaining work

1. **Vendor API integrations:**
   - Fortinet FortiGate, Sophos, SonicWall, Cisco Meraki, Ubiquiti UniFi (REST APIs)
   - SNMP v3 fallback for managed switches/APs
   - SSH config dump parsing for Cisco IOS/ASA

2. **Controls to add (~30 vendor-specific):**
   - Firewall rule base hygiene, admin interface exposure, firmware currency
   - IDS/IPS, guest VLAN isolation, wireless encryption (WPA3/WPA2)
   - 802.1X, STP protection, port security
   - SNMP community strings, NTP source, syslog forwarding

**Estimated effort:** 4–6 weeks.

---

## Phase 6 — Compliance Attestation & Manual Controls (PENDING)

**Trigger:** Clients asking for audit-grade HIPAA/SOC2 reports.
**Status:** Not started. Cloud Assessment compliance scoring partially covers this need.

### Remaining work

- **Attestation controls table:** checklist items MSP marks manually
- **Evidence upload:** PDF/DOCX attachments per control
- **Review workflow:** technician fills → supervisor signs off
- **HIPAA §164.308 Administrative Safeguards** (full coverage)
- **HIPAA §164.310 Physical Safeguards** (full coverage)
- **SOC 2 Trust Services Criteria** (non-technical controls)

**Estimated effort:** 2–3 weeks (mostly UI).

---

## Agent backlog status

| Item | Original | Current status |
|---|---|---|
| Payload enrichment (raw_* blocks) | P1 | ✅ Mostly done: hardware 25+ fields, multi-disk, software 600+ apps, ports, AD hygiene. Missing: `raw_users`, `raw_network`, `raw_security_posture` as structured raw blocks (data available via dedicated endpoints) |
| Envelope encryption | P2 | ✅ RSA-OAEP-256 + AES-256-GCM wired into ApiClient |
| Binary size / AOT | P3 | ✅ Trimmed to 11.9 MB (82% reduction). Not true AOT but `PublishTrimmed=true` with feature flags |
| Remote mode (PsExec) | P4 | ✅ Removed. Deploy via GPO/NinjaOne/Intune scripts |
| Offline fallback (results) | P4 | ✅ OfflineStore queues results when no connectivity |
| Housekeeping | P5 | 🟡 Partial. Test project still missing. Some cleanup done |

---

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-04-08 | Phase 1 limited to W10/W11 only | MVP focus, 70% of SMB client fleets are workstations |
| 2026-04-08 | Legacy BL-XXX soft-deleted not hard-deleted | FK to `assessment_controls` preserves history |
| 2026-04-08 | `control_platforms` is scope enforcement mechanism | Add servers with a single INSERT seed |
| 2026-04-08 | Agent stays dumb, backend evaluates | Catalog changes without rescanning |
| 2026-04-10 | Easy Auth disabled on func-kryoss | All auth via custom middleware |
| 2026-04-10 | Enrollment codes support multi-use (`maxUses`) | MSPs enroll multiple machines with one code |
| 2026-04-10 | Auto-create default assessment on enrollment | Reduce MSP setup friction |
| 2026-04-10 | Server platforms share W10/W11 controls | Same 647 controls via seed_007c; server-specific deferred |
| 2026-04-10 | Portal uses friendly slug URLs | Frontend-only resolution |
| 2026-04-11 | Agent v1.2.2: self-contained network scan | Single .exe does discovery + ports + hygiene |
| 2026-04-11 | Binary patching via UTF-16LE sentinels | Server-side BinaryPatcher for org-specific .exe |
| 2026-04-13 | M365 Phase 4: admin consent flow | One-click Connect M365, multi-tenant app |
| 2026-04-13 | Auto-provision Entra ID users as viewer | No 403 on first login |
| 2026-04-14 | Agent v1.3.0-1.4.0: zero Process.Start | PsExec deleted, all engines native .NET |
| 2026-04-14 | Binary trimming: 67 → 11.9 MB | PublishTrimmed + feature flags |
| 2026-04-14 | v1.5.0: 52 legacy controls execute natively | TLS, UserRights, AppLocker, inline registry |
| 2026-04-14 | v1.5.1: Protocol Usage Audit (NTLM + SMBv1) | 90-day retention, per-org toggle |
| 2026-04-15 | Reports 4-type baseline consolidation | C-Level, Technical, Preventas, Monthly Progress |
| 2026-04-16 | Cloud Assessment platform (CA-4) | 7 pipelines, ~95 findings, recommendation engine |
| 2026-04-17 | Azure consent + infrastructure pipeline (CA-6) | ARM Reader RBAC, Defender for Cloud, 19 Azure checks |
| 2026-04-17 | Power BI governance pipeline (CA-7) | 12 workspace/capacity/dataset/gateway checks |
| 2026-04-18 | Compliance framework scoring (CA-8) | 7 frameworks + Kryoss Baseline, drilldown UI |
| 2026-04-18 | Mail flow pipeline (CA-9) | SPF/DKIM/DMARC/MTA-STS/BIMI + forwarding rules |
| 2026-04-18 | Unified Cloud Connect (CA-10) | Single button for M365+Azure+PBI |
| 2026-04-18 | Industry benchmarks (CA-11) | Franchise/industry/global with privacy gates |
| 2026-04-19 | Unified Cloud Experience (CA-12) | CopilotReadiness deprecated, Copilot Lens in CA |
| 2026-04-19 | Kryoss Recommended Baseline | 95 controls, 108 mappings, 100% finding coverage |
| 2026-04-19 | Compliance recompute endpoint | Backfill scores for new frameworks without re-scan |
| 2026-04-19 | All license-gated 403s are non-fatal warnings | Scan continues, shows "Not Licensed" |

---

## Reference: current platform metrics (2026-04-19)

### Endpoint catalog

```
Total control_defs:              738
  Active:                        647
  Inactive (legacy soft-deleted): 91

Active by engine:
  registry     356    command     211
  auditpol      24    firewall     21
  service       11    netaccount    5
  secedit        2    eventlog      4
  certstore      6    bitlocker     4
  tpm            3

Platform mapping:
  W10   → 647 controls
  W11   → 647 controls
  MS19  → 647 controls (shared, no server-specific yet)
  MS22  → 647 controls
  MS25  → 647 controls
  DC*   → 0 (Phase 3)
```

### Cloud Assessment catalog

```
Pipelines:              7
Findings (unique):      ~95
Recommendation files:   7

Compliance frameworks:  8
  HIPAA, ISO27001, NIST_CSF, SOC2, PCI_DSS, CIS, CMMC_L2, KRYOSS

KRYOSS Baseline:
  Controls:             95
  Mappings:             ~108 (some PBI features map many-to-one)
  Coverage:             100% by design (1:1 with all findings)

Categories:
  Identity:             16 controls (KRY-ID-001..016)
  Endpoint:             17 controls (KRY-EP-001..025)
  Data Protection:      15 controls (KRY-DA-001..030)
  Productivity:         10 controls (KRY-PR-001..010)
  Azure:                19 controls (KRY-AZ-001..019)
  Power BI:             11 controls (KRY-PB-001..011)
  Mail Flow:             8 controls (KRY-MF-001..008)
```

### API endpoints

```
Agent API (v1):          4 endpoints (enroll, controls, results, hygiene)
Portal API (v2):        ~40 endpoints across 15 function files
Cloud Assessment:       ~15 endpoints (scan, detail, compliance, benchmarks, connect, copilot-lens)
Deprecated:             /v2/copilot-readiness/* (410 Gone, sunset 2026-05-18)
```

### Infrastructure

```
Backend:    func-kryoss (Azure Functions, .NET 8)
Database:   sql-kryoss.database.windows.net / KryossDb
Portal:     zealous-dune-0ac672d10.6.azurestaticapps.net (Azure SWA)
Agent:      11.9 MB self-contained .exe (win-x64)
SQL seeds:  039 migrations/seeds applied
```
