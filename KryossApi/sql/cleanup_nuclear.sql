-- cleanup_nuclear.sql
-- NUCLEAR WIPE: Deletes ALL operational data. Keeps schema, orgs, franchises, catalog, users, roles.
-- Run manually. NOT idempotent by design — run once.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

-- ── Disable triggers on tables that have them ──
DISABLE TRIGGER ALL ON actlog;
DISABLE TRIGGER ALL ON control_results;
DISABLE TRIGGER ALL ON run_framework_scores;
DISABLE TRIGGER ALL ON assessment_runs;
DISABLE TRIGGER ALL ON machine_snapshots;
DISABLE TRIGGER ALL ON machines;
DISABLE TRIGGER ALL ON machine_software;
PRINT '=== Triggers disabled ===';

-- ── 1. Actlog ──
DELETE FROM actlog;
PRINT CONCAT('actlog: ', @@ROWCOUNT);

-- ── 2. Remediation (children first) ──
DELETE FROM remediation_log;
PRINT CONCAT('remediation_log: ', @@ROWCOUNT);
DELETE FROM remediation_tasks;
PRINT CONCAT('remediation_tasks: ', @@ROWCOUNT);
DELETE FROM org_auto_remediate;
PRINT CONCAT('org_auto_remediate: ', @@ROWCOUNT);

-- ── 3. Machine services ──
DELETE FROM machine_services;
PRINT CONCAT('machine_services: ', @@ROWCOUNT);

-- ── 4. CVE / Patch ──
DELETE FROM machine_cve_findings;
PRINT CONCAT('machine_cve_findings: ', @@ROWCOUNT);
DELETE FROM machine_patches;
PRINT CONCAT('machine_patches: ', @@ROWCOUNT);
DELETE FROM machine_patch_status;
PRINT CONCAT('machine_patch_status: ', @@ROWCOUNT);

-- ── 5. DC Health ──
DELETE FROM dc_replication_partners;
PRINT CONCAT('dc_replication_partners: ', @@ROWCOUNT);
DELETE FROM dc_health_snapshots;
PRINT CONCAT('dc_health_snapshots: ', @@ROWCOUNT);

-- ── 6. Network / WAN / Sites ──
DELETE FROM wan_findings;
PRINT CONCAT('wan_findings: ', @@ROWCOUNT);
DELETE FROM machine_network_routes;
PRINT CONCAT('machine_network_routes: ', @@ROWCOUNT);
DELETE FROM machine_network_latency;
PRINT CONCAT('machine_network_latency: ', @@ROWCOUNT);
DELETE FROM machine_network_diag;
PRINT CONCAT('machine_network_diag: ', @@ROWCOUNT);
DELETE FROM machine_public_ip_history;
PRINT CONCAT('machine_public_ip_history: ', @@ROWCOUNT);
DELETE FROM network_sites;
PRINT CONCAT('network_sites: ', @@ROWCOUNT);

-- ── 7. SNMP ──
DELETE FROM snmp_device_neighbors;
PRINT CONCAT('snmp_device_neighbors: ', @@ROWCOUNT);
DELETE FROM snmp_device_supplies;
PRINT CONCAT('snmp_device_supplies: ', @@ROWCOUNT);
DELETE FROM snmp_device_interfaces;
PRINT CONCAT('snmp_device_interfaces: ', @@ROWCOUNT);
DELETE FROM snmp_devices;
PRINT CONCAT('snmp_devices: ', @@ROWCOUNT);
DELETE FROM snmp_configs;
PRINT CONCAT('snmp_configs: ', @@ROWCOUNT);

-- ── 8. External Scan ──
DELETE FROM external_scan_findings;
PRINT CONCAT('external_scan_findings: ', @@ROWCOUNT);
DELETE FROM external_scan_results;
PRINT CONCAT('external_scan_results: ', @@ROWCOUNT);
DELETE FROM external_scans;
PRINT CONCAT('external_scans: ', @@ROWCOUNT);

-- ── 9. Threats / Ports / AD Hygiene ──
DELETE FROM machine_threats;
PRINT CONCAT('machine_threats: ', @@ROWCOUNT);
DELETE FROM machine_ports;
PRINT CONCAT('machine_ports: ', @@ROWCOUNT);
DELETE FROM ad_hygiene_findings;
PRINT CONCAT('ad_hygiene_findings: ', @@ROWCOUNT);
DELETE FROM ad_hygiene_scans;
PRINT CONCAT('ad_hygiene_scans: ', @@ROWCOUNT);

-- ── 10. Cloud Assessment (all children) ──
DELETE FROM cloud_assessment_finding_control_mappings;
PRINT CONCAT('ca_finding_control_mappings: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_framework_scores;
PRINT CONCAT('ca_framework_scores: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_finding_status;
PRINT CONCAT('ca_finding_status: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_suggestions;
PRINT CONCAT('ca_suggestions: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_findings;
PRINT CONCAT('ca_findings: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_metrics;
PRINT CONCAT('ca_metrics: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_licenses;
PRINT CONCAT('ca_licenses: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_adoption;
PRINT CONCAT('ca_adoption: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_wasted_licenses;
PRINT CONCAT('ca_wasted_licenses: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_azure_resources;
PRINT CONCAT('ca_azure_resources: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_azure_subscriptions;
PRINT CONCAT('ca_azure_subscriptions: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_powerbi_activity_summary;
PRINT CONCAT('ca_pbi_activity: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_powerbi_capacities;
PRINT CONCAT('ca_pbi_capacities: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_powerbi_gateways;
PRINT CONCAT('ca_pbi_gateways: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_powerbi_workspaces;
PRINT CONCAT('ca_pbi_workspaces: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_powerbi_connection;
PRINT CONCAT('ca_pbi_connection: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_sharepoint_sites;
PRINT CONCAT('ca_sharepoint_sites: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_external_users;
PRINT CONCAT('ca_external_users: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_mail_domains;
PRINT CONCAT('ca_mail_domains: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_mailbox_risks;
PRINT CONCAT('ca_mailbox_risks: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_shared_mailboxes;
PRINT CONCAT('ca_shared_mailboxes: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_scans;
PRINT CONCAT('ca_scans: ', @@ROWCOUNT);

-- ── 11. Cloud Assessment Benchmarks ──
DELETE FROM cloud_assessment_benchmark_comparisons;
PRINT CONCAT('ca_benchmark_comparisons: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_franchise_aggregates;
PRINT CONCAT('ca_franchise_aggregates: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_global_aggregates;
PRINT CONCAT('ca_global_aggregates: ', @@ROWCOUNT);

-- ── 12. Cloud Assessment Alerts ──
DELETE FROM cloud_assessment_alerts_sent;
PRINT CONCAT('ca_alerts_sent: ', @@ROWCOUNT);
DELETE FROM cloud_assessment_alert_rules;
PRINT CONCAT('ca_alert_rules: ', @@ROWCOUNT);

-- ── 13. M365 / Copilot (legacy) ──
DELETE FROM m365_findings;
PRINT CONCAT('m365_findings: ', @@ROWCOUNT);
DELETE FROM m365_tenants;
PRINT CONCAT('m365_tenants: ', @@ROWCOUNT);
DELETE FROM copilot_readiness_sharepoint;
PRINT CONCAT('copilot_sharepoint: ', @@ROWCOUNT);
DELETE FROM copilot_readiness_external_users;
PRINT CONCAT('copilot_external_users: ', @@ROWCOUNT);
DELETE FROM copilot_readiness_findings;
PRINT CONCAT('copilot_findings: ', @@ROWCOUNT);
DELETE FROM copilot_readiness_metrics;
PRINT CONCAT('copilot_metrics: ', @@ROWCOUNT);
DELETE FROM copilot_readiness_scans;
PRINT CONCAT('copilot_scans: ', @@ROWCOUNT);

-- ── 14. Infra Assessment / Hypervisors ──
DELETE FROM infra_vms;
PRINT CONCAT('infra_vms: ', @@ROWCOUNT);
DELETE FROM infra_hypervisors;
PRINT CONCAT('infra_hypervisors: ', @@ROWCOUNT);
DELETE FROM infra_hypervisor_configs;
PRINT CONCAT('infra_hypervisor_configs: ', @@ROWCOUNT);
DELETE FROM infra_assessment_findings;
PRINT CONCAT('infra_findings: ', @@ROWCOUNT);
DELETE FROM infra_assessment_capacity;
PRINT CONCAT('infra_capacity: ', @@ROWCOUNT);
DELETE FROM infra_assessment_connectivity;
PRINT CONCAT('infra_connectivity: ', @@ROWCOUNT);
DELETE FROM infra_assessment_devices;
PRINT CONCAT('infra_devices: ', @@ROWCOUNT);
DELETE FROM infra_assessment_sites;
PRINT CONCAT('infra_sites: ', @@ROWCOUNT);
DELETE FROM infra_assessment_scans;
PRINT CONCAT('infra_scans: ', @@ROWCOUNT);

-- ── 15. Assessment data ──
DELETE FROM control_results;
PRINT CONCAT('control_results: ', @@ROWCOUNT);
DELETE FROM run_framework_scores;
PRINT CONCAT('run_framework_scores: ', @@ROWCOUNT);
DELETE FROM assessment_runs;
PRINT CONCAT('assessment_runs: ', @@ROWCOUNT);
DELETE FROM machine_snapshots;
PRINT CONCAT('machine_snapshots: ', @@ROWCOUNT);
DELETE FROM machine_software;
PRINT CONCAT('machine_software: ', @@ROWCOUNT);
DELETE FROM machine_disks;
PRINT CONCAT('machine_disks: ', @@ROWCOUNT);

-- ── 16. Enrollment codes (reset, keep codes) ──
UPDATE enrollment_codes SET used_by = NULL, used_at = NULL, use_count = 0;
PRINT CONCAT('enrollment_codes reset: ', @@ROWCOUNT);

-- ── 17. Machines (parent — must be last) ──
DELETE FROM machines;
PRINT CONCAT('machines: ', @@ROWCOUNT);

-- ── 18. Assessments (optional — recreated on enrollment) ──
DELETE FROM assessment_controls;
PRINT CONCAT('assessment_controls: ', @@ROWCOUNT);
DELETE FROM assessments;
PRINT CONCAT('assessments: ', @@ROWCOUNT);

-- ── 19. Reset org-level operational fields ──
UPDATE organizations SET priority_services_json = NULL;
PRINT CONCAT('org priority_services reset: ', @@ROWCOUNT);

-- ── Re-enable triggers ──
ENABLE TRIGGER ALL ON actlog;
ENABLE TRIGGER ALL ON control_results;
ENABLE TRIGGER ALL ON run_framework_scores;
ENABLE TRIGGER ALL ON assessment_runs;
ENABLE TRIGGER ALL ON machine_snapshots;
ENABLE TRIGGER ALL ON machines;
ENABLE TRIGGER ALL ON machine_software;
PRINT '=== Triggers re-enabled ===';

COMMIT TRANSACTION;
PRINT '=== NUCLEAR WIPE COMPLETE ===';
GO

-- Verify
SELECT 'machines' AS tbl, COUNT(*) AS cnt FROM machines
UNION ALL SELECT 'actlog', COUNT(*) FROM actlog
UNION ALL SELECT 'remediation_log', COUNT(*) FROM remediation_log
UNION ALL SELECT 'machine_services', COUNT(*) FROM machine_services
UNION ALL SELECT 'assessment_runs', COUNT(*) FROM assessment_runs
UNION ALL SELECT 'cloud_assessment_scans', COUNT(*) FROM cloud_assessment_scans
UNION ALL SELECT 'snmp_devices', COUNT(*) FROM snmp_devices
UNION ALL SELECT 'assessments', COUNT(*) FROM assessments;
GO
