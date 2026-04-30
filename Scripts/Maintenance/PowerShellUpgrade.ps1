<#
.SYNOPSIS
    Upgrades PowerShell to the latest stable version.

.DESCRIPTION
    Downloads and installs the latest stable PowerShell release from GitHub.
    Detects the current installed version, skips installation if already up to date,
    and logs all activity. Designed for silent deployment via NinjaRMM (SYSTEM account).

.PARAMETER ForceReinstall
    Forces reinstallation even if the latest version is already installed.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-03-31
    Modified: 2026-03-31

.EXAMPLE
    .\PowerShellUpgrade.ps1

.EXAMPLE
    .\PowerShellUpgrade.ps1 -ForceReinstall
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$ForceReinstall
)

#region --- Constants ---
$LOG_DIR     = "C:\ProgramData\TeamLogicIT\Logs"
$SCRIPT_NAME = "PowerShellUpgrade"
$LOG_FILE    = "$LOG_DIR\${SCRIPT_NAME}_$(Get-Date -Format 'yyyyMMdd').log"
$GITHUB_API  = "https://api.github.com/repos/PowerShell/PowerShell/releases/latest"
$TEMP_DIR    = $env:TEMP
#endregion

#region --- Logging ---
function Write-Log {
    param(
        [string]$Message,
        [ValidateSet("INFO","WARN","ERROR")]
        [string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$timestamp] [$Level] $Message"
    Write-Host $entry
    Add-Content -Path $LOG_FILE -Value $entry -ErrorAction SilentlyContinue
}
#endregion

#region --- Helpers ---
function Get-InstalledPowerShellVersion {
    # pwsh.exe is PowerShell 7+; windows PowerShell 5.1 is always present
    $pwsh = Get-Command -Name pwsh -ErrorAction SilentlyContinue
    if ($pwsh) {
        try {
            $ver = & pwsh -NoProfile -NonInteractive -Command '$PSVersionTable.PSVersion.ToString()' 2>$null
            return [version]$ver.Trim()
        } catch {
            return $null
        }
    }
    return $null
}

function Get-LatestGitHubRelease {
    Write-Log "Querying GitHub API for latest PowerShell release..."
    try {
        $response = Invoke-RestMethod -Uri $GITHUB_API -UseBasicParsing -TimeoutSec 30 `
            -Headers @{ 'User-Agent' = 'TeamLogicIT-Kryoss/1.0' }
        return $response
    } catch {
        Write-Log "Failed to query GitHub API: $_" -Level ERROR
        return $null
    }
}

function Get-MsiAssetUrl {
    param($Release)
    # Target x64 MSI for Windows
    $asset = $Release.assets | Where-Object {
        $_.name -match 'win-x64\.msi$'
    } | Select-Object -First 1

    if (-not $asset) {
        # Fallback: any Windows MSI
        $asset = $Release.assets | Where-Object {
            $_.name -match '\.msi$' -and $_.name -match 'win'
        } | Select-Object -First 1
    }

    return $asset
}

function Install-PowerShellMsi {
    param(
        [string]$DownloadUrl,
        [string]$FileName
    )
    $msiPath = Join-Path -Path $TEMP_DIR -ChildPath $FileName

    Write-Log "Downloading: $FileName"
    try {
        $webClient = New-Object System.Net.WebClient
        $webClient.DownloadFile($DownloadUrl, $msiPath)
        $webClient.Dispose()
    } catch {
        Write-Log "Download failed: $_" -Level ERROR
        return $false
    }

    if (-not (Test-Path -Path $msiPath)) {
        Write-Log "Downloaded file not found at: $msiPath" -Level ERROR
        return $false
    }

    Write-Log "Installing PowerShell MSI silently..."
    $msiArgs = @(
        "/i", "`"$msiPath`"",
        "/qn",
        "/norestart",
        "ADD_EXPLORER_CONTEXT_MENU_OPENPOWERSHELL=1",
        "ENABLE_PSREMOTING=1",
        "REGISTER_MANIFEST=1",
        "/l*v", "`"$LOG_DIR\${SCRIPT_NAME}_msi_$(Get-Date -Format 'yyyyMMdd').log`""
    )

    try {
        $proc = Start-Process -FilePath "msiexec.exe" -ArgumentList $msiArgs -Wait -PassThru
        if ($proc.ExitCode -eq 0 -or $proc.ExitCode -eq 3010) {
            Write-Log "MSI installer exited with code $($proc.ExitCode) (success$(if ($proc.ExitCode -eq 3010) {', reboot may be needed'}))"
            return $true
        } else {
            Write-Log "MSI installer failed with exit code: $($proc.ExitCode)" -Level ERROR
            return $false
        }
    } catch {
        Write-Log "Failed to launch MSI installer: $_" -Level ERROR
        return $false
    } finally {
        Remove-Item -Path $msiPath -Force -ErrorAction SilentlyContinue
    }
}
#endregion

#region --- Main ---
# Ensure log directory exists
if (-not (Test-Path -Path $LOG_DIR)) {
    New-Item -ItemType Directory -Path $LOG_DIR -Force | Out-Null
}

Write-Log "=== PowerShell Upgrade Script Started ==="
Write-Log "Running as: $([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)"

# Check current version
$installedVersion = Get-InstalledPowerShellVersion
if ($installedVersion) {
    Write-Log "Currently installed PowerShell (pwsh): $installedVersion"
} else {
    Write-Log "PowerShell 7+ (pwsh) not detected - will perform fresh install"
}

# Get latest release info
$release = Get-LatestGitHubRelease
if (-not $release) {
    Write-Log "Cannot determine latest version. Aborting." -Level ERROR
    exit 1
}

# Parse latest version (strip leading 'v')
$latestVersionStr = $release.tag_name -replace '^v', ''
try {
    $latestVersion = [version]$latestVersionStr
} catch {
    Write-Log "Could not parse latest version string: '$latestVersionStr'" -Level ERROR
    exit 1
}

Write-Log "Latest available PowerShell version: $latestVersion"

# Skip if already up to date
if ($installedVersion -and ($installedVersion -ge $latestVersion) -and (-not $ForceReinstall)) {
    Write-Log "PowerShell is already at the latest version ($installedVersion). No action needed."
    exit 0
}

if ($ForceReinstall) {
    Write-Log "ForceReinstall flag set - proceeding regardless of current version."
}

# Locate MSI asset
$asset = Get-MsiAssetUrl -Release $release
if (-not $asset) {
    Write-Log "No Windows x64 MSI asset found in release '$($release.tag_name)'." -Level ERROR
    exit 1
}

Write-Log "Selected asset: $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB)"

# Download and install
if ($PSCmdlet.ShouldProcess("PowerShell $latestVersion", "Install")) {
    $success = Install-PowerShellMsi -DownloadUrl $asset.browser_download_url -FileName $asset.name
    if ($success) {
        Write-Log "PowerShell upgrade to $latestVersion completed successfully."
        exit 0
    } else {
        Write-Log "PowerShell upgrade failed. Review MSI log for details." -Level ERROR
        exit 1
    }
} else {
    Write-Log "WhatIf mode - no changes made."
    exit 0
}
#endregion
