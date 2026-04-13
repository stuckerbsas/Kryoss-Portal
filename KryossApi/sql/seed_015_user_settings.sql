SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_015_user_settings.sql
-- Kryoss Platform -- Per-User HKU Security Settings
--
-- Adds 15 controls (USR-001 through USR-015) that check
-- settings under HKU\<SID>\ for each logged-in user profile.
-- The RegistryEngine already supports HKU enumeration via
-- "hive": "HKU" in check_json.
--
-- Platform scope: W10, W11 only (user-level settings on
-- workstations). NOT linked to servers or DCs.
--
-- Framework scope: ALL 5 active frameworks.
-- Assessment scope: ALL active assessments.
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

-- Platform IDs (workstation only)
DECLARE @platW10  INT = (SELECT id FROM platforms WHERE code='W10');
DECLARE @platW11  INT = (SELECT id FROM platforms WHERE code='W11');

IF @platW10 IS NULL OR @platW11 IS NULL
BEGIN
    RAISERROR('W10 or W11 platform missing. Run seed_002 first.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ============================================================
-- PART 0 -- New category for User-Level Security
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'User Profile Security')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'User Profile Security', 210, @systemUserId);

DECLARE @catUserProf INT = (SELECT id FROM control_categories WHERE name=N'User Profile Security');

-- ============================================================
-- SECTION A: Screen Saver / Lock Screen (USR-001..USR-003)
-- ============================================================

-- USR-001: Screen saver enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-001')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-001', @catUserProf,
    N'Screen Saver Enabled (Per-User)',
    'registry', 'medium',
    N'{"hive":"HKU","path":"Control Panel\\Desktop","valueName":"ScreenSaveActive","expected":"1","operator":"eq","display":"Checking screen saver enabled per user profile"}',
    N'GPO: User Configuration > Administrative Templates > Control Panel > Personalization > "Enable screen saver" = Enabled. Ensures screen saver activates to trigger the lock screen after inactivity.',
    1, 1, @systemUserId);

-- USR-002: Screen saver password protected
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-002')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-002', @catUserProf,
    N'Screen Saver Password Protected (Per-User)',
    'registry', 'high',
    N'{"hive":"HKU","path":"Control Panel\\Desktop","valueName":"ScreenSaverIsSecure","expected":"1","operator":"eq","display":"Checking screen saver password protection per user"}',
    N'GPO: User Configuration > Administrative Templates > Control Panel > Personalization > "Password protect the screen saver" = Enabled. Requires password to unlock after screen saver activates. Critical for HIPAA/PCI physical access control.',
    1, 1, @systemUserId);

-- USR-003: Screen saver timeout <= 900 seconds (15 min)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-003')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-003', @catUserProf,
    N'Screen Saver Timeout <= 15 Minutes (Per-User)',
    'registry', 'high',
    N'{"hive":"HKU","path":"Control Panel\\Desktop","valueName":"ScreenSaveTimeOut","expected":"900","operator":"lte","display":"Checking screen saver timeout per user profile"}',
    N'GPO: User Configuration > Administrative Templates > Control Panel > Personalization > "Screen saver timeout" <= 900 seconds (15 minutes). HIPAA requires workstation auto-lock. NIST recommends <= 15 minutes.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION B: Explorer Security Settings (USR-004..USR-007)
-- ============================================================

-- USR-004: Show file extensions
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-004')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-004', @catUserProf,
    N'Explorer: Show File Extensions (Per-User)',
    'registry', 'medium',
    N'{"hive":"HKU","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","valueName":"HideFileExt","expected":0,"operator":"eq","display":"Checking file extension visibility per user"}',
    N'Set HideFileExt = 0 in each user profile. Showing file extensions prevents social engineering attacks where malicious files masquerade as documents (e.g., invoice.pdf.exe).',
    1, 1, @systemUserId);

-- USR-005: Show hidden files
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-005')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-005', @catUserProf,
    N'Explorer: Show Hidden Files (Per-User)',
    'registry', 'low',
    N'{"hive":"HKU","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","valueName":"Hidden","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking hidden file visibility per user"}',
    N'Set Hidden = 1 to show hidden files and folders. Helps administrators detect hidden malware or unauthorized hidden files. Value 1 = Show, Value 2 = Hide.',
    1, 1, @systemUserId);

-- USR-006: Show protected OS files disabled (for non-admin)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-006')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-006', @catUserProf,
    N'Explorer: Protected OS Files Hidden (Per-User)',
    'registry', 'low',
    N'{"hive":"HKU","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced","valueName":"ShowSuperHidden","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking protected OS file visibility per user"}',
    N'Set ShowSuperHidden = 0 to hide protected operating system files. Standard users should not see system files to prevent accidental deletion or modification.',
    1, 1, @systemUserId);

-- USR-007: Run dialog disabled for standard users
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-007')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-007', @catUserProf,
    N'Explorer: Run Dialog Disabled (Per-User)',
    'registry', 'medium',
    N'{"hive":"HKU","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer","valueName":"NoRun","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking Run dialog availability per user"}',
    N'GPO: User Configuration > Administrative Templates > Start Menu and Taskbar > "Remove Run menu from Start Menu" = Enabled. Prevents standard users from executing arbitrary commands via Win+R.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION C: Script & Execution Restrictions (USR-008..USR-010)
-- ============================================================

-- USR-008: Windows Script Host disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-008')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-008', @catUserProf,
    N'Windows Script Host Disabled (Per-User)',
    'registry', 'high',
    N'{"hive":"HKU","path":"SOFTWARE\\Microsoft\\Windows Script Host\\Settings","valueName":"Enabled","expected":0,"operator":"eq","missingBehavior":"warn","display":"Checking Windows Script Host status per user"}',
    N'Set HKCU\\SOFTWARE\\Microsoft\\Windows Script Host\\Settings\\Enabled = 0. Disables WSH (wscript.exe, cscript.exe) which is heavily abused by malware for VBS/JS execution. Most users do not need WSH.',
    1, 1, @systemUserId);

-- USR-009: PowerShell execution policy per-user
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-009')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-009', @catUserProf,
    N'PowerShell Execution Policy: RemoteSigned or Restricted (Per-User)',
    'registry', 'medium',
    N'{"hive":"HKU","path":"SOFTWARE\\Microsoft\\PowerShell\\1\\ShellIds\\Microsoft.PowerShell","valueName":"ExecutionPolicy","expected":"RemoteSigned","operator":"eq","missingBehavior":"pass","display":"Checking PowerShell execution policy per user"}',
    N'Set per-user PowerShell execution policy to RemoteSigned or more restrictive. Prevents execution of unsigned downloaded scripts. Machine policy (HKLM) takes precedence if set via GPO.',
    1, 1, @systemUserId);

-- USR-010: Attachment manager high-risk file types
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-010')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-010', @catUserProf,
    N'Attachment Manager: Do Not Preserve Zone Info Disabled',
    'registry', 'high',
    N'{"hive":"HKU","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Attachments","valueName":"SaveZoneInformation","expected":2,"operator":"eq","missingBehavior":"pass","display":"Checking attachment zone info preservation per user"}',
    N'GPO: User Configuration > Administrative Templates > Windows Components > Attachment Manager > "Do not preserve zone information in file attachments" = Disabled (value 2). Preserves Mark of the Web on downloaded files so SmartScreen and other protections can evaluate them.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION D: Lock Screen & Privacy (USR-011..USR-015)
-- ============================================================

-- USR-011: Lock screen notifications disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-011')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-011', @catUserProf,
    N'Toast Notifications on Lock Screen Disabled (Per-User)',
    'registry', 'medium',
    N'{"hive":"HKU","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\CurrentVersion\\PushNotifications","valueName":"NoToastApplicationNotificationOnLockScreen","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking lock screen toast notification status per user"}',
    N'GPO: User Configuration > Administrative Templates > Start Menu and Taskbar > Notifications > "Turn off toast notifications on the lock screen" = Enabled. Prevents sensitive information from being displayed on the lock screen where it could be seen by unauthorized persons.',
    1, 1, @systemUserId);

-- USR-012: Camera privacy (app access)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-012')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-012', @catUserProf,
    N'App Camera Access Controlled (Per-User)',
    'registry', 'medium',
    N'{"hive":"HKU","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\CapabilityAccessManager\\ConsentStore\\webcam","valueName":"Value","expected":"Deny","operator":"eq","missingBehavior":"warn","display":"Checking per-user camera access policy"}',
    N'Set webcam ConsentStore Value = Deny to block app access to camera by default. Users can grant access per-app as needed. Protects against unauthorized camera access by malware or rogue apps.',
    1, 1, @systemUserId);

-- USR-013: Microphone privacy (app access)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-013')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-013', @catUserProf,
    N'App Microphone Access Controlled (Per-User)',
    'registry', 'medium',
    N'{"hive":"HKU","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\CapabilityAccessManager\\ConsentStore\\microphone","valueName":"Value","expected":"Deny","operator":"eq","missingBehavior":"warn","display":"Checking per-user microphone access policy"}',
    N'Set microphone ConsentStore Value = Deny to block app access to microphone by default. Protects against unauthorized audio recording by malware or rogue apps.',
    1, 1, @systemUserId);

-- USR-014: Location privacy (app access)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-014')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-014', @catUserProf,
    N'App Location Access Controlled (Per-User)',
    'registry', 'low',
    N'{"hive":"HKU","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\CapabilityAccessManager\\ConsentStore\\location","valueName":"Value","expected":"Deny","operator":"eq","missingBehavior":"warn","display":"Checking per-user location access policy"}',
    N'Set location ConsentStore Value = Deny to block app access to location services by default. Reduces location data exposure. Users can enable per-app as needed for legitimate use cases.',
    1, 1, @systemUserId);

-- USR-015: OneDrive auto-sign-in disabled (if not managed)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='USR-015')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('USR-015', @catUserProf,
    N'OneDrive: Silent Auto Sign-In Disabled (Per-User)',
    'registry', 'low',
    N'{"hive":"HKU","path":"SOFTWARE\\Microsoft\\OneDrive","valueName":"SilentAccountConfig","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking OneDrive silent auto sign-in per user"}',
    N'Set SilentAccountConfig = 0 to prevent OneDrive from automatically signing in with Windows credentials. In unmanaged environments, this prevents uncontrolled cloud sync of potentially sensitive data.',
    1, 1, @systemUserId);

-- ============================================================
-- PART P -- Platform linkage (W10, W11 ONLY)
-- ============================================================

DECLARE @usrBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @usrBlock VALUES
    ('USR-001'),('USR-002'),('USR-003'),('USR-004'),('USR-005'),('USR-006'),('USR-007'),('USR-008'),('USR-009'),('USR-010'),
    ('USR-011'),('USR-012'),('USR-013'),('USR-014'),('USR-015');

-- Link to W10
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platW10
FROM control_defs cd
JOIN @usrBlock b ON b.control_id = cd.control_id
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
JOIN @usrBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platW11
);
DECLARE @w11Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for W11: ', @w11Rows);

-- ============================================================
-- PART Q -- Framework mappings (ALL 5 frameworks)
-- ============================================================

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST
FROM control_defs cd
JOIN @usrBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwNIST
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS
FROM control_defs cd
JOIN @usrBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwCIS
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA
FROM control_defs cd
JOIN @usrBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwHIPAA
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO
FROM control_defs cd
JOIN @usrBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwISO
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwPCI
FROM control_defs cd
JOIN @usrBlock b ON b.control_id = cd.control_id
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
JOIN @usrBlock b ON b.control_id = cd.control_id
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

-- Count new user-level controls
SELECT COUNT(*) AS new_usr_controls
FROM control_defs
WHERE control_id LIKE 'USR-%' AND is_active = 1;
-- Expected: 15

-- Breakdown by engine type
SELECT cd.[type], COUNT(*) AS control_count
FROM control_defs cd
WHERE cd.control_id LIKE 'USR-%' AND cd.is_active = 1
GROUP BY cd.[type]
ORDER BY cd.[type];
-- Expected: registry 15

-- Breakdown by severity
SELECT cd.severity, COUNT(*) AS control_count
FROM control_defs cd
WHERE cd.control_id LIKE 'USR-%' AND cd.is_active = 1
GROUP BY cd.severity
ORDER BY cd.severity;
-- Expected: high ~4, medium ~7, low ~4

-- Verify platform linkage (workstation only)
SELECT p.code, COUNT(cp.control_def_id) AS usr_controls_linked
FROM platforms p
LEFT JOIN control_platforms cp ON cp.platform_id = p.id
LEFT JOIN control_defs cd ON cd.id = cp.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'USR-%'
WHERE p.code IN ('W10','W11')
GROUP BY p.code
ORDER BY p.code;
-- Expected: W10=15, W11=15

-- Verify NOT linked to servers or DCs
SELECT p.code, COUNT(cp.control_def_id) AS usr_controls_linked
FROM platforms p
LEFT JOIN control_platforms cp ON cp.platform_id = p.id
LEFT JOIN control_defs cd ON cd.id = cp.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'USR-%'
WHERE p.code IN ('MS19','MS22','MS25','DC19','DC22','DC25')
GROUP BY p.code
ORDER BY p.code;
-- Expected: all 0

-- Verify framework linkage
SELECT f.code, COUNT(cf.control_def_id) AS usr_controls_linked
FROM frameworks f
LEFT JOIN control_frameworks cf ON cf.framework_id = f.id
LEFT JOIN control_defs cd ON cd.id = cf.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'USR-%'
WHERE f.is_active = 1
GROUP BY f.code
ORDER BY f.code;
-- Expected: CIS=15, HIPAA=15, ISO27001=15, NIST=15, PCI-DSS=15

-- Verify HKU hive usage
SELECT cd.control_id, cd.name,
    JSON_VALUE(cd.check_json, '$.hive') AS hive
FROM control_defs cd
WHERE cd.control_id LIKE 'USR-%' AND cd.is_active = 1
ORDER BY cd.control_id;
-- Expected: all rows show hive = HKU

-- Verify assessment linkage
SELECT a.name, COUNT(ac.control_def_id) AS usr_controls_linked
FROM assessments a
JOIN assessment_controls ac ON ac.assessment_id = a.id
JOIN control_defs cd ON cd.id = ac.control_def_id
WHERE cd.control_id LIKE 'USR-%' AND cd.is_active = 1
  AND a.is_active = 1 AND a.deleted_at IS NULL
GROUP BY a.name;
-- Expected: each active assessment has 15 USR controls linked

-- ============================================================
-- Grand total after all 3 seed files (013 + 014 + 015)
-- ============================================================
SELECT
    COUNT(*) AS total_active,
    SUM(CASE WHEN control_id LIKE 'USR-%' THEN 1 ELSE 0 END) AS user_settings,
    SUM(CASE WHEN control_id LIKE 'EDGE-%' THEN 1 ELSE 0 END) AS edge_browser,
    SUM(CASE WHEN control_id LIKE 'DC-%' THEN 1 ELSE 0 END) AS dc_controls,
    SUM(CASE WHEN control_id LIKE 'BLG-%' THEN 1 ELSE 0 END) AS baseline_gap,
    SUM(CASE WHEN control_id LIKE 'SRV-%' THEN 1 ELSE 0 END) AS server_specific,
    SUM(CASE WHEN control_id LIKE 'AV-%' THEN 1 ELSE 0 END) AS antivirus,
    SUM(CASE WHEN control_id LIKE 'SC-%' THEN 1 ELSE 0 END) AS scored,
    SUM(CASE WHEN control_id LIKE 'BL-%' THEN 1 ELSE 0 END) AS baseline_registry
FROM control_defs
WHERE is_active = 1;
-- Expected: total_active = ~908 (823 + 40 + 30 + 15)
