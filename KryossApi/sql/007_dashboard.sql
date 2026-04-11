SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 007_dashboard.sql
-- Kryoss Platform — Dashboard Summary Tables
-- Refreshed by cron/trigger after each assessment or monthly
-- Depends on: 003_cmdb.sql (machines), 004_assessment.sql (runs, frameworks)
-- =============================================

-- =============================================
-- LATEST_MACHINE_ASSESSMENT: Most recent run per machine
-- Updated after each POST /results
-- =============================================
CREATE TABLE latest_machine_assessment (
    machine_id      UNIQUEIDENTIFIER PRIMARY KEY REFERENCES machines(id),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    run_id          UNIQUEIDENTIFIER NOT NULL REFERENCES assessment_runs(id),
    global_score    DECIMAL(5,2),
    grade           VARCHAR(10),
    pass_count      SMALLINT,
    warn_count      SMALLINT,
    fail_count      SMALLINT,
    run_date        DATETIME2(2)   NOT NULL,
    updated_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_lma_org ON latest_machine_assessment(organization_id);

-- =============================================
-- ORG_COMPLIANCE_MONTHLY: Monthly compliance per framework per org
-- Generated automatically on the 1st of each month
-- =============================================
CREATE TABLE org_compliance_monthly (
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    framework_id    INT            NOT NULL REFERENCES frameworks(id),
    year_month      CHAR(7)        NOT NULL,           -- '2026-04'
    avg_score       DECIMAL(5,2)   NOT NULL,
    machine_count   INT            NOT NULL,
    pass_rate       DECIMAL(5,2)   NOT NULL,
    critical_vulns  INT            NOT NULL DEFAULT 0,
    high_vulns      INT            NOT NULL DEFAULT 0,
    calculated_at   DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT pk_org_compliance PRIMARY KEY (organization_id, framework_id, year_month)
);

-- =============================================
-- CONTROL_FAILURE_SUMMARY: Heatmap of failing controls per org
-- Which controls fail the most across the fleet
-- =============================================
CREATE TABLE control_failure_summary (
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    control_def_id  INT            NOT NULL REFERENCES control_defs(id),
    year_month      CHAR(7)        NOT NULL,
    fail_count      INT            NOT NULL DEFAULT 0,
    warn_count      INT            NOT NULL DEFAULT 0,
    total_machines  INT            NOT NULL DEFAULT 0,
    calculated_at   DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT pk_ctrl_failure PRIMARY KEY (organization_id, control_def_id, year_month)
);

-- =============================================
-- ORG_FLEET_SUMMARY: High-level KPIs per organization
-- For MSP cross-client dashboard
-- =============================================
CREATE TABLE org_fleet_summary (
    organization_id     UNIQUEIDENTIFIER PRIMARY KEY REFERENCES organizations(id),
    total_machines      INT            NOT NULL DEFAULT 0,
    active_machines     INT            NOT NULL DEFAULT 0,
    total_network_devices INT          NOT NULL DEFAULT 0,
    avg_global_score    DECIMAL(5,2),
    critical_vuln_count INT            NOT NULL DEFAULT 0,
    high_vuln_count     INT            NOT NULL DEFAULT 0,
    expiring_certs_30d  INT            NOT NULL DEFAULT 0,
    admin_user_count    INT            NOT NULL DEFAULT 0,
    software_count      INT            NOT NULL DEFAULT 0,
    last_assessment_at  DATETIME2(2),
    calculated_at       DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME()
);
