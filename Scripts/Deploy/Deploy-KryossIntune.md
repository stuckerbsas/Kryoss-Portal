# Kryoss Agent — Intune (Endpoint Manager) Deployment Guide

Deploy Kryoss Agent v1.3.0+ as a Win32 app via Microsoft Intune.

---

## Prerequisites

- Microsoft Intune license (M365 E3/E5 or standalone)
- Global Administrator or Intune Administrator role
- Microsoft Win32 Content Prep Tool (free):
  https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool
- `KryossAgent.exe` from the Kryoss portal
- Enrollment code from the Kryoss portal

---

## Step 1: Package the agent as .intunewin

1. Download the Content Prep Tool (`IntuneWinAppUtil.exe`) to a local folder
2. Put `KryossAgent.exe` and `install.ps1` (see below) in a source folder:

   ```
   C:\Intune\Kryoss\Source\
   ├── KryossAgent.exe
   └── install.ps1
   ```

3. Create `install.ps1`:

   ```powershell
   # install.ps1 — Intune Win32 install script
   param([string]$EnrollmentCode = "REPLACE_WITH_YOUR_CODE")

   $ErrorActionPreference = "Stop"
   $agentDir = "$env:ProgramData\Kryoss"
   $agentExe = "$agentDir\KryossAgent.exe"

   if (-not (Test-Path $agentDir)) {
       New-Item -Path $agentDir -ItemType Directory -Force | Out-Null
   }

   Copy-Item -Path "$PSScriptRoot\KryossAgent.exe" -Destination $agentExe -Force

   & $agentExe --silent --code $EnrollmentCode
   exit $LASTEXITCODE
   ```

4. Run the Content Prep Tool:

   ```
   IntuneWinAppUtil.exe ^
     -c "C:\Intune\Kryoss\Source" ^
     -s "install.ps1" ^
     -o "C:\Intune\Kryoss\Output"
   ```

5. Output: `C:\Intune\Kryoss\Output\install.intunewin`

---

## Step 2: Upload to Intune

1. Go to **https://intune.microsoft.com** → **Apps** → **Windows** → **Add**
2. App type: **Windows app (Win32)**
3. Upload `install.intunewin`

### App information

| Field | Value |
|---|---|
| Name | Kryoss Security Agent |
| Description | Kryoss security assessment agent — local-only, read-only sensor |
| Publisher | TeamLogic IT |
| Category | Other |

### Program

| Field | Value |
|---|---|
| Install command | `powershell.exe -ExecutionPolicy Bypass -File install.ps1 -EnrollmentCode "K7X9-M2P4-Q8R1-T5W3"` |
| Uninstall command | `cmd.exe /c rmdir /s /q "%ProgramData%\Kryoss" && reg delete "HKLM\SOFTWARE\Kryoss" /f` |
| Install behavior | System |
| Device restart behavior | No specific action |
| Return codes | 0 = Success, 2 = Soft reboot (deferred upload) |

### Requirements

| Field | Value |
|---|---|
| Operating system architecture | x64 |
| Minimum OS | Windows 10 1809 |

### Detection rules

Use a **Registry** detection rule (the agent writes its state to `HKLM\SOFTWARE\Kryoss\Agent` on successful enrollment):

| Field | Value |
|---|---|
| Rule type | Registry |
| Key path | `HKEY_LOCAL_MACHINE\SOFTWARE\Kryoss\Agent` |
| Value name | `AgentId` |
| Detection method | Value exists |
| Associated with a 32-bit app | No |

### Assignments

- **Required** → assign to device groups (e.g. "All Windows Workstations")
- The agent will install and enroll on the next device check-in (typically within 1 hour)

---

## Step 3: Monitoring

- **Intune**: Apps → Kryoss Security Agent → Device install status
- **Kryoss Portal**: Organization → Fleet tab → new machines appear as they enroll

---

## Re-enrollment / Updates

When you need to push a new agent version or change the enrollment code:

1. Replace `KryossAgent.exe` in the source folder
2. Re-run the Content Prep Tool to create a new `.intunewin`
3. Upload as a new version in Intune (Apps → Kryoss Security Agent → Properties → Update version)
4. Intune will push the update on the next check-in

To force re-enrollment, add `--reenroll` to the install command in Step 2.

---

## Uninstall

The uninstall command wipes the agent binary and registry state:

```
cmd.exe /c rmdir /s /q "%ProgramData%\Kryoss" && reg delete "HKLM\SOFTWARE\Kryoss" /f
```

The machine will still appear in the Kryoss Portal (historical data retained) until
manually removed from the organization.

---

## Security notes

- The agent runs as **SYSTEM** (via Intune install context)
- All credentials are stored in `HKLM\SOFTWARE\Kryoss\Agent` with SYSTEM-only ACL
- v1.3.0+ agent does **NOT** spawn external processes — zero `Process.Start` calls
- Communication with the Kryoss API is HMAC-SHA256 signed + AES-256-GCM envelope-encrypted
- The agent is a passive sensor — it cannot execute commands, modify policy, or deploy to other machines

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Install fails with exit code 1 | Enrollment code invalid/expired | Generate new code in portal, update install.ps1 |
| Install succeeds but machine doesn't appear in portal | Network / firewall blocks HTTPS to func-kryoss.azurewebsites.net | Whitelist the domain |
| Detection rule never matches | Agent failed silently | Check `%ProgramData%\Kryoss\PendingResults\` for offline queue |
| Exit code 2 | Upload deferred (offline) | Expected on machines with intermittent connectivity; will retry next run |
