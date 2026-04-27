# Kryoss Platform — Roadmap

**Last updated:** 2026-04-26 | **API** 1.27.0 | **Portal** 1.17.0 | **Agent** 2.6.2

---

## Shipped Features

### Core Assessment (v1.0-v1.5)
- [x] Azure SQL schema (63+ tables), RBAC, RLS, actlog
- [x] .NET 8 Azure Functions API with HMAC + Bearer auth
- [x] Agent: 12 native engines, zero Process.Start, 827 controls
- [x] Server-side evaluation (PASS/FAIL/WARN), per-framework scoring
- [x] Portal: React 18 + Vite + shadcn/ui + MSAL (7 org tabs)
- [x] Binary patching for org-specific agent .exe downloads
- [x] Platform scoping: W10/W11/MS19/MS22/MS25/DC19/DC22/DC25

### Network & Infrastructure (v1.5-v2.5)
- [x] Network diagnostics (speed, latency, VPN, routes, adapters)
- [x] SNMP discovery (MAC dedup, HOST-RESOURCES-MIB, vendor profiles)
- [x] Network topology (LLDP/CDP, D3.js force graph)
- [x] Network sites (auto-cluster by public IP, GeoIP, Leaflet map)
- [x] WAN health scoring (0-100, 11 finding rules, traceroute)
- [x] Port scanning (TCP top 100 + UDP top 20)
- [x] External exposure scanning (server-side, consent-gated)
- [x] Passive discovery (NetBIOS/mDNS/SSDP listeners)

### Cloud Assessment (CA-0 through CA-15)
- [x] M365: 50 security checks via Graph API, admin consent flow
- [x] Azure: resources, Defender, NSG, Key Vault, VM audits
- [x] Power BI: workspace security, sharing, dataset refresh
- [x] Unified cloud connect wizard (M365 + Azure + PBI in one flow)
- [x] Auto-consent (Fabric + ARM delegated flows)
- [x] Copilot Readiness Lens (D1-D6 from cloud scan)
- [x] Drift alerts + notification rules (webhook + email)
- [x] Benchmarks (franchise/industry/global, radar overlay)
- [x] 5-area radar scoring (Identity/Endpoint/Data/Productivity/Azure)

### Security & Compliance
- [x] RSA-2048 + AES-256-GCM envelope encryption
- [x] HMAC-SHA256 request signing with nonce cache
- [x] Per-machine key rotation (Kerberos-inspired 3-layer auth)
- [x] Hardware fingerprint (SHA-256, server-bound)
- [x] Error sanitization middleware
- [x] CVE scanner (60+ built-in patterns, semantic version matching)
- [x] Patch compliance (WU status, hotfix history, compliance score)
- [x] Protocol audit (NTLM/SMBv1 90-day measurement)

### Domain Controller
- [x] DC detection (ProductType=2 via WMI)
- [x] 100 DC-specific controls (DcEngine, 27 native check types)
- [x] AD hygiene (stale/dormant, privileged, kerberoastable, LAPS)
- [x] DC health (schema version, FSMO roles, replication status)

### Agent (v2.0-v2.6)
- [x] Windows Service mode (--install/--uninstall/--service)
- [x] Compliance scan every 24h, SNMP every 4h, heartbeat every 15min
- [x] Self-updater (blob storage check every 6h)
- [x] Remote configuration from portal (5 params via heartbeat)
- [x] Offline collection mode (--offline/--collect)
- [x] Closed-set remediation (whitelist catalog, tasks with rollback)
- [x] Trial mode + auto-report generation

### Reports (7 types)
- [x] C-Level, Technical, Risk Assessment, One-Pager
- [x] Pre-Sale Opener/Detailed, Business Proposal (auto-pricing)
- [x] Framework Compliance (NIST/CIS/HIPAA/ISO27001/PCI-DSS)
- [x] Network, Cloud Executive, AD Hygiene, Inventory
- [x] Monthly Progress, M365
- [x] Compositional block system (17 blocks, 15 recipes)
- [x] Scan orchestrator (server-assigned time slots)

---

## In Progress / Known Issues

### Bugs (2026-04-26 session)
- [x] Agent scan loop: retried every 15min on failure → now 30min backoff
- [x] AD hygiene dedup: old scans pile up → now retains last 5 per org
- [x] Portal: SoftwareInventoryTab missing React key prop
- [x] Portal: BenchmarksTab import triggering dead API call
- [x] Portal: infra-assessment hook returning undefined
- [x] Portal: AgentConfigCard switches not toggling (optimistic state)
- [x] Portal: NetworkDiagnostics 1min load (lazy detail load)
- [x] Portal: Protocol Usage tab placeholder → real data from control_results

### Deploy Gaps (need API redeploy)
- [ ] /v2/organizations returns 500 (entity mismatch with DB)
- [ ] /v2/patch-compliance returns 500 (migration not applied)
- [ ] /v2/external-scan returns 500 (migration not applied)
- [ ] Sites/WAN health empty (migration dependencies)

### Incomplete Features
- [ ] Topology edges: LLDP/CDP neighbor resolution rarely matches both endpoints
- [ ] Benchmarks: hidden, needs more org data for meaningful comparisons

---

## Backlog — Priority Order

### P0 — Deploy & Stabilize
- [ ] Redeploy API with all pending migrations (067-075)
- [ ] Verify all tabs load without 500s on Cox Science Center
- [ ] Reports: investigate and fix current breakage

### P1 — Security Remediation
- [ ] Security audit findings (30 items across API/Agent/Portal/Scripts)
- [ ] Spec: docs/superpowers/specs/2026-04-26-security-remediation-design.md

### P2 — Optimization & Code Quality

**API (from code review 2026-04-26):**
- [ ] **CRITICAL: Missing auth** — TopologyFunction, AutoConsentFunction callback, CatalogControlsFunction have no [RequirePermission]
- [ ] **CRITICAL: Missing org scoping** — InventoryFunction accepts arbitrary orgId without franchise validation
- [ ] **HIGH: N+1 queries** — InventoryFunction.Software loops per machine; RecycleBinFunction has 3 subqueries per row
- [ ] **HIGH: No pagination** — InventoryFunction (hardware+software), TopologyFunction, RecycleBinFunction, DashboardFunction.Fleet return unbounded result sets
- [ ] **MEDIUM: AutoConsentFunction** — PBI callback has no try/catch on token acquisition, no CSRF state validation
- [ ] API: connection pooling for parallel report queries

**Portal (from code review 2026-04-26):**
- [ ] **Extract shared constants** — severityColors, SERVICE_LABELS, STATUS_LABELS duplicated across 3+ files
- [ ] **React.memo** on list/table row components (zero usage currently → unnecessary re-renders)
- [ ] **Code-split Recharts** — imported unconditionally in OverviewTab, BenchmarksTab (only renders conditionally)
- [ ] **Error handling** — EnrollmentTab createCode.mutateAsync has no error toast; ConnectAzureCard has silent failures
- [ ] Portal: lazy load heavy tabs (Cloud Assessment, Reports)
- [ ] Portal: virtualize large tables (machines list, software inventory)

**Agent:**
- [ ] Reduce heartbeat payload when nothing changed

### P3 — Feature Completion
- [ ] Patch deployment Track B (WUA COM, rings + test plans)
- [ ] Hierarchical policy baselines (HQ → Franchise → Org)
- [ ] Per-org notes/annotations
- [ ] Password/TLS manager
- [ ] Hyper-V inventory (agent WMI module, IA-1b)

### P4 — Integrations (Deferred)
- [ ] PSA integration (vendor TBD — CA-16A suspended)
- [ ] NinjaRMM sync (orgs, alerts → tickets)
- [ ] SendGrid transactional emails

### P5 — Future
- [ ] CRM module (contacts, deals, pipeline)
- [ ] Helpdesk / ticketing with SLA
- [ ] Billing / invoicing + Odoo sync
- [ ] Multi-language portal (i18n ES/EN/PT)
- [ ] Mobile PWA for field techs
- [ ] AI: remediation recommendations, anomaly detection

---

*This file tracks what's shipped and what's next. For implementation specs see `docs/superpowers/specs/`. For detailed plans see `docs/superpowers/plans/`.*
