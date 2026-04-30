-- 078d_normalize_remediation_params.sql
-- Re-add params_template so seed_066 can run, then parse → remediation_action_params, then drop
-- Run AFTER seed_066_remediation_actions.sql

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Step 1: Parse params_template → remediation_action_params
IF COL_LENGTH('remediation_actions', 'params_template') IS NULL
BEGIN
    PRINT 'params_template column missing — add it first, then run seed_066, then re-run this script.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM remediation_actions WHERE params_template IS NOT NULL)
BEGIN
    PRINT 'params_template is empty — run seed_066 first, then re-run this script.';
END
ELSE
BEGIN
    DELETE FROM remediation_action_params;

    INSERT INTO remediation_action_params (remediation_action_id, param_name, param_value)
    SELECT
        a.id,
        j.[key],
        j.[value]
    FROM remediation_actions a
    CROSS APPLY OPENJSON(a.params_template) j
    WHERE a.params_template IS NOT NULL
      AND ISJSON(a.params_template) = 1;

    PRINT CONCAT('remediation_action_params: ', @@ROWCOUNT, ' rows inserted');

    ALTER TABLE remediation_actions DROP COLUMN params_template;
    PRINT 'Dropped params_template column';
END
GO
