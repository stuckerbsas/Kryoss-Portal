# Kryoss Role-Based Infrastructure Health — Feature Roadmap

**Document ID:** FEAT-RBIH-2026  
**Author:** Freddy Hansen / Architecture Team  
**Date:** 2026-04-27  
**Status:** APPROVED — Scheduled for future development  
**Version:** 1.0  

---

## 1. Executive Summary

### Vision

> **"If Kryoss detects any Microsoft server role or feature, it automatically applies ALL best practices AND health checks — security, performance, operational health, and capacity — without any manual configuration."**

Today, Kryoss evaluates 918+ security controls per machine (CIS-aligned, registry-based). This roadmap extends that engine to become a **full-stack Microsoft Infrastructure Health Platform**, covering every detectable server role across four dimensions.

### Why This Matters

- MSPs lack the specialized expertise to audit SQL Server, IIS, AD CS, Hyper-V, etc.
- Existing RMM tools only monitor uptime — they don't evaluate **best practices** or **operational health**
- No competing MSP platform auto-detects roles and applies contextualized checks
- This positions Kryoss as **the remote senior sysadmin / DBA** that MSPs can't afford to hire

### Business Impact

| Metric | Before | After |
|--------|--------|-------|
| SQL Server visibility | None (unless MSP manually checks) | Automatic: security + performance + blocked sessions + capacity |
| IIS health | None | Automatic: TLS, app pools, crashes, cert expiry |
| Time to detect SQL blocked session | Hours (client calls screaming) | Minutes (proactive alert) |
| Time to detect DHCP scope exhaustion | After outage | Before outage (80% threshold) |
| Competitive differentiation | "We monitor Windows" | "We audit your entire Microsoft infrastructure" |

---

## 2. Design Principles

### 2.1 Four Pillars Per Role

Every detected role is evaluated across exactly **four dimensions**:

| Pillar | Icon | What It Measures | Example |
|--------|------|-----------------|---------| 
| **Security** | 🔐 | CIS benchmarks, hardening, best practices | sa account disabled, TLS 1.2 enforced |
| **Performance** | ⚡ | CPU, memory, disk, queries, latency | Top CPU queries, buffer cache hit ratio |
| **Health** | 🏥 | Operational state, errors, sessions, jobs | Blocked sessions, failed backups, service status |
| **Capacity** | 📊 | Growth trends, free space, projections | TempDB growth, disk free %, log usage |

### 2.2 No PowerShell / No CMD (Mandatory)

All checks MUST use native methods only:

| Allowed | Forbidden |
|---------|-----------|
| Win32 APIs | powershell.exe |
| WMI / CIM | cmd.exe |
| .NET BCL (SMO, DirectoryServices, Web.Administration) | .ps1 scripts |
| Registry reads | sc.exe, netsh, dnscmd, sqlcmd, appcmd |
| XML config parsing | Any shell-out wrapper |
| Performance counters via WMI | Invoke-Expression or equivalent |
| COM interfaces | Start-Process with shell commands |

**Exception policy:** If no native alternative exists, a documented exception with risk assessment and explicit approval is required before implementation.

### 2.3 Extend, Don't Rebuild

- Role-based checks integrate into the **existing control evaluation framework**
- No separate scoring engine — reuse existing PASS/FAIL/WARN model
- No separate scan loop — role evaluation runs inside existing ScanLoop
- Portal extends existing Machine Detail — no new module

### 2.4 Detection-Driven, Zero Configuration

- Agent **auto-detects** installed roles via services + registry
- Controls applicable to detected roles are **automatically activated**
- MSP operator configures **nothing** — it just works
- Kill switch available per role: `KRYOSS_DISABLE_ROLE_{ROLENAME}`

---

## 3. Architecture Overview

```
Agent (SYSTEM)
  ScanLoop
  |-- Phase 1: Hardware/OS scan (existing)
  |-- Phase 2: Compliance controls (existing, 918+ checks)
  |-- Phase 3: Role Detection  <-- NEW
  |     |-- ServiceController: MSSQLSERVER, W3SVC, DNS, etc.
  |     |-- Registry: Terminal Server, CertSvc, Hyper-V keys
  |     +-- Output: detected_roles[] = ["sql_server", "iis"]
  |
  +-- Phase 4: Role-Specific Evaluation  <-- NEW
        |-- For each detected role:
        |     |-- Security checks (CIS/best practice)
        |     |-- Performance checks (DMVs, counters, WMI)
        |     |-- Health checks (state, errors, sessions)
        |     +-- Capacity checks (sizes, growth, free space)
        +-- Results: role_check_results[]

API (Backend)
  |-- Receives detected_roles + role_check_results
  |-- Stores in role_check_results table
  |-- Calculates per-pillar scores per role per machine
  |-- Aggregates org-level role health
  +-- Feeds security_ai_features table (future ML)

Portal (Frontend)
  Machine Detail:
  |-- "Server Roles" section (auto-shown if roles detected)
  |-- Per-role card: 4 pillar scores + top issues
  +-- Drill-down: full check list per pillar

  Org Overview:
  |-- "Infrastructure Health" dashboard
  |-- "X machines running SQL -- avg score: 72%"
  +-- Top issues across fleet per role
```

---

## 4. Role Catalog — Complete Reference

### 4.1 SQL Server

**Detection:** Service `MSSQLSERVER` or `MSSQL$*` (named instances)  
**Native API:** SQL Server Management Objects (SMO) via .NET, named pipes (local SYSTEM)

#### 🔐 Security

| # | Check | Method | CIS Ref |
|---|-------|--------|---------|
| 1 | sa account disabled | SMO ServerLogin | 2.1 |
| 2 | xp_cmdshell disabled | SMO sp_configure | 2.1 |
| 3 | CLR Enabled = 0 | SMO sp_configure | 2.2 |
| 4 | Cross DB Ownership Chaining off | SMO sp_configure | 2.3 |
| 5 | Ad Hoc Distributed Queries off | SMO sp_configure | 2.1 |
| 6 | Remote Admin Connections off | SMO sp_configure | 2.6 |
| 7 | Mixed Mode auth detection | Registry LoginMode | 3.1 |
| 8 | ForceEncryption enabled | Registry | 3.1 |
| 9 | Audit enabled | SMO Server Audit | 3.3 |
| 10 | Guest account disabled per DB | SMO database principals | 4.1 |
| 11 | TDE enabled (sensitive DBs) | DMV dm_database_encryption_keys | 3.1 |
| 12 | Trustworthy flag off per DB | SMO DatabaseOptions | 2.9 |
| 13 | SQL Browser service disabled | ServiceController | 2.7 |
| 14 | Latest CU installed | Registry version compare | 1.1 |
| 15 | Backup encryption status | msdb backup history via SMO | 3.1 |

#### ⚡ Performance

| # | Check | Method |
|---|-------|--------|
| 1 | Top 10 CPU-consuming queries | DMV dm_exec_query_stats |
| 2 | Page Life Expectancy (PLE) | DMV dm_os_performance_counters |
| 3 | Buffer Cache Hit Ratio | DMV dm_os_performance_counters |
| 4 | Top wait stats | DMV dm_os_wait_stats |
| 5 | TempDB contention (PFS/SGAM) | DMV dm_db_file_space_usage |
| 6 | Missing indexes | DMV dm_db_missing_index_details |
| 7 | Unused indexes | DMV dm_db_index_usage_stats |
| 8 | Long-running active queries (>30s) | DMV dm_exec_requests |
| 9 | Disk I/O latency per file | DMV dm_io_virtual_file_stats |
| 10 | Memory grants pending | DMV dm_exec_query_memory_grants |

#### 🏥 Health

| # | Check | Method |
|---|-------|--------|
| 1 | Blocked sessions (blocking_session_id > 0) | DMV dm_exec_requests |
| 2 | Deadlocks (recent) | Extended Events system_health via SMO |
| 3 | Failed SQL Agent jobs (last 24h) | msdb.dbo.sysjobhistory via SMO |
| 4 | Last successful backup per DB | msdb.dbo.backupset via SMO |
| 5 | Last DBCC CHECKDB per DB | msdb.dbo.suspect_pages + DBINFO via SMO |
| 6 | Always On replica health | DMV dm_hadr_availability_replica_states |
| 7 | Replication lag | distribution DB via SMO |
| 8 | Error log critical entries | sys.xp_readerrorlog via SMO |
| 9 | Orphaned users | sys.database_principals vs server_principals via SMO |
| 10 | Service status (SQL Server, Agent, Browser) | ServiceController |

#### 📊 Capacity

| # | Check | Method |
|---|-------|--------|
| 1 | Database file sizes + autogrowth config | sys.master_files via SMO |
| 2 | TempDB size and data file count | sys.master_files (db_id=2) via SMO |
| 3 | Log file usage % | DBCC SQLPERF(LOGSPACE) via SMO |
| 4 | Disk free space per data volume | WMI Win32_LogicalDisk |
| 5 | Autogrow events (last 7 days) | Default trace / XE via SMO |
| 6 | Row count trends per table (top 10 largest) | sys.dm_db_partition_stats via SMO |

### 4.2 IIS (Web Server)

**Detection:** Service `W3SVC`  
**Native API:** Microsoft.Web.Administration .NET API, XML parsing (applicationHost.config, web.config)

#### 🔐 Security

| # | Check | Method |
|---|-------|--------|
| 1 | TLS 1.2+ only (SChannel) | Registry SChannel keys |
| 2 | Directory browsing disabled | applicationHost.config XML |
| 3 | Custom error pages configured | web.config XML |
| 4 | Request filtering enabled | applicationHost.config XML |
| 5 | App pool identity not LocalSystem | Microsoft.Web.Administration |
| 6 | HTTPS redirect configured | URL Rewrite rules in web.config |
| 7 | Cookie security flags (httpOnly + secure) | web.config XML |
| 8 | Unlisted file extensions blocked | Request filtering config |
| 9 | X-Powered-By header removed | web.config XML |
| 10 | Server header removed | web.config / registry |
| 11 | HSTS enabled | web.config XML |
| 12 | MIME type restrictions | applicationHost.config XML |
| 13 | maxAllowedContentLength configured | web.config XML |
| 14 | Access logging with all required fields | IIS config XML |

#### ⚡ Performance

| # | Check | Method |
|---|-------|--------|
| 1 | App pool recycling configuration | Microsoft.Web.Administration |
| 2 | Worker process (w3wp.exe) CPU usage | WMI Win32_Process |
| 3 | Worker process memory usage | WMI Win32_Process |
| 4 | Request queue length | Performance counters via WMI |
| 5 | Current connections per site | Performance counters via WMI |
| 6 | Response time baseline (localhost self-test) | HTTP self-test (optional) |
| 7 | Idle timeout configuration | Microsoft.Web.Administration |

#### 🏥 Health

| # | Check | Method |
|---|-------|--------|
| 1 | App pool status (Started/Stopped) | Microsoft.Web.Administration |
| 2 | Recent app pool crashes (rapid-fail count) | Event Log + IIS config |
| 3 | HTTP 500 error rate (from IIS logs) | IIS log file sampling |
| 4 | Hung worker processes (age > recycling config) | WMI process creation time |
| 5 | TLS certificate expiry per binding | Certificate store + IIS bindings |
| 6 | Sites enabled/disabled status | Microsoft.Web.Administration |
| 7 | Application initialization module status | IIS config |

#### 📊 Capacity

| # | Check | Method |
|---|-------|--------|
| 1 | IIS log folder size | File system (DirectoryInfo) |
| 2 | Connection limits vs current usage | IIS config vs perf counters |
| 3 | Disk free space for log volumes | WMI Win32_LogicalDisk |
| 4 | Number of sites and app pools | Microsoft.Web.Administration |

### 4.3 DNS Server

**Detection:** Service `DNS`  
**Native API:** Registry + WMI MicrosoftDNS provider

#### 🔐 Security

| # | Check | Method |
|---|-------|--------|
| 1 | Zone transfer restrictions (to named servers only) | WMI MicrosoftDNS / registry |
| 2 | Secure dynamic updates enabled | WMI zone properties |
| 3 | Recursion restricted (not open resolver) | Registry DNS parameters |
| 4 | Socket pool size >= 2500 | Registry |
| 5 | DNSSEC signing status | WMI MicrosoftDNS |
| 6 | Cache locking percentage >= 100 | Registry |

#### ⚡ Performance

| # | Check | Method |
|---|-------|--------|
| 1 | Query response time (avg) | Performance counters via WMI |
| 2 | Recursive query rate | Performance counters |
| 3 | Cache hit ratio | Performance counters |
| 4 | TCP vs UDP query ratio | Performance counters |

#### 🏥 Health

| # | Check | Method |
|---|-------|--------|
| 1 | Service status | ServiceController |
| 2 | Zone load errors | Event Log |
| 3 | Scavenging status and last run | WMI MicrosoftDNS |
| 4 | Forwarder health (reachable) | WMI / registry |
| 5 | Conditional forwarder status | WMI MicrosoftDNS |

#### 📊 Capacity

| # | Check | Method |
|---|-------|--------|
| 1 | Zone sizes (record counts) | WMI MicrosoftDNS |
| 2 | Cache size | Performance counters |
| 3 | DNS database file sizes | File system |

### 4.4 DHCP Server

**Detection:** Service `DHCPServer`  
**Native API:** WMI DHCP provider + Registry

#### 🔐 Security

| # | Check | Method |
|---|-------|--------|
| 1 | DHCP server authorized in AD | WMI / registry |
| 2 | Audit logging enabled | Registry DHCP parameters |
| 3 | DNS credential configuration | Registry |
| 4 | Name protection enabled | WMI DHCP scope options |

#### ⚡ Performance

| # | Check | Method |
|---|-------|--------|
| 1 | Lease processing time | Performance counters |
| 2 | NACK rate | Performance counters |
| 3 | Discover/Offer/Request ratio | Performance counters |

#### 🏥 Health

| # | Check | Method |
|---|-------|--------|
| 1 | Service status | ServiceController |
| 2 | Scope utilization (warn >80%, critical >90%) | WMI DHCP scopes |
| 3 | Failover relationship status | WMI DHCP |
| 4 | Lease conflicts detected | WMI DHCP |
| 5 | Last backup status | Registry / file system |

#### 📊 Capacity

| # | Check | Method |
|---|-------|--------|
| 1 | Available IPs per scope (remaining) | WMI DHCP scopes |
| 2 | Scope usage trend (% used over time) | Historical comparison |
| 3 | Lease duration analysis | WMI DHCP scope options |

### 4.5 Hyper-V

**Detection:** Service `vmms`  
**Native API:** WMI Hyper-V namespace (root/virtualization/v2)

#### 🔐 Security

| # | Check | Method |
|---|-------|--------|
| 1 | Secure Boot enabled per VM | WMI Msvm_SecuritySettingData |
| 2 | Integration services current | WMI Msvm_GuestServiceInterfaceComponent |
| 3 | Enhanced Session Mode config | Registry |
| 4 | VM network isolation (VLANs) | WMI Msvm_VirtualEthernetSwitch |
| 5 | Host guardian / shielded VMs | Registry + WMI |

#### ⚡ Performance

| # | Check | Method |
|---|-------|--------|
| 1 | Host CPU overcommit ratio | WMI + logical processor count |
| 2 | Host memory overcommit ratio | WMI Msvm_MemorySettingData |
| 3 | VM CPU ready time / wait time | Hyper-V performance counters |
| 4 | VHD I/O latency per VM | WMI Msvm_StorageAllocationSettingData + counters |
| 5 | Virtual switch throughput | Hyper-V Network Adapter counters |

#### 🏥 Health

| # | Check | Method |
|---|-------|--------|
| 1 | VM states (Running/Off/Saved/Paused/Critical) | WMI Msvm_ComputerSystem |
| 2 | Replication health per VM | WMI Msvm_ReplicationService |
| 3 | Checkpoint age (warn if > 48h) | WMI Msvm_VirtualSystemSnapshotService |
| 4 | Integration services version mismatch | WMI comparison |
| 5 | VMMS service status | ServiceController |

#### 📊 Capacity

| # | Check | Method |
|---|-------|--------|
| 1 | VHD/VHDX sizes + type (fixed/dynamic) | WMI Msvm_StorageAllocationSettingData |
| 2 | Host storage free per volume | WMI Win32_LogicalDisk |
| 3 | Memory demand vs physically available | WMI Msvm_MemorySettingData + Win32_OS |
| 4 | VM count vs recommended limits | WMI count + MS published limits |

### 4.6 RDS (Remote Desktop Services)

**Detection:** Registry `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\RCM` + Service `TermService`  
**Native API:** WMI Win32_TerminalService + Registry

#### 🔐 Security

| # | Check | Method |
|---|-------|--------|
| 1 | Network Level Authentication (NLA) enforced | Registry |
| 2 | TLS security layer (not RDP Security Layer) | Registry |
| 3 | Idle session timeout configured | Registry / GPO values |
| 4 | Drive redirection disabled | Registry |
| 5 | Clipboard redirection policy | Registry |
| 6 | RDP port (default 3389 — flag if exposed) | Registry + port check |

#### ⚡ Performance

| # | Check | Method |
|---|-------|--------|
| 1 | Active session CPU/memory per user | WMI Win32_PerfFormattedData_TermService |
| 2 | Session count vs server capacity | WMI TerminalService |
| 3 | Graphics rendering mode | Registry |

#### 🏥 Health

| # | Check | Method |
|---|-------|--------|
| 1 | Disconnected sessions count + age | WMI Win32_LogonSession |
| 2 | Idle sessions > threshold | WMI Win32_LogonSession + config |
| 3 | Licensing status (grace period?) | WMI Win32_TSLicenseServer |
| 4 | Session broker health (if farm) | WMI / registry |
| 5 | TermService status | ServiceController |

#### 📊 Capacity

| # | Check | Method |
|---|-------|--------|
| 1 | Concurrent sessions trend | Historical tracking |
| 2 | License capacity remaining | WMI licensing provider |
| 3 | User profile disk usage | File system (if UPD configured) |

### 4.7 AD CS (Active Directory Certificate Services)

**Detection:** Service `CertSvc`  
**Native API:** Registry + CertificateAuthority WMI/DCOM + Certificate Store

#### 🔐 Security (ESC Attack Surface)

| # | Check | Method |
|---|-------|--------|
| 1 | EDITF_ATTRIBUTESUBJECTALTNAME2 flag | Registry CA policy |
| 2 | Template enrollment permissions (ESC1) | DCOM ICertAdmin2 / registry |
| 3 | Web enrollment over HTTP (not HTTPS) | IIS binding check |
| 4 | Manager approval required on sensitive templates | Registry template config |
| 5 | Certificate template allows client auth + enrollment | Registry EnrollmentFlags |

#### ⚡ Performance

| # | Check | Method |
|---|-------|--------|
| 1 | Certificate issuance rate | Performance counters |
| 2 | CRL generation time | Performance counters |
| 3 | CA request queue depth | WMI / DCOM |

#### 🏥 Health

| # | Check | Method |
|---|-------|--------|
| 1 | CA service status | ServiceController |
| 2 | CRL validity (not expired, not expiring soon) | Certificate store |
| 3 | Failed certificate requests (last 24h) | CA database via DCOM |
| 4 | Pending requests count | CA database via DCOM |
| 5 | CA certificate chain validity | Certificate store |

#### 📊 Capacity

| # | Check | Method |
|---|-------|--------|
| 1 | Certificates expiring in 30/60/90 days | Certificate store scan |
| 2 | Template usage (most issued types) | CA database via DCOM |
| 3 | CA database size | File system |
| 4 | Issued certificate count trend | Historical tracking |

### 4.8 File Server

**Detection:** Non-default SMB shares (excluding ADMIN$, C$, IPC$, SYSVOL, NETLOGON)  
**Native API:** WMI Win32_Share + Registry + DFSR WMI

#### 🔐 Security

| # | Check | Method |
|---|-------|--------|
| 1 | SMB signing required | Registry LanmanServer parameters |
| 2 | SMBv1 disabled | Registry + feature state |
| 3 | Share permissions audit (Everyone:FullControl = bad) | WMI Win32_LogicalShareSecuritySetting |
| 4 | NTFS vs Share permissions alignment | WMI + DACL comparison |
| 5 | ABE (Access-Based Enumeration) enabled | Registry per share |

#### ⚡ Performance

| # | Check | Method |
|---|-------|--------|
| 1 | Disk I/O (IOPS, queue length) per volume | Performance counters via WMI |
| 2 | SMB session count | WMI Win32_ServerSession |
| 3 | Network throughput | Performance counters |

#### 🏥 Health

| # | Check | Method |
|---|-------|--------|
| 1 | Open file locks (potentially blocking) | WMI Win32_ServerSession |
| 2 | Stale shares (pointing to non-existent paths) | WMI Win32_Share + path check |
| 3 | Shadow Copies / VSS status | WMI Win32_ShadowCopy |
| 4 | DFS Replication health (if DFSR) | WMI DFSR provider |
| 5 | Server service status | ServiceController |

#### 📊 Capacity

| # | Check | Method |
|---|-------|--------|
| 1 | Share sizes (top 10 largest) | File system (DirectoryInfo) |
| 2 | Disk free per volume | WMI Win32_LogicalDisk |
| 3 | Quota usage (if FSRM configured) | WMI FSRM provider |
| 4 | Growth rate (weekly delta) | Historical comparison |

### 4.9 Print Server

**Detection:** Service `Spooler` + at least one shared printer  
**Native API:** WMI Win32_Printer + Win32_PrintJob + Registry

#### 🔐 Security

| # | Check | Method |
|---|-------|--------|
| 1 | PrintNightmare patches applied | Registry point-and-print restrictions |
| 2 | Point-and-print restrictions enforced | Registry |
| 3 | Driver isolation enabled | Registry |
| 4 | Spooler service not running on DCs (best practice) | ServiceController + DC detection |
| 5 | Remote print disabled if not needed | Registry |

#### ⚡ Performance

| # | Check | Method |
|---|-------|--------|
| 1 | Print queue depth per printer | WMI Win32_PrintJob count |
| 2 | Print job processing time (avg) | WMI Win32_PrintJob timing |

#### 🏥 Health

| # | Check | Method |
|---|-------|--------|
| 1 | Printer status (Error/Offline/PaperJam/etc.) | WMI Win32_Printer.PrinterStatus |
| 2 | Spooler service status | ServiceController |
| 3 | Failed print jobs (last 24h) | Event Log |
| 4 | Stale printers (offline > 7 days) | WMI + last job timestamp |

#### 📊 Capacity

| # | Check | Method |
|---|-------|--------|
| 1 | Print volume trends (jobs/day) | WMI + Event Log |
| 2 | Top printers by volume | WMI Win32_PrintJob aggregation |
| 3 | Spool folder size | File system |

### 4.10 WSUS (Windows Server Update Services)

**Detection:** Service `WsusService` or `W3SVC` + WSUS IIS app  
**Native API:** Registry + WMI + SUSDB (if local SQL Express)

#### 🔐 Security

| # | Check | Method |
|---|-------|--------|
| 1 | SSL configured for WSUS | IIS binding check |
| 2 | WSUS database permissions | SMO (if SQL-backed) |
| 3 | Approved update policies | Registry WSUS config |

#### ⚡ Performance

| # | Check | Method |
|---|-------|--------|
| 1 | Synchronization duration | Registry + Event Log |
| 2 | WSUS DB size (WID or SQL) | File system or SMO |
| 3 | Cleanup last run date | Registry / Event Log |
| 4 | IIS application pool health (WSUS pool) | Microsoft.Web.Administration |

#### 🏥 Health

| # | Check | Method |
|---|-------|--------|
| 1 | Last sync status (success/fail) | Event Log |
| 2 | Last sync timestamp | Registry |
| 3 | Client reporting status | WSUS WMI provider |
| 4 | WsusService status | ServiceController |
| 5 | WSUS IIS app pool status | Microsoft.Web.Administration |

#### 📊 Capacity

| # | Check | Method |
|---|-------|--------|
| 1 | WSUS content folder size | File system |
| 2 | DB growth trend | File system or SMO |
| 3 | Approved vs needed vs installed counts | SUSDB queries or WMI |

---

## 5. Phased Execution Plan

### Phase 1 — SQL Server + IIS (Highest MSP Demand)
**Priority:** HIGH  
**Estimated effort:** 8-12 days  
**Reason first:** Most common server roles. Highest impact on MSP visibility. Highest client pain.

| Task ID | Description | Layer | Risk |
|---------|-------------|-------|------|
| ROLE-01 | Role detection engine (all roles) | Agent | Low |
| ROLE-02 | SQL Server security checks (15 checks) | Agent | Medium |
| ROLE-03 | SQL Server performance checks (10 checks) | Agent | Medium |
| ROLE-04 | SQL Server health checks (10 checks) | Agent | Medium |
| ROLE-05 | SQL Server capacity checks (6 checks) | Agent | Low |
| ROLE-06 | IIS security checks (14 checks) | Agent | Low |
| ROLE-07 | IIS performance checks (7 checks) | Agent | Low |
| ROLE-08 | IIS health checks (7 checks) | Agent | Low |
| ROLE-09 | IIS capacity checks (4 checks) | Agent | Low |
| ROLE-10 | Role check results API + DB schema | API/DB | Low |
| ROLE-11 | Per-pillar scoring engine | API | Low |
| ROLE-12 | Machine Detail — Server Roles section | Portal | Low |
| ROLE-13 | Org Overview — Infrastructure Health dashboard | Portal | Low |

### Phase 2 — DNS + DHCP + RDS (Common Infrastructure)
**Priority:** MEDIUM  
**Estimated effort:** 5-8 days  
**Reason second:** Present in almost every domain environment. Quick to implement (registry + WMI only).

| Task ID | Description | Layer | Risk |
|---------|-------------|-------|------|
| ROLE-14 | DNS Server — 4 pillars (18 checks) | Agent | Low |
| ROLE-15 | DHCP Server — 4 pillars (15 checks) | Agent | Low |
| ROLE-16 | RDS — 4 pillars (17 checks) | Agent | Low |

### Phase 3 — Hyper-V + AD CS + File Server (Heavy Infrastructure)
**Priority:** MEDIUM  
**Estimated effort:** 6-10 days  
**Reason third:** More complex APIs (Hyper-V WMI, DCOM for AD CS). AD CS is critical (ESC attacks).

| Task ID | Description | Layer | Risk |
|---------|-------------|-------|------|
| ROLE-17 | Hyper-V — 4 pillars (20 checks) | Agent | Medium |
| ROLE-18 | AD CS — 4 pillars (16 checks) + ESC detection | Agent | Medium |
| ROLE-19 | File Server — 4 pillars (16 checks) | Agent | Low |

### Phase 4 — Print Server + WSUS (Nice to Have)
**Priority:** LOW  
**Estimated effort:** 3-5 days  
**Reason last:** Lower MSP demand. Smaller check surface.

| Task ID | Description | Layer | Risk |
|---------|-------------|-------|------|
| ROLE-20 | Print Server — 4 pillars (12 checks) | Agent | Low |
| ROLE-21 | WSUS — 4 pillars (14 checks) | Agent | Low |

---

## 6. Data Model

### 6.1 New Tables

```sql
-- Role detection per machine
CREATE TABLE machine_detected_roles (
    id                  INT IDENTITY PRIMARY KEY,
    machine_id          UNIQUEIDENTIFIER NOT NULL,
    role_name           NVARCHAR(32) NOT NULL,
    role_version        NVARCHAR(64) NULL,
    instance_name       NVARCHAR(128) NULL,
    detected_at         DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    last_seen_at        DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UNIQUE(machine_id, role_name, instance_name)
);

-- Role-specific control definitions
CREATE TABLE role_control_defs (
    id                  INT IDENTITY PRIMARY KEY,
    role_name           NVARCHAR(32) NOT NULL,
    pillar              NVARCHAR(16) NOT NULL,
    control_id          NVARCHAR(64) NOT NULL,
    title               NVARCHAR(256) NOT NULL,
    description         NVARCHAR(1024) NULL,
    severity            NVARCHAR(16) NOT NULL,
    check_type          NVARCHAR(32) NOT NULL,
    check_params        NVARCHAR(MAX) NULL,
    rationale           NVARCHAR(1024) NULL,
    remediation         NVARCHAR(1024) NULL,
    cis_reference       NVARCHAR(64) NULL,
    enabled             BIT NOT NULL DEFAULT 1,
    UNIQUE(role_name, control_id)
);

-- Role check results per machine per scan
CREATE TABLE role_check_results (
    id                  BIGINT IDENTITY PRIMARY KEY,
    machine_id          UNIQUEIDENTIFIER NOT NULL,
    organization_id     UNIQUEIDENTIFIER NOT NULL,
    role_name           NVARCHAR(32) NOT NULL,
    instance_name       NVARCHAR(128) NULL,
    pillar              NVARCHAR(16) NOT NULL,
    control_id          NVARCHAR(64) NOT NULL,
    result              NVARCHAR(8) NOT NULL,
    evidence            NVARCHAR(1024) NULL,
    scanned_at          DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    INDEX IX_role_results_machine (machine_id, role_name, pillar),
    INDEX IX_role_results_org (organization_id, role_name)
);

-- Aggregated scores per machine per role per pillar
CREATE TABLE role_scores (
    id                  INT IDENTITY PRIMARY KEY,
    machine_id          UNIQUEIDENTIFIER NOT NULL,
    organization_id     UNIQUEIDENTIFIER NOT NULL,
    role_name           NVARCHAR(32) NOT NULL,
    instance_name       NVARCHAR(128) NULL,
    pillar              NVARCHAR(16) NOT NULL,
    score               FLOAT NOT NULL,
    total_checks        INT NOT NULL,
    passed_checks       INT NOT NULL,
    failed_checks       INT NOT NULL,
    warn_checks         INT NOT NULL,
    calculated_at       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UNIQUE(machine_id, role_name, instance_name, pillar)
);
```

### 6.2 Scoring Strategy (Deterministic, No ML)

```
pillar_score = (passed_checks / total_applicable_checks) x 100

Weighted adjustments:
  - Critical severity FAIL = -15 points
  - High severity FAIL = -10 points
  - Medium severity FAIL = -5 points
  - WARN = -2 points

Overall role score = avg(security_score, performance_score, health_score, capacity_score)
```

Score is **always explainable**: "Your SQL Server security score is 78% because 3 of 15 checks failed: sa enabled, mixed auth, xp_cmdshell."

---

## 7. AI/ML Readiness

This feature directly feeds the `security_ai_features` table designed for future AI:

| Feature from RBIH | AI Use Case |
|---|---|
| role_name + pillar + result | Training labels for automated triage |
| Blocked sessions count | Anomaly detection input |
| TempDB growth rate | Predictive capacity alert |
| Failed backup streak | Risk scoring input |
| Score trends over time | Drift detection / regression alert |
| Remediation outcomes (future) | Reinforcement learning for safe automation |

**Key principle:** All role check results are stored historically, enabling future supervised learning without schema changes.

---

## 8. Constraints

| Constraint | Description |
|---|---|
| No PowerShell | All checks via native APIs only (SMO, WMI, registry, XML, .NET) |
| No CMD | No shelling out to sc.exe, netsh, dnscmd, sqlcmd, appcmd, certutil |
| SYSTEM context | Agent runs as SYSTEM — all checks must work in this context |
| Kill switches | Per-role: `KRYOSS_DISABLE_ROLE_{ROLENAME}` |
| Timeout | Max 2 minutes per role evaluation. Timeout = SKIP result |
| No breaking changes | Existing control framework and scoring must remain untouched |
| Feature flags | Each role can be enabled/disabled per organization |

---

## 9. Out of Scope

| Item | Reason |
|---|---|
| Linux/macOS roles | Windows-only platform |
| Third-party applications (Apache, MySQL, etc.) | Microsoft roles only in this roadmap |
| Automated remediation of role-specific issues | Future work (requires MFA gate + safety engine) |
| Custom role definitions by MSP operators | V2 feature |
| Real-time streaming (live blocked session monitor) | V2 feature — current model is scan-based |
| Exchange Server | Deprecated on-prem; may add as Phase 5 if demand exists |

---

## 10. Dependencies

| Dependency | Status | Impact |
|---|---|---|
| Existing compliance control framework | Stable (918+ controls) | Role checks integrate into same model |
| ScanLoop architecture | Stable | Role detection and evaluation added as new scan phases |
| Machine Detail portal component | Stable (Phase 1 UX complete) | Server Roles section extends existing page |
| security_ai_features table | In design | Role results will feed this table |
| CVE pipeline (vendor filter fix) | In progress | CVE matching can correlate with role-specific vulnerabilities |

---

## 11. Estimated Total Effort

| Phase | Scope | Effort | Risk |
|-------|-------|--------|------|
| Phase 1 | SQL Server + IIS + Detection Engine + Portal | 8-12 days | Medium |
| Phase 2 | DNS + DHCP + RDS | 5-8 days | Low |
| Phase 3 | Hyper-V + AD CS + File Server | 6-10 days | Medium |
| Phase 4 | Print Server + WSUS | 3-5 days | Low |
| **Total** | **10 roles x 4 pillars = ~200 checks** | **~22-35 days** | |

---

## 12. Success Metrics

| Metric | Target |
|---|---|
| Roles detected per org (avg) | >= 3 |
| Check coverage per detected role | 100% of defined checks |
| False positive rate | < 5% |
| Time to detect SQL blocked session | < 15 minutes (scan interval) |
| Time to detect DHCP scope exhaustion | < 15 minutes |
| MSP adoption (orgs using feature) | > 80% within 30 days of launch |

---

## 13. Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-04-27 | Freddy Hansen | Initial roadmap — full catalog, 4 phases, 10 roles |

---

*"If it runs on Windows Server, Kryoss audits it. Automatically. Completely."*