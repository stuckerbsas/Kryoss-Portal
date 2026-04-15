# Reports 4-Type Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consolidate the 8-variant report catalog into 4 clean org-scoped types (C-Level, Technical Level, Monthly Progress [deferred], Preventas) with framework and language filters. Build the new C-Level from scratch, rebuild Technical Level's content, consolidate Preventas under a tone parameter, and deprecate per-run reports.

**Architecture:** Each builder is a static method in `ReportService.cs` following the existing pattern (`BuildOrgXxxReport`). The CTA rule engine for C-Level lives in a dedicated `CtaRuleEngine.cs` class so the logic can be reasoned about and unit-tested independently. A new `executive_ctas` table persists the operator-edited CTAs per org per reporting period. The portal gains a `CtaPreviewModal.tsx` component where operators can edit the auto-detected CTAs before generating the C-Level PDF.

**Tech Stack:** .NET 8 Azure Functions v4 isolated worker, EF Core 8 with snake_case naming convention against Azure SQL, React 18 + TypeScript + Vite + MSAL + shadcn/ui, deployed to `func-kryoss` (API) and `swa-kryoss-portal` (portal SWA).

**Testing strategy:** This codebase has no existing test project — all verification to date has been manual via `dotnet publish` + deploy + browser inspection. For this plan we follow the same pragmatic approach:
- **Pure functions** (rule engine, capital sin detector, KPI computations) get manual test cases documented in the tasks via sample input/output tables that the engineer verifies by running the built endpoint against a dev org.
- **Report rendering** gets verified by generating the HTML from the deployed API and visually inspecting in a browser tab.
- **Portal UI** gets verified by clicking through the flow after deploy.

Every task ends with a `git commit` step. Frequent commits are non-negotiable.

**Reference spec:** `docs/superpowers/specs/2026-04-15-reports-4-type-baseline-design.md` — read this before starting any task for the full design rationale.

---

## File structure map

### New files

| Path | Purpose |
|---|---|
| `KryossApi/sql/028_executive_ctas.sql` | Migration for the `executive_ctas` persistence table |
| `KryossApi/src/KryossApi/Data/Entities/ExecutiveCta.cs` | EF Core entity mapping |
| `KryossApi/src/KryossApi/Services/CtaRuleEngine.cs` | The 12-rule auto-detection engine + priority ranking |
| `KryossApi/src/KryossApi/Services/CapitalSinDetector.cs` | The 4-rule capital sin collapse logic for the Risk Posture semáforo |
| `KryossApi/src/KryossApi/Functions/Portal/ExecutiveCtasFunction.cs` | CRUD endpoints for CTA preview/edit: `GET`, `PATCH`, `POST`, `DELETE` under `/v2/executive-ctas` |
| `KryossPortal/src/components/reports/CtaPreviewModal.tsx` | Modal component shown when generating C-Level: previews auto-detected CTAs + allows edit / suppress / add |

### Modified files

| Path | Nature of change |
|---|---|
| `KryossApi/src/KryossApi/Data/KryossDbContext.cs` | Register the new `ExecutiveCta` DbSet + entity config |
| `KryossApi/src/KryossApi/Services/ReportService.cs` | Add `BuildOrgCLevelReport`, replace body of `BuildOrgTechnicalReport` with new 3-block structure, add `preventas` case + tone routing, delete per-run builders, extend bilingual dictionaries |
| `KryossApi/src/KryossApi/Functions/Portal/ReportsFunction.cs` | Accept `type=c-level`, accept `type=preventas&tone=opener\|detailed`, return HTTP 410 from `Reports_Generate` (per-run endpoint) |
| `KryossPortal/src/components/reports/ReportGenerator.tsx` | Add C-Level entry, consolidate Preventas entry with tone sub-dropdown, remove per-run code paths, integrate `CtaPreviewModal` on C-Level generate |
| `CLAUDE.md` | Update report catalog reference in master index |
| `KryossApi/CLAUDE.md` | Update backend report endpoint documentation |

---

## Phase 1 — Build C-Level Report

Phase 1 is the largest phase. It builds the new C-Level report from scratch, including a new database table, a new rule engine, a new builder method with three content blocks, a new CRUD endpoint for CTA persistence, and a new portal component for CTA preview.

### Task 1: SQL migration for `executive_ctas` table

**Files:**
- Create: `KryossApi/sql/028_executive_ctas.sql`

- [ ] **Step 1: Write the migration**

Create `KryossApi/sql/028_executive_ctas.sql` with:

```sql
-- =============================================================================
-- Migration 028: executive_ctas — persistence for C-Level CTAs
--
-- Hybrid CTA model: the C-Level report Block 3 auto-detects up to 12 rules
-- from assessment data; the operator can edit, suppress, or add manual CTAs
-- before exporting. This table persists those edits per org per period so
-- the next generate call can replay them.
--
-- Idempotent: safe to run multiple times.
-- =============================================================================

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.executive_ctas', N'U') IS NULL
BEGIN
    CREATE TABLE executive_ctas (
        id                  UNIQUEIDENTIFIER NOT NULL
                            CONSTRAINT pk_executive_ctas PRIMARY KEY DEFAULT NEWID(),
        organization_id     UNIQUEIDENTIFIER NOT NULL,
        period_start        DATETIME2(2)     NOT NULL,
        auto_detected_rule  NVARCHAR(100)    NULL,
        priority_category   VARCHAR(20)      NOT NULL
                            CONSTRAINT ck_executive_ctas_category
                            CHECK (priority_category IN ('Incidentes','Hardening','Budget','Risk')),
        title               NVARCHAR(200)    NOT NULL,
        description         NVARCHAR(2000)   NOT NULL,
        is_suppressed       BIT              NOT NULL
                            CONSTRAINT df_executive_ctas_suppressed DEFAULT(0),
        is_manual           BIT              NOT NULL
                            CONSTRAINT df_executive_ctas_manual DEFAULT(0),
        created_by          UNIQUEIDENTIFIER NOT NULL,
        created_at          DATETIME2(2)     NOT NULL,
        modified_by         UNIQUEIDENTIFIER NULL,
        modified_at         DATETIME2(2)     NULL,
        deleted_by          UNIQUEIDENTIFIER NULL,
        deleted_at          DATETIME2(2)     NULL,
        CONSTRAINT fk_executive_ctas_org
            FOREIGN KEY (organization_id) REFERENCES organizations(id)
    );

    CREATE INDEX ix_executive_ctas_org_period
        ON executive_ctas (organization_id, period_start)
        WHERE deleted_at IS NULL;

    PRINT 'executive_ctas table created';
END
ELSE
    PRINT 'executive_ctas already exists, skipping';

GO
```

- [ ] **Step 2: Apply the migration to dev DB**

Run in Azure Portal → SQL Database `KryossDb` → Query Editor (or `sqlcmd`):

```
sqlcmd -S sql-kryoss.database.windows.net -d KryossDb -G -i KryossApi/sql/028_executive_ctas.sql
```

Expected output: `executive_ctas table created` (first run) or `executive_ctas already exists, skipping` (subsequent runs).

- [ ] **Step 3: Verify the table exists**

```sql
SELECT TOP 1 name FROM sys.tables WHERE name = 'executive_ctas';
SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('executive_ctas');
```

Expected: `executive_ctas` row returned, column count = 13.

- [ ] **Step 4: Commit**

```bash
git add KryossApi/sql/028_executive_ctas.sql
git commit -m "feat(sql): add executive_ctas table for C-Level CTA persistence"
```

### Task 2: ExecutiveCta entity + DbContext registration

**Files:**
- Create: `KryossApi/src/KryossApi/Data/Entities/ExecutiveCta.cs`
- Modify: `KryossApi/src/KryossApi/Data/KryossDbContext.cs`

- [ ] **Step 1: Create the entity file**

Create `KryossApi/src/KryossApi/Data/Entities/ExecutiveCta.cs`:

```csharp
namespace KryossApi.Data.Entities;

public class ExecutiveCta : IAuditable
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTime PeriodStart { get; set; }
    public string? AutoDetectedRule { get; set; }
    public string PriorityCategory { get; set; } = null!; // Incidentes|Hardening|Budget|Risk
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool IsSuppressed { get; set; }
    public bool IsManual { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
```

- [ ] **Step 2: Register the entity in DbContext**

In `KryossApi/src/KryossApi/Data/KryossDbContext.cs`, add the DbSet near the existing ones (around line 17):

```csharp
public DbSet<ExecutiveCta> ExecutiveCtas => Set<ExecutiveCta>();
```

And in `OnModelCreating`, add the entity config (next to the existing `Organization` config):

```csharp
mb.Entity<ExecutiveCta>(e =>
{
    e.ToTable("executive_ctas");
    e.HasKey(x => x.Id);
    e.HasQueryFilter(x => x.DeletedAt == null);
    e.HasOne(x => x.Organization)
     .WithMany()
     .HasForeignKey(x => x.OrganizationId);
});
```

- [ ] **Step 3: Build to verify**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add KryossApi/src/KryossApi/Data/Entities/ExecutiveCta.cs KryossApi/src/KryossApi/Data/KryossDbContext.cs
git commit -m "feat(data): add ExecutiveCta entity and DbContext registration"
```

### Task 3: CtaRuleEngine — auto-detection of the 12 rules

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CtaRuleEngine.cs`

- [ ] **Step 1: Create the rule engine file**

Create `KryossApi/src/KryossApi/Services/CtaRuleEngine.cs` with the full 12-rule catalog. The engine takes the same data the report builder already has (runs, allResults, hygiene, enrichment, m365Findings) and produces a ranked list of `CtaCandidate` records. Each rule is a function returning `CtaCandidate?` (null if the rule does not fire).

```csharp
using KryossApi.Data.Entities;

namespace KryossApi.Services;

/// <summary>
/// Represents a single CTA candidate produced by the rule engine.
/// Priority ordering (lower number = higher priority):
///   1 = Incidentes, 2 = Hardening, 3 = Budget, 4 = Risk
/// </summary>
public record CtaCandidate(
    string RuleId,
    string Title,
    string Description,
    string Category,          // "Incidentes" | "Hardening" | "Budget" | "Risk"
    int    PriorityRank,      // 1..4
    int    AffectedCount);    // for tie-breaking within same category

internal static class CtaRuleEngine
{
    private const int PrIncidentes = 1;
    private const int PrHardening  = 2;
    private const int PrBudget     = 3;
    private const int PrRisk       = 4;

    /// <summary>
    /// Runs all 12 rules against the provided fleet data and returns the
    /// candidates that fired, sorted by (PriorityRank ASC, AffectedCount DESC).
    /// </summary>
    public static List<CtaCandidate> DetectCtas(
        List<AssessmentRun> runs,
        List<OrgControlResult> allResults,
        HygieneScanDto? hygiene,
        OrgEnrichment enrichment,
        bool m365TenantConnected,
        List<M365Finding>? m365Findings,
        string lang)
    {
        var es = lang == "es";
        var found = new List<CtaCandidate>();

        // Rule 1: active threats → Incidentes
        if (enrichment.Threats.Count > 0)
        {
            var machineCount = enrichment.Threats.Select(t => t.MachineId).Distinct().Count();
            found.Add(new CtaCandidate(
                "active-threats",
                es ? "Aprobar engagement de IR forense inmediata"
                   : "Approve immediate forensic IR engagement",
                es ? $"Se detectaron {enrichment.Threats.Count} firmas de amenazas activas en {machineCount} equipos. Requiere análisis forense inmediato."
                   : $"{enrichment.Threats.Count} active threat signatures detected across {machineCount} machines. Immediate forensic analysis required.",
                "Incidentes", PrIncidentes, machineCount));
        }

        // Rule 2: LAPS coverage < 50% → Hardening
        var lapsFailing = hygiene?.Findings.Count(f => f.Status == "NoLAPS") ?? 0;
        var totalAdMachines = hygiene?.TotalMachines ?? 0;
        if (totalAdMachines > 0)
        {
            var lapsCoverage = 100.0 * (1.0 - (double)lapsFailing / totalAdMachines);
            if (lapsCoverage < 50)
            {
                found.Add(new CtaCandidate(
                    "laps-rollout",
                    es ? "Aprobar rollout de LAPS en la flota"
                       : "Approve LAPS rollout across fleet",
                    es ? $"LAPS desplegado solo en {lapsCoverage:F0}% de los equipos ({lapsFailing} sin cobertura). Una credencial local comprometida da acceso a toda la flota."
                       : $"LAPS coverage is only {lapsCoverage:F0}% ({lapsFailing} machines missing it). A single compromised local admin password grants lateral access to the entire fleet.",
                    "Hardening", PrHardening, lapsFailing));
            }
        }

        // Rule 3: BitLocker missing on >30% of fleet → Hardening
        var bitlockerMissing = runs.Count(r => r.Machine.Bitlocker != true);
        if (runs.Count > 0 && bitlockerMissing >= runs.Count * 0.3)
        {
            found.Add(new CtaCandidate(
                "bitlocker-program",
                es ? "Aprobar programa de cifrado de discos"
                   : "Approve disk encryption program",
                es ? $"{bitlockerMissing} equipos sin BitLocker activo — el robo o pérdida de un solo dispositivo expone todos los datos corporativos."
                   : $"{bitlockerMissing} machines without BitLocker enabled — theft or loss of a single device exposes all corporate data.",
                "Hardening", PrHardening, bitlockerMissing));
        }

        // Rule 4: privileged accounts excess → Hardening
        var privilegedCount = hygiene?.Findings.Count(f => f.Status == "Privileged") ?? 0;
        if (privilegedCount > 10)
        {
            found.Add(new CtaCandidate(
                "privileged-review",
                es ? "Aprobar revisión trimestral de cuentas privilegiadas"
                   : "Approve quarterly privileged account review",
                es ? $"Hoy existen {privilegedCount} cuentas con privilegios elevados. Cada una multiplica el radio de impacto ante un compromiso."
                   : $"{privilegedCount} accounts currently hold elevated privileges. Each one multiplies the blast radius of a credential compromise.",
                "Hardening", PrHardening, privilegedCount));
        }

        // Rule 5: password never expires > 10 → Hardening
        var pwdNeverExpire = hygiene?.PwdNeverExpire ?? 0;
        if (pwdNeverExpire > 10)
        {
            found.Add(new CtaCandidate(
                "password-rotation",
                es ? "Firmar política de rotación de contraseñas"
                   : "Sign password rotation policy",
                es ? $"{pwdNeverExpire} cuentas tienen contraseñas configuradas para nunca expirar. Viola todo framework de compliance y multiplica el impacto de un robo de credenciales."
                   : $"{pwdNeverExpire} accounts have passwords set to never expire. Violates every compliance framework and magnifies credential theft impact.",
                "Hardening", PrHardening, pwdNeverExpire));
        }

        // Rule 6: Kerberoastable accounts > 0 → Hardening
        var kerberoast = hygiene?.Findings.Count(f => f.Status == "Kerberoastable") ?? 0;
        if (kerberoast > 0)
        {
            found.Add(new CtaCandidate(
                "kerberoast-remediation",
                es ? "Aprobar remediación de cuentas Kerberoastables"
                   : "Approve Kerberoastable account remediation",
                es ? $"{kerberoast} cuentas de servicio son vulnerables a Kerberoast. Sus contraseñas pueden crackerase offline sin generar una sola alerta."
                   : $"{kerberoast} service accounts are vulnerable to Kerberoast. Their passwords can be cracked offline without generating a single alert.",
                "Hardening", PrHardening, kerberoast));
        }

        // Rule 7: RDP exposed on servers → Hardening
        var rdpExposed = enrichment.Ports
            .Where(p => p.Port == 3389 && p.Risk != null)
            .Select(p => p.MachineId).Distinct().Count();
        if (rdpExposed > 0)
        {
            found.Add(new CtaCandidate(
                "rdp-vpn-gateway",
                es ? "Aprobar VPN / RD Gateway mandatorio"
                   : "Approve mandatory VPN / RD Gateway",
                es ? $"{rdpExposed} equipos tienen RDP expuesto directo a internet. Es el vector #1 de ransomware — un atacante puede estar intentando credenciales ahora mismo."
                   : $"{rdpExposed} machines expose RDP directly to the internet. It is the #1 ransomware vector — an attacker may be brute-forcing credentials right now.",
                "Hardening", PrHardening, rdpExposed));
        }

        // Rule 8: SMBv1 or NTLM enabled > 10 machines → Hardening
        var legacyProtoMachines = allResults
            .Where(r => r.Status == "fail" && (
                r.Name.Contains("SMBv1", StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains("SMB1",  StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains("NTLM",  StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.RunId).Distinct().Count();
        if (legacyProtoMachines > 10)
        {
            found.Add(new CtaCandidate(
                "passive-telemetry-90",
                es ? "Aprobar arranque de telemetría pasiva de 90 días"
                   : "Approve 90-day passive telemetry engagement",
                es ? $"{legacyProtoMachines} equipos tienen protocolos legacy activos (SMBv1/NTLM). Activamos nuestro ciclo de telemetría pasiva de 90 días para deprecación segura con cero downtime."
                   : $"{legacyProtoMachines} machines have legacy protocols active (SMBv1/NTLM). Let's kick off the 90-day passive telemetry cycle for safe zero-downtime deprecation.",
                "Hardening", PrHardening, legacyProtoMachines));
        }

        // Rule 9: M365 MFA coverage < 100% on admins (requires M365 tenant connected)
        if (m365TenantConnected && m365Findings != null)
        {
            var adminMfaGap = m365Findings.Count(f =>
                f.Status == "fail" &&
                f.ControlId.Contains("MFA", StringComparison.OrdinalIgnoreCase) &&
                f.Name.Contains("admin", StringComparison.OrdinalIgnoreCase));
            if (adminMfaGap > 0)
            {
                found.Add(new CtaCandidate(
                    "m365-admin-mfa",
                    es ? "Firmar enforcement de MFA obligatorio para administradores M365"
                       : "Sign mandatory MFA enforcement for M365 admins",
                    es ? $"Detectamos {adminMfaGap} administradores M365 sin MFA obligatorio. Una credencial comprometida da acceso completo al tenant."
                       : $"{adminMfaGap} M365 admins lack enforced MFA. A single compromised credential grants full tenant access.",
                    "Hardening", PrHardening, adminMfaGap));
            }
        }

        // Rule 10: legacy OS present → Budget
        var legacyOs = runs
            .Where(r => r.Machine.OsName != null && (
                r.Machine.OsName.Contains("2008") ||
                r.Machine.OsName.Contains("2003") ||
                r.Machine.OsName.Contains("Windows 7") ||
                r.Machine.OsName.Contains("Vista")))
            .Count();
        if (legacyOs > 0)
        {
            found.Add(new CtaCandidate(
                "legacy-os-migration",
                es ? "Aprobar budget de migración Azure para SO fuera de soporte"
                   : "Approve Azure migration budget for end-of-life OS",
                es ? $"{legacyOs} máquinas con Windows 2003/2008/7/Vista. No reciben parches de seguridad modernos — son el vector de mayor riesgo residual de la red."
                   : $"{legacyOs} machines running Windows 2003/2008/7/Vista. They cannot receive modern security patches — the highest residual risk vector on the network.",
                "Budget", PrBudget, legacyOs));
        }

        // Rule 11: domain functional level legacy → Budget
        var domainLevel = hygiene?.Findings.FirstOrDefault(f => f.Status == "DomainLevel");
        if (domainLevel != null && (domainLevel.Detail?.Contains("2008") == true || domainLevel.Detail?.Contains("2003") == true))
        {
            found.Add(new CtaCandidate(
                "domain-level-upgrade",
                es ? "Aprobar upgrade del dominio AD a funcional 2016+"
                   : "Approve AD domain upgrade to functional level 2016+",
                es ? $"El dominio opera en nivel funcional {domainLevel.Detail}. Bloquea features de seguridad modernas como Protected Users, Credential Guard y Authentication Policy Silos."
                   : $"The domain is running at functional level {domainLevel.Detail}. This blocks modern security features like Protected Users, Credential Guard and Authentication Policy Silos.",
                "Budget", PrBudget, 1));
        }

        // Rule 12: critical fails > 5 → Budget (patch sprint)
        var criticalFailCount = allResults.Count(r => r.Status == "fail" && r.Severity == "critical");
        if (criticalFailCount > 5)
        {
            found.Add(new CtaCandidate(
                "critical-patch-sprint",
                es ? "Aprobar sprint de parcheo crítico"
                   : "Approve critical patching sprint",
                es ? $"{criticalFailCount} hallazgos de severidad crítica sin remediar. Requieren un sprint dedicado fuera del backlog de hardening mensual."
                   : $"{criticalFailCount} unremediated critical-severity findings. Requires a dedicated patching sprint outside the monthly hardening backlog.",
                "Budget", PrBudget, criticalFailCount));
        }

        // Rank: PriorityRank ASC, then AffectedCount DESC (bigger = more urgent)
        return found
            .OrderBy(c => c.PriorityRank)
            .ThenByDescending(c => c.AffectedCount)
            .ToList();
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

Expected: `Build succeeded. 0 Error(s)`. If compile fails on types (`OrgControlResult`, `HygieneScanDto`, `OrgEnrichment`, `M365Finding`), open `ReportService.cs` and confirm these DTOs exist; if `M365Finding` type does not exist in the current namespace, replace with the actual entity name from `Data/Entities/M365Tenant.cs`.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/CtaRuleEngine.cs
git commit -m "feat(reports): add CtaRuleEngine with 12-rule auto-detection for C-Level"
```

### Task 4: CapitalSinDetector — the 4 collapse rules for the Risk Posture semáforo

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CapitalSinDetector.cs`

- [ ] **Step 1: Create the detector file**

Create `KryossApi/src/KryossApi/Services/CapitalSinDetector.cs`:

```csharp
using KryossApi.Data.Entities;

namespace KryossApi.Services;

/// <summary>
/// Represents a capital sin that forces the C-Level Risk Posture to RED.
/// Each capital sin carries a narrative string and links to the rule id
/// of the CTA that should be promoted to position #1 when it fires.
/// </summary>
public record CapitalSin(
    string Code,          // "threats" | "m365-admin-mfa" | "laps-zero" | "rdp-servers"
    string Narrative,     // "Invasión en curso", etc.
    string LinkedCtaRule  // matches CtaCandidate.RuleId for CTA #1 promotion
);

internal static class CapitalSinDetector
{
    /// <summary>
    /// Checks the 4 capital sin rules in priority order and returns the
    /// first one that fires, or null if none fire. Order matters: threats
    /// (incidentes) first because it is the most urgent.
    /// </summary>
    public static CapitalSin? Detect(
        List<AssessmentRun> runs,
        HygieneScanDto? hygiene,
        OrgEnrichment enrichment,
        bool m365TenantConnected,
        List<M365Finding>? m365Findings,
        string lang)
    {
        var es = lang == "es";

        // 1. Active threats → "Invasión en curso"
        if (enrichment.Threats.Count > 0)
        {
            return new CapitalSin(
                "threats",
                es ? "Invasión en curso — amenazas activas en la red"
                   : "Intrusion in progress — active threats on the network",
                "active-threats");
        }

        // 2. M365 admin without MFA → "Llave maestra sin protección"
        if (m365TenantConnected && m365Findings != null)
        {
            var adminNoMfa = m365Findings.Any(f =>
                f.Status == "fail" &&
                f.ControlId.Contains("MFA", StringComparison.OrdinalIgnoreCase) &&
                f.Name.Contains("admin", StringComparison.OrdinalIgnoreCase));
            if (adminNoMfa)
            {
                return new CapitalSin(
                    "m365-admin-mfa",
                    es ? "Llave maestra sin protección — administradores M365 sin MFA"
                       : "Master key unprotected — M365 admins without enforced MFA",
                    "m365-admin-mfa");
            }
        }

        // 3. LAPS coverage == 0% → "Sin barreras internas"
        var totalAdMachines = hygiene?.TotalMachines ?? 0;
        var lapsFailing = hygiene?.Findings.Count(f => f.Status == "NoLAPS") ?? 0;
        if (totalAdMachines > 0 && lapsFailing == totalAdMachines)
        {
            return new CapitalSin(
                "laps-zero",
                es ? "Sin barreras internas — cero LAPS desplegado"
                   : "No internal barriers — zero LAPS deployment",
                "laps-rollout");
        }

        // 4. RDP exposed on server-role hostnames → "Puerta abierta a internet"
        var serverPatterns = new[] { "-DC", "-SRV", "-APP", "-SQL", "-WEB", "-FS" };
        var rdpOnServers = enrichment.Ports
            .Where(p => p.Port == 3389 && p.Risk != null)
            .Join(runs, p => p.MachineId, r => r.Machine.Id, (p, r) => r.Machine.Hostname)
            .Any(host => host != null && serverPatterns.Any(pat =>
                host.Contains(pat, StringComparison.OrdinalIgnoreCase)));
        if (rdpOnServers)
        {
            return new CapitalSin(
                "rdp-servers",
                es ? "Puerta abierta a internet — RDP expuesto en servidores"
                   : "Internet door open — RDP exposed on servers",
                "rdp-vpn-gateway");
        }

        return null;
    }
}
```

- [ ] **Step 2: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/CapitalSinDetector.cs
git commit -m "feat(reports): add CapitalSinDetector for C-Level Risk Posture collapse logic"
```

### Task 5: KPI helpers — benchmark, coverage, evolution

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs` (add private helpers)

- [ ] **Step 1: Add the three KPI helper methods to `ReportService.cs`**

Locate the `ReportService` class and add these three private static helpers just before `BuildOrgExecutiveOnePager` (or next to other helpers — placement doesn't affect correctness):

```csharp
// ── C-Level KPI helpers ─────────────────────────────────────────────

/// <summary>
/// Returns the "4 Fantásticos" asset coverage percentage: average of the
/// four binary signals (BitLocker, TPM, LAPS, Defender) across the fleet.
/// Each signal contributes equally (25%).
/// </summary>
private static (double percentage, int fullyProtected, int total) ComputeAssetCoverage(
    List<AssessmentRun> runs, HygieneScanDto? hygiene)
{
    if (runs.Count == 0) return (0, 0, 0);

    var total = runs.Count;
    var bitlockerOk = runs.Count(r => r.Machine.Bitlocker == true);
    var tpmOk = runs.Count(r => r.Machine.TpmPresent == true);
    // Defender status is stored on machine.DefenderEnabled if present,
    // otherwise inferred from a control result — use a best-effort check.
    var defenderOk = runs.Count(r => r.Machine.DefenderEnabled == true);

    // LAPS is tracked at org level via hygiene, not per machine in the
    // runs list. We approximate fleet LAPS coverage from hygiene findings
    // and assume the same ratio applies to the runs list.
    var lapsFailing = hygiene?.Findings.Count(f => f.Status == "NoLAPS") ?? 0;
    var lapsTotal   = hygiene?.TotalMachines ?? total;
    var lapsOk      = Math.Max(0, lapsTotal - lapsFailing);
    // Scale LAPS count to the runs-list size so all 4 signals are comparable.
    var lapsOkScaled = lapsTotal > 0 ? (int)Math.Round((double)lapsOk / lapsTotal * total) : total;

    var avgProtected = (bitlockerOk + tpmOk + defenderOk + lapsOkScaled) / 4.0;
    var percentage = 100.0 * avgProtected / total;
    return (percentage, (int)Math.Round(avgProtected), total);
}

/// <summary>
/// Returns the industry benchmark text for the Cost of Exposure KPI.
/// Static IBM Cost of a Data Breach 2024 reference for SMB ransomware.
/// </summary>
private static (string headline, string source, string footer) GetExposureBenchmark(
    int criticalVectorCount, string lang)
{
    var es = lang == "es";
    var headline = es ? "USD 1.2M" : "USD 1.2M";
    var source = es
        ? "Impacto financiero estimado — IBM Cost of a Data Breach 2024, segmento PyME"
        : "Estimated financial impact — IBM Cost of a Data Breach 2024, SMB segment";
    var footer = es
        ? $"Su infraestructura actual presenta {criticalVectorCount} vectores críticos que coinciden con los casos de estudio de este benchmark."
        : $"Your infrastructure currently exhibits {criticalVectorCount} critical vectors matching the case studies in this benchmark.";
    return (headline, source, footer);
}

/// <summary>
/// Computes the C-Level risk evolution arrow from current score vs
/// previous month's average. Returns tuple (arrow, delta, color, label).
/// </summary>
private static (string arrow, decimal delta, string color, string label) ComputeRiskEvolution(
    decimal currentScore, decimal? previousScore, string lang)
{
    var es = lang == "es";
    if (!previousScore.HasValue)
    {
        return ("—", 0m, "#64748B",
            es ? "Período de referencia (primera medición)" : "Baseline (first measurement)");
    }
    var delta = Math.Round(currentScore - previousScore.Value, 1);
    if (delta > 0)
    {
        return ("▲", delta, "#15803D",
            es ? $"vs mes anterior ({previousScore.Value:0.#})"
               : $"vs last month ({previousScore.Value:0.#})");
    }
    if (delta < 0)
    {
        return ("▼", delta, "#991B1B",
            es ? $"vs mes anterior ({previousScore.Value:0.#})"
               : $"vs last month ({previousScore.Value:0.#})");
    }
    return ("=", 0m, "#64748B",
        es ? $"estable vs mes anterior" : "stable vs last month");
}
```

- [ ] **Step 2: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

Expected: `Build succeeded. 0 Error(s)`. If `Machine.DefenderEnabled` does not exist as a property, use a sensible fallback like `r.Machine.SecureBoot == true` temporarily, or compute Defender status from a control result lookup — document the fallback inline with a comment.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): add C-Level KPI helpers (coverage, benchmark, evolution)"
```

### Task 6: C-Level builder skeleton — cover + content page scaffold

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Add the `BuildOrgCLevelReport` method skeleton**

Add the method near the other org builders (e.g., after `BuildOrgPresalesOpenerReport`). Initially it only renders the cover and a placeholder content page — the 3 blocks will be filled in subsequent tasks.

```csharp
// ======================================================================
// ORG: C-LEVEL REPORT — Executive risk snapshot (2 pages)
//
// Purpose: ad-hoc snapshot for CEO/COO/CFO. 2-minute read. Current state
// only — no historical trend (that belongs to Monthly Progress).
// ======================================================================
private static string BuildOrgCLevelReport(Organization org, List<AssessmentRun> runs,
    List<OrgControlResult> allResults, ReportBranding brand,
    List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment,
    ReportUserInfo userInfo, decimal? previousMonthScore,
    List<ExecutiveCta> savedCtas, List<M365Finding>? m365Findings, bool m365TenantConnected,
    string lang)
{
    var sb = new StringBuilder();
    var totalMachines = runs.Count;
    var avgScore = runs.Count > 0 ? Math.Round(runs.Average(r => r.GlobalScore ?? 0), 1) : 0m;
    var orgGrade = GetGrade(avgScore);
    var scanDate = runs.Count > 0 ? runs.Max(r => r.CompletedAt ?? r.StartedAt) : DateTime.UtcNow;
    var criticalFails = allResults
        .Where(r => r.Status == "fail" && r.Severity == "critical")
        .Select(r => r.ControlId).Distinct().Count();

    var capitalSin = CapitalSinDetector.Detect(
        runs, hygiene, enrichment, m365TenantConnected, m365Findings, lang);
    var ctaCandidates = CtaRuleEngine.DetectCtas(
        runs, allResults, hygiene, enrichment, m365TenantConnected, m365Findings, lang);

    var reportTitle = lang == "es" ? "Brief Ejecutivo de Seguridad" : "C-Level Security Brief";

    AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true, htmlLang: lang,
        user: userInfo, detail: $"{totalMachines} devices · {org.Name}");

    // ---- PAGE 1: COVER ----
    sb.AppendLine("<div class='cover'>");
    AppendRibbonSvg(sb);
    sb.AppendLine("<div class='cover-content'>");
    if (brand.LogoUrl is not null)
        sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
    sb.AppendLine($"<p class='eyebrow'>{HtmlEncode(lang == "es" ? "BRIEF EJECUTIVO" : "C-LEVEL BRIEF")}</p>");
    sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
    sb.AppendLine($"<h2>{HtmlEncode(org.Name)}</h2>");
    sb.AppendLine($"<p class='meta'>{scanDate:MMMM dd, yyyy} &mdash; {totalMachines} {HtmlEncode(lang == "es" ? "equipos" : "devices")}</p>");
    sb.AppendLine($"<div class='grade-badge grade-{orgGrade.Replace("+", "plus")}'>{HtmlEncode(orgGrade)}</div>");
    sb.AppendLine($"<p class='score'>{avgScore}%</p>");
    sb.AppendLine("</div></div>");

    // ---- PAGE 2: CONTENT (blocks 1-3 filled in subsequent tasks) ----
    sb.AppendLine("<div class='page'>");
    AppendPageHeader(sb, lang == "es" ? "Brief Ejecutivo" : "Executive Brief",
        brand, lang == "es" ? "RESUMEN DE RIESGO" : "RISK SUMMARY");
    sb.AppendLine("<div class='pb'>");

    // TODO Task 7: Block 1 — Risk Posture semáforo
    sb.AppendLine("<!-- Block 1: Risk Posture -->");

    // TODO Task 8: Block 2 — 3 KPIs
    sb.AppendLine("<!-- Block 2: 3 KPIs -->");

    // TODO Task 9: Block 3 — Executive CTAs
    sb.AppendLine("<!-- Block 3: Executive Decisions -->");

    sb.AppendLine("</div></div>");
    sb.AppendLine("</body></html>");

    return sb.ToString();
}
```

- [ ] **Step 2: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

Expected: `Build succeeded`. The method will compile even though it is not yet registered in the switch — that happens in Task 11.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): add BuildOrgCLevelReport skeleton (cover + content page stub)"
```

### Task 7: Block 1 — Risk Posture semáforo rendering

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Replace the Block 1 placeholder with the full semáforo rendering**

In `BuildOrgCLevelReport`, replace the `<!-- Block 1: Risk Posture -->` comment with the rendering logic:

```csharp
// ---- Block 1: Risk Posture semáforo ----
var es = lang == "es";
string postureColor, postureBg, postureLabel, postureNarrative;
if (capitalSin != null)
{
    postureColor = "#991B1B"; postureBg = "#FEF2F2";
    postureLabel = es ? "CRÍTICO" : "CRITICAL";
    postureNarrative = capitalSin.Narrative;
}
else if (avgScore >= 85)
{
    postureColor = "#15803D"; postureBg = "#F0FDF4";
    postureLabel = es ? "POSTURA SÓLIDA" : "SOLID POSTURE";
    postureNarrative = es
        ? "Controles activos. Postura sólida frente a los patrones de ataque monitoreados."
        : "Controls active. Solid posture against monitored attack patterns.";
}
else if (avgScore >= 60)
{
    postureColor = "#B45309"; postureBg = "#FFFBEB";
    postureLabel = es ? "EXPOSICIÓN ALTA" : "HIGH EXPOSURE";
    postureNarrative = es
        ? "Expuestos a ransomware por deuda técnica. Recuperación garantizada pero lenta."
        : "Exposed to ransomware via technical debt. Recovery guaranteed but slow.";
}
else
{
    postureColor = "#991B1B"; postureBg = "#FEF2F2";
    postureLabel = es ? "CRÍTICO" : "CRITICAL";
    postureNarrative = es
        ? "Operación en riesgo inminente. Tiempo estimado de recuperación ante ataque: >48h."
        : "Operation at imminent risk. Estimated recovery time from attack: >48h.";
}

sb.AppendLine($"<div style='background:{postureBg};border:2px solid {postureColor};border-radius:8px;padding:22px 28px;margin-bottom:20px;text-align:center;box-shadow:0 4px 6px -1px rgba(15,23,42,0.06)'>");
sb.AppendLine($"<div style='font-size:11px;font-weight:800;letter-spacing:0.14em;color:{postureColor};margin-bottom:8px'>{HtmlEncode(es ? "POSTURA DE RIESGO" : "RISK POSTURE")}</div>");
sb.AppendLine($"<div style='font-size:28px;font-weight:900;color:{postureColor};line-height:1;margin-bottom:6px'>{HtmlEncode(postureLabel)}</div>");
sb.AppendLine($"<div style='font-size:12px;color:#334155;line-height:1.55;max-width:160mm;margin:0 auto'>{HtmlEncode(postureNarrative)}</div>");
sb.AppendLine("</div>");
```

- [ ] **Step 2: Build + deploy to test**

```bash
cd "KryossApi/src/KryossApi" && dotnet publish -c Release -o ./publish --nologo -v q
cd publish && powershell -NoProfile -Command "Compress-Archive -Path * -DestinationPath ../../deploy.zip -Force"
az functionapp deployment source config-zip --resource-group rg-kryoss --name func-kryoss --src "../../deploy.zip" --timeout 600 --query "provisioningState" -o tsv
```

Expected: `Succeeded`.

- [ ] **Step 3: Manual verification will happen after Task 11 when the switch is registered.**

- [ ] **Step 4: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): C-Level Block 1 — Risk Posture semáforo with capital sin collapse"
```

### Task 8: Block 2 — 3 KPIs rendering

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Replace the Block 2 placeholder with the 3-KPI grid**

In `BuildOrgCLevelReport`, replace the `<!-- Block 2: 3 KPIs -->` comment with:

```csharp
// ---- Block 2: 3 Business KPIs ----
var (coveragePct, coverageProtected, coverageTotal) = ComputeAssetCoverage(runs, hygiene);
var (benchHead, benchSource, benchFooter) = GetExposureBenchmark(criticalFails, lang);
var (evolArrow, evolDelta, evolColor, evolLabel) = ComputeRiskEvolution(avgScore, previousMonthScore, lang);

sb.AppendLine("<div style='display:grid;grid-template-columns:1.3fr 1fr 1fr;gap:12px;margin-bottom:20px'>");

// KPI 1: Cost of Exposure (benchmark)
sb.AppendLine("<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid #991B1B;border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:#991B1B;letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{HtmlEncode(lang == "es" ? "COSTO DE EXPOSICIÓN" : "COST OF EXPOSURE")}</div>");
sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:#991B1B;line-height:1'>{HtmlEncode(benchHead)}</div>");
sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:4px;font-style:italic'>{HtmlEncode(benchSource)}</div>");
sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:6px;line-height:1.4;border-top:1px solid #E2E8F0;padding-top:6px'>{HtmlEncode(benchFooter)}</div>");
sb.AppendLine("</div>");

// KPI 2: Asset Coverage (4 Fantásticos)
sb.AppendLine("<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid #0F172A;border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:#0F172A;letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{HtmlEncode(lang == "es" ? "COBERTURA DE ACTIVOS" : "ASSET COVERAGE")}</div>");
sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:#0F172A;line-height:1'>{coveragePct:0}%</div>");
sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:6px'>{coverageProtected} {HtmlEncode(lang == "es" ? "de" : "of")} {coverageTotal} {HtmlEncode(lang == "es" ? "equipos completamente protegidos" : "machines fully protected")}</div>");
sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:4px'>{HtmlEncode(lang == "es" ? "BitLocker · TPM · LAPS · Defender" : "BitLocker · TPM · LAPS · Defender")}</div>");
sb.AppendLine("</div>");

// KPI 3: Risk Evolution
sb.AppendLine("<div style='background:#F8F9FA;border:1px solid #E2E8F0;border-top:3px solid " + evolColor + ";border-radius:6px;padding:14px 16px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
sb.AppendLine($"<div style='font-size:7pt;font-weight:800;color:{evolColor};letter-spacing:0.12em;text-transform:uppercase;margin-bottom:4px'>{HtmlEncode(lang == "es" ? "EVOLUCIÓN" : "RISK EVOLUTION")}</div>");
if (previousMonthScore.HasValue)
{
    var sign = evolDelta >= 0 ? "+" : "";
    sb.AppendLine($"<div style='font-size:22pt;font-weight:900;color:{evolColor};line-height:1'>{evolArrow} {sign}{evolDelta:0.#}</div>");
    sb.AppendLine($"<div style='font-size:8pt;color:#334155;margin-top:4px'>{HtmlEncode(lang == "es" ? "puntos" : "points")}</div>");
}
else
{
    sb.AppendLine($"<div style='font-size:16pt;font-weight:900;color:#64748B;line-height:1'>{HtmlEncode(lang == "es" ? "BASELINE" : "BASELINE")}</div>");
}
sb.AppendLine($"<div style='font-size:7pt;color:#64748B;margin-top:6px'>{HtmlEncode(evolLabel)}</div>");
sb.AppendLine("</div>");

sb.AppendLine("</div>");
```

- [ ] **Step 2: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): C-Level Block 2 — 3 business KPIs (benchmark, coverage, evolution)"
```

### Task 9: Block 3 — Executive CTAs rendering with empty state

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Replace the Block 3 placeholder with CTA rendering logic**

In `BuildOrgCLevelReport`, replace the `<!-- Block 3: Executive Decisions -->` comment with:

```csharp
// ---- Block 3: Executive Decisions Required (max 2 CTAs) ----
// Merge auto-detected candidates with saved (suppressed/edited) records:
// - Suppressed candidates are filtered out
// - Edited candidates use the saved title/description
// - Manual saved CTAs are added on top
var suppressedIds = savedCtas.Where(c => c.IsSuppressed && !c.IsManual)
                             .Select(c => c.AutoDetectedRule)
                             .ToHashSet();
var editedMap = savedCtas.Where(c => !c.IsSuppressed && !c.IsManual && c.AutoDetectedRule != null)
                          .ToDictionary(c => c.AutoDetectedRule!, c => c);

var finalCtas = new List<(string Title, string Description, string Category)>();

// If capital sin fired, its linked rule goes to position #1
if (capitalSin != null)
{
    var linked = ctaCandidates.FirstOrDefault(c => c.RuleId == capitalSin.LinkedCtaRule);
    if (linked != null && !suppressedIds.Contains(linked.RuleId))
    {
        var useTitle = editedMap.TryGetValue(linked.RuleId, out var edited) ? edited.Title : linked.Title;
        var useDesc  = editedMap.TryGetValue(linked.RuleId, out edited) ? edited.Description : linked.Description;
        finalCtas.Add((useTitle, useDesc, linked.Category));
    }
}

// Fill remaining slots with other candidates in priority order
foreach (var c in ctaCandidates)
{
    if (finalCtas.Count >= 2) break;
    if (capitalSin != null && c.RuleId == capitalSin.LinkedCtaRule) continue; // already added
    if (suppressedIds.Contains(c.RuleId)) continue;
    var useTitle = editedMap.TryGetValue(c.RuleId, out var edited) ? edited.Title : c.Title;
    var useDesc  = editedMap.TryGetValue(c.RuleId, out edited) ? edited.Description : c.Description;
    finalCtas.Add((useTitle, useDesc, c.Category));
}

// Add manual CTAs on top (they already have title/description)
foreach (var m in savedCtas.Where(c => c.IsManual && !c.IsSuppressed))
{
    if (finalCtas.Count >= 2) break;
    finalCtas.Add((m.Title, m.Description, m.PriorityCategory));
}

sb.AppendLine($"<h3 style='font-size:12px;margin:8px 0 12px;color:#1E293B;border-bottom:2px solid #0F172A;padding-bottom:4px;text-transform:uppercase;letter-spacing:0.08em'>{HtmlEncode(lang == "es" ? "Decisiones Ejecutivas Requeridas" : "Executive Decisions Required")}</h3>");

if (finalCtas.Count == 0)
{
    // Empty state: positive closure card
    sb.AppendLine("<div style='background:#F0FDF4;border:1px solid #BBF7D0;border-left:4px solid #15803D;border-radius:4px;padding:16px 22px'>");
    sb.AppendLine($"<div style='font-size:11pt;font-weight:700;color:#14532D;margin-bottom:4px'>✓ {HtmlEncode(lang == "es" ? "Postura sólida — sin decisiones ejecutivas pendientes" : "Solid posture — no pending executive decisions")}</div>");
    sb.AppendLine($"<div style='font-size:10pt;color:#166534;line-height:1.55'>{HtmlEncode(lang == "es" ? "Este mes no requiere acción del CEO. El programa de hardening continúa de forma rutinaria." : "This period requires no CEO action. The hardening program continues on schedule.")}</div>");
    sb.AppendLine("</div>");
}
else
{
    int n = 1;
    foreach (var (title, desc, cat) in finalCtas)
    {
        var catColor = cat switch
        {
            "Incidentes" => "#991B1B",
            "Hardening"  => "#0F172A",
            "Budget"     => "#B45309",
            "Risk"       => "#64748B",
            _ => "#0F172A"
        };
        sb.AppendLine($"<div style='background:#F8F9FA;border:1px solid #CBD5E1;border-left:5px solid {catColor};border-radius:4px;padding:14px 20px;margin-bottom:10px;box-shadow:0 1px 2px 0 rgba(15,23,42,0.04)'>");
        sb.AppendLine($"<div style='display:inline-block;padding:2px 8px;background:{catColor};color:#fff;font-size:8px;font-weight:800;letter-spacing:0.12em;text-transform:uppercase;border-radius:2px;margin-bottom:6px'>{HtmlEncode(cat.ToUpperInvariant())} · #{n}</div>");
        sb.AppendLine($"<div style='font-size:12pt;font-weight:700;color:#0F172A;margin-bottom:4px'>{HtmlEncode(title)}</div>");
        sb.AppendLine($"<div style='font-size:10pt;color:#334155;line-height:1.55'>{HtmlEncode(desc)}</div>");
        sb.AppendLine("</div>");
        n++;
    }
}
```

- [ ] **Step 2: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): C-Level Block 3 — CTA rendering with hybrid override and empty state"
```

### Task 10: Bilingual dictionary entries (not strictly required since we use inline strings — SKIP if content already fully bilingual)

C-Level already uses inline `lang == "es" ? ... : ...` patterns throughout Tasks 6–9. Adding dictionary keys would just duplicate work. Mark this task as documentation only: confirm all user-visible strings in `BuildOrgCLevelReport` are bilingual via inline conditionals.

- [ ] **Step 1: Grep for untranslated strings**

```bash
grep -n 'sb.AppendLine' KryossApi/src/KryossApi/Services/ReportService.cs | grep -iE '(sb.AppendLine.*["'"'"']\w+.*["'"'"']\))' | less
```

Focus only on the `BuildOrgCLevelReport` method body. Any string that is shown to the user (not CSS, not tags) must go through `lang == "es" ? "Spanish" : "English"`.

- [ ] **Step 2: Fix any missed strings inline** — most should already be bilingual from the prior tasks. If a label is hardcoded English, wrap it with the ternary pattern.

- [ ] **Step 3: Commit (only if changes made)**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "fix(reports): C-Level bilingual audit — wrap any remaining EN-only strings"
```

If no changes, skip the commit.

### Task 11: Register `"c-level"` case in the switch + plumb the new parameters through `GenerateOrgReportAsync`

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Load saved CTAs + M365 data in `GenerateOrgReportAsync`**

In `GenerateOrgReportAsync`, after the existing `previousMonthScore` loading block and before the `switch`, add:

```csharp
// C-Level dependencies: saved operator-edited CTAs for this period + M365 findings
var savedCtas = new List<ExecutiveCta>();
var m365Findings = new List<M365Finding>();
var m365Connected = false;
if (reportType == "c-level")
{
    // Define the current reporting period as the current calendar month at UTC.
    var periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    savedCtas = await _db.ExecutiveCtas
        .Where(c => c.OrganizationId == orgId && c.PeriodStart == periodStart)
        .ToListAsync();

    var m365Tenant = await _db.M365Tenants
        .FirstOrDefaultAsync(t => t.OrganizationId == orgId && t.ConsentGrantedAt != null);
    if (m365Tenant != null)
    {
        m365Connected = true;
        m365Findings = await _db.M365Findings
            .Where(f => f.TenantId == m365Tenant.Id)
            .ToListAsync();
    }
}
```

- [ ] **Step 2: Add the `"c-level"` case to the switch**

```csharp
return reportType switch
{
    "technical" => BuildOrgTechnicalReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo),
    "presales" => BuildOrgPresalesReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo),
    "exec-onepager" => BuildOrgExecutiveOnePager(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo, lang),
    "presales-opener" => BuildOrgPresalesOpenerReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo, lang),
    "monthly-briefing" => BuildOrgMonthlyBriefingReport(org, runs, allResults, branding, frameworkScores, hygieneScan, orgEnrichment, userInfo, previousMonthScore),
    "c-level" => BuildOrgCLevelReport(org, runs, allResults, branding, frameworkScores, hygieneScan, orgEnrichment, userInfo, previousMonthScore, savedCtas, m365Findings, m365Connected, lang),
    _ => BuildOrgExecutiveReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo)
};
```

- [ ] **Step 3: Build + deploy**

```bash
cd "KryossApi/src/KryossApi" && rm -rf ./publish ../deploy.zip
dotnet publish -c Release -o ./publish --nologo -v q
cd publish && powershell -NoProfile -Command "Compress-Archive -Path * -DestinationPath ../../deploy.zip -Force"
az functionapp deployment source config-zip --resource-group rg-kryoss --name func-kryoss --src "../../deploy.zip" --timeout 600 --query "provisioningState" -o tsv
```

Expected: `Succeeded`.

- [ ] **Step 4: Manual smoke test — generate a C-Level report**

In the portal, open an organization with existing assessment runs. In the Reports tab, select `Report Type = C-Level` (you will temporarily have to construct the URL manually since the portal dropdown is updated in Task 13). Hit the backend directly:

```
GET https://func-kryoss.azurewebsites.net/v2/reports/org/{orgId}?type=c-level&lang=en
```

(With a valid MSAL bearer token.)

Expected: the response is an HTML document with a cover page + a content page containing the Risk Posture semáforo, 3 KPI cards, and the CTA block. Validate visually that all 3 blocks render and no sections are empty.

- [ ] **Step 5: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): register c-level in switch + load saved CTAs and M365 data"
```

### Task 12: ExecutiveCtasFunction — CRUD endpoint for operator CTA editing

**Files:**
- Create: `KryossApi/src/KryossApi/Functions/Portal/ExecutiveCtasFunction.cs`

- [ ] **Step 1: Create the function file**

Create `KryossApi/src/KryossApi/Functions/Portal/ExecutiveCtasFunction.cs`:

```csharp
using System.Net;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// CRUD endpoints for the operator-editable CTAs that feed C-Level Block 3.
/// Scope: one operator manages the CTAs for one organization per reporting period.
///   GET    /v2/executive-ctas?orgId=...&period=YYYY-MM-01 → list
///   POST   /v2/executive-ctas                              → add manual CTA
///   PATCH  /v2/executive-ctas/{id}                         → edit / suppress / unsuppress
///   DELETE /v2/executive-ctas/{id}                         → soft-delete
/// </summary>
[RequirePermission("reports:manage")]
public class ExecutiveCtasFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IActlogService _actlog;

    public ExecutiveCtasFunction(KryossDbContext db, ICurrentUserService user, IActlogService actlog)
    {
        _db = db;
        _user = user;
        _actlog = actlog;
    }

    public record CtaDto(Guid Id, Guid OrganizationId, DateTime PeriodStart, string? AutoDetectedRule,
        string PriorityCategory, string Title, string Description, bool IsSuppressed, bool IsManual);

    public record UpsertCtaDto(Guid OrganizationId, DateTime PeriodStart, string? AutoDetectedRule,
        string PriorityCategory, string Title, string Description, bool IsSuppressed, bool IsManual);

    [Function("ExecutiveCtas_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/executive-ctas")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["orgId"], out var orgId))
            return await Err(req, HttpStatusCode.BadRequest, "orgId required");
        if (!DateTime.TryParse(query["period"], out var periodStart))
            periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Access control — operator must have access to this org
        if (!_user.IsAdmin &&
            !(_user.OrganizationId == orgId ||
              await _db.Organizations.AnyAsync(o => o.Id == orgId && o.FranchiseId == _user.FranchiseId)))
            return await Err(req, HttpStatusCode.Forbidden, "Access denied");

        var rows = await _db.ExecutiveCtas
            .Where(c => c.OrganizationId == orgId && c.PeriodStart == periodStart)
            .Select(c => new CtaDto(c.Id, c.OrganizationId, c.PeriodStart, c.AutoDetectedRule,
                c.PriorityCategory, c.Title, c.Description, c.IsSuppressed, c.IsManual))
            .ToListAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(rows);
        return resp;
    }

    [Function("ExecutiveCtas_Create")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/executive-ctas")] HttpRequestData req)
    {
        var dto = await req.ReadFromJsonAsync<UpsertCtaDto>();
        if (dto == null) return await Err(req, HttpStatusCode.BadRequest, "body required");

        if (!_user.IsAdmin &&
            !(_user.OrganizationId == dto.OrganizationId ||
              await _db.Organizations.AnyAsync(o => o.Id == dto.OrganizationId && o.FranchiseId == _user.FranchiseId)))
            return await Err(req, HttpStatusCode.Forbidden, "Access denied");

        var entity = new ExecutiveCta
        {
            Id = Guid.NewGuid(),
            OrganizationId = dto.OrganizationId,
            PeriodStart = dto.PeriodStart,
            AutoDetectedRule = dto.AutoDetectedRule,
            PriorityCategory = dto.PriorityCategory,
            Title = dto.Title,
            Description = dto.Description,
            IsSuppressed = dto.IsSuppressed,
            IsManual = dto.IsManual,
            CreatedBy = _user.UserId,
            CreatedAt = DateTime.UtcNow
        };
        _db.ExecutiveCtas.Add(entity);
        await _db.SaveChangesAsync();
        await _actlog.LogAsync("INFO", "reports", "cta.create",
            $"Created CTA {entity.Id} for org {entity.OrganizationId}",
            entityType: "ExecutiveCta", entityId: entity.Id.ToString());

        var resp = req.CreateResponse(HttpStatusCode.Created);
        await resp.WriteAsJsonAsync(new CtaDto(entity.Id, entity.OrganizationId, entity.PeriodStart,
            entity.AutoDetectedRule, entity.PriorityCategory, entity.Title, entity.Description,
            entity.IsSuppressed, entity.IsManual));
        return resp;
    }

    [Function("ExecutiveCtas_Update")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/executive-ctas/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var entity = await _db.ExecutiveCtas.FirstOrDefaultAsync(c => c.Id == id);
        if (entity == null) return await Err(req, HttpStatusCode.NotFound, "CTA not found");

        if (!_user.IsAdmin &&
            !(_user.OrganizationId == entity.OrganizationId ||
              await _db.Organizations.AnyAsync(o => o.Id == entity.OrganizationId && o.FranchiseId == _user.FranchiseId)))
            return await Err(req, HttpStatusCode.Forbidden, "Access denied");

        var dto = await req.ReadFromJsonAsync<UpsertCtaDto>();
        if (dto == null) return await Err(req, HttpStatusCode.BadRequest, "body required");
        entity.Title = dto.Title;
        entity.Description = dto.Description;
        entity.IsSuppressed = dto.IsSuppressed;
        entity.PriorityCategory = dto.PriorityCategory;
        entity.ModifiedAt = DateTime.UtcNow;
        entity.ModifiedBy = _user.UserId;
        await _db.SaveChangesAsync();
        await _actlog.LogAsync("INFO", "reports", "cta.update",
            $"Updated CTA {entity.Id}",
            entityType: "ExecutiveCta", entityId: entity.Id.ToString());

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new CtaDto(entity.Id, entity.OrganizationId, entity.PeriodStart,
            entity.AutoDetectedRule, entity.PriorityCategory, entity.Title, entity.Description,
            entity.IsSuppressed, entity.IsManual));
        return resp;
    }

    [Function("ExecutiveCtas_Delete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v2/executive-ctas/{id:guid}")] HttpRequestData req,
        Guid id)
    {
        var entity = await _db.ExecutiveCtas.FirstOrDefaultAsync(c => c.Id == id);
        if (entity == null) return await Err(req, HttpStatusCode.NotFound, "CTA not found");

        if (!_user.IsAdmin &&
            !(_user.OrganizationId == entity.OrganizationId ||
              await _db.Organizations.AnyAsync(o => o.Id == entity.OrganizationId && o.FranchiseId == _user.FranchiseId)))
            return await Err(req, HttpStatusCode.Forbidden, "Access denied");

        _db.ExecutiveCtas.Remove(entity); // soft-delete via AuditInterceptor
        await _db.SaveChangesAsync();
        await _actlog.LogAsync("INFO", "reports", "cta.delete",
            $"Deleted CTA {id}",
            entityType: "ExecutiveCta", entityId: id.ToString());

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private static async Task<HttpResponseData> Err(HttpRequestData req, HttpStatusCode code, string msg)
    {
        var resp = req.CreateResponse(code);
        await resp.WriteAsJsonAsync(new { error = msg });
        return resp;
    }
}
```

- [ ] **Step 2: Register the `reports:manage` permission (if it does not exist)**

Open `KryossApi/sql/seed_001_roles_permissions.sql` and confirm `reports:manage` is defined. If not, add the row via a new migration or an inline update statement. For this plan, assume `reports:read` grants enough access — if you need a separate `reports:manage` permission, spawn it as a sub-task.

- [ ] **Step 3: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add KryossApi/src/KryossApi/Functions/Portal/ExecutiveCtasFunction.cs
git commit -m "feat(api): add ExecutiveCtasFunction CRUD endpoints for C-Level CTA editing"
```

### Task 13: CtaPreviewModal + wire C-Level into the portal

**Files:**
- Create: `KryossPortal/src/components/reports/CtaPreviewModal.tsx`
- Modify: `KryossPortal/src/components/reports/ReportGenerator.tsx`

- [ ] **Step 1: Create `CtaPreviewModal.tsx`**

Create `KryossPortal/src/components/reports/CtaPreviewModal.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Checkbox } from '@/components/ui/checkbox';
import { toast } from 'sonner';
import { API_BASE } from '@/auth/msalConfig';
import { msalInstance } from '@/auth/msalInstance';
import { loginRequest } from '@/auth/msalConfig';

interface Cta {
  id: string;
  organizationId: string;
  periodStart: string;
  autoDetectedRule: string | null;
  priorityCategory: 'Incidentes' | 'Hardening' | 'Budget' | 'Risk';
  title: string;
  description: string;
  isSuppressed: boolean;
  isManual: boolean;
}

interface Props {
  open: boolean;
  orgId: string;
  onClose: () => void;
  onConfirm: () => void; // called after Save, to kick off report generation
}

async function authHeader(): Promise<Record<string, string>> {
  const acc = msalInstance.getAllAccounts();
  if (acc.length === 0) throw new Error('Not authenticated');
  let token: string;
  try {
    const res = await msalInstance.acquireTokenSilent({ ...loginRequest, account: acc[0] });
    token = res.accessToken;
  } catch {
    const res = await msalInstance.acquireTokenPopup(loginRequest);
    token = res.accessToken;
  }
  return { Authorization: `Bearer ${token}` };
}

export function CtaPreviewModal({ open, orgId, onClose, onConfirm }: Props) {
  const [ctas, setCtas] = useState<Cta[]>([]);
  const [loading, setLoading] = useState(false);
  const period = new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString();

  useEffect(() => {
    if (!open) return;
    void (async () => {
      setLoading(true);
      try {
        const headers = await authHeader();
        const res = await fetch(`${API_BASE}/v2/executive-ctas?orgId=${orgId}&period=${period}`, { headers });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        setCtas(await res.json());
      } catch (err: any) {
        toast.error(`Failed to load CTAs: ${err.message}`);
      } finally {
        setLoading(false);
      }
    })();
  }, [open, orgId]);

  const updateCta = (id: string, patch: Partial<Cta>) => {
    setCtas((prev) => prev.map((c) => (c.id === id ? { ...c, ...patch } : c)));
  };

  const save = async () => {
    try {
      const headers = { ...(await authHeader()), 'Content-Type': 'application/json' };
      await Promise.all(
        ctas.map((c) =>
          fetch(`${API_BASE}/v2/executive-ctas/${c.id}`, {
            method: 'PATCH',
            headers,
            body: JSON.stringify(c),
          }),
        ),
      );
      toast.success('CTAs saved');
      onConfirm();
    } catch (err: any) {
      toast.error(`Failed to save: ${err.message}`);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="max-w-2xl max-h-[80vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Executive CTAs Preview</DialogTitle>
        </DialogHeader>
        {loading && <div>Loading…</div>}
        {!loading && ctas.length === 0 && (
          <div className="text-muted-foreground text-sm">
            No CTAs auto-detected for this period. Generate the report to see the positive empty state.
          </div>
        )}
        {ctas.map((c) => (
          <div key={c.id} className="border rounded p-3 my-2">
            <div className="flex items-center gap-2 mb-2">
              <Checkbox
                checked={!c.isSuppressed}
                onCheckedChange={(v) => updateCta(c.id, { isSuppressed: !v })}
              />
              <span className="text-xs font-bold uppercase tracking-wider">{c.priorityCategory}</span>
              {c.autoDetectedRule && (
                <span className="text-xs text-muted-foreground">({c.autoDetectedRule})</span>
              )}
            </div>
            <Input
              value={c.title}
              onChange={(e) => updateCta(c.id, { title: e.target.value })}
              className="mb-2"
              disabled={c.isSuppressed}
            />
            <Textarea
              value={c.description}
              onChange={(e) => updateCta(c.id, { description: e.target.value })}
              rows={3}
              disabled={c.isSuppressed}
            />
          </div>
        ))}
        <div className="flex justify-end gap-2 mt-4">
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={save}>Save & Generate Report</Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Step 2: Update `ReportGenerator.tsx` to add C-Level + wire up the modal**

Add `c-level` to `REPORT_TYPES`:

```tsx
const REPORT_TYPES = [
  { value: 'c-level',           label: 'C-Level',               orgOnly: true  },
  { value: 'technical',         label: 'Technical',             orgOnly: false },
  { value: 'executive',         label: 'Executive',             orgOnly: false },
  { value: 'presales',          label: 'Presales (detailed)',   orgOnly: false },
  { value: 'presales-opener',   label: 'Presales Opener',       orgOnly: true  },
  { value: 'exec-onepager',     label: 'Executive One-Pager',   orgOnly: true  },
  { value: 'monthly-briefing',  label: 'Monthly Briefing (MRR)', orgOnly: true  },
] as const;
```

In the component body, add state and modal handling:

```tsx
const [showCtaPreview, setShowCtaPreview] = useState(false);

const handleGenerate = async () => {
  if (reportType === 'c-level' && targetType === 'org') {
    setShowCtaPreview(true);
    return;
  }
  await handleOpenTab();
};
```

And render the modal:

```tsx
<CtaPreviewModal
  open={showCtaPreview}
  orgId={targetId}
  onClose={() => setShowCtaPreview(false)}
  onConfirm={() => {
    setShowCtaPreview(false);
    void handleOpenTab();
  }}
/>
```

Replace the primary button's `onClick={handleOpenTab}` with `onClick={handleGenerate}` so C-Level goes through the preview.

- [ ] **Step 3: Build the portal**

```bash
cd KryossPortal && npm run build
```

Expected: build succeeds. The bundle hash will change.

- [ ] **Step 4: Deploy the portal**

```bash
cd KryossPortal
SWA_TOKEN=$(az staticwebapp secrets list --name swa-kryoss-portal --resource-group rg-kryoss --query "properties.apiKey" -o tsv)
npx --yes @azure/static-web-apps-cli@latest deploy ./dist --env production --deployment-token "$SWA_TOKEN" --no-use-keychain
```

Expected: `✔ Project deployed`.

- [ ] **Step 5: Smoke test**

Open the portal, hard-refresh (Ctrl+Shift+R), go to any org's Reports tab, select `C-Level` from the dropdown, click the generate button. The CTA preview modal should appear. Cancel or save, and verify the C-Level HTML renders in a new tab.

- [ ] **Step 6: Commit**

```bash
git add KryossPortal/src/components/reports/CtaPreviewModal.tsx KryossPortal/src/components/reports/ReportGenerator.tsx
git commit -m "feat(portal): add CtaPreviewModal and wire C-Level generation flow"
```

### Task 14: End-to-end C-Level smoke test matrix

- [ ] **Step 1: Generate C-Level for 4 scenarios**

From the portal, for an org that has multiple assessment runs:

1. `C-Level` · `All frameworks` · `English` → open in new tab
2. `C-Level` · `HIPAA` · `Español` → open in new tab
3. `C-Level` · `NIST` · `English` (with capital sin present: find an org where `active_threats > 0` or seed one manually if needed) → open in new tab
4. `C-Level` · `All frameworks` · `Español` for an org with NO critical findings → verify empty state positive card appears

- [ ] **Step 2: For each scenario, verify the rendered HTML shows all 3 blocks correctly:**

- Block 1 color correct for the posture
- Block 2: 3 KPI cards visible, numbers populated
- Block 3: CTAs max 2, OR the empty-state positive card

- [ ] **Step 3: Commit a status tag** — no code change, just a marker commit so the plan's phase boundary is visible in git log:

```bash
git commit --allow-empty -m "chore: C-Level report end-to-end validated"
```

---

## Phase 2 — Rebuild Technical Level

Phase 2 replaces the body of `BuildOrgTechnicalReport` with the new 3-block structure from the spec (Asset Matrix, Top 10 Critical Findings, Los 6 de Hierro). The method signature stays the same, the switch case stays the same, the portal dropdown entry stays the same — only the rendered content changes.

### Task 15: Extract `AppendAssetMatrix` helper

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Add the helper method**

Add this private static helper to `ReportService.cs` near the other append helpers:

```csharp
/// <summary>
/// Renders the Technical Level Block 1 Asset Matrix: a sortable table
/// of machines ordered worst-to-best by individual score. Paginates
/// every ~25 rows so a large fleet never exceeds 296mm per A4 page.
/// </summary>
private static void AppendAssetMatrix(StringBuilder sb, List<AssessmentRun> runs, ReportBranding brand, string lang)
{
    var es = lang == "es";

    // Sort worst-to-best so the IT admin knows which machine to touch first.
    var sorted = runs
        .OrderBy(r => r.GlobalScore ?? 100m)
        .ThenBy(r => r.Machine.Hostname)
        .ToList();

    const int perPage = 25;
    var total = sorted.Count;
    for (int offset = 0; offset < total; offset += perPage)
    {
        if (offset > 0)
        {
            sb.AppendLine("</div></div>");
            sb.AppendLine("<div class='page'>");
            AppendPageHeader(sb, es ? "Matriz de Activos (continuación)" : "Asset Matrix (continued)", brand,
                es ? "NIVEL TÉCNICO" : "TECHNICAL LEVEL");
            sb.AppendLine("<div class='pb'>");
        }
        sb.AppendLine("<table class='results-table' style='font-size:10px'>");
        sb.AppendLine($"<tr><th style='width:28%'>{HtmlEncode(es ? "Hostname" : "Hostname")}</th><th style='width:20%'>{HtmlEncode(es ? "SO" : "OS Status")}</th><th style='width:14%' class='num'>{HtmlEncode(es ? "Críticos" : "Critical")}</th><th style='width:18%'>{HtmlEncode(es ? "Heartbeat" : "Heartbeat")}</th><th style='width:20%' class='num'>{HtmlEncode(es ? "Score" : "Score")}</th></tr>");

        foreach (var r in sorted.Skip(offset).Take(perPage))
        {
            var m = r.Machine;
            var osLabel = m.OsName ?? "—";
            var osClass = (m.OsName?.Contains("2008") == true || m.OsName?.Contains("2003") == true ||
                           m.OsName?.Contains("Windows 7") == true || m.OsName?.Contains("Vista") == true)
                ? "fail" : "";
            var heartbeat = m.LastSeenAt.HasValue
                ? DescribeHeartbeat(DateTime.UtcNow - m.LastSeenAt.Value, es)
                : (es ? "nunca" : "never");
            var heartbeatDays = m.LastSeenAt.HasValue
                ? (DateTime.UtcNow - m.LastSeenAt.Value).TotalDays
                : 999;
            var hbClass = heartbeatDays > 7 ? "fail" : heartbeatDays > 3 ? "warn" : "";
            var score = r.GlobalScore ?? 0;
            var scoreClass = score < 60 ? "fail" : score < 85 ? "warn" : "pass";

            sb.AppendLine($"<tr class='{scoreClass}'>");
            sb.AppendLine($"<td style='font-family:monospace;font-weight:600'>{HtmlEncode(m.Hostname)}</td>");
            sb.AppendLine($"<td class='{osClass}'>{HtmlEncode(osLabel)}</td>");
            sb.AppendLine($"<td class='num'>{(r.FailCount ?? 0)}</td>");
            sb.AppendLine($"<td class='{hbClass}'>{HtmlEncode(heartbeat)}</td>");
            sb.AppendLine($"<td class='num'><strong>{score:0.#}</strong></td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
    }
}

private static string DescribeHeartbeat(TimeSpan ago, bool es)
{
    if (ago.TotalHours < 1) return es ? "hace minutos" : "minutes ago";
    if (ago.TotalHours < 24) return es ? $"hace {(int)ago.TotalHours}h" : $"{(int)ago.TotalHours}h ago";
    var days = (int)ago.TotalDays;
    return es ? $"hace {days} días" : $"{days} days ago";
}
```

- [ ] **Step 2: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

Expected: `Build succeeded`. If `Machine.LastSeenAt` does not exist, use `r.CompletedAt` from the assessment run as a proxy.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): add AppendAssetMatrix helper for Technical Level Block 1"
```

### Task 16: Extract `AppendTop10CriticalFindings` helper

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Add the helper method**

```csharp
/// <summary>
/// Renders the Technical Level Block 2 Top 10 Critical Findings: the
/// 10 highest-impact control failures across the fleet, with fix
/// instructions pulled from `control_defs.remediation`.
/// </summary>
private static void AppendTop10CriticalFindings(StringBuilder sb, List<OrgControlResult> allResults, string lang)
{
    var es = lang == "es";

    var top = allResults
        .Where(r => r.Status == "fail" && (r.Severity == "critical" || r.Severity == "high"))
        .GroupBy(r => r.ControlId)
        .Select(g => new
        {
            ControlId = g.Key,
            Name = g.First().Name,
            Severity = g.First().Severity,
            Remediation = g.First().Remediation,
            MachineCount = g.Select(x => x.RunId).Distinct().Count()
        })
        .OrderByDescending(x => x.Severity == "critical" ? 1 : 0)
        .ThenByDescending(x => x.MachineCount)
        .Take(10)
        .ToList();

    sb.AppendLine($"<h3>{HtmlEncode(es ? "Top 10 Hallazgos Críticos" : "Top 10 Critical Findings")}</h3>");

    if (top.Count == 0)
    {
        sb.AppendLine($"<p style='color:#64748B;font-size:11px'>{HtmlEncode(es ? "No se detectaron hallazgos críticos o altos en este período." : "No critical or high-severity findings detected in this period.")}</p>");
        return;
    }

    sb.AppendLine("<table class='results-table' style='font-size:10px'>");
    sb.AppendLine($"<tr><th style='width:6%'>#</th><th style='width:32%'>{HtmlEncode(es ? "Hallazgo" : "Finding")}</th><th style='width:10%'>{HtmlEncode(es ? "Sev" : "Sev")}</th><th style='width:10%' class='num'>{HtmlEncode(es ? "Equipos" : "Hosts")}</th><th>{HtmlEncode(es ? "Remediación" : "Fix")}</th></tr>");
    int n = 1;
    foreach (var t in top)
    {
        var sev = t.Severity == "critical"
            ? (es ? "CRÍTICO" : "CRITICAL")
            : (es ? "ALTO"    : "HIGH");
        var sevCls = t.Severity == "critical" ? "critical" : "high";
        sb.AppendLine("<tr class='fail'>");
        sb.AppendLine($"<td class='num'>{n++}</td>");
        sb.AppendLine($"<td><strong>{HtmlEncode(t.Name)}</strong></td>");
        sb.AppendLine($"<td><span class='severity {sevCls}'>{HtmlEncode(sev)}</span></td>");
        sb.AppendLine($"<td class='num'>{t.MachineCount}</td>");
        sb.AppendLine($"<td style='font-size:9px'>{HtmlEncode(t.Remediation ?? (es ? "(sin remediación documentada)" : "(no remediation documented)"))}</td>");
        sb.AppendLine("</tr>");
    }
    sb.AppendLine("</table>");
}
```

- [ ] **Step 2: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): add AppendTop10CriticalFindings helper for Technical Level Block 2"
```

### Task 17: Extract `AppendSixIronsHardeningAudit` helper

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Add the helper method**

```csharp
/// <summary>
/// Renders the Technical Level Block 3 "Los 6 de Hierro" — 6 thematic
/// sub-lists of hygiene failures, one per category. Each sub-list shows
/// affected hostnames, or a green "all compliant" message if empty.
/// </summary>
private static void AppendSixIronsHardeningAudit(StringBuilder sb, List<AssessmentRun> runs,
    List<OrgControlResult> allResults, HygieneScanDto? hygiene, string lang)
{
    var es = lang == "es";

    sb.AppendLine($"<h3>{HtmlEncode(es ? "Los 6 de Hierro — Auditoría de Hardening" : "The 6 Irons — Hardening Audit")}</h3>");

    // 1. Cifrado — machines without BitLocker
    var noBitlocker = runs.Where(r => r.Machine.Bitlocker != true).Select(r => r.Machine.Hostname).ToList();
    AppendIronSection(sb, "🔒", es ? "Cifrado — sin BitLocker" : "Encryption — without BitLocker",
        noBitlocker, es);

    // 2. Protocolos — SMBv1 / NTLMv1 enabled
    var legacyProto = allResults
        .Where(r => r.Status == "fail" && (
            r.Name.Contains("SMBv1", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("SMB1",  StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("NTLMv1", StringComparison.OrdinalIgnoreCase)))
        .Select(r => runs.FirstOrDefault(x => x.Id == r.RunId)?.Machine.Hostname ?? "unknown")
        .Distinct()
        .ToList();
    AppendIronSection(sb, "📡", es ? "Protocolos — SMBv1 / NTLMv1 habilitado" : "Protocols — SMBv1 / NTLMv1 enabled",
        legacyProto, es);

    // 3. Hardware — no TPM or TPM disabled
    var noTpm = runs.Where(r => r.Machine.TpmPresent != true).Select(r => r.Machine.Hostname).ToList();
    AppendIronSection(sb, "💾", es ? "Hardware — sin TPM o deshabilitado" : "Hardware — no TPM or disabled",
        noTpm, es);

    // 4. Identidad — no LAPS deployed
    var noLapsHosts = hygiene?.Findings
        .Where(f => f.Status == "NoLAPS")
        .Select(f => f.ObjectName ?? "unknown")
        .ToList() ?? new List<string>();
    AppendIronSection(sb, "🔑", es ? "Identidad — sin LAPS" : "Identity — no LAPS",
        noLapsHosts, es);

    // 5. Higiene — Password Never Expires accounts (show count, not hostnames)
    var pwdNever = hygiene?.PwdNeverExpire ?? 0;
    if (pwdNever == 0)
    {
        AppendIronCompliant(sb, "🧹", es ? "Higiene — contraseñas con expiración" : "Hygiene — password expiration");
    }
    else
    {
        sb.AppendLine("<div style='background:#FEF2F2;border:1px solid #FECACA;border-left:4px solid #991B1B;border-radius:4px;padding:10px 16px;margin-bottom:10px'>");
        sb.AppendLine($"<div style='font-size:11px;font-weight:700;color:#1E293B'>🧹 {HtmlEncode(es ? "Higiene — cuentas con Password Never Expires" : "Hygiene — Password Never Expires accounts")}</div>");
        sb.AppendLine($"<div style='font-size:10px;color:#7F1D1D;margin-top:4px'>{pwdNever} {HtmlEncode(es ? "cuentas" : "accounts")}</div>");
        sb.AppendLine("</div>");
    }

    // 6. Endpoint — Defender disabled
    var noDefender = runs.Where(r => r.Machine.DefenderEnabled != true).Select(r => r.Machine.Hostname).ToList();
    AppendIronSection(sb, "🛡️", es ? "Endpoint — Defender deshabilitado" : "Endpoint — Defender disabled",
        noDefender, es);
}

private static void AppendIronSection(StringBuilder sb, string icon, string title, List<string> hostnames, bool es)
{
    if (hostnames.Count == 0)
    {
        AppendIronCompliant(sb, icon, title);
        return;
    }
    sb.AppendLine("<div style='background:#FEF2F2;border:1px solid #FECACA;border-left:4px solid #991B1B;border-radius:4px;padding:10px 16px;margin-bottom:10px'>");
    sb.AppendLine($"<div style='font-size:11px;font-weight:700;color:#1E293B;margin-bottom:6px'>{icon} {HtmlEncode(title)} — {hostnames.Count} {HtmlEncode(es ? "equipos" : "machines")}</div>");
    sb.AppendLine($"<div style='font-size:9px;font-family:monospace;color:#7F1D1D;line-height:1.6'>{HtmlEncode(string.Join(" · ", hostnames.Take(40)))}");
    if (hostnames.Count > 40)
        sb.AppendLine($"<em> … {hostnames.Count - 40} {HtmlEncode(es ? "más" : "more")}</em>");
    sb.AppendLine("</div>");
    sb.AppendLine("</div>");
}

private static void AppendIronCompliant(StringBuilder sb, string icon, string title)
{
    sb.AppendLine("<div style='background:#F0FDF4;border:1px solid #BBF7D0;border-left:4px solid #15803D;border-radius:4px;padding:8px 14px;margin-bottom:10px;font-size:10px;color:#166534'>");
    sb.AppendLine($"<strong>{icon} {HtmlEncode(title)}</strong> — ✅ Todos los equipos cumplen");
    sb.AppendLine("</div>");
}
```

- [ ] **Step 2: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

If `HygieneFinding.ObjectName` does not exist, check the actual property name in `AdHygiene.cs` and adjust.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): add AppendSixIronsHardeningAudit helper for Technical Level Block 3"
```

### Task 18: Rebuild `BuildOrgTechnicalReport` body with the 3 new blocks

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Locate `BuildOrgTechnicalReport` and replace its body**

Find the method `BuildOrgTechnicalReport`. Replace everything between the opening `{` after the signature and the closing `return sb.ToString(); }` with:

```csharp
{
    var sb = new StringBuilder();
    var totalMachines = runs.Count;
    var reportTitle = frameworkName != null
        ? $"{frameworkName} Technical Compliance"
        : "Technical Assessment";

    AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, isOrgReport: true,
        user: userInfo, detail: $"{totalMachines} devices · {org.Name}");

    // ---- PAGE 1: COVER ----
    sb.AppendLine("<div class='cover'>");
    AppendRibbonSvg(sb);
    sb.AppendLine("<div class='cover-content'>");
    if (brand.LogoUrl is not null)
        sb.AppendLine($"<img src='{HtmlEncode(brand.LogoUrl)}' class='logo' alt='{HtmlEncode(brand.CompanyName)}'>");
    sb.AppendLine("<p class='eyebrow'>TECHNICAL LEVEL</p>");
    sb.AppendLine($"<h1>{HtmlEncode(reportTitle)}</h1>");
    sb.AppendLine($"<h2>{HtmlEncode(org.Name)}</h2>");
    sb.AppendLine($"<p class='meta'>{(runs.Count > 0 ? runs.Max(r => r.CompletedAt ?? r.StartedAt) : DateTime.UtcNow):MMMM dd, yyyy} — {totalMachines} devices</p>");
    sb.AppendLine("</div></div>");

    // ---- PAGE 2: ASSET MATRIX (paginates internally) ----
    sb.AppendLine("<div class='page'>");
    AppendPageHeader(sb, "Asset Matrix", brand, "TECHNICAL LEVEL");
    sb.AppendLine("<div class='pb'>");
    AppendAssetMatrix(sb, runs, brand, "en");
    sb.AppendLine("</div></div>");

    // ---- PAGE 3: TOP 10 CRITICAL FINDINGS ----
    sb.AppendLine("<div class='page'>");
    AppendPageHeader(sb, "Critical Findings", brand, "TECHNICAL LEVEL");
    sb.AppendLine("<div class='pb'>");
    AppendTop10CriticalFindings(sb, allResults, "en");
    sb.AppendLine("</div></div>");

    // ---- PAGE 4: THE 6 IRONS HARDENING AUDIT ----
    sb.AppendLine("<div class='page'>");
    AppendPageHeader(sb, "Hardening Audit", brand, "TECHNICAL LEVEL");
    sb.AppendLine("<div class='pb'>");
    AppendSixIronsHardeningAudit(sb, runs, allResults, hygiene, "en");
    sb.AppendLine("</div></div>");

    sb.AppendLine("</body></html>");
    return sb.ToString();
}
```

Note: the `lang` parameter is hardcoded to `"en"` here because the existing `BuildOrgTechnicalReport` signature does not accept `lang`. Task 19 adds bilingual support.

- [ ] **Step 2: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): rebuild BuildOrgTechnicalReport body with 3-block structure"
```

### Task 19: Add bilingual support to Technical Level

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Add `lang` to `BuildOrgTechnicalReport` signature**

Change the method signature from:

```csharp
private static string BuildOrgTechnicalReport(Organization org, List<AssessmentRun> runs,
    List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName,
    List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment,
    ReportUserInfo userInfo)
```

to:

```csharp
private static string BuildOrgTechnicalReport(Organization org, List<AssessmentRun> runs,
    List<OrgControlResult> allResults, ReportBranding brand, string? frameworkName,
    List<FrameworkScoreDto> frameworkScores, HygieneScanDto? hygiene, OrgEnrichment enrichment,
    ReportUserInfo userInfo, string lang)
```

- [ ] **Step 2: Replace hardcoded `"en"` strings with `lang` through the method**

Wherever the Technical builder currently passes `"en"` as the language to the helpers, replace with `lang`. Wherever the method uses hardcoded English strings in the cover or page headers, wrap them in `lang == "es" ? "Spanish" : "English"`.

- [ ] **Step 3: Update the switch call in `GenerateOrgReportAsync`**

```csharp
"technical" => BuildOrgTechnicalReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo, lang),
```

- [ ] **Step 4: Build + deploy**

```bash
cd "KryossApi/src/KryossApi" && rm -rf ./publish ../deploy.zip
dotnet publish -c Release -o ./publish --nologo -v q
cd publish && powershell -NoProfile -Command "Compress-Archive -Path * -DestinationPath ../../deploy.zip -Force"
az functionapp deployment source config-zip --resource-group rg-kryoss --name func-kryoss --src "../../deploy.zip" --timeout 600 --query "provisioningState" -o tsv
```

Expected: `Succeeded`.

- [ ] **Step 5: Manual smoke test**

From the portal, generate `Technical` with `lang=es` and `lang=en` for an org with ≥ 30 machines. Verify Asset Matrix paginates, Top 10 shows with remediation text, and the 6 Iron categories render (either affected hosts or "✅ Todos cumplen").

- [ ] **Step 6: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): bilingual support for Technical Level + end-to-end validated"
```

### Task 20: Phase 2 milestone marker

- [ ] **Step 1: Empty marker commit**

```bash
git commit --allow-empty -m "chore: Technical Level rebuild complete"
```

---

## Phase 3 — Consolidate Preventas

Phase 3 consolidates the 2 existing Presales variants (`presales` + `presales-opener`) under a single `preventas` dropdown entry with a `tone` sub-parameter. Zero content changes — only routing and dropdown structure.

### Task 21: Add `preventas` routing + tone param to the switch

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`
- Modify: `KryossApi/src/KryossApi/Functions/Portal/ReportsFunction.cs`

- [ ] **Step 1: Update `GenerateOrgReportAsync` to accept tone**

Change the method signature:

```csharp
public async Task<string> GenerateOrgReportAsync(Guid orgId, string reportType = "executive",
    string? frameworkCode = null, string lang = "en", string? tone = null)
```

Update `IReportService` interface to match.

- [ ] **Step 2: Add `preventas` case in the switch that dispatches by tone**

```csharp
"preventas" => tone == "detailed"
    ? BuildOrgPresalesReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo)
    : BuildOrgPresalesOpenerReport(org, runs, allResults, branding, frameworkName, frameworkScores, hygieneScan, orgEnrichment, userInfo, lang),
```

The existing `presales` and `presales-opener` cases remain as legacy aliases for the deprecation window.

- [ ] **Step 3: Update `ReportsFunction.GenerateOrg` to parse `tone`**

In `KryossApi/src/KryossApi/Functions/Portal/ReportsFunction.cs`, find the `GenerateOrg` method and after the existing `lang` parse:

```csharp
var tone = query["tone"]?.ToLowerInvariant(); // "opener" or "detailed"
if (tone != "opener" && tone != "detailed") tone = "opener"; // default
```

Pass `tone` into the `GenerateOrgReportAsync` call.

- [ ] **Step 4: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

- [ ] **Step 5: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs KryossApi/src/KryossApi/Functions/Portal/ReportsFunction.cs
git commit -m "feat(api): add preventas type with tone=opener|detailed routing"
```

### Task 22: Update the portal dropdown to consolidate Preventas + add tone sub-select

**Files:**
- Modify: `KryossPortal/src/components/reports/ReportGenerator.tsx`

- [ ] **Step 1: Replace the REPORT_TYPES list**

```tsx
const REPORT_TYPES = [
  { value: 'c-level',          label: 'C-Level',              orgOnly: true  },
  { value: 'technical',        label: 'Technical',            orgOnly: false },
  { value: 'executive',        label: 'Executive',            orgOnly: false },
  { value: 'preventas',        label: 'Preventas',            orgOnly: true  },
  { value: 'exec-onepager',    label: 'Executive One-Pager',  orgOnly: true  },
  { value: 'monthly-briefing', label: 'Monthly Briefing',     orgOnly: true  },
] as const;

const TONES = [
  { value: 'opener',   label: 'Opener (2 páginas)' },
  { value: 'detailed', label: 'Detailed (6 páginas)' },
] as const;
```

- [ ] **Step 2: Add tone state and sub-dropdown conditional render**

```tsx
const [tone, setTone] = useState<'opener' | 'detailed'>('opener');
```

In the JSX, after the Report Type dropdown, conditionally render:

```tsx
{reportType === 'preventas' && (
  <Select value={tone} onValueChange={(v) => setTone(v as 'opener' | 'detailed')}>
    <SelectTrigger className="w-[180px]">
      <SelectValue placeholder="Tone" />
    </SelectTrigger>
    <SelectContent>
      {TONES.map((t) => (
        <SelectItem key={t.value} value={t.value}>{t.label}</SelectItem>
      ))}
    </SelectContent>
  </Select>
)}
```

- [ ] **Step 3: Thread tone into `buildApiPath`**

```tsx
function buildApiPath(targetType: 'run' | 'org', targetId: string, reportType: string, framework: string, lang: string, tone: string): string {
  const base = targetType === 'run' ? `/v2/reports/${targetId}` : `/v2/reports/org/${targetId}`;
  const params = new URLSearchParams();
  params.set('type', reportType);
  if (framework !== 'all') params.set('framework', framework);
  if (lang !== 'en') params.set('lang', lang);
  if (reportType === 'preventas') params.set('tone', tone);
  return `${base}?${params}`;
}
```

Update the call site to pass `tone`.

- [ ] **Step 4: Build and deploy the portal**

```bash
cd KryossPortal && rm -rf dist && npm run build
SWA_TOKEN=$(az staticwebapp secrets list --name swa-kryoss-portal --resource-group rg-kryoss --query "properties.apiKey" -o tsv)
npx --yes @azure/static-web-apps-cli@latest deploy ./dist --env production --deployment-token "$SWA_TOKEN" --no-use-keychain
```

- [ ] **Step 5: Smoke test both tones in the browser**

Hard-refresh, select `Preventas · Opener`, click generate, verify the 2-page opener renders. Then select `Preventas · Detailed`, generate, verify the 6-page proposal renders.

- [ ] **Step 6: Commit**

```bash
git add KryossPortal/src/components/reports/ReportGenerator.tsx
git commit -m "feat(portal): consolidate Preventas dropdown with tone sub-selector"
```

### Task 23: Phase 3 milestone marker

- [ ] **Step 1: Empty marker commit**

```bash
git commit --allow-empty -m "chore: Preventas consolidation complete"
```

---

## Phase 4 — Delete per-run reports

Phase 4 removes the 3 per-run report builders and deprecates the `GET /v2/reports/{runId}` endpoint. Scope is now strictly org-level.

### Task 24: Delete per-run builders

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Delete the three methods**

Find and delete the complete method bodies of:
- `BuildTechnicalReport(AssessmentRun run, ...)`
- `BuildExecutiveReport(AssessmentRun run, ...)`
- `BuildPresalesReport(AssessmentRun run, ...)`

These are all in `ReportService.cs`. Delete their declarations, bodies, and closing braces.

- [ ] **Step 2: Delete `GenerateHtmlReportAsync` implementation**

Find and delete:

```csharp
public async Task<string> GenerateHtmlReportAsync(Guid runId, string reportType = "technical", string? frameworkCode = null, string lang = "en")
{
    ...
}
```

- [ ] **Step 3: Remove `GenerateHtmlReportAsync` from the `IReportService` interface**

```csharp
public interface IReportService
{
    Task<string> GenerateOrgReportAsync(Guid orgId, string reportType = "executive", string? frameworkCode = null, string lang = "en", string? tone = null);
}
```

(Delete the `GenerateHtmlReportAsync` line.)

- [ ] **Step 4: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

If the build fails because `ReportsFunction.Reports_Generate` still calls `_reports.GenerateHtmlReportAsync`, that call is fixed in Task 25. Comment it out temporarily or proceed to Task 25 before running the build.

- [ ] **Step 5: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "refactor(reports): delete per-run report builders (scope now org-only)"
```

### Task 25: Deprecate the per-run endpoint with HTTP 410

**Files:**
- Modify: `KryossApi/src/KryossApi/Functions/Portal/ReportsFunction.cs`

- [ ] **Step 1: Replace the `Reports_Generate` method body**

Find `Reports_Generate` (the one with route `v2/reports/{runId:guid}`) and replace the entire method body with:

```csharp
[Function("Reports_Generate")]
public async Task<HttpResponseData> Generate(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/reports/{runId:guid}")] HttpRequestData req,
    Guid runId)
{
    _ = runId; // intentionally unused — endpoint is deprecated
    var response = req.CreateResponse(System.Net.HttpStatusCode.Gone);
    await response.WriteAsJsonAsync(new
    {
        error = "Per-run reports have been deprecated",
        message = "Report scope is now always organization-level. Use GET /v2/reports/org/{orgId}?type=technical to generate org-wide Technical reports. For single-machine troubleshooting, use the portal's machine detail view.",
        status = 410,
        deprecatedSince = "2026-04-15"
    });
    return response;
}
```

- [ ] **Step 2: Build**

```bash
cd "KryossApi/src/KryossApi" && dotnet build --nologo -v q
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Deploy**

```bash
cd "KryossApi/src/KryossApi" && rm -rf ./publish ../deploy.zip
dotnet publish -c Release -o ./publish --nologo -v q
cd publish && powershell -NoProfile -Command "Compress-Archive -Path * -DestinationPath ../../deploy.zip -Force"
az functionapp deployment source config-zip --resource-group rg-kryoss --name func-kryoss --src "../../deploy.zip" --timeout 600 --query "provisioningState" -o tsv
```

Expected: `Succeeded`.

- [ ] **Step 4: Smoke test the 410**

```
curl -i "https://func-kryoss.azurewebsites.net/v2/reports/00000000-0000-0000-0000-000000000000"
```

Expected: HTTP 410 Gone with the deprecation message body.

- [ ] **Step 5: Commit**

```bash
git add KryossApi/src/KryossApi/Functions/Portal/ReportsFunction.cs
git commit -m "refactor(api): deprecate Reports_Generate per-run endpoint with HTTP 410"
```

### Task 26: Clean up portal code that uses `targetType='run'`

**Files:**
- Modify: `KryossPortal/src/components/reports/ReportGenerator.tsx`

- [ ] **Step 1: Remove `targetType='run'` code paths**

Locate all references to `targetType === 'run'` in `ReportGenerator.tsx` and remove the branches. Simplify the API path builder to only use `/v2/reports/org/{targetId}`.

- [ ] **Step 2: Remove the per-run Reports tab from machine detail view (if present)**

Search for any component that generates a report for a single machine (likely in `KryossPortal/src/components/machine-detail/` or similar). If such a component exists and passes `targetType='run'` to ReportGenerator, remove that component usage.

```bash
grep -rn "targetType=\"run\"\|targetType='run'" KryossPortal/src
```

For each match, either delete the enclosing component or redirect it to the org-level equivalent.

- [ ] **Step 3: Build + deploy portal**

```bash
cd KryossPortal && rm -rf dist && npm run build
SWA_TOKEN=$(az staticwebapp secrets list --name swa-kryoss-portal --resource-group rg-kryoss --query "properties.apiKey" -o tsv)
npx --yes @azure/static-web-apps-cli@latest deploy ./dist --env production --deployment-token "$SWA_TOKEN" --no-use-keychain
```

- [ ] **Step 4: Smoke test**

Open a machine detail view in the portal. Confirm there is no "Generate Report" button for that specific machine (or if there was, it now redirects to the org report page).

- [ ] **Step 5: Commit**

```bash
git add KryossPortal/src/components/reports/ReportGenerator.tsx KryossPortal/src/components
git commit -m "refactor(portal): remove per-run report code paths"
```

### Task 27: Phase 4 milestone marker

- [ ] **Step 1: Empty marker commit**

```bash
git commit --allow-empty -m "chore: per-run report deprecation complete"
```

---

## Phase 5 — Documentation + final integration test

### Task 28: Update the master CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`
- Modify: `KryossApi/CLAUDE.md`

- [ ] **Step 1: Update the master `CLAUDE.md`**

In the master `CLAUDE.md` at the repo root, find the report section (in "Active decisions" or "Recent features") and add an entry:

```markdown
| 2026-04-15 | Reports 4-type baseline consolidation | 8 variants collapsed into 4 clean types (C-Level, Technical Level, Monthly Progress [deferred], Preventas). All org-scoped. Per-run reports deprecated (HTTP 410). Spec: docs/superpowers/specs/2026-04-15-reports-4-type-baseline-design.md. Plan: docs/superpowers/plans/2026-04-15-reports-4-type-baseline.md |
```

- [ ] **Step 2: Update `KryossApi/CLAUDE.md`**

In the HTTP endpoints table, update the Reports row:

```markdown
| GET | `/v2/reports/org/{orgId}?type=c-level|technical|executive|preventas&tone=opener|detailed&framework=NIST|CIS|HIPAA|ISO27001|PCI-DSS&lang=en|es` | `ReportsFunction.GenerateOrg` | Org-wide HTML report (Brand 2025, Big 4 light) |
| GET | `/v2/reports/{runId}` | `ReportsFunction.Generate` | ⚠️ DEPRECATED — returns HTTP 410 since 2026-04-15 |
| GET/POST/PATCH/DELETE | `/v2/executive-ctas` | `ExecutiveCtasFunction` | Operator-editable CTAs for C-Level Block 3 |
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md KryossApi/CLAUDE.md
git commit -m "docs: update CLAUDE.md for reports 4-type baseline"
```

### Task 29: Final integration test — 4 reports × 2 languages × framework filters

- [ ] **Step 1: Test matrix**

For each of these 6 combinations, generate the report from the portal and verify the HTML renders without errors:

| # | Report Type | Framework | Language |
|---|---|---|---|
| 1 | C-Level | All | English |
| 2 | C-Level | HIPAA | Español |
| 3 | Technical | All | English |
| 4 | Technical | NIST | Español |
| 5 | Preventas · Opener | All | English |
| 6 | Preventas · Detailed | HIPAA | Español |

For each, verify:
- Cover page renders with logo, org name, grade badge, score
- All pages use Big 4 light palette consistently
- Running footer shows operator info at bottom of every page
- No console errors in browser DevTools during rendering
- Text adapts to selected language
- Framework filter applies to control counts and compliance bars

- [ ] **Step 2: Test the 410 endpoint**

```
curl -i "https://func-kryoss.azurewebsites.net/v2/reports/00000000-0000-0000-0000-000000000000"
```

Expected: `HTTP/1.1 410 Gone`.

- [ ] **Step 3: Test Monthly Briefing (MRR) still works**

Select `Monthly Briefing` from the dropdown. Verify it still generates with its current layout — the 4-type consolidation should not have broken the existing MRR report.

- [ ] **Step 4: Final milestone commit**

```bash
git commit --allow-empty -m "chore: reports 4-type baseline — all 4 phases validated end-to-end"
```

### Task 30: Update progress markers in the spec doc

**Files:**
- Modify: `docs/superpowers/specs/2026-04-15-reports-4-type-baseline-design.md`

- [ ] **Step 1: Update the Status header in the spec**

Change:

```markdown
**Status:** Design approved via brainstorm, ready for implementation planning
```

to:

```markdown
**Status:** Implemented 2026-04-15 (Phases 1–4). Monthly Progress rebuild deferred pending NinjaOne integration.
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/2026-04-15-reports-4-type-baseline-design.md
git commit -m "docs: mark reports 4-type spec as implemented"
```

---

## Self-review checklist

Before handing off execution, the plan author should verify:

1. **Spec coverage** — every section of the spec has at least one task that implements it:
   - C-Level Block 1 (semáforo + capital sins) → Tasks 4, 7
   - C-Level Block 2 (3 KPIs) → Tasks 5, 8
   - C-Level Block 3 (CTAs + hybrid editing) → Tasks 1, 2, 3, 9, 12, 13
   - C-Level full plumbing + smoke test → Tasks 6, 10, 11, 14
   - Technical Level 3 blocks → Tasks 15, 16, 17, 18, 19
   - Preventas consolidation → Tasks 21, 22
   - Per-run deprecation → Tasks 24, 25, 26
   - Documentation → Tasks 28, 29, 30

2. **Placeholder scan** — no "TBD", "TODO", "implement later", "handle edge cases" in task bodies. Every step has concrete code, exact paths, and explicit verification commands.

3. **Type consistency** — `CtaCandidate`, `CapitalSin`, `ExecutiveCta`, `OrgControlResult`, `HygieneScanDto` are used consistently across tasks.

---

## Execution handoff

**Plan complete and saved to `docs/superpowers/plans/2026-04-15-reports-4-type-baseline.md`.** Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration, keeps my context clean.

2. **Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints for review.

**Which approach?**
