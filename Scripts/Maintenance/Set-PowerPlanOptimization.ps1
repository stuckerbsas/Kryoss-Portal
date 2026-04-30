<#
.SYNOPSIS
    Optimizes the Active Power Plan for RMM continuity using universal powercfg.
    Prevents Sleep and Disk Turn-off ONLY when on AC Power.

.DESCRIPTION
    Category: Onboarding / Monthly
    Context: SYSTEM
    Ticket Reduction: "RMM Agent Offline", Missed patch windows, Network drops during idle.
    Risk Level: Low

    Sets the AC power settings for Sleep and Disk timeout to 0 (Never) on the
    current power scheme. Battery (DC) settings are left untouched. The script
    is idempotent and will skip changes if the system is already compliant.

.PARAMETER None
    This script does not accept parameters. It operates on the active power scheme.

.NOTES
    Author:   TeamLogic IT
    Version:  1.1
    Created:  2026-03-23
    Modified: 2026-04-04

.EXAMPLE
    .\Set-PowerPlanOptimization.ps1
    Applies AC power optimizations silently for NinjaRMM deployment.

.EXAMPLE
    .\Set-PowerPlanOptimization.ps1 -WhatIf
    Shows what power settings would be changed without applying them.
#>

[CmdletBinding(SupportsShouldProcess)]
param()

# --- 1. PRE-REQUISITES & ELEVATION CHECK ---
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "CRITICAL: Elevated privileges required. Exiting."
    exit 1
}

# --- 2. CONSTANTS & LOGGING CONFIGURATION ---
$SCRIPT_NAME = $MyInvocation.MyCommand.Name
$SCRIPT_VERSION = "1.1"
$LOG_DIR  = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE = Join-Path -Path $LOG_DIR -ChildPath "$($SCRIPT_NAME.Replace('.ps1','')).log"

if (-not (Test-Path -Path $LOG_DIR)) {
    New-Item -Path $LOG_DIR -ItemType Directory -Force | Out-Null
}

function Write-Log {
    param(
        [string]$Message,
        [string]$Type = 'INFO'
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logLine = "[$timestamp] [$Type] $Message"

    # 1. Local Log File Output
    $logLine | Out-File -FilePath $LOG_FILE -Append -Encoding UTF8

    # 2. NinjaRMM Console Output (Write-Host)
    if ($Type -eq 'ERROR') {
        Write-Host $logLine -ForegroundColor Red
    } elseif ($Type -eq 'WARNING') {
        Write-Host $logLine -ForegroundColor Yellow
    } else {
        Write-Host $logLine -ForegroundColor Cyan
    }
}

Write-Log "--- Starting Script: $SCRIPT_NAME v$SCRIPT_VERSION ---"

# --- 3. EXECUTION LOGIC (TRY/CATCH) ---
try {
    # GUIDs for Power Settings
    $SLEEP_SUBGROUP = "238c9fa8-0aad-41ed-83f4-97be242c8f20"
    $SLEEP_SETTING  = "29f6c1db-86da-48c5-9fdb-f2b67b1f44da"

    $DISK_SUBGROUP  = "0012ee47-9041-4b5d-9b77-535fba8b1442"
    $DISK_SETTING   = "6738e2c4-e8a5-4a42-b16a-e040e769756e"

    # Helper function to read current AC values using powercfg (Compatible with PS 2.0+)
    function Get-PowerCfgACValue {
        param(
            [string]$SubGroup,
            [string]$Setting
        )
        $queryOutput = & powercfg /q SCHEME_CURRENT $SubGroup $Setting
        foreach ($line in $queryOutput) {
            # Matches "Current AC Power Setting Index: 0x00000000"
            if ($line -match "AC Power Setting Index:\s+0x([0-9a-fA-F]+)") {
                return [Convert]::ToInt32($matches[1], 16)
            }
        }
        return -1
    }

    # --- IDEMPOTENCY CHECK ---
    Write-Log "Evaluating current AC (Plugged In) power configurations..."

    $currentSleepAC = Get-PowerCfgACValue -SubGroup $SLEEP_SUBGROUP -Setting $SLEEP_SETTING
    $currentDiskAC  = Get-PowerCfgACValue -SubGroup $DISK_SUBGROUP -Setting $DISK_SETTING

    $isCompliant = $true
    if ($currentSleepAC -ne 0) { $isCompliant = $false }
    if ($currentDiskAC -ne 0)  { $isCompliant = $false }

    if ($isCompliant) {
        Write-Log "Status: Compliant. No changes needed. AC profiles are already optimized for RMM continuity."
        Write-Log "--- Script Completed Successfully ---"
        exit 0
    }

    # --- APPLYING OPTIMIZATIONS (AC ONLY) ---
    Write-Log "Applying optimizations to Plugged-in (AC) Power Settings. Battery (DC) settings remain untouched."

    if ($PSCmdlet.ShouldProcess("Sleep AC timeout", "Set to 0 (Never)")) {
        & powercfg /setacvalueindex SCHEME_CURRENT $SLEEP_SUBGROUP $SLEEP_SETTING 0
        Write-Log "Set Sleep AC timeout to 0 (Never)."
    }

    if ($PSCmdlet.ShouldProcess("Disk AC timeout", "Set to 0 (Never)")) {
        & powercfg /setacvalueindex SCHEME_CURRENT $DISK_SUBGROUP $DISK_SETTING 0
        Write-Log "Set Disk AC timeout to 0 (Never)."
    }

    # Activate the current scheme to apply changes immediately
    if ($PSCmdlet.ShouldProcess("SCHEME_CURRENT", "Activate power scheme")) {
        & powercfg /setactive SCHEME_CURRENT
        Write-Log "Activated current power scheme to apply changes."
    }

    Write-Log "Optimization applied: Disks and System will never sleep while plugged into AC Power."
    Write-Log "--- Script Completed Successfully ---"
    exit 0
}
catch {
    Write-Log "CRITICAL ERROR: $($_.Exception.Message)" -Type 'ERROR'
    Write-Error $_.Exception.Message
    exit 1
}
