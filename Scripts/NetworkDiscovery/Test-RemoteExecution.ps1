<#
.SYNOPSIS
    PsExec Remote Execution Validator - Tests which network devices can accept remote script execution

.DESCRIPTION
    Takes discovered network devices and validates which ones are Windows computers that can
    accept PsExec connections and execute scripts remotely. This identifies valid targets
    for remote security assessments.

.PARAMETER InputDevices
    Array of discovered devices (from Get-NetworkDevices.ps1 or manual input)

.PARAMETER InputFile
    JSON file containing discovered devices from network discovery

.PARAMETER Credential
    Credentials for remote access. If not provided, will prompt for credentials.

.PARAMETER TestCommand
    Command to test on remote systems (default: "hostname")

.PARAMETER Timeout
    Timeout in seconds for each PsExec test (default: 15)

.PARAMETER MaxConcurrent
    Maximum concurrent PsExec tests (default: 10)

.PARAMETER ExportResults
    Export validation results to CSV and JSON files

.PARAMETER OutputPath
    Directory to save export files (default: script directory)

.NOTES
    Author:   TeamLogic IT
    Project:  Kryoss Network Discovery & Validation
    Version:  1.0
    Created:  2026-04-03

.EXAMPLE
    .\Test-RemoteExecution.ps1 -InputFile "NetworkDiscovery_20260403_123456.json"

.EXAMPLE
    .\Test-RemoteExecution.ps1 -InputFile "devices.json" -ExportResults

.EXAMPLE
    $devices = @(@{IPAddress="192.168.1.10"; MACAddress="aa-bb-cc-dd-ee-ff"})
    .\Test-RemoteExecution.ps1 -InputDevices $devices
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false, ParameterSetName = "DeviceArray")]
    [array]$InputDevices = @(),

    [Parameter(Mandatory = $false, ParameterSetName = "InputFile")]
    [string]$InputFile = "",

    [Parameter(Mandatory = $false)]
    [System.Management.Automation.PSCredential]$Credential = $null,

    [Parameter(Mandatory = $false)]
    [string]$TestCommand = "hostname",

    [Parameter(Mandatory = $false)]
    [int]$Timeout = 15,

    [Parameter(Mandatory = $false)]
    [int]$MaxConcurrent = 10,

    [Parameter(Mandatory = $false)]
    [switch]$ExportResults,

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = $PSScriptRoot
)

# ── Logging ────────────────────────────────────────────────
$LOG_DIR  = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE = Join-Path -Path $LOG_DIR -ChildPath "Test-RemoteExecution_$(Get-Date -Format 'yyyyMMdd').log"

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

# ------------------------------------------------------------------------------------------
# PSEXEC FUNCTIONS
# ------------------------------------------------------------------------------------------

function Get-PsExecPath {
    <#
    .SYNOPSIS
        Gets or downloads PsExec executable
    #>
    Write-Host "PSEXEC SETUP: Checking for PsExec availability..." -ForegroundColor Cyan

    # Check common locations
    $possiblePaths = @(
        "${env:TEMP}\PsExec64.exe",
        "${env:SYSTEMROOT}\System32\PsExec64.exe",
        "${env:PROGRAMFILES}\Sysinternals\PsExec64.exe",
        "$PSScriptRoot\PsExec64.exe"
    )

    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            Write-Host "SUCCESS: Found PsExec at: $path" -ForegroundColor Green
            return $path
        }
    }

    # Download PsExec if not found
    $downloadPath = "${env:TEMP}\PsExec64.exe"
    Write-Host "Downloading PsExec from Microsoft Sysinternals..." -ForegroundColor Yellow

    try {
        $downloadUrl = "https://live.sysinternals.com/PsExec64.exe"
        Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath -TimeoutSec 60 -UseBasicParsing -ErrorAction Stop

        # Verify download
        $fileInfo = Get-Item $downloadPath -ErrorAction SilentlyContinue
        if ($fileInfo -and $fileInfo.Length -gt 100KB -and $fileInfo.Length -lt 1MB) {
            Write-Host "SUCCESS: PsExec downloaded successfully ($([math]::Round($fileInfo.Length/1KB, 1)) KB)" -ForegroundColor Green
            return $downloadPath
        } else {
            throw "Downloaded file appears to be invalid (size: $($fileInfo.Length) bytes)"
        }
    } catch {
        Write-Host "ERROR: Failed to download PsExec: $_" -ForegroundColor Red
        throw "PsExec is required but could not be obtained"
    }
}

function Test-RemoteDeviceAccess {
    <#
    .SYNOPSIS
        Tests PsExec connectivity to a single device
    #>
    param(
        [hashtable]$Device,
        [string]$PsExecPath,
        [System.Management.Automation.PSCredential]$Cred,
        [string]$Command,
        [int]$TimeoutSec
    )

    $ip = $Device.IPAddress
    $result = @{
        IPAddress = $ip
        MACAddress = if ($Device.MACAddress) { $Device.MACAddress } else { "Unknown" }
        OriginalHostname = if ($Device.Hostname) { $Device.Hostname } else { $ip }
        Status = "Unknown"
        AccessLevel = "None"
        ResponseTime = $null
        RemoteHostname = $null
        ErrorMessage = $null
        TestCommand = $Command
        Timestamp = Get-Date
    }

    try {
        # Build PsExec arguments
        $psExecArgs = @(
            "\\$ip",
            "-accepteula",
            "-n", "$TimeoutSec"
        )

        # Add credentials if provided
        if ($Cred) {
            $psExecArgs += @(
                "-u", $Cred.UserName,
                "-p", $Cred.GetNetworkCredential().Password
            )
        }

        # Add command
        $psExecArgs += @("cmd", "/c", $Command)

        Write-Host "  Testing $ip..." -ForegroundColor White -NoNewline

        # Execute PsExec with timeout
        $startTime = Get-Date
        $processInfo = New-Object System.Diagnostics.ProcessStartInfo
        $processInfo.FileName = $PsExecPath
        $processInfo.Arguments = $psExecArgs -join " "
        $processInfo.UseShellExecute = $false
        $processInfo.RedirectStandardOutput = $true
        $processInfo.RedirectStandardError = $true
        $processInfo.CreateNoWindow = $true

        $process = [System.Diagnostics.Process]::Start($processInfo)
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()

        # Wait for completion with timeout
        if (-not $process.WaitForExit($TimeoutSec * 1000)) {
            $process.Kill()
            $result.Status = "Timeout"
            $result.ErrorMessage = "PsExec operation timed out"
            Write-Host " [TIMEOUT]" -ForegroundColor Yellow
            return $result
        }

        $endTime = Get-Date
        $result.ResponseTime = [math]::Round(($endTime - $startTime).TotalSeconds, 2)
        $exitCode = $process.ExitCode

        # Analyze results
        if ($exitCode -eq 0 -and $stdout.Trim()) {
            # Success - got valid response
            $result.Status = "Success"
            $result.AccessLevel = "Full"
            $result.RemoteHostname = $stdout.Trim()
            Write-Host " [SUCCESS] $($result.RemoteHostname)" -ForegroundColor Green
        } elseif ($stderr -match "network path was not found" -or $stderr -match "could not be contacted") {
            # Network unreachable
            $result.Status = "Unreachable"
            $result.AccessLevel = "None"
            $result.ErrorMessage = "Network path not found"
            Write-Host " [UNREACHABLE]" -ForegroundColor Red
        } elseif ($stderr -match "Access is denied" -or $stderr -match "Logon failure") {
            # Authentication failed
            $result.Status = "AccessDenied"
            $result.AccessLevel = "Blocked"
            $result.ErrorMessage = "Authentication failed"
            Write-Host " [ACCESS DENIED]" -ForegroundColor Yellow
        } elseif ($stderr -match "The system cannot find the file specified" -or $stderr -match "not recognized") {
            # Command failed (but connection worked)
            $result.Status = "Connected"
            $result.AccessLevel = "Partial"
            $result.ErrorMessage = "Command execution failed"
            Write-Host " [CONNECTED/CMD FAILED]" -ForegroundColor Cyan
        } else {
            # Unknown error
            $result.Status = "Error"
            $result.AccessLevel = "Unknown"
            $result.ErrorMessage = if ($stderr.Trim()) { $stderr.Trim() } else { "Unknown error (exit code: $exitCode)" }
            Write-Host " [ERROR]" -ForegroundColor Red
        }

    } catch {
        $result.Status = "Exception"
        $result.AccessLevel = "None"
        $result.ErrorMessage = $_.Exception.Message
        Write-Host " [EXCEPTION]" -ForegroundColor Red
    }

    return $result
}

# ------------------------------------------------------------------------------------------
# VALIDATION FUNCTIONS
# ------------------------------------------------------------------------------------------

function Test-DeviceList {
    <#
    .SYNOPSIS
        Tests PsExec access to a list of devices
    #>
    param(
        [array]$Devices,
        [string]$PsExecPath,
        [System.Management.Automation.PSCredential]$Credential,
        [string]$TestCommand,
        [int]$Timeout,
        [int]$MaxConcurrent
    )

    Write-Host "VALIDATION: Testing PsExec connectivity to $($Devices.Count) devices..." -ForegroundColor Cyan
    Write-Host "  Test command: '$TestCommand'" -ForegroundColor Gray
    Write-Host "  Timeout: $Timeout seconds per device" -ForegroundColor Gray
    Write-Host "  Max concurrent: $MaxConcurrent" -ForegroundColor Gray
    Write-Host ""

    $results = @()
    $deviceQueue = [System.Collections.Queue]::new($Devices)
    $runningJobs = @()
    $completedCount = 0

    # Create runspace pool for parallel execution
    $runspacePool = [runspacefactory]::CreateRunspacePool(1, $MaxConcurrent)
    $runspacePool.Open()

    while ($deviceQueue.Count -gt 0 -or $runningJobs.Count -gt 0) {
        # Start new jobs if we have capacity and devices to test
        while ($runningJobs.Count -lt $MaxConcurrent -and $deviceQueue.Count -gt 0) {
            $device = $deviceQueue.Dequeue()

            $scriptBlock = {
                param($Device, $PsExecPath, $Credential, $Command, $TimeoutSec)

                # Import the test function (simplified version)
                $testResult = @{
                    IPAddress = $Device.IPAddress
                    MACAddress = if ($Device.MACAddress) { $Device.MACAddress } else { "Unknown" }
                    OriginalHostname = if ($Device.Hostname) { $Device.Hostname } else { $Device.IPAddress }
                    Status = "Unknown"
                    AccessLevel = "None"
                    ResponseTime = $null
                    RemoteHostname = $null
                    ErrorMessage = $null
                    TestCommand = $Command
                    Timestamp = Get-Date
                }

                try {
                    $ip = $Device.IPAddress
                    $psExecArgs = @("\\$ip", "-accepteula", "-n", "$TimeoutSec")

                    if ($Credential) {
                        $psExecArgs += @("-u", $Credential.UserName, "-p", $Credential.GetNetworkCredential().Password)
                    }

                    $psExecArgs += @("cmd", "/c", $Command)

                    $startTime = Get-Date
                    $output = & $PsExecPath @psExecArgs 2>&1
                    $endTime = Get-Date

                    $testResult.ResponseTime = [math]::Round(($endTime - $startTime).TotalSeconds, 2)

                    if ($LASTEXITCODE -eq 0 -and $output -and $output -notmatch "error|failed|denied") {
                        $testResult.Status = "Success"
                        $testResult.AccessLevel = "Full"
                        $testResult.RemoteHostname = ($output | Where-Object { $_ -match '^[A-Za-z0-9\-]+$' } | Select-Object -First 1)
                        if (-not $testResult.RemoteHostname) {
                            $testResult.RemoteHostname = $output | Select-Object -First 1
                        }
                    } else {
                        $errorText = ($output | Out-String).Trim()
                        if ($errorText -match "network path was not found|could not be contacted") {
                            $testResult.Status = "Unreachable"
                        } elseif ($errorText -match "Access is denied|Logon failure") {
                            $testResult.Status = "AccessDenied"
                            $testResult.AccessLevel = "Blocked"
                        } else {
                            $testResult.Status = "Error"
                            $testResult.ErrorMessage = $errorText
                        }
                    }
                } catch {
                    $testResult.Status = "Exception"
                    $testResult.ErrorMessage = $_.Exception.Message
                }

                return $testResult
            }

            $powerShell = [powershell]::Create()
            $powerShell.RunspacePool = $runspacePool
            $powerShell.AddScript($scriptBlock)
            $powerShell.AddArgument($device)
            $powerShell.AddArgument($PsExecPath)
            $powerShell.AddArgument($Credential)
            $powerShell.AddArgument($TestCommand)
            $powerShell.AddArgument($Timeout)

            $asyncResult = $powerShell.BeginInvoke()

            $runningJobs += @{
                PowerShell = $powerShell
                AsyncResult = $asyncResult
                Device = $device
                StartTime = Get-Date
            }
        }

        # Check for completed jobs
        $completedJobs = @()
        foreach ($job in $runningJobs) {
            if ($job.AsyncResult.IsCompleted) {
                try {
                    $result = $job.PowerShell.EndInvoke($job.AsyncResult)
                    $results += $result
                    $completedCount++

                    # Display result
                    switch ($result.Status) {
                        "Success" {
                            Write-Host "  [$completedCount/$($Devices.Count)] $($result.IPAddress) -> SUCCESS: $($result.RemoteHostname)" -ForegroundColor Green
                        }
                        "AccessDenied" {
                            Write-Host "  [$completedCount/$($Devices.Count)] $($result.IPAddress) -> ACCESS DENIED" -ForegroundColor Yellow
                        }
                        "Unreachable" {
                            Write-Host "  [$completedCount/$($Devices.Count)] $($result.IPAddress) -> UNREACHABLE" -ForegroundColor Red
                        }
                        default {
                            Write-Host "  [$completedCount/$($Devices.Count)] $($result.IPAddress) -> $($result.Status)" -ForegroundColor Gray
                        }
                    }
                } catch {
                    Write-Host "  [$completedCount/$($Devices.Count)] $($job.Device.IPAddress) -> EXCEPTION: $_" -ForegroundColor Red
                }

                $job.PowerShell.Dispose()
                $completedJobs += $job
            }
        }

        # Remove completed jobs
        foreach ($job in $completedJobs) {
            $runningJobs = $runningJobs | Where-Object { $_ -ne $job }
        }

        # Brief pause to prevent excessive CPU usage
        if ($runningJobs.Count -gt 0) {
            Start-Sleep -Milliseconds 200
        }
    }

    $runspacePool.Close()
    $runspacePool.Dispose()

    return $results
}

# ------------------------------------------------------------------------------------------
# INPUT/OUTPUT FUNCTIONS
# ------------------------------------------------------------------------------------------

function Import-DevicesFromFile {
    <#
    .SYNOPSIS
        Imports device list from JSON file
    #>
    param([string]$FilePath)

    Write-Host "INPUT: Loading devices from file: $FilePath" -ForegroundColor Cyan

    if (-not (Test-Path $FilePath)) {
        throw "Input file not found: $FilePath"
    }

    try {
        $jsonContent = Get-Content $FilePath -Raw | ConvertFrom-Json

        # Handle different JSON structures
        $devices = @()
        if ($jsonContent.Devices) {
            # Network discovery format
            $devices = $jsonContent.Devices
        } elseif ($jsonContent -is [array]) {
            # Direct device array
            $devices = $jsonContent
        } else {
            # Single device or unknown format
            $devices = @($jsonContent)
        }

        Write-Host "SUCCESS: Loaded $($devices.Count) devices from file" -ForegroundColor Green
        return $devices

    } catch {
        throw "Failed to parse JSON file: $_"
    }
}

function Export-ValidationResults {
    <#
    .SYNOPSIS
        Exports validation results to CSV and JSON
    #>
    param([array]$Results, [string]$OutputPath)

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $baseFileName = "RemoteValidation_$timestamp"

    # Export to CSV
    $csvPath = Join-Path $OutputPath "$baseFileName.csv"
    $Results | Select-Object IPAddress, OriginalHostname, RemoteHostname, Status, AccessLevel, ResponseTime, ErrorMessage, Timestamp |
        Export-Csv $csvPath -NoTypeInformation

    # Export to JSON
    $jsonData = @{
        ValidationTimestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        TotalDevicesTested = $Results.Count
        Statistics = @{
            SuccessfulConnections = ($Results | Where-Object { $_.Status -eq "Success" }).Count
            AccessDenied = ($Results | Where-Object { $_.Status -eq "AccessDenied" }).Count
            Unreachable = ($Results | Where-Object { $_.Status -eq "Unreachable" }).Count
            Errors = ($Results | Where-Object { $_.Status -eq "Error" }).Count
        }
        Results = $Results
    }

    $jsonPath = Join-Path $OutputPath "$baseFileName.json"
    $jsonData | ConvertTo-Json -Depth 5 | Out-File $jsonPath -Encoding UTF8

    Write-Host "EXPORT: Validation results exported to:" -ForegroundColor Green
    Write-Host "  CSV: $csvPath" -ForegroundColor White
    Write-Host "  JSON: $jsonPath" -ForegroundColor White
}

# ------------------------------------------------------------------------------------------
# MAIN EXECUTION
# ------------------------------------------------------------------------------------------

Write-Log -Message "Test-RemoteExecution v1.0 starting"

Write-Host "============================================" -ForegroundColor Green
Write-Host "   Remote Execution Validator - TeamLogic IT" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""

try {
    # 1. Get device list
    $devices = @()

    if ($InputFile) {
        $devices = Import-DevicesFromFile -FilePath $InputFile
    } elseif ($InputDevices.Count -gt 0) {
        $devices = $InputDevices
        Write-Host "INPUT: Using provided device array ($($devices.Count) devices)" -ForegroundColor Cyan
    } else {
        Write-Host "ERROR: No input devices specified. Use -InputFile or -InputDevices parameter." -ForegroundColor Red
        Write-Host ""
        Write-Host "Examples:" -ForegroundColor Yellow
        Write-Host "  .\Test-RemoteExecution.ps1 -InputFile 'NetworkDiscovery_20260403_123456.json'" -ForegroundColor Gray
        Write-Host "  .\Test-RemoteExecution.ps1 -InputDevices @(@{IPAddress='192.168.1.10'})" -ForegroundColor Gray
        exit 1
    }

    Write-Host ""

    # 2. Get credentials if not provided
    if (-not $Credential) {
        Write-Host "CREDENTIALS: Remote execution requires administrator credentials" -ForegroundColor Cyan
        Write-Host "  These will be used to test PsExec connectivity to discovered devices" -ForegroundColor Gray
        Write-Host ""

        $username = Read-Host "Username (e.g., Administrator or DOMAIN\username)"
        $password = Read-Host "Password" -AsSecureString

        if ($username -and $password) {
            $Credential = New-Object System.Management.Automation.PSCredential ($username, $password)
            Write-Host "SUCCESS: Credentials configured for user: $username" -ForegroundColor Green
        } else {
            Write-Host "WARNING: No credentials provided - will test anonymous access only" -ForegroundColor Yellow
        }
    } else {
        Write-Host "CREDENTIALS: Using provided credentials for user: $($Credential.UserName)" -ForegroundColor Green
    }

    Write-Host ""

    # 3. Get PsExec
    $psExecPath = Get-PsExecPath
    Write-Host ""

    # 4. Test devices
    $validationResults = Test-DeviceList -Devices $devices -PsExecPath $psExecPath -Credential $Credential -TestCommand $TestCommand -Timeout $Timeout -MaxConcurrent $MaxConcurrent

    # 5. Display results summary
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "             VALIDATION RESULTS" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""

    # Statistics
    $successful = $validationResults | Where-Object { $_.Status -eq "Success" }
    $accessDenied = $validationResults | Where-Object { $_.Status -eq "AccessDenied" }
    $unreachable = $validationResults | Where-Object { $_.Status -eq "Unreachable" }
    $errors = $validationResults | Where-Object { $_.Status -notin @("Success", "AccessDenied", "Unreachable") }

    Write-Host "SUMMARY: Remote execution validation completed" -ForegroundColor Green
    Write-Host "  Total devices tested: $($validationResults.Count)" -ForegroundColor White
    Write-Host "  Successful connections: $($successful.Count)" -ForegroundColor Green
    Write-Host "  Access denied: $($accessDenied.Count)" -ForegroundColor Yellow
    Write-Host "  Unreachable devices: $($unreachable.Count)" -ForegroundColor Red
    Write-Host "  Other errors: $($errors.Count)" -ForegroundColor Gray
    Write-Host ""

    # Successful devices details
    if ($successful.Count -gt 0) {
        Write-Host "WINDOWS COMPUTERS READY FOR REMOTE EXECUTION:" -ForegroundColor Green
        Write-Host "IP Address       Hostname                 Response Time" -ForegroundColor Yellow
        Write-Host "----------       --------                 -------------" -ForegroundColor Yellow

        foreach ($device in $successful | Sort-Object IPAddress) {
            $ip = $device.IPAddress.PadRight(16)
            $hostname = if ($device.RemoteHostname) { $device.RemoteHostname.PadRight(24) } else { "Unknown".PadRight(24) }
            $responseTime = if ($device.ResponseTime) { "$($device.ResponseTime)s" } else { "N/A" }

            Write-Host "$ip $hostname $responseTime" -ForegroundColor White
        }

        Write-Host ""
        Write-Host "SUCCESS: These $($successful.Count) devices can run remote security assessments!" -ForegroundColor Green
    } else {
        Write-Host "WARNING: No devices found that can accept remote execution!" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Common reasons:" -ForegroundColor Gray
        Write-Host "  • Devices are not Windows computers" -ForegroundColor Gray
        Write-Host "  • Windows Firewall is blocking remote access" -ForegroundColor Gray
        Write-Host "  • Incorrect credentials provided" -ForegroundColor Gray
        Write-Host "  • UAC or security policies prevent remote execution" -ForegroundColor Gray
        Write-Host "  • PsExec service is not allowed on target systems" -ForegroundColor Gray
    }

    # Problem devices summary
    if ($accessDenied.Count -gt 0) {
        Write-Host ""
        Write-Host "ACCESS DENIED DEVICES ($($accessDenied.Count)):" -ForegroundColor Yellow
        foreach ($device in $accessDenied | Sort-Object IPAddress | Select-Object -First 5) {
            Write-Host "  $($device.IPAddress) - Authentication failed" -ForegroundColor Yellow
        }
        if ($accessDenied.Count -gt 5) {
            Write-Host "  ... and $($accessDenied.Count - 5) more" -ForegroundColor Yellow
        }
    }

    if ($unreachable.Count -gt 0) {
        Write-Host ""
        Write-Host "UNREACHABLE DEVICES ($($unreachable.Count)):" -ForegroundColor Red
        foreach ($device in $unreachable | Sort-Object IPAddress | Select-Object -First 5) {
            Write-Host "  $($device.IPAddress) - Network path not found" -ForegroundColor Red
        }
        if ($unreachable.Count -gt 5) {
            Write-Host "  ... and $($unreachable.Count - 5) more" -ForegroundColor Red
        }
    }

    # 6. Export results if requested
    if ($ExportResults) {
        Write-Host ""
        if (-not $OutputPath -or $OutputPath -eq "") {
            $OutputPath = $PSScriptRoot
            if (-not $OutputPath) {
                $OutputPath = Get-Location
            }
        }
        Export-ValidationResults -Results $validationResults -OutputPath $OutputPath
    }

} catch {
    Write-Log -Message "FATAL ERROR: Remote execution validation failed - $_" -Level "ERROR"
    Write-Host ""
    Write-Host "FATAL ERROR: Remote execution validation failed" -ForegroundColor Red
    Write-Host "Error details: $_" -ForegroundColor Red
    exit 1
}

Write-Log -Message "Test-RemoteExecution completed successfully"

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "Remote execution validation completed!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green

exit 0