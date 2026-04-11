SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- seed_002_frameworks_platforms.sql
-- Kryoss Platform — Seed: Frameworks, Platforms, Control Categories,
--                         Network Device Types
-- Run AFTER 004_assessment.sql and 003_cmdb.sql
-- =============================================

DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- =============================================
-- FRAMEWORKS
-- =============================================
INSERT INTO frameworks (code, name, version, description, is_active, created_by) VALUES
    ('CIS',     N'CIS Benchmarks',                      'v3.0',  N'Center for Internet Security configuration benchmarks for Windows', 1, @systemUserId),
    ('NIST',    N'NIST Cybersecurity Framework',         'v2.0',  N'National Institute of Standards and Technology cybersecurity framework', 1, @systemUserId),
    ('HIPAA',   N'HIPAA Security Rule',                  '2024',  N'Health Insurance Portability and Accountability Act security requirements', 1, @systemUserId),
    ('PCI-DSS', N'PCI Data Security Standard',           'v4.0',  N'Payment Card Industry Data Security Standard', 1, @systemUserId),
    ('ISO27001', N'ISO/IEC 27001',                       '2022',  N'Information security management systems standard', 1, @systemUserId),
    ('CMMC',    N'Cybersecurity Maturity Model Cert.',   'v2.0',  N'CMMC for DoD contractors', 0, @systemUserId);

-- =============================================
-- PLATFORMS
-- =============================================
INSERT INTO platforms (code, name, is_active, created_by) VALUES
    ('W10',   N'Windows 10 (21H2+)',                  1, @systemUserId),
    ('W11',   N'Windows 11 (22H2+)',                  1, @systemUserId),
    ('MS19',  N'Windows Server 2019',                 1, @systemUserId),
    ('MS22',  N'Windows Server 2022',                 1, @systemUserId),
    ('MS25',  N'Windows Server 2025',                 1, @systemUserId),
    ('DC19',  N'Windows Server 2019 Domain Controller', 1, @systemUserId),
    ('DC22',  N'Windows Server 2022 Domain Controller', 1, @systemUserId),
    ('DC25',  N'Windows Server 2025 Domain Controller', 1, @systemUserId);

-- =============================================
-- CONTROL CATEGORIES (from CIS benchmark sections)
-- =============================================
INSERT INTO control_categories (name, sort_order, created_by) VALUES
    (N'Personalization',                1,  @systemUserId),
    (N'LAPS',                           2,  @systemUserId),
    (N'Audit Policy',                   3,  @systemUserId),
    (N'User Rights Assignment',         4,  @systemUserId),
    (N'Security Options',               5,  @systemUserId),
    (N'Account Policies',               6,  @systemUserId),
    (N'Account Lockout',                7,  @systemUserId),
    (N'Network Access',                 8,  @systemUserId),
    (N'Network Security',               9,  @systemUserId),
    (N'Firewall',                       10, @systemUserId),
    (N'Windows Defender',               11, @systemUserId),
    (N'Remote Desktop',                 12, @systemUserId),
    (N'BitLocker',                      13, @systemUserId),
    (N'Credential Guard',               14, @systemUserId),
    (N'Device Guard',                   15, @systemUserId),
    (N'Windows Update',                 16, @systemUserId),
    (N'Browser Security',               17, @systemUserId),
    (N'Services',                       18, @systemUserId),
    (N'Network Protocol',               19, @systemUserId),
    (N'Power Management',               20, @systemUserId),
    (N'AutoPlay',                       21, @systemUserId),
    (N'Telemetry',                      22, @systemUserId),
    (N'Windows Ink',                    23, @systemUserId),
    (N'App Runtime',                    24, @systemUserId),
    (N'Cloud Content',                  25, @systemUserId),
    (N'Credential UI',                  26, @systemUserId),
    (N'Data Collection',                27, @systemUserId),
    (N'Delivery Optimization',          28, @systemUserId),
    (N'Event Log',                      29, @systemUserId),
    (N'Explorer',                       30, @systemUserId),
    (N'HomeGroup',                      31, @systemUserId),
    (N'Lanman',                         32, @systemUserId),
    (N'MSS',                            33, @systemUserId),
    (N'Sleep Settings',                 34, @systemUserId),
    (N'Store',                          35, @systemUserId),
    (N'WinRM',                          36, @systemUserId),
    (N'AppLocker',                      37, @systemUserId),
    (N'WDAC',                           38, @systemUserId);

-- =============================================
-- NETWORK DEVICE TYPES
-- =============================================
INSERT INTO network_device_types (name) VALUES
    ('Router'),
    ('Switch'),
    ('Firewall'),
    ('AccessPoint'),
    ('Printer'),
    ('NAS'),
    ('UPS'),
    ('Camera'),
    ('IoT'),
    ('Unknown');
