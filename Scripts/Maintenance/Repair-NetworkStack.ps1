#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Resets the Windows network stack using ipconfig and netsh commands.

.DESCRIPTION
    Performs a full network stack reset by releasing and renewing the IP address,
    flushing the DNS resolver cache, resetting the Winsock catalog, and resetting
    the TCP/IP stack. Each step is logged with its exit code. A system restart is
    recommended after execution for changes to take full effect but is NOT
    triggered by this script.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-04
    Modified: 2026-04-04

.EXAMPLE
    .\Repair-NetworkStack.ps1
    Runs full network stack reset with logging.

.EXAMPLE
    .\Repair-NetworkStack.ps1 -WhatIf
    Shows what network reset steps would be executed without running them.
#>

[CmdletBinding(SupportsShouldProcess)]
param()

$SCRIPT_NAME    = "Repair-NetworkStack"
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

function Invoke-NetworkStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StepName,
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Log -Message "Starting: $StepName"
    Write-Log -Message "Command: $Command $($Arguments -join ' ')"

    if ($PSCmdlet.ShouldProcess($StepName, "Execute network reset step")) {
        $output = & $Command @Arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE

        $trimmedOutput = ($output -split "`n" | Where-Object { $_.Trim() -ne '' }) -join "`n"
        if ($trimmedOutput) {
            foreach ($line in ($trimmedOutput -split "`n")) {
                Write-Log -Message "  $($line.Trim())"
            }
        }

        Write-Log -Message "Exit code for '$StepName': $exitCode"
        return $exitCode
    }
    else {
        Write-Log -Message "WhatIf: Skipped '$StepName'"
        return 0
    }
}

try {
    Write-Log -Message "$SCRIPT_NAME v$SCRIPT_VERSION starting"

    $hasFailure = $false
    $hasWarning = $false

    # Step 1: Release IP address
    $exitCode = Invoke-NetworkStep -StepName "IP Release" `
        -Command "ipconfig" `
        -Arguments @("/release")

    # ipconfig /release may return non-zero if no DHCP adapter exists — treat as warning
    if ($exitCode -ne 0) {
        Write-Log -Message "IP release returned non-zero exit code: $exitCode (may indicate no DHCP adapter)" -Level "WARN"
        $hasWarning = $true
    }

    # Step 2: Renew IP address
    $exitCode = Invoke-NetworkStep -StepName "IP Renew" `
        -Command "ipconfig" `
        -Arguments @("/renew")

    if ($exitCode -ne 0) {
        Write-Log -Message "IP renew returned non-zero exit code: $exitCode" -Level "WARN"
        $hasWarning = $true
    }

    # Step 3: Flush DNS resolver cache
    $exitCode = Invoke-NetworkStep -StepName "DNS Flush" `
        -Command "ipconfig" `
        -Arguments @("/flushdns")

    if ($exitCode -ne 0) {
        Write-Log -Message "DNS flush failed with exit code: $exitCode" -Level "ERROR"
        $hasFailure = $true
    }

    # Step 4: Reset Winsock catalog
    $exitCode = Invoke-NetworkStep -StepName "Winsock Reset" `
        -Command "netsh" `
        -Arguments @("winsock", "reset")

    if ($exitCode -ne 0) {
        Write-Log -Message "Winsock reset failed with exit code: $exitCode" -Level "ERROR"
        $hasFailure = $true
    }

    # Step 5: Reset TCP/IP stack
    $exitCode = Invoke-NetworkStep -StepName "TCP/IP Stack Reset" `
        -Command "netsh" `
        -Arguments @("int", "ip", "reset")

    if ($exitCode -ne 0) {
        Write-Log -Message "TCP/IP stack reset failed with exit code: $exitCode" -Level "ERROR"
        $hasFailure = $true
    }

    # Final status
    Write-Log -Message "A system restart is recommended for all changes to take full effect" -Level "WARN"

    if ($hasFailure) {
        Write-Log -Message "$SCRIPT_NAME completed with errors" -Level "ERROR"
        exit 1
    }
    elseif ($hasWarning) {
        Write-Log -Message "$SCRIPT_NAME completed with warnings" -Level "WARN"
        exit 2
    }
    else {
        Write-Log -Message "$SCRIPT_NAME completed successfully"
        exit 0
    }
}
catch {
    Write-Log -Message "FATAL: $($_.Exception.Message)" -Level "ERROR"
    Write-Log -Message "Stack: $($_.ScriptStackTrace)" -Level "ERROR"
    exit 1
}
