-- 092: Add product_class to cve_entries for OS/PLATFORM/APPLICATION/LIBRARY classification
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('cve_entries') AND name = 'product_class')
BEGIN
    ALTER TABLE cve_entries ADD product_class NVARCHAR(20) NULL;
END
GO
