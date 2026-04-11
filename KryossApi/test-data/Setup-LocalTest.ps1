<#
.SYNOPSIS
    Sets up the local development environment for Kryoss Platform testing.

.DESCRIPTION
    1. Creates a LocalDB database (or connects to SQL Server)
    2. Runs all SQL migration scripts in order
    3. Runs seed scripts
    4. Updates local.settings.json with connection string

.NOTES
    Author:   TeamLogic IT
    Version:  1.1
    Created:  2026-04-06

.EXAMPLE
    .\Setup-LocalTest.ps1
    .\Setup-LocalTest.ps1 -SqlInstance "localhost\SQLEXPRESS"
#>
[CmdletBinding()]
param(
    [string]$SqlInstance = "(localdb)\MSSQLLocalDB",
    [string]$DatabaseName = "KryossDb",
    [switch]$UseExistingDb
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sqlDir = Join-Path -Path $projectRoot -ChildPath "sql"
$apiDir = Join-Path -Path $projectRoot -ChildPath "src\KryossApi"

Write-Host ""
Write-Host "  =========================================" -ForegroundColor Green
Write-Host "  Kryoss Platform - Local Test Setup" -ForegroundColor Green
Write-Host "  TeamLogic IT - Your Technology Advisor" -ForegroundColor Green
Write-Host "  =========================================" -ForegroundColor Green
Write-Host ""

# --- Step 0: Check prerequisites ---
Write-Host "  [0/6] Checking prerequisites..." -ForegroundColor Cyan

$hasSqlModule = $null -ne (Get-Module -ListAvailable -Name SqlServer -ErrorAction SilentlyContinue)
$hasSqlCmd = $null -ne (Get-Command -Name sqlcmd.exe -ErrorAction SilentlyContinue)

if (-not $hasSqlModule -and -not $hasSqlCmd) {
    Write-Host "        Installing SqlServer PowerShell module..." -ForegroundColor Yellow
    Install-Module -Name SqlServer -Scope CurrentUser -Force -AllowClobber
    Import-Module -Name SqlServer
    $hasSqlModule = $true
}

if ($hasSqlModule) {
    Import-Module -Name SqlServer -ErrorAction SilentlyContinue
}

$hasFuncTools = $null -ne (Get-Command -Name func -ErrorAction SilentlyContinue)
if (-not $hasFuncTools) {
    Write-Host "        [!] Azure Functions Core Tools not found." -ForegroundColor Yellow
    Write-Host "            Install: winget install Microsoft.Azure.FunctionsCoreTools" -ForegroundColor Yellow
}

Write-Host "        OK" -ForegroundColor Green

# --- Build connection string ---
$connString = "Server=$SqlInstance;Database=$DatabaseName;Trusted_Connection=True;TrustServerCertificate=True;"
Write-Host ""
Write-Host "  Connection: $connString" -ForegroundColor Gray

# --- Step 1: Drop and recreate database ---
if (-not $UseExistingDb) {
    Write-Host ""
    Write-Host "  [1/6] Creating database '$DatabaseName'..." -ForegroundColor Cyan

    $masterConn = "Server=$SqlInstance;Database=master;Trusted_Connection=True;TrustServerCertificate=True;"
    $dropAndCreate = @"
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = '$DatabaseName')
BEGIN
    ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$DatabaseName];
END
CREATE DATABASE [$DatabaseName];
"@

    if ($hasSqlModule) {
        Invoke-Sqlcmd -ConnectionString $masterConn -Query $dropAndCreate -ErrorAction Stop
    }
    else {
        sqlcmd.exe -S $SqlInstance -d master -Q $dropAndCreate -b
    }
    Write-Host "        OK (clean database)" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "  [1/6] Skipping database creation (UseExistingDb)" -ForegroundColor Yellow
}

# --- Step 2: Run migrations ---
Write-Host ""
Write-Host "  [2/6] Running migration scripts..." -ForegroundColor Cyan

$migrationFiles = @(
    "001_foundation.sql",
    "002_core.sql",
    "003_cmdb.sql",
    "004_assessment.sql",
    "005_enrollment_crypto.sql"
)

# These require Azure SQL features (RLS, advanced indexes) - skip on LocalDB
$skipOnLocalDb = @("009_rls.sql", "011_tickets.sql", "012_billing.sql")

$optionalMigrations = @(
    "006_vulnerability.sql",
    "007_dashboard.sql",
    "008_tags_future.sql",
    "009_rls.sql",
    "010_crm.sql",
    "011_tickets.sql",
    "012_billing.sql"
)

foreach ($file in $optionalMigrations) {
    $filePath = Join-Path -Path $sqlDir -ChildPath $file
    if (Test-Path -Path $filePath) {
        $migrationFiles += $file
    }
}

foreach ($file in $migrationFiles) {
    $filePath = Join-Path -Path $sqlDir -ChildPath $file
    if (-not (Test-Path -Path $filePath)) {
        Write-Host "        [!] Missing: $file (skipped)" -ForegroundColor Yellow
        continue
    }
    if ($SqlInstance -match 'localdb' -and $skipOnLocalDb -contains $file) {
        Write-Host "        [~] $file (skipped - requires Azure SQL)" -ForegroundColor DarkYellow
        continue
    }

    Write-Host "        Running $file..." -ForegroundColor Gray
    try {
        if ($hasSqlModule) {
            Invoke-Sqlcmd -ConnectionString $connString -InputFile $filePath -ErrorAction Stop
        }
        else {
            sqlcmd.exe -S $SqlInstance -d $DatabaseName -i $filePath -b
            if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed for $file" }
        }
        Write-Host "        [OK] $file" -ForegroundColor Green
    }
    catch {
        $errMsg = $_.Exception.Message
        if ($errMsg -match "already an object named|already exists") {
            Write-Host "        [~] $file (already applied)" -ForegroundColor DarkYellow
        }
        else {
            Write-Host "        [X] $file - $errMsg" -ForegroundColor Red
        }
    }
}

# --- Step 3: Run seed scripts ---
Write-Host ""
Write-Host "  [3/6] Running seed scripts..." -ForegroundColor Cyan

$seedFiles = @(
    "seed_001_roles_permissions.sql",
    "seed_002_frameworks_platforms.sql",
    "seed_100_test_data.sql"
)

foreach ($file in $seedFiles) {
    $filePath = Join-Path -Path $sqlDir -ChildPath $file
    if (-not (Test-Path -Path $filePath)) {
        Write-Host "        [!] Missing: $file (skipped)" -ForegroundColor Yellow
        continue
    }

    Write-Host "        Running $file..." -ForegroundColor Gray
    try {
        if ($hasSqlModule) {
            Invoke-Sqlcmd -ConnectionString $connString -InputFile $filePath -ErrorAction Stop
        }
        else {
            sqlcmd.exe -S $SqlInstance -d $DatabaseName -i $filePath -b
            if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed for $file" }
        }
        Write-Host "        [OK] $file" -ForegroundColor Green
    }
    catch {
        $errMsg = $_.Exception.Message
        if ($errMsg -match "duplicate|violation|already exists|Cannot insert") {
            Write-Host "        [~] $file (data already exists)" -ForegroundColor DarkYellow
        }
        else {
            Write-Host "        [X] $file - $errMsg" -ForegroundColor Red
        }
    }
}

# --- Step 4: Update local.settings.json ---
Write-Host ""
Write-Host "  [4/6] Updating local.settings.json..." -ForegroundColor Cyan

$settingsPath = Join-Path -Path $apiDir -ChildPath "local.settings.json"
$settingsContent = @"
{
    "IsEncrypted": false,
    "Values": {
        "AzureWebJobsStorage": "UseDevelopmentStorage=true",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
        "SqlConnectionString": "$($connString.Replace('\','\\'))"
    }
}
"@

Set-Content -Path $settingsPath -Value $settingsContent -Encoding UTF8
Write-Host "        Written to: $settingsPath" -ForegroundColor Green

# --- Step 5: Verify database ---
Write-Host ""
Write-Host "  [5/6] Verifying database..." -ForegroundColor Cyan

$verifySql = @"
SELECT 'Tables' AS item, COUNT(*) AS total FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'
UNION ALL SELECT 'Controls', COUNT(*) FROM control_defs WHERE deleted_at IS NULL
UNION ALL SELECT 'Assessment Controls', COUNT(*) FROM assessment_controls
UNION ALL SELECT 'Enrollment Codes', COUNT(*) FROM enrollment_codes WHERE deleted_at IS NULL
UNION ALL SELECT 'Roles', COUNT(*) FROM roles WHERE deleted_at IS NULL
UNION ALL SELECT 'Permissions', COUNT(*) FROM permissions
"@

try {
    if ($hasSqlModule) {
        $results = Invoke-Sqlcmd -ConnectionString $connString -Query $verifySql -ErrorAction Stop
        foreach ($row in $results) {
            Write-Host "        $($row.item): $($row.total)" -ForegroundColor White
        }
    }
    else {
        sqlcmd.exe -S $SqlInstance -d $DatabaseName -Q $verifySql
    }
}
catch {
    Write-Host "        [!] Verification query failed: $($_.Exception.Message)" -ForegroundColor Yellow
}

# --- Step 6: Done ---
Write-Host ""
Write-Host "  [6/6] Setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "  =========================================" -ForegroundColor Cyan
Write-Host "  HOW TO TEST:" -ForegroundColor Cyan
Write-Host "  =========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  1. Start the API:" -ForegroundColor White
Write-Host "     cd KryossApi\src\KryossApi" -ForegroundColor Gray
Write-Host "     func start" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. Run the agent (as Admin, in another terminal):" -ForegroundColor White
Write-Host "     cd KryossAgent\src\KryossAgent" -ForegroundColor Gray
Write-Host "     dotnet run" -ForegroundColor Gray
Write-Host "     URL:  http://localhost:7071" -ForegroundColor Gray
Write-Host "     Code: K7X9-M2P4-Q8R1-T5W3" -ForegroundColor Gray
Write-Host ""
Write-Host "  3. Or test with the PowerShell script:" -ForegroundColor White
Write-Host "     .\Test-AgentFlow.ps1 -BaseUrl http://localhost:7071/v1" -ForegroundColor Gray
Write-Host ""
Write-Host "  Enrollment Code: K7X9-M2P4-Q8R1-T5W3" -ForegroundColor Yellow
Write-Host ""
