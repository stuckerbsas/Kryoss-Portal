-- =============================================================================
-- 034_compliance_frameworks.sql
-- CA-8 Track 1: Compliance Manager + Framework Mapping
--
-- 4 new tables:
--   1. cloud_assessment_frameworks         — catalog of supported frameworks
--   2. cloud_assessment_framework_controls  — control catalog per framework
--   3. cloud_assessment_finding_control_mappings — finding → control mapping
--   4. cloud_assessment_framework_scores    — per-scan per-framework score
-- =============================================================================

-- 1. Frameworks catalog
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cloud_assessment_frameworks')
BEGIN
    CREATE TABLE cloud_assessment_frameworks (
        id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        code            NVARCHAR(30)     NOT NULL,
        name            NVARCHAR(200)    NOT NULL,
        description     NVARCHAR(MAX),
        version         NVARCHAR(30),
        authority       NVARCHAR(200),
        doc_url         NVARCHAR(500),
        active          BIT              NOT NULL DEFAULT 1,
        created_at      DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_ca_frameworks      PRIMARY KEY (id),
        CONSTRAINT uq_ca_frameworks_code UNIQUE (code)
    );
END;

-- 2. Framework controls
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cloud_assessment_framework_controls')
BEGIN
    CREATE TABLE cloud_assessment_framework_controls (
        id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        framework_id    UNIQUEIDENTIFIER NOT NULL,
        control_code    NVARCHAR(50)     NOT NULL,
        title           NVARCHAR(500)    NOT NULL,
        description     NVARCHAR(MAX),
        category        NVARCHAR(100),
        priority        NVARCHAR(10),
        created_at      DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_ca_framework_controls PRIMARY KEY (id),
        CONSTRAINT fk_ca_fc_framework       FOREIGN KEY (framework_id)
            REFERENCES cloud_assessment_frameworks(id)
    );

    CREATE UNIQUE INDEX ix_ca_framework_controls_code
        ON cloud_assessment_framework_controls(framework_id, control_code);
END;

-- 3. Finding → control mappings
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cloud_assessment_finding_control_mappings')
BEGIN
    CREATE TABLE cloud_assessment_finding_control_mappings (
        id                   UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        area                 NVARCHAR(30)     NOT NULL,
        service              NVARCHAR(30)     NOT NULL,
        feature              NVARCHAR(200)    NOT NULL,
        framework_control_id UNIQUEIDENTIFIER NOT NULL,
        coverage             NVARCHAR(20)     NOT NULL,
        rationale            NVARCHAR(MAX),
        created_at           DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_ca_fcm         PRIMARY KEY (id),
        CONSTRAINT fk_ca_fcm_control FOREIGN KEY (framework_control_id)
            REFERENCES cloud_assessment_framework_controls(id)
    );

    CREATE INDEX ix_ca_fcm_finding ON cloud_assessment_finding_control_mappings(area, service, feature);
    CREATE INDEX ix_ca_fcm_control ON cloud_assessment_finding_control_mappings(framework_control_id);
END;

-- 4. Per-scan framework scores
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cloud_assessment_framework_scores')
BEGIN
    CREATE TABLE cloud_assessment_framework_scores (
        id                UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        scan_id           UNIQUEIDENTIFIER NOT NULL,
        framework_id      UNIQUEIDENTIFIER NOT NULL,
        total_controls    INT              NOT NULL,
        covered_controls  INT              NOT NULL,
        passing_controls  INT              NOT NULL,
        failing_controls  INT              NOT NULL,
        unmapped_controls INT              NOT NULL,
        score_pct         DECIMAL(5,2)     NOT NULL,
        grade             NVARCHAR(5),
        computed_at       DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_ca_fw_scores     PRIMARY KEY (id),
        CONSTRAINT fk_ca_fws_scan      FOREIGN KEY (scan_id)
            REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
        CONSTRAINT fk_ca_fws_framework FOREIGN KEY (framework_id)
            REFERENCES cloud_assessment_frameworks(id)
    );

    CREATE INDEX ix_ca_fw_scores_scan ON cloud_assessment_framework_scores(scan_id);
END;
