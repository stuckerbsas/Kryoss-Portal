<#
.SYNOPSIS
    Test script to verify insecure protocols status

.DESCRIPTION
    This script checks the current status of SMBv1, LLMNR, and NetBIOS protocols
    to verify that they are properly disabled.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-01
    Modified: 2026-04-01
#>

[CmdletBinding()]
param()

$LOG_DIR  = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE = Join-Path -Path $LOG_DIR -ChildPath "Test-InsecureProtocols_$(Get-Date -Format 'yyyyMMdd').log"

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

$insecureFound = $false

# Check SMBv1 status
Write-Host "Checking SMBv1 status..."
try {
    $smbConfig = Get-SmbServerConfiguration -ErrorAction SilentlyContinue
    if ($smbConfig.EnableSMB1Protocol -eq $false) {
        Write-Host "  SMBv1 protocol: DISABLED" -ForegroundColor Green
        Write-Log -Message "SMBv1 protocol: DISABLED"
    } else {
        Write-Host "  SMBv1 protocol: ENABLED" -ForegroundColor Red
        Write-Log -Message "SMBv1 protocol: ENABLED" -Level "WARN"
        $insecureFound = $true
    }

    $smbFeature = Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -ErrorAction SilentlyContinue
    if ($smbFeature.State -eq "Disabled") {
        Write-Host "  SMBv1 feature: DISABLED" -ForegroundColor Green
        Write-Log -Message "SMBv1 feature: DISABLED"
    } else {
        Write-Host "  SMBv1 feature: ENABLED" -ForegroundColor Red
        Write-Log -Message "SMBv1 feature: ENABLED" -Level "WARN"
        $insecureFound = $true
    }
} catch {
    Write-Host "  SMBv1 check failed: $_" -ForegroundColor Yellow
}

# Check LLMNR status
Write-Host "`nChecking LLMNR status..."
try {
    $regPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient"
    $regName = "EnableMulticast"
    $regValue = Get-ItemProperty -Path $regPath -Name $regName -ErrorAction SilentlyContinue

    if ($regValue -and $regValue.$regName -eq 0) {
        Write-Host "  LLMNR: DISABLED" -ForegroundColor Green
        Write-Log -Message "LLMNR: DISABLED"
    } else {
        Write-Host "  LLMNR: ENABLED" -ForegroundColor Red
        Write-Log -Message "LLMNR: ENABLED" -Level "WARN"
        $insecureFound = $true
    }
} catch {
    Write-Host "  LLMNR check failed: $_" -ForegroundColor Yellow
}

# Check NetBIOS status
Write-Host "`nChecking NetBIOS status..."
try {
    $adapters = Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter "IPEnabled='True'"
    foreach ($adapter in $adapters) {
        if ($adapter.TcpipNetbiosOptions -eq 2) {
            Write-Host "  NetBIOS on adapter '$($adapter.Description)': DISABLED" -ForegroundColor Green
            Write-Log -Message "NetBIOS on adapter '$($adapter.Description)': DISABLED"
        } else {
            Write-Host "  NetBIOS on adapter '$($adapter.Description)': ENABLED" -ForegroundColor Red
            Write-Log -Message "NetBIOS on adapter '$($adapter.Description)': ENABLED" -Level "WARN"
            $insecureFound = $true
        }
    }
} catch {
    Write-Host "  NetBIOS check failed: $_" -ForegroundColor Yellow
    Write-Log -Message "NetBIOS check failed: $_" -Level "ERROR"
}

# Exit with appropriate code
if ($insecureFound) {
    Write-Log -Message "One or more insecure protocols are still enabled." -Level "WARN"
    exit 1
}
else {
    Write-Log -Message "All checked protocols are disabled."
    exit 0
}