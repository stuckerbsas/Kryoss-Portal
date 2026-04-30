#Requires -Version 5.1

<#
.SYNOPSIS
    Prohibits Internet Connection Sharing on domain networks.

.DESCRIPTION
    Sets NC_ShowSharedAccessUI to 0 in the Network Connections policy registry key,
    which prevents users from enabling Internet Connection Sharing (ICS).
    Applies CIS Benchmark Level 1 hardening.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-04
    Modified: 2026-04-04

.EXAMPLE
    .\Disable-InternetConnectionSharing.ps1
    Applies the hardening setting silently (NinjaRMM/Intune deployment).

.EXAMPLE
    .\Disable-InternetConnectionSharing.ps1 -WhatIf
    Shows what changes would be made without applying them.
#>

[CmdletBinding(SupportsShouldProcess)]
param()

# ── Constants ──────────────────────────────────────────────
$SCRIPT_NAME    = "Disable-InternetConnectionSharing"
$SCRIPT_VERSION = "1.0"
$LOG_DIR        = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE       = Join-Path -Path $LOG_DIR -ChildPath "$($SCRIPT_NAME)_$(Get-Date -Format 'yyyyMMdd').log"

# ── Registry Target ────────────────────────────────────────
$REG_PATH  = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Network Connections"
$REG_NAME  = "NC_ShowSharedAccessUI"
$REG_VALUE = 0
$REG_TYPE  = "DWord"

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

    # Ensure registry path exists
    if (-not (Test-Path -Path $REG_PATH)) {
        if ($PSCmdlet.ShouldProcess($REG_PATH, "Create registry key")) {
            New-Item -Path $REG_PATH -Force | Out-Null
            Write-Log -Message "Created registry key: $REG_PATH"
        }
    }

    # Check current state
    $currentValue = $null
    try {
        $currentValue = (Get-ItemProperty -Path $REG_PATH -Name $REG_NAME -ErrorAction Stop).$REG_NAME
    }
    catch {
        Write-Log -Message "Property '$REG_NAME' does not exist yet" -Level "INFO"
    }

    # Apply if needed
    if ($currentValue -eq $REG_VALUE) {
        Write-Log -Message "$REG_NAME already set to $REG_VALUE — no action needed"
        exit 0
    }

    if ($PSCmdlet.ShouldProcess("$REG_PATH\$REG_NAME", "Set value from '$currentValue' to '$REG_VALUE'")) {
        Set-ItemProperty -Path $REG_PATH -Name $REG_NAME -Value $REG_VALUE -Type $REG_TYPE -Force
        Write-Log -Message "$REG_NAME changed from '$currentValue' to '$REG_VALUE'"
    }

    Write-Log -Message "$SCRIPT_NAME completed successfully"
    exit 0
}
catch {
    Write-Log -Message "FATAL: $($_.Exception.Message)" -Level "ERROR"
    Write-Log -Message "Stack: $($_.ScriptStackTrace)" -Level "ERROR"
    exit 1
}
