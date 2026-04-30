<#
.SYNOPSIS
    Disables insecure network protocols (SMBv1, LLMNR, NetBIOS) for enhanced security.

.DESCRIPTION
    This script disables SMBv1, LLMNR, and NetBIOS protocols which are known security risks.
    It configures registry settings to disable these protocols and logs all changes made.
    The script is designed to run silently via NinjaRMM and is idempotent (safe to run multiple times).

.PARAMETER LogPath
    Specifies the path for the log file. Default is C:\ProgramData\TeamLogicIT\Logs\Disable-InsecureProtocols_YYYYMMDD.log

.PARAMETER WhatIf
    Shows what would happen if the script runs. The script will not make any changes.

.PARAMETER Verbose
    Displays detailed information about the script execution.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-01
    Modified: 2026-04-01

.EXAMPLE
    .\Disable-InsecureProtocols.ps1
    Runs the script with default settings.

.EXAMPLE
    .\Disable-InsecureProtocols.ps1 -WhatIf
    Shows what changes would be made without actually making them.
#>

[CmdletBinding(SupportsShouldProcess)]
param (
    [string]$LogPath = "C:\ProgramData\TeamLogicIT\Logs\Disable-InsecureProtocols_$(Get-Date -Format 'yyyyMMdd').log"
)

# Initialize variables
$ErrorActionPreference = 'Stop'
$script:changesMade = @()

# Standard logging function
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$timestamp] [$Level] $Message"
    Write-Host $entry
    if (!(Test-Path (Split-Path $LogPath -Parent))) {
        New-Item -ItemType Directory -Path (Split-Path $LogPath -Parent) -Force | Out-Null
    }
    Add-Content -Path $LogPath -Value $entry
}

# Function to disable SMBv1
function Disable-SMBv1 {
    try {
        if ($PSCmdlet.ShouldProcess("SMBv1", "Disable")) {
            # Check current SMB configuration
            $smbConfig = Get-SmbServerConfiguration -ErrorAction SilentlyContinue
            if ($smbConfig.EnableSMB1Protocol -eq $true) {
                Write-Log "Disabling SMBv1 protocol..."
                Set-SmbServerConfiguration -EnableSMB1Protocol $false -Force
                $script:changesMade += "Disabled SMBv1 protocol"
                Write-Log "Successfully disabled SMBv1 protocol" "INFO"
            } else {
                Write-Log "SMBv1 protocol is already disabled" "INFO"
            }

            # Disable SMBv1 feature if installed
            $smbFeature = Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -ErrorAction SilentlyContinue
            if ($smbFeature.State -eq "Enabled") {
                Write-Log "Disabling SMBv1 feature..."
                Disable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -NoRestart -WarningAction SilentlyContinue | Out-Null
                $script:changesMade += "Disabled SMBv1 feature"
                Write-Log "Successfully disabled SMBv1 feature" "INFO"
            } else {
                Write-Log "SMBv1 feature is already disabled" "INFO"
            }
        }
    } catch {
        Write-Log "Failed to disable SMBv1: $_" "ERROR"
        throw
    }
}

# Function to disable LLMNR
function Disable-LLMNR {
    try {
        if ($PSCmdlet.ShouldProcess("LLMNR", "Disable")) {
            $regPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient"
            $regName = "EnableMulticast"

            # Check if registry key exists and value
            $regValue = Get-ItemProperty -Path $regPath -Name $regName -ErrorAction SilentlyContinue

            if ($regValue -and $regValue.$regName -eq 0) {
                Write-Log "LLMNR is already disabled" "INFO"
            } else {
                Write-Log "Disabling LLMNR..."
                if (!(Test-Path $regPath)) {
                    New-Item -Path $regPath -Force | Out-Null
                }
                Set-ItemProperty -Path $regPath -Name $regName -Value 0
                $script:changesMade += "Disabled LLMNR"
                Write-Log "Successfully disabled LLMNR" "INFO"
            }
        }
    } catch {
        Write-Log "Failed to disable LLMNR: $_" "ERROR"
        throw
    }
}

# Function to disable NetBIOS
function Disable-NetBIOS {
    try {
        if ($PSCmdlet.ShouldProcess("NetBIOS", "Disable")) {
            Write-Log "Disabling NetBIOS over TCP/IP..."

            # Get all network adapters
            $adapters = Get-WmiObject -Class Win32_NetworkAdapterConfiguration -Filter "IPEnabled='True'"

            foreach ($adapter in $adapters) {
                try {
                    # Check current NetBIOS setting
                    # 0 = Default, 1 = Enable NetBIOS, 2 = Disable NetBIOS
                    if ($adapter.TcpipNetbiosOptions -ne 2) {
                        Write-Log "Disabling NetBIOS on adapter: $($adapter.Description)"
                        $adapter.SetTcpipNetbios(2) | Out-Null
                        $script:changesMade += "Disabled NetBIOS on adapter: $($adapter.Description)"
                        Write-Log "Successfully disabled NetBIOS on adapter: $($adapter.Description)" "INFO"
                    } else {
                        Write-Log "NetBIOS is already disabled on adapter: $($adapter.Description)" "INFO"
                    }
                } catch {
                    Write-Log "Failed to disable NetBIOS on adapter $($adapter.Description): $_" "WARN"
                }
            }
        }
    } catch {
        Write-Log "Failed to disable NetBIOS: $_" "ERROR"
        throw
    }
}

# Main execution
try {
    Write-Log "Starting Disable-InsecureProtocols script execution"

    # Disable SMBv1
    Disable-SMBv1

    # Disable LLMNR
    Disable-LLMNR

    # Disable NetBIOS
    Disable-NetBIOS

    # Report results
    if ($script:changesMade.Count -gt 0) {
        Write-Log "Script completed successfully with the following changes:"
        foreach ($change in $script:changesMade) {
            Write-Log "  - $change"
        }
        Write-Log "System should be rebooted for all changes to take effect" "WARN"
        exit 0
    } else {
        Write-Log "Script completed successfully. No changes were needed."
        exit 0
    }
} catch {
    Write-Log "Script failed with error: $_" "ERROR"
    Write-Log "Exiting with error code 1" "ERROR"
    exit 1
}