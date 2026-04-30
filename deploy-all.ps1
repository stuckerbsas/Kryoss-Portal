<#
.SYNOPSIS
    Deploy Kryoss API + Portal in one shot.

.DESCRIPTION
    1. Builds and publishes KryossApi (.NET 8) to func-kryoss Azure Functions
    2. Commits + pushes KryossPortal to GitHub (triggers Azure SWA auto-deploy)

.PARAMETER SkipApi
    Skip API deployment (only deploy portal)

.PARAMETER SkipPortal
    Skip portal deployment (only deploy API)

.PARAMETER CommitMessage
    Custom commit message for portal push (default: auto-generated with timestamp)

.EXAMPLE
    .\deploy-all.ps1
    .\deploy-all.ps1 -SkipPortal
    .\deploy-all.ps1 -CommitMessage "fix: report i18n"
#>
[CmdletBinding()]
param(
    [switch]$SkipApi,
    [switch]$SkipPortal,
    [string]$CommitMessage
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$apiDir = Join-Path $root 'KryossApi\src\KryossApi'
$portalDir = Join-Path $root 'KryossPortal'

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  KRYOSS DEPLOY" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# ---- API ----
if (-not $SkipApi) {
    Write-Host "[1/2] Deploying API to func-kryoss..." -ForegroundColor Yellow

    Write-Host "  -> dotnet publish..." -ForegroundColor Gray
    $publishDir = Join-Path $apiDir 'publish'
    dotnet publish $apiDir -c Release -o $publishDir --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    Write-Host "  -> func azure functionapp publish..." -ForegroundColor Gray
    Push-Location $publishDir
    try {
        func azure functionapp publish func-kryoss --dotnet-isolated
        if ($LASTEXITCODE -ne 0) { throw "func publish failed" }
    } finally {
        Pop-Location
    }

    Write-Host "  API deployed OK" -ForegroundColor Green
} else {
    Write-Host "[1/2] API deploy SKIPPED" -ForegroundColor DarkGray
}

# ---- PORTAL ----
# NOTE: swa-kryoss-portal has NO GitHub integration (provider=SwaCli).
# Git push does NOT trigger an auto-build. Must deploy via `swa deploy`.
if (-not $SkipPortal) {
    Write-Host "`n[2/2] Deploying Portal (build + SWA CLI deploy)..." -ForegroundColor Yellow

    Push-Location $portalDir
    try {
        # 1. Commit + push (versioning only — does NOT deploy)
        $status = git status --porcelain -- .
        if ($status) {
            Write-Host "  -> Changed files:" -ForegroundColor Gray
            $status | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }

            if (-not $CommitMessage) {
                $CommitMessage = "deploy: $(Get-Date -Format 'yyyy-MM-dd HH:mm') auto-deploy"
            }

            git add -A -- .
            git commit -m $CommitMessage
            if ($LASTEXITCODE -ne 0) { throw "git commit failed" }

            git push origin main
            if ($LASTEXITCODE -ne 0) { throw "git push failed" }
            Write-Host "  Portal changes pushed to GitHub." -ForegroundColor Gray
        } else {
            Write-Host "  No portal changes to commit." -ForegroundColor DarkGray
        }

        # 2. Build production bundle
        Write-Host "  -> npm run build..." -ForegroundColor Gray
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm build failed" }

        # 3. Get SWA deployment token
        Write-Host "  -> Fetching SWA deploy token..." -ForegroundColor Gray
        $token = az staticwebapp secrets list --name swa-kryoss-portal --resource-group rg-kryoss --query "properties.apiKey" -o tsv
        if (-not $token) { throw "Could not fetch SWA deploy token" }

        # 4. Deploy via SWA CLI
        Write-Host "  -> swa deploy..." -ForegroundColor Gray
        swa deploy ./dist --deployment-token $token --env production
        if ($LASTEXITCODE -ne 0) { throw "swa deploy failed" }

        Write-Host "  Portal deployed OK" -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host "`n[2/2] Portal deploy SKIPPED" -ForegroundColor DarkGray
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  DEPLOY COMPLETE" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan
