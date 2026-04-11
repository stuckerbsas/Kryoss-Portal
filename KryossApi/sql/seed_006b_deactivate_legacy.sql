SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;   -- any error -> full rollback
GO
-- ============================================================
-- seed_006b_deactivate_legacy.sql
-- Kryoss Platform -- Soft-delete legacy 91-control bootstrap
--
-- Replaces seed_006_cleanup_legacy.sql (which failed on the
-- assessment_controls FK and committed partial deletes).
--
-- Strategy: do NOT delete the 91 legacy control_defs; instead
-- flip is_active = 0 so the collector skips them and reports
-- exclude them, while preserving every FK (assessment_controls,
-- control_results, etc.).
--
-- Legacy ID shape: 'BL-' + 3 digits  (LEN = 6)
--   Example: BL-001, BL-200, BL-581
-- Current catalog: 'BL-' + 4 digits  (LEN = 7)
--   Example: BL-0001..BL-0469
--
-- Idempotent: re-running is a no-op.
-- ============================================================

BEGIN TRANSACTION;

DECLARE @toDeactivate INT;

SELECT @toDeactivate = COUNT(*)
FROM control_defs
WHERE control_id LIKE 'BL-%'
  AND LEN(control_id) = 6
  AND is_active = 1;

PRINT CONCAT('Legacy controls to deactivate: ', @toDeactivate);

UPDATE control_defs
SET is_active = 0
WHERE control_id LIKE 'BL-%'
  AND LEN(control_id) = 6
  AND is_active = 1;

PRINT CONCAT('Legacy controls deactivated: ', @@ROWCOUNT);

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification
-- ============================================================
-- 1) Total and active counts
-- SELECT
--     COUNT(*)                                  AS total,
--     SUM(CASE WHEN is_active=1 THEN 1 ELSE 0 END) AS active,
--     SUM(CASE WHEN is_active=0 THEN 1 ELSE 0 END) AS inactive
-- FROM control_defs;
-- -- expect total=721, active=630, inactive=91
--
-- 2) Framework coverage -- ACTIVE only (this is what reports use)
-- SELECT f.code, COUNT(*) AS tagged_active
-- FROM control_frameworks cf
-- JOIN frameworks f ON cf.framework_id = f.id
-- JOIN control_defs cd ON cd.id = cf.control_def_id
-- WHERE cd.is_active = 1
-- GROUP BY f.code
-- ORDER BY tagged_active DESC;
-- -- expect NIST ~630, CIS ~620, HIPAA ~310, ISO ~162, PCI 9
--
-- 3) Confirm no active legacy remains
-- SELECT control_id, name
-- FROM control_defs
-- WHERE LEN(control_id) = 6 AND is_active = 1;
-- -- expect 0 rows
