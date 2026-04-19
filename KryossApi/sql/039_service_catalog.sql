-- 039_service_catalog.sql
-- Unified Report System: service catalog for auto-generated proposals

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'service_catalog')
CREATE TABLE service_catalog (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    category_code   VARCHAR(30)    NOT NULL,
    name_en         NVARCHAR(100)  NOT NULL,
    name_es         NVARCHAR(100)  NOT NULL,
    unit_type       VARCHAR(20)    NOT NULL,
    base_hours      DECIMAL(5,2)   NOT NULL,
    trigger_source  VARCHAR(50)    NOT NULL,
    trigger_filter  NVARCHAR(500)  NULL,
    severity        VARCHAR(10)    NOT NULL DEFAULT 'medium',
    sort_order      INT            NOT NULL DEFAULT 0,
    is_active       BIT            NOT NULL DEFAULT 1,
    CONSTRAINT UQ_service_catalog_code UNIQUE (category_code)
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'franchise_service_rates')
CREATE TABLE franchise_service_rates (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    franchise_id    UNIQUEIDENTIFIER NOT NULL REFERENCES franchises(id),
    hourly_rate     DECIMAL(10,2)  NOT NULL DEFAULT 150.00,
    currency        VARCHAR(3)     NOT NULL DEFAULT 'USD',
    margin_pct      DECIMAL(5,2)   NOT NULL DEFAULT 0,
    effective_from  DATETIME2(2)   NOT NULL DEFAULT GETUTCDATE(),
    created_at      DATETIME2(2)   NOT NULL DEFAULT GETUTCDATE()
);
GO
