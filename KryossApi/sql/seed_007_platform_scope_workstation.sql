SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_007_platform_scope_workstation.sql
-- Kryoss Platform -- Phase 1 scope: Workstation only
--
-- Context:
--   seed_004 + seed_005 + seed_006b left us with 630 active
--   control_defs but control_platforms is empty, so the
--   collector has no way to know which controls apply to a
--   given machine type.
--
-- Goal (Phase 1):
--   Tag every ACTIVE control with Windows 10 + Windows 11 so
--   the agent running on workstations receives the full catalog.
--   Server (MS19/MS22/MS25) and DC (DC19/DC22/DC25) platforms
--   stay unlinked for Phase 1 -- they will be populated later
--   when the server report is designed.
--
-- Collector query pattern (backend should use this):
--
--   SELECT cd.* FROM control_defs cd
--   INNER JOIN control_platforms cp ON cp.control_def_id = cd.id
--   INNER JOIN platforms p          ON p.id = cp.platform_id
--   WHERE cd.is_active = 1
--     AND p.code IN ('W10','W11')        -- detected at agent runtime
--
-- Idempotent: NOT EXISTS guards prevent duplicate rows.
-- ============================================================

BEGIN TRANSACTION;

DECLARE @platW10 INT = (SELECT id FROM platforms WHERE code = 'W10');
DECLARE @platW11 INT = (SELECT id FROM platforms WHERE code = 'W11');

IF @platW10 IS NULL OR @platW11 IS NULL
BEGIN
    RAISERROR('W10 or W11 platform missing from platforms table. Run seed_002 first.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ------------------------------------------------------------
-- 1) Link every ACTIVE control to W10
-- ------------------------------------------------------------
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platW10
FROM control_defs cd
WHERE cd.is_active = 1
  AND NOT EXISTS (
      SELECT 1 FROM control_platforms cp
      WHERE cp.control_def_id = cd.id
        AND cp.platform_id = @platW10
  );

DECLARE @w10Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for W10: ', @w10Rows);

-- ------------------------------------------------------------
-- 2) Link every ACTIVE control to W11
-- ------------------------------------------------------------
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platW11
FROM control_defs cd
WHERE cd.is_active = 1
  AND NOT EXISTS (
      SELECT 1 FROM control_platforms cp
      WHERE cp.control_def_id = cd.id
        AND cp.platform_id = @platW11
  );

DECLARE @w11Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for W11: ', @w11Rows);

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification
-- ============================================================
-- Expect: W10=630, W11=630, all others 0 (Phase 1)
SELECT
    p.code,
    p.name,
    COUNT(cp.control_def_id) AS linked_controls
FROM platforms p
LEFT JOIN control_platforms cp ON cp.platform_id = p.id
LEFT JOIN control_defs cd      ON cd.id = cp.control_def_id AND cd.is_active = 1
GROUP BY p.id, p.code, p.name
ORDER BY p.code;

-- Sanity: any ACTIVE control not linked to any platform? (expect 0)
SELECT cd.control_id, cd.name
FROM control_defs cd
WHERE cd.is_active = 1
  AND NOT EXISTS (
      SELECT 1 FROM control_platforms cp
      WHERE cp.control_def_id = cd.id
  );

-- Simulation: "what would a W11 agent receive?" (expect 630)
SELECT COUNT(*) AS controls_for_w11_agent
FROM control_defs cd
INNER JOIN control_platforms cp ON cp.control_def_id = cd.id
INNER JOIN platforms p          ON p.id = cp.platform_id
WHERE cd.is_active = 1
  AND p.code = 'W11';
