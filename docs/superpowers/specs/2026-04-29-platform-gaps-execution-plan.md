# Kryoss Platform Gaps — Execution Plan

**Author:** Claude (Senior Platform Architect review)
**Date:** 2026-04-29
**Status:** DRAFT — pending validation
**Scope:** 27 identified UX/data/functional gaps across Agent, API, and Portal

---

## Security Constraint: No Shell Execution

**MANDATORY:** PowerShell and CMD execution are forbidden by default in the agent.

| Forbidden | Preferred Alternative |
|-----------|----------------------|
| `powershell.exe`, `cmd.exe`, `.ps1` scripts | — |
| `sc.exe`, `net.exe`, `dsquery`, `dsmod` | Win32 API, WMI/CIM, .NET BCL |
| Service management via shell | `System.ServiceProcess.ServiceController` + WMI `Win32_Service` |
| Local user/group management via shell | `System.DirectoryServices.AccountManagement` |
| AD operations via PowerShell AD module | `System.DirectoryServices` (LDAP) |
| Registry via `reg.exe` | `Microsoft.Win32.Registry` |
| Process execution via shell | `System.Diagnostics.Process` (direct EXE, no shell) |

If a shell is absolutely required: document why, assess risk, get approval before implementation.

---

## Current State Assessment

Before planning, cross-referenced each issue against the codebase:

| # | Issue | Current State |
|---|-------|--------------|
| 1 | Agent Loop Status 0–5 | **DONE** — icons + titles added this session |
| 2 | Azure AD join detection | Agent sends `DomainStatus` string — no structured Azure AD join flag |
| 3 | Service startup type change | API has start/stop/restart only — no `set_startup_type` action |
| 4 | Software licensed indicator | No data — would need external license DB or heuristic |
| 5 | Software uninstall | No agent capability — needs new remediation action type |
| 6 | Controls dropdown truncated | Portal CSS issue |
| 7 | Controls grouped by category | `control_categories` table exists — portal not grouping |
| 8 | Grade KPI sizing | Portal CSS issue |
| 9 | Local Admins | **DONE** — API endpoint + portal tab added this session |
| 10 | AD tab when no AD | No conditional rendering — always shows |
| 11 | AD Hygiene full listing + actions | Data: scan+findings model. No user listing. No AD write actions |
| 12 | Privileged accounts split | `AdHygieneFinding.Status = "PrivilegedAccount"` — no group breakdown |
| 13 | EA/SA count alerts | Not implemented |
| 14 | Unconstrained Delegation tooltip | No tooltip/explanation in portal |
| 15 | AdminCount Residual tooltip | No tooltip/explanation in portal |
| 16 | Schema version mapping | `SchemaVersionLabel` field exists on `DcHealthSnapshot` — agent sends raw `V88` |
| 17 | Patches score + missing KBs | Available Updates pipeline exists (v1.36.0). Score display unclear |
| 18 | CVE auto-map + empty state | CVE matching engine exists. Vendor filter bug just fixed (v1.37.1) |
| 19 | Threats: block executables | **ROADMAP** — no current capability |
| 20 | Network Topology relationships | Topology endpoint exists. LLDP/CDP data sparse. No auto-inference |
| 21 | WAN Health no data | **FIXED** — IP resolution fallback added this session (v1.37.2) |
| 22 | Network Diagnostics no data | 1081 rows exist in DB — likely portal display or query issue |
| 23 | Network Ports consolidated | Per-machine `machine_ports` exists — no org-level aggregation |
| 24 | Network Sites no data | Depends on WAN fix (v1.37.2) populating `machine_public_ip_history` |
| 25 | External Scan coverage | Exists for external IPs. No web/mail domain scanning |
| 26 | SNMP Unknown consolidation | `snmp_devices` with no profile → "Unknown". No hostname cross-ref |
| 27 | Cloud dashboard conditional | Cloud KPIs always render. License tiers exist (v1.34.3) |

---

## Phase 1: UX Quick Wins (Portal-Only)

**Objective:** Fix visual/clarity issues. Zero backend changes, zero risk.
**Why first:** Immediate user-facing improvement, builds confidence, unblocks demos.

### UX-01 — Controls Dropdown Truncation
- **Area:** Controls Management
- **Problem:** Dropdown text clips long control names
- **Expected:** Full text visible, tooltip on hover for very long names
- **Dependencies:** None
- **Risk:** Low
- **Validation:** Open controls page, verify longest control names display fully

### UX-02 — Controls Grouped by Category
- **Area:** Controls Management
- **Problem:** Flat list of 600+ controls, hard to navigate
- **Expected:** Controls grouped under `control_categories` (already in DB). Collapsible sections
- **Dependencies:** None (data exists: `control_categories` table + `control_defs.category_id`)
- **Risk:** Low
- **Validation:** Open controls page, verify categories render as groups with correct control counts

### UX-03 — Grade KPI Sizing
- **Area:** Organization Overview / Dashboard
- **Problem:** Grade KPI card visually smaller than other KPIs
- **Expected:** Same visual weight as other KPI cards
- **Dependencies:** None
- **Risk:** Low
- **Validation:** Screenshot compare before/after on dashboard

### UX-04 — AD Hygiene Term Tooltips
- **Area:** Security → Active Directory
- **Problem:** "Unconstrained Delegation" and "AdminCount Residual" are not understandable to MSP technicians
- **Expected:** Info icon (ℹ️) with tooltip explaining:
  - **Unconstrained Delegation:** "This computer can impersonate any user to any service. Attackers who compromise this machine can access anything in the domain."
  - **AdminCount Residual:** "User was once in a privileged group. AD set the adminCount flag but never cleared it when they were removed. This leaves stale elevated permissions on the object's ACL."
- **Dependencies:** None
- **Risk:** Low
- **Validation:** Hover over term, verify tooltip appears with clear explanation

### UX-05 — Schema Version Human-Readable Mapping
- **Area:** Security → AD Health (DC Health)
- **Problem:** Schema shown as "V88" — means nothing to technicians
- **Expected:** Map schema version integers to Windows Server versions:
  - 87 = Windows Server 2016
  - 88 = Windows Server 2019
  - 89 = Windows Server 2022
  - 90 = Windows Server 2025
  Display: "Windows Server 2019 (schema 88)"
- **Dependencies:** None — can be done in portal or in agent `SchemaVersionLabel` field
- **Risk:** Low
- **Validation:** DC Health tab shows human-readable version

### UX-06 — Cloud Dashboard Conditional KPIs
- **Area:** Cloud Assessment
- **Problem:** Azure and Power BI KPIs always show even when not connected — misleading zeros/defaults
- **Expected:** If no Azure subscription connected → hide Azure KPI + Azure section in radar. If no PBI → hide PBI KPI + PBI radar spoke. Show "Connect Azure" / "Connect Power BI" CTA instead
- **Dependencies:** None — `connectionStatus` endpoint already returns which are connected
- **Risk:** Low
- **Validation:** Org with no Azure/PBI → verify KPIs hidden. Org with both → verify KPIs shown

### UX-07 — AD Tab Conditional Display
- **Area:** Security → Active Directory
- **Problem:** AD tab shows empty state for workgroup machines / Azure AD-only orgs
- **Expected:** If org has zero AD hygiene scans AND zero DC health snapshots → show disabled tab with message "No Active Directory detected. This organization appears to use Azure AD / Entra ID only."
- **Dependencies:** None
- **Risk:** Low
- **Validation:** Workgroup-only org → tab disabled with message. Domain org → tab functional

### UX-08 — Patch Score Clarity
- **Area:** Security → Patches
- **Problem:** Patch compliance score meaning unclear to technicians
- **Expected:** Show score breakdown: "X of Y machines fully patched. Z critical KBs missing across fleet." Link to per-machine missing KB list
- **Dependencies:** Available Updates pipeline already populates data (v1.36.0)
- **Risk:** Low
- **Validation:** Patches tab shows clear score explanation + clickable missing KBs

---

## Phase 2: Data Enrichment & Backend Fixes ✅ COMPLETE (2026-04-30)

**Objective:** Fix data pipeline gaps so portal has correct data to display.
**Why second:** Phase 1 covers cosmetics; Phase 2 ensures correct underlying data.

### DAT-01 — Azure AD Join Detection ✅
- **Area:** Machine Detail
- **Problem:** `DomainStatus` is a raw string from agent (e.g., "Workgroup", "DomainJoined"). Doesn't distinguish Azure AD joined from workgroup
- **Expected:** Agent detects join state via **native APIs only** (no `dsregcmd.exe`):
  1. `NetGetJoinInformation()` P/Invoke → returns domain name + join status (domain/workgroup)
  2. Registry `HKLM\SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo\*` → if subkey exists with `TenantId` → Azure AD joined
  3. Combined logic: Domain + CloudDomainJoin = Hybrid. CloudDomainJoin only = Azure AD. Domain only = Domain. Neither = Workgroup
  4. Reports structured field: `domainJoinType` = `"Domain"` | `"AzureAD"` | `"Hybrid"` | `"Workgroup"` + `tenantId` (if Azure AD)
  5. Portal displays badge: "Azure AD Joined" / "Hybrid Joined" / "Domain Joined" / "Workgroup"
- **Dependencies:** Agent change required (new field in hardware payload)
- **Risk:** Medium (agent update required fleet-wide)
- **Validation:** Azure AD-joined machine shows "Azure AD Joined" in portal. Domain machine shows "Domain Joined". Hybrid shows "Hybrid Joined"

### DAT-02 — Privileged Accounts Group Breakdown ✅
- **Area:** Security → Active Directory
- **Problem:** All privileged accounts lumped under `Status = "PrivilegedAccount"`. No distinction between Domain Admins / Enterprise Admins / Schema Admins
- **Expected:** Agent's AD hygiene scan reports group membership per privileged user. `AdHygieneFinding.Detail` or new field stores group name(s). Portal splits into 3 sections: Domain Admins, Enterprise Admins, Schema Admins
- **Dependencies:** Agent change (AD hygiene collector must query group membership per user)
- **Risk:** Medium (agent + API + portal change)
- **Validation:** AD org shows 3 separate groups with correct member counts

### DAT-03 — Privileged Account Count Alerts ✅
- **Area:** Security → Active Directory
- **Problem:** No alerting when Enterprise Admins > 1 or Schema Admins > 1
- **Expected:** Warning badge on AD tab when:
  - Enterprise Admins count > 1 → "⚠ Best practice: max 1 Enterprise Admin"
  - Schema Admins count > 1 → "⚠ Best practice: max 1 Schema Admin"
  Optional: integrate with Drift Alert system (CA-15) for notification
- **Dependencies:** DAT-02 (needs group breakdown first)
- **Risk:** Low (once DAT-02 is done)
- **Validation:** Org with 3 Enterprise Admins → warning badge visible

### DAT-04 — SNMP Unknown Device Consolidation ✅
- **Area:** Network → SNMP
- **Problem:** Known computers appearing as "Unknown" SNMP devices. Multiple "Unknown" groups cluttering view
- **Expected:**
  1. Cross-reference `snmp_devices.ip_address` with `machines.ip_address` → if match, use machine hostname instead of "Unknown"
  2. Consolidate remaining unknowns into single "Unidentified Devices" group with count
  3. Add "Identify" action to manually assign a label to unknown devices
- **Dependencies:** None
- **Risk:** Low
- **Validation:** Known machine IPs show hostname. Unknowns consolidated into one group

### DAT-05 — Network Diagnostics Display Fix ✅
- **Area:** Network → Diagnostics
- **Problem:** "No data shown" despite 1081 rows in `machine_network_diag`
- **Expected:** Diagnostics tab displays data. Need to investigate: is it a query filter issue? org_id mismatch? portal not calling endpoint?
- **Dependencies:** None — data exists
- **Risk:** Low (likely query/filter bug)
- **Validation:** Network Diagnostics tab shows latency, speed, routes for machines with data

### DAT-06 — Network Sites Auto-Population ✅
- **Area:** Network → Sites
- **Problem:** Empty because `machine_public_ip_history` had no data (no public IPs tracked)
- **Expected:** After WAN fix (v1.37.2) deploys: heartbeats populate IPs → `SiteClusterService` auto-creates sites → sites show in portal. May need manual "Rebuild Sites" trigger initially
- **Dependencies:** v1.37.2 deploy (WAN Health fix)
- **Risk:** Low
- **Validation:** After 24h post-deploy: `network_sites` populated, Sites tab shows data

### DAT-07 — CVE Scan Empty State ✅
- **Area:** Security → CVE
- **Problem:** Shows "Scan now" with no results for orgs that haven't triggered a scan
- **Expected:** Auto-scan on first software inventory. If no software data yet, show "Waiting for agent to report software inventory" instead of "Scan now". After vendor filter fix (v1.37.1), CVE matching should work
- **Dependencies:** v1.37.1 deploy (vendor normalization fix)
- **Risk:** Low
- **Validation:** Org with software data → CVE findings auto-populated. Org without → clear message

---

## Phase 3: Service & Agent Capabilities

**Objective:** Add new agent-side capabilities that require coordinated agent + API + portal changes.
**Why third:** Higher risk (agent updates), requires careful testing before fleet rollout.

### AGT-01 — Service Startup Type Change
- **Area:** Machine Detail → Services
- **Problem:** Can only start/stop/restart services. Cannot change startup type (Disabled, Manual, Automatic, Automatic Delayed)
- **Expected:**
  1. New remediation action type: `set_startup_type`
  2. Parameters: `serviceName`, `startupType` (Disabled|Manual|Automatic|AutomaticDelayed)
  3. Agent handler: `System.ServiceProcess.ServiceController` for basic ops + WMI/CIM `Win32_Service.ChangeStartMode()` for startup type (no `sc.exe`)
  4. Portal: dropdown next to service row for startup type change
  5. MFA gate: changing startup type requires MFA confirmation
- **Dependencies:** Remediation pipeline (exists). Agent must implement handler
- **Risk:** Medium (wrong startup type change can break machine)
- **Validation:** Change service from Manual → Automatic. Verify on machine. Change back

### AGT-02 — Software Uninstall
- **Area:** Machine Detail → Software
- **Problem:** No ability to remotely uninstall software
- **Expected:**
  1. New remediation action type: `uninstall_software`
  2. Parameters: `displayName`, `uninstallString` (from registry)
  3. Agent: reads `UninstallString` from registry via `Microsoft.Win32.Registry`, launches via `System.Diagnostics.Process` with silent flags. **No CMD/PowerShell shell** — direct process execution of the uninstaller EXE with args parsed and passed natively
  4. Portal: "Uninstall" button per software item (MFA required)
  5. Safety: block uninstall of protected software (OS components, AV, RMM agent). Validate uninstall string is a valid EXE path, not a script
- **Dependencies:** Remediation pipeline. Agent must implement handler
- **Risk:** **High** (silent uninstall can break machines if wrong software targeted)
- **Validation:** Uninstall known test app. Verify removal. Verify protected list blocks OS components

### AGT-03 — Software Licensed/Paid Indicator ✅
- **Area:** Machine Detail → Software
- **Problem:** No way to know if software is licensed/paid vs. freeware
- **Expected:**
  - Approach A (recommended): Curated list of known commercial software (~200 entries) with boolean `isCommercial` flag. Already have 600+ app detection list in `InventoryFunction` — extend with `isCommercial`
  - Approach B: Agent checks for license keys in registry — too invasive and unreliable
  - Portal: badge "Commercial" / "Freeware" / "Unknown" on each software item
- **Dependencies:** None for curated list approach
- **Risk:** Low (data enrichment only, no agent change needed for Approach A)
- **Validation:** Microsoft Office → "Commercial". 7-Zip → "Freeware". Random app → "Unknown"

---

## Phase 4: Security Actions (MFA-Gated)

**Objective:** Enable administrative actions on endpoints and AD — all require MFA confirmation.
**Why fourth:** Highest blast radius. Needs MFA infrastructure + careful testing.

### SEC-01 — MFA Confirmation Gate
- **Area:** Platform-wide (prerequisite for all security actions)
- **Problem:** No MFA challenge flow for destructive portal actions
- **Expected:**
  1. Portal: when user clicks destructive action → trigger MSAL `acquireTokenPopup` with `claims` challenge or fresh `loginPopup` for step-up auth
  2. API: verify token was issued within last 5 minutes (`auth_time` claim) for MFA-gated endpoints
  3. Fallback: if MSAL step-up not available, use confirmation dialog with reason text input
- **Dependencies:** MSAL already integrated. Entra ID Conditional Access policy must exist for step-up
- **Risk:** Medium (auth flow complexity)
- **Validation:** Click "Disable Account" → MFA prompt appears → action executes only after MFA

### SEC-02 — Local Admin Actions (Disable/Remove)
- **Area:** Security → Local Admins
- **Problem:** Can see local admins but cannot take action
- **Expected:**
  1. "Remove from Administrators" button per account per machine
  2. "Disable Account" button (local accounts only, not domain accounts)
  3. Both actions create remediation tasks → agent executes via `System.DirectoryServices.AccountManagement` (`GroupPrincipal.Members.Remove()`, `UserPrincipal.Enabled = false`)
  4. MFA required (SEC-01)
  5. Audit trail in `remediation_log`
- **Dependencies:** SEC-01 (MFA gate). Remediation pipeline (exists)
- **Risk:** **High** (removing wrong admin can lock out machine)
- **Validation:** Remove test local admin from Administrators group. Verify membership changed

### SEC-03 — AD User Actions (Reset/Disable/Unlock/Delete)
- **Area:** Security → Active Directory
- **Problem:** Can see AD users but cannot take action
- **Expected:**
  1. Actions per AD user: Reset Password, Disable Account, Unlock Account, Delete Account
  2. All actions MFA-gated (SEC-01)
  3. Implementation: agent on DC uses `System.DirectoryServices` (`DirectoryEntry` + LDAP operations) or `System.DirectoryServices.AccountManagement` (`UserPrincipal.SetPassword()`, `.Enabled`, `.UnlockAccount()`, `.Delete()`). **No PowerShell AD module — pure .NET LDAP**
  4. Password reset generates random password via `System.Security.Cryptography.RandomNumberGenerator`, reveals once (MFA required to view)
  5. Delete requires double confirmation + MFA
- **Dependencies:** SEC-01, agent must detect if it runs on DC (check `ProductType == DomainController`), .NET `System.DirectoryServices` (in-box, no extra deps)
- **Risk:** **High** (AD writes have domain-wide impact)
- **Validation:** Disable test user → verify in ADUC. Unlock test user → verify. Reset password → verify login with new password

### SEC-04 — AD Hygiene Full User/Computer Listing
- **Area:** Security → Active Directory
- **Problem:** Only shows findings (stale, dormant, etc.) — not ALL users/computers
- **Expected:**
  1. Agent collects full AD user + computer list (not just anomalies)
  2. New endpoint: `GET /v2/ad-users?organizationId=X` — paginated, filterable
  3. Portal: searchable table with columns: Name, Type (User/Computer), Last Logon, Status (Enabled/Disabled), Group Memberships, OU
  4. Finding badges overlay (Stale, Dormant, PwdNeverExpires) on relevant rows
- **Dependencies:** Agent AD collector expansion. New API endpoint. Portal tab
- **Risk:** Medium (large data volume for big domains — need pagination)
- **Validation:** Domain with 500 users → all 500 visible. Filter by OU → correct subset

---

## Phase 5: Network Enhancements

**Objective:** Complete network module with consolidated views and auto-inference.
**Why fifth:** Network features depend on data flowing (WAN fix must propagate first).

### NET-01 — Network Topology Auto-Relationships
- **Area:** Network → Topology
- **Problem:** Topology graph shows nodes but no edges unless LLDP/CDP data exists
- **Expected:**
  1. Auto-infer relationships: if machine A reports device B via SNMP neighbor table → create edge A↔B
  2. If machine reports default gateway → create edge machine↔gateway
  3. If two machines share same subnet → create implicit "same-LAN" grouping
  4. LLDP/CDP edges rendered as solid lines. Inferred edges as dashed lines
- **Dependencies:** SNMP neighbor data (`snmp_device_neighbors`), machine network config
- **Risk:** Medium (false positives in auto-inference)
- **Validation:** Topology shows connections between machines and their gateways. SNMP neighbors linked

### NET-02 — Network Ports Consolidated View
- **Area:** Network → Ports (new org-level view)
- **Problem:** Port scan results only visible per-machine
- **Expected:**
  1. New endpoint: `GET /v2/network-ports?organizationId=X` — aggregates `machine_ports` across org
  2. Group by port number: "Port 3389 (RDP) — open on 12 machines [list]"
  3. Highlight risky ports (3389, 445, 23, 21, 1433, 3306) with warning badge
  4. Filter by port state (open/filtered/closed)
- **Dependencies:** `machine_ports` table (exists, populated by agent)
- **Risk:** Low
- **Validation:** Org with 20 machines → consolidated port view shows all ports across fleet

### NET-03 — External Scan Domain Coverage
- **Area:** Network → External Scan
- **Problem:** Only scans external IP addresses. No web or mail domain checks
- **Expected:**
  1. Accept domain names in addition to IPs
  2. For web domains: DNS resolution + TLS certificate check (expiry, issuer, SANs) + HTTP security headers (HSTS, CSP, X-Frame-Options)
  3. For mail domains: MX lookup + SPF/DKIM/DMARC record validation
  4. Results added to existing `external_scan_findings` with new finding types
- **Dependencies:** `ExternalScanner` service (exists). DNS resolution capability needed
- **Risk:** Medium (new scan types, DNS queries from Azure Functions)
- **Validation:** Scan `example.com` → shows TLS cert status, MX records, SPF/DKIM/DMARC results

---

## Phase 6: Roadmap / Future

**Objective:** Items explicitly marked as future. NOT included in current execution.

### FUT-01 — Threat Blocking (Block Executables)
- **Area:** Security → Threats
- **Problem:** Can detect threats but cannot block executables
- **Expected (future):** AppLocker or WDAC policy deployment via agent. Block by hash/path/publisher. Requires policy management infrastructure
- **Risk:** **Critical** (wrong block rule = machines unusable)
- **Status:** ROADMAP ONLY — not planned for current cycle

### FUT-02 — Full AD Write Operations at Scale
- **Area:** Security → AD
- **Problem:** SEC-03 covers individual user actions. Bulk operations (mass disable, mass password reset) need additional safety controls
- **Status:** ROADMAP — revisit after SEC-03 proves stable

---

## Execution Order

### Quick Wins (1–2 days, zero risk)
```
UX-01 → UX-02 → UX-03 → UX-04 → UX-05 → UX-06 → UX-07 → UX-08
```
All portal-only. Can be done in one session. Ship immediately.

### Data Pipeline (2–3 days, low risk)
```
DAT-05 → DAT-06 → DAT-07 → DAT-04 → AGT-03
```
- DAT-05 first (just a bug fix — data exists)
- DAT-06 and DAT-07 depend on today's deploy (v1.37.1 + v1.37.2)
- DAT-04 and AGT-03 are enrichment with no breaking changes

### Agent Updates (3–5 days, medium risk)
```
DAT-01 → DAT-02 → DAT-03 → AGT-01
```
- All require agent changes → bundle into one agent release
- DAT-02 → DAT-03 are sequential (group breakdown then alerts)
- AGT-01 (startup type) is independent but ships with same agent build

### High-Risk Actions (5–7 days, high risk)
```
SEC-01 → SEC-02 → AGT-02 → SEC-04 → SEC-03
```
- SEC-01 (MFA gate) is prerequisite for everything
- SEC-02 (local admin actions) is safer than SEC-03 (AD writes)
- AGT-02 (uninstall) needs careful protected-list curation
- SEC-03 (AD writes) last — highest blast radius

### Network Polish (3–4 days, medium risk)
```
NET-01 → NET-02 → NET-03
```
- Can run in parallel with other phases
- NET-03 (domain scanning) is the most complex

### Blockers & Prerequisites

```
v1.37.1 deploy ──→ DAT-07 (CVE auto-map)
v1.37.2 deploy ──→ DAT-06 (Network Sites)
                ──→ WAN Health display
SEC-01 (MFA)   ──→ SEC-02, SEC-03, AGT-01, AGT-02
DAT-02 (groups)──→ DAT-03 (alerts)
```

---

## Out of Scope (Explicitly Excluded)

| Item | Reason |
|------|--------|
| **FUT-01: Threat blocking / AppLocker** | Marked roadmap by user. Critical risk, needs dedicated design |
| **FUT-02: Bulk AD operations** | Needs SEC-03 proven stable first |
| **New modules** | Constraint: no new modules unless strictly necessary |
| **Agent rewrite** | Constraint: incremental changes only |
| **PSA/ticketing integration** | CA-16A suspended (vendor not decided) |
| **Franchise portal** | Separate workstream, not in scope here |
| **Mobile app** | Not discussed, not planned |
| **Linux/macOS agent** | Not in scope |
| **Patch deployment (Track B)** | Approved concept, separate execution plan |
| **Policy baselines / Password manager** | Roadmap items from separate planning |

---

## Summary Matrix

| Phase | Tasks | Effort | Risk | Backend | Agent | Portal |
|-------|-------|--------|------|---------|-------|--------|
| 1: UX Quick Wins | 8 | 1–2 days | Low | — | — | ✓ |
| 2: Data Enrichment | 7 | 2–3 days | Low-Med | ✓ | — | ✓ |
| 3: Agent Capabilities | 3 | 3–5 days | Med-High | ✓ | ✓ | ✓ |
| 4: Security Actions | 4 | 5–7 days | High | ✓ | ✓ | ✓ |
| 5: Network | 3 | 3–4 days | Medium | ✓ | — | ✓ |
| 6: Roadmap | 2 | TBD | Critical | — | — | — |
| **Total** | **27** | **~14–21 days** | | | | |

---

## Already Completed (This Session)

| Original Issue | Task | Version |
|---------------|------|---------|
| #1 Agent Loop Status 0–5 | Icons + titles in MachineDetail.tsx | Portal 1.22.0 |
| #8 Local Admins empty | API endpoint + portal tab | API 1.37.0, Portal 1.22.0 |
| #20 WAN Health no data | IP resolution fallback in middleware | API 1.37.2 |
| #18 CVE empty (vendor filter) | Vendor name normalization in sync + bulk import | API 1.37.1 |
