<#
.SYNOPSIS
    Disable Windows Fast Boot, also known as Hiberboot or Fast Startup.
.DESCRIPTION
    Disable Windows Fast Boot, also known as Hiberboot or Fast Startup.
.PARAMETER
    No parameters required.

.EXAMPLE
    No parameter needed.
    Disables Windows Fast Boot
.OUTPUTS
    None
.NOTES
    Minimum OS Architecture Supported: Windows 10, Windows Server 2016
    Release Notes:
    Initial Release
#>
[CmdletBinding()]
param ()

begin {
    $LOG_DIR  = "C:\ProgramData\TeamLogicIT\Logs"
    $LOG_FILE = Join-Path -Path $LOG_DIR -ChildPath "Disable-WindowsFastBoot_$(Get-Date -Format 'yyyyMMdd').log"

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
        $p.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
}
process {
    if (-not (Test-IsElevated)) {
        Write-Error -Message "Access Denied. Please run with Administrator privileges."
        exit 1
    }

    $Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power"
    $Name = "HiberbootEnabled"
    $Value = "0"

    # Idempotency check: skip if already disabled
    try {
        $currentValue = Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
        if ($currentValue -and $currentValue.$Name -eq 0) {
            Write-Log -Message "Fast Boot is already disabled (HiberbootEnabled = 0). No changes needed."
            exit 0
        }
    }
    catch {
        Write-Log -Message "Could not read current HiberbootEnabled value: $_" -Level "WARN"
    }

    try {
        if (-not $(Test-Path $Path)) {
            New-Item -Path $Path -Force | Out-Null
            New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType DWord -Force | Out-Null
        }
        else {
            New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType DWord -Force | Out-Null
        }
        Write-Log -Message "Fast Boot has been disabled successfully (HiberbootEnabled set to 0)."
    }
    catch {
        Write-Log -Message "Failed to disable Fast Boot: $_" -Level "ERROR"
        Write-Error $_
        exit 1
    }
    exit 0
}
end {}