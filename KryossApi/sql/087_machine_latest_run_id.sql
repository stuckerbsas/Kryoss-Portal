-- 087: Add latest_run_id to machines for fast dashboard queries
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'latest_run_id')
    ALTER TABLE machines ADD latest_run_id UNIQUEIDENTIFIER NULL;

-- Backfill from assessment_runs (latest per machine)
UPDATE m
SET m.latest_run_id = sub.run_id
FROM machines m
INNER JOIN (
    SELECT r.machine_id, r.id AS run_id,
           ROW_NUMBER() OVER (PARTITION BY r.machine_id ORDER BY r.started_at DESC) AS rn
    FROM assessment_runs r
) sub ON sub.machine_id = m.id AND sub.rn = 1
WHERE m.latest_run_id IS NULL;

-- Index for dashboard join
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_machines_latest_run_id' AND object_id = OBJECT_ID('machines'))
    CREATE INDEX IX_machines_latest_run_id ON machines(latest_run_id) WHERE latest_run_id IS NOT NULL;
