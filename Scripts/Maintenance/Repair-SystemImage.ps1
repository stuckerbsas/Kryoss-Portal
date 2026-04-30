#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Repairs Windows system image using DISM and SFC.

.DESCRIPTION
    Runs a sequential repair pipeline to detect and fix Windows component store
    and system file corruption. Executes DISM CheckHealth, ScanHealth, and
    RestoreHealth followed by SFC /scannow. Each step is logged with its exit
    code and the script returns an appropriate NinjaRMM exit code.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-04
    Modified: 2026-04-04

.EXAMPLE
    .\Repair-SystemImage.ps1
    Runs full DISM + SFC repair pipeline with logging.

.EXAMPLE
    .\Repair-SystemImage.ps1 -WhatIf
    Shows what repair steps would be executed without running them.
#>

[CmdletBinding(SupportsShouldProcess)]
param()

$SCRIPT_NAME    = "Repair-SystemImage"
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

# DISM exit codes:
#   0 = success
#   1 = error (but SFC uses different codes)
# SFC exit codes:
#   0 = no integrity violations found OR successfully repaired
#   1 = repair needed but could not be completed
#   2 = operation could not be performed (various reasons)

function Invoke-RepairStep {
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

    if ($PSCmdlet.ShouldProcess($StepName, "Execute repair step")) {
        $output = & $Command @Arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE

        # Log command output (trim excess whitespace)
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

    $hasFailure  = $false
    $hasWarning  = $false

    # Step 1: DISM CheckHealth
    $exitCode = Invoke-RepairStep -StepName "DISM CheckHealth" `
        -Command "DISM" `
        -Arguments @("/Online", "/Cleanup-Image", "/CheckHealth")

    if ($exitCode -ne 0) {
        Write-Log -Message "DISM CheckHealth returned non-zero exit code: $exitCode" -Level "WARN"
        $hasWarning = $true
    }

    # Step 2: DISM ScanHealth
    $exitCode = Invoke-RepairStep -StepName "DISM ScanHealth" `
        -Command "DISM" `
        -Arguments @("/Online", "/Cleanup-Image", "/ScanHealth")

    if ($exitCode -ne 0) {
        Write-Log -Message "DISM ScanHealth returned non-zero exit code: $exitCode" -Level "WARN"
        $hasWarning = $true
    }

    # Step 3: DISM RestoreHealth
    $exitCode = Invoke-RepairStep -StepName "DISM RestoreHealth" `
        -Command "DISM" `
        -Arguments @("/Online", "/Cleanup-Image", "/RestoreHealth")

    if ($exitCode -ne 0) {
        Write-Log -Message "DISM RestoreHealth failed with exit code: $exitCode" -Level "ERROR"
        $hasFailure = $true
    }

    # Step 4: SFC /scannow
    $exitCode = Invoke-RepairStep -StepName "SFC Scannow" `
        -Command "SFC" `
        -Arguments @("/scannow")

    if ($exitCode -ne 0) {
        Write-Log -Message "SFC /scannow failed with exit code: $exitCode" -Level "ERROR"
        $hasFailure = $true
    }

    # Determine final exit code
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
