SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- helpers/add_hq_user.sql
-- Kryoss Platform -- Parameterized helper to add a portal user
-- linked to an Entra ID identity and a franchise.
--
-- Usage: Fill in the @variables below, then run the script.
-- =============================================

DECLARE @entraOid      UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000'; -- Replace with real Entra Object ID
DECLARE @email         NVARCHAR(255)    = N'user@example.com';                    -- Replace with real email
DECLARE @displayName   NVARCHAR(255)    = N'First Last';                          -- Replace with real name
DECLARE @roleCode      VARCHAR(50)      = 'super_admin';                          -- Role code from roles table
DECLARE @franchiseName NVARCHAR(255)    = N'TeamLogic IT';                        -- Franchise name from franchises table

-- System user for created_by
DECLARE @systemUserId  UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- =============================================
-- Resolve role
-- =============================================
DECLARE @roleId INT;
SELECT @roleId = id FROM roles WHERE code = @roleCode AND deleted_at IS NULL;

IF @roleId IS NULL
BEGIN
    RAISERROR('Role "%s" not found or is soft-deleted. Check the roles table.', 16, 1, @roleCode);
    RETURN;
END;

-- =============================================
-- Resolve franchise
-- =============================================
DECLARE @franchiseId UNIQUEIDENTIFIER;
SELECT @franchiseId = id FROM franchises WHERE name = @franchiseName AND deleted_at IS NULL;

IF @franchiseId IS NULL
BEGIN
    RAISERROR('Franchise "%s" not found or is soft-deleted. Check the franchises table.', 16, 1, @franchiseName);
    RETURN;
END;

-- =============================================
-- Check if user already exists (by entra_oid)
-- =============================================
IF EXISTS (SELECT 1 FROM users WHERE entra_oid = @entraOid AND deleted_at IS NULL)
BEGIN
    PRINT 'User with Entra OID ' + CAST(@entraOid AS VARCHAR(36)) + ' already exists. No action taken.';
    RETURN;
END;

-- =============================================
-- Insert user
-- =============================================
DECLARE @newUserId UNIQUEIDENTIFIER = NEWID();

INSERT INTO users (id, entra_oid, email, display_name, role_id, franchise_id, auth_source, created_by)
VALUES (@newUserId, @entraOid, @email, @displayName, @roleId, @franchiseId, 'entra', @systemUserId);

PRINT 'User created successfully.';
PRINT '  ID:         ' + CAST(@newUserId AS VARCHAR(36));
PRINT '  Email:      ' + @email;
PRINT '  Role:       ' + @roleCode;
PRINT '  Franchise:  ' + @franchiseName;
PRINT '  Entra OID:  ' + CAST(@entraOid AS VARCHAR(36));
GO
