-- Verify migrations 061-066 applied correctly
-- Run against KryossDb — all checks should return rows

PRINT '=== 061: Agent Service Mode ==='
SELECT 'machines.last_heartbeat_at' AS [check],
    CASE WHEN COL_LENGTH('machines','last_heartbeat_at') IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS status
UNION ALL
SELECT 'machines.agent_mode',
    CASE WHEN COL_LENGTH('machines','agent_mode') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'machines.is_trial',
    CASE WHEN COL_LENGTH('machines','is_trial') IS NOT NULL THEN 'OK' ELSE 'MISSING' END;

PRINT '=== 062: Trial Enrollment ==='
SELECT 'enrollment_codes.is_trial' AS [check],
    CASE WHEN COL_LENGTH('enrollment_codes','is_trial') IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS status;

PRINT '=== 063: Port Banner ==='
SELECT 'machine_ports.banner' AS [check],
    CASE WHEN COL_LENGTH('machine_ports','banner') IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS status
UNION ALL
SELECT 'machine_ports.service_name',
    CASE WHEN COL_LENGTH('machine_ports','service_name') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'machine_ports.service_version',
    CASE WHEN COL_LENGTH('machine_ports','service_version') IS NOT NULL THEN 'OK' ELSE 'MISSING' END;

PRINT '=== 064: SNMP DNS + Ping ==='
SELECT 'snmp_devices.reverse_dns' AS [check],
    CASE WHEN COL_LENGTH('snmp_devices','reverse_dns') IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS status
UNION ALL
SELECT 'snmp_devices.ping_latency_ms',
    CASE WHEN COL_LENGTH('snmp_devices','ping_latency_ms') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'snmp_devices.ping_loss_pct',
    CASE WHEN COL_LENGTH('snmp_devices','ping_loss_pct') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'snmp_devices.ping_jitter_ms',
    CASE WHEN COL_LENGTH('snmp_devices','ping_jitter_ms') IS NOT NULL THEN 'OK' ELSE 'MISSING' END;

PRINT '=== 065: External Exposure ==='
SELECT 'organizations.external_scan_consent' AS [check],
    CASE WHEN COL_LENGTH('organizations','external_scan_consent') IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS status
UNION ALL
SELECT 'external_scan_results.service_name',
    CASE WHEN COL_LENGTH('external_scan_results','service_name') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'external_scan_findings (table)',
    CASE WHEN OBJECT_ID('external_scan_findings','U') IS NOT NULL THEN 'OK' ELSE 'MISSING' END;

PRINT '=== 066: Remediation ==='
SELECT 'remediation_actions (table)' AS [check],
    CASE WHEN OBJECT_ID('remediation_actions','U') IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS status
UNION ALL
SELECT 'remediation_tasks (table)',
    CASE WHEN OBJECT_ID('remediation_tasks','U') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'org_auto_remediate (table)',
    CASE WHEN OBJECT_ID('org_auto_remediate','U') IS NOT NULL THEN 'OK' ELSE 'MISSING' END;

-- Seed check
PRINT '=== Seed 066: Remediation Actions ==='
SELECT 'remediation_actions count' AS [check],
    CAST(COUNT(*) AS varchar) + ' rows' AS status
FROM remediation_actions;

PRINT '=== Summary ==='
SELECT
    (SELECT COUNT(*) FROM (
        SELECT 1 AS x WHERE COL_LENGTH('machines','last_heartbeat_at') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('machines','agent_mode') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('machines','is_trial') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('enrollment_codes','is_trial') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('machine_ports','banner') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('machine_ports','service_name') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('machine_ports','service_version') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('snmp_devices','reverse_dns') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('snmp_devices','ping_latency_ms') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('snmp_devices','ping_loss_pct') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('snmp_devices','ping_jitter_ms') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('organizations','external_scan_consent') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('external_scan_results','service_name') IS NOT NULL
        UNION ALL SELECT 1 WHERE OBJECT_ID('external_scan_findings','U') IS NOT NULL
        UNION ALL SELECT 1 WHERE OBJECT_ID('remediation_actions','U') IS NOT NULL
        UNION ALL SELECT 1 WHERE OBJECT_ID('remediation_tasks','U') IS NOT NULL
        UNION ALL SELECT 1 WHERE OBJECT_ID('org_auto_remediate','U') IS NOT NULL
    ) t) AS checks_passed,
    '17' AS checks_total,
    CASE WHEN (SELECT COUNT(*) FROM (
        SELECT 1 AS x WHERE COL_LENGTH('machines','last_heartbeat_at') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('machines','agent_mode') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('machines','is_trial') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('enrollment_codes','is_trial') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('machine_ports','banner') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('machine_ports','service_name') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('machine_ports','service_version') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('snmp_devices','reverse_dns') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('snmp_devices','ping_latency_ms') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('snmp_devices','ping_loss_pct') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('snmp_devices','ping_jitter_ms') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('organizations','external_scan_consent') IS NOT NULL
        UNION ALL SELECT 1 WHERE COL_LENGTH('external_scan_results','service_name') IS NOT NULL
        UNION ALL SELECT 1 WHERE OBJECT_ID('external_scan_findings','U') IS NOT NULL
        UNION ALL SELECT 1 WHERE OBJECT_ID('remediation_actions','U') IS NOT NULL
        UNION ALL SELECT 1 WHERE OBJECT_ID('remediation_tasks','U') IS NOT NULL
        UNION ALL SELECT 1 WHERE OBJECT_ID('org_auto_remediate','U') IS NOT NULL
    ) t) = 17 THEN 'ALL MIGRATIONS OK' ELSE 'SOME MISSING — check above' END AS verdict;
