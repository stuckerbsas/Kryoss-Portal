-- 078c_reset_controls_for_normalization.sql
-- Nuclear wipe of control catalog + re-seed preparation
-- Safe: DB has no production scan data. All controls will be re-seeded.
-- Run this ONCE, then re-run all seeds in order, then 078b.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

PRINT '=== Step 0: Disable ALL triggers on affected tables ===';
DISABLE TRIGGER ALL ON control_results;
DISABLE TRIGGER ALL ON control_defs;
DISABLE TRIGGER ALL ON control_frameworks;
DISABLE TRIGGER ALL ON control_platforms;
DISABLE TRIGGER ALL ON assessment_controls;
DISABLE TRIGGER ALL ON run_framework_scores;
IF OBJECT_ID('remediation_actions', 'U') IS NOT NULL
    DISABLE TRIGGER ALL ON remediation_actions;
IF OBJECT_ID('remediation_tasks', 'U') IS NOT NULL
    DISABLE TRIGGER ALL ON remediation_tasks;
IF OBJECT_ID('org_auto_remediate', 'U') IS NOT NULL
    DISABLE TRIGGER ALL ON org_auto_remediate;
IF OBJECT_ID('dashboard_control_scores', 'U') IS NOT NULL
    DISABLE TRIGGER ALL ON dashboard_control_scores;
PRINT 'All triggers disabled';
GO

PRINT '=== Step 1: Wipe FK-dependent tables ===';

DELETE FROM control_check_params;
PRINT CONCAT('control_check_params: ', @@ROWCOUNT, ' rows deleted');

DELETE FROM control_results;
PRINT CONCAT('control_results: ', @@ROWCOUNT, ' rows deleted');

DELETE FROM run_framework_scores;
PRINT CONCAT('run_framework_scores: ', @@ROWCOUNT, ' rows deleted');

DELETE FROM assessment_controls;
PRINT CONCAT('assessment_controls: ', @@ROWCOUNT, ' rows deleted');

IF OBJECT_ID('org_auto_remediate', 'U') IS NOT NULL
BEGIN
    DELETE FROM org_auto_remediate;
    PRINT CONCAT('org_auto_remediate: ', @@ROWCOUNT, ' rows deleted');
END

IF OBJECT_ID('remediation_tasks', 'U') IS NOT NULL
BEGIN
    DELETE FROM remediation_tasks;
    PRINT CONCAT('remediation_tasks: ', @@ROWCOUNT, ' rows deleted');
END

IF OBJECT_ID('remediation_actions', 'U') IS NOT NULL
BEGIN
    DELETE FROM remediation_actions;
    PRINT CONCAT('remediation_actions: ', @@ROWCOUNT, ' rows deleted');
END

IF OBJECT_ID('dashboard_control_scores', 'U') IS NOT NULL
BEGIN
    DELETE FROM dashboard_control_scores;
    PRINT CONCAT('dashboard_control_scores: ', @@ROWCOUNT, ' rows deleted');
END

DELETE FROM control_frameworks;
PRINT CONCAT('control_frameworks: ', @@ROWCOUNT, ' rows deleted');

DELETE FROM control_platforms;
PRINT CONCAT('control_platforms: ', @@ROWCOUNT, ' rows deleted');
GO

PRINT '=== Step 2: Wipe control_defs ===';
DELETE FROM control_defs;
PRINT CONCAT('control_defs: ', @@ROWCOUNT, ' rows deleted');
GO

PRINT '=== Step 2b: Re-enable ALL triggers ===';
ENABLE TRIGGER ALL ON control_results;
ENABLE TRIGGER ALL ON control_defs;
ENABLE TRIGGER ALL ON control_frameworks;
ENABLE TRIGGER ALL ON control_platforms;
ENABLE TRIGGER ALL ON assessment_controls;
ENABLE TRIGGER ALL ON run_framework_scores;
IF OBJECT_ID('remediation_actions', 'U') IS NOT NULL
    ENABLE TRIGGER ALL ON remediation_actions;
IF OBJECT_ID('remediation_tasks', 'U') IS NOT NULL
    ENABLE TRIGGER ALL ON remediation_tasks;
IF OBJECT_ID('org_auto_remediate', 'U') IS NOT NULL
    ENABLE TRIGGER ALL ON org_auto_remediate;
IF OBJECT_ID('dashboard_control_scores', 'U') IS NOT NULL
    ENABLE TRIGGER ALL ON dashboard_control_scores;
PRINT 'All triggers re-enabled';
GO

PRINT '=== Step 3: Drop CHECK constraint ===';
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_ctrldef_type')
    ALTER TABLE control_defs DROP CONSTRAINT ck_ctrldef_type;
PRINT 'ck_ctrldef_type dropped (seeds will re-create)';
GO

PRINT '=== Step 4: Ensure check_json column exists ===';
IF COL_LENGTH('control_defs', 'check_json') IS NULL
BEGIN
    ALTER TABLE control_defs ADD check_json nvarchar(max);
    PRINT 'check_json column re-added';
END
ELSE
    PRINT 'check_json column already exists';
GO

PRINT '';
PRINT '=== DONE. Now run seeds in this order: ===';
PRINT '  1. seed_004_controls.sql';
PRINT '  2. seed_005_controls_patch.sql';
PRINT '  3. seed_005b_fix_casing.sql';
PRINT '  4. seed_006b_deactivate_legacy.sql';
PRINT '  5. seed_007_platform_scope_workstation.sql';
PRINT '  6. seed_007b_prune_inactive_platforms.sql';
PRINT '  7. seed_007c_platform_scope_server.sql';
PRINT '  8. seed_008_new_engine_controls.sql';
PRINT '  9. seed_010_server_controls.sql';
PRINT ' 10. 040_dc_platform_support.sql';
PRINT ' 11. seed_013_dc_controls.sql';
PRINT ' 12. seed_014_edge_browser_controls.sql';
PRINT ' 13. seed_015_user_settings.sql';
PRINT ' 14. 026_protocol_audit.sql';
PRINT ' 15. seed_011_antivirus_controls.sql';
PRINT ' 16. seed_012_baseline_gaps.sql';
PRINT ' 17. seed_042_network_controls.sql';
PRINT ' 18. 047_dc_controls_v2.sql';
PRINT ' 19. seed_066_remediation_actions.sql';
PRINT ' 20. 078b_normalize_check_json.sql  <-- parses check_json, drops column';
PRINT ' 21. seed_078_cpe_mappings.sql';
GO
