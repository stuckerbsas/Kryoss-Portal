SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_014_edge_browser_controls.sql
-- Kryoss Platform -- Microsoft Edge Browser Hardening Controls
--
-- Adds 30 controls (EDGE-001 through EDGE-030) covering Edge
-- browser security settings from the Microsoft Edge v139
-- Security Baseline. All settings are registry-based under
-- HKLM\SOFTWARE\Policies\Microsoft\Edge\.
--
-- Platform scope: W10, W11 only (browsers on workstations).
-- NOT linked to servers (MS19, MS22, MS25) or DCs.
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
-- PART 0 -- New category for Edge/Browser hardening
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Edge Browser Security')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Edge Browser Security', 200, @systemUserId);

DECLARE @catEdge INT = (SELECT id FROM control_categories WHERE name=N'Edge Browser Security');

-- ============================================================
-- SECTION A: TLS / HTTPS (EDGE-001..EDGE-005)
-- ============================================================

-- EDGE-001: SSL error override disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-001')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-001', @catEdge,
    N'Edge: SSL Error Override Disabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"SSLErrorOverrideAllowed","expected":0,"operator":"eq","missingBehavior":"warn","display":"Checking Edge SSL error override policy"}',
    N'GPO: Microsoft Edge > "Allow users to proceed from the HTTPS warning page" = Disabled (value 0). Prevents users from bypassing certificate errors, which could expose them to MITM attacks.',
    1, 1, @systemUserId);

-- EDGE-002: Minimum TLS version 1.2
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-002')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-002', @catEdge,
    N'Edge: Minimum TLS Version 1.2',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"SSLVersionMin","expected":"tls1.2","operator":"eq","missingBehavior":"warn","display":"Checking Edge minimum TLS version"}',
    N'GPO: Microsoft Edge > "Minimum TLS version enabled" = tls1.2. Prevents downgrade to TLS 1.0/1.1 which have known vulnerabilities.',
    1, 1, @systemUserId);

-- EDGE-003: HTTPS upgrades enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-003')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-003', @catEdge,
    N'Edge: Automatic HTTPS Upgrades Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"HttpsUpgradesEnabled","expected":1,"operator":"eq","missingBehavior":"pass","display":"Checking Edge automatic HTTPS upgrades"}',
    N'GPO: Microsoft Edge > "Enable automatic HTTPS upgrades" = Enabled (value 1). Automatically upgrades HTTP navigations to HTTPS for improved security.',
    1, 1, @systemUserId);

-- EDGE-004: Certificate transparency enforcement
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-004')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-004', @catEdge,
    N'Edge: Certificate Transparency Enforcement Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"CertificateTransparencyEnforcementDisabledForUrls","expected":null,"operator":"notExists","missingBehavior":"pass","display":"Checking Edge certificate transparency enforcement"}',
    N'Ensure no URLs are exempt from Certificate Transparency enforcement. CT helps detect misissued certificates. Do not configure CertificateTransparencyEnforcementDisabledForUrls unless absolutely necessary.',
    1, 1, @systemUserId);

-- EDGE-005: Insecure content blocking
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-005')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-005', @catEdge,
    N'Edge: Mixed Content (Insecure Content) Blocked',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"DefaultInsecureContentSetting","expected":2,"operator":"eq","missingBehavior":"pass","display":"Checking Edge insecure content blocking"}',
    N'GPO: Microsoft Edge > "Default insecure content setting" = Block (value 2). Prevents loading HTTP resources on HTTPS pages (mixed content), which can compromise page security.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION B: SmartScreen & Phishing (EDGE-006..EDGE-010)
-- ============================================================

-- EDGE-006: SmartScreen enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-006')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-006', @catEdge,
    N'Edge: Microsoft Defender SmartScreen Enabled',
    'registry', 'critical',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"SmartScreenEnabled","expected":1,"operator":"eq","missingBehavior":"fail","display":"Checking Edge SmartScreen status"}',
    N'GPO: Microsoft Edge > SmartScreen settings > "Configure Microsoft Defender SmartScreen" = Enabled (value 1). SmartScreen protects against phishing and malware by checking sites and downloads against a dynamic threat database.',
    1, 1, @systemUserId);

-- EDGE-007: SmartScreen for PUA blocking
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-007')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-007', @catEdge,
    N'Edge: SmartScreen PUA (Potentially Unwanted Apps) Blocking',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"SmartScreenPuaEnabled","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking Edge SmartScreen PUA blocking"}',
    N'GPO: Microsoft Edge > SmartScreen settings > "Configure Microsoft Defender SmartScreen to block potentially unwanted apps" = Enabled (value 1). Blocks adware, coin miners, and bundleware.',
    1, 1, @systemUserId);

-- EDGE-008: Prevent SmartScreen bypass for sites
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-008')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-008', @catEdge,
    N'Edge: Prevent SmartScreen Prompt Override (Sites)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"PreventSmartScreenPromptOverride","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking Edge SmartScreen site bypass prevention"}',
    N'GPO: Microsoft Edge > SmartScreen settings > "Prevent bypassing Microsoft Defender SmartScreen prompts for sites" = Enabled (value 1). Users cannot ignore SmartScreen warnings and continue to malicious sites.',
    1, 1, @systemUserId);

-- EDGE-009: Prevent SmartScreen bypass for downloads
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-009')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-009', @catEdge,
    N'Edge: Prevent SmartScreen Prompt Override (Downloads)',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"PreventSmartScreenPromptOverrideForFiles","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking Edge SmartScreen download bypass prevention"}',
    N'GPO: Microsoft Edge > SmartScreen settings > "Prevent bypassing of Microsoft Defender SmartScreen warnings about downloads" = Enabled (value 1). Blocks users from ignoring SmartScreen download warnings.',
    1, 1, @systemUserId);

-- EDGE-010: Typosquatting protection
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-010')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-010', @catEdge,
    N'Edge: Website Typo Protection (Typosquatting) Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"TyposquattingCheckerEnabled","expected":1,"operator":"eq","missingBehavior":"pass","display":"Checking Edge typosquatting protection"}',
    N'GPO: Microsoft Edge > "Configure Edge Website Typo Protection" = Enabled (value 1). Warns users when they navigate to potential typosquatting sites that impersonate legitimate domains.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION C: Privacy & Security (EDGE-011..EDGE-017)
-- ============================================================

-- EDGE-011: Autofill for payment instruments disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-011')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-011', @catEdge,
    N'Edge: Autofill for Payment Instruments Disabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"AutofillCreditCardEnabled","expected":0,"operator":"eq","missingBehavior":"warn","display":"Checking Edge payment autofill status"}',
    N'GPO: Microsoft Edge > "Enable AutoFill for payment instruments" = Disabled (value 0). Prevents Edge from storing and auto-filling credit card information, reducing exposure if the browser is compromised.',
    1, 1, @systemUserId);

-- EDGE-012: Site isolation enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-012')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-012', @catEdge,
    N'Edge: Site Isolation (Site Per Process) Enabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"SitePerProcess","expected":1,"operator":"eq","missingBehavior":"pass","display":"Checking Edge site isolation"}',
    N'GPO: Microsoft Edge > "Enable site isolation for every site" = Enabled (value 1). Runs each site in its own process, mitigating Spectre-class side-channel attacks and cross-site data leaks.',
    1, 1, @systemUserId);

-- EDGE-013: Browser sign-in policy
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-013')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-013', @catEdge,
    N'Edge: Browser Sign-In Policy Configured',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"BrowserSignin","expected":1,"operator":"gte","missingBehavior":"warn","display":"Checking Edge browser sign-in policy"}',
    N'GPO: Microsoft Edge > "Configure whether a user can sign in to Microsoft Edge" = Enabled (1) or Force (2). Value 1 allows sign-in, value 2 forces sign-in with organizational account. Prevents unmanaged personal accounts.',
    1, 1, @systemUserId);

-- EDGE-014: Do Not Track header
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-014')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-014', @catEdge,
    N'Edge: Tracking Prevention Enabled (Strict)',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"TrackingPrevention","expected":3,"operator":"eq","missingBehavior":"warn","display":"Checking Edge tracking prevention level"}',
    N'GPO: Microsoft Edge > "Tracking prevention" = Strict (value 3). Blocks most third-party trackers. Values: 0=Off, 1=Basic, 2=Balanced, 3=Strict.',
    1, 1, @systemUserId);

-- EDGE-015: Application bound encryption
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-015')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-015', @catEdge,
    N'Edge: Application Bound Encryption Enabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"ApplicationBoundEncryptionEnabled","expected":1,"operator":"eq","missingBehavior":"pass","display":"Checking Edge application bound encryption"}',
    N'GPO: Microsoft Edge > "Enable Application Bound Encryption" = Enabled (value 1). Binds encryption keys to Microsoft Edge, preventing other applications from extracting stored credentials and cookies.',
    1, 1, @systemUserId);

-- EDGE-016: Basic auth over HTTP disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-016')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-016', @catEdge,
    N'Edge: Basic Authentication over HTTP Disabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"BasicAuthOverHttpEnabled","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Edge basic auth over HTTP"}',
    N'GPO: Microsoft Edge > HTTP authentication > "Allow Basic authentication for HTTP" = Disabled (value 0). Prevents sending credentials in cleartext over unencrypted HTTP connections.',
    1, 1, @systemUserId);

-- EDGE-017: SharedArrayBuffer restricted
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-017')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-017', @catEdge,
    N'Edge: SharedArrayBuffer Restricted to Cross-Origin Isolated',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"SharedArrayBufferUnrestrictedAccessAllowed","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Edge SharedArrayBuffer restriction"}',
    N'GPO: Microsoft Edge > "Specifies whether SharedArrayBuffers can be used in a non cross-origin-isolated context" = Disabled (value 0). SharedArrayBuffers have memory access vulnerabilities; restrict to cross-origin isolated contexts.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION D: Extensions & Downloads (EDGE-018..EDGE-022)
-- ============================================================

-- EDGE-018: Download restrictions
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-018')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-018', @catEdge,
    N'Edge: Dangerous Download Restrictions Enabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"DownloadRestrictions","expected":1,"operator":"gte","missingBehavior":"warn","display":"Checking Edge download restriction level"}',
    N'GPO: Microsoft Edge > "Allow download restrictions" >= 1. Values: 0=No restrictions, 1=Block dangerous downloads, 2=Block potentially dangerous, 3=Block all, 4=Block malicious. Recommended: 1 or 4.',
    1, 1, @systemUserId);

-- EDGE-019: Extension install blocklist
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-019')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-019', @catEdge,
    N'Edge: Extension Install Blocklist Configured',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge\\ExtensionInstallBlocklist","valueName":"1","expected":"*","operator":"eq","missingBehavior":"warn","display":"Checking Edge extension install blocklist"}',
    N'GPO: Microsoft Edge > Extensions > "Control which extensions cannot be installed" = * (block all by default). Then use ExtensionInstallAllowlist to whitelist approved extensions. Prevents installation of malicious or unapproved extensions.',
    1, 1, @systemUserId);

-- EDGE-020: User-level native messaging hosts disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-020')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-020', @catEdge,
    N'Edge: User-Level Native Messaging Hosts Disabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"NativeMessagingUserLevelHosts","expected":0,"operator":"eq","missingBehavior":"warn","display":"Checking Edge native messaging hosts policy"}',
    N'GPO: Microsoft Edge > Native Messaging > "Allow user-level native messaging hosts" = Disabled (value 0). Only system-level (admin-installed) native messaging hosts are allowed, preventing user-installed malicious hosts.',
    1, 1, @systemUserId);

-- EDGE-021: SwiftShader WebGL fallback disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-021')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-021', @catEdge,
    N'Edge: Software WebGL (SwiftShader) Fallback Disabled',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"EnableUnsafeSwiftShader","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Edge SwiftShader WebGL fallback"}',
    N'GPO: Microsoft Edge > "Allow software WebGL fallback using SwiftShader" = Disabled (value 0). SwiftShader has been deprecated due to security concerns as of Edge 139.',
    1, 1, @systemUserId);

-- EDGE-022: Legacy extension points blocking
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-022')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-022', @catEdge,
    N'Edge: Browser Legacy Extension Point Blocking Enabled',
    'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"BrowserLegacyExtensionPointsBlockingEnabled","expected":1,"operator":"eq","missingBehavior":"pass","display":"Checking Edge legacy extension point blocking"}',
    N'GPO: Microsoft Edge > "Enable browser legacy extension point blocking" = Enabled (value 1). Blocks code injection from legacy third-party applications into the browser process via ProcessExtensionPointDisablePolicy.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION E: Enterprise / IE Mode (EDGE-023..EDGE-026)
-- ============================================================

-- EDGE-023: IE mode reload disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-023')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-023', @catEdge,
    N'Edge: IE Mode Reload for Unconfigured Sites Disabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"InternetExplorerIntegrationReloadInIEModeAllowed","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Edge IE mode reload policy"}',
    N'GPO: Microsoft Edge > "Allow unconfigured sites to be reloaded in Internet Explorer mode" = Disabled (value 0). Prevents users from switching arbitrary sites to IE mode which has weaker security.',
    1, 1, @systemUserId);

-- EDGE-024: MHTML auto-open in IE mode disabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-024')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-024', @catEdge,
    N'Edge: Auto-Open Downloaded MHTML in IE Mode Disabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"InternetExplorerIntegrationZoneIdentifierMhtFileAllowed","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Edge MHTML IE mode auto-open"}',
    N'GPO: Microsoft Edge > "Automatically open downloaded MHT or MHTML files from the web in Internet Explorer mode" = Disabled (value 0). Prevents automatic IE mode rendering of downloaded MHTML files which could exploit IE vulnerabilities.',
    1, 1, @systemUserId);

-- EDGE-025: IE mode toolbar button hidden
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-025')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-025', @catEdge,
    N'Edge: IE Mode Toolbar Button Hidden',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"InternetExplorerModeToolbarButtonEnabled","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking Edge IE mode toolbar button visibility"}',
    N'GPO: Microsoft Edge > "Show the Reload in Internet Explorer mode button in the toolbar" = Disabled (value 0). Hides the IE mode toolbar button to discourage casual IE mode usage.',
    1, 1, @systemUserId);

-- EDGE-026: Built-in DNS client enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-026')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-026', @catEdge,
    N'Edge: Built-In DNS Client Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"BuiltInDnsClientEnabled","expected":1,"operator":"eq","missingBehavior":"pass","display":"Checking Edge built-in DNS client"}',
    N'GPO: Microsoft Edge > "Use built-in DNS client" = Enabled (value 1). Uses the Chromium DNS client for better DNS-over-HTTPS support and consistent DNS behavior.',
    1, 1, @systemUserId);

-- ============================================================
-- SECTION F: Additional Security (EDGE-027..EDGE-030)
-- ============================================================

-- EDGE-027: DNS-over-HTTPS mode
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-027')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-027', @catEdge,
    N'Edge: DNS-over-HTTPS (DoH) Mode Configured',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"DnsOverHttpsMode","expected":"automatic","operator":"eq","missingBehavior":"warn","display":"Checking Edge DNS-over-HTTPS mode"}',
    N'GPO: Microsoft Edge > "Control the mode of DNS-over-HTTPS" = automatic. Values: off, automatic, secure. "automatic" will upgrade to DoH when available. "secure" only allows DoH connections.',
    1, 1, @systemUserId);

-- EDGE-028: Password manager enabled with monitoring
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-028')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-028', @catEdge,
    N'Edge: Password Monitor (Leak Detection) Enabled',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"PasswordMonitorAllowed","expected":1,"operator":"eq","missingBehavior":"pass","display":"Checking Edge password monitor"}',
    N'GPO: Microsoft Edge > "Allow users to be alerted if their passwords are found to be unsafe" = Enabled (value 1). Alerts users when saved passwords appear in known data breaches.',
    1, 1, @systemUserId);

-- EDGE-029: InPrivate mode availability
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-029')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-029', @catEdge,
    N'Edge: InPrivate Mode Availability Controlled',
    'registry', 'low',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"InPrivateModeAvailability","expected":1,"operator":"lte","missingBehavior":"pass","display":"Checking Edge InPrivate mode availability"}',
    N'GPO: Microsoft Edge > "Configure InPrivate mode availability" = 0 (Enabled) or 1 (Disabled). Value 1 disables InPrivate browsing which bypasses enterprise logging. Consider organizational needs before disabling.',
    1, 1, @systemUserId);

-- EDGE-030: Sandbox external protocols blocked
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='EDGE-030')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('EDGE-030', @catEdge,
    N'Edge: Sandbox External Protocol Navigation Blocked',
    'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Edge","valueName":"SandboxExternalProtocolBlocked","expected":1,"operator":"eq","missingBehavior":"pass","display":"Checking Edge sandbox external protocol blocking"}',
    N'GPO: Microsoft Edge > "Allow Microsoft Edge to block navigations to external protocols in a sandboxed iframe" = Enabled (value 1). Prevents sandboxed iframes from launching external protocol handlers.',
    1, 1, @systemUserId);

-- ============================================================
-- PART P -- Platform linkage (W10, W11 ONLY)
-- ============================================================

DECLARE @edgeBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @edgeBlock VALUES
    ('EDGE-001'),('EDGE-002'),('EDGE-003'),('EDGE-004'),('EDGE-005'),('EDGE-006'),('EDGE-007'),('EDGE-008'),('EDGE-009'),('EDGE-010'),
    ('EDGE-011'),('EDGE-012'),('EDGE-013'),('EDGE-014'),('EDGE-015'),('EDGE-016'),('EDGE-017'),('EDGE-018'),('EDGE-019'),('EDGE-020'),
    ('EDGE-021'),('EDGE-022'),('EDGE-023'),('EDGE-024'),('EDGE-025'),('EDGE-026'),('EDGE-027'),('EDGE-028'),('EDGE-029'),('EDGE-030');

-- Link to W10
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platW10
FROM control_defs cd
JOIN @edgeBlock b ON b.control_id = cd.control_id
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
JOIN @edgeBlock b ON b.control_id = cd.control_id
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
JOIN @edgeBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwNIST
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS
FROM control_defs cd
JOIN @edgeBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwCIS
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA
FROM control_defs cd
JOIN @edgeBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwHIPAA
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO
FROM control_defs cd
JOIN @edgeBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwISO
);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwPCI
FROM control_defs cd
JOIN @edgeBlock b ON b.control_id = cd.control_id
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
JOIN @edgeBlock b ON b.control_id = cd.control_id
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

-- Count new Edge controls
SELECT COUNT(*) AS new_edge_controls
FROM control_defs
WHERE control_id LIKE 'EDGE-%' AND is_active = 1;
-- Expected: 30

-- Breakdown by engine type
SELECT cd.[type], COUNT(*) AS control_count
FROM control_defs cd
WHERE cd.control_id LIKE 'EDGE-%' AND cd.is_active = 1
GROUP BY cd.[type]
ORDER BY cd.[type];
-- Expected: registry 30

-- Breakdown by severity
SELECT cd.severity, COUNT(*) AS control_count
FROM control_defs cd
WHERE cd.control_id LIKE 'EDGE-%' AND cd.is_active = 1
GROUP BY cd.severity
ORDER BY cd.severity;
-- Expected: critical 1, high ~10, medium ~13, low ~6

-- Verify platform linkage (workstation only)
SELECT p.code, COUNT(cp.control_def_id) AS edge_controls_linked
FROM platforms p
LEFT JOIN control_platforms cp ON cp.platform_id = p.id
LEFT JOIN control_defs cd ON cd.id = cp.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'EDGE-%'
WHERE p.code IN ('W10','W11')
GROUP BY p.code
ORDER BY p.code;
-- Expected: W10=30, W11=30

-- Verify NOT linked to servers or DCs
SELECT p.code, COUNT(cp.control_def_id) AS edge_controls_linked
FROM platforms p
LEFT JOIN control_platforms cp ON cp.platform_id = p.id
LEFT JOIN control_defs cd ON cd.id = cp.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'EDGE-%'
WHERE p.code IN ('MS19','MS22','MS25','DC19','DC22','DC25')
GROUP BY p.code
ORDER BY p.code;
-- Expected: all 0

-- Verify framework linkage
SELECT f.code, COUNT(cf.control_def_id) AS edge_controls_linked
FROM frameworks f
LEFT JOIN control_frameworks cf ON cf.framework_id = f.id
LEFT JOIN control_defs cd ON cd.id = cf.control_def_id
    AND cd.is_active = 1
    AND cd.control_id LIKE 'EDGE-%'
WHERE f.is_active = 1
GROUP BY f.code
ORDER BY f.code;
-- Expected: CIS=30, HIPAA=30, ISO27001=30, NIST=30, PCI-DSS=30

-- Verify assessment linkage
SELECT a.name, COUNT(ac.control_def_id) AS edge_controls_linked
FROM assessments a
JOIN assessment_controls ac ON ac.assessment_id = a.id
JOIN control_defs cd ON cd.id = ac.control_def_id
WHERE cd.control_id LIKE 'EDGE-%' AND cd.is_active = 1
  AND a.is_active = 1 AND a.deleted_at IS NULL
GROUP BY a.name;
-- Expected: each active assessment has 30 EDGE controls linked

-- Updated catalog totals
SELECT
    COUNT(*) AS total_active,
    SUM(CASE WHEN control_id LIKE 'EDGE-%' THEN 1 ELSE 0 END) AS edge_browser,
    SUM(CASE WHEN control_id LIKE 'DC-%' THEN 1 ELSE 0 END) AS dc_controls,
    SUM(CASE WHEN control_id LIKE 'BLG-%' THEN 1 ELSE 0 END) AS baseline_gap,
    SUM(CASE WHEN control_id LIKE 'SRV-%' THEN 1 ELSE 0 END) AS server_specific,
    SUM(CASE WHEN control_id LIKE 'AV-%' THEN 1 ELSE 0 END) AS antivirus,
    SUM(CASE WHEN control_id LIKE 'SC-%' THEN 1 ELSE 0 END) AS scored,
    SUM(CASE WHEN control_id LIKE 'BL-%' THEN 1 ELSE 0 END) AS baseline_registry
FROM control_defs
WHERE is_active = 1;
