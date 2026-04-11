<#
.SYNOPSIS
    Simple Network Test - Tests PsExec connectivity to all ARP-discovered devices.

.DESCRIPTION
    Discovers all devices via ARP, then tests PsExec connectivity using provided credentials.
    Shows which devices are actual Windows computers that can accept remote connections.

.NOTES
    Author:   TeamLogic IT
    Version:  1.1
    Created:  2026-04-03
    Modified: 2026-04-04

.EXAMPLE
    .\Test-NetworkConnectivity.ps1
#>

[CmdletBinding()]
param()

# --- Logging Setup ---
$LOG_DIR = "C:\ProgramData\TeamLogicIT\Logs"
if (-not (Test-Path -Path $LOG_DIR)) { New-Item -ItemType Directory -Path $LOG_DIR -Force | Out-Null }
$logPath = Join-Path -Path $LOG_DIR -ChildPath "Test-NetworkConnectivity_$(Get-Date -Format 'yyyyMMdd').log"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$timestamp] [$Level] $Message"
    Write-Host $entry
    Add-Content -Path $logPath -Value $entry
}

# --- Main Execution ---
try {
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "   Simple Network Test - TeamLogic IT" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
    Write-Log "Script started."

    # Step 1: Get credentials from console
    Write-Host "Enter credentials for testing remote connections:" -ForegroundColor Cyan
    Write-Host "   These will be used to test PsExec connectivity" -ForegroundColor Gray
    Write-Host ""

    $username = Read-Host 'Username (e.g., Administrator or DOMAIN\username)'
    $password = Read-Host "Password" -AsSecureString

    if (-not $username -or -not $password) {
        Write-Log "Invalid credentials provided. Exiting." "ERROR"
        exit 1
    }

    $credential = New-Object System.Management.Automation.PSCredential ($username, $password)
    Write-Log "Credentials configured for user: $username"
    Write-Host "SUCCESS: Credentials configured for user: $username" -ForegroundColor Green
    Write-Host ""

    # Step 2: Discover devices via ARP
    Write-Host "DISCOVERY: Discovering network devices via ARP..." -ForegroundColor Cyan
    Write-Log "Discovering network devices via ARP..."
    $arpOutput = arp -a

    # Parse ARP entries to get IP addresses
    $discoveredIPs = @()
    $arpOutput | ForEach-Object {
        if ($_ -match '^\s*(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F-]{17})\s+(\w+)') {
            $ip = $matches[1]
            $mac = $matches[2]
            $type = $matches[3]

            # Skip multicast, broadcast, and local loopback
            if (-not ($ip -match '^(224\.|239\.|255\.255\.255\.255|127\.0\.0\.1)')) {
                $discoveredIPs += @{
                    IP = $ip
                    MAC = $mac
                    Type = $type
                }
            }
        }
    }

    Write-Log "Found $(@($discoveredIPs).Count) network devices."
    Write-Host "SUCCESS: Found $(@($discoveredIPs).Count) network devices" -ForegroundColor Green
    Write-Host ""

    # Step 3: Get or download PsExec
    Write-Host "DOWNLOAD: Checking for PsExec..." -ForegroundColor Cyan
    Write-Log "Checking for PsExec..."

    $tempDir = [System.IO.Path]::GetTempPath()
    $psExecPath = Join-Path $tempDir "PsExec64.exe"

    if (-not (Test-Path $psExecPath)) {
        Write-Host "Downloading PsExec from Microsoft Sysinternals..." -ForegroundColor Yellow
        Write-Log "Downloading PsExec from Microsoft Sysinternals..."
        try {
            $downloadUrl = "https://live.sysinternals.com/PsExec64.exe"
            Invoke-WebRequest -Uri $downloadUrl -OutFile $psExecPath -TimeoutSec 60 -UseBasicParsing -ErrorAction Stop
            Write-Log "PsExec downloaded successfully."
            Write-Host "SUCCESS: PsExec downloaded successfully" -ForegroundColor Green
        } catch {
            Write-Log "Failed to download PsExec: $_" "ERROR"
            exit 1
        }
    } else {
        Write-Log "PsExec found at: $psExecPath"
        Write-Host "SUCCESS: PsExec found at: $psExecPath" -ForegroundColor Green
    }
    Write-Host ""

    # Step 4: Test PsExec connectivity to each device
    Write-Host "COMPUTERS:  Testing PsExec connectivity and getting hostnames..." -ForegroundColor Cyan
    Write-Host "   This may take a few minutes depending on network size" -ForegroundColor Gray
    Write-Host ""
    Write-Log "Testing PsExec connectivity to $(@($discoveredIPs).Count) devices..."

    $windowsComputers = @()
    $totalDevices = @($discoveredIPs).Count
    $currentDevice = 0

    foreach ($device in $discoveredIPs) {
        $currentDevice++
        $ip = $device.IP

        Write-Host "[$currentDevice/$totalDevices] Testing $ip..." -ForegroundColor White -NoNewline

        # Build PsExec arguments array
        $psExecArgs = @(
            "\\$ip",
            "-u", $credential.UserName,
            "-p", $credential.GetNetworkCredential().Password,
            "-n", "10",
            "cmd", "/c", "hostname"
        )

        # Execute PsExec using direct invocation (secure - no Invoke-Expression)
        try {
            $result = & $psExecPath $psExecArgs 2>&1

            # Check if we got a hostname back
            $hostname = $result | Where-Object { $_ -match '^[A-Za-z0-9\-]+$' } | Select-Object -First 1

            if ($hostname -and $hostname.Trim() -ne "") {
                $hostname = $hostname.Trim()
                Write-Host " [SUCCESS] $hostname" -ForegroundColor Green
                Write-Log "Connected to $ip - Hostname: $hostname"

                $windowsComputers += @{
                    IP = $ip
                    Hostname = $hostname
                    MAC = $device.MAC
                    Status = "Connected"
                }
            } elseif ($result -match "PsExec could not start") {
                Write-Host " [FAILED] Access denied" -ForegroundColor Red
                Write-Log "Access denied on $ip" "WARN"
            } elseif ($result -match "network path was not found") {
                Write-Host " [FAILED] Not reachable" -ForegroundColor Red
                Write-Log "Not reachable: $ip" "WARN"
            } else {
                Write-Host " [FAILED] No response" -ForegroundColor Red
                Write-Log "No response from $ip" "WARN"
            }
        } catch {
            Write-Host " [FAILED] Connection error" -ForegroundColor Red
            Write-Log "Connection error on $ip : $_" "ERROR"
        }
    }

    # Step 5: Display results
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "             TEST RESULTS" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""

    Write-Host "SUMMARY: Summary:" -ForegroundColor Cyan
    Write-Host "   Total devices discovered: $totalDevices" -ForegroundColor White
    Write-Host "   Windows computers found: $(@($windowsComputers).Count)" -ForegroundColor Green
    Write-Host "   Non-Windows/Unreachable: $($totalDevices - @($windowsComputers).Count)" -ForegroundColor Yellow
    Write-Host ""

    Write-Log "Results: $totalDevices total devices, $(@($windowsComputers).Count) Windows computers found."

    if (@($windowsComputers).Count -gt 0) {
        Write-Host "COMPUTERS:  Windows Computers Found:" -ForegroundColor Green
        Write-Host ""
        Write-Host "   Hostname               IP Address       MAC Address" -ForegroundColor Cyan
        Write-Host "   --------               ----------       -----------" -ForegroundColor Cyan

        foreach ($computer in $windowsComputers) {
            $hostnameFormatted = $computer.Hostname.PadRight(22)
            $ipFormatted = $computer.IP.PadRight(16)
            $macFormatted = $computer.MAC
            Write-Host "   $hostnameFormatted $ipFormatted $macFormatted" -ForegroundColor White
        }

        Write-Host ""
        Write-Host "SUCCESS: These computers can be targeted for remote assessments!" -ForegroundColor Green

    } else {
        Write-Host "WARNING:  No Windows computers found that accept PsExec connections." -ForegroundColor Yellow
        Write-Host "   This could mean:" -ForegroundColor Gray
        Write-Host "   * Computers are powered off" -ForegroundColor Gray
        Write-Host "   * Windows Firewall is blocking connections" -ForegroundColor Gray
        Write-Host "   * Credentials are incorrect" -ForegroundColor Gray
        Write-Host "   * Devices are not Windows computers (printers, phones, etc.)" -ForegroundColor Gray
        Write-Host "   * UAC or other security settings prevent remote access" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "Test completed!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green

    Write-Log "Script completed successfully."
    exit 0

} catch {
    Write-Log "Fatal error: $_" "ERROR"
    Write-Host "FATAL ERROR: $_" -ForegroundColor Red
    exit 1
}
