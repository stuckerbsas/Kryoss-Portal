-- 076_agent_loop_status.sql
-- ServiceWorker v3: parallel loops status + error tracking on machines table

ALTER TABLE machines ADD
    loop_status_json NVARCHAR(MAX) NULL,
    last_error_at    DATETIME2 NULL,
    last_error_phase NVARCHAR(50) NULL,
    last_error_msg   NVARCHAR(500) NULL;
GO
