# Hardening Standards - TeamLogic IT

## About
This directory contains scripts for system hardening according to CIS benchmark standards and additional security best practices for our MSP clients.

## Hardening Scripts

### Disable-InsecureProtocols.ps1
Disables insecure network protocols that pose security risks:
- **SMBv1** - Deprecated protocol with numerous vulnerabilities
- **LLMNR** - Link-Local Multicast Name Resolution, susceptible to spoofing
- **NetBIOS** - Legacy naming protocol with security implications

#### Features
- Idempotent (safe to run multiple times)
- Comprehensive logging to `C:\ProgramData\TeamLogicIT\Logs\`
- Compatible with PowerShell 5.1+
- Supports `-WhatIf` for testing
- Silent execution for NinjaRMM deployment
- Proper error handling and exit codes

#### Deployment
- Designed for NinjaRMM silent deployment
- Runs as SYSTEM account
- Returns appropriate exit codes:
  - `0` = Success (completed or no changes needed)
  - `1` = Error occurred
  - `2` = Warning (reserved for future use)

#### Requirements
- Windows 10/11 or Windows Server 2019/2022
- PowerShell 5.1 or higher
- Administrator privileges (typically provided by NinjaRMM)

#### Manual Execution
```powershell
.\Disable-InsecureProtocols.ps1
```

#### Testing Mode
```powershell
.\Disable-InsecureProtocols.ps1 -WhatIf
```

This will show what changes would be made without actually making them.

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