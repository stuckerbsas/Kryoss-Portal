SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- seed_100_test_data.sql
-- Kryoss Platform — TEST DATA: Franchise + Org + Assessment + Controls + Enrollment Code
-- Run AFTER all migrations (001-009) and seeds (seed_001, seed_002)
-- =============================================

DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @franchiseId  UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @orgId        UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

-- =============================================
-- SYSTEM USER (for audit trail)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM users WHERE id = @systemUserId)
    INSERT INTO users (id, entra_oid, email, display_name, role_id, auth_source, created_by)
    VALUES (@systemUserId, @systemUserId, 'system@kryoss.dev', 'System',
            (SELECT id FROM roles WHERE code = 'super_admin'), 'entra', @systemUserId);

-- =============================================
-- TEST FRANCHISE: TeamLogic IT Panama
-- =============================================
IF NOT EXISTS (SELECT 1 FROM franchises WHERE id = @franchiseId)
    INSERT INTO franchises (id, name, legal_name, country, contact_email, status,
                            brand_name, brand_color_primary, brand_color_accent, created_by)
    VALUES (@franchiseId, N'TeamLogic IT Panama', N'Geminis Computer S.A.',
            N'Panama', 'feder@teamlogicit.com', 'active',
            N'TeamLogic IT', '#008852', '#A2C564', @systemUserId);

-- =============================================
-- TEST ORGANIZATION: Demo Client
-- =============================================
IF NOT EXISTS (SELECT 1 FROM organizations WHERE id = @orgId)
    INSERT INTO organizations (id, franchise_id, name, legal_name, status, created_by)
    VALUES (@orgId, @franchiseId, N'Demo Client', N'Demo Client S.A.',
            'current', @systemUserId);

-- =============================================
-- CONTROL DEFINITIONS: 90 CIS Level 1 controls
-- Each check_json contains BOTH agent instructions AND server-side evaluation
-- =============================================

-- Get category IDs
DECLARE @catPersonalization INT = (SELECT id FROM control_categories WHERE name = 'Personalization');
DECLARE @catNetworkSec      INT = (SELECT id FROM control_categories WHERE name = 'Network Security');
DECLARE @catSecurityOpt     INT = (SELECT id FROM control_categories WHERE name = 'Security Options');
DECLARE @catWindowsUpdate   INT = (SELECT id FROM control_categories WHERE name = 'Windows Update');
DECLARE @catNetworkProto    INT = (SELECT id FROM control_categories WHERE name = 'Network Protocol');
DECLARE @catAccountPol      INT = (SELECT id FROM control_categories WHERE name = 'Account Policies');
DECLARE @catAccountLock     INT = (SELECT id FROM control_categories WHERE name = 'Account Lockout');
DECLARE @catRemoteDesktop   INT = (SELECT id FROM control_categories WHERE name = 'Remote Desktop');
DECLARE @catWindowsDefender INT = (SELECT id FROM control_categories WHERE name = 'Windows Defender');
DECLARE @catExplorer        INT = (SELECT id FROM control_categories WHERE name = 'Explorer');
DECLARE @catFirewall        INT = (SELECT id FROM control_categories WHERE name = 'Firewall');
DECLARE @catServices        INT = (SELECT id FROM control_categories WHERE name = 'Services');
DECLARE @catAuditPolicy     INT = (SELECT id FROM control_categories WHERE name = 'Audit Policy');
DECLARE @catWinRM           INT = (SELECT id FROM control_categories WHERE name = 'WinRM');
DECLARE @catCredGuard       INT = (SELECT id FROM control_categories WHERE name = 'Credential Guard');

-- ── REGISTRY CONTROLS ──────────────────────────────────────────────

-- BL-001: Lock screen camera
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-001', @catPersonalization, N'Disable lock screen camera', 'registry', 'low',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\Personalization","valueName":"NoLockScreenCamera","display":"Reading lock screen camera policy","expected":"1","operator":"eq","missingBehavior":"fail"}',
 N'Set HKLM\\Software\\Policies\\Microsoft\\Windows\\Personalization\\NoLockScreenCamera = 1 (DWORD)', @systemUserId);

-- BL-002: Lock screen slideshow
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-002', @catPersonalization, N'Disable lock screen slideshow', 'registry', 'low',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\Personalization","valueName":"NoLockScreenSlideshow","display":"Reading lock screen slideshow policy","expected":"1","operator":"eq","missingBehavior":"fail"}',
 N'Set HKLM\\Software\\Policies\\Microsoft\\Windows\\Personalization\\NoLockScreenSlideshow = 1 (DWORD)', @systemUserId);

-- BL-003: Input personalization
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-003', @catPersonalization, N'Disable speech/inking/typing personalization', 'registry', 'low',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\InputPersonalization","valueName":"AllowInputPersonalization","display":"Reading speech/inking/typing personalization","expected":"0","operator":"eq","missingBehavior":"fail"}',
 N'Set HKLM\\Software\\Policies\\Microsoft\\InputPersonalization\\AllowInputPersonalization = 0 (DWORD)', @systemUserId);

-- BL-004: OneDrive sync
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-004', @catPersonalization, N'Disable OneDrive file sync (if required)', 'registry', 'low',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\OneDrive","valueName":"DisableFileSyncNGSC","display":"Reading OneDrive file sync policy","expected":"1","operator":"eq","missingBehavior":"warn"}',
 N'Set HKLM\\Software\\Policies\\Microsoft\\Windows\\OneDrive\\DisableFileSyncNGSC = 1 (DWORD). Note: this disables OneDrive sync completely.', @systemUserId);

-- BL-010: SMBv1 server
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-010', @catNetworkSec, N'Disable SMBv1 server', 'registry', 'critical',
 '{"hive":"HKLM","path":"System\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"SMB1","display":"Reading SMBv1 server status","expected":"0","operator":"eq","missingBehavior":"pass"}',
 N'Set HKLM\\System\\CurrentControlSet\\Services\\LanmanServer\\Parameters\\SMB1 = 0 (DWORD) or run: Disable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol', @systemUserId);

-- BL-011: SMBv1 client
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-011', @catNetworkSec, N'Disable SMBv1 client driver', 'registry', 'critical',
 '{"hive":"HKLM","path":"System\\CurrentControlSet\\Services\\mrxsmb10","valueName":"Start","display":"Reading SMBv1 client driver status","expected":"4","operator":"eq","missingBehavior":"pass"}',
 N'Set HKLM\\System\\CurrentControlSet\\Services\\mrxsmb10\\Start = 4 (Disabled)', @systemUserId);

-- BL-020: NTLMv2
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-020', @catNetworkSec, N'Require NTLMv2 authentication', 'registry', 'critical',
 '{"hive":"HKLM","path":"System\\CurrentControlSet\\Control\\Lsa","valueName":"LmCompatibilityLevel","display":"Reading NTLMv2 authentication level","expected":"5","operator":"gte","missingBehavior":"fail"}',
 N'Set HKLM\\System\\CurrentControlSet\\Control\\Lsa\\LmCompatibilityLevel = 5 (Send NTLMv2 response only, refuse LM & NTLM)', @systemUserId);

-- BL-021: LM Hash
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-021', @catNetworkSec, N'Disable LM hash storage', 'registry', 'high',
 '{"hive":"HKLM","path":"System\\CurrentControlSet\\Control\\Lsa","valueName":"NoLMHash","display":"Reading LM hash storage policy","expected":"1","operator":"eq","missingBehavior":"fail"}',
 N'Set HKLM\\System\\CurrentControlSet\\Control\\Lsa\\NoLMHash = 1', @systemUserId);

-- BL-030: Windows Update auto-update
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-030', @catWindowsUpdate, N'Enable Windows Update auto-update', 'registry', 'high',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU","valueName":"NoAutoUpdate","display":"Reading Windows Update auto-update policy","expected":"0","operator":"eq","missingBehavior":"pass"}',
 N'Set NoAutoUpdate = 0 or delete the value to use default (auto-update enabled)', @systemUserId);

-- BL-031: Windows Update options (4 = auto download + schedule install)
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-031', @catWindowsUpdate, N'Configure auto-download and install', 'registry', 'medium',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU","valueName":"AUOptions","display":"Reading Windows Update options","expected":"4","operator":"eq","missingBehavior":"warn"}',
 N'Set AUOptions = 4 (Auto download and schedule install)', @systemUserId);

-- BL-040: NetBIOS node type
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-040', @catNetworkProto, N'Set NetBIOS node type to P-node', 'registry', 'medium',
 '{"hive":"HKLM","path":"System\\CurrentControlSet\\Services\\Netbt\\Parameters","valueName":"NodeType","display":"Reading NetBIOS node type","expected":"2","operator":"eq","missingBehavior":"fail"}',
 N'Set NodeType = 2 (P-node, uses WINS only — disables broadcast resolution)', @systemUserId);

-- BL-041: LLMNR
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-041', @catNetworkProto, N'Disable LLMNR', 'registry', 'high',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows NT\\DNSClient","valueName":"EnableMulticast","display":"Reading LLMNR status","expected":"0","operator":"eq","missingBehavior":"fail"}',
 N'Set EnableMulticast = 0 (disables Link-Local Multicast Name Resolution)', @systemUserId);

-- BL-050: UAC enabled
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-050', @catSecurityOpt, N'Enable UAC', 'registry', 'critical',
 '{"hive":"HKLM","path":"Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"EnableLUA","display":"Reading UAC status","expected":"1","operator":"eq","missingBehavior":"fail"}',
 N'Set EnableLUA = 1', @systemUserId);

-- BL-051: UAC consent prompt
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-051', @catSecurityOpt, N'Configure UAC admin consent prompt', 'registry', 'high',
 '{"hive":"HKLM","path":"Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"ConsentPromptBehaviorAdmin","display":"Reading UAC admin consent prompt behavior","expected":"2","operator":"lte","missingBehavior":"fail"}',
 N'Set ConsentPromptBehaviorAdmin to 1 or 2 (prompt for credentials or consent on secure desktop)', @systemUserId);

-- BL-052: Secure desktop
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-052', @catSecurityOpt, N'Enable UAC secure desktop prompt', 'registry', 'high',
 '{"hive":"HKLM","path":"Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"PromptOnSecureDesktop","display":"Reading UAC secure desktop prompt","expected":"1","operator":"eq","missingBehavior":"fail"}',
 N'Set PromptOnSecureDesktop = 1', @systemUserId);

-- BL-060: RDP disabled
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-060', @catRemoteDesktop, N'Disable Remote Desktop (if unused)', 'registry', 'medium',
 '{"hive":"HKLM","path":"System\\CurrentControlSet\\Control\\Terminal Server","valueName":"fDenyTSConnections","display":"Reading Remote Desktop status","expected":"1","operator":"eq","missingBehavior":"warn"}',
 N'Set fDenyTSConnections = 1 to disable RDP, or restrict by IP if needed', @systemUserId);

-- BL-061: RDP NLA
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-061', @catRemoteDesktop, N'Require NLA for Remote Desktop', 'registry', 'high',
 '{"hive":"HKLM","path":"System\\CurrentControlSet\\Control\\Terminal Server\\WinStations\\RDP-Tcp","valueName":"UserAuthentication","display":"Reading RDP NLA requirement","expected":"1","operator":"eq","missingBehavior":"fail"}',
 N'Set UserAuthentication = 1 (Network Level Authentication required)', @systemUserId);

-- BL-070: Defender not disabled
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-070', @catWindowsDefender, N'Ensure Windows Defender is not disabled', 'registry', 'critical',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows Defender","valueName":"DisableAntiSpyware","display":"Reading Windows Defender status","expected":"0","operator":"eq","missingBehavior":"pass"}',
 N'Delete DisableAntiSpyware or set to 0. If absent, Defender is active by default.', @systemUserId);

-- BL-071: Defender real-time
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-071', @catWindowsDefender, N'Ensure Defender real-time protection is not disabled', 'registry', 'critical',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows Defender\\Real-Time Protection","valueName":"DisableRealtimeMonitoring","display":"Reading Defender real-time protection","expected":"0","operator":"eq","missingBehavior":"pass"}',
 N'Delete DisableRealtimeMonitoring or set to 0', @systemUserId);

-- BL-080: Show hidden files
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-080', @catExplorer, N'Show hidden files', 'registry', 'low',
 '{"hive":"HKCU","path":"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","valueName":"Hidden","display":"Reading show hidden files setting","expected":"1","operator":"eq","missingBehavior":"warn"}',
 N'Set Hidden = 1 (show hidden files and folders)', @systemUserId);

-- BL-081: Show file extensions
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-081', @catExplorer, N'Show file extensions', 'registry', 'medium',
 '{"hive":"HKCU","path":"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","valueName":"HideFileExt","display":"Reading hide file extensions setting","expected":"0","operator":"eq","missingBehavior":"warn"}',
 N'Set HideFileExt = 0 (show known file extensions — prevents .pdf.exe attacks)', @systemUserId);

-- BL-090: Inactivity timeout
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-090', @catSecurityOpt, N'Set machine inactivity timeout', 'registry', 'medium',
 '{"hive":"HKLM","path":"Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"InactivityTimeoutSecs","display":"Reading machine inactivity timeout","expected":"900","operator":"lte","missingBehavior":"fail"}',
 N'Set InactivityTimeoutSecs <= 900 (15 minutes or less)', @systemUserId);

-- BL-091: Legal notice caption
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-091', @catSecurityOpt, N'Set logon legal notice caption', 'registry', 'medium',
 '{"hive":"HKLM","path":"Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"LegalNoticeCaption","display":"Reading logon legal notice caption","expected":"","operator":"neq","missingBehavior":"fail"}',
 N'Set a legal notice caption at logon (e.g., "Authorized Use Only")', @systemUserId);

-- BL-092: Legal notice text
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-092', @catSecurityOpt, N'Set logon legal notice text', 'registry', 'medium',
 '{"hive":"HKLM","path":"Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"LegalNoticeText","display":"Reading logon legal notice text","expected":"","operator":"neq","missingBehavior":"fail"}',
 N'Set a legal notice text at logon', @systemUserId);

-- BL-100: AlwaysInstallElevated (machine)
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-100', @catSecurityOpt, N'Disable AlwaysInstallElevated (machine)', 'registry', 'critical',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\Installer","valueName":"AlwaysInstallElevated","display":"Reading AlwaysInstallElevated (machine)","expected":"0","operator":"eq","missingBehavior":"pass"}',
 N'Set AlwaysInstallElevated = 0 or delete the value. If absent, feature is disabled.', @systemUserId);

-- BL-101: AlwaysInstallElevated (user)
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-101', @catSecurityOpt, N'Disable AlwaysInstallElevated (user)', 'registry', 'critical',
 '{"hive":"HKCU","path":"Software\\Policies\\Microsoft\\Windows\\Installer","valueName":"AlwaysInstallElevated","display":"Reading AlwaysInstallElevated (user)","expected":"0","operator":"eq","missingBehavior":"pass"}',
 N'Set AlwaysInstallElevated = 0 or delete the value', @systemUserId);

-- BL-110: WinRM basic auth (service)
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-110', @catWinRM, N'Disable WinRM basic auth (service)', 'registry', 'high',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\WinRM\\Service","valueName":"AllowBasic","display":"Reading WinRM basic auth (service)","expected":"0","operator":"eq","missingBehavior":"pass"}',
 N'Set AllowBasic = 0 under WinRM\\Service', @systemUserId);

-- BL-111: WinRM basic auth (client)
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-111', @catWinRM, N'Disable WinRM basic auth (client)', 'registry', 'high',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\WinRM\\Client","valueName":"AllowBasic","display":"Reading WinRM basic auth (client)","expected":"0","operator":"eq","missingBehavior":"pass"}',
 N'Set AllowBasic = 0 under WinRM\\Client', @systemUserId);

-- BL-120: WDigest
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-120', @catCredGuard, N'Disable WDigest plaintext credential caching', 'registry', 'critical',
 '{"hive":"HKLM","path":"System\\CurrentControlSet\\Control\\SecurityProviders\\WDigest","valueName":"UseLogonCredential","display":"Reading WDigest plaintext credential caching","expected":"0","operator":"eq","missingBehavior":"pass"}',
 N'Set UseLogonCredential = 0', @systemUserId);

-- BL-130: Credential delegation
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-130', @catCredGuard, N'Enable Credential Guard remote delegation protection', 'registry', 'medium',
 '{"hive":"HKLM","path":"Software\\Policies\\Microsoft\\Windows\\CredentialsDelegation","valueName":"AllowProtectedCreds","display":"Reading Credential Guard remote delegation","expected":"1","operator":"eq","missingBehavior":"warn"}',
 N'Set AllowProtectedCreds = 1', @systemUserId);

-- ── SECEDIT CONTROLS ────────────────────────────────────────────────

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-200', @catAccountPol, N'Minimum password length >= 14', 'secedit', 'critical',
 '{"settingName":"MinimumPasswordLength","display":"Reading minimum password length policy","expected":"14","operator":"gte","missingBehavior":"fail"}',
 N'Set Minimum password length to 14 or greater via Group Policy or secpol.msc', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-201', @catAccountPol, N'Password complexity enabled', 'secedit', 'high',
 '{"settingName":"PasswordComplexity","display":"Reading password complexity requirement","expected":"1","operator":"eq","missingBehavior":"fail"}',
 N'Enable password complexity requirements', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-202', @catAccountPol, N'Maximum password age <= 90 days', 'secedit', 'medium',
 '{"settingName":"MaximumPasswordAge","display":"Reading maximum password age","expected":"90","operator":"lte","missingBehavior":"fail"}',
 N'Set maximum password age to 90 days or less', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-203', @catAccountPol, N'Minimum password age >= 1 day', 'secedit', 'medium',
 '{"settingName":"MinimumPasswordAge","display":"Reading minimum password age","expected":"1","operator":"gte","missingBehavior":"fail"}',
 N'Set minimum password age to 1 day or more', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-204', @catAccountPol, N'Password history >= 24', 'secedit', 'medium',
 '{"settingName":"PasswordHistorySize","display":"Reading password history size","expected":"24","operator":"gte","missingBehavior":"fail"}',
 N'Set password history to 24 or more remembered passwords', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-205', @catAccountPol, N'Disable reversible encryption', 'secedit', 'critical',
 '{"settingName":"ClearTextPassword","display":"Reading reversible encryption status","expected":"0","operator":"eq","missingBehavior":"fail"}',
 N'Disable "Store passwords using reversible encryption"', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-210', @catAccountLock, N'Account lockout threshold <= 5', 'secedit', 'high',
 '{"settingName":"LockoutBadCount","display":"Reading account lockout threshold","expected":"5","operator":"lte","missingBehavior":"fail"}',
 N'Set account lockout threshold to 5 or fewer invalid attempts', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-211', @catAccountLock, N'Lockout counter reset >= 30 min', 'secedit', 'medium',
 '{"settingName":"ResetLockoutCount","display":"Reading lockout counter reset time","expected":"30","operator":"gte","missingBehavior":"fail"}',
 N'Set lockout counter reset time to 30 minutes or more', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-212', @catAccountLock, N'Lockout duration >= 30 min', 'secedit', 'medium',
 '{"settingName":"LockoutDuration","display":"Reading lockout duration","expected":"30","operator":"gte","missingBehavior":"fail"}',
 N'Set lockout duration to 30 minutes or more', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-220', @catAccountPol, N'Guest account disabled', 'secedit', 'high',
 '{"settingName":"EnableGuestAccount","display":"Reading guest account status","expected":"0","operator":"eq","missingBehavior":"fail"}',
 N'Disable the Guest account', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-221', @catAccountPol, N'Built-in Administrator disabled', 'secedit', 'medium',
 '{"settingName":"EnableAdminAccount","display":"Reading built-in administrator account status","expected":"0","operator":"eq","missingBehavior":"warn"}',
 N'Disable the built-in Administrator account (use a renamed admin)', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-222', @catAccountPol, N'Rename Administrator account', 'secedit', 'medium',
 '{"settingName":"NewAdministratorName","display":"Reading renamed administrator account name","expected":"Administrator","operator":"neq","missingBehavior":"fail"}',
 N'Rename the built-in Administrator account to a non-obvious name', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-223', @catAccountPol, N'Rename Guest account', 'secedit', 'low',
 '{"settingName":"NewGuestName","display":"Reading renamed guest account name","expected":"Guest","operator":"neq","missingBehavior":"fail"}',
 N'Rename the built-in Guest account', @systemUserId);

-- ── AUDITPOL CONTROLS ───────────────────────────────────────────────

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-300', @catAuditPolicy, N'Audit Credential Validation', 'auditpol', 'high',
 '{"subcategory":"Credential Validation","display":"Checking credential validation audit policy","expected":"Success and Failure","operator":"eq","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Credential Validation" /success:enable /failure:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-301', @catAuditPolicy, N'Audit Application Group Management', 'auditpol', 'medium',
 '{"subcategory":"Application Group Management","display":"Checking application group management audit","expected":"Success and Failure","operator":"eq","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Application Group Management" /success:enable /failure:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-302', @catAuditPolicy, N'Audit Security Group Management', 'auditpol', 'high',
 '{"subcategory":"Security Group Management","display":"Checking security group management audit","expected":"Success and Failure","operator":"eq","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Security Group Management" /success:enable /failure:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-303', @catAuditPolicy, N'Audit User Account Management', 'auditpol', 'high',
 '{"subcategory":"User Account Management","display":"Checking user account management audit","expected":"Success and Failure","operator":"eq","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"User Account Management" /success:enable /failure:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-304', @catAuditPolicy, N'Audit Process Creation', 'auditpol', 'medium',
 '{"subcategory":"Process Creation","display":"Checking process creation audit","expected":"Success","operator":"contains","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Process Creation" /success:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-305', @catAuditPolicy, N'Audit Account Lockout', 'auditpol', 'medium',
 '{"subcategory":"Account Lockout","display":"Checking account lockout audit","expected":"Failure","operator":"contains","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Account Lockout" /failure:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-306', @catAuditPolicy, N'Audit Logoff', 'auditpol', 'low',
 '{"subcategory":"Logoff","display":"Checking logoff audit","expected":"Success","operator":"contains","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Logoff" /success:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-307', @catAuditPolicy, N'Audit Logon', 'auditpol', 'high',
 '{"subcategory":"Logon","display":"Checking logon audit","expected":"Success and Failure","operator":"eq","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Logon" /success:enable /failure:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-308', @catAuditPolicy, N'Audit Special Logon', 'auditpol', 'medium',
 '{"subcategory":"Special Logon","display":"Checking special logon audit","expected":"Success","operator":"contains","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Special Logon" /success:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-309', @catAuditPolicy, N'Audit Policy Change', 'auditpol', 'high',
 '{"subcategory":"Audit Policy Change","display":"Checking audit policy change audit","expected":"Success","operator":"contains","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Audit Policy Change" /success:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-310', @catAuditPolicy, N'Audit Authentication Policy Change', 'auditpol', 'medium',
 '{"subcategory":"Authentication Policy Change","display":"Checking authentication policy change audit","expected":"Success","operator":"contains","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Authentication Policy Change" /success:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-311', @catAuditPolicy, N'Audit Sensitive Privilege Use', 'auditpol', 'high',
 '{"subcategory":"Sensitive Privilege Use","display":"Checking sensitive privilege use audit","expected":"Success and Failure","operator":"eq","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Sensitive Privilege Use" /success:enable /failure:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-312', @catAuditPolicy, N'Audit Security State Change', 'auditpol', 'medium',
 '{"subcategory":"Security State Change","display":"Checking security state change audit","expected":"Success","operator":"contains","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Security State Change" /success:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-313', @catAuditPolicy, N'Audit Security System Extension', 'auditpol', 'high',
 '{"subcategory":"Security System Extension","display":"Checking security system extension audit","expected":"Success","operator":"contains","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"Security System Extension" /success:enable', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-314', @catAuditPolicy, N'Audit System Integrity', 'auditpol', 'high',
 '{"subcategory":"System Integrity","display":"Checking system integrity audit","expected":"Success and Failure","operator":"eq","missingBehavior":"fail"}',
 N'auditpol /set /subcategory:"System Integrity" /success:enable /failure:enable', @systemUserId);

-- ── FIREWALL CONTROLS ───────────────────────────────────────────────

-- Domain Profile
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-400', @catFirewall, N'Domain firewall enabled', 'firewall', 'critical',
 '{"profile":"Domain","property":"Enabled","display":"Checking domain firewall status","expected":"True","operator":"eq","missingBehavior":"fail"}',
 N'Enable Windows Firewall for Domain profile', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-401', @catFirewall, N'Domain firewall inbound default: Block', 'firewall', 'high',
 '{"profile":"Domain","property":"DefaultInboundAction","display":"Checking domain firewall inbound default","expected":"Block","operator":"eq","missingBehavior":"fail"}',
 N'Set Domain firewall default inbound action to Block', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-402', @catFirewall, N'Domain firewall outbound default: Allow', 'firewall', 'medium',
 '{"profile":"Domain","property":"DefaultOutboundAction","display":"Checking domain firewall outbound default","expected":"Allow","operator":"eq","missingBehavior":"fail"}',
 N'Set Domain firewall default outbound action to Allow', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-403', @catFirewall, N'Domain firewall log file path configured', 'firewall', 'medium',
 '{"profile":"Domain","property":"LogFilePath","display":"Reading domain firewall log path","expected":"","operator":"neq","missingBehavior":"fail"}',
 N'Configure a log file path for the Domain firewall profile', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-404', @catFirewall, N'Domain firewall log size >= 16384 KB', 'firewall', 'low',
 '{"profile":"Domain","property":"LogFileSize","display":"Reading domain firewall log size","expected":"16384","operator":"gte","missingBehavior":"fail"}',
 N'Set Domain firewall log file size to 16384 KB or larger', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-405', @catFirewall, N'Domain firewall log dropped packets', 'firewall', 'medium',
 '{"profile":"Domain","property":"LogDroppedPackets","display":"Checking domain firewall dropped packet logging","expected":"True","operator":"eq","missingBehavior":"fail"}',
 N'Enable logging of dropped packets for Domain profile', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-406', @catFirewall, N'Domain firewall log successful connections', 'firewall', 'low',
 '{"profile":"Domain","property":"LogSuccessfulConnections","display":"Checking domain firewall success logging","expected":"True","operator":"eq","missingBehavior":"fail"}',
 N'Enable logging of successful connections for Domain profile', @systemUserId);

-- Private Profile
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-410', @catFirewall, N'Private firewall enabled', 'firewall', 'critical',
 '{"profile":"Private","property":"Enabled","display":"Checking private firewall status","expected":"True","operator":"eq","missingBehavior":"fail"}',
 N'Enable Windows Firewall for Private profile', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-411', @catFirewall, N'Private firewall inbound default: Block', 'firewall', 'high',
 '{"profile":"Private","property":"DefaultInboundAction","display":"Checking private firewall inbound default","expected":"Block","operator":"eq","missingBehavior":"fail"}',
 N'Set Private firewall default inbound action to Block', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-412', @catFirewall, N'Private firewall outbound default: Allow', 'firewall', 'medium',
 '{"profile":"Private","property":"DefaultOutboundAction","display":"Checking private firewall outbound default","expected":"Allow","operator":"eq","missingBehavior":"fail"}',
 N'Set Private firewall default outbound action to Allow', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-413', @catFirewall, N'Private firewall log file path configured', 'firewall', 'medium',
 '{"profile":"Private","property":"LogFilePath","display":"Reading private firewall log path","expected":"","operator":"neq","missingBehavior":"fail"}',
 N'Configure a log file path for the Private firewall profile', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-414', @catFirewall, N'Private firewall log size >= 16384 KB', 'firewall', 'low',
 '{"profile":"Private","property":"LogFileSize","display":"Reading private firewall log size","expected":"16384","operator":"gte","missingBehavior":"fail"}',
 N'Set Private firewall log file size to 16384 KB or larger', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-415', @catFirewall, N'Private firewall log dropped packets', 'firewall', 'medium',
 '{"profile":"Private","property":"LogDroppedPackets","display":"Checking private firewall dropped packet logging","expected":"True","operator":"eq","missingBehavior":"fail"}',
 N'Enable logging of dropped packets for Private profile', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-416', @catFirewall, N'Private firewall log successful connections', 'firewall', 'low',
 '{"profile":"Private","property":"LogSuccessfulConnections","display":"Checking private firewall success logging","expected":"True","operator":"eq","missingBehavior":"fail"}',
 N'Enable logging of successful connections for Private profile', @systemUserId);

-- Public Profile
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-420', @catFirewall, N'Public firewall enabled', 'firewall', 'critical',
 '{"profile":"Public","property":"Enabled","display":"Checking public firewall status","expected":"True","operator":"eq","missingBehavior":"fail"}',
 N'Enable Windows Firewall for Public profile', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-421', @catFirewall, N'Public firewall inbound default: Block', 'firewall', 'high',
 '{"profile":"Public","property":"DefaultInboundAction","display":"Checking public firewall inbound default","expected":"Block","operator":"eq","missingBehavior":"fail"}',
 N'Set Public firewall default inbound action to Block', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-422', @catFirewall, N'Public firewall outbound default: Allow', 'firewall', 'medium',
 '{"profile":"Public","property":"DefaultOutboundAction","display":"Checking public firewall outbound default","expected":"Allow","operator":"eq","missingBehavior":"fail"}',
 N'Set Public firewall default outbound action to Allow', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-423', @catFirewall, N'Public firewall log file path configured', 'firewall', 'medium',
 '{"profile":"Public","property":"LogFilePath","display":"Reading public firewall log path","expected":"","operator":"neq","missingBehavior":"fail"}',
 N'Configure a log file path for the Public firewall profile', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-424', @catFirewall, N'Public firewall log size >= 16384 KB', 'firewall', 'low',
 '{"profile":"Public","property":"LogFileSize","display":"Reading public firewall log size","expected":"16384","operator":"gte","missingBehavior":"fail"}',
 N'Set Public firewall log file size to 16384 KB or larger', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-425', @catFirewall, N'Public firewall log dropped packets', 'firewall', 'medium',
 '{"profile":"Public","property":"LogDroppedPackets","display":"Checking public firewall dropped packet logging","expected":"True","operator":"eq","missingBehavior":"fail"}',
 N'Enable logging of dropped packets for Public profile', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-426', @catFirewall, N'Public firewall log successful connections', 'firewall', 'low',
 '{"profile":"Public","property":"LogSuccessfulConnections","display":"Checking public firewall success logging","expected":"True","operator":"eq","missingBehavior":"fail"}',
 N'Enable logging of successful connections for Public profile', @systemUserId);

-- ── SERVICE CONTROLS ────────────────────────────────────────────────

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-500', @catServices, N'Disable Xbox Game Save service', 'service', 'low',
 '{"serviceName":"XblGameSave","display":"Checking Xbox Game Save service","expected":"Disabled","operator":"eq","missingBehavior":"pass"}',
 N'Set XblGameSave service StartType to Disabled', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-501', @catServices, N'Disable Xbox Accessory Management service', 'service', 'low',
 '{"serviceName":"XboxGipSvc","display":"Checking Xbox Accessory Management service","expected":"Disabled","operator":"eq","missingBehavior":"pass"}',
 N'Set XboxGipSvc service StartType to Disabled', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-502', @catServices, N'Disable Xbox Live Networking service', 'service', 'low',
 '{"serviceName":"XboxNetApiSvc","display":"Checking Xbox Live Networking service","expected":"Disabled","operator":"eq","missingBehavior":"pass"}',
 N'Set XboxNetApiSvc service StartType to Disabled', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-503', @catServices, N'Disable Remote Registry service', 'service', 'high',
 '{"serviceName":"RemoteRegistry","display":"Checking Remote Registry service","expected":"Disabled","operator":"eq","missingBehavior":"pass"}',
 N'Set RemoteRegistry service StartType to Disabled', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-504', @catServices, N'Disable Link-Layer Topology Discovery service', 'service', 'low',
 '{"serviceName":"lltdsvc","display":"Checking Link-Layer Topology Discovery service","expected":"Disabled","operator":"eq","missingBehavior":"pass"}',
 N'Set lltdsvc service StartType to Disabled', @systemUserId);

-- ── NETACCOUNT CONTROLS ─────────────────────────────────────────────

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-550', @catAccountPol, N'Net accounts: minimum password length', 'netaccount', 'high',
 '{"field":"MinimumPasswordLength","display":"Reading net accounts password length","expected":"14","operator":"gte","missingBehavior":"fail"}',
 N'net accounts /minpwlen:14', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-551', @catAccountPol, N'Net accounts: maximum password age', 'netaccount', 'medium',
 '{"field":"MaximumPasswordAge","display":"Reading net accounts max password age","expected":"90","operator":"lte","missingBehavior":"fail"}',
 N'net accounts /maxpwage:90', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-552', @catAccountPol, N'Net accounts: minimum password age', 'netaccount', 'low',
 '{"field":"MinimumPasswordAge","display":"Reading net accounts min password age","expected":"1","operator":"gte","missingBehavior":"fail"}',
 N'net accounts /minpwage:1', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-553', @catAccountLock, N'Net accounts: lockout threshold', 'netaccount', 'high',
 '{"field":"LockoutThreshold","display":"Reading net accounts lockout threshold","expected":"5","operator":"lte","missingBehavior":"fail"}',
 N'net accounts /lockoutthreshold:5', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-554', @catAccountLock, N'Net accounts: lockout duration', 'netaccount', 'medium',
 '{"field":"LockoutDuration","display":"Reading net accounts lockout duration","expected":"30","operator":"gte","missingBehavior":"fail"}',
 N'net accounts /lockoutduration:30', @systemUserId);

-- ── COMMAND CONTROLS ────────────────────────────────────────────────

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-580', @catSecurityOpt, N'Check boot configuration (bcdedit)', 'command', 'medium',
 '{"executable":"bcdedit.exe","arguments":"/enum {default}","display":"Reading boot configuration data","expected":"nx","operator":"contains","missingBehavior":"fail"}',
 N'Verify DEP/NX is enabled via bcdedit /set nx AlwaysOn', @systemUserId);

INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, created_by) VALUES
('BL-581', @catSecurityOpt, N'Check for EFS encrypted files', 'command', 'low',
 '{"executable":"cipher.exe","arguments":"/u /n","display":"Checking EFS encrypted files","expected":"","operator":"exists","missingBehavior":"warn"}',
 N'Informational: lists EFS-encrypted files on the system', @systemUserId);

-- =============================================
-- ASSESSMENT PROFILE: CIS Level 1 - Windows 11
-- =============================================
DECLARE @assessmentId INT;

INSERT INTO assessments (organization_id, name, description, is_default, is_active, created_by)
VALUES (@orgId, N'CIS Level 1 - Windows 11', N'CIS Benchmark Level 1 assessment for Windows 11 endpoints', 1, 1, @systemUserId);

SET @assessmentId = SCOPE_IDENTITY();

-- Link ALL 90 controls to this assessment
INSERT INTO assessment_controls (assessment_id, control_def_id)
SELECT @assessmentId, id FROM control_defs WHERE deleted_at IS NULL;

-- =============================================
-- ENROLLMENT CODE: Ready to use for testing
-- =============================================
INSERT INTO enrollment_codes (organization_id, code, assessment_id, label, expires_at, created_by)
VALUES (@orgId, 'K7X9-M2P4-Q8R1-T5W3', @assessmentId, N'Test Enrollment Code',
        DATEADD(YEAR, 1, SYSUTCDATETIME()), @systemUserId);

-- =============================================
-- LINK CONTROLS TO FRAMEWORKS (CIS)
-- =============================================
DECLARE @cisId INT = (SELECT id FROM frameworks WHERE code = 'CIS');

INSERT INTO control_frameworks (control_def_id, framework_id, framework_ref)
SELECT id, @cisId,
    CASE
        WHEN control_id LIKE 'BL-00%' THEN 'CIS 2.3.' + CAST(RIGHT(control_id, 1) AS VARCHAR)
        WHEN control_id LIKE 'BL-01%' THEN 'CIS 9.1.' + CAST(RIGHT(control_id, 1) AS VARCHAR)
        WHEN control_id LIKE 'BL-02%' THEN 'CIS 2.3.11.' + CAST(RIGHT(control_id, 1) AS VARCHAR)
        WHEN control_id LIKE 'BL-03%' THEN 'CIS 5.4.' + CAST(RIGHT(control_id, 1) AS VARCHAR)
        WHEN control_id LIKE 'BL-04%' THEN 'CIS 18.4.' + CAST(RIGHT(control_id, 1) AS VARCHAR)
        WHEN control_id LIKE 'BL-05%' THEN 'CIS 2.3.17.' + CAST(RIGHT(control_id, 1) AS VARCHAR)
        WHEN control_id LIKE 'BL-06%' THEN 'CIS 18.9.65.' + CAST(RIGHT(control_id, 1) AS VARCHAR)
        WHEN control_id LIKE 'BL-07%' THEN 'CIS 18.9.47.' + CAST(RIGHT(control_id, 1) AS VARCHAR)
        WHEN control_id LIKE 'BL-2%'  THEN 'CIS 1.1.' + CAST(RIGHT(control_id, 1) AS VARCHAR)
        WHEN control_id LIKE 'BL-3%'  THEN 'CIS 17.x'
        WHEN control_id LIKE 'BL-4%'  THEN 'CIS 9.1.x'
        WHEN control_id LIKE 'BL-5%'  THEN 'CIS 5.x'
        ELSE 'CIS L1'
    END
FROM control_defs WHERE deleted_at IS NULL;

-- =============================================
-- LINK CONTROLS TO PLATFORMS (W10, W11)
-- =============================================
DECLARE @w10Id INT = (SELECT id FROM platforms WHERE code = 'W10');
DECLARE @w11Id INT = (SELECT id FROM platforms WHERE code = 'W11');

INSERT INTO control_platforms (control_def_id, platform_id)
SELECT id, @w10Id FROM control_defs WHERE deleted_at IS NULL;

INSERT INTO control_platforms (control_def_id, platform_id)
SELECT id, @w11Id FROM control_defs WHERE deleted_at IS NULL;

-- =============================================
-- VERIFICATION
-- =============================================
SELECT 'Controls seeded' AS step, COUNT(*) AS [count] FROM control_defs WHERE deleted_at IS NULL
UNION ALL
SELECT 'Assessment controls linked', COUNT(*) FROM assessment_controls WHERE assessment_id = @assessmentId
UNION ALL
SELECT 'Enrollment codes', COUNT(*) FROM enrollment_codes WHERE deleted_at IS NULL
UNION ALL
SELECT 'Framework links', COUNT(*) FROM control_frameworks
UNION ALL
SELECT 'Platform links', COUNT(*) FROM control_platforms;

PRINT 'Test data seeded successfully!';
PRINT 'Enrollment code: K7X9-M2P4-Q8R1-T5W3';
PRINT 'Assessment ID: ' + CAST(@assessmentId AS VARCHAR);
PRINT 'Organization: Demo Client (' + CAST(@orgId AS VARCHAR(36)) + ')';
PRINT 'Franchise: TeamLogic IT Panama (' + CAST(@franchiseId AS VARCHAR(36)) + ')';
GO
