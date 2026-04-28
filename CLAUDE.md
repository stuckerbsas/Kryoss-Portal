# Projecto Kryoss — Master Index

**Owner:** Federico / Geminis Computer S.A. / TeamLogic IT

---

## Version tracking

| Component | Current | Where | Endpoint |
|-----------|---------|-------|----------|
| **API** | 1.34.10 | `KryossApi.csproj` `<Version>` | `GET /v2/version` (no auth) |
| **Portal** | 1.18.4 | `KryossPortal/package.json` `"version"` | Sidebar footer "Powered by Kryoss vX.Y.Z" |
| **Agent** | 2.11.1 | `KryossAgent.csproj` `<Version>` | Registry `HKLM\SOFTWARE\Kryoss\Agent\Version` |

**VERSION RULES:** Every behavior change bumps version BEFORE build. Patch=bugfix, Minor=feature, Major=breaking. Update this table when bumping.

---

## What Kryoss is

Security assessment SaaS for MSPs (primary: TeamLogic IT franchise). Three pillars: (1) audit compliance — ~918 checks against CIS/NIST/HIPAA/ISO/PCI, (2) ticket reduction — drift/misconfig detection, (3) cloud workspace hardening — M365/Entra/Azure/PBI via Cloud Assessment.

**Business model:** Kryoss = backend. Franchise portal = MSPs sign up, enroll client machines, generate reports, sell remediation.

---

## Documentation system — WHAT GOES WHERE

Every session: read this file + subfolder CLAUDE.md of what you're editing. Read the others only when relevant.

| File | Purpose | When to READ | When to WRITE |
|------|---------|-------------|---------------|
| **`CLAUDE.md`** (this file) | Essential context for every session: versions, tech stack, repo layout, work rules | **Every session, first** | When you bump a version, change tech stack, or modify repo layout |
| **`docs/superpowers/plans/ROADMAP.md`** | Active queue + current state snapshot + session checklist | **Every session, second** | When a phase ships or queue changes |
| **`docs/BACKLOG.md`** | All pending/future work organized by tier | When planning next session or user asks "what's next" | When new work is identified or priorities change |
| **`docs/BITACORA.md`** | Decision log + shipped session history | When you need context on WHY something was built a certain way | After each session: add decisions made + summary of what shipped |
| **`docs/FEATURES-SHIPPED.md`** | What's built — by component, with technical details + codebase metrics | When you need implementation details of existing features | When a feature ships: add to relevant section + update metrics |
| **`KryossApi/CLAUDE.md`** | Full API map (endpoints, entities, services) | Before touching backend | When adding endpoints/entities/services |
| **`KryossAgent/CLAUDE.md`** | Full agent map (engines, services, CLI flags) | Before touching agent | When adding engines/services/flags |
| **`Scripts/CLAUDE.md`** | PowerShell standards for RMM/Intune deploy | When writing deploy scripts | When adding scripts |
| **`KryossApi/docs/security-baseline.md`** | Zero-trust defense-in-depth contract | **Before ANY auth/crypto/key change** | When security posture changes |

**GOLDEN RULE:** If you add something (endpoint, table, feature, decision), update the relevant doc in the same session. 30 seconds now saves 5 minutes of re-exploration next time.

---

## Repository layout

```
Projecto Kryoss\
├── CLAUDE.md                       <- YOU ARE HERE (essential context)
├── KryossApi\                      <- Backend (.NET 8 Azure Functions)
├── KryossAgent\                    <- Windows agent (.NET 8, v2.9.1, ~12 MB trimmed)
├── KryossPortal\                   <- Frontend (React 18 + Vite + TS + shadcn/ui + MSAL)
├── Scripts\                        <- PowerShell scripts & audit tools
├── docs\                           <- Documentation hub
│   ├── BACKLOG.md                  <- Pending work by tier
│   ├── BITACORA.md                 <- Decision log + session history
│   ├── FEATURES-SHIPPED.md         <- Shipped features reference
│   └── superpowers/plans/ROADMAP.md <- Active queue + session checklist
└── archive\                        <- Dead/legacy (don't touch without asking)
```

---

## Tech stack

| Layer | Tech |
|---|---|
| Backend | .NET 8 Azure Functions + EF Core 8 + Azure SQL (`sql-kryoss.database.windows.net` / `KryossDb`) |
| Agent | .NET 8 win-x64 single-file trimmed (~12 MB), 13 engines, zero Process.Start |
| Portal | React 18 + Vite + TS + shadcn/ui + MSAL, Azure SWA (`zealous-dune-0ac672d10.6.azurestaticapps.net`) |
| Auth | Agent: API Key + HMAC-SHA256 + per-machine key rotation (3-layer). Portal: MSAL JWT. Easy Auth DISABLED. |
| Crypto | RSA-2048 + AES-256-GCM envelope encryption (agent->API) |
| RLS | SQL Session Context via `RlsMiddleware` |

---

## Security baseline (summary)

**Full doc:** `KryossApi/docs/security-baseline.md` — read before touching auth/crypto/keys.

Zero-trust defense-in-depth. Envelope: RSA-OAEP-256 + AES-256-GCM. Integrity: HMAC-SHA256. Replay: nonce cache + timestamp window. Identity: TPM-bound hardware fingerprint. Infra: Managed Identity -> Azure SQL. App: DTOs separate from entities, error sanitization.

**Weakest link:** compromised endpoint submitting crafted but crypto-valid payloads. Mitigation: server-side (TPM binding, rate limits, anomaly scoring).

---

## Brand & writing rules

- Brand: **TeamLogic IT**, "Your Technology Advisor"
- Primary green: **#008852**, accent **#A2C564**, dark **#3D4043**
- Font: **Montserrat**. Logo: `Scripts/assets/TLITLogo.svg`
- PASS=#008852, WARN=#D97706, FAIL=#C0392B
- Code/comments/variables in **English**. User-facing: English or Spanish.

---

## Token-saving rules

1. Read `CLAUDE.md` first, then subfolder CLAUDE.md
2. Check docs before re-grepping — if it's documented, don't re-explore
3. **Update docs when you add something** — new table, endpoint, feature, decision
4. DB: `tcp:sql-kryoss.database.windows.net,1433` / `KryossDb` / `kryossadmin`

---

## Instrucciones
- Respuestas: maximo 1-2 lineas
- Codigo: comentarios solo si no es obvio
- Cambios: directo, sin explicar logica
- Errors: reporta y sugiere fix, no narres
- Sin saludos, cierres, resumenes
- NO mostrar codigo en las respuestas salvo que se este discutiendo algo en particular. Solo reportar que se hizo/cambio.
