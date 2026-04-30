-- 090_remediation_nullable_control.sql — Allow remediation tasks without a control_def (operational tasks like WU/app updates)

-- Drop FK constraints first
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_remediation_tasks_control_defs_control_def_id')
    ALTER TABLE remediation_tasks DROP CONSTRAINT FK_remediation_tasks_control_defs_control_def_id;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_remediation_tasks_remediation_actions_action_id')
    ALTER TABLE remediation_tasks DROP CONSTRAINT FK_remediation_tasks_remediation_actions_action_id;

-- Make columns nullable
ALTER TABLE remediation_tasks ALTER COLUMN control_def_id INT NULL;
ALTER TABLE remediation_tasks ALTER COLUMN action_id INT NULL;

-- Re-add FKs as nullable
ALTER TABLE remediation_tasks ADD CONSTRAINT FK_remediation_tasks_control_defs_control_def_id
    FOREIGN KEY (control_def_id) REFERENCES control_defs(id);
ALTER TABLE remediation_tasks ADD CONSTRAINT FK_remediation_tasks_remediation_actions_action_id
    FOREIGN KEY (action_id) REFERENCES remediation_actions(id);

PRINT '090_remediation_nullable_control.sql applied';
GO
