-- 062: Trial enrollment support
-- Enrollment codes can be marked as trial, machines inherit trial status

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('enrollment_codes') AND name = 'is_trial')
    ALTER TABLE enrollment_codes ADD is_trial bit NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('enrollment_codes') AND name = 'trial_days')
    ALTER TABLE enrollment_codes ADD trial_days int NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'is_trial')
    ALTER TABLE machines ADD is_trial bit NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'trial_expires_at')
    ALTER TABLE machines ADD trial_expires_at datetime2 NULL;
