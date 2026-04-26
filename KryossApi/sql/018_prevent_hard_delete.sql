SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 018_prevent_hard_delete.sql
-- Kryoss Platform -- INSTEAD OF DELETE triggers on all tables
-- except actlog (which already has its own immutability trigger).
-- Prevents accidental hard deletes; logs attempt to actlog.
-- Idempotent: drops and recreates each trigger.
-- =============================================

DECLARE @tableName SYSNAME;
DECLARE @sql       NVARCHAR(MAX);

-- Excluded tables: actlog (has its own trigger), plus system/temp tables
DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT t.name
    FROM sys.tables t
    WHERE t.schema_id = SCHEMA_ID('dbo')
      AND t.name NOT IN ('actlog')
      AND t.type = 'U'                         -- user tables only
      AND t.is_ms_shipped = 0                   -- not system tables
    ORDER BY t.name;

OPEN table_cursor;
FETCH NEXT FROM table_cursor INTO @tableName;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Drop existing trigger if present
    SET @sql = N'
        IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = N''trg_' + @tableName + N'_prevent_delete'' AND parent_id = OBJECT_ID(' + QUOTENAME(@tableName, '''') + N'))
            DROP TRIGGER ' + QUOTENAME('trg_' + @tableName + '_prevent_delete') + N';';
    EXEC sp_executesql @sql;

    -- Create INSTEAD OF DELETE trigger
    SET @sql = N'
CREATE TRIGGER ' + QUOTENAME('trg_' + @tableName + '_prevent_delete') + N'
ON ' + QUOTENAME(@tableName) + N'
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;

    -- Log the blocked attempt to actlog (best effort)
    BEGIN TRY
        INSERT INTO actlog ([timestamp], severity, module, action, entity_type, message)
        SELECT
            SYSUTCDATETIME(),
            ''SEC'',
            ''security'',
            ''hard_delete_blocked'',
            ''' + @tableName + N''',
            ''Blocked hard DELETE of '' + CAST((SELECT COUNT(*) FROM deleted) AS VARCHAR(10)) + '' row(s) from ' + @tableName + N''';
    END TRY
    BEGIN CATCH
        -- Swallow logging errors; the THROW below still fires
    END CATCH;

    -- Reject the delete
    THROW 50010, ''Hard DELETE is not allowed on ' + @tableName + N'. Use soft delete (SET deleted_at) instead.'', 1;
END;';

    EXEC sp_executesql @sql;
    PRINT 'Created trigger trg_' + @tableName + '_prevent_delete';

    FETCH NEXT FROM table_cursor INTO @tableName;
END;

CLOSE table_cursor;
DEALLOCATE table_cursor;
GO

-- =============================================
-- PRODUCTION NOTE: actlog protection
-- =============================================
-- In production, run the following with a privileged account
-- to prevent even the Managed Identity from deleting or
-- altering actlog rows. The trigger already blocks it, but
-- DENY adds a belt-and-suspenders layer.
--
-- Replace <managed_identity_user> with the actual MI principal.
--
-- DENY DELETE ON dbo.actlog TO [<managed_identity_user>];
-- DENY UPDATE ON dbo.actlog TO [<managed_identity_user>];
--
-- Do NOT run these in dev (kryossadmin needs full access for
-- migrations and test resets).
-- =============================================

PRINT '=== 018_prevent_hard_delete.sql complete ===';
GO
