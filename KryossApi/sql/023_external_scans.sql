-- 023_external_scans.sql
-- External port scan / pentest feature: cloud-side scan of public IPs

CREATE TABLE external_scans (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    target          NVARCHAR(255) NOT NULL,    -- domain or IP entered
    status          VARCHAR(20) NOT NULL DEFAULT 'pending', -- pending, running, completed, failed
    started_at      DATETIME2(2),
    completed_at    DATETIME2(2),
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE external_scan_results (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id         UNIQUEIDENTIFIER NOT NULL REFERENCES external_scans(id) ON DELETE CASCADE,
    ip_address      VARCHAR(45) NOT NULL,
    port            INT NOT NULL,
    protocol        VARCHAR(5) NOT NULL DEFAULT 'TCP',
    status          VARCHAR(20) NOT NULL,      -- open, closed, filtered
    service         VARCHAR(50),
    risk            VARCHAR(20),               -- critical, high, medium, info
    banner          NVARCHAR(500),             -- service banner if captured
    detail          NVARCHAR(500)
);

CREATE INDEX ix_ext_scans_org ON external_scans(organization_id, created_at DESC);
CREATE INDEX ix_ext_results_scan ON external_scan_results(scan_id);
