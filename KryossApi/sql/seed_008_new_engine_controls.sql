SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_008_new_engine_controls.sql
-- Kryoss Platform -- New-engine control seed
--
-- Introduces 15 new control_defs (BL-0470..BL-0484) that exercise
-- the four new agent engines shipped in the same redeploy cycle:
--
--   eventlog   -> BL-0470..BL-0473   (Security/System/App log state)
--   certstore  -> BL-0474..BL-0479   (LocalMachine cert store hygiene)
--   bitlocker  -> BL-0480..BL-0483   (dedicated BitLocker engine)
--   tpm        -> BL-0484..BL-0486   (hardware root of trust)
--
-- Design notes:
--   * camelCase check_json from day 1 (no snake_case debt).
--   * Platform scope: W10 + W11 (Phase 1 workstation-only).
--   * Linked to every currently active assessment (so existing
--     enrollment codes immediately pick up the new controls).
--   * BL-0458..0466 (wevtutil / wbadmin shell controls) stay
--     as-is -- this seed does NOT rewrite them. New eventlog/
--     bitlocker controls coexist with the legacy registry-based
--     counterparts; portal scoring can prefer whichever it likes.
--
-- Run AFTER seed_005_controls_patch.sql. Fully idempotent.
-- ============================================================

-- ============================================================
-- PART 0 -- Relax ck_ctrldef_type to accept the 4 new engine types
-- ============================================================
-- The CHECK constraint `ck_ctrldef_type` was created in 004_assessment.sql
-- with the original 7 engine types. Seed_004 also recreates it with the
-- same 7 values. We need to extend it to accept the new engines before
-- the INSERTs below, otherwise every new row fails with error 547.
--
-- DDL lives in its own batch (outside of any transaction) so the
-- constraint change is committed before PART 2 runs.
-- ============================================================
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_ctrldef_type')
    ALTER TABLE control_defs DROP CONSTRAINT ck_ctrldef_type;
GO
ALTER TABLE control_defs ADD CONSTRAINT ck_ctrldef_type CHECK ([type] IN (
    'registry','secedit','auditpol','firewall','service','netaccount','command',
    'eventlog','certstore','bitlocker','tpm'
));
GO

BEGIN TRANSACTION;

DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

DECLARE @fwCIS   INT = (SELECT id FROM frameworks WHERE code='CIS');
DECLARE @fwNIST  INT = (SELECT id FROM frameworks WHERE code='NIST');
DECLARE @fwHIPAA INT = (SELECT id FROM frameworks WHERE code='HIPAA');
DECLARE @fwPCI   INT = (SELECT id FROM frameworks WHERE code='PCI-DSS');
DECLARE @fwISO   INT = (SELECT id FROM frameworks WHERE code='ISO27001');

DECLARE @platW10 INT = (SELECT id FROM platforms WHERE code='W10');
DECLARE @platW11 INT = (SELECT id FROM platforms WHERE code='W11');

IF @platW10 IS NULL OR @platW11 IS NULL
BEGIN
    RAISERROR('W10 or W11 platform missing from platforms table. Run seed_002 first.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ============================================================
-- PART 1 -- New categories
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Endpoint Hardware Trust')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Endpoint Hardware Trust', 210, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Certificate Hygiene')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Certificate Hygiene', 211, @systemUserId);

DECLARE @catAuditLog INT = (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring');
DECLARE @catHwTrust  INT = (SELECT id FROM control_categories WHERE name=N'Endpoint Hardware Trust');
DECLARE @catCerts    INT = (SELECT id FROM control_categories WHERE name=N'Certificate Hygiene');

-- ============================================================
-- PART 2 -- New control_defs
-- ============================================================

-- ---------- 2.a EVENTLOG engine (BL-0470..BL-0473) ----------

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0470')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0470', @catAuditLog,
    N'Security Event Log - Effective Max Size',
    'eventlog', 'high',
    N'{"checkType":"max_size","logName":"Security","expected":201326592,"operator":"gte","display":"Reading Security log effective max size"}',
    N'Uses EventLogConfiguration.MaximumSizeInBytes. Should be at least 192 MB (201326592 bytes) for HIPAA/PCI audit retention.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0471')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0471', @catAuditLog,
    N'Security Event Log - Retention Mode',
    'eventlog', 'high',
    N'{"checkType":"retention","logName":"Security","expected":"Circular","operator":"eq","display":"Reading Security log retention mode"}',
    N'Returns LogMode (Circular / AutoBackup / Retain). Circular is the CIS-recommended default.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0472')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0472', @catAuditLog,
    N'Security Event Log - Last Cleared',
    'eventlog', 'medium',
    N'{"checkType":"last_cleared","logName":"Security","display":"Checking when Security log was last cleared"}',
    N'Captures timestamp of the most recent EventID 1102 (Security log cleared). Portal flags suspicious recent clears.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0473')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0473', @catAuditLog,
    N'Security - Failed Logons Last 24h (EventID 4625)',
    'eventlog', 'medium',
    N'{"checkType":"event_count","logName":"Security","valueName":"4625","expected":50,"operator":"lte","display":"Counting failed logons in last 24h"}',
    N'High counts of EventID 4625 suggest brute-force attempts. Threshold is advisory; portal correlates with account lockout policy.',
    1, 1, @systemUserId);

-- ---------- 2.b CERTSTORE engine (BL-0474..BL-0479) ----------

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0474')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0474', @catCerts,
    N'LocalMachine Personal Store - Self-Signed Count',
    'certstore', 'medium',
    N'{"checkType":"count_self_signed","storeName":"My","storeLocation":"LocalMachine","expected":2,"operator":"lte","display":"Counting self-signed certs in LocalMachine\\My"}',
    N'Excessive self-signed certs in LocalMachine\\My suggest ad-hoc trust. Investigate anything above 2.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0475')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0475', @catCerts,
    N'LocalMachine Personal Store - Expiring in 30 Days',
    'certstore', 'high',
    N'{"checkType":"count_expiring","storeName":"My","storeLocation":"LocalMachine","expected":0,"operator":"eq","display":"Counting expiring certs in LocalMachine\\My"}',
    N'Machine certs expiring in under 30 days must be renewed. Breaks TLS, RDP, 802.1X, SCCM, SCEP.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0476')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0476', @catCerts,
    N'LocalMachine Personal Store - Weak Key Count',
    'certstore', 'critical',
    N'{"checkType":"count_weak_key","storeName":"My","storeLocation":"LocalMachine","expected":0,"operator":"eq","display":"Scanning for weak-key certificates"}',
    N'Counts RSA certs under 2048 bits or ECC under 256 bits. PCI-DSS 2.3 and NIST SC-12 disallow weak keys.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0477')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0477', @catCerts,
    N'LocalMachine Root Store - Self-Signed Count (Info)',
    'certstore', 'low',
    N'{"checkType":"count_self_signed","storeName":"Root","storeLocation":"LocalMachine","display":"Counting trusted root certificates"}',
    N'Informational: self-signed entries in Root are expected (all trusted roots are self-signed by definition). Portal tracks trend for anomaly detection.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0478')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0478', @catCerts,
    N'TrustedPublisher Store - Thumbprint List',
    'certstore', 'low',
    N'{"checkType":"list_thumbprints","storeName":"TrustedPublisher","storeLocation":"LocalMachine","display":"Listing trusted publisher thumbprints"}',
    N'Captures the list of trusted code-signing publishers. Portal diffs across runs to detect unauthorized additions.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0479')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0479', @catCerts,
    N'LocalMachine Root Store - Weak Key Count',
    'certstore', 'high',
    N'{"checkType":"count_weak_key","storeName":"Root","storeLocation":"LocalMachine","expected":0,"operator":"eq","display":"Scanning trusted roots for weak keys"}',
    N'Trusted root certs must use RSA 2048+ or ECC 256+. Weak trusted roots undermine the entire PKI chain.',
    1, 1, @systemUserId);

-- ---------- 2.c BITLOCKER engine (BL-0480..BL-0483) ----------

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0480')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0480', @catHwTrust,
    N'BitLocker - OS Drive Protection Status',
    'bitlocker', 'critical',
    N'{"checkType":"protection_status","drive":"C:","expected":"On","operator":"eq","display":"Reading BitLocker protection status for C:"}',
    N'OS drive (C:) must have BitLocker protection ON. Required by CIS 18.9.x, HIPAA 164.312(a)(2)(iv), PCI 3.4.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0481')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0481', @catHwTrust,
    N'BitLocker - Encryption Method',
    'bitlocker', 'high',
    N'{"checkType":"encryption_method","drive":"C:","expected":"XTS-AES","operator":"contains","display":"Reading BitLocker encryption algorithm"}',
    N'Encryption method should be XTS-AES 128 or 256 (not legacy AES-CBC). Set via Group Policy before enabling BitLocker.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0482')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0482', @catHwTrust,
    N'BitLocker - Encryption Percent Complete',
    'bitlocker', 'medium',
    N'{"checkType":"encryption_percent","drive":"C:","expected":100,"operator":"eq","display":"Reading BitLocker encryption progress"}',
    N'Drive should be 100% encrypted. Lower values mean encryption is in progress or was paused.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0483')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0483', @catHwTrust,
    N'BitLocker - Recovery Key Protector Present',
    'bitlocker', 'high',
    N'{"checkType":"recovery_key_present","drive":"C:","expected":true,"operator":"eq","display":"Verifying BitLocker recovery protector"}',
    N'At least one recovery protector (Numerical Password) must exist so IT can unlock the drive if TPM fails.',
    1, 1, @systemUserId);

-- ---------- 2.d TPM engine (BL-0484..BL-0486) ----------

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0484')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0484', @catHwTrust,
    N'TPM - Present',
    'tpm', 'critical',
    N'{"checkType":"present","expected":true,"operator":"eq","display":"Detecting TPM presence"}',
    N'A TPM is required for Windows 11, BitLocker with TPM protector, Windows Hello for Business, and Credential Guard.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0485')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0485', @catHwTrust,
    N'TPM - Version 2.0',
    'tpm', 'high',
    N'{"checkType":"version","expected":"2.0","operator":"eq","display":"Reading TPM spec version"}',
    N'TPM 2.0 is required for Windows 11 and recommended for all CIS L1 hardened endpoints. 1.2 is legacy.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='BL-0486')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('BL-0486', @catHwTrust,
    N'TPM - Ready State',
    'tpm', 'high',
    N'{"checkType":"ready_state","expected":"Ready","operator":"eq","display":"Reading TPM ready state"}',
    N'TPM must be Ready (initialized, owned, storage + attestation enabled). NotReady means provisioning incomplete.',
    1, 1, @systemUserId);

-- ============================================================
-- PART 3 -- Framework mappings
-- ============================================================

DECLARE @logBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @logBlock VALUES ('BL-0470'),('BL-0471'),('BL-0472'),('BL-0473');

DECLARE @certBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @certBlock VALUES ('BL-0474'),('BL-0475'),('BL-0476'),('BL-0477'),('BL-0478'),('BL-0479');

DECLARE @blkBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @blkBlock VALUES ('BL-0480'),('BL-0481'),('BL-0482'),('BL-0483');

DECLARE @tpmBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @tpmBlock VALUES ('BL-0484'),('BL-0485'),('BL-0486');

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

-- Cert block -> NIST (SC-12, SC-17), CIS, PCI (2.3), ISO
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST FROM control_defs cd JOIN @certBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwNIST);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS FROM control_defs cd JOIN @certBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwCIS);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwPCI FROM control_defs cd JOIN @certBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwPCI);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO FROM control_defs cd JOIN @certBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwISO);

-- BitLocker block -> CIS, NIST, HIPAA, PCI, ISO
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS FROM control_defs cd JOIN @blkBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwCIS);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST FROM control_defs cd JOIN @blkBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwNIST);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA FROM control_defs cd JOIN @blkBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwHIPAA);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwPCI FROM control_defs cd JOIN @blkBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwPCI);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO FROM control_defs cd JOIN @blkBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwISO);

-- TPM block -> CIS, NIST, HIPAA, ISO  (not PCI-specific)
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS FROM control_defs cd JOIN @tpmBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwCIS);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST FROM control_defs cd JOIN @tpmBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwNIST);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA FROM control_defs cd JOIN @tpmBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwHIPAA);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO FROM control_defs cd JOIN @tpmBlock b ON b.control_id=cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id=cd.id AND cf.framework_id=@fwISO);

-- ============================================================
-- PART 4 -- Platform scope (W10 + W11, Phase 1)
-- ============================================================

DECLARE @newBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @newBlock VALUES
    ('BL-0470'),('BL-0471'),('BL-0472'),('BL-0473'),
    ('BL-0474'),('BL-0475'),('BL-0476'),('BL-0477'),('BL-0478'),('BL-0479'),
    ('BL-0480'),('BL-0481'),('BL-0482'),('BL-0483'),
    ('BL-0484'),('BL-0485'),('BL-0486');

INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platW10
FROM control_defs cd
JOIN @newBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platW10
);

INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platW11
FROM control_defs cd
JOIN @newBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = @platW11
);

-- ============================================================
-- PART 5 -- Link to every existing assessment
-- ============================================================
-- New controls get auto-added to all currently active assessments
-- so live enrollment codes pick them up on the next agent run.
-- ============================================================
INSERT INTO assessment_controls (assessment_id, control_def_id)
SELECT a.id, cd.id
FROM assessments a
CROSS JOIN control_defs cd
JOIN @newBlock b ON b.control_id = cd.control_id
WHERE a.is_active = 1
  AND a.deleted_at IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM assessment_controls ac
      WHERE ac.assessment_id = a.id AND ac.control_def_id = cd.id
  );

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification (run manually after applying)
-- ============================================================
-- SELECT COUNT(*) AS total_active FROM control_defs WHERE is_active=1;
-- -- Expect previous total + 17 (was 630 -> now 647)
--
-- SELECT [type], COUNT(*) FROM control_defs WHERE is_active=1 GROUP BY [type] ORDER BY [type];
-- -- Expect rows for: eventlog=4, certstore=6, bitlocker=4, tpm=3 (plus existing types)
--
-- SELECT control_id, [type], LEFT(check_json, 80) AS preview
-- FROM control_defs WHERE control_id BETWEEN 'BL-0470' AND 'BL-0486' ORDER BY control_id;
--
-- SELECT p.code, COUNT(*) AS linked
-- FROM control_platforms cp JOIN platforms p ON p.id=cp.platform_id
-- JOIN control_defs cd ON cd.id=cp.control_def_id
-- WHERE cd.control_id BETWEEN 'BL-0470' AND 'BL-0486'
-- GROUP BY p.code;
-- -- Expect W10=17, W11=17
