-- 024_m365_tenants.sql
-- M365 / Entra ID tenant integration tables for Phase 4: Cloud Workspace Hardening

CREATE TABLE m365_tenants (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    tenant_id       NVARCHAR(100) NOT NULL,    -- Entra tenant GUID
    tenant_name     NVARCHAR(255),
    client_id       NVARCHAR(100),             -- App registration client ID
    client_secret   NVARCHAR(500),             -- Encrypted client secret
    status          VARCHAR(20) NOT NULL DEFAULT 'active', -- active, disconnected, expired
    last_scan_at    DATETIME2(2),
    created_at      DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_m365_org UNIQUE (organization_id)
);

CREATE TABLE m365_findings (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES m365_tenants(id) ON DELETE CASCADE,
    check_id        VARCHAR(30) NOT NULL,      -- M365-001, M365-002, etc.
    name            NVARCHAR(300) NOT NULL,
    category        VARCHAR(50) NOT NULL,       -- conditional_access, mfa, mail_security, admin_roles, guest_access, security_defaults
    severity        VARCHAR(20) NOT NULL,
    status          VARCHAR(10) NOT NULL,       -- pass, fail, warn, info
    finding         NVARCHAR(MAX),
    actual_value    NVARCHAR(500),
    scanned_at      DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_m365_findings_tenant ON m365_findings(tenant_id, scanned_at DESC);
