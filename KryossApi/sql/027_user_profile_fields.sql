-- =============================================================================
-- Migration 027: User profile fields (phone, job_title)
--
-- Adds nullable profile columns to `users` so portal operators can be
-- identified in generated reports (footer shows "Prepared by: name · email ·
-- phone"). Populated later via a profile page (Path A — reports first,
-- profile UI deferred).
--
-- Idempotent: safe to run multiple times.
-- =============================================================================

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = 'phone' AND Object_ID = Object_ID('users'))
BEGIN
    ALTER TABLE users ADD phone NVARCHAR(50) NULL;
    PRINT 'users.phone added';
END
ELSE
    PRINT 'users.phone already exists';

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = 'job_title' AND Object_ID = Object_ID('users'))
BEGIN
    ALTER TABLE users ADD job_title NVARCHAR(200) NULL;
    PRINT 'users.job_title added';
END
ELSE
    PRINT 'users.job_title already exists';

GO
