#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Resets and repairs Windows Update components.

.DESCRIPTION
    Performs a comprehensive reset of the Windows Update subsystem by stopping
    related services, clearing cached data (SoftwareDistribution and catroot2),
    re-registering Windows Update DLLs, resetting Winsock and WinHTTP proxy,
    restoring default service security descriptors, and restarting all services.
    Designed for silent deployment via NinjaRMM.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-04
    Modified: 2026-04-04

.EXAMPLE
    .\Repair-WindowsUpdate.ps1

.EXAMPLE
    .\Repair-WindowsUpdate.ps1 -WhatIf
#>

[CmdletBinding(SupportsShouldProcess)]
param()

$SCRIPT_NAME    = "Repair-WindowsUpdate"
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

# Services involved in Windows Update
$WU_SERVICES = @("BITS", "wuauserv", "appidsvc", "cryptsvc")

# DLLs to re-register
$WU_DLLS = @(
    "atl.dll", "urlmon.dll", "mshtml.dll", "shdocvw.dll", "browseui.dll",
    "jscript.dll", "vbscript.dll", "scrrun.dll", "msxml.dll", "msxml3.dll",
    "msxml6.dll", "actxprxy.dll", "softpub.dll", "wintrust.dll", "dssenh.dll",
    "rsaenh.dll", "gpkcsp.dll", "sccbase.dll", "slbcsp.dll", "cryptdlg.dll",
    "oleaut32.dll", "ole32.dll", "shell32.dll", "initpki.dll", "wuapi.dll",
    "wuaueng.dll", "wuaueng1.dll", "wucltui.dll", "wups.dll", "wups2.dll",
    "wuweb.dll", "qmgr.dll", "qmgrprxy.dll", "wucltux.dll", "muweb.dll",
    "wuwebv.dll"
)

# Startup types to restore after repair
$SERVICE_STARTUP = @{
    "BITS"      = "AutomaticDelayedStart"
    "wuauserv"  = "Manual"
    "appidsvc"  = "Manual"
    "cryptsvc"  = "Automatic"
}

function Stop-WUServices {
    Write-Log -Message "Stopping Windows Update services"
    $allStopped = $true
    foreach ($svcName in $WU_SERVICES) {
        $svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue
        if (-not $svc) {
            Write-Log -Message "Service '$svcName' not found - skipping" -Level "WARN"
            continue
        }
        if ($svc.Status -eq 'Stopped') {
            Write-Log -Message "Service '$svcName' is already stopped"
            continue
        }
        if ($PSCmdlet.ShouldProcess($svcName, "Stop service")) {
            try {
                Stop-Service -Name $svcName -Force -ErrorAction Stop
                Write-Log -Message "Stopped service '$svcName'"
            }
            catch {
                Write-Log -Message "Failed to stop '$svcName': $($_.Exception.Message)" -Level "WARN"
                $allStopped = $false
            }
        }
    }
    return $allStopped
}

function Clear-WUCache {
    Write-Log -Message "Clearing Windows Update cache"

    $softwareDistPath = Join-Path -Path $env:SystemRoot -ChildPath "SoftwareDistribution"
    $catroot2Path     = Join-Path -Path $env:SystemRoot -ChildPath "System32\catroot2"

    # SoftwareDistribution
    if (Test-Path -Path $softwareDistPath) {
        if ($PSCmdlet.ShouldProcess($softwareDistPath, "Remove directory")) {
            try {
                Remove-Item -Path $softwareDistPath -Recurse -Force -ErrorAction Stop
                Write-Log -Message "Removed $softwareDistPath"
            }
            catch {
                Write-Log -Message "Failed to remove $softwareDistPath - attempting rename" -Level "WARN"
                $backupName = "SoftwareDistribution.bak_$(Get-Date -Format 'yyyyMMddHHmmss')"
                $backupPath = Join-Path -Path $env:SystemRoot -ChildPath $backupName
                try {
                    Rename-Item -Path $softwareDistPath -NewName $backupName -Force -ErrorAction Stop
                    Write-Log -Message "Renamed $softwareDistPath to $backupPath"
                }
                catch {
                    Write-Log -Message "Failed to rename $softwareDistPath : $($_.Exception.Message)" -Level "ERROR"
                }
            }
        }
    }
    else {
        Write-Log -Message "$softwareDistPath does not exist - skipping"
    }

    # catroot2
    if (Test-Path -Path $catroot2Path) {
        if ($PSCmdlet.ShouldProcess($catroot2Path, "Remove directory")) {
            try {
                Remove-Item -Path $catroot2Path -Recurse -Force -ErrorAction Stop
                Write-Log -Message "Removed $catroot2Path"
            }
            catch {
                Write-Log -Message "Failed to remove $catroot2Path - attempting rename" -Level "WARN"
                $backupName = "catroot2.bak_$(Get-Date -Format 'yyyyMMddHHmmss')"
                $backupPath = Join-Path -Path "$env:SystemRoot\System32" -ChildPath $backupName
                try {
                    Rename-Item -Path $catroot2Path -NewName $backupName -Force -ErrorAction Stop
                    Write-Log -Message "Renamed $catroot2Path to $backupPath"
                }
                catch {
                    Write-Log -Message "Failed to rename $catroot2Path : $($_.Exception.Message)" -Level "ERROR"
                }
            }
        }
    }
    else {
        Write-Log -Message "$catroot2Path does not exist - skipping"
    }

    # Clean BITS queue data
    $downloaderPath = Join-Path -Path $env:ALLUSERSPROFILE -ChildPath "Application Data\Microsoft\Network\Downloader"
    if (Test-Path -Path $downloaderPath) {
        $qmgrFiles = Get-ChildItem -Path $downloaderPath -Filter "qmgr*.dat" -ErrorAction SilentlyContinue
        if ($qmgrFiles) {
            if ($PSCmdlet.ShouldProcess($downloaderPath, "Remove qmgr*.dat files")) {
                foreach ($file in $qmgrFiles) {
                    try {
                        Remove-Item -Path $file.FullName -Force -ErrorAction Stop
                        Write-Log -Message "Removed $($file.FullName)"
                    }
                    catch {
                        Write-Log -Message "Failed to remove $($file.FullName): $($_.Exception.Message)" -Level "WARN"
                    }
                }
            }
        }
    }
}

function Register-WUDlls {
    Write-Log -Message "Re-registering $($WU_DLLS.Count) Windows Update DLLs"
    $successCount = 0
    $failCount    = 0

    foreach ($dll in $WU_DLLS) {
        $dllPath = Join-Path -Path "$env:SystemRoot\System32" -ChildPath $dll
        if (-not (Test-Path -Path $dllPath)) {
            Write-Log -Message "DLL not found: $dllPath - skipping" -Level "WARN"
            $failCount++
            continue
        }
        if ($PSCmdlet.ShouldProcess($dll, "Register DLL")) {
            & regsvr32.exe /s $dllPath 2>&1 | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Log -Message "Failed to register $dll (exit code $LASTEXITCODE)" -Level "WARN"
                $failCount++
            }
            else {
                $successCount++
            }
        }
    }

    Write-Log -Message "DLL registration complete: $successCount succeeded, $failCount failed/skipped"
}

function Reset-NetworkStack {
    Write-Log -Message "Resetting Winsock catalog"
    if ($PSCmdlet.ShouldProcess("Winsock", "Reset catalog")) {
        $winsockOutput = & netsh winsock reset 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Log -Message "netsh winsock reset failed (exit code $LASTEXITCODE): $winsockOutput" -Level "WARN"
        }
        else {
            Write-Log -Message "Winsock catalog reset successfully"
        }
    }

    Write-Log -Message "Resetting WinHTTP proxy"
    if ($PSCmdlet.ShouldProcess("WinHTTP", "Reset proxy")) {
        $proxyOutput = & netsh winhttp reset proxy 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Log -Message "netsh winhttp reset proxy failed (exit code $LASTEXITCODE): $proxyOutput" -Level "WARN"
        }
        else {
            Write-Log -Message "WinHTTP proxy reset successfully"
        }
    }
}

function Restore-ServiceSecurity {
    Write-Log -Message "Restoring default service security descriptors"
    $defaultSD = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;AU)(A;;CCLCSWRPWPDTLOCRRC;;;PU)"

    foreach ($svcName in @("bits", "wuauserv")) {
        if ($PSCmdlet.ShouldProcess($svcName, "Restore security descriptor")) {
            $sdOutput = & sc.exe sdset $svcName $defaultSD 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Log -Message "sc.exe sdset $svcName failed (exit code $LASTEXITCODE): $sdOutput" -Level "WARN"
            }
            else {
                Write-Log -Message "Restored security descriptor for '$svcName'"
            }
        }
    }
}

function Start-WUServices {
    Write-Log -Message "Configuring service startup types and starting services"
    $allStarted = $true

    foreach ($svcName in $WU_SERVICES) {
        $svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue
        if (-not $svc) {
            Write-Log -Message "Service '$svcName' not found - skipping" -Level "WARN"
            continue
        }

        # Set startup type
        $desiredStartup = $SERVICE_STARTUP[$svcName]
        if ($PSCmdlet.ShouldProcess($svcName, "Set startup type to $desiredStartup")) {
            try {
                # AutomaticDelayedStart requires a workaround in PS 5.1
                if ($desiredStartup -eq "AutomaticDelayedStart") {
                    Set-Service -Name $svcName -StartupType Automatic -ErrorAction Stop
                    $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$svcName"
                    Set-ItemProperty -Path $regPath -Name "DelayedAutostart" -Value 1 -Type DWord -ErrorAction Stop
                }
                else {
                    Set-Service -Name $svcName -StartupType $desiredStartup -ErrorAction Stop
                }
                Write-Log -Message "Set '$svcName' startup type to $desiredStartup"
            }
            catch {
                Write-Log -Message "Failed to set startup type for '$svcName': $($_.Exception.Message)" -Level "WARN"
            }
        }

        # Start service
        if ($PSCmdlet.ShouldProcess($svcName, "Start service")) {
            try {
                Start-Service -Name $svcName -ErrorAction Stop
                Write-Log -Message "Started service '$svcName'"
            }
            catch {
                Write-Log -Message "Failed to start '$svcName': $($_.Exception.Message)" -Level "WARN"
                $allStarted = $false
            }
        }
    }

    return $allStarted
}

try {
    Write-Log -Message "$SCRIPT_NAME v$SCRIPT_VERSION starting"

    # ── Step 1: Stop Windows Update services ──
    $stopResult = Stop-WUServices
    if (-not $stopResult) {
        Write-Log -Message "Some services could not be stopped - continuing with repair" -Level "WARN"
    }

    # ── Step 2: Clear Windows Update cache ──
    Clear-WUCache

    # ── Step 3: Restore default service security descriptors ──
    Restore-ServiceSecurity

    # ── Step 4: Re-register DLLs ──
    Register-WUDlls

    # ── Step 5: Reset network stack ──
    Reset-NetworkStack

    # ── Step 6: Start services back up ──
    $startResult = Start-WUServices
    if (-not $startResult) {
        Write-Log -Message "Some services failed to start after repair" -Level "WARN"
        Write-Log -Message "$SCRIPT_NAME completed with warnings"
        exit 2
    }

    Write-Log -Message "$SCRIPT_NAME completed successfully"
    exit 0
}
catch {
    Write-Log -Message "FATAL: $($_.Exception.Message)" -Level "ERROR"
    Write-Log -Message "Stack: $($_.ScriptStackTrace)" -Level "ERROR"
    exit 1
}
