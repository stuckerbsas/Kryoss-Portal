-- 089_cve_product_column.sql — Add product column for precise CVE-to-software matching

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('cve_entries') AND name = 'product')
BEGIN
    ALTER TABLE cve_entries ADD product NVARCHAR(256) NULL;
    CREATE INDEX IX_cve_entries_vendor_product ON cve_entries (vendor, product);
    PRINT 'Added product column + index to cve_entries';
END
ELSE
    PRINT 'product column already exists on cve_entries';

GO
