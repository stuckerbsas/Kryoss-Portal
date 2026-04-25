-- 057: SNMP deduplication + enrichment columns
-- Enables MAC-based dedup, machine correlation, stale detection, multi-IP tracking.

-- first_seen_at: when device was first discovered (never updated)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'first_seen_at')
    ALTER TABLE snmp_devices ADD first_seen_at DATETIME2 NOT NULL DEFAULT GETUTCDATE();

-- machine_id: FK to machines table when SNMP device is also an enrolled Kryoss agent
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'machine_id')
    ALTER TABLE snmp_devices ADD machine_id UNIQUEIDENTIFIER NULL;

-- is_stale: set to 1 when device not seen for 30+ days
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'is_stale')
    ALTER TABLE snmp_devices ADD is_stale BIT NOT NULL DEFAULT 0;

-- secondary_ips: JSON array of alternate IPs for same physical device (MAC-based merge)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'secondary_ips')
    ALTER TABLE snmp_devices ADD secondary_ips NVARCHAR(500) NULL;

-- scan_source: which agent scanned this device (hostname of scanning machine)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'scan_source')
    ALTER TABLE snmp_devices ADD scan_source NVARCHAR(100) NULL;

-- Index for MAC-based dedup lookup
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_snmp_devices_org_mac')
    CREATE INDEX ix_snmp_devices_org_mac ON snmp_devices(organization_id, mac_address)
    WHERE mac_address IS NOT NULL;

-- Index for machine correlation (EXEC to avoid same-batch parse error on new column)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_snmp_devices_machine')
    EXEC('CREATE INDEX ix_snmp_devices_machine ON snmp_devices(machine_id) WHERE machine_id IS NOT NULL');

-- Index for stale detection queries
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_snmp_devices_scanned')
    CREATE INDEX ix_snmp_devices_scanned ON snmp_devices(organization_id, scanned_at);

-- Backfill first_seen_at from scanned_at for existing rows
EXEC('UPDATE snmp_devices SET first_seen_at = scanned_at WHERE first_seen_at = ''1900-01-01''');

-- Drop the unique constraint on (org, ip) — MAC-based dedup replaces it
-- Need conditional drop since the constraint name may vary
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'uq_snmp_device_org_ip' AND object_id = OBJECT_ID('snmp_devices'))
    ALTER TABLE snmp_devices DROP CONSTRAINT uq_snmp_device_org_ip;

-- Re-add as non-unique index (still want fast IP lookups)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_snmp_devices_org_ip')
    CREATE INDEX ix_snmp_devices_org_ip ON snmp_devices(organization_id, ip_address);

PRINT '057: SNMP dedup + enrichment columns added';
