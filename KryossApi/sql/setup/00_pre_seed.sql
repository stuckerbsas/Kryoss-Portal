-- 00_pre_seed.sql
-- Run BEFORE seed scripts on a fresh install.
-- Disables delete-prevention triggers and adds temporary JSON columns
-- needed by legacy seed scripts.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ══════════════════════════════════════════════
-- Disable all delete-prevention triggers
-- ══════════════��═══════════════════════════════
PRINT '=== Disabling delete-prevention triggers ===';

DECLARE @sql NVARCHAR(MAX) = '';
SELECT @sql = @sql + 'DISABLE TRIGGER ALL ON ' + QUOTENAME(t.name) + ';' + CHAR(10)
FROM sys.tables t
WHERE EXISTS (
    SELECT 1 FROM sys.triggers tr
    WHERE tr.parent_id = t.object_id AND tr.is_disabled = 0
);

IF LEN(@sql) > 0
    EXEC sp_executesql @sql;

PRINT 'All triggers disabled';
GO

-- ════��═════════════════════════════════════════
-- Add temporary JSON columns for seeding
-- (078_db_normalization may have dropped them)
-- ═���════════════════════════════════════════════
PRINT '=== Adding temporary JSON columns for seeding ===';

IF COL_LENGTH('control_defs', 'check_json') IS NULL
BEGIN
    ALTER TABLE control_defs ADD check_json NVARCHAR(MAX);
    PRINT 'Added control_defs.check_json';
END
GO

IF COL_LENGTH('remediation_actions', 'params_template') IS NULL
BEGIN
    ALTER TABLE remediation_actions ADD params_template NVARCHAR(MAX);
    PRINT 'Added remediation_actions.params_template';
END
GO

-- Drop narrow CHECK constraint if exists (seeds will re-create with correct scope)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_ctrldef_type')
    ALTER TABLE control_defs DROP CONSTRAINT ck_ctrldef_type;
GO

PRINT '=== Pre-seed setup complete. Run seeds now. ===';
GO
