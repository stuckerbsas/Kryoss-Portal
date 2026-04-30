#Requires -Version 5.1

<#
.SYNOPSIS
    Set the Inactivity(Lock Computer) timeout time if it already isn't set.
.DESCRIPTION
    Set the Inactivity(Lock Computer) timeout time if it already isn't set.
    Can be set regardless if the -Force parameter is used.
.EXAMPLE
     -Minutes 5
    This set the Inactivity(Lock Computer) timeout to 5 minutes, does not change if already set.
.EXAMPLE
     -Minutes 5 -Force
    This set the Inactivity(Lock Computer) timeout to 5 minutes, and forces the change if already set.
.EXAMPLE
    PS C:\> Set-IdleLock.ps1 -Minutes 5
    This set the Inactivity(Lock Computer) timeout to 5 minutes
.OUTPUTS
    None
.NOTES
    Minimum OS Architecture Supported: Windows 10, Windows Server 2016
    Release Notes:
    Initial Release
.COMPONENT
    LocalUserAccountManagement
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 9999)]
    [int]
    $Minutes,
    [switch]
    $Force
)
begin {
    $LOG_DIR  = "C:\ProgramData\TeamLogicIT\Logs"
    $LOG_FILE = Join-Path -Path $LOG_DIR -ChildPath "Set-IdleLock_$(Get-Date -Format 'yyyyMMdd').log"

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

    function Test-IsElevated {
        $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $p = New-Object System.Security.Principal.WindowsPrincipal($id)
        if ($p.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator))
        { Write-Output $true }
        else
        { Write-Output $false }
    }
    function Set-ItemProp {
        param (
            $Path,
            $Name,
            $Value,
            [ValidateSet("DWord", "QWord", "String", "ExpandedString", "Binary", "MultiString", "Unknown")]
            $PropertyType = "DWord"
        )
        New-Item -Path $Path -Force -ErrorAction SilentlyContinue | Out-Null
        if ((Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue)) {
            Set-ItemProperty -Path $Path -Name $Name -Value $Value -Force -Confirm:$false | Out-Null
        }
        else {
            New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType $PropertyType -Force -Confirm:$false | Out-Null
        }
    }
}
process {
    if (-not (Test-IsElevated)) {
        Write-Error -Message "Access Denied. Please run with Administrator privileges."
        exit 1
    }
    
    Write-Log -Message "Starting Set-IdleLock with Minutes=$Minutes, Force=$Force"

    $Path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
    $IdleName = "InactivityTimeoutSecs"
    $Seconds = $Minutes * 60
    # Override "Check if already set"
    if (-not $Force) {
        # Check if already set
        if ($(Get-ItemProperty -Path $Path | Select-Object -Property $IdleName -ExpandProperty $IdleName -ErrorAction SilentlyContinue)) {
            $CurrentIdleSeconds = $(Get-ItemPropertyValue -Path $Path -Name $IdleName)
            # If value already set, do nothing.
            if ($CurrentIdleSeconds) {
                Write-Log -Message "Inactivity timeout already set to $($CurrentIdleSeconds / 60) minutes. No changes made."
                exit 0
            }
        }
        Write-Log -Message "Inactivity timeout not currently set. Proceeding to configure."
    }
    else {
        Write-Log -Message "Force flag specified. Will set timeout regardless of current value."
    }

    # Sets InactivityTimeoutSecs to $Minutes
    try {
        $ErrorActionPreference = 'Stop'
        Set-ItemProp -Path $Path -Name $IdleName -Value $Seconds
        Write-Host "Set the Inactivity to $($Seconds/60) minutes."
        Write-Log -Message "Set the Inactivity timeout to $($Seconds/60) minutes successfully."
        Ninja-Property-Set idleTimeSet "Set the Inactivity to $($Seconds/60) minutes."
    }
    catch {
        Write-Log -Message "Failed to set Inactivity timeout: $_" -Level "ERROR"
        Write-Error $_
        exit 1
    }
}
end {}