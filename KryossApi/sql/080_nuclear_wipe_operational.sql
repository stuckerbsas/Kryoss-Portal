-- 080_nuclear_wipe_operational.sql
-- Wipes ALL operational data for a completely fresh start.
-- PRESERVES: franchises, organizations, users, roles, permissions,
--            control catalog, remediation catalog, service catalog,
--            SNMP profiles, cloud frameworks, industry benchmarks,
--            CVE entries, software catalog, brands, org_crypto_keys.
-- Run once, then re-enroll agents from scratch.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

-- ============================================================
-- Phase 0: Disable ALL triggers on operational tables
-- ============================================================
DECLARE @sql NVARCHAR(MAX) = '';
SELECT @sql = @sql + 'DISABLE TRIGGER ALL ON [' + t.name + '];' + CHAR(10)
FROM sys.tables t
WHERE EXISTS (SELECT 1 FROM sys.triggers tr WHERE tr.parent_id = t.object_id);
EXEC sp_executesql @sql;
PRINT 'All triggers disabled';
GO

BEGIN TRANSACTION;

-- ============================================================
-- Phase 1: Deep leaf tables (no children)
-- ============================================================

-- Cloud assessment deep children
DELETE FROM cloud_finding_properties;
PRINT CONCAT('cloud_finding_properties: ', @@ROWCOUNT);

DELETE FROM cloud_resource_risk_flags;
PRINT CONCAT('cloud_resource_risk_flags: ', @@ROWCOUNT);

DELETE FROM mail_domain_spf_warnings;
PRINT CONCAT('mail_domain_spf_warnings: ', @@ROWCOUNT);

DELETE FROM mail_domain_dkim_selectors;
PRINT CONCAT('mail_domain_dkim_selectors: ', @@ROWCOUNT);

DELETE FROM shared_mailbox_delegates;
PRINT CONCAT('shared_mailbox_delegates: ', @@ROWCOUNT);

DELETE FROM alert_payload_fields;
PRINT CONCAT('alert_payload_fields: ', @@ROWCOUNT);

-- Cloud assessment scan children
DELETE FROM cloud_assessment_findings;
PRINT CONCAT('cloud_assessment_findings: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_metrics;
PRINT CONCAT('cloud_assessment_metrics: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_licenses;
PRINT CONCAT('cloud_assessment_licenses: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_adoption;
PRINT CONCAT('cloud_assessment_adoption: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_wasted_licenses;
PRINT CONCAT('cloud_assessment_wasted_licenses: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_sharepoint_sites;
PRINT CONCAT('cloud_assessment_sharepoint_sites: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_external_users;
PRINT CONCAT('cloud_assessment_external_users: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_mail_domains;
PRINT CONCAT('cloud_assessment_mail_domains: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_mailbox_risks;
PRINT CONCAT('cloud_assessment_mailbox_risks: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_shared_mailboxes;
PRINT CONCAT('cloud_assessment_shared_mailboxes: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_framework_scores;
PRINT CONCAT('cloud_assessment_framework_scores: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_azure_resources;
PRINT CONCAT('cloud_assessment_azure_resources: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_powerbi_workspaces;
PRINT CONCAT('cloud_assessment_powerbi_workspaces: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_powerbi_gateways;
PRINT CONCAT('cloud_assessment_powerbi_gateways: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_powerbi_capacities;
PRINT CONCAT('cloud_assessment_powerbi_capacities: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_powerbi_activity_summary;
PRINT CONCAT('cloud_assessment_powerbi_activity_summary: ', @@ROWCOUNT);

-- Benchmark operational data (keep industry_benchmarks seed)
DELETE FROM cloud_assessment_benchmark_comparisons;
PRINT CONCAT('cloud_assessment_benchmark_comparisons: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_franchise_aggregates;
PRINT CONCAT('cloud_assessment_franchise_aggregates: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_global_aggregates;
PRINT CONCAT('cloud_assessment_global_aggregates: ', @@ROWCOUNT);

-- Alert operational data (keep rules config)
DELETE FROM cloud_assessment_alerts_sent;
PRINT CONCAT('cloud_assessment_alerts_sent: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_finding_status;
PRINT CONCAT('cloud_assessment_finding_status: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_suggestions;
PRINT CONCAT('cloud_assessment_suggestions: ', @@ROWCOUNT);

-- Copilot readiness (deprecated but still has data)
DELETE FROM copilot_readiness_findings;
PRINT CONCAT('copilot_readiness_findings: ', @@ROWCOUNT);

DELETE FROM copilot_readiness_metrics;
PRINT CONCAT('copilot_readiness_metrics: ', @@ROWCOUNT);

DELETE FROM copilot_readiness_sharepoint;
PRINT CONCAT('copilot_readiness_sharepoint: ', @@ROWCOUNT);

DELETE FROM copilot_readiness_external_users;
PRINT CONCAT('copilot_readiness_external_users: ', @@ROWCOUNT);

-- M365
DELETE FROM m365_findings;
PRINT CONCAT('m365_findings: ', @@ROWCOUNT);

-- External scans
DELETE FROM external_scan_findings;
PRINT CONCAT('external_scan_findings: ', @@ROWCOUNT);

DELETE FROM external_scan_results;
PRINT CONCAT('external_scan_results: ', @@ROWCOUNT);

-- SNMP children
DELETE FROM snmp_device_interfaces;
PRINT CONCAT('snmp_device_interfaces: ', @@ROWCOUNT);

DELETE FROM snmp_device_supplies;
PRINT CONCAT('snmp_device_supplies: ', @@ROWCOUNT);

DELETE FROM snmp_device_neighbors;
PRINT CONCAT('snmp_device_neighbors: ', @@ROWCOUNT);

-- AD hygiene
DELETE FROM ad_hygiene_findings;
PRINT CONCAT('ad_hygiene_findings: ', @@ROWCOUNT);

-- Network diag children
DELETE FROM machine_network_latency;
PRINT CONCAT('machine_network_latency: ', @@ROWCOUNT);

DELETE FROM machine_network_routes;
PRINT CONCAT('machine_network_routes: ', @@ROWCOUNT);

DELETE FROM machine_traceroute_hops;
PRINT CONCAT('machine_traceroute_hops: ', @@ROWCOUNT);

-- DC health
DELETE FROM dc_replication_partners;
PRINT CONCAT('dc_replication_partners: ', @@ROWCOUNT);

-- WAN
DELETE FROM wan_findings;
PRINT CONCAT('wan_findings: ', @@ROWCOUNT);

-- Infra assessment children
DELETE FROM infra_assessment_findings;
PRINT CONCAT('infra_assessment_findings: ', @@ROWCOUNT);

DELETE FROM infra_assessment_connectivity;
PRINT CONCAT('infra_assessment_connectivity: ', @@ROWCOUNT);

DELETE FROM infra_assessment_capacity;
PRINT CONCAT('infra_assessment_capacity: ', @@ROWCOUNT);

DELETE FROM infra_assessment_devices;
PRINT CONCAT('infra_assessment_devices: ', @@ROWCOUNT);

DELETE FROM infra_assessment_sites;
PRINT CONCAT('infra_assessment_sites: ', @@ROWCOUNT);

-- Hypervisor
DELETE FROM infra_vms;
PRINT CONCAT('infra_vms: ', @@ROWCOUNT);

DELETE FROM infra_hypervisors;
PRINT CONCAT('infra_hypervisors: ', @@ROWCOUNT);

-- Remediation operational data (keep remediation_actions catalog)
DELETE FROM remediation_log;
PRINT CONCAT('remediation_log: ', @@ROWCOUNT);

DELETE FROM remediation_tasks;
PRINT CONCAT('remediation_tasks: ', @@ROWCOUNT);

DELETE FROM org_auto_remediate;
PRINT CONCAT('org_auto_remediate: ', @@ROWCOUNT);

-- Assessment children (may already be empty from prior truncate)
DELETE FROM control_results;
PRINT CONCAT('control_results: ', @@ROWCOUNT);

DELETE FROM run_framework_scores;
PRINT CONCAT('run_framework_scores: ', @@ROWCOUNT);

-- CVE findings (keep cve_entries + cve_product_maps catalog)
DELETE FROM machine_cve_findings;
PRINT CONCAT('machine_cve_findings: ', @@ROWCOUNT);

DELETE FROM cve_sync_log;
PRINT CONCAT('cve_sync_log: ', @@ROWCOUNT);

-- ============================================================
-- Phase 2: Mid-level tables
-- ============================================================

-- Network diag (parent of latency/routes/hops)
DELETE FROM machine_network_diag;
PRINT CONCAT('machine_network_diag: ', @@ROWCOUNT);

-- Machine child tables
DELETE FROM machine_disks;
PRINT CONCAT('machine_disks: ', @@ROWCOUNT);

DELETE FROM machine_ports;
PRINT CONCAT('machine_ports: ', @@ROWCOUNT);

DELETE FROM machine_threats;
PRINT CONCAT('machine_threats: ', @@ROWCOUNT);

DELETE FROM machine_local_admins;
PRINT CONCAT('machine_local_admins: ', @@ROWCOUNT);

DELETE FROM machine_loop_status;
PRINT CONCAT('machine_loop_status: ', @@ROWCOUNT);

DELETE FROM machine_patches;
PRINT CONCAT('machine_patches: ', @@ROWCOUNT);

DELETE FROM machine_patch_status;
PRINT CONCAT('machine_patch_status: ', @@ROWCOUNT);

DELETE FROM machine_services;
PRINT CONCAT('machine_services: ', @@ROWCOUNT);

DELETE FROM machine_software;
PRINT CONCAT('machine_software: ', @@ROWCOUNT);

DELETE FROM machine_snapshots;
PRINT CONCAT('machine_snapshots: ', @@ROWCOUNT);

DELETE FROM machine_public_ip_history;
PRINT CONCAT('machine_public_ip_history: ', @@ROWCOUNT);

DELETE FROM dc_health_snapshots;
PRINT CONCAT('dc_health_snapshots: ', @@ROWCOUNT);

DELETE FROM ad_hygiene_scans;
PRINT CONCAT('ad_hygiene_scans: ', @@ROWCOUNT);

-- Cloud parent tables
DELETE FROM cloud_assessment_scans;
PRINT CONCAT('cloud_assessment_scans: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_azure_subscriptions;
PRINT CONCAT('cloud_assessment_azure_subscriptions: ', @@ROWCOUNT);

DELETE FROM cloud_assessment_powerbi_connection;
PRINT CONCAT('cloud_assessment_powerbi_connection: ', @@ROWCOUNT);

DELETE FROM copilot_readiness_scans;
PRINT CONCAT('copilot_readiness_scans: ', @@ROWCOUNT);

DELETE FROM m365_tenants;
PRINT CONCAT('m365_tenants: ', @@ROWCOUNT);

-- Other parent tables
DELETE FROM external_scans;
PRINT CONCAT('external_scans: ', @@ROWCOUNT);

DELETE FROM snmp_devices;
PRINT CONCAT('snmp_devices: ', @@ROWCOUNT);

DELETE FROM snmp_configs;
PRINT CONCAT('snmp_configs: ', @@ROWCOUNT);

DELETE FROM network_sites;
PRINT CONCAT('network_sites: ', @@ROWCOUNT);

DELETE FROM infra_assessment_scans;
PRINT CONCAT('infra_assessment_scans: ', @@ROWCOUNT);

DELETE FROM infra_hypervisor_configs;
PRINT CONCAT('infra_hypervisor_configs: ', @@ROWCOUNT);

DELETE FROM org_priority_services;
PRINT CONCAT('org_priority_services: ', @@ROWCOUNT);

-- Alert rules (franchise-level config, wipe for fresh start)
DELETE FROM cloud_assessment_alert_rules;
PRINT CONCAT('cloud_assessment_alert_rules: ', @@ROWCOUNT);

-- ============================================================
-- Phase 3: Assessment runs + machines
-- ============================================================

DELETE FROM assessment_runs;
PRINT CONCAT('assessment_runs: ', @@ROWCOUNT);

-- Clear enrollment code FK to machines before deleting machines
UPDATE enrollment_codes SET used_by = NULL;
PRINT CONCAT('enrollment_codes FK cleared: ', @@ROWCOUNT);

DELETE FROM machines;
PRINT CONCAT('machines: ', @@ROWCOUNT);

-- ============================================================
-- Phase 4: Assessments + actlog + remaining
-- ============================================================

-- Wipe enrollment codes BEFORE assessments (FK enrollment_codes → assessments)
DELETE FROM enrollment_codes;
PRINT CONCAT('enrollment_codes: ', @@ROWCOUNT);

DELETE FROM assessment_controls;
PRINT CONCAT('assessment_controls: ', @@ROWCOUNT);

DELETE FROM assessments;
PRINT CONCAT('assessments: ', @@ROWCOUNT);

DELETE FROM actlog;
PRINT CONCAT('actlog: ', @@ROWCOUNT);

DELETE FROM org_crypto_keys;
PRINT CONCAT('org_crypto_keys: ', @@ROWCOUNT);

-- Wipe users (will re-provision from Entra ID on first login)
DELETE FROM users;
PRINT CONCAT('users: ', @@ROWCOUNT);

COMMIT TRANSACTION;
PRINT '=== COMMIT OK ===';
GO

-- ============================================================
-- Phase 5: Re-enable all triggers
-- ============================================================
DECLARE @sql2 NVARCHAR(MAX) = '';
SELECT @sql2 = @sql2 + 'ENABLE TRIGGER ALL ON [' + t.name + '];' + CHAR(10)
FROM sys.tables t
WHERE EXISTS (SELECT 1 FROM sys.triggers tr WHERE tr.parent_id = t.object_id);
EXEC sp_executesql @sql2;
PRINT 'All triggers re-enabled';
GO

-- ============================================================
-- Verify: everything operational should be 0
-- ============================================================
SELECT 'machines' AS tbl, COUNT(*) AS cnt FROM machines
UNION ALL SELECT 'assessment_runs', COUNT(*) FROM assessment_runs
UNION ALL SELECT 'control_results', COUNT(*) FROM control_results
UNION ALL SELECT 'actlog', COUNT(*) FROM actlog
UNION ALL SELECT 'machine_ports', COUNT(*) FROM machine_ports
UNION ALL SELECT 'machine_disks', COUNT(*) FROM machine_disks
UNION ALL SELECT 'snmp_devices', COUNT(*) FROM snmp_devices
UNION ALL SELECT 'machine_cve_findings', COUNT(*) FROM machine_cve_findings
UNION ALL SELECT 'ad_hygiene_scans', COUNT(*) FROM ad_hygiene_scans
UNION ALL SELECT 'cloud_assessment_scans', COUNT(*) FROM cloud_assessment_scans
UNION ALL SELECT 'm365_tenants', COUNT(*) FROM m365_tenants
UNION ALL SELECT 'external_scans', COUNT(*) FROM external_scans
UNION ALL SELECT 'network_sites', COUNT(*) FROM network_sites
UNION ALL SELECT 'dc_health_snapshots', COUNT(*) FROM dc_health_snapshots
UNION ALL SELECT 'remediation_tasks', COUNT(*) FROM remediation_tasks
UNION ALL SELECT 'machine_services', COUNT(*) FROM machine_services
UNION ALL SELECT 'machine_network_diag', COUNT(*) FROM machine_network_diag
UNION ALL SELECT 'infra_assessment_scans', COUNT(*) FROM infra_assessment_scans
ORDER BY cnt DESC;
GO

-- Preserved tables (should have data)
SELECT 'control_defs' AS tbl, COUNT(*) AS cnt FROM control_defs
UNION ALL SELECT 'control_check_params', COUNT(*) FROM control_check_params
UNION ALL SELECT 'control_frameworks', COUNT(*) FROM control_frameworks
UNION ALL SELECT 'control_platforms', COUNT(*) FROM control_platforms
UNION ALL SELECT 'frameworks', COUNT(*) FROM frameworks
UNION ALL SELECT 'platforms', COUNT(*) FROM platforms
UNION ALL SELECT 'remediation_actions', COUNT(*) FROM remediation_actions
UNION ALL SELECT 'remediation_action_params', COUNT(*) FROM remediation_action_params
UNION ALL SELECT 'organizations', COUNT(*) FROM organizations
UNION ALL SELECT 'franchises', COUNT(*) FROM franchises;
GO

PRINT '=== NUCLEAR WIPE COMPLETE — fresh start ===';
GO
