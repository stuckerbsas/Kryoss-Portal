-- 084: Add feature_inventory JSON column to cloud_assessment_scans
-- Stores per-feature license/implementation/adoption status detected during scan

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('cloud_assessment_scans')
      AND name = 'feature_inventory'
)
BEGIN
    ALTER TABLE cloud_assessment_scans
        ADD feature_inventory NVARCHAR(MAX) NULL;
END
