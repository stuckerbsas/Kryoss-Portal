<#
.SYNOPSIS
    Deploys and runs Kryoss Agent via NinjaOne (NinjaRMM).

.DESCRIPTION
    Auto-updates agent binary from Azure Blob Storage, enrolls the device if
    needed, runs a security assessment scan, creates an hourly scheduled task,
    and writes results back to NinjaOne custom fields.

    On every run the script compares the remote version.txt in blob with the
    local binary's ProductVersion. If they differ (or local binary is missing),
    it downloads the new .exe — zero manual intervention required.

    NINJAONE SETUP:
      1. Create Device Custom Fields (Administration > Devices > Custom Fields):
         - "kryossLastScan"     (Text, Automations: Read/Write)
         - "kryossAgentVersion" (Text, Automations: Read/Write)
      2. Create Script Variables (used as parameters):
         - KRYOSS_ENROLL_CODE   (Text, Required)
         - KRYOSS_BLOB_URL      (Text, Optional — override blob base URL)
         - KRYOSS_FORCE_REENROLL (Checkbox, Optional)
         - KRYOSS_NO_SCHEDULE   (Checkbox, Optional)
      3. Upload this script: Configuration > Scripting > New Script
      4. Run against device groups or individual devices

    The script is IDEMPOTENT — safe to run multiple times without side effects.

.NOTES
    Author:   TeamLogic IT
    Version:  4.0
    Created:  2026-04-14
    Modified: 2026-04-21
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnrollmentCode,

    [Parameter(Mandatory = $false)]
    [string]$BlobBaseUrl,

    [Parameter(Mandatory = $false)]
    [string]$AgentUrl,

    [Parameter(Mandatory = $false)]
    [switch]$ForceReenroll,

    [Parameter(Mandatory = $false)]
    [switch]$NoSchedule
)

$ErrorActionPreference = "Stop"

$SCRIPT_NAME   = "Deploy-KryossNinja"
$LOG_DIR       = "C:\ProgramData\TeamLogicIT\Logs"
$logPath       = Join-Path $LOG_DIR "${SCRIPT_NAME}_$(Get-Date -Format 'yyyyMMdd').log"
$AGENT_DIR     = Join-Path $env:ProgramData "Kryoss"
$AGENT_EXE     = Join-Path $AGENT_DIR "KryossAgent.exe"
$REG_PATH      = "HKLM:\SOFTWARE\Kryoss\Agent"
$TASK_NAME     = "Kryoss Agent Scan"
$DEFAULT_BLOB  = "https://stkryossagent.blob.core.windows.net/kryoss-agent-templates/latest"

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
Write-Log "Kryoss Agent Deployment v4.0 — NinjaOne"
Write-Log "Host: $env:COMPUTERNAME | User: $env:USERNAME"

# ── Resolve parameters from NinjaOne Script Variables ──
if (-not $EnrollmentCode) { $EnrollmentCode = $env:KRYOSS_ENROLL_CODE }
if (-not $BlobBaseUrl -and $env:KRYOSS_BLOB_URL) { $BlobBaseUrl = $env:KRYOSS_BLOB_URL }
if (-not $AgentUrl -and $env:KRYOSS_AGENT_URL) { $AgentUrl = $env:KRYOSS_AGENT_URL }
if (-not $ForceReenroll -and $env:KRYOSS_FORCE_REENROLL -eq "true") { $ForceReenroll = [switch]::Present }
if (-not $NoSchedule -and $env:KRYOSS_NO_SCHEDULE -eq "true") { $NoSchedule = [switch]::Present }

if (-not $EnrollmentCode) {
    Write-Log "Enrollment code required. Set KRYOSS_ENROLL_CODE in NinjaOne Script Variables." "ERROR"
    exit 1
}

# Resolve blob URL (BlobBaseUrl > AgentUrl > default)
if (-not $BlobBaseUrl) {
    if ($AgentUrl) {
        # Legacy: single URL mode — skip version check, direct download
        $BlobBaseUrl = $null
    } else {
        $BlobBaseUrl = $DEFAULT_BLOB
    }
}

# ── Defender ASR exclusion ──
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

# ── Auto-update from blob ──
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Get-LocalVersion {
    if (-not (Test-Path $AGENT_EXE)) { return $null }
    $ver = (Get-Item $AGENT_EXE).VersionInfo.ProductVersion
    if ($ver) { return $ver.Trim() }
    return $null
}

function Get-RemoteVersion {
    param([string]$BaseUrl)
    try {
        $versionUrl = "$BaseUrl/version.txt"
        $wc = New-Object System.Net.WebClient
        $remoteVer = $wc.DownloadString($versionUrl).Trim()
        $wc.Dispose()
        return $remoteVer
    } catch {
        Write-Log "Could not fetch remote version from $versionUrl : $_" "WARN"
        return $null
    }
}

function Download-Agent {
    param([string]$Url)
    $tempPath = "$AGENT_EXE.tmp"
    try {
        Write-Log "Downloading agent from $Url..."
        $wc = New-Object System.Net.WebClient
        $wc.DownloadFile($Url, $tempPath)
        $wc.Dispose()

        # Verify download is a valid PE
        $bytes = [System.IO.File]::ReadAllBytes($tempPath)
        if ($bytes.Length -lt 1024 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
            Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
            Write-Log "Downloaded file is not a valid executable." "ERROR"
            return $false
        }

        # Atomic replace: stop any running agent, swap file
        Get-Process -Name "KryossAgent" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1

        if (Test-Path $AGENT_EXE) {
            $backupPath = "$AGENT_EXE.bak"
            Move-Item -Path $AGENT_EXE -Destination $backupPath -Force
        }
        Move-Item -Path $tempPath -Destination $AGENT_EXE -Force

        $sizeMb = [Math]::Round((Get-Item $AGENT_EXE).Length / 1MB, 1)
        $newVer = Get-LocalVersion
        Write-Log "Agent updated: v$newVer ($sizeMb MB)"

        # Cleanup backup
        if (Test-Path "$AGENT_EXE.bak") {
            Remove-Item "$AGENT_EXE.bak" -Force -ErrorAction SilentlyContinue
        }
        return $true
    } catch {
        Write-Log "Download failed: $_" "ERROR"
        Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
        # Restore backup if swap failed
        if ((Test-Path "$AGENT_EXE.bak") -and -not (Test-Path $AGENT_EXE)) {
            Move-Item "$AGENT_EXE.bak" $AGENT_EXE -Force
            Write-Log "Restored previous binary from backup." "WARN"
        }
        return $false
    }
}

$needsDownload = $false
$localVersion = Get-LocalVersion

if ($BlobBaseUrl) {
    # Blob mode: check version.txt, download if different
    $remoteVersion = Get-RemoteVersion -BaseUrl $BlobBaseUrl
    if ($remoteVersion) {
        if (-not $localVersion) {
            Write-Log "No local binary. Remote: v$remoteVersion"
            $needsDownload = $true
        } elseif ($localVersion -ne $remoteVersion) {
            Write-Log "Update available: local v$localVersion -> remote v$remoteVersion"
            $needsDownload = $true
        } else {
            Write-Log "Agent up to date: v$localVersion"
        }
    } else {
        if (-not $localVersion) {
            Write-Log "Cannot determine remote version and no local binary." "ERROR"
            exit 1
        }
        Write-Log "Cannot check remote version. Using local v$localVersion"
    }

    if ($needsDownload) {
        $exeUrl = "$BlobBaseUrl/KryossAgent.exe"
        $ok = Download-Agent -Url $exeUrl
        if (-not $ok -and -not $localVersion) { exit 1 }
    }
} elseif ($AgentUrl) {
    # Legacy single-URL mode: always download if no local binary
    if (-not $localVersion) {
        $ok = Download-Agent -Url $AgentUrl
        if (-not $ok) { exit 1 }
    } else {
        Write-Log "Agent exists: v$localVersion (legacy URL mode, no version check)"
    }
} else {
    # Fallback: search local paths
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
Write-Log "Agent ready: $AGENT_EXE ($sizeMb MB, v$version)"

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

        # Full XML — bypasses PS 5.1 cmdlet RepetitionDuration bugs entirely
        $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <Triggers>
    <TimeTrigger>
      <StartBoundary>2026-01-01T00:00:00</StartBoundary>
      <Enabled>true</Enabled>
      <Repetition>
        <Interval>PT1H</Interval>
      </Repetition>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>S-1-5-18</UserId>
      <LogonType>ServiceAccount</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
    <RestartOnFailure>
      <Interval>PT10M</Interval>
      <Count>3</Count>
    </RestartOnFailure>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$AGENT_EXE</Command>
      <Arguments>--silent</Arguments>
    </Exec>
  </Actions>
</Task>
"@
        $xmlPath = Join-Path $env:TEMP "KryossTask.xml"
        $taskXml | Out-File -FilePath $xmlPath -Encoding Unicode -Force
        $schtasksOut = & schtasks.exe /Create /TN $TASK_NAME /XML $xmlPath /F 2>&1
        Remove-Item -Path $xmlPath -Force -ErrorAction SilentlyContinue
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Scheduled task '$TASK_NAME' created — hourly check-in (server assigns scan time)."
        } else {
            Write-Log "schtasks failed: $schtasksOut" "WARN"
        }
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
