-- 079_blob_payload_offload.sql
-- Add blob URL column to assessment_runs for Cool tier storage offload

IF COL_LENGTH('assessment_runs', 'raw_payload_blob_url') IS NULL
    ALTER TABLE assessment_runs ADD raw_payload_blob_url VARCHAR(256) NULL;
GO

PRINT 'Added assessment_runs.raw_payload_blob_url';
GO
