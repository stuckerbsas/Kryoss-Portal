-- 086: Enable network scan by default (12h interval already set)
-- Flip all existing machines to enable network scan
UPDATE machines SET config_enable_network_scan = 1 WHERE config_enable_network_scan = 0;

-- Update column default for future inserts outside EF
IF EXISTS (SELECT 1 FROM sys.default_constraints dc
           JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
           WHERE c.name = 'config_enable_network_scan' AND c.object_id = OBJECT_ID('machines'))
BEGIN
    DECLARE @dfName NVARCHAR(256);
    SELECT @dfName = dc.name FROM sys.default_constraints dc
           JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
           WHERE c.name = 'config_enable_network_scan' AND c.object_id = OBJECT_ID('machines');
    EXEC('ALTER TABLE machines DROP CONSTRAINT ' + @dfName);
END
ALTER TABLE machines ADD DEFAULT 1 FOR config_enable_network_scan;
