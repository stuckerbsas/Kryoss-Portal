SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- 015_machine_platform_id.sql
-- Kryoss Platform -- Add platform_id to machines (Phase 1 scope)
--
-- Purpose:
--   Let the backend resolve machines.os_name -> platforms.id at
--   enrollment time and store it. Then ControlsFunction joins
--   control_platforms on this id to return the right subset of
--   the 630 active controls (W10/W11 today, servers empty).
--
-- Design principle: the agent stays dumb. It never sends a
-- platformCode. The backend parses os_name server-side via the
-- PlatformResolver service and writes platform_id here.
--
-- Idempotent:
--   IF NOT EXISTS guards on column, FK, index. Backfill uses
--   WHERE platform_id IS NULL so re-runs only touch new rows.
-- ============================================================

-- 1) Add column
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('machines') AND name = 'platform_id'
)
    ALTER TABLE machines ADD platform_id INT NULL;
GO

-- 2) FK to platforms(id)
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'fk_machines_platform')
    ALTER TABLE machines
        ADD CONSTRAINT fk_machines_platform
        FOREIGN KEY (platform_id) REFERENCES platforms(id);
GO

-- 3) Index for lookups (ControlsFunction hot path)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_machines_platform_id')
    CREATE INDEX ix_machines_platform_id ON machines(platform_id)
        WHERE platform_id IS NOT NULL;
GO

-- 4) One-time backfill of existing machines based on os_name
--    Mirrors the C# PlatformResolver rules exactly.
--    Only touches rows where platform_id IS NULL so it is idempotent.
BEGIN TRANSACTION;

DECLARE @resolved INT = 0;
DECLARE @unresolved INT = 0;

UPDATE m
SET m.platform_id = p.id
FROM machines m
INNER JOIN platforms p ON p.code =
    CASE
        WHEN m.os_name LIKE N'%Windows 11%'     THEN 'W11'
        WHEN m.os_name LIKE N'%Windows 10%'     THEN 'W10'
        WHEN m.os_name LIKE N'%Server 2025%'    THEN 'MS25'
        WHEN m.os_name LIKE N'%Server 2022%'    THEN 'MS22'
        WHEN m.os_name LIKE N'%Server 2019%'    THEN 'MS19'
        ELSE NULL
    END
WHERE m.platform_id IS NULL
  AND m.os_name IS NOT NULL;

SET @resolved = @@ROWCOUNT;

SELECT @unresolved = COUNT(*)
FROM machines
WHERE platform_id IS NULL;

PRINT CONCAT('Backfill resolved: ', @resolved, '  still NULL: ', @unresolved);

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification (read-only, commented)
-- ============================================================
-- 1) Column exists with correct type
-- SELECT c.name, t.name AS type_name, c.is_nullable
-- FROM sys.columns c
-- JOIN sys.types t ON t.user_type_id = c.user_type_id
-- WHERE c.object_id = OBJECT_ID('machines') AND c.name = 'platform_id';
-- -- expect: platform_id, int, 1
--
-- 2) FK in place
-- SELECT name FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('machines');
-- -- expect row: fk_machines_platform
--
-- 3) Resolution coverage
-- SELECT
--     SUM(CASE WHEN platform_id IS NULL     THEN 1 ELSE 0 END) AS unresolved,
--     SUM(CASE WHEN platform_id IS NOT NULL THEN 1 ELSE 0 END) AS resolved,
--     COUNT(*)                                                 AS total
-- FROM machines;
--
-- 4) Per-platform breakdown
-- SELECT p.code, COUNT(m.id) AS machines
-- FROM machines m
-- LEFT JOIN platforms p ON p.id = m.platform_id
-- GROUP BY p.code
-- ORDER BY machines DESC;
