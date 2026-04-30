<#
.SYNOPSIS
    Hardens Windows UX and Privacy by disabling Copilot, FastBoot, SMBv1, and Taskbar clutter.

.DESCRIPTION
    Category: Onboarding / Monthly
    Context: SYSTEM
    Ticket Reduction: "PC not rebooting correctly", "Unwanted AI popups", "Taskbar is messy".
    Risk Level: Low

.PARAMETER
    This script does not accept parameters. All configuration is handled internally.

.NOTES
    Author:   TeamLogic IT
    Version:  1.1
    Created:  2026-03-23
    Modified: 2026-04-04

.EXAMPLE
    .\Set-WindowsUXAndPrivacyHardening.ps1
    Runs the script to disable Copilot, Fast Boot, SMBv1, and Taskbar clutter.
#>
[CmdletBinding(SupportsShouldProcess)]
param()

# --- 1. PRE-REQUISITES & ELEVATION CHECK ---
$IsAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $IsAdmin) { Write-Error "CRITICAL: Elevated privileges required. Exiting."; exit 1 }

# --- 2. LOGGING CONFIGURATION ---
$LogDirectory = "C:\ProgramData\TeamLogicIT\Logs"; $ScriptName = $MyInvocation.MyCommand.Name
$LogFile = Join-Path $LogDirectory "$($ScriptName.Replace('.ps1','')).log"
if (-not (Test-Path $LogDirectory)) { New-Item -Path $LogDirectory -ItemType Directory -Force | Out-Null }

function Write-Log {
    param([string]$Message, [string]$Type = 'INFO')
    $TimeStamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $LogLine = "$TimeStamp [$Type] - $Message"
    $LogLine | Out-File -FilePath $LogFile -Append -Encoding UTF8
    if ($Type -eq 'ERROR') { Write-Host $LogLine -ForegroundColor Red }
    elseif ($Type -eq 'WARNING') { Write-Host $LogLine -ForegroundColor Yellow }
    else { Write-Host $LogLine -ForegroundColor Cyan }
}

Write-Log "--- Starting Script: $ScriptName ---"

# --- 3. EXECUTION LOGIC (TRY/CATCH) ---
try {
    # --- IDEMPOTENCY CHECK ---
    Write-Log "Evaluating System UX and Privacy state..."
    $IsCompliant = $true

    # 1. Check Copilot
    $CopilotPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot"
    if ((Get-ItemProperty -Path $CopilotPath -Name "TurnOffWindowsCopilot" -ErrorAction SilentlyContinue).TurnOffWindowsCopilot -ne 1) { $IsCompliant = $false }

    # 2. Check Fast Boot
    $FastBootPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power"
    if ((Get-ItemProperty -Path $FastBootPath -Name "HiberbootEnabled" -ErrorAction SilentlyContinue).HiberbootEnabled -ne 0) { $IsCompliant = $false }

    # 3. Check SMBv1
    $Smb1 = Get-WindowsOptionalFeature -Online -FeatureName "SMB1Protocol" -ErrorAction SilentlyContinue
    if ($Smb1.State -eq "Enabled") { $IsCompliant = $false }

    if ($IsCompliant) {
        Write-Log "Status: Compliant. UX and Privacy are already hardened."
        Write-Log "--- Script Completed Successfully ---"
        exit 0
    }

    # --- APPLYING HARDENING ---
    Write-Log "Applying UX and Privacy Hardening..."

    # A. Disable Copilot
    if (-not (Test-Path $CopilotPath)) { New-Item -Path $CopilotPath -Force | Out-Null }
    Set-ItemProperty -Path $CopilotPath -Name "TurnOffWindowsCopilot" -Value 1 -Type DWord -Force
    Write-Log "Windows Copilot has been disabled."

    # B. Disable Fast Boot (Ensure real reboots)
    Set-ItemProperty -Path $FastBootPath -Name "HiberbootEnabled" -Value 0 -Type DWord -Force
    Write-Log "Fast Boot (Hiberboot) has been disabled."

    # C. Disable SMBv1 (Security)
    if ($Smb1.State -eq "Enabled") {
        Disable-WindowsOptionalFeature -Online -FeatureName "SMB1Protocol" -NoRestart | Out-Null
        Write-Log "SMBv1 Protocol has been disabled."
    }

    # D. Taskbar Clutter (News & Interests / Search / Meet Now)
    # These are often per-user, but setting them in HKLM/Policies helps for new profiles
    $PolicyPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds"
    if (-not (Test-Path $PolicyPath)) { New-Item -Path $PolicyPath -Force | Out-Null }
    Set-ItemProperty -Path $PolicyPath -Name "EnableFeeds" -Value 0 -Type DWord -Force
    Write-Log "Taskbar 'News and Interests' has been disabled."

    # E. Disable Teams 'Chat' Icon (Windows 11)
    $TeamsChatPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer"
    if (-not (Test-Path $TeamsChatPath)) { New-Item -Path $TeamsChatPath -Force | Out-Null }
    Set-ItemProperty -Path $TeamsChatPath -Name "ConfigureChatIcon" -Value 3 -Type DWord -Force
    Write-Log "Windows 11 Teams Chat icon has been removed from taskbar."

    Write-Log "--- Script Completed Successfully ---"
    exit 0
}
catch {
    Write-Log "CRITICAL ERROR: $($_.Exception.Message)" -Type 'ERROR'
    Write-Error $_.Exception.Message
    exit 1
}