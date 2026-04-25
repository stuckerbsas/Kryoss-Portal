-- 059: Interface traffic rate tracking
-- Stores previous octets for delta calculation between scans

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_device_interfaces') AND name = 'prev_in_octets')
    ALTER TABLE snmp_device_interfaces ADD prev_in_octets BIGINT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_device_interfaces') AND name = 'prev_out_octets')
    ALTER TABLE snmp_device_interfaces ADD prev_out_octets BIGINT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_device_interfaces') AND name = 'sample_interval_sec')
    ALTER TABLE snmp_device_interfaces ADD sample_interval_sec INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_device_interfaces') AND name = 'in_rate_bps')
    ALTER TABLE snmp_device_interfaces ADD in_rate_bps BIGINT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_device_interfaces') AND name = 'out_rate_bps')
    ALTER TABLE snmp_device_interfaces ADD out_rate_bps BIGINT NULL;

PRINT '059: Interface traffic rate columns added';
