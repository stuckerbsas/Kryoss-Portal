<#
.SYNOPSIS
    Registers the Kryoss M365 Security Scanner App in the customer's Entra ID tenant
    with all 21 required Graph API permissions (Application, Read-Only).

.DESCRIPTION
    Creates an App Registration with:
    - 21 required Graph permissions (Application, Read-Only) across 4 tiers
    - A self-signed certificate (more secure than client secret)
    - Output of a reusable JSON configuration file for the Kryoss portal

    REQUIREMENTS:
    - PowerShell 7+ or Windows PowerShell 5.1
    - Microsoft.Graph SDK installed (auto-installs if missing)
    - User must be Global Administrator or Application Administrator

    WORKFLOW:
    1. Run this script -> creates App + certificate + config.json
    2. Admin approves permissions (automatic with -AutoGrantConsent, or manual via URL)
    3. Enter TenantId + ClientId + CertThumbprint in Kryoss Portal M365 tab

.PARAMETER AppName
    App Registration name. Default: "Kryoss-M365-SecurityScanner"

.PARAMETER CertificateValidityYears
    Validity years for the self-signed certificate. Default: 2.

.PARAMETER OutputPath
    Folder where config.json and the exported certificate are saved. Default: .\

.PARAMETER AutoGrantConsent
    Switch. If specified, attempts automatic admin consent via Graph (requires Global Admin).

.EXAMPLE
    # Basic — manual consent later
    .\Register-KryossM365App.ps1

    # With auto-consent (requires Global Admin)
    .\Register-KryossM365App.ps1 -AutoGrantConsent

    # Custom name
    .\Register-KryossM365App.ps1 -AppName "Kryoss-Contoso" -AutoGrantConsent
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$AppName = "Kryoss-M365-SecurityScanner",

    [Parameter(Mandatory = $false)]
    [int]$CertificateValidityYears = 2,

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = ".\",

    [Parameter(Mandatory = $false)]
    [switch]$AutoGrantConsent
)

$ErrorActionPreference = "Stop"
$OutputPath = (Resolve-Path $OutputPath).Path

# ── Brand ──
$brand = @{
    Primary = "#008852"
    Name    = "TeamLogic IT"
    Product = "Kryoss Security Platform"
}

#region ── Modules ─────────────────────────────────────────────────────────

$requiredModules = @(
    "Microsoft.Graph.Applications",
    "Microsoft.Graph.Authentication",
    "Microsoft.Graph.Identity.SignIns"
)

foreach ($mod in $requiredModules) {
    if (-not (Get-Module -ListAvailable -Name $mod)) {
        Write-Host "  Installing $mod..." -ForegroundColor Cyan
        Install-Module $mod -Scope CurrentUser -Force -ErrorAction Stop
    }
    Import-Module $mod -ErrorAction Stop
}

#endregion

#region ── Banner ──────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host "    $($brand.Product) — M365 Security Scanner Setup" -ForegroundColor Green
Write-Host "    $($brand.Name)" -ForegroundColor Green
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host ""

#endregion

#region ── Connection ──────────────────────────────────────────────────────

Write-Host "  Connecting to Microsoft Graph..." -ForegroundColor Cyan
Write-Host "  Global Admin or Application Administrator required." -ForegroundColor Yellow
Write-Host ""

Connect-MgGraph -Scopes @(
    "Application.ReadWrite.All",
    "AppRoleAssignment.ReadWrite.All"
) -NoWelcome

$context  = Get-MgContext
$tenantId = $context.TenantId
Write-Host "  Connected as: $($context.Account)" -ForegroundColor Green
Write-Host "  Tenant:       $tenantId" -ForegroundColor Green

#endregion

#region ── Check existing app ──────────────────────────────────────────────

Write-Host ""
Write-Host "  Checking if app '$AppName' already exists..." -ForegroundColor Cyan

$existingApp = Get-MgApplication -Filter "displayName eq '$AppName'" -ErrorAction SilentlyContinue

if ($existingApp) {
    Write-Host "  WARNING: App already exists (Id: $($existingApp.Id))" -ForegroundColor Yellow
    $confirm = Read-Host "  Delete and recreate? (Y/N)"
    if ($confirm -eq "Y") {
        Remove-MgApplication -ApplicationId $existingApp.Id -ErrorAction Stop
        Write-Host "  Deleted." -ForegroundColor Green
    } else {
        Write-Host "  Cancelled." -ForegroundColor Red
        Disconnect-MgGraph | Out-Null
        exit 1
    }
}

#endregion

#region ── Permission Definitions (21 Application permissions) ─────────────
#
# Graph API AppId: 00000003-0000-0000-c000-000000000000
# All "Role" type = Application permissions (app-only, no user context)
# All are READ-ONLY — Kryoss never writes to the customer's tenant.

$graphApiAppId = "00000003-0000-0000-c000-000000000000"

$requiredPermissions = @(
    # ── Tier 0: Core security checks (existing 30 checks) ──
    @{ Id = "246dd0d5-5bd0-4def-940b-0421030a5b68"; Name = "Policy.Read.All"
       Tier = 0; Checks = "M365-001..008,013,015,024,039"; Desc = "Conditional Access, security defaults, auth methods, named locations" }
    @{ Id = "df021288-bdef-4463-88db-98f22de89214"; Name = "User.Read.All"
       Tier = 0; Checks = "M365-009..012,023,031,032"; Desc = "User enumeration, MFA enrollment, guest count, stale accounts" }
    @{ Id = "38d9df27-64da-44fd-b7c5-a6fbac20248f"; Name = "UserAuthenticationMethod.Read.All"
       Tier = 0; Checks = "M365-009,011"; Desc = "MFA method details per user" }
    @{ Id = "7ab1d382-f21e-4acd-a863-ba3e13f7da61"; Name = "Directory.Read.All"
       Tier = 0; Checks = "M365-018..022"; Desc = "Admin roles, directory role members" }
    @{ Id = "483bed4a-2ad3-4361-a73b-c83ccdbdc53c"; Name = "RoleManagement.Read.Directory"
       Tier = 0; Checks = "M365-018"; Desc = "Directory role assignments" }
    @{ Id = "40f97065-369a-49f4-947c-6a90f8a50271"; Name = "MailboxSettings.Read"
       Tier = 0; Checks = "M365-027"; Desc = "Mail forwarding detection" }
    @{ Id = "810c84a8-4a9e-49e6-bf7d-12d183f40d01"; Name = "Mail.Read"
       Tier = 0; Checks = "M365-027"; Desc = "Inbox forwarding rules inspection" }

    # ── Tier 1: Stale accounts, risky apps, Secure Score ──
    @{ Id = "b0afded3-3588-46d8-8b3d-9842eff778da"; Name = "AuditLog.Read.All"
       Tier = 1; Checks = "M365-031,032"; Desc = "Last sign-in activity for stale account detection" }
    @{ Id = "9a5d68dd-52b0-4cc2-bd40-abcf44ac3a30"; Name = "Application.Read.All"
       Tier = 1; Checks = "M365-033..036"; Desc = "App registrations audit: expired secrets, excessive permissions, risky consent" }
    @{ Id = "bf394140-e372-4bf9-a898-299cfc7564e5"; Name = "SecurityEvents.Read.All"
       Tier = 1; Checks = "M365-037,038"; Desc = "Microsoft Secure Score and improvement actions" }

    # ── Tier 2: Identity Protection + Intune ──
    @{ Id = "d04bb851-cb7c-4146-97c7-ca3e71baf56c"; Name = "IdentityRiskEvent.Read.All"
       Tier = 2; Checks = "M365-041"; Desc = "High-risk sign-in detections (requires Entra P2)" }
    @{ Id = "dc5007c0-2d7d-4c42-879c-2dab87571379"; Name = "IdentityRiskyUser.Read.All"
       Tier = 2; Checks = "M365-042"; Desc = "Risky user flagging (requires Entra P2)" }
    @{ Id = "2f51be20-0bb4-4fed-bf7b-db946066c75e"; Name = "DeviceManagementManagedDevices.Read.All"
       Tier = 2; Checks = "M365-043,044"; Desc = "Intune device enrollment and compliance status" }
    @{ Id = "dc377aa6-52d8-4e23-b271-b3b753c006e0"; Name = "DeviceManagementConfiguration.Read.All"
       Tier = 2; Checks = "M365-045,046"; Desc = "Intune compliance policies and encryption enforcement" }

    # ── Tier 3: DLP, SharePoint, Security Alerts, Org Config ──
    @{ Id = "dfb0dd15-61de-45b2-be36-d6a69fba3c79"; Name = "InformationProtectionPolicy.Read.All"
       Tier = 3; Checks = "M365-047"; Desc = "DLP and sensitivity label policies" }
    @{ Id = "5e0edab9-c148-49d0-b423-ac253e9430ab"; Name = "SecurityActions.Read.All"
       Tier = 3; Checks = "M365-048"; Desc = "Active security alerts" }
    @{ Id = "9a5d68dd-52b0-4cc2-bd40-abcf44ac3a30"; Name = "ThreatAssessment.Read.All"
       Tier = 3; Checks = "M365-048"; Desc = "Threat assessment results" }
    @{ Id = "332a536c-c7ef-4017-ab91-336970924f0d"; Name = "Sites.Read.All"
       Tier = 3; Checks = "M365-049"; Desc = "SharePoint external sharing audit" }
    @{ Id = "498476ce-e0fe-48b0-b801-37ba7e2685c6"; Name = "Organization.Read.All"
       Tier = 3; Checks = "M365-040,050"; Desc = "Org config, verified domains, password policies" }
)

# Deduplicate by Id (some GUIDs might be shared)
$uniquePermissions = $requiredPermissions | Sort-Object -Property Id -Unique

Write-Host ""
Write-Host "  Permissions to be requested ($($uniquePermissions.Count) total):" -ForegroundColor Cyan
Write-Host ""

$tiers = @{ 0 = "Core (30 checks)"; 1 = "Stale/Apps/Score"; 2 = "Risk/Intune"; 3 = "DLP/SPO/Alerts" }
foreach ($tier in 0..3) {
    $tierPerms = $requiredPermissions | Where-Object { $_.Tier -eq $tier }
    Write-Host "  Tier $tier — $($tiers[$tier]):" -ForegroundColor White
    foreach ($p in $tierPerms) {
        Write-Host "    $($p.Name.PadRight(50)) $($p.Desc)" -ForegroundColor Gray
    }
    Write-Host ""
}

#endregion

#region ── Self-Signed Certificate ─────────────────────────────────────────

Write-Host "  Creating self-signed certificate..." -ForegroundColor Cyan

$certName    = "CN=$AppName"
$certExpiry  = (Get-Date).AddYears($CertificateValidityYears)
$certStore   = "Cert:\CurrentUser\My"

$cert = New-SelfSignedCertificate `
    -Subject $certName `
    -CertStoreLocation $certStore `
    -KeyExportPolicy Exportable `
    -KeySpec Signature `
    -KeyLength 2048 `
    -KeyAlgorithm RSA `
    -HashAlgorithm SHA256 `
    -NotAfter $certExpiry

$certThumbprint = $cert.Thumbprint
Write-Host "    Thumbprint : $certThumbprint" -ForegroundColor Gray
Write-Host "    Expires    : $($certExpiry.ToString('yyyy-MM-dd'))" -ForegroundColor Gray

# Export .cer (public key — uploaded to App Registration)
$certPath = Join-Path $OutputPath "$AppName.cer"
Export-Certificate -Cert "$certStore\$certThumbprint" -FilePath $certPath -Type CERT | Out-Null
Write-Host "    Public cert: $certPath" -ForegroundColor Gray

# Export .pfx (private key — store securely)
$pfxPassword = Read-Host "  Password for .pfx file (private key)" -AsSecureString
$pfxPath = Join-Path $OutputPath "$AppName.pfx"
Export-PfxCertificate `
    -Cert "$certStore\$certThumbprint" `
    -FilePath $pfxPath `
    -Password $pfxPassword | Out-Null
Write-Host "    PFX (secure): $pfxPath" -ForegroundColor Yellow

$certBytes  = [System.IO.File]::ReadAllBytes($certPath)
$certBase64 = [System.Convert]::ToBase64String($certBytes)

#endregion

#region ── Create App Registration ─────────────────────────────────────────

Write-Host ""
Write-Host "  Creating App Registration '$AppName'..." -ForegroundColor Cyan

$graphApiAccess = [Microsoft.Graph.PowerShell.Models.MicrosoftGraphRequiredResourceAccess]@{
    ResourceAppId  = $graphApiAppId
    ResourceAccess = $uniquePermissions | ForEach-Object {
        [Microsoft.Graph.PowerShell.Models.MicrosoftGraphResourceAccess]@{
            Id   = $_.Id
            Type = "Role"
        }
    }
}

$appParams = @{
    DisplayName            = $AppName
    SignInAudience         = "AzureADMyOrg"
    RequiredResourceAccess = @($graphApiAccess)
    KeyCredentials         = @(
        @{
            Type        = "AsymmetricX509Cert"
            Usage       = "Verify"
            Key         = $certBytes
            DisplayName = "$AppName-Cert"
            EndDateTime = $cert.NotAfter.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        }
    )
    Notes = "$($brand.Product) — Read-only M365/Entra ID security scanner. 50 checks across Conditional Access, MFA, admin roles, Intune, DLP, and more. All permissions are Application (app-only) and Read-Only."
}

$newApp   = New-MgApplication @appParams -ErrorAction Stop
$appId    = $newApp.AppId
$appObjId = $newApp.Id

Write-Host "    Client ID: $appId" -ForegroundColor Green

# Create Service Principal
Write-Host "    Creating Service Principal..." -ForegroundColor Gray
$sp = New-MgServicePrincipal -AppId $appId -ErrorAction Stop
Write-Host "    SP created: $($sp.Id)" -ForegroundColor Gray

Write-Host "    Waiting for Entra ID replication..." -ForegroundColor Gray
Start-Sleep -Seconds 10

#endregion

#region ── Admin Consent ──────────────────────────────────────────────────

if ($AutoGrantConsent) {
    Write-Host ""
    Write-Host "  Granting admin consent..." -ForegroundColor Cyan

    $graphSP = Get-MgServicePrincipal -Filter "appId eq '$graphApiAppId'" -ErrorAction Stop

    $consentErrors = @()
    foreach ($perm in $uniquePermissions) {
        try {
            New-MgServicePrincipalAppRoleAssignment `
                -ServicePrincipalId $sp.Id `
                -PrincipalId        $sp.Id `
                -ResourceId         $graphSP.Id `
                -AppRoleId          $perm.Id `
                -ErrorAction Stop | Out-Null
            Write-Host "    Granted: $($perm.Name)" -ForegroundColor Green
        }
        catch {
            $consentErrors += $perm.Name
            Write-Warning "    Error for $($perm.Name): $_"
        }
    }

    if ($consentErrors.Count -gt 0) {
        Write-Host ""
        Write-Host "  Some permissions need manual consent:" -ForegroundColor Yellow
        $consentErrors | ForEach-Object { Write-Host "    - $_" -ForegroundColor Yellow }
    } else {
        Write-Host "    All permissions consented." -ForegroundColor Green
    }

} else {
    $consentUrl = "https://login.microsoftonline.com/$tenantId/adminconsent?client_id=$appId"

    Write-Host ""
    Write-Host "  ================================================================" -ForegroundColor Yellow
    Write-Host "    REQUIRED: Admin Consent" -ForegroundColor Yellow
    Write-Host "  ================================================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    A Global Admin must open this URL:" -ForegroundColor White
    Write-Host ""
    Write-Host "    $consentUrl" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "    Without consent, Kryoss scans will fail with 403." -ForegroundColor White
    Write-Host "  ================================================================" -ForegroundColor Yellow
}

#endregion

#region ── Generate config.json ────────────────────────────────────────────

$config = [ordered]@{
    _description    = "$($brand.Product) — M365 Security Scanner Configuration"
    _created        = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    _createdBy      = $context.Account
    _appName        = $AppName
    _certExpiry     = $certExpiry.ToString("yyyy-MM-dd")

    # These 3 values go into the Kryoss Portal M365 tab
    TenantId        = $tenantId
    ClientId        = $appId
    CertThumbprint  = $certThumbprint

    # Local paths (for reference)
    CertPfxPath     = $pfxPath
    CertCerPath     = $certPath

    # Granted permissions (documentation)
    GrantedPermissions = ($uniquePermissions | ForEach-Object { $_.Name }) | Sort-Object -Unique
    TotalChecks     = 50
}

$configPath = Join-Path $OutputPath "Kryoss_M365_Config.json"
$config | ConvertTo-Json -Depth 5 | Out-File -FilePath $configPath -Encoding UTF8

Write-Host ""
Write-Host "  Configuration saved: $configPath" -ForegroundColor Green

#endregion

#region ── Summary ─────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host "    $($brand.Product) — Setup Complete" -ForegroundColor Green
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "    App Name       : $AppName" -ForegroundColor White
Write-Host "    Client ID      : $appId" -ForegroundColor White
Write-Host "    Tenant ID      : $tenantId" -ForegroundColor White
Write-Host "    Cert Thumbprint: $certThumbprint" -ForegroundColor White
Write-Host "    Cert Expires   : $($certExpiry.ToString('yyyy-MM-dd'))" -ForegroundColor White
Write-Host "    Permissions    : $($uniquePermissions.Count) (all read-only)" -ForegroundColor White
Write-Host ""
Write-Host "    Files:" -ForegroundColor White
Write-Host "      Config : $configPath" -ForegroundColor Cyan
Write-Host "      Cert   : $certPath" -ForegroundColor Cyan
Write-Host "      PFX    : $pfxPath" -ForegroundColor Yellow
Write-Host ""
Write-Host "    NEXT STEPS:" -ForegroundColor Yellow
if (-not $AutoGrantConsent) {
Write-Host "      1. Open the admin consent URL above" -ForegroundColor Yellow
Write-Host "      2. Go to Kryoss Portal > Organization > M365/Cloud" -ForegroundColor Yellow
Write-Host "      3. Click 'Manual Setup' and enter:" -ForegroundColor Yellow
} else {
Write-Host "      1. Go to Kryoss Portal > Organization > M365/Cloud" -ForegroundColor Yellow
Write-Host "      2. Click 'Manual Setup' and enter:" -ForegroundColor Yellow
}
Write-Host "         - Tenant ID      : $tenantId" -ForegroundColor White
Write-Host "         - Client ID      : $appId" -ForegroundColor White
Write-Host "         - Cert Thumbprint: $certThumbprint" -ForegroundColor White
Write-Host ""
Write-Host "  ================================================================" -ForegroundColor Green

Disconnect-MgGraph | Out-Null

#endregion
