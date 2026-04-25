-- 043: Infrastructure Assessment scaffold (IA-0)
-- 6 tables for hybrid on-prem + cloud + multi-site assessment

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'infra_assessment_scans')
CREATE TABLE infra_assessment_scans (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    status          NVARCHAR(30)     NOT NULL DEFAULT 'pending', -- pending|running|completed|failed
    scope           NVARCHAR(MAX)    NULL, -- JSON: site names, device types, etc.
    overall_health  DECIMAL(5,2)     NULL, -- 0-100
    site_count      INT              NOT NULL DEFAULT 0,
    device_count    INT              NOT NULL DEFAULT 0,
    finding_count   INT              NOT NULL DEFAULT 0,
    started_at      DATETIME2        NULL,
    completed_at    DATETIME2        NULL,
    created_at      DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'infra_assessment_sites')
CREATE TABLE infra_assessment_sites (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    scan_id         UNIQUEIDENTIFIER NOT NULL REFERENCES infra_assessment_scans(id) ON DELETE CASCADE,
    site_name       NVARCHAR(200)    NOT NULL,
    location        NVARCHAR(500)    NULL,
    site_type       NVARCHAR(30)     NOT NULL DEFAULT 'branch', -- hq|branch|remote|industrial|datacenter|cloud
    device_count    INT              NOT NULL DEFAULT 0,
    user_count      INT              NOT NULL DEFAULT 0,
    connectivity_type NVARCHAR(100)  NULL, -- mpls|sdwan|ipsec|internet|cellular|satellite
    created_at      DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'infra_assessment_devices')
CREATE TABLE infra_assessment_devices (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    scan_id         UNIQUEIDENTIFIER NOT NULL REFERENCES infra_assessment_scans(id) ON DELETE CASCADE,
    site_id         UNIQUEIDENTIFIER NULL REFERENCES infra_assessment_sites(id),
    hostname        NVARCHAR(255)    NULL,
    device_type     NVARCHAR(30)     NOT NULL DEFAULT 'server', -- server|switch|router|firewall|ap|printer|ups|hvac|plc|iot
    vendor          NVARCHAR(200)    NULL,
    model           NVARCHAR(200)    NULL,
    role            NVARCHAR(200)    NULL,
    ip_address      NVARCHAR(50)     NULL,
    os              NVARCHAR(200)    NULL,
    firmware        NVARCHAR(200)    NULL,
    serial_number   NVARCHAR(200)    NULL,
    created_at      DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'infra_assessment_connectivity')
CREATE TABLE infra_assessment_connectivity (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    scan_id         UNIQUEIDENTIFIER NOT NULL REFERENCES infra_assessment_scans(id) ON DELETE CASCADE,
    site_a_id       UNIQUEIDENTIFIER NOT NULL REFERENCES infra_assessment_sites(id),
    site_b_id       UNIQUEIDENTIFIER NOT NULL REFERENCES infra_assessment_sites(id),
    link_type       NVARCHAR(30)     NOT NULL DEFAULT 'internet', -- mpls|sdwan|ipsec|expressroute|leased|internet|cellular|satellite
    bandwidth_mbps  DECIMAL(12,2)    NULL,
    latency_ms      DECIMAL(8,2)     NULL,
    uptime_pct      DECIMAL(5,2)     NULL,
    cost_monthly_usd DECIMAL(12,2)   NULL,
    created_at      DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'infra_assessment_capacity')
CREATE TABLE infra_assessment_capacity (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    scan_id         UNIQUEIDENTIFIER NOT NULL REFERENCES infra_assessment_scans(id) ON DELETE CASCADE,
    device_id       UNIQUEIDENTIFIER NULL REFERENCES infra_assessment_devices(id),
    metric_key      NVARCHAR(100)    NOT NULL, -- cpu_pct|ram_pct|disk_pct|bandwidth_pct
    current_value   DECIMAL(12,2)    NULL,
    peak_value      DECIMAL(12,2)    NULL,
    threshold       DECIMAL(12,2)    NULL,
    trend_direction NVARCHAR(20)     NOT NULL DEFAULT 'stable', -- stable|increasing|decreasing
    created_at      DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'infra_assessment_findings')
CREATE TABLE infra_assessment_findings (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    scan_id         UNIQUEIDENTIFIER NOT NULL REFERENCES infra_assessment_scans(id) ON DELETE CASCADE,
    area            NVARCHAR(30)     NOT NULL, -- hardware|network|connectivity|capacity|ot|migration
    service         NVARCHAR(200)    NULL,
    feature         NVARCHAR(200)    NULL,
    status          NVARCHAR(30)     NOT NULL DEFAULT 'warning', -- pass|warning|fail|info
    priority        NVARCHAR(20)     NOT NULL DEFAULT 'medium', -- critical|high|medium|low|info
    observation     NVARCHAR(MAX)    NULL,
    recommendation  NVARCHAR(MAX)    NULL,
    link_text       NVARCHAR(500)    NULL,
    link_url        NVARCHAR(2000)   NULL,
    created_at      DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

-- Indexes
CREATE NONCLUSTERED INDEX IX_ia_scans_org ON infra_assessment_scans(organization_id, created_at DESC);
CREATE NONCLUSTERED INDEX IX_ia_sites_scan ON infra_assessment_sites(scan_id);
CREATE NONCLUSTERED INDEX IX_ia_devices_scan ON infra_assessment_devices(scan_id);
CREATE NONCLUSTERED INDEX IX_ia_connectivity_scan ON infra_assessment_connectivity(scan_id);
CREATE NONCLUSTERED INDEX IX_ia_capacity_scan ON infra_assessment_capacity(scan_id);
CREATE NONCLUSTERED INDEX IX_ia_findings_scan ON infra_assessment_findings(scan_id);
