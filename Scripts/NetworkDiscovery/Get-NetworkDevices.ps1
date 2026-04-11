<#
.SYNOPSIS
    Network Discovery Module - Discovers all devices on local subnet

.DESCRIPTION
    Automatically detects the host computer's network configuration (IP and subnet mask)
    and discovers all active devices on the same network segment using multiple discovery methods.

.PARAMETER DiscoveryMethod
    Discovery method to use: ARP, Ping, Both (default)

.PARAMETER Timeout
    Timeout in seconds for ping operations (default: 2)

.PARAMETER MaxConcurrent
    Maximum concurrent ping operations (default: 50)

.PARAMETER IncludeHostnames
    Attempt to resolve hostnames via DNS (slower but more informative)

.PARAMETER ExportResults
    Export results to CSV and JSON files

.PARAMETER OutputPath
    Directory to save export files (default: script directory)

.NOTES
    Author:   TeamLogic IT
    Project:  Kryoss Network Discovery
    Version:  1.0
    Created:  2026-04-03

.EXAMPLE
    .\Get-NetworkDevices.ps1

.EXAMPLE
    .\Get-NetworkDevices.ps1 -DiscoveryMethod ARP -IncludeHostnames

.EXAMPLE
    .\Get-NetworkDevices.ps1 -ExportResults -OutputPath "C:\Reports"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("ARP", "Ping", "Both")]
    [string]$DiscoveryMethod = "Both",

    [Parameter(Mandatory = $false)]
    [int]$Timeout = 2,

    [Parameter(Mandatory = $false)]
    [int]$MaxConcurrent = 50,

    [Parameter(Mandatory = $false)]
    [switch]$IncludeHostnames,

    [Parameter(Mandatory = $false)]
    [switch]$ExportResults,

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = $PSScriptRoot
)

# ── Logging ────────────────────────────────────────────────
$LOG_DIR  = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE = Join-Path -Path $LOG_DIR -ChildPath "Get-NetworkDevices_$(Get-Date -Format 'yyyyMMdd').log"

if (-not (Test-Path -Path $LOG_DIR)) {
    New-Item -Path $LOG_DIR -ItemType Directory -Force | Out-Null
}

function Write-Log {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [Parameter(Mandatory = $false)]
        [ValidateSet("INFO", "WARN", "ERROR")]
        [string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$timestamp] [$Level] $Message"
    Write-Host $entry
    Add-Content -Path $LOG_FILE -Value $entry
}

# ------------------------------------------------------------------------------------------
# NETWORK CONFIGURATION FUNCTIONS
# ------------------------------------------------------------------------------------------

function Get-LocalNetworkConfig {
    <#
    .SYNOPSIS
        Gets the local computer's network configuration
    #>
    Write-Host "NETWORK CONFIG: Detecting local network configuration..." -ForegroundColor Cyan

    try {
        # Get active network adapters (exclude loopback, VPN, etc.)
        $networkAdapters = Get-NetIPConfiguration | Where-Object {
            $_.NetAdapter.Status -eq "Up" -and
            $_.NetAdapter.InterfaceDescription -notmatch "Loopback|VPN|Virtual|Hyper-V|VMware|VirtualBox|Bluetooth" -and
            $_.IPv4Address.Count -gt 0
        }

        if (-not $networkAdapters) {
            throw "No active network adapters found"
        }

        # Use the first active adapter (primary network)
        $primaryAdapter = $networkAdapters[0]
        $ipAddress = $primaryAdapter.IPv4Address[0].IPAddress
        $prefixLength = $primaryAdapter.IPv4Address[0].PrefixLength

        # Convert prefix length to subnet mask
        $subnetMask = Convert-PrefixLengthToSubnetMask -PrefixLength $prefixLength

        # Calculate network address
        $networkAddress = Get-NetworkAddress -IPAddress $ipAddress -SubnetMask $subnetMask

        # Calculate broadcast address
        $broadcastAddress = Get-BroadcastAddress -IPAddress $ipAddress -SubnetMask $subnetMask

        # Calculate total hosts
        $totalHosts = [Math]::Pow(2, (32 - $prefixLength)) - 2  # Exclude network and broadcast

        $config = @{
            InterfaceName = $primaryAdapter.InterfaceAlias
            IPAddress = $ipAddress
            SubnetMask = $subnetMask
            PrefixLength = $prefixLength
            NetworkAddress = $networkAddress
            BroadcastAddress = $broadcastAddress
            TotalPossibleHosts = $totalHosts
            Gateway = if ($primaryAdapter.IPv4DefaultGateway) { $primaryAdapter.IPv4DefaultGateway[0].NextHop } else { "Unknown" }
        }

        Write-Host "SUCCESS: Network configuration detected" -ForegroundColor Green
        Write-Host "  Interface: $($config.InterfaceName)" -ForegroundColor White
        Write-Host "  IP Address: $($config.IPAddress)" -ForegroundColor White
        Write-Host "  Subnet Mask: $($config.SubnetMask) (/$($config.PrefixLength))" -ForegroundColor White
        Write-Host "  Network Range: $($config.NetworkAddress) - $($config.BroadcastAddress)" -ForegroundColor White
        Write-Host "  Total Hosts: $($config.TotalPossibleHosts)" -ForegroundColor White
        Write-Host "  Gateway: $($config.Gateway)" -ForegroundColor White

        return $config

    } catch {
        Write-Host "ERROR: Failed to detect network configuration: $_" -ForegroundColor Red
        throw $_
    }
}

function Convert-PrefixLengthToSubnetMask {
    param([int]$PrefixLength)

    $mask = ([Math]::Pow(2, 32) - 1) -band ([Math]::Pow(2, 32) - [Math]::Pow(2, (32 - $PrefixLength)))
    $bytes = [BitConverter]::GetBytes([UInt32]$mask)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($bytes) }
    return "$($bytes[0]).$($bytes[1]).$($bytes[2]).$($bytes[3])"
}

function Get-NetworkAddress {
    param([string]$IPAddress, [string]$SubnetMask)

    $ip = [System.Net.IPAddress]::Parse($IPAddress).GetAddressBytes()
    $mask = [System.Net.IPAddress]::Parse($SubnetMask).GetAddressBytes()

    $network = @()
    for ($i = 0; $i -lt 4; $i++) {
        $network += $ip[$i] -band $mask[$i]
    }

    return "$($network[0]).$($network[1]).$($network[2]).$($network[3])"
}

function Get-BroadcastAddress {
    param([string]$IPAddress, [string]$SubnetMask)

    $ip = [System.Net.IPAddress]::Parse($IPAddress).GetAddressBytes()
    $mask = [System.Net.IPAddress]::Parse($SubnetMask).GetAddressBytes()

    $broadcast = @()
    for ($i = 0; $i -lt 4; $i++) {
        $broadcast += $ip[$i] -bor (-bnot $mask[$i] -band 255)
    }

    return "$($broadcast[0]).$($broadcast[1]).$($broadcast[2]).$($broadcast[3])"
}

# ------------------------------------------------------------------------------------------
# DISCOVERY FUNCTIONS
# ------------------------------------------------------------------------------------------

function Get-DevicesViaARP {
    <#
    .SYNOPSIS
        Discovers devices using ARP table analysis
    #>
    Write-Host "ARP DISCOVERY: Scanning ARP table for active devices..." -ForegroundColor Cyan

    try {
        $devices = @()
        $arpOutput = arp -a
        $deviceCount = 0

        $arpOutput | ForEach-Object {
            if ($_ -match '^\s*(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F-]{17})\s+(\w+)') {
                $ip = $matches[1]
                $mac = $matches[2]
                $type = $matches[3]

                # Skip multicast, broadcast, and invalid addresses
                if (-not ($ip -match '^(224\.|239\.|255\.255\.255\.255|127\.0\.0\.1)') -and $mac -ne "ff-ff-ff-ff-ff-ff") {
                    $deviceCount++

                    $device = @{
                        IPAddress = $ip
                        MACAddress = $mac
                        ARPType = $type
                        DiscoveryMethod = "ARP"
                        Timestamp = Get-Date
                        Hostname = $ip  # Default to IP, will be resolved later if requested
                        Status = "Active"
                    }

                    $devices += $device
                    Write-Host "  [ARP] Found: $ip ($mac)" -ForegroundColor Gray
                }
            }
        }

        Write-Host "SUCCESS: ARP discovery found $deviceCount devices" -ForegroundColor Green
        return $devices

    } catch {
        Write-Host "ERROR: ARP discovery failed: $_" -ForegroundColor Red
        return @()
    }
}

function Get-DevicesViaPing {
    <#
    .SYNOPSIS
        Discovers devices using ping sweep across the subnet
    #>
    param([hashtable]$NetworkConfig)

    Write-Host "PING DISCOVERY: Performing ping sweep of subnet..." -ForegroundColor Cyan

    try {
        $devices = @()

        # Generate all possible IP addresses in the subnet
        $ipList = Get-SubnetIPRange -NetworkAddress $NetworkConfig.NetworkAddress -SubnetMask $NetworkConfig.SubnetMask

        Write-Host "  Ping sweep range: $($NetworkConfig.NetworkAddress) - $($NetworkConfig.BroadcastAddress)" -ForegroundColor Gray
        Write-Host "  Testing $($ipList.Count) IP addresses with $MaxConcurrent concurrent pings..." -ForegroundColor Gray

        # Create runspace pool for parallel ping operations
        $runspacePool = [runspacefactory]::CreateRunspacePool(1, $MaxConcurrent)
        $runspacePool.Open()

        # Create ping jobs
        $jobs = @()
        foreach ($ip in $ipList) {
            $pingScript = {
                param($targetIP, $timeoutSec)

                try {
                    $ping = New-Object System.Net.NetworkInformation.Ping
                    $result = $ping.Send($targetIP, ($timeoutSec * 1000))

                    if ($result.Status -eq "Success") {
                        return @{
                            IPAddress = $targetIP
                            ResponseTime = $result.RoundtripTime
                            Status = "Reachable"
                        }
                    }
                } catch {
                    # Ping failed
                }
                return $null
            }

            $powerShell = [powershell]::Create()
            $powerShell.RunspacePool = $runspacePool
            $powerShell.AddScript($pingScript)
            $powerShell.AddArgument($ip)
            $powerShell.AddArgument($Timeout)

            $jobs += @{
                PowerShell = $powerShell
                AsyncResult = $powerShell.BeginInvoke()
                IP = $ip
            }
        }

        # Wait for completion and collect results
        $deviceCount = 0
        $completed = 0
        foreach ($job in $jobs) {
            $result = $job.PowerShell.EndInvoke($job.AsyncResult)
            $job.PowerShell.Dispose()
            $completed++

            if ($result) {
                $deviceCount++
                $device = @{
                    IPAddress = $result.IPAddress
                    MACAddress = "Unknown"
                    ARPType = "N/A"
                    DiscoveryMethod = "Ping"
                    ResponseTime = $result.ResponseTime
                    Timestamp = Get-Date
                    Hostname = $result.IPAddress  # Default to IP
                    Status = $result.Status
                }

                $devices += $device
                Write-Host "  [PING] Found: $($result.IPAddress) (${$result.ResponseTime}ms)" -ForegroundColor Gray
            }

            # Progress indicator
            if ($completed % 50 -eq 0 -or $completed -eq $jobs.Count) {
                Write-Host "  Progress: $completed/$($jobs.Count) IPs tested, $deviceCount responsive" -ForegroundColor Gray
            }
        }

        $runspacePool.Close()
        $runspacePool.Dispose()

        Write-Host "SUCCESS: Ping sweep found $deviceCount responsive devices" -ForegroundColor Green
        return $devices

    } catch {
        Write-Host "ERROR: Ping sweep failed: $_" -ForegroundColor Red
        return @()
    }
}

function Get-SubnetIPRange {
    <#
    .SYNOPSIS
        Generates all valid IP addresses in a subnet range
    #>
    param([string]$NetworkAddress, [string]$SubnetMask)

    $ipList = @()
    $network = [System.Net.IPAddress]::Parse($NetworkAddress).GetAddressBytes()
    $mask = [System.Net.IPAddress]::Parse($SubnetMask).GetAddressBytes()

    # Calculate the number of host bits
    $hostNumBits = 0
    for ($i = 0; $i -lt 4; $i++) {
        $hostNumBits += [System.Convert]::ToString($mask[$i], 2).Replace('1','').Length
    }

    $totalHosts = [Math]::Pow(2, $hostNumBits) - 2  # Exclude network and broadcast

    # Generate IP addresses (skip network and broadcast addresses)
    for ($hostNum = 1; $hostNum -lt $totalHosts + 1; $hostNum++) {
        $currentIP = $network.Clone()

        # Add host number to network address
        $carry = $hostNum
        for ($byte = 3; $byte -ge 0; $byte--) {
            if ($carry -gt 0) {
                $hostNumPart = (255 - $mask[$byte]) -band $carry
                $currentIP[$byte] = ($network[$byte] -bor $hostNumPart)
                $carry = $carry -shr (8 - [System.Convert]::ToString($mask[$byte], 2).Replace('1','').Length)
            }
        }

        $ipString = "$($currentIP[0]).$($currentIP[1]).$($currentIP[2]).$($currentIP[3])"
        $ipList += $ipString
    }

    return $ipList
}

function Resolve-DeviceHostnames {
    <#
    .SYNOPSIS
        Resolves hostnames for discovered devices
    #>
    param([array]$Devices)

    if (-not $IncludeHostnames) {
        return $Devices
    }

    Write-Host "HOSTNAME RESOLUTION: Resolving device hostnames..." -ForegroundColor Cyan

    $resolvedCount = 0
    $deviceCount = $Devices.Count

    for ($i = 0; $i -lt $deviceCount; $i++) {
        $device = $Devices[$i]
        $ip = $device.IPAddress

        try {
            # Try DNS lookup
            $hostNumname = [System.Net.Dns]::GetHostByAddress($ip).HostName
            if ($hostNumname -and $hostNumname -ne $ip) {
                $device.Hostname = $hostNumname
                $resolvedCount++
                Write-Host "  [DNS] $ip -> $hostNumname" -ForegroundColor Gray
            }
        } catch {
            # DNS resolution failed, try NetBIOS
            try {
                $netbiosResult = nbtstat -A $ip 2>$null | Where-Object { $_ -match '\s+([A-Z0-9\-]+)\s+<00>\s+UNIQUE' }
                if ($netbiosResult) {
                    $netbiosName = $matches[1].Trim()
                    $device.Hostname = $netbiosName
                    $resolvedCount++
                    Write-Host "  [NetBIOS] $ip -> $netbiosName" -ForegroundColor Gray
                }
            } catch {
                # Both DNS and NetBIOS failed, keep IP as hostname
            }
        }

        # Progress indicator
        if (($i + 1) % 10 -eq 0 -or ($i + 1) -eq $deviceCount) {
            Write-Host "  Progress: $($i + 1)/$deviceCount hostnames resolved ($resolvedCount successful)" -ForegroundColor Gray
        }
    }

    Write-Host "SUCCESS: Resolved $resolvedCount/$deviceCount hostnames" -ForegroundColor Green
    return $Devices
}

function Merge-DiscoveryResults {
    <#
    .SYNOPSIS
        Merges results from multiple discovery methods
    #>
    param([array]$ARPDevices, [array]$PingDevices)

    Write-Host "MERGE RESULTS: Consolidating discovery results..." -ForegroundColor Cyan

    $mergedDevices = @{}

    # Add ARP devices first (more accurate MAC info)
    foreach ($device in $ARPDevices) {
        $ip = $device.IPAddress
        if ($ip) {
            $mergedDevices[$ip] = $device
            $mergedDevices[$ip].DiscoveryMethod = @("ARP")
        }
    }

    # Merge ping devices
    foreach ($device in $PingDevices) {
        $ip = $device.IPAddress

        if ($ip -and $mergedDevices.ContainsKey($ip)) {
            # Device found by both methods - enhance existing entry
            $existing = $mergedDevices[$ip]
            $existing.DiscoveryMethod += "Ping"
            $existing.ResponseTime = $device.ResponseTime
            $existing.Status = "Active"  # Confirmed by ping
        } elseif ($ip) {
            # Device only found by ping
            $mergedDevices[$ip] = $device
            $mergedDevices[$ip].DiscoveryMethod = @("Ping")
        }
    }

    $finalDevices = $mergedDevices.Values | Sort-Object { [System.Version]$_.IPAddress }

    Write-Host "SUCCESS: Merged results - $($finalDevices.Count) unique devices found" -ForegroundColor Green
    return $finalDevices
}

# ------------------------------------------------------------------------------------------
# EXPORT FUNCTIONS
# ------------------------------------------------------------------------------------------

function Export-DeviceResults {
    <#
    .SYNOPSIS
        Exports discovery results to CSV and JSON
    #>
    param([array]$Devices, [hashtable]$NetworkConfig, [string]$OutputPath)

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $baseFileName = "NetworkDiscovery_$timestamp"

    # Export to CSV
    $csvPath = Join-Path $OutputPath "$baseFileName.csv"
    Write-Host "Debug: Exporting CSV to: $csvPath" -ForegroundColor Gray
    $Devices | Select-Object IPAddress, Hostname, MACAddress, DiscoveryMethod, ResponseTime, Status, Timestamp |
        Export-Csv $csvPath -NoTypeInformation

    # Export to JSON with network info
    $jsonData = @{
        DiscoveryTimestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        NetworkConfiguration = $NetworkConfig
        TotalDevicesFound = $Devices.Count
        Devices = $Devices
    }

    $jsonPath = Join-Path $OutputPath "$baseFileName.json"
    Write-Host "Debug: Exporting JSON to: $jsonPath" -ForegroundColor Gray
    $jsonData | ConvertTo-Json -Depth 5 | Out-File $jsonPath -Encoding UTF8

    Write-Host "EXPORT: Results exported to:" -ForegroundColor Green
    Write-Host "  CSV: $csvPath" -ForegroundColor White
    Write-Host "  JSON: $jsonPath" -ForegroundColor White
}

# ------------------------------------------------------------------------------------------
# MAIN EXECUTION
# ------------------------------------------------------------------------------------------

Write-Log -Message "Get-NetworkDevices v1.0 starting"

Write-Host "============================================" -ForegroundColor Green
Write-Host "   Network Discovery Module - TeamLogic IT" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""

try {
    # 1. Detect network configuration
    $networkConfig = Get-LocalNetworkConfig
    Write-Host ""

    # 2. Perform discovery based on selected method
    $allDevices = @()

    if ($DiscoveryMethod -eq "ARP" -or $DiscoveryMethod -eq "Both") {
        $arpDevices = Get-DevicesViaARP
        $allDevices += $arpDevices
        Write-Host ""
    }

    if ($DiscoveryMethod -eq "Ping" -or $DiscoveryMethod -eq "Both") {
        $pingDevices = Get-DevicesViaPing -NetworkConfig $networkConfig

        if ($DiscoveryMethod -eq "Both") {
            $allDevices = Merge-DiscoveryResults -ARPDevices $arpDevices -PingDevices $pingDevices
        } else {
            $allDevices += $pingDevices
        }
        Write-Host ""
    }

    # 3. Resolve hostnames if requested
    if ($allDevices.Count -gt 0) {
        $allDevices = Resolve-DeviceHostnames -Devices $allDevices
        Write-Host ""
    }

    # 4. Display results
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "             DISCOVERY RESULTS" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""

    if ($allDevices.Count -gt 0) {
        Write-Host "SUMMARY: Found $($allDevices.Count) active devices on network" -ForegroundColor Green
        Write-Host ""
        Write-Host "Device List:" -ForegroundColor Cyan
        Write-Host "Hostname                 IP Address       MAC Address       Method      Status" -ForegroundColor Yellow
        Write-Host "--------                 ----------       -----------       ------      ------" -ForegroundColor Yellow

        foreach ($device in $allDevices) {
            $hostNumname = $device.Hostname.PadRight(24)[0..23] -join ''
            $ip = $device.IPAddress.PadRight(16)
            $mac = $device.MACAddress.PadRight(17)
            $method = ($device.DiscoveryMethod -join ",").PadRight(11)
            $status = $device.Status

            $color = switch ($device.Status) {
                "Active" { "Green" }
                "Reachable" { "White" }
                default { "Gray" }
            }

            Write-Host "$hostNumname $ip $mac $method $status" -ForegroundColor $color
        }

        Write-Host ""
        Write-Host "STATISTICS:" -ForegroundColor Cyan
        $arpCount = ($allDevices | Where-Object { $_.DiscoveryMethod -contains "ARP" }).Count
        $pingCount = ($allDevices | Where-Object { $_.DiscoveryMethod -contains "Ping" }).Count
        $bothCount = ($allDevices | Where-Object { $_.DiscoveryMethod.Count -gt 1 }).Count
        $hostNumnameCount = ($allDevices | Where-Object { $_.Hostname -ne $_.IPAddress }).Count

        Write-Host "  Total devices: $($allDevices.Count)" -ForegroundColor White
        Write-Host "  Found by ARP: $arpCount" -ForegroundColor White
        Write-Host "  Found by Ping: $pingCount" -ForegroundColor White
        Write-Host "  Found by both methods: $bothCount" -ForegroundColor White
        Write-Host "  With resolved hostnames: $hostNumnameCount" -ForegroundColor White

        # 5. Export results if requested
        if ($ExportResults) {
            Write-Host ""
            # Ensure OutputPath has a valid value
            if (-not $OutputPath -or $OutputPath -eq "") {
                $OutputPath = $PSScriptRoot
                if (-not $OutputPath) {
                    $OutputPath = Get-Location
                }
            }
            Export-DeviceResults -Devices $allDevices -NetworkConfig $networkConfig -OutputPath $OutputPath
        }

    } else {
        Write-Host "WARNING: No devices found on network!" -ForegroundColor Yellow
        Write-Host "This could mean:" -ForegroundColor Gray
        Write-Host "  • Network is isolated or segmented" -ForegroundColor Gray
        Write-Host "  • Firewall is blocking discovery methods" -ForegroundColor Gray
        Write-Host "  • No other devices are currently active" -ForegroundColor Gray
        Write-Host "  • Network configuration is unusual" -ForegroundColor Gray
    }

} catch {
    Write-Log -Message "FATAL ERROR: Network discovery failed - $_" -Level "ERROR"
    Write-Host ""
    Write-Host "FATAL ERROR: Network discovery failed" -ForegroundColor Red
    Write-Host "Error details: $_" -ForegroundColor Red
    exit 1
}

Write-Log -Message "Get-NetworkDevices completed successfully"

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "Network discovery completed successfully!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green

exit 0