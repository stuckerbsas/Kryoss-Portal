SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- seed_001_roles_permissions.sql
-- Kryoss Platform — Seed: Modules, Actions, Permissions, Roles
-- Run AFTER 001_foundation.sql
-- =============================================

-- System user ID for seed operations
DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- =============================================
-- MODULES
-- =============================================
INSERT INTO modules (code, name, sort_order) VALUES
    ('assessment',     N'Security Assessment',     1),
    ('crm_franchise',  N'CRM Franchises',          2),
    ('crm_client',     N'CRM Clients',             3),
    ('tickets',        N'Helpdesk Tickets',         4),
    ('invoices',       N'Billing & Invoices',       5),
    ('reports',        N'Reports',                  6),
    ('controls',       N'Control Definitions',      7),
    ('machines',       N'Machine Inventory',        8),
    ('network',        N'Network Devices',          9),
    ('vulnerabilities', N'Vulnerabilities',         10),
    ('enrollment',     N'Agent Enrollment',         11),
    ('admin',          N'Administration',           12),
    ('audit',          N'Audit Log',                13);

-- =============================================
-- ACTIONS
-- =============================================
INSERT INTO actions (code, name) VALUES
    ('read',   N'Read / View'),
    ('create', N'Create'),
    ('edit',   N'Edit / Update'),
    ('delete', N'Delete'),
    ('export', N'Export / Download');

-- =============================================
-- PERMISSIONS: Module x Action = slug
-- Generate all combinations
-- =============================================
INSERT INTO permissions (module_id, action_id, slug, description)
SELECT
    m.id,
    a.id,
    m.code + ':' + a.code,
    m.name + N' — ' + a.name
FROM modules m
CROSS JOIN actions a;

-- =============================================
-- ROLES
-- =============================================
INSERT INTO roles (code, name, is_system, created_by) VALUES
    ('super_admin',     N'Super Administrator',   1, @systemUserId),
    ('franchise_owner', N'Franchise Owner',        1, @systemUserId),
    ('franchise_tech',  N'Franchise Technician',   1, @systemUserId),
    ('client_admin',    N'Client Administrator',   1, @systemUserId),
    ('client_viewer',   N'Client Viewer',          1, @systemUserId);

-- =============================================
-- ROLE_PERMISSIONS
-- =============================================

-- super_admin: ALL permissions
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
WHERE r.code = 'super_admin';

-- franchise_owner: Everything except admin and audit:delete
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
JOIN modules m ON p.module_id = m.id
JOIN actions a ON p.action_id = a.id
WHERE r.code = 'franchise_owner'
  AND m.code NOT IN ('admin')
  AND NOT (m.code = 'audit' AND a.code = 'delete');

-- franchise_tech: Operational modules (assessment, machines, network, tickets, enrollment, reports)
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
JOIN modules m ON p.module_id = m.id
JOIN actions a ON p.action_id = a.id
WHERE r.code = 'franchise_tech'
  AND m.code IN ('assessment', 'machines', 'network', 'vulnerabilities', 'tickets', 'enrollment', 'reports')
  AND a.code IN ('read', 'create', 'edit', 'export');

-- client_admin: Read/export on their org's data + tickets CRUD
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
JOIN modules m ON p.module_id = m.id
JOIN actions a ON p.action_id = a.id
WHERE r.code = 'client_admin'
  AND (
    -- Read/export on most modules
    (m.code IN ('assessment', 'machines', 'network', 'vulnerabilities', 'reports') AND a.code IN ('read', 'export'))
    -- Full CRUD on tickets
    OR (m.code = 'tickets' AND a.code IN ('read', 'create', 'edit'))
    -- Read audit
    OR (m.code = 'audit' AND a.code = 'read')
  );

-- client_viewer: Read-only on reports, assessment, machines
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
JOIN modules m ON p.module_id = m.id
JOIN actions a ON p.action_id = a.id
WHERE r.code = 'client_viewer'
  AND m.code IN ('assessment', 'machines', 'reports', 'tickets')
  AND a.code = 'read';
