#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Synchronizes system time with an NTP server.

.DESCRIPTION
    Ensures the Windows Time service (w32time) is running, configures it to use
    time.windows.com as the NTP source, and forces an immediate resync.
    Designed for silent deployment via NinjaRMM.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-04
    Modified: 2026-04-04

.EXAMPLE
    .\Sync-SystemTime.ps1
#>

[CmdletBinding(SupportsShouldProcess)]
param()

$SCRIPT_NAME    = "Sync-SystemTime"
$SCRIPT_VERSION = "1.0"
$LOG_DIR        = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE       = Join-Path -Path $LOG_DIR -ChildPath "$($SCRIPT_NAME)_$(Get-Date -Format 'yyyyMMdd').log"

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

try {
    Write-Log -Message "$SCRIPT_NAME v$SCRIPT_VERSION starting"

    $timeBefore = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    Write-Log -Message "System time before sync: $timeBefore"

    # ── Step 1: Ensure w32time service is running ──
    $service = Get-Service -Name 'w32time' -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Log -Message "Windows Time service (w32time) not found on this system" -Level "ERROR"
        exit 1
    }

    Write-Log -Message "w32time status: $($service.Status), StartType: $($service.StartType)"

    if ($service.StartType -eq 'Disabled') {
        if ($PSCmdlet.ShouldProcess("w32time", "Set startup type to Manual")) {
            Set-Service -Name 'w32time' -StartupType Manual -ErrorAction Stop
            Write-Log -Message "Changed w32time startup type from Disabled to Manual"
        }
    }

    if ($service.Status -ne 'Running') {
        if ($PSCmdlet.ShouldProcess("w32time", "Start service")) {
            try {
                Start-Service -Name 'w32time' -ErrorAction Stop
                Write-Log -Message "Started w32time service via Start-Service"
            }
            catch {
                Write-Log -Message "Start-Service failed: $($_.Exception.Message) - attempting net start fallback" -Level "WARN"
                & net start w32time 2>&1 | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    Write-Log -Message "net start w32time failed with exit code $LASTEXITCODE" -Level "ERROR"
                    exit 1
                }
                Write-Log -Message "Started w32time service via net start fallback"
            }
        }
    }
    else {
        Write-Log -Message "w32time service is already running"
    }

    # ── Step 2: Configure NTP peer ──
    if ($PSCmdlet.ShouldProcess("w32time", "Configure NTP peer to time.windows.com")) {
        Write-Log -Message "Configuring NTP peer to time.windows.com"
        $ntpOutput = & w32tm /config /manualpeerlist:"time.windows.com" /syncfromflags:manual /reliable:YES /update 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Log -Message "w32tm /config failed (exit code $LASTEXITCODE): $ntpOutput" -Level "ERROR"
            exit 1
        }
        Write-Log -Message "NTP configuration applied successfully"
    }

    # ── Step 3: Force resync ──
    if ($PSCmdlet.ShouldProcess("w32time", "Force time resync")) {
        Write-Log -Message "Forcing time resynchronization"
        $resyncOutput = & w32tm /resync 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Log -Message "w32tm /resync failed (exit code $LASTEXITCODE): $resyncOutput" -Level "ERROR"
            exit 1
        }
        Write-Log -Message "Time resynchronization completed successfully"
    }

    $timeAfter = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    Write-Log -Message "System time after sync: $timeAfter"
    Write-Log -Message "$SCRIPT_NAME completed successfully"
    exit 0
}
catch {
    Write-Log -Message "FATAL: $($_.Exception.Message)" -Level "ERROR"
    Write-Log -Message "Stack: $($_.ScriptStackTrace)" -Level "ERROR"
    exit 1
}
