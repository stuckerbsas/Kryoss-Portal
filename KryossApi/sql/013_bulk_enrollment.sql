SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 013_bulk_enrollment.sql
-- Add multi-use enrollment codes: max_uses + use_count
-- A code with max_uses=50 can enroll up to 50 machines.
-- max_uses=NULL means single-use (legacy behavior).
-- =============================================

ALTER TABLE enrollment_codes ADD max_uses INT NULL;
ALTER TABLE enrollment_codes ADD use_count INT NOT NULL DEFAULT 0;
GO
