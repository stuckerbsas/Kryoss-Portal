#Requires -Version 5.1

<#
.SYNOPSIS
    Enable and configure Windows Storage Sense policy via registry.

.DESCRIPTION
    Writes DWord values under HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\
    StorageSense\Parameters\StoragePolicy to enable Storage Sense and set its
    cleanup thresholds.

    IMPORTANT: This script targets HKCU (current user hive). When executed as
    SYSTEM via NinjaRMM, it writes to the SYSTEM account's HKCU hive, which
    does NOT affect interactive users. To apply per-user, either:
      - Run under the target user's context, or
      - Load each user's NTUSER.DAT via HKU and write there.

    Each registry value is checked before writing to ensure idempotency.
    All changes are logged with before/after values.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-04
    Modified: 2026-04-04

.EXAMPLE
    .\Set-StorageSensePolicy.ps1
    Applies Storage Sense settings silently (suitable for NinjaRMM deployment).

.EXAMPLE
    .\Set-StorageSensePolicy.ps1 -WhatIf
    Shows what changes would be made without writing to the registry.
#>

[CmdletBinding(SupportsShouldProcess)]
param()

$SCRIPT_NAME    = "Set-StorageSensePolicy"
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
# Storage Sense registry path and settings
# ---------------------------------------------------------------------------
$STORAGE_POLICY_PATH = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy"

# Each entry: PropertyName = @{ Value = <DWord>; Description = <string> }
$settings = [ordered]@{
    "01"   = @{ Value = 1;  Description = "Enable Storage Sense" }
    "02"   = @{ Value = 1;  Description = "Run Storage Sense when disk space is low" }
    "04"   = @{ Value = 1;  Description = "Enable automatic cleanup of temporary files" }
    "08"   = @{ Value = 1;  Description = "Enable cleanup of files in Downloads folder" }
    "32"   = @{ Value = 0;  Description = "Days before auto-deleting Recycle Bin files (0 = never)" }
    "256"  = @{ Value = 30; Description = "Days before cleaning up Downloads folder (30 days)" }
    "512"  = @{ Value = 30; Description = "Days before cleaning up Recycle Bin (30 days)" }
    "1024" = @{ Value = 0;  Description = "Run frequency (0 = during low disk space only)" }
    "2048" = @{ Value = 1;  Description = "Enable cloud content dehydration (OneDrive)" }
    "4096" = @{ Value = 60; Description = "Days before dehydrating cloud content (60 days)" }
}

try {
    Write-Log -Message "$SCRIPT_NAME v$SCRIPT_VERSION starting"

    # Ensure the full registry path exists (create parent keys if needed)
    $pathSegments = @(
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters",
        $STORAGE_POLICY_PATH
    )

    foreach ($segment in $pathSegments) {
        if (-not (Test-Path -Path $segment)) {
            if ($PSCmdlet.ShouldProcess($segment, "Create registry key")) {
                New-Item -Path $segment -Force | Out-Null
                Write-Log -Message "Created registry key: $segment"
            }
        }
    }

    Write-Log -Message "Applying $($settings.Count) Storage Sense policy settings"

    $changedCount = 0
    $skippedCount = 0

    foreach ($entry in $settings.GetEnumerator()) {
        $propertyName  = $entry.Key
        $desiredValue  = $entry.Value.Value
        $description   = $entry.Value.Description

        # Read current value (null if property does not exist yet)
        $currentValue = $null
        try {
            $currentValue = (Get-ItemProperty -Path $STORAGE_POLICY_PATH -Name $propertyName -ErrorAction Stop).$propertyName
        }
        catch {
            $currentValue = $null
        }

        if ($currentValue -eq $desiredValue) {
            Write-Log -Message "Already set [$propertyName] = $desiredValue ($description)"
            $skippedCount++
            continue
        }

        # Apply the change
        if ($PSCmdlet.ShouldProcess("$STORAGE_POLICY_PATH\$propertyName", "Set to $desiredValue ($description)")) {
            if ($null -eq $currentValue) {
                New-ItemProperty -Path $STORAGE_POLICY_PATH -Name $propertyName -Value $desiredValue -PropertyType DWord -Force | Out-Null
                Write-Log -Message "Created  [$propertyName] = $desiredValue ($description)"
            }
            else {
                Set-ItemProperty -Path $STORAGE_POLICY_PATH -Name $propertyName -Value $desiredValue
                Write-Log -Message "Changed  [$propertyName]: $currentValue -> $desiredValue ($description)"
            }
            $changedCount++
        }
    }

    Write-Log -Message "Summary: Changed=$changedCount, Skipped=$skippedCount"
    Write-Log -Message "$SCRIPT_NAME completed successfully"
    exit 0
}
catch {
    Write-Log -Message "FATAL: $($_.Exception.Message)" -Level "ERROR"
    Write-Log -Message "Stack: $($_.ScriptStackTrace)" -Level "ERROR"
    exit 1
}
