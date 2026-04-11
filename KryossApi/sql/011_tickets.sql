SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 011_tickets.sql
-- Kryoss Platform — Helpdesk: Tickets, SLA, Comments, Time Entries
-- Channels: Portal + Email + API (NinjaRMM alerts)
-- ITIL types + categories, auto-assign round-robin, SLA pausable
-- Depends on: 002_core.sql (franchises, organizations), 001_foundation.sql (users)
-- =============================================

-- =============================================
-- TICKET_QUEUES: Assignment queues (L1 Support, Network, etc.)
-- Round-robin auto-assign within each queue
-- =============================================
CREATE TABLE ticket_queues (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    franchise_id    UNIQUEIDENTIFIER NOT NULL REFERENCES franchises(id),
    name            NVARCHAR(100)  NOT NULL,            -- "L1 Support", "Networking", "Escalations"
    description     NVARCHAR(255),
    is_default      BIT            NOT NULL DEFAULT 0,  -- default queue for new tickets
    is_active       BIT            NOT NULL DEFAULT 1,
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_queues_franchise ON ticket_queues(franchise_id) WHERE is_active = 1 AND deleted_at IS NULL;

-- =============================================
-- QUEUE_MEMBERS: Technicians assigned to each queue
-- Used for round-robin auto-assignment
-- =============================================
CREATE TABLE queue_members (
    queue_id        INT            NOT NULL REFERENCES ticket_queues(id),
    user_id         UNIQUEIDENTIFIER NOT NULL REFERENCES users(id),
    last_assigned_at DATETIME2(2),                     -- for round-robin tracking
    is_active       BIT            NOT NULL DEFAULT 1,
    CONSTRAINT pk_queue_members PRIMARY KEY (queue_id, user_id)
);

-- =============================================
-- TICKET_CATEGORIES: What the ticket is about
-- =============================================
CREATE TABLE ticket_categories (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    name            NVARCHAR(100)  NOT NULL UNIQUE,     -- Hardware, Software, Network, Account, Email, Security, Other
    sort_order      SMALLINT       NOT NULL DEFAULT 0,
    is_active       BIT            NOT NULL DEFAULT 1
);

-- =============================================
-- SLA_POLICIES: SLA definitions per franchise (overrideable per org)
-- =============================================
CREATE TABLE sla_policies (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    franchise_id    UNIQUEIDENTIFIER NOT NULL REFERENCES franchises(id),
    organization_id UNIQUEIDENTIFIER REFERENCES organizations(id), -- NULL = franchise default
    name            NVARCHAR(100)  NOT NULL,
    priority        VARCHAR(10)    NOT NULL
        CONSTRAINT ck_sla_priority CHECK (priority IN ('critical', 'high', 'medium', 'low')),
    -- Response time (first reply)
    response_hours  SMALLINT       NOT NULL,            -- max hours to first response
    -- Resolution time
    resolution_hours SMALLINT      NOT NULL,            -- max hours to resolution
    -- Business hours or 24/7
    is_24x7         BIT            NOT NULL DEFAULT 0,  -- if 0, only count business hours
    -- SLA pause behavior
    pause_on_waiting BIT           NOT NULL DEFAULT 1,  -- pause SLA when waiting on customer
    is_active       BIT            NOT NULL DEFAULT 1,
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_sla_franchise ON sla_policies(franchise_id) WHERE is_active = 1 AND deleted_at IS NULL;
-- Unique per priority per scope (franchise default or org override)
CREATE UNIQUE INDEX ux_sla_scope ON sla_policies(franchise_id, ISNULL(organization_id, '00000000-0000-0000-0000-000000000000'), priority)
    WHERE is_active = 1 AND deleted_at IS NULL;

-- =============================================
-- TICKETS: Core ticket table
-- =============================================
CREATE TABLE tickets (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    franchise_id    UNIQUEIDENTIFIER NOT NULL REFERENCES franchises(id),
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    -- Ticket identity
    ticket_number   INT            NOT NULL,            -- sequential per franchise (TK-0001)
    -- Classification
    ticket_type     VARCHAR(20)    NOT NULL DEFAULT 'incident'
        CONSTRAINT ck_ticket_type CHECK (ticket_type IN ('incident', 'service_request', 'change', 'problem')),
    category_id     INT            REFERENCES ticket_categories(id),
    priority        VARCHAR(10)    NOT NULL DEFAULT 'medium'
        CONSTRAINT ck_ticket_priority CHECK (priority IN ('critical', 'high', 'medium', 'low')),
    -- Status
    status          VARCHAR(20)    NOT NULL DEFAULT 'new'
        CONSTRAINT ck_ticket_status CHECK (status IN (
            'new', 'open', 'in_progress', 'waiting_customer', 'waiting_vendor',
            'on_hold', 'resolved', 'closed', 'cancelled'
        )),
    -- Content
    subject         NVARCHAR(255)  NOT NULL,
    description     NVARCHAR(MAX)  NOT NULL,
    -- Source
    source          VARCHAR(20)    NOT NULL DEFAULT 'portal'
        CONSTRAINT ck_ticket_source CHECK (source IN ('portal', 'email', 'api', 'phone', 'chat')),
    source_ref      NVARCHAR(255),                     -- email message-id, NinjaRMM alert ID, etc.
    -- Assignment
    queue_id        INT            REFERENCES ticket_queues(id),
    assigned_to     UNIQUEIDENTIFIER REFERENCES users(id),
    -- Requester
    requester_id    UNIQUEIDENTIFIER REFERENCES users(id),     -- portal user who created it
    contact_id      UNIQUEIDENTIFIER REFERENCES contacts(id),  -- contact (if different from portal user)
    -- Machine reference (if related to a specific machine)
    machine_id      UNIQUEIDENTIFIER REFERENCES machines(id),
    -- SLA tracking
    sla_policy_id   INT            REFERENCES sla_policies(id),
    sla_response_due DATETIME2(2),                     -- when first response is due
    sla_resolution_due DATETIME2(2),                   -- when resolution is due
    sla_responded_at DATETIME2(2),                     -- when first response was made
    sla_resolved_at DATETIME2(2),                      -- when resolved
    sla_paused_at   DATETIME2(2),                      -- when SLA timer was paused
    sla_paused_total_min INT       NOT NULL DEFAULT 0, -- total minutes paused
    sla_response_breached BIT      NOT NULL DEFAULT 0,
    sla_resolution_breached BIT    NOT NULL DEFAULT 0,
    -- Resolution
    resolution_notes NVARCHAR(MAX),
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE UNIQUE INDEX ux_ticket_number ON tickets(franchise_id, ticket_number);
CREATE INDEX ix_tickets_org      ON tickets(organization_id) WHERE deleted_at IS NULL;
CREATE INDEX ix_tickets_assigned ON tickets(assigned_to) WHERE status NOT IN ('closed', 'cancelled') AND deleted_at IS NULL;
CREATE INDEX ix_tickets_status   ON tickets(franchise_id, status) WHERE deleted_at IS NULL;
CREATE INDEX ix_tickets_sla_resp ON tickets(sla_response_due) WHERE sla_response_breached = 0 AND status NOT IN ('closed', 'cancelled', 'resolved');
CREATE INDEX ix_tickets_sla_res  ON tickets(sla_resolution_due) WHERE sla_resolution_breached = 0 AND status NOT IN ('closed', 'cancelled', 'resolved');
CREATE INDEX ix_tickets_machine  ON tickets(machine_id) WHERE machine_id IS NOT NULL AND deleted_at IS NULL;

-- =============================================
-- TICKET_COMMENTS: Public (client sees) + Internal (tech only)
-- =============================================
CREATE TABLE ticket_comments (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ticket_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tickets(id),
    author_id       UNIQUEIDENTIFIER NOT NULL REFERENCES users(id),
    body            NVARCHAR(MAX)  NOT NULL,
    is_internal     BIT            NOT NULL DEFAULT 0, -- 0 = public (client visible), 1 = internal
    is_first_response BIT          NOT NULL DEFAULT 0, -- marks the SLA first response
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_comments_ticket ON ticket_comments(ticket_id, created_at) WHERE deleted_at IS NULL;

-- =============================================
-- TICKET_ATTACHMENTS: Files stored in Azure Blob Storage
-- =============================================
CREATE TABLE ticket_attachments (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ticket_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tickets(id),
    comment_id      UNIQUEIDENTIFIER REFERENCES ticket_comments(id), -- NULL if attached to ticket itself
    file_name       NVARCHAR(255)  NOT NULL,
    content_type    VARCHAR(100)   NOT NULL,            -- image/png, application/pdf
    file_size_bytes BIGINT         NOT NULL,
    blob_url        NVARCHAR(500)  NOT NULL,            -- Azure Blob Storage URL
    blob_container  VARCHAR(100)   NOT NULL DEFAULT 'ticket-attachments',
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_attachments_ticket ON ticket_attachments(ticket_id) WHERE deleted_at IS NULL;

-- =============================================
-- TICKET_TIME_ENTRIES: Hours worked per ticket
-- For billing and productivity reports
-- =============================================
CREATE TABLE ticket_time_entries (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ticket_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tickets(id),
    user_id         UNIQUEIDENTIFIER NOT NULL REFERENCES users(id),
    work_date       DATE           NOT NULL,
    duration_min    SMALLINT       NOT NULL,            -- duration in minutes
    description     NVARCHAR(500)  NOT NULL,
    is_billable     BIT            NOT NULL DEFAULT 1,
    billed          BIT            NOT NULL DEFAULT 0,  -- already invoiced?
    -- Audit columns
    created_by      UNIQUEIDENTIFIER NOT NULL,
    created_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    modified_by     UNIQUEIDENTIFIER,
    modified_at     DATETIME2(2),
    deleted_by      UNIQUEIDENTIFIER,
    deleted_at      DATETIME2(2)
);

CREATE INDEX ix_timeentries_ticket ON ticket_time_entries(ticket_id) WHERE deleted_at IS NULL;
CREATE INDEX ix_timeentries_user   ON ticket_time_entries(user_id, work_date) WHERE deleted_at IS NULL;
CREATE INDEX ix_timeentries_unbilled ON ticket_time_entries(ticket_id) WHERE is_billable = 1 AND billed = 0 AND deleted_at IS NULL;

-- =============================================
-- TICKET_STATUS_LOG: Track status transitions + SLA pause/resume
-- =============================================
CREATE TABLE ticket_status_log (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    ticket_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tickets(id),
    from_status     VARCHAR(20),
    to_status       VARCHAR(20)    NOT NULL,
    changed_by      UNIQUEIDENTIFIER NOT NULL REFERENCES users(id),
    changed_at      DATETIME2(2)   NOT NULL DEFAULT SYSUTCDATETIME(),
    sla_paused      BIT            NOT NULL DEFAULT 0, -- did this transition pause the SLA?
    sla_resumed     BIT            NOT NULL DEFAULT 0, -- did this transition resume the SLA?
    notes           NVARCHAR(255)
);

CREATE INDEX ix_statuslog_ticket ON ticket_status_log(ticket_id, changed_at);
