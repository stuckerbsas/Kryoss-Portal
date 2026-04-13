# Baseline Gap Analysis -- Kryoss vs Microsoft Security Baselines

**Generated:** 2026-04-12
**Baselines compared:**
- Windows 11 v25H2 (Build 26200.6584, last modified 2025-09-26)
- Windows Server 2025 v2602

**Kryoss catalog sources:**
- seed_004_controls.sql -- 161 scored (SC-001..SC-161) + ~444 baseline registry (BL-0001..BL-0469)
- seed_008_new_engine_controls.sql -- 17 controls (BL-0470..BL-0486): eventlog, certstore, bitlocker, tpm
- seed_010_server_controls.sql -- 80 server controls (SRV-001..SRV-080)
- seed_011_antivirus_controls.sql -- 40 antivirus controls (AV-001..AV-040)

---

## Summary

| Metric | Count |
|---|---|
| Total MS baseline settings in Win11 v25H2 (Security Template + Advanced Audit + Firewall + Computer GP + User GP + Services) | ~4940 lines / ~520 distinct configured settings |
| Total MS baseline settings in Server 2025 (same structure) | ~4923 lines / ~530 distinct configured settings |
| **Settings with an actual baseline value (non-NaN) in Win11** | ~280 |
| **Settings with an actual baseline value (non-NaN) in Server 2025** | ~290 |
| Already covered by existing Kryoss controls (direct or functional match) | ~195 |
| **GAPS -- not covered** | ~85-95 |

**Coverage rate: ~68-70%** of the Microsoft-recommended configured settings are already addressed by Kryoss controls, either through direct registry checks (BL-* controls), scored command functions (SC-* controls that check multiple settings internally), or dedicated engine controls (AV-*, SRV-*, BL-047x-048x).

---

## Methodology

Matching was done by:
1. **Registry path + value name** -- exact match between `check_json.path + valueName` and the baseline's `Registry Information` column (format: `HKLM\path!valueName`)
2. **Functional match** -- SC-* scored functions that internally check the same registry/setting (e.g., SC-016 "Test-SMBSigning" covers `LanmanServer\Parameters\RequireSecuritySignature`)
3. **Audit policy subcategory** -- comparing baseline Advanced Audit settings against SC-009/SC-062/SC-140 (Test-AuditPolicy/Test-AdvancedAuditPolicy/Test-BaselineAuditPolicyDetailed)
4. **Service name** -- comparing baseline Services settings against SC-074/AV-036..AV-039 (Test-UnnecessaryServices, EDR service checks)
5. **Firewall profile** -- comparing baseline Firewall settings against SC-010/SC-141 (Test-WindowsFirewall/Test-BaselineFirewallDetailed)

**Important note on SC-* controls:** Many SC-* controls (SC-124 through SC-161 especially) are "batch validators" that check multiple registry values internally. For example, SC-137 (Test-BaselineSecurityOptions) checks ~28 security option registry values, and SC-128 (Test-BaselineNetworkPolicies) checks ~19 network hardening policies. These provide functional coverage even though the individual registry values are not exposed as separate controls. The BL-* controls then break these out into individual registry checks.

---

## Gaps -- Windows 11 v25H2

### A. Security Template settings not covered

These are settings from the Security Template section of the baseline that have a recommended value but no matching Kryoss control.

| # | Policy Path | Policy Setting Name | Baseline Value | Notes |
|---|---|---|---|---|
| 1 | Security Options | Accounts: Block Microsoft accounts | Not Configured | Registry: `NoConnectedUser`. Not checked. Low priority -- operational. |
| 2 | Security Options | Domain member: Digitally encrypt or sign secure channel data (always) | Enabled | Registry: `Netlogon\Parameters\RequireSignOrSeal`. Not individually checked. |
| 3 | Security Options | Domain member: Digitally encrypt secure channel data (when possible) | Enabled | Registry: `Netlogon\Parameters\SealSecureChannel`. Not individually checked. |
| 4 | Security Options | Domain member: Digitally sign secure channel data (when possible) | Enabled | Registry: `Netlogon\Parameters\SignSecureChannel`. Not individually checked. |
| 5 | Security Options | Domain member: Require strong session key | Enabled | Registry: `Netlogon\Parameters\RequireStrongKey`. Not individually checked. |
| 6 | Security Options | Interactive logon: Smart card removal behavior | Lock Workstation | Registry: `Winlogon\ScRemoveOption`. Not checked. |
| 7 | Security Options | Microsoft network client: Digitally sign communications (always) | Enabled | Registry: `LanmanWorkstation\Parameters\RequireSecuritySignature`. Client-side SMB signing not individually checked (server-side is covered by SRV-001). |
| 8 | Security Options | Microsoft network client: Send unencrypted password to third-party SMB servers | Disabled | Registry: `LanmanWorkstation\Parameters\EnablePlainTextPassword`. Not checked. |
| 9 | Security Options | Microsoft network server: Digitally sign communications (always) | Enabled | Registry: `LanManServer\Parameters\RequireSecuritySignature`. Covered by SRV-001 for servers, but not for workstation scope (W10/W11). |
| 10 | Security Options | Network access: Do not allow storage of passwords and credentials for network authentication | NaN (should be reviewed) | Registry: `Lsa\DisableDomainCreds`. Not checked. |
| 11 | Security Options | Network access: Let Everyone permissions apply to anonymous users | Disabled | Registry: `Lsa\EveryoneIncludesAnonymous`. Not individually checked. |
| 12 | Security Options | Network access: Restrict anonymous access to Named Pipes and Shares | Enabled | Registry: `LanManServer\Parameters\RestrictNullSessAccess`. Covered by SRV-003 for servers, but not for workstations. |
| 13 | Security Options | Network access: Restrict clients allowed to make remote calls to SAM | Various | Registry: `Lsa\RestrictRemoteSAM`. Not individually checked. |
| 14 | Security Options | Network security: Allow LocalSystem NULL session fallback | NaN (review) | Registry: `Lsa\MSV1_0\AllowNullSessionFallback`. Not checked. |
| 15 | Security Options | Network security: Configure encryption types allowed for Kerberos | AES128+AES256 | Registry: `Lsa\Kerberos\Parameters\SupportedEncryptionTypes`. Not checked. Critical gap. |
| 16 | Security Options | Network security: LDAP client signing requirements | Negotiate signing | Registry: `LDAP\LDAPClientIntegrity`. Covered by SRV-011 for servers, not for workstations. |
| 17 | Security Options | Network security: Minimum session security for NTLM SSP based clients | Require NTLMv2 + 128-bit | Registry: `Lsa\MSV1_0\NtlmMinClientSec` = 537395200. Covered by SC-137 (batch), but not individual control. |
| 18 | Security Options | Network security: Minimum session security for NTLM SSP based servers | Require NTLMv2 + 128-bit | Registry: `Lsa\MSV1_0\NtlmMinServerSec` = 537395200. Covered by SC-137 (batch), but not individual control. |
| 19 | Security Options | System objects: Require case insensitivity for non-Windows subsystems | NaN (review) | Not checked. |
| 20 | Security Options | System objects: Strengthen default permissions of internal system objects | NaN (review) | Not checked. |
| 21 | Security Options | User Account Control: Behavior of the elevation prompt for administrators in Admin Approval Mode | Prompt for consent on the secure desktop | Registry: `System\ConsentPromptBehaviorAdmin` = 2. Partially covered by SC-014/SC-137. |
| 22 | Security Options | User Account Control: Behavior of the elevation prompt for standard users | Automatically deny | Registry: `System\ConsentPromptBehaviorUser` = 0. Not individually checked. |
| 23 | Security Options | User Account Control: Detect application installations and prompt for elevation | Enabled | Registry: `System\EnableInstallerDetection`. Not individually checked. |
| 24 | Security Options | User Account Control: Only elevate UIAccess applications installed in secure locations | Enabled | Registry: `System\EnableSecureUIAPaths`. Not individually checked. |
| 25 | Security Options | User Account Control: Run all administrators in Admin Approval Mode | Enabled | Registry: `System\EnableLUA`. Covered by SC-137 batch, but not individual control. |
| 26 | Security Options | User Account Control: Switch to the secure desktop when prompting for elevation | Enabled | Registry: `System\PromptOnSecureDesktop`. Not individually checked. |
| 27 | Security Options | User Account Control: Virtualize file and registry write failures to per-user locations | Enabled | Registry: `System\EnableVirtualization`. Not individually checked. |

### B. Advanced Audit Policy settings not covered

The Win11 baseline specifies 24 advanced audit subcategories with recommended values. Kryoss covers these through SC-009 (Test-AuditPolicy), SC-062 (Test-AdvancedAuditPolicy), and SC-140 (Test-BaselineAuditPolicyDetailed) which are batch validators. However, there are no individual controls for each subcategory.

**Baseline-configured subcategories:**

| # | Category | Subcategory | Baseline Value | Covered? |
|---|---|---|---|---|
| 1 | Account Logon | Audit Credential Validation | Success and Failure | Via SC-140 (batch) |
| 2 | Account Management | Audit Security Group Management | Success | Via SC-140 (batch) |
| 3 | Account Management | Audit User Account Management | Success and Failure | Via SC-140 (batch) |
| 4 | Detailed Tracking | Audit PNP Activity | Success | Via SC-140 (batch) |
| 5 | Detailed Tracking | Audit Process Creation | Success | Via SC-140 (batch) |
| 6 | Logon/Logoff | Audit Account Lockout | Failure | Via SC-140 (batch) |
| 7 | Logon/Logoff | Audit Group Membership | Success | Via SC-140 (batch) |
| 8 | Logon/Logoff | Audit Logon | Success and Failure | Via SC-140 (batch) |
| 9 | Logon/Logoff | Audit Other Logon/Logoff Events | Success and Failure | Via SC-140 (batch) |
| 10 | Logon/Logoff | Audit Special Logon | Success | Via SC-140 (batch) |
| 11 | Object Access | Audit Detailed File Share | Failure | Via SC-140 (batch) |
| 12 | Object Access | Audit File Share | Success and Failure | Via SC-140 (batch) |
| 13 | Object Access | Audit Other Object Access Events | Success and Failure | Via SC-140 (batch) |
| 14 | Object Access | Audit Removable Storage | Success and Failure | Via SC-140 (batch) |
| 15 | Policy Change | Audit Audit Policy Change | Success | Via SC-140 (batch) |
| 16 | Policy Change | Audit Authentication Policy Change | Success | Via SC-140 (batch) |
| 17 | Policy Change | Audit MPSSVC Rule-Level Policy Change | Success and Failure | Via SC-140 (batch) |
| 18 | Policy Change | Audit Other Policy Change Events | Failure | Via SC-140 (batch) |
| 19 | Privilege Use | Audit Sensitive Privilege Use | Success | Via SC-140 (batch) |
| 20 | System | Audit Other System Events | Success and Failure | Via SC-140 (batch) |
| 21 | System | Audit Security State Change | Success | Via SC-140 (batch) |
| 22 | System | Audit Security System Extension | Success | Via SC-140 (batch) |
| 23 | System | Audit System Integrity | Success and Failure | Via SC-140 (batch) |

**Verdict:** Functionally covered by SC-140 batch validator. However, if granular pass/fail per subcategory is desired, individual auditpol-type controls would be needed. This is a **reporting granularity gap**, not a coverage gap.

### C. Firewall settings not covered

The baseline specifies firewall settings for Domain, Private, and Public profiles. These are covered by SC-010 (Test-WindowsFirewall) and SC-141 (Test-BaselineFirewallDetailed) as batch validators.

**Settings with baseline values:**

| Setting | Domain | Private | Public | Covered? |
|---|---|---|---|---|
| Firewall State | On | On | On | SC-010/SC-141 |
| Inbound Connections | Block | Block | Block | SC-010/SC-141 |
| Outbound Connections | Allow | Allow | Allow | SC-010/SC-141 |
| Display a notification | No | No | No | SC-141 |
| Log size limit | 16384 | 16384 | 16384 | SC-141 |
| Log dropped packets | Yes | Yes | Yes | SC-022/SC-141 |
| Log successful connections | Yes | Yes | Yes | SC-141 |
| Apply local firewall rules (Public) | N/A | N/A | No | SC-141 |
| Apply local conn security rules (Public) | N/A | N/A | No | SC-141 |

**Verdict:** Functionally covered by SC-141 batch validator. No gap.

### D. Computer GP Administrative Template settings -- GAPS

These are the most significant gaps. The "Computer" section contains GP settings backed by HKLM registry values. Settings below have a baseline value AND are NOT covered by any existing Kryoss control.

#### D.1 Credential Delegation / Remote Desktop

| # | Policy Path | Setting Name | Baseline Value | Registry Path | Severity |
|---|---|---|---|---|---|
| 1 | Credentials Delegation | Encryption oracle remediation | Force Updated Clients | `CredSSP\Parameters!AllowEncryptionOracle` | High |
| 2 | Credentials Delegation | Remote host allows delegation of non-exportable credentials | Enabled | `CredSSP\Parameters!AllowDefaultCredentials` related | Medium |

#### D.2 Device Guard / Virtualization-Based Security

| # | Policy Path | Setting Name | Baseline Value | Registry Path | Severity |
|---|---|---|---|---|---|
| 3 | Device Guard | Turn On Virtualization Based Security | Enabled | `DeviceGuard!EnableVirtualizationBasedSecurity` | Critical |
| 4 | Device Guard | Platform Security Level | Secure Boot and DMA Protection | `DeviceGuard!RequirePlatformSecurityFeatures` | High |
| 5 | Device Guard | Credential Guard Configuration | Enabled with UEFI lock | `DeviceGuard\Lsa!LsaCfgFlags` | Critical |
| 6 | Device Guard | Secure Launch Configuration | Enabled | `DeviceGuard!ConfigureSystemGuardLaunch` | High |
| 7 | Device Guard | Kernel-mode Hardware-enforced Stack Protection | Enabled in enforcement mode | `DeviceGuard!ConfigureKernelShadowStacks` | High |

**Note:** SC-053 (Test-CredentialGuard) and SC-130 (Test-BaselineSystemPolicies) check VBS/CredGuard at a high level, but the specific Device Guard GP settings (Platform Security Level, Secure Launch, Kernel Shadow Stacks) are not individually checked. These are NEW in Win11 25H2.

#### D.3 Early Launch Anti-Malware (ELAM)

| # | Policy Path | Setting Name | Baseline Value | Registry Path | Severity |
|---|---|---|---|---|---|
| 8 | Early Launch Antimalware | Boot-Start Driver Initialization Policy | Good, unknown and bad but critical | `EarlyLaunch!DriverLoadPolicy` | Medium |

#### D.4 Local Security Authority (LSA)

| # | Policy Path | Setting Name | Baseline Value | Registry Path | Severity |
|---|---|---|---|---|---|
| 9 | Local Security Authority | Configures LSASS to run as a protected process | Enabled with UEFI Lock | `Lsa!RunAsPPL` = 2 (not just 1) | Critical |

**Note:** BL-0009 and SRV-065 check `RunAsPPL = 1`, but the Win11 25H2 baseline now requires value 2 (UEFI lock). This is a **value mismatch gap** -- we check for the setting but accept a weaker value.

#### D.5 Network Settings

| # | Policy Path | Setting Name | Baseline Value | Registry Path | Severity |
|---|---|---|---|---|---|
| 10 | DNS Client | Configure DNS over HTTPS (DoH) name resolution | Enabled | `DNSClient!DoHPolicy` | Medium |
| 11 | DNS Client | Configure NetBIOS settings | Disabled (Disable NetBIOS) | `DNSClient!EnableNetbios` | High |
| 12 | DNS Client | Turn off multicast name resolution | Enabled (disable LLMNR) | `DNSClient!EnableMulticast` | High |
| 13 | Lanman Server | Audit client does not support encryption | Enabled | `LanmanServer\Parameters!AuditClientDoesNotSupportEncryption` | Medium |
| 14 | Lanman Server | Audit client does not support signing | Enabled | `LanmanServer\Parameters!AuditClientDoesNotSupportSigning` | Medium |
| 15 | Lanman Server | SMB authentication rate limiter | Enabled | `LanmanServer\Parameters!EnableAuthRateLimiter` | Medium |
| 16 | Lanman Workstation | Audit insecure guest logon | Enabled | `LanmanWorkstation!AuditInsecureGuestLogon` | Medium |
| 17 | Lanman Workstation | Audit server does not support encryption | Enabled | `LanmanWorkstation!AuditServerDoesNotSupportEncryption` | Medium |
| 18 | Lanman Workstation | Audit server does not support signing | Enabled | `LanmanWorkstation!AuditServerDoesNotSupportSigning` | Medium |
| 19 | Network Provider | Hardened UNC Paths | RequireMutualAuthentication=1, RequireIntegrity=1, RequirePrivacy=1 | `NetworkProvider\HardenedPaths` | High |
| 20 | Network Provider | Enable Mailslots | Disabled | `NetworkProvider!EnableMailslots` | Medium |
| 21 | WLAN | Allow Windows to automatically connect to suggested open hotspots | Disabled | `WlanSvc\GPSvcGroup!AutoConnectAllowedOEM` | Low |
| 22 | WCM | Prohibit connection to non-domain networks when connected to domain | Enabled | `WcmSvc\GroupPolicy!fBlockNonDomain` | Medium |

**Note:** Items 11-22 are partially covered by BL-0016 through BL-0028 as individual BL registry checks, and also by SC-128 (Test-BaselineNetworkPolicies) as a batch validator. These are **already covered** in the catalog. Removing from gap count.

#### D.6 Windows Components -- Defender / Security

| # | Policy Path | Setting Name | Baseline Value | Registry Path | Severity |
|---|---|---|---|---|---|
| 23 | Windows Defender | Configure local setting override for reporting to MAPS | Disabled | `Windows Defender\SpyNet!LocalSettingOverrideSpyNetReporting` | Medium |
| 24 | Windows Defender | Cloud Block Level | High | `Windows Defender\MpEngine!MpCloudBlockLevel` = 2 | High |
| 25 | Windows Defender | Configure extended cloud check | 50 | `Windows Defender\MpEngine!MpBafsExtendedTimeout` | Medium |
| 26 | Windows Defender | Turn off routine remediation | Disabled | `Windows Defender!DisableRoutinelyTakingAction` | High |
| 27 | Windows Defender | Turn off auto exclusions | Disabled | `Windows Defender\Exclusions!DisableAutoExclusions` | Medium |

**Note:** AV-001 through AV-009 cover the main Defender settings, and SC-145 (Test-BaselineDefenderPolicy) is a batch validator for Defender GP settings. Items 23-27 are additional granular settings not individually checked. **Partial gap** -- Cloud Block Level and extended cloud check are notable.

#### D.7 Windows Components -- SmartScreen / Phishing Protection

| # | Policy Path | Setting Name | Baseline Value | Registry Path | Severity |
|---|---|---|---|---|---|
| 28 | SmartScreen | Configure Windows Defender SmartScreen | Enabled + Warn and prevent bypass | `Windows\System\ShellSmartScreenLevel` | High |
| 29 | WTDS | Notify Malicious | Enabled | `WTDS\Components!NotifyMalicious` | Medium |
| 30 | WTDS | Notify Password Reuse | Enabled | `WTDS\Components!NotifyPasswordReuse` | Medium |
| 31 | WTDS | Notify Unsafe App | Enabled | `WTDS\Components!NotifyUnsafeApp` | Medium |
| 32 | WTDS | Service Enabled | Enabled | `WTDS\Components!ServiceEnabled` | Medium |

**Note:** SC-147 (Test-BaselineSmartScreenEnhanced) covers these as a batch. **Functionally covered**, but no individual controls.

#### D.8 Windows Components -- Remote Desktop Services

| # | Policy Path | Setting Name | Baseline Value | Registry Path | Severity |
|---|---|---|---|---|---|
| 33 | RDS | Do not allow passwords to be saved | Enabled | `Terminal Services!DisablePasswordSaving` | Medium |
| 34 | RDS | Require secure RPC communication | Enabled | `Terminal Services!fEncryptRPCTraffic` | High |
| 35 | RDS | Set client connection encryption level | High | `Terminal Server\WinStations\RDP-Tcp!MinEncryptionLevel` = 3 | High |

**Note:** SRV-014/SRV-015/SRV-018 cover these for servers. For workstations (W10/W11), SC-015 (Test-RDP) and SC-146 (Test-BaselineRDPSecurity) provide functional coverage. **Covered for servers; workstation coverage via batch validators only.**

#### D.9 Windows Components -- WinRM

| # | Policy Path | Setting Name | Baseline Value | Registry Path | Severity |
|---|---|---|---|---|---|
| 36 | WinRM Client | Allow Basic authentication | Disabled | `WinRM\Client!AllowBasic` | High |
| 37 | WinRM Client | Allow unencrypted traffic | Disabled | `WinRM\Client!AllowUnencryptedTraffic` | High |
| 38 | WinRM Service | Allow Basic authentication | Disabled | `WinRM\Service!AllowBasic` | High |
| 39 | WinRM Service | Allow unencrypted traffic | Disabled | `WinRM\Service!AllowUnencryptedTraffic` | High |
| 40 | WinRM Service | Disallow Digest authentication | Enabled | `WinRM\Service!AllowAutoConfig` related | Medium |
| 41 | WinRM Service | Disallow WinRM from storing RunAs credentials | Enabled | `WinRM\Service!DisableRunAs` | Medium |

**Note:** SRV-022 covers WinRM Basic auth (service side). SC-148 (Test-BaselineWindowsMisc) covers WinRM as a batch. **Gap**: Client-side WinRM settings and Digest/RunAs not individually checked for workstations.

#### D.10 Windows Components -- Miscellaneous

| # | Policy Path | Setting Name | Baseline Value | Registry Path | Severity |
|---|---|---|---|---|---|
| 42 | App Runtime | Block launching Universal Windows apps with Windows Runtime API access from hosted content | Enabled | `AppPrivacy!BlockHostedAppAccessWinRT` | Low |
| 43 | AutoPlay | Disallow Autoplay for non-volume devices | Enabled | `Explorer!NoAutoplayfornonVolume` | Medium |
| 44 | AutoPlay | Set the default behavior for AutoRun | Do not execute any autorun commands | `Explorer!NoAutorun` | Medium |
| 45 | AutoPlay | Turn off Autoplay | All drives | `Explorer!NoDriveTypeAutoRun` | Medium |
| 46 | BitLocker | Deny write access to removable drives not protected by BitLocker | Enabled | `FVE!RDVDenyWriteAccess` | Medium |
| 47 | Cloud Content | Turn off cloud consumer account state content | Enabled | `CloudContent!DisableConsumerAccountStateContent` | Low |
| 48 | Cloud Content | Turn off cloud optimized content | Enabled | `CloudContent!DisableCloudOptimizedContent` | Low |
| 49 | Cloud Content | Turn off Microsoft consumer experiences | Enabled | `CloudContent!DisableWindowsConsumerFeatures` | Low |
| 50 | Credential User Interface | Enumerate administrator accounts on elevation | Disabled | `CredUI!EnumerateAdministrators` | Medium |
| 51 | Credential User Interface | Prevent the use of security questions for local accounts | Enabled | `CredUI!NoLocalPasswordResetQuestions` | Medium |
| 52 | Game DVR | Disable Game DVR | Enabled | `GameDVR!AllowGameDVR` | Low |
| 53 | LAPS | Configure password backup directory | Active Directory or Azure AD | `LAPS!BackupDirectory` | High |
| 54 | Printers | Configure Redirection Guard | Enabled + Guard on | `Printers!RedirectionGuardPolicy` | Medium |
| 55 | Printers | Configure RPC listener settings | RPC over TCP | `Printers\RPC!RpcProtocols` | Medium |
| 56 | Printers | Configure RPC over TCP port | 0 | `Printers\RPC!RpcTcpPort` | Low |
| 57 | Printers | Limits print driver installation to Administrators | Enabled | `Printers\PointAndPrint!RestrictDriverInstallationToAdmins` | High |
| 58 | Printers | Manage processing of Queue-specific files | Limit to Color profiles | `Printers!CopyFilesPolicy` | Medium |
| 59 | Search | Allow indexing of encrypted files | Disabled | `Windows Search!AllowIndexingEncryptedStoresOrItems` | Medium |
| 60 | Sudo | Enable sudo | Disabled | `Sudo!Enabled` | Medium |
| 61 | Windows Ink Workspace | Allow Windows Ink Workspace | Disabled | `WindowsInkWorkspace!AllowWindowsInkWorkspace` | Low |
| 62 | Windows Installer | Always install with elevated privileges | Disabled | `Installer!AlwaysInstallElevated` | High |
| 63 | Windows Logon Options | Enable MPR notifications for the system | Disabled | `System!EnableMPR` | Medium |
| 64 | Windows Logon Options | Sign-in and lock last interactive user automatically after a restart | Disabled | `Winlogon!DisableAutomaticRestartSignOn` | Medium |

**Note:** Items 43-64 are heavily covered by SC-134 (AutoPlay), SC-129 (Printer Hardening), SC-135 (Cloud Content), SC-130 (System Policies), SC-131 (Logon Policies), SC-148 (Windows Misc) as batch validators, and many have corresponding BL-0029..BL-0040 individual registry checks. **Mostly covered.** The gaps are items WITHOUT corresponding BL-* controls.

### E. Services settings (Win11)

The baseline specifies startup types for various services.

| # | Service | Baseline Startup | Covered? |
|---|---|---|---|
| 1 | BTAGService (Bluetooth Audio Gateway) | NaN | N/A |
| 2 | bthserv (Bluetooth Support) | NaN | N/A |
| 3 | Browser (Computer Browser) | Disabled | SC-074 (batch) |
| 4 | IISADMIN | Disabled | N/A (not on workstations) |
| 5 | irmon (Infrared Monitor) | Disabled | Not individually checked |
| 6 | LxssManager (WSL) | NaN | N/A |
| 7 | MapsBroker | Disabled | Not individually checked |
| 8 | MRXSMB10 (SMBv1) | Disabled | SC-001/BL-0006 |
| 9 | MSDTC | NaN | N/A |
| 10 | PushToInstall | Disabled | Not individually checked |
| 11 | RemoteAccess (Routing and Remote Access) | Disabled | SC-074 (batch) |
| 12 | RemoteRegistry | Disabled | SC-073 |
| 13 | simptcp (Simple TCP/IP Services) | Disabled | Not individually checked |
| 14 | SNMP | Disabled | SC-074 (batch) |
| 15 | WerSvc (Windows Error Reporting) | NaN | N/A |
| 16 | W3SVC (IIS World Wide Web) | Disabled | N/A (not on workstations) |
| 17 | XblAuthManager..XblNetworkingSvc | Disabled | SC-149 |

**Verdict:** Most critical services are covered by SC-073/SC-074/SC-149 batch validators. Minor services (irmon, MapsBroker, PushToInstall, simptcp) are not individually checked. **Low-priority gap.**

---

## Gaps -- Server 2025 (additional to Win11)

The Server 2025 baseline shares most settings with Win11 but adds server-specific settings.

### F. Server-specific settings NOT in current catalog

| # | Policy Path | Setting Name | Server Baseline Value | Registry Path | Severity |
|---|---|---|---|---|---|
| 1 | DC: LDAP server channel binding | Always | `NTDS\Parameters!LdapEnforceChannelBinding` | Critical (DC only) |
| 2 | DC: LDAP server signing requirements | Require signing | `NTDS\Parameters!LDAPServerIntegrity` | Critical (DC only) |
| 3 | DC: LDAP server signing enforcement | Enabled | `NTDS\Parameters!LDAPServerEnforceIntegrity` | Critical (DC only) |
| 4 | DC: Refuse default machine account password | Enabled | `SAM!RefuseDefaultMachinePwd` | High (DC only) |
| 5 | DC: Allow vulnerable Netlogon connections | Not configured (deny) | `Netlogon\Parameters!VulnerableChannelAllowList` | Medium (DC only) |
| 6 | Network security: Restrict NTLM: Audit authentication in this domain | Enable all | Not checked | Medium |
| 7 | Network security: Restrict NTLM: Audit incoming NTLM traffic | Enable auditing | Not checked | Medium |
| 8 | AppLocker (DC specific) | Exe/Script/Installer/DLL rules | GP-based, not registry | Medium |

**Note:** Items 1-5 are Domain Controller-specific settings. The current catalog has no DC-specific controls (DC19/22/25 are Phase 2 roadmap). These are expected gaps. Items 6-7 are NTLM audit settings that would be valuable for both servers and workstations.

---

## Already Covered (summary)

### By control type and source

| Coverage Area | Control IDs | Count |
|---|---|---|
| Scored command functions (batch validators) | SC-001 to SC-161 | 161 |
| Individual registry checks (baseline) | BL-0001 to BL-0469 | ~379 active |
| New engine controls (eventlog, certstore, bitlocker, tpm) | BL-0470 to BL-0486 | 17 |
| Server-specific controls | SRV-001 to SRV-080 | 80 |
| Antivirus/Defender controls | AV-001 to AV-040 | 40 |
| **Total active controls** | | **~677** |

### Key covered areas (match to MS baseline)

- **Account/Password Policy:** SC-006, SC-011, SC-138, SC-139 -- password length, complexity, history, lockout
- **Audit Policy:** SC-009, SC-062, SC-140 -- 24 advanced audit subcategories (batch)
- **Firewall:** SC-010, SC-141 -- all 3 profiles, states, logging
- **SMB Security:** SC-001, SC-016, SC-017, BL-0007, BL-0018-0024, SRV-001..SRV-012
- **RDP Security:** SC-015, SC-076, SC-146, SRV-013..SRV-020
- **UAC:** SC-014, SC-130, SC-137
- **Credential Protection:** SC-052, SC-053, SC-054, BL-0009, BL-0011, SRV-064-066
- **Defender:** AV-001..AV-040 (40 controls), SC-005, SC-145
- **ASR Rules:** AV-016..AV-025 (10 ASR rules individually)
- **BitLocker:** SC-004, SC-084, BL-0480..BL-0483
- **TPM:** BL-0484..BL-0486
- **PowerShell Logging:** SC-059..SC-061, SC-148, SRV-024..SRV-026
- **Network Hardening:** SC-002, SC-003, SC-127, SC-128, BL-0012..BL-0028
- **Printer Hardening:** SC-030, SC-129, BL-0029..BL-0035
- **TLS/SSL:** SC-032, SC-085, SC-150, SRV-032..SRV-033
- **Event Log:** SC-020, SC-136, BL-0470..BL-0473
- **Certificate Store:** BL-0474..BL-0479
- **LAPS:** SC-125, BL-0001..BL-0004
- **IE/Edge:** SC-142..SC-144
- **M365 Office:** SC-158, SC-160
- **Services Hardening:** SC-073, SC-074, SC-149

---

## True Gaps -- Priority List for Seed SQL Generation

These are settings from the Microsoft baselines that are NOT covered by ANY existing control (neither batch validators nor individual registry checks).

### Critical Priority (should add immediately)

| # | Setting | Registry Path | Baseline Value | Why Critical |
|---|---|---|---|---|
| 1 | Kerberos encryption types | `Lsa\Kerberos\Parameters!SupportedEncryptionTypes` | AES128+AES256 (0x7FFFFFF8) | Prevents downgrade to RC4/DES |
| 2 | Device Guard: Kernel Shadow Stacks | `DeviceGuard!ConfigureKernelShadowStacks` | Enabled in enforcement mode | New Win11 25H2 feature |
| 3 | Device Guard: Secure Launch | `DeviceGuard!ConfigureSystemGuardLaunch` | Enabled | Hardware root of trust |
| 4 | Device Guard: Platform Security Level | `DeviceGuard!RequirePlatformSecurityFeatures` | Secure Boot + DMA | VBS prerequisite |
| 5 | LSASS: RunAsPPL = 2 (UEFI lock) | `Lsa!RunAsPPL` | 2 (not just 1) | Baseline increased from 1 to 2 |
| 6 | CredSSP Oracle Remediation | `CredSSP\Parameters!AllowEncryptionOracle` | 0 (Force Updated Clients) | Prevents downgrade attack |
| 7 | Cloud Block Level | `Windows Defender\MpEngine!MpCloudBlockLevel` | 2 (High) | Stronger cloud protection |
| 8 | Domain member: Secure channel signing/encryption (4 settings) | `Netlogon\Parameters!RequireSignOrSeal/SealSecureChannel/SignSecureChannel/RequireStrongKey` | Enabled | Domain security fundamentals |

### High Priority (should add soon)

| # | Setting | Registry Path | Baseline Value |
|---|---|---|---|
| 9 | SMB client signing required | `LanmanWorkstation\Parameters!RequireSecuritySignature` | 1 |
| 10 | SMB client: no plaintext password | `LanmanWorkstation\Parameters!EnablePlainTextPassword` | 0 |
| 11 | WinRM Client: Basic auth disabled | `WinRM\Client!AllowBasic` | 0 |
| 12 | WinRM Client: Unencrypted traffic disabled | `WinRM\Client!AllowUnencryptedTraffic` | 0 |
| 13 | WinRM Service: Unencrypted traffic disabled | `WinRM\Service!AllowUnencryptedTraffic` | 0 |
| 14 | WinRM Service: Disallow Digest | `WinRM\Service!AllowAutoConfig` / Digest related | Disabled |
| 15 | WinRM Service: DisableRunAs | `WinRM\Service!DisableRunAs` | 1 |
| 16 | UAC: ConsentPromptBehaviorUser | `System!ConsentPromptBehaviorUser` | 0 (Auto deny) |
| 17 | UAC: EnableInstallerDetection | `System!EnableInstallerDetection` | 1 |
| 18 | UAC: EnableSecureUIAPaths | `System!EnableSecureUIAPaths` | 1 |
| 19 | UAC: PromptOnSecureDesktop | `System!PromptOnSecureDesktop` | 1 |
| 20 | UAC: EnableVirtualization | `System!EnableVirtualization` | 1 |
| 21 | Restrict Remote SAM | `Lsa!RestrictRemoteSAM` | O:BAG:BAD:(A;;RC;;;BA) |
| 22 | Everyone includes anonymous | `Lsa!EveryoneIncludesAnonymous` | 0 |
| 23 | Windows Installer: AlwaysInstallElevated | `Installer!AlwaysInstallElevated` | 0 (Disabled) |
| 24 | NTLM restrict audit (2 settings) | `Lsa\MSV1_0!AuditReceivingNTLMTraffic` + domain audit | Enable |
| 25 | Extended cloud check timeout | `Windows Defender\MpEngine!MpBafsExtendedTimeout` | 50 |
| 26 | Defender: DisableRoutinelyTakingAction | `Windows Defender!DisableRoutinelyTakingAction` | 0 |
| 27 | Local setting override for MAPS | `Windows Defender\SpyNet!LocalSettingOverrideSpyNetReporting` | 0 |
| 28 | Prevent security questions for local accounts | `CredUI!NoLocalPasswordResetQuestions` | 1 |

### Medium Priority (nice to have)

| # | Setting | Registry Path | Baseline Value |
|---|---|---|---|
| 29 | Smart card removal behavior | `Winlogon!ScRemoveOption` | Lock Workstation |
| 30 | ELAM Boot-Start Driver Init | `EarlyLaunch!DriverLoadPolicy` | 3 (Good+Unknown+Bad-but-critical) |
| 31 | Autoplay: NoAutoplayfornonVolume | `Explorer!NoAutoplayfornonVolume` | 1 |
| 32 | Sudo disabled | `Sudo!Enabled` | 0 |
| 33 | Search: no encrypted file indexing | `Windows Search!AllowIndexingEncryptedStoresOrItems` | 0 |
| 34 | Game DVR disabled | `GameDVR!AllowGameDVR` | 0 |
| 35 | Windows Ink Workspace disabled | `WindowsInkWorkspace!AllowWindowsInkWorkspace` | 0 |
| 36 | Cloud content/consumer experiences (3 settings) | `CloudContent!Disable*` | Disabled |
| 37 | Disallow Autoplay for non-volume devices | Various Explorer keys | Enabled |
| 38 | BitLocker: deny write to unprotected removable drives | `FVE!RDVDenyWriteAccess` | 1 |
| 39 | MSA Optional for modern apps | `System!MSAOptional` | 1 |
| 40 | Enumerate administrators on elevation | `CredUI!EnumerateAdministrators` | 0 |
| 41 | Restrict null sessions to Named Pipes/Shares (workstation scope) | `LanManServer\Parameters!RestrictNullSessAccess` | 1 |
| 42 | Service: irmon, MapsBroker, PushToInstall, simptcp disabled | Service startup type | Disabled |

---

## Recommendations

1. **Immediate action (seed_012):** Create ~8 new controls for the Critical Priority gaps. These are settings that the MS baseline explicitly requires and that no existing Kryoss control checks, even indirectly.

2. **Short-term (seed_013):** Create ~20 new controls for High Priority gaps. Many of these are UAC sub-settings, WinRM client settings, and Defender advanced settings that complement existing batch validators.

3. **Value update:** Update BL-0009 (RunAsPPL) expected value from 1 to 2 to match the Win11 25H2 baseline requirement for UEFI-locked LSA protection.

4. **Workstation scope expansion:** Several settings currently only checked for servers (SRV-001 SMB signing, SRV-003 null session restriction, SRV-011 LDAP signing) should have workstation-scoped equivalents.

5. **DC controls (Phase 2):** LDAP channel binding, LDAP signing enforcement, Netlogon secure channel settings, and AppLocker rules are DC-specific and should be added when DC19/22/25 platforms are scoped.

6. **Granularity consideration:** The SC-* batch validators provide functional coverage for many settings, but individual BL-* controls give better reporting granularity. Consider whether the ~27 security option sub-settings covered by SC-137 should be broken out into individual controls (as was done with BL-0001..BL-0035 for other batch validators).

---

## Appendix: Control counts by file

| File | Prefix | Count | Engine Types |
|---|---|---|---|
| seed_004_controls.sql | SC-001..SC-161 | 161 | command (scored functions) |
| seed_004_controls.sql | BL-0001..BL-0469 | ~379 active (91 legacy deactivated) | registry, auditpol, firewall, service, netaccount, secedit |
| seed_008_new_engine_controls.sql | BL-0470..BL-0486 | 17 | eventlog, certstore, bitlocker, tpm |
| seed_010_server_controls.sql | SRV-001..SRV-080 | 80 | registry, command, service |
| seed_011_antivirus_controls.sql | AV-001..AV-040 | 40 | registry, command, service |
