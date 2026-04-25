-- 038_cloud_assessment_benchmarks.sql
-- CA-11: Benchmarks — industry tagging + benchmark comparison snapshots.

-- Add industry tagging to organizations.
IF COL_LENGTH('organizations','industry_code') IS NULL
    ALTER TABLE organizations ADD industry_code NVARCHAR(30) NULL;
GO
IF COL_LENGTH('organizations','industry_subcode') IS NULL
    ALTER TABLE organizations ADD industry_subcode NVARCHAR(50) NULL;
GO
IF COL_LENGTH('organizations','employee_count_band') IS NULL
    ALTER TABLE organizations ADD employee_count_band NVARCHAR(20) NULL;
GO

-- Per-franchise opt-out from contributing to global anonymized aggregates.
IF COL_LENGTH('franchises','benchmark_opt_in') IS NULL
    ALTER TABLE franchises ADD benchmark_opt_in BIT NOT NULL CONSTRAINT DF_franchises_benchmark_opt_in DEFAULT 1;
GO

-- Industry benchmark catalog (static seed + periodic refresh).
IF OBJECT_ID('cloud_assessment_industry_benchmarks', 'U') IS NULL
CREATE TABLE cloud_assessment_industry_benchmarks (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    industry_code   NVARCHAR(30)   NOT NULL,
    employee_band   NVARCHAR(20)   NULL,
    metric_key      NVARCHAR(100)  NOT NULL,
    baseline_value  DECIMAL(10,2)  NOT NULL,
    percentile_25   DECIMAL(10,2)  NULL,
    percentile_50   DECIMAL(10,2)  NULL,
    percentile_75   DECIMAL(10,2)  NULL,
    sample_size     INT            NULL,
    source          NVARCHAR(100)  NULL,
    updated_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_industry_benchmarks UNIQUE (industry_code, employee_band, metric_key)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_industry_benchmarks_lookup')
    CREATE INDEX ix_industry_benchmarks_lookup ON cloud_assessment_industry_benchmarks(industry_code, metric_key);
GO

-- Per-scan benchmark comparison snapshot.
IF OBJECT_ID('cloud_assessment_benchmark_comparisons', 'U') IS NULL
CREATE TABLE cloud_assessment_benchmark_comparisons (
    id                    UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    scan_id               UNIQUEIDENTIFIER NOT NULL,
    metric_key            NVARCHAR(100)   NOT NULL,
    org_value             DECIMAL(10,2)   NULL,
    franchise_avg         DECIMAL(10,2)   NULL,
    franchise_percentile  DECIMAL(5,2)    NULL,
    franchise_sample_size INT             NULL,
    industry_baseline     DECIMAL(10,2)   NULL,
    industry_p25          DECIMAL(10,2)   NULL,
    industry_p50          DECIMAL(10,2)   NULL,
    industry_p75          DECIMAL(10,2)   NULL,
    industry_percentile   DECIMAL(5,2)    NULL,
    global_avg            DECIMAL(10,2)   NULL,
    global_percentile     DECIMAL(5,2)    NULL,
    global_sample_size    INT             NULL,
    verdict               NVARCHAR(30)    NULL,
    computed_at           DATETIME2(2)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_benchmark_scan FOREIGN KEY (scan_id) REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_benchmark_scan')
    CREATE INDEX ix_benchmark_scan ON cloud_assessment_benchmark_comparisons(scan_id, metric_key);
GO

-- Materialized franchise aggregates (refreshed nightly, not per-scan).
IF OBJECT_ID('cloud_assessment_franchise_aggregates', 'U') IS NULL
CREATE TABLE cloud_assessment_franchise_aggregates (
    id            UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    franchise_id  UNIQUEIDENTIFIER NOT NULL,
    metric_key    NVARCHAR(100)   NOT NULL,
    avg_value     DECIMAL(10,2)   NULL,
    percentile_25 DECIMAL(10,2)   NULL,
    percentile_50 DECIMAL(10,2)   NULL,
    percentile_75 DECIMAL(10,2)   NULL,
    sample_size   INT             NULL,
    refreshed_at  DATETIME2(2)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_franchise_aggregates UNIQUE (franchise_id, metric_key),
    CONSTRAINT FK_franchise_agg FOREIGN KEY (franchise_id) REFERENCES franchises(id) ON DELETE CASCADE
);
GO

-- Global aggregates (refreshed nightly, anonymized).
IF OBJECT_ID('cloud_assessment_global_aggregates', 'U') IS NULL
CREATE TABLE cloud_assessment_global_aggregates (
    id            UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    metric_key    NVARCHAR(100)   NOT NULL,
    industry_code NVARCHAR(30)    NULL,
    employee_band NVARCHAR(20)    NULL,
    avg_value     DECIMAL(10,2)   NULL,
    percentile_25 DECIMAL(10,2)   NULL,
    percentile_50 DECIMAL(10,2)   NULL,
    percentile_75 DECIMAL(10,2)   NULL,
    sample_size   INT             NULL,
    refreshed_at  DATETIME2(2)    NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_global_aggregates_lookup')
    CREATE INDEX ix_global_aggregates_lookup ON cloud_assessment_global_aggregates(metric_key, industry_code, employee_band);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ux_global_aggregates_key')
    CREATE UNIQUE INDEX ux_global_aggregates_key ON cloud_assessment_global_aggregates(metric_key, industry_code, employee_band)
    WHERE industry_code IS NOT NULL AND employee_band IS NOT NULL;
GO
