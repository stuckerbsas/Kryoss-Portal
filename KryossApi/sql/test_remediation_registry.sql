-- TEST: Write a harmless registry key on ACCESSCT-SERVER to validate remediation pipeline
-- Path: HKLM\SOFTWARE\Policies\Kryoss\TestRemediation\Validated = 1 (DWORD)
-- Safe to run multiple times — creates new task each time

DECLARE @machineId UNIQUEIDENTIFIER;
DECLARE @orgId UNIQUEIDENTIFIER;
DECLARE @actionId INT;
DECLARE @controlDefId INT;
DECLARE @userId UNIQUEIDENTIFIER;

-- Find machine
SELECT @machineId = id, @orgId = organization_id
FROM machines
WHERE hostname = 'ACCESSCT-SERVER' AND is_active = 1;

IF @machineId IS NULL
BEGIN
    RAISERROR('Machine ACCESSCT-SERVER not found or inactive', 16, 1);
    RETURN;
END

-- Find an admin user for created_by
SELECT TOP 1 @userId = u.id FROM users u
INNER JOIN roles r ON u.role_id = r.id
WHERE r.code = 'super_admin' AND u.deleted_at IS NULL;
IF @userId IS NULL
    SELECT TOP 1 @userId = id FROM users WHERE deleted_at IS NULL;

-- Pick any existing set_registry remediation action
SELECT TOP 1 @actionId = ra.id, @controlDefId = ra.control_def_id
FROM remediation_actions ra
WHERE ra.action_type = 'set_registry' AND ra.is_active = 1;

IF @actionId IS NULL
BEGIN
    RAISERROR('No active set_registry remediation action found', 16, 1);
    RETURN;
END

-- Create approved task with test params
INSERT INTO remediation_tasks (
    organization_id, machine_id, control_def_id, action_id,
    action_type, params, status, created_by, approved_by, approved_at, created_at
)
VALUES (
    @orgId, @machineId, @controlDefId, @actionId,
    'set_registry',
    '{"path":"HKLM:\\SOFTWARE\\Policies\\Kryoss\\TestRemediation","valueName":"Validated","valueData":"1","valueType":"DWORD"}',
    'approved', @userId, @userId,
    GETUTCDATE(), GETUTCDATE()
);

SELECT 'Task created' AS result,
       SCOPE_IDENTITY() AS task_id,
       @machineId AS machine_id,
       'ACCESSCT-SERVER' AS hostname;

-- To check result later:
-- SELECT * FROM remediation_tasks WHERE machine_id = @machineId ORDER BY created_at DESC;
-- On the machine: reg query "HKLM\SOFTWARE\Policies\Kryoss\TestRemediation"
