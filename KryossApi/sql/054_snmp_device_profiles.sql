-- 054: SNMP device profiles — vendor-specific OIDs for two-pass scanning
-- Agent does pass 1 with standard MIBs, gets sysObjectId.
-- Server matches sysObjectId prefix → returns vendor OIDs for pass 2.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'snmp_device_profiles')
BEGIN
    CREATE TABLE snmp_device_profiles (
        id            INT IDENTITY(1,1) PRIMARY KEY,
        vendor_name   NVARCHAR(50)  NOT NULL,
        oid_prefix    NVARCHAR(50)  NOT NULL,  -- sysObjectId prefix match
        enabled       BIT NOT NULL DEFAULT 1
    );

    CREATE UNIQUE INDEX uq_snmp_profile_prefix ON snmp_device_profiles(oid_prefix);
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'snmp_profile_oids')
BEGIN
    CREATE TABLE snmp_profile_oids (
        id            INT IDENTITY(1,1) PRIMARY KEY,
        profile_id    INT NOT NULL REFERENCES snmp_device_profiles(id) ON DELETE CASCADE,
        oid           NVARCHAR(100) NOT NULL,
        name          NVARCHAR(50)  NOT NULL,
        category      NVARCHAR(30)  NOT NULL,  -- performance, security, inventory, wireless, vpn
        data_type     NVARCHAR(10)  NOT NULL DEFAULT 'gauge',  -- gauge, counter, string, timeticks
        unit          NVARCHAR(20)  NULL,       -- %, bytes, sessions, etc.
        walk          BIT NOT NULL DEFAULT 0    -- 0 = GET, 1 = WALK subtree
    );

    CREATE INDEX ix_snmp_profile_oids_profile ON snmp_profile_oids(profile_id);
END
