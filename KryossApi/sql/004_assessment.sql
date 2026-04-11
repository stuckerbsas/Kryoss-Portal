SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 004_assessment.sql
-- Kryoss Platform — Assessment: Frameworks, Platforms, Controls, Runs, Results
-- Depends on: 002_core.sql (organizations), 003_cmdb.sql (machines)
-- =============================================

-- =============================================
-- FRAMEWORKS: Compliance frameworks (CIS, NIST, HIPAA, PCI-DSS)
-- =============================================
CREATE TABLE frameworks (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    code            VARCHAR(20)    NOT NULL UNIQUE,     -- CIS, NIST, HIPAA, PCI-DSS
    name            NVARCHAR(255)  NOT NULL,
    version         VARCHAR(50),
    description     NVARCHAR(MAX),
    is_active       BIT            NOT NULL DEFAULT 1,
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

-- =============================================
-- PLATFORMS: Target OS platforms (W10, W11, MS22, MS25, DC25)
-- =============================================
CREATE TABLE platforms (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    code            VARCHAR(30)    NOT NULL UNIQUE,     -- W10, W11, MS22, MS25, DC25
    name            NVARCHAR(255)  NOT NULL,
    is_active       BIT            NOT NULL DEFAULT 1,
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

-- =============================================
-- CONTROL_CATEGORIES: Grouping for controls
-- =============================================
CREATE TABLE control_categories (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    name            NVARCHAR(100)  NOT NULL UNIQUE,    -- Personalization, LAPS, Firewall...
    sort_order      SMALLINT       NOT NULL DEFAULT 0,
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

-- =============================================
-- CONTROL_DEFS: Individual security control definitions
-- The core of the assessment engine. check_json contains
-- the instructions sent to the agent + expected values for
-- server-side evaluation.
-- =============================================
CREATE TABLE control_defs (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    control_id      VARCHAR(20)    NOT NULL UNIQUE,    -- BL-001
    category_id     INT            NOT NULL REFERENCES control_categories(id),
    name            NVARCHAR(300)  NOT NULL,
    [type]          VARCHAR(20)    NOT NULL             -- registry/secedit/auditpol/firewall/service/netaccount/command
        CONSTRAINT ck_ctrldef_type CHECK ([type] IN (
            'registry', 'secedit', 'auditpol', 'firewall',
            'service', 'netaccount', 'command'
        )),
    severity        VARCHAR(10)
        CONSTRAINT ck_ctrldef_severity CHECK (severity IN ('low', 'medium', 'high', 'critical')),
    -- check_json: Instructions for agent (what to read) + expected values (for server-side eval)
    -- Agent receives: id, type, hive/path/valueName, display
    -- Server uses: expected, operator, missingBehavior
    check_json      NVARCHAR(MAX)  NOT NULL
        CONSTRAINT ck_ctrldef_json CHECK (ISJSON(check_json) = 1),
    remediation     NVARCHAR(MAX),
    is_active       BIT            NOT NULL DEFAULT 1,
    version         INT            NOT NULL DEFAULT 1,
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_ctrldef_category ON control_defs(category_id) WHERE is_active = 1 AND deleted_at IS NULL;
CREATE INDEX ix_ctrldef_type     ON control_defs([type])      WHERE is_active = 1 AND deleted_at IS NULL;

-- =============================================
-- CONTROL_FRAMEWORKS: M:N controls <-> frameworks
-- =============================================
CREATE TABLE control_frameworks (
    control_def_id  INT NOT NULL REFERENCES control_defs(id),
    framework_id    INT NOT NULL REFERENCES frameworks(id),
    framework_ref   VARCHAR(50),                       -- e.g. 'CIS 5.2.1'
    CONSTRAINT pk_control_frameworks PRIMARY KEY (control_def_id, framework_id)
);

-- =============================================
-- CONTROL_PLATFORMS: M:N controls <-> platforms
-- =============================================
CREATE TABLE control_platforms (
    control_def_id  INT NOT NULL REFERENCES control_defs(id),
    platform_id     INT NOT NULL REFERENCES platforms(id),
    CONSTRAINT pk_control_platforms PRIMARY KEY (control_def_id, platform_id)
);

-- =============================================
-- ASSESSMENTS: Assessment profiles (configurable per org)
-- "Quick Scan", "Full CIS L1", "HIPAA Compliance", etc.
-- =============================================
CREATE TABLE assessments (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    name            NVARCHAR(255)  NOT NULL,
    description     NVARCHAR(MAX),
    is_default      BIT            NOT NULL DEFAULT 0, -- run this if none specified
    is_active       BIT            NOT NULL DEFAULT 1,
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_assessments_org ON assessments(organization_id) WHERE is_active = 1 AND deleted_at IS NULL;

-- =============================================
-- ASSESSMENT_CONTROLS: Which controls are in each assessment
-- =============================================
CREATE TABLE assessment_controls (
    assessment_id   INT NOT NULL REFERENCES assessments(id),
    control_def_id  INT NOT NULL REFERENCES control_defs(id),
    CONSTRAINT pk_assessment_controls PRIMARY KEY (assessment_id, control_def_id)
);

-- =============================================
-- ASSESSMENT_RUNS: One per machine per execution
-- =============================================
CREATE TABLE assessment_runs (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    assessment_id   INT            REFERENCES assessments(id),
    agent_version   VARCHAR(20),
    controls_version VARCHAR(20),
    global_score    DECIMAL(5,2),                      -- 0.00 - 100.00
    grade           VARCHAR(10)
        CONSTRAINT ck_run_grade CHECK (grade IN ('A+', 'A', 'B', 'C', 'D', 'F')),
    total_points    SMALLINT,
    earned_points   SMALLINT,
    pass_count      SMALLINT,
    warn_count      SMALLINT,
    fail_count      SMALLINT,
    duration_ms     INT,
    started_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    completed_at    DATETIME2(2),
    raw_payload     NVARCHAR(MAX)                      -- full agent JSON (archival)
        CONSTRAINT ck_run_json CHECK (ISJSON(raw_payload) = 1 OR raw_payload IS NULL)
);

CREATE INDEX ix_runs_org     ON assessment_runs(organization_id, started_at DESC);
CREATE INDEX ix_runs_machine ON assessment_runs(machine_id, started_at DESC);

-- =============================================
-- CONTROL_RESULTS: Individual control results per run
-- =============================================
CREATE TABLE control_results (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    run_id          UNIQUEIDENTIFIER NOT NULL REFERENCES assessment_runs(id),
    control_def_id  INT            NOT NULL REFERENCES control_defs(id),
    status          VARCHAR(10)    NOT NULL
        CONSTRAINT ck_ctrlres_status CHECK (status IN ('pass', 'warn', 'fail', 'info', 'error')),
    score           SMALLINT       NOT NULL DEFAULT 0,
    max_score       SMALLINT       NOT NULL DEFAULT 0,
    finding         NVARCHAR(MAX),
    actual_value    NVARCHAR(500),
    CONSTRAINT uq_ctrlres UNIQUE (run_id, control_def_id)
);

CREATE INDEX ix_ctrlres_control ON control_results(control_def_id, status);
CREATE INDEX ix_ctrlres_run     ON control_results(run_id);

-- =============================================
-- RUN_FRAMEWORK_SCORES: Per-framework scores per run
-- Denormalized for fast dashboard queries
-- =============================================
CREATE TABLE run_framework_scores (
    run_id          UNIQUEIDENTIFIER NOT NULL REFERENCES assessment_runs(id),
    framework_id    INT            NOT NULL REFERENCES frameworks(id),
    score           DECIMAL(5,2)   NOT NULL,
    pass_count      SMALLINT       NOT NULL DEFAULT 0,
    warn_count      SMALLINT       NOT NULL DEFAULT 0,
    fail_count      SMALLINT       NOT NULL DEFAULT 0,
    CONSTRAINT pk_run_fw_scores PRIMARY KEY (run_id, framework_id)
);
