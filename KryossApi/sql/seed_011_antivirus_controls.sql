SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_011_antivirus_controls.sql
-- Kryoss Platform -- Antivirus & Endpoint Protection Controls
--
-- Adds 40 controls (AV-001..AV-040) covering:
--   - Windows Defender status & real-time protection
--   - Defender definitions & scan age
--   - Attack Surface Reduction (ASR) rules
--   - Network & exploit protection
--   - Defender exclusion auditing
--   - Third-party AV & EDR detection
--
-- Linked to ALL 5 platforms: W10, W11, MS19, MS22, MS25
-- Linked to ALL 5 active frameworks: NIST, CIS, HIPAA, ISO27001, PCI-DSS
--
-- Run AFTER seed_010. Fully idempotent (NOT EXISTS guards).
-- ============================================================

BEGIN TRANSACTION;

DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Framework IDs
DECLARE @fwCIS   INT = (SELECT id FROM frameworks WHERE code='CIS');
DECLARE @fwNIST  INT = (SELECT id FROM frameworks WHERE code='NIST');
DECLARE @fwHIPAA INT = (SELECT id FROM frameworks WHERE code='HIPAA');
DECLARE @fwPCI   INT = (SELECT id FROM frameworks WHERE code='PCI-DSS');
DECLARE @fwISO   INT = (SELECT id FROM frameworks WHERE code='ISO27001');

-- Platform IDs (all 5)
DECLARE @platW10  INT = (SELECT id FROM platforms WHERE code='W10');
DECLARE @platW11  INT = (SELECT id FROM platforms WHERE code='W11');
DECLARE @platMS19 INT = (SELECT id FROM platforms WHERE code='MS19');
DECLARE @platMS22 INT = (SELECT id FROM platforms WHERE code='MS22');
DECLARE @platMS25 INT = (SELECT id FROM platforms WHERE code='MS25');

IF @platW10 IS NULL OR @platW11 IS NULL OR @platMS19 IS NULL OR @platMS22 IS NULL OR @platMS25 IS NULL
BEGIN
    RAISERROR('One or more platforms missing (W10/W11/MS19/MS22/MS25). Run seed_002 first.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ============================================================
-- PART 0 -- New categories
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Attack Surface Reduction')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Attack Surface Reduction', 311, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Exploit Protection')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Exploit Protection', 312, @systemUserId);

-- Category lookups
DECLARE @catDefender INT = (SELECT id FROM control_categories WHERE name=N'Windows Defender');
DECLARE @catASR      INT = (SELECT id FROM control_categories WHERE name=N'Attack Surface Reduction');
DECLARE @catExploit  INT = (SELECT id FROM control_categories WHERE name=N'Exploit Protection');
DECLARE @catEPP      INT = (SELECT id FROM control_categories WHERE name=N'Endpoint Protection And Patching');

-- ============================================================
-- PART 1 -- Windows Defender Status (AV-001..AV-010)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-001')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-001', @catDefender,
    N'Windows Defender Antivirus Enabled',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender","valueName":"DisableAntiSpyware","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking if Windows Defender is enabled"}',
    N'Ensure DisableAntiSpyware is 0 or not set. GPO: Computer Configuration > Administrative Templates > Windows Components > Microsoft Defender Antivirus > "Turn off Microsoft Defender Antivirus" = Not Configured or Disabled.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-002')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-002', @catDefender,
    N'Windows Defender Real-Time Protection Enabled',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Real-Time Protection","valueName":"DisableRealtimeMonitoring","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking real-time protection status"}',
    N'Ensure DisableRealtimeMonitoring is 0 or not set. GPO: Microsoft Defender Antivirus > Real-time Protection > "Turn off real-time protection" = Not Configured or Disabled. Or PowerShell: Set-MpPreference -DisableRealtimeMonitoring $false.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-003')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-003', @catDefender,
    N'Windows Defender Behavior Monitoring Enabled',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Real-Time Protection","valueName":"DisableBehaviorMonitoring","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking behavior monitoring status"}',
    N'Ensure DisableBehaviorMonitoring is 0 or not set. GPO: Microsoft Defender Antivirus > Real-time Protection > "Turn off behavior monitoring" = Not Configured or Disabled. Behavior monitoring detects suspicious process activities.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-004')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-004', @catDefender,
    N'Windows Defender IOAV Protection Enabled (Scan Downloaded Files)',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Real-Time Protection","valueName":"DisableIOAVProtection","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking IOAV protection (download scanning) status"}',
    N'Ensure DisableIOAVProtection is 0 or not set. GPO: Microsoft Defender Antivirus > Real-time Protection > "Scan all downloaded files and attachments" = Enabled. IOAV scans files downloaded from the internet and email attachments.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-005')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-005', @catDefender,
    N'Windows Defender On-Access Protection Enabled',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Real-Time Protection","valueName":"DisableOnAccessProtection","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking on-access protection status"}',
    N'Ensure DisableOnAccessProtection is 0 or not set. GPO: Microsoft Defender Antivirus > Real-time Protection > "Monitor file and program activity on your computer" = Enabled. On-access protection scans files when they are accessed.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-006')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-006', @catDefender,
    N'Windows Defender Script Scanning Enabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Real-Time Protection","valueName":"DisableScriptScanning","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking script scanning status"}',
    N'Ensure DisableScriptScanning is 0 or not set. Script scanning via AMSI intercepts and inspects scripts (PowerShell, VBScript, JavaScript) at runtime before execution.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-007')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-007', @catDefender,
    N'Windows Defender PUA Protection Enabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender","valueName":"PUAProtection","expected":1,"operator":"gte","display":"Checking PUA (Potentially Unwanted Application) protection"}',
    N'Set PUAProtection = 1 (Block) or 2 (Audit). GPO: Microsoft Defender Antivirus > "Configure detection for potentially unwanted applications" = Enabled (Block). Blocks adware, bundleware, and potentially harmful applications.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-008')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-008', @catDefender,
    N'Windows Defender Cloud-Delivered Protection Enabled (MAPS)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\SpyNet","valueName":"SpyNetReporting","expected":1,"operator":"gte","display":"Checking cloud-delivered protection (MAPS) status"}',
    N'Set SpyNetReporting = 1 (Basic) or 2 (Advanced). GPO: Microsoft Defender Antivirus > MAPS > "Join Microsoft MAPS" = Enabled. Cloud protection provides rapid signature updates and machine-learning-based detection.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-009')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-009', @catDefender,
    N'Windows Defender Sample Submission Configured',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\SpyNet","valueName":"SubmitSamplesConsent","expected":1,"operator":"gte","display":"Checking automatic sample submission configuration"}',
    N'Set SubmitSamplesConsent = 1 (Send safe samples automatically), 2 (Always prompt), or 3 (Send all samples automatically). GPO: Microsoft Defender Antivirus > MAPS > "Send file samples when further analysis is required". Enables cloud analysis of suspicious files.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-010')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-010', @catDefender,
    N'Windows Defender Tamper Protection Enabled',
    'command', 'critical',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"(Get-MpComputerStatus).IsTamperProtected\"","expected":"True","operator":"eq","display":"Checking tamper protection status"}',
    N'Tamper protection prevents malware from disabling Defender. Enable via Windows Security > Virus & threat protection settings > Tamper Protection = On. Managed via Intune or Microsoft 365 Defender portal for enterprise.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 2 -- Defender Definitions & Updates (AV-011..AV-015)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-011')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-011', @catDefender,
    N'Windows Defender Signature Age (Max 3 Days)',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"(Get-MpComputerStatus).AntivirusSignatureAge\"","expected":3,"operator":"lte","display":"Checking antivirus signature age in days"}',
    N'Update signatures: Update-MpSignature. Signatures older than 3 days leave the system vulnerable to recently discovered threats. Ensure Windows Update or WSUS is delivering definition updates.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-012')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-012', @catDefender,
    N'Windows Defender Engine Version Current',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"(Get-MpComputerStatus).AMEngineVersion\"","expected":"","operator":"neq","display":"Checking antimalware engine version"}',
    N'The antimalware engine should be regularly updated. Engine updates are delivered via Windows Update. Run Update-MpSignature or ensure WSUS/Intune is configured to deliver engine updates.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-013')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-013', @catDefender,
    N'Windows Defender Antivirus Signature Version',
    'command', 'low',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"(Get-MpComputerStatus).AntivirusSignatureVersion\"","expected":"","operator":"neq","display":"Collecting antivirus signature version"}',
    N'Informational: captures the current antivirus definition version for inventory and comparison across the fleet.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-014')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-014', @catDefender,
    N'Windows Defender Full Scan Age (Max 30 Days)',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"(Get-MpComputerStatus).FullScanAge\"","expected":30,"operator":"lte","display":"Checking full scan age in days"}',
    N'Run a full scan: Start-MpScan -ScanType FullScan. Full scans should be completed at least monthly to detect dormant threats that quick scans may miss. Schedule via GPO or Intune.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-015')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-015', @catDefender,
    N'Windows Defender Quick Scan Age (Max 7 Days)',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"(Get-MpComputerStatus).QuickScanAge\"","expected":7,"operator":"lte","display":"Checking quick scan age in days"}',
    N'Run a quick scan: Start-MpScan -ScanType QuickScan. Quick scans should run at least weekly. Schedule via GPO: Microsoft Defender Antivirus > Scan > "Specify the scan type to use for a scheduled scan".',
    1, 1, @systemUserId);

-- ============================================================
-- PART 3 -- Attack Surface Reduction Rules (AV-016..AV-025)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-016')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-016', @catASR,
    N'ASR - Block Executable Content from Email and Webmail',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\ASR\\Rules","valueName":"BE9BA2D9-53EA-4CDC-84E5-9B1EEEE46550","expected":1,"operator":"eq","display":"Checking ASR rule: block executable content from email"}',
    N'Enable via GPO: Microsoft Defender Antivirus > Windows Defender Exploit Guard > Attack Surface Reduction > Configure ASR rules. Set GUID BE9BA2D9-53EA-4CDC-84E5-9B1EEEE46550 = 1 (Block). Prevents executable files and scripts from running via email clients.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-017')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-017', @catASR,
    N'ASR - Block All Office Apps from Creating Child Processes',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\ASR\\Rules","valueName":"D4F940AB-401B-4EFC-AADC-AD5F3C50688A","expected":1,"operator":"eq","display":"Checking ASR rule: block Office child processes"}',
    N'Set GUID D4F940AB-401B-4EFC-AADC-AD5F3C50688A = 1 (Block). Prevents Office applications from spawning child processes, blocking a common malware delivery technique via malicious macros.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-018')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-018', @catASR,
    N'ASR - Block Office Apps from Creating Executable Content',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\ASR\\Rules","valueName":"3B576869-A4EC-4529-8536-B80A7769E899","expected":1,"operator":"eq","display":"Checking ASR rule: block Office executable content creation"}',
    N'Set GUID 3B576869-A4EC-4529-8536-B80A7769E899 = 1 (Block). Prevents Office apps from writing executable files to disk, blocking dropper-style malware from macro-enabled documents.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-019')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-019', @catASR,
    N'ASR - Block Office Apps from Injecting Code into Other Processes',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\ASR\\Rules","valueName":"75668C1F-73B5-4CF0-BB93-3ECF5CB7CC84","expected":1,"operator":"eq","display":"Checking ASR rule: block Office code injection"}',
    N'Set GUID 75668C1F-73B5-4CF0-BB93-3ECF5CB7CC84 = 1 (Block). Prevents Office apps from injecting code into other processes, blocking process hollowing and DLL injection attacks.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-020')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-020', @catASR,
    N'ASR - Block JavaScript/VBScript from Launching Downloaded Executables',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\ASR\\Rules","valueName":"D3E037E1-3EB8-44C8-A917-57927947596D","expected":1,"operator":"eq","display":"Checking ASR rule: block JS/VBS downloaded executable launch"}',
    N'Set GUID D3E037E1-3EB8-44C8-A917-57927947596D = 1 (Block). Prevents scripts from launching executables downloaded from the internet, blocking a common drive-by download attack vector.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-021')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-021', @catASR,
    N'ASR - Block Execution of Potentially Obfuscated Scripts',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\ASR\\Rules","valueName":"5BEB7EFE-FD9A-4556-801D-275E5FFC04CC","expected":1,"operator":"eq","display":"Checking ASR rule: block obfuscated script execution"}',
    N'Set GUID 5BEB7EFE-FD9A-4556-801D-275E5FFC04CC = 1 (Block). Detects and blocks scripts that appear to be obfuscated, a technique commonly used by malware to evade signature-based detection.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-022')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-022', @catASR,
    N'ASR - Block Win32 API Calls from Office Macros',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\ASR\\Rules","valueName":"92E97FA1-2EDF-4476-BDD6-9DD0B4DDDC7B","expected":1,"operator":"eq","display":"Checking ASR rule: block Win32 API calls from macros"}',
    N'Set GUID 92E97FA1-2EDF-4476-BDD6-9DD0B4DDDC7B = 1 (Block). Prevents VBA macros from calling Win32 APIs, blocking shellcode execution and process manipulation from Office documents.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-023')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-023', @catASR,
    N'ASR - Block Credential Stealing from LSASS',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\ASR\\Rules","valueName":"9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2","expected":1,"operator":"eq","display":"Checking ASR rule: block credential stealing from LSASS"}',
    N'Set GUID 9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2 = 1 (Block). Blocks attempts to steal credentials from LSASS (Local Security Authority Subsystem Service), the primary target of Mimikatz and similar tools.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-024')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-024', @catASR,
    N'ASR - Block Untrusted and Unsigned Processes from USB',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\ASR\\Rules","valueName":"b2b3f03d-6a65-4f7b-a9c7-1c7ef74a9ba4","expected":1,"operator":"eq","display":"Checking ASR rule: block untrusted processes from USB"}',
    N'Set GUID b2b3f03d-6a65-4f7b-a9c7-1c7ef74a9ba4 = 1 (Block). Blocks untrusted and unsigned executables from running on removable USB drives, preventing USB-based malware delivery.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-025')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-025', @catASR,
    N'ASR - Use Advanced Protection Against Ransomware',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\ASR\\Rules","valueName":"c1db55ab-c21a-4637-bb3f-a12568109d35","expected":1,"operator":"eq","display":"Checking ASR rule: advanced ransomware protection"}',
    N'Set GUID c1db55ab-c21a-4637-bb3f-a12568109d35 = 1 (Block). Uses cloud intelligence and heuristics to block files that resemble ransomware behavior, providing an additional layer of protection.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 4 -- Network & Exploit Protection (AV-026..AV-030)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-026')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-026', @catExploit,
    N'Network Protection Enabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\Network Protection","valueName":"EnableNetworkProtection","expected":1,"operator":"eq","display":"Checking network protection status"}',
    N'Set EnableNetworkProtection = 1 (Block) or 2 (Audit). GPO: Microsoft Defender Antivirus > Windows Defender Exploit Guard > Network Protection > "Prevent users and apps from accessing dangerous websites". Blocks connections to malicious domains and IPs.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-027')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-027', @catExploit,
    N'Controlled Folder Access Enabled (Ransomware Protection)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Windows Defender Exploit Guard\\Controlled Folder Access","valueName":"EnableControlledFolderAccess","expected":1,"operator":"eq","display":"Checking controlled folder access (ransomware protection) status"}',
    N'Set EnableControlledFolderAccess = 1 (Block) or 2 (Audit). GPO: Microsoft Defender Antivirus > Windows Defender Exploit Guard > Controlled Folder Access > "Configure Controlled folder access". Protects documents, pictures, and other folders from unauthorized modification by ransomware.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-028')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-028', @catExploit,
    N'Exploit Protection - DEP Enabled System-Wide',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"(Get-ProcessMitigation -System).DEP.Enable\"","expected":"True","operator":"eq","display":"Checking system-wide DEP (Data Execution Prevention) status"}',
    N'Enable DEP system-wide: Set-ProcessMitigation -System -Enable DEP. Data Execution Prevention prevents code from executing in non-executable memory regions, blocking buffer overflow exploits.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-029')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-029', @catExploit,
    N'Exploit Protection - Mandatory ASLR Enabled',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"(Get-ProcessMitigation -System).ASLR.ForceRelocateImages\"","expected":"True","operator":"eq","display":"Checking system-wide mandatory ASLR status"}',
    N'Enable mandatory ASLR: Set-ProcessMitigation -System -Enable ForceRelocateImages. Address Space Layout Randomization randomizes memory addresses, making exploitation of memory corruption vulnerabilities significantly harder.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-030')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-030', @catExploit,
    N'Exploit Protection - Control Flow Guard (CFG) Enabled',
    'command', 'high',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"(Get-ProcessMitigation -System).CFG.Enable\"","expected":"True","operator":"eq","display":"Checking system-wide Control Flow Guard status"}',
    N'Enable CFG: Set-ProcessMitigation -System -Enable CFG. Control Flow Guard validates indirect call targets at runtime, preventing ROP (Return-Oriented Programming) and other control flow hijacking attacks.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 5 -- Defender Exclusions Audit (AV-031..AV-034)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-031')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-031', @catDefender,
    N'Defender Exclusions - Path Exclusions Count (Warn if > 5)',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$p=(Get-MpPreference).ExclusionPath; if($p){$p.Count}else{0}\"","expected":5,"operator":"lte","display":"Checking number of path exclusions in Defender"}',
    N'Review path exclusions: Get-MpPreference | Select -ExpandProperty ExclusionPath. Excessive exclusions reduce protection coverage. Each exclusion should be documented and justified. Attackers frequently abuse exclusion paths to hide malware.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-032')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-032', @catDefender,
    N'Defender Exclusions - Process Exclusions Count (Warn if > 5)',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$p=(Get-MpPreference).ExclusionProcess; if($p){$p.Count}else{0}\"","expected":5,"operator":"lte","display":"Checking number of process exclusions in Defender"}',
    N'Review process exclusions: Get-MpPreference | Select -ExpandProperty ExclusionProcess. Process exclusions bypass real-time scanning for specified executables. Limit to vendor-required exclusions only.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-033')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-033', @catDefender,
    N'Defender Exclusions - Extension Exclusions Count (Warn if > 3)',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$p=(Get-MpPreference).ExclusionExtension; if($p){$p.Count}else{0}\"","expected":3,"operator":"lte","display":"Checking number of extension exclusions in Defender"}',
    N'Review extension exclusions: Get-MpPreference | Select -ExpandProperty ExclusionExtension. Extension exclusions are particularly dangerous as they apply globally. Avoid excluding common script extensions (.ps1, .vbs, .js).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-034')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-034', @catDefender,
    N'Defender Exclusions - IP Address Exclusions Exist (Warn if Any)',
    'command', 'medium',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"$p=(Get-MpPreference).ExclusionIpAddress; if($p){$p.Count}else{0}\"","expected":0,"operator":"eq","display":"Checking for IP address exclusions in Defender"}',
    N'Review IP exclusions: Get-MpPreference | Select -ExpandProperty ExclusionIpAddress. IP exclusions bypass network inspection for traffic to/from specified addresses. These are rarely justified and should be removed unless documented.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 6 -- Third-Party AV & EDR Detection (AV-035..AV-040)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-035')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-035', @catEPP,
    N'Third-Party Antivirus Product Registered',
    'command', 'low',
    N'{"executable":"powershell.exe","arguments":"-NoProfile -Command \"Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction SilentlyContinue | Select-Object -ExpandProperty displayName | ConvertTo-Json\"","expected":"","operator":"neq","optional":true,"display":"Detecting registered antivirus products via WMI SecurityCenter2"}',
    N'Informational: lists all antivirus products registered with Windows Security Center. Use this to verify that the expected AV product is installed and registered across the fleet.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-036')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-036', @catEPP,
    N'EDR Agent Present - CrowdStrike Falcon',
    'service', 'low',
    N'{"serviceName":"CSFalconService","expectedStatus":"Running","optional":true,"display":"Checking for CrowdStrike Falcon EDR service"}',
    N'Informational: detects whether CrowdStrike Falcon sensor is installed and running. If expected by policy, ensure the CSFalconService service is present and in Running state.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-037')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-037', @catEPP,
    N'EDR Agent Present - SentinelOne',
    'service', 'low',
    N'{"serviceName":"SentinelAgent","expectedStatus":"Running","optional":true,"display":"Checking for SentinelOne EDR service"}',
    N'Informational: detects whether SentinelOne agent is installed and running. If expected by policy, ensure the SentinelAgent service is present and in Running state.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-038')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-038', @catEPP,
    N'EDR Agent Present - VMware Carbon Black',
    'service', 'low',
    N'{"serviceName":"CbDefense","expectedStatus":"Running","optional":true,"display":"Checking for Carbon Black EDR service"}',
    N'Informational: detects whether VMware Carbon Black agent is installed and running. The service name is CbDefense (Carbon Black Cloud) or CarbonBlack (legacy). If expected by policy, verify service status.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-039')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-039', @catEPP,
    N'EDR Agent Present - Palo Alto Cortex XDR',
    'service', 'low',
    N'{"serviceName":"CortexXDR","expectedStatus":"Running","optional":true,"display":"Checking for Cortex XDR EDR service"}',
    N'Informational: detects whether Palo Alto Cortex XDR agent is installed and running. If expected by policy, ensure the CortexXDR service (or cyserver) is present and in Running state.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='AV-040')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AV-040', @catEPP,
    N'Microsoft Defender for Endpoint Onboarded',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows Advanced Threat Protection\\Status","valueName":"OnboardingState","expected":1,"operator":"eq","optional":true,"display":"Checking Microsoft Defender for Endpoint onboarding status"}',
    N'Informational: checks whether the machine is onboarded to Microsoft Defender for Endpoint (MDE). OnboardingState = 1 means the device is connected to the MDE cloud service for advanced threat detection, EDR, and automated investigation.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 7 -- Platform scope (ALL 5: W10, W11, MS19, MS22, MS25)
-- ============================================================

-- Collect all AV-* control IDs
DECLARE @avBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @avBlock VALUES
    ('AV-001'),('AV-002'),('AV-003'),('AV-004'),('AV-005'),('AV-006'),('AV-007'),('AV-008'),('AV-009'),('AV-010'),
    ('AV-011'),('AV-012'),('AV-013'),('AV-014'),('AV-015'),('AV-016'),('AV-017'),('AV-018'),('AV-019'),('AV-020'),
    ('AV-021'),('AV-022'),('AV-023'),('AV-024'),('AV-025'),('AV-026'),('AV-027'),('AV-028'),('AV-029'),('AV-030'),
    ('AV-031'),('AV-032'),('AV-033'),('AV-034'),('AV-035'),('AV-036'),('AV-037'),('AV-038'),('AV-039'),('AV-040');

-- Link to W10
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platW10
FROM control_defs cd
JOIN @avBlock b ON b.control_id = cd.control_id
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
JOIN @avBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platW11
);

DECLARE @w11Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for W11: ', @w11Rows);

-- Link to MS19
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platMS19
FROM control_defs cd
JOIN @avBlock b ON b.control_id = cd.control_id
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
JOIN @avBlock b ON b.control_id = cd.control_id
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
JOIN @avBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platMS25
);

DECLARE @ms25Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for MS25: ', @ms25Rows);

-- ============================================================
-- PART 8 -- Framework mappings (ALL 5 frameworks)
-- ============================================================

-- Link all AV-* controls to NIST
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST
FROM control_defs cd
JOIN @avBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwNIST
);

-- Link all AV-* controls to CIS
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS
FROM control_defs cd
JOIN @avBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwCIS
);

-- Link all AV-* controls to HIPAA
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA
FROM control_defs cd
JOIN @avBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwHIPAA
);

-- Link all AV-* controls to ISO 27001
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO
FROM control_defs cd
JOIN @avBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwISO
);

-- Link all AV-* controls to PCI-DSS
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwPCI
FROM control_defs cd
JOIN @avBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwPCI
);

-- ============================================================
-- PART 9 -- Link to existing active assessments
-- ============================================================
INSERT INTO assessment_controls (assessment_id, control_def_id)
SELECT a.id, cd.id
FROM assessments a
CROSS JOIN control_defs cd
JOIN @avBlock b ON b.control_id = cd.control_id
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
PRINT '=== seed_011_antivirus_controls verification ===';

SELECT 'AV controls total' AS metric, COUNT(*) AS value
FROM control_defs WHERE control_id LIKE 'AV-%' AND is_active = 1;

SELECT 'AV controls by type' AS metric, [type], COUNT(*) AS cnt
FROM control_defs WHERE control_id LIKE 'AV-%' AND is_active = 1
GROUP BY [type] ORDER BY cnt DESC;

SELECT 'AV controls by severity' AS metric, severity, COUNT(*) AS cnt
FROM control_defs WHERE control_id LIKE 'AV-%' AND is_active = 1
GROUP BY severity ORDER BY cnt DESC;

SELECT 'AV platform linkage' AS metric, p.code, COUNT(*) AS cnt
FROM control_platforms cp
JOIN control_defs cd ON cd.id = cp.control_def_id
JOIN platforms p ON p.id = cp.platform_id
WHERE cd.control_id LIKE 'AV-%'
GROUP BY p.code ORDER BY p.code;

SELECT 'AV framework linkage' AS metric, f.code, COUNT(*) AS cnt
FROM control_frameworks cf
JOIN control_defs cd ON cd.id = cf.control_def_id
JOIN frameworks f ON f.id = cf.framework_id
WHERE cd.control_id LIKE 'AV-%'
GROUP BY f.code ORDER BY f.code;

SELECT 'Total active controls (all)' AS metric, COUNT(*) AS value
FROM control_defs WHERE is_active = 1;
GO
