# Install-KryossService.ps1
# Downloads and installs KryossAgent as a Windows Service
# Run as Administrator

#Requires -RunAsAdministrator
param(
    [string]$DownloadUrl,
    [string]$InstallDir = "$env:ProgramFiles\Kryoss",
    [string]$EnrollCode
)

$ErrorActionPreference = 'Stop'

Write-Host "[Kryoss] Installing Kryoss Security Agent as Windows Service..." -ForegroundColor Green

# Create install directory
if (!(Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

$exePath = Join-Path $InstallDir "KryossAgent.exe"

# Download if URL provided, otherwise expect binary already in place
if ($DownloadUrl) {
    Write-Host "[Kryoss] Downloading agent from $DownloadUrl..."
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $exePath -UseBasicParsing
}
elseif (!(Test-Path $exePath)) {
    Write-Error "KryossAgent.exe not found at $exePath. Provide -DownloadUrl or copy binary manually."
    exit 1
}

# Enroll first (one-shot) if code provided and not yet enrolled
if ($EnrollCode) {
    Write-Host "[Kryoss] Running initial enrollment..."
    & $exePath --code $EnrollCode --alone
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Initial enrollment returned exit code $LASTEXITCODE"
    }
}

# Install as Windows Service
Write-Host "[Kryoss] Installing Windows Service..."
& $exePath --install

Write-Host "[Kryoss] Installation complete." -ForegroundColor Green
Write-Host "  Service name: KryossAgent"
Write-Host "  Install dir:  $InstallDir"
Write-Host "  Status:       $(Get-Service -Name KryossAgent -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Status)"
