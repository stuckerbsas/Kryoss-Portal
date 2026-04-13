SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_010_server_controls.sql
-- Kryoss Platform -- Server-Specific Security Controls
--
-- Adds ~80 controls (SRV-001..SRV-080) that apply ONLY to
-- Windows Server platforms (MS19, MS22, MS25). These cover
-- server-role-specific hardening: SMB, RDP, IIS, DNS, DHCP,
-- Hyper-V, File Server, Print Server, WSUS, and general
-- server hardening.
--
-- NOT linked to workstations (W10, W11).
-- Linked to ALL 5 active frameworks (NIST, CIS, HIPAA, ISO27001, PCI-DSS).
--
-- Run AFTER seed_008. Fully idempotent (NOT EXISTS guards).
-- ============================================================

BEGIN TRANSACTION;

DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Framework IDs
DECLARE @fwCIS   INT = (SELECT id FROM frameworks WHERE code='CIS');
DECLARE @fwNIST  INT = (SELECT id FROM frameworks WHERE code='NIST');
DECLARE @fwHIPAA INT = (SELECT id FROM frameworks WHERE code='HIPAA');
DECLARE @fwPCI   INT = (SELECT id FROM frameworks WHERE code='PCI-DSS');
DECLARE @fwISO   INT = (SELECT id FROM frameworks WHERE code='ISO27001');

-- Platform IDs (server only)
DECLARE @platMS19 INT = (SELECT id FROM platforms WHERE code='MS19');
DECLARE @platMS22 INT = (SELECT id FROM platforms WHERE code='MS22');
DECLARE @platMS25 INT = (SELECT id FROM platforms WHERE code='MS25');

IF @platMS19 IS NULL OR @platMS22 IS NULL OR @platMS25 IS NULL
BEGIN
    RAISERROR('MS19, MS22, or MS25 platform missing. Run seed_002 first.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ============================================================
-- PART 0 -- New categories for server roles
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'IIS Hardening')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'IIS Hardening', 301, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'DNS Server')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'DNS Server', 302, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'DHCP Server')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'DHCP Server', 303, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Hyper-V Security')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Hyper-V Security', 304, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Print Server')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Print Server', 305, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Server Core And General Hardening')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Server Core And General Hardening', 306, @systemUserId);

-- Category lookups
DECLARE @catNetSec     INT = (SELECT id FROM control_categories WHERE name=N'Network Security');
DECLARE @catNetAccess  INT = (SELECT id FROM control_categories WHERE name=N'Network Access');
DECLARE @catRemote     INT = (SELECT id FROM control_categories WHERE name=N'Remote Access');
DECLARE @catRemoteDesk INT = (SELECT id FROM control_categories WHERE name=N'Remote Desktop');
DECLARE @catAuditLog   INT = (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring');
DECLARE @catServices   INT = (SELECT id FROM control_categories WHERE name=N'Services Hardening');
DECLARE @catFileSys    INT = (SELECT id FROM control_categories WHERE name=N'File System And Shared Resources');
DECLARE @catHardening  INT = (SELECT id FROM control_categories WHERE name=N'Hardening');
DECLARE @catSecOpts    INT = (SELECT id FROM control_categories WHERE name=N'Security Options And Local Policy');
DECLARE @catCredProt   INT = (SELECT id FROM control_categories WHERE name=N'Credential Protection');
DECLARE @catWinUpdate  INT = (SELECT id FROM control_categories WHERE name=N'Windows Update');
DECLARE @catIIS        INT = (SELECT id FROM control_categories WHERE name=N'IIS Hardening');
DECLARE @catDNS        INT = (SELECT id FROM control_categories WHERE name=N'DNS Server');
DECLARE @catDHCP       INT = (SELECT id FROM control_categories WHERE name=N'DHCP Server');
DECLARE @catHyperV     INT = (SELECT id FROM control_categories WHERE name=N'Hyper-V Security');
DECLARE @catPrint      INT = (SELECT id FROM control_categories WHERE name=N'Print Server');
DECLARE @catServerGen  INT = (SELECT id FROM control_categories WHERE name=N'Server Core And General Hardening');
DECLARE @catCrypto     INT = (SELECT id FROM control_categories WHERE name=N'Certificates And Cryptography');
DECLARE @catWinRM      INT = (SELECT id FROM control_categories WHERE name=N'WinRM');

-- ============================================================
-- PART 1 -- SMB Server Hardening (SRV-001..SRV-012)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-001')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-001', @catNetSec,
    N'SMB Server - Require Security Signature (Signing)',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"RequireSecuritySignature","expected":1,"operator":"eq","display":"Checking SMB server signing requirement"}',
    N'Enable via GPO: Computer Configuration > Policies > Windows Settings > Security Settings > Local Policies > Security Options > "Microsoft network server: Digitally sign communications (always)" = Enabled. Or set registry HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\RequireSecuritySignature = 1.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-002')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-002', @catNetSec,
    N'SMB Server - Require Encryption (EncryptData)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"EncryptData","expected":1,"operator":"eq","display":"Checking SMB server encryption requirement"}',
    N'Enable via PowerShell: Set-SmbServerConfiguration -EncryptData $true. Requires SMB 3.0+ clients. Prevents cleartext data exposure on the wire.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-003')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-003', @catNetAccess,
    N'SMB Server - Restrict Null Session Access',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"RestrictNullSessAccess","expected":1,"operator":"eq","display":"Checking null session access restriction"}',
    N'Set HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\RestrictNullSessAccess = 1. Prevents anonymous access to named pipes and shares.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-004')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-004', @catNetAccess,
    N'SMB Server - Null Session Pipes Empty',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"NullSessionPipes","expected":"","operator":"eq","missingBehavior":"pass","display":"Checking null session pipes list"}',
    N'Set HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\NullSessionPipes to empty (no values). Eliminates anonymous pipe enumeration.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-005')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-005', @catNetAccess,
    N'SMB Server - Null Session Shares Empty',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"NullSessionShares","expected":"","operator":"eq","missingBehavior":"pass","display":"Checking null session shares list"}',
    N'Set HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\NullSessionShares to empty. Prevents anonymous share enumeration.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-006')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-006', @catNetSec,
    N'SMB Server - SMBv1 Protocol Disabled',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"SMB1","expected":0,"operator":"eq","display":"Checking SMBv1 server protocol status"}',
    N'Disable via PowerShell: Set-SmbServerConfiguration -EnableSMB1Protocol $false. Or Disable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol. SMBv1 is vulnerable to EternalBlue/WannaCry.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-007')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-007', @catNetAccess,
    N'SMB Server - Administrative Shares Hidden (AutoShareServer)',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"AutoShareServer","expected":0,"operator":"eq","missingBehavior":"warn","display":"Checking administrative shares visibility"}',
    N'Set HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\AutoShareServer = 0 to disable automatic admin shares (C$, ADMIN$). Note: may break some management tools.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-008')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-008', @catNetAccess,
    N'Restrict Anonymous Enumeration of SAM Accounts',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RestrictAnonymousSAM","expected":1,"operator":"eq","display":"Checking SAM anonymous enumeration restriction"}',
    N'GPO: Computer Configuration > Windows Settings > Security Settings > Local Policies > Security Options > "Network access: Do not allow anonymous enumeration of SAM accounts" = Enabled.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-009')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-009', @catNetAccess,
    N'Restrict Anonymous Enumeration of SAM Accounts and Shares',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RestrictAnonymous","expected":1,"operator":"eq","display":"Checking anonymous enumeration restriction for shares"}',
    N'GPO: "Network access: Do not allow anonymous enumeration of SAM accounts and shares" = Enabled. Prevents unauthenticated users from listing domain accounts and shares.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-010')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-010', @catNetSec,
    N'LAN Manager Authentication Level - NTLMv2 Only',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"LmCompatibilityLevel","expected":5,"operator":"gte","display":"Checking LAN Manager authentication level"}',
    N'GPO: Computer Configuration > Windows Settings > Security Settings > Local Policies > Security Options > "Network security: LAN Manager authentication level" = "Send NTLMv2 response only. Refuse LM & NTLM" (value 5). Prevents downgrade to weak LM/NTLM hashes.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-011')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-011', @catNetSec,
    N'LDAP Client Signing Required',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LDAP","valueName":"LDAPClientIntegrity","expected":1,"operator":"gte","display":"Checking LDAP client signing requirement"}',
    N'GPO: "Network security: LDAP client signing requirements" = "Negotiate signing" (1) or "Require signing" (2). Prevents LDAP relay and man-in-the-middle attacks.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-012')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-012', @catNetSec,
    N'Server SPN Target Name Validation Level',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"SmbServerNameHardeningLevel","expected":1,"operator":"gte","missingBehavior":"warn","display":"Checking SPN target name validation level"}',
    N'Set HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\SmbServerNameHardeningLevel >= 1. Validates client-supplied SPNs to prevent SMB relay attacks.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 2 -- RDP Security (SRV-013..SRV-020)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-013')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-013', @catRemoteDesk,
    N'RDP - Network Level Authentication (NLA) Required',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Terminal Server\\WinStations\\RDP-Tcp","valueName":"UserAuthentication","expected":1,"operator":"eq","display":"Checking NLA requirement for RDP"}',
    N'GPO: Computer Configuration > Administrative Templates > Windows Components > Remote Desktop Services > Remote Desktop Session Host > Security > "Require user authentication for remote connections by using Network Level Authentication" = Enabled.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-014')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-014', @catRemoteDesk,
    N'RDP - Minimum Encryption Level (High)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Terminal Server\\WinStations\\RDP-Tcp","valueName":"MinEncryptionLevel","expected":3,"operator":"gte","display":"Checking RDP minimum encryption level"}',
    N'GPO: Remote Desktop Session Host > Security > "Set client connection encryption level" = High (3). Ensures 128-bit encryption for RDP sessions.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-015')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-015', @catRemoteDesk,
    N'RDP - Security Layer (SSL/TLS)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Terminal Server\\WinStations\\RDP-Tcp","valueName":"SecurityLayer","expected":2,"operator":"gte","display":"Checking RDP security layer"}',
    N'GPO: Remote Desktop Session Host > Security > "Require use of specific security layer for remote (RDP) connections" = SSL (2). Prevents downgrade to RDP Security Layer (0) or Negotiate (1).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-016')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-016', @catRemoteDesk,
    N'RDP - Idle Session Timeout Configured',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","valueName":"MaxIdleTime","expected":900000,"operator":"lte","missingBehavior":"fail","display":"Checking RDP idle session timeout"}',
    N'GPO: Remote Desktop Session Host > Session Time Limits > "Set time limit for active but idle Remote Desktop Services sessions" <= 15 minutes (900000 ms). Prevents abandoned sessions.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-017')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-017', @catRemoteDesk,
    N'RDP - Disconnected Session Timeout Configured',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","valueName":"MaxDisconnectionTime","expected":60000,"operator":"lte","missingBehavior":"fail","display":"Checking RDP disconnected session timeout"}',
    N'GPO: Remote Desktop Session Host > Session Time Limits > "Set time limit for disconnected sessions" <= 1 minute (60000 ms). Terminates disconnected sessions to free resources.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-018')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-018', @catRemoteDesk,
    N'RDP - Always Prompt for Password on Connection',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","valueName":"fPromptForPassword","expected":1,"operator":"eq","display":"Checking RDP password prompt requirement"}',
    N'GPO: Remote Desktop Session Host > Security > "Always prompt for password upon connection" = Enabled. Prevents cached credential replay over RDP.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-019')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-019', @catRemoteDesk,
    N'RDP - Drive Redirection Disabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","valueName":"fDisableCdm","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking RDP drive redirection restriction"}',
    N'GPO: Remote Desktop Session Host > Device and Resource Redirection > "Do not allow drive redirection" = Enabled. Prevents data exfiltration via mapped drives.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-020')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-020', @catRemoteDesk,
    N'RDP - Clipboard Redirection Disabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Terminal Services","valueName":"fDisableClip","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking RDP clipboard redirection restriction"}',
    N'GPO: Remote Desktop Session Host > Device and Resource Redirection > "Do not allow clipboard redirection" = Enabled. Prevents data leakage via clipboard between RDP session and local machine.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 3 -- Remote Management (SRV-021..SRV-026)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-021')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-021', @catWinRM,
    N'WinRM - Service Hardened (HTTPS or Disabled)',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$svc = Get-Service WinRM -ErrorAction SilentlyContinue; if(-not $svc -or $svc.Status -ne ''Running''){''NotRunning''}else{$l=winrm enumerate winrm/config/listener 2>$null; if($l -match ''Transport = HTTPS''){''HTTPS''}else{''HTTP_ONLY''}}\"","expected":"NotRunning","operator":"in","expectedValues":["NotRunning","HTTPS"],"display":"Checking WinRM service transport configuration"}',
    N'WinRM should either be disabled (if not needed) or configured with HTTPS transport only. Run: winrm quickconfig -transport:https. Delete HTTP listener: winrm delete winrm/config/Listener?Address=*+Transport=HTTP.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-022')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-022', @catWinRM,
    N'WinRM - Basic Authentication Disabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Service","valueName":"AllowBasic","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking WinRM basic authentication status"}',
    N'GPO: Computer Configuration > Administrative Templates > Windows Components > Windows Remote Management (WinRM) > WinRM Service > "Allow Basic authentication" = Disabled. Basic auth sends credentials in cleartext.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-023')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-023', @catSecOpts,
    N'PowerShell - Constrained Language Mode Configured',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment","valueName":"__PSLockdownPolicy","expected":4,"operator":"eq","missingBehavior":"warn","display":"Checking PowerShell language mode policy"}',
    N'Set environment variable __PSLockdownPolicy = 4 for ConstrainedLanguage mode. Best used with WDAC/AppLocker. Limits .NET, COM, and type access in PowerShell sessions.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-024')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-024', @catAuditLog,
    N'PowerShell - Script Block Logging Enabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\ScriptBlockLogging","valueName":"EnableScriptBlockLogging","expected":1,"operator":"eq","display":"Checking PowerShell script block logging"}',
    N'GPO: Administrative Templates > Windows Components > Windows PowerShell > "Turn on PowerShell Script Block Logging" = Enabled. Logs deobfuscated script content to Event Log 4104.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-025')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-025', @catAuditLog,
    N'PowerShell - Module Logging Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\ModuleLogging","valueName":"EnableModuleLogging","expected":1,"operator":"eq","display":"Checking PowerShell module logging"}',
    N'GPO: Administrative Templates > Windows Components > Windows PowerShell > "Turn on Module Logging" = Enabled. Logs pipeline execution details to Event Log 4103.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-026')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-026', @catAuditLog,
    N'PowerShell - Transcription Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\Transcription","valueName":"EnableTranscripting","expected":1,"operator":"eq","display":"Checking PowerShell transcription logging"}',
    N'GPO: Administrative Templates > Windows Components > Windows PowerShell > "Turn on PowerShell Transcription" = Enabled. Creates full text transcript of every PowerShell session.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 4 -- IIS Hardening (SRV-027..SRV-038)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-027')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-027', @catIIS,
    N'IIS - Installed Version Current',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$v=(Get-ItemProperty HKLM:\\SOFTWARE\\Microsoft\\InetStp -ErrorAction SilentlyContinue).MajorVersion; if($v){$v}else{''NotInstalled''}\"","expected":"10","operator":"gte","optional":true,"display":"Checking IIS version"}',
    N'IIS 10.0 ships with Server 2016+. Older versions have known vulnerabilities. If IIS is not needed, remove the role: Remove-WindowsFeature Web-Server.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-028')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-028', @catIIS,
    N'IIS - Default Web Site Removed',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"Import-Module WebAdministration -ErrorAction SilentlyContinue; $s=Get-Website -Name ''Default Web Site'' -ErrorAction SilentlyContinue; if($s){''Present''}else{''Removed''}\"","expected":"Removed","operator":"eq","optional":true,"display":"Checking for IIS Default Web Site"}',
    N'Remove via: Remove-Website -Name "Default Web Site". The default site exposes unnecessary attack surface and known content paths.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-029')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-029', @catIIS,
    N'IIS - Directory Browsing Disabled',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"Import-Module WebAdministration -ErrorAction SilentlyContinue; $v=(Get-WebConfigurationProperty -Filter /system.webServer/directoryBrowse -PSPath ''IIS:\\'' -Name enabled -ErrorAction SilentlyContinue).Value; if($v -eq $true){''Enabled''}else{''Disabled''}\"","expected":"Disabled","operator":"eq","optional":true,"display":"Checking IIS directory browsing setting"}',
    N'Disable via: Set-WebConfigurationProperty -Filter /system.webServer/directoryBrowse -PSPath "IIS:\" -Name enabled -Value $false. Prevents directory listing disclosure.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-030')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-030', @catIIS,
    N'IIS - Custom Error Pages Configured',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"Import-Module WebAdministration -ErrorAction SilentlyContinue; $e=(Get-WebConfigurationProperty -Filter /system.webServer/httpErrors -PSPath ''IIS:\\'' -Name errorMode -ErrorAction SilentlyContinue); if($e -eq ''Custom'' -or $e -eq ''DetailedLocalOnly''){''Configured''}else{''NotConfigured''}\"","expected":"Configured","operator":"eq","optional":true,"display":"Checking IIS custom error pages"}',
    N'Set error mode to Custom or DetailedLocalOnly. Prevents detailed error information disclosure to remote clients.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-031')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-031', @catIIS,
    N'IIS - Request Filtering Enabled',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$f=(Get-WindowsFeature Web-Filtering -ErrorAction SilentlyContinue); if($f -and $f.Installed){''Installed''}else{''NotInstalled''}\"","expected":"Installed","operator":"eq","optional":true,"display":"Checking IIS request filtering feature"}',
    N'Install via: Install-WindowsFeature Web-Filtering. Request Filtering blocks malicious URLs, double-encoding, and path traversal attacks.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-032')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-032', @catCrypto,
    N'TLS 1.2+ Required (Schannel - TLS 1.0 Disabled)',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\SCHANNEL\\Protocols\\TLS 1.0\\Server","valueName":"Enabled","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking TLS 1.0 server status"}',
    N'Disable TLS 1.0: Set HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.0\Server\Enabled = 0 and DisabledByDefault = 1. TLS 1.0 has known weaknesses (BEAST, POODLE).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-033')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-033', @catCrypto,
    N'SSL/TLS - Weak Ciphers Disabled (RC4)',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\SCHANNEL\\Ciphers\\RC4 128/128","valueName":"Enabled","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking RC4 cipher status"}',
    N'Disable RC4: Set HKLM\...\SCHANNEL\Ciphers\RC4 128/128\Enabled = 0. RC4 is cryptographically broken. Use AES-based cipher suites only.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-034')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-034', @catIIS,
    N'IIS - HSTS (HTTP Strict Transport Security) Configured',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"Import-Module WebAdministration -ErrorAction SilentlyContinue; $h=(Get-WebConfigurationProperty -Filter /system.webServer/httpProtocol/customHeaders -PSPath ''IIS:\\'' -Name collection -ErrorAction SilentlyContinue) | Where-Object {$_.name -eq ''Strict-Transport-Security''}; if($h){''Configured''}else{''NotConfigured''}\"","expected":"Configured","operator":"eq","optional":true,"display":"Checking IIS HSTS header"}',
    N'Add HSTS header: Set-WebConfigurationProperty -Filter /system.webServer/httpProtocol/customHeaders -PSPath "IIS:\" -Name collection -AtElement @{name="Strict-Transport-Security"} -Value @{value="max-age=31536000; includeSubDomains"}.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-035')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-035', @catIIS,
    N'IIS - Application Pool Identity Not LocalSystem',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"Import-Module WebAdministration -ErrorAction SilentlyContinue; $bad=(Get-ChildItem IIS:\\AppPools -ErrorAction SilentlyContinue | Where-Object {$_.processModel.identityType -eq ''LocalSystem''}); if($bad){$bad.Name -join '',''}else{''None''}\"","expected":"None","operator":"eq","optional":true,"display":"Checking IIS application pool identities"}',
    N'Change app pool identity from LocalSystem to ApplicationPoolIdentity or a dedicated service account. LocalSystem has full machine access, violating least-privilege.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-036')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-036', @catIIS,
    N'IIS - Detailed Error Messages Disabled for Remote',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"Import-Module WebAdministration -ErrorAction SilentlyContinue; $m=(Get-WebConfigurationProperty -Filter /system.webServer/httpErrors -PSPath ''IIS:\\'' -Name errorMode -ErrorAction SilentlyContinue); if($m -eq ''Detailed''){''Detailed''}else{''Safe''}\"","expected":"Safe","operator":"eq","optional":true,"display":"Checking IIS error detail level"}',
    N'Set httpErrors errorMode to DetailedLocalOnly or Custom. Detailed error messages expose stack traces, paths, and internal configuration to attackers.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-037')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-037', @catIIS,
    N'IIS - X-Powered-By Header Removed',
    'command', 'low',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"Import-Module WebAdministration -ErrorAction SilentlyContinue; $h=(Get-WebConfigurationProperty -Filter /system.webServer/httpProtocol/customHeaders -PSPath ''IIS:\\'' -Name collection -ErrorAction SilentlyContinue) | Where-Object {$_.name -eq ''X-Powered-By''}; if($h){''Present''}else{''Removed''}\"","expected":"Removed","operator":"eq","optional":true,"display":"Checking X-Powered-By header"}',
    N'Remove via: Remove-WebConfigurationProperty -Filter /system.webServer/httpProtocol/customHeaders -PSPath "IIS:\" -Name collection -AtElement @{name="X-Powered-By"}. Reduces information disclosure.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-038')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-038', @catIIS,
    N'IIS - Server Header Suppressed',
    'command', 'low',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"Import-Module WebAdministration -ErrorAction SilentlyContinue; $v=(Get-WebConfigurationProperty -Filter /system.webServer/security/requestFiltering -PSPath ''IIS:\\'' -Name removeServerHeader -ErrorAction SilentlyContinue); if($v -eq $true){''Suppressed''}else{''Exposed''}\"","expected":"Suppressed","operator":"eq","optional":true,"display":"Checking IIS Server header"}',
    N'Set removeServerHeader in requestFiltering or use URL Rewrite to blank the Server header. Prevents IIS version fingerprinting.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 5 -- DNS Server (SRV-039..SRV-044)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-039')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-039', @catDNS,
    N'DNS Server - Secure Dynamic Updates Configured',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$z=Get-DnsServerZone -ErrorAction SilentlyContinue | Where-Object {$_.IsAutoCreated -eq $false -and $_.ZoneType -eq ''Primary''}; if(-not $z){''NoDNS''}elseif($z | Where-Object {$_.DynamicUpdate -ne ''Secure''}){''Insecure''}else{''Secure''}\"","expected":"Secure","operator":"in","expectedValues":["Secure","NoDNS"],"optional":true,"display":"Checking DNS dynamic update security"}',
    N'Set all primary zones to Secure dynamic updates: Set-DnsServerPrimaryZone -Name <zone> -DynamicUpdate Secure. Prevents unauthorized DNS record injection.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-040')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-040', @catDNS,
    N'DNS Server - Recursion Disabled for External Zones',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$d=Get-DnsServerRecursion -ErrorAction SilentlyContinue; if(-not $d){''NoDNS''}elseif($d.Enable -eq $false){''Disabled''}else{''Enabled''}\"","expected":"Disabled","operator":"in","expectedValues":["Disabled","NoDNS"],"optional":true,"display":"Checking DNS recursion setting"}',
    N'Disable recursion on authoritative-only DNS servers: Set-DnsServerRecursion -Enable $false. Open recursion enables DNS amplification attacks.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-041')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-041', @catDNS,
    N'DNS Server - Socket Pool Size >= 2500',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$s=(Get-DnsServerSetting -All -ErrorAction SilentlyContinue).SocketPoolSize; if($s){$s}else{''NoDNS''}\"","expected":2500,"operator":"gte","optional":true,"display":"Checking DNS socket pool size"}',
    N'Increase socket pool: dnscmd /config /socketpoolsize 2500. Larger pool randomizes source ports, mitigating DNS cache poisoning (Kaminsky attack).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-042')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-042', @catDNS,
    N'DNS Server - Cache Locking Percentage >= 75%',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$c=(Get-DnsServerCache -ErrorAction SilentlyContinue).LockingPercent; if($c){$c}else{''NoDNS''}\"","expected":75,"operator":"gte","optional":true,"display":"Checking DNS cache locking percentage"}',
    N'Set cache locking: Set-DnsServerCache -LockingPercent 75. Prevents cached records from being overwritten during a percentage of their TTL.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-043')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-043', @catDNS,
    N'DNS Server - Diagnostic Logging Enabled',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$d=Get-DnsServerDiagnostics -ErrorAction SilentlyContinue; if(-not $d){''NoDNS''}elseif($d.Queries -or $d.Answers){''Enabled''}else{''Disabled''}\"","expected":"Enabled","operator":"in","expectedValues":["Enabled","NoDNS"],"optional":true,"display":"Checking DNS diagnostic logging"}',
    N'Enable query logging: Set-DnsServerDiagnostics -Queries $true -Answers $true. Essential for DNS threat detection and forensics.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-044')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-044', @catDNS,
    N'DNS Server - DNSSEC Signing Enabled for Zones',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$z=Get-DnsServerZone -ErrorAction SilentlyContinue | Where-Object {$_.IsAutoCreated -eq $false -and $_.ZoneType -eq ''Primary''}; if(-not $z){''NoDNS''}elseif($z | Where-Object {$_.IsSigned -eq $true}){''Signed''}else{''Unsigned''}\"","expected":"Signed","operator":"in","expectedValues":["Signed","NoDNS"],"optional":true,"display":"Checking DNSSEC signing status"}',
    N'Sign zones with DNSSEC: Invoke-DnsServerZoneSign -ZoneName <zone>. DNSSEC prevents DNS spoofing by cryptographically signing records.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 6 -- DHCP Server (SRV-045..SRV-048)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-045')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-045', @catDHCP,
    N'DHCP Server - Audit Logging Enabled',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$d=Get-DhcpServerAuditLog -ErrorAction SilentlyContinue; if(-not $d){''NoDHCP''}elseif($d.Enable){''Enabled''}else{''Disabled''}\"","expected":"Enabled","operator":"in","expectedValues":["Enabled","NoDHCP"],"optional":true,"display":"Checking DHCP audit logging"}',
    N'Enable: Set-DhcpServerAuditLog -Enable $true. DHCP audit logs track IP assignments critical for forensic investigations.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-046')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-046', @catDHCP,
    N'DHCP Server - Failover Configured',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$f=Get-DhcpServerv4Failover -ErrorAction SilentlyContinue; if(-not $f){$svc=Get-Service DHCPServer -ErrorAction SilentlyContinue; if($svc -and $svc.Status -eq ''Running''){''NoFailover''}else{''NoDHCP''}}else{''Configured''}\"","expected":"Configured","operator":"in","expectedValues":["Configured","NoDHCP"],"optional":true,"display":"Checking DHCP failover configuration"}',
    N'Configure DHCP failover: Add-DhcpServerv4Failover -Name <name> -PartnerServer <partner> -ScopeId <scope>. Ensures DHCP availability if primary server fails.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-047')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-047', @catDHCP,
    N'DHCP Server - Lease Duration Reasonable (<= 8 Days)',
    'command', 'low',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$s=Get-DhcpServerv4Scope -ErrorAction SilentlyContinue; if(-not $s){''NoDHCP''}else{$max=($s | ForEach-Object {$_.LeaseDuration.TotalDays} | Measure-Object -Maximum).Maximum; if($max -le 8){''OK''}else{''TooLong:'' + $max + ''days''}}\"","expected":"OK","operator":"in","expectedValues":["OK","NoDHCP"],"optional":true,"display":"Checking DHCP lease duration"}',
    N'Set lease duration to 8 days or less for most scopes. Shorter leases reclaim stale IPs faster and reduce rogue device exposure.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-048')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-048', @catDHCP,
    N'DHCP Server - Domain Name Option (015) Configured',
    'command', 'low',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$s=Get-DhcpServerv4Scope -ErrorAction SilentlyContinue; if(-not $s){''NoDHCP''}else{$o=Get-DhcpServerv4OptionValue -OptionId 15 -ErrorAction SilentlyContinue; if($o){''Configured''}else{''NotConfigured''}}\"","expected":"Configured","operator":"in","expectedValues":["Configured","NoDHCP"],"optional":true,"display":"Checking DHCP domain name option"}',
    N'Set DHCP option 015 (DNS Domain Name) so clients auto-join the correct DNS suffix. Prevents name resolution issues and domain confusion.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 7 -- Hyper-V (SRV-049..SRV-054)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-049')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-049', @catHyperV,
    N'Hyper-V - Integration Services Version Current',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$hv=Get-WindowsFeature Hyper-V -ErrorAction SilentlyContinue; if(-not $hv -or -not $hv.Installed){''NoHyperV''}else{$vms=Get-VM -ErrorAction SilentlyContinue; if(-not $vms){''NoVMs''}else{$old=$vms | Where-Object {(Get-VMIntegrationService -VMName $_.Name -ErrorAction SilentlyContinue | Where-Object {$_.Enabled -and -not $_.PrimaryStatusDescription -eq ''OK''}).Count -gt 0}; if($old){''Outdated''}else{''Current''}}}\"","expected":"Current","operator":"in","expectedValues":["Current","NoHyperV","NoVMs"],"optional":true,"display":"Checking Hyper-V integration services"}',
    N'Update integration services on all VMs. Outdated IC versions miss performance fixes and security patches.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-050')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-050', @catHyperV,
    N'Hyper-V - No Production Snapshots Present',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$hv=Get-WindowsFeature Hyper-V -ErrorAction SilentlyContinue; if(-not $hv -or -not $hv.Installed){''NoHyperV''}else{$snaps=Get-VM -ErrorAction SilentlyContinue | Get-VMSnapshot -ErrorAction SilentlyContinue; if($snaps){''HasSnapshots:'' + $snaps.Count}else{''Clean''}}\"","expected":"Clean","operator":"in","expectedValues":["Clean","NoHyperV"],"optional":true,"display":"Checking for Hyper-V snapshots in production"}',
    N'Remove production snapshots: Remove-VMSnapshot -VMName <vm>. Snapshots degrade disk performance and grow unbounded. Use proper backup instead.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-051')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-051', @catHyperV,
    N'Hyper-V - Enhanced Session Mode Disabled on Host',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"EnhancedSessionMode","expected":0,"operator":"eq","missingBehavior":"pass","optional":true,"display":"Checking Hyper-V Enhanced Session Mode"}',
    N'Disable Enhanced Session Mode on production hosts: Set-VMHost -EnableEnhancedSessionMode $false. ESM enables clipboard/drive redirection between host and VM.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-052')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-052', @catHyperV,
    N'Hyper-V - Secure Boot Enabled for Gen2 VMs',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$hv=Get-WindowsFeature Hyper-V -ErrorAction SilentlyContinue; if(-not $hv -or -not $hv.Installed){''NoHyperV''}else{$gen2=Get-VM -ErrorAction SilentlyContinue | Where-Object {$_.Generation -eq 2}; if(-not $gen2){''NoGen2VMs''}else{$nosb=$gen2 | Where-Object {(Get-VMFirmware $_.Name -ErrorAction SilentlyContinue).SecureBoot -ne ''On''}; if($nosb){''Disabled:'' + ($nosb.Name -join '','')}else{''AllEnabled''}}}\"","expected":"AllEnabled","operator":"in","expectedValues":["AllEnabled","NoHyperV","NoGen2VMs"],"optional":true,"display":"Checking Secure Boot on Gen2 VMs"}',
    N'Enable Secure Boot: Set-VMFirmware -VMName <vm> -EnableSecureBoot On. Prevents boot-level rootkits in virtual machines.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-053')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-053', @catHyperV,
    N'Hyper-V - MAC Spoofing Disabled on Virtual Switches',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$hv=Get-WindowsFeature Hyper-V -ErrorAction SilentlyContinue; if(-not $hv -or -not $hv.Installed){''NoHyperV''}else{$bad=Get-VMNetworkAdapter * -ErrorAction SilentlyContinue | Where-Object {$_.MacAddressSpoofing -eq ''On''}; if($bad){''Enabled:'' + ($bad.VMName -join '','')}else{''AllDisabled''}}\"","expected":"AllDisabled","operator":"in","expectedValues":["AllDisabled","NoHyperV"],"optional":true,"display":"Checking MAC spoofing on vSwitch adapters"}',
    N'Disable MAC spoofing: Set-VMNetworkAdapter -VMName <vm> -MacAddressSpoofing Off. Prevents VMs from impersonating other network devices.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-054')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-054', @catHyperV,
    N'Hyper-V - Guest State Encryption (Shielded VMs)',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$hv=Get-WindowsFeature Hyper-V -ErrorAction SilentlyContinue; if(-not $hv -or -not $hv.Installed){''NoHyperV''}else{$vms=Get-VM -ErrorAction SilentlyContinue; if(-not $vms){''NoVMs''}else{$enc=$vms | Where-Object {(Get-VMSecurity $_.Name -ErrorAction SilentlyContinue).EncryptStateAndVmMigrationTraffic}; if($enc.Count -eq $vms.Count){''AllEncrypted''}else{''NotAllEncrypted''}}}\"","expected":"AllEncrypted","operator":"in","expectedValues":["AllEncrypted","NoHyperV","NoVMs"],"optional":true,"display":"Checking VM state encryption"}',
    N'Enable VM encryption: Set-VMSecurity -VMName <vm> -EncryptStateAndVmMigrationTraffic $true. Protects VM state data and live migration traffic.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 8 -- File Server (SRV-055..SRV-060)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-055')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-055', @catFileSys,
    N'File Server - NTFS Permissions on System Root',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$acl=Get-Acl C:\\ -ErrorAction SilentlyContinue; $bad=$acl.Access | Where-Object {$_.IdentityReference -match ''Everyone|Users'' -and $_.FileSystemRights -match ''FullControl|Modify''}; if($bad){''Overpermissioned''}else{''Secure''}\"","expected":"Secure","operator":"eq","display":"Checking NTFS permissions on system root"}',
    N'Remove excessive permissions from C:\ root. Everyone and Users should not have FullControl or Modify on the system drive.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-056')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-056', @catAuditLog,
    N'File Server - Object Access Auditing Enabled',
    'auditpol', 'high',
    N'{"subcategory":"File System","expected":"Success and Failure","operator":"contains","display":"Checking file system object access auditing"}',
    N'Enable via: auditpol /set /subcategory:"File System" /success:enable /failure:enable. Required for tracking file access on shared folders.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-057')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-057', @catFileSys,
    N'File Server - FSRM Feature Installed',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$f=Get-WindowsFeature FS-Resource-Manager -ErrorAction SilentlyContinue; if($f -and $f.Installed){''Installed''}else{''NotInstalled''}\"","expected":"Installed","operator":"eq","optional":true,"display":"Checking File Server Resource Manager installation"}',
    N'Install: Install-WindowsFeature FS-Resource-Manager. FSRM enables quotas, file screening (block ransomware extensions), and storage reports.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-058')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-058', @catFileSys,
    N'File Server - Shadow Copies (VSS) Configured',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$v=vssadmin list shadowstorage 2>$null; if($v -match ''Shadow Copy Storage''){''Configured''}else{''NotConfigured''}\"","expected":"Configured","operator":"eq","optional":true,"display":"Checking Volume Shadow Copy configuration"}',
    N'Configure shadow copies: vssadmin add shadowstorage /for=D: /on=D: /maxsize=10%. Shadow copies provide quick file-level recovery without full backup restoration.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-059')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-059', @catFileSys,
    N'File Server - DFS Namespace Healthy (If Configured)',
    'command', 'low',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$dfs=Get-WindowsFeature FS-DFS-Namespace -ErrorAction SilentlyContinue; if(-not $dfs -or -not $dfs.Installed){''NoDFS''}else{$roots=Get-DfsnRoot -ErrorAction SilentlyContinue; if(-not $roots){''NoRoots''}else{$bad=$roots | Where-Object {$_.State -ne ''Online''}; if($bad){''Unhealthy''}else{''Healthy''}}}\"","expected":"Healthy","operator":"in","expectedValues":["Healthy","NoDFS","NoRoots"],"optional":true,"display":"Checking DFS Namespace health"}',
    N'Investigate offline DFS namespaces. Run dfsdiag /testdfsconfig to diagnose issues. Unhealthy DFS can cause file access failures across the network.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-060')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-060', @catFileSys,
    N'File Server - Access-Based Enumeration on Shares',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$shares=Get-SmbShare -ErrorAction SilentlyContinue | Where-Object {$_.ShareType -eq ''FileSystemDirectory'' -and $_.Name -notmatch ''\\$$''}; if(-not $shares){''NoShares''}else{$noabe=$shares | Where-Object {-not $_.FolderEnumerationMode -or $_.FolderEnumerationMode -ne ''AccessBased''}; if($noabe){''Missing:'' + ($noabe.Name -join '','')}else{''AllEnabled''}}\"","expected":"AllEnabled","operator":"in","expectedValues":["AllEnabled","NoShares"],"optional":true,"display":"Checking access-based enumeration on file shares"}',
    N'Enable ABE: Set-SmbShare -Name <share> -FolderEnumerationMode AccessBased. Users only see files/folders they have permission to access, preventing information disclosure.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 9 -- Print Server (SRV-061..SRV-064)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-061')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-061', @catPrint,
    N'Print Spooler Service Disabled (If Not Print Server)',
    'service', 'critical',
    N'{"serviceName":"Spooler","expectedStartType":"Disabled","display":"Checking Print Spooler service status"}',
    N'Disable on all servers that are NOT print servers: Stop-Service Spooler; Set-Service Spooler -StartupType Disabled. PrintNightmare (CVE-2021-34527) exploits the Spooler for RCE.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-062')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-062', @catPrint,
    N'Point and Print Restrictions Enabled',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\PointAndPrint","valueName":"Restricted","expected":1,"operator":"eq","display":"Checking Point and Print restrictions"}',
    N'GPO: Computer Configuration > Administrative Templates > Printers > "Point and Print Restrictions" = Enabled, with "Users can only point and print to these servers" = Enabled. Prevents driver installation from rogue print servers.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-063')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-063', @catPrint,
    N'Package Point and Print - Approved Servers Only',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers\\PackagePointAndPrint","valueName":"PackagePointAndPrintOnly","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking Package Point and Print restriction"}',
    N'Set PackagePointAndPrintOnly = 1 and PackagePointAndPrintServerList = 1 with approved server list. Restricts driver packages to trusted servers only.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-064')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-064', @catPrint,
    N'Print Spooler - Remote Access Restricted',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\Printers","valueName":"RegisterSpoolerRemoteRpcEndPoint","expected":2,"operator":"eq","missingBehavior":"warn","display":"Checking Print Spooler remote access"}',
    N'Set RegisterSpoolerRemoteRpcEndPoint = 2 (do not register). Prevents remote exploitation of the Print Spooler RPC interface.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 10 -- WSUS / Windows Update (SRV-065..SRV-068)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-065')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-065', @catWinUpdate,
    N'WSUS Server - Update Source Configured',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate","valueName":"WUServer","expected":"","operator":"neq","missingBehavior":"warn","optional":true,"display":"Checking WSUS server configuration"}',
    N'Configure WSUS URL via GPO: Computer Configuration > Administrative Templates > Windows Components > Windows Update > "Specify intranet Microsoft update service location".',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-066')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-066', @catWinUpdate,
    N'Windows Update - Use WSUS or Windows Update Configured',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU","valueName":"UseWUServer","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking Windows Update source policy"}',
    N'Set UseWUServer = 1 when WSUS is configured. Ensures clients pull updates from the WSUS server rather than directly from Microsoft.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-067')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-067', @catWinUpdate,
    N'Windows Update - Automatic Updates Enabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU","valueName":"NoAutoUpdate","expected":0,"operator":"eq","display":"Checking automatic updates status"}',
    N'GPO: Windows Update > "Configure Automatic Updates" = Enabled. NoAutoUpdate = 0 ensures automatic update downloads. Critical for server patching compliance.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-068')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-068', @catWinUpdate,
    N'Windows Update - Scheduled Install Day Configured',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU","valueName":"ScheduledInstallDay","expected":0,"operator":"gte","missingBehavior":"warn","display":"Checking Windows Update scheduled install day"}',
    N'GPO: "Configure Automatic Updates" > Scheduled install day. Value 0 = every day, 1-7 = specific day. Ensures updates are installed on a predictable schedule.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 11 -- Server Core / General Hardening (SRV-069..SRV-080)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-069')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-069', @catServerGen,
    N'Server Core Detection (GUI Shell Installed)',
    'command', 'low',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$gui=Get-WindowsFeature Server-Gui-Shell -ErrorAction SilentlyContinue; if($gui -and $gui.Installed){''DesktopExperience''}else{''ServerCore''}\"","expected":"ServerCore","operator":"eq","optional":true,"display":"Checking for Server Core vs Desktop Experience"}',
    N'Server Core reduces attack surface by removing the GUI shell. Consider converting to Server Core where feasible: Uninstall-WindowsFeature Server-Gui-Shell.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-070')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-070', @catServerGen,
    N'Windows Defender - Real-Time Protection Enabled (Server)',
    'command', 'critical',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$d=Get-MpPreference -ErrorAction SilentlyContinue; if($d.DisableRealtimeMonitoring -eq $false){''Enabled''}else{''Disabled''}\"","expected":"Enabled","operator":"eq","display":"Checking Windows Defender real-time protection on server"}',
    N'Enable: Set-MpPreference -DisableRealtimeMonitoring $false. Windows Defender provides baseline antimalware protection on servers. Ensure it is not disabled by third-party AV without a replacement.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-071')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-071', @catServerGen,
    N'Windows Firewall - Enabled on All Profiles (Server)',
    'command', 'critical',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$fw=Get-NetFirewallProfile -ErrorAction SilentlyContinue; $off=$fw | Where-Object {$_.Enabled -eq $false}; if($off){''Disabled:'' + ($off.Name -join '','')}else{''AllEnabled''}\"","expected":"AllEnabled","operator":"eq","display":"Checking Windows Firewall status on all profiles"}',
    N'Enable: Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled True. Windows Firewall must be enabled on all profiles, even with perimeter firewalls.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-072')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-072', @catServices,
    N'Remote Registry Service Disabled',
    'service', 'high',
    N'{"serviceName":"RemoteRegistry","expectedStartType":"Disabled","display":"Checking Remote Registry service status"}',
    N'Disable: Set-Service RemoteRegistry -StartupType Disabled; Stop-Service RemoteRegistry. Remote Registry allows remote modification of registry keys, enabling lateral movement.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-073')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-073', @catCredProt,
    N'WDigest Authentication Disabled (UseLogonCredential)',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\WDigest","valueName":"UseLogonCredential","expected":0,"operator":"eq","display":"Checking WDigest plaintext credential storage"}',
    N'Set HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest\UseLogonCredential = 0. WDigest stores plaintext passwords in LSASS memory, trivially dumped by Mimikatz.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-074')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-074', @catCredProt,
    N'LSA Protection Enabled (RunAsPPL)',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RunAsPPL","expected":1,"operator":"eq","display":"Checking LSA protection (RunAsPPL) status"}',
    N'Set HKLM\SYSTEM\CurrentControlSet\Control\Lsa\RunAsPPL = 1 and reboot. LSA Protection prevents unsigned code from loading into LSASS, blocking credential dumping tools.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-075')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-075', @catCredProt,
    N'Credential Guard Enabled (LsaCfgFlags)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\DeviceGuard","valueName":"EnableVirtualizationBasedSecurity","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking Credential Guard / VBS status"}',
    N'Enable Credential Guard via GPO: Computer Configuration > Administrative Templates > System > Device Guard > "Turn On Virtualization Based Security" = Enabled with Credential Guard. Requires UEFI, Secure Boot, and TPM.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-076')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-076', @catCredProt,
    N'LSASS Dump Prevention (AuditLevel)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\LSASS.exe","valueName":"AuditLevel","expected":8,"operator":"gte","missingBehavior":"warn","display":"Checking LSASS dump protection level"}',
    N'Set AuditLevel = 8 under HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\LSASS.exe. Blocks process creation for LSASS dumps.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-077')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-077', @catSecOpts,
    N'Shutdown Without Logon Disabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"ShutdownWithoutLogon","expected":0,"operator":"eq","display":"Checking shutdown without logon policy"}',
    N'GPO: Security Options > "Shutdown: Allow system to be shut down without having to log on" = Disabled. Prevents unauthorized physical shutdown of servers.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-078')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-078', @catSecOpts,
    N'Last Logged On User Display Hidden',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"DontDisplayLastUserName","expected":1,"operator":"eq","display":"Checking last logged-on user display policy"}',
    N'GPO: Security Options > "Interactive logon: Don''t display last signed-in" = Enabled. Prevents username enumeration at the logon screen.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-079')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-079', @catSecOpts,
    N'Legal Notice / Logon Banner Configured',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"LegalNoticeText","expected":"","operator":"neq","missingBehavior":"fail","display":"Checking legal notice text configuration"}',
    N'GPO: Security Options > "Interactive logon: Message text for users attempting to log on". A legal notice is required by many compliance frameworks and provides legal standing for prosecution.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='SRV-080')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SRV-080', @catAuditLog,
    N'Event Log Maximum Size - Security Log >= 128 MB',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\EventLog\\Security","valueName":"MaxSize","expected":134217728,"operator":"gte","display":"Checking Security event log maximum size"}',
    N'Set via GPO: Computer Configuration > Administrative Templates > Windows Components > Event Log Service > Security > "Maximum Log Size" >= 131072 KB (128 MB). Critical for audit retention on servers.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 12 -- Platform scope (MS19 + MS22 + MS25 ONLY)
-- ============================================================

-- Collect all SRV-* control IDs
DECLARE @srvBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @srvBlock VALUES
    ('SRV-001'),('SRV-002'),('SRV-003'),('SRV-004'),('SRV-005'),('SRV-006'),('SRV-007'),('SRV-008'),('SRV-009'),('SRV-010'),
    ('SRV-011'),('SRV-012'),('SRV-013'),('SRV-014'),('SRV-015'),('SRV-016'),('SRV-017'),('SRV-018'),('SRV-019'),('SRV-020'),
    ('SRV-021'),('SRV-022'),('SRV-023'),('SRV-024'),('SRV-025'),('SRV-026'),('SRV-027'),('SRV-028'),('SRV-029'),('SRV-030'),
    ('SRV-031'),('SRV-032'),('SRV-033'),('SRV-034'),('SRV-035'),('SRV-036'),('SRV-037'),('SRV-038'),('SRV-039'),('SRV-040'),
    ('SRV-041'),('SRV-042'),('SRV-043'),('SRV-044'),('SRV-045'),('SRV-046'),('SRV-047'),('SRV-048'),('SRV-049'),('SRV-050'),
    ('SRV-051'),('SRV-052'),('SRV-053'),('SRV-054'),('SRV-055'),('SRV-056'),('SRV-057'),('SRV-058'),('SRV-059'),('SRV-060'),
    ('SRV-061'),('SRV-062'),('SRV-063'),('SRV-064'),('SRV-065'),('SRV-066'),('SRV-067'),('SRV-068'),('SRV-069'),('SRV-070'),
    ('SRV-071'),('SRV-072'),('SRV-073'),('SRV-074'),('SRV-075'),('SRV-076'),('SRV-077'),('SRV-078'),('SRV-079'),('SRV-080');

-- Link to MS19
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platMS19
FROM control_defs cd
JOIN @srvBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platMS19
);

DECLARE @ms19Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for MS19: ', @ms19Rows);

-- Link to MS22
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platMS22
FROM control_defs cd
JOIN @srvBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platMS22
);

DECLARE @ms22Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for MS22: ', @ms22Rows);

-- Link to MS25
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platMS25
FROM control_defs cd
JOIN @srvBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platMS25
);

DECLARE @ms25Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for MS25: ', @ms25Rows);

-- ============================================================
-- PART 13 -- Framework mappings (ALL 5 frameworks)
-- ============================================================

-- Link all SRV-* controls to NIST (100% coverage)
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST
FROM control_defs cd
JOIN @srvBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwNIST
);

-- Link all SRV-* controls to CIS
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS
FROM control_defs cd
JOIN @srvBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwCIS
);

-- Link all SRV-* controls to HIPAA
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA
FROM control_defs cd
JOIN @srvBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwHIPAA
);

-- Link all SRV-* controls to ISO 27001
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO
FROM control_defs cd
JOIN @srvBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwISO
);

-- Link all SRV-* controls to PCI-DSS
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwPCI
FROM control_defs cd
JOIN @srvBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwPCI
);

-- ============================================================
-- PART 14 -- Link to existing active assessments
-- ============================================================
INSERT INTO assessment_controls (assessment_id, control_def_id)
SELECT a.id, cd.id
FROM assessments a
CROSS JOIN control_defs cd
JOIN @srvBlock b ON b.control_id = cd.control_id
WHERE a.is_active = 1
  AND a.deleted_at IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM assessment_controls ac
      WHERE ac.assessment_id = a.id AND ac.control_def_id = cd.id
  );

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification (run after applying)
-- ============================================================

-- Count new server controls
SELECT COUNT(*) AS new_server_controls
FROM control_defs
WHERE control_id LIKE 'SRV-%' AND is_active = 1;
-- Expected: 80

-- Verify platform linkage (server platforms only, no workstation)
SELECT p.code, COUNT(cp.control_def_id) AS srv_controls_linked
FROM platforms p
LEFT JOIN control_platforms cp ON cp.platform_id = p.id
LEFT JOIN control_defs cd ON cd.id = cp.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'SRV-%'
WHERE p.code IN ('W10','W11','MS19','MS22','MS25')
GROUP BY p.code
ORDER BY p.code;
-- Expected: W10=0, W11=0, MS19=80, MS22=80, MS25=80

-- Verify framework linkage
SELECT f.code, COUNT(cf.control_def_id) AS srv_controls_linked
FROM frameworks f
LEFT JOIN control_frameworks cf ON cf.framework_id = f.id
LEFT JOIN control_defs cd ON cd.id = cf.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'SRV-%'
WHERE f.is_active = 1
GROUP BY f.code
ORDER BY f.code;
-- Expected: CIS=80, HIPAA=80, ISO27001=80, NIST=80, PCI-DSS=80

-- Breakdown by engine type
SELECT cd.[type], COUNT(*) AS control_count
FROM control_defs cd
WHERE cd.control_id LIKE 'SRV-%' AND cd.is_active = 1
GROUP BY cd.[type]
ORDER BY cd.[type];

-- Breakdown by severity
SELECT cd.severity, COUNT(*) AS control_count
FROM control_defs cd
WHERE cd.control_id LIKE 'SRV-%' AND cd.is_active = 1
GROUP BY cd.severity
ORDER BY cd.severity;

-- Updated catalog totals
SELECT
    COUNT(*) AS total_active,
    SUM(CASE WHEN control_id LIKE 'SRV-%' THEN 1 ELSE 0 END) AS server_specific,
    SUM(CASE WHEN control_id NOT LIKE 'SRV-%' THEN 1 ELSE 0 END) AS shared_controls
FROM control_defs
WHERE is_active = 1;
-- Expected: total_active = 727 (647 + 80), server_specific = 80, shared_controls = 647
