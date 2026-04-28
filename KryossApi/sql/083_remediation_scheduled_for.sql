-- 083: Add scheduled_for to remediation_tasks
-- Allows portal users to schedule when a remediation task should be dispatched

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('remediation_tasks') AND name = 'scheduled_for')
BEGIN
    ALTER TABLE remediation_tasks ADD scheduled_for DATETIME2 NULL;
END
GO
