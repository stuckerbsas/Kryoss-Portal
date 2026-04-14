-- =============================================================================
-- Migration 026: Protocol Usage Audit (v1.5.1)
--
-- Adds the server-side toggle for NTLM/SMBv1 protocol usage auditing, plus
-- 12 new control definitions that read the resulting event logs and report
-- usage metrics. Feature: 90-day retention window, enterprise-grade.
--
-- Workflow:
-- 1. MSP toggles protocol_audit_enabled=1 via portal
-- 2. Next agent run (post-enroll) configures NTLM+SMB1 audit + sizes 3 event
--    logs to 500/300/300 MB via EventLogConfiguration native API
-- 3. 30-90 days later, new controls AUDIT-001..004 + NTLM-USE-* + SMB1-USE-*
--    + SAFE-TO-DISABLE-* report the findings in the portal
--
-- Idempotent: safe to run multiple times.
-- =============================================================================

SET NOCOUNT ON;

-- ── 1. Add organizations.protocol_audit_enabled column ──────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'protocol_audit_enabled'
               AND Object_ID = Object_ID('organizations'))
BEGIN
    ALTER TABLE organizations ADD protocol_audit_enabled BIT NOT NULL
        CONSTRAINT DF_organizations_protocol_audit_enabled DEFAULT(0);
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'protocol_audit_enabled_at'
               AND Object_ID = Object_ID('organizations'))
BEGIN
    ALTER TABLE organizations ADD protocol_audit_enabled_at DATETIME2 NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'protocol_audit_enabled_by'
               AND Object_ID = Object_ID('organizations'))
BEGIN
    ALTER TABLE organizations ADD protocol_audit_enabled_by NVARCHAR(255) NULL;
END

-- ── 2. Ensure category exists ──────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Protocol Usage Audit')
BEGIN
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Protocol Usage Audit', 9999, '00000000-0000-0000-0000-000000000000');
END

DECLARE @catId INT = (SELECT id FROM control_categories WHERE name = N'Protocol Usage Audit');
DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';

-- ── 3. Insert new controls (idempotent) ────────────────────────────────────

-- AUDIT-001: NTLM audit inbound enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'AUDIT-001')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AUDIT-001', @catId,
    N'NTLM inbound audit enabled',
    'registry', 'medium',
    N'{"checkType":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","valueName":"AuditReceivingNTLMTraffic","expected":2,"operator":"eq"}',
    N'Enable audit mode for inbound NTLM: set AuditReceivingNTLMTraffic=2 (all). Auto-configured when org has protocol_audit_enabled=1.',
    1, 1, @systemUserId);

-- AUDIT-002: NTLM outbound audit enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'AUDIT-002')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AUDIT-002', @catId,
    N'NTLM outbound audit enabled',
    'registry', 'medium',
    N'{"checkType":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa\\MSV1_0","valueName":"RestrictSendingNTLMTraffic","expected":1,"operator":"eq"}',
    N'Enable audit mode for outbound NTLM: set RestrictSendingNTLMTraffic=1 (audit all). Auto-configured when org has protocol_audit_enabled=1.',
    1, 1, @systemUserId);

-- AUDIT-003: SMB1 access audit enabled
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'AUDIT-003')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AUDIT-003', @catId,
    N'SMBv1 access audit enabled',
    'registry', 'medium',
    N'{"checkType":"registry","hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters","valueName":"AuditSmb1Access","expected":1,"operator":"eq"}',
    N'Enable audit mode for SMB1 access: set AuditSmb1Access=1. Auto-configured when org has protocol_audit_enabled=1.',
    1, 1, @systemUserId);

-- AUDIT-004: Security log sized for retention
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'AUDIT-004')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('AUDIT-004', @catId,
    N'Security log sized for 90-day retention (>= 500 MB)',
    'eventlog', 'low',
    N'{"checkType":"max_size","logName":"Security","expected":524288000,"operator":"gte"}',
    N'Grow Security log to >= 500 MB to retain 90 days of authentication events. Auto-configured when org has protocol_audit_enabled=1.',
    1, 1, @systemUserId);

-- NTLM-USE-001: NTLM outbound events (last 90 days)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'NTLM-USE-001')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NTLM-USE-001', @catId,
    N'NTLM outbound events (Event ID 8001, last 90 days)',
    'eventlog', 'high',
    N'{"checkType":"event_count","logName":"Microsoft-Windows-NTLM/Operational","eventIds":[8001],"days":90}',
    N'Count of outbound NTLM authentication attempts. Zero = safe to disable NTLM client.',
    1, 1, @systemUserId);

-- NTLM-USE-002: NTLM inbound events (last 90 days)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'NTLM-USE-002')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NTLM-USE-002', @catId,
    N'NTLM inbound events (Event IDs 8002/8003, last 90 days)',
    'eventlog', 'high',
    N'{"checkType":"event_count","logName":"Microsoft-Windows-NTLM/Operational","eventIds":[8002,8003],"days":90}',
    N'Count of inbound NTLM authentication attempts. Zero = safe to disable NTLM server.',
    1, 1, @systemUserId);

-- NTLM-USE-003: Top 10 NTLM callers (event payload analysis)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'NTLM-USE-003')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NTLM-USE-003', @catId,
    N'Top 10 NTLM callers (top source users)',
    'eventlog', 'medium',
    N'{"checkType":"event_top_sources","logName":"Microsoft-Windows-NTLM/Operational","eventIds":[8002,8003],"days":90,"topN":10,"payloadField":"UserName"}',
    N'Identifies which user accounts are using NTLM. Migrate to Kerberos or modern auth.',
    1, 1, @systemUserId);

-- NTLM-USE-004: Top 10 NTLM source workstations
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'NTLM-USE-004')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NTLM-USE-004', @catId,
    N'Top 10 NTLM source workstations',
    'eventlog', 'medium',
    N'{"checkType":"event_top_sources","logName":"Microsoft-Windows-NTLM/Operational","eventIds":[8002,8003],"days":90,"topN":10,"payloadField":"WorkstationName"}',
    N'Identifies which client machines are initiating NTLM. Update or retire legacy clients.',
    1, 1, @systemUserId);

-- SMB1-USE-001: SMB1 access events (last 90 days)
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'SMB1-USE-001')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SMB1-USE-001', @catId,
    N'SMBv1 access events (Event ID 3000, last 90 days)',
    'eventlog', 'high',
    N'{"checkType":"event_count","logName":"Microsoft-Windows-SMBServer/Audit","eventIds":[3000],"days":90}',
    N'Count of SMBv1 client connection attempts. Zero = safe to disable SMB1 server.',
    1, 1, @systemUserId);

-- SMB1-USE-002: Top 10 SMB1 client IPs
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'SMB1-USE-002')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SMB1-USE-002', @catId,
    N'Top 10 SMBv1 client IPs',
    'eventlog', 'medium',
    N'{"checkType":"event_top_sources","logName":"Microsoft-Windows-SMBServer/Audit","eventIds":[3000],"days":90,"topN":10,"payloadField":"ClientName"}',
    N'Identifies which clients are using SMBv1. Upgrade to SMBv2/v3 or retire.',
    1, 1, @systemUserId);

-- SAFE-TO-DISABLE-NTLM: Decision gate
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'SAFE-TO-DISABLE-NTLM')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SAFE-TO-DISABLE-NTLM', @catId,
    N'Safe to disable NTLM (zero usage in 90 days)',
    'eventlog', 'critical',
    N'{"checkType":"event_count","logName":"Microsoft-Windows-NTLM/Operational","eventIds":[8001,8002,8003,8004],"days":90,"expected":0,"operator":"eq"}',
    N'PASS when zero NTLM events in 90 days. Trigger remediation: disable NTLM via GPO.',
    1, 1, @systemUserId);

-- SAFE-TO-DISABLE-SMB1: Decision gate
IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id = 'SAFE-TO-DISABLE-SMB1')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('SAFE-TO-DISABLE-SMB1', @catId,
    N'Safe to disable SMBv1 (zero usage in 90 days)',
    'eventlog', 'critical',
    N'{"checkType":"event_count","logName":"Microsoft-Windows-SMBServer/Audit","eventIds":[3000],"days":90,"expected":0,"operator":"eq"}',
    N'PASS when zero SMB1 access events in 90 days. Trigger remediation: Disable-WindowsOptionalFeature -FeatureName SMB1Protocol.',
    1, 1, @systemUserId);

-- ── 4. Link controls to all active platforms (W10/W11/MS19/MS22/MS25) ──────

DECLARE @newControls TABLE (control_id VARCHAR(30));
INSERT INTO @newControls VALUES
    ('AUDIT-001'),('AUDIT-002'),('AUDIT-003'),('AUDIT-004'),
    ('NTLM-USE-001'),('NTLM-USE-002'),('NTLM-USE-003'),('NTLM-USE-004'),
    ('SMB1-USE-001'),('SMB1-USE-002'),
    ('SAFE-TO-DISABLE-NTLM'),('SAFE-TO-DISABLE-SMB1');

INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, p.id
FROM control_defs cd
CROSS JOIN platforms p
WHERE cd.control_id IN (SELECT control_id FROM @newControls)
  AND p.code IN ('W10','W11','MS19','MS22','MS25')
  AND NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = p.id
  );

-- ── 5. Tag with frameworks (NIST mainly) ───────────────────────────────────

DECLARE @nistId INT = (SELECT id FROM frameworks WHERE code = 'NIST');
IF @nistId IS NOT NULL
BEGIN
    INSERT INTO control_frameworks (control_def_id, framework_id)
    SELECT cd.id, @nistId
    FROM control_defs cd
    WHERE cd.control_id IN (SELECT control_id FROM @newControls)
      AND NOT EXISTS (
        SELECT 1 FROM control_frameworks cf
        WHERE cf.control_def_id = cd.id AND cf.framework_id = @nistId
      );
END

-- ── 6. Link to default assessments so existing orgs get them automatically ─

INSERT INTO assessment_controls (assessment_id, control_def_id)
SELECT a.id, cd.id
FROM assessments a
CROSS JOIN control_defs cd
WHERE a.is_default = 1 AND a.is_active = 1
  AND cd.control_id IN (SELECT control_id FROM @newControls)
  AND cd.is_active = 1
  AND NOT EXISTS (
    SELECT 1 FROM assessment_controls ac
    WHERE ac.assessment_id = a.id AND ac.control_def_id = cd.id
  );

PRINT 'Migration 026 complete: protocol_audit columns added + 12 new controls linked.';
