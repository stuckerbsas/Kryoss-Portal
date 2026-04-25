# Unified Report System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the monolithic 3866-line `ReportService.cs` + separate cloud report services with a compositional block architecture supporting 7 report types with integrated cloud data and auto-generated business proposals.

**Architecture:** 18 reusable HTML blocks composed into 7 recipes via a `ReportComposer`. A unified `ReportDataLoader` loads all data (endpoint + cloud + hygiene + benchmarks + service catalog) once per request. Each block implements `IReportBlock.Render(ReportData, ReportOptions)` returning self-contained HTML. Three new SQL tables: `service_catalog`, `franchise_service_rates`, `executive_ctas`.

**Tech Stack:** .NET 8, EF Core 8, Azure Functions v4 isolated worker, Azure SQL, HTML/CSS report rendering (no external template engine).

**Spec:** `docs/superpowers/specs/2026-04-19-unified-report-system-design.md`

---

## File Structure

### New files to create

```
Services/Reports/
├── IReportBlock.cs                    — IReportBlock + IReportRecipe interfaces
├── ReportData.cs                      — unified data model (endpoint + cloud + hygiene + catalog)
├── ReportOptions.cs                   — lang, framework, tone record
├── ReportDataLoader.cs                — loads all data from DB once per request
├── ReportComposer.cs                  — orchestrates recipe → blocks → final HTML
├── ReportStyles.cs                    — shared CSS (extracted from ReportService lines 3366-3800)
├── ReportHelpers.cs                   — shared helpers (AppendHtmlHead, grade, ribbon, footer — lines 1177-2867)
├── Blocks/
│   ├── CoverBlock.cs                  — cover page (brand, org, date, type title)
│   ├── SemaforoBlock.cs               — survival traffic light + 4 capital sins
│   ├── KpiBlock.cs                    — 3 executive KPIs + optional cloud KPI
│   ├── CtaBlock.cs                    — 12-rule auto CTA engine + manual CTAs
│   ├── AssetMatrixBlock.cs            — machine-by-machine table, paginated 25/page
│   ├── TopFindingsBlock.cs            — top N critical findings cross-fleet
│   ├── IronSixBlock.cs                — los 6 de hierro hardening audit
│   ├── FrameworkGaugeBlock.cs         — visual gauge per framework + grade
│   ├── GapAnalysisBlock.cs            — failing controls grouped by framework/category
│   ├── CloudPostureBlock.cs           — 5-area radar + cloud findings
│   ├── ScoreTrendBlock.cs             — 6-month sparkline evolution
│   ├── DeltaBlock.cs                  — resolved vs new findings this period
│   ├── RiskScoreBlock.cs              — aggressive presales risk framing
│   ├── ThreatVectorsBlock.cs          — top compromise vectors
│   ├── MethodologyBlock.cs            — 90-day safe deprecation
│   ├── ServiceCatalogBlock.cs         — auto-calculated pricing table
│   └── TimelineBlock.cs              — Gantt-style remediation roadmap
└── Recipes/
    ├── CLevelRecipe.cs
    ├── TechnicalRecipe.cs
    ├── PreventaOpenerRecipe.cs
    ├── PreventaDetailedRecipe.cs
    ├── MonthlyRecipe.cs
    ├── FrameworkRecipe.cs
    └── ProposalRecipe.cs
```

### New SQL files

```
sql/039_service_catalog.sql             — service_catalog + franchise_service_rates tables
sql/seed_039_service_catalog.sql        — 14 service categories seed
```

### Files to modify

```
Services/ReportService.cs               — gutted, replaced by ReportComposer delegation
Functions/Portal/ReportsFunction.cs     — route through ReportComposer, add new types
Program.cs                              — DI registration for new services
Data/KryossDbContext.cs                 — add DbSets for ServiceCatalog, FranchiseServiceRate
Data/Entities/ServiceCatalog.cs         — new entity (create)
Data/Entities/FranchiseServiceRate.cs   — new entity (create)
```

### Existing `executive_ctas` table

Already defined in migration 028. Entity `ExecutiveCta.cs` already exists. No new migration needed for CTAs.

---

## Module 1: Infrastructure (interfaces + data model + SQL)

### Task 1.1: SQL migrations — service_catalog + franchise_service_rates

**Files:**
- Create: `sql/039_service_catalog.sql`
- Create: `sql/seed_039_service_catalog.sql`

- [ ] **Step 1: Create schema migration**

```sql
-- sql/039_service_catalog.sql
-- Unified Report System: service catalog for auto-generated proposals

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'service_catalog')
CREATE TABLE service_catalog (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    category_code   VARCHAR(30)    NOT NULL,
    name_en         NVARCHAR(100)  NOT NULL,
    name_es         NVARCHAR(100)  NOT NULL,
    unit_type       VARCHAR(20)    NOT NULL,
    base_hours      DECIMAL(5,2)   NOT NULL,
    trigger_source  VARCHAR(50)    NOT NULL,
    trigger_filter  NVARCHAR(500)  NULL,
    severity        VARCHAR(10)    NOT NULL DEFAULT 'medium',
    sort_order      INT            NOT NULL DEFAULT 0,
    is_active       BIT            NOT NULL DEFAULT 1,
    CONSTRAINT UQ_service_catalog_code UNIQUE (category_code)
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'franchise_service_rates')
CREATE TABLE franchise_service_rates (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    franchise_id    UNIQUEIDENTIFIER NOT NULL REFERENCES franchises(id),
    hourly_rate     DECIMAL(10,2)  NOT NULL DEFAULT 150.00,
    currency        VARCHAR(3)     NOT NULL DEFAULT 'USD',
    margin_pct      DECIMAL(5,2)   NOT NULL DEFAULT 0,
    effective_from  DATETIME2(2)   NOT NULL DEFAULT GETUTCDATE(),
    created_at      DATETIME2(2)   NOT NULL DEFAULT GETUTCDATE()
);
GO
```

- [ ] **Step 2: Create seed data**

```sql
-- sql/seed_039_service_catalog.sql
-- 14 remediation service categories

MERGE service_catalog AS tgt
USING (VALUES
    ('disk_encryption',    'Disk Encryption Rollout',            'Cifrado de Discos',                   'per_machine',       0.50, 'machines.Bitlocker',                     NULL, 'critical', 1),
    ('laps_deploy',        'LAPS Deployment',                   'Despliegue de LAPS',                  'per_machine',       0.25, 'ad_hygiene.NoLAPS',                      NULL, 'critical', 2),
    ('endpoint_protection','Endpoint Protection Enablement',    'Habilitación de Protección Endpoint',  'per_machine',       0.50, 'machines.DefenderEnabled',               NULL, 'critical', 3),
    ('patch_management',   'Legacy OS Migration',               'Migración de SO Obsoleto',            'per_machine',       1.50, 'machines.OsName',                        'Windows 7|Windows 8|Server 2008|Server 2003', 'critical', 4),
    ('protocol_hardening', 'Protocol Hardening (SMBv1/NTLMv1)', 'Hardening de Protocolos (SMBv1/NTLMv1)', 'per_machine',   1.00, 'control_results.protocol',               NULL, 'high', 5),
    ('password_policy',    'Password Policy Enforcement',       'Aplicación de Política de Contraseñas', 'per_domain',      2.00, 'ad_hygiene.PwdNeverExpire',              NULL, 'high', 6),
    ('privileged_access',  'Privileged Access Review',          'Revisión de Acceso Privilegiado',     'per_account',       0.50, 'ad_hygiene.PrivilegedAccounts',          NULL, 'high', 7),
    ('rdp_hardening',      'RDP Hardening',                     'Hardening de RDP',                    'per_machine',       0.50, 'machine_ports.3389',                     NULL, 'high', 8),
    ('m365_security',      'M365 Security Hardening',           'Hardening de Seguridad M365',         'per_tenant',        4.00, 'cloud_assessment_findings.identity',     NULL, 'high', 9),
    ('azure_hardening',    'Azure Infrastructure Hardening',    'Hardening de Infraestructura Azure',  'per_subscription',  6.00, 'cloud_assessment_findings.azure',        NULL, 'high', 10),
    ('firewall_hardening', 'Firewall Hardening',                'Hardening de Firewall',               'per_machine',       0.75, 'control_results.firewall',               NULL, 'medium', 11),
    ('audit_logging',      'Audit & Logging Configuration',     'Configuración de Auditoría y Logs',   'per_domain',        3.00, 'control_results.auditpol',               NULL, 'medium', 12),
    ('cert_hygiene',       'Certificate Remediation',           'Remediación de Certificados',         'per_cert',          0.25, 'control_results.certstore',              NULL, 'medium', 13),
    ('ad_restructuring',   'AD Infrastructure Upgrade',         'Actualización de Infraestructura AD', 'per_domain',        8.00, 'ad_hygiene.DomainFunctionalLevel',       NULL, 'medium', 14)
) AS src (category_code, name_en, name_es, unit_type, base_hours, trigger_source, trigger_filter, severity, sort_order)
ON tgt.category_code = src.category_code
WHEN NOT MATCHED THEN INSERT (category_code, name_en, name_es, unit_type, base_hours, trigger_source, trigger_filter, severity, sort_order)
VALUES (src.category_code, src.name_en, src.name_es, src.unit_type, src.base_hours, src.trigger_source, src.trigger_filter, src.severity, src.sort_order);
GO
```

- [ ] **Step 3: Commit**

```bash
git add sql/039_service_catalog.sql sql/seed_039_service_catalog.sql
git commit -m "feat(reports): add service_catalog + franchise_service_rates schema and seed"
```

---

### Task 1.2: EF Core entities + DbContext

**Files:**
- Create: `src/KryossApi/Data/Entities/ServiceCatalog.cs`
- Create: `src/KryossApi/Data/Entities/FranchiseServiceRate.cs`
- Modify: `src/KryossApi/Data/KryossDbContext.cs`

- [ ] **Step 1: Create ServiceCatalog entity**

```csharp
// Data/Entities/ServiceCatalog.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KryossApi.Data.Entities;

[Table("service_catalog")]
public class ServiceCatalogItem
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("category_code")]
    [MaxLength(30)]
    public string CategoryCode { get; set; } = "";

    [Column("name_en")]
    [MaxLength(100)]
    public string NameEn { get; set; } = "";

    [Column("name_es")]
    [MaxLength(100)]
    public string NameEs { get; set; } = "";

    [Column("unit_type")]
    [MaxLength(20)]
    public string UnitType { get; set; } = "";

    [Column("base_hours")]
    public decimal BaseHours { get; set; }

    [Column("trigger_source")]
    [MaxLength(50)]
    public string TriggerSource { get; set; } = "";

    [Column("trigger_filter")]
    [MaxLength(500)]
    public string? TriggerFilter { get; set; }

    [Column("severity")]
    [MaxLength(10)]
    public string Severity { get; set; } = "medium";

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 2: Create FranchiseServiceRate entity**

```csharp
// Data/Entities/FranchiseServiceRate.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KryossApi.Data.Entities;

[Table("franchise_service_rates")]
public class FranchiseServiceRate
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("franchise_id")]
    public Guid FranchiseId { get; set; }

    [ForeignKey(nameof(FranchiseId))]
    public Franchise Franchise { get; set; } = null!;

    [Column("hourly_rate")]
    public decimal HourlyRate { get; set; } = 150.00m;

    [Column("currency")]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    [Column("margin_pct")]
    public decimal MarginPct { get; set; }

    [Column("effective_from")]
    public DateTime EffectiveFrom { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 3: Add DbSets to KryossDbContext**

Add to the DbSet section of `KryossDbContext.cs`:

```csharp
public DbSet<ServiceCatalogItem> ServiceCatalog => Set<ServiceCatalogItem>();
public DbSet<FranchiseServiceRate> FranchiseServiceRates => Set<FranchiseServiceRate>();
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/KryossApi/KryossApi.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/KryossApi/Data/Entities/ServiceCatalog.cs src/KryossApi/Data/Entities/FranchiseServiceRate.cs src/KryossApi/Data/KryossDbContext.cs
git commit -m "feat(reports): add ServiceCatalogItem + FranchiseServiceRate entities and DbSets"
```

---

### Task 1.3: Core interfaces + data models

**Files:**
- Create: `src/KryossApi/Services/Reports/IReportBlock.cs`
- Create: `src/KryossApi/Services/Reports/ReportData.cs`
- Create: `src/KryossApi/Services/Reports/ReportOptions.cs`

- [ ] **Step 1: Create interfaces**

```csharp
// Services/Reports/IReportBlock.cs
namespace KryossApi.Services.Reports;

public interface IReportBlock
{
    string Render(ReportData data, ReportOptions options);
}

public interface IReportRecipe
{
    string ReportTitle(ReportOptions options);
    IEnumerable<IReportBlock> GetBlocks(ReportData data);
}
```

- [ ] **Step 2: Create ReportOptions**

```csharp
// Services/Reports/ReportOptions.cs
namespace KryossApi.Services.Reports;

public record ReportOptions(
    string Lang = "en",
    string? FrameworkCode = null,
    string? FrameworkName = null,
    string? Tone = null
)
{
    public bool IsSpanish => Lang == "es";
}
```

- [ ] **Step 3: Create ReportData**

```csharp
// Services/Reports/ReportData.cs
using KryossApi.Data.Entities;

namespace KryossApi.Services.Reports;

public class ReportData
{
    // --- Org context ---
    public Organization Org { get; set; } = null!;
    public ReportBranding Branding { get; set; } = null!;
    public ReportUserInfo UserInfo { get; set; } = null!;

    // --- Endpoint data ---
    public List<AssessmentRun> Runs { get; set; } = new();
    public List<OrgControlResult> ControlResults { get; set; } = new();
    public List<FrameworkScoreDto> FrameworkScores { get; set; } = new();
    public HygieneScanDto? Hygiene { get; set; }
    public OrgEnrichment Enrichment { get; set; } = new();
    public decimal? PreviousMonthScore { get; set; }

    // --- Cloud data (null = not connected) ---
    public CloudAssessmentScan? CloudScan { get; set; }
    public List<CloudAssessmentFinding>? CloudFindings { get; set; }
    public Dictionary<string, decimal>? AreaScores { get; set; }
    public List<CloudAssessmentFrameworkScore>? CloudFrameworkScores { get; set; }
    public BenchmarkData? Benchmarks { get; set; }

    // --- M365 ---
    public bool M365Connected { get; set; }
    public List<M365Finding> M365Findings { get; set; } = new();

    // --- CTAs ---
    public List<ExecutiveCta> SavedCtas { get; set; } = new();

    // --- Service catalog ---
    public List<ServiceCatalogItem> ServiceCatalog { get; set; } = new();
    public FranchiseServiceRate? Rate { get; set; }

    // --- Computed convenience ---
    public decimal AvgScore => Runs.Count > 0 ? Math.Round(Runs.Average(r => r.GlobalScore ?? 0), 1) : 0;
    public int TotalMachines => Runs.Count;
    public DateTime ScanDate => Runs.Count > 0 ? Runs.Max(r => r.CompletedAt ?? r.StartedAt) : DateTime.UtcNow;
    public string OrgGrade => ReportHelpers.GetGrade(AvgScore);
    public bool HasCloudData => CloudScan != null;
}

public class BenchmarkData
{
    public Dictionary<string, decimal>? FranchisePeers { get; set; }
    public Dictionary<string, decimal>? IndustryBaseline { get; set; }
    public Dictionary<string, decimal>? GlobalKryoss { get; set; }
}
```

Note: `OrgControlResult`, `ReportBranding`, `FrameworkScoreDto`, `HygieneScanDto`, `OrgEnrichment`, and `ReportUserInfo` are existing DTOs at the bottom of `ReportService.cs` (lines 3804-3866). They will be moved to `ReportData.cs` in Task 2.1 when we extract helpers.

- [ ] **Step 4: Verify build**

Run: `dotnet build src/KryossApi/KryossApi.csproj`
Expected: Build succeeded (some types referenced but not yet moved — will compile because they exist in ReportService.cs)

- [ ] **Step 5: Commit**

```bash
git add src/KryossApi/Services/Reports/
git commit -m "feat(reports): add core interfaces IReportBlock, IReportRecipe, ReportData, ReportOptions"
```

---

## Module 2: Extract shared helpers + styles from ReportService

### Task 2.1: Extract DTOs, helpers, and styles

**Files:**
- Create: `src/KryossApi/Services/Reports/ReportHelpers.cs`
- Create: `src/KryossApi/Services/Reports/ReportStyles.cs`
- Modify: `src/KryossApi/Services/Reports/ReportData.cs` (move DTOs here)
- Modify: `src/KryossApi/Services/ReportService.cs` (remove extracted code, add `using`)

This is the largest extraction task. The goal is to move all shared static methods and DTOs out of `ReportService.cs` so blocks can use them without depending on the old service.

- [ ] **Step 1: Move DTOs from ReportService.cs (lines 3804-3866) into ReportData.cs**

Move these classes to the bottom of `Services/Reports/ReportData.cs` (change access from `internal` to `public`):
- `OrgControlResult` (line 3804)
- `ReportBranding` (line 3817)
- `FrameworkScoreDto` (line 3825)
- `HygieneScanDto` (line 3834)
- `OrgEnrichment` (line 3848)
- `ReportUserInfo` (line 3859)

Delete them from `ReportService.cs`. Add `using KryossApi.Services.Reports;` to `ReportService.cs`.

- [ ] **Step 2: Create ReportHelpers.cs with shared rendering methods**

Extract from `ReportService.cs` into `Services/Reports/ReportHelpers.cs`:

```csharp
// Services/Reports/ReportHelpers.cs
using System.Text;
using System.Web;
using KryossApi.Data.Entities;

namespace KryossApi.Services.Reports;

public static class ReportHelpers
{
    // From line 1177
    public static string GetGrade(decimal score) => score switch
    {
        >= 95 => "A+", >= 90 => "A", >= 85 => "A-",
        >= 80 => "B+", >= 75 => "B", >= 70 => "B-",
        >= 65 => "C+", >= 60 => "C", >= 55 => "C-",
        >= 50 => "D", _ => "F"
    };

    // From line 1202 — AppendRibbonSvg
    public static void AppendRibbonSvg(StringBuilder sb) { /* copy exact content from line 1202-2342 */ }

    // From line 2343 — AppendPageHeader
    public static void AppendPageHeader(StringBuilder sb, string title, ReportBranding brand, string? eyebrow = null) { /* copy exact */ }

    // From line 2357 — AppendFooter
    public static void AppendFooter(StringBuilder sb, ReportBranding brand, string detail, ReportUserInfo? user = null) { /* copy exact */ }

    // From line 2374 — AppendHtmlHead
    public static void AppendHtmlHead(StringBuilder sb, string title, ReportBranding brand, bool isOrgReport, string htmlLang, ReportUserInfo? user = null, string? detail = null) { /* copy exact */ }

    // From line 2403 — AppendRunningFooterCss
    public static void AppendRunningFooterCss(StringBuilder sb, ReportUserInfo user, string? detail) { /* copy exact */ }

    // From line 2867 — AppendOnePagerFooter
    public static void AppendOnePagerFooter(StringBuilder sb, ReportUserInfo user, Organization org, string lang) { /* copy exact */ }

    // From line 2887 — AppendFrameworkBars
    public static void AppendFrameworkBars(StringBuilder sb, List<FrameworkScoreDto> frameworkScores) { /* copy exact */ }

    // From line 2914 — AppendNormalizedFrameworkBars
    public static void AppendNormalizedFrameworkBars(StringBuilder sb, List<CloudAssessmentFrameworkScore>? cloudScores) { /* copy exact, adapt param type */ }

    public static string HtmlEncode(string s) => HttpUtility.HtmlEncode(s);
}
```

Copy the exact method bodies from `ReportService.cs`. Change `private static` to `public static`. Update all callers in `ReportService.cs` to use `ReportHelpers.MethodName(...)`.

- [ ] **Step 3: Create ReportStyles.cs**

Extract from `ReportService.cs`:

```csharp
// Services/Reports/ReportStyles.cs
namespace KryossApi.Services.Reports;

public static class ReportStyles
{
    // From line 3366 — GetOrgReportStyles
    public static string GetOrgReportStyles(ReportBranding brand) => /* copy exact content */;

    // From line 2454 — GetA4PrintCss
    public static string GetA4PrintCss(ReportBranding brand) => /* copy exact content */;

    // From line 3583 — GetReportStyles (legacy, used by old builders)
    public static string GetReportStyles(ReportBranding brand) => /* copy exact content */;
}
```

Update callers in `ReportService.cs` to use `ReportStyles.GetOrgReportStyles(...)` etc.

- [ ] **Step 4: Verify build**

Run: `dotnet build src/KryossApi/KryossApi.csproj`
Expected: Build succeeded. All existing report endpoints still work (pure extraction, no behavior change).

- [ ] **Step 5: Commit**

```bash
git add src/KryossApi/Services/Reports/ReportHelpers.cs src/KryossApi/Services/Reports/ReportStyles.cs src/KryossApi/Services/Reports/ReportData.cs src/KryossApi/Services/ReportService.cs
git commit -m "refactor(reports): extract DTOs, helpers, and styles from ReportService into Reports/"
```

---

## Module 3: ReportDataLoader + ReportComposer

### Task 3.1: ReportDataLoader

**Files:**
- Create: `src/KryossApi/Services/Reports/ReportDataLoader.cs`

- [ ] **Step 1: Implement data loader**

This consolidates the data loading logic from `ReportService.GenerateOrgReportAsync` (lines 36-262) + cloud assessment data loading into a single reusable loader.

```csharp
// Services/Reports/ReportDataLoader.cs
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services.Reports;

public interface IReportDataLoader
{
    Task<ReportData> LoadAsync(Guid orgId, ReportOptions options);
}

public class ReportDataLoader : IReportDataLoader
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ReportDataLoader(KryossDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ReportData> LoadAsync(Guid orgId, ReportOptions options)
    {
        var data = new ReportData();

        // --- Org + branding ---
        var org = await _db.Organizations
            .Include(o => o.Franchise)
            .Include(o => o.Brand)
            .FirstOrDefaultAsync(o => o.Id == orgId)
            ?? throw new InvalidOperationException($"Organization {orgId} not found");
        data.Org = org;
        data.Branding = ResolveBranding(org);
        data.UserInfo = await BuildReportUserInfoAsync(org);

        // --- Latest runs per machine ---
        var latestRunIds = await _db.AssessmentRuns
            .Where(r => r.OrganizationId == orgId && r.CompletedAt != null)
            .GroupBy(r => r.MachineId)
            .Select(g => g.OrderByDescending(r => r.CompletedAt).First().Id)
            .ToListAsync();

        if (latestRunIds.Count > 0)
        {
            data.Runs = await _db.AssessmentRuns
                .Include(r => r.Machine)
                .Where(r => latestRunIds.Contains(r.Id))
                .OrderBy(r => r.Machine.Hostname)
                .ToListAsync();

            // --- Control results ---
            data.ControlResults = await _db.ControlResults
                .Where(cr => latestRunIds.Contains(cr.RunId))
                .Join(_db.ControlDefs.Include(cd => cd.Category), cr => cr.ControlDefId, cd => cd.Id,
                    (cr, cd) => new OrgControlResult
                    {
                        ControlDefId = cd.Id,
                        RunId = cr.RunId,
                        ControlId = cd.ControlId,
                        Name = cd.Name,
                        Category = cd.Category.Name,
                        Severity = cd.Severity ?? "medium",
                        Status = cr.Status,
                        Finding = cr.Finding,
                        Remediation = cd.Remediation
                    })
                .ToListAsync();

            // --- Framework filter ---
            if (!string.IsNullOrEmpty(options.FrameworkCode))
            {
                var framework = await _db.Frameworks
                    .FirstOrDefaultAsync(f => f.Code == options.FrameworkCode && f.IsActive);
                if (framework != null)
                {
                    var controlDefIds = new HashSet<int>(
                        await _db.ControlFrameworks
                            .Where(cf => cf.FrameworkId == framework.Id)
                            .Select(cf => cf.ControlDefId)
                            .ToListAsync());
                    data.ControlResults = data.ControlResults
                        .Where(r => controlDefIds.Contains(r.ControlDefId)).ToList();
                }
            }

            // --- Framework scores ---
            data.FrameworkScores = await _db.RunFrameworkScores
                .Where(fs => latestRunIds.Contains(fs.RunId))
                .GroupBy(fs => fs.FrameworkId)
                .Select(g => new { frameworkId = g.Key, avgScore = Math.Round(g.Average(fs => (double)fs.Score), 1), totalPass = g.Sum(fs => (int)fs.PassCount), totalFail = g.Sum(fs => (int)fs.FailCount) })
                .Join(_db.Frameworks, x => x.frameworkId, fw => fw.Id,
                    (x, fw) => new FrameworkScoreDto { Code = fw.Code, Name = fw.Name, Score = x.avgScore, PassCount = (short)x.totalPass, FailCount = (short)x.totalFail })
                .OrderBy(x => x.Code)
                .ToListAsync();

            // --- Enrichment: disks, ports, threats ---
            var machineIds = data.Runs.Select(r => r.MachineId).ToList();
            data.Enrichment = new OrgEnrichment
            {
                Disks = await _db.MachineDisks.Where(d => machineIds.Contains(d.MachineId)).OrderBy(d => d.DriveLetter).ToListAsync(),
                Ports = await _db.MachinePorts.Where(p => machineIds.Contains(p.MachineId)).OrderBy(p => p.Port).ToListAsync(),
                Threats = await _db.MachineThreats.Where(t => machineIds.Contains(t.MachineId)).OrderByDescending(t => t.DetectedAt).ToListAsync()
            };

            // --- Previous month score ---
            var periodEnd = DateTime.UtcNow.AddDays(-30);
            var periodStart = DateTime.UtcNow.AddDays(-60);
            var prevScores = await _db.AssessmentRuns
                .Where(r => r.OrganizationId == orgId && r.CompletedAt != null && r.CompletedAt >= periodStart && r.CompletedAt < periodEnd && r.GlobalScore != null)
                .Select(r => (decimal)r.GlobalScore!)
                .ToListAsync();
            if (prevScores.Count > 0)
                data.PreviousMonthScore = Math.Round(prevScores.Average(), 1);
        }

        // --- AD Hygiene ---
        data.Hygiene = await _db.AdHygieneScans
            .Where(s => s.OrganizationId == orgId)
            .OrderByDescending(s => s.ScannedAt)
            .Select(s => new HygieneScanDto
            {
                ScannedAt = s.ScannedAt, TotalMachines = s.TotalMachines, TotalUsers = s.TotalUsers,
                StaleMachines = s.StaleMachines, DormantMachines = s.DormantMachines,
                StaleUsers = s.StaleUsers, DormantUsers = s.DormantUsers, DisabledUsers = s.DisabledUsers,
                PwdNeverExpire = s.PwdNeverExpire,
                Findings = _db.AdHygieneFindings.Where(f => f.ScanId == s.Id).OrderBy(f => f.ObjectType).ThenByDescending(f => f.DaysInactive).ToList()
            })
            .FirstOrDefaultAsync();

        // --- M365 ---
        var m365Tenant = await _db.M365Tenants
            .FirstOrDefaultAsync(t => t.OrganizationId == orgId && t.ConsentGrantedAt != null);
        if (m365Tenant != null)
        {
            data.M365Connected = true;
            data.M365Findings = await _db.M365Findings.Where(f => f.TenantId == m365Tenant.Id).ToListAsync();
        }

        // --- Cloud Assessment ---
        data.CloudScan = await _db.CloudAssessmentScans
            .Where(s => s.OrganizationId == orgId && s.Status == "completed")
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
        if (data.CloudScan != null)
        {
            data.CloudFindings = await _db.CloudAssessmentFindings
                .Where(f => f.ScanId == data.CloudScan.Id).ToListAsync();
            data.AreaScores = ParseAreaScores(data.CloudScan.AreaScores);
            data.CloudFrameworkScores = await _db.CloudAssessmentFrameworkScores
                .Where(s => s.ScanId == data.CloudScan.Id).ToListAsync();
        }

        // --- CTAs ---
        var ctaPeriodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        try
        {
            data.SavedCtas = await _db.ExecutiveCtas
                .Where(c => c.OrganizationId == orgId && c.PeriodStart == ctaPeriodStart)
                .ToListAsync();
        }
        catch { data.SavedCtas = new List<ExecutiveCta>(); }

        // --- Service catalog + rate ---
        data.ServiceCatalog = await _db.ServiceCatalog.Where(sc => sc.IsActive).OrderBy(sc => sc.SortOrder).ToListAsync();
        if (org.FranchiseId != Guid.Empty)
        {
            data.Rate = await _db.FranchiseServiceRates
                .Where(r => r.FranchiseId == org.FranchiseId && r.EffectiveFrom <= DateTime.UtcNow)
                .OrderByDescending(r => r.EffectiveFrom)
                .FirstOrDefaultAsync();
        }

        return data;
    }

    private static ReportBranding ResolveBranding(Organization org)
    {
        var brand = org.Brand;
        var franchise = org.Franchise;
        return new ReportBranding
        {
            CompanyName = brand?.Name ?? franchise.BrandName ?? franchise.Name,
            PrimaryColor = brand?.ColorPrimary ?? franchise.BrandColorPrimary ?? "#006536",
            AccentColor = brand?.ColorAccent ?? franchise.BrandColorAccent ?? "#A2C564",
            LogoUrl = brand?.LogoUrl ?? franchise.BrandLogoUrl ?? LogoData.DataUri
        };
    }

    private async Task<ReportUserInfo> BuildReportUserInfoAsync(Organization org)
    {
        var franchise = org.Franchise;
        string? fallbackPhone = franchise?.ContactPhone;
        if (_currentUser.UserId == Guid.Empty)
            return new ReportUserInfo { FullName = _currentUser.DisplayName, Email = _currentUser.Email, Phone = _currentUser.Phone ?? fallbackPhone, JobTitle = _currentUser.JobTitle, CompanyName = franchise?.Name };
        var dbUser = await _db.Users.Where(u => u.Id == _currentUser.UserId).Select(u => new { u.DisplayName, u.Email, u.Phone, u.JobTitle }).FirstOrDefaultAsync();
        return new ReportUserInfo { FullName = dbUser?.DisplayName ?? _currentUser.DisplayName, Email = dbUser?.Email ?? _currentUser.Email, Phone = dbUser?.Phone ?? _currentUser.Phone ?? fallbackPhone, JobTitle = dbUser?.JobTitle ?? _currentUser.JobTitle, CompanyName = franchise?.Name };
    }

    private static Dictionary<string, decimal>? ParseAreaScores(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(json); }
        catch { return null; }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/KryossApi/KryossApi.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/KryossApi/Services/Reports/ReportDataLoader.cs
git commit -m "feat(reports): add ReportDataLoader — unified data loading for all report types"
```

---

### Task 3.2: ReportComposer

**Files:**
- Create: `src/KryossApi/Services/Reports/ReportComposer.cs`

- [ ] **Step 1: Implement composer**

```csharp
// Services/Reports/ReportComposer.cs
using System.Text;

namespace KryossApi.Services.Reports;

public interface IReportComposer
{
    Task<string> GenerateAsync(Guid orgId, string reportType, ReportOptions options);
}

public class ReportComposer : IReportComposer
{
    private readonly IReportDataLoader _loader;

    public ReportComposer(IReportDataLoader loader) => _loader = loader;

    public async Task<string> GenerateAsync(Guid orgId, string reportType, ReportOptions options)
    {
        var data = await _loader.LoadAsync(orgId, options);
        var recipe = ResolveRecipe(reportType, options);
        var blocks = recipe.GetBlocks(data).ToList();

        var sb = new StringBuilder();
        var reportTitle = recipe.ReportTitle(options);
        var detail = $"{data.TotalMachines} {(options.IsSpanish ? "dispositivos" : "devices")} · {data.Org.Name}";

        ReportHelpers.AppendHtmlHead(sb, $"{reportTitle} - {data.Org.Name}", data.Branding,
            isOrgReport: true, htmlLang: options.Lang, user: data.UserInfo, detail: detail);

        foreach (var block in blocks)
            sb.Append(block.Render(data, options));

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static IReportRecipe ResolveRecipe(string reportType, ReportOptions options) => reportType switch
    {
        "c-level" => new Recipes.CLevelRecipe(),
        "technical" => new Recipes.TechnicalRecipe(),
        "preventa" or "preventas" => options.Tone == "detailed"
            ? new Recipes.PreventaDetailedRecipe()
            : new Recipes.PreventaOpenerRecipe(),
        "presales" => new Recipes.PreventaDetailedRecipe(),
        "presales-opener" => new Recipes.PreventaOpenerRecipe(),
        "monthly" or "monthly-briefing" => new Recipes.MonthlyRecipe(),
        "framework" => new Recipes.FrameworkRecipe(),
        "proposal" => new Recipes.ProposalRecipe(),
        _ => throw new ArgumentException($"Unknown report type: {reportType}")
    };
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/KryossApi/KryossApi.csproj`
Expected: Build errors for missing Recipe classes — expected, those come in Module 4+

- [ ] **Step 3: Commit**

```bash
git add src/KryossApi/Services/Reports/ReportComposer.cs
git commit -m "feat(reports): add ReportComposer — recipe-based report orchestrator"
```

---

## Module 4: Extract existing blocks from ReportService

Each task extracts one block from the existing `ReportService.cs` methods. The goal is exact behavioral parity — copy the rendering logic, adapt it to use `ReportData` instead of individual parameters.

### Task 4.1: CoverBlock

**Files:**
- Create: `src/KryossApi/Services/Reports/Blocks/CoverBlock.cs`

- [ ] **Step 1: Implement CoverBlock**

Extract cover page rendering from each `Build*` method (they all share the same pattern: lines 346-357 in BuildOrgExecutiveReport). Parameterize the title and eyebrow.

```csharp
// Services/Reports/Blocks/CoverBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class CoverBlock : IReportBlock
{
    private readonly string _reportTypeKey;

    public CoverBlock(string reportTypeKey) => _reportTypeKey = reportTypeKey;

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var brand = data.Branding;

        var reportTitle = GetTitle(es, options);
        var eyebrow = options.FrameworkName != null
            ? ReportHelpers.HtmlEncode(options.FrameworkName.ToUpperInvariant())
            : GetEyebrow(es);

        sb.AppendLine("<div class='cover'>");
        ReportHelpers.AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{ReportHelpers.HtmlEncode(brand.LogoUrl)}' class='logo' alt='{ReportHelpers.HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{eyebrow}</p>");
        sb.AppendLine($"<h1>{ReportHelpers.HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{ReportHelpers.HtmlEncode(data.Org.Name)}</h2>");
        var dateStr = es ? data.ScanDate.ToString("dd 'de' MMMM 'de' yyyy") : data.ScanDate.ToString("MMMM dd, yyyy");
        var devicesLabel = es ? "dispositivos evaluados" : "devices assessed";
        sb.AppendLine($"<p class='meta'>{dateStr} &mdash; {data.TotalMachines} {devicesLabel}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{data.OrgGrade.Replace("+", "plus")}'>{ReportHelpers.HtmlEncode(data.OrgGrade)}</div>");
        sb.AppendLine($"<p class='score'>{data.AvgScore:F1}%</p>");
        sb.AppendLine("</div></div>");

        return sb.ToString();
    }

    private string GetTitle(bool es, ReportOptions options) => _reportTypeKey switch
    {
        "c-level" => es ? "Informe Ejecutivo C-Level" : "C-Level Security Briefing",
        "technical" => options.FrameworkName != null
            ? $"{options.FrameworkName} {(es ? "Informe Técnico" : "Technical Report")}"
            : (es ? "Informe Técnico de Seguridad" : "Security Technical Report"),
        "preventa-opener" => es ? "Evaluación de Riesgo" : "Risk Assessment",
        "preventa-detailed" => es ? "Propuesta de Seguridad" : "Security Proposal",
        "monthly" => es ? "Informe de Progreso Mensual" : "Monthly Progress Report",
        "framework" => $"{options.FrameworkName} {(es ? "Informe de Cumplimiento" : "Compliance Report")}",
        "proposal" => es ? "Propuesta Comercial de Remediación" : "Remediation Business Proposal",
        _ => es ? "Informe de Seguridad" : "Security Report"
    };

    private string GetEyebrow(bool es) => _reportTypeKey switch
    {
        "c-level" => es ? "BRIEFING EJECUTIVO" : "EXECUTIVE BRIEFING",
        "technical" => es ? "INFORME TÉCNICO" : "TECHNICAL REPORT",
        "preventa-opener" or "preventa-detailed" => es ? "EVALUACIÓN DE SEGURIDAD" : "SECURITY ASSESSMENT",
        "monthly" => es ? "PROGRESO MENSUAL" : "MONTHLY PROGRESS",
        "framework" => es ? "CUMPLIMIENTO" : "COMPLIANCE",
        "proposal" => es ? "PROPUESTA COMERCIAL" : "BUSINESS PROPOSAL",
        _ => es ? "EVALUACIÓN DE SEGURIDAD" : "SECURITY ASSESSMENT"
    };
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/KryossApi/KryossApi.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/KryossApi/Services/Reports/Blocks/CoverBlock.cs
git commit -m "feat(reports): add CoverBlock — shared cover page for all report types"
```

---

### Task 4.2: SemaforoBlock + KpiBlock + CtaBlock (C-Level blocks)

**Files:**
- Create: `src/KryossApi/Services/Reports/Blocks/SemaforoBlock.cs`
- Create: `src/KryossApi/Services/Reports/Blocks/KpiBlock.cs`
- Create: `src/KryossApi/Services/Reports/Blocks/CtaBlock.cs`

- [ ] **Step 1: Implement SemaforoBlock**

Extract from `BuildOrgCLevelReport` lines 1547-1585 (Block 1: Risk Posture semáforo). Copy the 4 capital sins logic and score-based fallback exactly.

```csharp
// Services/Reports/Blocks/SemaforoBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class SemaforoBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        // Copy exact semáforo logic from BuildOrgCLevelReport lines 1547-1585
        // 4 capital sins → force RED, score-based fallback
        // Use data.Enrichment.Threats, data.M365Findings, data.Hygiene, data.Enrichment.Ports
        // ... (copy the full implementation from ReportService.cs)
        return sb.ToString();
    }
}
```

The actual implementation must be copied verbatim from `ReportService.cs` lines 1547-1585, adapting parameter access from individual variables to `data.PropertyName`.

- [ ] **Step 2: Implement KpiBlock**

Extract from `BuildOrgCLevelReport` lines 1586-1654 (Block 2: 3 Business KPIs). Add optional 4th cloud KPI when `data.HasCloudData`.

```csharp
// Services/Reports/Blocks/KpiBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class KpiBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        // Copy KPI 1: Costo de Exposición (static benchmark)
        // Copy KPI 2: Cobertura de Activos (4 Fantásticos)
        // Copy KPI 3: Evolución del Riesgo (delta arrow)
        // NEW KPI 4 (conditional): Cloud Security Score — only if data.HasCloudData
        //   avg of data.AreaScores values, displayed as gauge
        // ... (copy from ReportService.cs lines 1586-1654, adapt to data.*)
        return sb.ToString();
    }
}
```

- [ ] **Step 3: Implement CtaBlock**

Extract from `BuildOrgCLevelReport` lines 1655-1729 (Block 3: Executive Decisions Required). Copy the 12-rule engine exactly.

```csharp
// Services/Reports/Blocks/CtaBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class CtaBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        // Copy 12-rule auto-detection engine from lines 1655-1729
        // Merge with data.SavedCtas (manual + edited)
        // Max 2 shown, priority: Incidentes → Hardening → Budget → Risk
        // Empty state: positive closure card
        // ... (copy from ReportService.cs, adapt to data.*)
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/KryossApi/KryossApi.csproj`

- [ ] **Step 5: Commit**

```bash
git add src/KryossApi/Services/Reports/Blocks/SemaforoBlock.cs src/KryossApi/Services/Reports/Blocks/KpiBlock.cs src/KryossApi/Services/Reports/Blocks/CtaBlock.cs
git commit -m "feat(reports): add SemaforoBlock, KpiBlock, CtaBlock — C-Level report blocks"
```

---

### Task 4.3: AssetMatrixBlock + TopFindingsBlock + IronSixBlock (Technical blocks)

**Files:**
- Create: `src/KryossApi/Services/Reports/Blocks/AssetMatrixBlock.cs`
- Create: `src/KryossApi/Services/Reports/Blocks/TopFindingsBlock.cs`
- Create: `src/KryossApi/Services/Reports/Blocks/IronSixBlock.cs`

- [ ] **Step 1: Implement AssetMatrixBlock**

Extract from `ReportService.cs` lines 3056-3119 (`AppendAssetMatrix`). Wrap in `IReportBlock`.

```csharp
// Services/Reports/Blocks/AssetMatrixBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class AssetMatrixBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        // Copy AppendAssetMatrix logic (lines 3056-3119)
        // Uses data.Runs, data.Branding, options.Lang
        // Paginated at 25 rows per page
        return sb.ToString();
    }
}
```

- [ ] **Step 2: Implement TopFindingsBlock**

Extract from `ReportService.cs` lines 3120-3172 (`AppendTop10CriticalFindings`). Make N configurable (default 10).

```csharp
// Services/Reports/Blocks/TopFindingsBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class TopFindingsBlock : IReportBlock
{
    private readonly int _topN;
    private readonly bool _splitResolved;

    public TopFindingsBlock(int topN = 10, bool splitResolved = false)
    {
        _topN = topN;
        _splitResolved = splitResolved;
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        // Copy AppendTop10CriticalFindings logic (lines 3120-3172)
        // Use _topN instead of hardcoded 10
        // If _splitResolved, show two sub-sections: "Resolved" + "New" (for Monthly)
        // Uses data.ControlResults
        return sb.ToString();
    }
}
```

- [ ] **Step 3: Implement IronSixBlock**

Extract from `ReportService.cs` lines 3173-3260 (`AppendSixIronsHardeningAudit` + `AppendIronSection` + `AppendIronCompliant`).

```csharp
// Services/Reports/Blocks/IronSixBlock.cs
using System.Text;
using KryossApi.Data.Entities;

namespace KryossApi.Services.Reports.Blocks;

public class IronSixBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        // Copy AppendSixIronsHardeningAudit logic (lines 3173-3260)
        // 6 categories: Cifrado, Protocolos, Hardware, Identidad, Higiene, Endpoint
        // Uses data.Runs (machines), data.ControlResults, data.Hygiene
        // Zero-state: checkmark per category
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Verify build + commit**

```bash
git add src/KryossApi/Services/Reports/Blocks/AssetMatrixBlock.cs src/KryossApi/Services/Reports/Blocks/TopFindingsBlock.cs src/KryossApi/Services/Reports/Blocks/IronSixBlock.cs
git commit -m "feat(reports): add AssetMatrixBlock, TopFindingsBlock, IronSixBlock — Technical report blocks"
```

---

### Task 4.4: RiskScoreBlock + ThreatVectorsBlock + MethodologyBlock (Preventa blocks)

**Files:**
- Create: `src/KryossApi/Services/Reports/Blocks/RiskScoreBlock.cs`
- Create: `src/KryossApi/Services/Reports/Blocks/ThreatVectorsBlock.cs`
- Create: `src/KryossApi/Services/Reports/Blocks/MethodologyBlock.cs`

- [ ] **Step 1: Implement RiskScoreBlock**

Extract from `BuildOrgPresalesOpenerReport` (lines 1742-1850ish). The aggressive risk framing section. When `data.HasCloudData`, incorporate cloud findings into narrative.

- [ ] **Step 2: Implement ThreatVectorsBlock**

Extract from `BuildOrgPresalesOpenerReport` (lines 1850-1950ish). Top 4 compromise vectors.

- [ ] **Step 3: Implement MethodologyBlock**

Extract from `BuildOrgPresalesOpenerReport`/`BuildOrgPresalesReport` (90-day safe deprecation methodology section).

- [ ] **Step 4: Verify build + commit**

```bash
git add src/KryossApi/Services/Reports/Blocks/RiskScoreBlock.cs src/KryossApi/Services/Reports/Blocks/ThreatVectorsBlock.cs src/KryossApi/Services/Reports/Blocks/MethodologyBlock.cs
git commit -m "feat(reports): add RiskScoreBlock, ThreatVectorsBlock, MethodologyBlock — Preventa blocks"
```

---

## Module 5: New blocks (Cloud, Framework, ServiceCatalog, Timeline, Monthly)

### Task 5.1: CloudPostureBlock

**Files:**
- Create: `src/KryossApi/Services/Reports/Blocks/CloudPostureBlock.cs`

- [ ] **Step 1: Implement CloudPostureBlock**

Merge logic from `CloudAssessmentReportService.cs` into a block. Two modes: compact (C-Level: radar + top 3 findings) and full (Technical: radar + all findings + recommendations).

```csharp
// Services/Reports/Blocks/CloudPostureBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class CloudPostureBlock : IReportBlock
{
    private readonly bool _compact;

    public CloudPostureBlock(bool compact = false) => _compact = compact;

    public string Render(ReportData data, ReportOptions options)
    {
        if (!data.HasCloudData) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Postura Cloud" : "Cloud Posture", data.Branding);
        sb.AppendLine("<div class='pb'>");

        // 5-area radar chart (SVG)
        RenderRadarChart(sb, data.AreaScores!, es);

        if (_compact)
        {
            // Top 3 critical cloud findings only
            var critical = data.CloudFindings!
                .Where(f => f.Severity == "critical" || f.Severity == "high")
                .Take(3).ToList();
            RenderFindingsList(sb, critical, es);
        }
        else
        {
            // All findings grouped by area
            foreach (var area in new[] { "identity", "endpoint", "data", "productivity", "azure" })
            {
                var areaFindings = data.CloudFindings!.Where(f => f.Area == area).ToList();
                if (areaFindings.Count > 0)
                    RenderAreaSection(sb, area, areaFindings, es);
            }
        }

        // Benchmarks comparison if available
        if (data.Benchmarks != null)
            RenderBenchmarkComparison(sb, data, es);

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static void RenderRadarChart(StringBuilder sb, Dictionary<string, decimal> scores, bool es)
    {
        // SVG radar chart with 5 axes
        // Adapt from CloudAssessmentReportService radar rendering
        // Labels: Identity, Endpoint, Data, Productivity, Azure
        sb.AppendLine("<div class='radar-container'>");
        // ... SVG rendering (copy from CloudAssessmentReportService)
        sb.AppendLine("</div>");
    }

    private static void RenderFindingsList(StringBuilder sb, List<Data.Entities.CloudAssessmentFinding> findings, bool es)
    {
        foreach (var f in findings)
        {
            var severityClass = f.Severity == "critical" ? "fail" : f.Severity == "high" ? "warn" : "pass";
            sb.AppendLine($"<div class='finding {severityClass}'>");
            sb.AppendLine($"<strong>{ReportHelpers.HtmlEncode(f.Title)}</strong>");
            sb.AppendLine($"<p>{ReportHelpers.HtmlEncode(f.Description ?? "")}</p>");
            sb.AppendLine("</div>");
        }
    }

    private static void RenderAreaSection(StringBuilder sb, string area, List<Data.Entities.CloudAssessmentFinding> findings, bool es)
    {
        var areaTitle = area switch
        {
            "identity" => es ? "Identidad" : "Identity",
            "endpoint" => es ? "Dispositivos" : "Endpoint",
            "data" => es ? "Datos" : "Data",
            "productivity" => es ? "Productividad" : "Productivity",
            "azure" => "Azure",
            _ => area
        };
        sb.AppendLine($"<h3>{areaTitle} ({findings.Count})</h3>");
        RenderFindingsList(sb, findings, es);
    }

    private static void RenderBenchmarkComparison(StringBuilder sb, ReportData data, bool es)
    {
        // Show benchmark positioning if available
        // Uses data.Benchmarks.FranchisePeers, IndustryBaseline, GlobalKryoss
    }
}
```

- [ ] **Step 2: Verify build + commit**

```bash
git add src/KryossApi/Services/Reports/Blocks/CloudPostureBlock.cs
git commit -m "feat(reports): add CloudPostureBlock — integrated cloud posture with radar + findings"
```

---

### Task 5.2: FrameworkGaugeBlock + GapAnalysisBlock

**Files:**
- Create: `src/KryossApi/Services/Reports/Blocks/FrameworkGaugeBlock.cs`
- Create: `src/KryossApi/Services/Reports/Blocks/GapAnalysisBlock.cs`

- [ ] **Step 1: Implement FrameworkGaugeBlock**

New block. Large visual gauge for selected framework. Reuses `AppendFrameworkBars` pattern but renders a single large gauge instead of multiple small bars.

```csharp
// Services/Reports/Blocks/FrameworkGaugeBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class FrameworkGaugeBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb,
            es ? $"Cumplimiento {options.FrameworkName}" : $"{options.FrameworkName} Compliance",
            data.Branding);
        sb.AppendLine("<div class='pb'>");

        if (options.FrameworkCode != null)
        {
            // Single large gauge for selected framework
            var fw = data.FrameworkScores.FirstOrDefault(f => f.Code == options.FrameworkCode);
            if (fw != null)
                RenderLargeGauge(sb, fw, es);

            // Cloud framework score if available
            if (data.CloudFrameworkScores != null)
            {
                var cloudFw = data.CloudFrameworkScores
                    .FirstOrDefault(f => f.FrameworkCode == options.FrameworkCode);
                if (cloudFw != null)
                    RenderCloudFrameworkGauge(sb, cloudFw, es);
            }
        }
        else
        {
            // All frameworks as bar chart
            ReportHelpers.AppendFrameworkBars(sb, data.FrameworkScores);
        }

        // Previous period comparison if available
        if (data.PreviousMonthScore.HasValue)
        {
            var delta = data.AvgScore - data.PreviousMonthScore.Value;
            var arrow = delta > 0 ? "▲" : delta < 0 ? "▼" : "=";
            var color = delta > 0 ? "#008852" : delta < 0 ? "#C0392B" : "#6B7280";
            sb.AppendLine($"<p style='color:{color};font-size:1.1em;margin-top:1em;'>{arrow} {Math.Abs(delta):F1} pts vs {(es ? "período anterior" : "previous period")}</p>");
        }

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static void RenderLargeGauge(StringBuilder sb, FrameworkScoreDto fw, bool es)
    {
        var grade = ReportHelpers.GetGrade((decimal)fw.Score);
        var color = fw.Score >= 80 ? "#008852" : fw.Score >= 60 ? "#D97706" : "#C0392B";
        sb.AppendLine("<div style='text-align:center;padding:2em 0;'>");
        sb.AppendLine($"<div style='font-size:4em;font-weight:700;color:{color};'>{fw.Score:F1}%</div>");
        sb.AppendLine($"<div style='font-size:2em;color:{color};margin:0.25em 0;'>{grade}</div>");
        sb.AppendLine($"<div style='font-size:1.1em;color:#64748B;'>{fw.PassCount} {(es ? "aprobados" : "passed")} / {fw.FailCount} {(es ? "fallidos" : "failed")}</div>");
        sb.AppendLine("</div>");
    }

    private static void RenderCloudFrameworkGauge(StringBuilder sb, Data.Entities.CloudAssessmentFrameworkScore cfw, bool es)
    {
        sb.AppendLine($"<div style='margin-top:1.5em;padding:1em;background:#F8F9FA;border-radius:8px;'>");
        sb.AppendLine($"<h4>{(es ? "Cumplimiento Cloud" : "Cloud Compliance")}</h4>");
        sb.AppendLine($"<div style='font-size:2em;font-weight:700;'>{cfw.ScorePct:F1}%</div>");
        sb.AppendLine($"<div>{cfw.PassingControls}/{cfw.TotalControls} {(es ? "controles" : "controls")}</div>");
        sb.AppendLine("</div>");
    }
}
```

- [ ] **Step 2: Implement GapAnalysisBlock**

New block. Failing controls grouped by category with remediation text.

```csharp
// Services/Reports/Blocks/GapAnalysisBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class GapAnalysisBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        var failingControls = data.ControlResults
            .Where(r => r.Status == "fail")
            .OrderByDescending(r => r.Severity == "critical" ? 4 : r.Severity == "high" ? 3 : r.Severity == "medium" ? 2 : 1)
            .ToList();

        if (failingControls.Count == 0) return "";

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Análisis de Brechas" : "Gap Analysis", data.Branding);
        sb.AppendLine("<div class='pb'>");

        var grouped = failingControls.GroupBy(r => r.Category).OrderByDescending(g => g.Count());
        foreach (var group in grouped)
        {
            sb.AppendLine($"<h3>{ReportHelpers.HtmlEncode(group.Key)} ({group.Count()})</h3>");
            sb.AppendLine("<table class='data-table'><thead><tr>");
            sb.AppendLine($"<th>{(es ? "Control" : "Control")}</th><th>{(es ? "Severidad" : "Severity")}</th><th>{(es ? "Máquinas" : "Machines")}</th><th>{(es ? "Remediación" : "Remediation")}</th>");
            sb.AppendLine("</tr></thead><tbody>");

            var dedupedControls = group.GroupBy(r => r.ControlDefId)
                .Select(g => new { Control = g.First(), MachineCount = g.Select(r => r.RunId).Distinct().Count() })
                .OrderByDescending(x => x.Control.Severity == "critical" ? 4 : 3)
                .ThenByDescending(x => x.MachineCount);

            foreach (var item in dedupedControls)
            {
                var sevClass = item.Control.Severity == "critical" ? "fail" : item.Control.Severity == "high" ? "warn" : "";
                sb.AppendLine($"<tr><td>{ReportHelpers.HtmlEncode(item.Control.Name)}</td>");
                sb.AppendLine($"<td class='{sevClass}'>{item.Control.Severity}</td>");
                sb.AppendLine($"<td>{item.MachineCount}</td>");
                sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(item.Control.Remediation ?? "—")}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        // Cloud gap analysis if filtered by framework and cloud data exists
        if (data.HasCloudData && options.FrameworkCode != null && data.CloudFindings != null)
        {
            var cloudFails = data.CloudFindings.Where(f => f.Status == "fail").ToList();
            if (cloudFails.Count > 0)
            {
                sb.AppendLine($"<h3>{(es ? "Brechas Cloud" : "Cloud Gaps")} ({cloudFails.Count})</h3>");
                foreach (var f in cloudFails)
                {
                    sb.AppendLine($"<div class='finding'><strong>{ReportHelpers.HtmlEncode(f.Title)}</strong>");
                    sb.AppendLine($"<p>{ReportHelpers.HtmlEncode(f.Recommendation ?? "")}</p></div>");
                }
            }
        }

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }
}
```

- [ ] **Step 3: Verify build + commit**

```bash
git add src/KryossApi/Services/Reports/Blocks/FrameworkGaugeBlock.cs src/KryossApi/Services/Reports/Blocks/GapAnalysisBlock.cs
git commit -m "feat(reports): add FrameworkGaugeBlock + GapAnalysisBlock — compliance reporting blocks"
```

---

### Task 5.3: ServiceCatalogBlock + TimelineBlock

**Files:**
- Create: `src/KryossApi/Services/Reports/Blocks/ServiceCatalogBlock.cs`
- Create: `src/KryossApi/Services/Reports/Blocks/TimelineBlock.cs`

- [ ] **Step 1: Implement ServiceCatalogBlock**

New block. Auto-calculates pricing from findings + service catalog + franchise rate.

```csharp
// Services/Reports/Blocks/ServiceCatalogBlock.cs
using System.Text;
using KryossApi.Data.Entities;

namespace KryossApi.Services.Reports.Blocks;

public class ServiceCatalogBlock : IReportBlock
{
    private readonly bool _showPricing;

    public ServiceCatalogBlock(bool showPricing = true) => _showPricing = showPricing;

    public string Render(ReportData data, ReportOptions options)
    {
        if (data.ServiceCatalog.Count == 0) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var rate = data.Rate?.HourlyRate ?? 150m;
        var margin = data.Rate?.MarginPct ?? 0m;
        var currency = data.Rate?.Currency ?? "USD";

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Catálogo de Servicios" : "Service Catalog", data.Branding);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Servicio" : "Service")}</th>");
        sb.AppendLine($"<th>{(es ? "Afectados" : "Affected")}</th>");
        sb.AppendLine($"<th>{(es ? "Horas Est." : "Est. Hours")}</th>");
        if (_showPricing)
            sb.AppendLine($"<th>{(es ? "Costo" : "Cost")}</th>");
        sb.AppendLine("</tr></thead><tbody>");

        decimal totalHours = 0;
        decimal totalCost = 0;

        foreach (var svc in data.ServiceCatalog)
        {
            var affected = CountAffected(svc, data);
            if (affected == 0) continue;

            var hours = affected * svc.BaseHours;
            var cost = hours * rate * (1 + margin / 100);
            totalHours += hours;
            totalCost += cost;

            var name = es ? svc.NameEs : svc.NameEn;
            var sevClass = svc.Severity == "critical" ? "fail" : svc.Severity == "high" ? "warn" : "";

            sb.AppendLine($"<tr class='{sevClass}'>");
            sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(name)}</td>");
            sb.AppendLine($"<td>{affected}</td>");
            sb.AppendLine($"<td>{hours:F1}h</td>");
            if (_showPricing)
                sb.AppendLine($"<td>{currency} {cost:N0}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("<tr style='font-weight:700;border-top:2px solid #1E293B;'>");
        sb.AppendLine($"<td><strong>Total</strong></td><td></td><td>{totalHours:F1}h</td>");
        if (_showPricing)
            sb.AppendLine($"<td>{currency} {totalCost:N0}</td>");
        sb.AppendLine("</tr>");
        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</div></div>");

        return sb.ToString();
    }

    private static int CountAffected(ServiceCatalogItem svc, ReportData data) => svc.CategoryCode switch
    {
        "disk_encryption" => data.Runs.Count(r => r.Machine.Bitlocker != true),
        "laps_deploy" => data.Hygiene?.Findings?.Count(f => f.Status == "NoLAPS") ?? 0,
        "endpoint_protection" => data.Runs.Count(r => r.Machine.DefenderEnabled != true),
        "patch_management" => data.Runs.Count(r => IsLegacyOs(r.Machine.OsName)),
        "protocol_hardening" => data.ControlResults.Where(r => r.Status == "fail" && (r.ControlId.StartsWith("NTLM") || r.ControlId.StartsWith("SMB1"))).Select(r => r.RunId).Distinct().Count(),
        "password_policy" => data.Hygiene?.PwdNeverExpire > 0 ? 1 : 0,
        "privileged_access" => data.Hygiene?.Findings?.Count(f => f.ObjectType == "PrivilegedAccount") ?? 0,
        "rdp_hardening" => data.Enrichment.Ports.Count(p => p.Port == 3389 && p.State == "open"),
        "m365_security" => data.M365Connected ? 1 : 0,
        "azure_hardening" => data.CloudFindings?.Any(f => f.Area == "azure" && f.Status == "fail") == true ? 1 : 0,
        "firewall_hardening" => data.ControlResults.Where(r => r.Status == "fail" && r.Category.Contains("Firewall")).Select(r => r.RunId).Distinct().Count(),
        "audit_logging" => data.ControlResults.Any(r => r.Status == "fail" && r.Category.Contains("Audit")) ? 1 : 0,
        "cert_hygiene" => data.ControlResults.Count(r => r.Status == "fail" && r.ControlId.StartsWith("BL-047")),
        "ad_restructuring" => data.Hygiene?.Findings?.Any(f => f.ObjectType == "DomainInfo" && f.Status != "OK") == true ? 1 : 0,
        _ => 0
    };

    private static bool IsLegacyOs(string? os) =>
        os != null && (os.Contains("2008") || os.Contains("2003") || os.Contains("Windows 7") || os.Contains("Vista") || os.Contains("Windows 8"));
}
```

- [ ] **Step 2: Implement TimelineBlock**

New block. Gantt-style remediation roadmap.

```csharp
// Services/Reports/Blocks/TimelineBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class TimelineBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        if (data.ServiceCatalog.Count == 0) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;

        // Build phases from service catalog items that have findings
        var phases = data.ServiceCatalog
            .Select(svc => new { Svc = svc, Affected = CountAffected(svc, data) })
            .Where(x => x.Affected > 0)
            .OrderByDescending(x => x.Svc.Severity == "critical" ? 4 : x.Svc.Severity == "high" ? 3 : 2)
            .ToList();

        if (phases.Count == 0) return "";

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Roadmap de Remediación" : "Remediation Roadmap", data.Branding);
        sb.AppendLine("<div class='pb'>");

        // Phase timeline as horizontal bars
        int weekOffset = 0;
        foreach (var phase in phases)
        {
            var totalHours = phase.Affected * phase.Svc.BaseHours;
            var weeks = Math.Max(1, (int)Math.Ceiling((double)totalHours / 40));
            var name = es ? phase.Svc.NameEs : phase.Svc.NameEn;
            var sevColor = phase.Svc.Severity == "critical" ? "#C0392B" : phase.Svc.Severity == "high" ? "#D97706" : "#008852";
            var barWidthPct = Math.Min(90, weeks * 15);

            sb.AppendLine("<div style='margin-bottom:0.75em;'>");
            sb.AppendLine($"<div style='display:flex;justify-content:space-between;font-size:0.85em;'>");
            sb.AppendLine($"<span><strong>{ReportHelpers.HtmlEncode(name)}</strong></span>");
            sb.AppendLine($"<span>{(es ? "Semana" : "Week")} {weekOffset + 1}–{weekOffset + weeks}</span>");
            sb.AppendLine("</div>");
            sb.AppendLine($"<div style='background:#E2E8F0;border-radius:4px;height:24px;margin-top:4px;'>");
            sb.AppendLine($"<div style='background:{sevColor};width:{barWidthPct}%;height:100%;border-radius:4px;margin-left:{weekOffset * 10}%;'></div>");
            sb.AppendLine("</div></div>");

            weekOffset += weeks;
        }

        sb.AppendLine($"<p style='margin-top:1.5em;color:#64748B;font-size:0.9em;'>{(es ? "Duración total estimada" : "Total estimated duration")}: {weekOffset} {(es ? "semanas" : "weeks")}</p>");
        sb.AppendLine("</div></div>");

        return sb.ToString();
    }

    private static int CountAffected(Data.Entities.ServiceCatalogItem svc, ReportData data) =>
        ServiceCatalogBlock.CountAffected(svc, data);
}
```

Note: `CountAffected` in `ServiceCatalogBlock` needs to be changed to `internal static` so `TimelineBlock` can reuse it.

- [ ] **Step 3: Verify build + commit**

```bash
git add src/KryossApi/Services/Reports/Blocks/ServiceCatalogBlock.cs src/KryossApi/Services/Reports/Blocks/TimelineBlock.cs
git commit -m "feat(reports): add ServiceCatalogBlock + TimelineBlock — business proposal blocks"
```

---

### Task 5.4: ScoreTrendBlock + DeltaBlock (Monthly blocks)

**Files:**
- Create: `src/KryossApi/Services/Reports/Blocks/ScoreTrendBlock.cs`
- Create: `src/KryossApi/Services/Reports/Blocks/DeltaBlock.cs`

- [ ] **Step 1: Implement ScoreTrendBlock**

New block. 6-month sparkline of org-average score. Needs historical query — add to `ReportDataLoader` a `List<MonthlyScore> ScoreHistory` field.

```csharp
// Services/Reports/Blocks/ScoreTrendBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class ScoreTrendBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Evolución del Score" : "Score Evolution", data.Branding);
        sb.AppendLine("<div class='pb'>");

        // Current score + delta
        sb.AppendLine("<div style='text-align:center;padding:1.5em 0;'>");
        sb.AppendLine($"<div style='font-size:3.5em;font-weight:700;'>{data.AvgScore:F1}%</div>");
        if (data.PreviousMonthScore.HasValue)
        {
            var delta = data.AvgScore - data.PreviousMonthScore.Value;
            var arrow = delta > 0 ? "▲" : delta < 0 ? "▼" : "=";
            var color = delta > 0 ? "#008852" : delta < 0 ? "#C0392B" : "#6B7280";
            sb.AppendLine($"<div style='font-size:1.5em;color:{color};'>{arrow} {Math.Abs(delta):F1} pts</div>");
        }
        else
        {
            sb.AppendLine($"<div style='font-size:1.2em;color:#6B7280;'>— BASELINE</div>");
        }
        sb.AppendLine("</div>");

        // SVG sparkline if we have history
        if (data.ScoreHistory != null && data.ScoreHistory.Count > 1)
            RenderSparkline(sb, data.ScoreHistory);

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static void RenderSparkline(StringBuilder sb, List<MonthlyScore> history)
    {
        var width = 500;
        var height = 120;
        var points = new List<string>();
        var maxScore = 100m;
        var step = width / Math.Max(1, history.Count - 1);

        for (int i = 0; i < history.Count; i++)
        {
            var x = i * step;
            var y = height - (int)(history[i].Score / maxScore * height);
            points.Add($"{x},{y}");
        }

        sb.AppendLine($"<svg viewBox='0 0 {width} {height + 20}' style='width:100%;max-width:500px;margin:1em auto;display:block;'>");
        sb.AppendLine($"<polyline points='{string.Join(" ", points)}' fill='none' stroke='#008852' stroke-width='3'/>");
        for (int i = 0; i < history.Count; i++)
        {
            var x = i * step;
            var y = height - (int)(history[i].Score / maxScore * height);
            sb.AppendLine($"<circle cx='{x}' cy='{y}' r='4' fill='#008852'/>");
            sb.AppendLine($"<text x='{x}' y='{height + 15}' text-anchor='middle' font-size='10' fill='#64748B'>{history[i].Month:MMM}</text>");
        }
        sb.AppendLine("</svg>");
    }
}
```

Add to `ReportData.cs`:

```csharp
public List<MonthlyScore>? ScoreHistory { get; set; }

public class MonthlyScore
{
    public DateTime Month { get; set; }
    public decimal Score { get; set; }
}
```

Add history loading to `ReportDataLoader.LoadAsync`:

```csharp
// After previous month score loading, add:
var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
data.ScoreHistory = await _db.AssessmentRuns
    .Where(r => r.OrganizationId == orgId && r.CompletedAt != null && r.CompletedAt >= sixMonthsAgo && r.GlobalScore != null)
    .GroupBy(r => new { r.CompletedAt!.Value.Year, r.CompletedAt!.Value.Month })
    .Select(g => new MonthlyScore { Month = new DateTime(g.Key.Year, g.Key.Month, 1), Score = Math.Round((decimal)g.Average(r => r.GlobalScore!.Value), 1) })
    .OrderBy(x => x.Month)
    .ToListAsync();
```

- [ ] **Step 2: Implement DeltaBlock**

```csharp
// Services/Reports/Blocks/DeltaBlock.cs
using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class DeltaBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        // Compare current findings vs previous period
        // This uses data.ControlResults (current) vs a snapshot approach
        // For v1: show current fail counts grouped by severity
        // Future: compare against stored previous-period results
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        var critical = data.ControlResults.Count(r => r.Status == "fail" && r.Severity == "critical");
        var high = data.ControlResults.Count(r => r.Status == "fail" && r.Severity == "high");
        var medium = data.ControlResults.Count(r => r.Status == "fail" && r.Severity == "medium");
        var low = data.ControlResults.Count(r => r.Status == "fail" && r.Severity == "low");
        var totalPass = data.ControlResults.Count(r => r.Status == "pass");

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Estado Actual de Hallazgos" : "Current Findings Status", data.Branding);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='summary-grid'>");
        sb.AppendLine($"<div class='stat'><span class='stat-value' style='color:#008852;'>{totalPass}</span><span class='stat-label'>{(es ? "Controles Aprobados" : "Passing Controls")}</span></div>");
        if (critical > 0) sb.AppendLine($"<div class='stat fail-stat'><span class='stat-value'>{critical}</span><span class='stat-label'>{(es ? "Críticos" : "Critical")}</span></div>");
        if (high > 0) sb.AppendLine($"<div class='stat warn-stat'><span class='stat-value'>{high}</span><span class='stat-label'>{(es ? "Altos" : "High")}</span></div>");
        sb.AppendLine($"<div class='stat'><span class='stat-value'>{medium + low}</span><span class='stat-label'>{(es ? "Medio/Bajo" : "Medium/Low")}</span></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }
}
```

- [ ] **Step 3: Verify build + commit**

```bash
git add src/KryossApi/Services/Reports/Blocks/ScoreTrendBlock.cs src/KryossApi/Services/Reports/Blocks/DeltaBlock.cs src/KryossApi/Services/Reports/ReportData.cs src/KryossApi/Services/Reports/ReportDataLoader.cs
git commit -m "feat(reports): add ScoreTrendBlock + DeltaBlock — monthly progress blocks"
```

---

## Module 6: Wire recipes

### Task 6.1: All 7 recipes

**Files:**
- Create: `src/KryossApi/Services/Reports/Recipes/CLevelRecipe.cs`
- Create: `src/KryossApi/Services/Reports/Recipes/TechnicalRecipe.cs`
- Create: `src/KryossApi/Services/Reports/Recipes/PreventaOpenerRecipe.cs`
- Create: `src/KryossApi/Services/Reports/Recipes/PreventaDetailedRecipe.cs`
- Create: `src/KryossApi/Services/Reports/Recipes/MonthlyRecipe.cs`
- Create: `src/KryossApi/Services/Reports/Recipes/FrameworkRecipe.cs`
- Create: `src/KryossApi/Services/Reports/Recipes/ProposalRecipe.cs`

- [ ] **Step 1: CLevelRecipe**

```csharp
// Services/Reports/Recipes/CLevelRecipe.cs
using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class CLevelRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Informe Ejecutivo C-Level" : "C-Level Security Briefing";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("c-level");
        yield return new SemaforoBlock();
        yield return new KpiBlock();
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: true);
        yield return new CtaBlock();
    }
}
```

- [ ] **Step 2: TechnicalRecipe**

```csharp
// Services/Reports/Recipes/TechnicalRecipe.cs
using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class TechnicalRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        options.FrameworkName != null
            ? $"{options.FrameworkName} {(options.IsSpanish ? "Informe Técnico" : "Technical Report")}"
            : (options.IsSpanish ? "Informe Técnico de Seguridad" : "Security Technical Report");

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("technical");
        yield return new AssetMatrixBlock();
        yield return new TopFindingsBlock(topN: 10);
        yield return new IronSixBlock();
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: false);
        yield return new GapAnalysisBlock();
    }
}
```

- [ ] **Step 3: PreventaOpenerRecipe**

```csharp
// Services/Reports/Recipes/PreventaOpenerRecipe.cs
using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class PreventaOpenerRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Evaluación de Riesgo" : "Risk Assessment";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("preventa-opener");
        yield return new RiskScoreBlock();
        yield return new ThreatVectorsBlock();
        yield return new MethodologyBlock();
    }
}
```

- [ ] **Step 4: PreventaDetailedRecipe**

```csharp
// Services/Reports/Recipes/PreventaDetailedRecipe.cs
using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class PreventaDetailedRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Propuesta de Seguridad" : "Security Proposal";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("preventa-detailed");
        yield return new RiskScoreBlock();
        yield return new ThreatVectorsBlock();
        yield return new TopFindingsBlock(topN: 10);
        yield return new MethodologyBlock();
        yield return new ServiceCatalogBlock(showPricing: false);
        yield return new TimelineBlock();
    }
}
```

- [ ] **Step 5: FrameworkRecipe**

```csharp
// Services/Reports/Recipes/FrameworkRecipe.cs
using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class FrameworkRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        $"{options.FrameworkName} {(options.IsSpanish ? "Informe de Cumplimiento" : "Compliance Report")}";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("framework");
        yield return new FrameworkGaugeBlock();
        yield return new GapAnalysisBlock();
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: false);
        yield return new TimelineBlock();
    }
}
```

- [ ] **Step 6: ProposalRecipe**

```csharp
// Services/Reports/Recipes/ProposalRecipe.cs
using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class ProposalRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Propuesta Comercial de Remediación" : "Remediation Business Proposal";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("proposal");
        yield return new SemaforoBlock();
        yield return new TopFindingsBlock(topN: 10);
        yield return new GapAnalysisBlock();
        yield return new ServiceCatalogBlock(showPricing: true);
        yield return new TimelineBlock();
    }
}
```

- [ ] **Step 7: MonthlyRecipe**

```csharp
// Services/Reports/Recipes/MonthlyRecipe.cs
using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class MonthlyRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Informe de Progreso Mensual" : "Monthly Progress Report";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("monthly");
        yield return new ScoreTrendBlock();
        yield return new DeltaBlock();
        yield return new TopFindingsBlock(topN: 10, splitResolved: true);
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: true);
        yield return new KpiBlock();
    }
}
```

- [ ] **Step 8: Verify build**

Run: `dotnet build src/KryossApi/KryossApi.csproj`
Expected: Build succeeded

- [ ] **Step 9: Commit**

```bash
git add src/KryossApi/Services/Reports/Recipes/
git commit -m "feat(reports): add all 7 report recipes — C-Level, Technical, Preventa, Framework, Proposal, Monthly"
```

---

## Module 7: Wire everything together (DI + Function + cleanup)

### Task 7.1: DI registration

**Files:**
- Modify: `src/KryossApi/Program.cs`

- [ ] **Step 1: Register new services in Program.cs**

Add to the DI section:

```csharp
services.AddScoped<IReportDataLoader, ReportDataLoader>();
services.AddScoped<IReportComposer, ReportComposer>();
```

- [ ] **Step 2: Verify build + commit**

```bash
git add src/KryossApi/Program.cs
git commit -m "feat(reports): register ReportDataLoader + ReportComposer in DI"
```

---

### Task 7.2: Update ReportsFunction to route through ReportComposer

**Files:**
- Modify: `src/KryossApi/Functions/Portal/ReportsFunction.cs`

- [ ] **Step 1: Add ReportComposer to ReportsFunction constructor**

Add `IReportComposer` as dependency. In `GenerateOrg`, route new report types through the composer while keeping old types on the legacy `ReportService` during transition:

```csharp
private readonly IReportComposer _composer;

// In constructor:
public ReportsFunction(IReportService reports, IReportComposer composer, IActlogService actlog, KryossDbContext db, ICurrentUserService user)
{
    _reports = reports;
    _composer = composer;
    // ...
}

// In GenerateOrg, replace the switch at the end:
var newTypes = new HashSet<string> { "c-level", "technical", "preventa", "preventas", "presales", "presales-opener", "monthly", "monthly-briefing", "framework", "proposal" };

string html;
if (newTypes.Contains(reportType))
{
    if (reportType == "framework" && string.IsNullOrEmpty(frameworkCode))
    {
        var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
        await badReq.WriteAsJsonAsync(new { error = "Framework report requires ?framework= parameter" });
        return badReq;
    }
    var reportOptions = new KryossApi.Services.Reports.ReportOptions(
        Lang: lang,
        FrameworkCode: frameworkCode,
        FrameworkName: frameworkName,
        Tone: tone
    );
    html = await _composer.GenerateAsync(orgId, reportType, reportOptions);
}
else
{
    // Legacy fallback (executive, exec-onepager, m365)
    html = await _reports.GenerateOrgReportAsync(orgId, reportType, frameworkCode, lang, tone);
}
```

- [ ] **Step 2: Add `frameworkName` resolution before the switch**

The framework name needs to be resolved early for the options object. Add after the existing framework filter block:

```csharp
string? frameworkName = null;
if (!string.IsNullOrEmpty(frameworkCode))
{
    var fw = await _db.Frameworks.FirstOrDefaultAsync(f => f.Code == frameworkCode && f.IsActive);
    frameworkName = fw?.Name;
}
```

- [ ] **Step 3: Verify build + commit**

```bash
git add src/KryossApi/Functions/Portal/ReportsFunction.cs
git commit -m "feat(reports): route new report types through ReportComposer in ReportsFunction"
```

---

### Task 7.3: Service catalog + franchise rate API endpoints

**Files:**
- Create: `src/KryossApi/Functions/Portal/ServiceCatalogFunction.cs`

- [ ] **Step 1: Implement endpoints**

```csharp
// Functions/Portal/ServiceCatalogFunction.cs
using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

[RequirePermission("reports:read")]
public class ServiceCatalogFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public ServiceCatalogFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    [Function("ServiceCatalog_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/service-catalog")] HttpRequestData req)
    {
        var items = await _db.ServiceCatalog.Where(sc => sc.IsActive).OrderBy(sc => sc.SortOrder).ToListAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(items);
        return response;
    }

    [Function("FranchiseRates_Get")]
    public async Task<HttpResponseData> GetRate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/franchise-rates/{franchiseId:guid}")] HttpRequestData req,
        Guid franchiseId)
    {
        var rate = await _db.FranchiseServiceRates
            .Where(r => r.FranchiseId == franchiseId && r.EffectiveFrom <= DateTime.UtcNow)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(rate ?? new Data.Entities.FranchiseServiceRate { FranchiseId = franchiseId, HourlyRate = 150, Currency = "USD" });
        return response;
    }

    [Function("FranchiseRates_Set")]
    [RequirePermission("admin:write")]
    public async Task<HttpResponseData> SetRate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/franchise-rates/{franchiseId:guid}")] HttpRequestData req,
        Guid franchiseId)
    {
        var body = await req.ReadFromJsonAsync<RateUpdateDto>();
        if (body == null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Missing body" });
            return bad;
        }

        var rate = new Data.Entities.FranchiseServiceRate
        {
            FranchiseId = franchiseId,
            HourlyRate = body.HourlyRate,
            Currency = body.Currency ?? "USD",
            MarginPct = body.MarginPct ?? 0,
            EffectiveFrom = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _db.FranchiseServiceRates.Add(rate);
        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(rate);
        return response;
    }

    private record RateUpdateDto(decimal HourlyRate, string? Currency, decimal? MarginPct);
}
```

- [ ] **Step 2: Verify build + commit**

```bash
git add src/KryossApi/Functions/Portal/ServiceCatalogFunction.cs
git commit -m "feat(reports): add ServiceCatalogFunction — GET catalog + GET/PATCH franchise rates"
```

---

### Task 7.4: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`
- Modify: `KryossApi/CLAUDE.md`

- [ ] **Step 1: Add new endpoints to KryossApi/CLAUDE.md endpoint table**

Add:

```markdown
| GET | `/v2/reports/org/{orgId}?type=framework&framework=NIST` | `ReportsFunction.GenerateOrg` | Framework compliance report (requires framework param) |
| GET | `/v2/reports/org/{orgId}?type=proposal` | `ReportsFunction.GenerateOrg` | Auto-generated business proposal with pricing |
| GET | `/v2/reports/org/{orgId}?type=monthly` | `ReportsFunction.GenerateOrg` | Monthly progress report (v1, Kryoss data only) |
| GET | `/v2/service-catalog` | `ServiceCatalogFunction.List` | List active service catalog items |
| GET | `/v2/franchise-rates/{franchiseId}` | `ServiceCatalogFunction.GetRate` | Get franchise hourly rate |
| PATCH | `/v2/franchise-rates/{franchiseId}` | `ServiceCatalogFunction.SetRate` | Set franchise hourly rate + margin |
```

- [ ] **Step 2: Add decision to CLAUDE.md decision log**

Add:

```markdown
| 2026-04-19 | Unified Report System: 7 types, compositional blocks | Replaces monolithic ReportService with 18 blocks + 7 recipes. Cloud integrated. Auto-generated business proposals with service catalog pricing. Spec: `docs/superpowers/specs/2026-04-19-unified-report-system-design.md`. Plan: `docs/superpowers/plans/2026-04-19-unified-report-system.md`. |
```

- [ ] **Step 3: Add service catalog to KryossApi/CLAUDE.md services section**

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md KryossApi/CLAUDE.md
git commit -m "docs: update CLAUDE.md with unified report system endpoints + decision"
```

---

## Implementation Priority Summary

| Module | Tasks | Dependency | Effort |
|--------|-------|-----------|--------|
| **1. Infrastructure** | 1.1, 1.2, 1.3 | None | Small |
| **2. Extract helpers** | 2.1 | Module 1 | Medium (careful refactor) |
| **3. Loader + Composer** | 3.1, 3.2 | Module 2 | Medium |
| **4. Extract blocks** | 4.1-4.4 | Module 3 | Large (most code movement) |
| **5. New blocks** | 5.1-5.4 | Module 3 | Large (new functionality) |
| **6. Recipes** | 6.1 | Modules 4+5 | Small (composition only) |
| **7. Wiring** | 7.1-7.4 | Module 6 | Medium |

**Modules 4 and 5 can be parallelized** — extracting existing blocks and building new blocks are independent as long as Module 3 is done.

**Monthly recipe (Task 6.1 step 7) is P2** — implement last, after all other reports verified.
