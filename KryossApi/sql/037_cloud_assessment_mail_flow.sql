-- 037_cloud_assessment_mail_flow.sql
-- CA-10: Mail Flow & Email Security — three scan-scoped tables.

IF OBJECT_ID('cloud_assessment_mail_domains', 'U') IS NULL
CREATE TABLE cloud_assessment_mail_domains (
    id                     UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    scan_id                UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
    domain                 NVARCHAR(255) NOT NULL,
    is_default             BIT NOT NULL DEFAULT 0,
    is_verified            BIT NOT NULL DEFAULT 0,
    spf_record             NVARCHAR(MAX),
    spf_valid              BIT,
    spf_mechanism          NVARCHAR(20),   -- -all | ~all | ?all | +all | missing
    spf_lookup_count       INT,
    spf_warnings           NVARCHAR(MAX),  -- JSON array of strings
    dkim_s1_present        BIT,
    dkim_s2_present        BIT,
    dkim_selectors         NVARCHAR(MAX),  -- JSON array of detected selectors
    dmarc_record           NVARCHAR(MAX),
    dmarc_valid            BIT,
    dmarc_policy           NVARCHAR(20),   -- none | quarantine | reject
    dmarc_subdomain_policy NVARCHAR(20),
    dmarc_pct              INT,
    dmarc_rua              NVARCHAR(500),
    dmarc_ruf              NVARCHAR(500),
    mta_sts_record         NVARCHAR(MAX),
    mta_sts_policy         NVARCHAR(20),   -- enforce | testing | none | missing
    bimi_present           BIT,
    score                  DECIMAL(3,1),
    created_at             DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE INDEX ix_mail_domains_scan ON cloud_assessment_mail_domains(scan_id);
GO

CREATE UNIQUE INDEX ux_mail_domains_scan_domain ON cloud_assessment_mail_domains(scan_id, domain);
GO

IF OBJECT_ID('cloud_assessment_mailbox_risks', 'U') IS NULL
CREATE TABLE cloud_assessment_mailbox_risks (
    id                   UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    scan_id              UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
    user_principal_name  NVARCHAR(500) NOT NULL,
    display_name         NVARCHAR(500),
    risk_type            NVARCHAR(50) NOT NULL,   -- forwarding_external | auto_forwarding_enabled | shared_mailbox_password | orphaned_shared_mailbox
    risk_detail          NVARCHAR(MAX),
    forward_target       NVARCHAR(500),
    severity             NVARCHAR(20),             -- critical | high | medium | low
    created_at           DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE INDEX ix_mailbox_risks_scan ON cloud_assessment_mailbox_risks(scan_id);
GO

CREATE UNIQUE INDEX ux_mailbox_risks_scan_upn_type ON cloud_assessment_mailbox_risks(scan_id, user_principal_name, risk_type);
GO

IF OBJECT_ID('cloud_assessment_shared_mailboxes', 'U') IS NULL
CREATE TABLE cloud_assessment_shared_mailboxes (
    id                   UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    scan_id              UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
    mailbox_upn          NVARCHAR(500) NOT NULL,
    display_name         NVARCHAR(500),
    delegates_count      INT,
    full_access_users    NVARCHAR(MAX),   -- JSON array
    send_as_users        NVARCHAR(MAX),   -- JSON array
    has_password_enabled BIT,
    last_activity        DATETIME2(2),
    created_at           DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE INDEX ix_shared_mailboxes_scan ON cloud_assessment_shared_mailboxes(scan_id);
GO

CREATE UNIQUE INDEX ux_shared_mailboxes_scan_upn ON cloud_assessment_shared_mailboxes(scan_id, mailbox_upn);
GO
