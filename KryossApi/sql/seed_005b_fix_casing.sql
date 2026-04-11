SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;   -- any error -> full rollback
GO
-- ============================================================
-- seed_005b_fix_casing.sql
-- Kryoss Platform -- Fix snake_case -> camelCase in 25 HIPAA
-- refinement controls (BL-0445..BL-0469).
--
-- Background:
--   seed_005_controls_patch.sql inserted the 25 controls with
--   snake_case keys in check_json:
--     "check_type"        -> "checkType"
--     "service_name"      -> "serviceName"
--     "value_name"        -> "valueName"
--     "timeout_seconds"   -> "timeoutSeconds"
--     "expected_start_type" -> "expectedStartType"
--
--   The .NET agent (KryossAgent/Models/ControlDef.cs) deserializes
--   camelCase exclusively via [JsonPropertyName], so the 25 controls
--   would come through with null fields and fail silently.
--
-- Strategy:
--   In-place JSON REPLACE on control_defs.check_json for rows
--   matching control_id BETWEEN 'BL-0445' AND 'BL-0469'.
--   Keys NOT in the fix list (expected, operator, hive, path,
--   display, parent, optional) already match camelCase or are
--   single-word.
--
-- Idempotent:
--   Re-running is safe. REPLACE on a string that no longer contains
--   the snake_case key is a no-op. A final SELECT at the bottom
--   reports how many rows still contain any snake_case key (should
--   always be 0 after apply).
-- ============================================================

BEGIN TRANSACTION;

DECLARE @before INT;
SELECT @before = COUNT(*)
FROM control_defs
WHERE control_id BETWEEN 'BL-0445' AND 'BL-0469'
  AND (
        check_json LIKE '%"check_type"%'
     OR check_json LIKE '%"service_name"%'
     OR check_json LIKE '%"value_name"%'
     OR check_json LIKE '%"timeout_seconds"%'
     OR check_json LIKE '%"expected_start_type"%'
  );

PRINT CONCAT('Rows with snake_case keys before fix: ', @before);

UPDATE control_defs
SET check_json =
    REPLACE(
        REPLACE(
            REPLACE(
                REPLACE(
                    REPLACE(check_json,
                        '"check_type"',        '"checkType"'),
                    '"service_name"',          '"serviceName"'),
                '"value_name"',                '"valueName"'),
            '"timeout_seconds"',               '"timeoutSeconds"'),
        '"expected_start_type"',               '"expectedStartType"')
WHERE control_id BETWEEN 'BL-0445' AND 'BL-0469';

PRINT CONCAT('Rows updated: ', @@ROWCOUNT);

DECLARE @after INT;
SELECT @after = COUNT(*)
FROM control_defs
WHERE control_id BETWEEN 'BL-0445' AND 'BL-0469'
  AND (
        check_json LIKE '%"check_type"%'
     OR check_json LIKE '%"service_name"%'
     OR check_json LIKE '%"value_name"%'
     OR check_json LIKE '%"timeout_seconds"%'
     OR check_json LIKE '%"expected_start_type"%'
  );

PRINT CONCAT('Rows with snake_case keys after fix: ', @after);

IF @after > 0
BEGIN
    RAISERROR('Casing fix left %d rows still containing snake_case keys. Rolling back.', 16, 1, @after);
    ROLLBACK TRANSACTION;
    RETURN;
END

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification (read-only, commented)
-- ============================================================
-- 1) Sanity: pick one row and inspect
-- SELECT control_id, check_json
-- FROM control_defs
-- WHERE control_id = 'BL-0445';
-- -- expect: "checkType":"registry","hive":"HKLM",...,"valueName":"Enabled",...
--
-- 2) Count of refinement controls that still carry snake_case
-- SELECT COUNT(*) AS remaining_snake_case
-- FROM control_defs
-- WHERE control_id BETWEEN 'BL-0445' AND 'BL-0469'
--   AND (check_json LIKE '%"check_type"%'
--     OR check_json LIKE '%"service_name"%'
--     OR check_json LIKE '%"value_name"%'
--     OR check_json LIKE '%"timeout_seconds"%'
--     OR check_json LIKE '%"expected_start_type"%');
-- -- expect: 0
--
-- 3) Make sure camelCase arrived
-- SELECT
--     SUM(CASE WHEN check_json LIKE '%"checkType"%'      THEN 1 ELSE 0 END) AS has_checkType,
--     SUM(CASE WHEN check_json LIKE '%"serviceName"%'    THEN 1 ELSE 0 END) AS has_serviceName,
--     SUM(CASE WHEN check_json LIKE '%"valueName"%'      THEN 1 ELSE 0 END) AS has_valueName,
--     SUM(CASE WHEN check_json LIKE '%"timeoutSeconds"%' THEN 1 ELSE 0 END) AS has_timeoutSeconds
-- FROM control_defs
-- WHERE control_id BETWEEN 'BL-0445' AND 'BL-0469';
