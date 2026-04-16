-- 029_copilot_readiness.sql
-- Copilot Readiness Assessment tables (Phase 5)

CREATE TABLE copilot_readiness_scans (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id     UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    tenant_id           UNIQUEIDENTIFIER NOT NULL REFERENCES m365_tenants(id) ON DELETE CASCADE,
    status              VARCHAR(20)      NOT NULL DEFAULT 'running',
    d1_score            DECIMAL(3,2),
    d2_score            DECIMAL(3,2),
    d3_score            DECIMAL(3,2),
    d4_score            DECIMAL(3,2),
    d5_score            DECIMAL(3,2),
    d6_score            DECIMAL(3,2),
    overall_score       DECIMAL(3,2),
    verdict             VARCHAR(20),
    pipeline_status     NVARCHAR(MAX),
    started_at          DATETIME2(2)     NOT NULL,
    completed_at        DATETIME2(2),
    created_at          DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_copilot_scans_org ON copilot_readiness_scans(organization_id, created_at DESC);

CREATE TABLE copilot_readiness_metrics (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    dimension           VARCHAR(10)      NOT NULL,
    metric_key          VARCHAR(100)     NOT NULL,
    metric_value        NVARCHAR(500)    NOT NULL,
    created_at          DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_copilot_metrics_scan ON copilot_readiness_metrics(scan_id);

CREATE TABLE copilot_readiness_findings (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    service             VARCHAR(30)      NOT NULL,
    feature             NVARCHAR(200)    NOT NULL,
    status              VARCHAR(30)      NOT NULL,
    priority            VARCHAR(10)      NOT NULL DEFAULT '',
    observation         NVARCHAR(MAX),
    recommendation      NVARCHAR(MAX),
    link_text           NVARCHAR(500),
    link_url            NVARCHAR(500),
    created_at          DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_copilot_findings_scan ON copilot_readiness_findings(scan_id, service);

CREATE TABLE copilot_readiness_sharepoint (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    site_url            NVARCHAR(500)    NOT NULL,
    site_title          NVARCHAR(500),
    total_files         INT              NOT NULL DEFAULT 0,
    labeled_files       INT              NOT NULL DEFAULT 0,
    overshared_files    INT              NOT NULL DEFAULT 0,
    risk_level          VARCHAR(10),
    top_labels          NVARCHAR(MAX),
    created_at          DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_copilot_sp_scan ON copilot_readiness_sharepoint(scan_id);

CREATE TABLE copilot_readiness_external_users (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    user_principal      NVARCHAR(500)    NOT NULL,
    display_name        NVARCHAR(500),
    email_domain        NVARCHAR(200),
    last_sign_in        DATETIME2(2),
    risk_level          VARCHAR(10),
    sites_accessed      INT              NOT NULL DEFAULT 0,
    highest_permission  NVARCHAR(50),
    created_at          DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_copilot_ext_scan ON copilot_readiness_external_users(scan_id);
