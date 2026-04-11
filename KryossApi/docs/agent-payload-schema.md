# Kryoss Agent Payload Schema

**Version:** 1.1
**Status:** Draft
**Updated:** 2026-04-08 (added HIPAA refinements: mfa, event_logs, backup_posture)

## Core principle: the agent is dumb

The agent collects **raw state** and ships it to the portal. It does NOT:
- Evaluate PASS/FAIL
- Calculate scores or grades
- Decide severity
- Know which framework a check belongs to
- Apply business rules

The portal holds the rules (in `control_defs.check_json`) and evaluates the raw data server-side. Rules can change, new controls can be added, existing controls can be retuned — all without rescanning machines, because the raw state is persisted in `machine_snapshots.raw_*` columns.

## Payload shape (POST /api/v1/results)

```jsonc
{
  "schema_version": "1.0",
  "agent_id": "uuid",
  "agent_version": "1.0.0",
  "run_id": "uuid",             // generated client-side, becomes assessment_runs.id
  "assessment_id": 1,            // from enrollment config
  "started_at": "2026-04-08T15:30:00Z",
  "completed_at": "2026-04-08T15:30:04Z",
  "duration_ms": 4200,

  // ─────────────────────────────────────────────────────────
  // control_results: raw answers to the ~630 atomic checks
  // (one entry per control_id the agent was told to run;
  //  Phase 1 workstation scope = 630 active controls)
  // ─────────────────────────────────────────────────────────
  "control_results": [
    { "control_id": "BL-0001", "exists": true,  "value": 1,  "reg_type": "REG_DWORD" },
    { "control_id": "BL-0002", "exists": false, "value": null },
    { "control_id": "BL-0301", "profile": "Domain",  "property": "Enabled", "value": true },
    { "control_id": "BL-0400", "setting_name": "MinimumPasswordLength", "value": "14" },
    { "control_id": "BL-0500", "service_name": "XblGameSave", "exists": true, "start_type": "Disabled", "status": "Stopped" },
    { "control_id": "SC-001",  "raw": { "smb1_feature_state": "Disabled", "smb1_protocol_enabled": false } }
  ],

  // ─────────────────────────────────────────────────────────
  // raw_hardware: stored verbatim in machine_snapshots.raw_hardware
  // ─────────────────────────────────────────────────────────
  "raw_hardware": {
    "system": {
      "manufacturer": "Dell Inc.",
      "model": "OptiPlex 7090",
      "serial_number": "ABC1234",
      "system_sku": "0A1B",
      "chassis_type": "Desktop",
      "uuid": "...",
      "purchase_date": null,
      "warranty_end": null
    },
    "bios": {
      "vendor": "Dell Inc.",
      "version": "1.20.0",
      "release_date": "2024-08-15",
      "smbios_version": "3.3",
      "mode": "UEFI",
      "firmware_type": "UEFI"
    },
    "cpu": [{
      "name": "Intel(R) Core(TM) i7-12700 CPU @ 2.10GHz",
      "manufacturer": "GenuineIntel",
      "architecture": "x64",
      "cores_physical": 12,
      "cores_logical": 20,
      "max_clock_mhz": 4900,
      "l2_cache_kb": 12288,
      "l3_cache_kb": 25600,
      "virtualization_enabled": true,
      "vt_x": true,
      "vt_d": true,
      "sgx": false,
      "aes_ni": true
    }],
    "ram": {
      "total_gb": 32,
      "installed_gb": 32,
      "max_capacity_gb": 128,
      "slots_total": 4,
      "slots_used": 2,
      "modules": [
        { "slot": "DIMM1", "size_gb": 16, "speed_mhz": 3200, "manufacturer": "Micron", "part_number": "...", "type": "DDR4", "ecc": false },
        { "slot": "DIMM2", "size_gb": 16, "speed_mhz": 3200, "manufacturer": "Micron", "part_number": "...", "type": "DDR4", "ecc": false }
      ]
    },
    "disks": [
      {
        "device_id": "\\\\.\\PhysicalDrive0",
        "model": "Samsung SSD 980 1TB",
        "serial_number": "...",
        "size_gb": 953,
        "media_type": "SSD",
        "bus_type": "NVMe",
        "health_status": "Healthy",
        "smart": {
          "reallocated_sectors": 0,
          "power_on_hours": 2134,
          "power_cycles": 478,
          "temperature_c": 41,
          "percent_used": 3,
          "available_spare_pct": 100,
          "critical_warning": 0,
          "data_units_read": 12345678,
          "data_units_written": 9876543
        },
        "partitions": [
          { "letter": "C:", "label": "Windows", "size_gb": 950, "free_gb": 412, "file_system": "NTFS", "encrypted": true, "encryption_method": "XTS-AES 256" }
        ]
      }
    ],
    "gpu": [
      {
        "name": "NVIDIA GeForce RTX 3060",
        "driver_version": "546.33",
        "driver_date": "2024-11-14",
        "vram_mb": 12288,
        "adapter_ram_bytes": 12884901888,
        "pnp_device_id": "..."
      }
    ],
    "monitors": [
      { "name": "Dell U2723QE", "manufacturer": "Dell", "size_inches": 27, "native_resolution": "3840x2160", "serial": "..." }
    ],
    "battery": {
      "present": true,
      "design_capacity_mwh": 58000,
      "full_charge_capacity_mwh": 54200,
      "cycle_count": 185,
      "health_pct": 93
    },
    "peripherals": {
      "usb_devices_count": 7,
      "printers": [ { "name": "HP LaserJet M404", "driver": "HP Universal Printing PCL 6", "is_default": true, "is_network": true } ]
    }
  },

  // ─────────────────────────────────────────────────────────
  // raw_security_posture: stored in machine_snapshots.raw_security_posture
  // Everything security-hardware-related the portal may care about
  // ─────────────────────────────────────────────────────────
  "raw_security_posture": {
    "tpm": {
      "present": true,
      "enabled": true,
      "activated": true,
      "owned": true,
      "spec_version": "2.0, 0, 1.59",
      "manufacturer_id": "IFX",
      "manufacturer_version": "7.2.0.1",
      "physical_presence_version": "1.3"
    },
    "secure_boot": {
      "enabled": true,
      "ueki_mode": "UEFI",
      "setup_mode": false,
      "platform_key_present": true
    },
    "bitlocker": {
      "feature_installed": true,
      "volumes": [
        {
          "mount_point": "C:",
          "volume_type": "OperatingSystem",
          "protection_status": "On",
          "lock_status": "Unlocked",
          "encryption_method": "XtsAes256",
          "encryption_percentage": 100,
          "auto_unlock_enabled": false,
          "key_protectors": ["Tpm","RecoveryPassword","TpmPin"]
        }
      ]
    },
    "credential_guard": {
      "running": true,
      "configured": "Enabled",
      "security_services_running": ["CredentialGuard","HVCI"]
    },
    "hvci": {
      "running": true,
      "configured": "Enabled"
    },
    "memory_integrity": {
      "enabled": true,
      "license": "Enabled"
    },
    "kernel_dma_protection": {
      "enabled": true
    },
    "system_guard": {
      "enabled": true
    },
    "defender": {
      "am_service_enabled": true,
      "antispyware_enabled": true,
      "antivirus_enabled": true,
      "real_time_protection_enabled": true,
      "behavior_monitor_enabled": true,
      "ioav_protection_enabled": true,
      "nis_enabled": true,
      "on_access_protection_enabled": true,
      "tamper_protection_enabled": true,
      "cloud_protection_enabled": true,
      "asr_rules": {
        "enabled_count": 12,
        "rules": [
          { "id": "56a863a9-875e-4185-98a7-b882c64b5ce5", "name": "Abuse of exploited vulnerable signed drivers", "action": "Block" },
          { "id": "3b576869-a4ec-4529-8536-b80a7769e899", "name": "Block Office apps creating child processes", "action": "Block" }
        ]
      },
      "engine_version": "1.1.23100.2009",
      "signature_version": "1.401.2841.0",
      "signature_age_days": 1,
      "last_full_scan": "2026-04-01T03:00:00Z",
      "last_quick_scan": "2026-04-08T03:00:00Z"
    },
    "wdac_applocker": {
      "wdac_policies_active": ["Microsoft Windows Driver Policy"],
      "applocker_collections": {
        "Exe":    { "enforcement_mode": "Enabled", "rule_count": 12 },
        "Script": { "enforcement_mode": "AuditOnly", "rule_count": 4 },
        "Msi":    { "enforcement_mode": "NotConfigured", "rule_count": 0 },
        "Dll":    { "enforcement_mode": "NotConfigured", "rule_count": 0 },
        "Appx":   { "enforcement_mode": "Enabled", "rule_count": 2 }
      }
    },
    "exploit_protection": {
      "system_level": { "dep": "On", "aslr": "On", "cfg": "On", "sehop": "On" },
      "per_app_count": 42
    },

    // ─── HIPAA refinement block A — MFA / Windows Hello / Smart Card ───
    // Feeds control_defs BL-0445..BL-0450
    // HIPAA §164.312(d) Person or Entity Authentication
    "mfa": {
      "whfb": {
        "policy_enabled": true,           // HKLM\SOFTWARE\Policies\Microsoft\PassportForWork\Enabled
        "require_security_device": true,  // ...\RequireSecurityDevice (TPM required)
        "minimum_pin_length": 6,          // ...\PINComplexity\MinimumPINLength
        "biometrics_enabled": true,       // ...\Biometrics\Enabled
        "provisioned_users_count": 2,     // non-empty subfolders under C:\Windows\ServiceProfiles\LocalService\AppData\Local\Microsoft\Ngc
        "ngc_container_exists": true
      },
      "smart_card": {
        "service_name": "SCardSvr",
        "service_start_type": "Manual",   // Disabled | Manual | Automatic
        "service_status": "Running",
        "readers_detected": 1,            // Get-PnpDevice -Class SmartCardReader
        "certificates_in_user_my_store": 0
      },
      "device_join": {
        // Full dsregcmd /status output captured verbatim, portal parses
        "azure_ad_joined": true,
        "domain_joined": false,
        "workplace_joined": false,
        "device_auth_status": "SUCCESS",
        "wam_default_set": true,
        "ngc_set": true,
        "can_reach_dsr": true,
        "tenant_name": "contoso.onmicrosoft.com",
        "tenant_id": "00000000-0000-0000-0000-000000000000",
        "raw_dsregcmd": "+----------------------------------------------------------------------+\n| Device State                                                         |\n..."
      },
      "fido2": {
        "security_keys_registered": 0    // query via Get-WinBioEnrollments or WebAuthN registry, best-effort
      }
    },

    // ─── HIPAA refinement block B — Event log retention ───
    // Feeds control_defs BL-0451..BL-0459
    // HIPAA §164.312(b) Audit Controls
    "event_logs": {
      "security": {
        // Registry-side (policy-declared)
        "max_size_bytes": 201326592,      // HKLM\SYSTEM\CurrentControlSet\Services\EventLog\Security\MaxSize
        "retention_mode": 0,               // 0=overwrite as needed, 1=overwrite when older than N days, -1=never overwrite
        "auto_backup_log_files": 1,        // ...\AutoBackupLogFiles (0 or 1)
        "restrict_guest_access": 1,        // ...\RestrictGuestAccess
        // Effective-side (what wevtutil actually reports)
        "effective_max_size_bytes": 201326592,
        "effective_retention": "false",    // 'true' = do not overwrite, 'false' = overwrite
        "effective_log_path": "%SystemRoot%\\System32\\Winevt\\Logs\\Security.evtx",
        "effective_is_enabled": true,
        "current_size_bytes": 18874368,
        "record_count": 45123,
        "oldest_record_timestamp": "2026-03-28T14:12:01Z",
        "newest_record_timestamp": "2026-04-08T15:29:47Z",
        "sddl": "O:BAG:SYD:(A;;0x7;;;BA)(A;;0x5;;;SO)..."
      },
      "system": {
        "max_size_bytes": 33554432,
        "retention_mode": 0,
        "auto_backup_log_files": 0,
        "effective_max_size_bytes": 33554432,
        "effective_retention": "false",
        "current_size_bytes": 8192000,
        "record_count": 12876,
        "oldest_record_timestamp": "2026-03-30T00:00:00Z",
        "newest_record_timestamp": "2026-04-08T15:29:12Z"
      },
      "application": {
        "max_size_bytes": 33554432,
        "retention_mode": 0,
        "auto_backup_log_files": 0,
        "effective_max_size_bytes": 33554432,
        "effective_retention": "false",
        "current_size_bytes": 6291456,
        "record_count": 9543,
        "oldest_record_timestamp": "2026-03-29T00:00:00Z",
        "newest_record_timestamp": "2026-04-08T15:28:55Z"
      },
      "forwarding": {
        // Optional: if WEF / log forwarding is configured
        "subscription_manager_configured": false,
        "subscription_manager_url": null,
        "wef_service_state": "Stopped"
      }
    },

    // ─── HIPAA refinement block C — Backup posture ───
    // Feeds control_defs BL-0460..BL-0469
    // HIPAA §164.308(a)(7)(ii)(A) Data Backup Plan
    // §164.310(d)(2)(iv) Data Backup and Storage
    "backup_posture": {
      "windows_server_backup": {
        "installed": false,                  // wbadmin.exe present
        "last_successful_backup": null,      // ISO8601 or null
        "last_backup_type": null,            // Full | Incremental
        "last_backup_target": null,
        "last_backup_size_bytes": null,
        "days_since_last_backup": null,
        "scheduled": false,
        "raw_wbadmin_output": null
      },
      "windows_backup_modern": {
        // Win10/11 Backup and Restore (legacy) / Windows Backup app
        "valid_config": false,                // HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WindowsBackup\ValidConfig
        "last_run_result": null,
        "last_successful_run": null,
        "next_run": null
      },
      "vss": {
        "service_state": "Running",          // VSS service
        "shadow_copies": [
          {
            "id": "{00000000-0000-0000-0000-000000000000}",
            "volume": "C:",
            "creation_time": "2026-04-08T06:00:00Z",
            "size_mb": 2048,
            "provider": "Microsoft Software Shadow Copy provider 1.0"
          }
        ],
        "writers": [
          { "name": "BITS Writer",              "state": "Stable", "last_error": "No error" },
          { "name": "Registry Writer",          "state": "Stable", "last_error": "No error" },
          { "name": "System Writer",            "state": "Stable", "last_error": "No error" }
        ],
        "writers_in_error_count": 0
      },
      "third_party_agents": {
        // Presence detection only -- service existence + state.
        // BL-0463..BL-0468 each map to one of these.
        "veeam_endpoint":   { "installed": false, "service_name": "VeeamEndpointBackupSvc", "status": null, "start_type": null },
        "datto":            { "installed": false, "service_name": "CCSDK",                  "status": null, "start_type": null },
        "acronis":          { "installed": false, "service_name": "acronis_mms",            "status": null, "start_type": null },
        "carbonite":        { "installed": false, "service_name": "CarboniteService",       "status": null, "start_type": null },
        "backup_exec":      { "installed": false, "service_name": "BackupExecRPCService",   "status": null, "start_type": null },
        "mozypro":          { "installed": false, "service_name": "MozyProBackup",          "status": null, "start_type": null }
      },
      "cloud_sync_heuristic": {
        // Rough signal only -- presence of cloud drive processes/services.
        // The portal treats these as "file sync" not "backup" but records
        // them so reports can distinguish "has backup" vs "has file sync".
        "onedrive_running": true,
        "dropbox_running": false,
        "google_drive_running": false,
        "box_running": false
      }
    }
  },

  // ─────────────────────────────────────────────────────────
  // raw_software: stored in machine_snapshots.raw_software
  // ─────────────────────────────────────────────────────────
  "raw_software": {
    "collected_at": "2026-04-08T15:30:03Z",
    "installed_programs": [
      {
        "name": "Microsoft Office LTSC Professional Plus 2021",
        "version": "16.0.14332.20647",
        "publisher": "Microsoft Corporation",
        "install_date": "2024-02-14",
        "install_location": "C:\\Program Files\\Microsoft Office",
        "uninstall_string": "...",
        "estimated_size_kb": 2510000,
        "registry_scope": "HKLM64"
      }
    ],
    "store_apps": [
      { "name": "Microsoft.Paint", "version": "11.2405.35.0", "publisher": "Microsoft Corporation", "package_family_name": "Microsoft.Paint_8wekyb3d8bbwe", "install_date": "2024-05-01" }
    ],
    "hotfixes": [
      { "hotfix_id": "KB5036892", "description": "Security Update", "installed_on": "2026-03-12" }
    ],
    "browsers": [
      { "name": "Microsoft Edge", "version": "124.0.2478.51", "install_path": "..." },
      { "name": "Google Chrome", "version": "124.0.6367.62", "install_path": "..." }
    ],
    "office_products": [
      { "product": "Office LTSC Professional Plus 2021", "version": "16.0.14332.20647", "channel": "PerpetualVL2021", "bitness": "x64", "licensing": "KMS" }
    ]
  },

  // ─────────────────────────────────────────────────────────
  // raw_network: stored in machine_snapshots.raw_network
  // ─────────────────────────────────────────────────────────
  "raw_network": {
    "hostname": "PC-CONTABILIDAD",
    "fqdn": "PC-CONTABILIDAD.sfsm.local",
    "domain_role": "MemberWorkstation",
    "nics": [
      {
        "name": "Ethernet",
        "description": "Intel(R) Ethernet Connection I219-LM",
        "mac_address": "AA:BB:CC:DD:EE:FF",
        "ip_addresses": ["192.168.10.42"],
        "subnet_masks": ["255.255.255.0"],
        "gateway": "192.168.10.1",
        "dns_servers": ["192.168.10.10","1.1.1.1"],
        "dhcp_enabled": true,
        "mtu": 1500,
        "link_speed_mbps": 1000,
        "connection_state": "Connected"
      }
    ],
    "firewall_profiles": [
      { "name": "Domain",  "enabled": true,  "default_inbound": "Block", "default_outbound": "Allow", "notify_on_listen": true,  "log_file": "%systemroot%\\system32\\LogFiles\\Firewall\\pfirewall.log", "log_size_kb": 16384, "log_dropped": true, "log_allowed": false },
      { "name": "Private", "enabled": true,  "default_inbound": "Block", "default_outbound": "Allow" },
      { "name": "Public",  "enabled": true,  "default_inbound": "Block", "default_outbound": "Allow" }
    ],
    "open_ports_listening": [
      { "protocol": "TCP", "local_address": "0.0.0.0", "port": 135, "process_name": "svchost.exe", "process_id": 1024 },
      { "protocol": "TCP", "local_address": "0.0.0.0", "port": 3389, "process_name": "svchost.exe", "process_id": 4532 }
    ],
    "smb_shares": [
      { "name": "Users$", "path": "C:\\Users",   "access": "Everyone:Read" }
    ],
    "wifi_profiles": [
      { "ssid": "CorpWiFi", "auth": "WPA2-Enterprise", "encryption": "CCMP", "saved_key": null }
    ]
  },

  // ─────────────────────────────────────────────────────────
  // raw_users: stored in machine_snapshots.raw_users
  // ─────────────────────────────────────────────────────────
  "raw_users": {
    "local_users": [
      {
        "sid": "S-1-5-21-...-500",
        "username": "Administrator",
        "full_name": "",
        "enabled": false,
        "password_required": true,
        "password_changeable": true,
        "password_expires": false,
        "password_last_set": "2021-03-15T00:00:00Z",
        "last_logon": null,
        "is_builtin": true
      }
    ],
    "local_groups": [
      { "sid": "S-1-5-32-544", "name": "Administrators", "members": [
        { "sid": "S-1-5-21-...-500", "name": "Administrator", "source": "Local" },
        { "sid": "S-1-5-21-...-1001", "name": "juan.perez", "source": "ActiveDirectory" }
      ]}
    ],
    "logged_on_users": [
      { "sid": "S-1-5-21-...-1001", "username": "SFSM\\juan.perez", "logon_time": "2026-04-08T08:15:00Z", "logon_type": "Interactive" }
    ]
  }
}
```

## Field-level contract

| Section | Destination column | Required? | Notes |
|---|---|---|---|
| `control_results[]` | `control_results` table rows (one per entry, status eval'd server-side) | yes | Shape varies by check type; portal uses `control_defs.check_json` to know how to interpret |
| `raw_hardware` | `machine_snapshots.raw_hardware` | yes | Portal uses for hardware lifecycle rules (age, RAM min, disk SMART, battery health) |
| `raw_security_posture` | `machine_snapshots.raw_security_posture` | yes | Portal evaluates security-hardware rules (TPM, SecureBoot, BitLocker, Defender state, ASR, WDAC) |
| `raw_software` | `machine_snapshots.raw_software` + `machine_software` table | yes | Portal populates normalized `software` catalog for reporting, keeps full raw for re-evaluation |
| `raw_network` | `machine_snapshots.raw_network` | yes | Portal uses for firewall config checks, open-port audits, network discovery enrichment |
| `raw_users` | `machine_snapshots.raw_users` + `machine_users` table | yes | Portal extracts admin users, stale accounts, password-never-expires into normalized tables |

## What the agent MUST collect (minimum)

The agent implementation MUST populate every top-level raw_* section for every run. Empty sections are allowed only if collection genuinely failed — in which case include an `error` field:

```json
"raw_hardware": { "error": "WMI namespace unavailable" }
```

## How the portal uses this

1. **POST /api/v1/results** receives the payload
2. `ResultsFunction` persists:
   - One `machine_snapshots` row with `raw_*` columns populated verbatim
   - One `assessment_runs` row linked to the snapshot
   - N `control_results` rows (one per `control_results[].control_id`), with status evaluated server-side against `control_defs.check_json`
3. `EvaluationService` walks `raw_hardware` + `raw_security_posture` against rule definitions (stored as additional control_defs of type `custom`) and generates more control_results
4. Normalized tables (`software`, `machine_software`, `machine_users`, `certificates`) are updated from the raw sections for fast querying
5. Reports read the normalized tables; re-evaluation reads the raw snapshots

## Versioning

- `schema_version` at the top of the payload lets the portal support multiple agent generations
- Breaking changes bump the major version
- Additive changes (new fields) are backwards-compatible; portal uses `JSON_VALUE(raw_*, '$.foo.bar')` defensively

## Size expectations

Typical workstation payload (rough):
- control_results (~630 entries): ~85 KB
- raw_hardware: ~8 KB
- raw_security_posture: ~12 KB (up from 6 KB after HIPAA refinements — dsregcmd raw output + event log SDDL add bulk)
- raw_software (200 programs): ~60 KB
- raw_network: ~4 KB
- raw_users: ~3 KB
- **Total: ~175 KB per run** (uncompressed JSON)

The payload is RSA+AES encrypted end-to-end per the enrollment key, so bandwidth impact is minimal even at 1 run/day/machine.

## Collector engine contract (Phase 1)

The 630 active controls map to 7 engines in `control_defs.type`. The
agent must implement these and use `control_defs.check_json` to know
what to read.

| Engine | Count | How the agent reads it |
|---|---|---|
| `registry` | 356 | `Get-ItemProperty HKLM:\<path>` → return `{exists, value, reg_type}` |
| `command` | 211 | `Start-Process <exe> -Args <args>` with timeout, capture stdout + exit code |
| `auditpol` | 24 | `auditpol /get /category:* /r` parsed into per-subcategory JSON (single bulk call, not per-control) |
| `firewall` | 21 | `Get-NetFirewallProfile -All` → one row per profile (Domain/Private/Public) |
| `service` | 11 | `Get-Service <name>` → `{exists, status, start_type}` |
| `netaccount` | 5 | `net accounts` parsed into `{min_password_length, max_password_age, ...}` (single call) |
| `secedit` | 2 | `secedit /export /cfg <tmpfile>` then parse the INF (single call) |

**Batching rule:** `auditpol`, `netaccount` and `secedit` should each
run **once per agent invocation** and the agent splits the result into
per-control_id answers locally. Running them once per control would
make the assessment 10x slower.

## HIPAA refinement implementation notes (v1.1)

The 25 control_defs added in `seed_005` (BL-0445..BL-0469) do not need
new engines — they reuse `registry`, `service`, and `command`. But the
agent must populate the three dedicated blocks under
`raw_security_posture` so the portal can evaluate them holistically:

### A. `raw_security_posture.mfa`

| Data point | Source |
|---|---|
| `whfb.policy_enabled` | `HKLM\SOFTWARE\Policies\Microsoft\PassportForWork\Enabled` |
| `whfb.require_security_device` | same path, `RequireSecurityDevice` |
| `whfb.minimum_pin_length` | `...\PassportForWork\PINComplexity\MinimumPINLength` |
| `whfb.biometrics_enabled` | `...\PassportForWork\Biometrics\Enabled` |
| `whfb.provisioned_users_count` | enumerate subfolders of `C:\Windows\ServiceProfiles\LocalService\AppData\Local\Microsoft\Ngc` (requires SYSTEM context) |
| `smart_card.*` | `Get-Service SCardSvr` + `Get-PnpDevice -Class SmartCardReader` |
| `device_join.*` | `dsregcmd /status` — ship the full stdout under `raw_dsregcmd`, also parse the main flags |
| `fido2.security_keys_registered` | best-effort; leave `0` if no reliable source — don't fail the run |

### B. `raw_security_posture.event_logs`

For each of `Security`, `System`, `Application`:

1. **Policy values** — read from `HKLM\SYSTEM\CurrentControlSet\Services\EventLog\<log>`:
   `MaxSize`, `Retention`, `AutoBackupLogFiles`, `RestrictGuestAccess`
2. **Effective values** — `wevtutil gl <log>`:
   parses to `effective_max_size_bytes`, `effective_retention`,
   `effective_log_path`, `effective_is_enabled`, `sddl`
3. **Live state** — `Get-WinEvent -ListLog <log>`:
   `current_size_bytes` (= `FileSize`), `record_count` (= `RecordCount`),
   timestamps of oldest and newest records

### C. `raw_security_posture.backup_posture`

1. **Windows Server Backup** — only if `wbadmin.exe` exists:
   - `wbadmin get versions` (full stdout under `raw_wbadmin_output`)
   - Parse last version timestamp into `last_successful_backup`
2. **Modern Windows Backup** — registry only:
   `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WindowsBackup`
   values `ValidConfig`, `LastSuccessfulRun`, etc.
3. **VSS** — three calls:
   - `Get-Service VSS` → `service_state`
   - `vssadmin list shadows` → `shadow_copies[]`
   - `vssadmin list writers` → `writers[]` + `writers_in_error_count`
4. **Third-party agents** — for each vendor in the list, call
   `Get-Service <name> -ErrorAction SilentlyContinue`. If it returns
   anything, set `installed: true` and fill `status`, `start_type`.
   Never fail on missing services — this block is detection-only.
5. **Cloud sync heuristic** — check if the known processes are running
   (`OneDrive.exe`, `Dropbox.exe`, `GoogleDriveFS.exe`, `Box.exe`).
   This is a heuristic signal for the reports to distinguish "has
   backup" from "has file sync".

## Phase 1 vs Phase 2 behavior

- **Phase 1 (current):** agent only runs on workstations.
  At startup it reads `Get-CimInstance Win32_OperatingSystem`. If
  `ProductType != 1`, the agent aborts with a clear log message and
  exit code 2 (warning, non-critical) — the catalog for server
  platforms is intentionally empty in Phase 1.
- **Phase 2:** same agent binary, but backend `control_platforms`
  gets server mappings, so `ProductType=3` requests return a real
  catalog. See `docs/phase-roadmap.md`.
