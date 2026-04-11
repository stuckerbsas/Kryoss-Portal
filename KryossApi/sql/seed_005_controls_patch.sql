SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- ============================================================
-- seed_005_controls_patch.sql
-- Kryoss Platform -- Control catalog patch
--
-- Two goals:
--   1) Fix framework tags so CIS / NIST show their real coverage.
--      The original PS scripts left 30 scored controls without the CIS
--      tag and 27 controls without the NIST tag, even though the
--      underlying checks clearly map to both. This is pure metadata.
--
--   2) Add 25 new control_defs (BL-0445..BL-0469) covering the three
--      HIPAA refinements that were missing from the collector:
--        a) MFA / Windows Hello for Business / Smart Card
--        b) Event log retention (Security/System/Application)
--        c) Backup posture (Windows Backup / VSS / 3rd-party agents)
--      No new engines required -- everything reuses existing
--      registry / service / command engines in the agent.
--
-- Run AFTER seed_004_controls.sql. Fully idempotent.
-- ============================================================

DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @fwCIS   INT = (SELECT id FROM frameworks WHERE code='CIS');
DECLARE @fwNIST  INT = (SELECT id FROM frameworks WHERE code='NIST');
DECLARE @fwHIPAA INT = (SELECT id FROM frameworks WHERE code='HIPAA');
DECLARE @fwPCI   INT = (SELECT id FROM frameworks WHERE code='PCI-DSS');
DECLARE @fwISO   INT = (SELECT id FROM frameworks WHERE code='ISO27001');

-- ============================================================
-- PART 1 -- CIS tag patch (30 controls)
-- ============================================================
-- All of these implement CIS Benchmark requirements but were
-- mis-tagged in the source catalog.
-- ============================================================
DECLARE @cisPatch TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @cisPatch(control_id) VALUES
    ('SC-004'),('SC-036'),('SC-037'),('SC-038'),('SC-039'),('SC-040'),
    ('SC-061'),('SC-083'),('SC-084'),('SC-086'),('SC-091'),('SC-092'),
    ('SC-093'),('SC-094'),('SC-096'),('SC-098'),('SC-099'),('SC-101'),
    ('SC-106'),('SC-107'),('SC-108'),('SC-109'),('SC-110'),('SC-113'),
    ('SC-114'),('SC-116'),('SC-117'),('SC-121'),('SC-122'),('SC-123');

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS
FROM control_defs cd
JOIN @cisPatch p ON p.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwCIS
);

-- ============================================================
-- PART 2 -- NIST tag patch (27 controls)
-- ============================================================
DECLARE @nistPatch TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @nistPatch(control_id) VALUES
    ('SC-056'),('SC-065'),('SC-066'),('SC-067'),('SC-068'),('SC-069'),
    ('SC-070'),('SC-071'),('SC-073'),('SC-074'),('SC-076'),('SC-077'),
    ('SC-080'),('SC-081'),('SC-087'),('SC-088'),('SC-089'),('SC-090'),
    ('SC-096'),('SC-102'),('SC-109'),('SC-149'),
    ('BL-0272'),('BL-0273'),('BL-0274'),('BL-0275'),('BL-0276');

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST
FROM control_defs cd
JOIN @nistPatch p ON p.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwNIST
);

-- ============================================================
-- PART 3 -- New categories (idempotent)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Credential Protection')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Credential Protection', 200, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Audit, Logging And Monitoring')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Audit, Logging And Monitoring', 201, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Backup And Recovery')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Backup And Recovery', 202, @systemUserId);

-- ============================================================
-- PART 4 -- New control_defs (BL-0445..BL-0469)
-- HIPAA collector refinements
-- ============================================================
-- All inserts guarded by NOT EXISTS on control_id (idempotent).
-- check_json embeds what the agent needs to read and a "display"
-- string shown to the technician during execution.
-- ============================================================

-- ---------- 4.a MFA / Windows Hello for Business / Smart Card ----------

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0445')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0445',
    (SELECT id FROM control_categories WHERE name=N'Credential Protection'),
    N'Windows Hello for Business - Policy Enabled',
    'registry', 'high',
    N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\PassportForWork","value_name":"Enabled","expected":1,"operator":"eq","display":"Reading Windows Hello for Business policy","parent":"Test-WHfBPolicy"}',
    N'Enable Windows Hello for Business via Intune or GPO: Computer Config > Admin Templates > Windows Components > Windows Hello for Business.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0446')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0446',
    (SELECT id FROM control_categories WHERE name=N'Credential Protection'),
    N'Windows Hello for Business - Require TPM',
    'registry', 'high',
    N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\PassportForWork","value_name":"RequireSecurityDevice","expected":1,"operator":"eq","display":"Reading WHfB TPM requirement","parent":"Test-WHfBPolicy"}',
    N'Require TPM for Windows Hello for Business keys. Prevents software-only credential storage.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0447')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0447',
    (SELECT id FROM control_categories WHERE name=N'Credential Protection'),
    N'Windows Hello for Business - Minimum PIN Length',
    'registry', 'medium',
    N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\PassportForWork\\PINComplexity","value_name":"MinimumPINLength","expected":6,"operator":"gte","display":"Reading WHfB minimum PIN length","parent":"Test-WHfBPolicy"}',
    N'Set minimum WHfB PIN length to at least 6 digits.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0448')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0448',
    (SELECT id FROM control_categories WHERE name=N'Credential Protection'),
    N'Smart Card Service - Start Type',
    'service', 'low',
    N'{"check_type":"service","service_name":"SCardSvr","expected_start_type":"Manual","operator":"in","expected":["Manual","Automatic"],"display":"Reading smart card service","parent":"Test-SmartCardService"}',
    N'Smart Card service should be Manual or Automatic when smart card auth is in use.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0449')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0449',
    (SELECT id FROM control_categories WHERE name=N'Credential Protection'),
    N'Device Join Status (dsregcmd)',
    'command', 'medium',
    N'{"check_type":"command","executable":"dsregcmd.exe","arguments":"/status","timeout_seconds":30,"display":"Reading Azure AD / domain join status","parent":"Test-DeviceJoinStatus"}',
    N'Agent captures full dsregcmd output; portal parses AzureAdJoined, DomainJoined, WamDefaultSet, NgcSet, etc.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0450')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0450',
    (SELECT id FROM control_categories WHERE name=N'Credential Protection'),
    N'NGC Container Provisioned (WHfB credential exists)',
    'command', 'medium',
    N'{"check_type":"command","executable":"cmd.exe","arguments":"/c dir /b /a \"C:\\Windows\\ServiceProfiles\\LocalService\\AppData\\Local\\Microsoft\\Ngc\" 2>nul","timeout_seconds":10,"display":"Checking NGC container for WHfB credentials","parent":"Test-WHfBProvisioned"}',
    N'Non-empty NGC folder means at least one user has provisioned WHfB credentials on this device.',
    1, 1, @systemUserId);

-- ---------- 4.b Event log retention ----------

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0451')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0451',
    (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'),
    N'Security Event Log - Max Size (bytes)',
    'registry', 'high',
    N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\EventLog\\Security","value_name":"MaxSize","expected":201326592,"operator":"gte","display":"Reading Security log max size","parent":"Test-EventLogRetention"}',
    N'Set Security log max size to at least 192 MB (201326592 bytes). HIPAA/PCI require sufficient retention of audit events.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0452')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0452',
    (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'),
    N'Security Event Log - Retention Mode',
    'registry', 'high',
    N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\EventLog\\Security","value_name":"Retention","expected":0,"operator":"eq","display":"Reading Security log retention mode","parent":"Test-EventLogRetention"}',
    N'Retention=0 means overwrite as needed. Combined with large MaxSize or AutoBackupLogFiles=1 this prevents log loss.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0453')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0453',
    (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'),
    N'Security Event Log - Auto Backup When Full',
    'registry', 'medium',
    N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\EventLog\\Security","value_name":"AutoBackupLogFiles","expected":1,"operator":"eq","display":"Reading Security log auto-backup","parent":"Test-EventLogRetention"}',
    N'Enable auto-backup so full logs are archived instead of lost. Required for long-term HIPAA retention.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0454')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0454',
    (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'),
    N'System Event Log - Max Size (bytes)',
    'registry', 'medium',
    N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\EventLog\\System","value_name":"MaxSize","expected":33554432,"operator":"gte","display":"Reading System log max size","parent":"Test-EventLogRetention"}',
    N'Set System log max size to at least 32 MB (33554432 bytes).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0455')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0455',
    (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'),
    N'System Event Log - Retention Mode',
    'registry', 'medium',
    N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\EventLog\\System","value_name":"Retention","expected":0,"operator":"eq","display":"Reading System log retention mode","parent":"Test-EventLogRetention"}',
    N'Retention=0 (overwrite as needed) is the CIS-recommended default.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0456')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0456',
    (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'),
    N'Application Event Log - Max Size (bytes)',
    'registry', 'medium',
    N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\EventLog\\Application","value_name":"MaxSize","expected":33554432,"operator":"gte","display":"Reading Application log max size","parent":"Test-EventLogRetention"}',
    N'Set Application log max size to at least 32 MB.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0457')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0457',
    (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'),
    N'Application Event Log - Retention Mode',
    'registry', 'medium',
    N'{"check_type":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\EventLog\\Application","value_name":"Retention","expected":0,"operator":"eq","display":"Reading Application log retention mode","parent":"Test-EventLogRetention"}',
    N'Retention=0 (overwrite as needed).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0458')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0458',
    (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'),
    N'Effective Config - Security Log (wevtutil gl)',
    'command', 'medium',
    N'{"check_type":"command","executable":"wevtutil.exe","arguments":"gl Security","timeout_seconds":15,"display":"Reading effective Security log config","parent":"Test-EventLogRetention"}',
    N'Agent captures effective log config (includes SDDL permissions). Portal parses retention, autoBackup, maxSize.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0459')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0459',
    (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring'),
    N'Effective Config - System Log (wevtutil gl)',
    'command', 'low',
    N'{"check_type":"command","executable":"wevtutil.exe","arguments":"gl System","timeout_seconds":15,"display":"Reading effective System log config","parent":"Test-EventLogRetention"}',
    N'Same as BL-0458 but for System log.',
    1, 1, @systemUserId);

-- ---------- 4.c Backup posture ----------

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0460')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0460',
    (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'),
    N'Windows Server Backup - Last Versions',
    'command', 'high',
    N'{"check_type":"command","executable":"wbadmin.exe","arguments":"get versions","timeout_seconds":30,"display":"Reading Windows Backup history","parent":"Test-BackupPosture"}',
    N'Agent captures wbadmin output. Portal parses last successful backup date and evaluates freshness (HIPAA 164.308(a)(7)).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0461')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0461',
    (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'),
    N'VSS Shadow Copies - List',
    'command', 'medium',
    N'{"check_type":"command","executable":"vssadmin.exe","arguments":"list shadows","timeout_seconds":30,"display":"Listing VSS shadow copies","parent":"Test-BackupPosture"}',
    N'Confirms shadow copies exist. Portal parses creation time of most recent shadow.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0462')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0462',
    (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'),
    N'VSS Writers - Health',
    'command', 'medium',
    N'{"check_type":"command","executable":"vssadmin.exe","arguments":"list writers","timeout_seconds":30,"display":"Reading VSS writers status","parent":"Test-BackupPosture"}',
    N'All VSS writers should report Stable / No error. Failed writers break backups silently.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0463')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0463',
    (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'),
    N'Veeam Endpoint Backup - Service',
    'service', 'low',
    N'{"check_type":"service","service_name":"VeeamEndpointBackupSvc","display":"Checking Veeam Endpoint Backup service","parent":"Test-BackupPosture","optional":true}',
    N'Detection-only: reports presence and state of Veeam Agent service if installed.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0464')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0464',
    (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'),
    N'Datto / Continuity Agent - Service',
    'service', 'low',
    N'{"check_type":"service","service_name":"CCSDK","display":"Checking Datto continuity service","parent":"Test-BackupPosture","optional":true}',
    N'Detection-only: Datto SIRIS / Continuity agent service.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0465')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0465',
    (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'),
    N'Acronis Managed Machine Service',
    'service', 'low',
    N'{"check_type":"service","service_name":"acronis_mms","display":"Checking Acronis Managed Machine Service","parent":"Test-BackupPosture","optional":true}',
    N'Detection-only: Acronis Cyber Protect agent.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0466')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0466',
    (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'),
    N'Carbonite Backup - Service',
    'service', 'low',
    N'{"check_type":"service","service_name":"CarboniteService","display":"Checking Carbonite backup service","parent":"Test-BackupPosture","optional":true}',
    N'Detection-only: Carbonite cloud backup agent.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0467')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0467',
    (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'),
    N'Veritas Backup Exec RPC - Service',
    'service', 'low',
    N'{"check_type":"service","service_name":"BackupExecRPCService","display":"Checking Backup Exec service","parent":"Test-BackupPosture","optional":true}',
    N'Detection-only: Veritas Backup Exec agent.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0468')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0468',
    (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'),
    N'MozyPro Backup - Service',
    'service', 'low',
    N'{"check_type":"service","service_name":"MozyProBackup","display":"Checking MozyPro backup service","parent":"Test-BackupPosture","optional":true}',
    N'Detection-only: MozyPro cloud backup agent.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0469')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0469',
    (SELECT id FROM control_categories WHERE name=N'Backup And Recovery'),
    N'Windows Backup (Modern) - Valid Config',
    'registry', 'medium',
    N'{"check_type":"registry","hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\WindowsBackup","value_name":"ValidConfig","expected":1,"operator":"eq","display":"Reading Windows Backup config state","parent":"Test-BackupPosture"}',
    N'Indicates Windows Backup has a configured schedule and target.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 5 -- Framework mappings for new controls
-- ============================================================
-- MFA / WHfB block: HIPAA, NIST, CIS, ISO27001
-- Event log block:  HIPAA, NIST, CIS, PCI-DSS, ISO27001
-- Backup block:     HIPAA, NIST, ISO27001
-- ============================================================

DECLARE @mfaBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @mfaBlock VALUES
    ('BL-0445'),('BL-0446'),('BL-0447'),('BL-0448'),('BL-0449'),('BL-0450');

DECLARE @logBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @logBlock VALUES
    ('BL-0451'),('BL-0452'),('BL-0453'),('BL-0454'),('BL-0455'),
    ('BL-0456'),('BL-0457'),('BL-0458'),('BL-0459');

DECLARE @bkpBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @bkpBlock VALUES
    ('BL-0460'),('BL-0461'),('BL-0462'),('BL-0463'),('BL-0464'),
    ('BL-0465'),('BL-0466'),('BL-0467'),('BL-0468'),('BL-0469');

-- Helper: insert a (control, framework) pair only if missing.
-- Done per framework per block using set-based INSERTs.

-- MFA block -> HIPAA, NIST, CIS, ISO
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA FROM control_defs cd JOIN @mfaBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwHIPAA);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST FROM control_defs cd JOIN @mfaBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwNIST);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS FROM control_defs cd JOIN @mfaBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwCIS);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO FROM control_defs cd JOIN @mfaBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwISO);

-- Event log block -> HIPAA, NIST, CIS, PCI, ISO
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA FROM control_defs cd JOIN @logBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwHIPAA);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST FROM control_defs cd JOIN @logBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwNIST);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS FROM control_defs cd JOIN @logBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwCIS);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwPCI FROM control_defs cd JOIN @logBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwPCI);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO FROM control_defs cd JOIN @logBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwISO);

-- Backup block -> HIPAA, NIST, ISO
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA FROM control_defs cd JOIN @bkpBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwHIPAA);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST FROM control_defs cd JOIN @bkpBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwNIST);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO FROM control_defs cd JOIN @bkpBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwISO);

GO

-- ============================================================
-- Verification queries (run manually after applying)
-- ============================================================
-- SELECT COUNT(*) AS total_controls FROM control_defs;          -- expect 630 (605 + 25)
-- SELECT f.code, COUNT(*) AS tagged
--   FROM control_frameworks cf JOIN frameworks f ON cf.framework_id=f.id
--   GROUP BY f.code ORDER BY tagged DESC;
-- SELECT control_id, name FROM control_defs WHERE control_id LIKE 'BL-04[4-6]%' ORDER BY control_id;
