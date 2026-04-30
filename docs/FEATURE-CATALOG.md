# Kryoss — Feature & Control Catalog

> **Purpose:** Complete inventory of every feature, control, check, and data source in the platform.
> Use for: report gap analysis, marketing materials, folletos, and sales collateral.
>
> **Last updated:** 2026-04-27
> **Total checks across all modules: ~960+**

---

## Table of Contents

1. [Grand Summary](#1-grand-summary)
2. [Platform Data Sources](#2-platform-data-sources)
3. [Baseline Controls (BL-0001..BL-0444)](#3-baseline-controls-bl-0001bl-0444--444-controls)
4. [Scored Function Controls (SC-001..SC-161)](#4-scored-function-controls-sc-001sc-161--161-controls)
5. [HIPAA Refinement Controls (BL-0445..BL-0469)](#5-hipaa-refinement-controls-bl-0445bl-0469--25-controls)
6. [Server Controls (SRV-001..SRV-080)](#6-server-controls-srv-001srv-080--80-controls)
7. [Domain Controller Controls (DC-001..DC-100)](#7-domain-controller-controls-dc-001dc-100--100-controls)
8. [Network Diagnostic Controls (NET-001..NET-050)](#8-network-diagnostic-controls-net-001net-050--50-controls)
9. [Protocol Audit Controls (12 controls)](#9-protocol-audit-controls--12-controls)
10. [M365 Security Checks (M365-001..M365-050)](#10-m365-security-checks-m365-001m365-050--50-checks)
11. [Cloud Assessment Checks (7 pipelines, ~100 generators)](#11-cloud-assessment-checks--7-pipelines-100-generators)
12. [Agent Collection Capabilities](#12-agent-collection-capabilities)
13. [Server-Side Scanners](#13-server-side-scanners)
14. [Report Types & Coverage Matrix](#14-report-types--coverage-matrix)
15. [Coverage Gap Analysis](#15-coverage-gap-analysis)

---

## 1. Grand Summary

| Module | Count | ID Range |
|--------|------:|----------|
| Scored Function Controls (SC-*) | 161 | SC-001..SC-161 |
| Baseline Atomic Controls (BL-*) | 444 | BL-0001..BL-0444 |
| HIPAA Refinement Controls | 25 | BL-0445..BL-0469 |
| Server Controls (SRV-*) | 80 | SRV-001..SRV-080 |
| Domain Controller Controls (DC-*) | 100 | DC-001..DC-100 |
| Network Diagnostic Controls (NET-*) | 50 | NET-001..NET-050 |
| Protocol Audit Controls | 12 | AUDIT-*, NTLM-*, SMB1-*, SAFE-* |
| M365 Security Checks | 50 | M365-001..M365-050 |
| Cloud Assessment — Identity | 18 | CA-1 generators |
| Cloud Assessment — Endpoint | 15 | CA-2 generators |
| Cloud Assessment — Data | 15 | CA-3 generators |
| Cloud Assessment — Productivity | 11 | CA-4 generators |
| Cloud Assessment — Mail Flow | 8 per domain | CA-5 generators |
| Cloud Assessment — Azure | 20 | CA-6 generators |
| Cloud Assessment — Power BI | 14 | CA-7 checks |
| **Total endpoint controls (DB)** | **872** | |
| **Total incl. cloud generators** | **~960+** | |

### Platform Scope

| Platform | Controls | Description |
|----------|------:|-------------|
| W10 (Windows 10) | 647 | Baseline |
| W11 (Windows 11) | 647 | Baseline |
| MS19 (Server 2019) | 727 | Baseline + 80 SRV |
| MS22 (Server 2022) | 727 | Baseline + 80 SRV |
| MS25 (Server 2025) | 727 | Baseline + 80 SRV |
| DC19 (DC 2019) | 827 | Baseline + 80 SRV + 100 DC |
| DC22 (DC 2022) | 827 | Baseline + 80 SRV + 100 DC |
| DC25 (DC 2025) | 827 | Baseline + 80 SRV + 100 DC |

### Framework Coverage

| Framework | Endpoint Controls | Cloud Checks | Status |
|-----------|------------------:|:------------:|--------|
| NIST CSF | 827 (100%) | Yes | Full |
| CIS Controls | ~810 (~98%) | Yes | Full |
| HIPAA | ~380 (~46%) | Yes | Partial (admin/physical need attestation) |
| ISO 27001 | ~240 (~29%) | Yes | Partial |
| PCI DSS | ~30 (~3.6%) | Yes | Minimal |
| SOC 2 | — | Yes | Cloud only |
| CMMC | — | Yes | Cloud only |

---

## 2. Platform Data Sources

### 2A. Agent Engines (13 engines)

| Engine | Type | Controls | What it collects |
|--------|------|------:|------------------|
| RegistryEngine | `registry` | 371 | HKLM/HKCU/HKU/HKCR keys — security policies, protocol settings, features |
| NativeCommandEngine | `command` | 188 | TLS (16), UserRights (16 P/Invoke), AppLocker (5), inline registry (19), custom (5) |
| AuditpolEngine | `auditpol` | 34 | 60+ audit subcategories via P/Invoke |
| FirewallEngine | `firewall` | 21 | Per-profile: enabled, default actions, logging |
| ServiceEngine | `service` | 18 | Service status + start type |
| CertStoreEngine | `certstore` | 6 | Self-signed, expiring, weak-key certs |
| BitLockerEngine | `bitlocker` | 5 | Protection, encryption method, protectors, recovery |
| NetAccountCompatEngine | `netaccount` | 5 | Password/lockout/account policies |
| TpmEngine | `tpm` | 4 | TPM presence, version, manufacturer, ready state |
| EventLogEngine | `eventlog` | 4 | Log size, retention, event counts, top sources |
| SecurityPolicyEngine | `secedit` | 2 | UAC, RDP, AutoRun via P/Invoke |
| DcEngine | `dc` | 100 | 27 check types via LDAP/WMI/Registry/ServiceController |
| ProtocolAuditService | (toggle) | 12 | NTLM/SMBv1 audit — event log analysis |

### 2B. Agent Services (data collection beyond engines)

| Service | What it collects |
|---------|------------------|
| PlatformDetector | OS, CPU, RAM, multi-disk, TPM, BitLocker, SecureBoot, domain, Defender, product type |
| NetworkDiagnostics | Speed (up/down), latency, VPN, routes, adapters, cloud endpoint latency (6 M365 URLs), DNS, jitter, packet loss, traceroute |
| PortScanner | TCP top 100 + UDP top 20, banner grab, service detection |
| AdHygieneReport | Stale/dormant machines+users, disabled users, pwd-never-expire, privileged accounts, Kerberoastable, LAPS, delegation |
| SnmpScanner | Devices, interfaces, supplies (toner), CPU/mem/disk/processes, LLDP/CDP neighbors |
| PassiveDiscovery | NetBIOS, mDNS, SSDP listener — agentless device detection |
| PatchCollector | WU status, reboot pending, WSUS/WUfB config, installed hotfixes, NinjaRMM detection |
| DcHealthCollector | AD schema version, FSMO roles, replication status, sites/subnets/DCs/GCs |
| ThreatDetector | Event log threat indicators |
| SoftwareInventory | Installed apps (600+ commercial app normalization) |
| SelfUpdater | Auto-update from blob storage, SHA256 mandatory |
| ProtocolAuditService | Configures NTLM/SMBv1 audit logging on endpoints |

### 2C. Server-Side Scanners

| Scanner | What it produces |
|---------|-----------------|
| CveService | CVE matching against installed software (~60 built-in high-impact CVEs) |
| ExternalScanner | TCP scan of public IPs (53 ports), banner grab, findings |
| WanHealthService | Per-site WAN score 0-100 + 11 finding rules |
| EvaluationService | PASS/FAIL for all controls, framework scores, global score |
| AlertService | Drift detection: score drops, new critical findings, framework thresholds |
| GeoIpService | Country/city/ISP/ASN/connectivity type |
| SiteClusterService | Auto-groups machines by public IP into network sites |
| KeyRotationService | Per-machine session key rotation (48h) |

---

## 3. Baseline Controls (BL-0001..BL-0444) — 444 Controls

Direct registry/secedit/auditpol/service checks. Each validates a single setting.

### Control Categories

| Category | Description | Approx. Count |
|----------|-------------|------:|
| Account And Access Controls | User accounts, lockout, guest, UAC, screen lock | ~35 |
| Application Control | AppLocker, WDAC, software restrictions | ~15 |
| Audit And Logging | Event log config, audit policies | ~30 |
| Audit, Logging And Monitoring | Advanced audit subcategories | ~25 |
| Authentication | Logon/auth settings, Kerberos, NTLM | ~20 |
| Backup And Recovery | VSS, backup services, recovery env | ~12 |
| Browser And Application Policies | IE/Edge hardening, Java, Flash | ~15 |
| Browser Hardening | Browser-specific security settings | ~10 |
| Certificates And Cryptography | Cert store, TLS/SSL, SCHANNEL | ~20 |
| Credential Protection | WDigest, LSA, Credential Guard, LSASS | ~15 |
| Cryptography | Crypto algorithms, key lengths | ~10 |
| Encryption | BitLocker, EFS, disk encryption | ~10 |
| Endpoint Protection And Patching | Defender, updates, patching | ~25 |
| Exploit And Memory Protection | DEP, ASLR, Exploit Guard, EMET | ~10 |
| File System And Shared Resources | NTFS permissions, shared folders | ~10 |
| Firewall | Windows Firewall per-profile settings | ~20 |
| Hardening | General OS hardening | ~15 |
| Local Users And Account Management | Local accounts, stale users, admin rename | ~10 |
| Multi-Framework Coverage | Cross-framework controls | ~15 |
| Network And Protocol Security | SMB, NTLM, LLMNR, NetBIOS, IPv6 | ~30 |
| Network Security | Network protocol hardening | ~20 |
| Office Hardening | Office macro security, Trust Center | ~10 |
| Patch Management | WSUS, WU config, update scheduling | ~8 |
| Persistence Detection And Integrity | Scheduled tasks, startup items | ~5 |
| Privacy And Telemetry | Telemetry, data collection, diagnostics | ~15 |
| Remote Access | RDP, WinRM, remote management | ~15 |
| Security Options And Local Policy | Security policy settings | ~20 |
| Services Hardening | Unnecessary service disablement | ~15 |
| Software And Application Security | Unsigned/outdated software detection | ~10 |
| Time Synchronization | NTP, W32Time settings | ~4 |
| Windows Security Baseline | Microsoft security baseline settings | ~15 |

**Engine distribution:** registry (~280), secedit (~60), auditpol (~55), service (~30), command (~15), netaccount (~4)

---

## 4. Scored Function Controls (SC-001..SC-161) — 161 Controls

Composite/higher-order checks. Each evaluates multiple sub-aspects.

| ID | Name | Category |
|----|------|----------|
| SC-001 | SMBv1 Protocol | Network And Protocol Security |
| SC-002 | LLMNR (Link-Local Multicast Name Resolution) | Network And Protocol Security |
| SC-003 | NetBIOS over TCP/IP | Network And Protocol Security |
| SC-004 | BitLocker Full-Disk Encryption | Endpoint Protection And Patching |
| SC-005 | Windows Defender / Antivirus | Endpoint Protection And Patching |
| SC-006 | Password Policy | Account And Access Controls |
| SC-007 | Windows Updates | Endpoint Protection And Patching |
| SC-008 | Local Administrator Accounts | Account And Access Controls |
| SC-009 | Windows Audit Policy & Log Retention | Audit, Logging And Monitoring |
| SC-010 | Windows Firewall | Network And Protocol Security |
| SC-011 | Account Lockout Policy | Account And Access Controls |
| SC-012 | Guest Account | Account And Access Controls |
| SC-013 | Screen Lock and Auto-Logoff | Account And Access Controls |
| SC-014 | User Account Control (UAC) | Account And Access Controls |
| SC-015 | RDP (Remote Desktop Protocol) | Network And Protocol Security |
| SC-016 | SMB Signing | Network And Protocol Security |
| SC-017 | NTLMv2 Authentication Level | Network And Protocol Security |
| SC-018 | Autorun / AutoPlay | Endpoint Protection And Patching |
| SC-019 | PowerShell Execution Policy | Endpoint Protection And Patching |
| SC-020 | Event Log Size and Retention | Audit, Logging And Monitoring |
| SC-021 | Windows Defender Event Logging | Audit, Logging And Monitoring |
| SC-022 | Windows Firewall Logging | Audit, Logging And Monitoring |
| SC-023 | Secure Boot (UEFI) | Audit, Logging And Monitoring |
| SC-024 | Anonymous Access & Null Sessions | Audit, Logging And Monitoring |
| SC-025 | Open Ports — Unexpected Listening Services | Network And Protocol Security |
| SC-026 | Cleartext Protocols FTP/Telnet | Network And Protocol Security |
| SC-027 | WinRM Remote Management | Network And Protocol Security |
| SC-028 | IPv6 Configuration | Network And Protocol Security |
| SC-029 | Network Discovery | Network And Protocol Security |
| SC-030 | Print Spooler (PrintNightmare) | Network And Protocol Security |
| SC-031 | WPAD / Proxy Configuration | Network And Protocol Security |
| SC-032 | Weak TLS/SSL Protocols (Schannel) | Network And Protocol Security |
| SC-033 | Stale Local User Accounts | Local Users And Account Management |
| SC-034 | Accounts with Password Never Expires | Local Users And Account Management |
| SC-035 | Built-in Administrator Account Rename | Local Users And Account Management |
| SC-036 | Concurrent Logged-On Users | Local Users And Account Management |
| SC-037 | VSS Shadow Copies | Backup And Recovery |
| SC-038 | Volume Shadow Copy Service | Backup And Recovery |
| SC-039 | Windows Backup Configuration | Backup And Recovery |
| SC-040 | Windows Recovery Environment (WinRE) | Backup And Recovery |
| SC-041 | Certificate Store Management | Certificates And Cryptography |
| SC-042 | Weak Cryptographic Protocols | Certificates And Cryptography |
| SC-043 | SSL Certificate Validation | Certificates And Cryptography |
| SC-044 | Unsigned Software Detection | Software And Application Security |
| SC-045 | Outdated Software Detection | Software And Application Security |
| SC-046 | Suspicious Process Detection | Software And Application Security |
| SC-047 | Shared Folder Security | File System And Shared Resources |
| SC-048 | NTFS Permission Security | File System And Shared Resources |
| SC-049 | Browser Security Configuration | Browser And Application Policies |
| SC-050..SC-161 | AppLocker, WDAC, Credential Guard, Device Guard, BitLocker recovery, Exploit Guard, Windows Hello, Kerberos, NTLM restrictions, time sync, telemetry, Office macros, etc. | Various |

---

## 5. HIPAA Refinement Controls (BL-0445..BL-0469) — 25 Controls

Three functional blocks targeting HIPAA §164.312 requirements.

### Block A: MFA / Windows Hello for Business (BL-0445..BL-0450)

| ID | Name | Severity | What It Checks |
|----|------|----------|----------------|
| BL-0445 | WHfB - Policy Enabled | high | `PassportForWork\Enabled = 1` |
| BL-0446 | WHfB - Require TPM | high | `RequireSecurityDevice = 1` |
| BL-0447 | WHfB - Minimum PIN Length | medium | `MinimumPINLength >= 6` |
| BL-0448 | Smart Card Service - Start Type | low | SCardSvr = Manual or Automatic |
| BL-0449 | Device Join Status (dsregcmd) | medium | AzureAdJoined, DomainJoined, NgcSet |
| BL-0450 | NGC Container Provisioned | medium | WHfB credential exists |

### Block B: Event Log Retention (BL-0451..BL-0459)

| ID | Name | Severity | What It Checks |
|----|------|----------|----------------|
| BL-0451 | Security Log - Max Size | high | >= 192 MB |
| BL-0452 | Security Log - Retention Mode | high | Overwrite as needed |
| BL-0453 | Security Log - Auto Backup | medium | AutoBackupLogFiles = 1 |
| BL-0454 | System Log - Max Size | medium | >= 32 MB |
| BL-0455 | System Log - Retention Mode | medium | Overwrite as needed |
| BL-0456 | Application Log - Max Size | medium | >= 32 MB |
| BL-0457 | Application Log - Retention Mode | medium | Overwrite as needed |
| BL-0458 | Effective Config - Security Log | medium | Live effective config check |
| BL-0459 | Effective Config - System Log | low | Live effective config check |

### Block C: Backup Posture (BL-0460..BL-0469)

| ID | Name | Severity | What It Checks |
|----|------|----------|----------------|
| BL-0460 | Windows Server Backup - Last Versions | high | Backup freshness |
| BL-0461 | VSS Shadow Copies - List | medium | Shadow copies exist |
| BL-0462 | VSS Writers - Health | medium | All writers stable |
| BL-0463 | Veeam Endpoint Backup | low | Service detection |
| BL-0464 | Datto / Continuity Agent | low | Service detection |
| BL-0465 | Acronis Managed Machine | low | Service detection |
| BL-0466 | Carbonite Backup | low | Service detection |
| BL-0467 | Veritas Backup Exec | low | Service detection |
| BL-0468 | MozyPro Backup | low | Service detection |
| BL-0469 | Windows Backup (Modern) - Valid Config | medium | Scheduled backup configured |

---

## 6. Server Controls (SRV-001..SRV-080) — 80 Controls

Targeting Windows Server (MS19, MS22, MS25).

### SMB & Network Access (SRV-001..SRV-012)

| ID | Name |
|----|------|
| SRV-001 | SMB Server - Require Security Signature (Signing) |
| SRV-002 | SMB Server - Require Encryption (EncryptData) |
| SRV-003 | SMB Server - Restrict Null Session Access |
| SRV-004 | SMB Server - Null Session Pipes Empty |
| SRV-005 | SMB Server - Null Session Shares Empty |
| SRV-006 | SMB Server - SMBv1 Protocol Disabled |
| SRV-007 | SMB Server - Administrative Shares Hidden |
| SRV-008 | Restrict Anonymous Enumeration of SAM Accounts |
| SRV-009 | Restrict Anonymous Enumeration of SAM Accounts and Shares |
| SRV-010 | LAN Manager Authentication Level - NTLMv2 Only |
| SRV-011 | LDAP Client Signing Required |
| SRV-012 | Server SPN Target Name Validation Level |

### Remote Desktop / RDP (SRV-013..SRV-020)

| ID | Name |
|----|------|
| SRV-013 | RDP - Network Level Authentication (NLA) Required |
| SRV-014 | RDP - Minimum Encryption Level (High) |
| SRV-015 | RDP - Security Layer (SSL/TLS) |
| SRV-016 | RDP - Idle Session Timeout |
| SRV-017 | RDP - Disconnected Session Timeout |
| SRV-018 | RDP - Always Prompt for Password |
| SRV-019 | RDP - Drive Redirection Disabled |
| SRV-020 | RDP - Clipboard Redirection Disabled |

### WinRM & PowerShell (SRV-021..SRV-026)

| ID | Name |
|----|------|
| SRV-021 | WinRM - Service Hardened (HTTPS or Disabled) |
| SRV-022 | WinRM - Basic Authentication Disabled |
| SRV-023 | PowerShell - Constrained Language Mode |
| SRV-024 | PowerShell - Script Block Logging Enabled |
| SRV-025 | PowerShell - Module Logging Enabled |
| SRV-026 | PowerShell - Transcription Enabled |

### IIS Hardening (SRV-027..SRV-038)

| ID | Name |
|----|------|
| SRV-027 | IIS - Installed Version Current |
| SRV-028 | IIS - Default Web Site Removed |
| SRV-029 | IIS - Directory Browsing Disabled |
| SRV-030 | IIS - Custom Error Pages Configured |
| SRV-031 | IIS - Request Filtering Enabled |
| SRV-032 | TLS 1.2+ Required (TLS 1.0 Disabled) |
| SRV-033 | SSL/TLS - Weak Ciphers Disabled (RC4) |
| SRV-034 | IIS - HSTS Configured |
| SRV-035 | IIS - App Pool Identity Not LocalSystem |
| SRV-036 | IIS - Detailed Errors Disabled for Remote |
| SRV-037 | IIS - X-Powered-By Header Removed |
| SRV-038 | IIS - Server Header Suppressed |

### DNS Server (SRV-039..SRV-044)

| ID | Name |
|----|------|
| SRV-039 | DNS - Secure Dynamic Updates |
| SRV-040 | DNS - Recursion Disabled for External Zones |
| SRV-041 | DNS - Socket Pool Size >= 2500 |
| SRV-042 | DNS - Cache Locking >= 75% |
| SRV-043 | DNS - Diagnostic Logging Enabled |
| SRV-044 | DNS - DNSSEC Signing Enabled |

### DHCP Server (SRV-045..SRV-048)

| ID | Name |
|----|------|
| SRV-045 | DHCP - Audit Logging Enabled |
| SRV-046 | DHCP - Failover Configured |
| SRV-047 | DHCP - Lease Duration <= 8 Days |
| SRV-048 | DHCP - Domain Name Option Configured |

### Hyper-V (SRV-049..SRV-054)

| ID | Name |
|----|------|
| SRV-049 | Hyper-V - Integration Services Current |
| SRV-050 | Hyper-V - No Production Snapshots |
| SRV-051 | Hyper-V - Enhanced Session Mode Disabled |
| SRV-052 | Hyper-V - Secure Boot for Gen2 VMs |
| SRV-053 | Hyper-V - MAC Spoofing Disabled |
| SRV-054 | Hyper-V - Shielded VMs (Guest State Encryption) |

### File Server (SRV-055..SRV-060)

| ID | Name |
|----|------|
| SRV-055 | NTFS Permissions on System Root |
| SRV-056 | Object Access Auditing Enabled |
| SRV-057 | FSRM Feature Installed |
| SRV-058 | Shadow Copies (VSS) Configured |
| SRV-059 | DFS Namespace Healthy |
| SRV-060 | Access-Based Enumeration on Shares |

### Print Server (SRV-061..SRV-064)

| ID | Name |
|----|------|
| SRV-061 | Print Spooler Disabled (If Not Print Server) |
| SRV-062 | Point and Print Restrictions Enabled |
| SRV-063 | Package Point and Print - Approved Servers Only |
| SRV-064 | Print Spooler - Remote Access Restricted |

### Windows Update (SRV-065..SRV-068)

| ID | Name |
|----|------|
| SRV-065 | WSUS Server - Update Source Configured |
| SRV-066 | WU - Use WSUS or WU Configured |
| SRV-067 | WU - Automatic Updates Enabled |
| SRV-068 | WU - Scheduled Install Day Configured |

### General Server (SRV-069..SRV-080)

| ID | Name |
|----|------|
| SRV-069 | Server Core Detection (GUI Shell Installed) |
| SRV-070 | Defender Real-Time Protection (Server) |
| SRV-071 | Firewall Enabled All Profiles (Server) |
| SRV-072 | Remote Registry Service Disabled |
| SRV-073 | WDigest Authentication Disabled |
| SRV-074 | LSA Protection (RunAsPPL) |
| SRV-075 | Credential Guard Enabled |
| SRV-076 | LSASS Dump Prevention |
| SRV-077 | Shutdown Without Logon Disabled |
| SRV-078 | Last Logged On User Hidden |
| SRV-079 | Legal Notice / Logon Banner |
| SRV-080 | Security Event Log >= 128 MB |

---

## 7. Domain Controller Controls (DC-001..DC-100) — 100 Controls

Platforms: DC19, DC22, DC25. Engine: `dc` (LDAP/WMI) + `registry` + `auditpol` + `service` + `bitlocker` + `tpm`.

### A. AD Replication & Health (DC-001..DC-006)

| ID | Name | Severity |
|----|------|----------|
| DC-001 | AD Replication Status - No Failures | critical |
| DC-002 | SYSVOL Replication (DFS-R) Healthy | critical |
| DC-003 | FSMO Role Holders Accessible | high |
| DC-004 | DCDiag Core Tests Pass | critical |
| DC-005 | DCDiag DNS Registration Tests | high |
| DC-006 | AD Database (NTDS.dit) Integrity | high |

### B. LDAP Security (DC-007..DC-011)

| ID | Name | Severity |
|----|------|----------|
| DC-007 | LDAP Server Signing Enforcement | critical |
| DC-008 | LDAP Channel Binding Token Requirement | critical |
| DC-009 | LDAP Server Enforce Integrity | critical |
| DC-010 | LDAPS Certificate Bound (Port 636) | high |
| DC-011 | Refuse Machine Accounts with Default Passwords | high |

### C. Kerberos Security (DC-012..DC-017)

| ID | Name | Severity |
|----|------|----------|
| DC-012 | Krbtgt Password Age < 180 Days | critical |
| DC-013 | Kerberos Max Ticket Lifetime (10h) | medium |
| DC-014 | Kerberos Max Renewal Lifetime (7d) | medium |
| DC-015 | No Accounts with Pre-Auth Disabled (AS-REP Roastable) | critical |
| DC-016 | Kerberos Encryption: AES Only | critical |
| DC-017 | ZeroLogon Protection (Netlogon) | critical |

### D. DC Hardening (DC-018..DC-030)

| ID | Name | Severity |
|----|------|----------|
| DC-018 | AD Recycle Bin Enabled | high |
| DC-019 | Protected Users Group Has Privileged Members | high |
| DC-020 | Fine-Grained Password Policies | medium |
| DC-021 | Print Spooler Disabled on DC (PrintNightmare) | critical |
| DC-022 | No Unconstrained Delegation (Except DCs) | critical |
| DC-023 | DC Internet Access Restricted | high |
| DC-024 | Authentication Policies (Silos) | medium |
| DC-025 | NTLM Audit: Auth in Domain Enabled | medium |
| DC-026 | NTLM Audit: Incoming Traffic Enabled | medium |
| DC-027 | AdminSDHolder Default Permissions Intact | high |
| DC-028 | PDC Emulator Time Source Configured | high |
| DC-029 | AD Tombstone Lifetime >= 180 Days | medium |
| DC-030 | Domain Functional Level >= Server 2016 | high |

### E. DC Network / DNS (DC-031..DC-035)

| ID | Name | Severity |
|----|------|----------|
| DC-031 | DNS Zones: Secure-Only Dynamic Updates | high |
| DC-032 | DNS Scavenging Enabled | medium |
| DC-033 | DNS Root Hints or Secure Forwarders | medium |
| DC-034 | DC Windows Firewall Enabled (Domain Profile) | high |
| DC-035 | DNSSEC Trust Anchors Configured | low |

### F. DC Audit (DC-036..DC-040)

| ID | Name | Severity |
|----|------|----------|
| DC-036 | Audit: Directory Service Access | high |
| DC-037 | Audit: Directory Service Changes | critical |
| DC-038 | Audit: Directory Service Replication | medium |
| DC-039 | Audit: Kerberos Authentication Service | high |
| DC-040 | Audit: Kerberos Service Ticket Operations | high |

### G. AD Security (DC-041..DC-050)

| ID | Name | Severity |
|----|------|----------|
| DC-041 | Kerberoastable Accounts Count | critical |
| DC-042 | Stale Computer Accounts (90+ Days) | medium |
| DC-043 | Accounts with Password Never Expires | high |
| DC-044 | Inactive Admin Accounts (60+ Days) | critical |
| DC-045 | Schema Admins Group Empty | high |
| DC-046 | LAPS Coverage >= 80% | high |
| DC-047 | Orphaned AdminCount Cleaned | medium |
| DC-048 | GPO Count Reasonable (< 200) | low |
| DC-049 | AD Replication No Consecutive Failures | critical |
| DC-050 | DSRM Admin Logon Behavior Set | high |

### H. Registry Hardening (DC-051..DC-070)

| ID | Name | Severity |
|----|------|----------|
| DC-051 | SMB Client Signing Required | high |
| DC-052 | SMB Server Signing Required on DC | critical |
| DC-053 | LDAP Channel Binding Enforced | critical |
| DC-054 | Credential Guard Enabled on DC | high |
| DC-055 | LSA Protection (RunAsPPL) on DC | critical |
| DC-056 | WDigest Credential Caching Disabled | critical |
| DC-057 | Null Session Enumeration Restricted | high |
| DC-058 | Null Session Named Pipe Access Restricted | high |
| DC-059 | LM Hash Storage Disabled | critical |
| DC-060 | LAN Manager Auth Level (NTLMv2 Only) | critical |
| DC-061 | Remote Registry Disabled on DC | medium |
| DC-062 | NTDS Service Running | critical |
| DC-063 | Print Spooler Disabled (DcEngine) | critical |
| DC-064 | SMB Server Signing Enforced (DcEngine) | critical |
| DC-065 | Netlogon Secure Channel (ZeroLogon) | critical |
| DC-066 | NTLM Restriction Level on DC | high |
| DC-067 | Expired Certificates in CA Store | medium |
| DC-068 | DNS Forwarders Configured | medium |
| DC-069 | LDAP Server Signing Enforced (DcEngine) | critical |
| DC-070 | Time Source Configured (Not Local Clock) | high |

### I. Additional Registry Hardening (DC-071..DC-085)

| ID | Name | Severity |
|----|------|----------|
| DC-071 | SAM Remote Enumeration Restricted | high |
| DC-072 | Cached Logon Credentials Limited | high |
| DC-073 | Anonymous SID/Name Translation Disabled | medium |
| DC-074 | NTDS.dit Auditing via Object Access | high |
| DC-075 | IPv6 Disabled on DC (If Not Used) | medium |
| DC-076 | LLMNR Disabled on DC | high |
| DC-077 | NetBIOS over TCP/IP Disabled on DC | medium |
| DC-078 | WPAD Protocol Disabled on DC | high |
| DC-079 | Secure Boot Enabled on DC | high |
| DC-080 | PowerShell Script Block Logging on DC | high |
| DC-081 | PowerShell Module Logging on DC | medium |
| DC-082 | PowerShell Transcription on DC | medium |
| DC-083 | WinRM Restricted to Domain/Private | medium |
| DC-084 | Remote Desktop Restricted on DC | high |
| DC-085 | AutoPlay Disabled on DC | medium |

### J. Additional Audit Policies (DC-086..DC-095)

| ID | Name | Severity |
|----|------|----------|
| DC-086 | Audit: Credential Validation | high |
| DC-087 | Audit: Computer Account Management | high |
| DC-088 | Audit: Security Group Management | critical |
| DC-089 | Audit: User Account Management | critical |
| DC-090 | Audit: Logon/Logoff Events | high |
| DC-091 | Audit: Special Logon | high |
| DC-092 | Audit: Sensitive Privilege Use | high |
| DC-093 | Audit: Authentication Policy Change | high |
| DC-094 | Audit: MPSSVC Rule-Level Policy Change | medium |
| DC-095 | Security Event Log >= 1 GB | high |

### K. Services & Hardware (DC-096..DC-100)

| ID | Name | Severity |
|----|------|----------|
| DC-096 | Xbox Services Disabled on DC | low |
| DC-097 | Fax Service Disabled on DC | low |
| DC-098 | BitLocker on DC System Drive | high |
| DC-099 | TPM Present on DC | medium |
| DC-100 | Windows Defender Running on DC | high |

---

## 8. Network Diagnostic Controls (NET-001..NET-050) — 50 Controls

Server-side evaluation from `machine_network_diag` data.

### Network Performance

| ID | Name | Severity | Threshold |
|----|------|----------|-----------|
| NET-001 | Download speed above 10 Mbps | medium | >= 10 |
| NET-002 | Upload speed above 5 Mbps | medium | >= 5 |
| NET-003 | Internet latency below 100ms | medium | <= 100 |
| NET-004 | Internet latency below 50ms (optimal) | low | <= 50 |
| NET-005 | No peers with >5% packet loss | high | <= 5% |
| NET-006 | Peer avg latency below 10ms | medium | <= 10 |
| NET-007 | Peer jitter below 5ms | low | <= 5 |
| NET-008 | All internal peers reachable | medium | 0 unreachable |
| NET-017 | Bandwidth utilization send | low | informational |
| NET-018 | Bandwidth utilization receive | low | informational |
| NET-020 | WiFi link speed >= 100 Mbps | low | >= 100 |
| NET-025 | Primary adapter >= 1 Gbps | low | >= 1000 |
| NET-027 | Cross-subnet latency < 50ms | medium | <= 50 |
| NET-031 | Download speed above 50 Mbps | low | >= 50 |
| NET-032 | Download speed above 100 Mbps | low | >= 100 |
| NET-033 | Upload speed above 20 Mbps | low | >= 20 |
| NET-042 | MTU size standard (1500) | low | standard |
| NET-043 | No bandwidth saturation (>80%) | high | <= 80% |
| NET-044 | Cross-subnet peers reachable | medium | 0 unreachable |
| NET-050 | Network health composite >= 70 | medium | >= 70 |

### Network Security

| ID | Name | Severity | Threshold |
|----|------|----------|-----------|
| NET-009 | VPN detected | low | informational |
| NET-010 | Route table < 100 routes | low | <= 100 |
| NET-011 | No default gateway conflicts | high | <= 1 gateway |
| NET-012 | Adapter count | low | informational |
| NET-013 | DNS servers configured | critical | must exist |
| NET-014 | No public DNS on domain machines | high | false |
| NET-015 | DHCP status | low | informational |
| NET-016 | Gateway on primary adapter | high | must exist |
| NET-019 | No open/WEP WiFi | critical | 0 open |
| NET-021 | IPv6 status | low | informational |
| NET-022 | WPAD disabled | high | false |
| NET-023 | Hosts file not tampered | high | <= 5 entries |
| NET-024 | NTP time source configured | high | true |
| NET-026 | Subnet count | low | informational |
| NET-028 | No duplicate IPs | critical | 0 |
| NET-029 | ARP table < 500 entries | low | <= 500 |
| NET-030 | Listening ports < 20 | medium | <= 20 |
| NET-034 | No disconnected adapters with IP | medium | 0 |
| NET-035 | >= 2 DNS servers | medium | >= 2 |
| NET-036 | VPN split tunnel detection | medium | informational |
| NET-037 | No invalid subnet masks | high | false |
| NET-038 | No APIPA addresses (169.254.x.x) | critical | 0 |
| NET-039 | Single gateway per adapter | medium | 0 multi |
| NET-040 | MAC address inventory | low | informational |
| NET-041 | NIC teaming detection | low | informational |
| NET-045 | Site topology map | low | informational |
| NET-046 | No excessive loopback routes | medium | <= 3 |
| NET-047 | Persistent route inventory | low | informational |
| NET-048 | No route metric conflicts | medium | 0 |
| NET-049 | LLMNR/NetBIOS disabled | high | false |

---

## 9. Protocol Audit Controls — 12 Controls

Org-level opt-in. Evaluates NTLM and SMBv1 usage over 90 days.

| ID | Name | Engine | Severity |
|----|------|--------|----------|
| AUDIT-001 | NTLM inbound audit enabled | registry | medium |
| AUDIT-002 | NTLM outbound audit enabled | registry | medium |
| AUDIT-003 | SMBv1 access audit enabled | registry | medium |
| AUDIT-004 | Security log >= 500 MB for 90-day retention | eventlog | low |
| NTLM-USE-001 | NTLM outbound events (8001, 90d) | eventlog | high |
| NTLM-USE-002 | NTLM inbound events (8002/8003, 90d) | eventlog | high |
| NTLM-USE-003 | Top 10 NTLM callers (by user) | eventlog | medium |
| NTLM-USE-004 | Top 10 NTLM source workstations | eventlog | medium |
| SMB1-USE-001 | SMBv1 access events (3000, 90d) | eventlog | high |
| SMB1-USE-002 | Top 10 SMBv1 client IPs | eventlog | medium |
| SAFE-TO-DISABLE-NTLM | Zero NTLM usage in 90 days → safe to disable | eventlog | critical |
| SAFE-TO-DISABLE-SMB1 | Zero SMBv1 usage in 90 days → safe to disable | eventlog | critical |

---

## 10. M365 Security Checks (M365-001..M365-050) — 50 Checks

Server-side via Microsoft Graph API. 15 categories.

### Conditional Access (M365-001..M365-008)

| ID | Name |
|----|------|
| M365-001 | CA policies configured |
| M365-002 | MFA required for all users via CA |
| M365-003 | MFA required for admin roles via CA |
| M365-004 | Legacy authentication blocked via CA |
| M365-005 | Compliant device requirement via CA |
| M365-006 | Risky sign-in CA policy |
| M365-007 | Risky user CA policy |
| M365-008 | CA policies in report-only mode |

### MFA (M365-009..M365-014)

| ID | Name |
|----|------|
| M365-009 | MFA enrollment rate |
| M365-010 | Admin accounts MFA status |
| M365-011 | MFA methods distribution |
| M365-012 | MFA registration campaign |
| M365-013 | Auth methods policy configured |
| M365-014 | SSPR (Self-Service Password Reset) |

### Security Defaults / Audit (M365-015..M365-017)

| ID | Name |
|----|------|
| M365-015 | Security defaults status |
| M365-016 | Unified audit log |
| M365-017 | Sign-in risk policy |

### Admin Roles (M365-018..M365-022)

| ID | Name |
|----|------|
| M365-018 | Global Administrator count |
| M365-019 | PIM (Privileged Identity Management) |
| M365-020 | Admin MFA enforcement |
| M365-021 | Break-glass emergency accounts |
| M365-022 | Active directory roles in use |

### Guest Access (M365-023..M365-026)

| ID | Name |
|----|------|
| M365-023 | Guest user count |
| M365-024 | Guest invitation restrictions |
| M365-025 | Guest access to Teams/Groups |
| M365-026 | SharePoint external sharing |

### Mail Security (M365-027..M365-030)

| ID | Name |
|----|------|
| M365-027 | External mail forwarding rules |
| M365-028 | SPF records configured |
| M365-029 | DMARC policy configured |
| M365-030 | DKIM selectors configured |

### Stale Accounts (M365-031..M365-032)

| ID | Name |
|----|------|
| M365-031 | Stale user accounts (30 days) |
| M365-032 | Dormant user accounts (90 days) |

### App Registrations (M365-033..M365-036)

| ID | Name |
|----|------|
| M365-033 | App registrations with expired secrets |
| M365-034 | App registrations with excessive permissions |
| M365-035 | Enterprise apps with risky consent |
| M365-036 | App registrations with no owner |

### Secure Score & Identity (M365-037..M365-042)

| ID | Name |
|----|------|
| M365-037 | Microsoft Secure Score |
| M365-038 | Secure Score improvement actions pending |
| M365-039 | Named locations configured |
| M365-040 | Password expiration policy |
| M365-041 | High-risk sign-ins (30 days) |
| M365-042 | High-risk users |

### Device Management (M365-043..M365-046)

| ID | Name |
|----|------|
| M365-043 | Intune managed device count |
| M365-044 | Non-compliant Intune devices |
| M365-045 | Intune compliance policies |
| M365-046 | Device encryption enforcement |

### Data Protection, Alerts, SharePoint, Org (M365-047..M365-050)

| ID | Name |
|----|------|
| M365-047 | DLP / sensitivity labels |
| M365-048 | Active security alerts (30 days) |
| M365-049 | SharePoint external sharing settings |
| M365-050 | Verified domains |

---

## 11. Cloud Assessment Checks — 7 Pipelines, ~100 Generators

Dynamic findings (not fixed IDs). Each generator evaluates conditions and produces findings.

### CA-1: Identity (18 generators)

| Generator | What It Evaluates |
|-----------|-------------------|
| AAD Premium P1 | CA coverage, MFA enrollment %, legacy auth sign-ins, passwordless % |
| AAD Premium P2 | PIM permanent vs eligible, access reviews, risky users, guest licensing |
| Conditional Access | Policy total/enabled, MFA-required policies, legacy auth blocking |
| Identity Protection | Compromised accounts, at-risk users, risk-based CA policies |
| Intune (Plan A) | Device compliance rate, CA requiring compliance |
| MFA Premium | MFA licensing check |
| Governance | PIM config, access reviews recurring/scope, user consent, risky apps |
| Internet Access (GSA) | Web filtering policies, FQDN/category rules, forwarding profiles |
| Internet Access Frontline | GSA licensing |
| Private Access | Connector deployment, app segments |
| Private Access CA | GSA licensing for Private Access |
| Service Principal Hygiene | Expired/expiring credentials, creds older than 2 years |
| B2B Trust Posture | Inbound MFA trust, compliant-device trust, allow/block domains |
| PIM Audit Maturity | Activations 30d, roles without MFA/justification requirement |
| User Lifecycle Hygiene | Password-never-expires, no sign-in 90d |
| Admin MFA Coverage | Admins without MFA, permanent GA count > 5 |
| Consent Surface | AllPrincipals OAuth grants, unrestricted user consent |
| Device Security Posture | BitLocker coverage %, devices without compliance policy |

### CA-2: Endpoint (15 generators)

| Generator | What It Evaluates |
|-----------|-------------------|
| Compliance Policy Coverage | Zero policies = critical gap |
| Per-Platform Compliance Gaps | Per-OS device count vs policy count |
| Non-Compliance Rate | >15% = high, 5-15% = warning |
| BYOD App Protection | BYOD without App Protection policies |
| Per-Platform App Protection | iOS/Android policy presence |
| Autopilot | Profile count for Windows fleets > 5 devices |
| Config Profile Drift | Failed + conflicting / total assigned > 15% |
| Device Encryption | Unencrypted devices % |
| Enrollment Restrictions | Zero restrictions = any device enrolls |
| Defender Activation | 403/404 = not licensed or no devices |
| Exposure Score | >60 = high, 30-60 = medium, <30 = low |
| Critical Vulnerabilities | Critical CVE count > 0 |
| High Vulnerabilities | High-severity CVE count > 10 |
| Unpatched Software | Apps with known weaknesses |
| High-Risk Machines | Machines flagged High by Defender |

### CA-3: Data Protection (15 generators)

| Generator | What It Evaluates |
|-----------|-------------------|
| AIP/DLP Licensing | AIP P1/P2 + DLP license presence |
| Sensitivity Label Deployment | 0 = critical, <3 = warning |
| Label Coverage | <40% labeled files = critical |
| Oversharing | >=20% org/public shared = critical |
| High-Risk Sites | Sites exceeding oversharing thresholds |
| DLP Posture | Zero DLP alerts 30d = misconfigured |
| Guest User Access | >50 guests = critical |
| eDiscovery | License presence |
| Advanced Audit | License (10-year retention + Copilot events) |
| Retention Labels | Publication status |
| Customer Lockbox | Status verification |
| Information Barriers | Licensed + deployment |
| Insider Risk | License + policy check |
| Unlabeled Content | Unlabeled files across scanned sites |
| OneDrive Hoarding | Avg GB/user > 100 = warning |

### CA-4: Productivity (11 generators)

| Generator | What It Evaluates |
|-----------|-------------------|
| License Checks (per SKU) | Purchased vs assigned utilization % |
| Copilot Adoption | Assigned/Purchased: <50% = critical |
| Email Adoption | Inactive 30d: >20% = warning |
| Teams Adoption | Inactive 30d: >20% = warning |
| SharePoint Deployment | 0 sites = warning |
| OneDrive Adoption | Inactive 30d: >30% = warning |
| Office Desktop Adoption | Activation rate <70% = warning |
| Wasted Licenses | No sign-in 30d: >5 = critical |
| Wasted Copilot Licenses | Copilot + inactive = critical (expensive) |
| Guest Ratio | Guests/Total > 20% = warning |
| Graph Connectors for Copilot | Copilot licensed + 0 connectors = warning |

### CA-5: Mail Flow (8 generators per domain)

| Generator | What It Evaluates |
|-----------|-------------------|
| SPF | Record presence, lookup count, mechanism type |
| DKIM | selector1 + selector2 CNAME presence |
| DMARC | Policy (none/quarantine/reject), pct, rua |
| MTA-STS | Policy file presence and mode |
| BIMI | BIMI TXT record presence |
| Forwarding Findings | Mailboxes with external forwarding |
| Shared Mailbox Sign-in | Active sign-in on shared mailboxes |
| Consent Finding | Exchange permissions availability |

### CA-6: Azure Infrastructure (20 generators)

| Generator | What It Evaluates |
|-----------|-------------------|
| No Subscriptions Scanned | Zero subs |
| Empty Subscriptions | ResourceCount = 0 |
| Resource Sprawl | Avg > 500 resources/sub |
| Multi-Region Footprint | Regions > 3 |
| Public Exposure Posture | Positive rollup |
| Defender Not Enabled | Plans not enabled |
| Defender Unhealthy Ratio | Unhealthy assessment threshold |
| Defender Healthy Dominates | Positive: healthy dominant |
| Secure Score Low | Score threshold violation |
| Secure Score Good | Healthy range |
| Storage Public Blob | Public blob access enabled |
| Storage HTTP | Non-HTTPS traffic allowed |
| Storage No Soft-Delete | No blob soft-delete |
| Key Vault No Soft-Delete | Without soft-delete |
| Key Vault No Purge Protection | Without purge protection |
| Public IP Sprawl | Count vs subscriptions |
| NSG Any/Any/Allow | Dangerous NSG rules |
| VM Unencrypted OS Disk | Unencrypted OS disks |
| VM Without Managed Identity | No managed identity |
| Policy Non-Compliant | Non-compliant resources |

### CA-7: Power BI (14 checks)

| Check | What It Evaluates |
|-------|-------------------|
| Admin API Access | API accessibility |
| Orphaned Workspaces | No active admin |
| Personal Workspaces | Personal workspace count |
| External Workspace Users | External users in workspaces |
| No Premium Capacity | No Premium/Fabric capacity |
| Capacity Overload | > 85% usage |
| Capacity Usage | Within healthy range |
| Datasets Never Refreshed | Never refreshed |
| Datasets Stale | Not refreshed 30+ days |
| Gateway Offline | Offline gateways |
| Personal Gateways Only | No enterprise gateway |
| External Sharing Volume | > 10 external shares 30d |
| High Export Volume | > 50 exports 30d |
| High Delete Activity | > 20 deletes 30d |

---

## 12. Agent Collection Capabilities

Data collected per machine beyond controls:

| Category | Fields |
|----------|--------|
| **Hardware** | OS, CPU, RAM, multi-disk inventory, TPM version, BitLocker status, SecureBoot, Defender status, product type (workstation/server/DC) |
| **Software** | 600+ commercial app detection from registry Uninstall keys |
| **Network Diag** | Download/upload Mbps, latency, VPN detection, route table, adapter inventory, cloud endpoint latency (outlook/teams/sharepoint/graph/login/admin), DNS resolution, jitter, packet loss, traceroute, hop count |
| **Port Scan** | TCP top 100 + UDP top 20 per discovered host, banner grab, service detection |
| **AD Hygiene** | Stale/dormant machines+users, disabled users, pwd-never-expire, privileged accounts, Kerberoastable, unconstrained delegation, LAPS coverage, adminCount residual, domain functional level |
| **SNMP** | Device discovery, interfaces, supplies (toner), CPU/mem/disk/processes (HOST-RESOURCES-MIB), LLDP/CDP neighbors |
| **Passive Discovery** | NetBIOS, mDNS, SSDP — agentless device detection |
| **Patch Status** | WU service status, reboot pending, WSUS/WUfB config, last check/install times, installed hotfixes (QFE), NinjaRMM agent detection |
| **DC Health** | AD schema version, forest/domain functional levels, 5 FSMO role holders, site/subnet/DC/GC counts, per-partner replication status |
| **Threats** | Event log threat indicators, severity, category, vector |
| **Protocol Audit** | NTLM/SMBv1 event log analysis over 90 days |

---

## 13. Server-Side Scanners

| Scanner | What it produces |
|---------|-----------------|
| **CVE Scanner** | Matches installed software against ~60 built-in high-impact CVEs (Chrome, Firefox, 7-Zip, WinRAR, Adobe, Java, .NET, Zoom, TeamViewer) |
| **External Port Scanner** | TCP scan of public IPs (53 ports), banner grab, service detection, risk findings |
| **WAN Health** | Per-site score 0-100 (latency 30%, jitter 20%, packet loss 25%, throughput 15%, DNS 10%), 11 finding rules |
| **Evaluation Engine** | Server-side PASS/FAIL for all 872 controls, per-framework scores, global score |
| **Drift Alerts** | Score drops, new critical findings, framework threshold breaches |
| **GeoIP** | Country/city/ISP/ASN/connectivity type per public IP |
| **Site Clustering** | Auto-groups machines by public IP into network sites |
| **Benchmarks** | Franchise peers, industry baseline (15 NAICS × 5 employee bands), global Kryoss |

---

## 14. Report Types & Coverage Matrix

### 16 Report Types

| # | Type | Audience | Key content |
|---|------|----------|-------------|
| 1 | C-Level | Executives | Score, grade, trend, top 3 risks, frameworks, cloud posture, CTAs |
| 2 | Exec One-Pager | Board (2 pages) | Grade hero, mini KPIs, framework grid, top 5 findings |
| 3 | Technical | IT team | Asset matrix, all categories, top findings, control details, Iron Six, gap analysis |
| 4 | Framework | Auditors | Framework-specific: gauge, categories, gap analysis, control detail, evidence |
| 5 | Compliance | Compliance officers | Scorecard with ring gauges, benchmarks, multi-framework, evidence |
| 6 | Preventa Opener | Sales (short) | Risk score, top 3, threat vectors, network mini, next step |
| 7 | Preventa Detailed | Sales (full) | Risk score, categories, threats, findings, ROI, service catalog, timeline |
| 8 | Proposal | Business proposal | KPIs, ROI, decisions matrix, gap analysis, pricing, timeline |
| 9 | Monthly | Recurring briefing | Score trend, frameworks, categories, resolved vs new findings |
| 10 | Cloud Executive | Cloud-focused exec | Cloud findings, area radar, effort estimate |
| 11 | M365 | M365 security | M365 findings by category, Copilot readiness |
| 12 | Hygiene | AD health | Hygiene breakdown, stale objects, privileged accounts |
| 13 | Risk Assessment | Risk & threat | Threats, open ports, attack vectors, credential exposure |
| 14 | Network | Network assessment | Speed, latency, VPN, routes, per-machine diag |
| 15 | Inventory | Asset inventory | OS distribution, security coverage, storage, hardware table |
| 16 | Test Fixture | QA | All blocks with synthetic data |

### Data Coverage Matrix

| Data Source | C-Lvl | Tech | Frmwk | Compl | PrevO | PrevD | Prop | Month | CldEx | M365 | Hyg | Risk | Net | Inv |
|-------------|:-----:|:----:|:-----:|:-----:|:-----:|:-----:|:----:|:-----:|:-----:|:----:|:---:|:----:|:---:|:---:|
| Endpoint controls | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — | — | ✅ | ✅ | ✅ | ✅ |
| Framework scores | ✅ | — | ✅ | ✅ | — | — | — | ✅ | ✅ | — | — | — | — | — |
| Hygiene (AD) | ✅ | ✅ | — | ✅ | ✅ | ✅ | ✅ | ✅ | — | — | ✅ | ✅ | — | — |
| Enrichment | ✅ | — | — | — | ✅ | ✅ | ✅ | — | — | — | ✅ | ✅ | — | ✅ |
| Cloud Assessment | ✅ | ✅ | ✅ | — | — | ✅ | ✅ | ✅ | ✅ | ✅ | — | — | ✅ | — |
| M365 findings | ✅ | — | — | — | — | ✅ | ✅ | — | ✅ | ✅ | — | — | — | — |
| Network diag | ✅ | — | — | — | ✅ | — | — | — | — | — | — | — | ✅ | ✅ |
| Service catalog | — | — | — | — | — | ✅ | ✅ | — | — | — | — | — | — | — |
| Benchmarks | — | — | — | ✅ | — | — | — | — | — | — | — | — | — | — |
| Score history | ✅ | — | — | — | — | — | — | ✅ | — | — | — | — | — | — |
| CTAs | ✅ | — | — | — | — | — | — | — | ✅ | ✅ | ✅ | ✅ | — | — |
| **CVE findings** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Patch compliance** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **DC health** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **WAN health** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **SNMP devices** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **External scan** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Remediation tasks** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Hypervisor/VM** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

**Legend:** ✅ = used | ❌ = data exists but not in any report | — = not relevant

---

## 15. Coverage Gap Analysis

### Data in platform but NOT in any report

| # | Data Source | Impact | Should appear in |
|---|------------|--------|------------------|
| GAP-1 | **CVE Findings** | Critical | Technical, Risk, C-Level, Monthly, Proposal |
| GAP-2 | **Patch Compliance** | Critical | Technical, C-Level, Monthly, Inventory |
| GAP-3 | **DC Health** | High | Technical, Hygiene, C-Level |
| GAP-4 | **WAN Health** | High | Network, C-Level, Technical |
| GAP-5 | **SNMP Devices** | Medium | Network, Inventory |
| GAP-6 | **Network Topology** | Medium | Network |
| GAP-7 | **External Scan** | High | Risk, Technical, C-Level, Proposal |
| GAP-8 | **Remediation Tasks** | Medium | Monthly, Proposal |
| GAP-9 | **Hypervisor/VM** | Medium | Future IA report |

### Reports that should exist but don't

| # | Report | Why |
|---|--------|-----|
| MISSING-1 | DC / Active Directory Report | 100 DC controls + DC health + FSMO + replication — no dedicated recipe |
| MISSING-2 | Infrastructure Assessment Report | IA-10 in roadmap — would combine WAN, SNMP, topology, hypervisors |
| MISSING-3 | Vulnerability Report | CVE + external scan + patch compliance = natural standalone |
| MISSING-4 | Patch Status Report | Patch data exists, no report representation |
