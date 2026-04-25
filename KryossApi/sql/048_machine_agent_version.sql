-- 048_machine_agent_version.sql
-- A-09: Agent version tracking on machines table

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'agent_version')
    ALTER TABLE machines ADD agent_version NVARCHAR(20) NULL;
GO
