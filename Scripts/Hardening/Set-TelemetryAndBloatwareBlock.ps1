<#
.SYNOPSIS
    Blocks Windows Telemetry, disables Xbox Game DVR, and prevents automatic bloatware installation.

.DESCRIPTION
    Category: Onboarding / Monthly
    Context: SYSTEM
    Ticket Reduction: "PC is slow", "High CPU/RAM usage", Unwanted Apps installed by Windows.
    Risk Level: Low

.PARAMETER
    This script does not accept parameters. All configuration is handled internally.

.NOTES
    Author:   TeamLogic IT
    Version:  1.1
    Created:  2026-03-23
    Modified: 2026-04-04

.EXAMPLE
    .\Set-TelemetryAndBloatwareBlock.ps1
    Runs the script to block telemetry, disable Game DVR, and prevent bloatware installation.
#>
[CmdletBinding(SupportsShouldProcess)]
param()

# --- 1. PRE-REQUISITES & ELEVATION CHECK ---
$IsAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $IsAdmin) {
    Write-Error "CRITICAL: Elevated privileges required. Exiting."
    exit 1
}

# --- 2. LOGGING CONFIGURATION ---
$LogDirectory = "C:\ProgramData\TeamLogicIT\Logs"
$ScriptName = $MyInvocation.MyCommand.Name
$LogFile = Join-Path $LogDirectory "$($ScriptName.Replace('.ps1','')).log"

if (-not (Test-Path $LogDirectory)) {
    New-Item -Path $LogDirectory -ItemType Directory -Force | Out-Null
}

function Write-Log {
    param([string]$Message, [string]$Type = 'INFO')
    $TimeStamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $LogLine = "$TimeStamp [$Type] - $Message"
    
    # 1. Local Log File Output
    $LogLine | Out-File -FilePath $LogFile -Append -Encoding UTF8
    
    # 2. NinjaOne RMM Console Output (Write-Host)
    if ($Type -eq 'ERROR') {
        Write-Host $LogLine -ForegroundColor Red
    } elseif ($Type -eq 'WARNING') {
        Write-Host $LogLine -ForegroundColor Yellow
    } else {
        Write-Host $LogLine -ForegroundColor Cyan
    }
}

Write-Log "--- Starting Script: $ScriptName ---"

# --- 3. EXECUTION LOGIC (TRY/CATCH) ---
try {
    # Define Registry Paths
    $CloudContentPath   = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent"
    $DataCollectionPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection"
    $GameDVRPath        = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR"

    # --- IDEMPOTENCY CHECK ---
    Write-Log "Evaluating current Telemetry, GameDVR, and Bloatware configurations..."
    $IsCompliant = $true

    # 1. Check Consumer Features (Bloatware blocker)
    $ConsumerFeatures = (Get-ItemProperty -Path $CloudContentPath -Name "DisableWindowsConsumerFeatures" -ErrorAction SilentlyContinue).DisableWindowsConsumerFeatures
    if ($ConsumerFeatures -ne 1) { $IsCompliant = $false }

    # 2. Check Telemetry Level
    $Telemetry = (Get-ItemProperty -Path $DataCollectionPath -Name "AllowTelemetry" -ErrorAction SilentlyContinue).AllowTelemetry
    if ($Telemetry -ne 0) { $IsCompliant = $false }

    # 3. Check GameDVR
    $GameDVR = (Get-ItemProperty -Path $GameDVRPath -Name "AllowGameDVR" -ErrorAction SilentlyContinue).AllowGameDVR
    if ($GameDVR -ne 0) { $IsCompliant = $false }

    # 4. Check DiagTrack Service (Connected User Experiences and Telemetry)
    $DiagTrack = Get-Service -Name "DiagTrack" -ErrorAction SilentlyContinue
    if ($DiagTrack) {
        if ($DiagTrack.Status -ne 'Stopped' -or $DiagTrack.StartType -ne 'Disabled') {
            $IsCompliant = $false
        }
    }

    if ($IsCompliant) {
        Write-Log "Status: Compliant. No changes needed. Telemetry and Bloatware are already blocked."
        Write-Log "--- Script Completed Successfully ---"
        exit 0
    }

    # --- APPLYING OPTIMIZATIONS ---
    Write-Log "Non-compliant state detected. Applying blocks..."

    # Ensure registry paths exist
    foreach ($Path in @($CloudContentPath, $DataCollectionPath, $GameDVRPath)) {
        if (-not (Test-Path $Path)) {
            New-Item -Path $Path -Force -ErrorAction SilentlyContinue | Out-Null
        }
    }

    # Apply Registry Policies
    Set-ItemProperty -Path $CloudContentPath -Name "DisableWindowsConsumerFeatures" -Value 1 -Type DWord -Force
    Write-Log "Windows Consumer Features (Bloatware auto-install) disabled."

    Set-ItemProperty -Path $DataCollectionPath -Name "AllowTelemetry" -Value 0 -Type DWord -Force
    Write-Log "Windows Telemetry set to 0 (Security/Disabled)."

    Set-ItemProperty -Path $GameDVRPath -Name "AllowGameDVR" -Value 0 -Type DWord -Force
    Write-Log "Xbox Game DVR background recording disabled."

    # Stop and Disable DiagTrack Service completely
    if ($DiagTrack) {
        Stop-Service -Name "DiagTrack" -Force -ErrorAction SilentlyContinue
        Set-Service -Name "DiagTrack" -StartupType Disabled -ErrorAction SilentlyContinue
        Write-Log "DiagTrack Service stopped and disabled."
    }

    Write-Log "--- Script Completed Successfully ---"
    exit 0
}
catch {
    Write-Log "CRITICAL ERROR: $($_.Exception.Message)" -Type 'ERROR'
    Write-Error $_.Exception.Message
    exit 1
}