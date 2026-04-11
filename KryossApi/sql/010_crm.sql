SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 010_crm.sql
-- Kryoss Platform — CRM: Contacts, Pipeline, Deals, Activities
-- Depends on: 002_core.sql (franchises, organizations)
-- =============================================

-- =============================================
-- CONTACTS: People (M:N with organizations)
-- =============================================
CREATE TABLE contacts (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    first_name      NVARCHAR(100)  NOT NULL,
    last_name       NVARCHAR(100)  NOT NULL,
    email           NVARCHAR(255),
    phone           NVARCHAR(50),
    mobile          NVARCHAR(50),
    job_title       NVARCHAR(100),
    contact_type    VARCHAR(20)    NOT NULL DEFAULT 'general'
        CONSTRAINT ck_contact_type CHECK (contact_type IN ('decision_maker', 'technical', 'billing', 'general')),
    preferred_channel VARCHAR(20)  DEFAULT 'email'
        CONSTRAINT ck_contact_channel CHECK (preferred_channel IN ('email', 'phone', 'whatsapp', 'teams')),
    notes           NVARCHAR(MAX),
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_contacts_email ON contacts(email) WHERE deleted_at IS NULL;
CREATE INDEX ix_contacts_name  ON contacts(last_name, first_name) WHERE deleted_at IS NULL;

-- =============================================
-- CONTACT_ORGANIZATIONS: M:N contact <-> organization
-- A contact can work for multiple orgs (e.g., consultant)
-- =============================================
CREATE TABLE contact_organizations (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    contact_id      UNIQUEIDENTIFIER NOT NULL REFERENCES contacts(id),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    role_in_org     NVARCHAR(100),                     -- "IT Manager", "CEO", "External Consultant"
    is_primary      BIT            NOT NULL DEFAULT 0, -- primary contact for this org
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2),
    CONSTRAINT uq_contact_org UNIQUE (contact_id, organization_id)
);

CREATE INDEX ix_contactorg_org     ON contact_organizations(organization_id) WHERE deleted_at IS NULL;
CREATE INDEX ix_contactorg_contact ON contact_organizations(contact_id) WHERE deleted_at IS NULL;

-- =============================================
-- PIPELINE_STAGES: Fixed stages for all franchises
-- =============================================
CREATE TABLE pipeline_stages (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    code            VARCHAR(30)    NOT NULL UNIQUE,
    name            NVARCHAR(100)  NOT NULL,
    sort_order      SMALLINT       NOT NULL,
    probability     SMALLINT       NOT NULL DEFAULT 0, -- 0-100% default probability
    is_closed       BIT            NOT NULL DEFAULT 0, -- Won/Lost are closed stages
    is_won          BIT            NOT NULL DEFAULT 0, -- Won = revenue realized
    color           CHAR(7)                            -- hex color for UI
);

-- =============================================
-- CURRENCIES: Supported currencies
-- =============================================
CREATE TABLE currencies (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    code            CHAR(3)        NOT NULL UNIQUE,    -- USD, ARS, EUR
    name            NVARCHAR(50)   NOT NULL,
    symbol          NVARCHAR(5)    NOT NULL             -- $, AR$, €
);

-- =============================================
-- SERVICE_CATALOG: Products/services that can be quoted in deals
-- Global catalog managed by admin + custom items per franchise
-- =============================================
CREATE TABLE service_catalog (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    franchise_id    UNIQUEIDENTIFIER REFERENCES franchises(id), -- NULL = global catalog
    code            VARCHAR(50)    NOT NULL,
    name            NVARCHAR(255)  NOT NULL,
    description     NVARCHAR(MAX),
    category        VARCHAR(30)    NOT NULL DEFAULT 'service'
        CONSTRAINT ck_svccat_category CHECK (category IN ('service', 'product', 'subscription', 'assessment', 'project')),
    unit            VARCHAR(20)    NOT NULL DEFAULT 'unit'
        CONSTRAINT ck_svccat_unit CHECK (unit IN ('unit', 'hour', 'month', 'year', 'device', 'user', 'license')),
    base_price      DECIMAL(12,2),                     -- suggested price (can be overridden in deal)
    currency_id     INT            REFERENCES currencies(id),
    is_recurring    BIT            NOT NULL DEFAULT 0, -- monthly/annual recurring
    is_active       BIT            NOT NULL DEFAULT 1,
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_svccat_franchise ON service_catalog(franchise_id) WHERE is_active = 1 AND deleted_at IS NULL;

-- =============================================
-- DEALS: Sales opportunities / quotes
-- =============================================
CREATE TABLE deals (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    franchise_id    UNIQUEIDENTIFIER NOT NULL REFERENCES franchises(id),
    organization_id UNIQUEIDENTIFIER REFERENCES organizations(id), -- NULL if new prospect not yet in system
    contact_id      UNIQUEIDENTIFIER REFERENCES contacts(id),      -- primary contact for this deal
    stage_id        INT            NOT NULL REFERENCES pipeline_stages(id),
    assigned_to     UNIQUEIDENTIFIER REFERENCES users(id),         -- salesperson
    -- Deal info
    title           NVARCHAR(255)  NOT NULL,
    description     NVARCHAR(MAX),
    total_value     DECIMAL(12,2)  NOT NULL DEFAULT 0,
    monthly_recurring DECIMAL(12,2) NOT NULL DEFAULT 0,            -- MRR portion
    currency_id     INT            NOT NULL REFERENCES currencies(id),
    probability     SMALLINT,                                       -- override stage default
    expected_close  DATE,
    actual_close    DATE,
    lost_reason     NVARCHAR(500),
    -- Source
    source          VARCHAR(30)
        CONSTRAINT ck_deal_source CHECK (source IN ('assessment', 'referral', 'cold_call', 'website', 'event', 'partner', 'other')),
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_deals_franchise ON deals(franchise_id) WHERE deleted_at IS NULL;
CREATE INDEX ix_deals_org       ON deals(organization_id) WHERE organization_id IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX ix_deals_stage     ON deals(stage_id) WHERE deleted_at IS NULL;
CREATE INDEX ix_deals_assigned  ON deals(assigned_to) WHERE deleted_at IS NULL;

-- =============================================
-- DEAL_ITEMS: Line items in a deal (from catalog + custom)
-- =============================================
CREATE TABLE deal_items (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    deal_id         UNIQUEIDENTIFIER NOT NULL REFERENCES deals(id),
    service_id      INT            REFERENCES service_catalog(id), -- NULL for custom items
    -- If custom (service_id IS NULL):
    custom_name     NVARCHAR(255),
    custom_description NVARCHAR(MAX),
    -- Pricing
    quantity        DECIMAL(10,2)  NOT NULL DEFAULT 1,
    unit_price      DECIMAL(12,2)  NOT NULL,
    discount_pct    DECIMAL(5,2)   NOT NULL DEFAULT 0,
    line_total      AS (quantity * unit_price * (1 - discount_pct / 100)) PERSISTED,
    is_recurring    BIT            NOT NULL DEFAULT 0,
    sort_order      SMALLINT       NOT NULL DEFAULT 0
);

CREATE INDEX ix_dealitems_deal ON deal_items(deal_id);

-- =============================================
-- CRM_ACTIVITIES: Interactions with contacts/orgs
-- =============================================
CREATE TABLE crm_activities (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    franchise_id    UNIQUEIDENTIFIER NOT NULL REFERENCES franchises(id),
    organization_id UNIQUEIDENTIFIER REFERENCES organizations(id),
    contact_id      UNIQUEIDENTIFIER REFERENCES contacts(id),
    deal_id         UNIQUEIDENTIFIER REFERENCES deals(id),
    -- Activity
    activity_type   VARCHAR(20)    NOT NULL
        CONSTRAINT ck_activity_type CHECK (activity_type IN ('call', 'email', 'meeting', 'note', 'task', 'whatsapp')),
    subject         NVARCHAR(255)  NOT NULL,
    body            NVARCHAR(MAX),
    activity_date   DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    duration_min    SMALLINT,                          -- duration in minutes
    -- Follow-up
    followup_date   DATE,
    followup_done   BIT            NOT NULL DEFAULT 0,
    -- Assignment
    assigned_to     UNIQUEIDENTIFIER REFERENCES users(id),
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_activities_org      ON crm_activities(organization_id, activity_date DESC) WHERE deleted_at IS NULL;
CREATE INDEX ix_activities_contact  ON crm_activities(contact_id, activity_date DESC) WHERE deleted_at IS NULL;
CREATE INDEX ix_activities_deal     ON crm_activities(deal_id) WHERE deal_id IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX ix_activities_followup ON crm_activities(followup_date) WHERE followup_done = 0 AND deleted_at IS NULL;
