-- 078b_normalize_check_json.sql
-- Parse check_json → control_check_params, then drop check_json column
-- Run AFTER seed_004 + seed_005 + all control seeds are applied
-- Idempotent: re-adds check_json if previously dropped, safe to re-run

-- Step 0: Re-add check_json if it was already dropped (idempotent recovery)
IF COL_LENGTH('control_defs', 'check_json') IS NULL
BEGIN
    ALTER TABLE control_defs ADD check_json nvarchar(max);
    PRINT 'Re-added check_json column (was previously dropped). Re-run seeds then re-run this script.';
END
GO

-- Step 1: Parse check_json → control_check_params
IF EXISTS (SELECT 1 FROM control_check_params)
BEGIN
    PRINT 'control_check_params already populated, skipping parse';
END
ELSE IF NOT EXISTS (SELECT 1 FROM control_defs WHERE check_json IS NOT NULL)
BEGIN
    PRINT 'check_json column is empty — run seed_004 + control seeds first, then re-run this script.';
END
ELSE
BEGIN
    INSERT INTO control_check_params (control_def_id, param_name, param_value)
    SELECT
        c.id,
        j.[key],
        j.[value]
    FROM control_defs c
    CROSS APPLY OPENJSON(c.check_json) j
    WHERE c.check_json IS NOT NULL
      AND ISJSON(c.check_json) = 1
      AND j.[type] IN (0, 1, 2, 3);

    PRINT CONCAT('control_check_params scalar values: ', @@ROWCOUNT, ' rows');

    INSERT INTO control_check_params (control_def_id, param_name, param_value)
    SELECT
        c.id,
        j.[key],
        j.[value]
    FROM control_defs c
    CROSS APPLY OPENJSON(c.check_json) j
    WHERE c.check_json IS NOT NULL
      AND ISJSON(c.check_json) = 1
      AND j.[type] IN (4, 5)
      AND NOT EXISTS (
          SELECT 1 FROM control_check_params cp
          WHERE cp.control_def_id = c.id AND cp.param_name = j.[key] COLLATE DATABASE_DEFAULT
      );

    PRINT CONCAT('control_check_params nested values: ', @@ROWCOUNT, ' rows');
END
GO

-- Step 2: Drop check_json column now that data is normalized
-- Only drops if control_check_params has data (prevents accidental data loss)
IF COL_LENGTH('control_defs', 'check_json') IS NOT NULL
   AND EXISTS (SELECT 1 FROM control_check_params)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_ctrldef_json')
        ALTER TABLE control_defs DROP CONSTRAINT ck_ctrldef_json;

    ALTER TABLE control_defs DROP COLUMN check_json;
    PRINT 'Dropped control_defs.check_json column';
END
ELSE IF COL_LENGTH('control_defs', 'check_json') IS NOT NULL
BEGIN
    PRINT 'check_json column kept — control_check_params is empty, data not yet parsed.';
END
GO
