# Features Shipped — Technical Reference

> **Purpose:** What's built and how, organized by component. Read when you need implementation details. Not needed every session.
>
> **Last updated:** 2026-04-28

---

## Codebase Metrics (2026-04-28)

| Pillar | Metric | Count |
|--------|--------|-------|
| **API** | Version | 1.34.3 |
| | HTTP endpoints | 163 |
| | Entity classes | 28 |
| | Services | 50+ |
| | Middleware | 7 (ApiKeyAuth, BearerAuth, RBAC, RLS, Actlog, ErrorSanitization, SecurityHeaders) |
| | Report blocks | 41 |
| | Report recipes | 16 |
| | CA pipelines | 7 (Identity, Endpoint, Data, Productivity, Azure, MailFlow, PowerBI) |
| | Feature Inventory entries | ~42 |
| **Agent** | Version | 2.9.1 |
| | Source files | 48 |
| | Engines | 13 (12 + NetAccountCompat) |
| | Services | 22 |
| | CLI flags | 23 |
| **Portal** | Version | 1.18.0 |
| | Pages | 8 |
| | Tabs (org detail) | 18 |
| | CA tabs | 7 |
| | API modules | 20 |
| **DB** | SQL migrations | 81 (001-081) |
| | Seed files | 24 |
| | Tables | 154+ |

---

## Control Catalog

```
control_defs total: 918  (738 prior + 80 SRV + 100 DC)
  Active:   827  (647 baseline + 80 SRV + 100 DC)
  Inactive: 91   (legacy BL-XXX soft-deleted)

Active by engine:
  registry 371  command 188  auditpol 34  firewall 21
  dc 27  service 18  certstore 6  netaccount 5
  bitlocker 5  tpm 4  eventlog 4  secedit 2

Framework coverage (active):
  NIST 827 (100%)  CIS ~810 (98%)  HIPAA ~380 (46%)
  ISO27001 ~240 (29%)  PCI-DSS ~30 (3.6%)

Platform scope:
  W10, W11 -> 647 controls
  MS19, MS22, MS25 -> 727 (647 + 80 SRV)
  DC19, DC22, DC25 -> 827 (647 + 80 SRV + 100 DC)
```

Health check: `KryossApi/sql/check_catalog_health.sql`

---

## Cloud Assessment (CA-0..CA-15)

| Phase | What |
|-------|------|
| CA-0 | Scaffold: DB schema, service skeleton, endpoints, portal placeholder |
| CA-1 | Identity Pipeline: Entra deep, 14 checks, MFA/CA/PIM/risky users/B2B/OAuth/GSA |
| CA-2 | Endpoint Pipeline: Intune + Defender for Endpoint + 4 KQL hunting queries |
| CA-3 | Data Pipeline: Purview + DLP + labels + SharePoint deep + external users |
| CA-4 | Productivity Pipeline: M365 usage + adoption + licenses + wasted licenses |
| CA-5 | Overview + Timeline: Radar chart, timeline, compare mode, area score cards |
| CA-6 | Azure Pipeline: Subs + resources + Security Center + public exposure + RBAC consent |
| CA-7 | Remediation Tracker: Manual status workflow, suggestions engine |
| CA-8 | Compliance Frameworks: 7 frameworks (HIPAA/ISO/NIST/SOC2/PCI/CIS/CMMC) + Compliance Manager |
| CA-9 | Power BI Governance: Workspaces, gateways, capacity, activity, orphaned detection |
| CA-10 | Mail Flow: SPF/DKIM/DMARC/MTA-STS per domain + forwarding rule risks |
| CA-11 | Benchmarks: Franchise peer + industry + global, NAICS 15 industries x 5 bands |
| CA-12 | Unified Experience: CopilotReadiness deprecated, ConnectCloudWizard, single scan button |
| CA-13 | Intune Deep: Autopilot profiles + config profile drift detection |
| CA-14 | Auto-consent: PBI Fabric delegated OAuth + Azure ARM auto-assign Reader |
| CA-15 | Drift Alerts: 4 rule types, webhook + email, per-franchise config |
| CA-LH | Lighthouse Baseline: 15 gap checks (SSPR, break-glass, device join, AV/FW/ASR, Edge/OneDrive/WinUpdate, notifications, endpoint analytics, Defender auto-onboard) |
| CA-EXO | Exchange Online REST: Unified Audit Logs, Safe Attachments, EOP/MDO via InvokeCommand |
| CA-TIER | License Tier Detection: subscribedSkus → none/standard/premium per feature (22 features) |
| CA-FI | Feature Inventory: ~42 entries per scan with licensed/implemented/adoptionPct/licenseTier |

---

## Infrastructure Assessment (IA)

| Phase | What |
|-------|------|
| IA-0 | Scaffold: 6 tables, service skeleton, 4 endpoints, portal tab |
| IA-1 | Server & Hypervisor: VMware vCenter + Proxmox REST, 7 finding generators |
| IA-2 P1 | Network Topology: LLDP/CDP persistence, D3.js force-directed graph |
| IA-3 | WAN Health: Score 0-100 (weighted 5 factors), 11 findings, traceroute, jitter/loss |
| IA-11 | Network Sites: PublicIpTracker + SiteClusterService + GeoIP + Leaflet map + speed history + SLA |

---

## Agent Evolution (v1.2 -> v2.9)

| Version | Key Changes |
|---------|-------------|
| 1.2.2 | Network scanner, port scanner, AD hygiene, binary patching, multi-disk |
| 1.3.0 | PsExec deleted, batch engines native (WMI/P/Invoke) |
| 1.4.0 | All 5 shell engines converted to native, zero Process.Start |
| 1.5.0 | NativeCommandEngine: 52 legacy controls native (TLS, UserRights, AppLocker, etc.) |
| 1.5.1 | Protocol Usage Audit (NTLM/SMBv1), EventLog event_count/event_top_sources |
| 2.0.0 | Windows Service mode (ServiceWorker, ScanCycle, P/Invoke install) |
| 2.1.0 | 9-block network pipeline: trial, banners, DNS+ping, WMI probe, passive discovery, self-update, external exposure, remediation |
| 2.2.0 | Per-machine key rotation (SH-KEY): machine_secret + session_key 48h + HMAC chain |
| 2.3.0 | Remote config from portal (5 params via heartbeat) |
| 2.4.x | Security sprints 1-4 (HMAC expiry, DPAPI, EventLog auth, SPKI fallback, ACL) |
| 2.5.0 | ICMP traceroute + jitter/loss measurement |
| 2.6.0 | PatchCollector (WMI Win32_QuickFixEngineering + registry) |
| 2.7.0 | Ookla-style multi-stream speed test (8 parallel) |
| 2.8.0 | ServiceWorker v3: 5 parallel loops, per-loop timeouts, AgentLogger, error queue |
| 2.9.0 | Remediation HMAC signatures, ProtectedServices (16 core), ServiceHealer, service inventory |
| 2.9.1 | Banner version fix, SNMP skip in one-shot |

---

## Report System

**Architecture:** `ReportComposer` with 41 blocks + 16 recipes + 3 SVG chart generators.

**Recipes:** C-Level, Technical, PreventaOpener, PreventaDetailed, Framework, Proposal, Monthly, Network, CloudExecutive, ExecOnePager, M365, Compliance, Hygiene, RiskAssessment, Inventory, TestFixture.

**Chart generators:** Donut, Radar, Sparkline (SVG).

**Variants:** KpiVariant (Exec/Business/Compact), CtaMode (Simple/Stepped), AudiencePerspective (Technical/Audit).

**Recent blocks (2026-04-28):** DcHealthBlock, WanHealthBlock, RemediationStatusBlock, VulnerabilityBlock, PatchComplianceBlock, ExternalExposureBlock.

---

## Security Hardening (4 Sprints, 2026-04-26)

**Sprint 1 — 8 CRITICAL:** ErrorSanitization frozen, hypervisor AES-256-GCM, JWT OIDC validation, SelfUpdater SCM recovery, trial UseShellExecute removed, enrollment code scrubbed from 12+ files, Setup-Azure password fixed, QUOTENAME in SQL.

**Sprint 2 — 13 HIGH:** /v1/schedule auth, SSL validation, Redis nonce cache, SelfUpdater hash mandatory, SPKI compiled-in fallback, SYSTEM-only registry ACL, remediation path whitelist (server+agent), MSAL env vars, generic error messages, deploy checksum/GUID/HTTPS.

**Sprint 3+4 — 9 MEDIUM + 8 LOW:** SNMP typed DTOs, AutoConsent tenant validation, enrollment actlog enriched, HMAC session key expiry, verbose credential leak removed, EventLog auth failures, ZeroMemory on keys, DPAPI offline encryption, RequirePermission UI guard, qs() helper, agent service hardening doc, deploy retry with backoff.

---

## Other Shipped Features

- **External Scan** — portal + API (`ExternalScanFunction`, `external_scans` table), IP range network vulnerability scans
- **Threat Detection** — agent `ThreatDetector.cs` + portal ThreatsTab + API endpoints
- **Protocol Usage** — agent `ProtocolAuditService` + portal tab + 12 controls (AUDIT/NTLM/SMB1), org-level toggle
- **SNMP Scanner** — MAC-based dedup, HOST-RESOURCES-MIB, ARP noise filter, 12 vendor profiles, batched upload
- **Recycle Bin** — portal `RecycleBinPage` + API endpoints for soft-delete restore
- **CVE Scanner** — server-side matching (~60 built-in CVEs), 4 endpoints, portal tab, severity filter, dismiss
- **Patch Compliance** — agent PatchCollector, compliance score 0-100, portal tab
- **DC Health** — DcHealthCollector (schema version, FSMO, replication), 2 tables, portal tab
- **Offline Collection** — `--offline --share` + `--collect`, auto-enroll unknown machines
- **Scan Orchestrator** — server assigns time slots, agent hourly check-in, org window 2-6AM
- **Service Management** — ProtectedServices (16 core), priority services auto-heal, portal ServicesTab
- **Activity Timeline** — unified actlog + remediation_log, portal ActivityTab

---

## Known Gaps (as of 2026-04-28)

### Resolved
- **check_json case mismatch** — 25 HIPAA controls fixed via `seed_005b_fix_casing.sql`
- **Platform scope enforcement** — PlatformResolver, agent dumb + server parses OS

### Open
- **Agent payload partially enriched** — `raw_users` and `raw_network` still missing as structured raw blocks (data available through dedicated endpoints instead)
- **Publish script inconsistency** — `PublishAot=true` in csproj but `PublishAot=false` in publish.ps1 (published binary is NOT AOT)
- **RP-RECIPE-OVERHAUL 6/9** — remaining: SNMP->Network/Inventory, Topology->Network, Hypervisor->Inventory
