<#
.SYNOPSIS
    Deploys and runs Kryoss Agent via NinjaOne (NinjaRMM).

.DESCRIPTION
    Downloads (or locates) the Kryoss agent binary, enrolls the device if needed,
    runs a security assessment scan, creates a daily scheduled task, and writes
    results back to NinjaOne custom fields.

    NINJAONE SETUP:
      1. Create two Device Custom Fields in NinjaOne (Administration > Devices > Custom Fields):
         - "kryossLastScan"    (Text, Automations: Read/Write)
         - "kryossAgentVersion" (Text, Automations: Read/Write)
      2. Create Script Variables (used as parameters):
         - KRYOSS_ENROLL_CODE  (Text, Required)
         - KRYOSS_AGENT_URL    (Text, Optional — URL to download agent binary)
         - KRYOSS_FORCE_REENROLL (Checkbox, Optional)
         - KRYOSS_NO_SCHEDULE  (Checkbox, Optional)
      3. Upload this script: Configuration > Scripting > New Script
      4. Run against device groups or individual devices

    The script is IDEMPOTENT — safe to run multiple times without side effects.

.NOTES
    Author:   TeamLogic IT
    Version:  3.0
    Created:  2026-04-14
    Modified: 2026-04-20
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnrollmentCode,

    [Parameter(Mandatory = $false)]
    [string]$AgentUrl,

    [Parameter(Mandatory = $false)]
    [switch]$ForceReenroll,

    [Parameter(Mandatory = $false)]
    [switch]$NoSchedule
)

$ErrorActionPreference = "Stop"

$SCRIPT_NAME = "Deploy-KryossNinja"
$LOG_DIR     = "C:\ProgramData\TeamLogicIT\Logs"
$logPath     = Join-Path $LOG_DIR "${SCRIPT_NAME}_$(Get-Date -Format 'yyyyMMdd').log"
$AGENT_DIR   = Join-Path $env:ProgramData "Kryoss"
$AGENT_EXE   = Join-Path $AGENT_DIR "KryossAgent.exe"
$REG_PATH    = "HKLM:\SOFTWARE\Kryoss\Agent"
$TASK_NAME   = "Kryoss Agent Scan"

# ── Logging ──
if (-not (Test-Path $LOG_DIR)) { New-Item -Path $LOG_DIR -ItemType Directory -Force | Out-Null }

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$timestamp] [$Level] $Message"
    Write-Host $entry
    Add-Content -Path $logPath -Value $entry
}

# ── Banner ──
Write-Log "Kryoss Agent Deployment — NinjaOne"
Write-Log "Host: $env:COMPUTERNAME | User: $env:USERNAME"

# ── Resolve parameters from NinjaOne Script Variables ──
if (-not $EnrollmentCode) { $EnrollmentCode = $env:KRYOSS_ENROLL_CODE }
if (-not $AgentUrl -and $env:KRYOSS_AGENT_URL) { $AgentUrl = $env:KRYOSS_AGENT_URL }
if (-not $ForceReenroll -and $env:KRYOSS_FORCE_REENROLL -eq "true") { $ForceReenroll = [switch]::Present }
if (-not $NoSchedule -and $env:KRYOSS_NO_SCHEDULE -eq "true") { $NoSchedule = [switch]::Present }

if (-not $EnrollmentCode) {
    Write-Log "Enrollment code required. Set KRYOSS_ENROLL_CODE in NinjaOne Script Variables." "ERROR"
    exit 1
}

# ── Check existing enrollment ──
if (-not $ForceReenroll) {
    try {
        $existing = Get-ItemProperty -Path $REG_PATH -Name "AgentId" -ErrorAction SilentlyContinue
        if ($existing -and $existing.AgentId) {
            Write-Log "Already enrolled (AgentId: $($existing.AgentId)). Running scan only."
            if (Test-Path $AGENT_EXE) {
                $output = & $AGENT_EXE --silent 2>&1
                $exitCode = $LASTEXITCODE
                foreach ($line in $output) { Write-Log $line }

                # Write NinjaOne custom fields
                try {
                    Ninja-Property-Set kryossLastScan (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
                    $ver = (Get-Item $AGENT_EXE).VersionInfo.ProductVersion
                    if ($ver) { Ninja-Property-Set kryossAgentVersion $ver }
                } catch { Write-Log "NinjaOne custom field write failed: $_" "WARN" }

                exit $exitCode
            }
            Write-Log "Agent binary missing at $AGENT_EXE — re-downloading." "WARN"
        }
    } catch { }
}

# ── Defender ASR exclusion (Exploit Guard blocks unsigned executables from ProgramData) ──
try {
    $currentExclusions = (Get-MpPreference).AttackSurfaceReductionOnlyExclusions
    if ($currentExclusions -notcontains $AGENT_EXE) {
        Add-MpPreference -AttackSurfaceReductionOnlyExclusions $AGENT_EXE
        Write-Log "Added Defender ASR exclusion for $AGENT_EXE"
    }
} catch {
    Write-Log "Could not set ASR exclusion (non-Defender AV?): $_" "WARN"
}

# ── Ensure agent directory ──
if (-not (Test-Path $AGENT_DIR)) {
    New-Item -Path $AGENT_DIR -ItemType Directory -Force | Out-Null
    Write-Log "Created $AGENT_DIR"
}

# ── Download or copy agent binary ──
if ($AgentUrl) {
    Write-Log "Downloading agent from $AgentUrl..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $webClient = New-Object System.Net.WebClient
        $webClient.DownloadFile($AgentUrl, $AGENT_EXE)
        $webClient.Dispose()
    } catch {
        Write-Log "Download failed: $_" "ERROR"
        exit 1
    }
} else {
    $searchPaths = @(
        (Split-Path -Parent $MyInvocation.MyCommand.Path),
        "C:\ProgramData\TeamLogicIT",
        "C:\ProgramData\TeamLogicIT\Kryoss",
        $AGENT_DIR
    )
    $localAgent = $null
    foreach ($dir in $searchPaths) {
        $candidate = Join-Path $dir "KryossAgent.exe"
        if (Test-Path $candidate) { $localAgent = $candidate; break }
    }
    if (-not $localAgent) {
        Write-Log "KryossAgent.exe not found in search paths: $($searchPaths -join ', ')" "ERROR"
        exit 1
    }
    if ($localAgent -ne $AGENT_EXE) {
        Copy-Item -Path $localAgent -Destination $AGENT_EXE -Force
    }
    Write-Log "Agent found at $localAgent"
}

if (-not (Test-Path $AGENT_EXE)) {
    Write-Log "Agent binary missing after install step." "ERROR"
    exit 1
}

$fileInfo = Get-Item $AGENT_EXE
$sizeMb = [Math]::Round($fileInfo.Length / 1MB, 1)
$version = $fileInfo.VersionInfo.ProductVersion
Write-Log "Agent installed: $AGENT_EXE ($sizeMb MB, v$version)"

# ── Run agent (enroll + scan) ──
Write-Log "Running Kryoss Agent..."

$agentArgs = @("--silent", "--code", $EnrollmentCode)
if ($ForceReenroll) { $agentArgs += "--reenroll" }

try {
    $ErrorActionPreference = "Continue"
    $output = & $AGENT_EXE $agentArgs 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    foreach ($line in $output) { Write-Log "$line" }

    # ── Write NinjaOne custom fields ──
    try {
        Ninja-Property-Set kryossLastScan (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
        if ($version) { Ninja-Property-Set kryossAgentVersion $version }
    } catch {
        Write-Log "NinjaOne custom field write failed: $_" "WARN"
    }

    switch ($exitCode) {
        0  { Write-Log "Agent completed successfully." }
        1  { Write-Log "Agent failed." "ERROR" }
        2  { Write-Log "Agent completed with warnings (upload deferred)." "WARN" }
        99 { Write-Log "Agent unhandled exception." "ERROR"; $exitCode = 1 }
        default { Write-Log "Agent exited with code $exitCode." "WARN" }
    }
} catch {
    Write-Log "Agent execution failed: $_" "ERROR"
    exit 1
}

# ── Create scheduled task ──
if (-not $NoSchedule) {
    try {
        $existingTask = Get-ScheduledTask -TaskName $TASK_NAME -ErrorAction SilentlyContinue
        if ($existingTask) {
            Unregister-ScheduledTask -TaskName $TASK_NAME -Confirm:$false
            Write-Log "Removed existing scheduled task."
        }

        $action   = New-ScheduledTaskAction -Execute $AGENT_EXE -Argument "--silent"
        $trigger  = New-ScheduledTaskTrigger -Once -At "00:00" `
            -RepetitionInterval (New-TimeSpan -Hours 1) `
            -RepetitionDuration ([TimeSpan]::MaxValue)
        $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet `
            -AllowStartIfOnBatteries `
            -DontStopIfGoingOnBatteries `
            -StartWhenAvailable `
            -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
            -RestartCount 3 `
            -RestartInterval (New-TimeSpan -Minutes 10)

        Register-ScheduledTask -TaskName $TASK_NAME -Action $action -Trigger $trigger `
            -Principal $principal -Settings $settings `
            -Description "Kryoss security assessment — hourly check-in, server-assigned scan slot" | Out-Null

        Write-Log "Scheduled task '$TASK_NAME' created — hourly check-in (server assigns scan time)."
    } catch {
        Write-Log "Scheduled task creation failed: $_" "WARN"
    }
}

# ── Cleanup old logs (30 days) ──
Get-ChildItem -Path $LOG_DIR -Filter "${SCRIPT_NAME}_*.log" -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } |
    Remove-Item -Force -ErrorAction SilentlyContinue

Write-Log "Deployment complete."
exit $exitCode
