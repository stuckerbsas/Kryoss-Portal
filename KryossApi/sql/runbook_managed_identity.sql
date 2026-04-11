-- =============================================================================
-- runbook_managed_identity.sql
-- =============================================================================
-- One-time setup to grant the Function App's Managed Identity access to
-- Azure SQL without any SQL login / password. This is part of the security
-- baseline (see KryossApi/docs/security-baseline.md, P0 #2).
--
-- PREREQUISITES
-- -------------
-- 1. Function App `func-kryoss` already has a system-assigned managed
--    identity enabled. Verify with:
--       az functionapp identity show --name func-kryoss --resource-group rg-kryoss
--    If empty, enable it:
--       az functionapp identity assign --name func-kryoss --resource-group rg-kryoss
--
-- 2. Azure SQL server `sql-kryoss.database.windows.net` has an Entra ID
--    admin assigned. The OPERATOR RUNNING THIS SCRIPT must connect to the
--    database AS that Entra admin. SQL auth logins (like `kryossadmin`)
--    CANNOT grant permission to Entra users — only Entra can create Entra.
--       az ad signed-in-user show --query id -o tsv
--       az sql server ad-admin create ^
--          --resource-group rg-kryoss ^
--          --server-name sql-kryoss ^
--          --display-name "<your-email>" ^
--          --object-id "<your-entra-object-id>"
--
-- 3. Connect to the `KryossDb` database (NOT `master`) as the Entra admin,
--    e.g. from SSMS using "Azure Active Directory - Universal with MFA".
--    Do NOT use "SQL Server Authentication".
--
-- HOW TO RUN
-- ----------
--   sqlcmd -S tcp:sql-kryoss.database.windows.net,1433 -d KryossDb ^
--          --authentication-method=ActiveDirectoryInteractive ^
--          -G -U "<your-email>" ^
--          -i runbook_managed_identity.sql
--
-- Or paste into SSMS and F5 while connected as the Entra admin.
--
-- IDEMPOTENCY
-- -----------
-- Safe to re-run. Uses IF NOT EXISTS guards on user creation.
--
-- IMPORTANT — SINGLE BATCH DESIGN
-- -------------------------------
-- This script intentionally contains NO `GO` separators. Everything runs
-- as a single batch so that a failed precondition check can RETURN and
-- actually prevent the rest of the statements from executing. Do NOT add
-- `GO` anywhere in this file.
-- =============================================================================

SET NOCOUNT ON;
PRINT '=== Managed Identity setup: start ===';

-- -----------------------------------------------------------------------------
-- 0. HARD PRECONDITION: you MUST be connected as an Entra identity, not via
--    SQL authentication. Azure SQL refuses CREATE USER FROM EXTERNAL PROVIDER
--    from SQL-auth sessions with a cryptic 33159 error. Detect it up front
--    and bail out loudly with instructions instead of half-applying the script.
-- -----------------------------------------------------------------------------
IF SUSER_SNAME() NOT LIKE '%@%' OR ORIGINAL_LOGIN() NOT LIKE '%@%'
BEGIN
    DECLARE @whoami NVARCHAR(256) = SUSER_SNAME();
    PRINT '';
    PRINT '==================================================================';
    PRINT 'ABORT — this script must be run from an Entra ID (Azure AD) session.';
    PRINT '==================================================================';
    PRINT 'Current SUSER_SNAME() = ' + @whoami;
    PRINT '';
    PRINT 'Fix:';
    PRINT '  1. az sql server ad-admin list --resource-group rg-kryoss --server-name sql-kryoss';
    PRINT '  2. If empty:';
    PRINT '       az ad signed-in-user show --query id -o tsv';
    PRINT '       az sql server ad-admin create --resource-group rg-kryoss --server-name sql-kryoss \';
    PRINT '            --display-name "<your-email>" --object-id "<the-guid-above>"';
    PRINT '  3. Reconnect to KryossDb using "Azure Active Directory - Universal with MFA"';
    PRINT '     (NOT SQL Server Authentication) and re-run this script.';
    PRINT '==================================================================';
    -- Raise a fatal error to make sure the client exits non-zero.
    -- THROW aborts the batch, RAISERROR at severity 16 does not reliably
    -- do so in every client, so we use THROW here.
    THROW 50001, 'Cannot run as SQL auth. See message above.', 1;
    RETURN;
END

PRINT 'Precondition OK — connected as Entra principal: ' + SUSER_SNAME();

-- -----------------------------------------------------------------------------
-- 1. Create the contained database user for the Function App's managed
--    identity. The user name MUST match the Function App name exactly.
--    SQL looks up the identity in Entra by display name.
-- -----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'func-kryoss')
BEGIN
    -- CREATE USER does not accept variables, must be inline.
    CREATE USER [func-kryoss] FROM EXTERNAL PROVIDER;
    PRINT 'Created contained user [func-kryoss] from Entra ID.';
END
ELSE
BEGIN
    PRINT 'User [func-kryoss] already exists. Skipping creation.';
END

-- -----------------------------------------------------------------------------
-- 2. Grant least-privilege roles.
--    - db_datareader: SELECT on all tables (needed for reads)
--    - db_datawriter: INSERT/UPDATE/DELETE on all tables (needed for writes)
--    We intentionally do NOT grant:
--      - db_owner / db_ddladmin  (schema changes belong in migrations only)
--      - db_securityadmin         (role/permission management is forbidden)
--      - VIEW DATABASE STATE      (reduces info leakage if compromised)
-- -----------------------------------------------------------------------------
ALTER ROLE db_datareader ADD MEMBER [func-kryoss];
ALTER ROLE db_datawriter ADD MEMBER [func-kryoss];
PRINT 'Granted db_datareader + db_datawriter to [func-kryoss].';

-- -----------------------------------------------------------------------------
-- 3. Grant EXECUTE on the dbo schema so EF Core can run parameterized
--    queries that call stored procedures (if any are added later) and
--    sp_set_session_context which is used by RlsMiddleware.
-- -----------------------------------------------------------------------------
GRANT EXECUTE ON SCHEMA::dbo TO [func-kryoss];
PRINT 'Granted EXECUTE on schema::dbo to [func-kryoss].';

-- -----------------------------------------------------------------------------
-- 4. Verification block. If anything is missing, these SELECTs will show it.
-- -----------------------------------------------------------------------------
PRINT '--- Verification ---';

SELECT
    dp.name              AS principal_name,
    dp.type_desc         AS principal_type,
    dp.authentication_type_desc AS auth_type,
    dp.create_date
FROM sys.database_principals dp
WHERE dp.name = N'func-kryoss';

SELECT
    r.name AS role_name
FROM sys.database_role_members rm
JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
WHERE m.name = N'func-kryoss'
ORDER BY r.name;

SELECT
    perm.permission_name,
    perm.state_desc,
    obj.name AS object_name
FROM sys.database_permissions perm
LEFT JOIN sys.objects obj ON obj.object_id = perm.major_id
JOIN sys.database_principals dp ON dp.principal_id = perm.grantee_principal_id
WHERE dp.name = N'func-kryoss';

PRINT '=== Managed Identity setup: done ===';

-- =============================================================================
-- POST-RUN CHECKLIST (operator, outside of SQL)
-- =============================================================================
-- [ ] Update the Function App's SqlConnectionString app setting to:
--       Server=tcp:sql-kryoss.database.windows.net,1433;
--       Database=KryossDb;
--       Authentication=Active Directory Default;
--       Encrypt=True;
--       TrustServerCertificate=False;
--
--     az functionapp config appsettings set ^
--        --name func-kryoss --resource-group rg-kryoss ^
--        --settings "SqlConnectionString=Server=tcp:sql-kryoss.database.windows.net,1433;Database=KryossDb;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;"
--
-- [ ] Remove any SQL login that used to be used by the Function:
--       (connect as server admin on master)
--       DROP LOGIN [func_sql_login];
--
-- [ ] Remove the old SQL user from KryossDb:
--       DROP USER [func_sql_login];
--
-- [ ] Restart the Function App:
--       az functionapp restart --name func-kryoss --resource-group rg-kryoss
--
-- [ ] Tail the logs and confirm you see:
--       "[KryossApi] SQL auth method: ActiveDirectoryDefault (no secrets in connection string)"
--
-- [ ] Test an authenticated endpoint to confirm the managed identity can
--     actually read + write (pick any /v2/* endpoint that hits the DB).
-- =============================================================================
