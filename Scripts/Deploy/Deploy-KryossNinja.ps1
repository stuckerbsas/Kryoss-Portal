<#
.SYNOPSIS
    Deploys Kryoss Agent as a Windows Service via NinjaOne.

.DESCRIPTION
    Downloads agent binary from Azure Blob Storage, enrolls the device,
    installs as Windows Service (auto-start, heartbeat every 15 min,
    compliance scan every 24h, SNMP every 4h, self-updater every 6h).

    Migrates from legacy scheduled task mode to service mode automatically.

    NINJAONE SETUP:
      1. Create Device Custom Fields (Administration > Devices > Custom Fields):
         - "kryossLastScan"     (Text, Automations: Read/Write)
         - "kryossAgentVersion" (Text, Automations: Read/Write)
      2. Create Script Variables (used as parameters):
         - KRYOSS_ENROLL_CODE   (Text, Required)
         - KRYOSS_BLOB_URL      (Text, Optional - override blob base URL)
         - KRYOSS_FORCE_REENROLL (Checkbox, Optional)
      3. Upload this script: Configuration > Scripting > New Script
      4. Run against device groups or individual devices

    The script is IDEMPOTENT - safe to run multiple times without side effects.

.NOTES
    Author:   TeamLogic IT
    Version:  5.0
    Created:  2026-04-14
    Modified: 2026-04-26
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
    [switch]$ForceReenroll
)

$ErrorActionPreference = "Stop"

$SCRIPT_NAME   = "Deploy-KryossNinja"
$LOG_DIR       = "C:\ProgramData\TeamLogicIT\Logs"
$logPath       = Join-Path $LOG_DIR "${SCRIPT_NAME}_$(Get-Date -Format 'yyyyMMdd').log"
$INSTALL_DIR   = Join-Path $env:ProgramFiles "Kryoss"
$AGENT_EXE     = Join-Path $INSTALL_DIR "KryossAgent.exe"
$LEGACY_DIR    = Join-Path $env:ProgramData "Kryoss"
$LEGACY_EXE    = Join-Path $LEGACY_DIR "KryossAgent.exe"
$REG_PATH      = "HKLM:\SOFTWARE\Kryoss\Agent"
$SERVICE_NAME  = "KryossAgent"
$LEGACY_TASK   = "Kryoss Agent Scan"
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
Write-Log "Kryoss Agent Deployment v5.0 (Service Mode) - NinjaOne"
Write-Log "Host: $env:COMPUTERNAME | User: $env:USERNAME"

# ── Resolve parameters from NinjaOne Script Variables ──
if (-not $EnrollmentCode) { $EnrollmentCode = $env:KRYOSS_ENROLL_CODE }
if (-not $BlobBaseUrl -and $env:KRYOSS_BLOB_URL) { $BlobBaseUrl = $env:KRYOSS_BLOB_URL }
if (-not $AgentUrl -and $env:KRYOSS_AGENT_URL) { $AgentUrl = $env:KRYOSS_AGENT_URL }
if (-not $ForceReenroll -and $env:KRYOSS_FORCE_REENROLL -eq "true") { $ForceReenroll = [switch]::Present }

if (-not $EnrollmentCode) {
    Write-Log "Enrollment code required. Set KRYOSS_ENROLL_CODE in NinjaOne Script Variables." "ERROR"
    exit 1
}

# Resolve blob URL
if (-not $BlobBaseUrl) {
    if ($AgentUrl) { $BlobBaseUrl = $null }
    else { $BlobBaseUrl = $DEFAULT_BLOB }
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

# ── Ensure install directory ──
if (-not (Test-Path $INSTALL_DIR)) {
    New-Item -Path $INSTALL_DIR -ItemType Directory -Force | Out-Null
    Write-Log "Created $INSTALL_DIR"
}

# ── Migrate from legacy location (C:\ProgramData\Kryoss) ──
if ((Test-Path $LEGACY_EXE) -and ($LEGACY_DIR -ne $INSTALL_DIR)) {
    Write-Log "Migrating from legacy location $LEGACY_DIR..."
    # Copy registry data (enrollment is in HKLM, stays)
    # Copy binary
    if (-not (Test-Path $AGENT_EXE)) {
        Copy-Item -Path $LEGACY_EXE -Destination $AGENT_EXE -Force
        Write-Log "Copied binary from legacy location."
    }
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
        # Strip +commithash suffix (e.g., "2.3.0+abc123" -> "2.3.0")
        if ($remoteVer -match '^\d+\.\d+\.\d+') {
            return $Matches[0]
        }
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

        # Stop service or process before replacing binary
        $svc = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
        if ($svc -and $svc.Status -eq 'Running') {
            Stop-Service -Name $SERVICE_NAME -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            Write-Log "Stopped $SERVICE_NAME service for update."
        }
        Get-Process -Name "KryossAgent" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1

        # Atomic replace
        if (Test-Path $AGENT_EXE) {
            $backupPath = "$AGENT_EXE.bak"
            Move-Item -Path $AGENT_EXE -Destination $backupPath -Force
        }
        Move-Item -Path $tempPath -Destination $AGENT_EXE -Force

        $sizeMb = [Math]::Round((Get-Item $AGENT_EXE).Length / 1MB, 1)
        $newVer = Get-LocalVersion
        Write-Log "Agent updated: v$newVer ($sizeMb MB)"

        if (Test-Path "$AGENT_EXE.bak") {
            Remove-Item "$AGENT_EXE.bak" -Force -ErrorAction SilentlyContinue
        }
        return $true
    } catch {
        Write-Log "Download failed: $_" "ERROR"
        Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
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
    if (-not $localVersion) {
        $ok = Download-Agent -Url $AgentUrl
        if (-not $ok) { exit 1 }
    } else {
        Write-Log "Agent exists: v$localVersion (legacy URL mode, no version check)"
    }
} else {
    if (-not (Test-Path $AGENT_EXE)) {
        Write-Log "Agent binary not found at $AGENT_EXE and no download URL configured." "ERROR"
        exit 1
    }
}

if (-not (Test-Path $AGENT_EXE)) {
    Write-Log "Agent binary missing after download step." "ERROR"
    exit 1
}

$fileInfo = Get-Item $AGENT_EXE
$sizeMb = [Math]::Round($fileInfo.Length / 1MB, 1)
$version = $fileInfo.VersionInfo.ProductVersion
Write-Log "Agent ready: $AGENT_EXE ($sizeMb MB, v$version)"

# ── Enroll (one-shot, only if not already enrolled) ──
$agentId = $null
try { $agentId = Get-ItemPropertyValue -Path $REG_PATH -Name "AgentId" -ErrorAction SilentlyContinue } catch {}

if (-not $agentId -or $ForceReenroll) {
    Write-Log "Enrolling device..."
    $enrollArgs = @("--silent", "--alone", "--code", $EnrollmentCode)
    if ($ForceReenroll) { $enrollArgs += "--reenroll" }

    $ErrorActionPreference = "Continue"
    $output = & $AGENT_EXE $enrollArgs 2>&1
    $enrollExit = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    foreach ($line in $output) { Write-Log "$line" }

    if ($enrollExit -ne 0 -and $enrollExit -ne 2) {
        Write-Log "Enrollment failed with exit code $enrollExit." "ERROR"
        exit 1
    }
    Write-Log "Enrollment complete."
} else {
    Write-Log "Already enrolled: AgentId=$agentId"
}

# ── Remove legacy scheduled task ──
try {
    $existingTask = Get-ScheduledTask -TaskName $LEGACY_TASK -ErrorAction SilentlyContinue
    if ($existingTask) {
        Unregister-ScheduledTask -TaskName $LEGACY_TASK -Confirm:$false
        Write-Log "Removed legacy scheduled task '$LEGACY_TASK'."
    }
} catch {
    Write-Log "Could not remove legacy task: $_" "WARN"
}

# ── Install/restart Windows Service ──
$svc = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue

if ($svc) {
    Write-Log "Service '$SERVICE_NAME' already exists (Status: $($svc.Status))."
    if ($needsDownload) {
        # Binary was updated, restart service
        if ($svc.Status -eq 'Running') {
            Stop-Service -Name $SERVICE_NAME -Force
            Start-Sleep -Seconds 2
        }
        Start-Service -Name $SERVICE_NAME
        Write-Log "Service restarted with updated binary."
    } elseif ($svc.Status -ne 'Running') {
        Start-Service -Name $SERVICE_NAME
        Write-Log "Service started."
    }
} else {
    Write-Log "Installing Windows Service..."
    $ErrorActionPreference = "Continue"
    $output = & $AGENT_EXE --install 2>&1
    $installExit = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    foreach ($line in $output) { Write-Log "$line" }

    if ($installExit -ne 0) {
        Write-Log "Service installation failed with exit code $installExit." "ERROR"
        exit 1
    }

    $svc = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Log "Service '$SERVICE_NAME' installed (Status: $($svc.Status))."
    } else {
        Write-Log "Service not found after install. Check agent logs." "ERROR"
        exit 1
    }
}

# ── Write NinjaOne custom fields ──
try {
    Ninja-Property-Set kryossLastScan (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    if ($version) { Ninja-Property-Set kryossAgentVersion $version }
} catch {
    Write-Log "NinjaOne custom field write failed: $_" "WARN"
}

# ── Cleanup legacy location ──
if ((Test-Path $LEGACY_EXE) -and ($LEGACY_DIR -ne $INSTALL_DIR)) {
    try {
        Remove-Item -Path $LEGACY_DIR -Recurse -Force -ErrorAction SilentlyContinue
        Write-Log "Cleaned up legacy directory $LEGACY_DIR."
    } catch {
        Write-Log "Could not clean legacy dir: $_" "WARN"
    }
}

# ── Cleanup old logs (30 days) ──
Get-ChildItem -Path $LOG_DIR -Filter "${SCRIPT_NAME}_*.log" -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } |
    Remove-Item -Force -ErrorAction SilentlyContinue

Write-Log "Deployment complete. Service mode active."
exit 0
