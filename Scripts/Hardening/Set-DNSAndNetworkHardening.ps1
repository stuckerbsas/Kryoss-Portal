<#
.SYNOPSIS
    Hardens network settings by disabling insecure legacy protocols (LLMNR, NetBIOS) and ICS.

.DESCRIPTION
    Category: Onboarding / Monthly
    Context: SYSTEM
    Ticket Reduction: Network spoofing vulnerabilities, DNS resolution delays, ICS conflicts.
    Risk Level: Medium (Internal legacy apps might require NetBIOS)

.PARAMETER
    This script does not accept parameters. All configuration is handled internally.

.NOTES
    Author:   TeamLogic IT
    Version:  1.1
    Created:  2026-03-23
    Modified: 2026-04-04

.EXAMPLE
    .\Set-DNSAndNetworkHardening.ps1
    Runs the script to disable LLMNR, NetBIOS over TCP/IP, and ICS service.
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
    Write-Log "Checking network protocol compliance..."
    $IsCompliant = $true

    # 1. Check LLMNR (Link-Local Multicast Name Resolution)
    $LLMNRPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient"
    $LLMNRValue = (Get-ItemProperty -Path $LLMNRPath -Name "EnableMulticast" -ErrorAction SilentlyContinue).EnableMulticast
    if ($LLMNRValue -ne 0) { $IsCompliant = $false }

    # 2. Check NetBIOS over TCP/IP on all active adapters
    $Adapters = Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter "IPEnabled = True"
    foreach ($Adapter in $Adapters) {
        # TcpipNetbiosOptions: 2 = Disabled, 1 = Enabled, 0 = Use DHCP setting
        if ($Adapter.TcpipNetbiosOptions -ne 2) { $IsCompliant = $false }
    }

    # 3. Check ICS Service (Internet Connection Sharing)
    $IcsService = Get-Service -Name "SharedAccess" -ErrorAction SilentlyContinue
    if ($IcsService.Status -ne 'Stopped' -or $IcsService.StartType -ne 'Disabled') { $IsCompliant = $false }

    if ($IsCompliant) {
        Write-Log "Status: Compliant. Network hardening is already in place."
        Write-Log "--- Script Completed Successfully ---"
        exit 0
    }

    # --- APPLYING HARDENING ---
    Write-Log "Applying Network Hardening..."

    # A. Disable LLMNR via Registry
    if (-not (Test-Path $LLMNRPath)) { New-Item -Path $LLMNRPath -Force | Out-Null }
    Set-ItemProperty -Path $LLMNRPath -Name "EnableMulticast" -Value 0 -Type DWord -Force
    Write-Log "LLMNR has been disabled."

    # B. Disable NetBIOS over TCP/IP on all adapters
    foreach ($Adapter in $Adapters) {
        $Result = Invoke-CimMethod -InputObject $Adapter -MethodName SetTcpipNetbios -Arguments @{TcpipNetbiosOptions = 2}
        if ($Result.ReturnValue -eq 0) {
            Write-Log "NetBIOS disabled on adapter: $($Adapter.Description)"
        } else {
            Write-Log "Failed to disable NetBIOS on adapter: $($Adapter.Description)" -Type 'WARNING'
        }
    }

    # C. Disable ICS (SharedAccess) Service
    if ($IcsService) {
        Stop-Service -Name "SharedAccess" -Force -ErrorAction SilentlyContinue
        Set-Service -Name "SharedAccess" -StartupType Disabled
        Write-Log "Internet Connection Sharing (ICS) service disabled."
    }

    Write-Log "--- Script Completed Successfully ---"
    exit 0
}
catch {
    Write-Log "CRITICAL ERROR: $($_.Exception.Message)" -Type 'ERROR'
    Write-Error $_.Exception.Message
    exit 1
}