-- 094: Widen fixed_version columns to handle multi-version strings (e.g. Dell OS10 lists)
-- Fixes 21 truncation errors during bulk CVE import

ALTER TABLE cve_entries ALTER COLUMN fixed_version NVARCHAR(500) NULL;
ALTER TABLE machine_cve_findings ALTER COLUMN fixed_version NVARCHAR(500) NULL;
ALTER TABLE cve_product_map ALTER COLUMN fixed_version NVARCHAR(500) NULL;
