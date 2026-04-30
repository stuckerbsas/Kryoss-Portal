IF COL_LENGTH('machines', 'aad_tenant_id') IS NULL
    ALTER TABLE machines ADD aad_tenant_id NVARCHAR(36) NULL;
