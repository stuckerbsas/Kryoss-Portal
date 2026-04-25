-- 061: Agent v2.0 service mode support
-- Adds heartbeat tracking + agent mode to machines table

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'last_heartbeat_at')
    ALTER TABLE machines ADD last_heartbeat_at datetime2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'agent_mode')
    ALTER TABLE machines ADD agent_mode varchar(10) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'agent_uptime_seconds')
    ALTER TABLE machines ADD agent_uptime_seconds bigint NULL;
