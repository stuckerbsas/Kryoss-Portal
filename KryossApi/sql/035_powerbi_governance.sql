-- ============================================================
-- 035_powerbi_governance.sql
-- CA-9: Power BI Governance pipeline tables
-- ============================================================

-- 1. Per-org connection tracking (singleton per org)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cloud_assessment_powerbi_connection')
BEGIN
    CREATE TABLE cloud_assessment_powerbi_connection (
        organization_id UNIQUEIDENTIFIER NOT NULL,
        enabled BIT NOT NULL DEFAULT 0,
        last_verified_at DATETIME2(2),
        connection_state NVARCHAR(20) NOT NULL DEFAULT 'pending',
        error_message NVARCHAR(MAX),
        updated_at DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_powerbi_connection PRIMARY KEY (organization_id),
        CONSTRAINT FK_powerbi_conn_org FOREIGN KEY (organization_id) REFERENCES organizations(id) ON DELETE CASCADE
    );
END;

-- 2. Per-scan workspace inventory
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cloud_assessment_powerbi_workspaces')
BEGIN
    CREATE TABLE cloud_assessment_powerbi_workspaces (
        id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        scan_id UNIQUEIDENTIFIER NOT NULL,
        workspace_id NVARCHAR(50) NOT NULL,
        name NVARCHAR(500) NOT NULL,
        type NVARCHAR(30),
        state NVARCHAR(30),
        is_on_dedicated_capacity BIT,
        capacity_id NVARCHAR(50),
        has_workspace_level_settings BIT,
        member_count INT,
        admin_count INT,
        external_user_count INT,
        dataset_count INT,
        report_count INT,
        dashboard_count INT,
        dataflow_count INT,
        last_updated_date DATETIME2(2),
        created_at DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_powerbi_workspaces PRIMARY KEY (id),
        CONSTRAINT FK_powerbi_ws_scan FOREIGN KEY (scan_id) REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE
    );
    CREATE INDEX ix_powerbi_ws_scan ON cloud_assessment_powerbi_workspaces(scan_id);
END;

-- 3. Per-scan gateway inventory
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cloud_assessment_powerbi_gateways')
BEGIN
    CREATE TABLE cloud_assessment_powerbi_gateways (
        id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        scan_id UNIQUEIDENTIFIER NOT NULL,
        gateway_id NVARCHAR(50) NOT NULL,
        name NVARCHAR(500) NOT NULL,
        type NVARCHAR(30),
        public_key_valid BIT,
        status NVARCHAR(30),
        version NVARCHAR(50),
        contact_information NVARCHAR(500),
        created_at DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_powerbi_gateways PRIMARY KEY (id),
        CONSTRAINT FK_powerbi_gw_scan FOREIGN KEY (scan_id) REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE
    );
    CREATE INDEX ix_powerbi_gw_scan ON cloud_assessment_powerbi_gateways(scan_id);
END;

-- 4. Per-scan capacity inventory
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cloud_assessment_powerbi_capacities')
BEGIN
    CREATE TABLE cloud_assessment_powerbi_capacities (
        id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        scan_id UNIQUEIDENTIFIER NOT NULL,
        capacity_id NVARCHAR(50),
        display_name NVARCHAR(500),
        sku NVARCHAR(50),
        region NVARCHAR(50),
        state NVARCHAR(30),
        usage_pct DECIMAL(5,2),
        admin_count INT,
        created_at DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_powerbi_capacities PRIMARY KEY (id),
        CONSTRAINT FK_powerbi_cap_scan FOREIGN KEY (scan_id) REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE
    );
    CREATE INDEX ix_powerbi_cap_scan ON cloud_assessment_powerbi_capacities(scan_id);
END;

-- 5. Per-scan activity summary (aggregated from activity events)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cloud_assessment_powerbi_activity_summary')
BEGIN
    CREATE TABLE cloud_assessment_powerbi_activity_summary (
        id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        scan_id UNIQUEIDENTIFIER NOT NULL,
        activities_total INT,
        unique_users INT,
        view_report_count INT,
        edit_report_count INT,
        create_dataset_count INT,
        delete_count INT,
        share_external_count INT,
        export_count INT,
        period_days INT,
        created_at DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_powerbi_activity PRIMARY KEY (id),
        CONSTRAINT FK_powerbi_act_scan FOREIGN KEY (scan_id) REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE
    );
    CREATE INDEX ix_powerbi_act_scan ON cloud_assessment_powerbi_activity_summary(scan_id);
END;
