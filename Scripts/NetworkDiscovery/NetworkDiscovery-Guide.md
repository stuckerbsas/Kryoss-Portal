# Network Discovery & Remote Validation Guide

## Overview

The Network Discovery module provides a complete solution for discovering and validating devices on your network for remote security assessments. It consists of three main components that work together to identify Windows computers capable of running remote security assessments.

## Architecture

```
┌─────────────────────┐    ┌──────────────────────┐    ┌─────────────────────────┐
│   Get-NetworkDevices │───▶│ Test-RemoteExecution │───▶│ Ready for Assessment    │
│                     │    │                      │    │                         │
│ • Auto-detect subnet│    │ • Test PsExec access │    │ • Validated Windows PCs │
│ • ARP + Ping sweep  │    │ • Verify credentials │    │ • Remote execution OK   │
│ • Find all devices  │    │ • Identify Windows   │    │ • Security assessment  │
└─────────────────────┘    └──────────────────────┘    └─────────────────────────┘
```

## Components

### 1. **Get-NetworkDevices.ps1** - Network Discovery
**Purpose:** Automatically discovers all devices on your local network subnet

**Key Features:**
- **Auto-detects network configuration** (IP, subnet mask, network range)
- **Multiple discovery methods** (ARP table scan, ping sweep, or both)
- **Hostname resolution** attempts (DNS + NetBIOS)
- **Export capabilities** (CSV + JSON with metadata)

**Usage:**
```powershell
# Quick ARP scan (fastest)
.\Get-NetworkDevices.ps1 -DiscoveryMethod ARP

# Comprehensive discovery
.\Get-NetworkDevices.ps1 -DiscoveryMethod Both -IncludeHostnames -ExportResults

# Custom ping sweep
.\Get-NetworkDevices.ps1 -DiscoveryMethod Ping -Timeout 1 -MaxConcurrent 100
```

### 2. **Test-RemoteExecution.ps1** - PsExec Validation
**Purpose:** Tests which discovered devices can accept remote script execution

**Key Features:**
- **PsExec connectivity testing** with timeout and error handling
- **Credential validation** for remote access
- **Windows computer identification** vs. other devices
- **Parallel testing** for efficiency
- **Detailed error categorization** (access denied, unreachable, etc.)

**Usage:**
```powershell
# Test devices from discovery JSON
.\Test-RemoteExecution.ps1 -InputFile "NetworkDiscovery_20260403_123456.json"

# Test specific devices
$devices = @(@{IPAddress="192.168.1.10"; MACAddress="aa-bb-cc-dd-ee-ff"})
.\Test-RemoteExecution.ps1 -InputDevices $devices -ExportResults

# With custom credentials
$cred = Get-Credential
.\Test-RemoteExecution.ps1 -InputFile "devices.json" -Credential $cred
```

### 3. **Invoke-NetworkAssessment.ps1** - Complete Workflow
**Purpose:** Combines discovery + validation in a single workflow

**Key Features:**
- **End-to-end automation** from discovery to validation
- **Integrated reporting** with recommendations
- **Export coordination** with timestamped files
- **Summary statistics** and next steps

**Usage:**
```powershell
# Basic network assessment
.\Invoke-NetworkAssessment.ps1

# Complete assessment with validation
.\Invoke-NetworkAssessment.ps1 -TestRemoteExecution -ExportResults

# Comprehensive scan with exports
.\Invoke-NetworkAssessment.ps1 -DiscoveryMethod Both -TestRemoteExecution -OutputPath "C:\Reports"
```

## Workflow Examples

### Scenario 1: Quick Network Discovery
**Goal:** Find all devices on network quickly

```powershell
# 1. Run ARP discovery (1-2 seconds)
.\Get-NetworkDevices.ps1 -DiscoveryMethod ARP

# Result: List of all recently active devices
```

### Scenario 2: Complete Network Mapping
**Goal:** Comprehensive device discovery with hostnames

```powershell
# 1. Full discovery with hostname resolution
.\Get-NetworkDevices.ps1 -DiscoveryMethod Both -IncludeHostnames -ExportResults

# 2. Review exported CSV and JSON files
# Files: NetworkDiscovery_YYYYMMDD_HHMMSS.csv/.json
```

### Scenario 3: Security Assessment Preparation
**Goal:** Identify Windows computers ready for remote assessment

```powershell
# 1. Discover network
.\Get-NetworkDevices.ps1 -DiscoveryMethod ARP -ExportResults

# 2. Test remote execution capability
.\Test-RemoteExecution.ps1 -InputFile "NetworkDiscovery_YYYYMMDD_HHMMSS.json" -ExportResults

# 3. Review validation results
# Files: RemoteValidation_YYYYMMDD_HHMMSS.csv/.json

# 4. Use validated IPs for security assessment
```

### Scenario 4: End-to-End Automation
**Goal:** Complete assessment workflow in one command

```powershell
# Single command for complete assessment
.\Invoke-NetworkAssessment.ps1 -TestRemoteExecution -ExportResults

# Provides:
# - Network discovery results
# - Remote execution validation
# - Summary with recommendations
# - All export files
```

## Understanding Results

### Network Discovery Output
```
NETWORK CONFIG: Detecting local network configuration...
SUCCESS: Network configuration detected
  Interface: Wi-Fi
  IP Address: 192.168.5.132
  Subnet Mask: 255.255.252.0 (/22)           ← Your subnet configuration
  Network Range: 192.168.4.0 - 192.168.7.255 ← All possible IPs
  Total Hosts: 1022                          ← Devices that could exist
  Gateway: 192.168.4.1

Device List:
Hostname                 IP Address       MAC Address       Method      Status
--------                 ----------       -----------       ------      ------
ROUTER-01                192.168.4.1      b4-20-46-4d-9c-ad ARP,Ping    Active
DESKTOP-ABC123           192.168.4.20     0c-ef-15-a8-ae-cd ARP         Active
192.168.4.22             192.168.4.22     42-a2-1b-c0-7c-69 ARP         Active

STATISTICS:
  Total devices: 19                          ← Actually found
  Found by ARP: 19                          ← Recently communicated
  Found by Ping: 8                          ← Currently responsive
  Found by both methods: 8                   ← Most reliable
  With resolved hostnames: 1                 ← DNS/NetBIOS success
```

### Remote Execution Validation Output
```
VALIDATION: Testing PsExec connectivity to 19 devices...

  [1/19] 192.168.4.1 -> UNREACHABLE        ← Router/gateway
  [2/19] 192.168.4.20 -> SUCCESS: DESKTOP-ABC123  ← Windows computer!
  [3/19] 192.168.4.22 -> ACCESS DENIED      ← Windows but wrong credentials
  [4/19] 192.168.4.23 -> UNREACHABLE        ← Printer/IoT device

WINDOWS COMPUTERS READY FOR REMOTE EXECUTION:
IP Address       Hostname                 Response Time
----------       --------                 -------------
192.168.4.20     DESKTOP-ABC123          2.1s
192.168.4.133    LAPTOP-XYZ789           1.8s

SUCCESS: These 2 devices can run remote security assessments!
```

## Result Categories

### Discovery Status
- **Active** - Found by ARP (recently communicated)
- **Reachable** - Found by ping (currently responsive)

### Remote Execution Status
- **Success** - Windows computer, PsExec works, ready for assessment
- **AccessDenied** - Windows computer but authentication failed
- **Unreachable** - Network connection failed (firewall/offline/non-Windows)
- **Error** - Other connection or execution errors

## Integration with Security Assessment

Once you have validated Windows computers, use them with the main assessment script:

```powershell
# Example: Run security assessment on validated devices
$validatedDevices = @("192.168.4.20", "192.168.4.133")

foreach ($ip in $validatedDevices) {
    .\Invoke-KryossAssessment.ps1 -ClientName "NetworkScan" -TargetIP $ip
}
```

## Performance Considerations

### Network Size Impact
| Subnet | Total IPs | ARP Time | Ping Time* | Recommended Method |
|--------|-----------|----------|------------|-------------------|
| /24    | 254       | ~1 sec   | ~30 sec    | Both             |
| /22    | 1022      | ~1 sec   | ~2 min     | ARP first        |
| /20    | 4094      | ~1 sec   | ~8 min     | ARP only         |

*With default timeout settings

### Optimization Tips
- **Use ARP first** for large networks (fastest)
- **Increase MaxConcurrent** for faster ping sweeps on fast networks
- **Decrease Timeout** for faster ping sweeps (but may miss slow devices)
- **Run during business hours** for maximum device discovery

## Troubleshooting

### No Devices Found
- Check network connectivity
- Verify subnet detection is correct
- Try both discovery methods
- Check if ARP table has entries: `arp -a`

### Remote Execution Fails
- Verify credentials have admin rights on target machines
- Check Windows Firewall on target devices
- Ensure File and Printer Sharing is enabled
- Test manual PsExec: `psexec \\TARGET-IP cmd`

### Performance Issues
- Reduce MaxConcurrent for slower networks
- Increase timeout for slower devices
- Use ARP-only discovery for large subnets
- Run validation with smaller device batches

## Security Notes
- Credentials are only used during the session (not stored)
- PsExec requires admin rights on target systems
- All remote tests use read-only commands (hostname)
- Network discovery uses passive methods (ARP) and standard ping
- Export files may contain sensitive network information - secure appropriately