-- 078_db_normalization.sql
-- DB-NORM: Normalize all JSON columns into relational tables
-- Idempotent — safe to re-run

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ══════════════════════════════════════════════
-- 1A. control_check_params (from control_defs.check_json)
-- ══════════════════════════════════════════════
IF OBJECT_ID('control_check_params', 'U') IS NULL
CREATE TABLE control_check_params (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    control_def_id  INT NOT NULL REFERENCES control_defs(id) ON DELETE CASCADE,
    param_name      VARCHAR(50) NOT NULL,
    param_value     NVARCHAR(500),
    CONSTRAINT uq_ccp_def_param UNIQUE (control_def_id, param_name)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_ccp_control_def')
    CREATE INDEX ix_ccp_control_def ON control_check_params(control_def_id);
GO

-- ══════════════════════════════════════════════
-- 1B. software table — add CPE columns
-- ══════════════════════════════════════════════
IF COL_LENGTH('software', 'cpe_vendor') IS NULL
    ALTER TABLE software ADD cpe_vendor VARCHAR(100) NULL;
IF COL_LENGTH('software', 'cpe_product') IS NULL
    ALTER TABLE software ADD cpe_product VARCHAR(100) NULL;
IF COL_LENGTH('software', 'is_commercial') IS NULL
    ALTER TABLE software ADD is_commercial BIT NOT NULL DEFAULT 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_software_cpe')
    CREATE INDEX ix_software_cpe ON software(cpe_vendor, cpe_product) WHERE cpe_vendor IS NOT NULL;
GO

-- ══════════════════════════════════════════════
-- 1C. machine_local_admins (from machines.local_admins_json)
-- ══════════════════════════════════════════════
IF OBJECT_ID('machine_local_admins', 'U') IS NULL
CREATE TABLE machine_local_admins (
    id          BIGINT IDENTITY(1,1) PRIMARY KEY,
    machine_id  UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id) ON DELETE CASCADE,
    name        NVARCHAR(200) NOT NULL,
    type        VARCHAR(20) NOT NULL,
    source      VARCHAR(30),
    CONSTRAINT uq_mla_machine_name UNIQUE (machine_id, name)
);
GO

-- ══════════════════════════════════════════════
-- 1D. machine_loop_status (from machines.loop_status_json)
-- ══════════════════════════════════════════════
IF OBJECT_ID('machine_loop_status', 'U') IS NULL
CREATE TABLE machine_loop_status (
    machine_id    UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id) ON DELETE CASCADE,
    loop_name     VARCHAR(20) NOT NULL,
    state         VARCHAR(15) NOT NULL DEFAULT 'idle',
    last_run_at   DATETIME2(3),
    duration_ms   INT,
    last_error    NVARCHAR(500),
    PRIMARY KEY (machine_id, loop_name)
);
GO

-- ══════════════════════════════════════════════
-- 1E. org_priority_services (from organizations.priority_services_json)
-- ══════════════════════════════════════════════
IF OBJECT_ID('org_priority_services', 'U') IS NULL
CREATE TABLE org_priority_services (
    id                INT IDENTITY(1,1) PRIMARY KEY,
    organization_id   UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    service_name      VARCHAR(100) NOT NULL,
    CONSTRAINT uq_ops_org_svc UNIQUE (organization_id, service_name)
);
GO

-- ══════════════════════════════════════════════
-- 1F. machine_traceroute_hops (from machine_network_diag.traceroute_json)
-- ══════════════════════════════════════════════
IF OBJECT_ID('machine_traceroute_hops', 'U') IS NULL
CREATE TABLE machine_traceroute_hops (
    id          BIGINT IDENTITY(1,1) PRIMARY KEY,
    diag_id     INT NOT NULL REFERENCES machine_network_diag(id) ON DELETE CASCADE,
    hop_number  SMALLINT NOT NULL,
    ip_address  VARCHAR(45),
    hostname    VARCHAR(200),
    rtt_ms      DECIMAL(8,2),
    CONSTRAINT uq_mth_diag_hop UNIQUE (diag_id, hop_number)
);
GO

-- ══════════════════════════════════════════════
-- 1G. cloud_finding_properties (from cloud_assessment_azure_resources.properties_json)
-- ══════════════════════════════════════════════
IF OBJECT_ID('cloud_finding_properties', 'U') IS NULL
CREATE TABLE cloud_finding_properties (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    azure_resource_id BIGINT NOT NULL REFERENCES cloud_assessment_azure_resources(id) ON DELETE CASCADE,
    prop_name       VARCHAR(100) NOT NULL,
    prop_value      NVARCHAR(1000),
    CONSTRAINT uq_cfp_res_prop UNIQUE (azure_resource_id, prop_name)
);
GO

-- ══════════════════════════════════════════════
-- 1G2. cloud_resource_risk_flags (from cloud_assessment_azure_resources.risk_flags)
-- ══════════════════════════════════════════════
IF OBJECT_ID('cloud_resource_risk_flags', 'U') IS NULL
CREATE TABLE cloud_resource_risk_flags (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    azure_resource_id BIGINT NOT NULL REFERENCES cloud_assessment_azure_resources(id) ON DELETE CASCADE,
    flag_code       VARCHAR(50) NOT NULL,
    CONSTRAINT uq_crrf_res_flag UNIQUE (azure_resource_id, flag_code)
);
GO

-- ══════════════════════════════════════════════
-- 1H. mail_domain_spf_warnings + mail_domain_dkim_selectors
-- ══════════════════════════════════════════════
IF OBJECT_ID('mail_domain_spf_warnings', 'U') IS NULL
CREATE TABLE mail_domain_spf_warnings (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    mail_domain_id  UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_mail_domains(id) ON DELETE CASCADE,
    warning_text    NVARCHAR(500) NOT NULL
);
GO

IF OBJECT_ID('mail_domain_dkim_selectors', 'U') IS NULL
CREATE TABLE mail_domain_dkim_selectors (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    mail_domain_id  UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_mail_domains(id) ON DELETE CASCADE,
    selector        VARCHAR(100) NOT NULL,
    is_valid        BIT NOT NULL DEFAULT 0
);
GO

-- ══════════════════════════════════════════════
-- 1I. shared_mailbox_delegates (from cloud_assessment_shared_mailboxes)
-- ══════════════════════════════════════════════
IF OBJECT_ID('shared_mailbox_delegates', 'U') IS NULL
CREATE TABLE shared_mailbox_delegates (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    mailbox_id      UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_shared_mailboxes(id) ON DELETE CASCADE,
    user_email      VARCHAR(320) NOT NULL,
    permission_type VARCHAR(20) NOT NULL,
    CONSTRAINT uq_smd_mbx_user_perm UNIQUE (mailbox_id, user_email, permission_type)
);
GO

-- ══════════════════════════════════════════════
-- 1J. alert_payload_fields (from cloud_assessment_alerts_sent.payload_json)
-- ══════════════════════════════════════════════
IF OBJECT_ID('alert_payload_fields', 'U') IS NULL
CREATE TABLE alert_payload_fields (
    id          BIGINT IDENTITY(1,1) PRIMARY KEY,
    alert_id    BIGINT NOT NULL REFERENCES cloud_assessment_alerts_sent(id) ON DELETE CASCADE,
    field_name  VARCHAR(100) NOT NULL,
    field_value NVARCHAR(1000),
    CONSTRAINT uq_apf_alert_field UNIQUE (alert_id, field_name)
);
GO

-- ══════════════════════════════════════════════
-- 1K. remediation_action_params (from remediation_actions.params_template)
-- ══════════════════════════════════════════════
IF OBJECT_ID('remediation_action_params', 'U') IS NULL
CREATE TABLE remediation_action_params (
    id                    INT IDENTITY(1,1) PRIMARY KEY,
    remediation_action_id INT NOT NULL REFERENCES remediation_actions(id) ON DELETE CASCADE,
    param_name            VARCHAR(50) NOT NULL,
    param_value           NVARCHAR(500),
    param_type            VARCHAR(20) NOT NULL DEFAULT 'string',
    CONSTRAINT uq_rap_action_param UNIQUE (remediation_action_id, param_name)
);
GO

-- ══════════════════════════════════════════════
-- 2A. cve_entries — add KEV + CPE columns
-- ══════════════════════════════════════════════
IF COL_LENGTH('cve_entries', 'is_known_exploited') IS NULL
    ALTER TABLE cve_entries ADD is_known_exploited BIT NOT NULL DEFAULT 0;
IF COL_LENGTH('cve_entries', 'cpe_match_string') IS NULL
    ALTER TABLE cve_entries ADD cpe_match_string VARCHAR(500);
IF COL_LENGTH('cve_entries', 'references_url') IS NULL
    ALTER TABLE cve_entries ADD references_url VARCHAR(500);
IF COL_LENGTH('cve_entries', 'kev_due_date') IS NULL
    ALTER TABLE cve_entries ADD kev_due_date DATE;
IF COL_LENGTH('cve_entries', 'kev_added_date') IS NULL
    ALTER TABLE cve_entries ADD kev_added_date DATE;
GO

-- ══════════════════════════════════════════════
-- 2B. cve_product_map (pre-computed CVE ↔ software mapping)
-- ══════════════════════════════════════════════
IF OBJECT_ID('cve_product_map', 'U') IS NULL
CREATE TABLE cve_product_map (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    cve_entry_id        INT NOT NULL REFERENCES cve_entries(id) ON DELETE CASCADE,
    software_id         INT NOT NULL REFERENCES software(id) ON DELETE CASCADE,
    affected_below      VARCHAR(50),
    fixed_version       VARCHAR(50),
    CONSTRAINT uq_cpm_cve_sw UNIQUE (cve_entry_id, software_id)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_cpm_software')
    CREATE INDEX ix_cpm_software ON cve_product_map(software_id);
GO

-- ══════════════════════════════════════════════
-- 3. DROP JSON columns (DB is empty, no data loss)
-- ══════════════════════════════════════════════

-- machines: drop JSON columns
IF COL_LENGTH('machines', 'local_admins_json') IS NOT NULL
    ALTER TABLE machines DROP COLUMN local_admins_json;
IF COL_LENGTH('machines', 'loop_status_json') IS NOT NULL
    ALTER TABLE machines DROP COLUMN loop_status_json;
GO

-- organizations: drop JSON column
IF COL_LENGTH('organizations', 'priority_services_json') IS NOT NULL
    ALTER TABLE organizations DROP COLUMN priority_services_json;
GO

-- machine_network_diag: drop JSON columns
IF COL_LENGTH('machine_network_diag', 'traceroute_json') IS NOT NULL
    ALTER TABLE machine_network_diag DROP COLUMN traceroute_json;
IF COL_LENGTH('machine_network_diag', 'raw_data') IS NOT NULL
    ALTER TABLE machine_network_diag DROP COLUMN raw_data;
GO

-- cloud_assessment_azure_resources: drop JSON columns
IF COL_LENGTH('cloud_assessment_azure_resources', 'properties_json') IS NOT NULL
    ALTER TABLE cloud_assessment_azure_resources DROP COLUMN properties_json;
IF COL_LENGTH('cloud_assessment_azure_resources', 'risk_flags') IS NOT NULL
    ALTER TABLE cloud_assessment_azure_resources DROP COLUMN risk_flags;
GO

-- cloud_assessment_mail_domains: drop JSON columns
IF COL_LENGTH('cloud_assessment_mail_domains', 'spf_warnings') IS NOT NULL
    ALTER TABLE cloud_assessment_mail_domains DROP COLUMN spf_warnings;
IF COL_LENGTH('cloud_assessment_mail_domains', 'dkim_selectors') IS NOT NULL
    ALTER TABLE cloud_assessment_mail_domains DROP COLUMN dkim_selectors;
GO

-- cloud_assessment_shared_mailboxes: drop JSON columns
IF COL_LENGTH('cloud_assessment_shared_mailboxes', 'full_access_users') IS NOT NULL
    ALTER TABLE cloud_assessment_shared_mailboxes DROP COLUMN full_access_users;
IF COL_LENGTH('cloud_assessment_shared_mailboxes', 'send_as_users') IS NOT NULL
    ALTER TABLE cloud_assessment_shared_mailboxes DROP COLUMN send_as_users;
GO

-- cloud_assessment_alerts_sent: drop JSON column
IF COL_LENGTH('cloud_assessment_alerts_sent', 'payload_json') IS NOT NULL
    ALTER TABLE cloud_assessment_alerts_sent DROP COLUMN payload_json;
GO

-- remediation_actions: drop JSON column
IF COL_LENGTH('remediation_actions', 'params_template') IS NOT NULL
    ALTER TABLE remediation_actions DROP COLUMN params_template;
GO

PRINT '=== 078_db_normalization complete ===';
GO
