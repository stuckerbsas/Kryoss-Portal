SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_007c_platform_scope_server.sql
-- Kryoss Platform -- Phase 1.5: Add server platform scope
--
-- Links all active controls to MS19, MS22, MS25 (member servers).
-- Same controls as workstation — engines work identically on Server.
-- DC platforms (DC19/DC22/DC25) remain unlinked until Phase 2
-- adds ProductType-based DC detection.
--
-- Idempotent: NOT EXISTS guards prevent duplicate rows.
-- Depends on: seed_002 (platforms), seed_004+ (controls)
-- ============================================================

BEGIN TRANSACTION;

DECLARE @platMS19 INT = (SELECT id FROM platforms WHERE code = 'MS19');
DECLARE @platMS22 INT = (SELECT id FROM platforms WHERE code = 'MS22');
DECLARE @platMS25 INT = (SELECT id FROM platforms WHERE code = 'MS25');

IF @platMS19 IS NULL OR @platMS22 IS NULL OR @platMS25 IS NULL
BEGIN
    RAISERROR('MS19, MS22, or MS25 platform missing from platforms table. Run seed_002 first.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

-- ------------------------------------------------------------
-- 1) Link every ACTIVE control to MS19
-- ------------------------------------------------------------
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platMS19
FROM control_defs cd
WHERE cd.is_active = 1
  AND NOT EXISTS (
      SELECT 1 FROM control_platforms cp
      WHERE cp.control_def_id = cd.id
        AND cp.platform_id = @platMS19
  );

DECLARE @ms19Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for MS19: ', @ms19Rows);

-- ------------------------------------------------------------
-- 2) Link every ACTIVE control to MS22
-- ------------------------------------------------------------
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platMS22
FROM control_defs cd
WHERE cd.is_active = 1
  AND NOT EXISTS (
      SELECT 1 FROM control_platforms cp
      WHERE cp.control_def_id = cd.id
        AND cp.platform_id = @platMS22
  );

DECLARE @ms22Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for MS22: ', @ms22Rows);

-- ------------------------------------------------------------
-- 3) Link every ACTIVE control to MS25
-- ------------------------------------------------------------
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platMS25
FROM control_defs cd
WHERE cd.is_active = 1
  AND NOT EXISTS (
      SELECT 1 FROM control_platforms cp
      WHERE cp.control_def_id = cd.id
        AND cp.platform_id = @platMS25
  );

DECLARE @ms25Rows INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for MS25: ', @ms25Rows);

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification
-- ============================================================
SELECT
    p.code,
    p.name,
    COUNT(cp.control_def_id) AS linked_controls
FROM platforms p
LEFT JOIN control_platforms cp ON cp.platform_id = p.id
LEFT JOIN control_defs cd      ON cd.id = cp.control_def_id AND cd.is_active = 1
GROUP BY p.id, p.code, p.name
ORDER BY p.code;
