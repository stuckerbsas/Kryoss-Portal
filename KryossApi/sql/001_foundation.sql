SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 001_foundation.sql
-- Kryoss Platform — Foundation: RBAC + Activity Log
-- Azure SQL syntax. Run FIRST before all other migrations.
-- =============================================

-- =============================================
-- MODULES: Each functional area of the portal
-- =============================================
CREATE TABLE modules (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    code        VARCHAR(50)    NOT NULL UNIQUE,
    name        NVARCHAR(100)  NOT NULL,
    sort_order  SMALLINT       NOT NULL DEFAULT 0
);

-- =============================================
-- ACTIONS: What can be done within a module
-- =============================================
CREATE TABLE actions (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    code        VARCHAR(20)    NOT NULL UNIQUE,
    name        NVARCHAR(50)   NOT NULL
);

-- =============================================
-- PERMISSIONS: Module x Action = permission slug
-- =============================================
CREATE TABLE permissions (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    module_id   INT            NOT NULL REFERENCES modules(id),
    action_id   INT            NOT NULL REFERENCES actions(id),
    slug        VARCHAR(80)    NOT NULL UNIQUE,        -- 'assessment:read', 'crm_franchise:delete'
    description NVARCHAR(200),
    CONSTRAINT uq_permission_module_action UNIQUE (module_id, action_id)
);

-- =============================================
-- ROLES: System and custom roles
-- =============================================
CREATE TABLE roles (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    code        VARCHAR(50)    NOT NULL UNIQUE,
    name        NVARCHAR(100)  NOT NULL,
    is_system   BIT            NOT NULL DEFAULT 0,     -- system roles can't be deleted
    -- Audit columns
    created_by  UNIQUEIDENTIFIER NOT NULL,
    created_at  DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by UNIQUEIDENTIFIER,
    modified_at DATETIME2(2),
    deleted_by  UNIQUEIDENTIFIER,
    deleted_at  DATETIME2(2)                           -- NULL = active (soft delete)
);

-- =============================================
-- ROLE_PERMISSIONS: M:N role <-> permission
-- =============================================
CREATE TABLE role_permissions (
    role_id       INT NOT NULL REFERENCES roles(id),
    permission_id INT NOT NULL REFERENCES permissions(id),
    CONSTRAINT pk_role_permissions PRIMARY KEY (role_id, permission_id)
);

-- =============================================
-- USERS: Portal users (Entra ID corp + B2C)
-- Depends on franchises + organizations (created in 002_core.sql)
-- FK constraints added via ALTER TABLE in 002_core.sql
-- =============================================
CREATE TABLE users (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    entra_oid       UNIQUEIDENTIFIER,                  -- Entra ID Object ID (corporate)
    b2c_oid         UNIQUEIDENTIFIER,                  -- B2C Object ID (end client)
    email           NVARCHAR(255)  NOT NULL,
    display_name    NVARCHAR(255)  NOT NULL,
    role_id         INT            NOT NULL REFERENCES roles(id),
    franchise_id    UNIQUEIDENTIFIER,                  -- FK added in 002_core.sql
    organization_id UNIQUEIDENTIFIER,                  -- FK added in 002_core.sql
    auth_source     VARCHAR(10)    NOT NULL
        CONSTRAINT ck_users_auth_source CHECK (auth_source IN ('entra', 'b2c')),
    last_login_at   DATETIME2(2),
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

-- Only one active user per Entra OID or B2C OID
CREATE UNIQUE INDEX ux_users_entra_oid ON users(entra_oid) WHERE entra_oid IS NOT NULL AND deleted_at IS NULL;
CREATE UNIQUE INDEX ux_users_b2c_oid   ON users(b2c_oid)   WHERE b2c_oid IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX ix_users_email            ON users(email)      WHERE deleted_at IS NULL;
CREATE INDEX ix_users_role             ON users(role_id)    WHERE deleted_at IS NULL;

-- =============================================
-- ACTLOG: Full forensic activity log (IBM TSM style)
-- Immutable: no UPDATE or DELETE allowed
-- =============================================
CREATE TABLE actlog (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    [timestamp]     DATETIME2(3)   NOT NULL DEFAULT SYSUTCDATETIME(),
    -- Who
    actor_id        UNIQUEIDENTIFIER,                  -- user ID (NULL for system/agent)
    actor_email     NVARCHAR(255),
    actor_ip        VARCHAR(45),                       -- IPv4 or IPv6
    session_id      NVARCHAR(100),                     -- correlate actions in same session
    -- What
    severity        VARCHAR(4)     NOT NULL
        CONSTRAINT ck_actlog_severity CHECK (severity IN ('INFO', 'WARN', 'ERR', 'CRIT', 'SEC')),
    module          VARCHAR(50)    NOT NULL,            -- 'assessment', 'crm', 'auth', 'agent'
    action          VARCHAR(100)   NOT NULL,            -- 'assessment.run.created', 'machine.enrolled'
    -- On what
    entity_type     VARCHAR(50),                       -- 'Machine', 'AssessmentRun', 'ControlDef'
    entity_id       NVARCHAR(100),                     -- ID of affected entity
    -- Detail
    old_values      NVARCHAR(MAX),                     -- JSON diff (before)
    new_values      NVARCHAR(MAX),                     -- JSON diff (after)
    request_body    NVARCHAR(MAX),                     -- full HTTP request body (sanitized)
    response_code   SMALLINT,                          -- HTTP status code
    duration_ms     INT,                               -- request duration
    message         NVARCHAR(500)                      -- human-readable summary
);

-- Actlog indexes for common query patterns
CREATE INDEX ix_actlog_timestamp ON actlog([timestamp] DESC);
CREATE INDEX ix_actlog_actor     ON actlog(actor_id, [timestamp] DESC);
CREATE INDEX ix_actlog_entity    ON actlog(entity_type, entity_id);
CREATE INDEX ix_actlog_module    ON actlog(module, [timestamp] DESC);
CREATE INDEX ix_actlog_severity  ON actlog(severity) WHERE severity IN ('ERR', 'CRIT', 'SEC');

-- =============================================
-- IMMUTABILITY TRIGGER: Prevent UPDATE/DELETE on actlog
-- =============================================
GO

CREATE TRIGGER trg_actlog_immutable
ON actlog
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    RAISERROR('actlog is immutable. UPDATE and DELETE are not allowed.', 16, 1);
    ROLLBACK TRANSACTION;
END;
GO
