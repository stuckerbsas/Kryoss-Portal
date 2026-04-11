<#
.SYNOPSIS
    Complete Network Assessment - Discovery + Remote Execution Validation

.DESCRIPTION
    Combines network discovery with remote execution validation to provide a complete
    picture of which devices on your network can run security assessments.

    Workflow:
    1. Discovers all devices on the local subnet
    2. Tests PsExec connectivity to discovered devices
    3. Identifies which devices are Windows computers ready for remote assessments
    4. Exports comprehensive results

.PARAMETER DiscoveryMethod
    Discovery method: ARP, Ping, or Both (default: ARP for speed)

.PARAMETER TestRemoteExecution
    Test PsExec connectivity to discovered devices

.PARAMETER Credential
    Credentials for remote testing (will prompt if not provided and testing is enabled)

.PARAMETER ExportResults
    Export all results to CSV and JSON files

.PARAMETER OutputPath
    Directory to save export files (default: script directory)

.NOTES
    Author:   TeamLogic IT
    Project:  Kryoss Network Assessment Suite
    Version:  1.0
    Created:  2026-04-03

.EXAMPLE
    .\Invoke-NetworkAssessment.ps1

.EXAMPLE
    .\Invoke-NetworkAssessment.ps1 -TestRemoteExecution -ExportResults

.EXAMPLE
    .\Invoke-NetworkAssessment.ps1 -DiscoveryMethod Both -TestRemoteExecution -OutputPath "C:\Reports"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("ARP", "Ping", "Both")]
    [string]$DiscoveryMethod = "ARP",

    [Parameter(Mandatory = $false)]
    [switch]$TestRemoteExecution,

    [Parameter(Mandatory = $false)]
    [System.Management.Automation.PSCredential]$Credential = $null,

    [Parameter(Mandatory = $false)]
    [switch]$ExportResults,

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = $PSScriptRoot
)

# ── Logging ────────────────────────────────────────────────
$LOG_DIR  = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE = Join-Path -Path $LOG_DIR -ChildPath "Invoke-NetworkAssessment_$(Get-Date -Format 'yyyyMMdd').log"

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

# ── Validate child scripts ─────────────────────────────────
$getDevicesScript = Join-Path -Path $PSScriptRoot -ChildPath "Get-NetworkDevices.ps1"
if (-not (Test-Path -Path $getDevicesScript)) {
    Write-Log -Message "Required script not found: $getDevicesScript" -Level "ERROR"
    exit 1
}

Write-Log -Message "Invoke-NetworkAssessment v1.0 starting"

Write-Host "============================================" -ForegroundColor Green
Write-Host "   Network Assessment Suite - TeamLogic IT" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""

try {
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

    # Step 1: Network Discovery
    Write-Host "STEP 1: NETWORK DISCOVERY" -ForegroundColor Cyan
    Write-Host "Discovering devices on local network using $DiscoveryMethod method..." -ForegroundColor Gray
    Write-Host ""

    # Run network discovery
    $discoveryArgs = @{
        DiscoveryMethod = $DiscoveryMethod
    }

    if ($ExportResults) {
        $discoveryArgs.ExportResults = $true
        $discoveryArgs.OutputPath = $OutputPath
    }

    # Call the network discovery script
    $discoveryResult = & "$PSScriptRoot\Get-NetworkDevices.ps1" @discoveryArgs

    # Get the discovered devices (this would need to be modified in the main script to return devices)
    # For now, let's parse the latest JSON file or use a simplified approach
    Write-Host "Discovery completed. Searching for discovered devices..." -ForegroundColor Gray

    # Find the most recent discovery file
    $latestDiscoveryFile = Get-ChildItem "$OutputPath\NetworkDiscovery_*.json" -ErrorAction SilentlyContinue |
        Sort-Object CreationTime -Descending |
        Select-Object -First 1

    if ($latestDiscoveryFile) {
        $discoveryData = Get-Content $latestDiscoveryFile.FullName -Raw | ConvertFrom-Json
        $discoveredDevices = $discoveryData.Devices
        Write-Host "SUCCESS: Found $($discoveredDevices.Count) devices from discovery" -ForegroundColor Green
    } else {
        # Fallback - use ARP discovery directly
        Write-Host "No discovery file found, using direct ARP query..." -ForegroundColor Yellow
        $arpOutput = arp -a
        $discoveredDevices = @()

        $arpOutput | ForEach-Object {
            if ($_ -match '^\s*(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F-]{17})\s+(\w+)') {
                $ip = $matches[1]
                $mac = $matches[2]
                $type = $matches[3]

                if (-not ($ip -match '^(224\.|239\.|255\.255\.255\.255|127\.0\.0\.1)') -and $mac -ne "ff-ff-ff-ff-ff-ff") {
                    $discoveredDevices += @{
                        IPAddress = $ip
                        MACAddress = $mac
                        Hostname = $ip
                        DiscoveryMethod = @("ARP")
                        Status = "Active"
                    }
                }
            }
        }
        Write-Host "SUCCESS: Found $($discoveredDevices.Count) devices via direct ARP" -ForegroundColor Green
    }

    Write-Host ""

    # Step 2: Remote Execution Testing (if requested)
    $validationResults = $null
    $remoteCapableDevices = @()

    if ($TestRemoteExecution) {
        Write-Host "STEP 2: REMOTE EXECUTION VALIDATION" -ForegroundColor Cyan
        Write-Host "Testing PsExec connectivity to discovered devices..." -ForegroundColor Gray
        Write-Host ""

        # Prepare validation arguments
        $validationArgs = @{
            InputDevices = $discoveredDevices
            Timeout = 10
            MaxConcurrent = 5
        }

        if ($Credential) {
            $validationArgs.Credential = $Credential
        }

        if ($ExportResults) {
            $validationArgs.ExportResults = $true
            $validationArgs.OutputPath = $OutputPath
        }

        # Call the remote execution validator
        try {
            Write-Host "Starting remote execution validation..." -ForegroundColor Gray
            & "$PSScriptRoot\Test-RemoteExecution.ps1" @validationArgs
            Write-Host ""
        } catch {
            Write-Host "Remote validation encountered an issue: $_" -ForegroundColor Yellow
            Write-Host ""
        }

        # Try to get validation results from the export file
        $latestValidationFile = Get-ChildItem "$OutputPath\RemoteValidation_*.json" -ErrorAction SilentlyContinue |
            Sort-Object CreationTime -Descending |
            Select-Object -First 1

        if ($latestValidationFile) {
            $validationData = Get-Content $latestValidationFile.FullName -Raw | ConvertFrom-Json
            $validationResults = $validationData.Results
            $remoteCapableDevices = $validationResults | Where-Object { $_.Status -eq "Success" }
        }
    }

    # Step 3: Summary Report
    Write-Host "STEP 3: ASSESSMENT SUMMARY" -ForegroundColor Cyan
    Write-Host ""

    Write-Host "NETWORK ASSESSMENT RESULTS:" -ForegroundColor Green
    Write-Host "Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor White
    Write-Host ""

    Write-Host "DISCOVERY SUMMARY:" -ForegroundColor Yellow
    Write-Host "  Total devices found: $($discoveredDevices.Count)" -ForegroundColor White
    if ($discoveredDevices.Count -gt 0) {
        $arpDevices = ($discoveredDevices | Where-Object { $_.DiscoveryMethod -contains "ARP" }).Count
        $pingDevices = ($discoveredDevices | Where-Object { $_.DiscoveryMethod -contains "Ping" }).Count
        Write-Host "    - Found by ARP: $arpDevices" -ForegroundColor Gray
        if ($pingDevices -gt 0) {
            Write-Host "    - Found by Ping: $pingDevices" -ForegroundColor Gray
        }
    }

    if ($TestRemoteExecution -and $validationResults) {
        Write-Host ""
        Write-Host "REMOTE EXECUTION VALIDATION:" -ForegroundColor Yellow
        $successful = ($validationResults | Where-Object { $_.Status -eq "Success" }).Count
        $accessDenied = ($validationResults | Where-Object { $_.Status -eq "AccessDenied" }).Count
        $unreachable = ($validationResults | Where-Object { $_.Status -eq "Unreachable" }).Count

        Write-Host "  Devices tested: $($validationResults.Count)" -ForegroundColor White
        Write-Host "    - Remote execution capable: $successful" -ForegroundColor Green
        Write-Host "    - Access denied: $accessDenied" -ForegroundColor Yellow
        Write-Host "    - Unreachable: $unreachable" -ForegroundColor Red

        if ($remoteCapableDevices.Count -gt 0) {
            Write-Host ""
            Write-Host "WINDOWS COMPUTERS READY FOR ASSESSMENT:" -ForegroundColor Green
            foreach ($device in $remoteCapableDevices) {
                $hostname = if ($device.RemoteHostname -and $device.RemoteHostname -ne $device.IPAddress) {
                    $device.RemoteHostname
                } else {
                    "Unknown"
                }
                Write-Host "  • $($device.IPAddress) - $hostname" -ForegroundColor White
            }
        }
    }

    # Step 4: Next Steps Recommendations
    Write-Host ""
    Write-Host "RECOMMENDATIONS:" -ForegroundColor Yellow

    if ($TestRemoteExecution) {
        if ($remoteCapableDevices.Count -gt 0) {
            Write-Host "SUCCESS: Found $($remoteCapableDevices.Count) device(s) ready for remote assessment!" -ForegroundColor Green
            Write-Host "• These devices can run Kryoss security assessments remotely" -ForegroundColor White
            Write-Host "• Use the main Invoke-KryossAssessment.ps1 script with these IPs" -ForegroundColor White
        } else {
            Write-Host "No devices found that accept remote execution." -ForegroundColor Yellow
            Write-Host "• Check Windows Firewall settings on target devices" -ForegroundColor Gray
            Write-Host "• Verify credentials have administrator rights" -ForegroundColor Gray
            Write-Host "• Ensure target devices are Windows computers" -ForegroundColor Gray
            Write-Host "• Consider enabling Windows Remote Management (WinRM)" -ForegroundColor Gray
        }
    } else {
        Write-Host "To test which devices can run remote assessments:" -ForegroundColor White
        Write-Host "• Re-run with -TestRemoteExecution parameter" -ForegroundColor Gray
        Write-Host "• Ensure you have administrator credentials for target devices" -ForegroundColor Gray
    }

    if ($ExportResults) {
        Write-Host ""
        Write-Host "EXPORTED FILES:" -ForegroundColor Cyan
        $exportFiles = Get-ChildItem $OutputPath -Name "*Discovery*$($timestamp.Substring(0,8))*.json", "*Validation*$($timestamp.Substring(0,8))*.json" -ErrorAction SilentlyContinue
        foreach ($file in $exportFiles) {
            Write-Host "  • $file" -ForegroundColor Gray
        }
    }

} catch {
    Write-Log -Message "FATAL ERROR: Network assessment failed - $_" -Level "ERROR"
    Write-Host ""
    Write-Host "FATAL ERROR: Network assessment failed" -ForegroundColor Red
    Write-Host "Error details: $_" -ForegroundColor Red
    exit 1
}

Write-Log -Message "Invoke-NetworkAssessment completed successfully"

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "Network assessment completed successfully!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green

exit 0