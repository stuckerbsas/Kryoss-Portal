<#
.SYNOPSIS
    Publishes KryossAgent as a standalone single-file .exe.

.DESCRIPTION
    Produces a self-contained Windows x64 executable that runs on any
    Windows 10/11 or Server 2019/2022 machine without .NET installed.
    Output: KryossAgent/publish/KryossAgent.exe

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-07

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectDir "src\KryossAgent\KryossAgent.csproj"
$outputDir = Join-Path $projectDir "publish"

Write-Host ""
Write-Host "  Publishing KryossAgent..." -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration"
Write-Host "  Output: $outputDir"
Write-Host ""

# Clean previous output


# Publish as self-contained single-file (no AOT to avoid C++ build tools requirement)
dotnet publish $projectFile `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishAot=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "  PUBLISH FAILED" -ForegroundColor Red
    exit 1
}

$exePath = Join-Path $outputDir "KryossAgent.exe"
if (Test-Path $exePath) {
    $size = (Get-Item $exePath).Length / 1MB
    Write-Host ""
    Write-Host "  Published successfully!" -ForegroundColor Green
    Write-Host "  File: $exePath"
    Write-Host "  Size: $([math]::Round($size, 1)) MB"
    Write-Host ""
    Write-Host "  Usage:" -ForegroundColor Yellow
    Write-Host "    Interactive:  KryossAgent.exe"
    Write-Host "    Silent:       KryossAgent.exe --silent --code XXXX-XXXX-XXXX-XXXX --api-url https://your-api.azurewebsites.net"
    Write-Host ""
}
else {
    Write-Host "  ERROR: KryossAgent.exe not found in output" -ForegroundColor Red
    exit 1
}
