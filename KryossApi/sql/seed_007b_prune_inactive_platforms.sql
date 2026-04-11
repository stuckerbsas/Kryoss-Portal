SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_007b_prune_inactive_platforms.sql
-- Kryoss Platform -- Clean control_platforms of inactive links
--
-- Context:
--   seed_100_test_data.sql linked every control_def to W10/W11
--   using `deleted_at IS NULL` instead of `is_active = 1`. That
--   left the 91 legacy (soft-deleted) controls wired to the
--   workstation platforms, so the collector would still pull
--   them when the backend joins control_platforms.
--
-- Goal:
--   Remove any control_platforms row pointing to an inactive
--   control_def. Expected deletion: 91 legacy x 2 platforms
--   = 182 rows. Final state: 630 controls linked to W10 and
--   630 linked to W11.
--
-- Idempotent: re-running is a no-op.
-- ============================================================

BEGIN TRANSACTION;

DECLARE @beforeRows INT = (SELECT COUNT(*) FROM control_platforms);
PRINT CONCAT('control_platforms rows BEFORE: ', @beforeRows);

DELETE cp
FROM control_platforms cp
INNER JOIN control_defs cd ON cd.id = cp.control_def_id
WHERE cd.is_active = 0;

PRINT CONCAT('Rows deleted (inactive control mappings): ', @@ROWCOUNT);

DECLARE @afterRows INT = (SELECT COUNT(*) FROM control_platforms);
PRINT CONCAT('control_platforms rows AFTER: ', @afterRows);

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification (correct version -- filters by is_active in the
-- join predicate using INNER JOIN, not LEFT JOIN)
-- ============================================================
SELECT
    p.code,
    p.name,
    COUNT(cd.id) AS linked_active_controls
FROM platforms p
LEFT JOIN control_platforms cp ON cp.platform_id = p.id
LEFT JOIN control_defs cd      ON cd.id = cp.control_def_id
                              AND cd.is_active = 1
GROUP BY p.id, p.code, p.name
ORDER BY p.code;
-- Expect:
--   W10  630
--   W11  630
--   MS19   0
--   MS22   0
--   MS25   0
--   DC19   0
--   DC22   0
--   DC25   0
