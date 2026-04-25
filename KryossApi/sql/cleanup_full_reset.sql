SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- cleanup_full_reset.sql
-- Nuclear wipe: ALL scan/machine/assessment/cloud/network data.
-- Keeps: control_defs, frameworks, platforms, orgs, franchises,
--        enrollment_codes (reset usage), users, roles, permissions.
-- For pre-production fresh start ONLY.
-- ============================================================

BEGIN TRANSACTION;

-- Disable triggers on tables that have them
IF OBJECT_ID('control_results') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('control_results'))
    DISABLE TRIGGER ALL ON control_results;
IF OBJECT_ID('run_framework_scores') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('run_framework_scores'))
    DISABLE TRIGGER ALL ON run_framework_scores;
IF OBJECT_ID('assessment_runs') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('assessment_runs'))
    DISABLE TRIGGER ALL ON assessment_runs;
IF OBJECT_ID('machine_snapshots') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('machine_snapshots'))
    DISABLE TRIGGER ALL ON machine_snapshots;
IF OBJECT_ID('machines') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('machines'))
    DISABLE TRIGGER ALL ON machines;
IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = 'trg_machines_prevent_delete')
    DISABLE TRIGGER trg_machines_prevent_delete ON machines;
IF OBJECT_ID('machine_software') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('machine_software'))
    DISABLE TRIGGER ALL ON machine_software;
IF OBJECT_ID('assessment_controls') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('assessment_controls'))
    DISABLE TRIGGER ALL ON assessment_controls;
IF OBJECT_ID('assessments') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('assessments'))
    DISABLE TRIGGER ALL ON assessments;
PRINT '=== Triggers disabled ===';

-- ============================================================
-- 1) Assessment & scoring
-- ============================================================
DELETE FROM control_results;
PRINT CONCAT('control_results: ', @@ROWCOUNT);

DELETE FROM run_framework_scores;
PRINT CONCAT('run_framework_scores: ', @@ROWCOUNT);

DELETE FROM assessment_runs;
PRINT CONCAT('assessment_runs: ', @@ROWCOUNT);

-- ============================================================
-- 2) Machine child tables
-- ============================================================
DELETE FROM machine_snapshots;
PRINT CONCAT('machine_snapshots: ', @@ROWCOUNT);

DELETE FROM machine_software;
PRINT CONCAT('machine_software: ', @@ROWCOUNT);

IF OBJECT_ID('machine_disks') IS NOT NULL
BEGIN DELETE FROM machine_disks; PRINT CONCAT('machine_disks: ', @@ROWCOUNT); END

IF OBJECT_ID('machine_ports') IS NOT NULL
BEGIN DELETE FROM machine_ports; PRINT CONCAT('machine_ports: ', @@ROWCOUNT); END

IF OBJECT_ID('machine_network_diag') IS NOT NULL
BEGIN DELETE FROM machine_network_diag; PRINT CONCAT('machine_network_diag: ', @@ROWCOUNT); END

IF OBJECT_ID('machine_network_latency') IS NOT NULL
BEGIN DELETE FROM machine_network_latency; PRINT CONCAT('machine_network_latency: ', @@ROWCOUNT); END

IF OBJECT_ID('machine_network_routes') IS NOT NULL
BEGIN DELETE FROM machine_network_routes; PRINT CONCAT('machine_network_routes: ', @@ROWCOUNT); END

IF OBJECT_ID('machine_public_ip_history') IS NOT NULL
BEGIN DELETE FROM machine_public_ip_history; PRINT CONCAT('machine_public_ip_history: ', @@ROWCOUNT); END

-- ============================================================
-- 3) AD Hygiene
-- ============================================================
IF OBJECT_ID('ad_hygiene_findings') IS NOT NULL
BEGIN DELETE FROM ad_hygiene_findings; PRINT CONCAT('ad_hygiene_findings: ', @@ROWCOUNT); END

IF OBJECT_ID('ad_hygiene_scans') IS NOT NULL
BEGIN DELETE FROM ad_hygiene_scans; PRINT CONCAT('ad_hygiene_scans: ', @@ROWCOUNT); END

-- ============================================================
-- 4) Network sites
-- ============================================================
IF OBJECT_ID('network_sites') IS NOT NULL
BEGIN DELETE FROM network_sites; PRINT CONCAT('network_sites: ', @@ROWCOUNT); END

-- ============================================================
-- 5) Cloud Assessment
-- ============================================================
IF OBJECT_ID('cloud_assessment_findings') IS NOT NULL
BEGIN DELETE FROM cloud_assessment_findings; PRINT CONCAT('cloud_assessment_findings: ', @@ROWCOUNT); END

IF OBJECT_ID('cloud_assessment_scans') IS NOT NULL
BEGIN DELETE FROM cloud_assessment_scans; PRINT CONCAT('cloud_assessment_scans: ', @@ROWCOUNT); END

IF OBJECT_ID('cloud_assessment_azure_resources') IS NOT NULL
BEGIN DELETE FROM cloud_assessment_azure_resources; PRINT CONCAT('cloud_assessment_azure_resources: ', @@ROWCOUNT); END

IF OBJECT_ID('cloud_assessment_azure_subscriptions') IS NOT NULL
BEGIN DELETE FROM cloud_assessment_azure_subscriptions; PRINT CONCAT('cloud_assessment_azure_subscriptions: ', @@ROWCOUNT); END

IF OBJECT_ID('cloud_assessment_benchmark_scores') IS NOT NULL
BEGIN DELETE FROM cloud_assessment_benchmark_scores; PRINT CONCAT('cloud_assessment_benchmark_scores: ', @@ROWCOUNT); END

IF OBJECT_ID('cloud_assessment_benchmark_franchise') IS NOT NULL
BEGIN DELETE FROM cloud_assessment_benchmark_franchise; PRINT CONCAT('cloud_assessment_benchmark_franchise: ', @@ROWCOUNT); END

IF OBJECT_ID('cloud_assessment_benchmark_global') IS NOT NULL
BEGIN DELETE FROM cloud_assessment_benchmark_global; PRINT CONCAT('cloud_assessment_benchmark_global: ', @@ROWCOUNT); END

IF OBJECT_ID('cloud_assessment_benchmark_industry') IS NOT NULL
BEGIN DELETE FROM cloud_assessment_benchmark_industry; PRINT CONCAT('cloud_assessment_benchmark_industry: ', @@ROWCOUNT); END

-- ============================================================
-- 6) M365 (legacy)
-- ============================================================
IF OBJECT_ID('m365_findings') IS NOT NULL
BEGIN DELETE FROM m365_findings; PRINT CONCAT('m365_findings: ', @@ROWCOUNT); END

IF OBJECT_ID('m365_tenants') IS NOT NULL
BEGIN DELETE FROM m365_tenants; PRINT CONCAT('m365_tenants: ', @@ROWCOUNT); END

-- ============================================================
-- 7) Infrastructure Assessment
-- ============================================================
IF OBJECT_ID('infra_assessment_findings') IS NOT NULL
BEGIN DELETE FROM infra_assessment_findings; PRINT CONCAT('infra_assessment_findings: ', @@ROWCOUNT); END

IF OBJECT_ID('infra_assessment_capacity') IS NOT NULL
BEGIN DELETE FROM infra_assessment_capacity; PRINT CONCAT('infra_assessment_capacity: ', @@ROWCOUNT); END

IF OBJECT_ID('infra_assessment_connectivity') IS NOT NULL
BEGIN DELETE FROM infra_assessment_connectivity; PRINT CONCAT('infra_assessment_connectivity: ', @@ROWCOUNT); END

IF OBJECT_ID('infra_assessment_devices') IS NOT NULL
BEGIN DELETE FROM infra_assessment_devices; PRINT CONCAT('infra_assessment_devices: ', @@ROWCOUNT); END

IF OBJECT_ID('infra_assessment_sites') IS NOT NULL
BEGIN DELETE FROM infra_assessment_sites; PRINT CONCAT('infra_assessment_sites: ', @@ROWCOUNT); END

IF OBJECT_ID('infra_assessment_scans') IS NOT NULL
BEGIN DELETE FROM infra_assessment_scans; PRINT CONCAT('infra_assessment_scans: ', @@ROWCOUNT); END

IF OBJECT_ID('infra_hypervisor_configs') IS NOT NULL
BEGIN DELETE FROM infra_hypervisor_configs; PRINT CONCAT('infra_hypervisor_configs: ', @@ROWCOUNT); END

IF OBJECT_ID('infra_vms') IS NOT NULL
BEGIN DELETE FROM infra_vms; PRINT CONCAT('infra_vms: ', @@ROWCOUNT); END

IF OBJECT_ID('infra_hypervisors') IS NOT NULL
BEGIN DELETE FROM infra_hypervisors; PRINT CONCAT('infra_hypervisors: ', @@ROWCOUNT); END

-- ============================================================
-- 8) External scans & threats
-- ============================================================
IF OBJECT_ID('external_scan_findings') IS NOT NULL
BEGIN DELETE FROM external_scan_findings; PRINT CONCAT('external_scan_findings: ', @@ROWCOUNT); END

IF OBJECT_ID('external_scans') IS NOT NULL
BEGIN DELETE FROM external_scans; PRINT CONCAT('external_scans: ', @@ROWCOUNT); END

IF OBJECT_ID('machine_threats') IS NOT NULL
BEGIN DELETE FROM machine_threats; PRINT CONCAT('machine_threats: ', @@ROWCOUNT); END

-- ============================================================
-- 9) SNMP
-- ============================================================
IF OBJECT_ID('snmp_devices') IS NOT NULL
BEGIN DELETE FROM snmp_devices; PRINT CONCAT('snmp_devices: ', @@ROWCOUNT); END

IF OBJECT_ID('snmp_configs') IS NOT NULL
BEGIN DELETE FROM snmp_configs; PRINT CONCAT('snmp_configs: ', @@ROWCOUNT); END

-- ============================================================
-- 10) Protocol audit
-- ============================================================
IF OBJECT_ID('protocol_audit_results') IS NOT NULL
BEGIN DELETE FROM protocol_audit_results; PRINT CONCAT('protocol_audit_results: ', @@ROWCOUNT); END

-- ============================================================
-- 11) Enrollment codes — reset usage (keep codes themselves)
-- ============================================================
UPDATE enrollment_codes SET used_by = NULL, used_at = NULL, use_count = 0, assessment_id = NULL;
PRINT CONCAT('enrollment_codes reset: ', @@ROWCOUNT);

-- (api_key is a column on machines, not a separate table — cleared when machines are deleted)

-- ============================================================
-- 13) Assessments (org-level assessment definitions)
-- ============================================================
DELETE FROM assessment_controls;
PRINT CONCAT('assessment_controls: ', @@ROWCOUNT);

DELETE FROM assessments;
PRINT CONCAT('assessments: ', @@ROWCOUNT);

-- ============================================================
-- 14) Machines (now safe — all FKs cleared above)
-- ============================================================
DELETE FROM machines;
PRINT CONCAT('machines: ', @@ROWCOUNT);

-- ============================================================
-- 15) Actlog (optional — comment out to keep audit trail)
-- ============================================================
DELETE FROM actlog;
PRINT CONCAT('actlog: ', @@ROWCOUNT);

-- Re-enable triggers
IF OBJECT_ID('control_results') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('control_results'))
    ENABLE TRIGGER ALL ON control_results;
IF OBJECT_ID('run_framework_scores') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('run_framework_scores'))
    ENABLE TRIGGER ALL ON run_framework_scores;
IF OBJECT_ID('assessment_runs') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('assessment_runs'))
    ENABLE TRIGGER ALL ON assessment_runs;
IF OBJECT_ID('machine_snapshots') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('machine_snapshots'))
    ENABLE TRIGGER ALL ON machine_snapshots;
IF OBJECT_ID('machines') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('machines'))
    ENABLE TRIGGER ALL ON machines;
IF OBJECT_ID('machine_software') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('machine_software'))
    ENABLE TRIGGER ALL ON machine_software;
IF OBJECT_ID('assessment_controls') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('assessment_controls'))
    ENABLE TRIGGER ALL ON assessment_controls;
IF OBJECT_ID('assessments') IS NOT NULL AND EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('assessments'))
    ENABLE TRIGGER ALL ON assessments;
PRINT '=== Triggers re-enabled ===';

COMMIT TRANSACTION;
GO

-- ============================================================
-- VERIFY: all operational tables empty
-- ============================================================
PRINT '';
PRINT '=== VERIFICATION ===';
SELECT 'machines' AS tbl, COUNT(*) AS cnt FROM machines
UNION ALL SELECT 'assessment_runs', COUNT(*) FROM assessment_runs
UNION ALL SELECT 'control_results', COUNT(*) FROM control_results
UNION ALL SELECT 'machine_snapshots', COUNT(*) FROM machine_snapshots
UNION ALL SELECT 'enrollment_codes (unused)', SUM(CASE WHEN use_count = 0 THEN 1 ELSE 0 END) FROM enrollment_codes
UNION ALL SELECT 'assessments', COUNT(*) FROM assessments;

PRINT '';
PRINT '=== PRESERVED (catalog) ===';
SELECT 'control_defs (active)', COUNT(*) FROM control_defs WHERE is_active = 1
UNION ALL SELECT 'control_defs (inactive)', COUNT(*) FROM control_defs WHERE is_active = 0
UNION ALL SELECT 'frameworks', COUNT(*) FROM frameworks
UNION ALL SELECT 'platforms', COUNT(*) FROM platforms
UNION ALL SELECT 'organizations', COUNT(*) FROM organizations
UNION ALL SELECT 'users', COUNT(*) FROM users;
