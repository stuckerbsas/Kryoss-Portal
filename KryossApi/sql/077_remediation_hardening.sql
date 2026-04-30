-- 077_remediation_hardening.sql
-- SEC-REM-FW + SVC-MGR: task signatures, immutable audit trail, service inventory, priority services
-- Idempotent — safe to re-run after partial failure

-- 1A: Task signature column
IF COL_LENGTH('remediation_tasks', 'signature_hash') IS NULL
    ALTER TABLE remediation_tasks ADD signature_hash VARCHAR(64) NULL;
GO

-- 1B: Immutable audit trail
IF OBJECT_ID('remediation_log', 'U') IS NULL
BEGIN
    CREATE TABLE remediation_log (
        id              BIGINT IDENTITY(1,1) PRIMARY KEY,
        task_id         BIGINT NOT NULL,
        machine_id      UNIQUEIDENTIFIER NOT NULL,
        organization_id UNIQUEIDENTIFIER NOT NULL,
        event_type      VARCHAR(30) NOT NULL,
        actor_id        UNIQUEIDENTIFIER NULL,
        action_type     VARCHAR(30) NOT NULL,
        control_def_id  INT NULL,
        service_name    VARCHAR(100) NULL,
        params_hash     VARCHAR(64) NULL,
        previous_value  NVARCHAR(500) NULL,
        new_value       NVARCHAR(500) NULL,
        error_message   NVARCHAR(500) NULL,
        signature_hash  VARCHAR(64) NULL,
        ip_address      VARCHAR(45) NULL,
        [timestamp]     DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_rem_log_machine')
    CREATE INDEX ix_rem_log_machine ON remediation_log(machine_id, [timestamp] DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_rem_log_org')
    CREATE INDEX ix_rem_log_org ON remediation_log(organization_id, [timestamp] DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_rem_log_task')
    CREATE INDEX ix_rem_log_task ON remediation_log(task_id);
GO

-- 1C: Priority services per org
IF COL_LENGTH('organizations', 'priority_services_json') IS NULL
    ALTER TABLE organizations ADD priority_services_json NVARCHAR(MAX) NULL;
GO

-- 2B: Machine service inventory
IF OBJECT_ID('machine_services', 'U') IS NULL
BEGIN
    CREATE TABLE machine_services (
        id           BIGINT IDENTITY(1,1) PRIMARY KEY,
        machine_id   UNIQUEIDENTIFIER NOT NULL,
        name         VARCHAR(100) NOT NULL,
        display_name NVARCHAR(256) NULL,
        status       VARCHAR(20) NOT NULL,
        startup_type VARCHAR(20) NOT NULL,
        updated_at   DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_machine_service UNIQUE (machine_id, name)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_machine_services_machine')
    CREATE INDEX ix_machine_services_machine ON machine_services(machine_id);
GO

-- 2B: Expand remediation action types CHECK constraint
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'chk_rem_action_type')
    ALTER TABLE remediation_actions DROP CONSTRAINT chk_rem_action_type;

ALTER TABLE remediation_actions ADD CONSTRAINT chk_rem_action_type
    CHECK (action_type IN ('set_registry','enable_service','disable_service',
        'restart_service','stop_service','set_service_startup',
        'set_audit_policy','set_account_policy'));
GO
