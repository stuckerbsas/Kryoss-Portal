# Kryoss Master Roadmap — Orchestrator

> **Role of this file:** Single source of truth for what's done, what's queued, what's backlog. Use as orchestrator entry-point at start of every session. Update status inline as phases ship.
>
> **Last updated:** 2026-04-26 (session: NinjaOne deploy fixes + query optimization)
> **Owner:** Federico
> **Orchestrator:** Claude (caveman mode default)

---

## How to use this file

1. **Start of new session:** read this file first, then `CLAUDE.md`, then the specific phase prompt below.
2. **Next phase to run:** check "Active Queue" section — top item is next.
3. **Mark phase done:** flip ⚪ → ✅ + date + brief commit SHA / key artifacts.
4. **New idea:** append to "Backlog" with tier, don't insert into queue unless re-prioritizing.
5. **Prompts are pre-written** — copy-paste verbatim into new session to start phase.

---

## Current State Snapshot (2026-04-26)

**Shipped in production:**
- Kryoss Agent v2.4.1 — 918 controls (827 active: 647 baseline + 80 SRV + 100 DC), 12 engines, Windows Service mode (compliance 24h / SNMP 4h / heartbeat 15min), passive discovery (NetBIOS/mDNS/SSDP), self-updater (6h check), closed-set remediation (~50 controls), port banner grab, reverse DNS + ping enrichment, WMI probe, external exposure (server-side), trial enrollment + auto-report, zero Process.Start, per-machine key rotation (SH-KEY), remote config from portal via heartbeat, SNMP MAC-based dedup + HOST-RESOURCES-MIB
- Assessment engine — 5 frameworks (CIS, NIST, HIPAA, ISO27001, PCI-DSS), 4 report types (C-Level, Technical, Preventas, Framework)
- M365 Security Checks (50 checks) — DEPRECATED, rolled into Cloud Assessment. Portal M365 route redirects to `/cloud-assessment`. Dead files cleaned up 2026-04-20
- Copilot Readiness Assessment — DEPRECATED as standalone (2026-04-18), Copilot Lens tab inside Cloud Assessment reads from CA scan data
- **Cloud Assessment platform** — CA-0..CA-12 COMPLETE. 7 areas (identity, endpoint, data, productivity, azure, powerbi, compliance), 7 frameworks with benchmark comparisons, unified consent wizard, Copilot Lens filter view
- **External Scan** — portal + API (`ExternalScanFunction`, `external_scans` table), IP range network vulnerability scans. NOT in previous roadmap
- **Threat Detection** — agent `ThreatDetector.cs` + portal ThreatsTab + API endpoints. NOT in previous roadmap
- **Protocol Usage Audit** — agent `ProtocolAuditService` + portal ProtocolUsageTab + 12 controls (AUDIT/NTLM/SMB1). Org-level toggle
- **SNMP Scanner** — agent `SnmpScanner.cs` + API `SnmpConfigFunction`/`SnmpFunction` + DB tables (`snmp_configs`, `snmp_devices`). MAC-based dedup, HOST-RESOURCES-MIB, ARP noise filter, machine correlation, stale marking, batched upload. Portal `SnmpTab.tsx`
- **Network Diagnostics (Phase 5a)** — agent + API (`NetworkDiagnosticsFunction`, `SpeedTestFunction`, 3 tables, 50 controls NET-001..050) + `NetworkBlock`/`NetworkRecipe` report. Portal `NetworkDiagnosticsTab.tsx`
- **Network Topology (IA-2 Phase 1)** — LLDP/CDP neighbor persistence, `TopologyFunction`, D3.js force-directed graph in portal `TopologyTab.tsx`
- **Recycle Bin** — portal `RecycleBinPage` + API endpoints for soft-delete restore. NOT in previous roadmap
- **Unified Report System** — `ReportComposer` with 35+ blocks, 16 recipes: C-Level, Technical, PreventaOpener, PreventaDetailed, Framework, Proposal, Monthly, Network, CloudExecutive, ExecOnePager, M365, Compliance, Hygiene, RiskAssessment, Inventory, TestFixture. 3 SVG chart generators (Donut, Radar, Sparkline). All functional. Monthly may need Ninja data enrichment for full value.

**In progress:** None

**Shipped 2026-04-26 security Sprint 1 (8 CRITICAL):**
- API v1.22.6: ErrorSanitization frozen to {error,traceId} (C1), hypervisor password AES-256-GCM encryption (C2), JWT signature validation via OIDC + X-MS-CLIENT-PRINCIPAL restricted to SWA (C3), QUOTENAME in dynamic SQL (C8)
- Agent v2.4.2: SelfUpdater uses SCM recovery instead of cmd.exe batch (C4), trial mode prints path instead of UseShellExecute (C5)
- Scripts: hardcoded enrollment code removed from 12+ files (C6), Setup-Azure.ps1 plaintext password eliminated (C7)
- Spec: `docs/superpowers/specs/2026-04-26-security-remediation-design.md` (30 findings, 4 sprints)
- Plan: `docs/superpowers/plans/2026-04-26-security-remediation-sprint1.md`

**Shipped 2026-04-26 session:**
- Agent v2.4.1: banner version fix (was hardcoded v2.0.0), SNMP skip in one-shot mode (--alone/--silent), remote config delivery via heartbeat
- API v1.22.5: Server 2016→MS19/DC19 platform mapping, enrollment rate limit 5→30 (NinjaOne mass deploy), HeartbeatRequest JsonPropertyName fix (agent_mode was NULL), remediation cancel endpoint, denormalized latest_score on machines table (eliminates correlated subquery), Dashboard Fleet GroupBy+Max rewrite (EF Core 8 translatable), OrgComparison uses denormalized columns, GET /v2/machines/by-hostname/{hostname} endpoint, AsNoTracking on list/detail queries, SQL migration 071
- Portal v1.12.1: Tasks tab in MachineDetail (remediation + scan pending + cancel button), useMachine/useResolvedMachineId rewrite (direct by-hostname call, eliminates full 100-machine list fetch)
- NinjaOne deploy script v5.0: service mode install, legacy task migration, auto-update from blob, Defender ASR exclusion

**✅ Accuracy notes resolved (2026-04-25 housekeeping):**
- Agent `.csproj` at 2.4.1. All features verified through v2.4.1
- Portal M365Tab dead files deleted, route already redirected to `/cloud-assessment`
- CopilotReadinessFunction already returns 410 Gone (earlier audit was false negative)
- Agent + API + master CLAUDE.md updated to match actual code state
- SQL migrations 061-071 applied and verified

**Codebase inventory (2026-04-25 audit):**

| Pillar | Metric | Count |
|--------|--------|-------|
| **API** | Version | 1.22.6 |
| | HTTP endpoints | 163 |
| | Entity classes | 28 |
| | Services | 50+ |
| | Middleware | 7 (ApiKeyAuth, BearerAuth, RBAC, RLS, Actlog, ErrorSanitization, SecurityHeaders) |
| | Report blocks | 35+ |
| | Report recipes | 16 |
| | CA pipelines | 7 (Identity, Endpoint, Data, Productivity, Azure, MailFlow, PowerBI) |
| **Agent** | Version | 2.4.2 |
| | Source files | 48 |
| | Engines | 13 (12 + NetAccountCompat wrapper) |
| | Services | 22 |
| | CLI flags | 23 |
| | Models | 11 files, 35+ classes |
| **Portal** | Version | 1.12.1 |
| | Pages | 8 |
| | Tabs (org detail) | 18 |
| | CA tabs | 7 |
| | API modules | 20 |
| | Routes | 30+ (incl. redirects) |
| **DB** | SQL migrations | 71 (001-071, no gaps) |
| | Seed files | 24 |
| | Tables | 154+ |
| | Check/utility scripts | 7 |

**Customer context driving next priorities:**
- Multi-site enterprise client expects infrastructure relevamiento (agenda: arquitectura actual, capacidad, conectividad entre sitios + yacimientos, optimización, evolución cloud)
- → Infrastructure Assessment (IA) product line added as parallel to Cloud Assessment

---

## Active Queue — Next 5 Phases

Execute in order. Each ships independently. Prompts ready below.

| # | Phase | Status | Est | Blocks |
|---|-------|--------|-----|--------|
| 1 | ~~**IA-0** Infrastructure Assessment Scaffold~~ | ✅ shipped | 1 session | All IA phases |
| 2 | ~~**IA-11** Network Tab + Device Map + Auto-Speedtest (KILLER FEATURE)~~ | ✅ Track A+B+C shipped | 2-3 sessions | Diff vs Datto/Ninja/Auvik |
| 3 | ~~**CA-13** Intune Deep verify/gap-fill~~ | ✅ shipped | 0-1 session | None |
| 4 | ~~**CA-14** Auto-consent (Fabric + ARM)~~ | ✅ shipped | 1 session | None (UX win) |
| 5 | ~~**IA-1** Server & Hypervisor Inventory~~ | ✅ shipped | 1 session | IA-10 report |
| 6 | ~~**IA-2** Network Topology Discovery~~ | ✅ Phase 1 shipped | 2 sessions | IA-10 report |
| 7 | ~~**CA-15** Drift Alerts + Notifications~~ | ✅ shipped 2026-04-21 | 1 session | MSP retention |
| 8 | ~~**A-SVC** Agent v2.0.0: Windows Service mode~~ | ✅ shipped 2026-04-25 | 1 session | ServiceWorker, ScanCycle, P/Invoke install |
| 9 | ~~**A-NET** Agent v2.1.0: Full Network Pipeline + Remediation~~ | ✅ shipped 2026-04-25 | 1 session | 9 blocks: service, trial, banners, DNS+ping, WMI, passive, self-update, external exposure, remediation |
| 10 | **RP-EXPANSION** Report Block Library Finalization | ⚪ queued | 3.25 sessions | Unlocks consistent reports across all tiers. Spec: `2026-04-20-report-block-library.md` (37 tasks, 5 phases) |

After active queue complete → replan from Backlog.

---

## Cloud Assessment (CA) — Shipped

| Phase | Status | Date | Notes |
|-------|--------|------|-------|
| CA-0 Scaffold | ✅ | 2026-04-16 | DB schema, service skeleton, endpoints stub, portal placeholder |
| CA-1 Identity Pipeline | ✅ | — | Entra deep, 14 checks, MFA/CA/PIM/risky users/B2B/OAuth/GSA |
| CA-2 Endpoint Pipeline | ✅ | — | Intune basics + Defender for Endpoint + 4 KQL hunting queries |
| CA-3 Data Pipeline | ✅ | — | Purview + DLP + labels + SharePoint deep + external users |
| CA-4 Productivity Pipeline | ✅ | — | M365 usage + adoption + licenses + wasted licenses |
| CA-5 Overview + Timeline | ✅ | — | Radar chart, timeline, compare mode, area score cards |
| CA-6 Azure Pipeline | ✅ | — | Subs + resources + Security Center + public exposure + RBAC consent |
| CA-7 Remediation Tracker | ✅ | — | Manual status workflow, suggestions engine, fix button skeleton |
| CA-8 Compliance Frameworks | ✅ | — | 7 frameworks (HIPAA/ISO/NIST/SOC2/PCI/CIS/CMMC) + Compliance Manager |
| CA-9 Power BI Governance | ✅ | — | Workspaces, gateways, capacity, activity, orphaned detection |
| CA-10 Mail Flow | ✅ | — | SPF/DKIM/DMARC/MTA-STS per domain + forwarding rule risks |
| CA-11 Benchmarks | ✅ | 2026-04-18 | Franchise peer + industry + global, NAICS 15 industries × 5 bands |
| CA-12 Unified Experience | ✅ | 2026-04-18 | CopilotReadiness deprecated, ConnectCloudWizard, single tab, single scan button |
| CA-13 Intune Deep | ✅ | 2026-04-20 | Verify/gap-fill of Intune checks in Endpoint pipeline |
| CA-14 Auto-consent | ✅ | 2026-04-20 | PBI Fabric auto-enable (delegated OAuth) + Azure ARM auto-assign Reader (delegated). Wizard updated with "Enable automatically" primary buttons + manual fallback. `AutoConsentFunction.cs` + `FabricAdminService.cs` |

---

## Cloud Assessment (CA) — Remaining Gaps

### CA-13: Intune Deep Verify + Gap Fill ✅
**Priority:** P0 — verify first, may be done
**Effort:** 0-1 session — **SHIPPED 2026-04-20**

Audit CA-2 coverage vs spec:
- [x] App protection policies iOS + Android — already in CA-2
- [x] Managed apps inventory — already in CA-2
- [x] Enrollment restrictions — already in CA-2
- [x] Autopilot deployment profiles — **NEW**: `CollectAutopilotProfiles` via Graph beta `/beta/deviceManagement/windowsAutopilotDeploymentProfiles`
- [x] Configuration profiles drift detection — **NEW**: `CollectConfigProfileAssignmentStatus` via `DeviceConfigurationDeviceStateSummaries` (compliant/nonCompliant/error/conflict/notApplicable)

New recommendations: `GenerateConfigProfileDrift` (>15% = action required, >5% = warning), enhanced `GenerateAutopilot` (pass when profiles exist).
New metrics: `config_profiles_assigned/succeeded/failed/pending/conflict`, `autopilot_profiles` now populated.
Signature change: `EndpointPipeline.RunAsync` now takes `graphBetaHttp` param (like other pipelines).

### CA-14: Auto-Consent (Fabric + ARM)
**Priority:** P0 — UX win, cuts onboarding 3 steps → 1 click
**Effort:** 2 sessions

**Track A — Fabric Admin API (Power BI auto-enable):**
- Delegated OAuth `https://api.fabric.microsoft.com/.default`
- Customer Fabric Admin signs in
- `FabricAdminService.EnableServicePrincipalAccess` + add SPN to allowlist
- Verify via existing PBI verify endpoint

**Track B — Delegated ARM Role Assignment:**
- Delegated OAuth with Azure management scope
- Customer Owner/UAA signs in
- `PUT /subscriptions/{id}/providers/Microsoft.Authorization/roleAssignments/{guid}` assigns Reader to Kryoss SPN
- Auto-verify via existing ARM verify

Result: ConnectCloudWizard Steps 2+3 become single-click per service.

### CA-15: Drift Alerts + Notifications ✅ SHIPPED 2026-04-21
**Priority:** P0 — MSP retention
**Effort:** 1 session

- `cloud_assessment_alert_rules` table (per-franchise thresholds)
- `AlertService` runs after each scan, detects drops
- Email via SendGrid / Azure Comms
- Optional webhook (feeds CA-16 PSA)
- Portal: franchise settings → Alerts config page

Triggers:
- Overall score ≥0.5 drop
- New Critical finding
- New High priority finding in regulated framework
- Compliance framework drop below threshold

### CA-16: PSA / Ticketing Integration
**Priority:** P1 — MSP workflow
**Effort:** 2 sessions per PSA

Phase A: **ConnectWise Manage** (largest MSP share first)
Phase B: Autotask, HaloPSA, generic webhook

On trigger: create ticket with finding detail + remediation + link back to Kryoss.

### CA-17: Automated Remediation Library
**Priority:** P1 — per-fix careful review
**Effort:** 1 session per handler

Ship one handler per session:
- Enable MFA for user
- Apply sensitivity label
- Disable legacy auth (CA policy)
- Revoke risky OAuth grant
- Block external forwarding
- Enable Customer Lockbox
- Enable DKIM

Each: preview + apply + rollback instructions + actlog.

### CA-18: Benchmark Periodic Refresh
**Priority:** P3 — needs 100+ tenants first
**Effort:** 1 session

Quarterly job recomputes percentiles from real data.

### CA-19: Graph Connectors + Copilot Knowledge Sources
**Priority:** P3
**Effort:** 1 session

Deeper Graph Connectors + Copilot ground truth mapping. Stale connector detection.

### CA-20: Audit Log Deep Dive
**Priority:** P3
**Effort:** 1 session

M365 unified audit log views (sign-in risk timeline, admin actions, impossible travel).

---

## Infrastructure Assessment (IA) — New Product Line

Parallel to Cloud Assessment. Customer: hybrid on-prem + cloud + multi-site (yacimientos).

### IA-0: Scaffold ✅
**Priority:** P0 — unlocks all IA
**Effort:** 1 session — **SHIPPED 2026-04-20**

SQL migration `043_infra_assessment.sql` (6 tables). Entity classes in `Data/Entities/InfraAssessment.cs`.
DbContext mappings. `IInfraAssessmentService` + `InfraAssessmentService` stub (start, latest, detail, history).
`InfraAssessmentFunction.cs` (POST scan, GET latest, GET detail, GET history). DI registered.
Portal: `api/infraAssessment.ts` hooks + `InfraAssessmentTab.tsx` with KPI cards + findings list + nav entry + route.

### IA-1: Server & Hypervisor Inventory ✅
**Priority:** P1
**Effort:** 1 session — **SHIPPED 2026-04-20**

Migration `046_hypervisor_inventory.sql` (3 tables: `infra_hypervisor_configs`, `infra_hypervisors`, `infra_vms`).
Entities `InfraHypervisorConfig`, `InfraHypervisor`, `InfraVm`. DbContext mappings.
`HypervisorPipeline.cs` — VMware vCenter REST + Proxmox REST (QEMU + LXC) collectors.
7 finding generators (idle VM, over-provisioned, stale snapshots, missing backup, capacity exhaustion, no HA, EOL OS).
`HypervisorConfigFunction.cs` — CRUD + test + scan results (6 endpoints).
Portal: "Servers & VMs" sub-tab in InfraAssessmentTab with config manager (add/delete/test), host table, VM table, findings.
Hyper-V deferred to agent-side WMI module (IA-1b).

**Previous spec (kept for reference):**

- VMware vCenter API integration
- Hyper-V host + VM enumeration
- Proxmox detection
- Per-VM resource allocation vs utilization
- Consolidation opportunities

### IA-2: Network Topology Discovery
**Priority:** P1
**Effort:** 2 sessions
**Phase 1 status:** 🟡 shipped 2026-04-24

**Phase 1 shipped (data + visualization):**
- SQL migration `058_network_topology.sql`: `snmp_device_neighbors` table (persists LLDP/CDP neighbor data, was discarded before)
- Entity `SnmpDeviceNeighbor` + DbContext mapping
- `SnmpFunction.SubmitResults`: now persists LLDP + CDP neighbors (replace-on-scan, like interfaces)
- `ResolveNeighborLinks`: auto-matches remoteSysName/remoteChassisId/remoteIp to known devices in same org
- `TopologyFunction.cs`: `GET /v2/topology?organizationId=X` — returns nodes + edges + phantom devices (seen via LLDP/CDP but not scanned). Dedup edges, classify phantom device types from CDP platform strings
- Portal: `TopologyTab.tsx` — D3.js force-directed graph with drag, zoom, node detail panel. Color by device type, dashed border for phantoms, green border for Kryoss agents. Port labels on edges. LLDP=blue, CDP=yellow links. KPI cards (devices, links, phantoms, type breakdown). Legend.
- Wired as first sub-tab of Network tab (default view)

**Phase 2 remaining:**
- Switch/router config snapshots (read-only)
- Wireless AP inventory + channel analysis
- Firewall rules (where accessible)
- VLAN topology overlay
- Traffic flow visualization (from interface octets delta)

Vendors: Cisco, Fortinet, Palo Alto, pfSense/OPNsense, MikroTik, SNMP generic.

### IA-3: WAN & Site Connectivity Health
**Priority:** P1 — customer key ask ("conectividad entre sitios")
**Effort:** 2 sessions

- Active probes per site (ping, traceroute, bandwidth)
- Link classification (MPLS/SD-WAN/IPsec/ExpressRoute/Internet/Cellular/Satellite)
- Uptime SLA measured
- Latency/jitter/packet loss
- Cost input (MSP manual)
- Redundancy check
- Yacimientos-specific: satellite/cellular cost flags

### IA-4: Cloud Connectivity Audit
**Priority:** P2
**Effort:** 1 session

ExpressRoute, Virtual WAN, VPN Gateway, NVA inventory. Integrates with CA-6 Azure data.

### IA-5: Capacity Planning
**Priority:** P2
**Effort:** 1 session

Time-series 90d + linear regression. 6/12/18-month projections. License utilization beyond M365 (SQL, Windows Server, VMware, Citrix).

### IA-6: Collaboration Audit
**Priority:** P3
**Effort:** 1 session

Teams Rooms, phone system, meeting quality, video endpoints. Sprawl detection (Teams + Slack duplication).

### IA-7: OT / Industrial Network Detection
**Priority:** P3 — yacimientos-critical
**Effort:** 1-2 sessions

Passive SCADA/PLC detection (Modbus, DNP3, EtherNet/IP, Siemens S7). Air-gap verification. Defender for IoT integration.

### IA-8: Cloud Migration Readiness
**Priority:** P3
**Effort:** 2 sessions

Per-workload classification + cloud compatibility score + TCO estimate + wave plan.

### IA-9: Availability & SLA Tracking
**Priority:** P3
**Effort:** 1 session

Active probes, MTTR/MTBF, dependency tree, SLA compliance.

### IA-10: Infrastructure Assessment Report
**Priority:** P2 — customer deliverable
**Effort:** 1 session

Dedicated report type. 10 sections matching customer agenda.

### IA-11: Network Tab + Device Map + Auto-Speedtest
**Priority:** P1 — KILLER DIFFERENTIATOR (no MSP tool combines this)
**Effort:** 2-3 sessions
**Track A status:** ✅ shipped 2026-04-20
**Track B status:** ✅ shipped 2026-04-20
**Track C status:** ✅ shipped 2026-04-20

**Track A shipped (data layer + portal):**
- SQL migration `044_network_sites.sql`: `machines.last_public_ip` + `last_public_ip_at`, `machine_public_ip_history` table, `network_sites` table
- `PublicIpTracker` service: captures public IP from `X-Forwarded-For` on every agent results submission, maintains IP history
- `SiteClusterService`: groups machines by public IP → auto-derived network sites, computes agent count, IP changes 90d, avg speed/latency
- `NetworkSitesFunction.cs`: 4 endpoints (GET list, POST rebuild, PATCH update, GET ip-history)
- Portal `NetworkSitesTab.tsx`: KPI cards (sites, agents, avg download, unstable/cellular alerts) + Sites table (IP, location, ISP, type, speed, latency, IP changes) + IP History tab
- Wired into `ResultsFunction` (non-fatal IP tracking after evaluation)

**Track B shipped (agent upgrade + server response + portal):**
- Agent `NetworkDiagnostics.cs`: `MeasureCloudEndpointLatencyAsync` (6 M365 endpoints: outlook, teams, sharepoint, graph, login, admin) + `MeasureDnsResolutionAsync` (3-query average)
- Agent models: `CloudEndpointLatency` class, `NetworkDiagResult` extended with `CloudEndpointLatency`, `DnsResolutionMs`, `TriggeredByIpChange`
- Agent `JsonContext.cs`: `CloudEndpointLatency` + `List<CloudEndpointLatency>` added for AOT source-gen
- Agent `ResultsResponse`: `YourPublicIp` + `SpeedtestRequested` fields for server→agent communication
- Agent `Program.cs`: console output shows cloud endpoint count + DNS latency, prints public IP after upload
- Server `ResultsFunction`: response now includes `yourPublicIp` (from `_currentUser.IpAddress`) + `speedtestRequested`
- Server `EvaluationService`: `NetworkDiagDto` extended with `CloudEndpointLatencyDto`, `DnsResolutionMs`, `TriggeredByIpChange`; persists `dns_resolution_ms`, `cloud_endpoint_count`, `cloud_endpoint_avg_ms`, `triggered_by_ip_change` columns
- Server `MachineNetworkDiag` entity: 4 new columns
- SQL migration `045_network_diag_cloud_dns.sql`: adds 4 columns to `machine_network_diag`
- Portal `NetworkDiagnosticsTab.tsx`: 2 new KPI cards (DNS, Cloud Latency) + 2 new table columns + 6-column grid
- Portal `networkDiagnostics.ts`: API types extended

**Track C shipped (GeoIP + map + charts + SLA + device drill-down):**
- Server `GeoIpService.cs`: `IGeoIpService` interface + `IpApiGeoIpService` implementation (ip-api.com free tier, auto-detects country/region/city/lat/lon/ISP/ASN/connectivity type)
- `PublicIpTracker` enriched: auto-populates geo fields on `machine_public_ip_history` rows via GeoIP lookup on first IP detection
- `SiteClusterService` enriched: propagates geo data from IP history to `network_sites`, auto-names sites by city
- `NetworkSitesFunction.cs`: 2 new endpoints — `GET /v2/network-sites/{siteId}/speed-history` (90d speed/latency/DNS/cloud timeseries) + `GET /v2/network-sites/{siteId}/machines` (devices at site with latest diag)
- Portal `SiteMap.tsx`: Leaflet interactive map (react-leaflet + OpenStreetMap tiles), auto-fit bounds, click-to-select site, popup with speed/ISP/agents
- Portal `SpeedHistoryChart.tsx`: Recharts line chart (download/upload 90d), SLA reference line from contracted bandwidth
- Portal `SiteDetailDrawer.tsx`: slide-over drawer with SLA compliance bar, speed/latency/upload KPIs, speed history chart, device table
- Portal `NetworkSitesTab.tsx`: full rewrite — map card, SLA breaches KPI, SLA column in sites table, clickable rows → drawer, 5 KPI cards
- API types: `SpeedHistoryPoint`, `SpeedHistoryResponse`, `SiteMachine` + `useSpeedHistory`, `useSiteMachines` hooks
- NPM: `leaflet`, `react-leaflet`, `@types/leaflet` installed

Combines: public IP detection + GeoIP + auto-speedtest on IP change + site clustering + device map + bandwidth/SLA tracking. Competitors (Datto, Ninja, Auvik) do pieces, none combine all.

**Track A — Data layer (1 session):**
- Migration: `machines.last_public_ip` NVARCHAR(45) + `last_public_ip_detected_at` + `machine_public_ip_history` table (machine_id, public_ip, first_seen, last_seen, geo_country, geo_city, isp, asn, connectivity_type_inferred)
- New middleware: `AgentIpCaptureMiddleware` inspects Results/Inventory/Hygiene/NetworkDiag upload endpoints
- Agent response schema extended: include `{yourPublicIp, speedtestRequested: bool}` on every response
- New service: `SiteClusterService` groups machines by public IP → site
- GeoIP enrichment: MaxMind GeoLite2 free DB (50MB, monthly refresh via timer)
- ASN → connectivity type mapping (residential/business/cellular/satellite)

**Track B — Agent upgrade v1.6+ (1 session):**
- Agent compares received `yourPublicIp` vs stored in registry `HKLM\SOFTWARE\Kryoss\Agent\LastPublicIp`
- On change: set local flag `speedtest_pending=true`, queue speedtest on next scheduled run (not immediate — respect rate limit)
- Extend `NetworkDiagnostics` service:
  - Auto-trigger speedtest on flag
  - Cloud endpoint latency probe (M365 endpoints: outlook.office.com, teams.microsoft.com, sharepoint.com, graph.microsoft.com)
  - DNS resolution time (3 queries, avg)
  - MTU discovery (path MTU to M365 endpoints)
- Rate limit: 1 speedtest per day per machine (local tracking)
- Config override: franchise admin can bump to N/day for troubleshooting window
- Upload via existing `POST /v1/network-diag` endpoint (extended payload)

**Track C — Portal Network tab (1 session):**
- New top-level tab "Network" (or sub-tab of Infrastructure Assessment)
- 6 sections:
  1. **Site map** (Leaflet + react-leaflet): pins clustered by public IP, click → drawer
  2. **Sites table**: name (auto-derived, editable), public IP, stability (N changes/90d), agent count, detected device count, avg down/up/latency, connectivity type, SLA compliance
  3. **Speedtest history**: per-site line chart down/up/latency 90d, anomaly flags (>30% drop vs baseline), top-N worst
  4. **IP change events**: timeline of public IP changes per agent/site
  5. **Connectivity health**: M365 endpoint reachability matrix, DNS resolution time, MTU issues, packet loss
  6. **Devices per site**: Managed (agent) | Detected (network scan only) | Unmanaged alert (detected + not in M365 users)

Findings generated:
- "Site {X} averaging {Y} Mbps down, contracted {Z} Mbps — contact ISP" (if contracted bandwidth configured)
- "Agent {Y} changed public IP {N} times in 30d — router instability"
- "Remote site bandwidth degraded {N}% over 90d"
- "Cellular/satellite ASN detected at site {X} — ensure redundancy"
- "Unmanaged device detected on same LAN as managed agents — shadow IT risk"

Yacimientos-specific value:
- Auto-detects satellite/cellular ASNs → bandwidth/latency reality check
- Contract vs actual bandwidth gap (common SMB overspend)
- ISP stability tracking
- Per-site MTU issues (common satellite problem)

Permissions: agent already uses API Key + HMAC, no change. Server: no new auth.

Bandwidth concern: default 100MB/test, max 1/day, franchise-level config. Silent mode default (no user notification). Optional tray notification future.

### IA-12: External Scan — Domain Health integration
**Priority:** P2
**Effort:** 0.5 session

Extend existing External Scan (IP range vuln scan) with DNS-based domain health:
- Input: domain name (parallel to IP range)
- Reuse `DnsLookup` + SPF/DKIM/DMARC parse from CA-10 MailFlowPipeline
- Results persist to `external_scan_results` with new check_type="domain_health"
- Portal ExternalScan tab: new "Domain Health" sub-section with per-domain score + findings

No new consent. No new tables. Pure code reuse.

---

## Non-Cloud Roadmap (Kryoss Core)

### Agent (KryossAgent v2.1.0)

| # | Feature | Tier | Effort |
|---|---------|------|--------|
| ~~A-VER~~ | ~~Version audit: verify v1.5.0/v1.5.1 features in code, bump `.csproj` to match~~ | ✅ | — |
| ~~A-OFL~~ | ~~Offline collection mode (`--offline`/`--share`/`--collect`)~~ | ✅ | 0.5 session |
| ~~A-SVC~~ | ~~Windows Service mode (`--install`/`--uninstall`/`--service`, `ServiceWorker`, `ScanCycle`, P/Invoke install)~~ | ✅ shipped 2026-04-25 | 1 session |
| ~~A-TRI~~ | ~~Trial enrollment + auto-report (limited scan, one-shot PDF)~~ | ✅ shipped 2026-04-25 | — |
| ~~A-BAN~~ | ~~Port banner grab + service detection~~ | ✅ shipped 2026-04-25 | — |
| ~~A-DNS~~ | ~~Reverse DNS + ping enrichment for SNMP devices~~ | ✅ shipped 2026-04-25 | — |
| ~~A-WMI~~ | ~~WMI probe for network devices~~ | ✅ shipped 2026-04-25 | — |
| ~~A-PAS~~ | ~~Passive discovery (NetBIOS/mDNS/SSDP listener)~~ | ✅ shipped 2026-04-25 | — |
| ~~A-UPD~~ | ~~Self-updater (checks `/v1/agent-version`, downloads, restarts service)~~ | ✅ shipped 2026-04-25 | — |
| ~~A-EXT~~ | ~~External exposure (server-side port scan + findings)~~ | ✅ shipped 2026-04-25 | — |
| ~~A-REM~~ | ~~Closed-set remediation (~50 controls, whitelist catalog, heartbeat delivery, rollback)~~ | ✅ shipped 2026-04-25 | — |
| A-01 | CVE scanner (standalone, beyond Defender) | P1 | 1 session |
| A-02 | Patch compliance tracking (WSUS/WUfB/Ninja) | P1 | 1 session |
| A-03 | Backup verification (Veeam/Acronis/Ninja signal) | P2 | 1 session |
| A-04 | BitLocker escrow verification | P2 | 0.5 session |
| A-05 | Firewall rules inventory (full rule list) | P2 | 1 session |
| A-06 | Scheduled tasks inventory (persistence detection) | P2 | 0.5 session |
| A-07 | Browser extensions inventory | P3 | 1 session |
| A-08 | Browser saved passwords risk signal | P3 | 0.5 session |
| ~~A-09~~ | ~~Agent version detection + drift dashboard~~ | ✅ shipped 2026-04-20 | 0.5 session |
| A-10 | Sentinel decouple binary → registry (prereq for auto-update) | P2 | 1 session |
| A-11 | Self-update helper binary (updater.exe + hash verify + rollback) | 🟡 basic self-updater shipped (A-UPD), full rollback + hash verify pending | 1 session |
| A-11b | NinjaRMM auto-update alternative: version check in deploy script + download from URL or `KRYOSS_LATEST_VERSION` Script Variable. Lighter than A-11, no updater.exe needed | P2 | 0.5 session |
| A-12 | Staged rollout + update channels (stable/beta, 5→25→100%, auto-rollback) | P3 | 2 sessions |
| ~~A-13~~ | ~~Server-side scan orchestrator: agent polls `/v1/schedule`, server returns slot time~~ | ✅ shipped 2026-04-20 | 2-3 sessions |

### Domain Controller Scope (Phase 2 per CLAUDE.md)

| # | Feature | Tier | Effort |
|---|---------|------|--------|
| DC-01 | DC19/22/25 platform codes + control mapping (~100 DC controls) | ✅ shipped 2026-04-20 | 1 session |
| DC-02 | AD schema/replication health | P1 | 1 session |
| DC-03 | FSMO role distribution audit | P1 | 0.5 session |
| DC-04 | GPO inventory + inheritance analysis | P2 | 2 sessions |
| DC-05 | DNS zone audit (stale records, secure dynamic updates) | P2 | 1 session |
| DC-06 | DFS namespace health | P3 | 1 session |
| DC-07 | Domain functional level recommendations | P2 | 0.5 session |

### Attestation Module (Phase 6 per CLAUDE.md)

| # | Feature | Tier | Effort |
|---|---------|------|--------|
| AT-01 | Policy documentation upload + version control | P1 | 2 sessions |
| AT-02 | Evidence artifacts (screenshots, signed attestations) | P1 | 1 session |
| AT-03 | Per-control attestation workflow | P1 | 2 sessions |
| AT-04 | SOC 2 Type II evidence collection | P2 | 2 sessions |
| AT-05 | Change log / audit trail | P2 | 1 session |

### RMM / PSA Integrations

| # | Feature | Tier | Effort |
|---|---------|------|--------|
| R-01 | NinjaRMM deploy agent end-to-end | P1 | 1 session |
| R-02 | NinjaRMM device inventory sync | P1 | 2 sessions |
| R-03 | NinjaRMM custom fields import (backup/patch) | P2 | 1 session |
| R-04 | ConnectWise Manage integration (CA-16 Track A) | P1 | 2 sessions |
| R-05 | Autotask PSA integration | P2 | 2 sessions |
| R-06 | HaloPSA integration | P2 | 2 sessions |
| R-07 | Intune deploy agent via Win32 app | P2 | 1 session |

### Reports & Deliverables

| # | Feature | Tier | Effort | Blocks |
|---|---------|------|--------|--------|
| ~~RP-01~~ | ~~Monthly Progress report~~ | ✅ shipped 2026-04-21 | 1 session | `MonthlyRecipe.cs` exists — functional but may need Ninja RMM data enrichment for full monthly delta |
| RP-02 | SOC 2 Type II evidence report | P2 | 1 session | Needs AT-01..AT-04 |
| RP-03 | Vendor risk report (SaaS inventory) | P3 | 1 session |  |
| RP-04 | Insurance renewal questionnaire report | P3 | 1 session |  |
| RP-05 | Board-level exec report | Icebox | 1 session |  |
| ~~RP-06~~ | ~~Business Proposal report (auto-pricing from `service_catalog` + `franchise_service_rates`)~~ | ✅ shipped 2026-04-21 | 1 session | `ProposalRecipe.cs` + `ServiceCatalogBlock.cs` + portal dropdown added |
| ~~RP-07~~ | ~~Network Assessment report (portal viewer for `NetworkRecipe` output)~~ | ✅ shipped 2026-04-20 | 0.5 session | `NetworkRecipe.cs` + `NetworkBlock.cs` + portal `ReportGenerator.tsx` has `network` type |
| ~~RP-08~~ | ~~Cloud Executive report (findings + hours estimate, no remediation)~~ | ✅ shipped 2026-04-21 | 0.5 session | `CloudExecutiveRecipe.cs` + `CloudExecutiveBlock.cs` + portal dropdown added |
| ~~RP-09~~ | ~~Executive One-Pager (2-page summary with grade + frameworks + top findings)~~ | ✅ shipped 2026-04-21 | 0.5 session | `ExecOnePagerRecipe.cs` + `ExecOnePagerBlock.cs` |
| ~~RP-10~~ | ~~M365 Security & Copilot Readiness report~~ | ✅ shipped 2026-04-21 | 0.5 session | `M365Recipe.cs` + `M365Block.cs` (M365 findings by category + Copilot readiness page) |
| ~~RP-11~~ | ~~Compliance Scorecard (multi-framework side-by-side + ring gauges + benchmarks)~~ | ✅ shipped 2026-04-21 | 0.5 session | `ComplianceRecipe.cs` + `ComplianceScorecardBlock.cs` |
| ~~RP-12~~ | ~~AD Hygiene Audit (privileged accounts, stale objects, LAPS, kerberoastable, domain info)~~ | ✅ shipped 2026-04-21 | 0.5 session | `HygieneRecipe.cs` + `HygieneBlock.cs` |
| ~~RP-13~~ | ~~Risk & Threat Assessment (threats + open ports + attack vectors + credential exposure)~~ | ✅ shipped 2026-04-21 | 0.5 session | `RiskAssessmentRecipe.cs` + `RiskSummaryBlock.cs` |
| ~~RP-14~~ | ~~Asset Inventory (OS distribution, security coverage, storage, hardware table)~~ | ✅ shipped 2026-04-21 | 0.5 session | `InventoryRecipe.cs` + `InventoryBlock.cs` |

### Assessment & Controls

| # | Feature | Tier | Effort |
|---|---------|------|--------|
| AC-01 | Custom control builder | P2 | 2 sessions |
| AC-02 | Control exceptions workflow (with expiry) | P2 | 1 session |
| AC-03 | Policy upload → auto-map to controls | P3 | 2 sessions |
| AC-04 | ISO 27001:2022 full (90+ Annex A controls) | P2 | 2 sessions |
| AC-05 | NIST 800-171 / CMMC predecessor | P2 | 2 sessions |
| AC-06 | PCI DSS 4.0 full (250+ sub-reqs) | P2 | 2 sessions |
| AC-07 | Essential 8 (ACSC Australia) | P3 | 1 session |
| AC-08 | CIS Critical Security Controls v8 full | P2 | 2 sessions |

### Security Additional

| # | Feature | Tier | Effort |
|---|---------|------|--------|
| SE-01 | TLS cert expiration monitoring | P2 | 0.5 session |
| SE-02 | HaveIBeenPwned leaked creds check | P2 | 1 session |
| SE-03 | Shadow IT discovery (Defender Cloud Apps deep) | P2 | 2 sessions |
| SE-04 | DNS record audit (subdomain takeover) | P2 | 1 session |
| SE-05 | Breach notification monitoring | P3 | 1 session |
| SE-06 | Typosquat domain detection | Icebox | 1 session |
| SE-07 | Brand impersonation monitoring | Icebox | 2 sessions |
| SE-08 | Phishing simulation | P3 | 3 sessions |
| SE-09 | Security awareness training tracking | P3 | 2 sessions |

### Security Hardening (Pre-Production) — do after reports, before go-live

| # | Task | Tier | Effort | Notes |
|---|------|------|--------|-------|
| SH-01 | ✅ Rate limiting (enrollment brute-force + per-org) | P0 | — | **SHIPPED 2026-04-25** — SH-KEY: 5/15min per IP on enrollment, 200/min per org |
| SH-02 | M365 client secret → Key Vault | P0 | 0.5 session | Currently in env var (`M365ScannerClientSecret`) |
| SH-03 | ✅ Per-machine key rotation + session keys | P0 | — | **SHIPPED 2026-04-25** — SH-KEY: Kerberos-inspired 3-layer auth (machine_secret + session_key 48h + rotation via heartbeat). Supersedes org-level API key expiration. |
| SH-04 | Report HTML CSP meta tag | P1 | 0.5 session | Defense-in-depth against XSS if report HTML rendered in browser |
| SH-05 | Request size limits on `POST /v1/results` | P1 | 0.5 session | No max body size — potential abuse vector |
| SH-06 | Enrollment code cryptographic randomness audit | P1 | 0.5 session | Verify codes use CSPRNG, not `Random` |
| SH-07 | SPKI pinning activation (agent: log-only → enforce) | P1 | 0.5 session | Agent has it log-only until `SpkiPins` registry populated |
| SH-08 | `dotnet list package --vulnerable` dependency audit | P2 | 0.5 session | No automated vuln check in CI today |
| SH-09 | ✅ Per-machine key rotation (automated via heartbeat) | P2 | — | **SHIPPED 2026-04-25** — SH-KEY: session_key auto-rotates every 24h via heartbeat, machine_secret long-term, backward compat with org ApiSecret |
| SH-10 | Anomaly scoring on agent payloads (server-side) | P2 | 1 session | Crafted but crypto-valid payloads = weakest link per security-baseline.md |
| SH-11 | `Content-Disposition: attachment` on report HTML responses | P2 | 0.5 session | Prevents report HTML from executing in browser context |

### Cross-Platform Agent

| # | Task | Tier | Effort | Notes |
|---|------|------|--------|-------|
| XP-01 | Linux agent MVP (~50 controls: SSH, ufw, sysctl, PAM, cron, sudo, updates) | Icebox | 3-4 weeks | .NET 8 linux-x64. Framework (heartbeat, HMAC, key rotation) portable. New engines: file-config, systemd, sysctl. New catalog LIN-001..LIN-xxx |
| XP-02 | Mac agent MVP (~30 controls: FileVault, Gatekeeper, SIP, MDM, profiles) | Icebox | 2-3 weeks | .NET 8 osx-arm64. New engines: profiles, defaults, csrutil. New catalog MAC-001..MAC-xxx |

### CMDB & Inventory

| # | Feature | Tier | Effort |
|---|---------|------|--------|
| CM-01 | Printer audit (firmware, public, default creds) | P3 | 1 session |
| CM-02 | Network device full inventory | P2 | 1 session | — overlaps IA-2 |
| CM-03 | IoT device detection | P3 | 1 session |
| CM-04 | Adobe/QuickBooks/Zoom/Slack SaaS audit | P3 | 2 sessions |
| CM-05 | License optimizer across SaaS portfolio | P3 | 2 sessions |

### Platform / Admin

| # | Feature | Tier | Effort |
|---|---------|------|--------|
| PL-01 | Franchise hierarchy (parent + child reseller) | P3 | 2 sessions |
| PL-02 | White-label customization per franchise | P2 | 1 session |
| PL-03 | Franchise-level RBAC (sub-admins) | P3 | 2 sessions |
| PL-04 | Service catalog UI (existing tables) | P2 | 1 session |
| PL-05 | Billing engine (FranchiseServiceRate) | P3 | 3 sessions |
| PL-06 | Stripe integration | P3 | 2 sessions |
| PL-07 | Invoice generation | P3 | 1 session |
| PL-08 | Revenue dashboard | P3 | 1 session |
| PL-09 | Public API (API key auth) | P3 | 2 sessions |
| PL-10 | Webhooks catalog (beyond CA-15) | P3 | 1 session |
| PL-11 | OData query endpoint | Icebox | 1 session |
| PL-12 | GraphQL endpoint | Icebox | 3 sessions |

### Mobile / UX

| # | Feature | Tier | Effort |
|---|---------|------|--------|
| UX-01 | Responsive portal (tablet/phone) | P2 | 1 session |
| UX-02 | Native iOS/Android app | Icebox | 5+ sessions |
| UX-03 | Slack/Teams bot | Icebox | 2 sessions |
| UX-04 | Browser extension (score badge) | Icebox | 2 sessions |

---

## Portal Gaps (PG) — All resolved ✅

| # | Feature | Status |
|---|---------|--------|
| ~~PG-01~~ | ~~Network Diagnostics portal tab~~ | ✅ `NetworkDiagnosticsTab.tsx` |
| ~~PG-02~~ | ~~SNMP device management portal UI~~ | ✅ `SnmpTab.tsx` |
| ~~PG-03~~ | ~~Network Assessment report viewer~~ | ✅ `ReportGenerator.tsx` network type |

---

## Housekeeping (HK) — Cleanup & consistency tasks

Discovered during 2026-04-20 code audit. Not features, but needed for accuracy.

| # | Task | Tier | Effort | Notes |
|---|------|------|--------|-------|
| HK-01 | ~~Agent version bump `.csproj` to match actual feature level~~ | ✅ | — | Bumped 1.3.0 → 1.5.1 (2026-04-20) |
| HK-02 | ~~CopilotReadinessFunction → return 410 Gone~~ | ✅ | — | Already returns 410 — earlier audit was wrong |
| HK-03 | ~~Remove/redirect M365Tab in portal~~ | ✅ | — | Route already redirected. Dead files `M365Tab.tsx` + `org-detail/CopilotReadinessTab.tsx` deleted |
| HK-04 | ~~Verify NativeCommandEngine + UserRightsApi exist~~ | ✅ | — | Both exist. Earlier audit false negative |
| HK-05 | ~~Verify EventLogEngine `event_count`/`event_top_sources` exist~~ | ✅ | — | Both exist in EventLogEngine.cs + ControlDef.cs |
| HK-06 | ~~Update CLAUDE.md shipped features to match actual code state~~ | ✅ | — | Agent CLAUDE.md v1.2.2→v1.5.1, engines table, services list, payload version, repo layout, master CLAUDE.md updated |
| ~~HK-07~~ | ~~`service_catalog` + `franchise_service_rates` migration (sql/039)~~ | ✅ | — | SQL files exist: `sql/039_service_catalog.sql` + `seed_039_service_catalog.sql`. Verified 2026-04-21 |

---

## Priority Tiers Summary

| Tier | Meaning | Items |
|------|---------|-------|
| **P0** | ~~Immediate~~ | ✅ All shipped (IA-0, IA-11, CA-13, CA-14, IA-1, SH-01, SH-03). **Pre-prod:** SH-02 |
| **P1** | Month 1 | IA-2, IA-3, CA-16 A, CA-17 MFA, DC-02..03, AT-01..03, A-01..02, R-01..02, R-04, SH-04..07 |
| **P2** | Month 2-3 | RP-06, IA-4/5/10/12, CA-18, AC-04/05/06/08, RP-01, SE-01..04, A-03..06, A-10, A-11, DC-04..07, CM-02, PL-02/04, UX-01, SH-08..11 |
| **P3** | Month 3+ | IA-6..9, CA-19/20, A-07/08, AC-07, SE-05/08/09, CM-01/03..05, PL-01/03/05..10, RP-02..04, AT-04/05 |
| **Icebox** | Needs demand signal | SE-06/07, RP-05, PL-11/12, UX-02..04, XP-01 (Linux agent MVP), XP-02 (Mac agent MVP) |

---

## Decisions Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-04-16 | Copilot Readiness = parallel feature from Cloud Assessment (Option 3) | Ship CA without refactor risk |
| 2026-04-18 | Copilot Readiness deprecated, absorbed as Lens into CA | Unified UX request |
| 2026-04-18 | CA scoring weights graceful fallback when areas N/A | Customer tenants vary |
| 2026-04-18 | Benchmark privacy: franchise-only, global anonymized, opt-out per franchise | MSP competitive concerns |
| 2026-04-20 | Auto-remediation manual-only (CA-7) — no automatic status changes | User control always |
| 2026-04-20 | IA (Infrastructure Assessment) new product line parallel to CA | Enterprise client demand (yacimientos use case) |
| 2026-04-20 | ROADMAP.md = orchestrator source of truth, plan file archived | Single entry point for sessions |
| 2026-04-20 | Agent auto-update phased: A-09 detect-only first (RMM pushes actual update), A-10+A-11 later for self-update | Aligns with current RMM-based deploy. EV code signing cert (~$500/yr) deferred until A-11. |
| 2026-04-20 | IA-11 Network Tab added as P0 — promoted above CA-13/CA-14 | Killer differentiator: no MSP tool combines public IP detection + GeoIP + auto-speedtest + device map + SLA tracking. Yacimientos client value: satellite/cellular detection, bandwidth reality vs contracted. |
| 2026-04-20 | IA-12 External Scan Domain Health added as P2 | Reuses CA-10 DNS/SPF/DKIM logic, extends existing ExternalScan feature, 0.5 session effort |
| 2026-04-20 | Full code audit: 9 dead folders → `archive/`, roadmap synced to actual code | Found: External Scan, Threat Detection, SNMP Scanner, Recycle Bin shipped but not in roadmap. Found: Network Diag + SNMP have no portal tab. Found: Agent version discrepancy (csproj 1.3.0 vs docs 1.5.1). Added PG-01..03, HK-01..07, RP-06..07, A-VER |
| 2026-04-20 | Inactive folders archived: Kryoss Partner Portal, Kryoss-main, Kryoss.Portal, Kryoss, New Portal, The Final portal, Assesment, CopilotReadinessAssessment, antigravity | Declutter repo — only KryossApi, KryossAgent, KryossPortal, Scripts, docs, Web, Procedimientos, Propuesta remain active |
| 2026-04-24 | SNMP refinements (10 migrations 051-060): gateway latency, LLDP/CDP counts, device classification, toner tracking, vendor profiles, HOST-RESOURCES-MIB, MAC-based dedup, topology persistence, interface traffic deltas, dedup cleanup | Incremental SNMP maturation — IP-only dedup was unreliable, vendor-specific OIDs needed two-pass scan |
| 2026-04-24 | IA-2 Phase 1: Network Topology (LLDP/CDP neighbor persistence + D3.js graph) | Neighbor data was collected but discarded — now persisted + visualized |
| 2026-04-25 | Agent v2.0.0: Windows Service mode replaces NinjaOne one-shot scheduling | Service self-manages scan intervals (compliance 24h, SNMP 4h, heartbeat 15min). Old NinjaOne script obsolete for production orgs |
| 2026-04-25 | Agent v2.1.0: 9-block network pipeline + remediation | Trial enrollment, port banners, reverse DNS, WMI probe, passive discovery, self-updater, external exposure, closed-set remediation (~50 controls). SQL 061-066 |
| 2026-04-25 | Remediation = closed-set whitelist only | Agent only executes pre-approved action types (set_registry, enable/disable_service, set_audit_policy). No arbitrary command execution. Heartbeat = task delivery channel |
| 2026-04-25 | Self-updater checks `/v1/agent-version` every 6h | Downloads new binary, replaces exe, restarts service. No code signing yet (deferred to A-11 full version) |
| 2026-04-26 | Denormalized `latest_score`/`latest_grade`/`latest_scan_at` on `machines` table | Eliminates correlated subquery in fleet list + org comparison. `EvaluationService` updates on each run. Migration `071_machine_latest_score.sql` |
| 2026-04-26 | EF Core 8: GroupBy+First() → GroupBy+Max()+join-back pattern | EF Core 8 cannot translate `GroupBy().Select(g => g.OrderByDescending().First())` to SQL. Must use `GroupBy+Max()` aggregate then join. Applied to Dashboard Fleet + OrgComparison |
| 2026-04-26 | `GET /v2/machines/by-hostname/{hostname}` endpoint | Portal hostname resolution now uses direct endpoint instead of fetching full machine list. Portal `useMachine` hook auto-detects GUID vs hostname |
| 2026-04-26 | NinjaOne deploy v5.0: auto-installs service mode | Script detects missing service, runs `--install`, migrates legacy scheduled task, auto-updates binary from blob. v2.3.0+ agents transition to service mode on next NinjaOne run |
| 2026-04-26 | Security Sprint 1: 8 CRITICAL findings fixed | ErrorSanitization frozen, hypervisor AES-256-GCM, JWT validation via OIDC, SelfUpdater SCM recovery, trial UseShellExecute removed, enrollment code scrubbed, Setup-Azure password fixed, QUOTENAME in SQL. API 1.22.6 + Agent 2.4.2. Sprints 2-4 (HIGH/MED/LOW) queued. |

---

## Orchestrator Prompt Library

Ready-to-paste prompts for each phase. Copy verbatim into new session.

Pattern each prompt follows:
- Context (what exists, what NOT to touch)
- Scope + deliverables
- File paths + schemas + endpoints
- Build/deploy commands
- Verification checklist
- Split guidance

### → Prompt CA-13 (Intune Deep Verify)

```
Phase CA-13: Verify Intune Deep coverage, fill gaps. Use subagent-driven development.

Context:
- CA-0..CA-12 shipped. CA-2 EndpointPipeline covers Intune partial + Defender for Endpoint.
- Goal: audit CA-2 against original Phase 7 spec, add missing collectors.

Task 1: Read Services/CloudAssessment/Pipelines/EndpointPipeline.cs + Services/CloudAssessment/Recommendations/EndpointRecommendations.cs. List which of these endpoints are covered:

1. /deviceManagement/deviceCompliancePolicies
2. /deviceManagement/deviceConfigurations
3. /deviceAppManagement/iosManagedAppProtections
4. /deviceAppManagement/androidManagedAppProtections
5. /deviceAppManagement/mobileApps
6. /deviceManagement/deviceEnrollmentConfigurations
7. /deviceManagement/windowsAutopilotDeploymentProfiles

Task 2: For each missing: add collector to EndpointPipeline. Extend EndpointInsights class. Add corresponding recommendations. No new tables — reuse cloud_assessment_findings with area="endpoint".

Task 3: Build + deploy + push.

Deliverable: report of "already covered" vs "gap filled" in commit message.

Permissions already consented: DeviceManagementManagedDevices.Read.All, DeviceManagementConfiguration.Read.All. No re-consent needed.

Do NOT touch: Services/CopilotReadiness/* or copilot_readiness_* tables.
```

### → Prompt CA-14 (Auto-Consent)

```
Phase CA-14: Auto-Consent for Power BI (Fabric Admin API) + Azure (Delegated ARM). Use subagent-driven development.

Context:
- CA-12 ConnectCloudWizard shipped — currently Steps 2 (Azure) + 3 (PBI) show manual instructions + verify button.
- Goal: add "Enable automatically" button that does actual work via delegated admin OAuth.
- Result: 3 manual steps → 1 click per service.

Track A — Fabric Admin API:
- New delegated scope: https://api.fabric.microsoft.com/.default
- New OAuth endpoint: GET /v2/cloud-assessment/powerbi/auto-consent-url → returns delegated auth URL with state=orgId
- New callback: GET /v2/cloud-assessment/powerbi/auto-consent-callback
  - Exchange code for delegated token
  - Call POST https://api.fabric.microsoft.com/v1/admin/tenantsettings/ServicePrincipalAccess enable
  - Add Kryoss SPN to allowlist (security group or direct)
  - Verify via existing app-only PBI probe
  - Redirect to portal with ?powerbi_autoenabled=true
- New service: Services/CloudAssessment/FabricAdminService.cs
- Portal ConnectPowerBiCard: new "Enable automatically (admin sign-in)" button + manual fallback link

Track B — Delegated ARM Role Assignment:
- New delegated scope: https://management.azure.com/user_impersonation
- New OAuth endpoints: GET /v2/cloud-assessment/azure/auto-consent-url + callback
  - Delegated token with ARM scope
  - List subs user has access to + Owner or User Access Administrator role check
  - User picks subs to grant Kryoss Reader role
  - PUT /subscriptions/{id}/providers/Microsoft.Authorization/roleAssignments/{guid}?api-version=2022-04-01
- Portal ConnectAzureCard: new "Auto-assign role (admin sign-in)" flow

Risks to handle:
- Customer admin refuses delegated scope → fall back to manual
- Tenant conditional access blocks delegated app → graceful error
- SPN object ID resolution if not yet visible in tenant → Graph fallback

Deliverables:
- FabricAdminService + DelegatedArmService
- 4 new endpoints (2 auto-url + 2 callbacks)
- Portal UI updates on 2 Connect cards
- Actlog: powerbi.auto-enable.*, azure.auto-assign.*
- Build + deploy + push

Permissions needed (add to Kryoss app):
- Delegated: Fabric.Tenant.ReadWrite.All (Fabric) — customer Fabric/PBI Admin consent
- Delegated: user_impersonation (Azure Service Management) — customer Owner/UAA consent
- No new app-only perms

Do NOT touch: CopilotReadiness files.
```

### → Prompt CA-15 (Drift Alerts)

```
Phase CA-15: Drift Alerts + Notifications. Use subagent-driven development.

Context:
- CA-0..CA-14 shipped.
- Goal: detect score regressions / new critical findings between scans, notify MSP via email + optional webhook.

DB additions:
- cloud_assessment_alert_rules (franchise_id, rule_type, threshold, enabled, delivery_channel, target_email|webhook_url, created_at)
- cloud_assessment_alerts_sent (scan_id, rule_id, fired_at, severity, payload_json)

Rule types:
- score_drop_threshold (default ≥0.5)
- new_critical_finding
- new_high_priority_in_regulated_framework (HIPAA/PCI only)
- compliance_framework_below (per-framework threshold)
- copilot_license_utilization_drop

Service: Services/CloudAssessment/AlertService.cs
- Called post-scan from CloudAssessmentService.RunScanInternalAsync
- For each active rule for org's franchise: evaluate vs previous scan
- If triggered: record alert, send email via SendGrid/Azure Comms, POST webhook

Portal: franchise settings → Alerts Config page
- Table of rules with toggle + edit
- Default rules seeded per new franchise
- Test button (fires sample email)

Actlog: alert.triggered, alert.sent, alert.delivery_failed.

Deliverables: DB + service + API endpoints (CRUD rules) + portal config + email template + webhook POST.

Permissions: email sending via SendGrid — requires API key in func-kryoss app settings. Webhook: no auth required (customer-provided URL).
```

### → Prompt IA-0 (Infrastructure Assessment Scaffold)

```
Phase IA-0: Infrastructure Assessment Platform Scaffold. Use subagent-driven development.

Context:
- Parallel product line to Cloud Assessment. Do NOT modify Cloud Assessment code.
- Customer use case: hybrid on-prem + cloud + multi-site (incl. yacimientos / remote industrial sites).
- Agenda mapping: arquitectura actual, capacidad, conectividad, oportunidades.

Goal: foundation only — DB schema + entities + service skeleton + first API endpoint (POST /v2/infra-assessment/scan stub) + portal placeholder tab.

DB migrations:
- infra_assessment_scans (id PK, organization_id FK, scope NVARCHAR(MAX) JSON, status, overall_health DECIMAL, started_at, completed_at, created_at)
- infra_assessment_sites (id PK, scan_id FK CASCADE, site_name, location, type NVARCHAR(30) hq|branch|remote|industrial|datacenter|cloud, device_count, user_count, connectivity_type, created_at)
- infra_assessment_devices (id PK, scan_id FK CASCADE, site_id FK, hostname, device_type server|switch|router|firewall|ap|printer|ups|hvac|plc|iot, vendor, model, role, created_at)
- infra_assessment_connectivity (id PK, scan_id FK CASCADE, site_a_id, site_b_id, link_type mpls|sdwan|ipsec|expressroute|leased|internet|cellular|satellite, bandwidth_mbps, latency_ms, uptime_pct, cost_monthly_usd, created_at)
- infra_assessment_capacity (id PK, scan_id FK CASCADE, device_id, metric_key, current_value DECIMAL, peak_value DECIMAL, threshold DECIMAL, trend_direction stable|increasing|decreasing, created_at)
- infra_assessment_findings (id PK, scan_id FK CASCADE, area hardware|network|connectivity|capacity|ot|migration, service, feature, status, priority, observation NVARCHAR(MAX), recommendation NVARCHAR(MAX), link_text, link_url, created_at)

All with snake_case HasColumnName mappings in KryossDbContext (follow CA pattern).

Backend:
- Services/InfraAssessment/IInfraAssessmentService.cs + InfraAssessmentService.cs (scan orchestrator shell, empty pipelines)
- Services/InfraAssessment/Pipelines/ (folder, empty for now)
- Functions/Portal/InfraAssessmentFunction.cs with:
  - POST /v2/infra-assessment/scan (stub returns 202 + scanId, creates scan row, no actual work yet)
  - GET /v2/infra-assessment?organizationId={id} (latest scan summary)
  - GET /v2/infra-assessment/{scanId} (detail)
  - GET /v2/infra-assessment/history?organizationId={id}

Entities: Data/Entities/InfraAssessment.cs (all 6 entity classes).

Portal:
- KryossPortal/src/components/infra-assessment/InfraAssessmentPage.tsx (placeholder tab)
- KryossPortal/src/api/infraAssessment.ts (hooks stubs)
- Left-menu: new "Infrastructure Assessment" entry below Cloud Assessment

No pipelines yet. No actual data collection. Ships foundation only. Confirms route, auth, DB writes work end-to-end.

Deliverables:
- Migration file
- 6 entities + DbContext registration + column mappings
- Service skeleton + orchestrator shell
- 4 API endpoints (POST + 3 GETs)
- Portal tab placeholder + hooks + left-menu entry
- Build clean: dotnet build + npm run build
- Deploy: func azure functionapp publish func-kryoss + npx swa deploy dist --env production --app-name swa-kryoss-portal --resource-group rg-kryoss
- Push origin/main

Do NOT touch: Cloud Assessment or Copilot Readiness files.

Next phase: IA-1 (Server & Hypervisor Inventory).
```

### → Prompt IA-1 (Server & Hypervisor)

```
Phase IA-1: Server & Hypervisor Inventory. Use subagent-driven development.

Context:
- IA-0 scaffold shipped. Tables + service skeleton + empty pipelines.
- Goal: VMware + Hyper-V + Proxmox inventory + VM consolidation opportunities.

Scope:
- VMware vCenter REST API (requires customer vCenter URL + service account credentials — new cloud_assessment-style connection flow)
- Hyper-V via PowerShell remoting or WMI (from agent installed on host)
- Proxmox REST API (auth token)

DB additions:
- infra_assessment_hypervisors (scan_id, site_id, type vmware|hyperv|proxmox, host_fqdn, version, cpu_cores_total, ram_gb_total, storage_gb_total, vm_count, cluster_name)
- infra_assessment_vms (scan_id, hypervisor_id, vm_name, os, cpu_cores, ram_gb, disk_gb, power_state, last_backup, snapshot_count, cpu_avg_pct, ram_avg_pct, is_idle)

Pipeline: Services/InfraAssessment/Pipelines/HypervisorPipeline.cs

Collectors:
- For each configured vCenter: GET /api/vcenter/vm (VMware vSphere REST API 7.0+)
- For each Hyper-V host: agent-side WMI query (Msvm_ComputerSystem)
- For each Proxmox node: GET /api/2/nodes/{node}/qemu

Findings:
- Idle VM (CPU<5% + no user login 30d) → Medium "Consolidate idle VM"
- Over-provisioned VM (allocated 16vCPU, avg 1.2) → Medium
- VM with stale snapshot (>7d) → Low "Disk space risk"
- VM missing recent backup → High
- Hypervisor host CPU/RAM >85% → High "Capacity exhaustion risk"
- Single hypervisor with no HA cluster → Medium
- End-of-life OS on VM (Windows Server 2012) → High

Portal:
- New sub-tab inside InfraAssessmentPage: "Servers & VMs"
- Hypervisor cards grid (host, cluster, VMs, utilization)
- VM table with sort/filter
- Consolidation opportunities widget ($ savings est)

Connection flow:
- Per-org vCenter connection config (FQDN, service account via encrypted secret)
- Test button
- For Hyper-V: relies on agent installed on host — new agent module KryossAgent.Hypervisor

Deliverables: DB + pipeline + findings + portal sub-tab + connection config UX + agent module stub for Hyper-V.
```

---

## Non-Cloud Prompt Stubs (write detail when queued)

For now, just outline. Full prompts written when phase enters Active Queue.

- DC-01 Domain Controller scope — read CLAUDE.md Phase 2 section first
- A-01 CVE scanner — integrate with existing vulnerability data pipeline
- AT-01 Attestation module — ties to existing assessment engine
- R-01 NinjaRMM deploy — Scripts/Deploy already has Ninja script, wire end-to-end
- RP-01 Monthly Progress — blocked on R-03 Ninja data sync
- AC-01 Custom control builder — new entities, reuse existing control eval

---

## Session Start Checklist

Every new session the orchestrator should:

1. Read this file (ROADMAP.md)
2. Read `CLAUDE.md` (master) + relevant sub-CLAUDE.md (KryossApi/, KryossAgent/, Scripts/)
3. Check Active Queue top item
4. Read that phase's prompt in library section
5. Invoke subagent-driven-development skill
6. Paste prompt verbatim to fresh subagent
7. On completion: update this file (move phase to shipped table + date + notes)
8. Commit + push roadmap change

---

## Historical Reference

Old detailed plan (pre-orchestrator format):
`docs/superpowers/plans/2026-04-16-cloud-assessment-platform.md`

Keep for historical context (decisions, DB schemas, detailed specs). ROADMAP.md is now the execution source of truth.
