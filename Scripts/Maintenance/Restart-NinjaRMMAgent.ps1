#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Restarts the NinjaRMM Agent service safely.

.DESCRIPTION
    Checks the NinjaRMMAgent service state, performs a graceful restart using
    Restart-Service, waits for the service to return to a running state with a
    configurable timeout, and verifies the final status. Logs previous state,
    action taken, and new state. Designed for silent deployment via NinjaRMM.

.PARAMETER TimeoutSeconds
    Maximum seconds to wait for the service to reach Running state after restart.
    Defaults to 120 seconds.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-04
    Modified: 2026-04-04

.EXAMPLE
    .\Restart-NinjaRMMAgent.ps1

.EXAMPLE
    .\Restart-NinjaRMMAgent.ps1 -TimeoutSeconds 180
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $false)]
    [int]$TimeoutSeconds = 120
)

$SCRIPT_NAME    = "Restart-NinjaRMMAgent"
$SCRIPT_VERSION = "1.0"
$SERVICE_NAME   = "NinjaRMMAgent"
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

    # ── Step 1: Verify service exists ──
    $service = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Log -Message "Service '$SERVICE_NAME' not found on this system" -Level "ERROR"
        exit 1
    }

    $previousStatus = $service.Status
    $previousStartType = $service.StartType
    Write-Log -Message "Service '$SERVICE_NAME' found - Status: $previousStatus, StartType: $previousStartType"

    # ── Step 2: Restart the service ──
    if ($PSCmdlet.ShouldProcess($SERVICE_NAME, "Restart service")) {
        Write-Log -Message "Restarting service '$SERVICE_NAME'"
        try {
            Restart-Service -Name $SERVICE_NAME -Force -ErrorAction Stop
            Write-Log -Message "Restart-Service command completed"
        }
        catch {
            Write-Log -Message "Restart-Service failed: $($_.Exception.Message)" -Level "ERROR"
            Write-Log -Message "Attempting manual stop/start cycle" -Level "WARN"

            try {
                Stop-Service -Name $SERVICE_NAME -Force -ErrorAction Stop
                Write-Log -Message "Service stopped successfully"
            }
            catch {
                Write-Log -Message "Stop-Service failed: $($_.Exception.Message)" -Level "WARN"
            }

            Start-Service -Name $SERVICE_NAME -ErrorAction Stop
            Write-Log -Message "Service started successfully via manual stop/start"
        }

        # ── Step 3: Wait for service to reach Running state ──
        Write-Log -Message "Waiting up to $TimeoutSeconds seconds for service to reach Running state"
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $serviceRunning = $false

        while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            $service.Refresh()
            if ($service.Status -eq 'Running') {
                $serviceRunning = $true
                break
            }
            Start-Sleep -Seconds 2
        }
        $stopwatch.Stop()

        if (-not $serviceRunning) {
            Write-Log -Message "Service '$SERVICE_NAME' did not reach Running state within $TimeoutSeconds seconds (current: $($service.Status))" -Level "ERROR"
            exit 1
        }

        $elapsedSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 1)
        Write-Log -Message "Service '$SERVICE_NAME' is Running after $elapsedSeconds seconds"
    }

    # ── Step 4: Verify and log final state ──
    $service.Refresh()
    $newStatus = $service.Status
    Write-Log -Message "Previous state: $previousStatus -> New state: $newStatus"

    if ($newStatus -ne 'Running') {
        Write-Log -Message "Service '$SERVICE_NAME' is not in Running state after restart (status: $newStatus)" -Level "ERROR"
        exit 1
    }

    Write-Log -Message "$SCRIPT_NAME completed successfully"
    exit 0
}
catch {
    Write-Log -Message "FATAL: $($_.Exception.Message)" -Level "ERROR"
    Write-Log -Message "Stack: $($_.ScriptStackTrace)" -Level "ERROR"
    exit 1
}
