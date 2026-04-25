-- 053: Add device_type, page_count, mac_address, vendor to snmp_devices + snmp_device_supplies table
-- Supports device classification (switch/router/printer/ups/firewall/etc.),
-- PRINTER-MIB toner/ink level tracking, and OUI vendor lookup from MAC address.

-- New columns on snmp_devices
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'device_type')
    ALTER TABLE snmp_devices ADD device_type NVARCHAR(30) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'page_count')
    ALTER TABLE snmp_devices ADD page_count BIGINT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'mac_address')
    ALTER TABLE snmp_devices ADD mac_address NVARCHAR(17) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('snmp_devices') AND name = 'vendor')
    ALTER TABLE snmp_devices ADD vendor NVARCHAR(50) NULL;

-- Printer supplies table
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'snmp_device_supplies')
BEGIN
    CREATE TABLE snmp_device_supplies (
        id            INT IDENTITY(1,1) PRIMARY KEY,
        device_id     INT NOT NULL REFERENCES snmp_devices(id) ON DELETE CASCADE,
        description   NVARCHAR(200) NOT NULL,
        supply_type   NVARCHAR(30) NOT NULL,  -- toner, ink, drum, fuser, opc, other
        color         NVARCHAR(30) NULL,       -- black, cyan, magenta, yellow, photo
        level_percent INT NULL,                -- 0-100
        max_capacity  INT NULL,
        current_level INT NULL
    );

    CREATE INDEX ix_snmp_device_supplies_device ON snmp_device_supplies(device_id);
END
