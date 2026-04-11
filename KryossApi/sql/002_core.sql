SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 002_core.sql
-- Kryoss Platform — Core: Franchises, Organizations, Sites
-- Depends on: 001_foundation.sql (users table for FK)
-- =============================================

-- =============================================
-- FRANCHISES: Partner companies (e.g., TeamLogic IT franchisees)
-- =============================================
CREATE TABLE franchises (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    name            NVARCHAR(255)  NOT NULL,
    legal_name      NVARCHAR(255),
    tax_id          NVARCHAR(100),
    country         NVARCHAR(100),
    contact_email   NVARCHAR(255),
    contact_phone   NVARCHAR(50),
    status          VARCHAR(20)    NOT NULL DEFAULT 'active'
        CONSTRAINT ck_franchises_status CHECK (status IN ('active', 'suspended', 'inactive')),
    -- Branding (white-label for reports)
    brand_name           NVARCHAR(255),                -- name shown on reports
    brand_logo_url       NVARCHAR(500),                -- logo URL (blob storage)
    brand_color_primary  CHAR(7),                      -- #008852
    brand_color_accent   CHAR(7),                      -- #A2C564
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_franchises_active ON franchises(status) WHERE deleted_at IS NULL;

-- =============================================
-- ORGANIZATIONS: End clients managed by franchisees
-- =============================================
CREATE TABLE organizations (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    franchise_id    UNIQUEIDENTIFIER NOT NULL REFERENCES franchises(id),
    name            NVARCHAR(255)  NOT NULL,
    legal_name      NVARCHAR(255),
    tax_id          NVARCHAR(100),
    status          VARCHAR(20)    NOT NULL DEFAULT 'prospect'
        CONSTRAINT ck_orgs_status CHECK (status IN ('prospect', 'current', 'disabled')),
    -- Agent auth (per-org API key for agent communication)
    api_key         NVARCHAR(64)   UNIQUE,
    api_secret      NVARCHAR(128),                     -- HMAC signing secret
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_orgs_franchise ON organizations(franchise_id) WHERE deleted_at IS NULL;
CREATE INDEX ix_orgs_status    ON organizations(status)       WHERE deleted_at IS NULL;

-- =============================================
-- SITES: Physical locations within an organization
-- =============================================
CREATE TABLE sites (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    name            NVARCHAR(255)  NOT NULL,
    address         NVARCHAR(500),
    subnet          VARCHAR(43),                       -- CIDR notation e.g. '192.168.1.0/24'
    is_default      BIT            NOT NULL DEFAULT 0,
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_sites_org ON sites(organization_id) WHERE deleted_at IS NULL;

-- =============================================
-- ADD FK CONSTRAINTS on users table (deferred from 001)
-- =============================================
ALTER TABLE users
    ADD CONSTRAINT fk_users_franchise
    FOREIGN KEY (franchise_id) REFERENCES franchises(id);

ALTER TABLE users
    ADD CONSTRAINT fk_users_organization
    FOREIGN KEY (organization_id) REFERENCES organizations(id);

CREATE INDEX ix_users_franchise ON users(franchise_id) WHERE franchise_id IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX ix_users_org       ON users(organization_id) WHERE organization_id IS NOT NULL AND deleted_at IS NULL;
