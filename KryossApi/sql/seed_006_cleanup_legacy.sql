SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- ============================================================
-- seed_006_cleanup_legacy.sql
-- Kryoss Platform -- Remove legacy 91-control bootstrap seed
--
-- Context:
--   An early hand-curated seed inserted 91 control_defs with
--   3-digit IDs (BL-001..BL-581). That bootstrap was superseded
--   by seed_004_controls.sql which inserted 605 AST-extracted
--   controls using 4-digit IDs (BL-0001..BL-0444) covering the
--   same ground in more detail.
--
--   seed_005 added 25 more (BL-0445..BL-0469).
--
-- Goal:
--   Remove the 91 legacy controls so we don't run duplicate
--   checks and pollute scoring. After this script:
--     control_defs = 630  (161 SC + 469 BL in 4-digit format)
--     control_frameworks clean
--
-- Safety:
--   - Deletes from control_frameworks first (child rows)
--   - Deletes from control_results if any exist (should be empty)
--   - Deletes from control_defs last
--   - Wrapped in a transaction so any FK failure rolls back
--
-- Idempotent: re-running on a clean catalog is a no-op.
-- ============================================================

BEGIN TRANSACTION;

-- Stage the legacy control_def IDs in a temp table for reuse
DECLARE @legacy TABLE (id INT PRIMARY KEY);

INSERT INTO @legacy (id)
SELECT id
FROM control_defs
WHERE control_id LIKE 'BL-%'
  AND LEN(control_id) = 6;                -- 'BL-' + 3 digits

DECLARE @legacyCount INT = (SELECT COUNT(*) FROM @legacy);
PRINT CONCAT('Legacy controls found: ', @legacyCount);

-- 1) Delete control_frameworks mappings
DELETE cf
FROM control_frameworks cf
JOIN @legacy l ON cf.control_def_id = l.id;
PRINT CONCAT('control_frameworks rows deleted: ', @@ROWCOUNT);

-- 2) Delete control_results (if any assessment has already run)
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'control_results')
BEGIN
    DELETE cr
    FROM control_results cr
    JOIN @legacy l ON cr.control_def_id = l.id;
    PRINT CONCAT('control_results rows deleted: ', @@ROWCOUNT);
END

-- 3) Delete the legacy control_defs themselves
DELETE cd
FROM control_defs cd
JOIN @legacy l ON cd.id = l.id;
PRINT CONCAT('control_defs rows deleted: ', @@ROWCOUNT);

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification queries (run manually after)
-- ============================================================
-- SELECT COUNT(*) AS total_controls FROM control_defs;
-- -- expect 630 (161 SC + 469 BL)
--
-- SELECT
--     CASE WHEN control_id LIKE 'SC-%' THEN 'SC'
--          WHEN control_id LIKE 'BL-%' THEN 'BL' END AS prefix,
--     COUNT(*) AS total,
--     MIN(control_id) AS first_id,
--     MAX(control_id) AS last_id
-- FROM control_defs
-- GROUP BY CASE WHEN control_id LIKE 'SC-%' THEN 'SC'
--               WHEN control_id LIKE 'BL-%' THEN 'BL' END;
-- -- expect SC=161, BL=469, BL range BL-0001..BL-0469
--
-- SELECT f.code, COUNT(*) AS tagged
-- FROM control_frameworks cf
-- JOIN frameworks f ON cf.framework_id = f.id
-- GROUP BY f.code
-- ORDER BY tagged DESC;
-- -- expect CIS ~620, NIST ~630, HIPAA ~310, ISO ~162, PCI 9
