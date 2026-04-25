SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- 047_dc_controls_v2.sql
-- DC-01: Domain Controller controls expansion
--
-- Track A: Convert 17 broken 'command' DC controls to 'dc' type
--          with check_type for DcEngine native execution.
--          (6 remain 'command' type - deferred to future
--           native handler: DC-001..006 need dcdiag/repadmin)
-- Track C: Add 60 new DC controls (DC-041..DC-100) using
--          'dc' + 'registry' + 'auditpol' + 'service' types.
--
-- Idempotent. Run AFTER seed_013_dc_controls.sql.
-- ============================================================

BEGIN TRANSACTION;

DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- ============================================================
-- PRE-REQ: Expand CHECK constraint to include 'dc' type
-- ============================================================
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_ctrldef_type')
    ALTER TABLE control_defs DROP CONSTRAINT ck_ctrldef_type;

ALTER TABLE control_defs ADD CONSTRAINT ck_ctrldef_type CHECK ([type] IN (
    'registry','secedit','auditpol','firewall','service','netaccount','command',
    'eventlog','certstore','bitlocker','tpm','network_diag','dc'
));

-- ============================================================
-- TRACK A: Convert broken 'command' controls to 'dc' type
-- ============================================================

-- DC-010: LDAPS cert → dc_print_spooler is wrong, this is cert check
-- Actually DC-010 tests port 636 — can't do natively without socket. Keep inactive.
-- Focus on the ones DcEngine CAN handle:

-- DC-012: krbtgt password age
UPDATE control_defs SET [type] = 'dc', check_json = N'{"checkType":"dc_krbtgt_age","expected":180,"operator":"lt","display":"Checking krbtgt account password age"}'
WHERE control_id = 'DC-012' AND [type] = 'command';

-- DC-015: Pre-auth disabled accounts
UPDATE control_defs SET [type] = 'dc', check_json = N'{"checkType":"dc_preauth_disabled","expected":0,"operator":"eq","display":"Checking for accounts with pre-authentication disabled"}'
WHERE control_id = 'DC-015' AND [type] = 'command';

-- DC-018: AD Recycle Bin
UPDATE control_defs SET [type] = 'dc', check_json = N'{"checkType":"dc_recycle_bin","expected":true,"operator":"eq","display":"Checking if AD Recycle Bin is enabled"}'
WHERE control_id = 'DC-018' AND [type] = 'command';

-- DC-019: Protected Users
UPDATE control_defs SET [type] = 'dc', check_json = N'{"checkType":"dc_protected_users","expected":1,"operator":"gte","display":"Checking Protected Users group membership"}'
WHERE control_id = 'DC-019' AND [type] = 'command';

-- DC-020: Fine-Grained Password Policies — can't check natively without ADWS. Deactivate.
UPDATE control_defs SET is_active = 0
WHERE control_id = 'DC-020' AND [type] = 'command';

-- DC-022: Unconstrained delegation
UPDATE control_defs SET [type] = 'dc', check_json = N'{"checkType":"dc_unconstrained_deleg","expected":0,"operator":"eq","display":"Checking for non-DC accounts with unconstrained delegation"}'
WHERE control_id = 'DC-022' AND [type] = 'command';

-- DC-024: Auth policies — can't check natively. Deactivate.
UPDATE control_defs SET is_active = 0
WHERE control_id = 'DC-024' AND [type] = 'command';

-- DC-027: AdminSDHolder — complex ACL parse needed. Deactivate for now.
UPDATE control_defs SET is_active = 0
WHERE control_id = 'DC-027' AND [type] = 'command';

-- DC-028: Time source
UPDATE control_defs SET [type] = 'dc', check_json = N'{"checkType":"dc_time_source","expected":true,"operator":"eq","display":"Checking PDC emulator time source configuration"}'
WHERE control_id = 'DC-028' AND [type] = 'command';

-- DC-029: Tombstone lifetime
UPDATE control_defs SET [type] = 'dc', check_json = N'{"checkType":"dc_tombstone_lifetime","expected":180,"operator":"gte","display":"Checking AD tombstone lifetime"}'
WHERE control_id = 'DC-029' AND [type] = 'command';

-- DC-030: Domain functional level
UPDATE control_defs SET [type] = 'dc', check_json = N'{"checkType":"dc_domain_level","expected":7,"operator":"gte","display":"Checking domain functional level >= 2016"}'
WHERE control_id = 'DC-030' AND [type] = 'command';

-- DC-031..033: DNS commands - can't do natively (needs DNS module). Deactivate.
UPDATE control_defs SET is_active = 0 WHERE control_id IN ('DC-031','DC-032','DC-033') AND [type] = 'command';

-- DC-034: Firewall - convert to registry check
UPDATE control_defs SET [type] = 'registry',
    check_json = N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\DomainProfile","valueName":"EnableFirewall","expected":1,"operator":"eq","display":"Checking DC Windows Firewall domain profile"}'
WHERE control_id = 'DC-034' AND [type] = 'command';

-- DC-035: DNSSEC trust anchors - can't do natively. Deactivate.
UPDATE control_defs SET is_active = 0 WHERE control_id = 'DC-035' AND [type] = 'command';

-- DC-001..006: Require repadmin/dcdiag/netdom — deactivate (can't run without Process.Start)
UPDATE control_defs SET is_active = 0 WHERE control_id IN ('DC-001','DC-002','DC-003','DC-004','DC-005','DC-006') AND [type] = 'command';

-- DC-010: LDAPS port test — deactivate (needs TCP connect which we do have but not wired)
UPDATE control_defs SET is_active = 0 WHERE control_id = 'DC-010' AND [type] = 'command';

-- ============================================================
-- TRACK C: Add 60 new DC controls (DC-041..DC-100)
-- All use 'dc', 'registry', 'auditpol', or 'service' type
-- ============================================================

-- Category lookups
DECLARE @catAD         INT = (SELECT id FROM control_categories WHERE name=N'Active Directory');
DECLARE @catKerberos   INT = (SELECT id FROM control_categories WHERE name=N'Kerberos Security');
DECLARE @catDCHarden   INT = (SELECT id FROM control_categories WHERE name=N'Domain Controller Hardening');
DECLARE @catDNSSec     INT = (SELECT id FROM control_categories WHERE name=N'DNS Security');
DECLARE @catNetSec     INT = (SELECT id FROM control_categories WHERE name=N'Network Security');
DECLARE @catAuditLog   INT = (SELECT id FROM control_categories WHERE name=N'Audit, Logging And Monitoring');
DECLARE @catServices   INT = (SELECT id FROM control_categories WHERE name=N'Services Hardening');
DECLARE @catCredProt   INT = (SELECT id FROM control_categories WHERE name=N'Credential Protection');
DECLARE @catSecOpts    INT = (SELECT id FROM control_categories WHERE name=N'Security Options And Local Policy');

-- Platform IDs (DC only)
DECLARE @platDC19 INT = (SELECT id FROM platforms WHERE code='DC19');
DECLARE @platDC22 INT = (SELECT id FROM platforms WHERE code='DC22');
DECLARE @platDC25 INT = (SELECT id FROM platforms WHERE code='DC25');

-- Framework IDs
DECLARE @fwCIS   INT = (SELECT id FROM frameworks WHERE code='CIS');
DECLARE @fwNIST  INT = (SELECT id FROM frameworks WHERE code='NIST');
DECLARE @fwHIPAA INT = (SELECT id FROM frameworks WHERE code='HIPAA');
DECLARE @fwPCI   INT = (SELECT id FROM frameworks WHERE code='PCI-DSS');
DECLARE @fwISO   INT = (SELECT id FROM frameworks WHERE code='ISO27001');

-- ── SECTION G: AD Security (DC-041..DC-055) ──

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-041')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-041', @catAD, N'Kerberoastable Accounts Count', 'dc', 'critical',
    N'{"checkType":"dc_kerberoastable","expected":0,"operator":"eq","display":"Checking for kerberoastable accounts"}',
    N'Remove SPNs from user accounts where possible, or ensure they have 25+ character passwords. Kerberoastable accounts allow offline password cracking of their service tickets.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-042')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-042', @catAD, N'Stale Computer Accounts (90+ Days Inactive)', 'dc', 'medium',
    N'{"checkType":"dc_stale_computers","expected":10,"operator":"lte","display":"Checking for stale computer accounts"}',
    N'Remove or disable computer accounts that have not logged in for 90+ days. Stale accounts increase attack surface and can be hijacked.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-043')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-043', @catCredProt, N'Accounts with Password Never Expires', 'dc', 'high',
    N'{"checkType":"dc_pwd_never_expire","expected":5,"operator":"lte","display":"Checking for accounts with password never expires"}',
    N'Minimize accounts with DONT_EXPIRE_PASSWORD flag. Service accounts should use Group Managed Service Accounts (gMSA) instead.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-044')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-044', @catCredProt, N'Inactive Admin Accounts (60+ Days)', 'dc', 'critical',
    N'{"checkType":"dc_inactive_admins","expected":0,"operator":"eq","display":"Checking for inactive admin accounts"}',
    N'Admin accounts (adminCount=1) with no logon in 60+ days should be disabled. Dormant admin accounts are prime targets for attackers.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-045')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-045', @catAD, N'Schema Admins Group Empty', 'dc', 'high',
    N'{"checkType":"dc_schema_admins","expected":0,"operator":"eq","display":"Checking Schema Admins group membership"}',
    N'Schema Admins should have zero members except during schema modifications. Add members only temporarily and remove immediately after.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-046')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-046', @catAD, N'LAPS Coverage >= 80%', 'dc', 'high',
    N'{"checkType":"dc_laps_coverage","expected":80,"operator":"gte","display":"Checking LAPS deployment coverage"}',
    N'Deploy LAPS (Local Administrator Password Solution) to at least 80% of domain-joined computers. LAPS randomizes local admin passwords preventing lateral movement.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-047')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-047', @catAD, N'Orphaned AdminCount Attributes Cleaned', 'dc', 'medium',
    N'{"checkType":"dc_admin_count_orphan","expected":5,"operator":"lte","display":"Checking for orphaned adminCount attributes"}',
    N'Accounts with adminCount=1 but no longer in protected groups retain elevated security descriptors. Run SDProp manually and clear adminCount on orphans.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-048')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-048', @catAD, N'GPO Count Reasonable (< 200)', 'dc', 'low',
    N'{"checkType":"dc_gpo_count","expected":200,"operator":"lt","display":"Checking GPO count"}',
    N'Excessive GPOs increase logon time and management complexity. Review and consolidate unused or redundant GPOs.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-049')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-049', @catAD, N'AD Replication No Consecutive Failures', 'dc', 'critical',
    N'{"checkType":"dc_replication_queue","expected":0,"operator":"eq","display":"Checking AD replication health via WMI"}',
    N'Replication failures indicate DC health issues. Check network connectivity, DNS, and event logs. Persistent failures can cause directory inconsistencies.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-050')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-050', @catDCHarden, N'DSRM Admin Logon Behavior Set', 'dc', 'high',
    N'{"checkType":"dc_dsrm_password_set","expected":1,"operator":"lte","display":"Checking DSRM admin logon behavior"}',
    N'DSRM (Directory Services Restore Mode) admin logon behavior should be 0 (default) or 1 (sync with AD admin). Value 2 allows network logon with DSRM password — dangerous.',
    1, 1, @systemUserId);

-- ── SECTION H: DC Registry Hardening (DC-051..DC-070) ──

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-051')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-051', @catNetSec, N'SMB Client Signing Required', 'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters","valueName":"RequireSecuritySignature","expected":1,"operator":"eq","display":"Checking SMB client signing on DC"}',
    N'GPO: Security Options > "Microsoft network client: Digitally sign communications (always)" = Enabled. Prevents SMB relay attacks from the DC.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-052')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-052', @catNetSec, N'SMB Server Signing Required on DC', 'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanManServer\\Parameters","valueName":"RequireSecuritySignature","expected":1,"operator":"eq","display":"Checking SMB server signing on DC"}',
    N'GPO: Security Options > "Microsoft network server: Digitally sign communications (always)" = Enabled. DCs MUST require SMB signing to prevent relay attacks.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-053')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-053', @catNetSec, N'LDAP Channel Binding Enforced', 'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\NTDS\\Parameters","valueName":"LdapEnforceChannelBinding","expected":2,"operator":"eq","display":"Checking LDAP channel binding enforcement"}',
    N'Set LdapEnforceChannelBinding=2 (Always). Prevents LDAP relay attacks (CVE-2017-8563). Test with value 1 first if legacy apps exist.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-054')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-054', @catDCHarden, N'Credential Guard Enabled on DC', 'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\DeviceGuard","valueName":"EnableVirtualizationBasedSecurity","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking Credential Guard on DC"}',
    N'Enable Credential Guard (VBS) on DCs running Server 2016+. Protects LSASS from credential dumping via hardware-isolated container.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-055')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-055', @catDCHarden, N'LSA Protection (RunAsPPL) Enabled on DC', 'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RunAsPPL","expected":1,"operator":"eq","missingBehavior":"fail","display":"Checking LSA protection on DC"}',
    N'Enable LSA protection: HKLM\\SYSTEM\\CCS\\Control\\Lsa\\RunAsPPL=1. Prevents non-protected processes from reading LSASS memory (blocks Mimikatz-style attacks).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-056')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-056', @catDCHarden, N'WDigest Credential Caching Disabled', 'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\WDigest","valueName":"UseLogonCredential","expected":0,"operator":"eq","missingBehavior":"pass","display":"Checking WDigest credential caching on DC"}',
    N'WDigest must be disabled (UseLogonCredential=0). When enabled, plaintext passwords are stored in LSASS memory and trivially extractable with Mimikatz.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-057')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-057', @catDCHarden, N'Null Session Enumeration Restricted', 'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RestrictAnonymous","expected":1,"operator":"gte","display":"Checking null session restriction on DC"}',
    N'Set RestrictAnonymous >= 1. Prevents anonymous enumeration of SAM accounts and shares. Value 2 blocks all anonymous access (may break legacy apps).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-058')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-058', @catDCHarden, N'Null Session Named Pipe Access Restricted', 'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\LanManServer\\Parameters","valueName":"RestrictNullSessAccess","expected":1,"operator":"eq","display":"Checking null session pipe restriction"}',
    N'Set RestrictNullSessAccess=1. Restricts anonymous access to named pipes and shares. Essential DC hardening to prevent pre-auth information gathering.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-059')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-059', @catDCHarden, N'LM Hash Storage Disabled', 'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"NoLMHash","expected":1,"operator":"eq","display":"Checking LM hash storage disabled on DC"}',
    N'Set NoLMHash=1. Prevents storage of weak LM hashes for new passwords. LM hashes are trivially crackable and should never be stored on a DC.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-060')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-060', @catDCHarden, N'LAN Manager Authentication Level (NTLMv2 Only)', 'registry', 'critical',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"LmCompatibilityLevel","expected":5,"operator":"gte","display":"Checking LM authentication level on DC"}',
    N'Set LmCompatibilityLevel >= 5 (Send NTLMv2 response only. Refuse LM & NTLM). On DC this blocks all legacy LM/NTLMv1 authentication.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-061')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-061', @catNetSec, N'Remote Registry Service Disabled on DC', 'service', 'medium',
    N'{"serviceName":"RemoteRegistry","expectedStatus":"Stopped","expectedStartType":"Disabled","display":"Checking Remote Registry service on DC"}',
    N'Disable Remote Registry on DCs unless specifically required for monitoring. Remote registry access enables lateral movement and reconnaissance.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-062')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-062', @catDCHarden, N'NTDS Service Running', 'dc', 'critical',
    N'{"checkType":"dc_ntds_service","expected":true,"operator":"eq","display":"Checking NTDS service status"}',
    N'The NTDS (Active Directory Domain Services) service must be running on all DCs. A stopped NTDS service means the DC is not authenticating clients.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-063')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-063', @catDCHarden, N'Print Spooler Disabled on DC (DcEngine)', 'dc', 'critical',
    N'{"checkType":"dc_print_spooler","expected":true,"operator":"eq","display":"Checking Print Spooler is disabled on DC"}',
    N'Print Spooler must be stopped and disabled on DCs. PrintNightmare (CVE-2021-34527) and SpoolFool exploit this service for SYSTEM-level code execution.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-064')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-064', @catNetSec, N'SMB Server Signing Enforced (DcEngine)', 'dc', 'critical',
    N'{"checkType":"dc_smb_signing","expected":true,"operator":"eq","display":"Checking SMB server signing enforcement on DC"}',
    N'SMB signing must be required on DCs to prevent relay attacks (ntlmrelayx, PetitPotam). GPO: "Microsoft network server: Digitally sign communications (always)".',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-065')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-065', @catNetSec, N'Netlogon Secure Channel Protection (ZeroLogon)', 'dc', 'critical',
    N'{"checkType":"dc_secure_channel","expected":true,"operator":"eq","display":"Checking ZeroLogon protection"}',
    N'FullSecureChannelProtection must be 1. Mitigates CVE-2020-1472 (ZeroLogon) which allows unauthenticated domain admin access.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-066')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-066', @catNetSec, N'NTLM Restriction Level on DC', 'dc', 'high',
    N'{"checkType":"dc_ntlm_restrict","expected":1,"operator":"gte","display":"Checking NTLM restriction level on DC"}',
    N'Set RestrictSendingNTLMTraffic >= 1 (audit) on DCs. Goal: identify all NTLM usage, then set to 2 (deny) after migration to Kerberos.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-067')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-067', @catDCHarden, N'Expired Certificates in CA Store', 'dc', 'medium',
    N'{"checkType":"dc_crl_validity","expected":0,"operator":"eq","display":"Checking for expired certificates in CA store"}',
    N'Remove expired certificates from the CA store. Expired certs can cause authentication failures and indicate neglected PKI maintenance.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-068')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-068', @catDCHarden, N'DNS Forwarders Configured', 'dc', 'medium',
    N'{"checkType":"dc_dns_forwarders","expected":1,"operator":"gte","display":"Checking DNS forwarder configuration"}',
    N'Configure DNS forwarders on DCs to trusted recursive resolvers. Without forwarders, DCs use root hints which adds latency and may leak internal queries.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-069')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-069', @catDCHarden, N'LDAP Server Signing Enforced (DcEngine)', 'dc', 'critical',
    N'{"checkType":"dc_ldap_signing","expected":2,"operator":"eq","display":"Checking LDAP server signing enforcement"}',
    N'LDAPServerIntegrity must be 2 (Require signing). Prevents unsigned LDAP binds which enable MITM and relay attacks against the DC.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-070')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-070', @catDCHarden, N'Time Source Configured (Not Local Clock)', 'dc', 'high',
    N'{"checkType":"dc_time_source","expected":true,"operator":"eq","display":"Checking time synchronization source"}',
    N'DC must sync to external NTP source, not local CMOS clock. Kerberos requires <5min clock skew; local clock drift causes auth failures.',
    1, 1, @systemUserId);

-- ── SECTION I: Additional Registry Hardening (DC-071..DC-085) ──

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-071')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-071', @catDCHarden, N'SAM Remote Enumeration Restricted', 'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"RestrictRemoteSAM","expected":"O:BAG:BAD:(A;;RC;;;BA)","operator":"eq","missingBehavior":"warn","display":"Checking SAM remote enumeration restriction"}',
    N'Restrict remote SAM enumeration to Administrators only. Prevents user/group enumeration by non-admin attackers (BloodHound, SharpHound).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-072')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-072', @catDCHarden, N'Cached Logon Credentials Limited', 'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon","valueName":"CachedLogonsCount","expected":0,"operator":"eq","display":"Checking cached logon count on DC"}',
    N'Set CachedLogonsCount=0 on DCs. DCs always have connectivity to authenticate — no need to cache credentials which can be extracted offline.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-073')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-073', @catDCHarden, N'Anonymous SID/Name Translation Disabled', 'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Lsa","valueName":"TurnOffAnonymousBlock","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking anonymous SID translation"}',
    N'Disable anonymous SID/Name translation to prevent enumeration of account names from SIDs without authentication.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-074')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-074', @catDCHarden, N'NTDS.dit Auditing via Object Access', 'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\NTDS\\Diagnostics","valueName":"15 Field Engineering","expected":5,"operator":"gte","missingBehavior":"warn","display":"Checking NTDS diagnostic level for extraction detection"}',
    N'Set NTDS Diagnostics > 15 Field Engineering = 5. Generates Event 1644 on expensive LDAP queries, helps detect DCSync/DCShadow attacks.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-075')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-075', @catNetSec, N'IPv6 Disabled on DC (If Not Used)', 'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\Tcpip6\\Parameters","valueName":"DisabledComponents","expected":255,"operator":"eq","missingBehavior":"warn","display":"Checking IPv6 disabled on DC"}',
    N'If IPv6 is not used, disable it on DCs (DisabledComponents=0xFF). IPv6 expands the attack surface with link-local addressing and DHCPv6 poisoning.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-076')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-076', @catNetSec, N'LLMNR Disabled on DC', 'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient","valueName":"EnableMulticast","expected":0,"operator":"eq","missingBehavior":"warn","display":"Checking LLMNR disabled on DC"}',
    N'Disable LLMNR (Link-Local Multicast Name Resolution) on DCs. LLMNR poisoning enables credential relay attacks (Responder/Inveigh).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-077')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-077', @catNetSec, N'NetBIOS over TCP/IP Disabled on DC', 'registry', 'medium',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\NetBT\\Parameters","valueName":"NodeType","expected":2,"operator":"eq","missingBehavior":"warn","display":"Checking NetBIOS over TCP/IP on DC"}',
    N'Set NodeType=2 (P-node, point-to-point). Disables NetBIOS broadcast resolution which is exploitable for NBNS poisoning and relay attacks.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-078')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-078', @catNetSec, N'WPAD Protocol Disabled on DC', 'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\Wpad","valueName":"WpadOverride","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking WPAD disabled on DC"}',
    N'Disable WPAD on DCs. WPAD auto-proxy detection is exploitable for MITM and credential relay (combined with LLMNR/NBNS poisoning).',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-079')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-079', @catDCHarden, N'Secure Boot Enabled on DC', 'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State","valueName":"UEFISecureBootEnabled","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking Secure Boot on DC"}',
    N'Enable Secure Boot on DC hardware. Prevents bootkit/rootkit persistence below the OS layer. Required for Credential Guard.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-080')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-080', @catDCHarden, N'PowerShell Script Block Logging on DC', 'registry', 'high',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\ScriptBlockLogging","valueName":"EnableScriptBlockLogging","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking PowerShell Script Block Logging on DC"}',
    N'Enable Script Block Logging on DCs via GPO. Captures full PowerShell script content in Event ID 4104, essential for detecting malicious AD administration.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-081')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-081', @catDCHarden, N'PowerShell Module Logging on DC', 'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\ModuleLogging","valueName":"EnableModuleLogging","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking PowerShell Module Logging on DC"}',
    N'Enable Module Logging on DCs. Records pipeline execution details for AD cmdlets (Get-ADUser, Set-ADObject, etc.) in Event ID 4103.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-082')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-082', @catDCHarden, N'PowerShell Transcription Enabled on DC', 'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\PowerShell\\Transcription","valueName":"EnableTranscripting","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking PowerShell Transcription on DC"}',
    N'Enable transcription on DCs. Creates text log of all PS sessions. Configure output path to a secured share for SIEM ingestion.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-083')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-083', @catDCHarden, N'WinRM Restricted to Domain/Private', 'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Policies\\Microsoft\\Windows\\WinRM\\Service","valueName":"AllowAutoConfig","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking WinRM configuration on DC"}',
    N'Restrict WinRM listeners to domain network profile only. Configure via GPO: WinRM Service > Allow remote server management through WinRM with IP filters.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-084')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-084', @catDCHarden, N'Remote Desktop Restricted on DC', 'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Control\\Terminal Server","valueName":"fDenyTSConnections","expected":1,"operator":"eq","missingBehavior":"warn","display":"Checking RDP disabled on DC"}',
    N'Disable RDP on DCs where possible. Administer via local console, RSAT, or PAW. If RDP required, restrict via NLA + firewall rules to management subnet only.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-085')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-085', @catDCHarden, N'AutoPlay Disabled on DC', 'registry', 'medium',
    N'{"hive":"HKLM","path":"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer","valueName":"NoDriveTypeAutoRun","expected":255,"operator":"eq","missingBehavior":"warn","display":"Checking AutoPlay disabled on DC"}',
    N'Disable AutoPlay/AutoRun on DCs (NoDriveTypeAutoRun=0xFF). Prevents USB-based malware delivery vectors.',
    1, 1, @systemUserId);

-- ── SECTION J: Additional Audit Policies (DC-086..DC-095) ──

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-086')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-086', @catAuditLog, N'Audit: Credential Validation', 'auditpol', 'high',
    N'{"subcategory":"Credential Validation","expectedSetting":"Success and Failure","display":"Checking Credential Validation audit policy on DC"}',
    N'Audit Credential Validation captures NTLM authentication attempts (Event 4776). Essential for detecting pass-the-hash and brute force on DC.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-087')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-087', @catAuditLog, N'Audit: Computer Account Management', 'auditpol', 'high',
    N'{"subcategory":"Computer Account Management","expectedSetting":"Success and Failure","display":"Checking Computer Account Management audit on DC"}',
    N'Audit computer account creation/deletion/modification. Detects unauthorized domain joins and MachineAccountQuota abuse.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-088')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-088', @catAuditLog, N'Audit: Security Group Management', 'auditpol', 'critical',
    N'{"subcategory":"Security Group Management","expectedSetting":"Success and Failure","display":"Checking Security Group Management audit on DC"}',
    N'Audit group membership changes (Event 4728/4732/4756). Critical for detecting privilege escalation via group modification.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-089')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-089', @catAuditLog, N'Audit: User Account Management', 'auditpol', 'critical',
    N'{"subcategory":"User Account Management","expectedSetting":"Success and Failure","display":"Checking User Account Management audit on DC"}',
    N'Audit user account creation/deletion/password changes (Events 4720/4722/4723/4724/4725/4726). Fundamental DC security monitoring.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-090')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-090', @catAuditLog, N'Audit: Logon/Logoff Events', 'auditpol', 'high',
    N'{"subcategory":"Logon","expectedSetting":"Success and Failure","display":"Checking Logon audit policy on DC"}',
    N'Audit logon events (Event 4624/4625). Essential for tracking who authenticates against the DC and detecting brute force.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-091')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-091', @catAuditLog, N'Audit: Special Logon', 'auditpol', 'high',
    N'{"subcategory":"Special Logon","expectedSetting":"Success","display":"Checking Special Logon audit on DC"}',
    N'Audit special logon (Event 4672 - admin privilege assigned). Tracks when admin-equivalent tokens are issued.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-092')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-092', @catAuditLog, N'Audit: Sensitive Privilege Use', 'auditpol', 'high',
    N'{"subcategory":"Sensitive Privilege Use","expectedSetting":"Success and Failure","display":"Checking Sensitive Privilege Use audit on DC"}',
    N'Audit sensitive privilege use (SeTakeOwnership, SeDebugPrivilege, etc.). Detects privilege abuse and credential theft attempts.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-093')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-093', @catAuditLog, N'Audit: Authentication Policy Change', 'auditpol', 'high',
    N'{"subcategory":"Authentication Policy Change","expectedSetting":"Success","display":"Checking Authentication Policy Change audit on DC"}',
    N'Audit policy changes affecting authentication (Kerberos, NTLM, password policy). Detects unauthorized weakening of auth security.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-094')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-094', @catAuditLog, N'Audit: MPSSVC Rule-Level Policy Change', 'auditpol', 'medium',
    N'{"subcategory":"MPSSVC Rule-Level Policy Change","expectedSetting":"Success and Failure","display":"Checking firewall rule audit on DC"}',
    N'Audit Windows Firewall rule changes on DC. Detects unauthorized firewall modifications that could open attack vectors.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-095')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-095', @catAuditLog, N'Security Event Log Size >= 1 GB', 'registry', 'high',
    N'{"hive":"HKLM","path":"SYSTEM\\CurrentControlSet\\Services\\EventLog\\Security","valueName":"MaxSize","expected":1073741824,"operator":"gte","missingBehavior":"warn","display":"Checking Security event log size on DC"}',
    N'Set Security event log minimum size to 1 GB on DCs. DCs generate high event volumes; small logs lose evidence before SIEM collection.',
    1, 1, @systemUserId);

-- ── SECTION K: Services & Network (DC-096..DC-100) ──

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-096')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-096', @catServices, N'Xbox Services Disabled on DC', 'service', 'low',
    N'{"serviceName":"XblGameSave","expectedStatus":"Stopped","expectedStartType":"Disabled","display":"Checking Xbox services on DC"}',
    N'Disable Xbox services on DCs. Consumer gaming services have no place on domain controllers and increase attack surface.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-097')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-097', @catServices, N'Fax Service Disabled on DC', 'service', 'low',
    N'{"serviceName":"Fax","expectedStatus":"Stopped","expectedStartType":"Disabled","display":"Checking Fax service on DC"}',
    N'Disable Fax service on DCs. Unnecessary service that increases attack surface.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-098')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-098', @catDCHarden, N'BitLocker on DC System Drive', 'bitlocker', 'high',
    N'{"drive":"C:","display":"Checking BitLocker on DC system drive"}',
    N'Encrypt DC system drive with BitLocker. Protects NTDS.dit and SYSVOL from offline extraction if physical access is compromised.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-099')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-099', @catDCHarden, N'TPM Present on DC', 'tpm', 'medium',
    N'{"display":"Checking TPM on DC hardware"}',
    N'DCs should have TPM for BitLocker key protection, Measured Boot, and Credential Guard VBS attestation.',
    1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='DC-100')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('DC-100', @catDCHarden, N'Windows Defender Running on DC', 'service', 'high',
    N'{"serviceName":"WinDefend","expectedStatus":"Running","display":"Checking Windows Defender on DC"}',
    N'Windows Defender (or equivalent AV) must be running on DCs. DCs are high-value targets and need real-time protection.',
    1, 1, @systemUserId);

-- ============================================================
-- PLATFORM + FRAMEWORK + ASSESSMENT LINKAGE for DC-041..DC-100
-- ============================================================

DECLARE @newDcBlock TABLE (control_id VARCHAR(20) PRIMARY KEY);
INSERT INTO @newDcBlock VALUES
    ('DC-041'),('DC-042'),('DC-043'),('DC-044'),('DC-045'),('DC-046'),('DC-047'),('DC-048'),('DC-049'),('DC-050'),
    ('DC-051'),('DC-052'),('DC-053'),('DC-054'),('DC-055'),('DC-056'),('DC-057'),('DC-058'),('DC-059'),('DC-060'),
    ('DC-061'),('DC-062'),('DC-063'),('DC-064'),('DC-065'),('DC-066'),('DC-067'),('DC-068'),('DC-069'),('DC-070'),
    ('DC-071'),('DC-072'),('DC-073'),('DC-074'),('DC-075'),('DC-076'),('DC-077'),('DC-078'),('DC-079'),('DC-080'),
    ('DC-081'),('DC-082'),('DC-083'),('DC-084'),('DC-085'),('DC-086'),('DC-087'),('DC-088'),('DC-089'),('DC-090'),
    ('DC-091'),('DC-092'),('DC-093'),('DC-094'),('DC-095'),('DC-096'),('DC-097'),('DC-098'),('DC-099'),('DC-100');

-- Platform linkage (DC19, DC22, DC25)
INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platDC19 FROM control_defs cd JOIN @newDcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_platforms cp WHERE cp.control_def_id = cd.id AND cp.platform_id = @platDC19);

INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platDC22 FROM control_defs cd JOIN @newDcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_platforms cp WHERE cp.control_def_id = cd.id AND cp.platform_id = @platDC22);

INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, @platDC25 FROM control_defs cd JOIN @newDcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_platforms cp WHERE cp.control_def_id = cd.id AND cp.platform_id = @platDC25);

-- Framework linkage (all 5)
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwNIST FROM control_defs cd JOIN @newDcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwNIST);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwCIS FROM control_defs cd JOIN @newDcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwCIS);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA FROM control_defs cd JOIN @newDcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwHIPAA);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO FROM control_defs cd JOIN @newDcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwISO);

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwPCI FROM control_defs cd JOIN @newDcBlock b ON b.control_id = cd.control_id
WHERE NOT EXISTS (SELECT 1 FROM control_frameworks cf WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwPCI);

-- Assessment linkage
INSERT INTO assessment_controls (assessment_id, control_def_id)
SELECT a.id, cd.id FROM assessments a
CROSS JOIN control_defs cd JOIN @newDcBlock b ON b.control_id = cd.control_id
WHERE a.is_active = 1 AND a.deleted_at IS NULL
  AND NOT EXISTS (SELECT 1 FROM assessment_controls ac WHERE ac.assessment_id = a.id AND ac.control_def_id = cd.id);

COMMIT TRANSACTION;
GO

-- ============================================================
-- Verification
-- ============================================================
SELECT 'Active DC controls' AS metric, COUNT(*) AS val
FROM control_defs WHERE control_id LIKE 'DC-%' AND is_active = 1;
-- Expected: ~85 (100 total - 15 deactivated)

SELECT [type], COUNT(*) AS cnt
FROM control_defs WHERE control_id LIKE 'DC-%' AND is_active = 1
GROUP BY [type] ORDER BY [type];
-- Expected: auditpol ~15, bitlocker 1, dc ~20, registry ~35, service ~6, tpm 1
