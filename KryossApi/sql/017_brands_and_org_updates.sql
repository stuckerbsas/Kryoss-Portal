SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 017_brands_and_org_updates.sql
-- Kryoss Platform -- Brands table, organization updates,
-- new modules/actions/permissions for portal MVP.
-- Depends on: 001_foundation.sql, 002_core.sql, seed_001
-- Idempotent: safe to re-run.
-- =============================================

-- =============================================
-- 1. BRANDS TABLE
-- =============================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'brands' AND TABLE_SCHEMA = 'dbo')
BEGIN
    CREATE TABLE brands (
        id              INT IDENTITY(1,1) PRIMARY KEY,
        code            VARCHAR(50)    NOT NULL UNIQUE,
        name            NVARCHAR(100)  NOT NULL,
        color_primary   VARCHAR(7)     NOT NULL,           -- e.g. #008852
        color_accent    VARCHAR(7)     NOT NULL,           -- e.g. #A2C564
        color_dark_bg   VARCHAR(7)     NULL,               -- e.g. #3D4043
        logo_url        NVARCHAR(500)  NULL,
        font_family     VARCHAR(50)    NOT NULL DEFAULT 'Montserrat',
        is_active       BIT            NOT NULL DEFAULT 1
    );
    PRINT 'Created table: brands';
END
ELSE
    PRINT 'Table brands already exists -- skipped.';
GO

-- =============================================
-- 2. SEED BRANDS
-- =============================================
IF NOT EXISTS (SELECT 1 FROM brands WHERE code = 'teamlogic')
    INSERT INTO brands (code, name, color_primary, color_accent, color_dark_bg, font_family)
    VALUES ('teamlogic', N'TeamLogic IT', '#008852', '#A2C564', '#3D4043', 'Montserrat');

IF NOT EXISTS (SELECT 1 FROM brands WHERE code = 'kryoss')
    INSERT INTO brands (code, name, color_primary, color_accent, color_dark_bg, font_family)
    VALUES ('kryoss', N'Kryoss', '#1A73E8', '#4FC3F7', '#1E1E2E', 'Montserrat');

IF NOT EXISTS (SELECT 1 FROM brands WHERE code = 'geminis')
    INSERT INTO brands (code, name, color_primary, color_accent, color_dark_bg, font_family)
    VALUES ('geminis', N'Geminis Computer', '#2E3A87', '#5C6BC0', '#212121', 'Montserrat');

PRINT 'Brands seeded (3 rows).';
GO

-- =============================================
-- 3. ALTER organizations: add brand_id + entra_tenant_id
-- =============================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'organizations' AND COLUMN_NAME = 'brand_id')
BEGIN
    -- Resolve the teamlogic brand id for the default
    DECLARE @teamlogicBrandId INT;
    SELECT @teamlogicBrandId = id FROM brands WHERE code = 'teamlogic';

    -- Add column with default — must use dynamic SQL because DEFAULT requires a literal
    DECLARE @alterSql NVARCHAR(MAX) = N'ALTER TABLE organizations ADD brand_id INT NOT NULL CONSTRAINT df_organizations_brand_id DEFAULT ' + CAST(@teamlogicBrandId AS NVARCHAR(10));
    EXEC sp_executesql @alterSql;

    -- Add FK constraint
    ALTER TABLE organizations
        ADD CONSTRAINT fk_organizations_brand
        FOREIGN KEY (brand_id) REFERENCES brands(id);

    PRINT 'Added organizations.brand_id with FK to brands.';
END
ELSE
    PRINT 'organizations.brand_id already exists -- skipped.';
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'organizations' AND COLUMN_NAME = 'entra_tenant_id')
BEGIN
    ALTER TABLE organizations ADD entra_tenant_id UNIQUEIDENTIFIER NULL;
    PRINT 'Added organizations.entra_tenant_id.';
END
ELSE
    PRINT 'organizations.entra_tenant_id already exists -- skipped.';
GO

-- =============================================
-- 4. NEW MODULES: organizations, recycle_bin
-- =============================================
IF NOT EXISTS (SELECT 1 FROM modules WHERE code = 'organizations')
    INSERT INTO modules (code, name, sort_order) VALUES ('organizations', N'Organizations', 14);

IF NOT EXISTS (SELECT 1 FROM modules WHERE code = 'recycle_bin')
    INSERT INTO modules (code, name, sort_order) VALUES ('recycle_bin', N'Recycle Bin', 15);

PRINT 'Modules organizations + recycle_bin ensured.';
GO

-- =============================================
-- 5. NEW ACTION: restore
-- =============================================
IF NOT EXISTS (SELECT 1 FROM actions WHERE code = 'restore')
    INSERT INTO actions (code, name) VALUES ('restore', N'Restore');

PRINT 'Action restore ensured.';
GO

-- =============================================
-- 6. AUTO-GENERATE PERMISSIONS for new modules x all actions
-- =============================================
INSERT INTO permissions (module_id, action_id, slug, description)
SELECT
    m.id,
    a.id,
    m.code + ':' + a.code,
    m.name + N' -- ' + a.name
FROM modules m
CROSS JOIN actions a
WHERE NOT EXISTS (
    SELECT 1 FROM permissions p
    WHERE p.module_id = m.id AND p.action_id = a.id
);

PRINT 'Permissions cross-join gap-filled.';
GO

-- =============================================
-- 7. ASSIGN NEW PERMISSIONS TO ROLES
-- =============================================

-- super_admin: ALL permissions (gap-fill any missing)
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
WHERE r.code = 'super_admin'
  AND NOT EXISTS (
      SELECT 1 FROM role_permissions rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );

PRINT 'super_admin permissions gap-filled.';

-- franchise_owner: organizations:* + recycle_bin:read,restore
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
JOIN modules m ON p.module_id = m.id
JOIN actions a ON p.action_id = a.id
WHERE r.code = 'franchise_owner'
  AND (
      m.code = 'organizations'
      OR (m.code = 'recycle_bin' AND a.code IN ('read', 'restore'))
  )
  AND NOT EXISTS (
      SELECT 1 FROM role_permissions rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );

PRINT 'franchise_owner new permissions assigned.';

-- franchise_tech: organizations:read + recycle_bin:read,restore
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
JOIN modules m ON p.module_id = m.id
JOIN actions a ON p.action_id = a.id
WHERE r.code = 'franchise_tech'
  AND (
      (m.code = 'organizations' AND a.code = 'read')
      OR (m.code = 'recycle_bin' AND a.code IN ('read', 'restore'))
  )
  AND NOT EXISTS (
      SELECT 1 FROM role_permissions rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );

PRINT 'franchise_tech new permissions assigned.';
GO

PRINT '=== 017_brands_and_org_updates.sql complete ===';
GO
