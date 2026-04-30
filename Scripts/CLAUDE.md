# TeamLogic IT — MSP PowerShell Environment
# Project: Kryoss | Company: Geminis Computer S.A.

## About
- **MSP Brand:** TeamLogic IT (powered by Kryoss, a Geminis Computer S.A. project)
- Scripts are deployed remotely via **NinjaRMM (NinjaOne)** or **Microsoft Intune** depending on the client
- All scripts must be client-agnostic unless a client-specific CLAUDE.md overrides this file
- **Script author header:** TeamLogic IT

---

## Working Directory
```
C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\Scripts\
```
All scripts, modules, and client folders live under this path.

---

## Client Infrastructure
Clients may have any combination of the following — always check before assuming:
- **Active Directory on-premise** (some clients)
- **Azure AD / Entra ID** (most clients)
- **Microsoft 365** (most clients)
- **Workgroup** (no domain — smaller clients)

When writing scripts that touch AD or user management, include a parameter or detection block to handle both on-premise AD and workgroup environments gracefully.

---

## Supported Platforms
- Windows 10 (21H2+)
- Windows 11 (22H2+)
- Windows Server 2019
- Windows Server 2022
- PowerShell 5.1 (minimum) and PowerShell 7+ (preferred when available)

Scripts must be compatible with **PowerShell 5.1** unless explicitly stated otherwise. Do not use PS7-only syntax without a version check.

---

## RMM: NinjaRMM
- Scripts run as **SYSTEM** account — no interactive prompts, no GUI dialogs
- NinjaRMM reads **exit codes**: `0` = success, `1` = error, `2` = warning/non-critical
- Always end scripts with `exit 0`, `exit 1`, or `exit 2` as appropriate
- NinjaRMM script timeout is typically 60 seconds — avoid long-running operations without chunking
- Use `Write-Host` for NinjaRMM activity log output (visible in the RMM console)
- Avoid `Write-Output` for status messages — reserve it for actual data output

---

## Script Standards

### Header (required on every script)
```powershell
<#
.SYNOPSIS
    Brief one-line description.

.DESCRIPTION
    Detailed description of what the script does.

.PARAMETER
    Document all parameters.

.NOTES
    Author:   TeamLogic IT
    Version:  1.0
    Created:  YYYY-MM-DD
    Modified: YYYY-MM-DD

.EXAMPLE
    .\ScriptName.ps1 -Parameter Value
#>
```

### Parameters
- Always use `[CmdletBinding()]` and `param()` blocks
- Include `-WhatIf` support on any script that makes destructive or irreversible changes
- Include a `-Verbose` flag for detailed output when needed
- Client-specific values (paths, thresholds, exceptions) go as parameters at the top — never hardcoded in the body

### Error Handling
- Wrap all critical operations in `try/catch`
- Use `$ErrorActionPreference = 'Stop'` inside try blocks where needed
- Never silently swallow errors — always log them
- On fatal error: log, then `exit 1`

### Idempotency
- All scripts must be safe to run multiple times without side effects
- Check current state before making changes (e.g., "is this already disabled?")

---

## Logging
- Log path: `C:\ProgramData\TeamLogicIT\Logs\`
- Log filename format: `ScriptName_YYYYMMDD.log`
- Log format: `[YYYY-MM-DD HH:MM:SS] [INFO|WARN|ERROR] Message`
- Always create the log directory if it doesn't exist
- Rotate or trim logs older than 30 days when relevant

```powershell
# Standard logging function to include in scripts
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$timestamp] [$Level] $Message"
    Write-Host $entry
    Add-Content -Path $logPath -Value $entry
}
```

---

## Security & Hardening Guidelines
- Baseline: **CIS Benchmark Level 1** for Windows 10/11 and Server 2019/2022
- Do not break Microsoft 365 / Office functionality
- Do not disable features required for NinjaRMM agent operation
- SMBv1, LLMNR, NetBIOS over TCP/IP — disable by default
- RDP — disable if unused; restrict by IP if enabled
- Windows Defender — must remain active; only add exclusions when explicitly required
- Password policy enforcement — minimum 12 characters, complexity enabled, max age 90 days
- All hardening scripts must log every change made with previous and new value

---

## Code Style
- Language: **English** (comments, variable names, log messages)
- Use **PascalCase** for functions: `Get-DiskHealth`, `Set-FirewallBaseline`
- Use **camelCase** for local variables: `$diskUsage`, `$logPath`
- Use **ALL_CAPS** for constants: `$LOG_DIR`, `$MAX_AGE_DAYS`
- Prefer explicit parameter names over positional: `Get-Item -Path $path` not `Get-Item $path`
- No aliases in scripts (`Get-ChildItem` not `gci`, `ForEach-Object` not `%`)
- Keep functions under 50 lines — split into smaller functions if needed
- Group scripts into modules when 3+ related functions exist

---

## Folder Structure
```
C:\Users\feder\OneDrive - Geminis Computer S.A\Projecto Kryoss\Scripts\
├── CLAUDE.md                          <- this file (global MSP context)
├── assets\
│   └── TLITLogo.svg                  <- official TeamLogic IT logo (SVG)
├── Audit\
│   ├── CLAUDE.md                     <- audit controls documentation
│   ├── Invoke-KryossAssessment.ps1   <- primary security assessment (56 controls)
│   ├── Invoke-KryossAssessment-Enhanced.ps1  <- enhanced assessment variant
│   ├── Kryoss-Report-Template.html   <- branded HTML report template
│   ├── Kryoss_DeepScan.ps1           <- network/infrastructure deep scanner
│   └── _archive\                     <- legacy assessment scripts (do not use)
├── Hardening\
│   ├── CLAUDE.md                     <- hardening standards & CIS baseline
│   ├── Disable-*.ps1                 <- scripts that disable insecure features
│   └── Set-*.ps1                     <- scripts that configure security policies
├── Maintenance\
│   ├── CLAUDE.md                     <- maintenance standards
│   ├── Repair-*.ps1                  <- system repair scripts
│   ├── Set-*.ps1                     <- configuration scripts
│   └── Sync-*, Invoke-*, Restart-*   <- operational maintenance
├── NetworkDiscovery\
│   ├── Get-NetworkDevices.ps1        <- subnet device discovery (ARP/Ping)
│   ├── Test-RemoteExecution.ps1      <- PsExec connectivity validation
│   ├── Invoke-NetworkAssessment.ps1  <- combined discovery + validation
│   └── Test-NetworkConnectivity.ps1  <- quick network connectivity test
├── Cloud\
│   ├── M365\                         <- Microsoft 365 audit & hardening (TODO)
│   └── GoogleWorkspace\              <- Google Workspace audit & hardening (TODO)
├── Purview Assessment\               <- M365 compliance assessment data
├── superpowers\                      <- Claude Code development workflow plugin
└── Clients\
    ├── ClientA\
    │   └── CLAUDE.md                 <- client-specific overrides
    └── ClientB\
        └── CLAUDE.md
```

---

## Branding — TeamLogic IT

### Colors (from Brand Standards Guide 2025)
| Role | Name | Hex | Pantone | CMYK |
|------|------|-----|---------|------|
| Primary (logo, headings) | Green | #008852 | PMS 348 | C100 M0 Y85 K24 |
| Accent (ribbon, highlights) | Light Green | #A2C564 | PMS 577 | C35 M0 Y75 K8 |
| Background / dark surfaces | Dark Gray | #636467 | Cool Gray 10 | C0 M0 Y0 K75 |
| Call-to-action (accent ONLY) | Blue | #0095DA | PMS 2925 | C100 M20 Y0 K0 |
| Dark green (hover, borders) | Dark Green | #006536 | PMS 356 | C100 M30 Y100 K30 |

**Note:** Brand guide says `#008851`, scripts use `#008852` — 1-value difference, both acceptable in digital.
**Blue rule:** PMS 2925 is ONLY for call-to-action and accent text/graphics. Not for primary branding.

### Typography
- **Campaign font:** Montserrat (Google Fonts) — the ONLY font for campaign materials
- **Weights used:** 300 (Light), 400 (Regular), 500 (Medium), 600 (Semi-Bold), 700 (Bold), 900 (Black)
- **Fallback:** Verdana Bold & Regular (when Montserrat unavailable)
- **Logo typefaces (in SVG, not for general use):** Frutiger Black ("TEAM"/"IT"), ITC Leawood Book ("Logic"), Berthold Akzidenz Grotesk Extended (tagline)

### Logo
- **SVG path:** `assets/TLITLogo.svg` (3210x915 px, 76.9 KB)
- **Fills in SVG:** `#008752` (green paths) and `#000000` (black paths)
- Logo is a registered trademark — MUST include (R) symbol
- Icon + wordmark = single unit — **never** separate elements
- **Never** alter colors, add text, attach city names, or modify in any way
- For dark backgrounds: replace `fill="#000000"` with `fill="#FFFFFF"` before encoding
- When embedding: `data:image/svg+xml;base64,` + base64-encoded SVG content
- Clear space: 1X preferred, 0.5X minimum around logo
- Minimum size with tagline: 0.75" wide. Without tagline: 0.5" wide
- The triad (icon) can be used standalone at **13-degree angle**
- In written text: "TeamLogic IT" (adjoined word, capital L, space before IT)
- Always try to include tagline when space allows

### Tagline
"Your Technology Advisor"

### Brand Campaign
- Campaign name: "The Color of Confidence"
- Tone: people first, tech next — warm, professional, trustworthy

### Signature Design Element: The Ribbon
- Diagonal green stripes in the bottom-right corner of any page/cover
- Uses 7 green shades (official values are CMYK; hex below are HTML approximations)
- Ribbon hex values in HTML (dark to light): #2D5A1F #3B6D2A #4E8A38 #62A845 #76C553 #A2C564 #C8DC8A
- **Must** bleed off both the right AND bottom edges
- **Must** include all 7 stripes in any crop
- **Only** the TeamLogic IT logo/tagline may appear inside the ribbon
- Two palettes exist: **Solid** (over solid backgrounds) and **Translucent** (over photos, with opacity)
- The two palettes are NOT interchangeable
- Ribbon colors are NOT to be extracted for use outside the ribbon element
- Do NOT alter supplied ribbon graphics

### HTML Report Rules
- Always use Kryoss-Report-Template.html as the base for any generated report
- Logo must be embedded as base64 — reports must be fully self-contained (no external files)
- Cover page: dark background #3D4043, white text, ribbon bottom-right
- Inner pages: white background, dark header #3D4043, green stripe divider
- Status colors: PASS = #008852, WARN = #D97706, FAIL = #C0392B
- Font loaded via Google Fonts CDN in HTML: Montserrat

---

## What to Always Ask Before Writing a Script
1. Does this need to run silently (NinjaRMM deploy) or interactively?
2. Does the client have AD, Azure AD, M365, or workgroup?
3. Is this destructive? If yes, add -WhatIf and confirmation logging.
4. Does it need to run once or be scheduled?
5. What should happen on failure — silent exit or alert?

---

## Coding Principles

### 1. Think Before Coding
Don't assume. Don't hide confusion. Surface tradeoffs.

- State assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them — don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

### 2. Simplicity First
Minimum code that solves the problem. Nothing speculative.

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.
- Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

### 3. Surgical Changes
Touch only what you must. Clean up only your own mess.

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it — don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

### 4. Goal-Driven Execution
Define success criteria. Loop until verified.

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.
