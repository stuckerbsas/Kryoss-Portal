SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- ============================================================
-- seed_004_controls.sql
-- Kryoss Platform -- Full Control Catalog Seed
-- Generated from:
--   controls_catalog.json (AST extraction, 161 scored + 155 baseline-array)
--   baseline_imperative_checks.json (manual curation, 289 imperative)
-- Total: ~605 control_defs
-- Run AFTER seed_002_frameworks_platforms.sql
-- ============================================================

-- Relax type constraint to accept extended check types in check_json
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_ctrldef_type')
    ALTER TABLE control_defs DROP CONSTRAINT ck_ctrldef_type;
GO
ALTER TABLE control_defs ADD CONSTRAINT ck_ctrldef_type CHECK ([type] IN (
    'registry','secedit','auditpol','firewall','service','netaccount','command'
));
GO

-- ============================================================
-- Main seed batch (all DECLAREs + all INSERTs in one batch so variables survive)
-- ============================================================
DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @fwCIS  INT = (SELECT id FROM frameworks WHERE code='CIS');
DECLARE @fwNIST INT = (SELECT id FROM frameworks WHERE code='NIST');
DECLARE @fwHIPAA INT = (SELECT id FROM frameworks WHERE code='HIPAA');
DECLARE @fwPCI  INT = (SELECT id FROM frameworks WHERE code='PCI-DSS');
DECLARE @fwISO  INT = (SELECT id FROM frameworks WHERE code='ISO27001');
DECLARE @fwCMMC INT = (SELECT id FROM frameworks WHERE code='CMMC');

-- ============================================================
-- CATEGORIES (upsert missing)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Account And Access Controls')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Account And Access Controls', 101, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Application Control')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Application Control', 102, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Audit And Logging')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Audit And Logging', 103, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Audit, Logging And Monitoring')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Audit, Logging And Monitoring', 104, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Authentication')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Authentication', 105, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Backup And Recovery')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Backup And Recovery', 106, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Browser And Application Policies')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Browser And Application Policies', 107, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Browser Hardening')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Browser Hardening', 108, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Certificates And Cryptography')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Certificates And Cryptography', 109, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Credential Protection')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Credential Protection', 110, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Cryptography')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Cryptography', 111, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Encryption')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Encryption', 112, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Endpoint Protection And Patching')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Endpoint Protection And Patching', 113, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Exploit And Memory Protection')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Exploit And Memory Protection', 114, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'File System And Shared Resources')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'File System And Shared Resources', 115, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Firewall')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Firewall', 116, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Hardening')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Hardening', 117, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Local Users And Account Management')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Local Users And Account Management', 118, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Multi-Framework Coverage')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Multi-Framework Coverage', 119, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Network And Protocol Security')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Network And Protocol Security', 120, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Network Security')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Network Security', 121, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Office Hardening')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Office Hardening', 122, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Patch Management')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Patch Management', 123, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Persistence Detection And Integrity')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Persistence Detection And Integrity', 124, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Privacy And Telemetry')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Privacy And Telemetry', 125, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Remote Access')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Remote Access', 126, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Security Options And Local Policy')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Security Options And Local Policy', 127, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Services Hardening')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Services Hardening', 128, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Software And Application Security')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Software And Application Security', 129, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Time Synchronization')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Time Synchronization', 130, @systemUserId);
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Windows Security Baseline')
    INSERT INTO control_categories (name, sort_order, created_by) VALUES (N'Windows Security Baseline', 131, @systemUserId);

-- ============================================================
-- CONTROL DEFS
-- ============================================================
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-001', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'SMBv1 Protocol (Test-SMBv1)', 'command', 'low', N'{"function":"Test-SMBv1","key":"SMBv1","check_type":"scored_function","description":"Legacy file-sharing protocol with known exploits (EternalBlue/WannaCry)","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-002', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'LLMNR (Link-Local Multicast Name Resolution) (Test-LLMNR)', 'command', 'low', N'{"function":"Test-LLMNR","key":"LLMNR","check_type":"scored_function","description":"Legacy name resolution protocol vulnerable to poisoning attacks","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-003', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'NetBIOS over TCP/IP (Test-NetBIOS)', 'command', 'low', N'{"function":"Test-NetBIOS","key":"NetBIOS","check_type":"scored_function","description":"Legacy name resolution vulnerable to poisoning and enumeration attacks","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-004', (SELECT id FROM control_categories WHERE name=N'Endpoint Protection And Patching'), N'BitLocker Full-Disk Encryption (Test-BitLocker)', 'command', 'medium', N'{"function":"Test-BitLocker","key":"BitLocker","check_type":"scored_function","description":"Full-disk encryption protecting data at rest -- required for HIPAA ePHI","points":2}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-005', (SELECT id FROM control_categories WHERE name=N'Endpoint Protection And Patching'), N'Windows Defender / Antivirus (Test-WindowsDefender)', 'command', 'medium', N'{"function":"Test-WindowsDefender","key":"Defender","check_type":"scored_function","description":"Endpoint antivirus, real-time protection, and definition currency","points":2}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-006', (SELECT id FROM control_categories WHERE name=N'Account And Access Controls'), N'Password Policy (Test-PasswordPolicy)', 'command', 'medium', N'{"function":"Test-PasswordPolicy","key":"PasswordPolicy","check_type":"scored_function","description":"Minimum length, complexity, and expiry enforcement (CIS: 12+ chars)","points":2}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-007', (SELECT id FROM control_categories WHERE name=N'Endpoint Protection And Patching'), N'Windows Updates (Test-WindowsUpdate)', 'command', 'medium', N'{"function":"Test-WindowsUpdate","key":"WindowsUpdate","check_type":"scored_function","description":"OS patch currency and security update compliance","points":2}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-008', (SELECT id FROM control_categories WHERE name=N'Account And Access Controls'), N'Local Administrator Accounts (Test-LocalAdmins)', 'command', 'low', N'{"function":"Test-LocalAdmins","key":"LocalAdmins","check_type":"scored_function","description":"Privileged local accounts and least-privilege enforcement","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-009', (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'), N'Windows Audit Policy & Log Retention (Test-AuditPolicy)', 'command', 'low', N'{"function":"Test-AuditPolicy","key":"AuditPolicy","check_type":"scored_function","description":"Event log policy coverage -- logon, object access, policy changes","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-010', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'Windows Firewall (Test-WindowsFirewall)', 'command', 'low', N'{"function":"Test-WindowsFirewall","key":"Firewall","check_type":"scored_function","description":"Host-based firewall profiles and inbound rule configuration","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-011', (SELECT id FROM control_categories WHERE name=N'Account And Access Controls'), N'Account Lockout Policy (Test-AccountLockout)', 'command', 'low', N'{"function":"Test-AccountLockout","key":"AccountLockout","check_type":"scored_function","description":"Lockout threshold and duration after failed logon attempts","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-012', (SELECT id FROM control_categories WHERE name=N'Account And Access Controls'), N'Guest Account (Test-GuestAccount)', 'command', 'low', N'{"function":"Test-GuestAccount","key":"GuestAccount","check_type":"scored_function","description":"Built-in Guest account must be disabled","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-013', (SELECT id FROM control_categories WHERE name=N'Account And Access Controls'), N'Screen Lock and Auto-Logoff (Test-ScreenLock)', 'command', 'low', N'{"function":"Test-ScreenLock","key":"ScreenLock","check_type":"scored_function","description":"Inactivity timeout --15 min with password-on-resume (HIPAA: required)","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-014', (SELECT id FROM control_categories WHERE name=N'Account And Access Controls'), N'User Account Control (UAC) (Test-UAC)', 'command', 'low', N'{"function":"Test-UAC","key":"UAC","check_type":"scored_function","description":"UAC enabled with Admin Approval Mode for built-in Administrator","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-015', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'RDP (Remote Desktop Protocol) (Test-RDP)', 'command', 'low', N'{"function":"Test-RDP","key":"RDP","check_type":"scored_function","description":"RDP disabled or NLA enforced -- open RDP is a primary ransomware vector","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-016', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'SMB Signing (Test-SMBSigning)', 'command', 'low', N'{"function":"Test-SMBSigning","key":"SMBSigning","check_type":"scored_function","description":"SMB packet signing prevents man-in-the-middle relay attacks","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-017', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'NTLMv2 Authentication Level (Test-NTLMv2)', 'command', 'low', N'{"function":"Test-NTLMv2","key":"NTLMv2","check_type":"scored_function","description":"LM and NTLMv1 disabled -- only NTLMv2 or Kerberos allowed","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-018', (SELECT id FROM control_categories WHERE name=N'Endpoint Protection And Patching'), N'Autorun / AutoPlay (Test-Autorun)', 'command', 'low', N'{"function":"Test-Autorun","key":"Autorun","check_type":"scored_function","description":"USB and removable media auto-execution disabled (malware vector)","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-019', (SELECT id FROM control_categories WHERE name=N'Endpoint Protection And Patching'), N'PowerShell Execution Policy (Test-PowerShellPolicy)', 'command', 'low', N'{"function":"Test-PowerShellPolicy","key":"PSExecPolicy","check_type":"scored_function","description":"Restricts unsigned script execution to prevent malware","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-020', (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'), N'Event Log Size and Retention (Test-EventLogSize)', 'command', 'low', N'{"function":"Test-EventLogSize","key":"EventLogSize","check_type":"scored_function","description":"Security log --192MB -- insufficient size causes evidence loss","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-021', (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'), N'Windows Defender Event Logging (Test-DefenderLogging)', 'command', 'low', N'{"function":"Test-DefenderLogging","key":"DefenderLogging","check_type":"scored_function","description":"Defender threat and detection events visible in Event Log","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-022', (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'), N'Windows Firewall Logging (Test-FirewallLogging)', 'command', 'low', N'{"function":"Test-FirewallLogging","key":"FirewallLogging","check_type":"scored_function","description":"Dropped packet and connection logging enabled for forensics","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-023', (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'), N'Secure Boot (UEFI) (Test-SecureBoot)', 'command', 'low', N'{"function":"Test-SecureBoot","key":"SecureBoot","check_type":"scored_function","description":"UEFI Secure Boot prevents unauthorized bootloader/rootkit execution","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-024', (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'), N'Anonymous Access & Null Sessions (Test-AnonymousAccess)', 'command', 'low', N'{"function":"Test-AnonymousAccess","key":"AnonymousAccess","check_type":"scored_function","description":"Prevents unauthenticated enumeration of users, shares, and SAM","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-025', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'Open Ports -- Unexpected Listening Services (Test-OpenPorts)', 'command', 'low', N'{"function":"Test-OpenPorts","key":"OpenPorts","check_type":"scored_function","description":"Services listening on non-standard ports indicate attack surface expansion","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-026', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'Cleartext Protocols (FTP/Telnet) (Test-CleartextProtocols)', 'command', 'low', N'{"function":"Test-CleartextProtocols","key":"CleartextProtocols","check_type":"scored_function","description":"FTP port 21 and Telnet port 23 transmit credentials in plaintext","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-027', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'WinRM Remote Management (Test-WinRM)', 'command', 'low', N'{"function":"Test-WinRM","key":"WinRM","check_type":"scored_function","description":"WinRM without HTTPS allows credential interception on the network","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-028', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'IPv6 Configuration (Test-IPv6Config)', 'command', 'low', N'{"function":"Test-IPv6Config","key":"IPv6Config","check_type":"scored_function","description":"Unconfigured IPv6 enables rogue DHCPv6 and SLAAC attacks","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-029', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'Network Discovery (Test-NetworkDiscovery)', 'command', 'low', N'{"function":"Test-NetworkDiscovery","key":"NetworkDiscovery","check_type":"scored_function","description":"Enabled network discovery broadcasts host presence to the entire network","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-030', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'Print Spooler (PrintNightmare) (Test-PrintSpooler)', 'command', 'low', N'{"function":"Test-PrintSpooler","key":"PrintSpooler","check_type":"scored_function","description":"Running Print Spooler enables PrintNightmare RCE -- CVE-2021-34527","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-031', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'WPAD / Proxy Configuration (Test-WPADProxy)', 'command', 'low', N'{"function":"Test-WPADProxy","key":"WPADProxy","check_type":"scored_function","description":"WPAD auto-discovery enables man-in-the-middle proxy attacks","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-032', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'Weak TLS/SSL Protocols (Schannel) (Test-WeakTLS)', 'command', 'low', N'{"function":"Test-WeakTLS","key":"WeakTLS","check_type":"scored_function","description":"SSL 3.0 and TLS 1.0/1.1 have known vulnerabilities (POODLE, BEAST)","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-033', (SELECT id FROM control_categories WHERE name=N'Local Users And Account Management'), N'Stale Local User Accounts (Test-StaleLocalAccounts)', 'command', 'low', N'{"function":"Test-StaleLocalAccounts","key":"StaleLocalAccounts","check_type":"scored_function","description":"Enabled accounts inactive for 90+ days are orphaned access vectors","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-034', (SELECT id FROM control_categories WHERE name=N'Local Users And Account Management'), N'Accounts with Password Never Expires (Test-PasswordNeverExpires)', 'command', 'low', N'{"function":"Test-PasswordNeverExpires","key":"PasswordNeverExpires","check_type":"scored_function","description":"Non-expiring passwords violate HIPAA and CIS rotation requirements","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-035', (SELECT id FROM control_categories WHERE name=N'Local Users And Account Management'), N'Built-in Administrator Account Rename (Test-AdminAccountRename)', 'command', 'low', N'{"function":"Test-AdminAccountRename","key":"AdminAccountRename","check_type":"scored_function","description":"Default \"Administrator\" name is a known brute-force target","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-036', (SELECT id FROM control_categories WHERE name=N'Local Users And Account Management'), N'Concurrent Logged-On Users (Test-LoggedOnUsers)', 'command', 'low', N'{"function":"Test-LoggedOnUsers","key":"LoggedOnUsers","check_type":"scored_function","description":"Multiple concurrent sessions may indicate shared credentials or unauthorized access","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-037', (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'), N'VSS Shadow Copies (Test-VSSCopies)', 'command', 'low', N'{"function":"Test-VSSCopies","key":"VSSCopies","check_type":"scored_function","description":"Shadow copies are the first line of ransomware recovery","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-038', (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'), N'Volume Shadow Copy Service (Test-VSSService)', 'command', 'low', N'{"function":"Test-VSSService","key":"VSSService","check_type":"scored_function","description":"VSS service must be enabled for shadow copies and backup tools to function","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-039', (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'), N'Windows Backup Configuration (Test-WindowsBackup)', 'command', 'low', N'{"function":"Test-WindowsBackup","key":"WindowsBackup","check_type":"scored_function","description":"No backup means no recovery -- HIPAA requires documented contingency plan","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-040', (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'), N'Windows Recovery Environment (WinRE) (Test-RecoveryPartition)', 'command', 'low', N'{"function":"Test-RecoveryPartition","key":"RecoveryPartition","check_type":"scored_function","description":"WinRE enables system recovery without external media","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-041', (SELECT id FROM control_categories WHERE name=N'Certificates And Cryptography'), N'Certificate Store Management (Test-CertificateStore)', 'command', 'low', N'{"function":"Test-CertificateStore","key":"ExpiringCertificates","check_type":"scored_function","description":"Expired or weak certificates create trust and security vulnerabilities","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-042', (SELECT id FROM control_categories WHERE name=N'Certificates And Cryptography'), N'Weak Cryptographic Protocols (Test-WeakCrypto)', 'command', 'low', N'{"function":"Test-WeakCrypto","key":"WeakTLSProtocols","check_type":"scored_function","description":"SSL 2.0/3.0 and weak TLS versions must be disabled","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-043', (SELECT id FROM control_categories WHERE name=N'Certificates And Cryptography'), N'SSL Certificate Validation (Test-SSLCertificates)', 'command', 'low', N'{"function":"Test-SSLCertificates","key":"UntrustedRootCAs","check_type":"scored_function","description":"Self-signed or weak SSL certificates compromise secure communications","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-044', (SELECT id FROM control_categories WHERE name=N'Software And Application Security'), N'Unsigned Software Detection (Test-UnsignedSoftware)', 'command', 'low', N'{"function":"Test-UnsignedSoftware","key":"BlacklistedSoftware","check_type":"scored_function","description":"Unsigned executables may indicate malware or unauthorized software","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-045', (SELECT id FROM control_categories WHERE name=N'Software And Application Security'), N'Outdated Software Detection (Test-OutdatedSoftware)', 'command', 'low', N'{"function":"Test-OutdatedSoftware","key":"EOLSoftware","check_type":"scored_function","description":"Outdated software contains known vulnerabilities","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-046', (SELECT id FROM control_categories WHERE name=N'Software And Application Security'), N'Suspicious Process Detection (Test-SuspiciousProcesses)', 'command', 'low', N'{"function":"Test-SuspiciousProcesses","key":"SuspiciousProcesses","check_type":"scored_function","description":"Unusual processes may indicate malware or unauthorized software","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-047', (SELECT id FROM control_categories WHERE name=N'File System And Shared Resources'), N'Shared Folder Security (Test-SharedFolders)', 'command', 'low', N'{"function":"Test-SharedFolders","key":"AdminShares","check_type":"scored_function","description":"Overly permissive shares create data exposure risks","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-048', (SELECT id FROM control_categories WHERE name=N'File System And Shared Resources'), N'NTFS Permission Security (Test-NTFSPermissions)', 'command', 'low', N'{"function":"Test-NTFSPermissions","key":"SensitiveFolderPermissions","check_type":"scored_function","description":"Weak NTFS permissions on system directories create privilege escalation risks","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-049', (SELECT id FROM control_categories WHERE name=N'Browser And Application Policies'), N'Browser Security Configuration (Test-BrowserSecurity)', 'command', 'low', N'{"function":"Test-BrowserSecurity","key":"BrowserSecurity","check_type":"scored_function","description":"Insecure browser settings enable malware delivery and data theft","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-050', (SELECT id FROM control_categories WHERE name=N'Browser And Application Policies'), N'Application Execution Controls (Test-AppExecutionPolicies)', 'command', 'low', N'{"function":"Test-AppExecutionPolicies","key":"AppExecutionPolicies","check_type":"scored_function","description":"Unrestricted application execution enables malware persistence and privilege escalation","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-051', (SELECT id FROM control_categories WHERE name=N'Software And Application Security'), N'Software Inventory (Test-SoftwareInventory)', 'command', 'low', N'{"function":"Test-SoftwareInventory","key":"SoftwareInventory","check_type":"scored_function","description":"Complete list of installed software for asset management and compliance","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-052', (SELECT id FROM control_categories WHERE name=N'Credential Protection'), N'WDigest Authentication (Test-WDigestAuthentication)', 'command', 'low', N'{"function":"Test-WDigestAuthentication","key":"WDigestAuth","check_type":"scored_function","description":"Ensures WDigest plaintext credential storage is disabled","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-053', (SELECT id FROM control_categories WHERE name=N'Credential Protection'), N'Credential Guard (Test-CredentialGuard)', 'command', 'low', N'{"function":"Test-CredentialGuard","key":"CredentialGuard","check_type":"scored_function","description":"Validates Virtualization-Based Security and Credential Guard are running","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-054', (SELECT id FROM control_categories WHERE name=N'Credential Protection'), N'LSASS Protection (Test-LSASSProtection)', 'command', 'low', N'{"function":"Test-LSASSProtection","key":"LSASSProtection","check_type":"scored_function","description":"Ensures LSASS runs as Protected Process Light to prevent credential dumping","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-055', (SELECT id FROM control_categories WHERE name=N'Exploit And Memory Protection'), N'DEP Policy (Test-DEPPolicy)', 'command', 'low', N'{"function":"Test-DEPPolicy","key":"DEPPolicy","check_type":"scored_function","description":"Validates Data Execution Prevention is configured for maximum protection","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-056', (SELECT id FROM control_categories WHERE name=N'Exploit And Memory Protection'), N'SEHOP Protection (Test-SEHOPProtection)', 'command', 'low', N'{"function":"Test-SEHOPProtection","key":"SEHOPProtection","check_type":"scored_function","description":"Ensures Structured Exception Handler Overwrite Protection is enabled","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-057', (SELECT id FROM control_categories WHERE name=N'Exploit And Memory Protection'), N'ASLR Policy (Test-ASLRPolicy)', 'command', 'low', N'{"function":"Test-ASLRPolicy","key":"ASLRPolicy","check_type":"scored_function","description":"Validates Address Space Layout Randomization is enabled system-wide","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-058', (SELECT id FROM control_categories WHERE name=N'Exploit And Memory Protection'), N'ASR Rules (Test-ASRRules)', 'command', 'low', N'{"function":"Test-ASRRules","key":"ASRRules","check_type":"scored_function","description":"Validates critical Attack Surface Reduction rules are configured in Block mode","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-059', (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'), N'PowerShell Script Block Logging (Test-PSScriptBlockLogging)', 'command', 'low', N'{"function":"Test-PSScriptBlockLogging","key":"PSScriptBlockLogging","check_type":"scored_function","description":"Ensures PowerShell script block logging is enabled for threat visibility","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-060', (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'), N'PowerShell Module Logging (Test-PSModuleLogging)', 'command', 'low', N'{"function":"Test-PSModuleLogging","key":"PSModuleLogging","check_type":"scored_function","description":"Ensures PowerShell module logging is enabled for audit trail","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-061', (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'), N'PowerShell Transcription (Test-PSTranscription)', 'command', 'low', N'{"function":"Test-PSTranscription","key":"PSTranscription","check_type":"scored_function","description":"Ensures PowerShell transcription is enabled for session recording","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-062', (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'), N'Advanced Audit Policy (Test-AdvancedAuditPolicy)', 'command', 'low', N'{"function":"Test-AdvancedAuditPolicy","key":"AdvancedAuditPolicy","check_type":"scored_function","description":"Validates critical audit subcategories are configured for security monitoring","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-063', (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'), N'Command Line Process Auditing (Test-CommandLineAuditing)', 'command', 'low', N'{"function":"Test-CommandLineAuditing","key":"CommandLineAuditing","check_type":"scored_function","description":"Ensures command-line arguments are included in process creation events","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-064', (SELECT id FROM control_categories WHERE name=N'Account And Access Controls'), N'User Rights Assignment (Test-UserRightsAssignment)', 'command', 'low', N'{"function":"Test-UserRightsAssignment","key":"UserRightsAssignment","check_type":"scored_function","description":"Validates critical user rights are properly restricted","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-065', (SELECT id FROM control_categories WHERE name=N'Security Options And Local Policy'), N'LDAP Client Signing (Test-LDAPClientSigning)', 'command', 'low', N'{"function":"Test-LDAPClientSigning","key":"LDAPClientSigning","check_type":"scored_function","description":"Ensures LDAP client signing is enabled to prevent relay attacks","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-066', (SELECT id FROM control_categories WHERE name=N'Security Options And Local Policy'), N'LAN Manager Authentication Level (Test-LanManagerAuthLevel)', 'command', 'low', N'{"function":"Test-LanManagerAuthLevel","key":"LanManagerAuthLevel","check_type":"scored_function","description":"Ensures NTLMv2 is enforced and legacy LM/NTLM protocols are refused","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-067', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'Remote Assistance (Test-RemoteAssistance)', 'command', 'low', N'{"function":"Test-RemoteAssistance","key":"RemoteAssistance","check_type":"scored_function","description":"Ensures Remote Assistance solicited offers are disabled","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-068', (SELECT id FROM control_categories WHERE name=N'Security Options And Local Policy'), N'Autoplay Disabled (Test-AutoplayDisabled)', 'command', 'low', N'{"function":"Test-AutoplayDisabled","key":"AutoplayDisabled","check_type":"scored_function","description":"Ensures AutoRun is disabled for all drive types to prevent malware execution","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-069', (SELECT id FROM control_categories WHERE name=N'Account And Access Controls'), N'Interactive Logon Message (Test-InteractiveLogonMessage)', 'command', 'low', N'{"function":"Test-InteractiveLogonMessage","key":"InteractiveLogonMessage","check_type":"scored_function","description":"Ensures a legal notice is displayed before logon for compliance and deterrence","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-070', (SELECT id FROM control_categories WHERE name=N'Account And Access Controls'), N'Cached Logons Count (Test-CachedLogonsCount)', 'command', 'low', N'{"function":"Test-CachedLogonsCount","key":"CachedLogonsCount","check_type":"scored_function","description":"Ensures the number of cached logon credentials is limited to reduce theft risk","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-071', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'DNS Client Multicast (Test-DNSClientMulticast)', 'command', 'low', N'{"function":"Test-DNSClientMulticast","key":"DNSClientMulticast","check_type":"scored_function","description":"Ensures multicast name resolution (LLMNR) is disabled to prevent poisoning attacks","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-072', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'Wi-Fi Security (Test-WiFiSecurity)', 'command', 'low', N'{"function":"Test-WiFiSecurity","key":"WiFiSecurity","check_type":"scored_function","description":"Ensures automatic connection to open Wi-Fi hotspots and Wi-Fi Sense are disabled","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-073', (SELECT id FROM control_categories WHERE name=N'Services Hardening'), N'Remote Registry Service (Test-RemoteRegistryService)', 'command', 'low', N'{"function":"Test-RemoteRegistryService","key":"RemoteRegistryService","check_type":"scored_function","description":"Ensures the Remote Registry service is disabled to prevent remote registry access","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-074', (SELECT id FROM control_categories WHERE name=N'Services Hardening'), N'Unnecessary Services (Test-UnnecessaryServices)', 'command', 'low', N'{"function":"Test-UnnecessaryServices","key":"UnnecessaryServices","check_type":"scored_function","description":"Ensures non-essential services (Xbox, SSDP, UPnP, Telnet, SNMP) are disabled","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-075', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'SSH Configuration (Test-SSHConfiguration)', 'command', 'low', N'{"function":"Test-SSHConfiguration","key":"SSHConfiguration","check_type":"scored_function","description":"Validates OpenSSH Server is either not installed or securely configured","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-076', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'RDP Session Timeout (Test-SessionTimeoutRDP)', 'command', 'low', N'{"function":"Test-SessionTimeoutRDP","key":"SessionTimeoutRDP","check_type":"scored_function","description":"Ensures idle and disconnected RDP sessions are configured with timeout limits","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-077', (SELECT id FROM control_categories WHERE name=N'Account And Access Controls'), N'Machine Inactivity Limit (Test-MachineInactivityLimit)', 'command', 'low', N'{"function":"Test-MachineInactivityLimit","key":"MachineInactivityLimit","check_type":"scored_function","description":"Ensures the machine locks after a defined period of inactivity","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-078', (SELECT id FROM control_categories WHERE name=N'Software And Application Security'), N'SmartScreen Status (Test-SmartScreenStatus)', 'command', 'low', N'{"function":"Test-SmartScreenStatus","key":"SmartScreenStatus","check_type":"scored_function","description":"Ensures Windows SmartScreen is enabled to protect against malicious downloads and sites","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-079', (SELECT id FROM control_categories WHERE name=N'Software And Application Security'), N'AppLocker or WDAC (Test-AppLockerOrWDAC)', 'command', 'low', N'{"function":"Test-AppLockerOrWDAC","key":"AppLockerOrWDAC","check_type":"scored_function","description":"Validates that application whitelisting is configured via AppLocker or Windows Defender Application Control","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-080', (SELECT id FROM control_categories WHERE name=N'Privacy And Telemetry'), N'Telemetry Level (Test-TelemetryLevel)', 'command', 'low', N'{"function":"Test-TelemetryLevel","key":"TelemetryLevel","check_type":"scored_function","description":"Ensures Windows telemetry is set to Security or Required (Basic) to minimize data collection","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-081', (SELECT id FROM control_categories WHERE name=N'Privacy And Telemetry'), N'Cortana and Search (Test-CortanaAndSearch)', 'command', 'low', N'{"function":"Test-CortanaAndSearch","key":"CortanaAndSearch","check_type":"scored_function","description":"Ensures Cortana is disabled and web search is turned off to prevent data leakage","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-082', (SELECT id FROM control_categories WHERE name=N'Privacy And Telemetry'), N'Removable Storage Control (Test-RemovableStorageControl)', 'command', 'low', N'{"function":"Test-RemovableStorageControl","key":"RemovableStorageControl","check_type":"scored_function","description":"Validates that removable storage devices are restricted to prevent data exfiltration","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-083', (SELECT id FROM control_categories WHERE name=N'Software And Application Security'), N'Insecure File Associations (Test-InsecureFileAssociations)', 'command', 'low', N'{"function":"Test-InsecureFileAssociations","key":"InsecureFileAssociations","check_type":"scored_function","description":"Ensures Windows Script Host is disabled and dangerous script file extensions are neutralized","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-084', (SELECT id FROM control_categories WHERE name=N'Certificates And Cryptography'), N'Encryption at Rest (Test-EncryptionAtRest)', 'command', 'medium', N'{"function":"Test-EncryptionAtRest","key":"EncryptionAtRest","check_type":"scored_function","description":"Verifies BitLocker uses AES-256 encryption with TPM key protector on the system drive","points":2}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-085', (SELECT id FROM control_categories WHERE name=N'Certificates And Cryptography'), N'TLS Cipher Suites (Test-TLSCipherSuites)', 'command', 'low', N'{"function":"Test-TLSCipherSuites","key":"TLSCipherSuites","check_type":"scored_function","description":"Ensures no weak or deprecated TLS cipher suites are enabled (RC4, DES, 3DES, NULL, EXPORT, MD5)","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-086', (SELECT id FROM control_categories WHERE name=N'Certificates And Cryptography'), N'EFS Status (Test-EFSStatus)', 'command', 'low', N'{"function":"Test-EFSStatus","key":"EFSStatus","check_type":"scored_function","description":"Checks whether EFS is available and whether a Data Recovery Agent certificate is configured","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-087', (SELECT id FROM control_categories WHERE name=N'File System And Shared Resources'), N'Access Control Lists (Test-AccessControlList)', 'command', 'low', N'{"function":"Test-AccessControlList","key":"AccessControlList","check_type":"scored_function","description":"Verifies that critical system directories do not grant excessive permissions to Everyone or Users","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-088', (SELECT id FROM control_categories WHERE name=N'File System And Shared Resources'), N'Anonymous Enumeration (Test-AnonymousEnumeration)', 'command', 'low', N'{"function":"Test-AnonymousEnumeration","key":"AnonymousEnumeration","check_type":"scored_function","description":"Ensures RestrictAnonymousSAM and RestrictAnonymous are set to prevent anonymous account/share enumeration","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-089', (SELECT id FROM control_categories WHERE name=N'File System And Shared Resources'), N'Null Session Pipes (Test-NullSessionPipes)', 'command', 'low', N'{"function":"Test-NullSessionPipes","key":"NullSessionPipes","check_type":"scored_function","description":"Ensures NullSessionPipes and NullSessionShares are empty to prevent anonymous access to named pipes and shares","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-090', (SELECT id FROM control_categories WHERE name=N'File System And Shared Resources'), N'LSA Anonymous Name Lookup (Test-LSAAnonymousNameLookup)', 'command', 'low', N'{"function":"Test-LSAAnonymousNameLookup","key":"LSAAnonymousNameLookup","check_type":"scored_function","description":"Ensures the LSAAnonymousNameLookup security policy is disabled to prevent anonymous SID-to-name translation","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-091', (SELECT id FROM control_categories WHERE name=N'Persistence Detection And Integrity'), N'Scheduled Tasks Audit (Test-ScheduledTasksAudit)', 'command', 'low', N'{"function":"Test-ScheduledTasksAudit","key":"ScheduledTasksAudit","check_type":"scored_function","description":"Detects suspicious scheduled tasks that may indicate persistence mechanisms","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-092', (SELECT id FROM control_categories WHERE name=N'Persistence Detection And Integrity'), N'Startup Program Audit (Test-StartupProgramAudit)', 'command', 'low', N'{"function":"Test-StartupProgramAudit","key":"StartupProgramAudit","check_type":"scored_function","description":"Checks auto-start registry locations and startup folders for suspicious entries","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-093', (SELECT id FROM control_categories WHERE name=N'Persistence Detection And Integrity'), N'WMI Subscriptions (Test-WMISubscriptions)', 'command', 'low', N'{"function":"Test-WMISubscriptions","key":"WMISubscriptions","check_type":"scored_function","description":"Detects permanent WMI event subscriptions commonly used for malware persistence","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-094', (SELECT id FROM control_categories WHERE name=N'Persistence Detection And Integrity'), N'Suspicious Registry Keys (Test-SuspiciousRegistryKeys)', 'command', 'low', N'{"function":"Test-SuspiciousRegistryKeys","key":"SuspiciousRegistryKeys","check_type":"scored_function","description":"Checks known registry hijack locations (IFEO, Winlogon, LSA) for tampering","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-095', (SELECT id FROM control_categories WHERE name=N'Local Users And Account Management'), N'Local Group Membership (Test-LocalGroupMembership)', 'command', 'low', N'{"function":"Test-LocalGroupMembership","key":"LocalGroupMembership","check_type":"scored_function","description":"Audits sensitive local groups (RDP Users, Backup Operators, Power Users) for unexpected members","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-096', (SELECT id FROM control_categories WHERE name=N'Local Users And Account Management'), N'Emergency Access Procedures (Test-EmergencyAccessProcedures)', 'command', 'low', N'{"function":"Test-EmergencyAccessProcedures","key":"EmergencyAccessProcedures","check_type":"scored_function","description":"Verifies a break-glass local admin account exists for emergency access (HIPAA-relevant)","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-097', (SELECT id FROM control_categories WHERE name=N'Time Synchronization'), N'NTP Configuration (Test-NTPConfiguration)', 'command', 'low', N'{"function":"Test-NTPConfiguration","key":"NTPConfiguration","check_type":"scored_function","description":"Verifies Windows Time service is running and synced to a valid NTP source","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-098', (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'), N'System Restore Config (Test-SystemRestoreConfig)', 'command', 'low', N'{"function":"Test-SystemRestoreConfig","key":"SystemRestoreConfig","check_type":"scored_function","description":"Verifies system restore points exist and are recent for recovery readiness","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-099', (SELECT id FROM control_categories WHERE name=N'Persistence Detection And Integrity'), N'Windows Integrity Check (Test-WindowsIntegrityCheck)', 'command', 'low', N'{"function":"Test-WindowsIntegrityCheck","key":"WindowsIntegrityCheck","check_type":"scored_function","description":"Checks CBS log for system file corruption indicators and pending repairs","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-100', (SELECT id FROM control_categories WHERE name=N'Persistence Detection And Integrity'), N'Driver Signing Enforcement (Test-DriverSigningEnforcement)', 'command', 'low', N'{"function":"Test-DriverSigningEnforcement","key":"DriverSigningEnforcement","check_type":"scored_function","description":"Verifies test signing and integrity check bypasses are not enabled in BCD","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-101', (SELECT id FROM control_categories WHERE name=N'Persistence Detection And Integrity'), N'UEFI Secure Boot Enhanced (Test-UEFISecureBootEnhanced)', 'command', 'low', N'{"function":"Test-UEFISecureBootEnhanced","key":"UEFISecureBootEnhanced","check_type":"scored_function","description":"Verifies the system uses UEFI firmware with Secure Boot enabled","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-102', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'IPSEC Exemptions (Test-IPSECExemptions)', 'command', 'low', N'{"function":"Test-IPSECExemptions","key":"IPSECExemptions","check_type":"scored_function","description":"Verifies IPSec NoDefaultExempt is configured to minimize protocol exemptions","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-103', (SELECT id FROM control_categories WHERE name=N'Network And Protocol Security'), N'DNS Over HTTPS (Test-DNSOverHTTPS)', 'command', 'low', N'{"function":"Test-DNSOverHTTPS","key":"DNSOverHTTPS","check_type":"scored_function","description":"Checks whether DNS-over-HTTPS (DoH) is configured for encrypted DNS resolution","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-104', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Multi-Factor Authentication Status (Test-MFAStatus)', 'command', 'low', N'{"function":"Test-MFAStatus","key":"MFAStatus","check_type":"scored_function","description":"Verifies Windows Hello, smartcard, or FIDO2 MFA enrollment on the endpoint","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-105', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Defender Signature Age (Test-DefenderSignatureAge)', 'command', 'low', N'{"function":"Test-DefenderSignatureAge","key":"DefenderSignatureAge","check_type":"scored_function","description":"Checks antivirus definition currency â€” stale signatures miss new threats","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-106', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'SIEM / Log Forwarding (Test-SIEMLogForwarding)', 'command', 'low', N'{"function":"Test-SIEMLogForwarding","key":"SIEMLogForwarding","check_type":"scored_function","description":"Verifies centralized log collection via WEF or SIEM agent","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-107', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Hardware Asset Inventory (Test-HardwareAssetInventory)', 'command', 'low', N'{"function":"Test-HardwareAssetInventory","key":"HardwareAssetInventory","check_type":"scored_function","description":"Collects hardware details for asset management and compliance tracking","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-108', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Vulnerability Scan Agent (Test-VulnScanAgent)', 'command', 'low', N'{"function":"Test-VulnScanAgent","key":"VulnScanAgent","check_type":"scored_function","description":"Verifies a vulnerability scanning agent is installed and running","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-109', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Capacity Monitoring (Test-CapacityMonitoring)', 'command', 'low', N'{"function":"Test-CapacityMonitoring","key":"CapacityMonitoring","check_type":"scored_function","description":"Reports disk and memory utilization for capacity planning","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-110', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Secure Delete Capability (Test-SecureDeleteCapability)', 'command', 'low', N'{"function":"Test-SecureDeleteCapability","key":"SecureDeleteCapability","check_type":"scored_function","description":"Checks availability of secure file erasure tools for data sanitization","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-111', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Web Filtering / Proxy (Test-WebFilteringProxy)', 'command', 'low', N'{"function":"Test-WebFilteringProxy","key":"WebFilteringProxy","check_type":"scored_function","description":"Checks for active web content filtering via proxy or DNS-based filtering","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-112', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'VPN Configuration (Test-VPNConfiguration)', 'command', 'low', N'{"function":"Test-VPNConfiguration","key":"VPNConfiguration","check_type":"scored_function","description":"Checks for VPN client installation and configured connections","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-113', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Cloud Sync Audit (Test-CloudSyncAudit)', 'command', 'low', N'{"function":"Test-CloudSyncAudit","key":"CloudSyncAudit","check_type":"scored_function","description":"Audits active cloud sync agents and management policy enforcement","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-114', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'PII Data Discovery (Test-PIIDataDiscovery)', 'command', 'low', N'{"function":"Test-PIIDataDiscovery","key":"PIIDataDiscovery","check_type":"scored_function","description":"Scans user directories for files with names suggesting personally identifiable information","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-115', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Patch Age Tracking (Test-PatchAgeTracking)', 'command', 'low', N'{"function":"Test-PatchAgeTracking","key":"PatchAgeTracking","check_type":"scored_function","description":"Evaluates days since last Windows hotfix installation to detect stale patching","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-116', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'File Integrity Monitoring (FIM) (Test-FileIntegrityMonitoring)', 'command', 'low', N'{"function":"Test-FileIntegrityMonitoring","key":"FileIntegrityMonitoring","check_type":"scored_function","description":"Detects presence of file integrity monitoring agents (Sysmon, Wazuh, OSSEC, AppLocker audit)","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-117', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Data Retention Age (Test-DataRetentionAge)', 'command', 'low', N'{"function":"Test-DataRetentionAge","key":"DataRetentionAge","check_type":"scored_function","description":"Identifies stale files in temp and downloads directories indicating poor data retention practices","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-118', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Baseline Drift Detection (Test-BaselineDriftDetection)', 'command', 'low', N'{"function":"Test-BaselineDriftDetection","key":"BaselineDriftDetection","check_type":"scored_function","description":"Validates presence of configuration management or baseline tracking to detect unauthorized changes","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-119', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Failed Logon Analysis (Test-FailedLogonAnalysis)', 'command', 'low', N'{"function":"Test-FailedLogonAnalysis","key":"FailedLogonAnalysis","check_type":"scored_function","description":"Analyzes Security event log for excessive failed logon attempts indicating brute force activity","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-120', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Firewall Rule Quality (Test-FirewallRuleQuality)', 'command', 'low', N'{"function":"Test-FirewallRuleQuality","key":"FirewallRuleQuality","check_type":"scored_function","description":"Identifies overly permissive inbound firewall rules allowing any port from any address","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-121', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Backup Restore Readiness (Test-BackupRestoreTest)', 'command', 'low', N'{"function":"Test-BackupRestoreTest","key":"BackupRestoreTest","check_type":"scored_function","description":"Validates recent backup existence via VSS shadow copies and third-party backup agents","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-122', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'Log Retention Period (Test-LogRetentionPeriod)', 'command', 'low', N'{"function":"Test-LogRetentionPeriod","key":"LogRetentionPeriod","check_type":"scored_function","description":"Validates event log retention mode and size meet compliance requirements for forensic readiness","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-123', (SELECT id FROM control_categories WHERE name=N'Multi-Framework Coverage'), N'USB Write Restriction (Test-USBWriteRestriction)', 'command', 'low', N'{"function":"Test-USBWriteRestriction","key":"USBWriteRestriction","check_type":"scored_function","description":"Validates that write access to removable USB storage is blocked via Group Policy or registry","points":1}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-124', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Lock Screen Personalization Policies (001-002) (Test-BaselinePersonalization)', 'command', 'medium', N'{"function":"Test-BaselinePersonalization","key":"BaselinePersonalization","check_type":"scored_function","description":"Ensures lock screen camera and slideshow are disabled via Group Policy.","points":2}', N'Set HKLM:\Software\Policies\Microsoft\Windows\Personalization: NoLockScreenCamera=1, NoLockScreenSlideshow=1', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-125', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Local Administrator Password Solution / LAPS (003, 057-059) (Test-BaselineLAPS)', 'command', 'critical', N'{"function":"Test-BaselineLAPS","key":"BaselineLAPS","check_type":"scored_function","description":"Verifies LAPS (legacy and Windows LAPS) is enabled and configured.","points":4}', N'Enable Windows LAPS via Intune or GPO. Set BackupDirectory, ADPasswordEncryptionEnabled, and ADBackupDSRMPassword as appropriate.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-126', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MS Security Guide Settings (004-012) (Test-BaselineSecurityGuide)', 'command', 'critical', N'{"function":"Test-BaselineSecurityGuide","key":"BaselineSecurityGuide","check_type":"scored_function","description":"Checks MS Security Guide registry settings including SMBv1, LSA protection, WDigest, and SEHOP.","points":7}', N'Apply MS Security Guide GPO templates or manually set registry values. Key items: disable SMBv1, enable RunAsPPL, disable WDigest.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-127', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MSS Legacy Network Settings (013-018) (Test-BaselineMSSLegacy)', 'command', 'critical', N'{"function":"Test-BaselineMSSLegacy","key":"BaselineMSSLegacy","check_type":"scored_function","description":"Checks MSS (Microsoft Solutions for Security) legacy TCP/IP hardening settings.","points":4}', N'Apply MSS Legacy GPO settings or set registry values manually. Disable IP source routing for IPv4/IPv6, disable ICMP redirect, prevent NetBIOS name release on demand.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-128', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) (Test-BaselineNetworkPolicies)', 'command', 'critical', N'{"function":"Test-BaselineNetworkPolicies","key":"BaselineNetworkPolicies","check_type":"scored_function","description":"Checks SMB signing/encryption audit, LLMNR, NetBIOS, mailslots, WLAN auto-connect, and hardened UNC paths.","points":13}', N'Apply CIS network policy GPO settings. Key items: disable LLMNR/NetBIOS, enable SMB audit settings, configure hardened UNC paths, disable mailslots.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-129', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Printer Hardening Policies (038-044) (Test-BaselinePrinterHardening)', 'command', 'critical', N'{"function":"Test-BaselinePrinterHardening","key":"BaselinePrinterHardening","check_type":"scored_function","description":"Checks printer RPC hardening, redirection guard, Point and Print restrictions, and copy files policy.","points":7}', N'Apply printer hardening GPO settings. Enable RpcAuthnLevelPrivacyEnabled, RedirectionGuardPolicy, restrict Point and Print to admins, and set CopyFilesPolicy=1.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-130', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) (Test-BaselineSystemPolicies)', 'command', 'critical', N'{"function":"Test-BaselineSystemPolicies","key":"BaselineSystemPolicies","check_type":"scored_function","description":"Checks VBS, ELAM, CredSSP, LSA PPL with UEFI lock, DMA protection, command-line auditing, and related system hardening settings.","points":12}', N'Enable VBS via DeviceGuard GPO, set RunAsPPL=2 for UEFI-locked LSA protection, enable process creation auditing, enforce CredSSP oracle remediation, and configure DMA protection policy.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-131', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Logon Policies Baseline (Test-BaselineLogonPolicies)', 'command', 'low', N'{"function":"Test-BaselineLogonPolicies","key":"BaselineLogonPolicies","check_type":"scored_function","description":"Validates logon-related registry policies (controls 062-063, 199-200): local user enumeration, domain PIN logon, MPR, and automatic restart sign-on.","points":0}', N'Set EnumerateLocalUsers=0, AllowDomainPINLogon=0, EnableMPR=0, DisableAutomaticRestartSignOn=1 under HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-132', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Power Management Baseline (Test-BaselinePowerManagement)', 'command', 'low', N'{"function":"Test-BaselinePowerManagement","key":"BaselinePowerManagement","check_type":"scored_function","description":"Validates sleep/wake password policies (controls 064-067): standby disabled and password required on wake for both battery and AC power.","points":0}', N'Configure Power policy GUIDs via GPO or registry: abfc2519... DCSettingIndex=0, ACSettingIndex=0; 0e796bdb... DCSettingIndex=1, ACSettingIndex=1.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-133', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Remote Services Baseline (Test-BaselineRemoteServices)', 'command', 'low', N'{"function":"Test-BaselineRemoteServices","key":"BaselineRemoteServices","check_type":"scored_function","description":"Validates remote service policies (controls 068-069): solicited Remote Assistance disabled and RPC restricted to authenticated clients.","points":0}', N'Set fAllowToGetHelp=0 under Terminal Services policy key. Set RestrictRemoteClients=1 under Rpc policy key.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-134', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'AutoPlay and Device Policies Baseline (Test-BaselineAutoPlayPolicies)', 'command', 'low', N'{"function":"Test-BaselineAutoPlayPolicies","key":"BaselineAutoPlayPolicies","check_type":"scored_function","description":"Validates AutoPlay, AutoRun, biometrics anti-spoofing, BitLocker DMA protection, and removable drive write access policies (controls 073-079).","points":0}', N'Configure via GPO: disable AutoPlay/AutoRun for all drives, enable Enhanced Anti-Spoofing, DisableExternalDMAUnderLock=1, UseEnhancedPin=1, RDVDenyWriteAccess=1.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-135', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Cloud Content and Privacy Baseline (Test-BaselineCloudContent)', 'command', 'low', N'{"function":"Test-BaselineCloudContent","key":"BaselineCloudContent","check_type":"scored_function","description":"Validates cloud content, privacy, and credential UI policies (controls 071-072, 080-081): voice activation above lock, MSA optional, consumer features, and admin enumeration.","points":0}', N'Set LetAppsActivateWithVoiceAboveLock=2, MSAOptional=1, DisableWindowsConsumerFeatures=1, EnumerateAdministrators=0 via GPO or registry.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-136', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Event Log Size Baseline (Test-BaselineEventLog)', 'command', 'low', N'{"function":"Test-BaselineEventLog","key":"BaselineEventLog","check_type":"scored_function","description":"Validates that the System event log maximum size is configured to at least 32768 KB (32 MB) per CIS benchmark (control 082).","points":0}', N'Set HKLM:\SOFTWARE\Policies\Microsoft\Windows\EventLog\System MaxSize to 32768 or greater via GPO (Computer Configuration > Windows Settings > Security Settings > Event Log).', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-137', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Security Options Baseline (Test-BaselineSecurityOptions)', 'command', 'low', N'{"function":"Test-BaselineSecurityOptions","key":"BaselineSecurityOptions","check_type":"scored_function","description":"Batch validation of ~28 security option registry values (controls 215-253): Lsa, Netlogon, LanmanWorkstation/Server, MSV1_0, LDAP, UAC, and session policies.","points":0}', N'Apply CIS Level 1 GPO baseline. Key items: LmCompatibilityLevel=5, EnableLUA=1, NTLMMinClientSec/ServerSec=537395200, RestrictRemoteSAM configured, ConsentPromptBehaviorAdmin=2.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-138', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Password Policy Detailed (Test-BaselinePasswordPolicyDetailed)', 'command', 'low', N'{"function":"Test-BaselinePasswordPolicyDetailed","key":"BaselinePasswordPolicyDetailed","check_type":"scored_function","description":"Validates password policy (controls 211-214) via net accounts and secedit: history \u003e= 24, minimum length \u003e= 14, complexity enabled, reversible encryption disabled.","points":0}', N'Configure via GPO (Computer Configuration > Windows Settings > Security Settings > Account Policies > Password Policy): History=24, MinLength=14, Complexity=Enabled, ReversibleEncryption=Disabled.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-139', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Account Lockout Policy Detailed (Test-BaselineAccountLockoutDetailed)', 'command', 'low', N'{"function":"Test-BaselineAccountLockoutDetailed","key":"BaselineAccountLockoutDetailed","check_type":"scored_function","description":"Validates account lockout policy (controls 207-210) via net accounts and registry: lockout duration \u003e= 15 min, threshold \u003c= $lockoutThresholdMax ($deviceType), observation window \u003e= 15 min, AllowAdministratorLockout = 1.","points":0}', N'Configure via GPO (Account Lockout Policy): Duration >= 15, Threshold <= 5 (servers) or <= 10 (workstations), Observation Window >= 15. Set AllowAdministratorLockout=1 in registry.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-140', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Audit Policy Configuration (Controls 278-301) (Test-BaselineAuditPolicyDetailed)', 'command', 'low', N'{"function":"Test-BaselineAuditPolicyDetailed","key":"BaselineAuditPolicyDetailed","check_type":"scored_function","description":"Validates 24 advanced audit policy subcategories per CIS benchmark.","points":0}', N'Ensure the script runs with administrative privileges and auditpol is available.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-141', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Windows Firewall Profile Configuration (Controls 302-324) (Test-BaselineFirewallDetailed)', 'command', 'low', N'{"function":"Test-BaselineFirewallDetailed","key":"BaselineFirewallDetailed","check_type":"scored_function","description":"Validates firewall settings across Domain, Private, and Public profiles.","points":0}', N'Ensure the Windows Firewall service is running and the script has administrative privileges.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-142', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIERestrictedZone (Test-BaselineIERestrictedZone)', 'command', 'low', N'{"function":"Test-BaselineIERestrictedZone","key":"BaselineIERestrictedZone","check_type":"scored_function","description":"Checks 37 IE Restricted Sites zone (Zone 4) policy registry values (controls 106-142).","points":0}', N'Apply CIS IE Benchmark GPO or manually set all listed DWORD values under HKLM:\Software\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\Zones\4 to their required values.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-143', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity (Test-BaselineIESecurity)', 'command', 'low', N'{"function":"Test-BaselineIESecurity","key":"BaselineIESecurity","check_type":"scored_function","description":"Checks general IE security policy settings across multiple registry paths (controls 084-105, 143-150).","points":0}', N'Apply CIS IE Benchmark GPO settings. Ensure TLS 1.1/1.2 are enabled (SecureProtocols=2688), Protected Mode and SmartScreen are active, and all phishing/certificate override policies are enforced.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-144', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity (Test-BaselineEdgeSecurity)', 'command', 'low', N'{"function":"Test-BaselineEdgeSecurity","key":"BaselineEdgeSecurity","check_type":"scored_function","description":"Checks Microsoft Edge v139 security policy registry settings (controls 325-343).","points":0}', N'Apply CIS Microsoft Edge Benchmark GPO. Ensure SmartScreen, site isolation, application-bound encryption, and extension blocklist policies are enforced via HKLM:\Software\Policies\Microsoft\Edge.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-145', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy (Test-BaselineDefenderPolicy)', 'command', 'low', N'{"function":"Test-BaselineDefenderPolicy","key":"BaselineDefenderPolicy","check_type":"scored_function","description":"Checks Windows Defender GPO-level policy registry values including real-time protection, MAPS, cloud block, and PUA settings (controls 151-178).","points":0}', N'Apply CIS Windows Defender Benchmark GPO. Ensure MAPS (SpynetReporting=2), cloud blocking (MpCloudBlockLevel=2), real-time monitoring, behavior monitoring, and script scanning are all active. Disable DisableRoutinelyTakingAction and DisableBlockAtFirstSeen.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-146', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'RDP Security Configuration (Controls 183-188) (Test-BaselineRDPSecurity)', 'command', 'low', N'{"function":"Test-BaselineRDPSecurity","key":"BaselineRDPSecurity","check_type":"scored_function","description":"Validates RDP session security policies including encryption, password prompting, and drive redirection restrictions.","points":0}', N'Apply via Group Policy: Computer Configuration > Administrative Templates > Windows Components > Remote Desktop Services. Set MinEncryptionLevel=3, enable fPromptForPassword, fEncryptRPCTraffic, DisablePasswordSaving, fDisableCdm. Disable AllowIndexingEncryptedStoresOrItems under Windows Search.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-147', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'SmartScreen and Phishing Protection (Controls 189-194) (Test-BaselineSmartScreenEnhanced)', 'command', 'low', N'{"function":"Test-BaselineSmartScreenEnhanced","key":"BaselineSmartScreenEnhanced","check_type":"scored_function","description":"Validates Windows SmartScreen, phishing protection, and password reuse notifications are enabled and enforced.","points":0}', N'Enable SmartScreen via Group Policy: Computer Configuration > Administrative Templates > Windows Components > Windows Defender SmartScreen. Set ShellSmartScreenLevel to Block. Enable all WTDS phishing notification settings under Windows Defender > Configure password protection.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-148', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Windows Miscellaneous Security Policies (Controls 195-198, 201-206) (Test-BaselineWindowsMisc)', 'command', 'low', N'{"function":"Test-BaselineWindowsMisc","key":"BaselineWindowsMisc","check_type":"scored_function","description":"Validates miscellaneous Windows component security policies including Game DVR, Windows Installer, PowerShell logging, and WinRM authentication settings.","points":0}', N'Apply via Group Policy under Computer Configuration > Administrative Templates. Disable Game DVR, restrict Windows Installer elevation, enable Script Block Logging under PowerShell, and harden WinRM by disabling Basic auth, Digest auth, unencrypted traffic, and RunAs. Configure Windows Ink Workspace to disallow above lock screen.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-149', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Xbox Services Disabled (Controls 508-512) (Test-BaselineXboxServices)', 'command', 'low', N'{"function":"Test-BaselineXboxServices","key":"BaselineXboxServices","check_type":"scored_function","description":"Validates that Xbox-related services and scheduled tasks are disabled to reduce attack surface on non-gaming endpoints.","points":0}', N'Disable Xbox services via Group Policy or PowerShell: Set-Service -Name XboxGipSvc,XblAuthManager,XblGameSave,XblNetworkingSvc -StartupType Disabled. Disable the XblGameSaveTask scheduled task: Disable-ScheduledTask -TaskName XblGameSaveTask.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-150', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'$proto (Test-BaselineTLSHardening)', 'command', 'low', N'{"function":"Test-BaselineTLSHardening","key":"BaselineTLSHardening","check_type":"scored_function","description":"$proto","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-151', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'$item.Name (Test-BaselineBitLockerPolicies)', 'command', 'low', N'{"function":"Test-BaselineBitLockerPolicies","key":"BaselineBitLockerPolicies","check_type":"scored_function","description":"$item.Name","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-152', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'EnableTranscripting (Test-BaselinePowerShellHardening)', 'command', 'low', N'{"function":"Test-BaselinePowerShellHardening","key":"BaselinePowerShellHardening","check_type":"scored_function","description":"EnableTranscripting","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-153', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'WDigest UseLogonCredential (Test-BaselineCredentialProtection)', 'command', 'low', N'{"function":"Test-BaselineCredentialProtection","key":"BaselineCredentialProtection","check_type":"scored_function","description":"WDigest UseLogonCredential","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-154', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'EnableAutoDoh (Test-BaselineDNSOverHTTPS)', 'command', 'low', N'{"function":"Test-BaselineDNSOverHTTPS","key":"BaselineDNSOverHTTPS","check_type":"scored_function","description":"EnableAutoDoh","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-155', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'NoAutoUpdate (Test-BaselineWindowsUpdatePolicy)', 'command', 'low', N'{"function":"Test-BaselineWindowsUpdatePolicy","key":"BaselineWindowsUpdatePolicy","check_type":"scored_function","description":"NoAutoUpdate","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-156', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'AppIDSvc Start=Automatic (Test-BaselineWDACAppLocker)', 'command', 'low', N'{"function":"Test-BaselineWDACAppLocker","key":"BaselineWDACAppLocker","check_type":"scored_function","description":"AppIDSvc Start=Automatic","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-157', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'NoDriveTypeAutoRun (Test-BaselineMiscHardening)', 'command', 'low', N'{"function":"Test-BaselineMiscHardening","key":"BaselineMiscHardening","check_type":"scored_function","description":"NoDriveTypeAutoRun","points":0}', N'See control documentation', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-158', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'M365 Office IE Security Feature Controls (Test-BaselineM365Computer)', 'command', 'low', N'{"function":"Test-BaselineM365Computer","key":"BaselineM365Computer","check_type":"scored_function","description":"Verifies that Internet Explorer feature controls are configured for Office applications under HKLM to reduce attack surface from malicious documents","points":0}', N'Deploy the CIS Microsoft Office 365 benchmark GPO. Enable FEATURE_* registry keys under HKLM:\Software\Policies\Microsoft\Internet Explorer\Main\FeatureControl for excel.exe, winword.exe, and powerpnt.exe. Set Flash CompatibilityFlags to 1024 and enable FEATURE_RESTRICT_LEGACY_JSCRIPT_PER_SECURITY_ZONE.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-159', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'User Rights Assignments (Detailed) (Test-BaselineUserRightsDetailed)', 'command', 'low', N'{"function":"Test-BaselineUserRightsDetailed","key":"BaselineUserRightsDetailed","check_type":"scored_function","description":"Validates 16 critical user privilege assignments against CIS Benchmark Level 1 expected values using secedit export","points":0}', N'Use Group Policy (Computer Configuration > Windows Settings > Security Settings > Local Policies > User Rights Assignment) to remove unexpected principals from sensitive privileges. Privileges such as SeTcbPrivilege and SeCreateTokenPrivilege should have no assignees. Backup and Restore rights should be restricted to Administrators only.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-160', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'M365 Office User Security Policies (Test-BaselineM365UserPolicies)', 'command', 'low', N'{"function":"Test-BaselineM365UserPolicies","key":"BaselineM365UserPolicies","check_type":"scored_function","description":"Validates Office 16.0 macro security, trust center, protected view, file block, and Outlook security settings across all user profiles via HKCU (HKU PSDrive)","points":0}', N'Deploy the CIS Microsoft Office 365 GPO baselines via Group Policy or Intune. Enable VBA macro notification level 3, block macros from internet, require trusted publisher for add-ins, disable trusted locations on network, and configure Outlook object model prompt to Automatically Deny.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SC-161', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'User-Level Windows Policies (Test-BaselineUserPolicies)', 'command', 'low', N'{"function":"Test-BaselineUserPolicies","key":"BaselineUserPolicies","check_type":"scored_function","description":"Validates per-user Windows policy settings: toast notification suppression, third-party content suggestions, and IE password form suggestion - evaluated across all user profiles via HKU","points":0}', N'Deploy via Group Policy User Configuration or Intune: disable NoToastApplicationNotification, DisableThirdPartySuggestions, and set FormSuggest Passwords to "no" under the appropriate user registry paths.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0001', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Local Administrator Password Solution / LAPS (003, 057-059) - AdmPwdEnabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\Software\\Policies\\Microsoft Services\\AdmPwd","parent":"Test-BaselineLAPS","operator":"eq","value_name":"AdmPwdEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0002', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Local Administrator Password Solution / LAPS (003, 057-059) - BackupDirectory', 'registry', 'medium', N'{"expected":"$null","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\LAPS","parent":"Test-BaselineLAPS","operator":"eq","value_name":"BackupDirectory","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0003', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Local Administrator Password Solution / LAPS (003, 057-059) - ADBackupDSRMPassword', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\LAPS","parent":"Test-BaselineLAPS","operator":"eq","value_name":"ADBackupDSRMPassword","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0004', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Local Administrator Password Solution / LAPS (003, 057-059) - ADPasswordEncryptionEnabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\LAPS","parent":"Test-BaselineLAPS","operator":"eq","value_name":"ADPasswordEncryptionEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0005', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MS Security Guide Settings (004-012) - LocalAccountTokenFilterPolicy', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineSecurityGuide","operator":"eq","value_name":"LocalAccountTokenFilterPolicy","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0006', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MS Security Guide Settings (004-012) - Start', 'registry', 'medium', N'{"expected":"4","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\MrxSmb10","parent":"Test-BaselineSecurityGuide","operator":"eq","value_name":"Start","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0007', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MS Security Guide Settings (004-012) - SMB1', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","parent":"Test-BaselineSecurityGuide","operator":"eq","value_name":"SMB1","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0008', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MS Security Guide Settings (004-012) - DisableExceptionChainValidation', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\kernel","parent":"Test-BaselineSecurityGuide","operator":"eq","value_name":"DisableExceptionChainValidation","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0009', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MS Security Guide Settings (004-012) - RunAsPPL', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","parent":"Test-BaselineSecurityGuide","operator":"eq","value_name":"RunAsPPL","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0010', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MS Security Guide Settings (004-012) - NodeType', 'registry', 'medium', N'{"expected":"2","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\NetBT\\Parameters","parent":"Test-BaselineSecurityGuide","operator":"eq","value_name":"NodeType","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0011', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MS Security Guide Settings (004-012) - UseLogonCredential', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\WDigest","parent":"Test-BaselineSecurityGuide","operator":"eq","value_name":"UseLogonCredential","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0012', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MSS Legacy Network Settings (013-018) - DisableIPSourceRouting', 'registry', 'medium', N'{"expected":"2","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip6\\Parameters","parent":"Test-BaselineMSSLegacy","operator":"eq","value_name":"DisableIPSourceRouting","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0013', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MSS Legacy Network Settings (013-018) - DisableIPSourceRouting', 'registry', 'medium', N'{"expected":"2","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters","parent":"Test-BaselineMSSLegacy","operator":"eq","value_name":"DisableIPSourceRouting","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0014', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MSS Legacy Network Settings (013-018) - EnableICMPRedirect', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters","parent":"Test-BaselineMSSLegacy","operator":"eq","value_name":"EnableICMPRedirect","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0015', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MSS Legacy Network Settings (013-018) - NoNameReleaseOnDemand', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Netbt\\Parameters","parent":"Test-BaselineMSSLegacy","operator":"eq","value_name":"NoNameReleaseOnDemand","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0016', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - EnableNetbios', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"EnableNetbios","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0017', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - EnableMulticast', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"EnableMulticast","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0018', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - AuditClientDoesNotSupportEncryption', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"AuditClientDoesNotSupportEncryption","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0019', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - AuditClientDoesNotSupportSigning', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"AuditClientDoesNotSupportSigning","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0020', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - EnableAuthRateLimiter', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"EnableAuthRateLimiter","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0021', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - AuditInsecureGuestLogon', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\LanmanWorkstation","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"AuditInsecureGuestLogon","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0022', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - AuditServerDoesNotSupportEncryption', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\LanmanWorkstation","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"AuditServerDoesNotSupportEncryption","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0023', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - AuditServerDoesNotSupportSigning', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\LanmanWorkstation","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"AuditServerDoesNotSupportSigning","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0024', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - AllowInsecureGuestAuth', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"AllowInsecureGuestAuth","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0025', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - EnableMailslots', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\NetworkProvider","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"EnableMailslots","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0026', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - $null', 'registry', 'medium', N'{"expected":"$null","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\NetworkProvider\\HardenedPaths","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"$null","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0027', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - AutoConnectAllowedOEM', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WlanSvc\\GPSvcGroup","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"AutoConnectAllowedOEM","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0028', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Network Hardening Policies (019-037) - fBlockNonDomain', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WcmSvc\\GroupPolicy","parent":"Test-BaselineNetworkPolicies","operator":"eq","value_name":"fBlockNonDomain","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0029', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Printer Hardening Policies (038-044) - RpcUseNamedPipeProtocol', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\RPC","parent":"Test-BaselinePrinterHardening","operator":"eq","value_name":"RpcUseNamedPipeProtocol","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0030', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Printer Hardening Policies (038-044) - RpcProtocols', 'registry', 'medium', N'{"expected":"5","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\RPC","parent":"Test-BaselinePrinterHardening","operator":"eq","value_name":"RpcProtocols","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0031', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Printer Hardening Policies (038-044) - RpcTcpPort', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\RPC","parent":"Test-BaselinePrinterHardening","operator":"eq","value_name":"RpcTcpPort","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0032', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Printer Hardening Policies (038-044) - RpcAuthnLevelPrivacyEnabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers","parent":"Test-BaselinePrinterHardening","operator":"eq","value_name":"RpcAuthnLevelPrivacyEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0033', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Printer Hardening Policies (038-044) - RedirectionGuardPolicy', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers","parent":"Test-BaselinePrinterHardening","operator":"eq","value_name":"RedirectionGuardPolicy","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0034', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Printer Hardening Policies (038-044) - RestrictDriverInstallationToAdmins', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\PointAndPrint","parent":"Test-BaselinePrinterHardening","operator":"eq","value_name":"RestrictDriverInstallationToAdmins","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0035', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Printer Hardening Policies (038-044) - CopyFilesPolicy', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers","parent":"Test-BaselinePrinterHardening","operator":"eq","value_name":"CopyFilesPolicy","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0036', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - Enabled', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Sudo","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"Enabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0037', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - ProcessCreationIncludeCmdLine_Enabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\\Audit","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"ProcessCreationIncludeCmdLine_Enabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0038', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - AllowEncryptionOracle', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\\CredSSP\\Parameters","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"AllowEncryptionOracle","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0039', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - AllowProtectedCreds', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\CredentialsDelegation","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"AllowProtectedCreds","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0040', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - EnableVirtualizationBasedSecurity', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DeviceGuard","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"EnableVirtualizationBasedSecurity","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0041', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - DenyDeviceClasses', 'registry', 'medium', N'{"expected":"$null","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DeviceInstall\\Restrictions","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"DenyDeviceClasses","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0042', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - DriverLoadPolicy', 'registry', 'medium', N'{"expected":"3","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Policies\\EarlyLaunch","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"DriverLoadPolicy","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0043', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - NoWebServices', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"NoWebServices","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0044', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - DisableWebPnPDownload', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"DisableWebPnPDownload","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0045', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - DeviceEnumerationPolicy', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\Kernel DMA Protection","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"DeviceEnumerationPolicy","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0046', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - AllowCustomSSPsAPs', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"AllowCustomSSPsAPs","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0047', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System Security Policies (045-056) - RunAsPPL', 'registry', 'medium', N'{"expected":"2","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","parent":"Test-BaselineSystemPolicies","operator":"eq","value_name":"RunAsPPL","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0048', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Logon Policies Baseline - EnumerateLocalUsers', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineLogonPolicies","operator":"eq","value_name":"EnumerateLocalUsers","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0049', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Logon Policies Baseline - AllowDomainPINLogon', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineLogonPolicies","operator":"eq","value_name":"AllowDomainPINLogon","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0050', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Logon Policies Baseline - EnableMPR', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineLogonPolicies","operator":"eq","value_name":"EnableMPR","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0051', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Logon Policies Baseline - DisableAutomaticRestartSignOn', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineLogonPolicies","operator":"eq","value_name":"DisableAutomaticRestartSignOn","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0052', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Standby disabled on battery (DC)', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Power\\PowerSettings\\abfc2519-3608-4c2a-94ea-171b0ed546ab","parent":"Test-BaselinePowerManagement","operator":"eq","value_name":"DCSettingIndex","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0053', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Standby disabled plugged in (AC)', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Power\\PowerSettings\\abfc2519-3608-4c2a-94ea-171b0ed546ab","parent":"Test-BaselinePowerManagement","operator":"eq","value_name":"ACSettingIndex","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0054', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Require password on wake battery (DC)', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Power\\PowerSettings\\0e796bdb-100d-47d6-a2d5-f7d2daa51f51","parent":"Test-BaselinePowerManagement","operator":"eq","value_name":"DCSettingIndex","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0055', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Require password on wake plugged in (AC)', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Power\\PowerSettings\\0e796bdb-100d-47d6-a2d5-f7d2daa51f51","parent":"Test-BaselinePowerManagement","operator":"eq","value_name":"ACSettingIndex","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0056', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Solicited Remote Assistance disabled', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","parent":"Test-BaselineRemoteServices","operator":"eq","value_name":"fAllowToGetHelp","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0057', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'RPC restrict remote clients (authenticated)', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\Rpc","parent":"Test-BaselineRemoteServices","operator":"eq","value_name":"RestrictRemoteClients","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0058', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'AutoPlay disabled for non-volume devices', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\Explorer","parent":"Test-BaselineAutoPlayPolicies","operator":"eq","value_name":"NoAutoplayfornonVolume","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0059', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'AutoRun disabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\Explorer","parent":"Test-BaselineAutoPlayPolicies","operator":"eq","value_name":"NoAutorun","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0060', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'AutoRun disabled for all drive types (255)', 'registry', 'medium', N'{"expected":"255","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer","parent":"Test-BaselineAutoPlayPolicies","operator":"eq","value_name":"NoDriveTypeAutoRun","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0061', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Enhanced Anti-Spoofing for biometrics enabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Biometrics\\FacialFeatures","parent":"Test-BaselineAutoPlayPolicies","operator":"eq","value_name":"EnhancedAntiSpoofing","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0062', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'External DMA blocked under lock', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\FVE","parent":"Test-BaselineAutoPlayPolicies","operator":"eq","value_name":"DisableExternalDMAUnderLock","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0063', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'BitLocker enhanced PIN enabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\FVE","parent":"Test-BaselineAutoPlayPolicies","operator":"eq","value_name":"UseEnhancedPin","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0064', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Removable drives deny write access', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\FVE","parent":"Test-BaselineAutoPlayPolicies","operator":"eq","value_name":"RDVDenyWriteAccess","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0065', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Voice activation above lock screen force-denied', 'registry', 'medium', N'{"expected":"2","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\AppPrivacy","parent":"Test-BaselineCloudContent","operator":"eq","value_name":"LetAppsActivateWithVoiceAboveLock","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0066', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Microsoft Account optional for store apps', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\MicrosoftAccount","parent":"Test-BaselineCloudContent","operator":"eq","value_name":"MSAOptional","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0067', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Windows consumer features disabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent","parent":"Test-BaselineCloudContent","operator":"eq","value_name":"DisableWindowsConsumerFeatures","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0068', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Elevation prompt does not enumerate administrators', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\CredUI","parent":"Test-BaselineCloudContent","operator":"eq","value_name":"EnumerateAdministrators","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0069', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Lsa LimitBlankPasswordUse', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"LimitBlankPasswordUse","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0070', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Lsa SCENoApplyLegacyAuditPolicy', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"SCENoApplyLegacyAuditPolicy","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0071', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Netlogon RequireSignOrSeal', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Netlogon\\Parameters","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"RequireSignOrSeal","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0072', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Netlogon SealSecureChannel', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Netlogon\\Parameters","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"SealSecureChannel","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0073', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Netlogon SignSecureChannel', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Netlogon\\Parameters","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"SignSecureChannel","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0074', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Netlogon RequireStrongKey', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Netlogon\\Parameters","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"RequireStrongKey","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0075', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System InactivityTimeoutSecs (<= 900)', 'registry', 'medium', N'{"expected":"900","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineSecurityOptions","operator":"lte","value_name":"InactivityTimeoutSecs","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0076', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Winlogon ScRemoveOption (lock on removal)', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"ScRemoveOption","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0077', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'LanmanWorkstation RequireSecuritySignature', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"RequireSecuritySignature","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0078', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'LanmanWorkstation EnablePlainTextPassword', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"EnablePlainTextPassword","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0079', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'LanmanServer RequireSecuritySignature', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"RequireSecuritySignature","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0080', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Lsa RestrictAnonymousSAM', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"RestrictAnonymousSAM","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0081', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Lsa RestrictNullSessAccess', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"RestrictNullSessAccess","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0082', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Lsa RestrictRemoteSAM', 'registry', 'medium', N'{"expected":"O:BAG:BAD:(A;;RC;;;BA)","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"RestrictRemoteSAM","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0083', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MSV1_0 AllowNullSessionFallback', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"AllowNullSessionFallback","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0084', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Lsa NoLMHash', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"NoLMHash","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0085', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Lsa LmCompatibilityLevel (NTLMv2 only)', 'registry', 'medium', N'{"expected":"5","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"LmCompatibilityLevel","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0086', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'LDAP LDAPClientIntegrity', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\LDAP","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"LDAPClientIntegrity","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0087', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MSV1_0 NTLMMinClientSec', 'registry', 'medium', N'{"expected":"537395200","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"NTLMMinClientSec","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0088', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'MSV1_0 NTLMMinServerSec', 'registry', 'medium', N'{"expected":"537395200","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"NTLMMinServerSec","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0089', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Session Manager ProtectionMode', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"ProtectionMode","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0090', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System FilterAdministratorToken', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"FilterAdministratorToken","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0091', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System ConsentPromptBehaviorAdmin (prompt on secure desktop)', 'registry', 'medium', N'{"expected":"2","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"ConsentPromptBehaviorAdmin","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0092', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System ConsentPromptBehaviorUser (auto-deny)', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"ConsentPromptBehaviorUser","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0093', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System EnableInstallerDetection', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"EnableInstallerDetection","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0094', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System EnableSecureUIAPaths', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"EnableSecureUIAPaths","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0095', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System EnableLUA (UAC)', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"EnableLUA","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0096', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'System EnableVirtualization', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","parent":"Test-BaselineSecurityOptions","operator":"eq","value_name":"EnableVirtualization","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0097', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - DisableInternetExplorerLaunchViaCOM', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseIE\\Main","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"DisableInternetExplorerLaunchViaCOM","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0098', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - PreventOverride', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseIE\\PhishingFilter","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"PreventOverride","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0099', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - PreventOverrideAppRepUnknown', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseIE\\PhishingFilter","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"PreventOverrideAppRepUnknown","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0100', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - EnabledV9', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseIE\\PhishingFilter","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"EnabledV9","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0101', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - BlockNonAdminActiveXInstall', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseIE\\ActiveX","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"BlockNonAdminActiveXInstall","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0102', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - Security_zones_map_edit', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseWin\\CurrentVersion\\Internet Settings","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"Security_zones_map_edit","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0103', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - Security_options_edit', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseWin\\CurrentVersion\\Internet Settings","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"Security_options_edit","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0104', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - Security_HKLM_only', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseWin\\CurrentVersion\\Internet Settings","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"Security_HKLM_only","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0105', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - OnlyUseAXISForActiveXInstall', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseIE\\AxInstaller","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"OnlyUseAXISForActiveXInstall","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0106', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - NoCrashDetection', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseIE\\Restrictions","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"NoCrashDetection","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0107', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - DisableSecuritySettingsCheck', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseIE\\Security","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"DisableSecuritySettingsCheck","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0108', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - PreventIgnoreCertErrors', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseWin\\CurrentVersion\\Internet Settings","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"PreventIgnoreCertErrors","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0109', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - RunInvalidSignatures', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseIE\\Download","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"RunInvalidSignatures","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0110', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - CertificateRevocation', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseWin\\CurrentVersion\\Internet Settings","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"CertificateRevocation","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0111', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - CheckExeSignatures', 'registry', 'medium', N'{"expected":"yes","hive":"HKLM","path":"$baseIE\\Download","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"CheckExeSignatures","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0112', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - DisableEPMCompat', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseIE\\Main","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"DisableEPMCompat","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0113', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - SecureProtocols', 'registry', 'medium', N'{"expected":"2688","hive":"HKLM","path":"$baseWin\\CurrentVersion\\Internet Settings","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"SecureProtocols","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0114', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - Isolation64Bit', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseIE\\Main","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"Isolation64Bit","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0115', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - Isolation', 'registry', 'medium', N'{"expected":"PMEM","hive":"HKLM","path":"$baseIE\\Main","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"Isolation","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0116', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - UNCAsIntranet', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseIE\\ZoneMap","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"UNCAsIntranet","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0117', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - WarnOnBadCertRecving', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseWin\\CurrentVersion\\Internet Settings","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"WarnOnBadCertRecving","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0118', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - 270C', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseWin\\CurrentVersion\\Internet Settings\\Zones\\2","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"270C","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0119', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - 1201', 'registry', 'medium', N'{"expected":"3","hive":"HKLM","path":"$baseWin\\CurrentVersion\\Internet Settings\\Zones\\2","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"1201","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0120', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - 1C00', 'registry', 'medium', N'{"expected":"65536","hive":"HKLM","path":"$baseWin\\CurrentVersion\\Internet Settings\\Zones\\2","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"1C00","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0121', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - RunThisTimeEnabled', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseIE\\Ext","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"RunThisTimeEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0122', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineIESecurity - VersionCheckEnabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseIE\\Ext","parent":"Test-BaselineIESecurity","operator":"eq","value_name":"VersionCheckEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0123', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - EnableUnsafeSwiftShader', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"EnableUnsafeSwiftShader","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0124', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - InternetExplorerIntegrationReloadInIEModeAllowed', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"InternetExplorerIntegrationReloadInIEModeAllowed","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0125', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - SSLErrorOverrideAllowed', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"SSLErrorOverrideAllowed","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0126', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - InternetExplorerIntegrationZoneIdentifierMhtFileAllowed', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"InternetExplorerIntegrationZoneIdentifierMhtFileAllowed","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0127', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - DynamicCodeSettings', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"DynamicCodeSettings","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0128', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - ApplicationBoundEncryptionEnabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"ApplicationBoundEncryptionEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0129', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - BrowserLegacyExtensionPointsBlockingEnabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"BrowserLegacyExtensionPointsBlockingEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0130', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - SitePerProcess', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"SitePerProcess","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0131', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - InternetExplorerModeToolbarButtonEnabled', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"InternetExplorerModeToolbarButtonEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0132', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - SharedArrayBufferUnrestrictedAccessAllowed', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"SharedArrayBufferUnrestrictedAccessAllowed","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0133', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - TyposquattingCheckerEnabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseEdge\\Recommended","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"TyposquattingCheckerEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0134', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - BasicAuthOverHttpEnabled', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"BasicAuthOverHttpEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0135', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - AuthSchemes', 'registry', 'medium', N'{"expected":"ntlm,negotiate","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"AuthSchemes","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0136', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - NativeMessagingUserLevelHosts', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"NativeMessagingUserLevelHosts","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0137', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - SmartScreenEnabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"SmartScreenEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0138', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - SmartScreenPuaEnabled', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"SmartScreenPuaEnabled","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0139', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - PreventSmartScreenPromptOverride', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"PreventSmartScreenPromptOverride","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0140', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineEdgeSecurity - PreventSmartScreenPromptOverrideForFiles', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseEdge","parent":"Test-BaselineEdgeSecurity","operator":"eq","value_name":"PreventSmartScreenPromptOverrideForFiles","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0141', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - PUAProtection', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseDefender","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"PUAProtection","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0142', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - DisableLocalAdminMerge', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseDefender","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"DisableLocalAdminMerge","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0143', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - HideExclusionsFromLocalAdmins', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseDefender","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"HideExclusionsFromLocalAdmins","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0144', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - DisableRoutinelyTakingAction', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseDefender","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"DisableRoutinelyTakingAction","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0145', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - PassiveRemediation', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseDefender\\Features","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"PassiveRemediation","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0146', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - DisableBlockAtFirstSeen', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseDefender\\Spynet","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"DisableBlockAtFirstSeen","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0147', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - SpynetReporting', 'registry', 'medium', N'{"expected":"2","hive":"HKLM","path":"$baseDefender\\Spynet","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"SpynetReporting","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0148', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - SubmitSamplesConsent', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseDefender\\Spynet","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"SubmitSamplesConsent","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0149', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - MpCloudBlockLevel', 'registry', 'medium', N'{"expected":"2","hive":"HKLM","path":"$baseDefender\\MpEngine","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"MpCloudBlockLevel","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0150', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - EnableConvertWarnToBlock', 'registry', 'medium', N'{"expected":"1","hive":"HKLM","path":"$baseDefender\\NIS","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"EnableConvertWarnToBlock","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0151', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - DisableIOAVProtection', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseDefender\\Real-Time Protection","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"DisableIOAVProtection","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0152', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - DisableRealtimeMonitoring', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseDefender\\Real-Time Protection","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"DisableRealtimeMonitoring","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0153', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - DisableBehaviorMonitoring', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseDefender\\Real-Time Protection","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"DisableBehaviorMonitoring","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0154', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - DisableScriptScanning', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseDefender\\Real-Time Protection","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"DisableScriptScanning","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0155', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Test-BaselineDefenderPolicy - DisableRemovableDriveScanning', 'registry', 'medium', N'{"expected":"0","hive":"HKLM","path":"$baseDefender\\Scan","parent":"Test-BaselineDefenderPolicy","operator":"eq","value_name":"DisableRemovableDriveScanning","check_type":"registry"}', N'Configure via GPO / registry policy. See CIS/Microsoft baseline.', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0156', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Lock screen camera disabled', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\Personalization","value_name":"NoLockScreenCamera","expected":1,"operator":"eq","label":"Lock screen camera disabled","parent":"Test-BaselinePersonalization"}', N'See parent function: Test-BaselinePersonalization', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0157', (SELECT id FROM control_categories WHERE name=N'Windows Security Baseline'), N'Lock screen slideshow disabled', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\Personalization","value_name":"NoLockScreenSlideshow","expected":1,"operator":"eq","label":"Lock screen slideshow disabled","parent":"Test-BaselinePersonalization"}', N'See parent function: Test-BaselinePersonalization', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0158', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'System Event Log MaxSize >= 32768 KB', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\EventLog\\System","value_name":"MaxSize","expected":32768,"operator":"gte","label":"System Event Log MaxSize \u003e= 32768 KB","parent":"Test-BaselineEventLog"}', N'See parent function: Test-BaselineEventLog', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0159', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'Password history length >= 24', 'netaccount', 'high', N'{"check_type":"net_accounts","field":"PasswordHistoryLength","expected":24,"operator":"gte","label":"Password history length \u003e= 24","parent":"Test-BaselinePasswordPolicyDetailed"}', N'See parent function: Test-BaselinePasswordPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0160', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'Minimum password length >= 14', 'netaccount', 'high', N'{"check_type":"net_accounts","field":"MinimumPasswordLength","expected":14,"operator":"gte","label":"Minimum password length \u003e= 14","parent":"Test-BaselinePasswordPolicyDetailed"}', N'See parent function: Test-BaselinePasswordPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0161', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'Password complexity enabled', 'secedit', 'high', N'{"check_type":"secedit","setting_name":"PasswordComplexity","expected":1,"operator":"eq","label":"Password complexity enabled","parent":"Test-BaselinePasswordPolicyDetailed"}', N'See parent function: Test-BaselinePasswordPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0162', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'Reversible password encryption disabled', 'secedit', 'high', N'{"check_type":"secedit","setting_name":"ClearTextPassword","expected":0,"operator":"eq","label":"Reversible password encryption disabled","parent":"Test-BaselinePasswordPolicyDetailed"}', N'See parent function: Test-BaselinePasswordPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0163', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'Lockout duration >= 15 minutes', 'netaccount', 'high', N'{"check_type":"net_accounts","field":"LockoutDuration","expected":15,"operator":"gte","label":"Lockout duration \u003e= 15 minutes","parent":"Test-BaselineAccountLockoutDetailed"}', N'See parent function: Test-BaselineAccountLockoutDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0164', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'Lockout threshold <= 10 (workstation; <=5 on server)', 'netaccount', 'high', N'{"check_type":"net_accounts","field":"LockoutThreshold","expected":10,"operator":"lte","label":"Lockout threshold \u003c= 10 (workstation; \u003c=5 on server)","parent":"Test-BaselineAccountLockoutDetailed"}', N'See parent function: Test-BaselineAccountLockoutDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0165', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'Lockout observation window >= 15 minutes', 'netaccount', 'high', N'{"check_type":"net_accounts","field":"LockoutObservationWindow","expected":15,"operator":"gte","label":"Lockout observation window \u003e= 15 minutes","parent":"Test-BaselineAccountLockoutDetailed"}', N'See parent function: Test-BaselineAccountLockoutDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0166', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'AllowAdministratorLockout = 1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","value_name":"AllowAdministratorLockout","expected":1,"operator":"eq","label":"AllowAdministratorLockout = 1","parent":"Test-BaselineAccountLockoutDetailed"}', N'See parent function: Test-BaselineAccountLockoutDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0167', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Credential Validation', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"Credential Validation","expected":"Success and Failure","operator":"eq","label":"Audit Credential Validation","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0168', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Security Group Management', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"Security Group Management","expected":"Success","operator":"eq","label":"Audit Security Group Management","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0169', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit User Account Management', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"User Account Management","expected":"Success and Failure","operator":"eq","label":"Audit User Account Management","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0170', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Plug and Play Events', 'auditpol', 'medium', N'{"check_type":"auditpol","subcategory":"Plug and Play Events","expected":"Success","operator":"eq","label":"Audit Plug and Play Events","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0171', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Process Creation', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"Process Creation","expected":"Success","operator":"eq","label":"Audit Process Creation","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0172', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Account Lockout', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"Account Lockout","expected":"Failure","operator":"eq","label":"Audit Account Lockout","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0173', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Group Membership', 'auditpol', 'medium', N'{"check_type":"auditpol","subcategory":"Group Membership","expected":"Success","operator":"eq","label":"Audit Group Membership","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0174', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Logon', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"Logon","expected":"Success and Failure","operator":"eq","label":"Audit Logon","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0175', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Other Logon/Logoff Events', 'auditpol', 'medium', N'{"check_type":"auditpol","subcategory":"Other Logon/Logoff Events","expected":"Success and Failure","operator":"eq","label":"Audit Other Logon/Logoff Events","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0176', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Special Logon', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"Special Logon","expected":"Success","operator":"eq","label":"Audit Special Logon","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0177', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Detailed File Share', 'auditpol', 'medium', N'{"check_type":"auditpol","subcategory":"Detailed File Share","expected":"Failure","operator":"eq","label":"Audit Detailed File Share","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0178', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit File Share', 'auditpol', 'medium', N'{"check_type":"auditpol","subcategory":"File Share","expected":"Success and Failure","operator":"eq","label":"Audit File Share","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0179', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Other Object Access Events', 'auditpol', 'medium', N'{"check_type":"auditpol","subcategory":"Other Object Access Events","expected":"Success and Failure","operator":"eq","label":"Audit Other Object Access Events","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0180', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Removable Storage', 'auditpol', 'medium', N'{"check_type":"auditpol","subcategory":"Removable Storage","expected":"Success and Failure","operator":"eq","label":"Audit Removable Storage","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0181', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Audit Policy Change', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"Audit Policy Change","expected":"Success","operator":"eq","label":"Audit Audit Policy Change","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0182', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Authentication Policy Change', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"Authentication Policy Change","expected":"Success","operator":"eq","label":"Audit Authentication Policy Change","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0183', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Authorization Policy Change', 'auditpol', 'medium', N'{"check_type":"auditpol","subcategory":"Authorization Policy Change","expected":"Success","operator":"eq","label":"Audit Authorization Policy Change","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0184', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit MPSSVC Rule-Level Policy Change', 'auditpol', 'medium', N'{"check_type":"auditpol","subcategory":"MPSSVC Rule-Level Policy Change","expected":"Success and Failure","operator":"eq","label":"Audit MPSSVC Rule-Level Policy Change","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0185', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Other Policy Change Events', 'auditpol', 'low', N'{"check_type":"auditpol","subcategory":"Other Policy Change Events","expected":"Failure","operator":"eq","label":"Audit Other Policy Change Events","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0186', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Sensitive Privilege Use', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"Sensitive Privilege Use","expected":"Success","operator":"eq","label":"Audit Sensitive Privilege Use","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0187', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Other System Events', 'auditpol', 'low', N'{"check_type":"auditpol","subcategory":"Other System Events","expected":"Success and Failure","operator":"eq","label":"Audit Other System Events","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0188', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Security State Change', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"Security State Change","expected":"Success","operator":"eq","label":"Audit Security State Change","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0189', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit Security System Extension', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"Security System Extension","expected":"Success","operator":"eq","label":"Audit Security System Extension","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0190', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'Audit System Integrity', 'auditpol', 'high', N'{"check_type":"auditpol","subcategory":"System Integrity","expected":"Success and Failure","operator":"eq","label":"Audit System Integrity","parent":"Test-BaselineAuditPolicyDetailed"}', N'See parent function: Test-BaselineAuditPolicyDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0191', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Domain firewall profile enabled', 'firewall', 'critical', N'{"check_type":"firewall","profile":"Domain","property":"Enabled","expected":true,"operator":"eq","label":"Domain firewall profile enabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0192', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Domain default inbound = Block', 'firewall', 'high', N'{"check_type":"firewall","profile":"Domain","property":"DefaultInboundAction","expected":"Block","operator":"eq","label":"Domain default inbound = Block","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0193', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Domain default outbound = Allow', 'firewall', 'low', N'{"check_type":"firewall","profile":"Domain","property":"DefaultOutboundAction","expected":"Allow","operator":"eq","label":"Domain default outbound = Allow","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0194', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Domain NotifyOnListen disabled', 'firewall', 'low', N'{"check_type":"firewall","profile":"Domain","property":"NotifyOnListen","expected":false,"operator":"eq","label":"Domain NotifyOnListen disabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0195', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Domain log size >= 16384 KB', 'firewall', 'medium', N'{"check_type":"firewall","profile":"Domain","property":"LogMaxSizeKilobytes","expected":16384,"operator":"gte","label":"Domain log size \u003e= 16384 KB","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0196', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Domain LogBlocked enabled', 'firewall', 'medium', N'{"check_type":"firewall","profile":"Domain","property":"LogBlocked","expected":true,"operator":"eq","label":"Domain LogBlocked enabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0197', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Domain LogAllowed enabled', 'firewall', 'medium', N'{"check_type":"firewall","profile":"Domain","property":"LogAllowed","expected":true,"operator":"eq","label":"Domain LogAllowed enabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0198', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Private firewall profile enabled', 'firewall', 'critical', N'{"check_type":"firewall","profile":"Private","property":"Enabled","expected":true,"operator":"eq","label":"Private firewall profile enabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0199', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Private default inbound = Block', 'firewall', 'high', N'{"check_type":"firewall","profile":"Private","property":"DefaultInboundAction","expected":"Block","operator":"eq","label":"Private default inbound = Block","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0200', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Private default outbound = Allow', 'firewall', 'low', N'{"check_type":"firewall","profile":"Private","property":"DefaultOutboundAction","expected":"Allow","operator":"eq","label":"Private default outbound = Allow","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0201', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Private NotifyOnListen disabled', 'firewall', 'low', N'{"check_type":"firewall","profile":"Private","property":"NotifyOnListen","expected":false,"operator":"eq","label":"Private NotifyOnListen disabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0202', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Private log size >= 16384 KB', 'firewall', 'medium', N'{"check_type":"firewall","profile":"Private","property":"LogMaxSizeKilobytes","expected":16384,"operator":"gte","label":"Private log size \u003e= 16384 KB","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0203', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Private LogBlocked enabled', 'firewall', 'medium', N'{"check_type":"firewall","profile":"Private","property":"LogBlocked","expected":true,"operator":"eq","label":"Private LogBlocked enabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0204', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Private LogAllowed enabled', 'firewall', 'medium', N'{"check_type":"firewall","profile":"Private","property":"LogAllowed","expected":true,"operator":"eq","label":"Private LogAllowed enabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0205', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Public firewall profile enabled', 'firewall', 'critical', N'{"check_type":"firewall","profile":"Public","property":"Enabled","expected":true,"operator":"eq","label":"Public firewall profile enabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0206', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Public default inbound = Block', 'firewall', 'critical', N'{"check_type":"firewall","profile":"Public","property":"DefaultInboundAction","expected":"Block","operator":"eq","label":"Public default inbound = Block","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0207', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Public default outbound = Allow', 'firewall', 'low', N'{"check_type":"firewall","profile":"Public","property":"DefaultOutboundAction","expected":"Allow","operator":"eq","label":"Public default outbound = Allow","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0208', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Public NotifyOnListen disabled', 'firewall', 'low', N'{"check_type":"firewall","profile":"Public","property":"NotifyOnListen","expected":false,"operator":"eq","label":"Public NotifyOnListen disabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0209', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Public log size >= 16384 KB', 'firewall', 'medium', N'{"check_type":"firewall","profile":"Public","property":"LogMaxSizeKilobytes","expected":16384,"operator":"gte","label":"Public log size \u003e= 16384 KB","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0210', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Public LogBlocked enabled', 'firewall', 'medium', N'{"check_type":"firewall","profile":"Public","property":"LogBlocked","expected":true,"operator":"eq","label":"Public LogBlocked enabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0211', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Public LogAllowed enabled', 'firewall', 'medium', N'{"check_type":"firewall","profile":"Public","property":"LogAllowed","expected":true,"operator":"eq","label":"Public LogAllowed enabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0212', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Public profile AllowLocalPolicyMerge disabled', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\WindowsFirewall\\PublicProfile","value_name":"AllowLocalPolicyMerge","expected":0,"operator":"eq","label":"Public profile AllowLocalPolicyMerge disabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0213', (SELECT id FROM control_categories WHERE name=N'Firewall'), N'Public profile AllowLocalIPsecPolicyMerge disabled', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\WindowsFirewall\\PublicProfile","value_name":"AllowLocalIPsecPolicyMerge","expected":0,"operator":"eq","label":"Public profile AllowLocalIPsecPolicyMerge disabled","parent":"Test-BaselineFirewallDetailed"}', N'See parent function: Test-BaselineFirewallDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0214', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1406=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1406","expected":3,"operator":"eq","label":"IE Zone4 1406=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0215', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1608=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1608","expected":3,"operator":"eq","label":"IE Zone4 1608=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0216', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 140C=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"140C","expected":3,"operator":"eq","label":"IE Zone4 140C=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0217', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1400=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1400","expected":3,"operator":"eq","label":"IE Zone4 1400=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0218', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2000=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2000","expected":3,"operator":"eq","label":"IE Zone4 2000=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0219', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1407=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1407","expected":3,"operator":"eq","label":"IE Zone4 1407=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0220', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1802=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1802","expected":3,"operator":"eq","label":"IE Zone4 1802=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0221', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1803=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1803","expected":3,"operator":"eq","label":"IE Zone4 1803=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0222', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2402=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2402","expected":3,"operator":"eq","label":"IE Zone4 2402=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0223', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 120b=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"120b","expected":3,"operator":"eq","label":"IE Zone4 120b=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0224', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 120c=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"120c","expected":3,"operator":"eq","label":"IE Zone4 120c=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0225', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2102=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2102","expected":3,"operator":"eq","label":"IE Zone4 2102=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0226', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1206=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1206","expected":3,"operator":"eq","label":"IE Zone4 1206=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0227', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1209=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1209","expected":3,"operator":"eq","label":"IE Zone4 1209=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0228', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2103=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2103","expected":3,"operator":"eq","label":"IE Zone4 2103=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0229', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2200=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2200","expected":3,"operator":"eq","label":"IE Zone4 2200=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0230', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1001=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1001","expected":3,"operator":"eq","label":"IE Zone4 1001=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0231', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1004=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1004","expected":3,"operator":"eq","label":"IE Zone4 1004=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0232', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2709=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2709","expected":3,"operator":"eq","label":"IE Zone4 2709=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0233', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2708=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2708","expected":3,"operator":"eq","label":"IE Zone4 2708=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0234', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 160A=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"160A","expected":3,"operator":"eq","label":"IE Zone4 160A=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0235', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1804=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1804","expected":3,"operator":"eq","label":"IE Zone4 1804=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0236', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1A00=65536', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1A00","expected":65536,"operator":"eq","label":"IE Zone4 1A00=65536","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0237', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1607=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1607","expected":3,"operator":"eq","label":"IE Zone4 1607=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0238', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2004=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2004","expected":3,"operator":"eq","label":"IE Zone4 2004=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0239', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2001=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2001","expected":3,"operator":"eq","label":"IE Zone4 2001=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0240', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1200=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1200","expected":3,"operator":"eq","label":"IE Zone4 1200=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0241', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1405=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1405","expected":3,"operator":"eq","label":"IE Zone4 1405=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0242', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1402=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1402","expected":3,"operator":"eq","label":"IE Zone4 1402=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0243', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1806=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1806","expected":1,"operator":"eq","label":"IE Zone4 1806=1","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0244', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1409=0', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1409","expected":0,"operator":"eq","label":"IE Zone4 1409=0","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0245', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2500=0', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2500","expected":0,"operator":"eq","label":"IE Zone4 2500=0","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0246', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2301=0', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2301","expected":0,"operator":"eq","label":"IE Zone4 2301=0","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0247', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1809=0', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1809","expected":0,"operator":"eq","label":"IE Zone4 1809=0","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0248', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 1606=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"1606","expected":3,"operator":"eq","label":"IE Zone4 1606=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0249', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'IE Zone4 2101=3', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Zones\\4","value_name":"2101","expected":3,"operator":"eq","label":"IE Zone4 2101=3","parent":"Test-BaselineIERestrictedZone"}', N'See parent function: Test-BaselineIERestrictedZone', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0250', (SELECT id FROM control_categories WHERE name=N'Remote Access'), N'RDP DisablePasswordSaving=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","value_name":"DisablePasswordSaving","expected":1,"operator":"eq","label":"RDP DisablePasswordSaving=1","parent":"Test-BaselineRDPSecurity"}', N'See parent function: Test-BaselineRDPSecurity', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0251', (SELECT id FROM control_categories WHERE name=N'Remote Access'), N'RDP fDisableCdm=1 (no drive redirection)', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","value_name":"fDisableCdm","expected":1,"operator":"eq","label":"RDP fDisableCdm=1 (no drive redirection)","parent":"Test-BaselineRDPSecurity"}', N'See parent function: Test-BaselineRDPSecurity', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0252', (SELECT id FROM control_categories WHERE name=N'Remote Access'), N'RDP fPromptForPassword=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","value_name":"fPromptForPassword","expected":1,"operator":"eq","label":"RDP fPromptForPassword=1","parent":"Test-BaselineRDPSecurity"}', N'See parent function: Test-BaselineRDPSecurity', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0253', (SELECT id FROM control_categories WHERE name=N'Remote Access'), N'RDP fEncryptRPCTraffic=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","value_name":"fEncryptRPCTraffic","expected":1,"operator":"eq","label":"RDP fEncryptRPCTraffic=1","parent":"Test-BaselineRDPSecurity"}', N'See parent function: Test-BaselineRDPSecurity', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0254', (SELECT id FROM control_categories WHERE name=N'Remote Access'), N'RDP MinEncryptionLevel=3 (High)', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","value_name":"MinEncryptionLevel","expected":3,"operator":"eq","label":"RDP MinEncryptionLevel=3 (High)","parent":"Test-BaselineRDPSecurity"}', N'See parent function: Test-BaselineRDPSecurity', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0255', (SELECT id FROM control_categories WHERE name=N'Remote Access'), N'AllowIndexingEncryptedStoresOrItems=0', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search","value_name":"AllowIndexingEncryptedStoresOrItems","expected":0,"operator":"eq","label":"AllowIndexingEncryptedStoresOrItems=0","parent":"Test-BaselineRDPSecurity"}', N'See parent function: Test-BaselineRDPSecurity', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0256', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'WTDS NotifyMalicious=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\System","value_name":"NotifyMalicious","expected":1,"operator":"eq","label":"WTDS NotifyMalicious=1","parent":"Test-BaselineSmartScreenEnhanced"}', N'See parent function: Test-BaselineSmartScreenEnhanced', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0257', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'WTDS NotifyPasswordReuse=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\System","value_name":"NotifyPasswordReuse","expected":1,"operator":"eq","label":"WTDS NotifyPasswordReuse=1","parent":"Test-BaselineSmartScreenEnhanced"}', N'See parent function: Test-BaselineSmartScreenEnhanced', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0258', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'WTDS NotifyUnsafeApp=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\System","value_name":"NotifyUnsafeApp","expected":1,"operator":"eq","label":"WTDS NotifyUnsafeApp=1","parent":"Test-BaselineSmartScreenEnhanced"}', N'See parent function: Test-BaselineSmartScreenEnhanced', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0259', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'WTDS ServiceEnabled=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\System","value_name":"ServiceEnabled","expected":1,"operator":"eq","label":"WTDS ServiceEnabled=1","parent":"Test-BaselineSmartScreenEnhanced"}', N'See parent function: Test-BaselineSmartScreenEnhanced', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0260', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'EnableSmartScreen=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\System","value_name":"EnableSmartScreen","expected":1,"operator":"eq","label":"EnableSmartScreen=1","parent":"Test-BaselineSmartScreenEnhanced"}', N'See parent function: Test-BaselineSmartScreenEnhanced', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0261', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'ShellSmartScreenLevel=Block', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\System","value_name":"ShellSmartScreenLevel","expected":"Block","operator":"eq","label":"ShellSmartScreenLevel=Block","parent":"Test-BaselineSmartScreenEnhanced"}', N'See parent function: Test-BaselineSmartScreenEnhanced', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0262', (SELECT id FROM control_categories WHERE name=N'Browser Hardening'), N'Legacy Edge PhishingFilter PreventOverride=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\MicrosoftEdge\\PhishingFilter","value_name":"PreventOverride","expected":1,"operator":"eq","label":"Legacy Edge PhishingFilter PreventOverride=1","parent":"Test-BaselineSmartScreenEnhanced"}', N'See parent function: Test-BaselineSmartScreenEnhanced', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0263', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'Disable Game DVR', 'registry', 'low', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR","value_name":"AllowGameDVR","expected":0,"operator":"eq","label":"Disable Game DVR","parent":"Test-BaselineWindowsMisc"}', N'See parent function: Test-BaselineWindowsMisc', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0264', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'Windows Ink Workspace allowed (not above lock)', 'registry', 'low', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\WindowsInkWorkspace","value_name":"AllowWindowsInkWorkspace","expected":1,"operator":"eq","label":"Windows Ink Workspace allowed (not above lock)","parent":"Test-BaselineWindowsMisc"}', N'See parent function: Test-BaselineWindowsMisc', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0265', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'Windows Installer EnableUserControl=0', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\Installer","value_name":"EnableUserControl","expected":0,"operator":"eq","label":"Windows Installer EnableUserControl=0","parent":"Test-BaselineWindowsMisc"}', N'See parent function: Test-BaselineWindowsMisc', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0266', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'AlwaysInstallElevated=0', 'registry', 'critical', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\Installer","value_name":"AlwaysInstallElevated","expected":0,"operator":"eq","label":"AlwaysInstallElevated=0","parent":"Test-BaselineWindowsMisc"}', N'See parent function: Test-BaselineWindowsMisc', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0267', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'PowerShell EnableScriptBlockLogging=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\ScriptBlockLogging","value_name":"EnableScriptBlockLogging","expected":1,"operator":"eq","label":"PowerShell EnableScriptBlockLogging=1","parent":"Test-BaselineWindowsMisc"}', N'See parent function: Test-BaselineWindowsMisc', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0268', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'WinRM Client AllowDigest=0', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Client","value_name":"AllowDigest","expected":0,"operator":"eq","label":"WinRM Client AllowDigest=0","parent":"Test-BaselineWindowsMisc"}', N'See parent function: Test-BaselineWindowsMisc', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0269', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'WinRM Service AllowBasic=0', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Service","value_name":"AllowBasic","expected":0,"operator":"eq","label":"WinRM Service AllowBasic=0","parent":"Test-BaselineWindowsMisc"}', N'See parent function: Test-BaselineWindowsMisc', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0270', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'WinRM Service AllowUnencryptedTraffic=0', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Service","value_name":"AllowUnencryptedTraffic","expected":0,"operator":"eq","label":"WinRM Service AllowUnencryptedTraffic=0","parent":"Test-BaselineWindowsMisc"}', N'See parent function: Test-BaselineWindowsMisc', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0271', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'WinRM Service DisableRunAs=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Service","value_name":"DisableRunAs","expected":1,"operator":"eq","label":"WinRM Service DisableRunAs=1","parent":"Test-BaselineWindowsMisc"}', N'See parent function: Test-BaselineWindowsMisc', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0272', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'XboxGipSvc disabled', 'service', 'low', N'{"check_type":"service","service_name":"XboxGipSvc","property":"StartType","expected":"Disabled","operator":"eq","label":"XboxGipSvc disabled","parent":"Test-BaselineXboxServices"}', N'See parent function: Test-BaselineXboxServices', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0273', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'XblAuthManager disabled', 'service', 'low', N'{"check_type":"service","service_name":"XblAuthManager","property":"StartType","expected":"Disabled","operator":"eq","label":"XblAuthManager disabled","parent":"Test-BaselineXboxServices"}', N'See parent function: Test-BaselineXboxServices', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0274', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'XblGameSave disabled', 'service', 'low', N'{"check_type":"service","service_name":"XblGameSave","property":"StartType","expected":"Disabled","operator":"eq","label":"XblGameSave disabled","parent":"Test-BaselineXboxServices"}', N'See parent function: Test-BaselineXboxServices', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0275', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'XblNetworkingSvc disabled', 'service', 'low', N'{"check_type":"service","service_name":"XblNetworkingSvc","property":"StartType","expected":"Disabled","operator":"eq","label":"XblNetworkingSvc disabled","parent":"Test-BaselineXboxServices"}', N'See parent function: Test-BaselineXboxServices', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0276', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'XblGameSaveTask scheduled task disabled or absent', 'command', 'low', N'{"check_type":"custom","label":"XblGameSaveTask scheduled task disabled or absent","notes":"Agent should run Get-ScheduledTask -TaskName XblGameSaveTask. Pass if not found, or State=Disabled. Fail otherwise.","parent":"Test-BaselineXboxServices"}', N'See parent function: Test-BaselineXboxServices', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0277', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'SSL 2.0 Client disabled', 'command', 'high', N'{"check_type":"tls","protocol":"SSL 2.0","side":"Client","property":"Enabled","expected":0,"operator":"eq","label":"SSL 2.0 Client disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0278', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'SSL 2.0 Server disabled', 'command', 'high', N'{"check_type":"tls","protocol":"SSL 2.0","side":"Server","property":"Enabled","expected":0,"operator":"eq","label":"SSL 2.0 Server disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0279', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'SSL 3.0 Client disabled', 'command', 'high', N'{"check_type":"tls","protocol":"SSL 3.0","side":"Client","property":"Enabled","expected":0,"operator":"eq","label":"SSL 3.0 Client disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0280', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'SSL 3.0 Server disabled', 'command', 'high', N'{"check_type":"tls","protocol":"SSL 3.0","side":"Server","property":"Enabled","expected":0,"operator":"eq","label":"SSL 3.0 Server disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0281', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.0 Client disabled', 'command', 'high', N'{"check_type":"tls","protocol":"TLS 1.0","side":"Client","property":"Enabled","expected":0,"operator":"eq","label":"TLS 1.0 Client disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0282', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.0 Server disabled', 'command', 'high', N'{"check_type":"tls","protocol":"TLS 1.0","side":"Server","property":"Enabled","expected":0,"operator":"eq","label":"TLS 1.0 Server disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0283', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.1 Client disabled', 'command', 'high', N'{"check_type":"tls","protocol":"TLS 1.1","side":"Client","property":"Enabled","expected":0,"operator":"eq","label":"TLS 1.1 Client disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0284', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.1 Server disabled', 'command', 'high', N'{"check_type":"tls","protocol":"TLS 1.1","side":"Server","property":"Enabled","expected":0,"operator":"eq","label":"TLS 1.1 Server disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0285', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.2 Client enabled', 'command', 'high', N'{"check_type":"tls","protocol":"TLS 1.2","side":"Client","property":"Enabled","expected":1,"operator":"eq","label":"TLS 1.2 Client enabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0286', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.2 Client DisabledByDefault=0', 'command', 'high', N'{"check_type":"tls","protocol":"TLS 1.2","side":"Client","property":"DisabledByDefault","expected":0,"operator":"eq","label":"TLS 1.2 Client DisabledByDefault=0","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0287', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.2 Server enabled', 'command', 'high', N'{"check_type":"tls","protocol":"TLS 1.2","side":"Server","property":"Enabled","expected":1,"operator":"eq","label":"TLS 1.2 Server enabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0288', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.2 Server DisabledByDefault=0', 'command', 'high', N'{"check_type":"tls","protocol":"TLS 1.2","side":"Server","property":"DisabledByDefault","expected":0,"operator":"eq","label":"TLS 1.2 Server DisabledByDefault=0","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0289', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.3 Client enabled', 'command', 'medium', N'{"check_type":"tls","protocol":"TLS 1.3","side":"Client","property":"Enabled","expected":1,"operator":"eq","label":"TLS 1.3 Client enabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0290', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.3 Client DisabledByDefault=0', 'command', 'medium', N'{"check_type":"tls","protocol":"TLS 1.3","side":"Client","property":"DisabledByDefault","expected":0,"operator":"eq","label":"TLS 1.3 Client DisabledByDefault=0","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0291', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.3 Server enabled', 'command', 'medium', N'{"check_type":"tls","protocol":"TLS 1.3","side":"Server","property":"Enabled","expected":1,"operator":"eq","label":"TLS 1.3 Server enabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0292', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'TLS 1.3 Server DisabledByDefault=0', 'command', 'medium', N'{"check_type":"tls","protocol":"TLS 1.3","side":"Server","property":"DisabledByDefault","expected":0,"operator":"eq","label":"TLS 1.3 Server DisabledByDefault=0","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0293', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'Cipher RC4 128/128 disabled', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\SCHANNEL\\Ciphers\\RC4 128/128","value_name":"Enabled","expected":0,"operator":"eq","label":"Cipher RC4 128/128 disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0294', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'Cipher RC4 40/128 disabled', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\SCHANNEL\\Ciphers\\RC4 40/128","value_name":"Enabled","expected":0,"operator":"eq","label":"Cipher RC4 40/128 disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0295', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'Cipher RC4 56/128 disabled', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\SCHANNEL\\Ciphers\\RC4 56/128","value_name":"Enabled","expected":0,"operator":"eq","label":"Cipher RC4 56/128 disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0296', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'Cipher DES 56/56 disabled', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\SCHANNEL\\Ciphers\\DES 56/56","value_name":"Enabled","expected":0,"operator":"eq","label":"Cipher DES 56/56 disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0297', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'Cipher Triple DES 168 disabled', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\SCHANNEL\\Ciphers\\Triple DES 168","value_name":"Enabled","expected":0,"operator":"eq","label":"Cipher Triple DES 168 disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0298', (SELECT id FROM control_categories WHERE name=N'Cryptography'), N'Cipher NULL disabled', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\SCHANNEL\\Ciphers\\NULL","value_name":"Enabled","expected":0,"operator":"eq","label":"Cipher NULL disabled","parent":"Test-BaselineTLSHardening"}', N'See parent function: Test-BaselineTLSHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0299', (SELECT id FROM control_categories WHERE name=N'Encryption'), N'BitLocker OS XTS-AES-256', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","value_name":"EncryptionMethodWithXtsOs","expected":7,"operator":"eq","label":"BitLocker OS XTS-AES-256","parent":"Test-BaselineBitLockerPolicies"}', N'See parent function: Test-BaselineBitLockerPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0300', (SELECT id FROM control_categories WHERE name=N'Encryption'), N'BitLocker fixed XTS-AES-256', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","value_name":"EncryptionMethodWithXtsFdv","expected":7,"operator":"eq","label":"BitLocker fixed XTS-AES-256","parent":"Test-BaselineBitLockerPolicies"}', N'See parent function: Test-BaselineBitLockerPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0301', (SELECT id FROM control_categories WHERE name=N'Encryption'), N'BitLocker removable AES-CBC-256', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","value_name":"EncryptionMethodWithXtsRdv","expected":4,"operator":"eq","label":"BitLocker removable AES-CBC-256","parent":"Test-BaselineBitLockerPolicies"}', N'See parent function: Test-BaselineBitLockerPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0302', (SELECT id FROM control_categories WHERE name=N'Encryption'), N'BitLocker UseAdvancedStartup', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","value_name":"UseAdvancedStartup","expected":1,"operator":"eq","label":"BitLocker UseAdvancedStartup","parent":"Test-BaselineBitLockerPolicies"}', N'See parent function: Test-BaselineBitLockerPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0303', (SELECT id FROM control_categories WHERE name=N'Encryption'), N'Disallow BitLocker without TPM', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","value_name":"EnableBDEWithNoTPM","expected":0,"operator":"eq","label":"Disallow BitLocker without TPM","parent":"Test-BaselineBitLockerPolicies"}', N'See parent function: Test-BaselineBitLockerPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0304', (SELECT id FROM control_categories WHERE name=N'Encryption'), N'Require TPM+PIN', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","value_name":"UseTPMPIN","expected":1,"operator":"eq","label":"Require TPM+PIN","parent":"Test-BaselineBitLockerPolicies"}', N'See parent function: Test-BaselineBitLockerPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0305', (SELECT id FROM control_categories WHERE name=N'Encryption'), N'OSEncryptionType=Full', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","value_name":"OSEncryptionType","expected":1,"operator":"eq","label":"OSEncryptionType=Full","parent":"Test-BaselineBitLockerPolicies"}', N'See parent function: Test-BaselineBitLockerPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0306', (SELECT id FROM control_categories WHERE name=N'Encryption'), N'OSRecovery enabled', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","value_name":"OSRecovery","expected":1,"operator":"eq","label":"OSRecovery enabled","parent":"Test-BaselineBitLockerPolicies"}', N'See parent function: Test-BaselineBitLockerPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0307', (SELECT id FROM control_categories WHERE name=N'Encryption'), N'Require AD backup of recovery key', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","value_name":"OSRequireActiveDirectoryBackup","expected":1,"operator":"eq","label":"Require AD backup of recovery key","parent":"Test-BaselineBitLockerPolicies"}', N'See parent function: Test-BaselineBitLockerPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0308', (SELECT id FROM control_categories WHERE name=N'Encryption'), N'OSActiveDirectoryBackup=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","value_name":"OSActiveDirectoryBackup","expected":1,"operator":"eq","label":"OSActiveDirectoryBackup=1","parent":"Test-BaselineBitLockerPolicies"}', N'See parent function: Test-BaselineBitLockerPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0309', (SELECT id FROM control_categories WHERE name=N'Encryption'), N'Deny write to unprotected fixed drives', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","value_name":"FDVDenyWriteAccess","expected":1,"operator":"eq","label":"Deny write to unprotected fixed drives","parent":"Test-BaselineBitLockerPolicies"}', N'See parent function: Test-BaselineBitLockerPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0310', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'PowerShell EnableTranscripting=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\Transcription","value_name":"EnableTranscripting","expected":1,"operator":"eq","label":"PowerShell EnableTranscripting=1","parent":"Test-BaselinePowerShellHardening"}', N'See parent function: Test-BaselinePowerShellHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0311', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'PowerShell Transcription OutputDirectory configured', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\Transcription","value_name":"OutputDirectory","expected":"","operator":"exists","label":"PowerShell Transcription OutputDirectory configured","parent":"Test-BaselinePowerShellHardening"}', N'See parent function: Test-BaselinePowerShellHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0312', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'PowerShell EnableInvocationHeader=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\Transcription","value_name":"EnableInvocationHeader","expected":1,"operator":"eq","label":"PowerShell EnableInvocationHeader=1","parent":"Test-BaselinePowerShellHardening"}', N'See parent function: Test-BaselinePowerShellHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0313', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'PowerShell EnableModuleLogging=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\ModuleLogging","value_name":"EnableModuleLogging","expected":1,"operator":"eq","label":"PowerShell EnableModuleLogging=1","parent":"Test-BaselinePowerShellHardening"}', N'See parent function: Test-BaselinePowerShellHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0314', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'PowerShell ModuleLogging wildcard ''*'' present', 'command', 'medium', N'{"check_type":"custom","label":"PowerShell ModuleLogging wildcard \u0027*\u0027 present","notes":"Agent must enumerate values under HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\ModuleLogging\\ModuleNames and confirm at least one value equals \u0027*\u0027.","parent":"Test-BaselinePowerShellHardening"}', N'See parent function: Test-BaselinePowerShellHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0315', (SELECT id FROM control_categories WHERE name=N'Audit And Logging'), N'MicrosoftWindowsPowerShellV2Root optional feature disabled', 'command', 'medium', N'{"check_type":"custom","label":"MicrosoftWindowsPowerShellV2Root optional feature disabled","notes":"Agent should run Get-WindowsOptionalFeature -Online -FeatureName MicrosoftWindowsPowerShellV2Root and confirm State=Disabled.","parent":"Test-BaselinePowerShellHardening"}', N'See parent function: Test-BaselinePowerShellHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0316', (SELECT id FROM control_categories WHERE name=N'Credential Protection'), N'WDigest UseLogonCredential=0', 'registry', 'critical', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\WDigest","value_name":"UseLogonCredential","expected":0,"operator":"eq","label":"WDigest UseLogonCredential=0","parent":"Test-BaselineCredentialProtection"}', N'See parent function: Test-BaselineCredentialProtection', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0317', (SELECT id FROM control_categories WHERE name=N'Credential Protection'), N'WDigest Negotiate=0', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\WDigest","value_name":"Negotiate","expected":0,"operator":"eq","label":"WDigest Negotiate=0","parent":"Test-BaselineCredentialProtection"}', N'See parent function: Test-BaselineCredentialProtection', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0318', (SELECT id FROM control_categories WHERE name=N'Credential Protection'), N'FilterAdministratorToken=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","value_name":"FilterAdministratorToken","expected":1,"operator":"eq","label":"FilterAdministratorToken=1","parent":"Test-BaselineCredentialProtection"}', N'See parent function: Test-BaselineCredentialProtection', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0319', (SELECT id FROM control_categories WHERE name=N'Credential Protection'), N'AuditReceivingNTLMTraffic=2', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","value_name":"AuditReceivingNTLMTraffic","expected":2,"operator":"eq","label":"AuditReceivingNTLMTraffic=2","parent":"Test-BaselineCredentialProtection"}', N'See parent function: Test-BaselineCredentialProtection', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0320', (SELECT id FROM control_categories WHERE name=N'Credential Protection'), N'RestrictSendingNTLMTraffic=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","value_name":"RestrictSendingNTLMTraffic","expected":1,"operator":"eq","label":"RestrictSendingNTLMTraffic=1","parent":"Test-BaselineCredentialProtection"}', N'See parent function: Test-BaselineCredentialProtection', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0321', (SELECT id FROM control_categories WHERE name=N'Credential Protection'), N'Print Spooler service disabled (Start=4)', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Spooler","value_name":"Start","expected":4,"operator":"eq","label":"Print Spooler service disabled (Start=4)","parent":"Test-BaselineCredentialProtection"}', N'See parent function: Test-BaselineCredentialProtection', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0322', (SELECT id FROM control_categories WHERE name=N'Credential Protection'), N'Windows Script Host disabled', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows Script Host\\Settings","value_name":"Enabled","expected":0,"operator":"eq","label":"Windows Script Host disabled","parent":"Test-BaselineCredentialProtection"}', N'See parent function: Test-BaselineCredentialProtection', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0323', (SELECT id FROM control_categories WHERE name=N'Network Security'), N'EnableAutoDoh=2 (Automatic)', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Dnscache\\Parameters","value_name":"EnableAutoDoh","expected":2,"operator":"eq","label":"EnableAutoDoh=2 (Automatic)","parent":"Test-BaselineDNSOverHTTPS"}', N'See parent function: Test-BaselineDNSOverHTTPS', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0324', (SELECT id FROM control_categories WHERE name=N'Network Security'), N'At least one DoH server address configured', 'command', 'medium', N'{"check_type":"custom","label":"At least one DoH server address configured","notes":"Agent should run Get-DnsClientDohServerAddress and pass if count \u003e 0.","parent":"Test-BaselineDNSOverHTTPS"}', N'See parent function: Test-BaselineDNSOverHTTPS', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0325', (SELECT id FROM control_categories WHERE name=N'Network Security'), N'DoHPolicy=3 (Require DoH)', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient","value_name":"DoHPolicy","expected":3,"operator":"eq","label":"DoHPolicy=3 (Require DoH)","parent":"Test-BaselineDNSOverHTTPS"}', N'See parent function: Test-BaselineDNSOverHTTPS', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0326', (SELECT id FROM control_categories WHERE name=N'Patch Management'), N'NoAutoUpdate=0', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU","value_name":"NoAutoUpdate","expected":0,"operator":"eq","label":"NoAutoUpdate=0","parent":"Test-BaselineWindowsUpdatePolicy"}', N'See parent function: Test-BaselineWindowsUpdatePolicy', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0327', (SELECT id FROM control_categories WHERE name=N'Patch Management'), N'AUOptions=4 (auto download & schedule)', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU","value_name":"AUOptions","expected":4,"operator":"eq","label":"AUOptions=4 (auto download \u0026 schedule)","parent":"Test-BaselineWindowsUpdatePolicy"}', N'See parent function: Test-BaselineWindowsUpdatePolicy', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0328', (SELECT id FROM control_categories WHERE name=N'Patch Management'), N'IncludeRecommendedUpdates=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU","value_name":"IncludeRecommendedUpdates","expected":1,"operator":"eq","label":"IncludeRecommendedUpdates=1","parent":"Test-BaselineWindowsUpdatePolicy"}', N'See parent function: Test-BaselineWindowsUpdatePolicy', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0329', (SELECT id FROM control_categories WHERE name=N'Patch Management'), N'NoAutoRebootWithLoggedOnUsers=0', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU","value_name":"NoAutoRebootWithLoggedOnUsers","expected":0,"operator":"eq","label":"NoAutoRebootWithLoggedOnUsers=0","parent":"Test-BaselineWindowsUpdatePolicy"}', N'See parent function: Test-BaselineWindowsUpdatePolicy', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0330', (SELECT id FROM control_categories WHERE name=N'Patch Management'), N'TargetReleaseVersion=1', 'registry', 'low', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate","value_name":"TargetReleaseVersion","expected":1,"operator":"eq","label":"TargetReleaseVersion=1","parent":"Test-BaselineWindowsUpdatePolicy"}', N'See parent function: Test-BaselineWindowsUpdatePolicy', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0331', (SELECT id FROM control_categories WHERE name=N'Patch Management'), N'DeferQualityUpdatesPeriodInDays=0', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate","value_name":"DeferQualityUpdatesPeriodInDays","expected":0,"operator":"eq","label":"DeferQualityUpdatesPeriodInDays=0","parent":"Test-BaselineWindowsUpdatePolicy"}', N'See parent function: Test-BaselineWindowsUpdatePolicy', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0332', (SELECT id FROM control_categories WHERE name=N'Patch Management'), N'SetDisablePauseUXAccess=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate","value_name":"SetDisablePauseUXAccess","expected":1,"operator":"eq","label":"SetDisablePauseUXAccess=1","parent":"Test-BaselineWindowsUpdatePolicy"}', N'See parent function: Test-BaselineWindowsUpdatePolicy', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0333', (SELECT id FROM control_categories WHERE name=N'Application Control'), N'AppIDSvc Start=Automatic', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\AppIDSvc","value_name":"Start","expected":2,"operator":"eq","label":"AppIDSvc Start=Automatic","parent":"Test-BaselineWDACAppLocker"}', N'See parent function: Test-BaselineWDACAppLocker', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0334', (SELECT id FROM control_categories WHERE name=N'Application Control'), N'AppLocker effective policy has at least one rule collection', 'command', 'medium', N'{"check_type":"applocker","collection":"Any","property":"rule_count","expected":1,"operator":"gte","label":"AppLocker effective policy has at least one rule collection","parent":"Test-BaselineWDACAppLocker"}', N'See parent function: Test-BaselineWDACAppLocker', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0335', (SELECT id FROM control_categories WHERE name=N'Application Control'), N'AppLocker Exe collection configured', 'command', 'medium', N'{"check_type":"applocker","collection":"Exe","property":"rule_count","expected":1,"operator":"gte","label":"AppLocker Exe collection configured","parent":"Test-BaselineWDACAppLocker"}', N'See parent function: Test-BaselineWDACAppLocker', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0336', (SELECT id FROM control_categories WHERE name=N'Application Control'), N'AppLocker Script collection configured', 'command', 'medium', N'{"check_type":"applocker","collection":"Script","property":"rule_count","expected":1,"operator":"gte","label":"AppLocker Script collection configured","parent":"Test-BaselineWDACAppLocker"}', N'See parent function: Test-BaselineWDACAppLocker', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0337', (SELECT id FROM control_categories WHERE name=N'Application Control'), N'AppLocker Msi collection configured', 'command', 'medium', N'{"check_type":"applocker","collection":"Msi","property":"rule_count","expected":1,"operator":"gte","label":"AppLocker Msi collection configured","parent":"Test-BaselineWDACAppLocker"}', N'See parent function: Test-BaselineWDACAppLocker', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0338', (SELECT id FROM control_categories WHERE name=N'Application Control'), N'AppLocker Dll collection configured', 'command', 'medium', N'{"check_type":"applocker","collection":"Dll","property":"rule_count","expected":1,"operator":"gte","label":"AppLocker Dll collection configured","parent":"Test-BaselineWDACAppLocker"}', N'See parent function: Test-BaselineWDACAppLocker', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0339', (SELECT id FROM control_categories WHERE name=N'Application Control'), N'WDAC active policies via citool', 'command', 'medium', N'{"check_type":"command","executable":"citool.exe","arguments":"--list-policies","match_pattern":"PolicyId","expected":1,"operator":"gte","label":"WDAC active policies via citool","parent":"Test-BaselineWDACAppLocker"}', N'See parent function: Test-BaselineWDACAppLocker', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0340', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'NoDriveTypeAutoRun=255', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer","value_name":"NoDriveTypeAutoRun","expected":255,"operator":"eq","label":"NoDriveTypeAutoRun=255","parent":"Test-BaselineMiscHardening"}', N'See parent function: Test-BaselineMiscHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0341', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'NoAutorun=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer","value_name":"NoAutorun","expected":1,"operator":"eq","label":"NoAutorun=1","parent":"Test-BaselineMiscHardening"}', N'See parent function: Test-BaselineMiscHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0342', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'Tcpip6 DisabledComponents=0xFF', 'registry', 'low', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Tcpip6\\Parameters","value_name":"DisabledComponents","expected":255,"operator":"eq","label":"Tcpip6 DisabledComponents=0xFF","parent":"Test-BaselineMiscHardening"}', N'See parent function: Test-BaselineMiscHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0343', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'SMB workstation RequireSecuritySignature=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters","value_name":"RequireSecuritySignature","expected":1,"operator":"eq","label":"SMB workstation RequireSecuritySignature=1","parent":"Test-BaselineMiscHardening"}', N'See parent function: Test-BaselineMiscHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0344', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'LLMNR EnableMulticast=0', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient","value_name":"EnableMulticast","expected":0,"operator":"eq","label":"LLMNR EnableMulticast=0","parent":"Test-BaselineMiscHardening"}', N'See parent function: Test-BaselineMiscHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0345', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'Remote Assistance fAllowToGetHelp=0', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Terminal Server","value_name":"fAllowToGetHelp","expected":0,"operator":"eq","label":"Remote Assistance fAllowToGetHelp=0","parent":"Test-BaselineMiscHardening"}', N'See parent function: Test-BaselineMiscHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0346', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'Lsa NoLMHash=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","value_name":"NoLMHash","expected":1,"operator":"eq","label":"Lsa NoLMHash=1","parent":"Test-BaselineMiscHardening"}', N'See parent function: Test-BaselineMiscHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0347', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'Lsa RestrictAnonymousSAM=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","value_name":"RestrictAnonymousSAM","expected":1,"operator":"eq","label":"Lsa RestrictAnonymousSAM=1","parent":"Test-BaselineMiscHardening"}', N'See parent function: Test-BaselineMiscHardening', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0348', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_ADDON_MANAGEMENT for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_ADDON_MANAGEMENT","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_ADDON_MANAGEMENT for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0349', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_ADDON_MANAGEMENT for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_ADDON_MANAGEMENT","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_ADDON_MANAGEMENT for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0350', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_ADDON_MANAGEMENT for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_ADDON_MANAGEMENT","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_ADDON_MANAGEMENT for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0351', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_MIME_HANDLING for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_MIME_HANDLING","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_MIME_HANDLING for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0352', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_MIME_HANDLING for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_MIME_HANDLING","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_MIME_HANDLING for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0353', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_MIME_HANDLING for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_MIME_HANDLING","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_MIME_HANDLING for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0354', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_HTTP_USERNAME_PASSWORD_DISABLE for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_HTTP_USERNAME_PASSWORD_DISABLE","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_HTTP_USERNAME_PASSWORD_DISABLE for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0355', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_HTTP_USERNAME_PASSWORD_DISABLE for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_HTTP_USERNAME_PASSWORD_DISABLE","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_HTTP_USERNAME_PASSWORD_DISABLE for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0356', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_HTTP_USERNAME_PASSWORD_DISABLE for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_HTTP_USERNAME_PASSWORD_DISABLE","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_HTTP_USERNAME_PASSWORD_DISABLE for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0357', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_SECURITYBAND for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_SECURITYBAND","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_SECURITYBAND for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0358', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_SECURITYBAND for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_SECURITYBAND","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_SECURITYBAND for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0359', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_SECURITYBAND for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_SECURITYBAND","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_SECURITYBAND for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0360', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_LOCALMACHINE_LOCKDOWN for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_LOCALMACHINE_LOCKDOWN","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_LOCALMACHINE_LOCKDOWN for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0361', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_LOCALMACHINE_LOCKDOWN for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_LOCALMACHINE_LOCKDOWN","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_LOCALMACHINE_LOCKDOWN for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0362', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_LOCALMACHINE_LOCKDOWN for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_LOCALMACHINE_LOCKDOWN","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_LOCALMACHINE_LOCKDOWN for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0363', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_MIME_SNIFFING for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_MIME_SNIFFING","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_MIME_SNIFFING for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0364', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_MIME_SNIFFING for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_MIME_SNIFFING","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_MIME_SNIFFING for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0365', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_MIME_SNIFFING for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_MIME_SNIFFING","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_MIME_SNIFFING for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0366', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_VALIDATE_NAVIGATE_URL for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_VALIDATE_NAVIGATE_URL","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_VALIDATE_NAVIGATE_URL for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0367', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_VALIDATE_NAVIGATE_URL for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_VALIDATE_NAVIGATE_URL","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_VALIDATE_NAVIGATE_URL for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0368', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_VALIDATE_NAVIGATE_URL for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_VALIDATE_NAVIGATE_URL","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_VALIDATE_NAVIGATE_URL for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0369', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_OBJECT_CACHING for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_OBJECT_CACHING","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_OBJECT_CACHING for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0370', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_OBJECT_CACHING for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_OBJECT_CACHING","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_OBJECT_CACHING for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0371', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_OBJECT_CACHING for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_OBJECT_CACHING","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_OBJECT_CACHING for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0372', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_ZONE_ELEVATION for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_ZONE_ELEVATION","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_ZONE_ELEVATION for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0373', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_ZONE_ELEVATION for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_ZONE_ELEVATION","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_ZONE_ELEVATION for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0374', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_ZONE_ELEVATION for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_ZONE_ELEVATION","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_ZONE_ELEVATION for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0375', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_RESTRICT_ACTIVEXINSTALL for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_RESTRICT_ACTIVEXINSTALL","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_RESTRICT_ACTIVEXINSTALL for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0376', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_RESTRICT_ACTIVEXINSTALL for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_RESTRICT_ACTIVEXINSTALL","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_RESTRICT_ACTIVEXINSTALL for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0377', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_RESTRICT_ACTIVEXINSTALL for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_RESTRICT_ACTIVEXINSTALL","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_RESTRICT_ACTIVEXINSTALL for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0378', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_RESTRICT_FILEDOWNLOAD for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_RESTRICT_FILEDOWNLOAD","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_RESTRICT_FILEDOWNLOAD for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0379', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_RESTRICT_FILEDOWNLOAD for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_RESTRICT_FILEDOWNLOAD","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_RESTRICT_FILEDOWNLOAD for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0380', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_RESTRICT_FILEDOWNLOAD for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_RESTRICT_FILEDOWNLOAD","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_RESTRICT_FILEDOWNLOAD for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0381', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_UNC_SAVEDFILECHECK for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_UNC_SAVEDFILECHECK","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_UNC_SAVEDFILECHECK for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0382', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_UNC_SAVEDFILECHECK for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_UNC_SAVEDFILECHECK","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_UNC_SAVEDFILECHECK for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0383', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_UNC_SAVEDFILECHECK for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_UNC_SAVEDFILECHECK","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_UNC_SAVEDFILECHECK for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0384', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_WINDOW_RESTRICTIONS for excel.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_WINDOW_RESTRICTIONS","value_name":"excel.exe","expected":1,"operator":"exists","label":"FEATURE_WINDOW_RESTRICTIONS for excel.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0385', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_WINDOW_RESTRICTIONS for winword.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_WINDOW_RESTRICTIONS","value_name":"winword.exe","expected":1,"operator":"exists","label":"FEATURE_WINDOW_RESTRICTIONS for winword.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0386', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_WINDOW_RESTRICTIONS for powerpnt.exe', 'registry', 'medium', N'{"check_type":"registry","hive":"HKLM","path":"Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_WINDOW_RESTRICTIONS","value_name":"powerpnt.exe","expected":1,"operator":"exists","label":"FEATURE_WINDOW_RESTRICTIONS for powerpnt.exe","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0387', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Office Flash CompatibilityFlags=1024 (block)', 'registry', 'high', N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Microsoft\\Office\\ClickToRun\\REGISTRY\\MACHINE\\Software\\Microsoft\\Office\\16.0\\Common\\COM Compatibility\\{D27CDB6E-AE6D-11CF-96B8-444553540000}","value_name":"Compatibility Flags","expected":1024,"operator":"eq","label":"Office Flash CompatibilityFlags=1024 (block)","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0388', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'FEATURE_RESTRICT_LEGACY_JSCRIPT_PER_SECURITY_ZONE key exists', 'command', 'medium', N'{"check_type":"custom","label":"FEATURE_RESTRICT_LEGACY_JSCRIPT_PER_SECURITY_ZONE key exists","notes":"Agent should Test-Path HKLM:\\Software\\Policies\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_RESTRICT_LEGACY_JSCRIPT_PER_SECURITY_ZONE.","parent":"Test-BaselineM365Computer"}', N'See parent function: Test-BaselineM365Computer', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0389', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeTrustedCredManAccessPrivilege empty', 'command', 'high', N'{"check_type":"user_right","privilege":"SeTrustedCredManAccessPrivilege","expected_sids_or_accounts":[],"operator":"eq","label":"SeTrustedCredManAccessPrivilege empty","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0390', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeTcbPrivilege empty', 'command', 'critical', N'{"check_type":"user_right","privilege":"SeTcbPrivilege","expected_sids_or_accounts":[],"operator":"eq","label":"SeTcbPrivilege empty","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0391', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeCreateTokenPrivilege empty', 'command', 'critical', N'{"check_type":"user_right","privilege":"SeCreateTokenPrivilege","expected_sids_or_accounts":[],"operator":"eq","label":"SeCreateTokenPrivilege empty","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0392', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeCreatePermanentPrivilege empty', 'command', 'high', N'{"check_type":"user_right","privilege":"SeCreatePermanentPrivilege","expected_sids_or_accounts":[],"operator":"eq","label":"SeCreatePermanentPrivilege empty","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0393', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeLockMemoryPrivilege empty', 'command', 'high', N'{"check_type":"user_right","privilege":"SeLockMemoryPrivilege","expected_sids_or_accounts":[],"operator":"eq","label":"SeLockMemoryPrivilege empty","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0394', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeBackupPrivilege = Administrators only', 'command', 'high', N'{"check_type":"user_right","privilege":"SeBackupPrivilege","expected_sids_or_accounts":["*S-1-5-32-544"],"operator":"eq","label":"SeBackupPrivilege = Administrators only","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0395', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeCreatePagefilePrivilege = Administrators only', 'command', 'medium', N'{"check_type":"user_right","privilege":"SeCreatePagefilePrivilege","expected_sids_or_accounts":["*S-1-5-32-544"],"operator":"eq","label":"SeCreatePagefilePrivilege = Administrators only","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0396', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeDebugPrivilege = Administrators only', 'command', 'high', N'{"check_type":"user_right","privilege":"SeDebugPrivilege","expected_sids_or_accounts":["*S-1-5-32-544"],"operator":"eq","label":"SeDebugPrivilege = Administrators only","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0397', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeRemoteShutdownPrivilege = Administrators only', 'command', 'medium', N'{"check_type":"user_right","privilege":"SeRemoteShutdownPrivilege","expected_sids_or_accounts":["*S-1-5-32-544"],"operator":"eq","label":"SeRemoteShutdownPrivilege = Administrators only","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0398', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeLoadDriverPrivilege = Administrators only', 'command', 'high', N'{"check_type":"user_right","privilege":"SeLoadDriverPrivilege","expected_sids_or_accounts":["*S-1-5-32-544"],"operator":"eq","label":"SeLoadDriverPrivilege = Administrators only","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0399', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeSecurityPrivilege = Administrators only', 'command', 'high', N'{"check_type":"user_right","privilege":"SeSecurityPrivilege","expected_sids_or_accounts":["*S-1-5-32-544"],"operator":"eq","label":"SeSecurityPrivilege = Administrators only","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0400', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeSystemEnvironmentPrivilege = Administrators only', 'command', 'medium', N'{"check_type":"user_right","privilege":"SeSystemEnvironmentPrivilege","expected_sids_or_accounts":["*S-1-5-32-544"],"operator":"eq","label":"SeSystemEnvironmentPrivilege = Administrators only","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0401', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeManageVolumePrivilege = Administrators only', 'command', 'medium', N'{"check_type":"user_right","privilege":"SeManageVolumePrivilege","expected_sids_or_accounts":["*S-1-5-32-544"],"operator":"eq","label":"SeManageVolumePrivilege = Administrators only","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0402', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeProfileSingleProcessPrivilege = Administrators only', 'command', 'low', N'{"check_type":"user_right","privilege":"SeProfileSingleProcessPrivilege","expected_sids_or_accounts":["*S-1-5-32-544"],"operator":"eq","label":"SeProfileSingleProcessPrivilege = Administrators only","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0403', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeRestorePrivilege = Administrators only', 'command', 'high', N'{"check_type":"user_right","privilege":"SeRestorePrivilege","expected_sids_or_accounts":["*S-1-5-32-544"],"operator":"eq","label":"SeRestorePrivilege = Administrators only","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0404', (SELECT id FROM control_categories WHERE name=N'Authentication'), N'SeTakeOwnershipPrivilege = Administrators only', 'command', 'high', N'{"check_type":"user_right","privilege":"SeTakeOwnershipPrivilege","expected_sids_or_accounts":["*S-1-5-32-544"],"operator":"eq","label":"SeTakeOwnershipPrivilege = Administrators only","parent":"Test-BaselineUserRightsDetailed"}', N'See parent function: Test-BaselineUserRightsDetailed', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0405', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Access VBAWarnings=3', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\access\\Security","value_name":"VBAWarnings","expected":3,"operator":"eq","label":"Access VBAWarnings=3","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0406', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Access BlockContentExecutionFromInternet=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\access\\Security","value_name":"BlockContentExecutionFromInternet","expected":1,"operator":"eq","label":"Access BlockContentExecutionFromInternet=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0407', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Access RequireAddinSig=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\access\\Security","value_name":"RequireAddinSig","expected":1,"operator":"eq","label":"Access RequireAddinSig=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0408', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Excel VBAWarnings=3', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\excel\\Security","value_name":"VBAWarnings","expected":3,"operator":"eq","label":"Excel VBAWarnings=3","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0409', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Excel BlockContentExecutionFromInternet=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\excel\\Security","value_name":"BlockContentExecutionFromInternet","expected":1,"operator":"eq","label":"Excel BlockContentExecutionFromInternet=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0410', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Excel RequireAddinSig=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\excel\\Security","value_name":"RequireAddinSig","expected":1,"operator":"eq","label":"Excel RequireAddinSig=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0411', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'PowerPoint VBAWarnings=3', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\powerpoint\\Security","value_name":"VBAWarnings","expected":3,"operator":"eq","label":"PowerPoint VBAWarnings=3","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0412', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'PowerPoint BlockContentExecutionFromInternet=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\powerpoint\\Security","value_name":"BlockContentExecutionFromInternet","expected":1,"operator":"eq","label":"PowerPoint BlockContentExecutionFromInternet=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0413', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'PowerPoint RequireAddinSig=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\powerpoint\\Security","value_name":"RequireAddinSig","expected":1,"operator":"eq","label":"PowerPoint RequireAddinSig=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0414', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Publisher VBAWarnings=3', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\publisher\\Security","value_name":"VBAWarnings","expected":3,"operator":"eq","label":"Publisher VBAWarnings=3","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0415', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Publisher BlockContentExecutionFromInternet=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\publisher\\Security","value_name":"BlockContentExecutionFromInternet","expected":1,"operator":"eq","label":"Publisher BlockContentExecutionFromInternet=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0416', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Publisher RequireAddinSig=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\publisher\\Security","value_name":"RequireAddinSig","expected":1,"operator":"eq","label":"Publisher RequireAddinSig=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0417', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Visio VBAWarnings=3', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\visio\\Security","value_name":"VBAWarnings","expected":3,"operator":"eq","label":"Visio VBAWarnings=3","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0418', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Visio BlockContentExecutionFromInternet=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\visio\\Security","value_name":"BlockContentExecutionFromInternet","expected":1,"operator":"eq","label":"Visio BlockContentExecutionFromInternet=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0419', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Visio RequireAddinSig=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\visio\\Security","value_name":"RequireAddinSig","expected":1,"operator":"eq","label":"Visio RequireAddinSig=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0420', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Word VBAWarnings=3', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\word\\Security","value_name":"VBAWarnings","expected":3,"operator":"eq","label":"Word VBAWarnings=3","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0421', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Word BlockContentExecutionFromInternet=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\word\\Security","value_name":"BlockContentExecutionFromInternet","expected":1,"operator":"eq","label":"Word BlockContentExecutionFromInternet=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0422', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Word RequireAddinSig=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\word\\Security","value_name":"RequireAddinSig","expected":1,"operator":"eq","label":"Word RequireAddinSig=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0423', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Project VBAWarnings=3', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\project\\Security","value_name":"VBAWarnings","expected":3,"operator":"eq","label":"Project VBAWarnings=3","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0424', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Project BlockContentExecutionFromInternet=1', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\project\\Security","value_name":"BlockContentExecutionFromInternet","expected":1,"operator":"eq","label":"Project BlockContentExecutionFromInternet=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0425', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Project RequireAddinSig=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\project\\Security","value_name":"RequireAddinSig","expected":1,"operator":"eq","label":"Project RequireAddinSig=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0426', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Trusted Locations AllowNetworkLocations=0', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\word\\Security\\Trusted Locations","value_name":"AllowNetworkLocations","expected":0,"operator":"eq","label":"Trusted Locations AllowNetworkLocations=0","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0427', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Word NoTrustBar=1', 'registry', 'medium', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\word\\Security","value_name":"NoTrustBar","expected":1,"operator":"eq","label":"Word NoTrustBar=1","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0428', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Excel ProtectedView Internet=on', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\excel\\Security\\ProtectedView","value_name":"DisableInternetFilesInPV","expected":0,"operator":"eq","label":"Excel ProtectedView Internet=on","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0429', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Excel ProtectedView Unsafe=on', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\excel\\Security\\ProtectedView","value_name":"DisableUnsafeLocationsInPV","expected":0,"operator":"eq","label":"Excel ProtectedView Unsafe=on","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0430', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Excel ProtectedView Attachments=on', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\excel\\Security\\ProtectedView","value_name":"DisableAttachmentsInPV","expected":0,"operator":"eq","label":"Excel ProtectedView Attachments=on","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0431', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'PowerPoint ProtectedView Internet=on', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\powerpoint\\Security\\ProtectedView","value_name":"DisableInternetFilesInPV","expected":0,"operator":"eq","label":"PowerPoint ProtectedView Internet=on","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0432', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'PowerPoint ProtectedView Unsafe=on', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\powerpoint\\Security\\ProtectedView","value_name":"DisableUnsafeLocationsInPV","expected":0,"operator":"eq","label":"PowerPoint ProtectedView Unsafe=on","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0433', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'PowerPoint ProtectedView Attachments=on', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\powerpoint\\Security\\ProtectedView","value_name":"DisableAttachmentsInPV","expected":0,"operator":"eq","label":"PowerPoint ProtectedView Attachments=on","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0434', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Word ProtectedView Internet=on', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\word\\Security\\ProtectedView","value_name":"DisableInternetFilesInPV","expected":0,"operator":"eq","label":"Word ProtectedView Internet=on","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0435', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Word ProtectedView Unsafe=on', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\word\\Security\\ProtectedView","value_name":"DisableUnsafeLocationsInPV","expected":0,"operator":"eq","label":"Word ProtectedView Unsafe=on","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0436', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Word ProtectedView Attachments=on', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\word\\Security\\ProtectedView","value_name":"DisableAttachmentsInPV","expected":0,"operator":"eq","label":"Word ProtectedView Attachments=on","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0437', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Word FileBlock OpenInProtectedView=0', 'registry', 'medium', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\word\\Security\\FileBlock","value_name":"OpenInProtectedView","expected":0,"operator":"eq","label":"Word FileBlock OpenInProtectedView=0","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0438', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Outlook AdminSecurityMode=3', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\Outlook\\Security","value_name":"AdminSecurityMode","expected":3,"operator":"eq","label":"Outlook AdminSecurityMode=3","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0439', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Outlook PromptOOMAddressBookAccess=0', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\Outlook\\Security","value_name":"PromptOOMAddressBookAccess","expected":0,"operator":"eq","label":"Outlook PromptOOMAddressBookAccess=0","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0440', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Outlook PromptOOMSendMail=0', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\Outlook\\Security","value_name":"PromptOOMSendMail","expected":0,"operator":"eq","label":"Outlook PromptOOMSendMail=0","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0441', (SELECT id FROM control_categories WHERE name=N'Office Hardening'), N'Outlook PreferredEncryptAlgorithm >= 14', 'registry', 'high', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Office\\16.0\\Outlook\\Security","value_name":"PreferredEncryptAlgorithm","expected":14,"operator":"gte","label":"Outlook PreferredEncryptAlgorithm \u003e= 14","parent":"Test-BaselineM365UserPolicies"}', N'See parent function: Test-BaselineM365UserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0442', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'NoToastApplicationNotification=1', 'registry', 'low', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\SOFTWARE\\Policies\\Microsoft\\Windows\\CurrentVersion\\PushNotifications","value_name":"NoToastApplicationNotification","expected":1,"operator":"eq","label":"NoToastApplicationNotification=1","parent":"Test-BaselineUserPolicies"}', N'See parent function: Test-BaselineUserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0443', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'DisableThirdPartySuggestions=1', 'registry', 'low', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\Software\\Policies\\Microsoft\\Windows\\CloudContent","value_name":"DisableThirdPartySuggestions","expected":1,"operator":"eq","label":"DisableThirdPartySuggestions=1","parent":"Test-BaselineUserPolicies"}', N'See parent function: Test-BaselineUserPolicies', 1, 1, @systemUserId);
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0444', (SELECT id FROM control_categories WHERE name=N'Hardening'), N'IE FormSuggest Passwords = ''no''', 'registry', 'medium', N'{"check_type":"registry","hive":"HKU","path":"{SID}\\Software\\Policies\\Microsoft\\Internet Explorer\\Main","value_name":"FormSuggest Passwords","expected":"no","operator":"eq","label":"IE FormSuggest Passwords = \u0027no\u0027","parent":"Test-BaselineUserPolicies"}', N'See parent function: Test-BaselineUserPolicies', 1, 1, @systemUserId);

-- ============================================================
-- CONTROL_FRAMEWORKS (M:N mappings)
-- ============================================================
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-161';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-161';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0391';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0391';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0391';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0387';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0387';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0211';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0211';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0211';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-001';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-001';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0234';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0234';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0094';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0094';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0094';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0094';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0144';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0144';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0144';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0246';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0246';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-114';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-114';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-114';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0375';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0375';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0127';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0127';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0384';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0384';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0067';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0067';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0154';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0154';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0154';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0190';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0190';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0190';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0190';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0128';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0128';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0032';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0032';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0407';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0407';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0437';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0437';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-047';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-047';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0436';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0436';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0333';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0333';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-086';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-086';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0161';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0161';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0161';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0069';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0069';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0069';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0069';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0112';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0112';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0078';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0078';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0078';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0078';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0115';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0115';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0340';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0340';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0340';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0156';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0156';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0267';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0267';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0267';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0204';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0204';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0204';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0251';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0251';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0251';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0399';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0399';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0399';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0266';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0266';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0266';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0011';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0011';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0011';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0216';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0216';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0187';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0187';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0187';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0187';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0141';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0141';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0141';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0222';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0222';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0420';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0420';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0304';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0304';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0304';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0126';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0126';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-136';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-136';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-136';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-028';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-028';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-031';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-031';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-123';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-123';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-123';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0018';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0018';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0018';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0171';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0171';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0171';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0171';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0408';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0408';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0046';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0046';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0046';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-102';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0203';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0203';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0203';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-007';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-007';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-007';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-007';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0020';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0020';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0020';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-052';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-052';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0316';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0316';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0316';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-092';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-029';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-029';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0107';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0107';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0052';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0052';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-051';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-051';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-051';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0273';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0088';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0088';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0088';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0088';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0091';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0091';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0091';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0091';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0048';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0048';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0051';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0051';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0363';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0363';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0345';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0345';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0345';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-005';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-005';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-005';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-005';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0188';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0188';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0188';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0188';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0325';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0325';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0232';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0232';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0195';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0195';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0195';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0307';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0307';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0307';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0320';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0320';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0320';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0253';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0253';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0253';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-079';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-079';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0293';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0293';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0293';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0293';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-117';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-117';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-117';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0130';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0130';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0064';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0064';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0064';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0157';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0157';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0035';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0035';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-105';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-105';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-105';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0006';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0006';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0006';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-150';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-150';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-150';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0001';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0001';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0001';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-077';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0005';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0005';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0005';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0426';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0426';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-073';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-048';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-048';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-048';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-048';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-122';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-122';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-122';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-108';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-108';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-023';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-023';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0073';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0073';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0073';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0073';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0339';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0339';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-027';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-027';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0371';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0371';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0312';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0312';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0312';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0357';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0357';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0360';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0360';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0303';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0303';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0303';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-008';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-008';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-008';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0306';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0306';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0306';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0169';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0169';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0169';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0169';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0070';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0070';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0070';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0070';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0326';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0326';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0108';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0108';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-097';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-097';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-097';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0432';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0432';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0337';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0337';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-135';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-135';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0116';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0116';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0170';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0170';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0170';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0170';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0056';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0056';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0056';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0330';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0330';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0196';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0196';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0196';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0085';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0085';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0085';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0085';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0098';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0098';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-039';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-039';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-039';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-132';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-132';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-118';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-118';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-118';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0145';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0145';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0145';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0364';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0364';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0027';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0027';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0027';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0172';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0172';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0172';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0172';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0158';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0158';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0158';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0023';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0023';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0023';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0198';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0198';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0198';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0124';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0124';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0416';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0416';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0059';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0059';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0059';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-074';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-160';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-160';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0103';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0103';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0119';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0119';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-038';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-038';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-038';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-100';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-100';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0074';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0074';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0074';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0074';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0359';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0359';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0419';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0419';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0368';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0368';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0208';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0208';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0208';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0212';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0212';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0212';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0341';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0341';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0341';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0334';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0334';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-090';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0274';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0425';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0425';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0081';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0081';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0081';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0081';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-127';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-127';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-050';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-050';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-050';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-050';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0254';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0254';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0254';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0083';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0083';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0083';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0083';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-024';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-024';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-024';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-024';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0221';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0221';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-113';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-113';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-109';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0378';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0378';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-019';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-019';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0009';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0009';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0009';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-044';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-044';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-044';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-044';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-065';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0415';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0415';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0302';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0302';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0302';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-069';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-069';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0422';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0422';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0111';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0111';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0258';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0258';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0016';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0016';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0016';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0298';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0298';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0298';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0298';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0044';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0044';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0044';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-145';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-145';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-145';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0395';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0395';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0395';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0353';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0353';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0349';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0349';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0282';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0282';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0282';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0282';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0235';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0235';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0184';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0184';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0184';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0184';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0224';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0224';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0327';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0327';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0249';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0249';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0374';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0374';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-012';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-012';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-012';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0428';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0428';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0322';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0322';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0322';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-131';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-131';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0176';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0176';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0176';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0176';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-101';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0280';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0280';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0280';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0280';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0024';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0024';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0024';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0323';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0323';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0386';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0386';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0039';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0039';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0039';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0050';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0050';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-106';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-106';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-066';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0269';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0269';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0269';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0351';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0351';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-062';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-062';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-062';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0277';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0277';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0277';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0277';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0062';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0062';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0062';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-154';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-154';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-040';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-040';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0346';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0346';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0346';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0181';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0181';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0181';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0181';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0167';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0167';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0167';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0167';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0441';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0441';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0358';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0358';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-059';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-059';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-059';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0129';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0129';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-043';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-043';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-043';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0082';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0082';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0082';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0082';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-083';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0149';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0149';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0149';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0281';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0281';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0281';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0281';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0297';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0297';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0297';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0297';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-139';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-139';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-139';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0047';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0047';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0047';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0121';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0121';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-026';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-026';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-026';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0179';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0179';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0179';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0179';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-054';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-054';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0272';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0255';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0255';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0255';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0089';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0089';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0089';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0089';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0430';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0430';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0049';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0049';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0421';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0421';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0308';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0308';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0308';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0086';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0086';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0086';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0086';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0382';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0382';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-035';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-035';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0230';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0230';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0292';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0292';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0292';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0292';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-153';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-153';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0380';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0380';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-130';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-130';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-130';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0223';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0223';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0194';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0194';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0194';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0087';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0087';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0087';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0087';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-061';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-061';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-078';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-078';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-091';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-116';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-116';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-116';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0434';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0434';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0080';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0080';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0080';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0080';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-087';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-087';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0065';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0065';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0133';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0133';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-149';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0034';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0034';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-104';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-104';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-104';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-104';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0066';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0066';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0239';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0239';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-076';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-076';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0004';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0004';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0004';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0247';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0247';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-049';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-049';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0097';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0097';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0278';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0278';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0278';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0278';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0072';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0072';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0072';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0072';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0383';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0383';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0165';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0165';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0165';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0319';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0319';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0319';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0260';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0260';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0356';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0356';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0138';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0138';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-124';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-124';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0120';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0120';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0142';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0142';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0142';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-055';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-055';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0031';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0031';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0164';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0164';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0164';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0248';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0248';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-020';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-020';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-020';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-020';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-096';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0367';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0367';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-088';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-158';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-158';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0328';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0328';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-095';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-095';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-095';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0077';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0077';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0077';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0077';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0410';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0410';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0294';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0294';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0294';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0294';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-141';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-141';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-141';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0429';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0429';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0201';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0201';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0201';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0019';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0019';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0019';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0229';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0229';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0240';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0240';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0442';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0442';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0163';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0163';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0163';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-057';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-057';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0418';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0418';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0270';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0270';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0270';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-003';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-003';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0093';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0093';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0093';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0093';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0053';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0053';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0311';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0311';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0311';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0286';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0286';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0286';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0286';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-147';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-147';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0377';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0377';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0125';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0125';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0427';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0427';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0028';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0028';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0028';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0398';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0398';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0398';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0197';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0197';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0197';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0055';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0055';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0289';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0289';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0289';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0289';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0244';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0244';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0102';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0102';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0160';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0160';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0160';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0180';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0180';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0180';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0180';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0058';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0058';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0058';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0412';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0412';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-119';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-119';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-119';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-119';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0413';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0413';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0026';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0026';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0026';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0245';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0245';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-070';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-070';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0394';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0394';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0394';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-157';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-157';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0003';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0003';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0003';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0321';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0321';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0321';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0227';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0227';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0030';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0030';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0315';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0315';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0315';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0262';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0262';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0411';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0411';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-021';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-021';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-021';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-015';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-015';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-015';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-015';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0025';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0025';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0025';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-112';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-112';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-022';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-022';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0336';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0336';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0355';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0355';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0137';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0137';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0220';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0220';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0008';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0008';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0008';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-045';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-045';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-045';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-045';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-041';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-041';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-041';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-041';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-058';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-058';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0243';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0243';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0259';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0259';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0041';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0041';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0041';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0045';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0045';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0045';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0215';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0215';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0288';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0288';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0288';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0288';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0228';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0228';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-142';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-142';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0348';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0348';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0193';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0193';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0193';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0175';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0175';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0175';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0175';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0237';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0237';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0414';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0414';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0296';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0296';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0296';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0296';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0401';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0401';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0401';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0406';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0406';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-033';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-033';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-033';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-033';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-099';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0362';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0362';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-037';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-037';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-037';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0189';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0189';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0189';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0189';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0409';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0409';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0122';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0122';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0285';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0285';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0285';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0285';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0015';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0015';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0021';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0021';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0021';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0038';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0038';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0038';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0178';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0178';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0178';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0178';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0393';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0393';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0393';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0389';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0389';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0389';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0182';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0182';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0182';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0182';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0209';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0209';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0209';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0213';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0213';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0213';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0183';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0183';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0183';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0183';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0438';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0438';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-098';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-098';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0110';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0110';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-016';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-016';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0347';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0347';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0347';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-010';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-010';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-010';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0134';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0134';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-128';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-128';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-128';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0373';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0373';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0060';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0060';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0060';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0405';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0405';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0168';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0168';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0168';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0168';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-018';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-018';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-018';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-115';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-115';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-115';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0271';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0271';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0271';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-084';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-084';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0150';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0150';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0150';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0324';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0324';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0314';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0314';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0314';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0037';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0037';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0037';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0264';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0264';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0264';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0381';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0381';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0033';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0033';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0206';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0206';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0206';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0372';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0372';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-046';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-046';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-046';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0290';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0290';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0290';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0290';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-042';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-042';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-042';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-042';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0276';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0042';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0042';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0042';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-034';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-034';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-034';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-034';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0218';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0218';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-152';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-152';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0370';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0370';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0366';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0366';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0012';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0012';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0147';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0147';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0147';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0331';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0331';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0010';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0010';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0010';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0118';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0118';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-140';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-140';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-140';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-140';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-067';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0105';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0105';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0313';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0313';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0313';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-063';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-063';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0238';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0238';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0054';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0054';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0132';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0132';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0063';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0063';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0063';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-103';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-103';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0174';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0174';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0174';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0174';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0361';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0361';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-146';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-146';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-146';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0265';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0265';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0265';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-080';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0236';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0236';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0279';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0279';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0279';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0279';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0440';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0440';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0153';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0153';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0153';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0231';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0231';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0095';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0095';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0095';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0095';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0400';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0400';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0400';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0318';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0318';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0318';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0332';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0332';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-148';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-148';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-148';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0291';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0291';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0291';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0291';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-125';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-125';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-125';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0344';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0344';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0344';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-155';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-155';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-138';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-138';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-138';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0155';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0155';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0155';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0443';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0443';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0205';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0205';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0205';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-072';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-072';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0329';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0329';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-089';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0217';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0217';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0185';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0185';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0185';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0185';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-094';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0295';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0295';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0295';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0295';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0139';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0139';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0113';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0113';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0079';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0079';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0079';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0079';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0100';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0100';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0369';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0369';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0365';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0365';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0444';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0444';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-056';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0283';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0283';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0283';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0283';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-006';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-006';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-006';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-006';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0090';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0090';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0090';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0090';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-002';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-002';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0092';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0092';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0092';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0092';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0106';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0106';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-111';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-111';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-111';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0335';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0335';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-137';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-137';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-137';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-137';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0261';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0261';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0376';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0376';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0114';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0114';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0343';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0343';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0343';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0029';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0029';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0040';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0040';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0040';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0162';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0162';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0162';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0148';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0148';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0148';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0202';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0202';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0202';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0350';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0350';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0385';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0385';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-053';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-053';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-093';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-064';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-064';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0140';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0140';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0096';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0096';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0096';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0096';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0287';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0287';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0287';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0287';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0186';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0186';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0186';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0186';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0057';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0057';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0057';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0131';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0131';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0177';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0177';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0177';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0177';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0402';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0402';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0402';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-156';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-156';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0417';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0417';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0002';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0002';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0002';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0226';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0226';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0143';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0143';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0143';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0300';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0300';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0300';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0403';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0403';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0403';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0305';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0305';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0305';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0435';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0435';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-014';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-014';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-011';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-011';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-011';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-011';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0101';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0101';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0317';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0317';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0317';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0252';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0252';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0252';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0152';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0152';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0152';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0136';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0136';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0390';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0390';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0390';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0076';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0076';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0076';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0076';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-107';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-107';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0433';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0433';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0275';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0007';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0007';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0007';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0424';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0424';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0310';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0310';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0310';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0268';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0268';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0268';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0439';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0439';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0214';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0214';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0256';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0256';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-159';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-159';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-159';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-143';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-143';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-134';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-134';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-134';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-110';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-110';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-121';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-121';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-121';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0192';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0192';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0192';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-060';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-060';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0342';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0342';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0342';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0151';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0151';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0151';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0191';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0191';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0191';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-009';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-009';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-009';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-009';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-081';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0061';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0061';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0061';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0338';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0338';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0299';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0299';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0299';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0109';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0109';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0123';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0123';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0284';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0284';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0284';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0284';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0014';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0014';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0117';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0117';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0392';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0392';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0392';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0396';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0396';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0396';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0200';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0200';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0200';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0043';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0043';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0043';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0263';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0263';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0263';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0084';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0084';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0084';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0084';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0099';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0099';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0250';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0250';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0250';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-151';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-151';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-151';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-144';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-144';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-133';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-133';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-133';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-129';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-129';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-075';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-075';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0210';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0210';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0210';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0388';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0388';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0173';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0173';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0173';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0173';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0159';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0159';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0159';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0022';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0022';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0022';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0199';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0199';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0199';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0241';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0241';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0397';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0397';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0397';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-013';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-013';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-013';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-013';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-085';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-085';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-085';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0352';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0352';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-017';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-017';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-071';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0036';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0036';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0036';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0431';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0431';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0068';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0068';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0071';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0071';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0071';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0071';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0135';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0135';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0207';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0207';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0207';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0075';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0075';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='BL-0075';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0075';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0404';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0404';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0404';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-120';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-120';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0219';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0219';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0233';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0233';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0354';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0354';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0301';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0301';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0301';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0309';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0309';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0309';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0225';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0225';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-032';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-032';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-032';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-032';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-126';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-126';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-126';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-036';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-036';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-036';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0146';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0146';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0146';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0166';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0166';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0166';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0242';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0242';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-004';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-004';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-004';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0104';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0104';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-082';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-082';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-082';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0257';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0257';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-030';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-030';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-025';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='SC-025';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwISO FROM control_defs WHERE control_id='SC-025';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='SC-025';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='SC-068';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0423';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0423';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0379';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0379';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0017';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwHIPAA FROM control_defs WHERE control_id='BL-0017';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0017';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwCIS FROM control_defs WHERE control_id='BL-0013';
INSERT INTO control_frameworks (control_def_id, framework_id) SELECT id, @fwNIST FROM control_defs WHERE control_id='BL-0013';
GO

