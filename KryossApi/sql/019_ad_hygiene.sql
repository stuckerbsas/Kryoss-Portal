SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- 019_ad_hygiene.sql
-- AD Hygiene findings: stale/dormant machines and users
-- Captured during network scans and displayed in the portal.
-- ============================================================

CREATE TABLE ad_hygiene_scans (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    scanned_by      NVARCHAR(255)    NOT NULL,  -- hostname of the scanning machine
    scanned_at      DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME(),
    total_machines   INT NOT NULL DEFAULT 0,
    total_users      INT NOT NULL DEFAULT 0,
    stale_machines   INT NOT NULL DEFAULT 0,
    dormant_machines INT NOT NULL DEFAULT 0,
    stale_users      INT NOT NULL DEFAULT 0,
    dormant_users    INT NOT NULL DEFAULT 0,
    disabled_users   INT NOT NULL DEFAULT 0,
    pwd_never_expire INT NOT NULL DEFAULT 0
);

CREATE INDEX ix_hygiene_scans_org ON ad_hygiene_scans(organization_id, scanned_at DESC);

CREATE TABLE ad_hygiene_findings (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id         UNIQUEIDENTIFIER NOT NULL REFERENCES ad_hygiene_scans(id) ON DELETE CASCADE,
    name            NVARCHAR(255)    NOT NULL,  -- sAMAccountName or computer name
    object_type     VARCHAR(20)      NOT NULL,  -- 'Computer' or 'User'
    status          VARCHAR(30)      NOT NULL,  -- 'Stale', 'Dormant', 'Disabled', 'PwdNeverExpires', 'OldPassword'
    days_inactive   INT              NOT NULL DEFAULT 0,
    detail          NVARCHAR(500)    NULL
);

CREATE INDEX ix_hygiene_findings_scan ON ad_hygiene_findings(scan_id);
CREATE INDEX ix_hygiene_findings_status ON ad_hygiene_findings(object_type, status);
GO

-- Verify
SELECT 'ad_hygiene_scans' AS tbl, COUNT(*) AS cnt FROM ad_hygiene_scans
UNION ALL SELECT 'ad_hygiene_findings', COUNT(*) FROM ad_hygiene_findings;
