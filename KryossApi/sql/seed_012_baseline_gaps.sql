SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_012_baseline_gaps.sql
-- Kryoss Platform -- Baseline Gap Controls
--
-- Adds controls for settings identified in the Microsoft
-- Security Baselines (Win11 v25H2 / Server 2025) gap analysis
-- that are NOT covered by any existing Kryoss control.
--
-- Control IDs: BLG-001 through BLG-091
-- Prefix BLG = Baseline Gap
--
-- Platform scope: ALL platforms (W10, W11, MS19, MS22, MS25)
-- unless noted otherwise (some are workstation-only or
-- server-only).
--
-- Framework scope: ALL 5 active frameworks.
-- Assessment scope: ALL active assessments.
--
-- Run AFTER seed_011. Fully idempotent (NOT EXISTS guards).
-- ============================================================

BEGIN TRANSACTION;

DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Framework IDs
DECLARE @fwCIS   INT = (SELECT id FROM frameworks WHERE code='CIS');
DECLARE @fwNIST  INT = (SELECT id FROM frameworks WHERE code='NIST');
DECLARE @fwHIPAA INT = (SELECT id FROM frameworks WHERE code='HIPAA');
DECLARE @fwPCI   INT = (SELECT id FROM frameworks WHERE code='PCI-DSS');
DECLARE @fwISO   INT = (SELECT id FROM frameworks WHERE code='ISO27001');

-- Platform IDs (all)
DECLARE @platW10  INT = (SELECT id FROM platforms WHERE code='W10');
DECLARE @platW11  INT = (SELECT id FROM platforms WHERE code='W11');
DECLARE @platMS19 INT = (SELECT id FROM platforms WHERE code='MS19');
DECLARE @platMS22 INT = (SELECT id FROM platforms WHERE code='MS22');
DECLARE @platMS25 INT = (SELECT id FROM platforms WHERE code='MS25');

IF @platW10 IS NULL OR @platW11 IS NULL
BEGIN
    RAISERROR('W10 or W11 platform missing. Run seed_002 first.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- Category lookups
DECLARE @catNetSec     INT = (SELECT id FROM control_categories WHERE name=N'Network Security');
DECLARE @catNetAccess  INT = (SELECT id FROM control_categories WHERE name=N'Network Access');
DECLARE @catSecOpts    INT = (SELECT id FROM control_categories WHERE name=N'Security Options And Local Policy');
DECLARE @catCredProt   INT = (SELECT id FROM control_categories WHERE name=N'Credential Protection');
DECLARE @catHardening  INT = (SELECT id FROM control_categories WHERE name=N'Hardening');
DECLARE @catRemoteDesk INT = (SELECT id FROM control_categories WHERE name=N'Remote Desktop');
DECLARE @catWinRM      INT = (SELECT id FROM control_categories WHERE name=N'WinRM');
DECLARE @catAuditLog   INT = (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring');
DECLARE @catServices   INT = (SELECT id FROM control_categories WHERE name=N'Services Hardening');
DECLARE @catFileSys    INT = (SELECT id FROM control_categories WHERE name=N'File System And Shared Resources');
DECLARE @catCrypto     INT = (SELECT id FROM control_categories WHERE name=N'Certificates And Cryptography');
DECLARE @catRemote     INT = (SELECT id FROM control_categories WHERE name=N'Remote Access');

-- New category for Device Guard / VBS
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Virtualization Based Security')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Virtualization Based Security', 120, @systemUserId);

-- New category for Windows Defender Advanced
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Windows Defender Advanced')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Windows Defender Advanced', 155, @systemUserId);

-- New category for User Account Control
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'User Account Control')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'User Account Control', 135, @systemUserId);

-- New category for AutoPlay and Media
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'AutoPlay And Media')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'AutoPlay And Media', 180, @systemUserId);

DECLARE @catVBS       INT = (SELECT id FROM control_categories WHERE name=N'Virtualization Based Security');
DECLARE @catDefAdv    INT = (SELECT id FROM control_categories WHERE name=N'Windows Defender Advanced');
DECLARE @catUAC       INT = (SELECT id FROM control_categories WHERE name=N'User Account Control');
DECLARE @catAutoPlay  INT = (SELECT id FROM control_categories WHERE name=N'AutoPlay And Media');

-- ============================================================
-- SECTION A: Security Options gaps (gap analysis #1-27)
-- ============================================================

-- Gap #1: Block Microsoft accounts (NaN -- low priority, operational)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-001')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-001', @catSecOpts,
    N'Accounts: Block Microsoft Accounts',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"NoConnectedUser","expected":3,"operator":"eq","missingBehavior":"warn","display":"Checking Microsoft account blocking policy"}',
    N'GPO: Security Options > "Accounts: Block Microsoft accounts" = "Users can''t add or log on with Microsoft accounts" (value 3). Prevents use of Microsoft accounts on domain-joined machines.',
    1, 1, @systemUserId);

-- Gap #2: Domain member: Digitally encrypt or sign secure channel data (always)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-002')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-002', @catNetSec,
    N'Domain Member: Require Secure Channel Signing/Encryption',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Netlogon\\Parameters","valueName":"RequireSignOrSeal","expected":1,"operator":"eq","display":"Checking domain member secure channel requirement"}',
    N'GPO: Security Options > "Domain member: Digitally encrypt or sign secure channel data (always)" = Enabled. Ensures all Netlogon traffic is signed or encrypted.',
    1, 1, @systemUserId);

-- Gap #3: Domain member: Digitally encrypt secure channel data (when possible)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-003')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-003', @catNetSec,
    N'Domain Member: Encrypt Secure Channel Data (When Possible)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Netlogon\\Parameters","valueName":"SealSecureChannel","expected":1,"operator":"eq","display":"Checking domain member secure channel encryption"}',
    N'GPO: Security Options > "Domain member: Digitally encrypt secure channel data (when possible)" = Enabled. Encrypts Netlogon RPC traffic when the DC supports it.',
    1, 1, @systemUserId);

-- Gap #4: Domain member: Digitally sign secure channel data (when possible)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-004')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-004', @catNetSec,
    N'Domain Member: Sign Secure Channel Data (When Possible)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Netlogon\\Parameters","valueName":"SignSecureChannel","expected":1,"operator":"eq","display":"Checking domain member secure channel signing"}',
    N'GPO: Security Options > "Domain member: Digitally sign secure channel data (when possible)" = Enabled. Signs Netlogon RPC traffic when the DC supports it.',
    1, 1, @systemUserId);

-- Gap #5: Domain member: Require strong session key
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-005')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-005', @catNetSec,
    N'Domain Member: Require Strong Session Key',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Netlogon\\Parameters","valueName":"RequireStrongKey","expected":1,"operator":"eq","display":"Checking domain member strong session key requirement"}',
    N'GPO: Security Options > "Domain member: Require strong (Windows 2000 or later) session key" = Enabled. Prevents use of weak 56-bit/40-bit session keys for Netlogon.',
    1, 1, @systemUserId);

-- Gap #6: Smart card removal behavior
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-006')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-006', @catSecOpts,
    N'Interactive Logon: Smart Card Removal Behavior',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon","valueName":"ScRemoveOption","expected":"1","operator":"eq","missingBehavior":"warn","display":"Checking smart card removal behavior"}',
    N'GPO: Security Options > "Interactive logon: Smart card removal behavior" = "Lock Workstation" (value 1). Automatically locks the workstation when the smart card is removed.',
    1, 1, @systemUserId);

-- Gap #7: SMB Client signing required (workstation equivalent of SRV-001)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-007')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-007', @catNetSec,
    N'SMB Client: Require Security Signature (Signing)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters","valueName":"RequireSecuritySignature","expected":1,"operator":"eq","display":"Checking SMB client signing requirement"}',
    N'GPO: Security Options > "Microsoft network client: Digitally sign communications (always)" = Enabled. Requires SMB signing on all client connections to prevent relay/MitM attacks.',
    1, 1, @systemUserId);

-- Gap #8: SMB Client: no plaintext password
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-008')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-008', @catNetSec,
    N'SMB Client: Disable Plaintext Password to Third-Party Servers',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters","valueName":"EnablePlainTextPassword","expected":0,"operator":"eq","display":"Checking SMB client plaintext password policy"}',
    N'GPO: Security Options > "Microsoft network client: Send unencrypted password to third-party SMB servers" = Disabled. Prevents sending cleartext credentials to non-Microsoft SMB servers.',
    1, 1, @systemUserId);

-- Gap #9: SMB Server signing required (workstation scope -- SRV-001 is server-only)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-009')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-009', @catNetSec,
    N'SMB Server: Require Security Signature (Workstation Scope)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanManServer\\Parameters","valueName":"RequireSecuritySignature","expected":1,"operator":"eq","display":"Checking SMB server signing on workstation"}',
    N'GPO: Security Options > "Microsoft network server: Digitally sign communications (always)" = Enabled. Ensures workstations acting as SMB servers also require signing.',
    1, 1, @systemUserId);

-- Gap #10: Network access: Do not allow storage of passwords (NaN -- review)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-010')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-010', @catCredProt,
    N'Disable Storage of Passwords for Network Authentication',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"DisableDomainCreds","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking domain credential storage policy"}',
    N'GPO: Security Options > "Network access: Do not allow storage of passwords and credentials for network authentication" = Enabled. Prevents caching of domain credentials in Credential Manager.',
    1, 1, @systemUserId);

-- Gap #11: Everyone includes anonymous
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-011')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-011', @catNetAccess,
    N'Network Access: Everyone Excludes Anonymous Users',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"EveryoneIncludesAnonymous","expected":0,"operator":"eq","display":"Checking anonymous user inclusion in Everyone group"}',
    N'GPO: Security Options > "Network access: Let Everyone permissions apply to anonymous users" = Disabled. Prevents anonymous users from inheriting permissions assigned to the Everyone group.',
    1, 1, @systemUserId);

-- Gap #12: Restrict null session access (workstation scope -- SRV-003 is server-only)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-012')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-012', @catNetAccess,
    N'Restrict Null Session Access to Named Pipes/Shares (Workstation)',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanManServer\\Parameters","valueName":"RestrictNullSessAccess","expected":1,"operator":"eq","display":"Checking null session access restriction on workstation"}',
    N'GPO: Security Options > "Network access: Restrict anonymous access to Named Pipes and Shares" = Enabled. Prevents unauthenticated access to shared resources on workstations.',
    1, 1, @systemUserId);

-- Gap #13: Restrict remote SAM calls
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-013')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-013', @catNetAccess,
    N'Restrict Remote Calls to SAM',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RestrictRemoteSAM","expected":"O:BAG:BAD:(A;;RC;;;BA)","operator":"eq","missingBehavior":"fail","display":"Checking remote SAM access restriction"}',
    N'GPO: Security Options > "Network access: Restrict clients allowed to make remote calls to SAM" = "O:BAG:BAD:(A;;RC;;;BA)" (Administrators only). Prevents non-admin enumeration of local accounts.',
    1, 1, @systemUserId);

-- Gap #14: Allow LocalSystem NULL session fallback (NaN -- review)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-014')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-014', @catNetSec,
    N'Disable LocalSystem NULL Session Fallback',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","valueName":"AllowNullSessionFallback","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking NULL session fallback for LocalSystem"}',
    N'Set HKLM\SYSTEM\CurrentControlSet\Control\Lsa\MSV1_0\AllowNullSessionFallback = 0. Prevents LocalSystem from falling back to NULL sessions for NTLM authentication.',
    1, 1, @systemUserId);

-- Gap #15: Kerberos encryption types (CRITICAL)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-015')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-015', @catCrypto,
    N'Kerberos Encryption Types: AES Only (No RC4/DES)',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\\Kerberos\\Parameters","valueName":"SupportedEncryptionTypes","expected":2147483640,"operator":"eq","display":"Checking Kerberos encryption type configuration"}',
    N'GPO: Security Options > "Network security: Configure encryption types allowed for Kerberos" = AES128_HMAC_SHA1 + AES256_HMAC_SHA1 + Future types (0x7FFFFFF8). Prevents downgrade to RC4/DES. Critical for preventing Kerberoasting.',
    1, 1, @systemUserId);

-- Gap #16: LDAP client signing (workstation scope -- SRV-011 is server-only)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-016')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-016', @catNetSec,
    N'LDAP Client Signing Required (Workstation)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LDAP","valueName":"LDAPClientIntegrity","expected":1,"operator":"gte","display":"Checking LDAP client signing on workstation"}',
    N'GPO: Security Options > "Network security: LDAP client signing requirements" = "Negotiate signing" (1) or higher. Prevents LDAP relay attacks from workstations.',
    1, 1, @systemUserId);

-- Gap #17: NTLM minimum client session security
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-017')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-017', @catNetSec,
    N'NTLM Minimum Session Security (Client): NTLMv2 + 128-bit',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","valueName":"NtlmMinClientSec","expected":537395200,"operator":"eq","display":"Checking NTLM client minimum session security"}',
    N'GPO: Security Options > "Network security: Minimum session security for NTLM SSP based (including secure RPC) clients" = "Require NTLMv2 session security, Require 128-bit encryption" (537395200).',
    1, 1, @systemUserId);

-- Gap #18: NTLM minimum server session security
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-018')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-018', @catNetSec,
    N'NTLM Minimum Session Security (Server): NTLMv2 + 128-bit',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","valueName":"NtlmMinServerSec","expected":537395200,"operator":"eq","display":"Checking NTLM server minimum session security"}',
    N'GPO: Security Options > "Network security: Minimum session security for NTLM SSP based (including secure RPC) servers" = "Require NTLMv2 session security, Require 128-bit encryption" (537395200).',
    1, 1, @systemUserId);

-- Gap #19: System objects: case insensitivity (NaN -- review)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-019')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-019', @catSecOpts,
    N'System Objects: Require Case Insensitivity for Non-Windows Subsystems',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Kernel","valueName":"ObCaseInsensitive","expected":1,"operator":"eq","missingBehavior":"pass","display":"Checking case insensitivity requirement for subsystems"}',
    N'GPO: Security Options > "System objects: Require case insensitivity for non-Windows subsystems" = Enabled. Ensures consistent name resolution across all subsystems.',
    1, 1, @systemUserId);

-- Gap #20: System objects: Strengthen default permissions (NaN -- review)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-020')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-020', @catSecOpts,
    N'System Objects: Strengthen Default Permissions of Internal System Objects',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Session Manager","valueName":"ProtectionMode","expected":1,"operator":"eq","missingBehavior":"pass","display":"Checking system object default permissions strength"}',
    N'GPO: Security Options > "System objects: Strengthen default permissions of internal system objects" = Enabled. Strengthens DACLs on system objects to reduce privilege escalation risk.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION B: UAC gaps (gap analysis #21-27)
-- ============================================================

-- Gap #21: UAC ConsentPromptBehaviorAdmin
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-021')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-021', @catUAC,
    N'UAC: Admin Elevation Prompt on Secure Desktop',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"ConsentPromptBehaviorAdmin","expected":2,"operator":"eq","display":"Checking UAC admin elevation prompt behavior"}',
    N'GPO: Security Options > "User Account Control: Behavior of the elevation prompt for administrators in Admin Approval Mode" = "Prompt for consent on the secure desktop" (value 2). Prevents silent or auto-elevated admin actions.',
    1, 1, @systemUserId);

-- Gap #22: UAC ConsentPromptBehaviorUser
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-022')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-022', @catUAC,
    N'UAC: Standard User Elevation Auto-Deny',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"ConsentPromptBehaviorUser","expected":0,"operator":"eq","display":"Checking UAC standard user elevation behavior"}',
    N'GPO: Security Options > "User Account Control: Behavior of the elevation prompt for standard users" = "Automatically deny elevation requests" (value 0). Prevents social engineering via elevation prompts for standard users.',
    1, 1, @systemUserId);

-- Gap #23: UAC EnableInstallerDetection
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-023')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-023', @catUAC,
    N'UAC: Detect Application Installations and Prompt',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"EnableInstallerDetection","expected":1,"operator":"eq","display":"Checking UAC installer detection"}',
    N'GPO: Security Options > "User Account Control: Detect application installations and prompt for elevation" = Enabled. Detects setup programs and prompts for admin credentials.',
    1, 1, @systemUserId);

-- Gap #24: UAC EnableSecureUIAPaths
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-024')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-024', @catUAC,
    N'UAC: Only Elevate UIAccess Apps from Secure Locations',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"EnableSecureUIAPaths","expected":1,"operator":"eq","display":"Checking UIAccess secure location restriction"}',
    N'GPO: Security Options > "User Account Control: Only elevate UIAccess applications that are installed in secure locations" = Enabled. Prevents UIAccess binaries in non-trusted paths from elevating.',
    1, 1, @systemUserId);

-- Gap #25: UAC EnableLUA (Admin Approval Mode)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-025')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-025', @catUAC,
    N'UAC: Run All Administrators in Admin Approval Mode',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"EnableLUA","expected":1,"operator":"eq","display":"Checking Admin Approval Mode status"}',
    N'GPO: Security Options > "User Account Control: Run all administrators in Admin Approval Mode" = Enabled. Disabling this effectively disables UAC entirely. Critical security setting.',
    1, 1, @systemUserId);

-- Gap #26: UAC PromptOnSecureDesktop
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-026')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-026', @catUAC,
    N'UAC: Switch to Secure Desktop When Prompting',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"PromptOnSecureDesktop","expected":1,"operator":"eq","display":"Checking UAC secure desktop usage"}',
    N'GPO: Security Options > "User Account Control: Switch to the secure desktop when prompting for elevation" = Enabled. Prevents malware from spoofing or automating the elevation prompt.',
    1, 1, @systemUserId);

-- Gap #27: UAC EnableVirtualization
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-027')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-027', @catUAC,
    N'UAC: Virtualize File/Registry Write Failures',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"EnableVirtualization","expected":1,"operator":"eq","display":"Checking UAC write failure virtualization"}',
    N'GPO: Security Options > "User Account Control: Virtualize file and registry write failures to per-user locations" = Enabled. Redirects legacy app write failures to per-user locations instead of system locations.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION C: Credential Delegation / CredSSP (gap analysis D.1)
-- ============================================================

-- Gap D.1 #1: CredSSP Oracle Remediation
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-028')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-028', @catCredProt,
    N'CredSSP: Encryption Oracle Remediation (Force Updated Clients)',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\\CredSSP\\Parameters","valueName":"AllowEncryptionOracle","expected":0,"operator":"eq","missingBehavior":"fail","display":"Checking CredSSP encryption oracle remediation"}',
    N'GPO: Computer Configuration > Administrative Templates > System > Credentials Delegation > "Encryption Oracle Remediation" = "Force Updated Clients" (value 0). Prevents CVE-2018-0886 CredSSP downgrade attack.',
    1, 1, @systemUserId);

-- Gap D.1 #2: Remote host delegation of non-exportable credentials
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-029')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-029', @catCredProt,
    N'CredSSP: Allow Delegation of Non-Exportable Credentials',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\CredentialsDelegation","valueName":"AllowProtectedCreds","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking non-exportable credential delegation"}',
    N'GPO: Credentials Delegation > "Remote host allows delegation of non-exportable credentials" = Enabled. Uses Restricted Admin or Remote Credential Guard to protect credentials during RDP.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION D: Device Guard / VBS (gap analysis D.2)
-- ============================================================

-- Gap D.2 #3: VBS enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-030')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-030', @catVBS,
    N'Device Guard: Virtualization Based Security Enabled',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\DeviceGuard","valueName":"EnableVirtualizationBasedSecurity","expected":1,"operator":"eq","missingBehavior":"fail","display":"Checking VBS enablement status"}',
    N'GPO: Computer Configuration > Administrative Templates > System > Device Guard > "Turn On Virtualization Based Security" = Enabled. VBS uses hardware virtualization to isolate critical processes.',
    1, 1, @systemUserId);

-- Gap D.2 #4: Platform Security Level
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-031')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-031', @catVBS,
    N'Device Guard: Platform Security Level (Secure Boot + DMA)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\DeviceGuard","valueName":"RequirePlatformSecurityFeatures","expected":3,"operator":"gte","missingBehavior":"fail","display":"Checking VBS platform security level"}',
    N'GPO: Device Guard > "Turn On Virtualization Based Security" > "Select Platform Security Level" = "Secure Boot and DMA Protection" (value 3). Ensures VBS uses full hardware protection.',
    1, 1, @systemUserId);

-- Gap D.2 #5: Credential Guard with UEFI lock
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-032')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-032', @catVBS,
    N'Device Guard: Credential Guard with UEFI Lock',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\DeviceGuard\\Scenarios\\HypervisorEnforcedCodeIntegrity","valueName":"Enabled","expected":1,"operator":"eq","missingBehavior":"fail","display":"Checking Credential Guard UEFI lock status"}',
    N'GPO: Device Guard > "Credential Guard Configuration" = "Enabled with UEFI lock". UEFI lock prevents remote disabling of Credential Guard. Requires VBS (BLG-030).',
    1, 1, @systemUserId);

-- Gap D.2 #5b: Credential Guard LsaCfgFlags
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-033')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-033', @catVBS,
    N'Device Guard: LSA Credential Guard Configuration Flag',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"LsaCfgFlags","expected":1,"operator":"gte","missingBehavior":"fail","display":"Checking LSA Credential Guard configuration"}',
    N'Set HKLM\SYSTEM\CurrentControlSet\Control\Lsa\LsaCfgFlags = 1 (Enabled with UEFI lock) or 2. Configures Credential Guard protection for LSA secrets.',
    1, 1, @systemUserId);

-- Gap D.2 #6: Secure Launch
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-034')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-034', @catVBS,
    N'Device Guard: Secure Launch (System Guard)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\DeviceGuard","valueName":"ConfigureSystemGuardLaunch","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking System Guard Secure Launch status"}',
    N'GPO: Device Guard > "Secure Launch Configuration" = Enabled. System Guard uses DRTM (Dynamic Root of Trust for Measurement) to validate boot integrity.',
    1, 1, @systemUserId);

-- Gap D.2 #7: Kernel Shadow Stacks
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-035')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-035', @catVBS,
    N'Device Guard: Kernel-mode Hardware-enforced Stack Protection',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\DeviceGuard","valueName":"ConfigureKernelShadowStacks","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking kernel shadow stack enforcement"}',
    N'GPO: Device Guard > "Kernel-mode Hardware-enforced Stack Protection" = "Enabled in enforcement mode". New Win11 25H2 feature using Intel CET to detect ROP/JOP attacks in kernel mode.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION E: Early Launch Anti-Malware (gap analysis D.3)
-- ============================================================

-- Gap D.3 #8: ELAM Boot-Start Driver Init Policy
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-036')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-036', @catHardening,
    N'ELAM: Boot-Start Driver Initialization Policy',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Policies\\EarlyLaunch","valueName":"DriverLoadPolicy","expected":3,"operator":"eq","missingBehavior":"warn","display":"Checking ELAM boot-start driver initialization policy"}',
    N'GPO: Computer Configuration > Administrative Templates > System > Early Launch Antimalware > "Boot-Start Driver Initialization Policy" = "Good, unknown and bad but critical" (value 3). Controls which boot drivers are loaded based on ELAM classification.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION F: LSA Protection (gap analysis D.4)
-- ============================================================

-- Gap D.4 #9: LSASS RunAsPPL = 2 (UEFI Lock)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-037')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-037', @catCredProt,
    N'LSASS: Protected Process with UEFI Lock (RunAsPPL = 2)',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RunAsPPL","expected":2,"operator":"eq","display":"Checking LSASS UEFI-locked protected process status"}',
    N'GPO: Computer Configuration > Administrative Templates > System > Local Security Authority > "Configures LSASS to run as a protected process" = "Enabled with UEFI Lock" (value 2). Win11 25H2 baseline requires value 2 (UEFI lock), not just 1. Prevents credential dumping tools.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION G: Network Settings (gap analysis D.5)
-- Note: Items 11-22 from gap analysis are noted as already
-- covered by BL-0016..BL-0028 and SC-128. Only items 1-2 and
-- 10 are true gaps.
-- ============================================================

-- Gap D.5 #1: CredSSP Oracle (already BLG-028 above)
-- Gap D.5 #2: already BLG-029 above

-- Gap D.5 #10: DNS over HTTPS
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-038')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-038', @catNetSec,
    N'DNS Client: Enable DNS over HTTPS (DoH)',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient","valueName":"DoHPolicy","expected":2,"operator":"gte","missingBehavior":"warn","display":"Checking DNS over HTTPS policy"}',
    N'GPO: Computer Configuration > Administrative Templates > Network > DNS Client > "Configure DNS over HTTPS (DoH) name resolution" = Enabled. Encrypts DNS queries to prevent eavesdropping and tampering.',
    1, 1, @systemUserId);

-- Gap D.5 #13: SMB Server audit client no encryption
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-039')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-039', @catAuditLog,
    N'SMB Server: Audit Client Does Not Support Encryption',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"AuditClientDoesNotSupportEncryption","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking SMB encryption audit setting"}',
    N'Set LanmanServer\\Parameters\\AuditClientDoesNotSupportEncryption = 1. Logs events when SMB clients connect without encryption support, aiding in identifying legacy clients.',
    1, 1, @systemUserId);

-- Gap D.5 #14: SMB Server audit client no signing
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-040')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-040', @catAuditLog,
    N'SMB Server: Audit Client Does Not Support Signing',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"AuditClientDoesNotSupportSigning","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking SMB signing audit setting"}',
    N'Set LanmanServer\\Parameters\\AuditClientDoesNotSupportSigning = 1. Logs events when SMB clients connect without signing support.',
    1, 1, @systemUserId);

-- Gap D.5 #15: SMB authentication rate limiter
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-041')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-041', @catNetSec,
    N'SMB Server: Authentication Rate Limiter Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"EnableAuthRateLimiter","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking SMB authentication rate limiting"}',
    N'Set LanmanServer\\Parameters\\EnableAuthRateLimiter = 1. Limits SMB authentication attempts to slow brute-force attacks. New in Windows Server 2025 / Win11 25H2.',
    1, 1, @systemUserId);

-- Gap D.5 #16: Audit insecure guest logon
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-042')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-042', @catAuditLog,
    N'SMB Client: Audit Insecure Guest Logon',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\LanmanWorkstation","valueName":"AuditInsecureGuestLogon","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking SMB insecure guest logon audit"}',
    N'GPO: Computer Configuration > Administrative Templates > Network > Lanman Workstation > "Audit insecure guest logon" = Enabled. Logs when SMB client falls back to guest authentication.',
    1, 1, @systemUserId);

-- Gap D.5 #17: SMB Client audit server no encryption
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-043')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-043', @catAuditLog,
    N'SMB Client: Audit Server Does Not Support Encryption',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\LanmanWorkstation","valueName":"AuditServerDoesNotSupportEncryption","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking SMB client encryption audit setting"}',
    N'GPO: Lanman Workstation > "Audit server does not support encryption" = Enabled. Logs when SMB servers do not support encryption.',
    1, 1, @systemUserId);

-- Gap D.5 #18: SMB Client audit server no signing
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-044')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-044', @catAuditLog,
    N'SMB Client: Audit Server Does Not Support Signing',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\LanmanWorkstation","valueName":"AuditServerDoesNotSupportSigning","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking SMB client signing audit setting"}',
    N'GPO: Lanman Workstation > "Audit server does not support signing" = Enabled. Logs when SMB servers do not support signing.',
    1, 1, @systemUserId);

-- Gap D.5 #19: Hardened UNC Paths
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-045')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-045', @catNetSec,
    N'Network Provider: Hardened UNC Paths (SYSVOL)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\NetworkProvider\\HardenedPaths","valueName":"\\\\*\\SYSVOL","expected":"RequireMutualAuthentication=1, RequireIntegrity=1, RequirePrivacy=1","operator":"eq","missingBehavior":"fail","display":"Checking hardened UNC paths for SYSVOL"}',
    N'GPO: Computer Configuration > Administrative Templates > Network > Network Provider > "Hardened UNC Paths" with \\*\\SYSVOL = RequireMutualAuthentication=1, RequireIntegrity=1, RequirePrivacy=1. Prevents GPO spoofing via UNC path manipulation.',
    1, 1, @systemUserId);

-- Gap D.5 #19b: Hardened UNC Paths (NETLOGON)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-046')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-046', @catNetSec,
    N'Network Provider: Hardened UNC Paths (NETLOGON)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\NetworkProvider\\HardenedPaths","valueName":"\\\\*\\NETLOGON","expected":"RequireMutualAuthentication=1, RequireIntegrity=1, RequirePrivacy=1","operator":"eq","missingBehavior":"fail","display":"Checking hardened UNC paths for NETLOGON"}',
    N'GPO: Network Provider > "Hardened UNC Paths" with \\*\\NETLOGON = RequireMutualAuthentication=1, RequireIntegrity=1, RequirePrivacy=1. Protects logon scripts from interception.',
    1, 1, @systemUserId);

-- Gap D.5 #20: Disable Mailslots
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-047')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-047', @catNetSec,
    N'Network Provider: Disable Mailslots',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\NetworkProvider","valueName":"EnableMailslots","expected":0,"operator":"eq","missingBehavior":"warn","display":"Checking mailslot protocol status"}',
    N'GPO: Network Provider > "Enable Mailslots" = Disabled. Mailslots are an unauthenticated legacy protocol used for NetBIOS name resolution and browsing.',
    1, 1, @systemUserId);

-- Gap D.5 #22: Prohibit non-domain connections
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-048')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-048', @catNetSec,
    N'WCM: Prohibit Non-Domain Network When Domain Connected',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WcmSvc\\GroupPolicy","valueName":"fBlockNonDomain","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking simultaneous network connection policy"}',
    N'GPO: Computer Configuration > Administrative Templates > Network > Windows Connection Manager > "Prohibit connection to non-domain networks when connected to domain authenticated network" = Enabled.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION H: Windows Defender Advanced (gap analysis D.6)
-- ============================================================

-- Gap D.6 #23: Local setting override for MAPS reporting
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-049')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-049', @catDefAdv,
    N'Defender: Disable Local Override for MAPS Reporting',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\SpyNet","valueName":"LocalSettingOverrideSpyNetReporting","expected":0,"operator":"eq","missingBehavior":"warn","display":"Checking Defender MAPS local override setting"}',
    N'GPO: Windows Defender > MAPS > "Configure local setting override for reporting to Microsoft MAPS" = Disabled (value 0). Prevents local users from overriding MAPS reporting configuration.',
    1, 1, @systemUserId);

-- Gap D.6 #24: Cloud Block Level
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-050')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-050', @catDefAdv,
    N'Defender: Cloud Block Level (High)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\MpEngine","valueName":"MpCloudBlockLevel","expected":2,"operator":"gte","missingBehavior":"warn","display":"Checking Defender cloud block level"}',
    N'GPO: Windows Defender > MpEngine > "Cloud Block Level" = "High" (value 2) or above. Higher levels increase detection aggressiveness using cloud-based heuristics.',
    1, 1, @systemUserId);

-- Gap D.6 #25: Extended cloud check timeout
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-051')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-051', @catDefAdv,
    N'Defender: Extended Cloud Check Timeout (50s)',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\MpEngine","valueName":"MpBafsExtendedTimeout","expected":50,"operator":"gte","missingBehavior":"warn","display":"Checking Defender extended cloud check timeout"}',
    N'GPO: Windows Defender > MpEngine > "Configure extended cloud check" = 50 seconds. Allows more time for cloud-based analysis before allowing potentially malicious files.',
    1, 1, @systemUserId);

-- Gap D.6 #26: Disable routine remediation override
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-052')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-052', @catDefAdv,
    N'Defender: Routine Remediation Enabled (Not Disabled)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender","valueName":"DisableRoutinelyTakingAction","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Defender routine remediation status"}',
    N'GPO: Windows Defender > "Turn off routine remediation" = Disabled (value 0 or not present). Ensures Defender automatically remediates detected threats.',
    1, 1, @systemUserId);

-- Gap D.6 #27: Auto exclusions not disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-053')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-053', @catDefAdv,
    N'Defender: Auto Exclusions Enabled (Not Disabled)',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Exclusions","valueName":"DisableAutoExclusions","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Defender auto exclusions status"}',
    N'GPO: Windows Defender > Exclusions > "Turn off auto exclusions" = Disabled (value 0 or not present). Auto exclusions improve server performance without reducing protection.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION I: SmartScreen / WTDS (gap analysis D.7)
-- ============================================================

-- Gap D.7 #28: SmartScreen level
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-054')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-054', @catHardening,
    N'SmartScreen: Warn and Prevent Bypass',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\System","valueName":"ShellSmartScreenLevel","expected":"Block","operator":"eq","missingBehavior":"fail","display":"Checking SmartScreen enforcement level"}',
    N'GPO: Windows Components > Windows Defender SmartScreen > Explorer > "Configure Windows Defender SmartScreen" = Enabled with "Warn and prevent bypass". Prevents users from ignoring SmartScreen warnings.',
    1, 1, @systemUserId);

-- Gap D.7 #29: WTDS Notify Malicious
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-055')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-055', @catHardening,
    N'Enhanced Phishing Protection: Notify Malicious',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WTDS\\Components","valueName":"NotifyMalicious","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking WTDS malicious notification"}',
    N'GPO: Windows Components > Windows Defender SmartScreen > Enhanced Phishing Protection > "Notify Malicious" = Enabled. Alerts users when they enter passwords on known malicious sites.',
    1, 1, @systemUserId);

-- Gap D.7 #30: WTDS Notify Password Reuse
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-056')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-056', @catHardening,
    N'Enhanced Phishing Protection: Notify Password Reuse',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WTDS\\Components","valueName":"NotifyPasswordReuse","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking WTDS password reuse notification"}',
    N'GPO: Enhanced Phishing Protection > "Notify Password Reuse" = Enabled. Warns users when they reuse their Windows password on other sites.',
    1, 1, @systemUserId);

-- Gap D.7 #31: WTDS Notify Unsafe App
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-057')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-057', @catHardening,
    N'Enhanced Phishing Protection: Notify Unsafe App',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WTDS\\Components","valueName":"NotifyUnsafeApp","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking WTDS unsafe app notification"}',
    N'GPO: Enhanced Phishing Protection > "Notify Unsafe App" = Enabled. Warns users when they paste passwords into unsafe applications.',
    1, 1, @systemUserId);

-- Gap D.7 #32: WTDS Service Enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-058')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-058', @catHardening,
    N'Enhanced Phishing Protection: Service Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WTDS\\Components","valueName":"ServiceEnabled","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking WTDS service status"}',
    N'GPO: Enhanced Phishing Protection > "Service Enabled" = Enabled. Enables the Windows Threat Detection Service for phishing protection.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION J: RDP (gap analysis D.8) -- workstation scope
-- (SRV-014/015/018 cover servers, these are for W10/W11)
-- ============================================================

-- Gap D.8 #33: Do not allow passwords to be saved
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-059')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-059', @catRemoteDesk,
    N'RDP: Do Not Allow Password Saving (Workstation)',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","valueName":"DisablePasswordSaving","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking RDP password saving restriction"}',
    N'GPO: Remote Desktop Services > Remote Desktop Connection Client > "Do not allow passwords to be saved" = Enabled. Prevents storing RDP credentials which could be extracted by attackers.',
    1, 1, @systemUserId);

-- Gap D.8 #34: Require secure RPC communication
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-060')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-060', @catRemoteDesk,
    N'RDP: Require Secure RPC Communication (Workstation)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","valueName":"fEncryptRPCTraffic","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking RDP secure RPC requirement"}',
    N'GPO: Remote Desktop Session Host > Security > "Require secure RPC communication" = Enabled. Encrypts RPC traffic between RDP client and server.',
    1, 1, @systemUserId);

-- Gap D.8 #35: Client connection encryption level
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-061')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-061', @catRemoteDesk,
    N'RDP: Minimum Encryption Level High (Workstation)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Terminal Server\\WinStations\\RDP-Tcp","valueName":"MinEncryptionLevel","expected":3,"operator":"gte","display":"Checking RDP minimum encryption level on workstation"}',
    N'GPO: Remote Desktop Session Host > Security > "Set client connection encryption level" = High (3). Ensures 128-bit encryption for RDP sessions on workstations.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION K: WinRM (gap analysis D.9)
-- ============================================================

-- Gap D.9 #36: WinRM Client Basic auth
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-062')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-062', @catWinRM,
    N'WinRM Client: Basic Authentication Disabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Client","valueName":"AllowBasic","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking WinRM client basic authentication"}',
    N'GPO: WinRM > WinRM Client > "Allow Basic authentication" = Disabled. Prevents sending credentials in cleartext via WinRM client connections.',
    1, 1, @systemUserId);

-- Gap D.9 #37: WinRM Client unencrypted traffic
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-063')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-063', @catWinRM,
    N'WinRM Client: Unencrypted Traffic Disabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Client","valueName":"AllowUnencryptedTraffic","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking WinRM client unencrypted traffic"}',
    N'GPO: WinRM > WinRM Client > "Allow unencrypted traffic" = Disabled. Prevents WinRM client from sending data in cleartext.',
    1, 1, @systemUserId);

-- Gap D.9 #38: WinRM Service Basic auth (workstation scope -- SRV-022 is server-only)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-064')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-064', @catWinRM,
    N'WinRM Service: Basic Authentication Disabled (Workstation)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Service","valueName":"AllowBasic","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking WinRM service basic authentication on workstation"}',
    N'GPO: WinRM > WinRM Service > "Allow Basic authentication" = Disabled. Prevents receiving credentials in cleartext via WinRM on workstations.',
    1, 1, @systemUserId);

-- Gap D.9 #39: WinRM Service unencrypted traffic
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-065')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-065', @catWinRM,
    N'WinRM Service: Unencrypted Traffic Disabled (Workstation)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Service","valueName":"AllowUnencryptedTraffic","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking WinRM service unencrypted traffic on workstation"}',
    N'GPO: WinRM > WinRM Service > "Allow unencrypted traffic" = Disabled. Prevents WinRM service from accepting unencrypted data on workstations.',
    1, 1, @systemUserId);

-- Gap D.9 #40: WinRM Service Digest auth
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-066')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-066', @catWinRM,
    N'WinRM Service: Digest Authentication Disabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Service","valueName":"AllowDigest","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking WinRM service digest authentication"}',
    N'GPO: WinRM > WinRM Service > "Disallow Digest authentication" = Enabled (AllowDigest = 0). Digest auth is weaker than Kerberos/NTLM and should be disabled.',
    1, 1, @systemUserId);

-- Gap D.9 #41: WinRM Service DisableRunAs
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-067')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-067', @catWinRM,
    N'WinRM Service: Disallow RunAs Credentials',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Service","valueName":"DisableRunAs","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking WinRM RunAs credential storage"}',
    N'GPO: WinRM > WinRM Service > "Disallow WinRM from storing RunAs credentials" = Enabled. Prevents storing service account credentials in WinRM configuration.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION L: Miscellaneous Windows Components (gap analysis D.10)
-- ============================================================

-- Gap D.10 #42: Block Universal Windows apps with WinRT API from hosted content
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-068')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-068', @catHardening,
    N'App Runtime: Block Hosted App WinRT API Access',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\\AppPrivacy","valueName":"BlockHostedAppAccessWinRT","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking hosted app WinRT access restriction"}',
    N'GPO: Windows Components > App runtime > "Block launching Universal Windows apps with Windows Runtime API access from hosted content" = Enabled.',
    1, 1, @systemUserId);

-- Gap D.10 #43: Autoplay for non-volume devices
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-069')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-069', @catAutoPlay,
    N'AutoPlay: Disallow for Non-Volume Devices',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\Explorer","valueName":"NoAutoplayfornonVolume","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking autoplay restriction for non-volume devices"}',
    N'GPO: Windows Components > AutoPlay Policies > "Disallow Autoplay for non-volume devices" = Enabled. Prevents autorun from MTP devices, cameras, and other non-volume devices.',
    1, 1, @systemUserId);

-- Gap D.10 #44: Default AutoRun behavior
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-070')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-070', @catAutoPlay,
    N'AutoPlay: Do Not Execute Any AutoRun Commands',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer","valueName":"NoAutorun","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking default AutoRun behavior"}',
    N'GPO: Windows Components > AutoPlay Policies > "Set the default behavior for AutoRun" = "Do not execute any autorun commands". Prevents automatic execution of autorun.inf commands.',
    1, 1, @systemUserId);

-- Gap D.10 #45: Turn off Autoplay (all drives)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-071')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-071', @catAutoPlay,
    N'AutoPlay: Turn Off for All Drives',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer","valueName":"NoDriveTypeAutoRun","expected":255,"operator":"eq","missingBehavior":"warn","display":"Checking Autoplay status for all drives"}',
    N'GPO: Windows Components > AutoPlay Policies > "Turn off Autoplay" = "All drives" (value 255). Disables autoplay on all drive types including USB, CD/DVD, and network drives.',
    1, 1, @systemUserId);

-- Gap D.10 #46: BitLocker deny write to unprotected removable drives
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-072')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-072', @catHardening,
    N'BitLocker: Deny Write to Unprotected Removable Drives',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\FVE","valueName":"RDVDenyWriteAccess","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking BitLocker removable drive write protection"}',
    N'GPO: Windows Components > BitLocker Drive Encryption > Removable Data Drives > "Deny write access to removable drives not protected by BitLocker" = Enabled. Prevents data leakage via unencrypted USB drives.',
    1, 1, @systemUserId);

-- Gap D.10 #47: Cloud consumer account state content
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-073')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-073', @catHardening,
    N'Cloud Content: Disable Consumer Account State Content',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent","valueName":"DisableConsumerAccountStateContent","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking cloud consumer account state content"}',
    N'GPO: Windows Components > Cloud Content > "Turn off cloud consumer account state content" = Enabled. Prevents consumer account suggestions and promotions in enterprise environments.',
    1, 1, @systemUserId);

-- Gap D.10 #48: Cloud optimized content
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-074')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-074', @catHardening,
    N'Cloud Content: Disable Cloud Optimized Content',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent","valueName":"DisableCloudOptimizedContent","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking cloud optimized content"}',
    N'GPO: Windows Components > Cloud Content > "Turn off cloud optimized content" = Enabled. Prevents cloud-optimized content suggestions.',
    1, 1, @systemUserId);

-- Gap D.10 #49: Microsoft consumer experiences
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-075')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-075', @catHardening,
    N'Cloud Content: Disable Windows Consumer Features',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent","valueName":"DisableWindowsConsumerFeatures","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking Windows consumer features"}',
    N'GPO: Windows Components > Cloud Content > "Turn off Microsoft consumer experiences" = Enabled. Disables consumer features like Start menu suggestions and account notifications.',
    1, 1, @systemUserId);

-- Gap D.10 #50: Enumerate administrators on elevation
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-076')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-076', @catUAC,
    N'Credential UI: Do Not Enumerate Administrators on Elevation',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\CredUI","valueName":"EnumerateAdministrators","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking administrator enumeration on elevation"}',
    N'GPO: Windows Components > Credential User Interface > "Enumerate administrator accounts on elevation" = Disabled. Prevents displaying a list of admin accounts when elevation is requested.',
    1, 1, @systemUserId);

-- Gap D.10 #51: Prevent security questions for local accounts
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-077')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-077', @catCredProt,
    N'Credential UI: No Security Questions for Local Accounts',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\System","valueName":"NoLocalPasswordResetQuestions","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking local account security questions restriction"}',
    N'GPO: Windows Components > Credential User Interface > "Prevent the use of security questions for local accounts" = Enabled. Security questions are easily socially engineered and should not be used for password recovery.',
    1, 1, @systemUserId);

-- Gap D.10 #52: Game DVR
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-078')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-078', @catHardening,
    N'Game DVR: Disabled',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR","valueName":"AllowGameDVR","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Game DVR status"}',
    N'GPO: Windows Components > Windows Game Recording and Broadcasting > "Enables or disables Windows Game Recording and Broadcasting" = Disabled. Game DVR is unnecessary on enterprise endpoints and consumes resources.',
    1, 1, @systemUserId);

-- Gap D.10 #53: LAPS backup directory
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-079')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-079', @catCredProt,
    N'LAPS: Password Backup Directory Configured',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\LAPS","valueName":"BackupDirectory","expected":0,"operator":"neq","missingBehavior":"fail","display":"Checking LAPS backup directory configuration"}',
    N'GPO: Windows Components > LAPS > "Configure password backup directory" = Active Directory (1) or Azure AD (2). Ensures local admin passwords are backed up and rotated.',
    1, 1, @systemUserId);

-- Gap D.10 #54: Printer Redirection Guard
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-080')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-080', @catHardening,
    N'Printers: Redirection Guard Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers","valueName":"RedirectionGuardPolicy","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking printer redirection guard"}',
    N'GPO: Windows Components > Printers > "Configure Redirection Guard" = Enabled. Prevents print driver redirection attacks.',
    1, 1, @systemUserId);

-- Gap D.10 #55: Printer RPC over TCP
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-081')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-081', @catHardening,
    N'Printers: RPC Listener Over TCP Only',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\RPC","valueName":"RpcProtocols","expected":5,"operator":"eq","missingBehavior":"warn","display":"Checking printer RPC listener protocol"}',
    N'GPO: Windows Components > Printers > "Configure RPC listener settings" = "RPC over TCP" (value 5). Prevents use of named pipes for print spooler RPC which can be exploited.',
    1, 1, @systemUserId);

-- Gap D.10 #56: Printer RPC TCP port
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-082')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-082', @catHardening,
    N'Printers: RPC Over TCP Port (Dynamic)',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\RPC","valueName":"RpcTcpPort","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking printer RPC TCP port"}',
    N'GPO: Windows Components > Printers > "Configure RPC over TCP port" = 0 (dynamic port). Port 0 means the spooler uses a dynamic RPC port.',
    1, 1, @systemUserId);

-- Gap D.10 #57: Restrict driver installation to admins
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-083')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-083', @catHardening,
    N'Printers: Restrict Driver Installation to Administrators',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\PointAndPrint","valueName":"RestrictDriverInstallationToAdmins","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking printer driver installation restriction"}',
    N'GPO: Windows Components > Printers > "Limits print driver installation to Administrators" = Enabled. Prevents PrintNightmare-style exploitation via Point and Print driver installation.',
    1, 1, @systemUserId);

-- Gap D.10 #58: Manage processing of Queue-specific files
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-084')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-084', @catHardening,
    N'Printers: Queue-Specific Files Limited to Color Profiles',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers","valueName":"CopyFilesPolicy","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking printer queue-specific file processing"}',
    N'GPO: Windows Components > Printers > "Manage processing of Queue-specific files" = "Limit Queue-specific files to Color profiles". Prevents arbitrary file copying via print queues.',
    1, 1, @systemUserId);

-- Gap D.10 #59: Search: no encrypted file indexing
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-085')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-085', @catHardening,
    N'Windows Search: Disable Encrypted File Indexing',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search","valueName":"AllowIndexingEncryptedStoresOrItems","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking encrypted file indexing status"}',
    N'GPO: Windows Components > Search > "Allow indexing of encrypted files" = Disabled. Prevents Windows Search from indexing EFS-encrypted files which would store decrypted content in the index.',
    1, 1, @systemUserId);

-- Gap D.10 #60: Sudo disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-086')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-086', @catSecOpts,
    N'Sudo: Disabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Sudo","valueName":"Enabled","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Windows sudo feature status"}',
    N'GPO: Windows Components > Sudo > "Enable sudo" = Disabled (value 0). The sudo feature in Windows 11 can bypass UAC protections and should be disabled in managed environments.',
    1, 1, @systemUserId);

-- Gap D.10 #61: Windows Ink Workspace
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-087')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-087', @catHardening,
    N'Windows Ink Workspace: Disabled',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\WindowsInkWorkspace","valueName":"AllowWindowsInkWorkspace","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Windows Ink Workspace status"}',
    N'GPO: Windows Components > Windows Ink Workspace > "Allow Windows Ink Workspace" = Disabled. Ink Workspace is unnecessary on most enterprise endpoints.',
    1, 1, @systemUserId);

-- Gap D.10 #62: Windows Installer AlwaysInstallElevated
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-088')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-088', @catUAC,
    N'Windows Installer: Always Install with Elevated Privileges Disabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\Installer","valueName":"AlwaysInstallElevated","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Windows Installer elevation policy"}',
    N'GPO: Windows Components > Windows Installer > "Always install with elevated privileges" = Disabled. When enabled, this is a well-known privilege escalation vector. Critical to disable.',
    1, 1, @systemUserId);

-- Gap D.10 #63: MPR notifications disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-089')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-089', @catSecOpts,
    N'Windows Logon: MPR Notifications Disabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"EnableMPR","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking MPR notification status"}',
    N'GPO: Windows Components > Windows Logon Options > "Enable MPR notifications for the system" = Disabled. MPR can expose user credentials to third-party credential providers during logon.',
    1, 1, @systemUserId);

-- Gap D.10 #64: Auto restart sign-on disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-090')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-090', @catSecOpts,
    N'Windows Logon: Automatic Restart Sign-On Disabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"DisableAutomaticRestartSignOn","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking automatic restart sign-on policy"}',
    N'GPO: Windows Components > Windows Logon Options > "Sign-in and lock last interactive user automatically after a restart" = Disabled (DisableAutomaticRestartSignOn = 1). Prevents automatic sign-in after Windows Update restarts.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION M: NTLM Audit (gap analysis F #6-7, server-specific
-- but valuable for all platforms)
-- ============================================================

-- Gap F #6: Restrict NTLM: Audit incoming NTLM traffic
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-091')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-091', @catAuditLog,
    N'NTLM: Audit Incoming NTLM Traffic',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","valueName":"AuditReceivingNTLMTraffic","expected":2,"operator":"eq","missingBehavior":"warn","display":"Checking NTLM incoming traffic audit"}',
    N'GPO: Security Options > "Network security: Restrict NTLM: Audit incoming NTLM traffic" = "Enable auditing for all accounts" (value 2). Logs NTLM authentication attempts to help plan NTLM deprecation.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION N: Services gaps (gap analysis E)
-- Minor services not covered by SC-073/SC-074/SC-149
-- ============================================================

-- Gap E #5: irmon (Infrared Monitor) disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-092')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-092', @catServices,
    N'Service: Infrared Monitor (irmon) Disabled',
    'service', 'low',
    N'{"serviceName":"irmon","expected":"Disabled","operator":"eq","missingBehavior":"pass","display":"Checking Infrared Monitor service status"}',
    N'Set irmon service startup type to Disabled. Infrared is a legacy communication protocol with no enterprise use case and potential security risks.',
    1, 1, @systemUserId);

-- Gap E #7: MapsBroker disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-093')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-093', @catServices,
    N'Service: Downloaded Maps Manager (MapsBroker) Disabled',
    'service', 'low',
    N'{"serviceName":"MapsBroker","expected":"Disabled","operator":"eq","missingBehavior":"pass","display":"Checking Downloaded Maps Manager service status"}',
    N'Set MapsBroker service startup type to Disabled. Downloaded Maps Manager is unnecessary on enterprise endpoints and creates unnecessary network traffic.',
    1, 1, @systemUserId);

-- Gap E #10: PushToInstall disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-094')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-094', @catServices,
    N'Service: PushToInstall Disabled',
    'service', 'low',
    N'{"serviceName":"PushToInstall","expected":"Disabled","operator":"eq","missingBehavior":"pass","display":"Checking PushToInstall service status"}',
    N'Set PushToInstall service startup type to Disabled. Allows remote installation of Store apps, which should be managed through enterprise deployment tools.',
    1, 1, @systemUserId);

-- Gap E #13: simptcp (Simple TCP/IP Services) disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-095')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-095', @catServices,
    N'Service: Simple TCP/IP Services (simptcp) Disabled',
    'service', 'low',
    N'{"serviceName":"simptcp","expected":"Disabled","operator":"eq","missingBehavior":"pass","display":"Checking Simple TCP/IP Services status"}',
    N'Set simptcp service startup type to Disabled. Simple TCP/IP Services (echo, daytime, etc.) are legacy diagnostic protocols with no enterprise need.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION O: Additional gap -- MSA Optional for modern apps
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BLG-096')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BLG-096', @catSecOpts,
    N'MSA Optional for Modern Apps',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"MSAOptional","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking Microsoft Account requirement for modern apps"}',
    N'Set MSAOptional = 1 under System policies. Makes Microsoft Account sign-in optional rather than required for modern/UWP applications. Allows enterprise users to use apps without consumer MSA.',
    1, 1, @systemUserId);

-- ============================================================
-- PART P -- Platform scope
-- BLG controls apply to ALL platforms (W10, W11, MS19, MS22, MS25)
-- unless marked workstation-only or server-only
-- ============================================================

-- Collect all BLG-* control IDs
DECLARE @blgBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @blgBlock VALUES
    ('BLG-001'),('BLG-002'),('BLG-003'),('BLG-004'),('BLG-005'),('BLG-006'),('BLG-007'),('BLG-008'),('BLG-009'),('BLG-010'),
    ('BLG-011'),('BLG-012'),('BLG-013'),('BLG-014'),('BLG-015'),('BLG-016'),('BLG-017'),('BLG-018'),('BLG-019'),('BLG-020'),
    ('BLG-021'),('BLG-022'),('BLG-023'),('BLG-024'),('BLG-025'),('BLG-026'),('BLG-027'),('BLG-028'),('BLG-029'),('BLG-030'),
    ('BLG-031'),('BLG-032'),('BLG-033'),('BLG-034'),('BLG-035'),('BLG-036'),('BLG-037'),('BLG-038'),('BLG-039'),('BLG-040'),
    ('BLG-041'),('BLG-042'),('BLG-043'),('BLG-044'),('BLG-045'),('BLG-046'),('BLG-047'),('BLG-048'),('BLG-049'),('BLG-050'),
    ('BLG-051'),('BLG-052'),('BLG-053'),('BLG-054'),('BLG-055'),('BLG-056'),('BLG-057'),('BLG-058'),('BLG-059'),('BLG-060'),
    ('BLG-061'),('BLG-062'),('BLG-063'),('BLG-064'),('BLG-065'),('BLG-066'),('BLG-067'),('BLG-068'),('BLG-069'),('BLG-070'),
    ('BLG-071'),('BLG-072'),('BLG-073'),('BLG-074'),('BLG-075'),('BLG-076'),('BLG-077'),('BLG-078'),('BLG-079'),('BLG-080'),
    ('BLG-081'),('BLG-082'),('BLG-083'),('BLG-084'),('BLG-085'),('BLG-086'),('BLG-087'),('BLG-088'),('BLG-089'),('BLG-090'),
    ('BLG-091'),('BLG-092'),('BLG-093'),('BLG-094'),('BLG-095'),('BLG-096');

-- Link to W10
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platW10
FROM control_defs cd
JOIN @blgBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platW10
);
DECLARE @w10Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for W10: ', @w10Rows);

-- Link to W11
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platW11
FROM control_defs cd
JOIN @blgBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platW11
);
DECLARE @w11Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for W11: ', @w11Rows);

-- Link to MS19
IF @platMS19 IS NOT NULL
BEGIN
    INSERT INTO control_platforms (control_def_id, platform_id)
    SELECT cd.id, @platMS19
    FROM control_defs cd
    JOIN @blgBlock b ON b.control_id = cd.control_id
    WHERE NOT EXISTS (
        SELECT 1 FROM control_platforms cp
        WHERE cp.control_def_id = cd.id AND cp.platform_id = @platMS19
    );
    DECLARE @ms19Rows INT = @@ROWCOUNT;
    PRINT CONCAT('control_platforms rows added for MS19: ', @ms19Rows);
END

-- Link to MS22
IF @platMS22 IS NOT NULL
BEGIN
    INSERT INTO control_platforms (control_def_id, platform_id)
    SELECT cd.id, @platMS22
    FROM control_defs cd
    JOIN @blgBlock b ON b.control_id = cd.control_id
    WHERE NOT EXISTS (
        SELECT 1 FROM control_platforms cp
        WHERE cp.control_def_id = cd.id AND cp.platform_id = @platMS22
    );
    DECLARE @ms22Rows INT = @@ROWCOUNT;
    PRINT CONCAT('control_platforms rows added for MS22: ', @ms22Rows);
END

-- Link to MS25
IF @platMS25 IS NOT NULL
BEGIN
    INSERT INTO control_platforms (control_def_id, platform_id)
    SELECT cd.id, @platMS25
    FROM control_defs cd
    JOIN @blgBlock b ON b.control_id = cd.control_id
    WHERE NOT EXISTS (
        SELECT 1 FROM control_platforms cp
        WHERE cp.control_def_id = cd.id AND cp.platform_id = @platMS25
    );
    DECLARE @ms25Rows INT = @@ROWCOUNT;
    PRINT CONCAT('control_platforms rows added for MS25: ', @ms25Rows);
END

-- ============================================================
-- PART Q -- Framework mappings (ALL 5 frameworks)
-- ============================================================

-- Link all BLG-* controls to NIST
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST
FROM control_defs cd
JOIN @blgBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwNIST
);

-- Link all BLG-* controls to CIS
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS
FROM control_defs cd
JOIN @blgBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwCIS
);

-- Link all BLG-* controls to HIPAA
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA
FROM control_defs cd
JOIN @blgBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwHIPAA
);

-- Link all BLG-* controls to ISO 27001
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO
FROM control_defs cd
JOIN @blgBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwISO
);

-- Link all BLG-* controls to PCI-DSS
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwPCI
FROM control_defs cd
JOIN @blgBlock b ON b.control_id = cd.control_id
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
JOIN @blgBlock b ON b.control_id = cd.control_id
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

-- Count new baseline gap controls
SELECT COUNT(*) AS new_blg_controls
FROM control_defs
WHERE control_id LIKE 'BLG-%' AND is_active = 1;
-- Expected: 96

-- Breakdown by engine type
SELECT cd.[type], COUNT(*) AS control_count
FROM control_defs cd
WHERE cd.control_id LIKE 'BLG-%' AND cd.is_active = 1
GROUP BY cd.[type]
ORDER BY cd.[type];
-- Expected: registry ~92, service 4

-- Breakdown by severity
SELECT cd.severity, COUNT(*) AS control_count
FROM control_defs cd
WHERE cd.control_id LIKE 'BLG-%' AND cd.is_active = 1
GROUP BY cd.severity
ORDER BY cd.severity;
-- Expected: critical ~9, high ~30, medium ~40, low ~17

-- Verify platform linkage (all platforms)
SELECT p.code, COUNT(cp.control_def_id) AS blg_controls_linked
FROM platforms p
LEFT JOIN control_platforms cp ON cp.platform_id = p.id
LEFT JOIN control_defs cd ON cd.id = cp.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'BLG-%'
WHERE p.code IN ('W10','W11','MS19','MS22','MS25')
GROUP BY p.code
ORDER BY p.code;
-- Expected: W10=96, W11=96, MS19=96, MS22=96, MS25=96

-- Verify framework linkage
SELECT f.code, COUNT(cf.control_def_id) AS blg_controls_linked
FROM frameworks f
LEFT JOIN control_frameworks cf ON cf.framework_id = f.id
LEFT JOIN control_defs cd ON cd.id = cf.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'BLG-%'
WHERE f.is_active = 1
GROUP BY f.code
ORDER BY f.code;
-- Expected: CIS=96, HIPAA=96, ISO27001=96, NIST=96, PCI-DSS=96

-- New category check
SELECT cc.name, COUNT(cd.id) AS control_count
FROM control_categories cc
JOIN control_defs cd ON cd.category_id = cc.id
WHERE cd.control_id LIKE 'BLG-%' AND cd.is_active = 1
GROUP BY cc.name
ORDER BY cc.name;

-- Updated catalog totals
SELECT
    COUNT(*) AS total_active,
    SUM(CASE WHEN control_id LIKE 'BLG-%' THEN 1 ELSE 0 END) AS baseline_gap,
    SUM(CASE WHEN control_id LIKE 'SRV-%' THEN 1 ELSE 0 END) AS server_specific,
    SUM(CASE WHEN control_id LIKE 'AV-%' THEN 1 ELSE 0 END) AS antivirus,
    SUM(CASE WHEN control_id LIKE 'SC-%' THEN 1 ELSE 0 END) AS scored,
    SUM(CASE WHEN control_id LIKE 'BL-%' THEN 1 ELSE 0 END) AS baseline_registry
FROM control_defs
WHERE is_active = 1;
-- Expected: total_active = ~823 (727 + 96), baseline_gap = 96

-- Verify assessment linkage
SELECT a.name, COUNT(ac.control_def_id) AS blg_controls_linked
FROM assessments a
JOIN assessment_controls ac ON ac.assessment_id = a.id
JOIN control_defs cd ON cd.id = ac.control_def_id
WHERE cd.control_id LIKE 'BLG-%' AND cd.is_active = 1
  AND a.is_active = 1 AND a.deleted_at IS NULL
GROUP BY a.name;
-- Expected: each active assessment has 96 BLG controls linked
