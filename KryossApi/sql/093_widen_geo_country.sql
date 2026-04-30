-- 093: Widen geo_country from NVARCHAR(2) to NVARCHAR(60) — geo API returns full country names
ALTER TABLE machine_public_ip_history ALTER COLUMN geo_country NVARCHAR(60) NULL;
ALTER TABLE network_sites ALTER COLUMN geo_country NVARCHAR(60) NULL;
GO
