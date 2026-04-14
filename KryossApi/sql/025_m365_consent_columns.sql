-- 025_m365_consent_columns.sql
-- Add consent tracking columns to m365_tenants for the multi-tenant admin consent flow.
-- client_id and client_secret are already nullable in 024_m365_tenants.sql.

-- Track when and who granted admin consent
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('m365_tenants') AND name = 'consent_granted_at')
    ALTER TABLE m365_tenants ADD consent_granted_at DATETIME2(2) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('m365_tenants') AND name = 'consent_granted_by')
    ALTER TABLE m365_tenants ADD consent_granted_by NVARCHAR(255) NULL;
GO
