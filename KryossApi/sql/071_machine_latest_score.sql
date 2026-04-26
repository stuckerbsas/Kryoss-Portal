-- 071: Denormalized latest score on machines table for fast list queries
-- Eliminates correlated subquery per machine in fleet list

ALTER TABLE machines ADD
    latest_score DECIMAL(5,2) NULL,
    latest_grade NVARCHAR(2) NULL,
    latest_scan_at DATETIME2 NULL;
GO

-- Backfill from existing runs
;WITH cte AS (
    SELECT machine_id, global_score, grade, started_at,
           ROW_NUMBER() OVER (PARTITION BY machine_id ORDER BY started_at DESC) AS rn
    FROM assessment_runs
)
UPDATE m
SET m.latest_score = c.global_score,
    m.latest_grade = c.grade,
    m.latest_scan_at = c.started_at
FROM machines m
JOIN cte c ON c.machine_id = m.id AND c.rn = 1;
