SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_009_backfill_assessment_controls.sql
-- Kryoss Platform -- Back-link all active controls to all active assessments
--
-- Context:
--   seed_100_test_data.sql linked control_defs to assessment id=1
--   at creation time (when the catalog had ~90 controls). Subsequent
--   catalog seeds (seed_004, seed_005) added hundreds of new controls
--   but DID NOT back-link them to existing assessments. Result:
--   assessment 1 had 108 linked controls instead of 647.
--
--   Only seed_008 had the back-link logic, which is why a post-seed_008
--   agent run received exactly 17 controls (the intersection of "in
--   assessment" + "linked to W10 platform" + "is_active").
--
-- Goal:
--   For every active assessment, ensure every active control_def is
--   linked via assessment_controls. Idempotent.
--
-- Decision:
--   Default assessments receive the WHOLE catalog. If/when we introduce
--   curated assessments (e.g. "HIPAA only", "PCI only"), those should
--   be new assessments with deliberate subsets — we never go back to
--   partial linking on the default ones.
-- ============================================================

BEGIN TRANSACTION;

INSERT INTO assessment_controls (assessment_id, control_def_id)
SELECT a.id, cd.id
FROM assessments a
CROSS JOIN control_defs cd
WHERE a.is_active = 1
  AND a.deleted_at IS NULL
  AND cd.is_active = 1
  AND NOT EXISTS (
      SELECT 1 FROM assessment_controls ac
      WHERE ac.assessment_id = a.id
        AND ac.control_def_id = cd.id
  );

DECLARE @added INT = @@ROWCOUNT;
PRINT CONCAT('assessment_controls rows added: ', @added);

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification
-- ============================================================
-- Expect: active_linked = 647 for every active assessment
SELECT
    a.id,
    a.name,
    a.is_active,
    (SELECT COUNT(*)
     FROM assessment_controls ac
     JOIN control_defs cd ON cd.id = ac.control_def_id
     WHERE ac.assessment_id = a.id AND cd.is_active = 1) AS active_linked
FROM assessments a
WHERE a.deleted_at IS NULL
ORDER BY a.id;
