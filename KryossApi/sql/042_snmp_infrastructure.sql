-- ============================================================
-- 042_snmp_infrastructure.sql
-- Phase 5b: SNMP infrastructure device inventory
--
-- Stores per-org SNMP credentials and discovered device data:
-- system info, interfaces, entity/chassis info.
-- ============================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- SNMP configuration per organization
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'snmp_configs')
CREATE TABLE snmp_configs (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    snmp_version    SMALLINT NOT NULL DEFAULT 2,  -- 1, 2 (v2c), 3
    community       NVARCHAR(256) NULL,           -- v1/v2c community string
    username        NVARCHAR(256) NULL,           -- v3 username
    auth_protocol   VARCHAR(10) NULL,             -- MD5, SHA
    auth_password   NVARCHAR(256) NULL,           -- v3 auth password (encrypted in future)
    priv_protocol   VARCHAR(10) NULL,             -- DES, AES
    priv_password   NVARCHAR(256) NULL,           -- v3 privacy password (encrypted in future)
    targets         NVARCHAR(MAX) NULL,           -- JSON array of specific IPs
    enabled         BIT NOT NULL DEFAULT 1,
    created_at      DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    updated_at      DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT uq_snmp_config_org UNIQUE (organization_id)
);
GO

-- Discovered SNMP devices
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'snmp_devices')
CREATE TABLE snmp_devices (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    ip_address      VARCHAR(45) NOT NULL,
    sys_name        NVARCHAR(256) NULL,
    sys_descr       NVARCHAR(MAX) NULL,
    sys_uptime_sec  BIGINT NULL,
    sys_contact     NVARCHAR(256) NULL,
    sys_location    NVARCHAR(256) NULL,
    sys_object_id   VARCHAR(256) NULL,
    entity_model    NVARCHAR(256) NULL,
    entity_serial   NVARCHAR(256) NULL,
    entity_mfg      NVARCHAR(256) NULL,
    entity_firmware NVARCHAR(256) NULL,
    interface_count INT NULL,
    raw_data        NVARCHAR(MAX) NULL,           -- full JSON
    scanned_at      DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT uq_snmp_device_org_ip UNIQUE (organization_id, ip_address)
);
GO

-- Device interfaces
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'snmp_device_interfaces')
CREATE TABLE snmp_device_interfaces (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    device_id       INT NOT NULL REFERENCES snmp_devices(id) ON DELETE CASCADE,
    if_index        INT NOT NULL,
    name            NVARCHAR(256) NULL,
    description     NVARCHAR(256) NULL,
    if_type         INT NULL,
    speed_mbps      BIGINT NULL,
    mac_address     VARCHAR(20) NULL,
    admin_status    SMALLINT NULL,   -- 1=up, 2=down, 3=testing
    oper_status     SMALLINT NULL,   -- 1=up, 2=down
    in_octets       BIGINT NULL,
    out_octets      BIGINT NULL,
    in_errors       BIGINT NULL,
    out_errors      BIGINT NULL,
    in_discards     BIGINT NULL,
    out_discards    BIGINT NULL
);
GO

-- Indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_snmp_devices_org')
    CREATE INDEX ix_snmp_devices_org ON snmp_devices(organization_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_snmp_device_interfaces_device')
    CREATE INDEX ix_snmp_device_interfaces_device ON snmp_device_interfaces(device_id);
GO

PRINT 'SNMP infrastructure tables created successfully';
