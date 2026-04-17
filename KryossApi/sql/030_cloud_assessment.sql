-- 030_cloud_assessment.sql
-- Cloud Assessment Platform — foundation schema (Phase CA-0 scaffold)
-- Separate from Copilot Readiness (029). All tables prefixed cloud_assessment_*.

CREATE TABLE cloud_assessment_scans (
    id                       UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id          UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    tenant_id                UNIQUEIDENTIFIER NULL REFERENCES m365_tenants(id) ON DELETE CASCADE,
    azure_subscription_ids   NVARCHAR(MAX),
    status                   VARCHAR(20)      NOT NULL DEFAULT 'running',
    overall_score            DECIMAL(3,2),
    area_scores              NVARCHAR(MAX),
    verdict                  VARCHAR(20),
    pipeline_status          NVARCHAR(MAX),
    started_at               DATETIME2(2)     NOT NULL,
    completed_at             DATETIME2(2),
    created_at               DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_cloud_assessment_scans_org ON cloud_assessment_scans(organization_id, created_at DESC);
CREATE INDEX ix_cloud_assessment_scans_tenant ON cloud_assessment_scans(tenant_id) WHERE tenant_id IS NOT NULL;

CREATE TABLE cloud_assessment_findings (
    id               BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id          UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
    area             VARCHAR(30)      NOT NULL,
    service          VARCHAR(30)      NOT NULL,
    feature          NVARCHAR(200)    NOT NULL,
    status           VARCHAR(30)      NOT NULL,
    priority         VARCHAR(10)      NOT NULL DEFAULT '',
    observation      NVARCHAR(MAX),
    recommendation   NVARCHAR(MAX),
    link_text        NVARCHAR(500),
    link_url         NVARCHAR(500),
    created_at       DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_cloud_assessment_findings_scan ON cloud_assessment_findings(scan_id, area, service);

CREATE TABLE cloud_assessment_metrics (
    id               BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id          UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
    area             VARCHAR(30)      NOT NULL,
    metric_key       VARCHAR(100)     NOT NULL,
    metric_value     NVARCHAR(500)    NOT NULL,
    created_at       DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_cloud_assessment_metrics_scan ON cloud_assessment_metrics(scan_id, area);

CREATE TABLE cloud_assessment_azure_subscriptions (
    id                BIGINT IDENTITY(1,1) PRIMARY KEY,
    organization_id   UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    subscription_id   VARCHAR(64)      NOT NULL,
    display_name      NVARCHAR(200),
    state             VARCHAR(30),
    tenant_id         VARCHAR(64),
    consent_state     VARCHAR(30),
    connected_at      DATETIME2(2),
    created_at        DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_cloud_azure_sub UNIQUE (organization_id, subscription_id)
);

CREATE TABLE cloud_assessment_licenses (
    id               BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id          UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
    sku_part_number  VARCHAR(100)     NOT NULL,
    friendly_name    NVARCHAR(200),
    purchased        INT              NOT NULL DEFAULT 0,
    assigned         INT              NOT NULL DEFAULT 0,
    available        INT              NOT NULL DEFAULT 0,
    created_at       DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_cloud_assessment_licenses_scan ON cloud_assessment_licenses(scan_id);

CREATE TABLE cloud_assessment_adoption (
    id               BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id          UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
    area             VARCHAR(30)      NOT NULL,
    service_name     VARCHAR(100)     NOT NULL,
    licensed_count   INT              NOT NULL DEFAULT 0,
    active_30d       INT              NOT NULL DEFAULT 0,
    adoption_rate    DECIMAL(5,2)     NOT NULL DEFAULT 0,
    created_at       DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_cloud_assessment_adoption_scan ON cloud_assessment_adoption(scan_id);

CREATE TABLE cloud_assessment_wasted_licenses (
    id                    BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id               UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
    user_principal        NVARCHAR(500)    NOT NULL,
    display_name          NVARCHAR(500),
    sku                   VARCHAR(100),
    last_sign_in          DATETIME2(2),
    days_inactive         INT,
    estimated_cost_year   DECIMAL(10,2),
    created_at            DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_cloud_assessment_wasted_scan ON cloud_assessment_wasted_licenses(scan_id);

CREATE TABLE cloud_assessment_finding_status (
    id               BIGINT IDENTITY(1,1) PRIMARY KEY,
    organization_id  UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    area             VARCHAR(30)      NOT NULL,
    service          VARCHAR(30)      NOT NULL,
    feature          NVARCHAR(200)    NOT NULL,
    status           VARCHAR(20)      NOT NULL,
    owner_user_id    UNIQUEIDENTIFIER NULL REFERENCES users(id),
    notes            NVARCHAR(MAX),
    updated_at       DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_by       UNIQUEIDENTIFIER NULL REFERENCES users(id),
    CONSTRAINT uq_cloud_finding_status UNIQUE (organization_id, area, service, feature)
);
