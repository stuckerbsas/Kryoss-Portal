# Client Network Assessment — Deployment Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make KryossAgent deployable to a client's domain network — publish as standalone .exe, add CLI params for silent remote deployment, support bulk enrollment codes, and create a PowerShell orchestrator that discovers machines and runs the agent on each.

**Architecture:** The agent already works end-to-end (enroll → controls → scan → results). We need to: (1) add `--code` and `--api-url` CLI arguments for unattended enrollment, (2) add `max_uses` column to enrollment_codes so one code can enroll N machines, (3) publish as a single-file .exe with embedded runtime, (4) create `Invoke-KryossDeployment.ps1` that discovers domain machines and deploys+runs the agent via PsExec or PSRemoting.

**Tech Stack:** .NET 8 (win-x64 self-contained publish), PowerShell 5.1, SQL Server, Azure Functions

---

## File Map

| Action | File | Responsibility |
|--------|------|---------------|
| Modify | `KryossAgent/src/KryossAgent/Program.cs` | Add `--code`, `--api-url`, `--silent` CLI argument parsing |
| Modify | `KryossApi/sql/005_enrollment_crypto.sql` | Add `max_uses` and `use_count` columns |
| Create | `KryossApi/sql/013_bulk_enrollment.sql` | Migration to add bulk enrollment support |
| Modify | `KryossApi/src/KryossApi/Data/Entities/Enrollment.cs` | Add MaxUses, UseCount properties |
| Modify | `KryossApi/src/KryossApi/Data/KryossDbContext.cs` | Map new columns |
| Modify | `KryossApi/src/KryossApi/Services/EnrollmentService.cs` | Change single-use to multi-use logic |
| Create | `Scripts/NetworkDiscovery/Invoke-KryossDeployment.ps1` | Main orchestrator: discover + deploy + scan |
| Create | `KryossAgent/publish.ps1` | Build script to publish standalone .exe |
| Create | `KryossApi/sql/seed_client.sql` | Template: create client org + enrollment codes |

---

### Task 1: CLI Arguments for Silent Enrollment

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Program.cs`

Currently the agent prompts interactively for enrollment code and API URL. For remote deployment via PsExec, it needs to accept these as command-line arguments.

- [ ] **Step 1: Add CLI argument parsing to Program.cs**

Replace the enrollment section (lines 28-95) to check for `--code` and `--api-url` args before falling back to interactive prompts:

```csharp
// ── Load or create config ──
var config = AgentConfig.Load();

// ── CLI overrides ──
var cliCode = GetArg(args, "--code");
var cliApiUrl = GetArg(args, "--api-url");
if (!string.IsNullOrEmpty(cliApiUrl)) config.ApiUrl = cliApiUrl;

// ── Enrollment (first run) ──
if (!config.IsEnrolled)
{
    string? code = cliCode;

    if (string.IsNullOrEmpty(code))
    {
        if (silent)
        {
            Console.Error.WriteLine("[ERROR] Agent not enrolled. Provide --code for silent enrollment.");
            Environment.Exit(1);
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  No configuration found. Entering enrollment mode.");
        Console.ResetColor();
        Console.WriteLine();

        Console.Write("  Enter enrollment code: ");
        code = Console.ReadLine()?.Trim();
    }

    if (string.IsNullOrEmpty(code))
    {
        Console.Error.WriteLine("[ERROR] Enrollment code is required.");
        Environment.Exit(1);
        return;
    }

    // Only prompt for API URL interactively if not provided via CLI
    if (string.IsNullOrEmpty(cliApiUrl) && !silent)
    {
        Console.Write($"  Enter API URL [{config.ApiUrl}]: ");
        var apiUrl = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(apiUrl)) config.ApiUrl = apiUrl;
    }

    var hostname = Environment.MachineName;
    var platform = PlatformDetector.DetectPlatform();
    var hardware = PlatformDetector.DetectHardware();

    if (!silent) Console.WriteLine($"  Enrolling {hostname}...");

    try
    {
        using var enrollClient = new ApiClient(config);
        var enrollment = await enrollClient.EnrollAsync(code, hostname, platform, hardware);
        if (enrollment is null)
        {
            Console.Error.WriteLine("[ERROR] Enrollment returned null.");
            Environment.Exit(1);
            return;
        }

        config.AgentId = enrollment.AgentId;
        config.ApiKey = enrollment.ApiKey;
        config.ApiSecret = enrollment.ApiSecret;
        config.PublicKeyPem = enrollment.PublicKey;
        config.AssessmentId = enrollment.AssessmentId;
        config.AssessmentName = enrollment.AssessmentName;
        config.Save();

        if (!silent)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Enrolled successfully as {hostname}");
            Console.WriteLine($"  Assessment: {enrollment.AssessmentName ?? "Default"} ({enrollment.AssessmentId})");
            Console.ResetColor();
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine($"Kryoss: Enrolled {hostname} — Assessment: {enrollment.AssessmentName} ({enrollment.AssessmentId})");
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] Enrollment failed: {ex.Message}");
        Console.ResetColor();
        Environment.Exit(1);
        return;
    }
}
```

Add the helper function at the bottom of Program.cs (after the `UploadPendingResults` method):

```csharp
static string? GetArg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }
    return null;
}
```

- [ ] **Step 2: Test interactive enrollment still works**

```powershell
# Delete existing registry config to force re-enrollment
Remove-Item "HKLM:\SOFTWARE\Kryoss\Agent" -Recurse -ErrorAction SilentlyContinue
# Reset DB enrollment code
sqlcmd -S "(localdb)\MSSQLLocalDB" -d KryossDb -I -Q "DELETE FROM control_results; DELETE FROM assessment_runs; UPDATE enrollment_codes SET used_by=NULL, used_at=NULL WHERE code='K7X9-M2P4-Q8R1-T5W3'; DELETE FROM machines;"
# Run interactively
cd "C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\KryossAgent\src\KryossAgent"
dotnet run
```

Expected: Same interactive prompts as before, enrollment works.

- [ ] **Step 3: Test silent enrollment with CLI args**

```powershell
Remove-Item "HKLM:\SOFTWARE\Kryoss\Agent" -Recurse -ErrorAction SilentlyContinue
sqlcmd -S "(localdb)\MSSQLLocalDB" -d KryossDb -I -Q "DELETE FROM control_results; DELETE FROM assessment_runs; UPDATE enrollment_codes SET used_by=NULL, used_at=NULL WHERE code='K7X9-M2P4-Q8R1-T5W3'; DELETE FROM machines;"
dotnet run -- --silent --code K7X9-M2P4-Q8R1-T5W3 --api-url http://localhost:7071
```

Expected: No prompts, enrolls silently, runs assessment, prints one-line result.

- [ ] **Step 4: Commit**

```bash
git add KryossAgent/src/KryossAgent/Program.cs
git commit -m "feat(agent): add --code, --api-url CLI args for silent deployment"
```

---

### Task 2: Bulk Enrollment Codes (Multi-Use)

**Files:**
- Create: `KryossApi/sql/013_bulk_enrollment.sql`
- Modify: `KryossApi/src/KryossApi/Data/Entities/Enrollment.cs`
- Modify: `KryossApi/src/KryossApi/Data/KryossDbContext.cs`
- Modify: `KryossApi/src/KryossApi/Services/EnrollmentService.cs`

Currently each enrollment code is single-use (used_by = machine_id marks it consumed). For deploying to 20+ machines, we need one code that works N times.

- [ ] **Step 1: Create SQL migration**

Create `KryossApi/sql/013_bulk_enrollment.sql`:

```sql
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- 013_bulk_enrollment.sql
-- Add multi-use enrollment codes: max_uses + use_count
-- A code with max_uses=50 can enroll up to 50 machines.
-- max_uses=NULL means single-use (legacy behavior).
-- =============================================

ALTER TABLE enrollment_codes ADD max_uses INT NULL;
ALTER TABLE enrollment_codes ADD use_count INT NOT NULL DEFAULT 0;
GO

-- For bulk codes, used_by becomes NULL (each machine links via enrollment_machine)
-- But we keep the original column for backwards compat with single-use codes.
```

- [ ] **Step 2: Run the migration on LocalDB**

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d KryossDb -I -i "C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\KryossApi\sql\013_bulk_enrollment.sql"
```

Expected: `Commands completed successfully.`

- [ ] **Step 3: Update EnrollmentCode entity**

In `KryossApi/src/KryossApi/Data/Entities/Enrollment.cs`, add to `EnrollmentCode`:

```csharp
public int? MaxUses { get; set; }       // NULL = single-use, N = can enroll N machines
public int UseCount { get; set; }        // how many machines have used this code
```

- [ ] **Step 4: Update EnrollmentService.RedeemCodeAsync**

Change the code lookup to support both single-use and multi-use:

```csharp
public async Task<EnrollmentResult?> RedeemCodeAsync(string code, string hostname, string? os, string? osVersion, string? osBuild)
{
    var enrollment = await _db.EnrollmentCodes
        .Include(x => x.Organization)
        .Include(x => x.Assessment)
        .FirstOrDefaultAsync(x => x.Code == code
            && x.ExpiresAt > DateTime.UtcNow
            && (
                // Single-use: not yet consumed
                (x.MaxUses == null && x.UsedBy == null)
                // Multi-use: under the limit
                || (x.MaxUses != null && x.UseCount < x.MaxUses)
            ));

    if (enrollment is null)
        return null;

    // Create machine
    var agentId = Guid.NewGuid();
    var machine = new Machine
    {
        OrganizationId = enrollment.OrganizationId,
        AgentId = agentId,
        Hostname = hostname,
        OsName = os,
        OsVersion = osVersion,
        OsBuild = osBuild,
        FirstSeenAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
        IsActive = true
    };
    _db.Machines.Add(machine);

    // Mark enrollment code usage
    enrollment.UseCount++;
    if (enrollment.MaxUses is null)
    {
        // Single-use: mark fully consumed
        enrollment.UsedBy = machine.Id;
        enrollment.UsedAt = DateTime.UtcNow;
    }

    // Get org's API key (or generate one if missing)
    var org = enrollment.Organization;
    if (string.IsNullOrEmpty(org.ApiKey))
    {
        org.ApiKey = GenerateRandomCode(64).Replace("-", "");
        org.ApiSecret = GenerateRandomCode(128).Replace("-", "");
    }

    // Get active crypto key for the org
    var cryptoKey = await _db.OrgCryptoKeys
        .FirstOrDefaultAsync(x => x.OrganizationId == enrollment.OrganizationId && x.IsActive);

    await _db.SaveChangesAsync();

    return new EnrollmentResult(
        AgentId: agentId,
        ApiKey: org.ApiKey,
        ApiSecret: org.ApiSecret!,
        PublicKeyPem: cryptoKey?.PublicKeyPem ?? "",
        AssessmentId: enrollment.AssessmentId,
        AssessmentName: enrollment.Assessment?.Name
    );
}
```

- [ ] **Step 5: Build and verify**

```powershell
cd "C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\KryossApi\src\KryossApi"
dotnet build --no-restore
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add KryossApi/sql/013_bulk_enrollment.sql KryossApi/src/KryossApi/Data/Entities/Enrollment.cs KryossApi/src/KryossApi/Services/EnrollmentService.cs
git commit -m "feat(api): add bulk enrollment codes with max_uses support"
```

---

### Task 3: Client Seed SQL Template

**Files:**
- Create: `KryossApi/sql/seed_client.sql`

Reusable template to onboard a new client: creates the organization and a bulk enrollment code.

- [ ] **Step 1: Create seed_client.sql**

```sql
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- =============================================
-- seed_client.sql
-- Create a new client organization + bulk enrollment code
-- USAGE: Update the variables below, then run against KryossDb
-- =============================================

-- ┌─────────────────────────────────────────┐
-- │  EDIT THESE VALUES FOR EACH CLIENT      │
-- └─────────────────────────────────────────┘
DECLARE @clientName    NVARCHAR(255) = N'ACME Corp';
DECLARE @clientLegal   NVARCHAR(255) = N'ACME Corporation S.A.';
DECLARE @maxMachines   INT           = 50;        -- enrollment code allows up to N machines
DECLARE @expiryDays    INT           = 30;        -- code expires in N days

-- ┌─────────────────────────────────────────┐
-- │  DO NOT EDIT BELOW THIS LINE            │
-- └─────────────────────────────────────────┘
DECLARE @systemUserId  UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @franchiseId   UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @orgId         UNIQUEIDENTIFIER = NEWID();
DECLARE @assessmentId  INT;

-- Create org
INSERT INTO organizations (id, franchise_id, name, legal_name, status, created_by)
VALUES (@orgId, @franchiseId, @clientName, @clientLegal, 'current', @systemUserId);

-- Find default assessment (CIS Level 1)
SELECT @assessmentId = id FROM assessments
WHERE organization_id = (SELECT TOP 1 organization_id FROM assessments WHERE is_default = 1)
  AND is_default = 1 AND deleted_at IS NULL;

-- If no default, use the first active one
IF @assessmentId IS NULL
    SELECT TOP 1 @assessmentId = id FROM assessments WHERE is_active = 1 AND deleted_at IS NULL;

-- Generate enrollment code (4 groups of 4 chars)
DECLARE @code VARCHAR(19) = UPPER(
    SUBSTRING(CONVERT(VARCHAR(36), NEWID()), 1, 4) + '-' +
    SUBSTRING(CONVERT(VARCHAR(36), NEWID()), 1, 4) + '-' +
    SUBSTRING(CONVERT(VARCHAR(36), NEWID()), 1, 4) + '-' +
    SUBSTRING(CONVERT(VARCHAR(36), NEWID()), 1, 4)
);

INSERT INTO enrollment_codes (organization_id, code, assessment_id, label, max_uses, expires_at, created_by)
VALUES (@orgId, @code, @assessmentId, @clientName + N' — Network Assessment',
        @maxMachines, DATEADD(DAY, @expiryDays, SYSUTCDATETIME()), @systemUserId);

-- Output
SELECT 'CLIENT CREATED' AS status;
SELECT @orgId AS organization_id, @clientName AS name;
SELECT @code AS enrollment_code, @maxMachines AS max_machines,
       DATEADD(DAY, @expiryDays, SYSUTCDATETIME()) AS expires_at;

PRINT '';
PRINT '══════════════════════════════════════════════';
PRINT '  Client:         ' + CAST(@clientName AS VARCHAR(100));
PRINT '  Org ID:         ' + CAST(@orgId AS VARCHAR(36));
PRINT '  Enrollment Code:' + @code;
PRINT '  Max Machines:   ' + CAST(@maxMachines AS VARCHAR(10));
PRINT '  Expires:        ' + CAST(@expiryDays AS VARCHAR(10)) + ' days';
PRINT '══════════════════════════════════════════════';
```

- [ ] **Step 2: Commit**

```bash
git add KryossApi/sql/seed_client.sql
git commit -m "feat(sql): add client onboarding seed template"
```

---

### Task 4: Publish Agent as Standalone .exe

**Files:**
- Create: `KryossAgent/publish.ps1`

Build the agent as a single .exe with embedded .NET runtime. No .NET installation needed on target machines.

- [ ] **Step 1: Create publish.ps1**

```powershell
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
if (Test-Path $outputDir) {
    Remove-Item -Path $outputDir -Recurse -Force
}

# Publish as self-contained single-file (no AOT — AOT requires C++ build tools)
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
```

- [ ] **Step 2: Run publish and verify**

```powershell
cd "C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\KryossAgent"
.\publish.ps1
```

Expected: `KryossAgent.exe` in `publish/` folder, ~20-40 MB single file.

- [ ] **Step 3: Test the published .exe**

```powershell
# Reset DB
sqlcmd -S "(localdb)\MSSQLLocalDB" -d KryossDb -I -Q "DELETE FROM control_results; DELETE FROM assessment_runs; UPDATE enrollment_codes SET used_by=NULL, used_at=NULL, use_count=0 WHERE code='K7X9-M2P4-Q8R1-T5W3'; DELETE FROM machines;"
Remove-Item "HKLM:\SOFTWARE\Kryoss\Agent" -Recurse -ErrorAction SilentlyContinue

# Run the .exe
& "C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\KryossAgent\publish\KryossAgent.exe" --silent --code K7X9-M2P4-Q8R1-T5W3 --api-url http://localhost:7071
```

Expected: One-line output with grade and score. Exit code 0.

- [ ] **Step 4: Commit**

```bash
git add KryossAgent/publish.ps1
git commit -m "feat(agent): add publish.ps1 for standalone .exe build"
```

---

### Task 5: Network Deployment Orchestrator

**Files:**
- Create: `Scripts/NetworkDiscovery/Invoke-KryossDeployment.ps1`

PowerShell script that: discovers domain machines → copies KryossAgent.exe → runs it remotely on each.

- [ ] **Step 1: Create Invoke-KryossDeployment.ps1**

```powershell
<#
.SYNOPSIS
    Deploys and runs KryossAgent on all discovered Windows machines in a domain network.

.DESCRIPTION
    Three-phase deployment:
    1. DISCOVER: Find Windows machines via AD query or network scan
    2. DEPLOY:   Copy KryossAgent.exe to each machine via admin share (\\host\C$)
    3. SCAN:     Run the agent remotely via PsExec with enrollment code

    Requires: Domain admin credentials, PsExec64.exe, KryossAgent.exe

.PARAMETER ApiUrl
    Kryoss API endpoint URL. Example: https://kryoss-api.azurewebsites.net

.PARAMETER EnrollmentCode
    Bulk enrollment code (supports multi-use).

.PARAMETER AgentPath
    Path to published KryossAgent.exe.

.PARAMETER TargetHosts
    Optional: specific hostnames or IPs to scan (skips discovery).

.PARAMETER DiscoveryMethod
    How to find machines: AD (Active Directory query) or Network (ping sweep).
    Default: AD

.PARAMETER Credential
    PSCredential for remote access. Prompted if not provided.

.PARAMETER MaxConcurrent
    Maximum parallel deployments. Default: 5.

.PARAMETER OutputDir
    Directory for deployment report. Default: current directory.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-07

.EXAMPLE
    .\Invoke-KryossDeployment.ps1 -ApiUrl "https://kryoss-api.azurewebsites.net" -EnrollmentCode "XXXX-XXXX-XXXX-XXXX"
    .\Invoke-KryossDeployment.ps1 -ApiUrl "https://my-api.com" -EnrollmentCode "CODE" -TargetHosts "PC01","PC02","PC03"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ApiUrl,

    [Parameter(Mandatory)]
    [string]$EnrollmentCode,

    [string]$AgentPath,

    [string[]]$TargetHosts,

    [ValidateSet('AD', 'Network')]
    [string]$DiscoveryMethod = 'AD',

    [PSCredential]$Credential,

    [int]$MaxConcurrent = 5,

    [string]$OutputDir = "."
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ── Constants ──────────────────────────────────────────────────────
$REMOTE_DIR = 'C$\ProgramData\Kryoss'
$REMOTE_EXE = 'C:\ProgramData\Kryoss\KryossAgent.exe'
$PSEXEC_URL = 'https://live.sysinternals.com/PsExec64.exe'
$LOG_DIR    = 'C:\ProgramData\TeamLogicIT\Logs'

# ── Banner ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ╔══════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "  ║    Kryoss Network Assessment Deployer    ║" -ForegroundColor Green
Write-Host "  ║           TeamLogic IT v1.0              ║" -ForegroundColor Green
Write-Host "  ╚══════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

# ── Validate prerequisites ─────────────────────────────────────────
# Find KryossAgent.exe
if (-not $AgentPath) {
    $defaultPath = Join-Path (Split-Path $scriptDir -Parent) "..\KryossAgent\publish\KryossAgent.exe"
    $resolved = Resolve-Path $defaultPath -ErrorAction SilentlyContinue
    if ($resolved) { $AgentPath = $resolved.Path }
}

if (-not $AgentPath -or -not (Test-Path $AgentPath)) {
    Write-Host "  [ERROR] KryossAgent.exe not found." -ForegroundColor Red
    Write-Host "  Run KryossAgent\publish.ps1 first, or provide -AgentPath." -ForegroundColor Yellow
    exit 1
}

$agentSize = [math]::Round((Get-Item $AgentPath).Length / 1MB, 1)
Write-Host "  Agent:     $AgentPath ($agentSize MB)" -ForegroundColor White
Write-Host "  API URL:   $ApiUrl" -ForegroundColor White
Write-Host "  Code:      $EnrollmentCode" -ForegroundColor White
Write-Host ""

# Get credentials
if (-not $Credential) {
    Write-Host "  Enter domain admin credentials for remote access:" -ForegroundColor Yellow
    $Credential = Get-Credential -Message "Domain Admin for remote deployment"
}

# Get/download PsExec
$psExecPath = Join-Path $scriptDir "PsExec64.exe"
if (-not (Test-Path $psExecPath)) {
    Write-Host "  Downloading PsExec64.exe..." -ForegroundColor Gray
    try {
        Invoke-WebRequest -Uri $PSEXEC_URL -OutFile $psExecPath -UseBasicParsing
        Write-Host "  PsExec64.exe downloaded." -ForegroundColor Green
    }
    catch {
        Write-Host "  [ERROR] Failed to download PsExec: $_" -ForegroundColor Red
        Write-Host "  Download manually from https://live.sysinternals.com/PsExec64.exe" -ForegroundColor Yellow
        exit 1
    }
}

# ── Phase 1: Discovery ────────────────────────────────────────────
Write-Host "  ════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  PHASE 1: DISCOVERY" -ForegroundColor Cyan
Write-Host "  ════════════════════════════════════════" -ForegroundColor Cyan

$machines = @()

if ($TargetHosts -and $TargetHosts.Count -gt 0) {
    Write-Host "  Using provided target list: $($TargetHosts.Count) hosts" -ForegroundColor White
    $machines = $TargetHosts | ForEach-Object {
        [PSCustomObject]@{ Name = $_; IP = $_; Status = 'Pending' }
    }
}
elseif ($DiscoveryMethod -eq 'AD') {
    Write-Host "  Querying Active Directory for Windows computers..." -ForegroundColor Gray
    try {
        $adComputers = Get-ADComputer -Filter {
            OperatingSystem -like "Windows*" -and Enabled -eq $true
        } -Properties OperatingSystem, IPv4Address, LastLogonDate |
        Where-Object { $_.LastLogonDate -gt (Get-Date).AddDays(-30) } |
        Sort-Object Name

        $machines = $adComputers | ForEach-Object {
            [PSCustomObject]@{
                Name   = $_.Name
                IP     = $_.IPv4Address
                OS     = $_.OperatingSystem
                Status = 'Pending'
            }
        }
        Write-Host "  Found $($machines.Count) active Windows machines" -ForegroundColor Green
    }
    catch {
        Write-Host "  [WARN] AD query failed: $_" -ForegroundColor Yellow
        Write-Host "  Falling back to network discovery..." -ForegroundColor Yellow
        $DiscoveryMethod = 'Network'
    }
}

if ($DiscoveryMethod -eq 'Network' -and $machines.Count -eq 0) {
    Write-Host "  Running network discovery (ARP + ping sweep)..." -ForegroundColor Gray
    $discoveryScript = Join-Path $scriptDir "Get-NetworkDevices.ps1"
    if (Test-Path $discoveryScript) {
        $devices = & $discoveryScript -DiscoveryMethod Both -IncludeHostnames
        $machines = $devices | Where-Object { $_.Hostname } | ForEach-Object {
            [PSCustomObject]@{
                Name   = $_.Hostname
                IP     = $_.IPAddress
                Status = 'Pending'
            }
        }
        Write-Host "  Found $($machines.Count) devices with hostnames" -ForegroundColor Green
    }
    else {
        Write-Host "  [ERROR] Get-NetworkDevices.ps1 not found. Provide -TargetHosts." -ForegroundColor Red
        exit 1
    }
}

if ($machines.Count -eq 0) {
    Write-Host "  [ERROR] No machines found. Nothing to deploy." -ForegroundColor Red
    exit 1
}

Write-Host ""
$machines | Format-Table Name, IP, OS -AutoSize | Out-Host

# ── Phase 2: Deploy Agent ─────────────────────────────────────────
Write-Host "  ════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  PHASE 2: DEPLOY ($($machines.Count) machines)" -ForegroundColor Cyan
Write-Host "  ════════════════════════════════════════" -ForegroundColor Cyan

$username = $Credential.UserName
$password = $Credential.GetNetworkCredential().Password

$deployResults = @()

foreach ($machine in $machines) {
    $target = if ($machine.IP) { $machine.IP } else { $machine.Name }
    $sharePath = "\\$target\$REMOTE_DIR"

    Write-Host "  [$($machine.Name)] " -NoNewline -ForegroundColor White

    # Test connectivity
    if (-not (Test-Connection -ComputerName $target -Count 1 -Quiet -TimeoutSeconds 3)) {
        Write-Host "OFFLINE" -ForegroundColor Red
        $machine.Status = 'Offline'
        $deployResults += [PSCustomObject]@{ Host = $machine.Name; Phase = 'Deploy'; Status = 'Offline'; Detail = 'Ping failed' }
        continue
    }

    # Create remote directory and copy agent
    try {
        # Map drive temporarily with credentials
        $netResult = & net use "\\$target\C$" /user:$username $password 2>&1
        if ($LASTEXITCODE -ne 0 -and $netResult -notmatch 'already') {
            Write-Host "ACCESS DENIED" -ForegroundColor Red
            $machine.Status = 'AccessDenied'
            $deployResults += [PSCustomObject]@{ Host = $machine.Name; Phase = 'Deploy'; Status = 'AccessDenied'; Detail = $netResult }
            continue
        }

        # Create directory
        $remoteKryossDir = "\\$target\C$\ProgramData\Kryoss"
        if (-not (Test-Path $remoteKryossDir)) {
            New-Item -Path $remoteKryossDir -ItemType Directory -Force | Out-Null
        }

        # Copy agent
        Copy-Item -Path $AgentPath -Destination "$remoteKryossDir\KryossAgent.exe" -Force
        Write-Host "DEPLOYED" -ForegroundColor Green
        $machine.Status = 'Deployed'
        $deployResults += [PSCustomObject]@{ Host = $machine.Name; Phase = 'Deploy'; Status = 'OK'; Detail = 'Agent copied' }

        # Disconnect
        & net use "\\$target\C$" /delete /y 2>&1 | Out-Null
    }
    catch {
        Write-Host "FAILED: $_" -ForegroundColor Red
        $machine.Status = 'DeployFailed'
        $deployResults += [PSCustomObject]@{ Host = $machine.Name; Phase = 'Deploy'; Status = 'Failed'; Detail = $_.ToString() }
        & net use "\\$target\C$" /delete /y 2>&1 | Out-Null
    }
}

Write-Host ""

# ── Phase 3: Run Assessment ───────────────────────────────────────
Write-Host "  ════════════════════════════════════════" -ForegroundColor Cyan
$deployedMachines = $machines | Where-Object { $_.Status -eq 'Deployed' }
Write-Host "  PHASE 3: SCAN ($($deployedMachines.Count) machines)" -ForegroundColor Cyan
Write-Host "  ════════════════════════════════════════" -ForegroundColor Cyan

$scanResults = @()

foreach ($machine in $deployedMachines) {
    $target = if ($machine.IP) { $machine.IP } else { $machine.Name }

    Write-Host "  [$($machine.Name)] Scanning... " -NoNewline -ForegroundColor White

    try {
        $psExecArgs = @(
            "\\$target"
            "-u", $username
            "-p", $password
            "-h"                           # Run elevated
            "-n", "600"                    # 10 minute timeout
            "-accepteula"
            $REMOTE_EXE
            "--silent"
            "--code", $EnrollmentCode
            "--api-url", $ApiUrl
        )

        $process = Start-Process -FilePath $psExecPath -ArgumentList $psExecArgs `
            -Wait -PassThru -NoNewWindow -RedirectStandardOutput "$env:TEMP\kryoss_$($machine.Name)_out.txt" `
            -RedirectStandardError "$env:TEMP\kryoss_$($machine.Name)_err.txt"

        $stdout = Get-Content "$env:TEMP\kryoss_$($machine.Name)_out.txt" -Raw -ErrorAction SilentlyContinue
        $stderr = Get-Content "$env:TEMP\kryoss_$($machine.Name)_err.txt" -Raw -ErrorAction SilentlyContinue

        if ($process.ExitCode -eq 0) {
            # Parse the one-line output: "Kryoss Assessment: A (96.29%) - Pass:84 Warn:3 Fail:4"
            $resultLine = ($stdout -split "`n" | Where-Object { $_ -match 'Kryoss Assessment' } | Select-Object -Last 1)
            if ($resultLine) {
                Write-Host $resultLine.Trim() -ForegroundColor Green
            } else {
                Write-Host "COMPLETED (exit 0)" -ForegroundColor Green
            }
            $machine.Status = 'Scanned'
            $scanResults += [PSCustomObject]@{ Host = $machine.Name; Phase = 'Scan'; Status = 'OK'; Detail = $resultLine }
        }
        elseif ($process.ExitCode -eq 2) {
            Write-Host "WARN (exit 2) — results saved offline" -ForegroundColor Yellow
            $machine.Status = 'Partial'
            $scanResults += [PSCustomObject]@{ Host = $machine.Name; Phase = 'Scan'; Status = 'Warn'; Detail = $stderr }
        }
        else {
            Write-Host "FAILED (exit $($process.ExitCode))" -ForegroundColor Red
            $machine.Status = 'ScanFailed'
            $scanResults += [PSCustomObject]@{ Host = $machine.Name; Phase = 'Scan'; Status = 'Failed'; Detail = $stderr }
        }

        # Cleanup temp files
        Remove-Item "$env:TEMP\kryoss_$($machine.Name)_out.txt" -ErrorAction SilentlyContinue
        Remove-Item "$env:TEMP\kryoss_$($machine.Name)_err.txt" -ErrorAction SilentlyContinue
    }
    catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
        $machine.Status = 'ScanFailed'
        $scanResults += [PSCustomObject]@{ Host = $machine.Name; Phase = 'Scan'; Status = 'Error'; Detail = $_.ToString() }
    }
}

# ── Summary ────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ╔══════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "  ║         DEPLOYMENT COMPLETE               ║" -ForegroundColor Green
Write-Host "  ╚══════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

$totalCount    = $machines.Count
$scannedCount  = ($machines | Where-Object { $_.Status -eq 'Scanned' }).Count
$offlineCount  = ($machines | Where-Object { $_.Status -eq 'Offline' }).Count
$deniedCount   = ($machines | Where-Object { $_.Status -eq 'AccessDenied' }).Count
$failedCount   = $totalCount - $scannedCount - $offlineCount - $deniedCount

Write-Host "  Total machines:    $totalCount"
Write-Host "  Scanned OK:        $scannedCount" -ForegroundColor Green
Write-Host "  Offline:           $offlineCount" -ForegroundColor $(if ($offlineCount -gt 0) { 'Yellow' } else { 'White' })
Write-Host "  Access Denied:     $deniedCount" -ForegroundColor $(if ($deniedCount -gt 0) { 'Red' } else { 'White' })
Write-Host "  Failed:            $failedCount" -ForegroundColor $(if ($failedCount -gt 0) { 'Red' } else { 'White' })
Write-Host ""

# Export report
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$reportPath = Join-Path $OutputDir "KryossDeployment_$timestamp.json"
$report = @{
    timestamp       = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    apiUrl          = $ApiUrl
    enrollmentCode  = $EnrollmentCode
    totalMachines   = $totalCount
    scanned         = $scannedCount
    offline         = $offlineCount
    accessDenied    = $deniedCount
    failed          = $failedCount
    machines        = $machines
    deployResults   = $deployResults
    scanResults     = $scanResults
} | ConvertTo-Json -Depth 5

$reportPath = Join-Path $OutputDir "KryossDeployment_$timestamp.json"
$report | Out-File -FilePath $reportPath -Encoding utf8
Write-Host "  Report saved: $reportPath" -ForegroundColor Cyan
Write-Host ""
```

- [ ] **Step 2: Commit**

```bash
git add Scripts/NetworkDiscovery/Invoke-KryossDeployment.ps1
git commit -m "feat(deploy): add network deployment orchestrator for client assessments"
```

---

### Task 6: End-to-End Test (Local)

- [ ] **Step 1: Build the agent .exe**

```powershell
cd "C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\KryossAgent"
.\publish.ps1
```

- [ ] **Step 2: Reset DB and create bulk enrollment code**

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d KryossDb -I -Q "
DELETE FROM control_results;
DELETE FROM assessment_runs;
DELETE FROM machines;
DELETE FROM enrollment_codes;
-- Insert bulk code (max 50 uses)
INSERT INTO enrollment_codes (organization_id, code, assessment_id, label, max_uses, expires_at, created_by)
VALUES ('22222222-2222-2222-2222-222222222222', 'TEST-BULK-CODE-2026',
        (SELECT TOP 1 id FROM assessments WHERE is_active=1),
        'Local Test - Bulk', 50, DATEADD(DAY, 30, SYSUTCDATETIME()),
        '00000000-0000-0000-0000-000000000001');
-- Clear org API keys to force regeneration
UPDATE organizations SET api_key=NULL, api_secret=NULL WHERE id='22222222-2222-2222-2222-222222222222';
"
```

- [ ] **Step 3: Start API and test the .exe**

```powershell
# Terminal 1: API
cd "C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\KryossApi\src\KryossApi"
func start

# Terminal 2: Agent (admin)
Remove-Item "HKLM:\SOFTWARE\Kryoss\Agent" -Recurse -ErrorAction SilentlyContinue
& "C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\KryossAgent\publish\KryossAgent.exe" --silent --code TEST-BULK-CODE-2026 --api-url http://localhost:7071
```

Expected: One-line output with grade. Check DB shows use_count=1, machine created.

- [ ] **Step 4: Run .exe again (simulates second machine)**

```powershell
Remove-Item "HKLM:\SOFTWARE\Kryoss\Agent" -Recurse -ErrorAction SilentlyContinue
& "...\KryossAgent.exe" --silent --code TEST-BULK-CODE-2026 --api-url http://localhost:7071
```

Expected: Same code works again. DB shows use_count=2, two machines.

---

## Execution Order for a Real Client Assessment

Once all tasks are complete, here's the workflow for an actual client:

```
1. Deploy API to Azure:      .\Setup-Azure.ps1
2. Create client in DB:       Edit + run seed_client.sql
3. Build agent:               .\publish.ps1
4. Go to client site, run:    .\Invoke-KryossDeployment.ps1 -ApiUrl "https://..." -EnrollmentCode "XXXX-..."
5. Query results:             SELECT * FROM assessment_runs ORDER BY started_at DESC
```
