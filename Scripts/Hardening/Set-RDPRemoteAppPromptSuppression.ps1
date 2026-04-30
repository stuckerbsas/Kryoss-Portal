<#
.SYNOPSIS
    Suppresses the RemoteApp security prompt introduced by KB5082142.

.DESCRIPTION
    Microsoft Windows update KB5082142 introduced enhanced security protections for
    Remote Desktop (.rdp) files. This causes a new security warning dialog when
    launching RemoteApp applications, prompting users to verify the publisher and
    confirm connection settings (Drives, Clipboard, Printers).

    In some environments the "Remember my choices" checkbox is not shown, causing
    the prompt to appear on every launch. This script applies the Microsoft-recommended
    registry policy to suppress the redirection warning dialog.

    Registry key applied:
      HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services\Client
      RedirectionWarningDialogVersion = 1 (DWORD)

    Reference: Microsoft Learn — KB5082142 RDP security enhancements.
    Source KB: Attivo Cloud support article.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  2026-04-29
    Modified: 2026-04-29

.EXAMPLE
    .\Set-RDPRemoteAppPromptSuppression.ps1
#>

[CmdletBinding()]
param()

# ── Constants ──
$LOG_DIR          = "C:\ProgramData\TeamLogicIT\Logs"
$LOG_FILE         = "Set-RDPRemoteAppPromptSuppression_$(Get-Date -Format 'yyyyMMdd').log"
$REGISTRY_PATH    = "HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services\Client"
$REGISTRY_NAME    = "RedirectionWarningDialogVersion"
$REGISTRY_VALUE   = 1

$logPath = Join-Path -Path $LOG_DIR -ChildPath $LOG_FILE

# ── Logging ──
function Write-Log {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet("INFO","WARN","ERROR")][string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$timestamp] [$Level] $Message"
    Write-Host $entry
    Add-Content -Path $logPath -Value $entry
}

# ── Main ──
try {
    # Ensure log directory exists
    if (-not (Test-Path -Path $LOG_DIR)) {
        New-Item -Path $LOG_DIR -ItemType Directory -Force | Out-Null
    }

    Write-Log "Starting RemoteApp prompt suppression (KB5082142 mitigation)"

    # Check if registry path exists, create if missing
    if (-not (Test-Path -Path $REGISTRY_PATH)) {
        Write-Log "Registry path does not exist, creating: $REGISTRY_PATH"
        New-Item -Path $REGISTRY_PATH -Force | Out-Null
        Write-Log "Registry path created"
    }

    # Check current value
    $currentValue = $null
    try {
        $currentValue = Get-ItemProperty -Path $REGISTRY_PATH -Name $REGISTRY_NAME -ErrorAction Stop
        $currentValue = $currentValue.$REGISTRY_NAME
    }
    catch {
        $currentValue = $null
    }

    if ($currentValue -eq $REGISTRY_VALUE) {
        Write-Log "Registry value already set to $REGISTRY_VALUE — no changes needed"
    }
    else {
        $previousDisplay = if ($null -eq $currentValue) { "(not set)" } else { $currentValue }
        Write-Log "Setting $REGISTRY_NAME: $previousDisplay -> $REGISTRY_VALUE"

        Set-ItemProperty -Path $REGISTRY_PATH -Name $REGISTRY_NAME -Value $REGISTRY_VALUE -Type DWord -Force
        Write-Log "Registry value applied successfully"
    }

    # Force group policy update to propagate the change
    Write-Log "Running gpupdate /force to propagate policy"
    $gpResult = & gpupdate /force 2>&1
    Write-Log "gpupdate output: $($gpResult -join ' | ')"

    Write-Log "RemoteApp prompt suppression policy applied successfully"
    Write-Log "=== POLICY SUMMARY ==="
    Write-Log "  Key:   $REGISTRY_PATH"
    Write-Log "  Value: $REGISTRY_NAME = $REGISTRY_VALUE (DWORD)"
    Write-Log "  Effect: RDP redirection warning dialog suppressed for RemoteApp connections"
    Write-Log "  Source: Microsoft KB5082142 mitigation"
    Write-Log "=== END ==="

    exit 0
}
catch {
    Write-Log "Fatal error: $($_.Exception.Message)" -Level "ERROR"
    Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level "ERROR"
    exit 1
}
