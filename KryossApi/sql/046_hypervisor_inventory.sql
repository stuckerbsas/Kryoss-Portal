-- 046: Hypervisor & VM Inventory (IA-1)
-- Adds: infra_hypervisor_configs (connection credentials per org),
--        infra_hypervisors (discovered hosts), infra_vms (per-VM inventory)

-- Connection configs (vCenter, Proxmox — encrypted secrets)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'infra_hypervisor_configs')
CREATE TABLE infra_hypervisor_configs (
    id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    platform        NVARCHAR(20)     NOT NULL, -- vmware, hyperv, proxmox
    display_name    NVARCHAR(200)    NULL,
    host_url        NVARCHAR(500)    NOT NULL, -- https://vcenter.local, https://proxmox:8006
    username        NVARCHAR(200)    NULL,
    encrypted_password NVARCHAR(MAX) NULL,     -- AES-encrypted at rest
    api_token       NVARCHAR(MAX)    NULL,     -- for Proxmox token auth
    verify_ssl      BIT              NOT NULL DEFAULT 1,
    is_active       BIT              NOT NULL DEFAULT 1,
    last_tested_at  DATETIME2        NULL,
    last_test_ok    BIT              NULL,
    last_error      NVARCHAR(500)    NULL,
    created_at      DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    updated_at      DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);
GO

-- Discovered hypervisor hosts
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'infra_hypervisors')
CREATE TABLE infra_hypervisors (
    id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    scan_id         UNIQUEIDENTIFIER NOT NULL REFERENCES infra_assessment_scans(id) ON DELETE CASCADE,
    config_id       UNIQUEIDENTIFIER NULL REFERENCES infra_hypervisor_configs(id),
    site_id         UNIQUEIDENTIFIER NULL REFERENCES infra_assessment_sites(id),
    platform        NVARCHAR(20)     NOT NULL, -- vmware, hyperv, proxmox
    host_fqdn       NVARCHAR(300)    NOT NULL,
    version         NVARCHAR(100)    NULL,
    cluster_name    NVARCHAR(200)    NULL,
    cpu_cores_total INT              NULL,
    ram_gb_total    DECIMAL(10,2)    NULL,
    storage_gb_total DECIMAL(12,2)   NULL,
    cpu_usage_pct   DECIMAL(5,2)     NULL,
    ram_usage_pct   DECIMAL(5,2)     NULL,
    vm_count        INT              NOT NULL DEFAULT 0,
    vm_running      INT              NOT NULL DEFAULT 0,
    ha_enabled      BIT              NULL,
    power_state     NVARCHAR(20)     NOT NULL DEFAULT 'on', -- on, standby, maintenance
    created_at      DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);
GO

-- Per-VM inventory
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'infra_vms')
CREATE TABLE infra_vms (
    id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    scan_id         UNIQUEIDENTIFIER NOT NULL REFERENCES infra_assessment_scans(id) ON DELETE CASCADE,
    hypervisor_id   UNIQUEIDENTIFIER NOT NULL REFERENCES infra_hypervisors(id) ON DELETE NO ACTION,
    vm_name         NVARCHAR(300)    NOT NULL,
    os              NVARCHAR(200)    NULL,
    power_state     NVARCHAR(20)     NOT NULL DEFAULT 'on', -- on, off, suspended, paused
    cpu_cores       INT              NULL,
    ram_gb          DECIMAL(10,2)    NULL,
    disk_gb         DECIMAL(12,2)    NULL,
    cpu_avg_pct     DECIMAL(5,2)     NULL,
    ram_avg_pct     DECIMAL(5,2)     NULL,
    disk_used_pct   DECIMAL(5,2)     NULL,
    snapshot_count  INT              NOT NULL DEFAULT 0,
    oldest_snapshot_days INT         NULL,
    last_backup     DATETIME2        NULL,
    last_login      DATETIME2        NULL,
    ip_address      NVARCHAR(50)     NULL,
    tools_status    NVARCHAR(50)     NULL, -- ok, outdated, not_installed
    is_template     BIT              NOT NULL DEFAULT 0,
    is_idle         BIT              NOT NULL DEFAULT 0,
    notes           NVARCHAR(500)    NULL,
    created_at      DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);
GO

-- Indexes (idempotent)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_infra_hypervisor_configs_org')
    CREATE INDEX IX_infra_hypervisor_configs_org ON infra_hypervisor_configs(organization_id) WHERE is_active = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_infra_hypervisors_scan')
    CREATE INDEX IX_infra_hypervisors_scan ON infra_hypervisors(scan_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_infra_vms_scan')
    CREATE INDEX IX_infra_vms_scan ON infra_vms(scan_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_infra_vms_hypervisor')
    CREATE INDEX IX_infra_vms_hypervisor ON infra_vms(hypervisor_id);
GO
