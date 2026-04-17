-- 031_azure_consent_tracking.sql
-- Add verify-and-track-failure columns to cloud_assessment_azure_subscriptions.
-- Supports the CA-6 Subsession A Azure ARM consent verification flow.
-- consent_state (VARCHAR(30)) already exists from 030_cloud_assessment.sql and is
-- application-assigned — NOT altered here.

-- Timestamp of last successful ARM verify call (null = never verified)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('cloud_assessment_azure_subscriptions') AND name = 'last_verified_at')
    ALTER TABLE cloud_assessment_azure_subscriptions ADD last_verified_at DATETIME2(2) NULL;
GO

-- Last verification failure message (null on success)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('cloud_assessment_azure_subscriptions') AND name = 'error_message')
    ALTER TABLE cloud_assessment_azure_subscriptions ADD error_message NVARCHAR(MAX) NULL;
GO
