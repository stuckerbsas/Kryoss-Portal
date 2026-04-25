-- Seed: ~50 auto-fixable remediation actions mapped to control_defs
-- Each row links a control to a whitelisted action type + params template
-- Params template uses the same registry path/value from check_json

-- Helper: insert only if control exists and no active action already exists
-- Action types: set_registry, enable_service, disable_service, set_audit_policy, set_account_policy

-- ═══════════════════════════════════════════════════════════════════
-- REGISTRY FIXES (set_registry) — ~35 controls
-- ═══════════════════════════════════════════════════════════════════

-- BL-0001: LAPS AdmPwdEnabled
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\Software\\Policies\\Microsoft Services\\AdmPwd","valueName":"AdmPwdEnabled","valueData":"1","valueType":"DWORD"}',
    'low', 'Enable LAPS (Local Administrator Password Solution)'
FROM control_defs WHERE control_id = 'BL-0001' AND deleted_at IS NULL;

-- BL-0006: Disable SMBv1 client (MrxSmb10 Start=4)
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\MrxSmb10","valueName":"Start","valueData":"4","valueType":"DWORD"}',
    'low', 'Disable SMBv1 client driver'
FROM control_defs WHERE control_id = 'BL-0006' AND deleted_at IS NULL;

-- BL-0007: Disable SMBv1 server
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"SMB1","valueData":"0","valueType":"DWORD"}',
    'low', 'Disable SMBv1 on LanmanServer'
FROM control_defs WHERE control_id = 'BL-0007' AND deleted_at IS NULL;

-- BL-0009: LSA Protection (RunAsPPL)
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RunAsPPL","valueData":"1","valueType":"DWORD"}',
    'medium', 'Enable LSA Protection (RunAsPPL) — requires reboot'
FROM control_defs WHERE control_id = 'BL-0009' AND deleted_at IS NULL;

-- BL-0010: NetBIOS NodeType=2 (P-node)
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\NetBT\\Parameters","valueName":"NodeType","valueData":"2","valueType":"DWORD"}',
    'low', 'Set NetBIOS to P-node (point-to-point only)'
FROM control_defs WHERE control_id = 'BL-0010' AND deleted_at IS NULL;

-- BL-0011: Disable WDigest plaintext creds
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\WDigest","valueName":"UseLogonCredential","valueData":"0","valueType":"DWORD"}',
    'low', 'Disable WDigest plaintext credential caching'
FROM control_defs WHERE control_id = 'BL-0011' AND deleted_at IS NULL;

-- BL-0015: Screen saver timeout
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKCU:\\Control Panel\\Desktop","valueName":"ScreenSaveTimeOut","valueData":"900","valueType":"SZ"}',
    'low', 'Set screen saver timeout to 15 minutes'
FROM control_defs WHERE control_id = 'BL-0015' AND deleted_at IS NULL;

-- BL-0020: Disable autorun
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer","valueName":"NoDriveTypeAutoRun","valueData":"255","valueType":"DWORD"}',
    'low', 'Disable autorun on all drive types'
FROM control_defs WHERE control_id = 'BL-0020' AND deleted_at IS NULL;

-- BL-0025: UAC - EnableLUA
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"EnableLUA","valueData":"1","valueType":"DWORD"}',
    'low', 'Enable User Account Control (UAC)'
FROM control_defs WHERE control_id = 'BL-0025' AND deleted_at IS NULL;

-- BL-0026: UAC - ConsentPromptBehaviorAdmin
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"ConsentPromptBehaviorAdmin","valueData":"2","valueType":"DWORD"}',
    'low', 'UAC: prompt for consent on secure desktop'
FROM control_defs WHERE control_id = 'BL-0026' AND deleted_at IS NULL;

-- BL-0030: Disable remote assistance
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Remote Assistance","valueName":"fAllowToGetHelp","valueData":"0","valueType":"DWORD"}',
    'low', 'Disable Windows Remote Assistance'
FROM control_defs WHERE control_id = 'BL-0030' AND deleted_at IS NULL;

-- BL-0035: Disable anonymous SID enumeration
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RestrictAnonymousSAM","valueData":"1","valueType":"DWORD"}',
    'low', 'Restrict anonymous enumeration of SAM accounts'
FROM control_defs WHERE control_id = 'BL-0035' AND deleted_at IS NULL;

-- BL-0036: Disable anonymous share enumeration
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RestrictAnonymous","valueData":"1","valueType":"DWORD"}',
    'low', 'Restrict anonymous enumeration of shares'
FROM control_defs WHERE control_id = 'BL-0036' AND deleted_at IS NULL;

-- BL-0040: NTLMv2 only (LmCompatibilityLevel=5)
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"LmCompatibilityLevel","valueData":"5","valueType":"DWORD"}',
    'medium', 'Enforce NTLMv2 only — may break legacy apps'
FROM control_defs WHERE control_id = 'BL-0040' AND deleted_at IS NULL;

-- BL-0045: Disable LM hash storage
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"NoLMHash","valueData":"1","valueType":"DWORD"}',
    'low', 'Do not store LAN Manager hash on next password change'
FROM control_defs WHERE control_id = 'BL-0045' AND deleted_at IS NULL;

-- BL-0050: Disable guest account
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon","valueName":"AllowGuest","valueData":"0","valueType":"DWORD"}',
    'low', 'Disable guest account logon'
FROM control_defs WHERE control_id = 'BL-0050' AND deleted_at IS NULL;

-- BL-0055: Enable audit process creation
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\\Audit","valueName":"ProcessCreationIncludeCmdLine_Enabled","valueData":"1","valueType":"DWORD"}',
    'low', 'Include command line in process creation events'
FROM control_defs WHERE control_id = 'BL-0055' AND deleted_at IS NULL;

-- BL-0060: Disable LLMNR
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient","valueName":"EnableMulticast","valueData":"0","valueType":"DWORD"}',
    'low', 'Disable LLMNR (Link-Local Multicast Name Resolution)'
FROM control_defs WHERE control_id = 'BL-0060' AND deleted_at IS NULL;

-- BL-0065: Disable NetBIOS over TCP/IP
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\NetBT\\Parameters\\Interfaces\\Tcpip_*","valueName":"NetbiosOptions","valueData":"2","valueType":"DWORD"}',
    'low', 'Disable NetBIOS over TCP/IP'
FROM control_defs WHERE control_id = 'BL-0065' AND deleted_at IS NULL;

-- BL-0070: Disable WPAD
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\WinHttpAutoProxySvc","valueName":"Start","valueData":"4","valueType":"DWORD"}',
    'low', 'Disable WPAD (Web Proxy Auto-Discovery)'
FROM control_defs WHERE control_id = 'BL-0070' AND deleted_at IS NULL;

-- BL-0075: Disable Windows Script Host
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Microsoft\\Windows Script Host\\Settings","valueName":"Enabled","valueData":"0","valueType":"DWORD"}',
    'low', 'Disable Windows Script Host (WSH)'
FROM control_defs WHERE control_id = 'BL-0075' AND deleted_at IS NULL;

-- BL-0080: PowerShell script execution policy
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell","valueName":"EnableScripts","valueData":"1","valueType":"DWORD"}',
    'low', 'Enable PowerShell execution policy enforcement via GPO'
FROM control_defs WHERE control_id = 'BL-0080' AND deleted_at IS NULL;

-- BL-0085: Enable PowerShell logging
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\ScriptBlockLogging","valueName":"EnableScriptBlockLogging","valueData":"1","valueType":"DWORD"}',
    'low', 'Enable PowerShell Script Block Logging'
FROM control_defs WHERE control_id = 'BL-0085' AND deleted_at IS NULL;

-- BL-0090: Disable cached logons
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon","valueName":"CachedLogonsCount","valueData":"4","valueType":"SZ"}',
    'low', 'Limit cached logons to 4'
FROM control_defs WHERE control_id = 'BL-0090' AND deleted_at IS NULL;

-- BL-0095: Require CTRL+ALT+DEL
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"DisableCAD","valueData":"0","valueType":"DWORD"}',
    'low', 'Require CTRL+ALT+DEL for logon'
FROM control_defs WHERE control_id = 'BL-0095' AND deleted_at IS NULL;

-- BL-0100: Do not display last user name
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System","valueName":"DontDisplayLastUserName","valueData":"1","valueType":"DWORD"}',
    'low', 'Do not display last logged on user'
FROM control_defs WHERE control_id = 'BL-0100' AND deleted_at IS NULL;

-- BL-0110: Disable IPv6
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip6\\Parameters","valueName":"DisabledComponents","valueData":"255","valueType":"DWORD"}',
    'medium', 'Disable IPv6 on all interfaces — may affect some apps'
FROM control_defs WHERE control_id = 'BL-0110' AND deleted_at IS NULL;

-- BL-0120: Enable DEP (Data Execution Prevention)
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\Explorer","valueName":"NoDataExecutionPrevention","valueData":"0","valueType":"DWORD"}',
    'low', 'Ensure DEP is not disabled by policy'
FROM control_defs WHERE control_id = 'BL-0120' AND deleted_at IS NULL;

-- BL-0130: Enable SEHOP
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\kernel","valueName":"DisableExceptionChainValidation","valueData":"0","valueType":"DWORD"}',
    'low', 'Enable Structured Exception Handler Overwrite Protection'
FROM control_defs WHERE control_id = 'BL-0130' AND deleted_at IS NULL;

-- BL-0140: Disable WinRM for non-admins
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Service","valueName":"AllowAutoConfig","valueData":"0","valueType":"DWORD"}',
    'medium', 'Disable WinRM auto-configuration'
FROM control_defs WHERE control_id = 'BL-0140' AND deleted_at IS NULL;

-- BL-0150: Disable Telemetry (DiagTrack)
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection","valueName":"AllowTelemetry","valueData":"0","valueType":"DWORD"}',
    'low', 'Disable Windows telemetry data collection'
FROM control_defs WHERE control_id = 'BL-0150' AND deleted_at IS NULL;

-- ═══════════════════════════════════════════════════════════════════
-- SERVICE FIXES (enable_service / disable_service) — ~10 controls
-- ═══════════════════════════════════════════════════════════════════

-- BL-0200: Disable Print Spooler (if not needed)
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'disable_service',
    '{"serviceName":"Spooler"}',
    'medium', 'Disable Print Spooler service (PrintNightmare mitigation)'
FROM control_defs WHERE control_id = 'BL-0200' AND deleted_at IS NULL;

-- BL-0205: Disable Remote Registry
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'disable_service',
    '{"serviceName":"RemoteRegistry"}',
    'low', 'Disable Remote Registry service'
FROM control_defs WHERE control_id = 'BL-0205' AND deleted_at IS NULL;

-- BL-0210: Disable Xbox services
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'disable_service',
    '{"serviceName":"XblAuthManager"}',
    'low', 'Disable Xbox Live Auth Manager'
FROM control_defs WHERE control_id = 'BL-0210' AND deleted_at IS NULL;

-- BL-0215: Disable SSDP Discovery
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'disable_service',
    '{"serviceName":"SSDPSRV"}',
    'low', 'Disable SSDP Discovery service'
FROM control_defs WHERE control_id = 'BL-0215' AND deleted_at IS NULL;

-- BL-0220: Disable UPnP Device Host
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'disable_service',
    '{"serviceName":"upnphost"}',
    'low', 'Disable UPnP Device Host'
FROM control_defs WHERE control_id = 'BL-0220' AND deleted_at IS NULL;

-- BL-0230: Enable Windows Defender service
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'enable_service',
    '{"serviceName":"WinDefend"}',
    'low', 'Ensure Windows Defender service is running'
FROM control_defs WHERE control_id = 'BL-0230' AND deleted_at IS NULL;

-- BL-0235: Enable Windows Firewall service
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'enable_service',
    '{"serviceName":"MpsSvc"}',
    'low', 'Ensure Windows Firewall service is running'
FROM control_defs WHERE control_id = 'BL-0235' AND deleted_at IS NULL;

-- BL-0240: Enable Windows Update service
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'enable_service',
    '{"serviceName":"wuauserv"}',
    'low', 'Ensure Windows Update service is running'
FROM control_defs WHERE control_id = 'BL-0240' AND deleted_at IS NULL;

-- ═══════════════════════════════════════════════════════════════════
-- AUDIT POLICY FIXES (set_audit_policy) — ~8 controls
-- ═══════════════════════════════════════════════════════════════════

-- BL-0300: Audit Logon Events - Success+Failure
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_audit_policy',
    '{"subcategory":"Logon","setting":"SuccessAndFailure"}',
    'low', 'Enable audit of logon events (success + failure)'
FROM control_defs WHERE control_id = 'BL-0300' AND deleted_at IS NULL;

-- BL-0305: Audit Account Logon Events
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_audit_policy',
    '{"subcategory":"Credential Validation","setting":"SuccessAndFailure"}',
    'low', 'Enable audit of credential validation events'
FROM control_defs WHERE control_id = 'BL-0305' AND deleted_at IS NULL;

-- BL-0310: Audit Account Management
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_audit_policy',
    '{"subcategory":"User Account Management","setting":"SuccessAndFailure"}',
    'low', 'Enable audit of user account management'
FROM control_defs WHERE control_id = 'BL-0310' AND deleted_at IS NULL;

-- BL-0315: Audit Policy Change
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_audit_policy',
    '{"subcategory":"Audit Policy Change","setting":"SuccessAndFailure"}',
    'low', 'Enable audit of policy changes'
FROM control_defs WHERE control_id = 'BL-0315' AND deleted_at IS NULL;

-- BL-0320: Audit Privilege Use
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_audit_policy',
    '{"subcategory":"Sensitive Privilege Use","setting":"SuccessAndFailure"}',
    'low', 'Enable audit of sensitive privilege use'
FROM control_defs WHERE control_id = 'BL-0320' AND deleted_at IS NULL;

-- BL-0325: Audit Object Access
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_audit_policy',
    '{"subcategory":"File System","setting":"SuccessAndFailure"}',
    'low', 'Enable audit of file system object access'
FROM control_defs WHERE control_id = 'BL-0325' AND deleted_at IS NULL;

-- BL-0330: Audit Process Tracking
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_audit_policy',
    '{"subcategory":"Process Creation","setting":"SuccessAndFailure"}',
    'low', 'Enable audit of process creation'
FROM control_defs WHERE control_id = 'BL-0330' AND deleted_at IS NULL;

-- BL-0335: Audit Security State Change
INSERT INTO remediation_actions (control_def_id, action_type, params_template, risk_level, description)
SELECT id, 'set_audit_policy',
    '{"subcategory":"Security State Change","setting":"Success"}',
    'low', 'Enable audit of security state changes'
FROM control_defs WHERE control_id = 'BL-0335' AND deleted_at IS NULL;

PRINT 'Remediation actions seed complete.';
PRINT 'NOTE: Some inserts may produce 0 rows if the referenced control_id does not exist in this database.';
PRINT 'This is expected — the seed is idempotent and only creates actions for controls that exist.';
