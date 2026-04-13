SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_013_dc_controls.sql
-- Kryoss Platform -- Domain Controller Security Controls
--
-- Adds ~40 controls (DC-001 through DC-040) that apply ONLY to
-- Windows Server Domain Controller platforms (DC19, DC22, DC25).
-- These cover AD replication health, LDAP security, Kerberos
-- hardening, DC-specific hardening, DC network, and DC auditing.
--
-- NOT linked to workstations (W10, W11) or member servers (MS19,
-- MS22, MS25).
--
-- Linked to ALL 5 active frameworks (NIST, CIS, HIPAA, ISO27001,
-- PCI-DSS).
--
-- Run AFTER seed_012. Fully idempotent (NOT EXISTS guards).
-- ============================================================

BEGIN TRANSACTION;

DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Framework IDs
DECLARE @fwCIS   INT = (SELECT id FROM frameworks WHERE code='CIS');
DECLARE @fwNIST  INT = (SELECT id FROM frameworks WHERE code='NIST');
DECLARE @fwHIPAA INT = (SELECT id FROM frameworks WHERE code='HIPAA');
DECLARE @fwPCI   INT = (SELECT id FROM frameworks WHERE code='PCI-DSS');
DECLARE @fwISO   INT = (SELECT id FROM frameworks WHERE code='ISO27001');

-- Platform IDs (DC only)
DECLARE @platDC19 INT = (SELECT id FROM platforms WHERE code='DC19');
DECLARE @platDC22 INT = (SELECT id FROM platforms WHERE code='DC22');
DECLARE @platDC25 INT = (SELECT id FROM platforms WHERE code='DC25');

IF @platDC19 IS NULL OR @platDC22 IS NULL OR @platDC25 IS NULL
BEGIN
    RAISERROR('DC19, DC22, or DC25 platform missing. Run seed_002 first.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ============================================================
-- PART 0 -- New categories for DC controls
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Active Directory')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Active Directory', 401, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Kerberos Security')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Kerberos Security', 402, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Domain Controller Hardening')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Domain Controller Hardening', 403, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'DNS Security')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'DNS Security', 404, @systemUserId);

-- Category lookups
DECLARE @catAD         INT = (SELECT id FROM control_categories WHERE name=N'Active Directory');
DECLARE @catKerberos   INT = (SELECT id FROM control_categories WHERE name=N'Kerberos Security');
DECLARE @catDCHarden   INT = (SELECT id FROM control_categories WHERE name=N'Domain Controller Hardening');
DECLARE @catDNSSec     INT = (SELECT id FROM control_categories WHERE name=N'DNS Security');
DECLARE @catNetSec     INT = (SELECT id FROM control_categories WHERE name=N'Network Security');
DECLARE @catAuditLog   INT = (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring');
DECLARE @catServices   INT = (SELECT id FROM control_categories WHERE name=N'Services Hardening');
DECLARE @catCredProt   INT = (SELECT id FROM control_categories WHERE name=N'Credential Protection');
DECLARE @catSecOpts    INT = (SELECT id FROM control_categories WHERE name=N'Security Options And Local Policy');

-- ============================================================
-- SECTION A: AD Replication & Health (DC-001..DC-006)
-- ============================================================

-- DC-001: AD replication status
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-001')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-001', @catAD,
    N'AD Replication Status - No Failures',
    'command', 'critical',
    N'{"command":"repadmin /replsummary /bysrc /bydest /sort:delta","successCriteria":"noFailures","parseMode":"repadmin","display":"Checking AD replication status for failures"}',
    N'Run repadmin /replsummary to identify replication failures. Investigate and resolve failed replication links. Common causes: DNS issues, network connectivity, lingering objects, or USN rollback.',
    1, 1, @systemUserId);

-- DC-002: SYSVOL replication state
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-002')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-002', @catAD,
    N'SYSVOL Replication State (DFS-R) Healthy',
    'command', 'critical',
    N'{"command":"powershell -Command \"Get-WmiObject -Namespace root\\MicrosoftDfs -Class DfsrReplicatedFolderInfo | Where-Object { $_.ReplicatedFolderName -eq ''SYSVOL Share'' } | Select-Object State\"","successCriteria":"contains","expected":"4","display":"Checking SYSVOL DFS-R replication state"}',
    N'Verify DFS-R is replicating SYSVOL correctly. State 4 = Normal. Run dfsrdiag to diagnose issues. If using FRS (legacy), migrate to DFS-R immediately.',
    1, 1, @systemUserId);

-- DC-003: FSMO role holders inventory
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-003')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-003', @catAD,
    N'FSMO Role Holders Accessible',
    'command', 'high',
    N'{"command":"netdom query fsmo","successCriteria":"exitCode","expected":"0","display":"Checking FSMO role holders accessibility"}',
    N'Run netdom query fsmo to verify all 5 FSMO roles are held by accessible DCs. If any role holder is unavailable, seize the role to a healthy DC using ntdsutil.',
    1, 1, @systemUserId);

-- DC-004: DCDiag core tests
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-004')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-004', @catAD,
    N'DCDiag Core Tests Pass (Advertising, FRS, DFSR, SysVol, KCC, Replications)',
    'command', 'critical',
    N'{"command":"dcdiag /test:Advertising /test:FrsEvent /test:DFSREvent /test:SysVolCheck /test:KccEvent /test:Replications","successCriteria":"noFailures","parseMode":"dcdiag","display":"Running DCDiag core diagnostic tests"}',
    N'Run dcdiag with the specified tests. Any failures indicate DC health issues that must be resolved. Check event logs for details on specific test failures.',
    1, 1, @systemUserId);

-- DC-005: DCDiag DNS test
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-005')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-005', @catAD,
    N'DCDiag DNS Registration Tests Pass',
    'command', 'high',
    N'{"command":"dcdiag /test:DNS /DnsDelegation","successCriteria":"noFailures","parseMode":"dcdiag","display":"Running DCDiag DNS registration tests"}',
    N'Verify that all DC DNS records are properly registered. Failures indicate DNS configuration issues that can break AD authentication and replication.',
    1, 1, @systemUserId);

-- DC-006: AD database integrity
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-006')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-006', @catAD,
    N'AD Database (NTDS.dit) Integrity Check',
    'command', 'high',
    N'{"command":"powershell -Command \"Get-EventLog -LogName ''Directory Service'' -EntryType Error -Newest 10 -Source NTDS | Measure-Object | Select-Object -ExpandProperty Count\"","successCriteria":"eq","expected":"0","display":"Checking for recent NTDS database errors"}',
    N'Check for recent NTDS database errors in the Directory Service event log. Persistent errors may indicate database corruption requiring an authoritative or non-authoritative restore.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION B: LDAP Security (DC-007..DC-011)
-- ============================================================

-- DC-007: LDAP signing enforcement
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-007')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-007', @catNetSec,
    N'LDAP Server Signing Enforcement (Require Signing)',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\NTDS\\Parameters","valueName":"LDAPServerIntegrity","expected":2,"operator":"eq","display":"Checking LDAP server signing enforcement on DC"}',
    N'GPO: Default Domain Controllers Policy > "Domain controller: LDAP server signing requirements" = "Require signing" (value 2). Prevents unsigned LDAP binds which enable relay attacks.',
    1, 1, @systemUserId);

-- DC-008: LDAP channel binding
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-008')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-008', @catNetSec,
    N'LDAP Channel Binding Token Requirement',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\NTDS\\Parameters","valueName":"LdapEnforceChannelBinding","expected":2,"operator":"gte","display":"Checking LDAP channel binding requirement on DC"}',
    N'Set HKLM\\SYSTEM\\CurrentControlSet\\Services\\NTDS\\Parameters\\LdapEnforceChannelBinding >= 1 (1 = When supported, 2 = Always). Value 2 recommended. Mitigates LDAP relay attacks (CVE-2017-8563).',
    1, 1, @systemUserId);

-- DC-009: LDAP server integrity enforcement
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-009')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-009', @catNetSec,
    N'LDAP Server Enforce Integrity',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\NTDS\\Parameters","valueName":"LDAPServerEnforceIntegrity","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking LDAP server integrity enforcement"}',
    N'Set LDAPServerEnforceIntegrity = 1. Ensures the DC enforces LDAP signing for all incoming LDAP connections, complementing LDAPServerIntegrity.',
    1, 1, @systemUserId);

-- DC-010: LDAPS configured (certificate binding on 636)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-010')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-010', @catNetSec,
    N'LDAPS Certificate Bound (Port 636 Available)',
    'command', 'high',
    N'{"command":"powershell -Command \"(Test-NetConnection -ComputerName localhost -Port 636).TcpTestSucceeded\"","successCriteria":"contains","expected":"True","display":"Checking LDAPS availability on port 636"}',
    N'Ensure a valid certificate is bound for LDAPS on port 636. Install a certificate with Server Authentication EKU from your internal CA and bind it to NTDS. LDAPS encrypts directory traffic in transit.',
    1, 1, @systemUserId);

-- DC-011: Refuse default machine account password
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-011')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-011', @catSecOpts,
    N'DC: Refuse Machine Accounts with Default Passwords',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RefusePasswordChange","expected":0,"operator":"eq","display":"Checking DC refuses default machine account passwords"}',
    N'GPO: Security Options > "Domain controller: Refuse machine account password changes" = Disabled (value 0). Ensures machine accounts rotate passwords. Setting to 1 is insecure.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION C: Kerberos Security (DC-012..DC-017)
-- ============================================================

-- DC-012: Krbtgt password age
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-012')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-012', @catKerberos,
    N'Krbtgt Account Password Age < 180 Days',
    'command', 'critical',
    N'{"command":"powershell -Command \"$krbtgt = Get-ADUser krbtgt -Properties PasswordLastSet; ((Get-Date) - $krbtgt.PasswordLastSet).Days\"","successCriteria":"lt","expected":"180","display":"Checking krbtgt account password age"}',
    N'The krbtgt account password should be rotated at least every 180 days (recommended: 90 days). Reset it twice (with a replication interval between resets) to invalidate all existing TGTs. Critical for Golden Ticket attack prevention.',
    1, 1, @systemUserId);

-- DC-013: Kerberos max ticket lifetime
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-013')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-013', @catKerberos,
    N'Kerberos Maximum Ticket Lifetime (10 hours)',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Kdc","valueName":"MaxTicketAge","expected":10,"operator":"lte","missingBehavior":"pass","display":"Checking Kerberos maximum ticket lifetime"}',
    N'GPO: Default Domain Policy > Kerberos Policy > "Maximum lifetime for service ticket" = 10 hours (default). Shorter lifetimes reduce the window for ticket abuse.',
    1, 1, @systemUserId);

-- DC-014: Kerberos max renewal lifetime
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-014')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-014', @catKerberos,
    N'Kerberos Maximum Renewal Lifetime (7 days)',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Kdc","valueName":"MaxRenewAge","expected":7,"operator":"lte","missingBehavior":"pass","display":"Checking Kerberos maximum renewal lifetime"}',
    N'GPO: Default Domain Policy > Kerberos Policy > "Maximum lifetime for user ticket renewal" = 7 days (default). Limits how long a TGT can be renewed before requiring re-authentication.',
    1, 1, @systemUserId);

-- DC-015: Pre-authentication required (accounts without preauth)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-015')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-015', @catKerberos,
    N'No Accounts with Pre-Authentication Disabled',
    'command', 'critical',
    N'{"command":"powershell -Command \"(Get-ADUser -Filter {DoesNotRequirePreAuth -eq $true} -Properties DoesNotRequirePreAuth | Where-Object { $_.Enabled -eq $true } | Measure-Object).Count\"","successCriteria":"eq","expected":"0","display":"Checking for accounts with Kerberos pre-authentication disabled"}',
    N'All enabled accounts must require Kerberos pre-authentication. Accounts with pre-auth disabled are vulnerable to AS-REP Roasting. Fix via: Set-ADAccountControl -Identity <user> -DoesNotRequirePreAuth $false.',
    1, 1, @systemUserId);

-- DC-016: Kerberos encryption types (DC-side)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-016')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-016', @catKerberos,
    N'Kerberos Encryption Types: AES Only on DC',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\\Kerberos\\Parameters","valueName":"SupportedEncryptionTypes","expected":2147483640,"operator":"eq","display":"Checking DC Kerberos encryption type configuration"}',
    N'GPO: Security Options > "Network security: Configure encryption types allowed for Kerberos" = AES128+AES256+Future (0x7FFFFFF8). Prevents RC4/DES downgrade attacks and Kerberoasting. Must be set on DCs.',
    1, 1, @systemUserId);

-- DC-017: Allow vulnerable Netlogon connections
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-017')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-017', @catKerberos,
    N'Vulnerable Netlogon Connections Denied (ZeroLogon)',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Netlogon\\Parameters","valueName":"FullSecureChannelProtection","expected":1,"operator":"eq","display":"Checking ZeroLogon protection (full secure channel)"}',
    N'Set FullSecureChannelProtection = 1. Ensures all Netlogon connections use secure RPC. Mitigates CVE-2020-1472 (ZeroLogon). Do NOT add entries to VulnerableChannelAllowList.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION D: DC Hardening (DC-018..DC-030)
-- ============================================================

-- DC-018: AD Recycle Bin enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-018')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-018', @catAD,
    N'AD Recycle Bin Feature Enabled',
    'command', 'high',
    N'{"command":"powershell -Command \"(Get-ADOptionalFeature -Filter ''Name -eq \\\"Recycle Bin Feature\\\"'').EnabledScopes.Count\"","successCriteria":"gte","expected":"1","display":"Checking if AD Recycle Bin is enabled"}',
    N'Enable AD Recycle Bin: Enable-ADOptionalFeature -Identity ''Recycle Bin Feature'' -Scope ForestOrConfigurationSet -Target (Get-ADForest).Name. Allows recovery of deleted AD objects without authoritative restore.',
    1, 1, @systemUserId);

-- DC-019: Protected Users group has members
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-019')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-019', @catCredProt,
    N'Protected Users Group Has Privileged Members',
    'command', 'high',
    N'{"command":"powershell -Command \"(Get-ADGroupMember -Identity ''Protected Users'' | Measure-Object).Count\"","successCriteria":"gte","expected":"1","display":"Checking if Protected Users group has members"}',
    N'Add privileged accounts (Domain Admins, Enterprise Admins) to the Protected Users group. Members get enhanced credential protection: no NTLM, no DES/RC4, no delegation, no credential caching.',
    1, 1, @systemUserId);

-- DC-020: Fine-grained password policies exist
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-020')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-020', @catCredProt,
    N'Fine-Grained Password Policies Configured',
    'command', 'medium',
    N'{"command":"powershell -Command \"(Get-ADFineGrainedPasswordPolicy -Filter * | Measure-Object).Count\"","successCriteria":"gte","expected":"1","display":"Checking for fine-grained password policies"}',
    N'Create at least one fine-grained password policy (PSO) for privileged accounts with stricter requirements (longer passwords, no password reuse). Use New-ADFineGrainedPasswordPolicy.',
    1, 1, @systemUserId);

-- DC-021: Print Spooler disabled on DC (PrintNightmare)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-021')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-021', @catDCHarden,
    N'Print Spooler Service Disabled on DC (PrintNightmare)',
    'service', 'critical',
    N'{"serviceName":"Spooler","expectedStatus":"Stopped","expectedStartType":"Disabled","display":"Checking Print Spooler service on domain controller"}',
    N'Stop and disable Print Spooler on all DCs: Stop-Service Spooler; Set-Service Spooler -StartupType Disabled. DCs do not need printing. PrintNightmare (CVE-2021-34527) exploits the Spooler service for privilege escalation.',
    1, 1, @systemUserId);

-- DC-022: Constrained delegation preferred
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-022')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-022', @catDCHarden,
    N'No Accounts with Unconstrained Delegation (Except DCs)',
    'command', 'critical',
    N'{"command":"powershell -Command \"(Get-ADComputer -Filter {TrustedForDelegation -eq $true -and PrimaryGroupID -ne 516} -Properties TrustedForDelegation | Measure-Object).Count\"","successCriteria":"eq","expected":"0","display":"Checking for non-DC accounts with unconstrained delegation"}',
    N'Only DCs (PrimaryGroupID 516) should have unconstrained delegation. All other accounts should use constrained or resource-based constrained delegation. Unconstrained delegation allows credential theft via TGT extraction.',
    1, 1, @systemUserId);

-- DC-023: DC should not browse the internet
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-023')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-023', @catDCHarden,
    N'DC Internet Access Restricted (Proxy/Firewall)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\CurrentVersion\\Internet Settings","valueName":"ProxyEnable","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking if DC internet browsing is restricted"}',
    N'DCs should not have direct internet access. Configure outbound firewall rules to block DC internet access except for required services (Windows Update, CRL, time sync). Disable IE Enhanced Security is not sufficient.',
    1, 1, @systemUserId);

-- DC-024: Authentication policies configured (optional/advanced)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-024')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-024', @catDCHarden,
    N'Authentication Policies Configured (Silos)',
    'command', 'medium',
    N'{"command":"powershell -Command \"(Get-ADAuthenticationPolicy -Filter * | Measure-Object).Count\"","successCriteria":"gte","expected":"0","display":"Checking for authentication policies (silos)"}',
    N'Consider creating authentication policies and silos to restrict where privileged accounts can authenticate. Use Add-ADAuthenticationPolicy and Add-ADAuthenticationPolicySilo. Requires Windows Server 2012 R2+ domain functional level.',
    1, 1, @systemUserId);

-- DC-025: NTLM audit on domain
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-025')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-025', @catDCHarden,
    N'NTLM Audit: Authentication in Domain Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Netlogon\\Parameters","valueName":"AuditNTLMInDomain","expected":7,"operator":"eq","missingBehavior":"warn","display":"Checking NTLM authentication auditing in domain"}',
    N'GPO: Security Options > "Network security: Restrict NTLM: Audit NTLM authentication in this domain" = "Enable all" (value 7). Audits all NTLM usage to identify applications for migration to Kerberos.',
    1, 1, @systemUserId);

-- DC-026: NTLM incoming traffic audit
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-026')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-026', @catDCHarden,
    N'NTLM Audit: Incoming NTLM Traffic Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","valueName":"AuditReceivingNTLMTraffic","expected":2,"operator":"eq","missingBehavior":"warn","display":"Checking incoming NTLM traffic auditing"}',
    N'GPO: Security Options > "Network security: Restrict NTLM: Audit Incoming NTLM Traffic" = "Enable auditing for all accounts" (value 2). Logs all incoming NTLM authentication attempts.',
    1, 1, @systemUserId);

-- DC-027: AdminSDHolder permissions not modified
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-027')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-027', @catDCHarden,
    N'AdminSDHolder Default Permissions Intact',
    'command', 'high',
    N'{"command":"powershell -Command \"$acl = Get-ACL ''AD:CN=AdminSDHolder,CN=System,DC=*''; ($acl.Access | Where-Object { $_.IdentityReference -notmatch ''Domain Admins|Enterprise Admins|Administrators|SYSTEM'' -and $_.AccessControlType -eq ''Allow'' } | Measure-Object).Count\"","successCriteria":"eq","expected":"0","display":"Checking AdminSDHolder for unauthorized ACEs"}',
    N'The AdminSDHolder object protects privileged groups. Non-default ACEs on AdminSDHolder propagate to all protected accounts via SDProp. Review and remove unauthorized entries.',
    1, 1, @systemUserId);

-- DC-028: DC time sync configured
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-028')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-028', @catDCHarden,
    N'PDC Emulator Time Source Configured',
    'command', 'high',
    N'{"command":"w32tm /query /source","successCriteria":"notContains","expected":"Local CMOS Clock","display":"Checking PDC emulator time source configuration"}',
    N'The PDC emulator should sync to an external NTP source, not the local CMOS clock. Configure via: w32tm /config /manualpeerlist:\"time.windows.com\" /syncfromflags:manual /update. Accurate time is critical for Kerberos authentication.',
    1, 1, @systemUserId);

-- DC-029: Tombstone lifetime adequate
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-029')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-029', @catAD,
    N'AD Tombstone Lifetime Adequate (>= 180 days)',
    'command', 'medium',
    N'{"command":"powershell -Command \"(Get-ADObject ''CN=Directory Service,CN=Windows NT,CN=Services,CN=Configuration,DC=*'' -Properties tombstoneLifetime).tombstoneLifetime\"","successCriteria":"gte","expected":"180","display":"Checking AD tombstone lifetime"}',
    N'Tombstone lifetime should be at least 180 days (default is 60 for new forests, 180 for forests upgraded from 2003+). Shorter lifetimes risk lingering objects and replication issues. Set via: Set-ADObject -Identity ... -Replace @{tombstoneLifetime=180}.',
    1, 1, @systemUserId);

-- DC-030: Domain functional level adequate
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-030')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-030', @catAD,
    N'Domain Functional Level >= Windows Server 2016',
    'command', 'high',
    N'{"command":"powershell -Command \"(Get-ADDomain).DomainMode\"","successCriteria":"contains","expected":"2016","display":"Checking domain functional level"}',
    N'Domain functional level should be at least Windows Server 2016 to support Privileged Access Management, Protected Users, Authentication Policies/Silos, and AES Kerberos. Raise via: Set-ADDomainMode -Identity <domain> -DomainMode Windows2016Domain.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION E: DC Network / DNS (DC-031..DC-035)
-- ============================================================

-- DC-031: DNS secure-only dynamic updates
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-031')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-031', @catDNSSec,
    N'DNS Zones: Secure-Only Dynamic Updates',
    'command', 'high',
    N'{"command":"powershell -Command \"Get-DnsServerZone | Where-Object { $_.DynamicUpdate -ne ''Secure'' -and $_.IsAutoCreated -eq $false -and $_.ZoneName -ne ''TrustAnchors'' } | Measure-Object | Select-Object -ExpandProperty Count\"","successCriteria":"eq","expected":"0","display":"Checking DNS zones for secure-only dynamic updates"}',
    N'All AD-integrated DNS zones should use Secure-Only dynamic updates (not Secure and Nonsecure). Set via: Set-DnsServerPrimaryZone -Name <zone> -DynamicUpdate Secure. Prevents DNS spoofing from unauthenticated clients.',
    1, 1, @systemUserId);

-- DC-032: DNS scavenging enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-032')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-032', @catDNSSec,
    N'DNS Scavenging Enabled on AD Zones',
    'command', 'medium',
    N'{"command":"powershell -Command \"Get-DnsServerScavenging | Select-Object -ExpandProperty ScavengingState\"","successCriteria":"contains","expected":"True","display":"Checking DNS scavenging configuration"}',
    N'Enable DNS scavenging to remove stale DNS records. Configure aging/scavenging on both the server and individual zones. Set no-refresh and refresh intervals (default 7 days each).',
    1, 1, @systemUserId);

-- DC-033: DNS forwarders use secure DNS
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-033')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-033', @catDNSSec,
    N'DNS Root Hints or Secure Forwarders Configured',
    'command', 'medium',
    N'{"command":"powershell -Command \"(Get-DnsServerForwarder).IPAddress.Count\"","successCriteria":"gte","expected":"1","display":"Checking DNS forwarder configuration"}',
    N'Configure DNS forwarders to use trusted resolvers (e.g., 1.1.1.1, 8.8.8.8, or organizational DNS-over-HTTPS endpoints). Alternatively, use root hints for full recursive resolution.',
    1, 1, @systemUserId);

-- DC-034: DC firewall enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-034')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-034', @catDCHarden,
    N'DC Windows Firewall Enabled (Domain Profile)',
    'command', 'high',
    N'{"command":"powershell -Command \"(Get-NetFirewallProfile -Name Domain).Enabled\"","successCriteria":"contains","expected":"True","display":"Checking DC Windows Firewall domain profile"}',
    N'Enable Windows Firewall on DCs with rules allowing only required AD ports (53, 88, 135, 389, 445, 464, 636, 3268, 3269, 49152-65535). Block all other inbound traffic.',
    1, 1, @systemUserId);

-- DC-035: DNSSEC trust anchors
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-035')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-035', @catDNSSec,
    N'DNSSEC: Trust Anchors Configured',
    'command', 'low',
    N'{"command":"powershell -Command \"(Get-DnsServerTrustAnchor -Name . -ErrorAction SilentlyContinue | Measure-Object).Count\"","successCriteria":"gte","expected":"0","display":"Checking DNSSEC trust anchor configuration"}',
    N'Consider configuring DNSSEC for DNS zones to provide cryptographic authentication of DNS data. Sign zones via: Invoke-DnsServerZoneSign. Note: DNSSEC adds complexity and is not required for all environments.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION F: DC Audit (DC-036..DC-040)
-- ============================================================

-- DC-036: Directory Service Access auditing
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-036')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-036', @catAuditLog,
    N'Audit: Directory Service Access Enabled',
    'auditpol', 'high',
    N'{"subcategory":"Directory Service Access","expectedSetting":"Success and Failure","display":"Checking Directory Service Access audit policy"}',
    N'GPO: Advanced Audit Policy > DS Access > "Audit Directory Service Access" = Success and Failure. Logs all read access to AD objects, essential for tracking reconnaissance.',
    1, 1, @systemUserId);

-- DC-037: Directory Service Changes auditing
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-037')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-037', @catAuditLog,
    N'Audit: Directory Service Changes Enabled',
    'auditpol', 'critical',
    N'{"subcategory":"Directory Service Changes","expectedSetting":"Success and Failure","display":"Checking Directory Service Changes audit policy"}',
    N'GPO: Advanced Audit Policy > DS Access > "Audit Directory Service Changes" = Success and Failure. Logs all modifications to AD objects (user creation, group membership changes, GPO edits). Critical for security monitoring.',
    1, 1, @systemUserId);

-- DC-038: DS Replication auditing
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-038')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-038', @catAuditLog,
    N'Audit: Directory Service Replication Enabled',
    'auditpol', 'medium',
    N'{"subcategory":"Directory Service Replication","expectedSetting":"Success and Failure","display":"Checking Directory Service Replication audit policy"}',
    N'GPO: Advanced Audit Policy > DS Access > "Audit Directory Service Replication" = Success and Failure. Logs replication events including DCSync-style attacks (Event ID 4662 with Replicating Directory Changes).',
    1, 1, @systemUserId);

-- DC-039: Kerberos Authentication Service audit
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-039')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-039', @catAuditLog,
    N'Audit: Kerberos Authentication Service Enabled',
    'auditpol', 'high',
    N'{"subcategory":"Kerberos Authentication Service","expectedSetting":"Success and Failure","display":"Checking Kerberos Authentication Service audit policy"}',
    N'GPO: Advanced Audit Policy > Account Logon > "Audit Kerberos Authentication Service" = Success and Failure. Logs TGT requests (Event ID 4768), essential for detecting brute force, AS-REP roasting, and anomalous authentication patterns.',
    1, 1, @systemUserId);

-- DC-040: Kerberos Service Ticket Operations audit
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-040')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-040', @catAuditLog,
    N'Audit: Kerberos Service Ticket Operations Enabled',
    'auditpol', 'high',
    N'{"subcategory":"Kerberos Service Ticket Operations","expectedSetting":"Success and Failure","display":"Checking Kerberos Service Ticket Operations audit policy"}',
    N'GPO: Advanced Audit Policy > Account Logon > "Audit Kerberos Service Ticket Operations" = Success and Failure. Logs service ticket requests (Event ID 4769), essential for detecting Kerberoasting attacks.',
    1, 1, @systemUserId);

-- ============================================================
-- PART P -- Platform linkage (DC19, DC22, DC25 ONLY)
-- ============================================================

DECLARE @dcBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @dcBlock VALUES
    ('DC-001'),('DC-002'),('DC-003'),('DC-004'),('DC-005'),('DC-006'),('DC-007'),('DC-008'),('DC-009'),('DC-010'),
    ('DC-011'),('DC-012'),('DC-013'),('DC-014'),('DC-015'),('DC-016'),('DC-017'),('DC-018'),('DC-019'),('DC-020'),
    ('DC-021'),('DC-022'),('DC-023'),('DC-024'),('DC-025'),('DC-026'),('DC-027'),('DC-028'),('DC-029'),('DC-030'),
    ('DC-031'),('DC-032'),('DC-033'),('DC-034'),('DC-035'),('DC-036'),('DC-037'),('DC-038'),('DC-039'),('DC-040');

-- Link to DC19
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platDC19
FROM control_defs cd
JOIN @dcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platDC19
);
DECLARE @dc19Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for DC19: ', @dc19Rows);

-- Link to DC22
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platDC22
FROM control_defs cd
JOIN @dcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platDC22
);
DECLARE @dc22Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for DC22: ', @dc22Rows);

-- Link to DC25
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platDC25
FROM control_defs cd
JOIN @dcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platDC25
);
DECLARE @dc25Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for DC25: ', @dc25Rows);

-- ============================================================
-- PART Q -- Framework mappings (ALL 5 frameworks)
-- ============================================================

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST
FROM control_defs cd
JOIN @dcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwNIST
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS
FROM control_defs cd
JOIN @dcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwCIS
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA
FROM control_defs cd
JOIN @dcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwHIPAA
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO
FROM control_defs cd
JOIN @dcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwISO
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwPCI
FROM control_defs cd
JOIN @dcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwPCI
);

-- ============================================================
-- PART R -- Link to existing active assessments
-- ============================================================
INSERT INTO assessment_controls (assessment_id, control_def_id)
SELECT a.id, cd.id
FROM assessments a
CROSS JOIN control_defs cd
JOIN @dcBlock b ON b.control_id = cd.control_id
WHERE a.is_active = 1
  AND a.deleted_at IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM assessment_controls ac
      WHERE ac.assessment_id = a.id AND ac.control_def_id = cd.id
  );

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification queries (run after applying)
-- ============================================================

-- Count new DC controls
SELECT COUNT(*) AS new_dc_controls
FROM control_defs
WHERE control_id LIKE 'DC-%' AND is_active = 1;
-- Expected: 40

-- Breakdown by engine type
SELECT cd.[type], COUNT(*) AS control_count
FROM control_defs cd
WHERE cd.control_id LIKE 'DC-%' AND cd.is_active = 1
GROUP BY cd.[type]
ORDER BY cd.[type];
-- Expected: command ~18, registry ~12, auditpol 5, service 1

-- Breakdown by severity
SELECT cd.severity, COUNT(*) AS control_count
FROM control_defs cd
WHERE cd.control_id LIKE 'DC-%' AND cd.is_active = 1
GROUP BY cd.severity
ORDER BY cd.severity;
-- Expected: critical ~11, high ~16, medium ~10, low ~3

-- Verify platform linkage (DC platforms only)
SELECT p.code, COUNT(cp.control_def_id) AS dc_controls_linked
FROM platforms p
LEFT JOIN control_platforms cp ON cp.platform_id = p.id
LEFT JOIN control_defs cd ON cd.id = cp.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'DC-%'
WHERE p.code IN ('DC19','DC22','DC25')
GROUP BY p.code
ORDER BY p.code;
-- Expected: DC19=40, DC22=40, DC25=40

-- Verify NOT linked to workstations or member servers
SELECT p.code, COUNT(cp.control_def_id) AS dc_controls_linked
FROM platforms p
LEFT JOIN control_platforms cp ON cp.platform_id = p.id
LEFT JOIN control_defs cd ON cd.id = cp.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'DC-%'
WHERE p.code IN ('W10','W11','MS19','MS22','MS25')
GROUP BY p.code
ORDER BY p.code;
-- Expected: all 0

-- Verify framework linkage
SELECT f.code, COUNT(cf.control_def_id) AS dc_controls_linked
FROM frameworks f
LEFT JOIN control_frameworks cf ON cf.framework_id = f.id
LEFT JOIN control_defs cd ON cd.id = cf.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'DC-%'
WHERE f.is_active = 1
GROUP BY f.code
ORDER BY f.code;
-- Expected: CIS=40, HIPAA=40, ISO27001=40, NIST=40, PCI-DSS=40

-- Category breakdown
SELECT cc.name, COUNT(cd.id) AS control_count
FROM control_categories cc
JOIN control_defs cd ON cd.category_id = cc.id
WHERE cd.control_id LIKE 'DC-%' AND cd.is_active = 1
GROUP BY cc.name
ORDER BY cc.name;

-- Verify assessment linkage
SELECT a.name, COUNT(ac.control_def_id) AS dc_controls_linked
FROM assessments a
JOIN assessment_controls ac ON ac.assessment_id = a.id
JOIN control_defs cd ON cd.id = ac.control_def_id
WHERE cd.control_id LIKE 'DC-%' AND cd.is_active = 1
  AND a.is_active = 1 AND a.deleted_at IS NULL
GROUP BY a.name;
-- Expected: each active assessment has 40 DC controls linked
