<#
.SYNOPSIS
    Provisions the Kryoss Platform infrastructure in Azure from scratch.

.DESCRIPTION
    Creates: Resource Group, Azure SQL Server + DB, Azure Functions App,
    Storage Account, Application Insights. Then runs migrations and seeds.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-06

.PARAMETER Location
    Azure region. Default: eastus

.PARAMETER ResourceGroup
    Resource group name. Default: rg-kryoss

.PARAMETER SqlAdminPassword
    SQL Server admin password (prompted if not provided)

.PARAMETER SkipInfra
    Skip resource creation (only deploy code + run migrations)

.EXAMPLE
    .\Setup-Azure.ps1
    .\Setup-Azure.ps1 -Location "eastus2" -ResourceGroup "rg-kryoss-dev"
#>
[CmdletBinding()]
param(
    [string]$Location = "eastus",
    [string]$ResourceGroup = "rg-kryoss",
    [string]$SqlAdminUser = "kryossadmin",
    [SecureString]$SqlAdminPassword,
    [switch]$SkipInfra
)

$ErrorActionPreference = 'Stop'

# ── Naming convention ──
$suffix         = "kryoss"
$sqlServerName  = "sql-$suffix"
$sqlDbName      = "KryossDb"
$storageAccount = "st${suffix}$(Get-Random -Minimum 1000 -Maximum 9999)"
$funcAppName    = "func-$suffix"
$appInsights    = "ai-$suffix"
$planName       = "plan-$suffix"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sqlDir      = Join-Path $projectRoot "sql"
$apiDir      = Join-Path $projectRoot "src\KryossApi"

Write-Host ""
Write-Host "  ====================================================" -ForegroundColor Green
Write-Host "    Kryoss Platform - Azure Deployment" -ForegroundColor Green
Write-Host "    TeamLogic IT - Your Technology Advisor" -ForegroundColor Green
Write-Host "  ====================================================" -ForegroundColor Green
Write-Host ""

# ─── Step 0: Check Azure CLI login ────────────────────────────────
Write-Host "  [0/8] Checking Azure CLI..." -ForegroundColor Cyan

$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "        Not logged in. Running az login..." -ForegroundColor Yellow
    az login
    $account = az account show | ConvertFrom-Json
}
Write-Host "        Subscription: $($account.name) ($($account.id))" -ForegroundColor White

# ─── Prompt for SQL password if needed ─────────────────────────────
if (-not $SqlAdminPassword) {
    $SqlAdminPassword = Read-Host -Prompt "  Enter SQL admin password (min 8 chars, upper+lower+number+special)" -AsSecureString
}
# Extract password via NetworkCredential for single-use (no persistent plaintext variable)
$sqlCred = New-Object System.Net.NetworkCredential("", $SqlAdminPassword)

if (-not $SkipInfra) {

# ─── Step 1: Resource Group ───────────────────────────────────────
Write-Host ""
Write-Host "  [1/8] Creating Resource Group: $ResourceGroup..." -ForegroundColor Cyan

az group create --name $ResourceGroup --location $Location --output none
Write-Host "        Done." -ForegroundColor Green

# ─── Step 2: Azure SQL Server + Database ──────────────────────────
Write-Host ""
Write-Host "  [2/8] Creating Azure SQL Server: $sqlServerName..." -ForegroundColor Cyan

az sql server create `
    --name $sqlServerName `
    --resource-group $ResourceGroup `
    --location $Location `
    --admin-user $SqlAdminUser `
    --admin-password $($sqlCred.Password) `
    --output none

# Allow Azure services to access
az sql server firewall-rule create `
    --resource-group $ResourceGroup `
    --server $sqlServerName `
    --name "AllowAzureServices" `
    --start-ip-address 0.0.0.0 `
    --end-ip-address 0.0.0.0 `
    --output none

# Allow your current public IP
$myIp = (Invoke-RestMethod -Uri "https://api.ipify.org" -TimeoutSec 5)
Write-Host "        Adding firewall rule for your IP: $myIp" -ForegroundColor Gray
az sql server firewall-rule create `
    --resource-group $ResourceGroup `
    --server $sqlServerName `
    --name "DevMachine" `
    --start-ip-address $myIp `
    --end-ip-address $myIp `
    --output none

Write-Host "        Creating database: $sqlDbName (Basic tier, 2GB)..." -ForegroundColor Gray
az sql db create `
    --resource-group $ResourceGroup `
    --server $sqlServerName `
    --name $sqlDbName `
    --edition "Basic" `
    --capacity 5 `
    --max-size "2GB" `
    --output none

Write-Host "        Done." -ForegroundColor Green

# ─── Step 3: Storage Account ─────────────────────────────────────
Write-Host ""
Write-Host "  [3/8] Creating Storage Account: $storageAccount..." -ForegroundColor Cyan

az storage account create `
    --name $storageAccount `
    --resource-group $ResourceGroup `
    --location $Location `
    --sku Standard_LRS `
    --output none

Write-Host "        Done." -ForegroundColor Green

# ─── Step 4: Application Insights ─────────────────────────────────
Write-Host ""
Write-Host "  [4/8] Creating Application Insights: $appInsights..." -ForegroundColor Cyan

az monitor app-insights component create `
    --app $appInsights `
    --location $Location `
    --resource-group $ResourceGroup `
    --kind web `
    --output none

$aiKey = az monitor app-insights component show `
    --app $appInsights `
    --resource-group $ResourceGroup `
    --query "connectionString" -o tsv

Write-Host "        Done." -ForegroundColor Green

# ─── Step 5: Azure Functions App ──────────────────────────────────
Write-Host ""
Write-Host "  [5/8] Creating Azure Functions App: $funcAppName..." -ForegroundColor Cyan

# Create Consumption plan (or use existing)
az functionapp create `
    --name $funcAppName `
    --resource-group $ResourceGroup `
    --storage-account $storageAccount `
    --consumption-plan-location $Location `
    --runtime dotnet-isolated `
    --runtime-version 8 `
    --functions-version 4 `
    --os-type Windows `
    --output none

Write-Host "        Done." -ForegroundColor Green

} # end SkipInfra

# ─── Step 6: Build connection string and configure app settings ───
Write-Host ""
Write-Host "  [6/8] Configuring app settings..." -ForegroundColor Cyan

$azureSqlConn = "Server=tcp:${sqlServerName}.database.windows.net,1433;Database=$sqlDbName;User ID=$SqlAdminUser;Password=$($sqlCred.Password);Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

az functionapp config appsettings set `
    --name $funcAppName `
    --resource-group $ResourceGroup `
    --settings "SqlConnectionString=$azureSqlConn" `
    --output none

if ($aiKey) {
    az functionapp config appsettings set `
        --name $funcAppName `
        --resource-group $ResourceGroup `
        --settings "APPLICATIONINSIGHTS_CONNECTION_STRING=$aiKey" `
        --output none
}

Write-Host "        Done." -ForegroundColor Green

# ─── Step 7: Run SQL migrations on Azure SQL ─────────────────────
Write-Host ""
Write-Host "  [7/8] Running SQL migrations on Azure SQL..." -ForegroundColor Cyan

$hasSqlModule = $null -ne (Get-Module -ListAvailable SqlServer -ErrorAction SilentlyContinue)
if (-not $hasSqlModule) {
    Write-Host "        Installing SqlServer module..." -ForegroundColor Yellow
    Install-Module SqlServer -Scope CurrentUser -Force -AllowClobber
}
Import-Module SqlServer

$allSqlFiles = @(
    "001_foundation.sql",
    "002_core.sql",
    "003_cmdb.sql",
    "004_assessment.sql",
    "005_enrollment_crypto.sql",
    "006_vulnerability.sql",
    "007_dashboard.sql",
    "008_tags_future.sql",
    "009_rls.sql",
    "010_crm.sql",
    "011_tickets.sql",
    "012_billing.sql",
    "013_bulk_enrollment.sql",
    "seed_001_roles_permissions.sql",
    "seed_002_frameworks_platforms.sql",
    "seed_100_test_data.sql"
)

foreach ($file in $allSqlFiles) {
    $filePath = Join-Path $sqlDir $file
    if (-not (Test-Path $filePath)) {
        continue
    }
    Write-Host "        Running $file..." -ForegroundColor Gray
    try {
        Invoke-Sqlcmd -ConnectionString $azureSqlConn -InputFile $filePath -ErrorAction Stop
        Write-Host "        [OK] $file" -ForegroundColor Green
    }
    catch {
        $err = $_.Exception.Message
        if ($err -match "already an object named|already exists|duplicate|Cannot insert") {
            Write-Host "        [SKIP] $file (already applied)" -ForegroundColor DarkYellow
        } else {
            Write-Host "        [FAIL] $file - $err" -ForegroundColor Red
        }
    }
}

# Clean up SQL credential
$sqlCred = $null

# ─── Step 8: Deploy the Functions app ─────────────────────────────
Write-Host ""
Write-Host "  [8/8] Publishing Azure Functions app..." -ForegroundColor Cyan

Push-Location $apiDir
try {
    dotnet publish -c Release -o ./publish --nologo -v q
    Push-Location ./publish
    Compress-Archive -Path * -DestinationPath ../deploy.zip -Force
    Pop-Location

    az functionapp deployment source config-zip `
        --resource-group $ResourceGroup `
        --name $funcAppName `
        --src (Join-Path $apiDir "deploy.zip") `
        --output none

    Remove-Item -Path (Join-Path $apiDir "deploy.zip") -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $apiDir "publish") -Recurse -Force -ErrorAction SilentlyContinue
}
finally {
    Pop-Location
}

Write-Host "        Done." -ForegroundColor Green

# ─── Summary ──────────────────────────────────────────────────────
$funcUrl = "https://${funcAppName}.azurewebsites.net"

Write-Host ""
Write-Host "  ====================================================" -ForegroundColor Green
Write-Host "    DEPLOYMENT COMPLETE" -ForegroundColor Green
Write-Host "  ====================================================" -ForegroundColor Green
Write-Host ""
Write-Host "    API URL:        $funcUrl" -ForegroundColor White
Write-Host "    SQL Server:     ${sqlServerName}.database.windows.net" -ForegroundColor White
Write-Host "    Database:       $sqlDbName" -ForegroundColor White
Write-Host "    Resource Group: $ResourceGroup" -ForegroundColor White
Write-Host ""
Write-Host "    Endpoints:" -ForegroundColor Green
Write-Host "      POST $funcUrl/v1/enroll" -ForegroundColor White
Write-Host "      GET  $funcUrl/v1/controls?assessmentId=1" -ForegroundColor White
Write-Host "      POST $funcUrl/v1/results" -ForegroundColor White
Write-Host ""
Write-Host "    Enrollment Code: <ENROLLMENT_CODE>" -ForegroundColor Yellow
Write-Host "  ====================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Test with:" -ForegroundColor Cyan
Write-Host "    .\Test-AgentFlow.ps1 -BaseUrl $funcUrl/v1" -ForegroundColor White
Write-Host ""
Write-Host "  Or run the agent on any Windows machine:" -ForegroundColor Cyan
Write-Host "    KryossAgent.exe" -ForegroundColor White
Write-Host "    > URL:  $funcUrl" -ForegroundColor White
Write-Host "    > Code: <ENROLLMENT_CODE>" -ForegroundColor White
Write-Host ""

# ─── Save config for future reference ─────────────────────────────
$deployConfig = @{
    Timestamp       = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    ResourceGroup   = $ResourceGroup
    Location        = $Location
    SqlServer       = "${sqlServerName}.database.windows.net"
    SqlDatabase     = $sqlDbName
    SqlAdminUser    = $SqlAdminUser
    FunctionApp     = $funcAppName
    FunctionUrl     = $funcUrl
    StorageAccount  = $storageAccount
    AppInsights     = $appInsights
    EnrollmentCode  = "<ENROLLMENT_CODE>"
}

$configPath = Join-Path (Split-Path $MyInvocation.MyCommand.Path) "deploy-config.json"
$deployConfig | ConvertTo-Json | Set-Content $configPath
Write-Host "  Config saved to: $configPath" -ForegroundColor Gray
Write-Host ""
