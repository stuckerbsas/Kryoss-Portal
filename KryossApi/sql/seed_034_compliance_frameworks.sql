-- =============================================================================
-- seed_034_compliance_frameworks.sql
-- CA-8 Track 1: Seed 7 frameworks + controls + finding→control mappings
--
-- Frameworks: HIPAA, ISO 27001, NIST CSF, SOC 2, PCI DSS, CIS Benchmarks, CMMC L2
-- Idempotent: uses MERGE or IF NOT EXISTS patterns.
-- =============================================================================

-- ============================================================
-- 1. FRAMEWORKS
-- ============================================================

-- Use temp table approach for idempotent upsert
MERGE cloud_assessment_frameworks AS tgt
USING (VALUES
    ('HIPAA',      'HIPAA Security Rule',                  '45 CFR Parts 160, 162, 164', 'U.S. Department of Health and Human Services', 'https://www.hhs.gov/hipaa/for-professionals/security/index.html',
     'Health Insurance Portability and Accountability Act — security standards for electronic protected health information (ePHI)'),
    ('ISO27001',   'ISO/IEC 27001:2022',                   '2022', 'International Organization for Standardization', 'https://www.iso.org/standard/27001',
     'Information security management system (ISMS) standard with Annex A controls'),
    ('NIST_CSF',   'NIST Cybersecurity Framework',         'v2.0', 'National Institute of Standards and Technology', 'https://www.nist.gov/cyberframework',
     'Voluntary framework for managing and reducing cybersecurity risk'),
    ('SOC2',       'SOC 2 Type II',                        '2017 TSC', 'American Institute of CPAs', 'https://www.aicpa.org/topic/audit-assurance/audit-and-assurance-greater-than-soc-2',
     'Trust Services Criteria: Security, Availability, Processing Integrity, Confidentiality, Privacy'),
    ('PCI_DSS',    'PCI DSS',                              'v4.0', 'PCI Security Standards Council', 'https://www.pcisecuritystandards.org/',
     'Payment Card Industry Data Security Standard'),
    ('CIS',        'CIS Microsoft 365 Foundations Benchmark', 'v3.1.0', 'Center for Internet Security', 'https://www.cisecurity.org/benchmark/microsoft_365',
     'Consensus-based security configuration guidelines for Microsoft 365'),
    ('CMMC_L2',    'CMMC Level 2',                         'v2.0', 'U.S. Department of Defense', 'https://dodcio.defense.gov/CMMC/',
     'Cybersecurity Maturity Model Certification — 110 practices aligned to NIST SP 800-171')
) AS src(code, name, version, authority, doc_url, description)
ON tgt.code = src.code
WHEN NOT MATCHED THEN
    INSERT (code, name, version, authority, doc_url, description, active)
    VALUES (src.code, src.name, src.version, src.authority, src.doc_url, src.description, 1);

-- ============================================================
-- 2. FRAMEWORK CONTROLS
--    Real control codes mapped to our assessment areas.
--    Category groups controls for UI drill-down.
-- ============================================================

-- Helper: declare framework ID variables
DECLARE @fw_hipaa   UNIQUEIDENTIFIER = (SELECT id FROM cloud_assessment_frameworks WHERE code = 'HIPAA');
DECLARE @fw_iso     UNIQUEIDENTIFIER = (SELECT id FROM cloud_assessment_frameworks WHERE code = 'ISO27001');
DECLARE @fw_nist    UNIQUEIDENTIFIER = (SELECT id FROM cloud_assessment_frameworks WHERE code = 'NIST_CSF');
DECLARE @fw_soc2    UNIQUEIDENTIFIER = (SELECT id FROM cloud_assessment_frameworks WHERE code = 'SOC2');
DECLARE @fw_pci     UNIQUEIDENTIFIER = (SELECT id FROM cloud_assessment_frameworks WHERE code = 'PCI_DSS');
DECLARE @fw_cis     UNIQUEIDENTIFIER = (SELECT id FROM cloud_assessment_frameworks WHERE code = 'CIS');
DECLARE @fw_cmmc    UNIQUEIDENTIFIER = (SELECT id FROM cloud_assessment_frameworks WHERE code = 'CMMC_L2');

-- Idempotent insert helper: only insert if (framework_id, control_code) not exists
-- Using a CTE + NOT EXISTS pattern for bulk idempotent inserts

;WITH controls_seed AS (
    SELECT * FROM (VALUES
    -- ── HIPAA ──────────────────────────────────────────────────
    (@fw_hipaa, '164.312(a)(1)',  'Access Control',                          'Implement technical policies for electronic information systems with ePHI', 'Access Control',       'High'),
    (@fw_hipaa, '164.312(a)(2)(i)', 'Unique User Identification',           'Assign unique name/number for tracking user identity',                     'Access Control',       'High'),
    (@fw_hipaa, '164.312(a)(2)(ii)', 'Emergency Access Procedure',          'Establish procedures for obtaining ePHI during an emergency',              'Access Control',       'Medium'),
    (@fw_hipaa, '164.312(a)(2)(iii)', 'Automatic Logoff',                   'Implement electronic procedures that terminate session after inactivity',  'Access Control',       'Medium'),
    (@fw_hipaa, '164.312(a)(2)(iv)', 'Encryption and Decryption',           'Implement mechanism to encrypt/decrypt ePHI',                             'Access Control',       'High'),
    (@fw_hipaa, '164.312(b)',    'Audit Controls',                           'Implement hardware/software/procedural mechanisms to record and examine activity', 'Audit',        'High'),
    (@fw_hipaa, '164.312(c)(1)', 'Integrity Controls',                      'Implement policies to protect ePHI from improper alteration or destruction', 'Integrity',          'High'),
    (@fw_hipaa, '164.312(c)(2)', 'Mechanism to Authenticate ePHI',          'Implement electronic mechanisms to corroborate ePHI has not been altered', 'Integrity',           'Medium'),
    (@fw_hipaa, '164.312(d)',    'Person or Entity Authentication',          'Implement procedures to verify identity of person/entity seeking access', 'Authentication',      'High'),
    (@fw_hipaa, '164.312(e)(1)', 'Transmission Security',                   'Implement technical security measures for ePHI transmitted over network',  'Transmission',        'High'),
    (@fw_hipaa, '164.312(e)(2)(i)', 'Integrity Controls (Transmission)',    'Implement security measures to ensure ePHI not improperly modified during transmission', 'Transmission', 'High'),
    (@fw_hipaa, '164.312(e)(2)(ii)', 'Encryption (Transmission)',           'Implement mechanism to encrypt ePHI when transmitted over open networks',  'Transmission',        'High'),
    (@fw_hipaa, '164.308(a)(1)', 'Security Management Process',             'Implement policies to prevent, detect, contain, and correct security violations', 'Administrative', 'High'),
    (@fw_hipaa, '164.308(a)(3)', 'Workforce Security',                      'Implement policies to ensure appropriate workforce access to ePHI',        'Administrative',      'High'),
    (@fw_hipaa, '164.308(a)(4)', 'Information Access Management',           'Implement policies authorizing access to ePHI',                            'Administrative',      'High'),
    (@fw_hipaa, '164.308(a)(5)', 'Security Awareness and Training',         'Implement security awareness and training program for workforce',          'Administrative',      'Medium'),
    (@fw_hipaa, '164.308(a)(6)', 'Security Incident Procedures',            'Implement policies to address security incidents',                         'Administrative',      'High'),
    (@fw_hipaa, '164.308(a)(7)', 'Contingency Plan',                        'Establish policies for responding to emergency or occurrence',              'Administrative',      'High'),

    -- ── ISO 27001 (Annex A) ────────────────────────────────────
    (@fw_iso, 'A.5.1',   'Policies for information security',       'Management direction for information security',                              'Organizational',  'High'),
    (@fw_iso, 'A.5.15',  'Access control',                          'Rules to control physical and logical access',                               'Access Control',  'High'),
    (@fw_iso, 'A.5.17',  'Authentication information',              'Allocation and management of authentication information',                    'Access Control',  'High'),
    (@fw_iso, 'A.5.23',  'Information security for cloud services',  'Processes for acquisition, use, management and exit from cloud services',   'Cloud',           'High'),
    (@fw_iso, 'A.5.29',  'Information security during disruption',   'Plan how to maintain infosec during disruption',                            'Continuity',      'Medium'),
    (@fw_iso, 'A.5.34',  'Privacy and protection of PII',           'Achieve compliance with privacy regulations',                                'Privacy',         'High'),
    (@fw_iso, 'A.6.1',   'Screening',                               'Background verification checks on all candidates',                          'People',          'Medium'),
    (@fw_iso, 'A.7.1',   'Physical security perimeters',            'Define security perimeters for areas with sensitive information',             'Physical',        'Medium'),
    (@fw_iso, 'A.8.1',   'User endpoint devices',                   'Protect information stored on, processed by or accessible via endpoint',     'Technology',      'High'),
    (@fw_iso, 'A.8.3',   'Information access restriction',          'Restrict access to information according to established access control policy', 'Technology',   'High'),
    (@fw_iso, 'A.8.5',   'Secure authentication',                   'Secure authentication technologies and procedures',                          'Technology',      'High'),
    (@fw_iso, 'A.8.7',   'Protection against malware',              'Implement controls against malware',                                         'Technology',      'High'),
    (@fw_iso, 'A.8.8',   'Management of technical vulnerabilities',  'Obtain information about vulnerabilities; take appropriate action',          'Technology',      'High'),
    (@fw_iso, 'A.8.9',   'Configuration management',                'Establish, document, implement, monitor and review configurations',           'Technology',      'Medium'),
    (@fw_iso, 'A.8.10',  'Information deletion',                     'Delete information when no longer required',                                 'Technology',      'Medium'),
    (@fw_iso, 'A.8.11',  'Data masking',                             'Data masking in accordance with access control policy',                      'Technology',      'Medium'),
    (@fw_iso, 'A.8.12',  'Data leakage prevention',                  'Apply data leakage prevention measures to systems and networks',             'Technology',      'High'),
    (@fw_iso, 'A.8.15',  'Logging',                                  'Produce, store, protect and analyze logs',                                   'Technology',      'High'),
    (@fw_iso, 'A.8.20',  'Networks security',                        'Secure network services, protect information in systems and applications',    'Technology',      'High'),
    (@fw_iso, 'A.8.24',  'Use of cryptography',                      'Rules for effective use of cryptography including key management',            'Technology',      'High'),
    (@fw_iso, 'A.8.25',  'Secure development life cycle',            'Rules for secure development of software and systems',                       'Technology',      'Medium'),
    (@fw_iso, 'A.8.28',  'Secure coding',                            'Apply secure coding principles to software development',                     'Technology',      'Medium'),

    -- ── NIST CSF v2.0 ──────────────────────────────────────────
    (@fw_nist, 'GV.SC-01', 'Supply Chain Risk Management Program',  'Establish and maintain a cyber supply chain risk management program',        'Govern',          'Medium'),
    (@fw_nist, 'ID.AM-01', 'Asset Management — Inventories',        'Inventories of hardware, software, services, and data',                      'Identify',        'High'),
    (@fw_nist, 'ID.AM-02', 'Asset Management — Software Inventory',  'Software platforms and applications are inventoried',                       'Identify',        'High'),
    (@fw_nist, 'ID.RA-01', 'Risk Assessment — Vulnerabilities',      'Asset vulnerabilities are identified and documented',                       'Identify',        'High'),
    (@fw_nist, 'PR.AA-01', 'Identity Management',                    'Identities and credentials for authorized users, services, and hardware',   'Protect',         'High'),
    (@fw_nist, 'PR.AA-03', 'Multi-Factor Authentication',            'Users, services, and hardware are authenticated',                            'Protect',         'High'),
    (@fw_nist, 'PR.AA-05', 'Access Permissions',                     'Access permissions, entitlements, and authorizations defined',                'Protect',         'High'),
    (@fw_nist, 'PR.AT-01', 'Awareness and Training',                 'Security awareness education is provided to the workforce',                  'Protect',         'Medium'),
    (@fw_nist, 'PR.DS-01', 'Data Security — At Rest',                'Data-at-rest is protected',                                                 'Protect',         'High'),
    (@fw_nist, 'PR.DS-02', 'Data Security — In Transit',             'Data-in-transit is protected',                                               'Protect',         'High'),
    (@fw_nist, 'PR.DS-10', 'Data Security — DLP',                    'Measures to prevent data leaks',                                             'Protect',         'High'),
    (@fw_nist, 'PR.IR-01', 'Technology Infrastructure Resilience',    'Protect network integrity incorporating network segmentation',              'Protect',         'Medium'),
    (@fw_nist, 'PR.PS-01', 'Platform Security — Configuration',      'Configuration management practices are established and applied',             'Protect',         'High'),
    (@fw_nist, 'PR.PS-02', 'Platform Security — Software Maintained', 'Software is maintained, replaced, and removed',                            'Protect',         'High'),
    (@fw_nist, 'DE.CM-01', 'Continuous Monitoring — Networks',        'Networks and network services are monitored',                               'Detect',          'High'),
    (@fw_nist, 'DE.CM-06', 'Continuous Monitoring — Computing',       'Computing hardware and software, runtime environments are monitored',       'Detect',          'High'),
    (@fw_nist, 'DE.AE-02', 'Adverse Event Analysis',                  'Anomalies, indicators of compromise detected and analyzed',                'Detect',          'High'),
    (@fw_nist, 'RS.MA-01', 'Incident Management — Response Plan',     'Incident response plan is executed',                                       'Respond',         'High'),
    (@fw_nist, 'RC.RP-01', 'Recovery — Recovery Plan Executed',        'Recovery plan is executed during or after a cybersecurity incident',        'Recover',         'Medium'),

    -- ── SOC 2 (Trust Services Criteria) ────────────────────────
    (@fw_soc2, 'CC1.1',  'COSO Principle 1 — Integrity and Ethics',  'The entity demonstrates commitment to integrity and ethical values',          'Common Criteria',    'Medium'),
    (@fw_soc2, 'CC5.1',  'COSO Principle 10 — Control Activities',    'The entity selects and develops control activities',                         'Common Criteria',    'High'),
    (@fw_soc2, 'CC6.1',  'Logical and Physical Access Controls',      'Logical access security software, infrastructure and architectures',         'Logical Access',     'High'),
    (@fw_soc2, 'CC6.2',  'User Registration and Authorization',       'System registration and authorization for new users',                        'Logical Access',     'High'),
    (@fw_soc2, 'CC6.3',  'Role-Based Access and Least Privilege',     'Role-based access and least privilege for system components',                 'Logical Access',     'High'),
    (@fw_soc2, 'CC6.6',  'Boundary Protection',                       'System boundaries and access points protected',                              'Logical Access',     'High'),
    (@fw_soc2, 'CC6.7',  'Restrict Data Transmission',                'Restrict transmission, movement, and removal of information',                 'Logical Access',     'High'),
    (@fw_soc2, 'CC6.8',  'Malicious Software Prevention',             'Prevent, detect, and act upon introduction of unauthorized software',        'Logical Access',     'High'),
    (@fw_soc2, 'CC7.1',  'Detection and Monitoring',                   'Detect configuration changes that could introduce vulnerabilities',         'System Operations',  'High'),
    (@fw_soc2, 'CC7.2',  'Monitoring for Anomalies',                   'Monitor system components for anomalies indicative of malicious acts',      'System Operations',  'High'),
    (@fw_soc2, 'CC7.3',  'Evaluate Security Events',                   'Evaluate events to determine if they are security incidents',               'System Operations',  'High'),
    (@fw_soc2, 'CC7.4',  'Incident Response',                          'Respond to identified security incidents',                                  'System Operations',  'High'),
    (@fw_soc2, 'CC8.1',  'Change Management',                          'Changes to infrastructure, data, software, and procedures are authorized',  'Change Management',  'Medium'),
    (@fw_soc2, 'CC9.1',  'Risk Mitigation',                            'Risk mitigation activities are planned and executed',                        'Risk Mitigation',    'Medium'),
    (@fw_soc2, 'A1.1',   'Availability — Meet Objectives',             'System availability meets the principal service commitments',                'Availability',       'Medium'),
    (@fw_soc2, 'C1.1',   'Confidentiality — Identify and Maintain',    'Identify and maintain confidential information',                            'Confidentiality',    'High'),
    (@fw_soc2, 'C1.2',   'Confidentiality — Disposal',                 'Dispose of confidential information as retention policies require',         'Confidentiality',    'Medium'),
    (@fw_soc2, 'PI1.1',  'Processing Integrity',                       'The entity obtains/generates, uses, and communicates info to meet objectives', 'Processing Integrity', 'Medium'),

    -- ── PCI DSS v4.0 ───────────────────────────────────────────
    (@fw_pci, '1.2.1',  'Network Security Controls — Restrict Traffic',  'Inbound and outbound traffic is restricted to that which is necessary',     'Network Security',       'High'),
    (@fw_pci, '2.2.1',  'System Configuration Standards',                'Configuration standards are developed and applied',                          'Secure Configuration',   'High'),
    (@fw_pci, '3.4.1',  'Protect Stored Account Data',                   'PAN is secured with strong cryptography wherever it is stored',              'Data Protection',        'High'),
    (@fw_pci, '5.2.1',  'Anti-Malware Mechanisms Deployed',              'An anti-malware solution is deployed on all components',                      'Malware Protection',     'High'),
    (@fw_pci, '6.3.1',  'Security Vulnerabilities Identified and Managed', 'Security vulnerabilities are identified and addressed',                    'Vulnerability Management','High'),
    (@fw_pci, '7.2.1',  'Access Control — Appropriate Access',           'Access is appropriately defined and assigned',                                'Access Control',         'High'),
    (@fw_pci, '7.2.2',  'Access Control — Least Privilege',              'Access is assigned based on classification and function',                     'Access Control',         'High'),
    (@fw_pci, '8.3.1',  'MFA for Non-Console Admin Access',              'MFA is implemented for all non-console administrative access',                'Authentication',         'High'),
    (@fw_pci, '8.3.6',  'Strong Passwords',                              'Passwords/passphrases meet defined minimum complexity',                      'Authentication',         'High'),
    (@fw_pci, '10.2.1', 'Audit Logs Capture Details',                    'Audit logs capture all elements for each auditable event',                    'Monitoring',             'High'),
    (@fw_pci, '10.4.1', 'Audit Logs are Reviewed',                       'Audit logs are reviewed at least once daily',                                'Monitoring',             'High'),
    (@fw_pci, '11.3.1', 'Vulnerability Scans',                            'Internal vulnerability scans are performed at least quarterly',              'Testing',                'High'),
    (@fw_pci, '12.1.1', 'Information Security Policy',                    'An overall information security policy is established and published',        'Policy',                 'Medium'),

    -- ── CIS Microsoft 365 Foundations v3.1 ─────────────────────
    (@fw_cis, 'CIS-1.1',  'Ensure MFA is enabled for all users',                  'Multi-factor authentication for all user accounts',                    'Identity',           'High'),
    (@fw_cis, 'CIS-1.2',  'Ensure Security Defaults is disabled (use CA)',         'Replace security defaults with Conditional Access policies',           'Identity',           'High'),
    (@fw_cis, 'CIS-1.3',  'Ensure legacy authentication is blocked',              'Block legacy authentication protocols via Conditional Access',          'Identity',           'High'),
    (@fw_cis, 'CIS-1.4',  'Ensure privileged accounts use PIM',                   'Use PIM for just-in-time activation of admin roles',                    'Identity',           'High'),
    (@fw_cis, 'CIS-1.5',  'Ensure no more than 5 Global Administrators',          'Limit permanent Global Administrator count',                            'Identity',           'High'),
    (@fw_cis, 'CIS-2.1',  'Ensure DLP policies are enabled',                      'Configure DLP policies for Exchange, SharePoint, Teams',                'Data Protection',    'High'),
    (@fw_cis, 'CIS-2.2',  'Ensure sensitivity labels are published',              'Publish and deploy sensitivity labels across tenant',                    'Data Protection',    'High'),
    (@fw_cis, 'CIS-3.1',  'Ensure Defender for Endpoint is enabled',              'Enable Microsoft Defender for Endpoint across all devices',              'Endpoint Security',  'High'),
    (@fw_cis, 'CIS-3.2',  'Ensure device compliance policies exist',              'Create device compliance policies for all managed platforms',            'Endpoint Security',  'High'),
    (@fw_cis, 'CIS-3.3',  'Ensure BitLocker is enforced on Windows devices',      'Enforce disk encryption via Intune compliance policy',                   'Endpoint Security',  'High'),
    (@fw_cis, 'CIS-4.1',  'Ensure audit log search is enabled',                   'Ensure unified audit logging is enabled in Purview',                     'Monitoring',         'High'),
    (@fw_cis, 'CIS-5.1',  'Ensure external sharing is restricted in SharePoint',  'Restrict external sharing to authenticated guests only',                 'Data Protection',    'High'),
    (@fw_cis, 'CIS-5.2',  'Ensure guest user access is reviewed quarterly',       'Implement access reviews for guest users',                               'Identity',           'Medium'),
    (@fw_cis, 'CIS-6.1',  'Ensure app consent is restricted to admin approval',   'Require admin consent for third-party app permissions',                   'Application Security','High'),

    -- ── CMMC Level 2 (NIST SP 800-171 controls) ────────────────
    (@fw_cmmc, 'AC.L2-3.1.1',  'Limit System Access',                   'Limit information system access to authorized users',                      'Access Control',           'High'),
    (@fw_cmmc, 'AC.L2-3.1.2',  'Limit Transaction Access',              'Limit system access to the types of transactions authorized',              'Access Control',           'High'),
    (@fw_cmmc, 'AC.L2-3.1.5',  'Least Privilege',                        'Employ the principle of least privilege, including admin functions',       'Access Control',           'High'),
    (@fw_cmmc, 'AC.L2-3.1.7',  'Privileged Functions',                   'Prevent non-privileged users from executing privileged functions',         'Access Control',           'High'),
    (@fw_cmmc, 'IA.L2-3.5.3',  'Multifactor Authentication',             'Use multifactor authentication for local and network access',              'Identification & Auth',    'High'),
    (@fw_cmmc, 'IA.L2-3.5.4',  'Replay-Resistant Authentication',        'Employ replay-resistant authentication mechanisms',                        'Identification & Auth',    'High'),
    (@fw_cmmc, 'AU.L2-3.3.1',  'System Auditing',                        'Create and retain system audit logs to enable monitoring',                  'Audit & Accountability',   'High'),
    (@fw_cmmc, 'AU.L2-3.3.2',  'User Accountability',                    'Ensure actions can be traced to individual users',                          'Audit & Accountability',   'High'),
    (@fw_cmmc, 'CM.L2-3.4.1',  'System Baselining',                      'Establish and maintain baseline configurations',                            'Configuration Mgmt',       'High'),
    (@fw_cmmc, 'CM.L2-3.4.2',  'Security Config Enforcement',            'Enforce security configuration settings for IT products',                   'Configuration Mgmt',       'High'),
    (@fw_cmmc, 'MP.L2-3.8.1',  'Media Protection',                       'Protect system media containing CUI',                                       'Media Protection',         'High'),
    (@fw_cmmc, 'PE.L2-3.10.1', 'Physical Access',                        'Limit physical access to organizational information systems',               'Physical Protection',      'Medium'),
    (@fw_cmmc, 'RA.L2-3.11.1', 'Risk Assessment',                        'Periodically assess risk to organizational operations and assets',          'Risk Assessment',          'High'),
    (@fw_cmmc, 'SC.L2-3.13.1', 'Boundary Protection',                    'Monitor, control, and protect communications at system boundaries',        'System & Comm Protection', 'High'),
    (@fw_cmmc, 'SC.L2-3.13.8', 'Data in Transit',                        'Implement cryptographic mechanisms to prevent unauthorized disclosure during transmission', 'System & Comm Protection', 'High'),
    (@fw_cmmc, 'SC.L2-3.13.11','CUI Encryption',                          'Employ FIPS-validated cryptography when used to protect CUI',              'System & Comm Protection', 'High'),
    (@fw_cmmc, 'SI.L2-3.14.1', 'Flaw Remediation',                        'Identify, report, and correct information system flaws in a timely manner', 'System & Info Integrity', 'High'),
    (@fw_cmmc, 'SI.L2-3.14.2', 'Malicious Code Protection',               'Provide protection from malicious code at appropriate locations',          'System & Info Integrity', 'High'),
    (@fw_cmmc, 'SI.L2-3.14.6', 'Security Alerts',                          'Monitor organizational information systems for security alerts',          'System & Info Integrity', 'High'),
    (@fw_cmmc, 'IR.L2-3.6.1',  'Incident Handling',                        'Establish operational incident-handling capability',                      'Incident Response',       'High')
) AS src(framework_id, control_code, title, description, category, priority)
)
INSERT INTO cloud_assessment_framework_controls
    (framework_id, control_code, title, description, category, priority)
SELECT src.framework_id, src.control_code, src.title, src.description, src.category, src.priority
FROM controls_seed src
WHERE NOT EXISTS (
    SELECT 1 FROM cloud_assessment_framework_controls fc
    WHERE fc.framework_id = src.framework_id AND fc.control_code = src.control_code
);

-- ============================================================
-- 3. FINDING → CONTROL MAPPINGS
--    Maps (area, service, feature) from cloud_assessment_findings
--    to framework controls. Each mapping has a coverage level.
-- ============================================================

-- Build mappings via INSERT ... WHERE NOT EXISTS pattern.
-- We join on framework_id + control_code to resolve framework_control_id.

;WITH mapping_seed AS (
    SELECT * FROM (VALUES
    -- ── Identity → HIPAA ──
    ('identity', 'entra', 'Microsoft Entra ID P1',                '164.312(a)(1)',     'HIPAA',   'full',    'MFA + CA policies enforce access control for ePHI systems'),
    ('identity', 'entra', 'Microsoft Entra ID P1',                '164.312(d)',        'HIPAA',   'full',    'MFA enrollment = person authentication'),
    ('identity', 'entra', 'Microsoft Entra ID P2',                '164.312(b)',        'HIPAA',   'partial', 'Access reviews + PIM provide audit trail for privileged access'),
    ('identity', 'entra', 'Microsoft Entra ID P2',                '164.308(a)(4)',     'HIPAA',   'partial', 'Access reviews govern information access management'),
    ('identity', 'entra', 'Conditional Access',                   '164.312(a)(1)',     'HIPAA',   'full',    'CA policies enforce technical access control'),
    ('identity', 'entra', 'Conditional Access',                   '164.312(a)(2)(iii)','HIPAA',   'partial', 'Session controls provide automatic logoff'),
    ('identity', 'entra', 'Entra Identity Protection',            '164.308(a)(1)',     'HIPAA',   'partial', 'Risk detection supports security management process'),
    ('identity', 'entra', 'Entra Identity Protection',            '164.308(a)(6)',     'HIPAA',   'partial', 'Risky user detection supports incident procedures'),
    ('identity', 'entra', 'User Lifecycle Hygiene',               '164.308(a)(3)',     'HIPAA',   'partial', 'Stale account cleanup supports workforce security'),
    ('identity', 'entra', 'Administrator MFA',                    '164.312(d)',        'HIPAA',   'full',    'Admin MFA = person authentication for privileged users'),

    -- ── Identity → ISO 27001 ──
    ('identity', 'entra', 'Microsoft Entra ID P1',                'A.8.5',             'ISO27001','full',    'MFA + passwordless = secure authentication'),
    ('identity', 'entra', 'Microsoft Entra ID P2',                'A.5.15',            'ISO27001','full',    'PIM + access reviews = access control'),
    ('identity', 'entra', 'Conditional Access',                   'A.8.3',             'ISO27001','full',    'CA policies enforce information access restriction'),
    ('identity', 'entra', 'Microsoft Entra ID Governance',        'A.5.15',            'ISO27001','full',    'PIM + reviews = access control lifecycle'),
    ('identity', 'entra', 'Service Principal Credentials',        'A.5.17',            'ISO27001','full',    'Credential rotation = authentication information management'),
    ('identity', 'entra', 'OAuth Consent',                        'A.5.23',            'ISO27001','partial', 'App consent governance supports cloud service security'),
    ('identity', 'entra', 'Device Security Posture',              'A.8.1',             'ISO27001','full',    'BitLocker + compliance = user endpoint device protection'),

    -- ── Identity → NIST CSF ──
    ('identity', 'entra', 'Microsoft Entra ID P1',                'PR.AA-03',          'NIST_CSF','full',    'MFA = multi-factor authentication'),
    ('identity', 'entra', 'Microsoft Entra ID P2',                'PR.AA-05',          'NIST_CSF','full',    'PIM + access reviews = access permissions governance'),
    ('identity', 'entra', 'Conditional Access',                   'PR.AA-01',          'NIST_CSF','full',    'CA = identity management and credential policies'),
    ('identity', 'entra', 'Entra Identity Protection',            'DE.AE-02',          'NIST_CSF','full',    'Risk detection = adverse event analysis'),
    ('identity', 'entra', 'Administrator MFA',                    'PR.AA-03',          'NIST_CSF','full',    'Admin MFA = multi-factor authentication for privileged'),
    ('identity', 'entra', 'User Lifecycle Hygiene',               'PR.AA-05',          'NIST_CSF','partial', 'Stale account cleanup = access permissions hygiene'),

    -- ── Identity → SOC 2 ──
    ('identity', 'entra', 'Microsoft Entra ID P1',                'CC6.1',             'SOC2',    'full',    'MFA + CA = logical access security'),
    ('identity', 'entra', 'Microsoft Entra ID P2',                'CC6.2',             'SOC2',    'full',    'Access reviews = user registration and authorization'),
    ('identity', 'entra', 'Microsoft Entra ID P2',                'CC6.3',             'SOC2',    'full',    'PIM = role-based access and least privilege'),
    ('identity', 'entra', 'Conditional Access',                   'CC6.1',             'SOC2',    'full',    'CA policies = logical access controls'),
    ('identity', 'entra', 'OAuth Consent',                        'CC6.1',             'SOC2',    'partial', 'App consent restrictions = logical access security'),

    -- ── Identity → PCI DSS ──
    ('identity', 'entra', 'Microsoft Entra ID P1',                '8.3.1',             'PCI_DSS', 'full',    'MFA for admin access'),
    ('identity', 'entra', 'Conditional Access',                   '7.2.1',             'PCI_DSS', 'full',    'CA = appropriate access definition'),
    ('identity', 'entra', 'Microsoft Entra ID P2',                '7.2.2',             'PCI_DSS', 'full',    'PIM = least privilege access'),
    ('identity', 'entra', 'Administrator MFA',                    '8.3.1',             'PCI_DSS', 'full',    'Admin MFA meets PCI MFA requirement'),

    -- ── Identity → CIS ──
    ('identity', 'entra', 'Microsoft Entra ID P1',                'CIS-1.1',           'CIS',     'full',    'MFA for all users'),
    ('identity', 'entra', 'Microsoft Entra ID P1',                'CIS-1.3',           'CIS',     'full',    'Legacy auth blocking'),
    ('identity', 'entra', 'Conditional Access',                   'CIS-1.2',           'CIS',     'full',    'CA policies replace security defaults'),
    ('identity', 'entra', 'Microsoft Entra ID P2',                'CIS-1.4',           'CIS',     'full',    'PIM for privileged accounts'),
    ('identity', 'entra', 'Administrator MFA',                    'CIS-1.5',           'CIS',     'full',    'Global Admin count limit'),
    ('identity', 'entra', 'OAuth Consent',                        'CIS-6.1',           'CIS',     'full',    'Admin consent for apps'),
    ('identity', 'entra', 'Microsoft Entra ID P2',                'CIS-5.2',           'CIS',     'full',    'Guest access reviews'),

    -- ── Identity → CMMC ──
    ('identity', 'entra', 'Microsoft Entra ID P1',                'IA.L2-3.5.3',       'CMMC_L2', 'full',    'MFA = multifactor authentication'),
    ('identity', 'entra', 'Conditional Access',                   'AC.L2-3.1.1',       'CMMC_L2', 'full',    'CA = limit system access'),
    ('identity', 'entra', 'Microsoft Entra ID P2',                'AC.L2-3.1.5',       'CMMC_L2', 'full',    'PIM = least privilege'),
    ('identity', 'entra', 'Microsoft Entra ID P2',                'AU.L2-3.3.1',       'CMMC_L2', 'partial', 'Access reviews = system auditing support'),
    ('identity', 'entra', 'Entra Identity Protection',            'SI.L2-3.14.6',      'CMMC_L2', 'full',    'Risk detection = security alerts'),

    -- ── Endpoint → Various frameworks ──
    ('endpoint', 'intune',            'Device Compliance Policies',   'A.8.1',             'ISO27001','full',    'Device compliance = endpoint protection'),
    ('endpoint', 'intune',            'Device Compliance Policies',   'PR.PS-01',          'NIST_CSF','full',    'Compliance policies = configuration management'),
    ('endpoint', 'intune',            'Device Encryption',            '164.312(a)(2)(iv)', 'HIPAA',   'full',    'BitLocker = encryption of ePHI at rest'),
    ('endpoint', 'intune',            'Device Encryption',            'PR.DS-01',          'NIST_CSF','full',    'Disk encryption = data-at-rest protection'),
    ('endpoint', 'intune',            'Device Encryption',            'MP.L2-3.8.1',       'CMMC_L2', 'full',    'Encryption = media protection'),
    ('endpoint', 'intune',            'Device Encryption',            'CC6.7',             'SOC2',    'partial', 'Encryption restricts data transmission from stolen device'),
    ('endpoint', 'intune',            'Device Encryption',            'CIS-3.3',           'CIS',     'full',    'BitLocker enforcement'),
    ('endpoint', 'intune',            'Compliance Rate',              'CC7.1',             'SOC2',    'partial', 'Compliance monitoring = detection and monitoring'),
    ('endpoint', 'intune',            'BYOD App Protection',          'A.8.1',             'ISO27001','partial', 'App protection on BYOD = endpoint device controls'),
    ('endpoint', 'defender-endpoint', 'Defender for Endpoint',        'A.8.7',             'ISO27001','full',    'EDR = protection against malware'),
    ('endpoint', 'defender-endpoint', 'Defender for Endpoint',        'CIS-3.1',           'CIS',     'full',    'Defender for Endpoint enabled'),
    ('endpoint', 'defender-endpoint', 'Defender for Endpoint',        'SI.L2-3.14.2',      'CMMC_L2', 'full',    'EDR = malicious code protection'),
    ('endpoint', 'defender-endpoint', 'Defender for Endpoint',        '5.2.1',             'PCI_DSS', 'full',    'EDR = anti-malware deployed'),
    ('endpoint', 'defender-endpoint', 'Exposure Score',               'ID.RA-01',          'NIST_CSF','full',    'Exposure score = vulnerability identification'),
    ('endpoint', 'defender-endpoint', 'Vulnerability Posture',        '6.3.1',             'PCI_DSS', 'full',    'Vulnerability management'),
    ('endpoint', 'defender-endpoint', 'Vulnerability Posture',        'A.8.8',             'ISO27001','full',    'Technical vulnerability management'),
    ('endpoint', 'defender-endpoint', 'Vulnerability Posture',        'SI.L2-3.14.1',      'CMMC_L2', 'full',    'Flaw remediation'),
    ('endpoint', 'defender-endpoint', 'Vulnerability Posture',        'RA.L2-3.11.1',      'CMMC_L2', 'partial', 'Vulnerability assessment = risk assessment'),
    ('endpoint', 'defender-endpoint', 'Vulnerability Posture',        '11.3.1',            'PCI_DSS', 'full',    'Vulnerability scans'),
    ('endpoint', 'defender-endpoint', 'High-Risk Machines',           'DE.CM-06',          'NIST_CSF','full',    'High-risk machine monitoring'),
    ('endpoint', 'intune',            'Device Compliance Policies',   'CIS-3.2',           'CIS',     'full',    'Compliance policies exist'),

    -- ── Data → Various frameworks ──
    ('data', 'purview',    'Sensitivity Label Deployment', 'A.8.11',            'ISO27001','full',    'Sensitivity labels = data masking/classification'),
    ('data', 'purview',    'Sensitivity Label Deployment', 'PR.DS-01',          'NIST_CSF','partial', 'Labels support data-at-rest protection via classification'),
    ('data', 'purview',    'Sensitivity Label Deployment', 'C1.1',              'SOC2',    'full',    'Labels identify and maintain confidential information'),
    ('data', 'purview',    'Sensitivity Label Deployment', 'CIS-2.2',           'CIS',     'full',    'Sensitivity labels published'),
    ('data', 'purview',    'DLP Policy Posture',           'A.8.12',            'ISO27001','full',    'DLP = data leakage prevention'),
    ('data', 'purview',    'DLP Policy Posture',           'PR.DS-10',          'NIST_CSF','full',    'DLP = data leak prevention measures'),
    ('data', 'purview',    'DLP Policy Posture',           'CC6.7',             'SOC2',    'full',    'DLP restricts data transmission'),
    ('data', 'purview',    'DLP Policy Posture',           'CIS-2.1',           'CIS',     'full',    'DLP policies enabled'),
    ('data', 'purview',    'DLP Policy Posture',           '3.4.1',             'PCI_DSS', 'partial', 'DLP can protect stored account data patterns'),
    ('data', 'purview',    'AIP/DLP Licensing',            '164.312(a)(2)(iv)', 'HIPAA',   'partial', 'AIP licensing enables ePHI encryption'),
    ('data', 'purview',    'eDiscovery',                   '164.312(b)',        'HIPAA',   'partial', 'eDiscovery supports audit controls'),
    ('data', 'purview',    'Advanced Audit',               '164.312(b)',        'HIPAA',   'full',    'Advanced audit = comprehensive audit controls'),
    ('data', 'purview',    'Advanced Audit',               'A.8.15',            'ISO27001','full',    'Advanced audit = logging'),
    ('data', 'purview',    'Advanced Audit',               'CIS-4.1',           'CIS',     'full',    'Audit log enabled'),
    ('data', 'purview',    'Advanced Audit',               '10.2.1',            'PCI_DSS', 'full',    'Audit logs capture details'),
    ('data', 'purview',    'Advanced Audit',               'AU.L2-3.3.1',       'CMMC_L2', 'full',    'System auditing'),
    ('data', 'purview',    'Advanced Audit',               'AU.L2-3.3.2',       'CMMC_L2', 'full',    'User accountability'),
    ('data', 'purview',    'Insider Risk Management',      '164.308(a)(1)',     'HIPAA',   'partial', 'Insider risk supports security management'),
    ('data', 'purview',    'Insider Risk Management',      'DE.CM-01',          'NIST_CSF','partial', 'Insider risk = continuous monitoring'),
    ('data', 'sharepoint', 'SharePoint Oversharing',       'CC6.3',             'SOC2',    'partial', 'Oversharing check validates least privilege'),
    ('data', 'sharepoint', 'SharePoint Oversharing',       'CIS-5.1',           'CIS',     'full',    'External sharing restriction'),
    ('data', 'sharepoint', 'External Guest Users',         '164.308(a)(4)',     'HIPAA',   'partial', 'Guest access review supports information access management'),
    ('data', 'sharepoint', 'External Guest Users',         'CIS-5.2',           'CIS',     'partial', 'Guest access review'),
    ('data', 'sharepoint', 'Sensitivity Label Coverage',   'A.8.11',            'ISO27001','partial', 'Label coverage = data masking applied'),
    ('data', 'purview',    'Retention Labels',             'C1.2',              'SOC2',    'full',    'Retention = confidentiality disposal'),
    ('data', 'purview',    'Retention Labels',             'A.8.10',            'ISO27001','full',    'Retention labels = information deletion'),

    -- ── Productivity → Various frameworks ──
    ('productivity', 'licensing', 'Wasted Licenses',       'CC9.1',             'SOC2',    'partial', 'License optimization = risk mitigation (cost)'),
    ('productivity', 'licensing', 'Wasted Copilot Licenses','CC9.1',            'SOC2',    'partial', 'Copilot license waste = risk mitigation (cost)'),

    -- ── Azure → Various frameworks ──
    ('azure', 'storage',         'Public Blob Access',          'A.8.3',             'ISO27001','full',    'Public blob check = information access restriction'),
    ('azure', 'storage',         'Public Blob Access',          'CC6.6',             'SOC2',    'full',    'Public blob = boundary protection'),
    ('azure', 'storage',         'Public Blob Access',          'SC.L2-3.13.1',      'CMMC_L2', 'full',    'Public blob = boundary protection'),
    ('azure', 'storage',         'Secure Transfer Required',    '164.312(e)(2)(ii)', 'HIPAA',   'full',    'HTTPS enforcement = encryption in transmission'),
    ('azure', 'storage',         'Secure Transfer Required',    'PR.DS-02',          'NIST_CSF','full',    'HTTPS = data-in-transit protection'),
    ('azure', 'storage',         'Secure Transfer Required',    'SC.L2-3.13.8',      'CMMC_L2', 'full',    'HTTPS = data in transit encryption'),
    ('azure', 'keyvault',        'Key Vault Soft-Delete',       'A.8.24',            'ISO27001','partial', 'Key Vault protection = cryptography/key management'),
    ('azure', 'network',         'NSG Unrestricted Inbound',    '1.2.1',             'PCI_DSS', 'full',    'NSG = restrict inbound traffic'),
    ('azure', 'network',         'NSG Unrestricted Inbound',    'A.8.20',            'ISO27001','full',    'NSG = network security'),
    ('azure', 'network',         'NSG Unrestricted Inbound',    'SC.L2-3.13.1',      'CMMC_L2', 'full',    'NSG = boundary protection'),
    ('azure', 'compute',         'VM OS Disk Encryption',       'PR.DS-01',          'NIST_CSF','full',    'VM encryption = data-at-rest'),
    ('azure', 'compute',         'VM OS Disk Encryption',       'SC.L2-3.13.11',     'CMMC_L2', 'full',    'VM encryption = CUI encryption'),
    ('azure', 'compute',         'VM OS Disk Encryption',       '164.312(a)(2)(iv)', 'HIPAA',   'full',    'VM encryption = ePHI at rest'),
    ('azure', 'defender-cloud',  'Defender for Cloud Activation','DE.CM-06',          'NIST_CSF','full',    'Defender for Cloud = computing hardware monitoring'),
    ('azure', 'defender-cloud',  'Defender for Cloud Activation','CC7.2',             'SOC2',    'full',    'Defender = monitoring for anomalies'),
    ('azure', 'defender-cloud',  'Secure Score',                 'PR.PS-01',          'NIST_CSF','partial', 'Secure score reflects configuration management posture'),
    ('azure', 'policy',          'Policy Compliance',            'A.8.9',             'ISO27001','full',    'Azure Policy = configuration management'),
    ('azure', 'policy',          'Policy Compliance',            'CM.L2-3.4.1',       'CMMC_L2', 'full',    'Policy = system baselining'),
    ('azure', 'policy',          'Policy Compliance',            'CM.L2-3.4.2',       'CMMC_L2', 'full',    'Policy = security config enforcement'),
    ('azure', 'policy',          'Policy Compliance',            '2.2.1',             'PCI_DSS', 'full',    'Policy = configuration standards')
) AS src(area, service, feature, control_code, fw_code, coverage, rationale)
)
INSERT INTO cloud_assessment_finding_control_mappings
    (area, service, feature, framework_control_id, coverage, rationale)
SELECT
    src.area, src.service, src.feature,
    fc.id,
    src.coverage, src.rationale
FROM mapping_seed src
INNER JOIN cloud_assessment_frameworks fw ON fw.code = src.fw_code
INNER JOIN cloud_assessment_framework_controls fc ON fc.framework_id = fw.id AND fc.control_code = src.control_code
WHERE NOT EXISTS (
    SELECT 1 FROM cloud_assessment_finding_control_mappings m
    WHERE m.area = src.area AND m.service = src.service AND m.feature = src.feature
      AND m.framework_control_id = fc.id
);
