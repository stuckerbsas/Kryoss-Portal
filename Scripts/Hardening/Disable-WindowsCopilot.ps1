<#
.SYNOPSIS
    Disables Windows Copilot, Recall, and Edge Sidebar on Windows 11.

.DESCRIPTION
    Applies machine-level registry policies (HKLM) to ensure no user can re-enable
    Windows Copilot, Windows Recall/AI Data Analysis, or the Edge Sidebar.
    Also removes the Copilot AppX package if present.
    Generates a success/failure report for the NinjaRMM console.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-04
    Modified: 2026-04-04

.EXAMPLE
    .\Disable-WindowsCopilot.ps1
    Applies all Copilot-related policies silently and reports results.

.EXAMPLE
    .\Disable-WindowsCopilot.ps1 -WhatIf
    Shows what changes would be made without actually applying them.
#>

[CmdletBinding(SupportsShouldProcess)]
param()

# --- Constants ---
$SCRIPT_NAME    = "Disable-WindowsCopilot"
$SCRIPT_VERSION = "1.0"
$LOG_DIR        = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE       = Join-Path -Path $LOG_DIR -ChildPath "${SCRIPT_NAME}_$(Get-Date -Format 'yyyyMMdd').log"

# --- Create log directory if missing ---
if (-not (Test-Path -Path $LOG_DIR)) {
    New-Item -Path $LOG_DIR -ItemType Directory -Force | Out-Null
}

# --- Logging function ---
function Write-Log {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$timestamp] [$Level] $Message"
    Write-Host $entry
    Add-Content -Path $LOG_FILE -Value $entry
}

# --- Registry helper with idempotency ---
function Set-RegistryPolicy {
    param(
        [string]$Path,
        [string]$Name,
        [int]$Value
    )
    try {
        # Idempotency: read current value before writing
        if (Test-Path -Path $Path) {
            $currentValue = Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
            if ($null -ne $currentValue -and $currentValue.$Name -eq $Value) {
                Write-Log "Registry '$Path\$Name' already set to $Value. Skipping."
                return "Already Compliant"
            }
        }

        if ($PSCmdlet.ShouldProcess("$Path\$Name", "Set registry value to $Value")) {
            if (-not (Test-Path -Path $Path)) {
                New-Item -Path $Path -Force | Out-Null
                Write-Log "Created registry key: $Path"
            }
            Set-ItemProperty -Path $Path -Name $Name -Value $Value -Type DWord -Force
            Write-Log "Set '$Path\$Name' to $Value."
            return "Success"
        } else {
            return "WhatIf - Skipped"
        }
    } catch {
        Write-Log "Failed to set '$Path\$Name': $($_.Exception.Message)" -Level "ERROR"
        return "Failed: $($_.Exception.Message)"
    }
}

# --- Main Execution ---
Write-Log "--- Starting $SCRIPT_NAME v$SCRIPT_VERSION ---"

$results = [System.Collections.Generic.List[PSObject]]::new()

# 1. Disable Windows Copilot (Shell / Taskbar)
Write-Log "Processing policy: Windows Copilot Shell..."
$status = Set-RegistryPolicy -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot" -Name "TurnOffWindowsCopilot" -Value 1
$results.Add([PSCustomObject]@{ Policy = "Windows Copilot Shell"; Status = $status })

# 2. Disable Recall / AI Data Analysis (Privacy)
Write-Log "Processing policy: Windows Recall/AI..."
$status = Set-RegistryPolicy -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows AI" -Name "DisableAIDataAnalysis" -Value 1
$results.Add([PSCustomObject]@{ Policy = "Windows Recall/AI"; Status = $status })

# 3. Disable Edge Copilot & Sidebar (Browser)
Write-Log "Processing policy: Edge Sidebar/Copilot..."
$status = Set-RegistryPolicy -Path "HKLM:\SOFTWARE\Policies\Microsoft\Edge" -Name "HubsSidebarEnabled" -Value 0
$results.Add([PSCustomObject]@{ Policy = "Edge Sidebar/Copilot"; Status = $status })

# 4. Remove Copilot AppX package (Start Menu)
Write-Log "Processing: Copilot AppX package cleanup..."
try {
    $app = Get-AppxPackage -AllUsers -Name "*Microsoft.Copilot*" -ErrorAction SilentlyContinue
    if ($app) {
        if ($PSCmdlet.ShouldProcess("Microsoft.Copilot AppX", "Remove package for all users")) {
            $app | Remove-AppxPackage -AllUsers -ErrorAction Stop
            Write-Log "Copilot AppX package removed successfully."
            $status = "App Removed"
        } else {
            $status = "WhatIf - Skipped"
        }
    } else {
        Write-Log "Copilot AppX package not found. Already clean."
        $status = "Not Found (Already Clean)"
    }
} catch {
    Write-Log "Error removing Copilot AppX package: $($_.Exception.Message)" -Level "ERROR"
    $status = "Failed: $($_.Exception.Message)"
}
$results.Add([PSCustomObject]@{ Policy = "Copilot AppX Cleanup"; Status = $status })

# --- Final Report for NinjaRMM ---
Write-Log "--- Results Summary ---"
$results | Format-Table -AutoSize

$failedCount = ($results | Where-Object { $_.Status -like "Failed*" }).Count
if ($failedCount -gt 0) {
    Write-Log "$failedCount policy application(s) failed. Review errors above." -Level "ERROR"
    exit 1
} else {
    Write-Log "All policies applied successfully. Copilot has been disabled."
    Write-Log "--- $SCRIPT_NAME completed ---"
    exit 0
}
