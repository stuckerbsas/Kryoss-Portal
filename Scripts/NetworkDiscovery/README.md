# Network Discovery Module

## Overview
The Network Discovery Module is a comprehensive PowerShell tool that automatically detects your local network configuration and discovers all active devices on the same subnet.

## Features

### 🔍 **Automatic Network Detection**
- Detects local IP address and subnet mask
- Supports any subnet size (/22, /24, /20, etc.)
- Calculates network range automatically
- Shows total possible hosts

### 📡 **Multiple Discovery Methods**
- **ARP Discovery**: Fast, uses existing ARP table entries
- **Ping Sweep**: Comprehensive, tests all IPs in subnet range
- **Both**: Combines ARP + Ping for maximum accuracy

### 🏷️ **Hostname Resolution**
- DNS lookup attempts
- NetBIOS name resolution fallback
- Graceful fallback to IP addresses

### 📊 **Export Capabilities**
- CSV export for spreadsheet analysis
- JSON export with full network metadata
- Timestamped files for record keeping

## Usage Examples

### Basic Discovery (ARP Only)
```powershell
.\Get-NetworkDevices.ps1
```

### Comprehensive Discovery (Both Methods)
```powershell
.\Get-NetworkDevices.ps1 -DiscoveryMethod Both
```

### Fast Ping Sweep
```powershell
.\Get-NetworkDevices.ps1 -DiscoveryMethod Ping -Timeout 1 -MaxConcurrent 100
```

### With Hostname Resolution and Export
```powershell
.\Get-NetworkDevices.ps1 -IncludeHostnames -ExportResults -OutputPath "C:\Reports"
```

## Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `DiscoveryMethod` | ARP, Ping, or Both | Both |
| `Timeout` | Ping timeout in seconds | 2 |
| `MaxConcurrent` | Max concurrent ping operations | 50 |
| `IncludeHostnames` | Attempt hostname resolution | False |
| `ExportResults` | Export to CSV and JSON | False |
| `OutputPath` | Export directory | Script directory |

## Sample Output

```
============================================
   Network Discovery Module - TeamLogic IT
============================================

NETWORK CONFIG: Detecting local network configuration...
SUCCESS: Network configuration detected
  Interface: Wi-Fi
  IP Address: 192.168.5.132
  Subnet Mask: 255.255.252.0 (/22)
  Network Range: 192.168.4.0 - 192.168.7.255
  Total Hosts: 1022
  Gateway: 192.168.4.1

ARP DISCOVERY: Scanning ARP table for active devices...
  [ARP] Found: 192.168.4.1 (b4-20-46-4d-9c-ad)
  [ARP] Found: 192.168.4.20 (0c-ef-15-a8-ae-cd)
  ... (19 devices found)

PING DISCOVERY: Performing ping sweep of subnet...
  [PING] Found: 192.168.4.1 (5ms)
  ... (8 devices responsive)

============================================
             DISCOVERY RESULTS
============================================

Device List:
Hostname                 IP Address       MAC Address       Method      Status
--------                 ----------       -----------       ------      ------
ROUTER-01                192.168.4.1      b4-20-46-4d-9c-ad ARP,Ping    Active
192.168.4.20             192.168.4.20     0c-ef-15-a8-ae-cd ARP         Active

STATISTICS:
  Total devices: 19
  Found by ARP: 19
  Found by Ping: 8  
  Found by both methods: 8
  With resolved hostnames: 1
```

## Performance Notes

### ARP Discovery
- **Speed**: Very fast (~1-2 seconds)
- **Coverage**: Only devices that have recently communicated
- **Accuracy**: High for active devices
- **Best for**: Quick scans, recent activity detection

### Ping Sweep  
- **Speed**: Depends on network size and settings
- **Coverage**: All possible IPs in subnet
- **Accuracy**: High for responsive devices
- **Best for**: Comprehensive discovery, finding all devices

### Both Methods
- **Speed**: Moderate (ARP + reduced ping sweep)
- **Coverage**: Maximum - combines both approaches
- **Accuracy**: Highest
- **Best for**: Complete network mapping

## Network Size Examples

| Subnet | Mask | Total IPs | Ping Time* |
|--------|------|-----------|------------|
| /24 | 255.255.255.0 | 254 | ~30 sec |
| /22 | 255.255.252.0 | 1022 | ~2 min |
| /20 | 255.255.240.0 | 4094 | ~8 min |

*Approximate time for full ping sweep with default settings

## Integration

This module is designed to work independently or as part of the larger Kryoss security assessment suite. It can be called from other scripts or used standalone for network mapping tasks.

## Author
**TeamLogic IT** - Part of the Kryoss project by Geminis Computer S.A.