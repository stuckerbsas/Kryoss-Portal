<#
.SYNOPSIS
    Clears KryossAgent enrollment from local and remote machines.

.DESCRIPTION
    Removes HKLM\SOFTWARE\Kryoss\Agent registry key and optionally
    deletes the agent binary and offline results from each machine.
    After running this, machines will re-enroll on next agent execution.

.PARAMETER ComputerName
    Explicit list of hostnames. If omitted, discovers via AD.

.PARAMETER IncludeLocal
    Also clear the local machine (default: true).

.PARAMETER RemoveAgent
    Also delete KryossAgent.exe and PendingResults from each machine.

.PARAMETER Credential
    PSCredential for remote access. Prompted if not supplied.

.EXAMPLE
    .\Clear-KryossEnrollment.ps1
    # Clears all AD machines + local

.EXAMPLE
    .\Clear-KryossEnrollment.ps1 -ComputerName DC2,SERVER1 -RemoveAgent
    # Clears specific machines and removes agent binary
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string[]]$ComputerName = @(),

    [Parameter(Mandatory = $false)]
    [switch]$IncludeLocal = $true,

    [Parameter(Mandatory = $false)]
    [switch]$RemoveAgent,

    [Parameter(Mandatory = $false)]
    [System.Management.Automation.PSCredential]$Credential
)

$regPath = "HKLM:\SOFTWARE\Kryoss\Agent"
$agentDir = "C:\ProgramData\Kryoss"

# ── Build target list ──
$targets = @()

if ($ComputerName.Count -gt 0) {
    $targets = $ComputerName
} else {
    Write-Host "Discovering machines from Active Directory..." -ForegroundColor Cyan
    try {
        Import-Module ActiveDirectory -ErrorAction Stop
        $adComputers = Get-ADComputer -Filter {
            OperatingSystem -like "Windows*" -and Enabled -eq $true
        } -Properties LastLogonDate |
            Where-Object { $_.LastLogonDate -and $_.LastLogonDate -ge (Get-Date).AddDays(-30) }
        $targets = $adComputers | Select-Object -ExpandProperty Name
        Write-Host "  Found $($targets.Count) machines in AD" -ForegroundColor Green
    } catch {
        Write-Host "  AD discovery failed: $_" -ForegroundColor Yellow
        Write-Host "  Provide -ComputerName explicitly" -ForegroundColor Yellow
        exit 1
    }
}

# ── Clear local ──
if ($IncludeLocal) {
    $localName = $env:COMPUTERNAME
    Write-Host ""
    Write-Host "[$localName] (local)" -ForegroundColor Cyan
    try {
        if (Test-Path $regPath) {
            Remove-Item $regPath -Recurse -Force
            Write-Host "  [OK] Registry cleared" -ForegroundColor Green
        } else {
            Write-Host "  [SKIP] No enrollment found" -ForegroundColor Gray
        }
        if ($RemoveAgent -and (Test-Path $agentDir)) {
            Remove-Item $agentDir -Recurse -Force
            Write-Host "  [OK] Agent directory removed" -ForegroundColor Green
        }
    } catch {
        Write-Host "  [ERROR] $_" -ForegroundColor Red
    }
}

# ── Clear remote ──
$scriptBlock = {
    param([bool]$CleanAgent)
    $regPath = "HKLM:\SOFTWARE\Kryoss\Agent"
    $agentDir = "C:\ProgramData\Kryoss"
    $result = @{ Registry = $false; Agent = $false; Error = $null }

    try {
        if (Test-Path $regPath) {
            Remove-Item $regPath -Recurse -Force
            $result.Registry = $true
        }
        if ($CleanAgent -and (Test-Path $agentDir)) {
            Remove-Item $agentDir -Recurse -Force
            $result.Agent = $true
        }
    } catch {
        $result.Error = $_.Exception.Message
    }
    return $result
}

$cleared = 0
$skipped = 0
$failed = 0

Write-Host ""
Write-Host "Clearing enrollment on $($targets.Count) remote machine(s)..." -ForegroundColor Cyan
Write-Host ""

foreach ($pc in $targets) {
    if ($pc -eq $env:COMPUTERNAME) { continue } # already did local

    Write-Host "[$pc] " -NoNewline
    try {
        $invokeParams = @{
            ComputerName = $pc
            ScriptBlock  = $scriptBlock
            ArgumentList = @($RemoveAgent.IsPresent)
            ErrorAction  = 'Stop'
        }
        if ($Credential) { $invokeParams.Credential = $Credential }

        $result = Invoke-Command @invokeParams

        if ($result.Error) {
            Write-Host "ERROR: $($result.Error)" -ForegroundColor Red
            $failed++
        } elseif ($result.Registry) {
            Write-Host "Cleared" -ForegroundColor Green
            $cleared++
        } else {
            Write-Host "No enrollment" -ForegroundColor Gray
            $skipped++
        }
    } catch {
        Write-Host "Unreachable" -ForegroundColor Yellow
        $skipped++
    }
}

# ── Summary ──
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Cleared:      $cleared" -ForegroundColor Green
Write-Host "  No enrollment: $skipped" -ForegroundColor Gray
Write-Host "  Failed:       $failed" -ForegroundColor $(if ($failed -gt 0) { 'Red' } else { 'Gray' })
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next: run Invoke-KryossDeployment.ps1 with the correct enrollment code." -ForegroundColor Yellow
