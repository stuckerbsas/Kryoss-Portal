-- =============================================================================
-- seed_039_kryoss_baseline.sql
-- Kryoss Recommended Baseline — a compliance framework that maps 1:1 to every
-- Cloud Assessment finding. Gives customers a single "Kryoss Score" covering
-- all areas: identity, endpoint, data, productivity, azure, powerbi, mail_flow.
--
-- Idempotent: MERGE + NOT EXISTS patterns.
-- =============================================================================

-- 1. Framework
MERGE cloud_assessment_frameworks AS tgt
USING (VALUES
    ('KRYOSS', 'Kryoss Recommended Baseline', 'v1.0', 'Kryoss / Geminis Computer',
     NULL,
     'Kryoss opinionated baseline covering all Cloud Assessment areas. One control per finding — 100% coverage by design.')
) AS src(code, name, version, authority, doc_url, description)
ON tgt.code = src.code
WHEN NOT MATCHED THEN
    INSERT (code, name, version, authority, doc_url, description, active)
    VALUES (src.code, src.name, src.version, src.authority, src.doc_url, src.description, 1)
WHEN MATCHED THEN
    UPDATE SET name = src.name, version = src.version, description = src.description;

DECLARE @fw UNIQUEIDENTIFIER = (SELECT id FROM cloud_assessment_frameworks WHERE code = 'KRYOSS');

-- 2. Controls — one per finding, grouped by category = area label
;WITH controls_seed AS (
    SELECT * FROM (VALUES
    -- ── IDENTITY (area=identity, service=entra) ───────────────────
    (@fw, 'KRY-ID-001',  'Microsoft Entra ID P1',                        'Verify P1 licensing and core features',                            'Identity',     'High'),
    (@fw, 'KRY-ID-002',  'Microsoft Entra ID P2',                        'Verify P2 licensing for advanced identity protection',              'Identity',     'High'),
    (@fw, 'KRY-ID-003',  'Conditional Access',                           'Enforce Conditional Access policies',                               'Identity',     'High'),
    (@fw, 'KRY-ID-004',  'Entra Identity Protection',                    'Enable risk-based identity protection',                             'Identity',     'High'),
    (@fw, 'KRY-ID-005',  'Microsoft Intune (Plan A)',                    'Verify Intune licensing for device management',                     'Identity',     'High'),
    (@fw, 'KRY-ID-006',  'Microsoft Entra ID Governance',                'Enable identity governance and lifecycle management',                'Identity',     'Medium'),
    (@fw, 'KRY-ID-007',  'Microsoft Entra Internet Access',              'Configure secure internet access via Entra',                        'Identity',     'Medium'),
    (@fw, 'KRY-ID-008',  'Microsoft Entra Private Access',               'Configure private access for internal apps',                        'Identity',     'Medium'),
    (@fw, 'KRY-ID-009',  'Service Principal Credentials',                'Audit and rotate service principal credentials',                    'Identity',     'High'),
    (@fw, 'KRY-ID-010',  'B2B Cross-Tenant Access',                     'Review cross-tenant access policies',                               'Identity',     'Medium'),
    (@fw, 'KRY-ID-011',  'PIM Activation Policies',                     'Configure Privileged Identity Management activation',                'Identity',     'High'),
    (@fw, 'KRY-ID-012',  'User Lifecycle Hygiene',                      'Manage stale/inactive user accounts',                                'Identity',     'Medium'),
    (@fw, 'KRY-ID-013',  'Administrator MFA',                           'Enforce MFA for all administrators',                                 'Identity',     'High'),
    (@fw, 'KRY-ID-014',  'OAuth Consent',                               'Review and restrict OAuth app consent',                              'Identity',     'High'),
    (@fw, 'KRY-ID-015',  'Device Security Posture',                     'Verify device compliance and security posture',                      'Identity',     'Medium'),
    (@fw, 'KRY-ID-016',  'Multi-Factor Authentication',                 'Verify MFA adoption across all users',                               'Identity',     'High'),

    -- ── ENDPOINT (area=endpoint, service=intune) ──────────────────
    (@fw, 'KRY-EP-001',  'Device Compliance Policies',                  'Deploy and enforce device compliance policies',                      'Endpoint',     'High'),
    (@fw, 'KRY-EP-002',  'Compliance Rate',                             'Monitor overall device compliance rate',                              'Endpoint',     'High'),
    (@fw, 'KRY-EP-003',  'BYOD App Protection',                        'Configure app protection policies for BYOD',                          'Endpoint',     'Medium'),
    (@fw, 'KRY-EP-004',  'Windows Autopilot',                           'Deploy Windows Autopilot for zero-touch provisioning',                'Endpoint',     'Medium'),
    (@fw, 'KRY-EP-005',  'Device Encryption',                           'Enforce BitLocker/FileVault encryption',                              'Endpoint',     'High'),
    (@fw, 'KRY-EP-006',  'Enrollment Restrictions',                     'Configure enrollment restrictions for managed devices',               'Endpoint',     'Medium'),
    (@fw, 'KRY-EP-007',  'Windows Compliance',                          'Windows device compliance status',                                    'Endpoint',     'High'),
    (@fw, 'KRY-EP-008',  'iOS Compliance',                              'iOS device compliance status',                                        'Endpoint',     'Medium'),
    (@fw, 'KRY-EP-009',  'Android Compliance',                          'Android device compliance status',                                    'Endpoint',     'Medium'),
    (@fw, 'KRY-EP-010',  'iOS App Protection',                          'iOS app protection policy status',                                    'Endpoint',     'Medium'),
    (@fw, 'KRY-EP-011',  'Android App Protection',                      'Android app protection policy status',                                'Endpoint',     'Medium'),

    -- ── ENDPOINT (area=endpoint, service=defender-endpoint) ───────
    (@fw, 'KRY-EP-020',  'Defender for Endpoint',                       'Verify Defender for Endpoint onboarding',                             'Endpoint',     'High'),
    (@fw, 'KRY-EP-021',  'Exposure Score',                              'Monitor Defender exposure score',                                     'Endpoint',     'High'),
    (@fw, 'KRY-EP-022',  'Vulnerability Posture',                       'Assess vulnerability remediation posture',                             'Endpoint',     'High'),
    (@fw, 'KRY-EP-023',  'High Vulnerabilities',                        'Track high-severity vulnerabilities',                                  'Endpoint',     'High'),
    (@fw, 'KRY-EP-024',  'Software Inventory',                          'Maintain software inventory via Defender',                              'Endpoint',     'Medium'),
    (@fw, 'KRY-EP-025',  'High-Risk Machines',                          'Identify and remediate high-risk machines',                             'Endpoint',     'High'),

    -- ── DATA (area=data, service=purview) ─────────────────────────
    (@fw, 'KRY-DA-001',  'AIP/DLP Licensing',                          'Verify Azure Information Protection / DLP licensing',                  'Data Protection', 'High'),
    (@fw, 'KRY-DA-002',  'Sensitivity Label Deployment',               'Deploy sensitivity labels across the organization',                    'Data Protection', 'High'),
    (@fw, 'KRY-DA-003',  'DLP Policy Posture',                         'Configure and enforce DLP policies',                                   'Data Protection', 'High'),
    (@fw, 'KRY-DA-004',  'eDiscovery',                                 'Configure eDiscovery for compliance',                                  'Data Protection', 'Medium'),
    (@fw, 'KRY-DA-005',  'Advanced Audit',                             'Enable advanced audit capabilities',                                   'Data Protection', 'Medium'),
    (@fw, 'KRY-DA-006',  'Retention Labels',                           'Deploy retention labels and policies',                                  'Data Protection', 'Medium'),
    (@fw, 'KRY-DA-007',  'Customer Lockbox',                           'Enable Customer Lockbox for data access control',                      'Data Protection', 'Medium'),
    (@fw, 'KRY-DA-008',  'Information Barriers',                       'Configure information barriers for data segregation',                   'Data Protection', 'Medium'),
    (@fw, 'KRY-DA-009',  'Insider Risk Management',                    'Enable insider risk management policies',                               'Data Protection', 'Medium'),

    -- ── DATA (area=data, service=sharepoint) ──────────────────────
    (@fw, 'KRY-DA-020',  'Sensitivity Label Coverage',                 'Monitor sensitivity label coverage on SharePoint',                      'Data Protection', 'High'),
    (@fw, 'KRY-DA-021',  'SharePoint Oversharing',                     'Detect and reduce SharePoint oversharing',                             'Data Protection', 'High'),
    (@fw, 'KRY-DA-022',  'High-Risk SharePoint Sites',                 'Identify high-risk SharePoint sites',                                  'Data Protection', 'High'),
    (@fw, 'KRY-DA-023',  'External Guest Users',                       'Audit external guest user access to SharePoint',                       'Data Protection', 'High'),
    (@fw, 'KRY-DA-024',  'Unlabeled Sensitive Content',                'Detect unlabeled sensitive content in SharePoint',                     'Data Protection', 'High'),

    -- ── DATA (area=data, service=onedrive) ────────────────────────
    (@fw, 'KRY-DA-030',  'OneDrive Storage Hoarding',                  'Detect excessive OneDrive storage consumption',                        'Data Protection', 'Medium'),

    -- ── PRODUCTIVITY (area=productivity) ──────────────────────────
    (@fw, 'KRY-PR-001',  'Microsoft 365 Copilot Adoption',             'Track Copilot adoption and usage',                                     'Productivity', 'Medium'),
    (@fw, 'KRY-PR-002',  'Exchange Online Adoption',                   'Monitor Exchange Online adoption rates',                                'Productivity', 'Medium'),
    (@fw, 'KRY-PR-003',  'Microsoft Teams Adoption',                   'Monitor Teams adoption rates',                                          'Productivity', 'Medium'),
    (@fw, 'KRY-PR-004',  'SharePoint Deployment',                      'Verify SharePoint site deployment',                                     'Productivity', 'Medium'),
    (@fw, 'KRY-PR-005',  'OneDrive for Business Adoption',             'Monitor OneDrive adoption rates',                                       'Productivity', 'Medium'),
    (@fw, 'KRY-PR-006',  'Microsoft 365 Apps Desktop Adoption',        'Track desktop Office activation rates',                                  'Productivity', 'Medium'),
    (@fw, 'KRY-PR-007',  'Wasted Licenses',                            'Identify and reclaim unused licenses',                                   'Productivity', 'High'),
    (@fw, 'KRY-PR-008',  'Wasted Copilot Licenses',                    'Identify unused Copilot license assignments',                            'Productivity', 'High'),
    (@fw, 'KRY-PR-009',  'External Guest Ratio',                       'Monitor external guest user ratio',                                      'Productivity', 'Medium'),
    (@fw, 'KRY-PR-010',  'Graph Connectors for Copilot',               'Deploy Graph Connectors for Copilot grounding',                          'Productivity', 'Medium'),

    -- ── AZURE (area=azure) ────────────────────────────────────────
    (@fw, 'KRY-AZ-001',  'Subscription Coverage',                      'Verify Azure subscription enrollment in Kryoss',                        'Azure',        'High'),
    (@fw, 'KRY-AZ-002',  'Resource Density',                           'Assess resource distribution across subscriptions',                      'Azure',        'Medium'),
    (@fw, 'KRY-AZ-003',  'Regional Footprint',                         'Review multi-region resource deployment',                                'Azure',        'Medium'),
    (@fw, 'KRY-AZ-004',  'Public Exposure Posture',                    'Audit public-facing resource exposure',                                  'Azure',        'High'),
    (@fw, 'KRY-AZ-005',  'Defender for Cloud Activation',              'Enable Defender for Cloud across subscriptions',                          'Azure',        'High'),
    (@fw, 'KRY-AZ-006',  'Defender Unhealthy Ratio',                   'Monitor Defender unhealthy resource ratio',                               'Azure',        'High'),
    (@fw, 'KRY-AZ-007',  'Defender Assessment Posture',                'Review Defender for Cloud assessment results',                            'Azure',        'High'),
    (@fw, 'KRY-AZ-008',  'Secure Score',                               'Monitor Azure Secure Score',                                              'Azure',        'High'),
    (@fw, 'KRY-AZ-009',  'Secure Score Healthy',                       'Track Secure Score health over time',                                     'Azure',        'High'),
    (@fw, 'KRY-AZ-010',  'Public Blob Access',                         'Disable anonymous public blob access',                                    'Azure',        'High'),
    (@fw, 'KRY-AZ-011',  'Secure Transfer Required',                   'Enforce HTTPS for storage accounts',                                      'Azure',        'High'),
    (@fw, 'KRY-AZ-012',  'Blob Soft-Delete',                           'Enable blob soft-delete for recovery',                                    'Azure',        'Medium'),
    (@fw, 'KRY-AZ-013',  'Key Vault Soft-Delete',                      'Enable Key Vault soft-delete',                                            'Azure',        'High'),
    (@fw, 'KRY-AZ-014',  'Key Vault Purge Protection',                 'Enable Key Vault purge protection',                                       'Azure',        'High'),
    (@fw, 'KRY-AZ-015',  'Public IP Sprawl',                           'Audit and reduce public IP allocation',                                   'Azure',        'Medium'),
    (@fw, 'KRY-AZ-016',  'NSG Unrestricted Inbound',                   'Block unrestricted inbound NSG rules',                                    'Azure',        'High'),
    (@fw, 'KRY-AZ-017',  'VM OS Disk Encryption',                      'Enforce OS disk encryption on VMs',                                       'Azure',        'High'),
    (@fw, 'KRY-AZ-018',  'VM Managed Identity',                        'Use managed identities for VMs',                                          'Azure',        'Medium'),
    (@fw, 'KRY-AZ-019',  'Policy Compliance',                          'Monitor Azure Policy compliance',                                         'Azure',        'High'),

    -- ── POWER BI (area=powerbi, service=powerbi) ──────────────────
    (@fw, 'KRY-PB-001',  'Admin API Access',                           'Verify Power BI admin API access',                                        'Power BI',     'High'),
    (@fw, 'KRY-PB-002',  'Orphaned Workspaces',                        'Detect and remediate orphaned workspaces',                                'Power BI',     'Medium'),
    (@fw, 'KRY-PB-003',  'Personal Workspaces',                        'Audit personal workspace usage',                                          'Power BI',     'Medium'),
    (@fw, 'KRY-PB-004',  'External Workspace Users',                   'Audit external users in workspaces',                                      'Power BI',     'Medium'),
    (@fw, 'KRY-PB-005',  'Premium Capacity',                           'Verify Premium capacity availability',                                    'Power BI',     'Medium'),
    (@fw, 'KRY-PB-006',  'Capacity Usage',                             'Monitor capacity utilization',                                             'Power BI',     'Medium'),
    (@fw, 'KRY-PB-007',  'Dataset Freshness',                          'Monitor dataset refresh health',                                           'Power BI',     'Medium'),
    (@fw, 'KRY-PB-008',  'Gateway Status',                             'Monitor data gateway health',                                              'Power BI',     'High'),
    (@fw, 'KRY-PB-009',  'External Sharing',                           'Audit external sharing volume',                                            'Power BI',     'Medium'),
    (@fw, 'KRY-PB-010',  'Export Activity',                            'Monitor high export volume',                                                'Power BI',     'Medium'),
    (@fw, 'KRY-PB-011',  'Delete Activity',                            'Monitor high delete activity',                                              'Power BI',     'Medium'),

    -- ── MAIL FLOW (area=mail_flow) ────────────────────────────────
    (@fw, 'KRY-MF-001',  'SPF',                                        'Configure SPF records for email authentication',                           'Mail Flow',    'High'),
    (@fw, 'KRY-MF-002',  'DKIM',                                       'Configure DKIM signing for email authentication',                          'Mail Flow',    'High'),
    (@fw, 'KRY-MF-003',  'DMARC',                                      'Configure DMARC policy for email spoofing protection',                     'Mail Flow',    'High'),
    (@fw, 'KRY-MF-004',  'MTA-STS',                                    'Configure MTA-STS for transport security',                                 'Mail Flow',    'Medium'),
    (@fw, 'KRY-MF-005',  'BIMI',                                       'Configure BIMI for brand authentication',                                  'Mail Flow',    'Medium'),
    (@fw, 'KRY-MF-006',  'External Mailbox Forwarding',                'Detect and block external mailbox forwarding',                              'Mail Flow',    'High'),
    (@fw, 'KRY-MF-007',  'Stealth Forwarding Rules',                   'Detect hidden/stealth forwarding rules',                                   'Mail Flow',    'High'),
    (@fw, 'KRY-MF-008',  'Shared Mailbox Sign-in',                     'Audit shared mailbox direct sign-in',                                      'Mail Flow',    'Medium')

    ) AS v(framework_id, control_code, title, description, category, priority)
)
INSERT INTO cloud_assessment_framework_controls
    (framework_id, control_code, title, description, category, priority)
SELECT cs.framework_id, cs.control_code, cs.title, cs.description, cs.category, cs.priority
FROM controls_seed cs
WHERE NOT EXISTS (
    SELECT 1 FROM cloud_assessment_framework_controls c
    WHERE c.framework_id = cs.framework_id AND c.control_code = cs.control_code
);

-- 3. Finding → control mappings (area/service/feature → control)
-- Each mapping has coverage='full' since Kryoss baseline maps 1:1.

;WITH mapping_seed AS (
    SELECT * FROM (VALUES
    -- ── IDENTITY ──────────────────────────────────────────────────
    ('identity', 'entra',            'Microsoft Entra ID P1',                       'KRY-ID-001'),
    ('identity', 'entra',            'Microsoft Entra ID P2',                       'KRY-ID-002'),
    ('identity', 'entra',            'Conditional Access',                          'KRY-ID-003'),
    ('identity', 'entra',            'Entra Identity Protection',                   'KRY-ID-004'),
    ('identity', 'entra',            'Microsoft Intune (Plan A)',                   'KRY-ID-005'),
    ('identity', 'entra',            'Microsoft Entra ID Governance',               'KRY-ID-006'),
    ('identity', 'entra',            'Microsoft Entra Internet Access',             'KRY-ID-007'),
    ('identity', 'entra',            'Microsoft Entra Private Access',              'KRY-ID-008'),
    ('identity', 'entra',            'Service Principal Credentials',               'KRY-ID-009'),
    ('identity', 'entra',            'B2B Cross-Tenant Access',                    'KRY-ID-010'),
    ('identity', 'entra',            'PIM Activation Policies',                    'KRY-ID-011'),
    ('identity', 'entra',            'User Lifecycle Hygiene',                     'KRY-ID-012'),
    ('identity', 'entra',            'Administrator MFA',                          'KRY-ID-013'),
    ('identity', 'entra',            'OAuth Consent',                              'KRY-ID-014'),
    ('identity', 'entra',            'Device Security Posture',                    'KRY-ID-015'),
    ('identity', 'entra',            'Microsoft Entra ID Multi-Factor Authentication', 'KRY-ID-016'),

    -- ── ENDPOINT (intune) ─────────────────────────────────────────
    ('endpoint', 'intune',           'Device Compliance Policies',                  'KRY-EP-001'),
    ('endpoint', 'intune',           'Compliance Rate',                             'KRY-EP-002'),
    ('endpoint', 'intune',           'BYOD App Protection',                         'KRY-EP-003'),
    ('endpoint', 'intune',           'Windows Autopilot',                           'KRY-EP-004'),
    ('endpoint', 'intune',           'Device Encryption',                           'KRY-EP-005'),
    ('endpoint', 'intune',           'Enrollment Restrictions',                     'KRY-EP-006'),
    ('endpoint', 'intune',           'Windows Compliance',                          'KRY-EP-007'),
    ('endpoint', 'intune',           'iOS Compliance',                              'KRY-EP-008'),
    ('endpoint', 'intune',           'Android Compliance',                          'KRY-EP-009'),
    ('endpoint', 'intune',           'iOS App Protection',                          'KRY-EP-010'),
    ('endpoint', 'intune',           'Android App Protection',                      'KRY-EP-011'),

    -- ── ENDPOINT (defender-endpoint) ──────────────────────────────
    ('endpoint', 'defender-endpoint', 'Defender for Endpoint',                      'KRY-EP-020'),
    ('endpoint', 'defender-endpoint', 'Exposure Score',                             'KRY-EP-021'),
    ('endpoint', 'defender-endpoint', 'Vulnerability Posture',                      'KRY-EP-022'),
    ('endpoint', 'defender-endpoint', 'High Vulnerabilities',                       'KRY-EP-023'),
    ('endpoint', 'defender-endpoint', 'Software Inventory',                         'KRY-EP-024'),
    ('endpoint', 'defender-endpoint', 'High-Risk Machines',                         'KRY-EP-025'),

    -- ── DATA (purview) ────────────────────────────────────────────
    ('data',     'purview',          'AIP/DLP Licensing',                           'KRY-DA-001'),
    ('data',     'purview',          'Sensitivity Label Deployment',                'KRY-DA-002'),
    ('data',     'purview',          'DLP Policy Posture',                          'KRY-DA-003'),
    ('data',     'purview',          'eDiscovery',                                  'KRY-DA-004'),
    ('data',     'purview',          'Advanced Audit',                              'KRY-DA-005'),
    ('data',     'purview',          'Retention Labels',                            'KRY-DA-006'),
    ('data',     'purview',          'Customer Lockbox',                            'KRY-DA-007'),
    ('data',     'purview',          'Information Barriers',                        'KRY-DA-008'),
    ('data',     'purview',          'Insider Risk Management',                     'KRY-DA-009'),

    -- ── DATA (sharepoint) ─────────────────────────────────────────
    ('data',     'sharepoint',       'Sensitivity Label Coverage',                  'KRY-DA-020'),
    ('data',     'sharepoint',       'SharePoint Oversharing',                      'KRY-DA-021'),
    ('data',     'sharepoint',       'High-Risk SharePoint Sites',                  'KRY-DA-022'),
    ('data',     'sharepoint',       'External Guest Users',                        'KRY-DA-023'),
    ('data',     'sharepoint',       'Unlabeled Sensitive Content',                 'KRY-DA-024'),

    -- ── DATA (onedrive) ───────────────────────────────────────────
    ('data',     'onedrive',         'OneDrive Storage Hoarding',                   'KRY-DA-030'),

    -- ── PRODUCTIVITY ──────────────────────────────────────────────
    ('productivity', 'copilot',      'Microsoft 365 Copilot Adoption',              'KRY-PR-001'),
    ('productivity', 'exchange',     'Exchange Online Adoption',                    'KRY-PR-002'),
    ('productivity', 'teams',        'Microsoft Teams Adoption',                    'KRY-PR-003'),
    ('productivity', 'sharepoint',   'SharePoint Deployment',                       'KRY-PR-004'),
    ('productivity', 'onedrive',     'OneDrive for Business Adoption',              'KRY-PR-005'),
    ('productivity', 'office',       'Microsoft 365 Apps Desktop Adoption',         'KRY-PR-006'),
    ('productivity', 'licensing',    'Wasted Licenses',                             'KRY-PR-007'),
    ('productivity', 'licensing',    'Wasted Copilot Licenses',                     'KRY-PR-008'),
    ('productivity', 'identity',     'External Guest Ratio',                        'KRY-PR-009'),
    ('productivity', 'copilot',      'Graph Connectors for Copilot',                'KRY-PR-010'),

    -- ── AZURE ─────────────────────────────────────────────────────
    ('azure',    'arm',              'Subscription Coverage',                       'KRY-AZ-001'),
    ('azure',    'arm',              'Resource Density',                            'KRY-AZ-002'),
    ('azure',    'arm',              'Regional Footprint',                          'KRY-AZ-003'),
    ('azure',    'arm',              'Public exposure posture',                     'KRY-AZ-004'),
    ('azure',    'defender-cloud',   'Defender for Cloud Activation',               'KRY-AZ-005'),
    ('azure',    'defender-cloud',   'Defender Unhealthy Ratio',                    'KRY-AZ-006'),
    ('azure',    'defender-cloud',   'Defender Assessment Posture',                 'KRY-AZ-007'),
    ('azure',    'defender-cloud',   'Secure Score',                                'KRY-AZ-008'),
    ('azure',    'defender-cloud',   'Secure Score Healthy',                        'KRY-AZ-009'),
    ('azure',    'storage',          'Public Blob Access',                          'KRY-AZ-010'),
    ('azure',    'storage',          'Secure Transfer Required',                    'KRY-AZ-011'),
    ('azure',    'storage',          'Blob Soft-Delete',                            'KRY-AZ-012'),
    ('azure',    'keyvault',         'Key Vault Soft-Delete',                       'KRY-AZ-013'),
    ('azure',    'keyvault',         'Key Vault Purge Protection',                  'KRY-AZ-014'),
    ('azure',    'network',          'Public IP Sprawl',                            'KRY-AZ-015'),
    ('azure',    'network',          'NSG Unrestricted Inbound',                    'KRY-AZ-016'),
    ('azure',    'compute',          'VM OS Disk Encryption',                       'KRY-AZ-017'),
    ('azure',    'compute',          'VM Managed Identity',                         'KRY-AZ-018'),
    ('azure',    'policy',           'Policy Compliance',                           'KRY-AZ-019'),

    -- ── POWER BI ──────────────────────────────────────────────────
    ('powerbi',  'powerbi',          'admin-api-access',                            'KRY-PB-001'),
    ('powerbi',  'powerbi',          'orphaned-workspaces',                         'KRY-PB-002'),
    ('powerbi',  'powerbi',          'personal-workspaces',                         'KRY-PB-003'),
    ('powerbi',  'powerbi',          'external-workspace-users',                    'KRY-PB-004'),
    ('powerbi',  'powerbi',          'no-premium-capacity',                         'KRY-PB-005'),
    ('powerbi',  'powerbi',          'capacity-overload',                           'KRY-PB-006'),
    ('powerbi',  'powerbi',          'capacity-usage',                              'KRY-PB-006'),
    ('powerbi',  'powerbi',          'datasets-never-refreshed',                    'KRY-PB-007'),
    ('powerbi',  'powerbi',          'datasets-stale',                              'KRY-PB-007'),
    ('powerbi',  'powerbi',          'dataset-freshness',                           'KRY-PB-007'),
    ('powerbi',  'powerbi',          'gateway-offline',                             'KRY-PB-008'),
    ('powerbi',  'powerbi',          'gateway-status',                              'KRY-PB-008'),
    ('powerbi',  'powerbi',          'personal-gateways-only',                      'KRY-PB-008'),
    ('powerbi',  'powerbi',          'no-gateways',                                 'KRY-PB-008'),
    ('powerbi',  'powerbi',          'external-sharing-volume',                     'KRY-PB-009'),
    ('powerbi',  'powerbi',          'external-sharing',                            'KRY-PB-009'),
    ('powerbi',  'powerbi',          'high-export-volume',                          'KRY-PB-010'),
    ('powerbi',  'powerbi',          'high-delete-activity',                        'KRY-PB-011'),

    -- ── MAIL FLOW ─────────────────────────────────────────────────
    ('mail_flow', 'email',           'SPF',                                         'KRY-MF-001'),
    ('mail_flow', 'email',           'DKIM',                                        'KRY-MF-002'),
    ('mail_flow', 'email',           'DMARC',                                       'KRY-MF-003'),
    ('mail_flow', 'email',           'MTA-STS',                                     'KRY-MF-004'),
    ('mail_flow', 'email',           'BIMI',                                        'KRY-MF-005'),
    ('mail_flow', 'mail_flow',       'External Mailbox Forwarding',                 'KRY-MF-006'),
    ('mail_flow', 'mail_flow',       'Stealth Forwarding Rules',                    'KRY-MF-007'),
    ('mail_flow', 'mail_flow',       'Shared Mailbox Sign-in',                      'KRY-MF-008')

    ) AS v(area, service, feature, control_code)
)
INSERT INTO cloud_assessment_finding_control_mappings
    (area, service, feature, framework_control_id, coverage)
SELECT
    ms.area,
    ms.service,
    ms.feature,
    c.id,
    'full'
FROM mapping_seed ms
INNER JOIN cloud_assessment_framework_controls c
    ON c.framework_id = @fw AND c.control_code = ms.control_code
WHERE NOT EXISTS (
    SELECT 1 FROM cloud_assessment_finding_control_mappings m
    WHERE m.area = ms.area
      AND m.service = ms.service
      AND m.feature = ms.feature
      AND m.framework_control_id = c.id
);

-- Verification
SELECT 'KRYOSS Baseline' AS framework,
       (SELECT COUNT(*) FROM cloud_assessment_framework_controls WHERE framework_id = @fw) AS controls,
       (SELECT COUNT(*) FROM cloud_assessment_finding_control_mappings m
        INNER JOIN cloud_assessment_framework_controls c ON m.framework_control_id = c.id
        WHERE c.framework_id = @fw) AS mappings;
