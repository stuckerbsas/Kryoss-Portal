-- 096: Widen affected_below to match fixed_version (NVARCHAR(500))
-- Same truncation issue as fixed_version (migration 094) — Dell CVEs have long multi-product version strings

ALTER TABLE cve_entries ALTER COLUMN affected_below NVARCHAR(500);
ALTER TABLE cve_product_map ALTER COLUMN affected_below NVARCHAR(500);
