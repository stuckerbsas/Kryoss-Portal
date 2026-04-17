-- 032_azure_resource_cache.sql
-- Azure resource cache for the CA-6 Subsession B Azure pipeline.
-- Stores per-scan snapshots of ARM resources (storage, key vault, NSG, VMs, ...)
-- plus a slice of their properties and detected risk flags used by recommendations.
-- Idempotent — guards mirror 031_azure_consent_tracking.sql.

IF OBJECT_ID('cloud_assessment_azure_resources', 'U') IS NULL
BEGIN
    CREATE TABLE cloud_assessment_azure_resources (
        id                BIGINT IDENTITY(1,1) PRIMARY KEY,
        scan_id           UNIQUEIDENTIFIER NOT NULL
            REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
        subscription_id   VARCHAR(64)      NOT NULL,
        resource_type     VARCHAR(200)     NOT NULL,
        resource_id       NVARCHAR(500)    NOT NULL,
        name              NVARCHAR(200)    NULL,
        location          VARCHAR(50)      NULL,
        kind              NVARCHAR(100)    NULL,
        properties_json   NVARCHAR(MAX)    NULL,
        risk_flags        NVARCHAR(MAX)    NULL,
        created_at        DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'ix_car_scan'
                 AND object_id = OBJECT_ID('cloud_assessment_azure_resources'))
    CREATE INDEX ix_car_scan
        ON cloud_assessment_azure_resources(scan_id, resource_type);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'ix_car_subscription'
                 AND object_id = OBJECT_ID('cloud_assessment_azure_resources'))
    CREATE INDEX ix_car_subscription
        ON cloud_assessment_azure_resources(scan_id, subscription_id);
GO
