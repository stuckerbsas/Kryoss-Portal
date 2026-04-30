# Backlog — Pending Work by Priority

> **Purpose:** All future work organized by tier. Read when planning next session or when user asks "what's next." Active queue items at top.
>
> **Last updated:** 2026-04-28

---

## Active Queue — Next Up

From Queue v2 + v3 remaining items. Execute in priority order.

| # | Phase | Priority | Est | What |
|---|-------|----------|-----|------|
| 1 | **RP-RECIPE-OVERHAUL** (3/9 remain) | P1 | 0.5 session | SNMP->Network/Inventory, Topology->Network, Hypervisor->Inventory recipes |
| 2 | **GAP-REM** Remediation Action Plan Generator | P0 | 1-2 sessions | Map Control_ID->PowerShell fix, auto-generate action plan per FAIL, inject into Technical Report |
| 3 | **RP-DC** AD & Domain Health Report | P1 | 1 session | New recipe: 100 DC controls + replication + FSMO + schema |
| 4 | **RP-WAN** WAN & Network Health in Reports | P1 | 1 session | SNMP + topology + WAN health into NetworkRecipe |
| 5 | **RP-VULN** Vulnerability & Patch Report | P1 | 1 session | CVE + patch compliance + external scan standalone recipe |
| 6 | **CA-17** Auto-remediation first 3 handlers | P1 | 1 session | MFA enable + disable legacy auth + Customer Lockbox |
| 7 | **IA-10** Infrastructure Assessment Report | P2 | 1 session | Dedicated report: hypervisors + SNMP + topology + WAN |
| 8 | **SH-HARDEN** Security hardening final | P1-P2 | 1 session | SPKI enforce (SH-07) + anomaly scoring (SH-10) |

**Suspended:** CA-16A (ConnectWise PSA) — vendor not decided.

---

## P1.5 — After Active Queue

| Phase | Est | What |
|-------|-----|------|
| **SEC-SHADOW** Shadow Admin Audit | 1 session | Domain root ACLs + AdminSDHolder, effective admin rights detection |
| **SEC-BACKUP** Backup Isolation Check | 1 session | Ransomware resilience: probe backup repos from workstations |
| **CA-COPILOT** Copilot Exposure Lens | 1 session | SharePoint/OneDrive "Everyone" permissions audit, Copilot data risk score |
| **SEC-EGRESS** Egress Filtering Audit | 1 session | Non-standard outbound from servers (DNS/HTTPS/SMTP unusual ports) |
| **RP-INSURE** Cyber Insurance Readiness | 1 session | Auto-generate insurance questionnaire readiness score from existing data |
| **CA-OAUTH** SaaS/OAuth Governance | 1 session | M365 3rd-party app registrations with excessive permissions |

---

## P2 — Month 2-3

### Infrastructure Assessment (hidden from portal 2026-04-28 — stub, no real pipeline)
| Phase | What |
|-------|------|
| IA-0 | **Build real IA pipeline** — InfraAssessmentService currently stub (only creates scan row). Need: site discovery, device audit, hypervisor scan, capacity metrics. Portal tab + route removed until ready. Files kept: `InfraAssessmentTab.tsx`, `infraAssessment.ts`, `InfraAssessmentFunction.cs`, `HypervisorConfigFunction.cs` |
| IA-2 P2 | Network Topology: switch/router config, wireless AP, firewall rules, VLAN overlay, traffic flow |
| IA-4 | Cloud Connectivity Audit: ExpressRoute, Virtual WAN, VPN Gateway |
| IA-5 | Capacity Planning: 90d time-series + linear regression, 6/12/18-month projections |
| IA-12 | External Scan Domain Health: DNS-based domain health (reuse CA-10 SPF/DKIM/DMARC) |

### Agent
| Phase | What |
|-------|------|
| A-03 | Backup verification (Veeam/Acronis/Ninja signal) |
| A-04 | BitLocker escrow verification |
| A-05 | Firewall rules inventory (full rule list) |
| A-06 | Scheduled tasks inventory (persistence detection) |
| A-10 | Sentinel decouple binary -> registry (prereq for auto-update) |
| A-11 | Self-update full: rollback + hash verify (basic shipped, full pending) |
| A-11b | NinjaRMM auto-update via script variable (lighter than A-11) |

### Domain Controller
| Phase | What |
|-------|------|
| DC-04 | GPO inventory + inheritance analysis |
| DC-05 | DNS zone audit (stale records, secure dynamic updates) |
| DC-07 | Domain functional level recommendations |

### Assessment & Controls
| Phase | What |
|-------|------|
| AC-01 | Custom control builder |
| AC-02 | Control exceptions workflow (with expiry) |
| AC-04 | ISO 27001:2022 full (90+ Annex A) |
| AC-05 | NIST 800-171 / CMMC |
| AC-06 | PCI DSS 4.0 full (250+ sub-reqs) |
| AC-08 | CIS Critical Security Controls v8 full |

### Security
| Phase | What |
|-------|------|
| SH-08 | `dotnet list package --vulnerable` audit |
| SE-01 | TLS cert expiration monitoring |
| SE-02 | HaveIBeenPwned leaked creds check |
| SE-03 | Shadow IT discovery (Defender Cloud Apps deep) |
| SE-04 | DNS record audit (subdomain takeover) |

### Reports
| Phase | What |
|-------|------|
| RP-TREND | Historical trend tracking: `report_history` table, score over time, sparklines |
| DASH-RISK | Risk Cost Dashboard: financial weight per finding, speak to business owners |

### Platform
| Phase | What |
|-------|------|
| PL-02 | White-label customization per franchise |
| PL-04 | Service catalog UI (tables exist) |
| PL-05 | **Org offboarding / uninstall** — remote agent uninstall command, soft-delete org + all data (60-day retention), revoke API keys, disconnect M365 tenant. Hard purge only after 60 days via scheduled job. For churned clients. |
| UX-01 | Responsive portal (tablet/phone) |

---

## P3 — Month 3+

| Phase | What |
|-------|------|
| IA-6 | Collaboration Audit (Teams Rooms, phone, video) |
| IA-7 | OT / Industrial Network Detection (SCADA/PLC, Modbus, DNP3) |
| IA-8 | Cloud Migration Readiness (per-workload classification + TCO) |
| IA-9 | Availability & SLA Tracking (MTTR/MTBF, dependency tree) |
| CA-18 | Benchmark Periodic Refresh (quarterly, needs 100+ tenants) |
| CA-19 | Graph Connectors + Copilot Knowledge Sources |
| CA-20 | Audit Log Deep Dive (sign-in risk, admin actions, impossible travel) |
| DC-06 | DFS namespace health |
| A-07 | Browser extensions inventory |
| A-08 | Browser saved passwords risk signal |
| A-12 | Staged rollout + update channels (stable/beta, 5->25->100%) |
| AC-03 | Policy upload -> auto-map to controls |
| AC-07 | Essential 8 (ACSC Australia) |
| SE-05 | Breach notification monitoring |
| SE-08 | Phishing simulation |
| SE-09 | Security awareness training tracking |
| AT-01..05 | Attestation Module (policy docs, evidence, per-control workflow, SOC2, audit trail) |
| RP-02 | SOC 2 Type II evidence report |
| RP-03 | Vendor risk report (SaaS inventory) |
| RP-04 | Insurance renewal questionnaire (-> see RP-INSURE) |
| CM-01 | Printer audit (firmware, default creds) |
| CM-03..05 | IoT detection, Adobe/Zoom SaaS audit, license optimizer |
| PL-01 | Franchise hierarchy (parent + child reseller) |
| PL-03 | Franchise-level RBAC (sub-admins) |
| PL-05..08 | Billing engine, Stripe, invoices, revenue dashboard |
| PL-09..10 | Public API, webhooks catalog |
| R-01..07 | RMM/PSA integrations (NinjaRMM deploy/sync/fields, ConnectWise, Autotask, HaloPSA, Intune deploy) |

---

## Icebox — Needs Demand Signal

| Phase | What |
|-------|------|
| SE-06 | Typosquat domain detection |
| SE-07 | Brand impersonation monitoring |
| RP-05 | Board-level exec report |
| PL-11 | OData query endpoint |
| PL-12 | GraphQL endpoint |
| UX-02 | Native iOS/Android app |
| UX-03 | Slack/Teams bot |
| UX-04 | Browser extension (score badge) |
| XP-01 | Linux agent MVP (~50 controls: SSH, ufw, sysctl, PAM) |
| XP-02 | Mac agent MVP (~30 controls: FileVault, Gatekeeper, SIP) |
| CM-02 | Network device full inventory (overlaps IA-2) |

---

## Prompt Stubs (expand when queued)

- **GAP-REM:** Map Control_ID -> remediation snippet, auto-generate action plan, inject into Technical Report. Shift Zero philosophy.
- **RP-DC:** New recipe consolidating 100 DC controls, replication, FSMO, schema, DCDiag-equivalent.
- **CA-17:** Ship one handler per session: MFA enable, disable legacy auth, Customer Lockbox. Each: preview + apply + rollback + actlog.
- **IA-10:** Dedicated report type, 10 sections matching customer agenda (arquitectura, capacidad, conectividad, cloud).
- **DC-04:** GPO inventory via LDAP `CN=Policies,CN=System`, inheritance analysis per OU, orphaned GPO detection.
