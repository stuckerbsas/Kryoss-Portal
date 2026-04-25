<#
.SYNOPSIS
    Publishes KryossAgent.exe + version.txt to Azure Blob Storage.

.DESCRIPTION
    Compiles the agent (self-contained, trimmed), uploads the binary and a
    version.txt file to the 'agent' container in stkryoss4031. NinjaOne
    deploy scripts auto-detect the new version on next run.

    Requires: Azure CLI (az) logged in with access to stkryoss4031.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-21

.EXAMPLE
    .\Publish-AgentToBlob.ps1
    .\Publish-AgentToBlob.ps1 -SkipBuild
#>

[CmdletBinding()]
param(
    [switch]$SkipBuild,

    [string]$StorageAccount = "stkryossagent",
    [string]$Container = "kryoss-agent-templates"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path "$PSScriptRoot\..\..\KryossAgent").Path
$projectPath = Join-Path $repoRoot "src\KryossAgent\KryossAgent.csproj"
$publishDir = Join-Path $repoRoot "publish"
$exePath = Join-Path $publishDir "KryossAgent.exe"

# ── Build ──
if (-not $SkipBuild) {
    Write-Host "Building agent (self-contained, trimmed)..." -ForegroundColor Cyan
    dotnet publish $projectPath `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=true `
        -p:TrimMode=partial `
        -p:PublishAot=false `
        -p:InvariantGlobalization=true `
        -p:DebuggerSupport=false `
        -p:EnableCompressionInSingleFile=true `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed." -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Path $exePath)) {
    Write-Host "KryossAgent.exe not found at $exePath" -ForegroundColor Red
    exit 1
}

# ── Extract version ──
$version = (Get-Item $exePath).VersionInfo.ProductVersion
if (-not $version) {
    Write-Host "Cannot read version from binary." -ForegroundColor Red
    exit 1
}

$sizeMb = [Math]::Round((Get-Item $exePath).Length / 1MB, 1)
Write-Host "Agent: v$version ($sizeMb MB)" -ForegroundColor Green

# ── Write version.txt ──
$versionFile = Join-Path $publishDir "version.txt"
$version | Out-File -FilePath $versionFile -Encoding ascii -NoNewline

# ── Upload to blob ──
Write-Host "Uploading to $StorageAccount/$Container..." -ForegroundColor Cyan

az storage blob upload `
    --account-name $StorageAccount `
    --container-name $Container `
    --name "latest/KryossAgent.exe" `
    --file $exePath `
    --overwrite `
    --no-progress

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to upload KryossAgent.exe" -ForegroundColor Red
    exit 1
}

az storage blob upload `
    --account-name $StorageAccount `
    --container-name $Container `
    --name "latest/version.txt" `
    --file $versionFile `
    --overwrite `
    --no-progress

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to upload version.txt" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Published v$version to blob." -ForegroundColor Green
Write-Host "  Blob URL: https://$StorageAccount.blob.core.windows.net/$Container/latest/KryossAgent.exe"
Write-Host "  Version:  https://$StorageAccount.blob.core.windows.net/$Container/latest/version.txt"
Write-Host ""
Write-Host "NinjaOne devices will auto-update on next hourly run." -ForegroundColor Yellow
