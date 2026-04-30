#Requires -Version 5.1

<#
.SYNOPSIS
    Prevents the system from entering standby/sleep mode on AC power.

.DESCRIPTION
    Configures the active power plan to disable standby timeout when on AC power.
    This ensures RMM agents, scheduled tasks, and monitoring tools remain active
    without interruption from system sleep.

    Uses powercfg to query current settings and apply changes only if needed.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-04
    Modified: 2026-04-04

.EXAMPLE
    .\Set-PreventSleep.ps1
    Disables standby timeout on AC power silently (NinjaRMM/Intune deployment).

.EXAMPLE
    .\Set-PreventSleep.ps1 -WhatIf
    Shows what changes would be made without applying them.
#>

[CmdletBinding(SupportsShouldProcess)]
param()

# ── Constants ──────────────────────────────────────────────
$SCRIPT_NAME    = "Set-PreventSleep"
$SCRIPT_VERSION = "1.0"
$LOG_DIR        = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE       = Join-Path -Path $LOG_DIR -ChildPath "$($SCRIPT_NAME)_$(Get-Date -Format 'yyyyMMdd').log"

# ── Logging ────────────────────────────────────────────────
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

# ── Main Logic ─────────────────────────────────────────────
try {
    Write-Log -Message "$SCRIPT_NAME v$SCRIPT_VERSION starting"

    # Query current AC standby timeout
    $currentTimeout = $null
    $queryOutput = & powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Log -Message "Failed to query power settings: $queryOutput" -Level "ERROR"
        exit 1
    }

    # Parse the current AC setting (value in seconds, hex)
    $acLine = $queryOutput | Select-String -Pattern "Current AC Power Setting Index:\s+(0x[0-9a-fA-F]+)"
    if ($acLine) {
        $currentTimeout = [int]$acLine.Matches[0].Groups[1].Value
    }

    Write-Log -Message "Current AC standby timeout: $currentTimeout seconds"

    # Check if already disabled (0 = never sleep)
    if ($currentTimeout -eq 0) {
        Write-Log -Message "Standby timeout already disabled (0) — no action needed"
        exit 0
    }

    # Apply change
    if ($PSCmdlet.ShouldProcess("AC standby timeout", "Change from '$currentTimeout' seconds to '0' (disabled)")) {
        & powercfg -change -standby-timeout-ac 0 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Log -Message "powercfg failed with exit code $LASTEXITCODE" -Level "ERROR"
            exit 1
        }
        Write-Log -Message "AC standby timeout changed from '$currentTimeout' seconds to '0' (disabled)"
    }

    Write-Log -Message "$SCRIPT_NAME completed successfully"
    exit 0
}
catch {
    Write-Log -Message "FATAL: $($_.Exception.Message)" -Level "ERROR"
    Write-Log -Message "Stack: $($_.ScriptStackTrace)" -Level "ERROR"
    exit 1
}
