-- ============================================================
-- 040_dc_platform_support.sql
-- Phase 2: Domain Controller vs Member Server differentiation
--
-- Adds product_type column to machines table so PlatformResolver
-- can distinguish DCs (ProductType=2) from member servers (ProductType=3)
-- and workstations (ProductType=1).
--
-- Also links ALL active controls currently on MS19 to DC platforms
-- (DC19/DC22/DC25), which includes the 647 baseline + any SRV-* controls
-- from seed_010. DCs need all server controls + DC-specific controls.
--
-- Deploy order: seed_010 → seed_013 → this file (040)
-- ============================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Add product_type to machines (1=Workstation, 2=DomainController, 3=Server)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'product_type')
BEGIN
    ALTER TABLE machines ADD product_type SMALLINT NULL;
    PRINT 'Added product_type column to machines';
END
GO

-- Link all active controls to DC platforms
-- DCs get the same baseline as member servers plus DC-specific controls (seed_013)
BEGIN TRANSACTION;

DECLARE @platDC19 INT = (SELECT id FROM platforms WHERE code='DC19');
DECLARE @platDC22 INT = (SELECT id FROM platforms WHERE code='DC22');
DECLARE @platDC25 INT = (SELECT id FROM platforms WHERE code='DC25');

IF @platDC19 IS NULL OR @platDC22 IS NULL OR @platDC25 IS NULL
BEGIN
    PRINT 'WARNING: DC platform rows not found. Skipping control linkage.';
    COMMIT TRANSACTION;
    RETURN;
END

-- Get all active control IDs that are currently linked to MS19 (member server baseline)
-- These same controls should also apply to DCs
DECLARE @platMS19 INT = (SELECT id FROM platforms WHERE code='MS19');

-- Link to DC19
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cp.control_def_id, @platDC19
FROM control_platforms cp
INNER JOIN control_defs cd ON cd.id = cp.control_def_id
WHERE cp.platform_id = @platMS19
  AND cd.is_active = 1
  AND NOT EXISTS (
    SELECT 1 FROM control_platforms x
    WHERE x.control_def_id = cp.control_def_id AND x.platform_id = @platDC19
  );
DECLARE @dc19Count INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for DC19: ', @dc19Count);

-- Link to DC22
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cp.control_def_id, @platDC22
FROM control_platforms cp
INNER JOIN control_defs cd ON cd.id = cp.control_def_id
WHERE cp.platform_id = @platMS19
  AND cd.is_active = 1
  AND NOT EXISTS (
    SELECT 1 FROM control_platforms x
    WHERE x.control_def_id = cp.control_def_id AND x.platform_id = @platDC22
  );
DECLARE @dc22Count INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for DC22: ', @dc22Count);

-- Link to DC25
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cp.control_def_id, @platDC25
FROM control_platforms cp
INNER JOIN control_defs cd ON cd.id = cp.control_def_id
WHERE cp.platform_id = @platMS19
  AND cd.is_active = 1
  AND NOT EXISTS (
    SELECT 1 FROM control_platforms x
    WHERE x.control_def_id = cp.control_def_id AND x.platform_id = @platDC25
  );
DECLARE @dc25Count INT = @@ROWCOUNT;
PRINT CONCAT('control_platforms rows added for DC25: ', @dc25Count);

COMMIT TRANSACTION;
GO

-- Verification
SELECT p.code, COUNT(*) AS control_count
FROM control_platforms cp
INNER JOIN platforms p ON p.id = cp.platform_id
INNER JOIN control_defs cd ON cd.id = cp.control_def_id
WHERE cd.is_active = 1
  AND p.code IN ('W10','W11','MS19','MS22','MS25','DC19','DC22','DC25')
GROUP BY p.code
ORDER BY p.code;
