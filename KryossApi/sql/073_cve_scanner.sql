-- 073_cve_scanner.sql — CVE Scanner tables
-- A-01: Server-side CVE matching against installed software

-- Known CVE entries (seeded + NVD-enriched)
CREATE TABLE cve_entries (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    cve_id          NVARCHAR(20) NOT NULL,           -- CVE-2024-12345
    product_pattern NVARCHAR(256) NOT NULL,           -- regex or LIKE pattern for software name
    vendor          NVARCHAR(128) NULL,
    affected_below  NVARCHAR(64) NULL,                -- versions below this are affected
    affected_above  NVARCHAR(64) NULL,                -- versions above this are affected (rare)
    fixed_version   NVARCHAR(64) NULL,                -- first patched version
    severity        NVARCHAR(16) NOT NULL DEFAULT 'medium', -- critical, high, medium, low
    cvss_score      DECIMAL(3,1) NULL,
    description     NVARCHAR(1024) NULL,
    cwe_id          NVARCHAR(20) NULL,
    published_at    DATETIME2 NULL,
    source          NVARCHAR(32) NOT NULL DEFAULT 'builtin', -- builtin, nvd, manual
    created_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT UQ_cve_entries_cve_product UNIQUE (cve_id, product_pattern)
);
CREATE INDEX IX_cve_entries_product ON cve_entries (product_pattern);
CREATE INDEX IX_cve_entries_severity ON cve_entries (severity);

-- Per-machine CVE findings (regenerated on each scan)
CREATE TABLE machine_cve_findings (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    machine_id      UNIQUEIDENTIFIER NOT NULL,
    organization_id UNIQUEIDENTIFIER NOT NULL,
    run_id          UNIQUEIDENTIFIER NULL,
    cve_id          NVARCHAR(20) NOT NULL,
    software_name   NVARCHAR(512) NOT NULL,
    software_version NVARCHAR(128) NULL,
    installed_version NVARCHAR(128) NULL,
    fixed_version   NVARCHAR(64) NULL,
    severity        NVARCHAR(16) NOT NULL,
    cvss_score      DECIMAL(3,1) NULL,
    description     NVARCHAR(1024) NULL,
    status          NVARCHAR(16) NOT NULL DEFAULT 'open', -- open, patched, ignored
    found_at        DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    resolved_at     DATETIME2 NULL,

    CONSTRAINT FK_mcf_machine FOREIGN KEY (machine_id) REFERENCES machines(id),
    CONSTRAINT FK_mcf_org FOREIGN KEY (organization_id) REFERENCES organizations(id)
);
CREATE INDEX IX_mcf_org ON machine_cve_findings (organization_id);
CREATE INDEX IX_mcf_machine ON machine_cve_findings (machine_id);
CREATE INDEX IX_mcf_severity ON machine_cve_findings (severity);
CREATE INDEX IX_mcf_cve ON machine_cve_findings (cve_id);

-- NVD sync tracking
CREATE TABLE cve_sync_log (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    synced_at       DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    entries_added   INT NOT NULL DEFAULT 0,
    entries_updated INT NOT NULL DEFAULT 0,
    source          NVARCHAR(32) NOT NULL DEFAULT 'nvd',
    status          NVARCHAR(16) NOT NULL DEFAULT 'success',
    error_message   NVARCHAR(512) NULL
);

PRINT '073_cve_scanner.sql applied';
GO
