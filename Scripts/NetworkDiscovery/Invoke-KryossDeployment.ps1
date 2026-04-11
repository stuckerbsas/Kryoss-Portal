<#
.SYNOPSIS
    KryossAgent Network Deployment Orchestrator - Discovers, deploys, and scans Windows machines.

.DESCRIPTION
    Three-phase orchestrator for deploying KryossAgent across a client's domain network:
      Phase 1 (DISCOVER): Finds Windows machines via Active Directory or network scan.
      Phase 2 (DEPLOY):   Copies KryossAgent.exe to each machine via admin shares (always runs).
      Phase 3 (SCAN):     Executes the agent remotely via PsExec and collects results.
    Produces a JSON summary report at the end.

    By default machines are scanned sequentially (one at a time) for reliability.
    Use -Parallel to opt into concurrent scanning via background jobs.

    The API URL is hardcoded in the agent binary. Do NOT pass --api-url via PsExec.

.PARAMETER EnrollmentCode
    Enrollment code for agent registration with the API.

.PARAMETER AgentPath
    Path to KryossAgent.exe. If omitted, auto-detects from the default publish directory.

.PARAMETER TargetHosts
    Explicit list of hostnames or IPs. Skips discovery when provided.

.PARAMETER DiscoveryMethod
    How to find machines: 'AD' queries Active Directory, 'Network' uses Get-NetworkDevices.ps1.

.PARAMETER Credential
    PSCredential for admin access. Prompted interactively if not supplied.

.PARAMETER Parallel
    Opt-in: run PsExec scans in parallel using background jobs (max -MaxConcurrent).
    Default is sequential (one machine at a time) which is more reliable.

.PARAMETER MaxConcurrent
    Maximum machines to process in parallel (default 5). Only used with -Parallel.

.PARAMETER OutputDir
    Directory for the JSON report file (default: current directory).

.NOTES
    Author:   TeamLogic IT
    Project:  Kryoss Network Deployment
    Version:  2.0
    Created:  2026-04-07
    Modified: 2026-04-10

.EXAMPLE
    .\Invoke-KryossDeployment.ps1 -EnrollmentCode "ABC123"

.EXAMPLE
    .\Invoke-KryossDeployment.ps1 -EnrollmentCode "ABC123" -TargetHosts "SRV01","SRV02","WS10"

.EXAMPLE
    .\Invoke-KryossDeployment.ps1 -EnrollmentCode "ABC123" -Parallel -MaxConcurrent 3

.EXAMPLE
    .\Invoke-KryossDeployment.ps1 -EnrollmentCode "ABC123" -DiscoveryMethod Network -Credential (Get-Credential)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EnrollmentCode,

    [Parameter(Mandatory = $false)]
    [string]$AgentPath = "",

    [Parameter(Mandatory = $false)]
    [string[]]$TargetHosts = @(),

    [Parameter(Mandatory = $false)]
    [ValidateSet("AD", "Network")]
    [string]$DiscoveryMethod = "AD",

    [Parameter(Mandatory = $false)]
    [System.Management.Automation.PSCredential]$Credential = $null,

    [Parameter(Mandatory = $false)]
    [switch]$Parallel,

    [Parameter(Mandatory = $false)]
    [int]$MaxConcurrent = 5,

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "."
)

# ============================================================================
# LOGGING
# ============================================================================
$LOG_DIR  = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE = Join-Path -Path $LOG_DIR -ChildPath "Invoke-KryossDeployment_$(Get-Date -Format 'yyyyMMdd').log"
$REMOTE_AGENT_DIR = "C:\ProgramData\Kryoss"

if (-not (Test-Path -Path $LOG_DIR)) {
    New-Item -Path $LOG_DIR -ItemType Directory -Force | Out-Null
}

function Write-Log {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [Parameter(Mandatory = $false)]
        [ValidateSet("INFO", "WARN", "ERROR")]
        [string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$timestamp] [$Level] $Message"
    Write-Host $entry
    Add-Content -Path $LOG_FILE -Value $entry
}

function Write-Banner {
    param([string]$Title)
    $line = "=" * 70
    Write-Host ""
    Write-Host $line -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host $line -ForegroundColor Cyan
    Write-Host ""
}

function Write-SectionHeader {
    param([string]$Title)
    $line = "-" * 60
    Write-Host ""
    Write-Host $line -ForegroundColor White
    Write-Host "  $Title" -ForegroundColor White
    Write-Host $line -ForegroundColor White
}

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

function Resolve-AgentPath {
    <#
    .SYNOPSIS
        Locates KryossAgent.exe. Search order:
        1. Explicit -AgentPath if provided
        2. Same directory as this script (for client deployment packages)
        3. KryossAgent\publish\ relative to project root (for development)
    #>
    param([string]$ExplicitPath)

    # 1. Explicit path
    if ($ExplicitPath -and (Test-Path -Path $ExplicitPath)) {
        Write-Log -Message "Agent path provided: $ExplicitPath"
        return $ExplicitPath
    }

    # 2. Same directory as this script (client deployment scenario)
    $sameDirPath = Join-Path -Path $PSScriptRoot -ChildPath "KryossAgent.exe"
    if (Test-Path -Path $sameDirPath) {
        Write-Log -Message "Agent found next to script: $sameDirPath"
        return $sameDirPath
    }

    # 3. Development publish directory
    $devPath = Join-Path -Path $PSScriptRoot -ChildPath "..\..\..\KryossAgent\publish\KryossAgent.exe"
    $devPath = [System.IO.Path]::GetFullPath($devPath)
    if (Test-Path -Path $devPath) {
        Write-Log -Message "Agent found in dev publish dir: $devPath"
        return $devPath
    }

    Write-Log -Message "KryossAgent.exe not found. Searched: '$sameDirPath', '$devPath'" -Level "ERROR"
    return $null
}

function Get-PsExecPath {
    <#
    .SYNOPSIS
        Locates or downloads PsExec64.exe from Microsoft Sysinternals.
    #>
    $possiblePaths = @(
        "$PSScriptRoot\PsExec64.exe",
        "${env:TEMP}\PsExec64.exe",
        "${env:SYSTEMROOT}\System32\PsExec64.exe",
        "${env:PROGRAMFILES}\Sysinternals\PsExec64.exe"
    )

    foreach ($path in $possiblePaths) {
        if (Test-Path -Path $path) {
            Write-Log -Message "PsExec found: $path"
            return $path
        }
    }

    # Download PsExec
    $downloadPath = Join-Path -Path $env:TEMP -ChildPath "PsExec64.exe"
    Write-Log -Message "Downloading PsExec64.exe from live.sysinternals.com..."

    try {
        $downloadUrl = "https://live.sysinternals.com/PsExec64.exe"
        Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath -TimeoutSec 60 -UseBasicParsing -ErrorAction Stop

        $fileInfo = Get-Item -Path $downloadPath -ErrorAction SilentlyContinue
        if ($fileInfo -and $fileInfo.Length -gt 100KB -and $fileInfo.Length -lt 2MB) {
            Write-Log -Message "PsExec downloaded successfully ($([math]::Round($fileInfo.Length / 1KB, 1)) KB)"
            return $downloadPath
        } else {
            throw "Downloaded file appears invalid (size: $($fileInfo.Length) bytes)"
        }
    } catch {
        Write-Log -Message "Failed to download PsExec: $_" -Level "ERROR"
        return $null
    }
}

function Get-CredentialSafe {
    <#
    .SYNOPSIS
        Returns the provided credential or prompts for one interactively.
    #>
    param([System.Management.Automation.PSCredential]$Cred)

    if ($Cred) {
        return $Cred
    }

    Write-Host ""
    Write-Host "[INFO] Credentials required for remote admin access." -ForegroundColor Yellow
    Write-Host "       Use DOMAIN\username or .\localadmin format." -ForegroundColor Yellow
    Write-Host ""

    try {
        $username = Read-Host -Prompt "  Username"
        if ([string]::IsNullOrWhiteSpace($username)) { throw "No username provided" }
        $secPass = Read-Host -Prompt "  Password" -AsSecureString
        $Cred = New-Object System.Management.Automation.PSCredential($username, $secPass)
        return $Cred
    } catch {
        Write-Log -Message "Credential prompt cancelled or failed: $_" -Level "ERROR"
        return $null
    }
}

# ============================================================================
# PHASE 1: DISCOVER
# ============================================================================

function Invoke-DiscoveryPhase {
    <#
    .SYNOPSIS
        Discovers target Windows machines via explicit list, AD, or network scan.
    #>
    param(
        [string[]]$Hosts,
        [string]$Method
    )

    Write-Banner -Title "PHASE 1: DISCOVER"

    # If explicit hosts were provided, skip discovery
    if ($Hosts -and $Hosts.Count -gt 0) {
        Write-Log -Message "Using $($Hosts.Count) explicitly provided target host(s)"
        $machines = @()
        foreach ($h in $Hosts) {
            $machines += @{
                Name      = $h
                IPAddress = $h
                Source    = "Manual"
                Status    = "Pending"
            }
        }
        Show-DiscoveryTable -Machines $machines
        return $machines
    }

    # Try AD discovery first (if method is AD)
    if ($Method -eq "AD") {
        Write-Log -Message "Attempting Active Directory discovery..."
        $machines = Get-ADMachines
        if ($machines -and $machines.Count -gt 0) {
            Write-Log -Message "AD discovery found $($machines.Count) machine(s)"
            Show-DiscoveryTable -Machines $machines
            return $machines
        }
        Write-Log -Message "AD discovery returned no results or failed, falling back to Network scan" -Level "WARN"
    }

    # Network discovery fallback
    Write-Log -Message "Running network discovery via Get-NetworkDevices.ps1..."
    $machines = Get-NetworkMachines
    if ($machines -and $machines.Count -gt 0) {
        Write-Log -Message "Network discovery found $($machines.Count) device(s)"
        Show-DiscoveryTable -Machines $machines
        return $machines
    }

    Write-Log -Message "No machines discovered" -Level "ERROR"
    return @()
}

function Get-ADMachines {
    <#
    .SYNOPSIS
        Queries Active Directory for enabled Windows computers with recent logon activity.
    #>
    try {
        Import-Module -Name ActiveDirectory -ErrorAction Stop
    } catch {
        Write-Log -Message "ActiveDirectory module not available: $_" -Level "WARN"
        return @()
    }

    try {
        $cutoffDate = (Get-Date).AddDays(-30)
        $adComputers = Get-ADComputer -Filter {
            OperatingSystem -like "Windows*" -and Enabled -eq $true
        } -Properties Name, DNSHostName, OperatingSystem, LastLogonDate |
            Where-Object { $_.LastLogonDate -and $_.LastLogonDate -ge $cutoffDate }

        if (-not $adComputers -or @($adComputers).Count -eq 0) {
            Write-Log -Message "No active Windows computers found in AD within last 30 days" -Level "WARN"
            return @()
        }

        $machines = @()
        foreach ($computer in $adComputers) {
            $hostEntry = $computer.DNSHostName
            if (-not $hostEntry) { $hostEntry = $computer.Name }

            $machines += @{
                Name      = $computer.Name
                IPAddress = $hostEntry
                OS        = $computer.OperatingSystem
                LastLogon = $computer.LastLogonDate
                Source    = "AD"
                Status    = "Pending"
            }
        }
        return $machines
    } catch {
        Write-Log -Message "AD query failed: $_" -Level "ERROR"
        return @()
    }
}

function Get-NetworkMachines {
    <#
    .SYNOPSIS
        Uses Get-NetworkDevices.ps1 to discover hosts on the local subnet.
    #>
    $discoveryScript = Join-Path -Path $PSScriptRoot -ChildPath "Get-NetworkDevices.ps1"

    if (-not (Test-Path -Path $discoveryScript)) {
        Write-Log -Message "Get-NetworkDevices.ps1 not found at: $discoveryScript" -Level "ERROR"
        return @()
    }

    try {
        Write-Log -Message "Executing: $discoveryScript -IncludeHostnames -ExportResults"
        $devices = & $discoveryScript -IncludeHostnames -ExportResults -OutputPath $env:TEMP 2>&1

        # Look for exported JSON to parse results
        $latestJson = Get-ChildItem -Path $env:TEMP -Filter "NetworkDiscovery_*.json" -ErrorAction SilentlyContinue |
            Sort-Object -Property LastWriteTime -Descending |
            Select-Object -First 1

        if ($latestJson) {
            $rawDevices = Get-Content -Path $latestJson.FullName -Raw | ConvertFrom-Json
            $machines = @()
            foreach ($device in $rawDevices) {
                $machines += @{
                    Name      = if ($device.Hostname) { $device.Hostname } else { $device.IPAddress }
                    IPAddress = $device.IPAddress
                    Source    = "Network"
                    Status    = "Pending"
                }
            }
            return $machines
        }

        Write-Log -Message "No network discovery JSON output found" -Level "WARN"
        return @()
    } catch {
        Write-Log -Message "Network discovery failed: $_" -Level "ERROR"
        return @()
    }
}

function Show-DiscoveryTable {
    <#
    .SYNOPSIS
        Displays discovered machines in a formatted table.
    #>
    param([array]$Machines)

    Write-Host ""
    Write-Host ("  {0,-5} {1,-25} {2,-20} {3,-10}" -f "#", "Name", "IP/Host", "Source")
    Write-Host ("  {0,-5} {1,-25} {2,-20} {3,-10}" -f "---", "-------------------------", "--------------------", "----------")
    $index = 1
    foreach ($m in $Machines) {
        $name = if ($m.Name.Length -gt 24) { $m.Name.Substring(0, 21) + "..." } else { $m.Name }
        $ip   = if ($m.IPAddress.Length -gt 19) { $m.IPAddress.Substring(0, 16) + "..." } else { $m.IPAddress }
        Write-Host ("  {0,-5} {1,-25} {2,-20} {3,-10}" -f $index, $name, $ip, $m.Source)
        $index++
    }
    Write-Host ""
    Write-Log -Message "Discovered $($Machines.Count) target machine(s)"
}

# ============================================================================
# PHASE 2: DEPLOY
# ============================================================================

function Invoke-DeployPhase {
    <#
    .SYNOPSIS
        Copies KryossAgent.exe to each discovered machine via admin share.
    #>
    param(
        [array]$Machines,
        [string]$AgentExePath,
        [System.Management.Automation.PSCredential]$Cred
    )

    Write-Banner -Title "PHASE 2: DEPLOY"

    $username = $Cred.UserName
    $password = $Cred.GetNetworkCredential().Password
    $totalCount = $Machines.Count
    $currentIndex = 0

    foreach ($machine in $Machines) {
        $currentIndex++
        $target = $machine.IPAddress
        $displayName = $machine.Name
        Write-SectionHeader -Title "[$currentIndex/$totalCount] Deploying to: $displayName ($target)"

        # Step 1: Test connectivity
        Write-Log -Message "Testing connectivity to $target..."
        try {
            $ping = Test-Connection -ComputerName $target -Count 2 -Quiet -ErrorAction Stop
            if (-not $ping) {
                Write-Log -Message "$displayName is offline or unreachable" -Level "WARN"
                $machine.Status = "Offline"
                Write-Host "  [WARN] $displayName -- Offline" -ForegroundColor Yellow
                continue
            }
            Write-Host "  [OK] Ping successful" -ForegroundColor Green
        } catch {
            Write-Log -Message "Connectivity test failed for $target -- $_" -Level "WARN"
            $machine.Status = "Offline"
            Write-Host "  [WARN] $displayName -- Offline" -ForegroundColor Yellow
            continue
        }

        # Step 2: Map admin share
        $adminShare = "\\$target\C$"
        Write-Log -Message "Mapping admin share $adminShare..."
        try {
            # Disconnect any stale mapping first
            $null = net use $adminShare /delete /y 2>&1
            $netUseResult = net use $adminShare /user:$username $password 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "net use returned exit code $LASTEXITCODE -- $netUseResult"
            }
            Write-Host "  [OK] Admin share mapped" -ForegroundColor Green
        } catch {
            Write-Log -Message "Access denied to $adminShare -- $_" -Level "WARN"
            $machine.Status = "AccessDenied"
            Write-Host "  [WARN] $displayName -- Access Denied" -ForegroundColor Yellow
            continue
        }

        # Step 3: Create target directory and always copy (overwrite) agent binary
        try {
            $remoteDir = Join-Path -Path $adminShare -ChildPath "ProgramData\Kryoss"
            New-Item -Path $remoteDir -ItemType Directory -Force -ErrorAction SilentlyContinue | Out-Null

            $remoteAgentPath = Join-Path -Path $remoteDir -ChildPath "KryossAgent.exe"
            Copy-Item -Path $AgentExePath -Destination $remoteAgentPath -Force -ErrorAction Stop
            Write-Host "  [OK] KryossAgent.exe copied (overwrite) to $displayName" -ForegroundColor Green

            $machine.Status = "Deployed"
            Write-Log -Message "Successfully deployed agent to $displayName"
        } catch {
            Write-Log -Message "Deploy failed on $displayName -- $_" -Level "ERROR"
            $machine.Status = "DeployFailed"
            Write-Host "  [ERROR] Deploy failed on $displayName" -ForegroundColor Red
        } finally {
            # Disconnect admin share
            $null = net use $adminShare /delete /y 2>&1
        }
    }

    # Deploy summary
    $deployed = @($Machines | Where-Object { $_.Status -eq "Deployed" }).Count
    $offline  = @($Machines | Where-Object { $_.Status -eq "Offline" }).Count
    $denied   = @($Machines | Where-Object { $_.Status -eq "AccessDenied" }).Count
    $failed   = @($Machines | Where-Object { $_.Status -eq "DeployFailed" }).Count

    Write-Host ""
    Write-Host "  Deploy Summary: Deployed=$deployed  Offline=$offline  Denied=$denied  Failed=$failed" -ForegroundColor Cyan
    Write-Log -Message "Deploy phase complete: Deployed=$deployed Offline=$offline Denied=$denied Failed=$failed"

    return $Machines
}

# ============================================================================
# PHASE 3: SCAN
# ============================================================================

function Invoke-PsExecScan {
    <#
    .SYNOPSIS
        Runs PsExec for a single machine and returns a result hashtable.
        Used by both sequential and parallel scan paths.
    #>
    param(
        [string]$PsExecPath,
        [string]$Target,
        [string]$DisplayName,
        [string]$Username,
        [string]$Password,
        [string]$AgentPath,
        [string]$EnrollCode
    )

    $result = @{
        Name      = $DisplayName
        Target    = $Target
        ExitCode  = -1
        Stdout    = ""
        Stderr    = ""
        Error     = ""
        Status    = "ScanFailed"
        ResultLine = ""
    }

    try {
        # NOTE: --api-url is NOT passed. The URL is hardcoded in the agent binary.
        $psExecArgs = @(
            "\\$Target",
            "-u", $Username,
            "-p", $Password,
            "-s",
            "-h",
            "-n", "600",
            "-accepteula",
            $AgentPath,
            "--silent",
            "--reenroll",
            "--code", $EnrollCode
        )

        $processInfo = New-Object System.Diagnostics.ProcessStartInfo
        $processInfo.FileName = $PsExecPath
        $processInfo.Arguments = $psExecArgs -join " "
        $processInfo.UseShellExecute = $false
        $processInfo.RedirectStandardOutput = $true
        $processInfo.RedirectStandardError = $true
        $processInfo.CreateNoWindow = $true

        $process = [System.Diagnostics.Process]::Start($processInfo)
        $result.Stdout = $process.StandardOutput.ReadToEnd()
        $result.Stderr = $process.StandardError.ReadToEnd()

        if (-not $process.WaitForExit(600000)) {
            $process.Kill()
            $result.Error = "Timed out after 600 seconds"
            return $result
        }

        $result.ExitCode = $process.ExitCode

        # Parse the RESULT: line from agent stdout for meaningful status
        if ($result.Stdout) {
            $resultLine = ($result.Stdout -split "`n" | Where-Object { $_ -match "^RESULT:" } | Select-Object -Last 1)
            if ($resultLine) { $result.ResultLine = $resultLine.Trim() }
        }

        # Determine status from the RESULT: line first, then fall back to exit code
        if ($result.ResultLine -match "^RESULT:\s*OK\s*\|") {
            $result.Status = "Scanned"
        } elseif ($result.ResultLine -match "^RESULT:\s*OFFLINE\s*\|") {
            $result.Status = "Partial"
        } elseif ($result.ResultLine -match "^RESULT:\s*SKIP\s*\|") {
            $result.Status = "Partial"
        } elseif ($result.ResultLine -match "^RESULT:\s*ENROLL_FAILED\s*\|") {
            $result.Status = "ScanFailed"
            $result.Error = "Enrollment failed: $($result.ResultLine)"
        } elseif ($result.ResultLine -match "^RESULT:\s*ERROR\s*\|") {
            $result.Status = "ScanFailed"
            $result.Error = $result.ResultLine
        } elseif ($process.ExitCode -eq 0) {
            $result.Status = "Scanned"
        } elseif ($process.ExitCode -eq 2) {
            $result.Status = "Partial"
        } else {
            $result.Status = "ScanFailed"
            $result.Error = "PsExec exit code: $($process.ExitCode)"
        }
    } catch {
        $result.Error = $_.Exception.Message
    }

    return $result
}

function Show-ScanResult {
    <#
    .SYNOPSIS
        Displays the scan result for a single machine.
    #>
    param(
        [hashtable]$ScanResult,
        [hashtable]$Machine
    )

    $displayName = $Machine.Name

    $Machine.Status = $ScanResult.Status
    $Machine.ScanExitCode = $ScanResult.ExitCode
    $Machine.ScanStdout = $ScanResult.Stdout
    $Machine.ScanStderr = $ScanResult.Stderr
    $Machine.ScanError = $ScanResult.Error
    if ($ScanResult.ResultLine) { $Machine.ScanResult = $ScanResult.ResultLine }

    switch ($ScanResult.Status) {
        "Scanned" {
            $detail = if ($ScanResult.ResultLine) { $ScanResult.ResultLine } else { "exit 0" }
            Write-Host "  [OK] $displayName -- $detail" -ForegroundColor Green
            Write-Log -Message "Scan succeeded on $displayName -- $detail"
        }
        "Partial" {
            $detail = if ($ScanResult.ResultLine) { $ScanResult.ResultLine } else { "exit $($ScanResult.ExitCode)" }
            Write-Host "  [WARN] $displayName -- $detail" -ForegroundColor Yellow
            Write-Log -Message "Partial scan on $displayName -- $detail" -Level "WARN"
        }
        default {
            $detail = if ($ScanResult.Error) { $ScanResult.Error } else { "exit $($ScanResult.ExitCode)" }
            Write-Host "  [ERROR] $displayName -- $detail" -ForegroundColor Red
            Write-Log -Message "Scan failed on $displayName -- $detail" -Level "ERROR"
            if ($ScanResult.Stderr) {
                $stderrTrimmed = $ScanResult.Stderr.Trim()
                if ($stderrTrimmed.Length -gt 300) { $stderrTrimmed = $stderrTrimmed.Substring(0, 300) + "..." }
                Write-Log -Message "  stderr: $stderrTrimmed" -Level "ERROR"
            }
        }
    }
}

function Invoke-ScanPhaseSequential {
    <#
    .SYNOPSIS
        Runs KryossAgent remotely via PsExec one machine at a time (default, most reliable).
    #>
    param(
        [array]$Machines,
        [string]$PsExecExe,
        [System.Management.Automation.PSCredential]$Cred,
        [string]$Code
    )

    Write-Banner -Title "PHASE 3: SCAN (sequential)"

    $username = $Cred.UserName
    $password = $Cred.GetNetworkCredential().Password
    $deployedMachines = @($Machines | Where-Object { $_.Status -eq "Deployed" })

    if ($deployedMachines.Count -eq 0) {
        Write-Log -Message "No machines in Deployed state -- skipping scan phase" -Level "WARN"
        return $Machines
    }

    $totalCount = $deployedMachines.Count
    $agentRemotePath = "$REMOTE_AGENT_DIR\KryossAgent.exe"
    $currentIndex = 0

    Write-Log -Message "Scanning $totalCount deployed machine(s) sequentially..."

    foreach ($machine in $deployedMachines) {
        $currentIndex++
        $target = $machine.IPAddress
        $displayName = $machine.Name
        Write-SectionHeader -Title "[$currentIndex/$totalCount] Scanning: $displayName ($target)"

        $scanResult = Invoke-PsExecScan `
            -PsExecPath $PsExecExe `
            -Target $target `
            -DisplayName $displayName `
            -Username $username `
            -Password $password `
            -AgentPath $agentRemotePath `
            -EnrollCode $Code

        Show-ScanResult -ScanResult $scanResult -Machine $machine
    }

    # Scan summary
    $scanned = @($Machines | Where-Object { $_.Status -eq "Scanned" }).Count
    $partial = @($Machines | Where-Object { $_.Status -eq "Partial" }).Count
    $scanFailed = @($Machines | Where-Object { $_.Status -eq "ScanFailed" }).Count

    Write-Host ""
    Write-Host "  Scan Summary: Scanned=$scanned  Partial=$partial  Failed=$scanFailed" -ForegroundColor Cyan
    Write-Log -Message "Scan phase complete: Scanned=$scanned Partial=$partial Failed=$scanFailed"

    return $Machines
}

function Invoke-ScanPhaseParallel {
    <#
    .SYNOPSIS
        Runs KryossAgent remotely via PsExec in parallel batches using background jobs.
        Opt-in via -Parallel flag.
    #>
    param(
        [array]$Machines,
        [string]$PsExecExe,
        [System.Management.Automation.PSCredential]$Cred,
        [string]$Code,
        [int]$Concurrent = 5
    )

    Write-Banner -Title "PHASE 3: SCAN (parallel, max $Concurrent concurrent)"

    $username = $Cred.UserName
    $password = $Cred.GetNetworkCredential().Password
    $deployedMachines = @($Machines | Where-Object { $_.Status -eq "Deployed" })

    if ($deployedMachines.Count -eq 0) {
        Write-Log -Message "No machines in Deployed state -- skipping scan phase" -Level "WARN"
        return $Machines
    }

    $totalCount = $deployedMachines.Count
    Write-Log -Message "Scanning $totalCount deployed machine(s) in parallel batches of $Concurrent..."

    # ScriptBlock for background jobs -- self-contained, no external dependencies
    $scanScriptBlock = {
        param(
            [string]$PsExecPath,
            [string]$Target,
            [string]$DisplayName,
            [string]$Username,
            [string]$Password,
            [string]$AgentPath,
            [string]$EnrollCode
        )

        $result = @{
            Name       = $DisplayName
            Target     = $Target
            ExitCode   = -1
            Stdout     = ""
            Stderr     = ""
            Error      = ""
            Status     = "ScanFailed"
            ResultLine = ""
        }

        try {
            $psExecArgs = @(
                "\\$Target",
                "-u", $Username,
                "-p", $Password,
                "-s",
                "-h",
                "-n", "600",
                "-accepteula",
                $AgentPath,
                "--silent",
                "--reenroll",
                "--code", $EnrollCode
            )

            $processInfo = New-Object System.Diagnostics.ProcessStartInfo
            $processInfo.FileName = $PsExecPath
            $processInfo.Arguments = $psExecArgs -join " "
            $processInfo.UseShellExecute = $false
            $processInfo.RedirectStandardOutput = $true
            $processInfo.RedirectStandardError = $true
            $processInfo.CreateNoWindow = $true

            $process = [System.Diagnostics.Process]::Start($processInfo)
            $result.Stdout = $process.StandardOutput.ReadToEnd()
            $result.Stderr = $process.StandardError.ReadToEnd()

            if (-not $process.WaitForExit(600000)) {
                $process.Kill()
                $result.Error = "Timed out after 600 seconds"
                return $result
            }

            $result.ExitCode = $process.ExitCode

            # Parse the RESULT: line from agent stdout
            if ($result.Stdout) {
                $resultLine = ($result.Stdout -split "`n" | Where-Object { $_ -match "^RESULT:" } | Select-Object -Last 1)
                if ($resultLine) { $result.ResultLine = $resultLine.Trim() }
            }

            if ($result.ResultLine -match "^RESULT:\s*OK\s*\|") {
                $result.Status = "Scanned"
            } elseif ($result.ResultLine -match "^RESULT:\s*OFFLINE\s*\|") {
                $result.Status = "Partial"
            } elseif ($result.ResultLine -match "^RESULT:\s*SKIP\s*\|") {
                $result.Status = "Partial"
            } elseif ($result.ResultLine -match "^RESULT:\s*ENROLL_FAILED\s*\|") {
                $result.Status = "ScanFailed"
                $result.Error = "Enrollment failed: $($result.ResultLine)"
            } elseif ($result.ResultLine -match "^RESULT:\s*ERROR\s*\|") {
                $result.Status = "ScanFailed"
                $result.Error = $result.ResultLine
            } elseif ($process.ExitCode -eq 0) {
                $result.Status = "Scanned"
            } elseif ($process.ExitCode -eq 2) {
                $result.Status = "Partial"
            } else {
                $result.Status = "ScanFailed"
                $result.Error = "PsExec exit code: $($process.ExitCode)"
            }
        } catch {
            $result.Error = $_.Exception.Message
        }

        return $result
    }

    $agentRemotePath = "$REMOTE_AGENT_DIR\KryossAgent.exe"

    # Continuous pool: keep $Concurrent jobs running at a time
    $queue = [System.Collections.Queue]::new()
    foreach ($m in $deployedMachines) { $queue.Enqueue($m) }

    $activeJobs = @{}   # JobId -> @{ Job; Machine }
    $completedCount = 0

    # Helper: launch a scan job for the next machine in the queue
    function Start-NextScan {
        if ($queue.Count -eq 0) { return }
        $machine = $queue.Dequeue()
        $target = $machine.IPAddress
        $displayName = $machine.Name

        Write-Host "  [START] $displayName ($target)  [$($totalCount - $queue.Count)/$totalCount]" -ForegroundColor White

        $job = Start-Job -ScriptBlock $scanScriptBlock -ArgumentList @(
            $PsExecExe,
            $target,
            $displayName,
            $username,
            $password,
            $agentRemotePath,
            $Code
        )

        $activeJobs[$job.Id] = @{ Job = $job; Machine = $machine }
    }

    # Fill the initial pool
    $initialCount = [Math]::Min($Concurrent, $queue.Count)
    for ($i = 0; $i -lt $initialCount; $i++) {
        Start-NextScan
    }

    # Poll loop: check for completed jobs, show results, launch replacements
    $globalTimeout = [DateTime]::Now.AddMinutes(120)
    while ($activeJobs.Count -gt 0) {
        if ([DateTime]::Now -gt $globalTimeout) {
            Write-Log -Message "Global scan timeout (120 min) reached -- stopping remaining jobs" -Level "ERROR"
            foreach ($entry in $activeJobs.Values) {
                $entry.Machine.Status = "ScanFailed"
                $entry.Machine.ScanError = "Global timeout"
                Stop-Job -Job $entry.Job -ErrorAction SilentlyContinue
                Remove-Job -Job $entry.Job -Force -ErrorAction SilentlyContinue
            }
            $activeJobs.Clear()
            break
        }

        # Find completed jobs
        $completedIds = @($activeJobs.Keys | Where-Object {
            $activeJobs[$_].Job.State -in @("Completed", "Failed", "Stopped")
        })

        foreach ($jobId in $completedIds) {
            $entry = $activeJobs[$jobId]
            $activeJobs.Remove($jobId)
            $completedCount++

            # Process result
            $jobResult = $null
            if ($entry.Job.State -eq "Running") {
                Stop-Job -Job $entry.Job
                $entry.Machine.Status = "ScanFailed"
                $entry.Machine.ScanError = "Job timed out"
                Write-Host "  [ERROR] $($entry.Machine.Name) -- Timed out" -ForegroundColor Red
                Write-Log -Message "Scan timed out on $($entry.Machine.Name)" -Level "ERROR"
            } else {
                $jobResult = Receive-Job -Job $entry.Job
                if ($jobResult) {
                    Show-ScanResult -ScanResult $jobResult -Machine $entry.Machine
                } else {
                    $entry.Machine.Status = "ScanFailed"
                    $entry.Machine.ScanError = "Job returned no result"
                    Write-Host "  [ERROR] $($entry.Machine.Name) -- No result from job" -ForegroundColor Red
                    Write-Log -Message "Scan job returned no result for $($entry.Machine.Name)" -Level "ERROR"
                }
            }

            Remove-Job -Job $entry.Job -Force

            # Show progress
            Write-Host "  --- Progress: $completedCount/$totalCount done, $($activeJobs.Count) running, $($queue.Count) queued ---" -ForegroundColor DarkGray

            # Launch next machine to fill the slot
            Start-NextScan
        }

        # Brief sleep to avoid CPU spin
        if ($completedIds.Count -eq 0) {
            Start-Sleep -Milliseconds 500
        }
    }

    # Scan summary
    $scanned = @($Machines | Where-Object { $_.Status -eq "Scanned" }).Count
    $partial = @($Machines | Where-Object { $_.Status -eq "Partial" }).Count
    $scanFailed = @($Machines | Where-Object { $_.Status -eq "ScanFailed" }).Count

    Write-Host ""
    Write-Host "  Scan Summary: Scanned=$scanned  Partial=$partial  Failed=$scanFailed" -ForegroundColor Cyan
    Write-Log -Message "Scan phase complete: Scanned=$scanned Partial=$partial Failed=$scanFailed"

    return $Machines
}

# ============================================================================
# SUMMARY AND REPORT
# ============================================================================

function Export-DeploymentReport {
    <#
    .SYNOPSIS
        Prints final summary and exports JSON report.
    #>
    param(
        [array]$Machines,
        [string]$ReportDir,
        [string]$Api,
        [string]$Code
    )

    Write-Banner -Title "DEPLOYMENT SUMMARY"

    $totalCount  = $Machines.Count
    $scanned     = @($Machines | Where-Object { $_.Status -eq "Scanned" }).Count
    $partial     = @($Machines | Where-Object { $_.Status -eq "Partial" }).Count
    $offline     = @($Machines | Where-Object { $_.Status -eq "Offline" }).Count
    $denied      = @($Machines | Where-Object { $_.Status -eq "AccessDenied" }).Count
    $deployFail  = @($Machines | Where-Object { $_.Status -eq "DeployFailed" }).Count
    $scanFail    = @($Machines | Where-Object { $_.Status -eq "ScanFailed" }).Count

    Write-Host "  Total targets:     $totalCount"
    Write-Host "  Scanned (OK):      $scanned" -ForegroundColor Green
    Write-Host "  Partial (warn):    $partial" -ForegroundColor Yellow
    Write-Host "  Offline:           $offline" -ForegroundColor Yellow
    Write-Host "  Access Denied:     $denied" -ForegroundColor Yellow
    Write-Host "  Deploy Failed:     $deployFail" -ForegroundColor Red
    Write-Host "  Scan Failed:       $scanFail" -ForegroundColor Red
    Write-Host ""

    # Per-machine status table
    Write-Host ("  {0,-25} {1,-20} {2,-15}" -f "Machine", "IP/Host", "Status")
    Write-Host ("  {0,-25} {1,-20} {2,-15}" -f "-------------------------", "--------------------", "---------------")
    foreach ($m in $Machines) {
        $name = if ($m.Name.Length -gt 24) { $m.Name.Substring(0, 21) + "..." } else { $m.Name }
        $ip   = if ($m.IPAddress.Length -gt 19) { $m.IPAddress.Substring(0, 16) + "..." } else { $m.IPAddress }
        $statusColor = switch ($m.Status) {
            "Scanned"      { "Green" }
            "Partial"      { "Yellow" }
            "Offline"      { "Yellow" }
            "AccessDenied" { "Yellow" }
            default        { "Red" }
        }
        Write-Host ("  {0,-25} {1,-20} {2,-15}" -f $name, $ip, $m.Status) -ForegroundColor $statusColor
    }
    Write-Host ""

    # Build JSON report
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $reportFileName = "KryossDeployment_$timestamp.json"

    if (-not (Test-Path -Path $ReportDir)) {
        New-Item -Path $ReportDir -ItemType Directory -Force | Out-Null
    }
    $reportPath = Join-Path -Path $ReportDir -ChildPath $reportFileName

    $reportData = @{
        Timestamp       = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
        ApiUrl          = $Api
        EnrollmentCode  = $Code
        TotalTargets    = $totalCount
        Summary         = @{
            Scanned      = $scanned
            Partial      = $partial
            Offline      = $offline
            AccessDenied = $denied
            DeployFailed = $deployFail
            ScanFailed   = $scanFail
        }
        Machines        = @()
    }

    foreach ($m in $Machines) {
        $entry = @{
            Name       = $m.Name
            IPAddress  = $m.IPAddress
            Source     = $m.Source
            Status     = $m.Status
        }
        if ($m.ScanExitCode -ne $null) { $entry.ScanExitCode = $m.ScanExitCode }
        if ($m.ScanResult)             { $entry.ScanResult = $m.ScanResult }
        if ($m.ScanError)              { $entry.ScanError = $m.ScanError }
        if ($m.OS)                     { $entry.OperatingSystem = $m.OS }
        if ($m.LastLogon)              { $entry.LastLogon = $m.LastLogon.ToString("yyyy-MM-dd HH:mm:ss") }
        $reportData.Machines += $entry
    }

    try {
        $reportData | ConvertTo-Json -Depth 5 | Out-File -FilePath $reportPath -Encoding UTF8 -Force
        Write-Log -Message "Report saved: $reportPath"
        Write-Host "  [OK] Report: $reportPath" -ForegroundColor Green
    } catch {
        Write-Log -Message "Failed to write report: $_" -Level "ERROR"
        Write-Host "  [ERROR] Failed to save report: $_" -ForegroundColor Red
    }

    return $reportPath
}

# ============================================================================
# MAIN EXECUTION
# ============================================================================

$scriptStartTime = Get-Date
Write-Banner -Title "KryossAgent Network Deployment Orchestrator"
$scanMode = if ($Parallel) { "parallel (max $MaxConcurrent)" } else { "sequential" }
Write-Log -Message "Deployment started -- ScanMode=$scanMode DiscoveryMethod=$DiscoveryMethod"

# --- Validate agent binary ---
$resolvedAgentPath = Resolve-AgentPath -ExplicitPath $AgentPath
if (-not $resolvedAgentPath) {
    Write-Log -Message "Cannot proceed without KryossAgent.exe" -Level "ERROR"
    Write-Host "  [ERROR] KryossAgent.exe not found. Provide -AgentPath or build the agent first." -ForegroundColor Red
    exit 1
}

# --- Validate PsExec ---
$psExecPath = Get-PsExecPath
if (-not $psExecPath) {
    Write-Log -Message "Cannot proceed without PsExec64.exe" -Level "ERROR"
    Write-Host "  [ERROR] PsExec64.exe not found and could not be downloaded." -ForegroundColor Red
    exit 1
}

# --- Obtain credentials ---
$Credential = Get-CredentialSafe -Cred $Credential
if (-not $Credential) {
    Write-Log -Message "No credentials provided -- cannot proceed" -Level "ERROR"
    exit 1
}

# --- Phase 1: Discover ---
$machines = Invoke-DiscoveryPhase -Hosts $TargetHosts -Method $DiscoveryMethod
if (-not $machines -or $machines.Count -eq 0) {
    Write-Log -Message "No targets found -- aborting deployment" -Level "ERROR"
    Write-Host "  [ERROR] No machines discovered. Check network or AD connectivity." -ForegroundColor Red
    exit 1
}

# --- Phase 2: Deploy (always runs -- always copies binary) ---
$machines = Invoke-DeployPhase -Machines $machines -AgentExePath $resolvedAgentPath -Cred $Credential

$deployedCount = @($machines | Where-Object { $_.Status -eq "Deployed" }).Count
if ($deployedCount -eq 0) {
    Write-Log -Message "No machines were successfully deployed -- skipping scan phase" -Level "WARN"
    Write-Host "  [WARN] No machines deployed. Skipping scan phase." -ForegroundColor Yellow
} else {
    # --- Phase 3: Scan ---
    if ($Parallel) {
        $machines = Invoke-ScanPhaseParallel -Machines $machines -PsExecExe $psExecPath -Cred $Credential -Code $EnrollmentCode -Concurrent $MaxConcurrent
    } else {
        $machines = Invoke-ScanPhaseSequential -Machines $machines -PsExecExe $psExecPath -Cred $Credential -Code $EnrollmentCode
    }
}

# --- Summary and Report ---
$reportFile = Export-DeploymentReport -Machines $machines -ReportDir $OutputDir -Api "(hardcoded in binary)" -Code $EnrollmentCode

$elapsed = (Get-Date) - $scriptStartTime
Write-Host ""
Write-Log -Message "Deployment orchestrator completed in $([math]::Round($elapsed.TotalMinutes, 1)) minutes"
Write-Host "  Total elapsed: $([math]::Round($elapsed.TotalMinutes, 1)) minutes" -ForegroundColor Cyan
Write-Host ""

# Exit code based on results
$scannedCount = @($machines | Where-Object { $_.Status -eq "Scanned" }).Count
$failureCount = @($machines | Where-Object { $_.Status -in @("DeployFailed", "ScanFailed") }).Count

if ($failureCount -gt 0 -and $scannedCount -eq 0) {
    exit 1
} elseif ($failureCount -gt 0) {
    exit 2
} else {
    exit 0
}
