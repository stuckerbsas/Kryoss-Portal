# Bitacora — Decision Log & Session History

> **Purpose:** Historical record of decisions and shipped sessions. Read when you need context on WHY something was built a certain way. Not needed every session.
>
> **Last updated:** 2026-04-28

---

## Decision Log (chronological)

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-04-08 | Phase 1 = workstation only (W10/W11) | MVP focus, 70% of SMB fleets are workstations |
| 2026-04-08 | Soft-delete legacy 91 controls | FK to `assessment_controls` blocks delete; preserves history |
| 2026-04-08 | Tag all active controls with NIST (100%) | Max flexibility for NIST framework report |
| 2026-04-08 | HIPAA only §164.312 controls | Reports stay honest; admin/physical safeguards need attestation (Phase 6) |
| 2026-04-08 | Agent is dumb, server evaluates | Catalog changes without rescanning; raw state in `machine_snapshots.raw_*` |
| 2026-04-10 | Easy Auth disabled on func-kryoss | All auth via custom middleware (ApiKeyAuth + BearerAuth); Easy Auth caused double-auth |
| 2026-04-10 | Enrollment codes support multi-use (`maxUses`) | MSPs need to enroll multiple machines with one code |
| 2026-04-10 | Agent re-enrollment via `--reenroll` | Clears registry, re-enrolls; server reuses machine row by hostname |
| 2026-04-10 | Auto-create default assessment on enrollment | If org has no assessment, one created automatically |
| 2026-04-10 | Server platforms share workstation controls | Same 647 controls linked; DC split deferred to Phase 2 |
| 2026-04-10 | Portal uses friendly slug URLs | Frontend-only slug resolution, no API changes |
| 2026-04-10 | Agent default API URL compiled in | `https://func-kryoss.azurewebsites.net`, no interactive prompt |
| 2026-04-11 | Self-contained network scan replaces PowerShell deployment | Single .exe does local scan + network discovery + remote PsExec deploy |
| 2026-04-11 | Binary patching via UTF-16LE sentinel replacement | Server-side `BinaryPatcher` replaces sentinels in compiled .exe |
| 2026-04-11 | Multi-disk inventory in `machine_disks` table | One row per drive letter |
| 2026-04-11 | Port scanning persisted in `machine_ports` table | TCP top 100 + UDP top 20 per host |
| 2026-04-11 | AD Hygiene full security audit | Privileged accounts, kerberoastable, unconstrained delegation, LAPS, etc. |
| 2026-04-11 | 600+ commercial software detection list | Server-side normalization in `InventoryFunction` |
| 2026-04-12 | Reports redesigned with Brand 2025 | Montserrat font, framework score gauges, AD hygiene section |
| 2026-04-13 | HMAC error differentiation | Server returns specific errors: timestamp skew vs signature mismatch |
| 2026-04-13 | Auto-provision Entra ID users as viewer | BearerAuthMiddleware creates user on first login instead of 403 |
| 2026-04-13 | M365 Phase 4: admin consent flow | One-click "Connect M365" button, multi-tenant app, no per-customer App Registration |
| 2026-04-13 | M365 50 security checks (was 30) | Added: stale accounts, app registrations, Secure Score, Identity Protection, Intune, DLP, etc. |
| 2026-04-13 | Agent security refactor planned (v1.3.0+) | Remove PsExec, deploy via GPO/NinjaOne/Intune, native .NET P/Invoke |
| 2026-04-14 | Agent v1.3.0+v1.4.0: zero Process.Start | PsExec deleted, all batch engines native (WMI/P/Invoke) |
| 2026-04-14 | Binary trimming: 67 MB -> 11.9 MB (82% reduction) | `PublishTrimmed=true` + feature flags + hand-written JSON writer |
| 2026-04-14 | Agent stateless cycle | Registry wiped after successful upload |
| 2026-04-14 | v1.5.0: 52 legacy command controls now native | NativeCommandEngine routes by CheckType: TLS, UserRights, AppLocker, inline registry, custom |
| 2026-04-15 | Reports 4-type baseline consolidation | 8 variants -> 4 types (C-Level, Technical, Monthly, Preventas) |
| 2026-04-14 | v1.5.1: Protocol Usage Audit | NTLM + SMBv1, 90-day retention, org toggle, 12 new controls |
| 2026-04-16 | Copilot Readiness = parallel from CA (Option 3) | Ship CA without refactor risk |
| 2026-04-17 | CA-6 Subsession A: Azure consent flow | Separate consent model from Graph, Reader RBAC at subscription scope |
| 2026-04-17 | CA-6 Subsession B: Azure Infrastructure pipeline | 20 recommendations, 5-area radar |
| 2026-04-18 | CA-10: Unified Cloud Connect | Single "Connect Cloud" button replaces 3 separate flows |
| 2026-04-18 | CA-11: Benchmarks | Franchise peers + industry + global, NAICS 15 industries x 5 bands |
| 2026-04-18 | CA-12: Unified Cloud Experience | CopilotReadiness deprecated, ConnectCloudWizard, single tab |
| 2026-04-18 | Copilot Readiness deprecated, absorbed as Lens into CA | Unified UX |
| 2026-04-18 | CA scoring weights graceful fallback when areas N/A | Customer tenants vary |
| 2026-04-18 | Benchmark privacy: franchise-only, global anonymized, opt-out | MSP competitive concerns |
| 2026-04-19 | Unified Report System: 17 blocks + 7 recipes via ReportComposer | Replaces monolithic ReportService |
| 2026-04-19 | Phase 2+3: DC vs Member Server detection | Agent ProductType via WMI, PlatformResolver for DC19/DC22/DC25 |
| 2026-04-19 | Phase 5a: Network Diagnostics (no 3rd-party APIs) | Speed test, latency, route table, VPN detection, 50 controls |
| 2026-04-20 | IA-11: Network Sites + GeoIP + Leaflet Map + Speedtest History + SLA | Killer differentiator vs Datto/Ninja/Auvik |
| 2026-04-20 | CA-14: Auto-consent (Fabric + ARM) | Delegated OAuth, 3 manual steps -> 1 click |
| 2026-04-20 | IA-1: Server & Hypervisor Inventory | VMware vCenter REST + Proxmox REST, 7 finding generators |
| 2026-04-20 | A-OFL: Offline Collection Mode | `--offline --share` + `--collect` for internet-less machines |
| 2026-04-20 | DC-01: Domain Controller expansion 40->100 | DcEngine (12th engine), 27 native check types |
| 2026-04-20 | A-13: Server-side scan orchestrator | Hourly check-in, org window 2-6AM, uniform slot distribution |
| 2026-04-20 | IA = new product line parallel to CA | Enterprise client demand (yacimientos use case) |
| 2026-04-20 | ROADMAP.md = orchestrator source of truth | Single entry point for sessions |
| 2026-04-20 | Agent auto-update phased | RMM pushes updates first, self-update later |
| 2026-04-20 | Full code audit: 9 dead folders -> archive/ | Declutter repo |
| 2026-04-21 | CA-15: Drift Alerts + Notifications | 4 rule types, webhook + email delivery |
| 2026-04-24 | SNMP dedup + enrichment | MAC-based dedup, HOST-RESOURCES-MIB, 12 vendor profiles |
| 2026-04-24 | IA-2 Phase 1: Network Topology | LLDP/CDP persistence + D3.js graph |
| 2026-04-25 | Agent v2.0.0: Windows Service mode | Replaces NinjaOne one-shot scheduling |
| 2026-04-25 | Agent v2.1.0: 9-block network pipeline + remediation | Trial, banners, DNS, WMI, passive, self-update, external exposure, remediation |
| 2026-04-25 | Remediation = closed-set whitelist only | No arbitrary command execution |
| 2026-04-25 | Self-updater checks /v1/agent-version every 6h | No code signing yet |
| 2026-04-25 | SH-KEY: Per-machine key rotation + rate limiting | Kerberos-inspired 3-layer auth |
| 2026-04-25 | Agent remote config from portal via heartbeat | 5 config params per machine |
| 2026-04-26 | Denormalized latest_score on machines table | Eliminates correlated subquery |
| 2026-04-26 | EF Core 8: GroupBy+Max()+join-back pattern | EF Core 8 can't translate GroupBy+First() |
| 2026-04-26 | GET /v2/machines/by-hostname endpoint | Direct lookup replaces full list fetch |
| 2026-04-26 | NinjaOne deploy v5.0: auto-installs service mode | Legacy task migration, auto-update from blob |
| 2026-04-26 | Security Sprint 1-4 (30 findings fixed) | See FEATURES-SHIPPED.md for details |
| 2026-04-27 | Reporting Evolution philosophy | "What's wrong? How much? How to fix?" — reports as management tools |
| 2026-04-27 | Queue v3 expanded to 16 items | Gap analysis drove new modules |
| 2026-04-27 | Ookla-style speed test + security fixes | 8 parallel streams, TopologyFunction auth gap fixed |
| 2026-04-27 | ServiceWorker v3: 5 parallel loops | Root cause fix: heartbeat blocked 17h on RIV-DC-01 |
| 2026-04-27 | SEC-REM-FW: HMAC per-task signatures + ProtectedServices | INSERT-only audit trail, 16 core services blocked |
| 2026-04-28 | Nuclear wipe + fresh start | Accumulated test data polluting production views |
| 2026-04-28 | RP-RECIPE-OVERHAUL partial: 3 new blocks | DcHealth, WanHealth, RemediationStatus blocks |
| 2026-04-28 | CA-LH: Lighthouse baseline gap closure | 32 Lighthouse tasks mapped → 15 new checks via Graph Beta + Settings Catalog |
| 2026-04-28 | Feature Inventory: ~42 entries with license/adoption matrix | Per-scan JSON persisted on cloud_assessment_scans |
| 2026-04-28 | Programmatic consent for Defender + Exchange | Post-admin-consent, code grants app roles via Graph API |
| 2026-04-28 | CA-EXO: Exchange Online REST via InvokeCommand | Audit logs, Safe Attachments, EOP/MDO — same backend as EXO PowerShell v3 |
| 2026-04-28 | Exchange Admin role assigned programmatically | directoryRoles/$ref pattern, auto-activates role if dormant |
| 2026-04-28 | CA-TIER: License tier detection from subscribedSkus | Maps 22 features to none/standard/premium via service plan presence |
| 2026-04-28 | PBI TryVerifyPowerBi re-enabled | Was disabled awaiting test licensing; now runs in consent callback |

---

## Session History (chronological)

### 2026-04-10 — Portal MVP + Auth
- Portal Phase 1 deployed (React 18 + Vite + shadcn/ui + MSAL)
- Easy Auth disabled, custom middleware auth
- Enrollment multi-use codes, auto-create assessment

### 2026-04-11-12 — Agent v1.2.2 + API Inventory
- Network scanner, port scanner, AD hygiene audit
- Binary patching sentinels, multi-disk detection
- API: inventory endpoints (hardware/software), hygiene, agent download
- Portal: hardware/software inventory tabs, download agent button
- Reports redesigned with Brand 2025

### 2026-04-13 — HMAC fixes + M365 Phase 4
- HMAC error differentiation (timestamp skew vs signature mismatch)
- Auto-provision Entra ID users as viewer
- M365 50 checks with admin consent flow
- Agent security refactor planned (not implemented)

### 2026-04-14 — Agent v1.3.0-v1.5.1
- Zero Process.Start (PsExec deleted, all engines native)
- Binary trimming 67->12 MB
- 52 legacy command controls now native
- Protocol Usage Audit (NTLM/SMBv1)

### 2026-04-15 — Reports 4-type consolidation
- 8 variants collapsed into 4 clean types

### 2026-04-16-18 — Cloud Assessment CA-0..CA-12
- Full CA platform shipped: 7 areas, 7 frameworks, unified consent wizard
- Copilot Readiness deprecated, absorbed as Lens
- Benchmarks (franchise/industry/global)

### 2026-04-19 — Unified Reports + DC + Network Diag
- ReportComposer with compositional blocks
- DC platform detection (ProductType)
- Network Diagnostics (speed test, latency, 50 controls)

### 2026-04-20 — IA Launch + Network Sites + Auto-Consent
- IA-0 scaffold, IA-1 hypervisor, IA-11 network sites (all 3 tracks)
- CA-14 auto-consent (Fabric + ARM)
- DC-01 expansion (40->100 controls)
- Offline collection mode, scan orchestrator
- Code audit: 9 dead folders archived

### 2026-04-21 — Alerts + Reports Expansion
- CA-15 drift alerts + notifications
- RP-06 business proposal, RP-07 network, RP-08..RP-14 (7 new recipes)
- Monthly progress report

### 2026-04-24 — SNMP + Topology
- SNMP MAC-based dedup + HOST-RESOURCES-MIB + 12 vendor profiles
- IA-2 Phase 1: LLDP/CDP topology + D3.js graph
- 10 SNMP migrations (051-060)

### 2026-04-25 — Agent v2.0.0-v2.3.0
- Windows Service mode (ServiceWorker, ScanCycle, P/Invoke install)
- 9-block network pipeline + remediation
- Per-machine key rotation (SH-KEY, 3-layer auth)
- Remote config from portal via heartbeat

### 2026-04-26 — Security Sprints + Features
- Security Sprint 1: 8 CRITICAL (ErrorSanitization, AES-256-GCM, JWT validation, etc.)
- Security Sprint 2: 13 HIGH (schedule auth, Redis nonce, SPKI, path whitelist, etc.)
- Security Sprint 3+4: 9 MEDIUM + 8 LOW
- SH-02: M365 secret -> Key Vault
- SH-BATCH: CSP meta, 10MB body limit, CSPRNG, Content-Disposition
- IA-3: WAN health (11 findings, traceroute, jitter/loss)
- A-01: CVE scanner (~60 built-in CVEs)
- A-02: Patch compliance (score 0-100)
- DC-02+03: AD schema/replication + FSMO

### 2026-04-27 — ServiceWorker v3 + Remediation Framework + Speed Test
- Agent v2.7.0: Ookla-style multi-stream speed test (8 parallel)
- Agent v2.8.0: ServiceWorker v3 (5 parallel loops, per-loop timeouts)
- Agent v2.9.0 + API v1.31.0: SEC-REM-FW (HMAC per-task, ProtectedServices, ServiceHealer)
- Portal v1.18.0: ServicesTab, ActivityTab
- GAP-VUL: 3 new report blocks (Vulnerability, PatchCompliance, ExternalExposure)
- Security: TopologyFunction auth, InventoryFunction N+1 fix, RecycleBin N+1 fix

### 2026-04-28 — DB Wipe + Report Blocks + Lighthouse + Exchange + License Tiers
- Nuclear wipe ~60 operational tables (fresh start)
- 3 new report blocks: DcHealth, WanHealth, RemediationStatus
- RP-RECIPE-OVERHAUL 6/9 done (remaining: SNMP, topology, hypervisor)
- CVE sync first run (14 KEV flagged)
- **CA-LH:** Mapped 32 M365 Lighthouse baseline tasks → identified 15 gaps → implemented all via Graph Beta, Settings Catalog, configurationPolicies. New checks: SSPR, break-glass, device join, AV/FW/ASR policies, Edge/OneDrive/WinUpdate profiles, notification templates, endpoint analytics, Defender auto-onboard
- **Feature Inventory:** ~42 entries across 7 areas (connections/identity/endpoint/data/productivity/mail_flow/azure/powerbi) with licensed/implemented/adoptionPct/detail
- **CA-EXO:** Exchange Online REST API via InvokeCommand pattern — 3 checks: unified audit logs (Get-AdminAuditLogConfig), safe attachments (Get-SafeAttachmentPolicy), EOP/MDO standard/strict protection (Get-EOPProtectionPolicyRule). Exchange.ManageAsApp + Exchange Admin role granted programmatically during consent
- **CA-TIER:** License tier detection — reads subscribedSkus service plans, resolves 22 features to none/standard/premium. Covers Entra P1/P2, Intune P1/P2, Purview basic/advanced, Defender O365 P1/P2, MDE, Copilot
- **PBI consent re-enabled** in UnifiedCloudConnectFunction
- API 1.34.0 → 1.34.3 (4 patches this session)
