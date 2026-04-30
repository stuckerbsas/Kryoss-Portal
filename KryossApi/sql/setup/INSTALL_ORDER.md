# KryossDb Fresh Install — Script Execution Order

Run all scripts against an **empty** database in this exact order.
SSMS: connect to target server, select target DB, execute each script.

---

## Phase 1: Schema (DDL — creates all tables)

```
001_foundation.sql
002_core.sql
003_cmdb.sql
004_assessment.sql
005_enrollment_crypto.sql
006_vulnerability.sql
007_dashboard.sql
008_tags_future.sql
009_rls.sql
010_crm.sql
011_tickets.sql
012_billing.sql
013_bulk_enrollment.sql
014_snapshot_rawdata.sql
015_machine_platform_id.sql
016_machine_hwid.sql
017_brands_and_org_updates.sql
018_prevent_hard_delete.sql
019_ad_hygiene.sql
020_machine_disks.sql
021_machine_ports.sql
022_machine_threats.sql
023_external_scans.sql
024_m365_tenants.sql
025_m365_consent_columns.sql
027_user_profile_fields.sql
028_executive_ctas.sql
029_copilot_readiness.sql
030_cloud_assessment.sql
031_azure_consent_tracking.sql
032_azure_resource_cache.sql
033_ca7_suggestions.sql
034_compliance_frameworks.sql
035_powerbi_governance.sql
036_unified_copilot_scores.sql
037_cloud_assessment_mail_flow.sql
038_cloud_assessment_benchmarks.sql
039_service_catalog.sql
041_network_diagnostics.sql
042_snmp_infrastructure.sql
043_infra_assessment.sql
044_network_sites.sql
045_network_diag_cloud_dns.sql
046_hypervisor_inventory.sql
048_machine_agent_version.sql
049_scan_orchestrator.sql
050_cloud_assessment_alerts.sql
051_network_diag_v2.sql
052_snmp_lldp_cdp.sql
053_snmp_device_type_supplies.sql
054_snmp_device_profiles.sql
055_snmp_vendor_data.sql
056_snmp_host_resources.sql
057_snmp_dedup_enrich.sql
058_network_topology.sql
059_interface_traffic.sql
060_snmp_dedup_cleanup.sql
061_agent_service_mode.sql
062_trial_enrollment.sql
063_port_banner.sql
064_snmp_dns_ping.sql
065_external_exposure.sql
066_remediation.sql
067_machine_auth_keys.sql
068_machine_local_admins.sql
069_agent_remote_config.sql
070_trigger_scan.sql
071_machine_latest_score.sql
072_wan_health.sql
073_cve_scanner.sql
074_patch_compliance.sql
075_dc_health.sql
076_agent_loop_status.sql
077_remediation_hardening.sql
078_db_normalization.sql
```

**Note:** 078 creates normalized tables (control_check_params, remediation_action_params, etc.)
AND drops JSON columns. On a fresh DB the JSON columns are empty so the drops are safe.
The temporary JSON columns needed for seeding are re-added in Phase 2.

---

## Phase 2: Seed Data

Run `setup/00_pre_seed.sql` first — disables delete-prevention triggers and
adds temporary JSON columns (check_json, params_template) needed by legacy seeds.

```
setup/00_pre_seed.sql          -- REQUIRED: disable triggers + add temp columns

seed_001_roles_permissions.sql
seed_002_frameworks_platforms.sql
seed_003_crm_tickets.sql
seed_004_controls.sql          -- 605 controls with check_json
seed_005_controls_patch.sql    -- HIPAA refinements
seed_005b_fix_casing.sql       -- camelCase fix for 25 HIPAA controls
seed_006b_deactivate_legacy.sql
seed_007_platform_scope_workstation.sql
seed_007b_prune_inactive_platforms.sql
seed_007c_platform_scope_server.sql
seed_008_new_engine_controls.sql
seed_010_server_controls.sql
040_dc_platform_support.sql    -- hybrid: adds product_type + links controls to DC platforms
seed_013_dc_controls.sql       -- 40 DC controls with check_json
seed_014_edge_browser_controls.sql
seed_015_user_settings.sql
026_protocol_audit.sql         -- hybrid: adds controls with check_json
seed_011_antivirus_controls.sql
seed_012_baseline_gaps.sql
seed_042_network_controls.sql
047_dc_controls_v2.sql         -- hybrid: converts/adds DC controls with check_json
seed_034_compliance_frameworks.sql
seed_038_industry_benchmarks.sql
seed_039_kryoss_baseline.sql
seed_039_service_catalog.sql
seed_054_snmp_profiles.sql
seed_054b_snmp_profiles_expanded.sql
seed_066_remediation_actions.sql  -- remediation actions with params_template
seed_073_cve_builtin.sql
```

---

## Phase 3: Normalize + Finalize

Parses JSON columns into normalized relational tables, drops temporary columns,
re-enables triggers.

```
setup/99_finalize.sql          -- parse JSON, drop temp columns, re-enable triggers
seed_078_cpe_mappings.sql      -- CVE product maps (no JSON dependency)
```

---

## Phase 4: Verification

```
check_catalog_health.sql       -- read-only, verifies catalog integrity
```

---

## Utility Scripts (DO NOT run during install)

| Script | Purpose |
|--------|---------|
| `cleanup_all_scan_data.sql` | Wipe scan data (dev/test only) |
| `cleanup_full_reset.sql` | Full data reset |
| `cleanup_nuclear.sql` | DROP everything |
| `078c_reset_controls_for_normalization.sql` | Wipe + re-seed controls (migration fix) |
| `049b_backfill_scan_slots.sql` | Backfill scan slots for existing machines |
| `seed_009_backfill_assessment_controls.sql` | Backfill assessment_controls |
| `seed_100_test_data.sql` | Test org/machine/enrollment data |
| `seed_client.sql` | Single client bootstrap |
| `diag_catalog_fails.sql` | Diagnostic: find controls that fail evaluation |
| `runbook_managed_identity.sql` | Managed Identity setup instructions |
| `check_migrations_061_066.sql` | Diagnostic: verify migration state |

---

## Hybrid Scripts (schema + seed in one file)

These scripts both create/alter tables AND insert control data.
They appear in Phase 1 (for DDL) AND Phase 2 (for seed data).
Each is idempotent — safe to run twice.

- `026_protocol_audit.sql` — adds protocol audit controls with check_json
- `040_dc_platform_support.sql` — adds product_type column + links controls to DC platforms
- `047_dc_controls_v2.sql` — converts + adds DC controls with check_json
