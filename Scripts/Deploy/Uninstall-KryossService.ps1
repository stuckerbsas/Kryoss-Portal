# Uninstall-KryossService.ps1
# Stops and removes the KryossAgent Windows Service
# Run as Administrator

#Requires -RunAsAdministrator
param(
    [string]$InstallDir = "$env:ProgramFiles\Kryoss",
    [switch]$RemoveFiles
)

$ErrorActionPreference = 'Stop'

$exePath = Join-Path $InstallDir "KryossAgent.exe"

if (Test-Path $exePath) {
    Write-Host "[Kryoss] Uninstalling Windows Service..." -ForegroundColor Yellow
    & $exePath --uninstall
}
else {
    # Fallback: stop and delete via sc.exe if binary is gone
    $svc = Get-Service -Name KryossAgent -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -eq 'Running') { Stop-Service -Name KryossAgent -Force }
        sc.exe delete KryossAgent | Out-Null
        Write-Host "[Kryoss] Service removed via sc.exe (binary not found)."
    }
    else {
        Write-Host "[Kryoss] Service not found. Nothing to uninstall."
        exit 0
    }
}

if ($RemoveFiles -and (Test-Path $InstallDir)) {
    Write-Host "[Kryoss] Removing install directory: $InstallDir"
    Remove-Item -Path $InstallDir -Recurse -Force
}

# Clean registry
Remove-Item -Path 'HKLM:\SOFTWARE\Kryoss\Agent' -Recurse -ErrorAction SilentlyContinue

Write-Host "[Kryoss] Uninstall complete." -ForegroundColor Green
