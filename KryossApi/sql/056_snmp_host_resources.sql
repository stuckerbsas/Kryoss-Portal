-- 056: Add HOST-RESOURCES-MIB columns to snmp_devices
-- CPU load, memory total/used — first-class columns for dashboard queries.
-- Full storage detail stays in raw_data JSON.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'cpu_load_pct')
    ALTER TABLE snmp_devices ADD cpu_load_pct INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'memory_total_mb')
    ALTER TABLE snmp_devices ADD memory_total_mb BIGINT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'memory_used_mb')
    ALTER TABLE snmp_devices ADD memory_used_mb BIGINT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'disk_total_gb')
    ALTER TABLE snmp_devices ADD disk_total_gb INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'disk_used_gb')
    ALTER TABLE snmp_devices ADD disk_used_gb INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'process_count')
    ALTER TABLE snmp_devices ADD process_count INT NULL;

PRINT '056: SNMP HOST-RESOURCES columns added';
