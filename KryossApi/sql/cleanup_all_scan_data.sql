SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- cleanup_all_scan_data.sql
-- Wipes ALL machine/assessment data for a fresh start.
-- ============================================================

BEGIN TRANSACTION;

-- Disable hard-delete triggers
DISABLE TRIGGER ALL ON control_results;
DISABLE TRIGGER ALL ON run_framework_scores;
DISABLE TRIGGER ALL ON assessment_runs;
DISABLE TRIGGER ALL ON machine_snapshots;
DISABLE TRIGGER ALL ON machines;
DISABLE TRIGGER ALL ON machine_software;
PRINT 'Triggers disabled';

-- 1) Reset enrollment codes FIRST (FK to machines.id)
UPDATE enrollment_codes SET used_by = NULL, used_at = NULL, use_count = 0;
PRINT CONCAT('enrollment_codes reset: ', @@ROWCOUNT);

-- 2) Control results
DELETE FROM control_results;
PRINT CONCAT('control_results deleted: ', @@ROWCOUNT);

-- 3) Framework scores
DELETE FROM run_framework_scores;
PRINT CONCAT('run_framework_scores deleted: ', @@ROWCOUNT);

-- 4) Assessment runs
DELETE FROM assessment_runs;
PRINT CONCAT('assessment_runs deleted: ', @@ROWCOUNT);

-- 5) Machine snapshots
DELETE FROM machine_snapshots;
PRINT CONCAT('machine_snapshots deleted: ', @@ROWCOUNT);

-- 6) Machine software
DELETE FROM machine_software;
PRINT CONCAT('machine_software deleted: ', @@ROWCOUNT);

-- 7) Machines (now safe — no FKs pointing to it)
DELETE FROM machines;
PRINT CONCAT('machines deleted: ', @@ROWCOUNT);

-- Re-enable triggers
ENABLE TRIGGER ALL ON control_results;
ENABLE TRIGGER ALL ON run_framework_scores;
ENABLE TRIGGER ALL ON assessment_runs;
ENABLE TRIGGER ALL ON machine_snapshots;
ENABLE TRIGGER ALL ON machines;
ENABLE TRIGGER ALL ON machine_software;
PRINT 'Triggers re-enabled';

COMMIT TRANSACTION;
GO

-- Verify
SELECT 'machines' AS tbl, COUNT(*) AS cnt FROM machines
UNION ALL SELECT 'assessment_runs', COUNT(*) FROM assessment_runs
UNION ALL SELECT 'control_results', COUNT(*) FROM control_results
UNION ALL SELECT 'run_framework_scores', COUNT(*) FROM run_framework_scores;
