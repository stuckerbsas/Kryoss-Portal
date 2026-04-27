-- 075_dc_health.sql
-- DC-02+03: AD Schema/Replication + FSMO role tracking

CREATE TABLE dc_health_snapshots (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    machine_id          UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    organization_id     UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    -- Schema
    schema_version      INT,
    schema_version_label NVARCHAR(50),
    forest_level        NVARCHAR(50),
    domain_level        NVARCHAR(50),
    forest_name         NVARCHAR(256),
    domain_name         NVARCHAR(256),
    -- FSMO roles (DN of role holder)
    schema_master       NVARCHAR(256),
    domain_naming_master NVARCHAR(256),
    pdc_emulator        NVARCHAR(256),
    rid_master          NVARCHAR(256),
    infrastructure_master NVARCHAR(256),
    fsmo_single_point   BIT DEFAULT 0,
    -- Replication summary
    repl_partner_count  INT DEFAULT 0,
    repl_failure_count  INT DEFAULT 0,
    last_successful_repl DATETIME2,
    -- Sites
    site_count          INT DEFAULT 0,
    subnet_count        INT DEFAULT 0,
    dc_count            INT DEFAULT 0,
    gc_count            INT DEFAULT 0,
    -- Metadata
    scanned_at          DATETIME2 DEFAULT SYSUTCDATETIME(),
    scanned_by          NVARCHAR(256)
);

CREATE INDEX ix_dc_health_org ON dc_health_snapshots(organization_id);
CREATE INDEX ix_dc_health_machine ON dc_health_snapshots(machine_id);

CREATE TABLE dc_replication_partners (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    snapshot_id         UNIQUEIDENTIFIER NOT NULL REFERENCES dc_health_snapshots(id) ON DELETE CASCADE,
    partner_hostname    NVARCHAR(256),
    partner_dn          NVARCHAR(512),
    direction           NVARCHAR(10),
    naming_context      NVARCHAR(512),
    last_success        DATETIME2,
    last_attempt        DATETIME2,
    failure_count       INT DEFAULT 0,
    last_error          NVARCHAR(512),
    transport           NVARCHAR(50)
);

CREATE INDEX ix_repl_partners_snapshot ON dc_replication_partners(snapshot_id);
