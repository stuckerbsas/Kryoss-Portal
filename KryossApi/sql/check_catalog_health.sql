SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- ============================================================
-- check_catalog_health.sql
-- Read-only DB health check for the Kryoss control catalog
-- after applying: seed_004 + seed_005 + seed_006b
--
-- Run this and review each section. Expected values are noted
-- next to each query. No writes, safe to run anytime.
-- ============================================================

PRINT '============================================================';
PRINT ' 1. control_defs totals';
PRINT '============================================================';
-- Expect: total=721, active=630, inactive=91
SELECT
    COUNT(*)                                     AS total,
    SUM(CASE WHEN is_active=1 THEN 1 ELSE 0 END) AS active,
    SUM(CASE WHEN is_active=0 THEN 1 ELSE 0 END) AS inactive
FROM control_defs;

PRINT '';
PRINT '============================================================';
PRINT ' 2. Breakdown by prefix (active only)';
PRINT '============================================================';
-- Expect:
--   SC   161   SC-001    SC-161
--   BL   469   BL-0001   BL-0469
SELECT
    CASE
        WHEN control_id LIKE 'SC-%' THEN 'SC'
        WHEN control_id LIKE 'BL-%' THEN 'BL'
        ELSE 'OTHER'
    END AS prefix,
    COUNT(*) AS total,
    MIN(control_id) AS first_id,
    MAX(control_id) AS last_id
FROM control_defs
WHERE is_active = 1
GROUP BY
    CASE
        WHEN control_id LIKE 'SC-%' THEN 'SC'
        WHEN control_id LIKE 'BL-%' THEN 'BL'
        ELSE 'OTHER'
    END;

PRINT '';
PRINT '============================================================';
PRINT ' 3. Legacy 91 are ALL inactive (expect 0 rows)';
PRINT '============================================================';
SELECT control_id, name, is_active
FROM control_defs
WHERE LEN(control_id) = 6
  AND control_id LIKE 'BL-%'
  AND is_active = 1;

PRINT '';
PRINT '============================================================';
PRINT ' 4. HIPAA refinement controls exist (BL-0445..BL-0469)';
PRINT '============================================================';
-- Expect 25 rows, all active
SELECT control_id, name, [type], is_active
FROM control_defs
WHERE control_id BETWEEN 'BL-0445' AND 'BL-0469'
ORDER BY control_id;

PRINT '';
PRINT '============================================================';
PRINT ' 5. Framework coverage -- ACTIVE controls only';
PRINT '============================================================';
-- Expect roughly:
--   NIST   630   (100% -- every active control has NIST)
--   CIS    ~620
--   HIPAA  ~310
--   ISO    ~162
--   PCI      9
SELECT
    f.code,
    COUNT(*) AS tagged_active,
    CAST(100.0 * COUNT(*) /
         NULLIF((SELECT COUNT(*) FROM control_defs WHERE is_active=1), 0)
         AS DECIMAL(5,1)) AS pct_of_active
FROM control_frameworks cf
JOIN frameworks f   ON cf.framework_id = f.id
JOIN control_defs cd ON cd.id = cf.control_def_id
WHERE cd.is_active = 1
GROUP BY f.code
ORDER BY tagged_active DESC;

PRINT '';
PRINT '============================================================';
PRINT ' 6. Duplicate control_id check (expect 0 rows)';
PRINT '============================================================';
SELECT control_id, COUNT(*) AS copies
FROM control_defs
GROUP BY control_id
HAVING COUNT(*) > 1;

PRINT '';
PRINT '============================================================';
PRINT ' 7. Orphan control_frameworks (expect 0 rows)';
PRINT '============================================================';
SELECT cf.control_def_id, cf.framework_id
FROM control_frameworks cf
LEFT JOIN control_defs cd ON cd.id = cf.control_def_id
WHERE cd.id IS NULL;

PRINT '';
PRINT '============================================================';
PRINT ' 8. Duplicate (control_def_id, framework_id) (expect 0)';
PRINT '============================================================';
SELECT control_def_id, framework_id, COUNT(*) AS dupes
FROM control_frameworks
GROUP BY control_def_id, framework_id
HAVING COUNT(*) > 1;

PRINT '';
PRINT '============================================================';
PRINT ' 9. Required categories exist';
PRINT '============================================================';
-- Expect 3 rows
SELECT id, name
FROM control_categories
WHERE name IN (
    N'Credential Protection',
    N'Audit, Logging And Monitoring',
    N'Backup And Recovery'
)
ORDER BY name;

PRINT '';
PRINT '============================================================';
PRINT '10. Active controls distribution by [type] engine';
PRINT '============================================================';
-- Sanity: every engine the agent must implement
SELECT [type], COUNT(*) AS total
FROM control_defs
WHERE is_active = 1
GROUP BY [type]
ORDER BY total DESC;

PRINT '';
PRINT '============================================================';
PRINT '11. Active controls distribution by severity';
PRINT '============================================================';
SELECT severity, COUNT(*) AS total
FROM control_defs
WHERE is_active = 1
GROUP BY severity
ORDER BY
    CASE severity
        WHEN 'critical' THEN 1
        WHEN 'high'     THEN 2
        WHEN 'medium'   THEN 3
        WHEN 'low'      THEN 4
        ELSE 5
    END;

PRINT '';
PRINT '============================================================';
PRINT '12. Active controls WITHOUT any framework tag (expect 0)';
PRINT '============================================================';
SELECT cd.control_id, cd.name
FROM control_defs cd
WHERE cd.is_active = 1
  AND NOT EXISTS (
      SELECT 1 FROM control_frameworks cf
      WHERE cf.control_def_id = cd.id
  )
ORDER BY cd.control_id;

PRINT '';
PRINT '============================================================';
PRINT '13. Active controls WITHOUT a category (expect 0)';
PRINT '============================================================';
SELECT cd.control_id, cd.name
FROM control_defs cd
WHERE cd.is_active = 1
  AND cd.category_id IS NULL;

PRINT '';
PRINT '============================================================';
PRINT '14. Frameworks registered';
PRINT '============================================================';
-- Expect at minimum: CIS, NIST, HIPAA, ISO27001, PCI-DSS
SELECT id, code, name
FROM frameworks
ORDER BY code;

PRINT '';
PRINT '============================================================';
PRINT ' DONE -- review each section above against the expected values';
PRINT '============================================================';
