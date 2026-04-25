-- 036: Unified scan — add Copilot Readiness D1-D6 scores + SharePoint/ExternalUser tables to Cloud Assessment
-- This merges the Copilot Readiness scoring into the Cloud Assessment scan,
-- eliminating the need for a separate copilot_readiness_scans pipeline.

-- 1. Add Copilot Readiness dimension scores to cloud_assessment_scans
ALTER TABLE cloud_assessment_scans ADD
    copilot_d1_score  DECIMAL(5,2)  NULL,
    copilot_d2_score  DECIMAL(5,2)  NULL,
    copilot_d3_score  DECIMAL(5,2)  NULL,
    copilot_d4_score  DECIMAL(5,2)  NULL,
    copilot_d5_score  DECIMAL(5,2)  NULL,
    copilot_d6_score  DECIMAL(5,2)  NULL,
    copilot_overall   DECIMAL(5,2)  NULL,
    copilot_verdict   NVARCHAR(50)  NULL;
GO

-- 2. SharePoint site-level data (labels, oversharing) per CA scan
IF OBJECT_ID('cloud_assessment_sharepoint_sites', 'U') IS NULL
CREATE TABLE cloud_assessment_sharepoint_sites (
    id               BIGINT         IDENTITY(1,1) PRIMARY KEY,
    scan_id          UNIQUEIDENTIFIER NOT NULL
        REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
    site_url         NVARCHAR(1000) NOT NULL,
    site_title       NVARCHAR(500)  NULL,
    total_files      INT            NOT NULL DEFAULT 0,
    labeled_files    INT            NOT NULL DEFAULT 0,
    overshared_files INT            NOT NULL DEFAULT 0,
    risk_level       NVARCHAR(20)   NULL,
    top_labels       NVARCHAR(1000) NULL,
    created_at       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE NONCLUSTERED INDEX IX_ca_sp_sites_scan
    ON cloud_assessment_sharepoint_sites(scan_id);
GO

-- 3. External/guest user enumeration per CA scan
IF OBJECT_ID('cloud_assessment_external_users', 'U') IS NULL
CREATE TABLE cloud_assessment_external_users (
    id                 BIGINT         IDENTITY(1,1) PRIMARY KEY,
    scan_id            UNIQUEIDENTIFIER NOT NULL
        REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
    user_principal     NVARCHAR(500)  NOT NULL,
    display_name       NVARCHAR(500)  NULL,
    email_domain       NVARCHAR(255)  NULL,
    last_sign_in       DATETIME2      NULL,
    risk_level         NVARCHAR(20)   NULL,
    sites_accessed     INT            NOT NULL DEFAULT 0,
    highest_permission NVARCHAR(100)  NULL,
    created_at         DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE NONCLUSTERED INDEX IX_ca_ext_users_scan
    ON cloud_assessment_external_users(scan_id);
GO
