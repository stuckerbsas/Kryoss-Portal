-- 066: Closed-set remediation — catalog + tasks + auto-remediate opt-in

-- Remediation action catalog (closed whitelist)
CREATE TABLE remediation_actions (
    id              int IDENTITY(1,1) PRIMARY KEY,
    control_def_id  int NOT NULL,
    action_type     varchar(30) NOT NULL,  -- set_registry, enable_service, disable_service, set_audit_policy, enable_firewall_rule, set_account_policy
    params_template nvarchar(max) NULL,    -- JSON: { "path": "...", "valueName": "...", "valueData": "...", "valueType": "DWORD" }
    risk_level      varchar(10) NOT NULL DEFAULT 'low',  -- low, medium, high
    description     varchar(500) NULL,
    is_active       bit NOT NULL DEFAULT 1,
    created_at      datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT fk_rem_action_control FOREIGN KEY (control_def_id) REFERENCES control_defs(id),
    CONSTRAINT chk_rem_action_type CHECK (action_type IN ('set_registry','enable_service','disable_service','set_audit_policy','enable_firewall_rule','set_account_policy'))
);
CREATE UNIQUE INDEX ux_rem_action_control ON remediation_actions(control_def_id) WHERE is_active = 1;

-- Remediation tasks (per-machine work items)
CREATE TABLE remediation_tasks (
    id              bigint IDENTITY(1,1) PRIMARY KEY,
    organization_id uniqueidentifier NOT NULL,
    machine_id      uniqueidentifier NOT NULL,
    control_def_id  int NOT NULL,
    action_id       int NOT NULL,
    action_type     varchar(30) NOT NULL,
    params          nvarchar(max) NULL,     -- JSON: resolved params for this specific execution
    status          varchar(20) NOT NULL DEFAULT 'pending',  -- pending, approved, running, completed, failed, rolled_back
    previous_value  nvarchar(max) NULL,     -- JSON: value before change (for rollback)
    new_value       nvarchar(max) NULL,     -- JSON: value after change (verification)
    error_message   varchar(1000) NULL,
    created_by      uniqueidentifier NOT NULL,
    approved_by     uniqueidentifier NULL,
    approved_at     datetime2 NULL,
    executed_at     datetime2 NULL,
    completed_at    datetime2 NULL,
    created_at      datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT fk_rem_task_org FOREIGN KEY (organization_id) REFERENCES organizations(id),
    CONSTRAINT fk_rem_task_machine FOREIGN KEY (machine_id) REFERENCES machines(id),
    CONSTRAINT fk_rem_task_control FOREIGN KEY (control_def_id) REFERENCES control_defs(id),
    CONSTRAINT fk_rem_task_action FOREIGN KEY (action_id) REFERENCES remediation_actions(id),
    CONSTRAINT chk_rem_task_status CHECK (status IN ('pending','approved','running','completed','failed','rolled_back'))
);
CREATE INDEX ix_rem_task_machine ON remediation_tasks(machine_id, status);
CREATE INDEX ix_rem_task_org ON remediation_tasks(organization_id, created_at DESC);

-- Auto-remediate opt-in per org per control
CREATE TABLE org_auto_remediate (
    id              bigint IDENTITY(1,1) PRIMARY KEY,
    organization_id uniqueidentifier NOT NULL,
    control_def_id  int NOT NULL,
    enabled_by      uniqueidentifier NOT NULL,
    enabled_at      datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT fk_org_auto_rem_org FOREIGN KEY (organization_id) REFERENCES organizations(id),
    CONSTRAINT fk_org_auto_rem_control FOREIGN KEY (control_def_id) REFERENCES control_defs(id),
    CONSTRAINT uq_org_auto_rem UNIQUE (organization_id, control_def_id)
);
