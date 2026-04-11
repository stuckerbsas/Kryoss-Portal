SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 003_cmdb.sql
-- Kryoss Platform — CMDB: Machines, Software, Network, Users, Certs
-- Depends on: 002_core.sql (organizations, sites)
-- =============================================

-- =============================================
-- MACHINES: Agent-managed endpoints
-- =============================================
CREATE TABLE machines (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    site_id         UNIQUEIDENTIFIER REFERENCES sites(id),
    agent_id        UNIQUEIDENTIFIER NOT NULL UNIQUE,  -- agent registration GUID
    hostname        NVARCHAR(255)  NOT NULL,
    -- OS
    os_name         NVARCHAR(255),                     -- Windows 11 Pro
    os_version      VARCHAR(50),                       -- 23H2
    os_build        VARCHAR(50),                       -- 22631.3880
    -- Hardware
    manufacturer    NVARCHAR(255),                     -- Dell, Lenovo, HP
    model           NVARCHAR(255),                     -- OptiPlex 7090
    serial_number   VARCHAR(255),
    cpu_name        NVARCHAR(255),                     -- Intel Core i7-12700
    cpu_cores       SMALLINT,
    ram_gb          SMALLINT,
    disk_type       VARCHAR(10)
        CONSTRAINT ck_machines_disk CHECK (disk_type IN ('SSD', 'HDD', 'NVMe', 'Unknown')),
    disk_size_gb    INT,
    disk_free_gb    DECIMAL(8,2),
    -- Security hardware
    tpm_present     BIT,
    tpm_version     VARCHAR(10),
    secure_boot     BIT,
    bitlocker       BIT,
    -- Network
    ip_address      VARCHAR(45),                       -- IPv4 or IPv6
    mac_address     VARCHAR(17),                       -- AA:BB:CC:DD:EE:FF
    -- Identity
    domain_status   VARCHAR(40)
        CONSTRAINT ck_machines_domain CHECK (domain_status IN ('DomainJoined', 'AzureADJoined', 'HybridJoined', 'Workgroup')),
    domain_name     NVARCHAR(255),
    -- Lifecycle
    system_age_days INT,
    last_boot_at    DATETIME2(2),
    last_seen_at    DATETIME2(2),
    first_seen_at   DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    is_active       BIT            NOT NULL DEFAULT 1,
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2),
    -- Unique hostname per org
    CONSTRAINT uq_machines_org_hostname UNIQUE (organization_id, hostname)
);

CREATE INDEX ix_machines_org      ON machines(organization_id) WHERE is_active = 1 AND deleted_at IS NULL;
CREATE INDEX ix_machines_site     ON machines(site_id) WHERE site_id IS NOT NULL;
CREATE INDEX ix_machines_lastseen ON machines(organization_id, last_seen_at DESC) WHERE is_active = 1;

-- =============================================
-- MACHINE_SNAPSHOTS: Hardware state per assessment run
-- =============================================
CREATE TABLE machine_snapshots (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    captured_at     DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    ram_gb          SMALLINT,
    disk_size_gb    INT,
    disk_free_gb    DECIMAL(8,2),
    os_build        VARCHAR(50),
    ip_address      VARCHAR(45),
    extended_data   NVARCHAR(MAX)                      -- JSON for less-queried fields
        CONSTRAINT ck_snapshots_json CHECK (ISJSON(extended_data) = 1 OR extended_data IS NULL)
);

CREATE INDEX ix_snapshots_machine ON machine_snapshots(machine_id, captured_at DESC);

-- =============================================
-- SOFTWARE: Normalized software catalog
-- =============================================
CREATE TABLE software (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    name            NVARCHAR(500)  NOT NULL,
    publisher       NVARCHAR(255),
    category        VARCHAR(50),                       -- Productivity, Security, Browser...
    is_blacklisted  BIT            NOT NULL DEFAULT 0,
    is_eol          BIT            NOT NULL DEFAULT 0,
    eol_date        DATE,
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2),
    CONSTRAINT uq_software_name_pub UNIQUE (name, publisher)
);

-- =============================================
-- MACHINE_SOFTWARE: M:N machine <-> software
-- =============================================
CREATE TABLE machine_software (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    software_id     INT            NOT NULL REFERENCES software(id),
    version         NVARCHAR(100),
    install_date    DATE,
    detected_at     DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    removed_at      DATETIME2(2),                      -- NULL = still installed
    CONSTRAINT uq_machsw UNIQUE (machine_id, software_id, version)
);

CREATE INDEX ix_machsw_software ON machine_software(software_id);
CREATE INDEX ix_machsw_active   ON machine_software(machine_id) WHERE removed_at IS NULL;

-- =============================================
-- NETWORK_DEVICE_TYPES: Lookup table
-- =============================================
CREATE TABLE network_device_types (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    name            VARCHAR(50)    NOT NULL UNIQUE
    -- Router, Switch, Firewall, AccessPoint, Printer, NAS, UPS, Camera, Unknown
);

-- =============================================
-- NETWORK_DEVICES: Agentless discovered devices
-- =============================================
CREATE TABLE network_devices (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    site_id         UNIQUEIDENTIFIER REFERENCES sites(id),
    device_type_id  INT            NOT NULL REFERENCES network_device_types(id),
    ip_address      VARCHAR(45)    NOT NULL,
    mac_address     VARCHAR(17),
    hostname        NVARCHAR(255),
    manufacturer    NVARCHAR(255),                     -- from OUI / SNMP
    model           NVARCHAR(255),
    firmware_version VARCHAR(100),
    discovery_method VARCHAR(20)
        CONSTRAINT ck_netdev_method CHECK (discovery_method IN ('ARP', 'Ping', 'SNMP', 'NetBIOS', 'Manual')),
    is_managed      BIT            NOT NULL DEFAULT 0,
    first_seen_at   DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    last_seen_at    DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    is_active       BIT            NOT NULL DEFAULT 1,
    notes           NVARCHAR(MAX),
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_netdev_org ON network_devices(organization_id) WHERE is_active = 1 AND deleted_at IS NULL;
CREATE UNIQUE INDEX ux_netdev_mac ON network_devices(organization_id, mac_address) WHERE mac_address IS NOT NULL AND deleted_at IS NULL;

-- =============================================
-- MACHINE_USERS: Local/AD users detected by agent
-- =============================================
CREATE TABLE machine_users (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    username        NVARCHAR(255)  NOT NULL,
    full_name       NVARCHAR(255),
    source          VARCHAR(20)    NOT NULL DEFAULT 'Local'
        CONSTRAINT ck_machuser_source CHECK (source IN ('Local', 'ActiveDirectory', 'AzureAD')),
    is_admin        BIT            NOT NULL DEFAULT 0,
    is_enabled      BIT            NOT NULL DEFAULT 1,
    is_builtin      BIT            NOT NULL DEFAULT 0, -- Administrator, Guest
    last_logon_at   DATETIME2(2),
    password_last_set       DATETIME2(2),
    password_never_expires  BIT NOT NULL DEFAULT 0,
    is_stale        BIT            NOT NULL DEFAULT 0, -- >90 days no logon
    detected_at     DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    removed_at      DATETIME2(2),
    CONSTRAINT uq_machuser UNIQUE (machine_id, username)
);

CREATE INDEX ix_machuser_admin ON machine_users(machine_id) WHERE is_admin = 1 AND removed_at IS NULL;

-- =============================================
-- CERTIFICATES: Machine certificate store
-- =============================================
CREATE TABLE certificates (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    thumbprint      VARCHAR(64)    NOT NULL,
    subject         NVARCHAR(500)  NOT NULL,
    issuer          NVARCHAR(500),
    store_name      VARCHAR(50),                       -- My, Root, CA, TrustedPublisher
    store_location  VARCHAR(30),                       -- LocalMachine, CurrentUser
    not_before      DATETIME2(2),
    not_after       DATETIME2(2)   NOT NULL,           -- expiration
    key_algorithm   VARCHAR(20),                       -- RSA, ECC
    key_length      SMALLINT,                          -- 2048, 4096
    is_self_signed  BIT            NOT NULL DEFAULT 0,
    detected_at     DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    removed_at      DATETIME2(2),
    CONSTRAINT uq_cert UNIQUE (machine_id, thumbprint, store_name)
);

CREATE INDEX ix_certs_expiry ON certificates(not_after) WHERE removed_at IS NULL;
