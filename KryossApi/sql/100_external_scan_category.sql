-- 100: Add category column to external_scan_findings for grouping (EXT-02)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('external_scan_findings') AND name = 'category'
)
BEGIN
    ALTER TABLE external_scan_findings
        ADD category NVARCHAR(20) NULL;
END
GO
