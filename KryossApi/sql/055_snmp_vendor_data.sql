-- 055: Add vendor_data column for two-pass SNMP vendor-specific metrics
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'vendor_data')
    ALTER TABLE snmp_devices ADD vendor_data NVARCHAR(MAX) NULL;
