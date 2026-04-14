<#
.SYNOPSIS
    Deploys Kryoss Agent v1.3.0+ via NinjaOne (NinjaRMM).

.DESCRIPTION
    This script is intended to be uploaded as a NinjaOne SCRIPT and run against
    devices or device groups. NinjaOne handles the remote execution — this script
    just downloads/installs the Kryoss agent on the local machine.

    FLOW:
      1. Upload this script to NinjaOne: Configuration > Scripts > New Script
      2. Upload KryossAgent.exe to NinjaOne: Configuration > Scripts > Attachments
         (or host it on a public URL accessible by all customer devices)
      3. Set the enrollment code as a NinjaOne Script Variable: KRYOSS_ENROLL_CODE
      4. Run against device groups from the NinjaOne console

    The script is IDEMPOTENT — it checks if the agent is already enrolled and
    only re-enrolls when --reenroll is forced.

.PARAMETER EnrollmentCode
    Enrollment code. Can also be read from environment variable KRYOSS_ENROLL_CODE
    (set via NinjaOne Script Variables).

.PARAMETER AgentUrl
    URL or local path to KryossAgent.exe. If a URL, it will be downloaded.
    If blank, assumes the script is running next to KryossAgent.exe.

.PARAMETER ForceReenroll
    If specified, wipes existing enrollment and re-enrolls.
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnrollmentCode,

    [Parameter(Mandatory = $false)]
    [string]$AgentUrl,

    [Parameter(Mandatory = $false)]
    [switch]$ForceReenroll
)

$ErrorActionPreference = "Stop"

# ── Banner (NinjaOne logs capture Write-Host output) ──
Write-Host "Kryoss Agent Deployment — NinjaOne"
Write-Host "Host: $env:COMPUTERNAME"
Write-Host "Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

# ── Read enrollment code from NinjaOne Script Variable if not passed ──
if (-not $EnrollmentCode) {
    $EnrollmentCode = $env:KRYOSS_ENROLL_CODE
}

if (-not $EnrollmentCode) {
    Write-Error "Enrollment code required. Pass -EnrollmentCode or set KRYOSS_ENROLL_CODE env var in NinjaOne Script Variables."
    exit 1
}

# ── Check if already enrolled ──
$regPath = "HKLM:\SOFTWARE\Kryoss\Agent"
if (-not $ForceReenroll) {
    try {
        $existing = Get-ItemProperty -Path $regPath -Name "AgentId" -ErrorAction SilentlyContinue
        if ($existing -and $existing.AgentId) {
            Write-Host "RESULT: SKIP | $env:COMPUTERNAME | Already enrolled (AgentId: $($existing.AgentId))"
            Write-Host "Use -ForceReenroll to re-enroll."
            # Still run the scan on subsequent invocations
            $agentExe = Join-Path $env:ProgramData "Kryoss\KryossAgent.exe"
            if (Test-Path $agentExe) {
                Write-Host "Running scheduled scan..."
                & $agentExe --silent 2>&1 | ForEach-Object { Write-Host $_ }
                exit $LASTEXITCODE
            }
        }
    }
    catch { }
}

# ── Resolve agent binary ──
$agentDir = Join-Path $env:ProgramData "Kryoss"
$agentExe = Join-Path $agentDir "KryossAgent.exe"

if (-not (Test-Path $agentDir)) {
    New-Item -Path $agentDir -ItemType Directory -Force | Out-Null
}

if ($AgentUrl) {
    # Download from URL
    Write-Host "Downloading agent from $AgentUrl..."
    try {
        # Use TLS 1.2+ for HTTPS downloads
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $AgentUrl -OutFile $agentExe -UseBasicParsing
    }
    catch {
        Write-Error "Failed to download agent: $_"
        Write-Host "RESULT: ERROR | $env:COMPUTERNAME | Agent download failed"
        exit 1
    }
}
else {
    # Assume agent is next to this script (NinjaOne attachment)
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $localAgent = Join-Path $scriptDir "KryossAgent.exe"
    if (-not (Test-Path $localAgent)) {
        Write-Error "KryossAgent.exe not found next to script and no -AgentUrl provided"
        Write-Host "RESULT: ERROR | $env:COMPUTERNAME | Agent binary missing"
        exit 1
    }
    Copy-Item -Path $localAgent -Destination $agentExe -Force
}

if (-not (Test-Path $agentExe)) {
    Write-Error "Agent binary missing after installation step"
    Write-Host "RESULT: ERROR | $env:COMPUTERNAME | Agent binary missing"
    exit 1
}

$size = [Math]::Round((Get-Item $agentExe).Length / 1MB, 1)
Write-Host "Agent installed at $agentExe ($size MB)"

# ── Run the agent ──
Write-Host "Running Kryoss Agent..."

$agentArgs = @("--silent", "--code", $EnrollmentCode)
if ($ForceReenroll) {
    $agentArgs += "--reenroll"
}

try {
    $output = & $agentExe $agentArgs 2>&1
    $exitCode = $LASTEXITCODE

    # Forward agent RESULT: lines (NinjaOne parses these)
    foreach ($line in $output) {
        Write-Host $line
    }

    switch ($exitCode) {
        0  { Write-Host "Agent completed successfully."; exit 0 }
        1  { Write-Host "Agent failed fatally."; exit 1 }
        2  { Write-Host "Agent completed with warnings (upload deferred)."; exit 0 }
        99 { Write-Host "Agent unhandled exception."; exit 1 }
        default { Write-Host "Agent exited with code $exitCode."; exit $exitCode }
    }
}
catch {
    Write-Error "Agent execution failed: $_"
    Write-Host "RESULT: ERROR | $env:COMPUTERNAME | $_"
    exit 1
}
