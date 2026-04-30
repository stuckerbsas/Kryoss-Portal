-- 081_restore_fk_constraints.sql
-- Re-add FK constraints dropped during TRUNCATE session.
-- Safe to run multiple times — checks IF NOT EXISTS via sys.foreign_keys.

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_control_results_assessment_runs')
    ALTER TABLE control_results
        ADD CONSTRAINT FK_control_results_assessment_runs
        FOREIGN KEY (run_id) REFERENCES assessment_runs(id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_run_framework_scores_assessment_runs')
    ALTER TABLE run_framework_scores
        ADD CONSTRAINT FK_run_framework_scores_assessment_runs
        FOREIGN KEY (run_id) REFERENCES assessment_runs(id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_machine_network_diag_assessment_runs')
    ALTER TABLE machine_network_diag
        ADD CONSTRAINT FK_machine_network_diag_assessment_runs
        FOREIGN KEY (run_id) REFERENCES assessment_runs(id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_machine_snapshots_assessment_runs')
    ALTER TABLE machine_snapshots
        ADD CONSTRAINT FK_machine_snapshots_assessment_runs
        FOREIGN KEY (assessment_run_id) REFERENCES assessment_runs(id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_machine_network_latency_diag')
    ALTER TABLE machine_network_latency
        ADD CONSTRAINT FK_machine_network_latency_diag
        FOREIGN KEY (diag_id) REFERENCES machine_network_diag(id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_machine_network_routes_diag')
    ALTER TABLE machine_network_routes
        ADD CONSTRAINT FK_machine_network_routes_diag
        FOREIGN KEY (diag_id) REFERENCES machine_network_diag(id);
GO

-- Verify
SELECT name, OBJECT_NAME(parent_object_id) AS [table], OBJECT_NAME(referenced_object_id) AS [references]
FROM sys.foreign_keys
WHERE name IN (
    'FK_control_results_assessment_runs',
    'FK_run_framework_scores_assessment_runs',
    'FK_machine_network_diag_assessment_runs',
    'FK_machine_snapshots_assessment_runs',
    'FK_machine_network_latency_diag',
    'FK_machine_network_routes_diag'
);
GO

PRINT '=== FK constraints restored ===';
GO
