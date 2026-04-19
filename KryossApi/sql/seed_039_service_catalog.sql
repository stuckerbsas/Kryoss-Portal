-- seed_039_service_catalog.sql
-- 14 remediation service categories

MERGE service_catalog AS tgt
USING (VALUES
    ('disk_encryption',    'Disk Encryption Rollout',            'Cifrado de Discos',                   'per_machine',       0.50, 'machines.Bitlocker',                     NULL, 'critical', 1),
    ('laps_deploy',        'LAPS Deployment',                   'Despliegue de LAPS',                  'per_machine',       0.25, 'ad_hygiene.NoLAPS',                      NULL, 'critical', 2),
    ('endpoint_protection','Endpoint Protection Enablement',    'Habilitación de Protección Endpoint',  'per_machine',       0.50, 'machines.DefenderEnabled',               NULL, 'critical', 3),
    ('patch_management',   'Legacy OS Migration',               'Migración de SO Obsoleto',            'per_machine',       1.50, 'machines.OsName',                        'Windows 7|Windows 8|Server 2008|Server 2003', 'critical', 4),
    ('protocol_hardening', 'Protocol Hardening (SMBv1/NTLMv1)', 'Hardening de Protocolos (SMBv1/NTLMv1)', 'per_machine',   1.00, 'control_results.protocol',               NULL, 'high', 5),
    ('password_policy',    'Password Policy Enforcement',       'Aplicación de Política de Contraseñas', 'per_domain',      2.00, 'ad_hygiene.PwdNeverExpire',              NULL, 'high', 6),
    ('privileged_access',  'Privileged Access Review',          'Revisión de Acceso Privilegiado',     'per_account',       0.50, 'ad_hygiene.PrivilegedAccounts',          NULL, 'high', 7),
    ('rdp_hardening',      'RDP Hardening',                     'Hardening de RDP',                    'per_machine',       0.50, 'machine_ports.3389',                     NULL, 'high', 8),
    ('m365_security',      'M365 Security Hardening',           'Hardening de Seguridad M365',         'per_tenant',        4.00, 'cloud_assessment_findings.identity',     NULL, 'high', 9),
    ('azure_hardening',    'Azure Infrastructure Hardening',    'Hardening de Infraestructura Azure',  'per_subscription',  6.00, 'cloud_assessment_findings.azure',        NULL, 'high', 10),
    ('firewall_hardening', 'Firewall Hardening',                'Hardening de Firewall',               'per_machine',       0.75, 'control_results.firewall',               NULL, 'medium', 11),
    ('audit_logging',      'Audit & Logging Configuration',     'Configuración de Auditoría y Logs',   'per_domain',        3.00, 'control_results.auditpol',               NULL, 'medium', 12),
    ('cert_hygiene',       'Certificate Remediation',           'Remediación de Certificados',         'per_cert',          0.25, 'control_results.certstore',              NULL, 'medium', 13),
    ('ad_restructuring',   'AD Infrastructure Upgrade',         'Actualización de Infraestructura AD', 'per_domain',        8.00, 'ad_hygiene.DomainFunctionalLevel',       NULL, 'medium', 14)
) AS src (category_code, name_en, name_es, unit_type, base_hours, trigger_source, trigger_filter, severity, sort_order)
ON tgt.category_code = src.category_code
WHEN NOT MATCHED THEN INSERT (category_code, name_en, name_es, unit_type, base_hours, trigger_source, trigger_filter, severity, sort_order)
VALUES (src.category_code, src.name_en, src.name_es, src.unit_type, src.base_hours, src.trigger_source, src.trigger_filter, src.severity, src.sort_order);
GO
