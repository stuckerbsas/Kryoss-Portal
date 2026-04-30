#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Runs Windows Disk Cleanup using a preconfigured sageset profile.

.DESCRIPTION
    Executes cleanmgr.exe with /sagerun:2 to perform disk cleanup using a
    previously configured cleanup profile (sageset 2). Verifies that
    cleanmgr.exe exists before execution, logs the operation, and checks the
    exit code. The sageset profile must be configured beforehand via
    Set-DiskCleanupRegistry.ps1 or manually via cleanmgr /sageset:2.

.PARAMETER SageRunId
    The sageset/sagerun profile ID to use. Defaults to 2.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-04
    Modified: 2026-04-04

.EXAMPLE
    .\Invoke-DiskCleanup.ps1
    Runs disk cleanup using sagerun profile 2.

.EXAMPLE
    .\Invoke-DiskCleanup.ps1 -SageRunId 1
    Runs disk cleanup using sagerun profile 1.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $false)]
    [ValidateRange(0, 65535)]
    [int]$SageRunId = 2
)

$SCRIPT_NAME    = "Invoke-DiskCleanup"
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

try {
    Write-Log -Message "$SCRIPT_NAME v$SCRIPT_VERSION starting"

    # Verify cleanmgr.exe exists
    $cleanmgrPath = Join-Path -Path $env:SystemRoot -ChildPath "System32\cleanmgr.exe"
    if (-not (Test-Path -Path $cleanmgrPath)) {
        Write-Log -Message "cleanmgr.exe not found at '$cleanmgrPath'. Disk Cleanup may not be installed on this system (e.g., Server Core)." -Level "ERROR"
        exit 1
    }

    Write-Log -Message "Found cleanmgr.exe at '$cleanmgrPath'"
    Write-Log -Message "Using sagerun profile ID: $SageRunId"

    # Check if sageset registry keys exist
    $sagesetRegPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches"
    if (Test-Path -Path $sagesetRegPath) {
        Write-Log -Message "Volume Caches registry path exists"
    }
    else {
        Write-Log -Message "Volume Caches registry path not found — cleanup categories may not be configured" -Level "WARN"
    }

    if ($PSCmdlet.ShouldProcess("Disk Cleanup with sagerun:$SageRunId", "Execute cleanmgr.exe")) {
        Write-Log -Message "Executing: cleanmgr /sagerun:$SageRunId"

        $process = Start-Process -FilePath $cleanmgrPath `
            -ArgumentList "/sagerun:$SageRunId" `
            -Wait `
            -PassThru `
            -NoNewWindow

        $exitCode = $process.ExitCode
        Write-Log -Message "cleanmgr.exe exited with code: $exitCode"

        if ($exitCode -ne 0) {
            Write-Log -Message "Disk Cleanup returned non-zero exit code: $exitCode" -Level "WARN"
            Write-Log -Message "$SCRIPT_NAME completed with warnings" -Level "WARN"
            exit 2
        }

        Write-Log -Message "$SCRIPT_NAME completed successfully"
        exit 0
    }
    else {
        Write-Log -Message "WhatIf: Skipped disk cleanup execution"
        exit 0
    }
}
catch {
    Write-Log -Message "FATAL: $($_.Exception.Message)" -Level "ERROR"
    Write-Log -Message "Stack: $($_.ScriptStackTrace)" -Level "ERROR"
    exit 1
}
