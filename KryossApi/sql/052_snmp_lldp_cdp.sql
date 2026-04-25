-- 052: SNMP LLDP/CDP neighbor counts on snmp_devices

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'lldp_neighbor_count')
    ALTER TABLE snmp_devices ADD lldp_neighbor_count INT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'cdp_neighbor_count')
    ALTER TABLE snmp_devices ADD cdp_neighbor_count INT NOT NULL DEFAULT 0;
GO

PRINT '052: SNMP LLDP/CDP neighbor counts added';
