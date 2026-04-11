-- =============================================================================
-- diag_catalog_fails.sql — Catalog quality diagnostic
-- =============================================================================
-- Purpose: Extract every FAIL result from the most recent assessment run on a
-- given machine, joined against control_defs so a human can triage whether
-- each fail is a true positive (machine misconfigured) or a false positive
-- (control logic / agent collection / platform scoping is wrong).
--
-- Output: three result sets
--   1. Run metadata (which run we're analyzing + totals sanity check)
--   2. Per-engine fail histogram (where are the fails concentrated)
--   3. Per-control detail rows (the data to export to CSV and review)
--
-- How to run:
--   sqlcmd -S tcp:sql-kryoss.database.windows.net,1433 -d KryossDb ^
--          -U kryossadmin -P <password> ^
--          -i diag_catalog_fails.sql ^
--          -s "|" -W -o diag_fails.txt
--
-- Or from SSMS: open, F5, right-click results > Save As CSV.
-- =============================================================================

SET NOCOUNT ON;

-- --------------------------------------------------------------------------
-- PART 0 — locate the run to analyze
-- Takes the most recent completed run across ALL machines. If you want to
-- target a specific machine, uncomment the WHERE clause.
-- --------------------------------------------------------------------------
DECLARE @run_id UNIQUEIDENTIFIER;

SELECT TOP 1 @run_id = id
FROM assessment_runs
-- WHERE machine_id = '<your-machine-id-here>'
WHERE completed_at IS NOT NULL
ORDER BY completed_at DESC;

IF @run_id IS NULL
BEGIN
    RAISERROR('No completed assessment runs found.', 16, 1);
    RETURN;
END

-- --------------------------------------------------------------------------
-- RESULT SET 1 — Run metadata
-- --------------------------------------------------------------------------
SELECT
    'RUN_METADATA'          AS section,
    r.id                    AS run_id,
    m.hostname              AS hostname,
    m.os_name               AS os_name,
    p.code                  AS platform_code,
    a.name                  AS assessment_name,
    r.global_score          AS score,
    r.grade                 AS grade,
    r.pass_count,
    r.warn_count,
    r.fail_count,
    r.duration_ms,
    r.completed_at
FROM assessment_runs r
JOIN machines     m ON m.id = r.machine_id
LEFT JOIN platforms p ON p.id = m.platform_id
LEFT JOIN assessments a ON a.id = r.assessment_id
WHERE r.id = @run_id;

-- --------------------------------------------------------------------------
-- RESULT SET 2 — Fails by engine (where to focus the triage effort)
-- --------------------------------------------------------------------------
SELECT
    'FAILS_BY_ENGINE'                  AS section,
    cd.[type]                          AS engine,
    COUNT(*)                           AS fail_count,
    SUM(CASE WHEN cr.actual_value IS NULL THEN 1 ELSE 0 END) AS null_actual,
    SUM(CASE WHEN cr.actual_value LIKE 'ERROR:%' THEN 1 ELSE 0 END) AS error_actual,
    SUM(CASE WHEN cd.severity = 'critical' THEN 1 ELSE 0 END) AS critical,
    SUM(CASE WHEN cd.severity = 'high'     THEN 1 ELSE 0 END) AS high,
    SUM(CASE WHEN cd.severity = 'medium'   THEN 1 ELSE 0 END) AS medium,
    SUM(CASE WHEN cd.severity = 'low'      THEN 1 ELSE 0 END) AS low
FROM control_results cr
JOIN control_defs cd ON cd.id = cr.control_def_id
WHERE cr.run_id = @run_id
  AND cr.status = 'fail'
GROUP BY cd.[type]
ORDER BY fail_count DESC;

-- --------------------------------------------------------------------------
-- RESULT SET 3 — Per-control detail rows (export this to CSV)
-- Column order is designed to be readable left-to-right during triage.
-- --------------------------------------------------------------------------
SELECT
    cd.control_id                                           AS control_id,
    cd.[type]                                               AS engine,
    cd.severity                                             AS severity,
    cat.code                                                AS category,
    LEFT(cd.name, 120)                                      AS name,
    cr.actual_value                                         AS actual_value,
    -- Extract the comparison fields from check_json for fast review
    JSON_VALUE(cd.check_json, '$.expected')                 AS expected,
    JSON_VALUE(cd.check_json, '$.operator')                 AS operator,
    JSON_VALUE(cd.check_json, '$.missingBehavior')          AS missing_behavior,
    JSON_VALUE(cd.check_json, '$.hive')                     AS reg_hive,
    JSON_VALUE(cd.check_json, '$.path')                     AS reg_path,
    JSON_VALUE(cd.check_json, '$.valueName')                AS reg_value,
    JSON_VALUE(cd.check_json, '$.field')                    AS field,
    JSON_VALUE(cd.check_json, '$.subcategory')              AS subcategory,
    LEFT(cr.finding, 200)                                   AS finding,
    cr.score                                                AS score,
    cr.max_score                                            AS max_score,
    -- Your triage columns — leave blank, fill during review
    CAST('' AS VARCHAR(20))                                 AS verdict,   -- TP | FP-agent | FP-control | FP-catalog
    CAST('' AS VARCHAR(500))                                AS notes
FROM control_results cr
JOIN control_defs     cd  ON cd.id = cr.control_def_id
JOIN control_categories cat ON cat.id = cd.category_id
WHERE cr.run_id = @run_id
  AND cr.status = 'fail'
ORDER BY
    cd.[type],
    cd.severity DESC,
    cd.control_id;

-- --------------------------------------------------------------------------
-- RESULT SET 4 — BONUS: Warn rows (for reference, not part of core triage)
-- Useful because some WARN cases may really be FAIL with wrong threshold.
-- --------------------------------------------------------------------------
SELECT
    'WARN_SUMMARY'   AS section,
    cd.[type]        AS engine,
    COUNT(*)         AS warn_count
FROM control_results cr
JOIN control_defs cd ON cd.id = cr.control_def_id
WHERE cr.run_id = @run_id
  AND cr.status = 'warn'
GROUP BY cd.[type]
ORDER BY warn_count DESC;
