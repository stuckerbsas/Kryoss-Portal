<#
.SYNOPSIS
    Deploys Kryoss Agent v1.3.0+ via Group Policy Startup Script.

.DESCRIPTION
    This is the SERVER-SIDE setup script. Run it ONCE on a domain controller
    (or any machine with RSAT + domain admin privileges) to:

      1. Copy KryossAgent.exe to a network share accessible by all domain machines
      2. Create a Startup Script GPO that runs the agent on each workstation/server
      3. Link the GPO to the chosen OU

    The agent itself is a passive sensor — v1.3.0+ does NOT do remote execution.
    Deployment is handled by native Windows infrastructure (GPO), not by the agent.

    REQUIREMENTS:
    - Windows Server with RSAT GroupPolicy module
    - Domain Admin or equivalent rights on the target OU
    - A network share readable by Domain Computers (e.g. \\contoso.local\NETLOGON\Kryoss)
    - KryossAgent.exe from the Kryoss portal (enrollment-code patched or use --code flag)

.PARAMETER AgentPath
    Local path to the KryossAgent.exe to deploy.

.PARAMETER NetworkShare
    UNC path where the agent will be copied.
    Default: \\<domain>\NETLOGON\Kryoss\KryossAgent.exe

.PARAMETER EnrollmentCode
    Enrollment code from the Kryoss portal. Required unless the binary is patched.

.PARAMETER TargetOU
    LDAP path of the OU where the GPO will be linked.
    Example: "OU=Workstations,DC=contoso,DC=local"

.PARAMETER GpoName
    Name of the GPO to create.
    Default: "Kryoss Security Agent Startup"

.PARAMETER ScheduleType
    How to run the agent:
      Startup   — runs once at machine startup (Group Policy Startup Script)
      Scheduled — creates a Scheduled Task that runs daily at 3 AM
    Default: Startup

.EXAMPLE
    .\Deploy-KryossGPO.ps1 `
        -AgentPath "C:\Temp\KryossAgent.exe" `
        -EnrollmentCode "<ENROLLMENT_CODE>" `
        -TargetOU "OU=Workstations,DC=contoso,DC=local"

.EXAMPLE
    # Daily scheduled scan instead of startup
    .\Deploy-KryossGPO.ps1 `
        -AgentPath "C:\Temp\KryossAgent.exe" `
        -EnrollmentCode "<ENROLLMENT_CODE>" `
        -TargetOU "OU=Workstations,DC=contoso,DC=local" `
        -ScheduleType Scheduled
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$AgentPath,

    [Parameter(Mandatory = $false)]
    [string]$NetworkShare,

    [Parameter(Mandatory = $false)]
    [string]$EnrollmentCode,

    [Parameter(Mandatory = $true)]
    [string]$TargetOU,

    [Parameter(Mandatory = $false)]
    [string]$GpoName = "Kryoss Security Agent Startup",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Startup", "Scheduled")]
    [string]$ScheduleType = "Startup"
)

$ErrorActionPreference = "Stop"

# ── Banner ──
Write-Host ""
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host "    Kryoss Security Agent — GPO Deployment" -ForegroundColor Green
Write-Host "    TeamLogic IT" -ForegroundColor Green
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host ""

# ── Validate agent binary ──
if (-not (Test-Path $AgentPath)) {
    Write-Error "Agent binary not found: $AgentPath"
    exit 1
}

$agentFile = Get-Item $AgentPath
Write-Host "  Agent binary: $($agentFile.FullName)" -ForegroundColor Cyan
Write-Host "  Size:         $([Math]::Round($agentFile.Length / 1MB, 1)) MB" -ForegroundColor Cyan

# ── Resolve domain + default share ──
Import-Module GroupPolicy -ErrorAction Stop
Import-Module ActiveDirectory -ErrorAction Stop

$domain = (Get-ADDomain).DNSRoot
Write-Host "  Domain:       $domain" -ForegroundColor Cyan

if (-not $NetworkShare) {
    $NetworkShare = "\\$domain\NETLOGON\Kryoss\KryossAgent.exe"
}
Write-Host "  Share target: $NetworkShare" -ForegroundColor Cyan
Write-Host ""

# ── Copy agent to network share ──
Write-Host "  Copying agent to network share..." -ForegroundColor Cyan
$shareDir = Split-Path $NetworkShare -Parent
if (-not (Test-Path $shareDir)) {
    New-Item -Path $shareDir -ItemType Directory -Force | Out-Null
}
Copy-Item -Path $AgentPath -Destination $NetworkShare -Force
Write-Host "    Copied." -ForegroundColor Green

# ── Create GPO ──
Write-Host ""
Write-Host "  Checking GPO '$GpoName'..." -ForegroundColor Cyan

$gpo = Get-GPO -Name $GpoName -ErrorAction SilentlyContinue
if ($gpo) {
    Write-Host "    GPO already exists (Id: $($gpo.Id))" -ForegroundColor Yellow
    $confirm = Read-Host "  Update existing GPO? (Y/N)"
    if ($confirm -ne "Y") { exit 1 }
} else {
    Write-Host "    Creating new GPO..." -ForegroundColor Gray
    $gpo = New-GPO -Name $GpoName -Comment "Kryoss Security Agent v1.3.0+ — local security assessment"
    Write-Host "    GPO created (Id: $($gpo.Id))" -ForegroundColor Green
}

# ── Build agent command ──
$agentArgs = "--silent"
if ($EnrollmentCode) {
    $agentArgs += " --code $EnrollmentCode"
}

if ($ScheduleType -eq "Startup") {
    # ── Option A: Startup Script ──
    Write-Host ""
    Write-Host "  Configuring Startup Script..." -ForegroundColor Cyan

    # Startup scripts go in: \\domain\SYSVOL\domain\Policies\{guid}\Machine\Scripts\Startup
    $gpoId = "{$($gpo.Id)}"
    $startupDir = "\\$domain\SYSVOL\$domain\Policies\$gpoId\Machine\Scripts\Startup"
    if (-not (Test-Path $startupDir)) {
        New-Item -Path $startupDir -ItemType Directory -Force | Out-Null
    }

    # scripts.ini — registers the startup script with the GPO
    $scriptsIniPath = "\\$domain\SYSVOL\$domain\Policies\$gpoId\Machine\Scripts\scripts.ini"
    $scriptsIniContent = @"
[Startup]
0CmdLine=$NetworkShare
0Parameters=$agentArgs
"@
    Set-Content -Path $scriptsIniPath -Value $scriptsIniContent -Encoding Unicode -Force

    # gPCMachineExtensionNames — tell Group Policy the GPO has CSE extensions
    Set-GPRegistryValue -Name $GpoName `
        -Key "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\State\Machine\Extension-List\{42B5FAAE-6536-11D2-AE5A-0000F87571E3}" `
        -ValueName "ProcessGroupPolicy" `
        -Type String `
        -Value "ScriptsProcessGroupPolicy" | Out-Null

    Write-Host "    Startup script registered." -ForegroundColor Green

} else {
    # ── Option B: Scheduled Task via GPO Preferences ──
    Write-Host ""
    Write-Host "  Configuring Scheduled Task (daily 3 AM)..." -ForegroundColor Cyan

    # Create a Scheduled Task XML definition and add it to the GPO Preferences
    $gpoId = "{$($gpo.Id)}"
    $prefDir = "\\$domain\SYSVOL\$domain\Policies\$gpoId\Machine\Preferences\ScheduledTasks"
    if (-not (Test-Path $prefDir)) {
        New-Item -Path $prefDir -ItemType Directory -Force | Out-Null
    }

    $taskGuid = [Guid]::NewGuid().ToString("B").ToUpper()
    $xmlContent = @"
<?xml version="1.0" encoding="utf-8"?>
<ScheduledTasks clsid="{CC63F200-7309-4ba0-B154-A71CD118DBCC}">
  <TaskV2 clsid="{D8896631-B747-47a7-84A6-C155337F3BC8}"
          name="Kryoss Agent Daily Scan"
          image="0"
          changed="$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
          uid="$taskGuid">
    <Properties action="C"
                name="Kryoss Agent Daily Scan"
                runAs="NT AUTHORITY\SYSTEM"
                logonType="S4U">
      <Task version="1.3">
        <RegistrationInfo>
          <Author>Kryoss</Author>
          <Description>Daily security assessment scan via Kryoss Agent</Description>
        </RegistrationInfo>
        <Principals>
          <Principal id="Author">
            <UserId>NT AUTHORITY\SYSTEM</UserId>
            <RunLevel>HighestAvailable</RunLevel>
          </Principal>
        </Principals>
        <Settings>
          <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
          <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
          <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
          <AllowHardTerminate>true</AllowHardTerminate>
          <StartWhenAvailable>true</StartWhenAvailable>
          <Enabled>true</Enabled>
          <Hidden>false</Hidden>
          <RunOnlyIfIdle>false</RunOnlyIfIdle>
          <WakeToRun>false</WakeToRun>
          <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
          <Priority>7</Priority>
        </Settings>
        <Triggers>
          <CalendarTrigger>
            <StartBoundary>2026-01-01T03:00:00</StartBoundary>
            <Enabled>true</Enabled>
            <ScheduleByDay>
              <DaysInterval>1</DaysInterval>
            </ScheduleByDay>
          </CalendarTrigger>
        </Triggers>
        <Actions Context="Author">
          <Exec>
            <Command>$NetworkShare</Command>
            <Arguments>$agentArgs</Arguments>
          </Exec>
        </Actions>
      </Task>
    </Properties>
  </TaskV2>
</ScheduledTasks>
"@

    Set-Content -Path "$prefDir\ScheduledTasks.xml" -Value $xmlContent -Encoding UTF8 -Force
    Write-Host "    Scheduled task configured." -ForegroundColor Green
}

# ── Link GPO to OU ──
Write-Host ""
Write-Host "  Linking GPO to $TargetOU..." -ForegroundColor Cyan
try {
    New-GPLink -Name $GpoName -Target $TargetOU -ErrorAction Stop | Out-Null
    Write-Host "    Linked." -ForegroundColor Green
}
catch {
    if ($_.Exception.Message -like "*already*") {
        Write-Host "    Already linked." -ForegroundColor Yellow
    } else {
        throw
    }
}

# ── Summary ──
Write-Host ""
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host "    Deployment Complete" -ForegroundColor Green
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "    GPO Name:     $GpoName" -ForegroundColor White
Write-Host "    Schedule:     $ScheduleType" -ForegroundColor White
Write-Host "    Agent Path:   $NetworkShare" -ForegroundColor White
Write-Host "    Target OU:    $TargetOU" -ForegroundColor White
if ($EnrollmentCode) {
    Write-Host "    Enrollment:   $EnrollmentCode" -ForegroundColor White
}
Write-Host ""
Write-Host "    Next steps:" -ForegroundColor Yellow
Write-Host "    1. Target machines will run the agent on next gpupdate /force + reboot" -ForegroundColor Yellow
Write-Host "    2. Or run 'gpupdate /force' on a test machine and then reboot it" -ForegroundColor Yellow
Write-Host "    3. Check Kryoss Portal > Organization > Fleet for enrolled machines" -ForegroundColor Yellow
Write-Host ""
Write-Host "  ================================================================" -ForegroundColor Green
