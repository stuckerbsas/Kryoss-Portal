SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 008_tags_future.sql
-- Kryoss Platform — Tags + Future Extensions (M365, Backup)
-- Depends on: 002_core.sql (organizations), 003_cmdb.sql (machines, network_devices)
-- =============================================

-- =============================================
-- TAGS: Flexible labeling for machines + network devices
-- =============================================
CREATE TABLE tags (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    name            NVARCHAR(100)  NOT NULL,
    color           CHAR(7),                           -- #FF5733
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2),
    CONSTRAINT uq_tag_org_name UNIQUE (organization_id, name)
);

-- =============================================
-- MACHINE_TAGS: M:N machine <-> tag
-- =============================================
CREATE TABLE machine_tags (
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    tag_id          INT            NOT NULL REFERENCES tags(id),
    CONSTRAINT pk_machine_tags PRIMARY KEY (machine_id, tag_id)
);

-- =============================================
-- NETWORK_DEVICE_TAGS: M:N network_device <-> tag
-- =============================================
CREATE TABLE network_device_tags (
    network_device_id UNIQUEIDENTIFIER NOT NULL REFERENCES network_devices(id),
    tag_id            INT            NOT NULL REFERENCES tags(id),
    CONSTRAINT pk_netdev_tags PRIMARY KEY (network_device_id, tag_id)
);

-- =============================================
-- M365_LICENSES: Microsoft 365 license tracking per org
-- Synced via MS Graph API
-- =============================================
CREATE TABLE m365_licenses (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    sku_id          UNIQUEIDENTIFIER NOT NULL,
    sku_part_number VARCHAR(100)   NOT NULL,            -- ENTERPRISEPACK, SPE_E3
    friendly_name   NVARCHAR(255),
    total_units     INT            NOT NULL DEFAULT 0,
    consumed_units  INT            NOT NULL DEFAULT 0,
    synced_at       DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_m365_org_sku UNIQUE (organization_id, sku_id)
);

-- =============================================
-- BACKUP_STATUS: Backup product status per machine
-- =============================================
CREATE TABLE backup_status (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    backup_product  NVARCHAR(100),                     -- Veeam, Datto, Windows Backup
    last_backup_at  DATETIME2(2),
    backup_result   VARCHAR(20)
        CONSTRAINT ck_backup_result CHECK (backup_result IN ('success', 'warning', 'failed', 'unknown')),
    backup_size_gb  DECIMAL(10,2),
    detected_at     DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_backup_machine ON backup_status(machine_id);
