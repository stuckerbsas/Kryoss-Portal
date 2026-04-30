-- 99_finalize.sql
-- Run AFTER all seed scripts on a fresh install.
-- Parses JSON columns → normalized tables, drops temp columns, re-enables triggers.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ══════════════════════════════════════════════
-- 1. Parse check_json → control_check_params
-- ══════════════════════════════════════════════
PRINT '=== Normalizing check_json → control_check_params ===';

IF COL_LENGTH('control_defs', 'check_json') IS NOT NULL
   AND EXISTS (SELECT 1 FROM control_defs WHERE check_json IS NOT NULL AND ISJSON(check_json) = 1)
BEGIN
    DELETE FROM control_check_params;

    -- Scalar values (string, number, bool, null)
    INSERT INTO control_check_params (control_def_id, param_name, param_value)
    SELECT c.id, j.[key], j.[value]
    FROM control_defs c
    CROSS APPLY OPENJSON(c.check_json) j
    WHERE c.check_json IS NOT NULL
      AND ISJSON(c.check_json) = 1
      AND j.[type] IN (0, 1, 2, 3);

    PRINT CONCAT('  Scalar params: ', @@ROWCOUNT, ' rows');

    -- Nested values (arrays, objects) — store as JSON string
    INSERT INTO control_check_params (control_def_id, param_name, param_value)
    SELECT c.id, j.[key], j.[value]
    FROM control_defs c
    CROSS APPLY OPENJSON(c.check_json) j
    WHERE c.check_json IS NOT NULL
      AND ISJSON(c.check_json) = 1
      AND j.[type] IN (4, 5)
      AND NOT EXISTS (
          SELECT 1 FROM control_check_params cp
          WHERE cp.control_def_id = c.id
            AND cp.param_name = j.[key] COLLATE DATABASE_DEFAULT
      );

    PRINT CONCAT('  Nested params: ', @@ROWCOUNT, ' rows');

    -- Drop JSON column
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_ctrldef_json')
        ALTER TABLE control_defs DROP CONSTRAINT ck_ctrldef_json;
    ALTER TABLE control_defs DROP COLUMN check_json;
    PRINT '  Dropped check_json column';
END
ELSE
    PRINT '  check_json: nothing to normalize (column missing or empty)';
GO

-- ══════════════════════════════════════════════
-- 2. Parse params_template → remediation_action_params
-- ══════════════════════════════════════════════
PRINT '=== Normalizing params_template → remediation_action_params ===';

IF COL_LENGTH('remediation_actions', 'params_template') IS NOT NULL
   AND EXISTS (SELECT 1 FROM remediation_actions WHERE params_template IS NOT NULL AND ISJSON(params_template) = 1)
BEGIN
    DELETE FROM remediation_action_params;

    INSERT INTO remediation_action_params (remediation_action_id, param_name, param_value)
    SELECT a.id, j.[key], j.[value]
    FROM remediation_actions a
    CROSS APPLY OPENJSON(a.params_template) j
    WHERE a.params_template IS NOT NULL
      AND ISJSON(a.params_template) = 1;

    PRINT CONCAT('  Remediation params: ', @@ROWCOUNT, ' rows');

    ALTER TABLE remediation_actions DROP COLUMN params_template;
    PRINT '  Dropped params_template column';
END
ELSE
    PRINT '  params_template: nothing to normalize (column missing or empty)';
GO

-- ══════════════════════════════════════════════
-- 3. Drop any other stale JSON columns (from 078)
-- ══════════════════════════════════════════════
PRINT '=== Cleaning remaining JSON columns ===';

IF COL_LENGTH('cloud_assessment_shared_mailboxes', 'full_access_users') IS NOT NULL
    ALTER TABLE cloud_assessment_shared_mailboxes DROP COLUMN full_access_users;
IF COL_LENGTH('cloud_assessment_shared_mailboxes', 'send_as_users') IS NOT NULL
    ALTER TABLE cloud_assessment_shared_mailboxes DROP COLUMN send_as_users;
IF COL_LENGTH('cloud_assessment_alerts_sent', 'payload_json') IS NOT NULL
    ALTER TABLE cloud_assessment_alerts_sent DROP COLUMN payload_json;
GO

-- ══════════════════════════════════════════════
-- 4. Re-enable all triggers
-- ══════════════════════════════════════════════
PRINT '=== Re-enabling all triggers ===';

DECLARE @sql NVARCHAR(MAX) = '';
SELECT @sql = @sql + 'ENABLE TRIGGER ALL ON ' + QUOTENAME(t.name) + ';' + CHAR(10)
FROM sys.tables t
WHERE EXISTS (
    SELECT 1 FROM sys.triggers tr
    WHERE tr.parent_id = t.object_id AND tr.is_disabled = 1
);

IF LEN(@sql) > 0
    EXEC sp_executesql @sql;

PRINT 'All triggers re-enabled';
GO

-- ══════════════════════════════════════════════
-- 5. Final CHECK constraint (widest scope)
-- ══════════════════════════════════════════════
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_ctrldef_type')
    ALTER TABLE control_defs DROP CONSTRAINT ck_ctrldef_type;

ALTER TABLE control_defs ADD CONSTRAINT ck_ctrldef_type CHECK ([type] IN (
    'registry','secedit','auditpol','firewall','service','netaccount','command',
    'eventlog','certstore','bitlocker','tpm','network_diag','dc'
));
GO

PRINT '=== Finalization complete ===';
PRINT 'Next: run seed_078_cpe_mappings.sql, then check_catalog_health.sql';
GO
