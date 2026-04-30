# Kryoss Platform — Test Plan

**Date:** 2026-04-26
**Scope:** All deployed modules across Agent, API, Portal
**Method:** SQL verification + API calls + Portal visual check
**Org for testing:** Cox Science Center (most machines, most data)

---

## How to use this plan

Each test has:
- **SQL** = run in SSMS against KryossDb
- **API** = curl against func-kryoss (use Bearer token)
- **Portal** = visual check in browser
- **Agent** = check on a machine with agent installed
- Result: ✅ PASS / ❌ FAIL / ⚠️ PARTIAL / 🔲 NOT TESTED

---

## 1. CORE — Enrollment & Auth

### 1.1 Enrollment codes exist and work
```sql
SELECT ec.code, ec.label, ec.max_uses, ec.use_count, ec.expires_at, ec.is_expired, o.name
FROM enrollment_codes ec
JOIN organizations o ON o.id = ec.organization_id
WHERE ec.deleted_at IS NULL
ORDER BY ec.created_at DESC;
```
**Expected:** Active codes with uses remaining
**Result:** 🔲

### 1.2 Machines are enrolled
```sql
SELECT o.name, COUNT(*) AS machines, 
       SUM(CASE WHEN m.agent_version IS NOT NULL THEN 1 ELSE 0 END) AS with_version,
       MIN(m.agent_version) AS min_ver, MAX(m.agent_version) AS max_ver
FROM machines m
JOIN organizations o ON o.id = m.organization_id
WHERE m.is_active = 1 AND m.deleted_at IS NULL
GROUP BY o.name ORDER BY machines DESC;
```
**Expected:** Machines per org with agent versions
**Result:** 🔲

### 1.3 Auth keys exist
```sql
SELECT o.name, ak.key_prefix, ak.is_active, ak.created_at
FROM auth_api_keys ak
JOIN organizations o ON o.id = ak.organization_id
WHERE ak.is_active = 1
ORDER BY o.name;
```
**Expected:** One active key per org
**Result:** 🔲

### 1.4 Per-machine auth (SH-KEY)
```sql
SELECT hostname, auth_version, 
       CASE WHEN machine_secret IS NOT NULL THEN 'YES' ELSE 'NO' END AS has_secret,
       CASE WHEN session_key IS NOT NULL THEN 'YES' ELSE 'NO' END AS has_session,
       session_key_expires_at, key_rotated_at
FROM machines 
WHERE is_active = 1 AND deleted_at IS NULL
ORDER BY auth_version DESC, hostname;
```
**Expected:** v2.2+ agents should have auth_version=2, machine_secret, session_key
**Result:** 🔲

### 1.5 Portal enrollment card
**Portal:** Go to any org → check enrollment code card in header (near Download button)
**Expected:** Code visible, copy button works, refresh generates new code
**Result:** 🔲

---

## 2. AGENT — Data Collection

### 2.1 Compliance scans running
```sql
SELECT TOP 20 m.hostname, o.name AS org, ar.started_at, ar.completed_at, 
       ar.score, ar.grade, ar.pass_count, ar.warn_count, ar.fail_count,
       DATEDIFF(SECOND, ar.started_at, ar.completed_at) AS duration_sec
FROM assessment_runs ar
JOIN machines m ON m.id = ar.machine_id
JOIN organizations o ON o.id = m.organization_id
ORDER BY ar.started_at DESC;
```
**Expected:** Recent runs with scores
**Result:** 🔲

### 2.2 Control results populated
```sql
SELECT TOP 10 cd.code, cd.display, cd.type, cr.status, cr.actual_value
FROM control_results cr
JOIN control_defs cd ON cd.id = cr.control_def_id
JOIN assessment_runs ar ON ar.id = cr.assessment_run_id
WHERE ar.id = (SELECT TOP 1 id FROM assessment_runs ORDER BY started_at DESC)
ORDER BY cr.status, cd.code;
```
**Expected:** Mix of PASS/WARN/FAIL with actual values
**Result:** 🔲

### 2.3 Framework scores calculated
```sql
SELECT TOP 20 m.hostname, f.code AS framework, rfs.score, rfs.pass_count, rfs.fail_count, ar.started_at
FROM run_framework_scores rfs
JOIN assessment_runs ar ON ar.id = rfs.assessment_run_id
JOIN machines m ON m.id = ar.machine_id
JOIN frameworks f ON f.id = rfs.framework_id
WHERE ar.id = (SELECT TOP 1 id FROM assessment_runs ORDER BY started_at DESC)
ORDER BY f.code;
```
**Expected:** Scores per framework (NIST, CIS, HIPAA, ISO, PCI)
**Result:** 🔲

### 2.4 Hardware info populated
```sql
SELECT hostname, os_name, manufacturer, model, cpu_name, cpu_cores, ram_gb,
       tpm_present, tpm_version, secure_boot, bitlocker, product_type, agent_version
FROM machines WHERE is_active = 1 AND deleted_at IS NULL
ORDER BY hostname;
```
**Expected:** Hardware fields populated (not all NULL)
**Result:** 🔲

### 2.5 Multi-disk inventory
```sql
SELECT m.hostname, md.drive_letter, md.size_gb, md.free_gb, md.disk_type
FROM machine_disks md
JOIN machines m ON m.id = md.machine_id
ORDER BY m.hostname, md.drive_letter;
```
**Expected:** Multiple drives per machine
**Result:** 🔲

### 2.6 Software inventory
```sql
SELECT TOP 30 ms.display_name, ms.version, ms.publisher, COUNT(*) AS machine_count
FROM machine_software ms
GROUP BY ms.display_name, ms.version, ms.publisher
ORDER BY machine_count DESC;
```
**Expected:** Recognized software with versions
**Result:** 🔲

### 2.7 Service mode & heartbeat
```sql
SELECT hostname, agent_mode, agent_version, last_heartbeat_at, agent_uptime_seconds,
       DATEDIFF(MINUTE, last_heartbeat_at, GETUTCDATE()) AS mins_since_heartbeat
FROM machines 
WHERE agent_mode = 'service' AND is_active = 1
ORDER BY last_heartbeat_at DESC;
```
**Expected:** Service-mode machines with recent heartbeats (<30 min)
**Result:** 🔲

---

## 3. AD HYGIENE

### 3.1 AD hygiene data exists
```sql
SELECT o.name, ah.category, COUNT(*) AS findings
FROM ad_hygiene ah
JOIN organizations o ON o.id = ah.organization_id
GROUP BY o.name, ah.category
ORDER BY o.name, ah.category;
```
**Expected:** Categories like stale_machines, stale_users, privileged, kerberoastable, etc.
**Result:** 🔲

### 3.2 Portal: Security → Active Directory → Hygiene
**Portal:** Org → Security tab → Active Directory sub-tab → Hygiene section
**Expected:** KPI cards + findings table
**Result:** 🔲

### 3.3 Portal: Security → Active Directory → Security
**Portal:** Same tab → Security section
**Expected:** Privileged accounts, kerberoastable, delegation, LAPS KPIs
**Result:** 🔲

---

## 4. DC HEALTH

### 4.1 DC health snapshots exist
```sql
SELECT m.hostname, dh.schema_version, dh.schema_label, dh.forest_level, dh.domain_level,
       dh.dc_count, dh.gc_count, dh.site_count, dh.repl_partner_count,
       dh.fsmo_schema, dh.fsmo_naming, dh.fsmo_pdc, dh.fsmo_rid, dh.fsmo_infra,
       dh.collected_at
FROM dc_health_snapshots dh
JOIN machines m ON m.id = dh.machine_id
ORDER BY dh.collected_at DESC;
```
**Expected:** At least one snapshot from a DC (if any DC enrolled)
**Result:** 🔲

### 4.2 Replication partners
```sql
SELECT dh.id AS snapshot_id, rp.source_dc, rp.naming_context, rp.last_success_at,
       rp.failure_count, rp.last_error, rp.transport
FROM dc_replication_partners rp
JOIN dc_health_snapshots dh ON dh.id = rp.snapshot_id
ORDER BY dh.collected_at DESC;
```
**Expected:** Replication partner entries (if DC exists)
**Result:** 🔲

### 4.3 Portal: Security → Active Directory → Health
**Portal:** Security tab → AD sub-tab → Health section
**Expected:** Schema version, FSMO roles table, replication partners table
**Result:** 🔲

---

## 5. CVE FINDINGS

### 5.1 CVE entries loaded
```sql
SELECT COUNT(*) AS total_cves, 
       SUM(CASE WHEN severity = 'CRITICAL' THEN 1 ELSE 0 END) AS critical,
       SUM(CASE WHEN severity = 'HIGH' THEN 1 ELSE 0 END) AS high
FROM cve_entries;
```
**Expected:** ~60 built-in CVEs
**Result:** 🔲

### 5.2 Machine CVE findings
```sql
SELECT TOP 20 m.hostname, ce.cve_id, ce.severity, ce.cvss_score, mcf.matched_software, mcf.matched_version
FROM machine_cve_findings mcf
JOIN machines m ON m.id = mcf.machine_id
JOIN cve_entries ce ON ce.id = mcf.cve_entry_id
WHERE mcf.dismissed_at IS NULL
ORDER BY ce.cvss_score DESC;
```
**Expected:** CVEs matched to machines via software inventory
**Result:** 🔲

### 5.3 Portal: Security → CVE tab
**Portal:** Security → CVE sub-tab
**Expected:** Findings list with severity filter, dismiss, rescan, top software grid
**Result:** 🔲

---

## 6. PATCH COMPLIANCE

### 6.1 Patch status data
```sql
SELECT TOP 20 m.hostname, ps.compliance_score, ps.wu_service_status, ps.reboot_pending,
       ps.update_source, ps.last_check_time, ps.last_install_time, ps.hotfix_count
FROM machine_patch_status ps
JOIN machines m ON m.id = ps.machine_id
ORDER BY ps.compliance_score;
```
**Expected:** Compliance scores 0-100, WU status, reboot flags
**Result:** 🔲

### 6.2 Installed patches
```sql
SELECT TOP 20 m.hostname, mp.hotfix_id, mp.description, mp.installed_on
FROM machine_patches mp
JOIN machines m ON m.id = mp.machine_id
ORDER BY mp.installed_on DESC;
```
**Expected:** KB articles with install dates
**Result:** 🔲

### 6.3 Portal: Security → Patches tab
**Portal:** Security → Patches sub-tab
**Expected:** KPI cards (avg score, reboot pending, unmanaged), source distribution, per-machine table
**Result:** 🔲

---

## 7. THREATS

### 7.1 Threat data exists
```sql
SELECT category, COUNT(*) AS findings
FROM threats
GROUP BY category
ORDER BY findings DESC;
```
**Expected:** Threat categories (if threats table exists and has data)
**Result:** 🔲

### 7.2 Portal: Security → Threats tab
**Portal:** Security → Threats sub-tab
**Expected:** Threat findings table or empty state
**Result:** 🔲

---

## 8. NETWORK

### 8.1 Port scan data
```sql
SELECT TOP 20 m.hostname, mp.port, mp.protocol, mp.state, mp.service_name
FROM machine_ports mp
JOIN machines m ON m.id = mp.machine_id
ORDER BY m.hostname, mp.port;
```
**Expected:** Open ports per machine
**Result:** 🔲

### 8.2 Network diagnostics
```sql
SELECT TOP 10 m.hostname, nd.download_mbps, nd.upload_mbps, nd.latency_ms, 
       nd.jitter_ms, nd.packet_loss_pct, nd.vpn_detected, nd.collected_at
FROM machine_network_diag nd
JOIN machines m ON m.id = nd.machine_id
ORDER BY nd.collected_at DESC;
```
**Expected:** Speed/latency/jitter data
**Result:** 🔲

### 8.3 Network sites
```sql
SELECT ns.name, ns.public_ip, ns.machine_count, ns.isp_name, ns.city, ns.region,
       ns.wan_score, ns.contracted_bandwidth_mbps
FROM network_sites ns
ORDER BY ns.machine_count DESC;
```
**Expected:** Auto-derived sites grouped by public IP
**Result:** 🔲

### 8.4 SNMP devices
```sql
SELECT sd.sys_name, sd.sys_descr, sd.ip_address, sd.vendor, sd.model, sd.device_type,
       sd.interface_count, sd.is_stale
FROM snmp_devices sd
ORDER BY sd.sys_name;
```
**Expected:** Discovered network devices with vendor info
**Result:** 🔲

### 8.5 Network topology (LLDP/CDP neighbors)
```sql
SELECT sd.sys_name, sdn.remote_sys_name, sdn.remote_port_desc, sdn.local_port, sdn.protocol
FROM snmp_device_neighbors sdn
JOIN snmp_devices sd ON sd.id = sdn.snmp_device_id
ORDER BY sd.sys_name;
```
**Expected:** Neighbor relationships
**Result:** 🔲

### 8.6 Portal: Network tab (all sub-tabs)
**Portal:** Check each sub-tab:
- [ ] Ports
- [ ] External Scan
- [ ] Diagnostics
- [ ] SNMP
- [ ] Sites (map + speed history)
- [ ] Protocol Usage
- [ ] Topology (D3 graph)
- [ ] WAN Health
**Result:** 🔲

---

## 9. CLOUD ASSESSMENT

### 9.1 M365 tenants connected
```sql
SELECT o.name, mt.tenant_id, mt.tenant_display_name, mt.consent_granted_at, mt.last_scan_at
FROM m365_tenants mt
JOIN organizations o ON o.id = mt.organization_id
ORDER BY mt.last_scan_at DESC;
```
**Expected:** Connected M365 tenants with scan dates
**Result:** 🔲

### 9.2 Cloud assessment scans
```sql
SELECT TOP 5 cas.id, o.name, cas.status, cas.overall_score, 
       cas.identity_score, cas.endpoint_score, cas.data_score, cas.productivity_score,
       cas.azure_score, cas.finding_count, cas.started_at
FROM cloud_assessment_scans cas
JOIN organizations o ON o.id = cas.organization_id
ORDER BY cas.started_at DESC;
```
**Expected:** Completed scans with area scores
**Result:** 🔲

### 9.3 Portal: Cloud Assessment tab
**Portal:** Cloud tab
- [ ] Connection status banner
- [ ] Overall score + area radar
- [ ] Findings list
- [ ] Copilot Lens sub-tab
- [ ] Azure sub-tab (if connected)
**Result:** 🔲

---

## 10. REPORTS

### 10.1 API report generation
**API:** Test each report type for an org with data:
```
GET /v2/reports/org/{orgId}?type=c-level&lang=en
GET /v2/reports/org/{orgId}?type=technical&lang=en
GET /v2/reports/org/{orgId}?type=preventas&tone=opener&lang=en
GET /v2/reports/org/{orgId}?type=preventas&tone=detailed&lang=en
GET /v2/reports/org/{orgId}?type=framework&framework=NIST&lang=en
GET /v2/reports/org/{orgId}?type=proposal&lang=en
GET /v2/reports/org/{orgId}?type=monthly&lang=en
```
**Expected:** HTML report returned for each type
- [ ] C-Level
- [ ] Technical
- [ ] Preventa Opener
- [ ] Preventa Detailed
- [ ] Framework (NIST)
- [ ] Proposal
- [ ] Monthly
**Result:** 🔲

### 10.2 Report diagnostics
```
GET /v2/reports/diagnose/{orgId}?type=all
```
**Expected:** Per-block timing + errors
**Result:** 🔲

### 10.3 Portal: Reports tab
**Portal:** Reports tab → download each type
**Expected:** Reports render and download
**Result:** 🔲

---

## 11. DEVICES TAB

### 11.1 Portal: Devices → Fleet
**Portal:** Devices tab → Fleet sub-tab
**Expected:** Machine list with hostname, OS, score, grade, last scan, agent version
**Result:** 🔲

### 11.2 Portal: Devices → Hardware
**Portal:** Devices → Hardware sub-tab
**Expected:** Hardware inventory table (CPU, RAM, disk, TPM, BitLocker)
**Result:** 🔲

### 11.3 Portal: Devices → Software
**Portal:** Devices → Software sub-tab
**Expected:** Software inventory with app names, versions, machine count
**Result:** 🔲

### 11.4 Portal: Machine Detail
**Portal:** Click a machine → detail page
**Expected:** Hardware info, assessment history, run detail link, software tab, agent config card
**Result:** 🔲

### 11.5 Portal: Machine → Agent Config
**Portal:** Machine detail → Agent Config card
**Expected:** Compliance interval, SNMP interval, network scan toggle, passive discovery toggle
**Result:** 🔲

---

## 12. INFRASTRUCTURE ASSESSMENT

### 12.1 Hypervisor configs
```sql
SELECT hc.name, hc.type, hc.host, hc.status, o.name AS org
FROM infra_hypervisor_configs hc
JOIN organizations o ON o.id = hc.organization_id;
```
**Expected:** VMware/Proxmox configs (if configured)
**Result:** 🔲

### 12.2 Portal: Infrastructure tab
**Portal:** Infrastructure tab
**Expected:** Config manager or empty state
**Result:** 🔲

---

## 13. REMEDIATION

### 13.1 Remediation catalog
```sql
SELECT code, display, category, risk_level, is_active
FROM remediation_actions
WHERE is_active = 1
ORDER BY category, code;
```
**Expected:** ~50 whitelisted remediation actions
**Result:** 🔲

### 13.2 Remediation tasks
```sql
SELECT TOP 10 m.hostname, ra.code, rt.status, rt.requested_at, rt.completed_at, rt.rollback_at
FROM remediation_tasks rt
JOIN machines m ON m.id = rt.machine_id
JOIN remediation_actions ra ON ra.id = rt.action_id
ORDER BY rt.requested_at DESC;
```
**Expected:** Task history (if any executed)
**Result:** 🔲

---

## 14. EXTERNAL SCAN

### 14.1 External scan data
```sql
SELECT o.name, es.status, es.public_ip, es.open_port_count, es.finding_count, es.started_at
FROM external_scans es
JOIN organizations o ON o.id = es.organization_id
ORDER BY es.started_at DESC;
```
**Expected:** Completed external scans (if consent given)
**Result:** 🔲

---

## 15. ALERTS

### 15.1 Alert rules configured
```sql
SELECT ar.rule_type, ar.threshold, ar.is_active, ar.delivery_method, f.name AS franchise
FROM cloud_assessment_alert_rules ar
JOIN franchises f ON f.id = ar.franchise_id;
```
**Expected:** Alert rules (if configured)
**Result:** 🔲

---

## 16. USERS & ADMIN

### 16.1 Users exist
```sql
SELECT u.display_name, u.email, r.name AS role, u.last_login_at
FROM users u
LEFT JOIN roles r ON r.id = u.role_id
WHERE u.deleted_at IS NULL
ORDER BY u.last_login_at DESC;
```
**Expected:** Users with roles
**Result:** 🔲

### 16.2 Portal: Users page
**Portal:** Users page
**Expected:** User list with role, last login
**Result:** 🔲

### 16.3 Portal: Activity Log
**Portal:** Activity Log page
**Expected:** Request log entries
**Result:** 🔲

### 16.4 Portal: Recycle Bin
**Portal:** Recycle Bin page
**Expected:** Deleted entities (or empty state)
**Result:** 🔲

---

## 17. AGENT BINARY & DOWNLOAD

### 17.1 Agent download works
**Portal:** Org header → Download Agent button
**Expected:** ZIP file downloads with patched binary
**Result:** 🔲

### 17.2 API version endpoint
```
GET https://func-kryoss.azurewebsites.net/v2/version
```
**Expected:** JSON with version, build time
**Result:** 🔲

---

## 18. PLATFORM COVERAGE

### 18.1 Platform distribution
```sql
SELECT p.code, p.display_name, COUNT(m.id) AS machines
FROM machines m
LEFT JOIN platforms p ON p.id = m.platform_id
WHERE m.is_active = 1 AND m.deleted_at IS NULL
GROUP BY p.code, p.display_name
ORDER BY machines DESC;
```
**Expected:** W10/W11/MS19/DC19 etc. with machine counts
**Result:** 🔲

### 18.2 Control-platform links
```sql
SELECT p.code, COUNT(cp.control_def_id) AS controls
FROM control_platforms cp
JOIN platforms p ON p.id = cp.platform_id
JOIN control_defs cd ON cd.id = cp.control_def_id
WHERE cd.is_active = 1
GROUP BY p.code
ORDER BY p.code;
```
**Expected:** 647+ controls per workstation, 727+ per server, 827+ per DC
**Result:** 🔲

---

## Summary

| Module | Tests | Pass | Fail | Partial | Notes |
|--------|-------|------|------|---------|-------|
| Enrollment & Auth | 5 | | | | |
| Agent Data Collection | 7 | | | | |
| AD Hygiene | 3 | | | | |
| DC Health | 3 | | | | |
| CVE Findings | 3 | | | | |
| Patch Compliance | 3 | | | | |
| Threats | 2 | | | | |
| Network | 6 | | | | |
| Cloud Assessment | 3 | | | | |
| Reports | 3 | | | | |
| Devices Tab | 5 | | | | |
| Infrastructure | 2 | | | | |
| Remediation | 2 | | | | |
| External Scan | 1 | | | | |
| Alerts | 1 | | | | |
| Users & Admin | 4 | | | | |
| Agent Binary | 2 | | | | |
| Platform Coverage | 2 | | | | |
| **TOTAL** | **55** | | | | |
