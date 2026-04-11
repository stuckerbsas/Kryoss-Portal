SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- seed_003_crm_tickets.sql
-- Kryoss Platform — Seed: Pipeline Stages, Currencies,
--                         Ticket Categories, SLA defaults
-- Run AFTER 010_crm.sql, 011_tickets.sql
-- =============================================

-- =============================================
-- CURRENCIES
-- =============================================
INSERT INTO currencies (code, name, symbol) VALUES
    ('USD', N'US Dollar',          N'$'),
    ('ARS', N'Argentine Peso',     N'AR$'),
    ('EUR', N'Euro',               N'€'),
    ('BRL', N'Brazilian Real',     N'R$'),
    ('CLP', N'Chilean Peso',       N'CL$'),
    ('MXN', N'Mexican Peso',       N'MX$'),
    ('COP', N'Colombian Peso',     N'CO$'),
    ('GBP', N'British Pound',      N'£');

-- =============================================
-- PIPELINE STAGES (fixed for all franchises)
-- =============================================
INSERT INTO pipeline_stages (code, name, sort_order, probability, is_closed, is_won, color) VALUES
    ('lead',         N'Lead',          1,   10, 0, 0, '#6B7280'),  -- gray
    ('qualified',    N'Qualified',     2,   25, 0, 0, '#3B82F6'),  -- blue
    ('proposal',     N'Proposal',      3,   50, 0, 0, '#8B5CF6'),  -- purple
    ('negotiation',  N'Negotiation',   4,   75, 0, 0, '#F59E0B'),  -- amber
    ('won',          N'Won',           5,  100, 1, 1, '#10B981'),  -- green
    ('lost',         N'Lost',          6,    0, 1, 0, '#EF4444');  -- red

-- =============================================
-- TICKET CATEGORIES
-- =============================================
INSERT INTO ticket_categories (name, sort_order, is_active) VALUES
    (N'Hardware',       1, 1),
    (N'Software',       2, 1),
    (N'Network',        3, 1),
    (N'Email / M365',   4, 1),
    (N'Account Access', 5, 1),
    (N'Security',       6, 1),
    (N'Backup',         7, 1),
    (N'Printer',        8, 1),
    (N'VPN / Remote',   9, 1),
    (N'Other',          10, 1);

-- =============================================
-- UPDATE MODULES: Add new modules for CRM, tickets, billing
-- (some already exist from seed_001, just add missing ones)
-- =============================================
-- Check if 'contacts' module exists, if not insert
IF NOT EXISTS (SELECT 1 FROM modules WHERE code = 'contacts')
    INSERT INTO modules (code, name, sort_order) VALUES ('contacts', N'Contacts', 14);

IF NOT EXISTS (SELECT 1 FROM modules WHERE code = 'deals')
    INSERT INTO modules (code, name, sort_order) VALUES ('deals', N'Deals & Pipeline', 15);

IF NOT EXISTS (SELECT 1 FROM modules WHERE code = 'billing')
    INSERT INTO modules (code, name, sort_order) VALUES ('billing', N'Billing & Invoices', 16);

IF NOT EXISTS (SELECT 1 FROM modules WHERE code = 'subscriptions')
    INSERT INTO modules (code, name, sort_order) VALUES ('subscriptions', N'Client Subscriptions', 17);

IF NOT EXISTS (SELECT 1 FROM modules WHERE code = 'commissions')
    INSERT INTO modules (code, name, sort_order) VALUES ('commissions', N'Commissions', 18);

-- =============================================
-- GENERATE PERMISSIONS for new modules
-- =============================================
INSERT INTO permissions (module_id, action_id, slug, description)
SELECT m.id, a.id, m.code + ':' + a.code, m.name + N' — ' + a.name
FROM modules m
CROSS JOIN actions a
WHERE m.code IN ('contacts', 'deals', 'billing', 'subscriptions', 'commissions')
  AND NOT EXISTS (
      SELECT 1 FROM permissions p WHERE p.module_id = m.id AND p.action_id = a.id
  );

-- =============================================
-- GRANT new permissions to super_admin
-- =============================================
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
WHERE r.code = 'super_admin'
  AND NOT EXISTS (
      SELECT 1 FROM role_permissions rp WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );

-- =============================================
-- GRANT CRM permissions to franchise_owner
-- =============================================
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
JOIN modules m ON p.module_id = m.id
JOIN actions a ON p.action_id = a.id
WHERE r.code = 'franchise_owner'
  AND m.code IN ('contacts', 'deals', 'billing', 'subscriptions', 'commissions')
  AND NOT EXISTS (
      SELECT 1 FROM role_permissions rp WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );

-- =============================================
-- GRANT ticket/contact read to franchise_tech
-- =============================================
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
JOIN modules m ON p.module_id = m.id
JOIN actions a ON p.action_id = a.id
WHERE r.code = 'franchise_tech'
  AND m.code IN ('contacts', 'deals')
  AND a.code IN ('read', 'create', 'edit')
  AND NOT EXISTS (
      SELECT 1 FROM role_permissions rp WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );
