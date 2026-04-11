SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- seed_client.sql
-- Create a new client organization + bulk enrollment code
-- USAGE: Update the variables below, then run against KryossDb
-- =============================================

-- ========== EDIT THESE VALUES FOR EACH CLIENT ==========
DECLARE @clientName    NVARCHAR(255) = N'ACME Corp';
DECLARE @clientLegal   NVARCHAR(255) = N'ACME Corporation S.A.';
DECLARE @maxMachines   INT           = 50;
DECLARE @expiryDays    INT           = 30;
-- ========================================================

DECLARE @systemUserId  UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @franchiseId   UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @orgId         UNIQUEIDENTIFIER = NEWID();
DECLARE @assessmentId  INT;

-- Create org
INSERT INTO organizations (id, franchise_id, name, legal_name, status, created_by)
VALUES (@orgId, @franchiseId, @clientName, @clientLegal, 'current', @systemUserId);

-- Find default assessment
SELECT TOP 1 @assessmentId = id FROM assessments WHERE is_default = 1 AND is_active = 1 AND deleted_at IS NULL;
IF @assessmentId IS NULL
    SELECT TOP 1 @assessmentId = id FROM assessments WHERE is_active = 1 AND deleted_at IS NULL;

-- Generate enrollment code
DECLARE @code VARCHAR(19) = UPPER(
    SUBSTRING(CONVERT(VARCHAR(36), NEWID()), 1, 4) + '-' +
    SUBSTRING(CONVERT(VARCHAR(36), NEWID()), 1, 4) + '-' +
    SUBSTRING(CONVERT(VARCHAR(36), NEWID()), 1, 4) + '-' +
    SUBSTRING(CONVERT(VARCHAR(36), NEWID()), 1, 4)
);

INSERT INTO enrollment_codes (organization_id, code, assessment_id, label, max_uses, expires_at, created_by)
VALUES (@orgId, @code, @assessmentId, @clientName + N' - Network Assessment',
        @maxMachines, DATEADD(DAY, @expiryDays, SYSUTCDATETIME()), @systemUserId);

-- Output results
PRINT '================================================';
PRINT '  Client:          ' + CAST(@clientName AS VARCHAR(100));
PRINT '  Org ID:          ' + CAST(@orgId AS VARCHAR(36));
PRINT '  Enrollment Code: ' + @code;
PRINT '  Max Machines:    ' + CAST(@maxMachines AS VARCHAR(10));
PRINT '  Expires:         ' + CAST(@expiryDays AS VARCHAR(10)) + ' days';
PRINT '  Assessment ID:   ' + CAST(ISNULL(@assessmentId, 0) AS VARCHAR(10));
PRINT '================================================';

SELECT @orgId AS organization_id, @clientName AS client_name, @code AS enrollment_code,
       @maxMachines AS max_machines, @assessmentId AS assessment_id;
