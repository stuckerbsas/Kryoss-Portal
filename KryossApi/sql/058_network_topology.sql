-- 058: Network Topology — persist LLDP/CDP neighbor relationships for topology graph
-- Enables: link resolution between known SNMP devices, D3.js force-directed visualization

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'snmp_device_neighbors')
CREATE TABLE snmp_device_neighbors (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    device_id           INT NOT NULL REFERENCES snmp_devices(id) ON DELETE CASCADE,
    protocol            NVARCHAR(10) NOT NULL,  -- 'lldp' or 'cdp'
    local_port          NVARCHAR(200) NULL,
    remote_chassis_id   NVARCHAR(200) NULL,     -- LLDP: MAC or chassis ID
    remote_port_id      NVARCHAR(200) NULL,
    remote_port_desc    NVARCHAR(500) NULL,
    remote_sys_name     NVARCHAR(200) NULL,     -- key for resolving to known device
    remote_sys_desc     NVARCHAR(500) NULL,
    remote_device_id_str NVARCHAR(200) NULL,    -- CDP: device ID string
    remote_ip           NVARCHAR(45) NULL,      -- CDP: IP address of neighbor
    remote_platform     NVARCHAR(200) NULL,     -- CDP: platform string
    resolved_device_id  INT NULL REFERENCES snmp_devices(id), -- FK when neighbor matches a known device
    updated_at          DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_snmp_neighbors_device')
    CREATE INDEX ix_snmp_neighbors_device ON snmp_device_neighbors(device_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_snmp_neighbors_resolved')
    CREATE INDEX ix_snmp_neighbors_resolved ON snmp_device_neighbors(resolved_device_id)
    WHERE resolved_device_id IS NOT NULL;

PRINT '058: Network topology neighbors table created';
