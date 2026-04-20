# Kryoss Master Roadmap — Orchestrator

> **Role of this file:** Single source of truth for what's done, what's queued, what's backlog. Use as orchestrator entry-point at start of every session. Update status inline as phases ship.
>
> **Last updated:** 2026-04-20
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

## Current State Snapshot (2026-04-20)

**Shipped in production:**
- Kryoss Agent v1.5.1 — 647 active controls, 11 engines, AD hygiene, port scan, native (zero Process.Start)
- Assessment engine — 5 frameworks (CIS, NIST, HIPAA, ISO27001, PCI-DSS), 4 report types (C-Level, Technical, Preventas, Executive)
- M365 Security Checks (50 checks) — DEPRECATED, rolled into Cloud Assessment
- Copilot Readiness Assessment — DEPRECATED as standalone (2026-04-18), data flows now via Cloud Assessment Copilot Lens
- **Cloud Assessment platform** — CA-0..CA-12 COMPLETE. 7 areas (identity, endpoint, data, productivity, azure, powerbi, compliance), 7 frameworks with benchmark comparisons, unified consent wizard, Copilot Lens filter view

**In progress:** None

**Customer context driving next priorities:**
- Multi-site enterprise client expects infrastructure relevamiento (agenda: arquitectura actual, capacidad, conectividad entre sitios + yacimientos, optimización, evolución cloud)
- → Infrastructure Assessment (IA) product line added as parallel to Cloud Assessment

---

## Active Queue — Next 5 Phases

Execute in order. Each ships independently. Prompts ready below.

| # | Phase | Status | Est | Blocks |
|---|-------|--------|-----|--------|
| 1 | **IA-0** Infrastructure Assessment Scaffold | ⚪ queued | 1 session | All IA phases |
| 2 | **CA-13** Intune Deep verify/gap-fill | ⚪ queued | 0-1 session | None |
| 3 | **CA-14** Auto-consent (Fabric + ARM) | ⚪ queued | 2 sessions | None (UX win) |
| 4 | **IA-1** Server & Hypervisor Inventory | ⚪ queued | 2 sessions | IA-10 report |
| 5 | **IA-2** Network Topology Discovery | ⚪ queued | 2 sessions | IA-10 report |

After 5 complete → replan from Backlog.

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

---

## Cloud Assessment (CA) — Remaining Gaps

### CA-13: Intune Deep Verify + Gap Fill
**Priority:** P0 — verify first, may be done
**Effort:** 0-1 session

Audit CA-2 coverage vs spec. Confirm:
- [ ] App protection policies iOS + Android
- [ ] Managed apps inventory
- [ ] Enrollment restrictions
- [ ] Autopilot deployment profiles
- [ ] Configuration profiles drift detection

If gaps: add to EndpointPipeline, no new area.

**Prompt ready below** in prompt library.

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

### CA-15: Drift Alerts + Notifications
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

### IA-0: Scaffold
**Priority:** P0 — unlocks all IA
**Effort:** 1 session

New DB: `infra_assessment_*` (scans, sites, devices, connectivity, capacity, findings).
New service `Services/InfraAssessment/`.
New portal tab, new report type `infra-assessment`.

### IA-1: Server & Hypervisor Inventory
**Priority:** P1
**Effort:** 2 sessions

- VMware vCenter API integration
- Hyper-V host + VM enumeration
- Proxmox detection
- Per-VM resource allocation vs utilization
- Consolidation opportunities

### IA-2: Network Topology Discovery
**Priority:** P1
**Effort:** 2 sessions

- Full L2/L3 via CDP/LLDP + SNMP
- Switch/router config snapshots (read-only)
- Wireless AP inventory + channel analysis
- Firewall rules (where accessible)
- D3.js topology visualization

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

---

## Non-Cloud Roadmap (Kryoss Core)

### Agent (KryossAgent v1.6+)

| # | Feature | Tier | Effort |
|---|---------|------|--------|
| A-01 | CVE scanner (standalone, beyond Defender) | P1 | 1 session |
| A-02 | Patch compliance tracking (WSUS/WUfB/Ninja) | P1 | 1 session |
| A-03 | Backup verification (Veeam/Acronis/Ninja signal) | P2 | 1 session |
| A-04 | BitLocker escrow verification | P2 | 0.5 session |
| A-05 | Firewall rules inventory (full rule list) | P2 | 1 session |
| A-06 | Scheduled tasks inventory (persistence detection) | P2 | 0.5 session |
| A-07 | Browser extensions inventory | P3 | 1 session |
| A-08 | Browser saved passwords risk signal | P3 | 0.5 session |
| A-09 | Agent version detection + drift dashboard | P1 | 1 session |
| A-10 | Sentinel decouple binary → registry (prereq for auto-update) | P2 | 1 session |
| A-11 | Self-update helper binary (updater.exe + hash verify + rollback) | P2 | 2-3 sessions |
| A-12 | Staged rollout + update channels (stable/beta, 5→25→100%, auto-rollback) | P3 | 2 sessions |

### Domain Controller Scope (Phase 2 per CLAUDE.md)

| # | Feature | Tier | Effort |
|---|---------|------|--------|
| DC-01 | DC19/22/25 platform codes + control mapping (~100 DC controls) | P1 | 2 sessions |
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
| RP-01 | Monthly Progress report | P2 | 1 session | Needs Ninja data |
| RP-02 | SOC 2 Type II evidence report | P2 | 1 session | Needs AT-01..AT-04 |
| RP-03 | Vendor risk report (SaaS inventory) | P3 | 1 session |  |
| RP-04 | Insurance renewal questionnaire report | P3 | 1 session |  |
| RP-05 | Board-level exec report | Icebox | 1 session |  |

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

## Priority Tiers Summary

| Tier | Meaning | Items count |
|------|---------|-------------|
| **P0** | Immediate — next 5 sessions | IA-0, CA-13, CA-14, IA-1, IA-2 |
| **P1** | Month 1 | IA-3, CA-15, CA-16 A, CA-17 MFA, DC-01..03, AT-01..03, A-01..02, A-09, R-01..02, R-04 |
| **P2** | Month 2-3 | IA-4/5/10, CA-18, AC-04/05/06/08, RP-01, SE-01..04, A-03..06, A-10, A-11, DC-04..07, CM-02, PL-02/04, UX-01 |
| **P3** | Month 3+ | IA-6..9, CA-19/20, A-07/08, AC-07, SE-05/08/09, CM-01/03..05, PL-01/03/05..10, RP-02..04, AT-04/05 |
| **Icebox** | Needs demand signal | SE-06/07, RP-05, PL-11/12, UX-02..04 |

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
