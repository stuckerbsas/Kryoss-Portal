SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 009_rls.sql
-- Kryoss Platform — Row-Level Security Policies
-- Multi-tenant isolation: each user only sees their org's data
-- Depends on: All previous migrations
--
-- NOTE: Azure SQL RLS uses security predicates with functions.
-- The current user's organization_id is passed via SESSION_CONTEXT
-- set by the API middleware on each request:
--   EXEC sp_set_session_context @key = N'organization_id', @value = @orgId;
--   EXEC sp_set_session_context @key = N'franchise_id', @value = @franchiseId;
--   EXEC sp_set_session_context @key = N'is_admin', @value = @isAdmin;
-- =============================================

-- =============================================
-- SCHEMA for security functions
-- =============================================
CREATE SCHEMA rls;
GO

-- =============================================
-- FILTER FUNCTION: Organization-level isolation
-- Returns 1 if the row belongs to the current user's org or if user is admin
-- =============================================
CREATE FUNCTION rls.fn_org_filter(@organization_id UNIQUEIDENTIFIER)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS result
    WHERE
        -- Admins (super_admin) see everything
        CAST(SESSION_CONTEXT(N'is_admin') AS BIT) = 1
        -- Franchise users see all orgs in their franchise
        OR @organization_id IN (
            SELECT id FROM dbo.organizations
            WHERE franchise_id = CAST(SESSION_CONTEXT(N'franchise_id') AS UNIQUEIDENTIFIER)
              AND deleted_at IS NULL
        )
        -- Client users see only their own org
        OR @organization_id = CAST(SESSION_CONTEXT(N'organization_id') AS UNIQUEIDENTIFIER);
GO

-- =============================================
-- FILTER FUNCTION: Franchise-level isolation
-- For tables that reference franchise_id directly (e.g., franchises itself)
-- =============================================
CREATE FUNCTION rls.fn_franchise_filter(@franchise_id UNIQUEIDENTIFIER)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS result
    WHERE
        CAST(SESSION_CONTEXT(N'is_admin') AS BIT) = 1
        OR @franchise_id = CAST(SESSION_CONTEXT(N'franchise_id') AS UNIQUEIDENTIFIER);
GO

-- =============================================
-- APPLY RLS POLICIES to organization-scoped tables
-- =============================================

-- Machines
ALTER TABLE machines ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_machines
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.machines,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.machines
    WITH (STATE = ON);
GO

-- Network Devices
ALTER TABLE network_devices ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_network_devices
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.network_devices,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.network_devices
    WITH (STATE = ON);
GO

-- Assessment Runs
ALTER TABLE assessment_runs ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_assessment_runs
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.assessment_runs,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.assessment_runs
    WITH (STATE = ON);
GO

-- Latest Machine Assessment
ALTER TABLE latest_machine_assessment ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_latest_machine_assessment
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.latest_machine_assessment,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.latest_machine_assessment
    WITH (STATE = ON);
GO

-- Org Compliance Monthly
ALTER TABLE org_compliance_monthly ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_org_compliance_monthly
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.org_compliance_monthly,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.org_compliance_monthly
    WITH (STATE = ON);
GO

-- Org Fleet Summary
ALTER TABLE org_fleet_summary ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_org_fleet_summary
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.org_fleet_summary,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.org_fleet_summary
    WITH (STATE = ON);
GO

-- Tags
ALTER TABLE tags ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_tags
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.tags,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.tags
    WITH (STATE = ON);
GO

-- Assessments (profiles)
ALTER TABLE assessments ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_assessments
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.assessments,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.assessments
    WITH (STATE = ON);
GO

-- Enrollment Codes
ALTER TABLE enrollment_codes ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_enrollment_codes
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.enrollment_codes,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.enrollment_codes
    WITH (STATE = ON);
GO

-- Org Crypto Keys
ALTER TABLE org_crypto_keys ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_org_crypto_keys
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.org_crypto_keys,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.org_crypto_keys
    WITH (STATE = ON);
GO

-- Organizations (filtered by franchise)
ALTER TABLE organizations ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_organizations
    ADD FILTER PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.organizations,
    ADD BLOCK PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.organizations
    WITH (STATE = ON);
GO

-- Control Failure Summary
ALTER TABLE control_failure_summary ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_control_failure_summary
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.control_failure_summary,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.control_failure_summary
    WITH (STATE = ON);
GO

-- =============================================
-- CRM TABLES (from 010_crm.sql)
-- =============================================

-- Contact Organizations (org-scoped)
ALTER TABLE contact_organizations ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_contact_organizations
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.contact_organizations,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.contact_organizations
    WITH (STATE = ON);
GO

-- Deals (franchise-scoped)
ALTER TABLE deals ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_deals
    ADD FILTER PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.deals,
    ADD BLOCK PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.deals
    WITH (STATE = ON);
GO

-- CRM Activities (franchise-scoped)
ALTER TABLE crm_activities ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_crm_activities
    ADD FILTER PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.crm_activities,
    ADD BLOCK PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.crm_activities
    WITH (STATE = ON);
GO

-- Service Catalog (franchise-scoped, NULL franchise_id = global)
-- Note: service_catalog has nullable franchise_id, global items visible to all
-- RLS not applied — filtered in application layer (global items + own franchise items)

-- =============================================
-- TICKET TABLES (from 011_tickets.sql)
-- =============================================

-- Ticket Queues (franchise-scoped)
ALTER TABLE ticket_queues ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_ticket_queues
    ADD FILTER PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.ticket_queues,
    ADD BLOCK PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.ticket_queues
    WITH (STATE = ON);
GO

-- Tickets (org-scoped — clients see their own, franchise sees all their clients')
ALTER TABLE tickets ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_tickets
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.tickets,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.tickets
    WITH (STATE = ON);
GO

-- SLA Policies (franchise-scoped)
ALTER TABLE sla_policies ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_sla_policies
    ADD FILTER PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.sla_policies,
    ADD BLOCK PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.sla_policies
    WITH (STATE = ON);
GO

-- =============================================
-- BILLING TABLES (from 012_billing.sql)
-- =============================================

-- Client Subscriptions (org-scoped)
ALTER TABLE client_subscriptions ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_client_subscriptions
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.client_subscriptions,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.client_subscriptions
    WITH (STATE = ON);
GO

-- Invoices (org-scoped)
ALTER TABLE invoices ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_invoices
    ADD FILTER PREDICATE rls.fn_org_filter(organization_id) ON dbo.invoices,
    ADD BLOCK PREDICATE rls.fn_org_filter(organization_id) ON dbo.invoices
    WITH (STATE = ON);
GO

-- Commission Periods (franchise-scoped)
ALTER TABLE commission_periods ENABLE ROW LEVEL SECURITY;
GO
CREATE SECURITY POLICY rls.pol_commission_periods
    ADD FILTER PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.commission_periods,
    ADD BLOCK PREDICATE rls.fn_franchise_filter(franchise_id) ON dbo.commission_periods
    WITH (STATE = ON);
GO
