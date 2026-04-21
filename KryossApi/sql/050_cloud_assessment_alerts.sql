-- 050_cloud_assessment_alerts.sql
-- CA-15: Drift Alerts + Notifications
-- Tables for alert rules (per-franchise config) and sent alert history.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cloud_assessment_alert_rules')
BEGIN
    CREATE TABLE cloud_assessment_alert_rules (
        id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
        franchise_id    UNIQUEIDENTIFIER NOT NULL REFERENCES franchises(id),
        rule_type       NVARCHAR(60)     NOT NULL,  -- score_drop, new_critical, new_high_regulated, framework_below, copilot_drop
        threshold       DECIMAL(8,2)     NULL,       -- e.g. 0.5 for score drop, 70 for framework pct
        framework_code  NVARCHAR(20)     NULL,       -- only for framework_below rule type
        is_enabled      BIT              NOT NULL DEFAULT 1,
        delivery_channel NVARCHAR(20)    NOT NULL DEFAULT 'email', -- email, webhook, both
        target_email    NVARCHAR(256)    NULL,
        webhook_url     NVARCHAR(512)    NULL,
        created_at      DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at      DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_alert_rules_franchise ON cloud_assessment_alert_rules(franchise_id);
END;

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'cloud_assessment_alerts_sent')
BEGIN
    CREATE TABLE cloud_assessment_alerts_sent (
        id              BIGINT IDENTITY(1,1) PRIMARY KEY,
        scan_id         UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
        rule_id         UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_alert_rules(id),
        organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
        severity        NVARCHAR(20)     NOT NULL,  -- critical, high, medium, info
        rule_type       NVARCHAR(60)     NOT NULL,
        summary         NVARCHAR(500)    NOT NULL,
        payload_json    NVARCHAR(MAX)    NULL,
        delivery_status NVARCHAR(20)     NOT NULL DEFAULT 'pending', -- pending, sent, failed
        delivered_at    DATETIME2        NULL,
        error_message   NVARCHAR(500)    NULL,
        fired_at        DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_alerts_sent_scan ON cloud_assessment_alerts_sent(scan_id);
    CREATE INDEX IX_alerts_sent_org ON cloud_assessment_alerts_sent(organization_id);
END;

PRINT 'Migration 050: cloud_assessment_alerts applied';
