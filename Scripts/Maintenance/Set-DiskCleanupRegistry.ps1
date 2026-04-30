#Requires -Version 5.1

<#
.SYNOPSIS
    Configure Windows Disk Cleanup (cleanmgr) volume cache registry flags for automated sagerun.

.DESCRIPTION
    Sets StateFlags0002 DWord values under each VolumeCaches subkey in HKLM so that
    running "cleanmgr /sagerun:2" will clean the enabled categories automatically.

    Value 2 = enabled for sagerun:2, value 0 = disabled.

    The script checks each setting before writing to ensure idempotency and logs
    every change with before/after values.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-04
    Modified: 2026-04-04

.EXAMPLE
    .\Set-DiskCleanupRegistry.ps1
    Applies all disk cleanup flags silently (suitable for NinjaRMM deployment).

.EXAMPLE
    .\Set-DiskCleanupRegistry.ps1 -WhatIf
    Shows what changes would be made without writing to the registry.
#>

[CmdletBinding(SupportsShouldProcess)]
param()

$SCRIPT_NAME    = "Set-DiskCleanupRegistry"
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

# ---------------------------------------------------------------------------
# Category definitions: Name = desired StateFlags0002 value
#   2 = enabled for sagerun:2
#   0 = disabled for sagerun:2
# ---------------------------------------------------------------------------
$VOLUME_CACHES_BASE = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches"
$FLAG_NAME          = "StateFlags0002"

$categories = [ordered]@{
    "Active Setup Temp Folders"                  = 2
    "BranchCache"                                = 2
    "Downloaded Program Files"                   = 2
    "Internet Cache Files"                       = 2
    "Old ChkDsk Files"                           = 2
    "Previous Installations"                     = 2
    "Recycle Bin"                                = 2
    "Service Pack Cleanup"                       = 2
    "Setup Log Files"                            = 2
    "System error memory dump files"             = 2
    "System error minidump files"                = 2
    "Temporary Files"                            = 2
    "Temporary Setup Files"                      = 2
    "Thumbnail Cache"                            = 2
    "Update Cleanup"                             = 2
    "Upgrade Discarded Files"                    = 2
    "Windows Defender"                           = 2
    "Windows Error Reporting Archive Files"      = 2
    "Windows Error Reporting Queue Files"        = 2
    "Windows Error Reporting System Archive Files" = 2
    "Windows Error Reporting System Queue Files" = 2
    "Windows ESD installation files"             = 2
    "Windows Upgrade Log Files"                  = 2
}

try {
    Write-Log -Message "$SCRIPT_NAME v$SCRIPT_VERSION starting"
    Write-Log -Message "Configuring $($categories.Count) volume cache categories for sagerun:2"

    $changedCount  = 0
    $skippedCount  = 0
    $createdCount  = 0
    $errorCount    = 0

    foreach ($category in $categories.GetEnumerator()) {
        $subkeyPath   = Join-Path -Path $VOLUME_CACHES_BASE -ChildPath $category.Key
        $desiredValue = $category.Value

        # Verify the volume cache subkey exists (it is created by Windows, not by us)
        if (-not (Test-Path -Path $subkeyPath)) {
            Write-Log -Message "Subkey not found, skipping: $($category.Key)" -Level "WARN"
            $errorCount++
            continue
        }

        # Read current value (null if property does not exist yet)
        $currentValue = $null
        try {
            $currentValue = (Get-ItemProperty -Path $subkeyPath -Name $FLAG_NAME -ErrorAction Stop).$FLAG_NAME
        }
        catch {
            # Property does not exist yet - will be created below
            $currentValue = $null
        }

        if ($currentValue -eq $desiredValue) {
            Write-Log -Message "Already set [$($category.Key)] $FLAG_NAME = $desiredValue (no change)"
            $skippedCount++
            continue
        }

        # Apply the change
        if ($PSCmdlet.ShouldProcess("$subkeyPath\$FLAG_NAME", "Set value to $desiredValue")) {
            if ($null -eq $currentValue) {
                New-ItemProperty -Path $subkeyPath -Name $FLAG_NAME -Value $desiredValue -PropertyType DWord -Force | Out-Null
                Write-Log -Message "Created  [$($category.Key)] $FLAG_NAME = $desiredValue"
                $createdCount++
            }
            else {
                Set-ItemProperty -Path $subkeyPath -Name $FLAG_NAME -Value $desiredValue
                Write-Log -Message "Changed  [$($category.Key)] $FLAG_NAME: $currentValue -> $desiredValue"
                $changedCount++
            }
        }
    }

    Write-Log -Message "Summary: Created=$createdCount, Changed=$changedCount, Skipped=$skippedCount, Errors=$errorCount"
    Write-Log -Message "$SCRIPT_NAME completed successfully"
    exit 0
}
catch {
    Write-Log -Message "FATAL: $($_.Exception.Message)" -Level "ERROR"
    Write-Log -Message "Stack: $($_.ScriptStackTrace)" -Level "ERROR"
    exit 1
}
