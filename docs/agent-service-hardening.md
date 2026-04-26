# KryossAgent Windows Service — Security Hardening Notes

## Why SYSTEM?

The agent runs as `LocalSystem` because it needs:

- **WMI queries** (`Win32_OperatingSystem`, `Win32_Tpm`, `MSFT_PhysicalDisk`, `Win32_EncryptableVolume`) — require admin or SYSTEM
- **Protected registry keys** (`HKLM\SYSTEM\CurrentControlSet`, `HKLM\SAM`, Security policy keys) — SYSTEM-only ACLs
- **Event Log configuration** (`EventLogConfiguration` resize, `AuditQuerySystemPolicy` P/Invoke) — requires `SeSecurityPrivilege`
- **Service enumeration** (`ServiceController`) — full access needs SYSTEM
- **LDAP/AD queries** (`System.DirectoryServices`) — machine account context for domain-joined hosts
- **Protocol audit** (NTLM/SMBv1 event log configuration) — SYSTEM-only registry writes

## Attack surface

Running as SYSTEM means a compromised agent binary has full machine control. Mitigations:

1. **No Process.Start** — zero shell/cmd/powershell execution since v1.4.0; all data via registry/WMI/P/Invoke/.NET APIs
2. **SPKI pinning** — TLS connections pinned to known server certificate (H6)
3. **HMAC signing** — every request cryptographically authenticated
4. **Hardware fingerprint** — agent bound to specific hardware via SHA-256 hwid
5. **Binary integrity** — self-updater requires SHA256 hash match (H5)
6. **Registry ACL** — config stored under SYSTEM-only ACL (H7)
7. **Remediation whitelist** — only pre-approved registry paths can be modified (H8)

## Virtual service account alternative

A `gMSA` or virtual service account (`NT SERVICE\KryossAgent`) would reduce blast radius. Requirements:

- Custom ACLs on ~15 registry paths (WMI/EventLog/SCHANNEL/policies)
- `SeSecurityPrivilege` delegation for audit policy reads
- Domain join for AD/LDAP queries (or skip DC engine on standalone)
- `Win32_EncryptableVolume` namespace requires admin — would need separate WMI proxy

**Decision:** Stay with SYSTEM for now. The zero-Process.Start architecture + HMAC + SPKI pinning makes the risk acceptable for MSP environments where the agent is deployed via GPO/RMM to trusted endpoints.
