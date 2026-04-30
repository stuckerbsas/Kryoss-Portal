# Hardening Scripts

This directory contains PowerShell scripts for system hardening according to TeamLogic IT security standards.

## Scripts

### Disable-InsecureProtocols.ps1
Primary hardening script that disables:
- SMBv1 protocol and feature
- LLMNR (Link-Local Multicast Name Resolution)
- NetBIOS over TCP/IP

Designed for silent deployment via NinjaRMM.

### Test-InsecureProtocols.ps1
Verification script to check the status of insecure protocols.

## Deployment

These scripts are designed to run silently as SYSTEM via NinjaRMM with appropriate exit codes:
- `0` = Success
- `1` = Error
- `2` = Warning

All changes are logged to `C:\ProgramData\TeamLogicIT\Logs\`