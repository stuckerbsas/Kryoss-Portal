-- 074_patch_compliance.sql
-- A-02: Patch Compliance — tracks Windows Update status per machine

CREATE TABLE machine_patch_status (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    update_source   NVARCHAR(20),       -- wsus, wufb, standalone, ninja, unknown
    wsus_server     NVARCHAR(512),
    wufb_ring       NVARCHAR(50),
    last_check_utc  DATETIME2,
    last_install_utc DATETIME2,
    reboot_pending  BIT DEFAULT 0,
    installed_count_30d INT DEFAULT 0,
    installed_count_90d INT DEFAULT 0,
    compliance_score INT DEFAULT 0,     -- 0-100
    ninja_managed   BIT DEFAULT 0,
    wu_service_status NVARCHAR(20),     -- running, stopped, disabled
    updated_at      DATETIME2 DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_patch_status_machine UNIQUE(machine_id)
);

CREATE INDEX ix_patch_status_org ON machine_patch_status(organization_id);

CREATE TABLE machine_patches (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    hotfix_id       NVARCHAR(20) NOT NULL,
    description     NVARCHAR(256),
    installed_on    DATETIME2,
    installed_by    NVARCHAR(128),
    created_at      DATETIME2 DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_machine_patches_machine ON machine_patches(machine_id);
CREATE INDEX ix_machine_patches_org ON machine_patches(organization_id);
CREATE UNIQUE INDEX uq_machine_patches_hotfix ON machine_patches(machine_id, hotfix_id);
