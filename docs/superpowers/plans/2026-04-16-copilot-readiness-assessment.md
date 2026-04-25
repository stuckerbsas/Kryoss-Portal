# Copilot Readiness Assessment — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Full reimplementation of Microsoft's Copilot Readiness Assessment as a native Kryoss feature — 6-dimension scoring, 169 service plan checks, unified M365 report, portal dashboard.

**Architecture:** Single `CopilotReadinessService` with 6 parallel pipelines via `Task.WhenAll`. Results in 5 new DB tables. Portal sub-tab in M365. New `m365` report type. Weekly timer trigger.

**Tech Stack:** .NET 8 Azure Functions, EF Core 8, Microsoft Graph SDK, HttpClient for Defender/Power Platform APIs, React 18 + Vite + shadcn/ui, React Query.

**Spec:** `docs/superpowers/specs/2026-04-16-copilot-readiness-assessment-design.md`

---

## File Structure

### Backend — New Files

```
KryossApi/src/KryossApi/
├── Services/CopilotReadiness/
│   ├── ICopilotReadinessService.cs          — interface
│   ├── CopilotReadinessService.cs           — orchestrator (RunScanAsync, GetLatestScanAsync)
│   ├── ScoringEngine.cs                     — D1-D6 formula, weights, verdict
│   ├── ServicePlanMapping.cs                — 169-plan static dictionary
│   ├── Pipelines/
│   │   ├── PipelineResult.cs                — shared result DTO
│   │   ├── EntraPipeline.cs                 — Entra data collection + enrichment
│   │   ├── DefenderPipeline.cs              — Defender API + Graph Security
│   │   ├── M365Pipeline.cs                  — license analysis + usage reports
│   │   ├── PurviewPipeline.cs               — compliance license checks
│   │   ├── PowerPlatformPipeline.cs         — PP admin API
│   │   └── SharePointDeepPipeline.cs        — D1 labels + D2 oversharing + D3 external users
│   └── Recommendations/
│       ├── RecommendationResult.cs           — finding DTO
│       ├── EntraRecommendations.cs           — 14 plan checks + enrichment
│       ├── DefenderRecommendations.cs        — 17 plan checks + enrichment
│       ├── PurviewRecommendations.cs         — 36 plan checks
│       ├── M365Recommendations.cs            — 85+ plan checks + enrichment
│       ├── PowerPlatformRecommendations.cs   — 17 plan checks
│       └── CopilotStudioRecommendations.cs   — 11 plan checks
├── Services/CopilotReadinessReportBuilder.cs — HTML report builder
├── Functions/Portal/CopilotReadinessFunction.cs — 4 endpoints
├── Functions/Timer/CopilotReadinessTimerFunction.cs — weekly scan
├── Data/Entities/CopilotReadiness.cs         — 5 EF entities
sql/
└── 029_copilot_readiness.sql                 — 5 tables + indexes
```

### Backend — Modified Files

```
KryossApi/src/KryossApi/
├── Data/KryossDbContext.cs                   — add 5 DbSets + OnModelCreating config
├── Services/ReportService.cs                 — add "m365" case to switch
├── Program.cs                                — register DI
```

### Portal — New Files

```
KryossPortal/src/
├── api/copilotReadiness.ts                   — React Query hooks
├── components/org-detail/CopilotReadinessTab.tsx — dashboard component
```

### Portal — Modified Files

```
KryossPortal/src/
├── components/org-detail/M365Tab.tsx          — add sub-tab navigation
├── components/reports/ReportGenerator.tsx      — add "m365" report type to dropdown
```

---

## Phase 1: Database Foundation

### Task 1: SQL Migration

**Files:**
- Create: `KryossApi/sql/029_copilot_readiness.sql`

- [ ] **Step 1: Write migration file**

```sql
-- 029_copilot_readiness.sql
-- Copilot Readiness Assessment tables (Phase 5)

CREATE TABLE copilot_readiness_scans (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id     UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    tenant_id           UNIQUEIDENTIFIER NOT NULL REFERENCES m365_tenants(id) ON DELETE CASCADE,
    status              VARCHAR(20)      NOT NULL DEFAULT 'running',
    d1_score            DECIMAL(3,2),
    d2_score            DECIMAL(3,2),
    d3_score            DECIMAL(3,2),
    d4_score            DECIMAL(3,2),
    d5_score            DECIMAL(3,2),
    d6_score            DECIMAL(3,2),
    overall_score       DECIMAL(3,2),
    verdict             VARCHAR(20),
    pipeline_status     NVARCHAR(MAX),
    started_at          DATETIME2(2)     NOT NULL,
    completed_at        DATETIME2(2),
    created_at          DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_copilot_scans_org ON copilot_readiness_scans(organization_id, created_at DESC);

CREATE TABLE copilot_readiness_metrics (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    dimension           VARCHAR(10)      NOT NULL,
    metric_key          VARCHAR(100)     NOT NULL,
    metric_value        NVARCHAR(500)    NOT NULL,
    created_at          DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_copilot_metrics_scan ON copilot_readiness_metrics(scan_id);

CREATE TABLE copilot_readiness_findings (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    service             VARCHAR(30)      NOT NULL,
    feature             NVARCHAR(200)    NOT NULL,
    status              VARCHAR(30)      NOT NULL,
    priority            VARCHAR(10)      NOT NULL DEFAULT '',
    observation         NVARCHAR(MAX),
    recommendation      NVARCHAR(MAX),
    link_text           NVARCHAR(500),
    link_url            NVARCHAR(500),
    created_at          DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_copilot_findings_scan ON copilot_readiness_findings(scan_id, service);

CREATE TABLE copilot_readiness_sharepoint (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    site_url            NVARCHAR(500)    NOT NULL,
    site_title          NVARCHAR(500),
    total_files         INT              NOT NULL DEFAULT 0,
    labeled_files       INT              NOT NULL DEFAULT 0,
    overshared_files    INT              NOT NULL DEFAULT 0,
    risk_level          VARCHAR(10),
    top_labels          NVARCHAR(MAX),
    created_at          DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_copilot_sp_scan ON copilot_readiness_sharepoint(scan_id);

CREATE TABLE copilot_readiness_external_users (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    scan_id             UNIQUEIDENTIFIER NOT NULL REFERENCES copilot_readiness_scans(id) ON DELETE CASCADE,
    user_principal      NVARCHAR(500)    NOT NULL,
    display_name        NVARCHAR(500),
    email_domain        NVARCHAR(200),
    last_sign_in        DATETIME2(2),
    risk_level          VARCHAR(10),
    sites_accessed      INT              NOT NULL DEFAULT 0,
    highest_permission  NVARCHAR(50),
    created_at          DATETIME2(2)     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ix_copilot_ext_scan ON copilot_readiness_external_users(scan_id);
```

- [ ] **Step 2: Run migration against KryossDb**

Run: `sqlcmd -S tcp:sql-kryoss.database.windows.net,1433 -d KryossDb -U kryossadmin -P <password> -i KryossApi/sql/029_copilot_readiness.sql`
Expected: Commands completed successfully.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/sql/029_copilot_readiness.sql
git commit -m "feat(copilot-readiness): add 5 tables for Copilot Readiness Assessment"
```

---

### Task 2: EF Entities

**Files:**
- Create: `KryossApi/src/KryossApi/Data/Entities/CopilotReadiness.cs`

- [ ] **Step 1: Write entity classes**

```csharp
namespace KryossApi.Data.Entities;

public class CopilotReadinessScan
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid TenantId { get; set; }
    public string Status { get; set; } = "running";
    public decimal? D1Score { get; set; }
    public decimal? D2Score { get; set; }
    public decimal? D3Score { get; set; }
    public decimal? D4Score { get; set; }
    public decimal? D5Score { get; set; }
    public decimal? D6Score { get; set; }
    public decimal? OverallScore { get; set; }
    public string? Verdict { get; set; }
    public string? PipelineStatus { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public M365Tenant Tenant { get; set; } = null!;
    public ICollection<CopilotReadinessMetric> Metrics { get; set; } = [];
    public ICollection<CopilotReadinessFinding> Findings { get; set; } = [];
    public ICollection<CopilotReadinessSharepoint> SharepointSites { get; set; } = [];
    public ICollection<CopilotReadinessExternalUser> ExternalUsers { get; set; } = [];
}

public class CopilotReadinessMetric
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string Dimension { get; set; } = null!;
    public string MetricKey { get; set; } = null!;
    public string MetricValue { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public CopilotReadinessScan Scan { get; set; } = null!;
}

public class CopilotReadinessFinding
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string Service { get; set; } = null!;
    public string Feature { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string Priority { get; set; } = "";
    public string? Observation { get; set; }
    public string? Recommendation { get; set; }
    public string? LinkText { get; set; }
    public string? LinkUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public CopilotReadinessScan Scan { get; set; } = null!;
}

public class CopilotReadinessSharepoint
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string SiteUrl { get; set; } = null!;
    public string? SiteTitle { get; set; }
    public int TotalFiles { get; set; }
    public int LabeledFiles { get; set; }
    public int OversharedFiles { get; set; }
    public string? RiskLevel { get; set; }
    public string? TopLabels { get; set; }
    public DateTime CreatedAt { get; set; }

    public CopilotReadinessScan Scan { get; set; } = null!;
}

public class CopilotReadinessExternalUser
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string UserPrincipal { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? EmailDomain { get; set; }
    public DateTime? LastSignIn { get; set; }
    public string? RiskLevel { get; set; }
    public int SitesAccessed { get; set; }
    public string? HighestPermission { get; set; }
    public DateTime CreatedAt { get; set; }

    public CopilotReadinessScan Scan { get; set; } = null!;
}
```

- [ ] **Step 2: Commit**

```bash
git add KryossApi/src/KryossApi/Data/Entities/CopilotReadiness.cs
git commit -m "feat(copilot-readiness): add EF entity classes"
```

---

### Task 3: DbContext Registration

**Files:**
- Modify: `KryossApi/src/KryossApi/Data/KryossDbContext.cs`

- [ ] **Step 1: Add DbSets**

Add after existing M365 DbSets:

```csharp
public DbSet<CopilotReadinessScan> CopilotReadinessScans => Set<CopilotReadinessScan>();
public DbSet<CopilotReadinessMetric> CopilotReadinessMetrics => Set<CopilotReadinessMetric>();
public DbSet<CopilotReadinessFinding> CopilotReadinessFindings => Set<CopilotReadinessFinding>();
public DbSet<CopilotReadinessSharepoint> CopilotReadinessSharepoint => Set<CopilotReadinessSharepoint>();
public DbSet<CopilotReadinessExternalUser> CopilotReadinessExternalUsers => Set<CopilotReadinessExternalUser>();
```

- [ ] **Step 2: Add OnModelCreating configuration**

Add in `OnModelCreating` method after existing M365 entity config:

```csharp
mb.Entity<CopilotReadinessScan>(e =>
{
    e.ToTable("copilot_readiness_scans");
    e.HasKey(x => x.Id);
    e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
    e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
    e.HasMany(x => x.Metrics).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
    e.HasMany(x => x.Findings).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
    e.HasMany(x => x.SharepointSites).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
    e.HasMany(x => x.ExternalUsers).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
});

mb.Entity<CopilotReadinessMetric>(e =>
{
    e.ToTable("copilot_readiness_metrics");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).UseIdentityColumn();
});

mb.Entity<CopilotReadinessFinding>(e =>
{
    e.ToTable("copilot_readiness_findings");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).UseIdentityColumn();
});

mb.Entity<CopilotReadinessSharepoint>(e =>
{
    e.ToTable("copilot_readiness_sharepoint");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).UseIdentityColumn();
});

mb.Entity<CopilotReadinessExternalUser>(e =>
{
    e.ToTable("copilot_readiness_external_users");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).UseIdentityColumn();
});
```

- [ ] **Step 3: Build to verify**

Run: `cd KryossApi/src/KryossApi && dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add KryossApi/src/KryossApi/Data/KryossDbContext.cs
git commit -m "feat(copilot-readiness): register 5 entities in DbContext"
```

---

## Phase 2: Scoring Engine + Service Plan Mapping

### Task 4: Service Plan Mapping

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/ServicePlanMapping.cs`

- [ ] **Step 1: Write the static mapping dictionary**

Port the Python `SERVICE_PLAN_MAPPING` dict. This is the routing table that classifies each M365 service plan into one of 6 categories.

```csharp
namespace KryossApi.Services.CopilotReadiness;

public static class ServicePlanMapping
{
    public enum ServiceCategory { Entra, Defender, Purview, M365, PowerPlatform, CopilotStudio }

    private static readonly Dictionary<string, ServiceCategory> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Entra (14)
        ["AAD_PREMIUM"] = ServiceCategory.Entra,
        ["AAD_PREMIUM_P2"] = ServiceCategory.Entra,
        ["AAD_PREMIUM_CONDITIONAL_ACCESS"] = ServiceCategory.Entra,
        ["AAD_PREMIUM_IDENTITY_PROTECTION"] = ServiceCategory.Entra,
        ["AAD_PREMIUM_P1"] = ServiceCategory.Entra,
        ["AAD_GOVERNANCE"] = ServiceCategory.Entra,
        ["MFA_PREMIUM"] = ServiceCategory.Entra,
        ["INTUNE_A"] = ServiceCategory.Entra,
        ["ENTRA_INTERNET_ACCESS"] = ServiceCategory.Entra,
        ["ENTRA_INTERNET_ACCESS_FRONTLINE"] = ServiceCategory.Entra,
        ["ENTRA_PRIVATE_ACCESS"] = ServiceCategory.Entra,
        ["ENTRA_PRIVATE_ACCESS_CA"] = ServiceCategory.Entra,

        // Defender (17)
        ["WINDEFATP"] = ServiceCategory.Defender,
        ["ATP_ENTERPRISE"] = ServiceCategory.Defender,
        ["MTP"] = ServiceCategory.Defender,
        ["ATA"] = ServiceCategory.Defender,
        ["ADALLOM_S_DISCOVERY"] = ServiceCategory.Defender,
        ["ADALLOM_S_O365"] = ServiceCategory.Defender,
        ["ADALLOM_S_STANDALONE"] = ServiceCategory.Defender,
        ["EOP_ENTERPRISE_PREMIUM"] = ServiceCategory.Defender,
        ["SAFEDOCS"] = ServiceCategory.Defender,
        ["THREAT_INTELLIGENCE"] = ServiceCategory.Defender,
        ["DEFENDER_FOR_IOT"] = ServiceCategory.Defender,
        ["Defender_for_Iot_Enterprise"] = ServiceCategory.Defender,

        // Purview (36)
        ["AIP_P1"] = ServiceCategory.Purview,
        ["AIP_P2"] = ServiceCategory.Purview,
        ["COMMUNICATIONS_COMPLIANCE"] = ServiceCategory.Purview,
        ["COMMUNICATIONS_DLP"] = ServiceCategory.Purview,
        ["ContentExplorer_Standard"] = ServiceCategory.Purview,
        ["CONTENTEXPLORER_STANDARD_ACTIVITY"] = ServiceCategory.Purview,
        ["Content_Explorer"] = ServiceCategory.Purview,
        ["CustomerLockboxA_Enterprise"] = ServiceCategory.Purview,
        ["CUSTOMER_KEY"] = ServiceCategory.Purview,
        ["DATAINVESTIGATION"] = ServiceCategory.Purview,
        ["DATA_INVESTIGATIONS"] = ServiceCategory.Purview,
        ["EDISCOVERY"] = ServiceCategory.Purview,
        ["EQUIVIO_ANALYTICS"] = ServiceCategory.Purview,
        ["EQUIVIO_ANALYTICS_EDM"] = ServiceCategory.Purview,
        ["INFORMATION_BARRIERS"] = ServiceCategory.Purview,
        ["INFORMATION_PROTECTION_ANALYTICS"] = ServiceCategory.Purview,
        ["INFORMATION_PROTECTION_COMPLIANCE_PREMIUM"] = ServiceCategory.Purview,
        ["INFO_GOVERNANCE"] = ServiceCategory.Purview,
        ["INSIDER_RISK"] = ServiceCategory.Purview,
        ["INSIDER_RISK_MANAGEMENT"] = ServiceCategory.Purview,
        ["LOCKBOX_ENTERPRISE"] = ServiceCategory.Purview,
        ["M365_ADVANCED_AUDITING"] = ServiceCategory.Purview,
        ["M365_AUDIT_PLATFORM"] = ServiceCategory.Purview,
        ["MICROSOFTENDPOINTDLP"] = ServiceCategory.Purview,
        ["MICROSOFT_COMMUNICATION_COMPLIANCE"] = ServiceCategory.Purview,
        ["MIP_S_CLP1"] = ServiceCategory.Purview,
        ["MIP_S_CLP2"] = ServiceCategory.Purview,
        ["MIP_S_Exchange"] = ServiceCategory.Purview,
        ["ML_CLASSIFICATION"] = ServiceCategory.Purview,
        ["PAM_ENTERPRISE"] = ServiceCategory.Purview,
        ["PREMIUM_ENCRYPTION"] = ServiceCategory.Purview,
        ["PURVIEW_DISCOVERY"] = ServiceCategory.Purview,
        ["RECORDS_MANAGEMENT"] = ServiceCategory.Purview,
        ["RMS_S_ENTERPRISE"] = ServiceCategory.Purview,
        ["RMS_S_PREMIUM"] = ServiceCategory.Purview,
        ["RMS_S_PREMIUM2"] = ServiceCategory.Purview,

        // M365 (85+) — representative subset, full list ported from Python
        ["EXCHANGE_S_ENTERPRISE"] = ServiceCategory.M365,
        ["EXCHANGE_S_STANDARD"] = ServiceCategory.M365,
        ["EXCHANGE_S_FOUNDATION"] = ServiceCategory.M365,
        ["EXCHANGE_S_DESKLESS"] = ServiceCategory.M365,
        ["EXCHANGE_S_ARCHIVE_ADDON"] = ServiceCategory.M365,
        ["EXCHANGE_ANALYTICS"] = ServiceCategory.M365,
        ["EXCHANGEDESKLESS"] = ServiceCategory.M365,
        ["EXCHANGEDESKLESS_GOV"] = ServiceCategory.M365,
        ["TEAMS1"] = ServiceCategory.M365,
        ["TEAMSPRO_CUST"] = ServiceCategory.M365,
        ["TEAMSPRO_MGMT"] = ServiceCategory.M365,
        ["TEAMSPRO_PROTECTION"] = ServiceCategory.M365,
        ["TEAMSPRO_VIRTUALAPPT"] = ServiceCategory.M365,
        ["TEAMSPRO_WEBINAR"] = ServiceCategory.M365,
        ["TEAMS_PREMIUM_CUSTOMER"] = ServiceCategory.M365,
        ["SHAREPOINTENTERPRISE"] = ServiceCategory.M365,
        ["SHAREPOINTSTANDARD"] = ServiceCategory.M365,
        ["SHAREPOINTWAC"] = ServiceCategory.M365,
        ["ONEDRIVE_BASIC_P2"] = ServiceCategory.M365,
        ["OFFICESUBSCRIPTION"] = ServiceCategory.M365,
        ["OFFICEMOBILE_SUBSCRIPTION"] = ServiceCategory.M365,
        ["M365_COPILOT_APPS"] = ServiceCategory.M365,
        ["M365_COPILOT_BUSINESS_CHAT"] = ServiceCategory.M365,
        ["M365_COPILOT_CONNECTORS"] = ServiceCategory.M365,
        ["M365_COPILOT_INTELLIGENT_SEARCH"] = ServiceCategory.M365,
        ["M365_COPILOT_SHAREPOINT"] = ServiceCategory.M365,
        ["M365_COPILOT_TEAMS"] = ServiceCategory.M365,
        ["GRAPH_CONNECTORS_COPILOT"] = ServiceCategory.M365,
        ["GRAPH_CONNECTORS_SEARCH_INDEX"] = ServiceCategory.M365,
        ["Bing_Chat_Enterprise"] = ServiceCategory.M365,
        ["MICROSOFT_LOOP"] = ServiceCategory.M365,
        ["MICROSOFT_SEARCH"] = ServiceCategory.M365,
        ["CLIPCHAMP"] = ServiceCategory.M365,
        ["SWAY"] = ServiceCategory.M365,
        ["FORMS_PLAN_E1"] = ServiceCategory.M365,
        ["FORMS_PLAN_E5"] = ServiceCategory.M365,
        ["BPOS_S_TODO_3"] = ServiceCategory.M365,
        ["PROJECTWORKMANAGEMENT"] = ServiceCategory.M365,
        ["PROJECTWORKMANAGEMENT_PLANNER"] = ServiceCategory.M365,
        ["PROJECT_O365_P1"] = ServiceCategory.M365,
        ["PROJECT_O365_P3"] = ServiceCategory.M365,
        ["DESKLESS"] = ServiceCategory.M365,
        ["EXCEL_PREMIUM"] = ServiceCategory.M365,
        ["MICROSOFTBOOKINGS"] = ServiceCategory.M365,
        ["MICROSOFT_ECDN"] = ServiceCategory.M365,
        ["MICROSOFT_MYANALYTICS_FULL"] = ServiceCategory.M365,
        ["MICROSOFT_PLACES"] = ServiceCategory.M365,
        ["INSIGHTS_BY_MYANALYTICS"] = ServiceCategory.M365,
        ["MYANALYTICS_P1"] = ServiceCategory.M365,
        ["MYANALYTICS_P2"] = ServiceCategory.M365,
        ["MYANALYTICS_P3"] = ServiceCategory.M365,
        ["NUCLEUS"] = ServiceCategory.M365,
        ["PEOPLE_SKILLS_FOUNDATION"] = ServiceCategory.M365,
        ["PLACES_CORE"] = ServiceCategory.M365,
        ["PLACES_ENHANCED"] = ServiceCategory.M365,
        ["QUEUES_APP"] = ServiceCategory.M365,
        ["STREAM_O365_E5"] = ServiceCategory.M365,
        ["UNIVERSAL_PRINT_01"] = ServiceCategory.M365,
        ["VIVAENGAGE_CORE"] = ServiceCategory.M365,
        ["VIVA_GOALS"] = ServiceCategory.M365,
        ["VIVA_INSIGHTS_BACKEND"] = ServiceCategory.M365,
        ["VIVA_INSIGHTS_MYANALYTICS_FULL"] = ServiceCategory.M365,
        ["VIVA_LEARNING_SEEDED"] = ServiceCategory.M365,
        ["WHITEBOARD_PLAN3"] = ServiceCategory.M365,
        ["WIN10_PRO_ENT_SUB"] = ServiceCategory.M365,
        ["WIN10_VDA_E3"] = ServiceCategory.M365,
        ["WINDOWSUPDATEFORBUSINESS_DEPLOYMENTSERVICE"] = ServiceCategory.M365,
        ["WINDOWS_AUTOPATCH"] = ServiceCategory.M365,
        ["WORKPLACE_ANALYTICS_INSIGHTS_BACKEND"] = ServiceCategory.M365,
        ["WORKPLACE_ANALYTICS_INSIGHTS_USER"] = ServiceCategory.M365,
        ["YAMMER_ENTERPRISE"] = ServiceCategory.M365,
        ["MCOEV"] = ServiceCategory.M365,
        ["MCOEV_VIRTUALUSER"] = ServiceCategory.M365,
        ["MCOIMP"] = ServiceCategory.M365,
        ["MCOMEETADV"] = ServiceCategory.M365,
        ["MCOSTANDARD"] = ServiceCategory.M365,
        ["MCOSTANDARD_GOV"] = ServiceCategory.M365,
        ["MCO_VIRTUAL_APPT"] = ServiceCategory.M365,
        ["MCO_VIRTUAL_APPT_PREMIUM"] = ServiceCategory.M365,
        ["MESH"] = ServiceCategory.M365,
        ["MESH_AVATARS_ADDITIONAL_FOR_TEAMS"] = ServiceCategory.M365,
        ["MESH_AVATARS_FOR_TEAMS"] = ServiceCategory.M365,
        ["MESH_IMMERSIVE"] = ServiceCategory.M365,
        ["MESH_IMMERSIVE_FOR_TEAMS"] = ServiceCategory.M365,
        ["INTUNE_O365"] = ServiceCategory.M365,
        ["INTUNE_SUITE"] = ServiceCategory.M365,
        ["KAIZALA_STANDALONE"] = ServiceCategory.M365,
        ["M365_LIGHTHOUSE_CUSTOMER_PLAN1"] = ServiceCategory.M365,
        ["TEAMWORK_ANALYTICS"] = ServiceCategory.M365,

        // Power Platform (17)
        ["FLOW_O365_P3"] = ServiceCategory.PowerPlatform,
        ["FLOW_FREE"] = ServiceCategory.PowerPlatform,
        ["FLOW_P2_VIRAL"] = ServiceCategory.PowerPlatform,
        ["FLOW_CCI_BOTS"] = ServiceCategory.PowerPlatform,
        ["POWERAPPS_O365_P3"] = ServiceCategory.PowerPlatform,
        ["CDS_O365_P1"] = ServiceCategory.PowerPlatform,
        ["CDS_O365_P2"] = ServiceCategory.PowerPlatform,
        ["CDS_O365_P3"] = ServiceCategory.PowerPlatform,
        ["CDS_VIRAL"] = ServiceCategory.PowerPlatform,
        ["DYN365_CDS_O365_P1"] = ServiceCategory.PowerPlatform,
        ["DYN365_CDS_O365_P2"] = ServiceCategory.PowerPlatform,
        ["DYN365_CDS_O365_P3"] = ServiceCategory.PowerPlatform,
        ["DYN365_CDS_VIRAL"] = ServiceCategory.PowerPlatform,
        ["DYN365_CDS_CCI_BOTS"] = ServiceCategory.PowerPlatform,
        ["BI_AZURE_P2"] = ServiceCategory.PowerPlatform,

        // Copilot Studio (11)
        ["POWER_VIRTUAL_AGENTS"] = ServiceCategory.CopilotStudio,
        ["POWER_VIRTUAL_AGENTS_BASE"] = ServiceCategory.CopilotStudio,
        ["POWER_VIRTUAL_AGENTS_O365_P3"] = ServiceCategory.CopilotStudio,
        ["CDS_VIRTUAL_AGENT_BASE_MESSAGES"] = ServiceCategory.CopilotStudio,
        ["CDS_VIRTUAL_AGENT_USL"] = ServiceCategory.CopilotStudio,
        ["COPILOT_STUDIO_IN_COPILOT_FOR_M365"] = ServiceCategory.CopilotStudio,
        ["FLOW_VIRTUAL_AGENT_BASE_MESSAGES"] = ServiceCategory.CopilotStudio,
        ["FLOW_VIRTUAL_AGENT_USL"] = ServiceCategory.CopilotStudio,
        ["VIRTUAL_AGENT_BASE_MESSAGES"] = ServiceCategory.CopilotStudio,
        ["VIRTUAL_AGENT_USL"] = ServiceCategory.CopilotStudio,
        ["CCIBOTS_PRIVPREV_VIRAL"] = ServiceCategory.CopilotStudio,
    };

    public static ServiceCategory? Classify(string servicePlanName)
    {
        return Map.TryGetValue(servicePlanName, out var cat) ? cat : null;
    }

    public static IReadOnlyDictionary<string, ServiceCategory> All => Map;
}
```

- [ ] **Step 2: Build to verify**

Run: `cd KryossApi/src/KryossApi && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/ServicePlanMapping.cs
git commit -m "feat(copilot-readiness): add 169 service plan mapping dictionary"
```

---

### Task 5: Scoring Engine

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/ScoringEngine.cs`

- [ ] **Step 1: Write scoring engine**

```csharp
namespace KryossApi.Services.CopilotReadiness;

public static class ScoringEngine
{
    public record DimensionScores(
        decimal D1, decimal D2, decimal D3, decimal D4, decimal D5, decimal D6,
        decimal Overall, string Verdict);

    public static decimal ScoreD1(decimal labelCoveragePct)
    {
        if (labelCoveragePct >= 80) return 5m;
        if (labelCoveragePct >= 60) return 3m;
        if (labelCoveragePct >= 40) return 2m;
        return 1m;
    }

    public static decimal ScoreD2(decimal oversharedPct)
    {
        if (oversharedPct < 5) return 5m;
        if (oversharedPct < 10) return 3m;
        if (oversharedPct < 20) return 2m;
        return 1m;
    }

    public static decimal ScoreD3(int highRiskExternal, int pendingInvites)
    {
        if (highRiskExternal == 0 && pendingInvites == 0) return 5m;
        if (highRiskExternal == 0 && pendingInvites < 10) return 4m;
        if (highRiskExternal < 10) return 3m;
        if (highRiskExternal < 50) return 2m;
        return 1m;
    }

    public static decimal ScoreD4(decimal caCompatScorePct)
    {
        if (caCompatScorePct >= 90) return 5m;
        if (caCompatScorePct >= 75) return 4m;
        if (caCompatScorePct >= 60) return 3m;
        if (caCompatScorePct >= 40) return 2m;
        return 1m;
    }

    public static decimal ScoreD5(int entraGaps, int defenderCritical, int defenderGaps)
    {
        var n = entraGaps + (defenderCritical * 2) + defenderGaps;
        if (n == 0) return 5m;
        if (n <= 2) return 4m;
        if (n <= 5) return 3m;
        if (n <= 8) return 2m;
        return 1m;
    }

    public static decimal ScoreD6(int purviewHighGaps)
    {
        if (purviewHighGaps == 0) return 5m;
        if (purviewHighGaps <= 2) return 4m;
        if (purviewHighGaps <= 5) return 3m;
        if (purviewHighGaps <= 8) return 2m;
        return 1m;
    }

    public static DimensionScores Compute(
        decimal labelCoveragePct, decimal oversharedPct,
        int highRiskExternal, int pendingInvites,
        decimal caCompatScorePct,
        int entraGaps, int defenderCritical, int defenderGaps,
        int purviewHighGaps)
    {
        var d1 = ScoreD1(labelCoveragePct);
        var d2 = ScoreD2(oversharedPct);
        var d3 = ScoreD3(highRiskExternal, pendingInvites);
        var d4 = ScoreD4(caCompatScorePct);
        var d5 = ScoreD5(entraGaps, defenderCritical, defenderGaps);
        var d6 = ScoreD6(purviewHighGaps);

        var overall = Math.Round(
            d1 * 0.25m + d2 * 0.25m + d3 * 0.20m +
            d4 * 0.15m + d5 * 0.10m + d6 * 0.05m, 2);

        var verdict = overall >= 4.0m ? "Ready"
                    : overall >= 3.0m ? "Nearly Ready"
                    : "Not Ready";

        return new DimensionScores(d1, d2, d3, d4, d5, d6, overall, verdict);
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `cd KryossApi/src/KryossApi && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/ScoringEngine.cs
git commit -m "feat(copilot-readiness): add D1-D6 scoring engine with weighted formula"
```

---

### Task 6: Pipeline Result + Recommendation DTOs

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/PipelineResult.cs`
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/RecommendationResult.cs`

- [ ] **Step 1: Write PipelineResult**

```csharp
namespace KryossApi.Services.CopilotReadiness.Pipelines;

public class PipelineResult
{
    public string PipelineName { get; set; } = null!;
    public string Status { get; set; } = "ok"; // ok, partial, failed, no_consent
    public string? Error { get; set; }
    public List<RecommendationResult> Findings { get; set; } = [];
    public Dictionary<string, string> Metrics { get; set; } = [];

    // SharePoint-specific (D1+D2)
    public List<SharepointSiteResult> SharepointSites { get; set; } = [];
    // External users (D3)
    public List<ExternalUserResult> ExternalUsers { get; set; } = [];
}

public class SharepointSiteResult
{
    public string SiteUrl { get; set; } = null!;
    public string? SiteTitle { get; set; }
    public int TotalFiles { get; set; }
    public int LabeledFiles { get; set; }
    public int OversharedFiles { get; set; }
    public string? RiskLevel { get; set; }
    public List<string> TopLabels { get; set; } = [];
}

public class ExternalUserResult
{
    public string UserPrincipal { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? EmailDomain { get; set; }
    public DateTime? LastSignIn { get; set; }
    public string? RiskLevel { get; set; }
    public int SitesAccessed { get; set; }
    public string? HighestPermission { get; set; }
}
```

- [ ] **Step 2: Write RecommendationResult**

```csharp
namespace KryossApi.Services.CopilotReadiness.Recommendations;

public class RecommendationResult
{
    public string Service { get; set; } = null!;
    public string Feature { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string Priority { get; set; } = "";
    public string? Observation { get; set; }
    public string? Recommendation { get; set; }
    public string? LinkText { get; set; }
    public string? LinkUrl { get; set; }

    public static RecommendationResult Success(string service, string feature, string observation,
        string? linkText = null, string? linkUrl = null) => new()
    {
        Service = service, Feature = feature, Status = "Success",
        Observation = observation, LinkText = linkText, LinkUrl = linkUrl
    };

    public static RecommendationResult ActionRequired(string service, string feature,
        string priority, string observation, string recommendation,
        string? linkText = null, string? linkUrl = null) => new()
    {
        Service = service, Feature = feature, Status = "Action Required",
        Priority = priority, Observation = observation, Recommendation = recommendation,
        LinkText = linkText, LinkUrl = linkUrl
    };

    public static RecommendationResult Warning(string service, string feature,
        string priority, string observation, string recommendation,
        string? linkText = null, string? linkUrl = null) => new()
    {
        Service = service, Feature = feature, Status = "Warning",
        Priority = priority, Observation = observation, Recommendation = recommendation,
        LinkText = linkText, LinkUrl = linkUrl
    };

    public static RecommendationResult Disabled(string service, string feature,
        string priority, string observation, string recommendation,
        string? linkText = null, string? linkUrl = null) => new()
    {
        Service = service, Feature = feature, Status = "Disabled",
        Priority = priority, Observation = observation, Recommendation = recommendation,
        LinkText = linkText, LinkUrl = linkUrl
    };

    public static RecommendationResult NotLicensed(string service, string feature,
        string recommendation, string? linkText = null, string? linkUrl = null) => new()
    {
        Service = service, Feature = feature, Status = "Not Licensed",
        Priority = "Medium", Observation = $"{feature} is not licensed in this tenant.",
        Recommendation = recommendation, LinkText = linkText, LinkUrl = linkUrl
    };

    public static RecommendationResult PermissionRequired(string service, string feature,
        string permission) => new()
    {
        Service = service, Feature = feature, Status = "Permission Required",
        Priority = "Low", Observation = $"Insufficient permissions to check {feature}.",
        Recommendation = $"Grant {permission} permission and re-scan."
    };
}
```

- [ ] **Step 3: Build to verify**

Run: `cd KryossApi/src/KryossApi && dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/PipelineResult.cs
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/RecommendationResult.cs
git commit -m "feat(copilot-readiness): add PipelineResult and RecommendationResult DTOs"
```

---

## Phase 3: Pipelines (6 parallel data collectors)

Each pipeline follows the same pattern: create Graph/HTTP client, call APIs, catch errors per-call, return `PipelineResult`. These are large files — each pipeline is implemented as a single static class with one public `RunAsync` method.

### Task 7: Entra Pipeline

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/EntraPipeline.cs`
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/EntraRecommendations.cs`

- [ ] **Step 1: Write EntraPipeline**

This is the heaviest pipeline. Calls ~22 Graph endpoints in parallel via `Task.WhenAll`. Collects CA policies, MFA status, risky users, PIM, access reviews, devices, B2B, OAuth apps, sign-in logs, GSA.

Read the spec Section 12.1 (endpoints 1-22) and Section 12.2 (endpoints 37-40) for the complete list. Read `CopilotReadinessAssessment/python_assessment/Core/get_entra_client.py` for the exact Graph SDK calls and data extraction logic. Port to C# using `GraphServiceClient`.

Key structure:
```csharp
namespace KryossApi.Services.CopilotReadiness.Pipelines;

using Microsoft.Graph;
using Microsoft.Graph.Models;
using KryossApi.Services.CopilotReadiness.Recommendations;

public static class EntraPipeline
{
    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph, HttpClient httpClient,
        ILogger log, CancellationToken ct)
    {
        var result = new PipelineResult { PipelineName = "entra" };
        var insights = new EntraInsights();

        // Parallel data collection
        var tasks = new List<Task>();
        tasks.Add(CollectCaPolicies(graph, insights, log, ct));
        tasks.Add(CollectMfaStatus(graph, insights, log, ct));
        tasks.Add(CollectRiskyUsers(graph, insights, log, ct));
        tasks.Add(CollectPim(graph, insights, log, ct));
        tasks.Add(CollectAccessReviews(graph, insights, log, ct));
        tasks.Add(CollectDevices(graph, insights, log, ct));
        tasks.Add(CollectB2b(graph, insights, log, ct));
        tasks.Add(CollectOAuthApps(graph, insights, log, ct));
        tasks.Add(CollectSignIns(graph, insights, log, ct));
        tasks.Add(CollectGsa(httpClient, insights, log, ct));

        await Task.WhenAll(tasks);

        // Generate recommendations using collected insights
        result.Findings.AddRange(EntraRecommendations.Generate(insights));

        // Extract D-score metrics
        result.Metrics["ca_compat_score_pct"] = insights.CaCompatScorePct.ToString();
        result.Metrics["mfa_registration_pct"] = insights.MfaRegistrationPct.ToString();
        // ... more metrics

        return result;
    }

    // ... private Collect* methods each wrap a try/catch, call Graph, populate insights
}

public class EntraInsights
{
    // CA
    public int CaPolicyTotal { get; set; }
    public int CaPolicyEnabled { get; set; }
    public int CaPolicyMfaRequired { get; set; }
    public int CaPolicyLegacyAuthBlocked { get; set; }
    public decimal CaCompatScorePct { get; set; }

    // MFA
    public int TotalUsers { get; set; }
    public int MfaRegistered { get; set; }
    public decimal MfaRegistrationPct { get; set; }
    public int PasswordlessEnabled { get; set; }
    public decimal PasswordlessPct { get; set; }

    // Risky users
    public int RiskyUsersHigh { get; set; }
    public int RiskyUsersMedium { get; set; }
    public int ConfirmedCompromised { get; set; }

    // PIM
    public int PermanentGlobalAdmins { get; set; }
    public int EligibleAssignments { get; set; }

    // Access Reviews
    public int AccessReviewTotal { get; set; }
    public int AccessReviewActive { get; set; }

    // Devices
    public int DevicesCompliant { get; set; }
    public int DevicesNonCompliant { get; set; }
    public bool CaRequiresCompliance { get; set; }

    // B2B
    public int GuestUsersTotal { get; set; }
    public int GuestUsersWithLicenses { get; set; }
    public string GuestInviteSetting { get; set; } = "";

    // OAuth
    public int HighRiskApps { get; set; }
    public int OverPrivilegedApps { get; set; }
    public bool UserConsentAllowed { get; set; }

    // Sign-ins
    public int LegacyAuthSignIns { get; set; }
    public int RiskySignIns { get; set; }
    public int FailedSignIns { get; set; }

    // GSA (nullable — beta, may not be available)
    public int? FilteringPolicies { get; set; }
    public int? ForwardingProfiles { get; set; }
    public int? PrivateAccessConnectors { get; set; }
}
```

- [ ] **Step 2: Write EntraRecommendations**

Port from `CopilotReadinessAssessment/python_assessment/Recommendations/entra/`. Each of the 14 Entra plans gets a check method. Enriched plans use `EntraInsights` to generate sub-observations.

Key structure:
```csharp
namespace KryossApi.Services.CopilotReadiness.Recommendations;

public static class EntraRecommendations
{
    public static List<RecommendationResult> Generate(EntraInsights insights)
    {
        var results = new List<RecommendationResult>();
        results.AddRange(CheckAadPremium(insights));
        results.AddRange(CheckAadPremiumP2(insights));
        results.AddRange(CheckMfaPremium(insights));
        results.AddRange(CheckIntuneA(insights));
        results.AddRange(CheckEntraInternetAccess(insights));
        results.AddRange(CheckEntraPrivateAccess(insights));
        // ... 14 check methods total
        return results;
    }

    private static List<RecommendationResult> CheckAadPremium(EntraInsights i)
    {
        var r = new List<RecommendationResult>();

        // MFA enrollment check
        if (i.MfaRegistrationPct < 50)
            r.Add(RecommendationResult.ActionRequired("entra", "MFA Enrollment",
                "High", $"Only {i.MfaRegistrationPct:F0}% of users have MFA registered.",
                "Increase MFA coverage to at least 90% before deploying Copilot.",
                "MFA deployment guide", "https://learn.microsoft.com/entra/identity/authentication/howto-mfa-getstarted"));
        else if (i.MfaRegistrationPct < 90)
            r.Add(RecommendationResult.Warning("entra", "MFA Enrollment",
                "Medium", $"{i.MfaRegistrationPct:F0}% MFA registered — nearly there.",
                "Push to 90%+ for full Copilot readiness."));
        else
            r.Add(RecommendationResult.Success("entra", "MFA Enrollment",
                $"{i.MfaRegistrationPct:F0}% of users have MFA registered."));

        // CA policy coverage
        // Legacy auth blocking
        // Passwordless adoption
        // Group-based licensing
        // ... follow patterns from Python source
        return r;
    }

    // ... one method per plan, following exact patterns from Python
}
```

The full implementations should be ported 1:1 from the Python recommendation files in `CopilotReadinessAssessment/python_assessment/Recommendations/entra/`. Each enriched check generates 2-10 sub-observations using the thresholds documented in the spec.

- [ ] **Step 3: Build to verify**

Run: `cd KryossApi/src/KryossApi && dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/EntraPipeline.cs
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/EntraRecommendations.cs
git commit -m "feat(copilot-readiness): add Entra pipeline with 14 enriched checks"
```

---

### Task 8: Defender Pipeline

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/DefenderPipeline.cs`
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/DefenderRecommendations.cs`

- [ ] **Step 1: Write DefenderPipeline**

Two API surfaces: Graph Security API (via `GraphServiceClient`) + Defender REST API (via `HttpClient` with bearer token for `https://api.security.microsoft.com/.default`).

Calls spec endpoints 23-26 (Graph) + 41-48 (Defender API). Port from `get_defender_client.py`. Includes 4 KQL advanced hunting queries.

Structure mirrors EntraPipeline: parallel data collection → `DefenderInsights` object → `DefenderRecommendations.Generate()`.

`DefenderInsights` properties: SecureScoreCurrent, SecureScoreMax, SecureScorePct, IncidentsTotal, IncidentsActive, IncidentsHighSeverity, DevicesHighRisk, DevicesMediumRisk, PhishingAlerts, MalwareAlerts, HighRiskOAuthApps, OverPrivilegedOAuthApps, ExposureScore, ExposureRisk, VulnerabilitiesCritical, VulnerabilitiesHigh, CopilotProcessEvents, CopilotNetworkEvents, CopilotFileAccessEvents, AiPhishingEmails.

Handle 403 from Defender API gracefully — set `result.Status = "partial"`, note which sub-APIs failed.

- [ ] **Step 2: Write DefenderRecommendations**

Port from `CopilotReadinessAssessment/python_assessment/Recommendations/defender/`. 17 plan checks including 3 composite assessments (Security Posture, Data Governance, Threat Intelligence).

- [ ] **Step 3: Build and commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/DefenderPipeline.cs
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/DefenderRecommendations.cs
git commit -m "feat(copilot-readiness): add Defender pipeline with 17 checks + KQL hunting"
```

---

### Task 9: M365 Pipeline

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/M365Pipeline.cs`
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/M365Recommendations.cs`

- [ ] **Step 1: Write M365Pipeline**

Calls `/subscribedSkus` for license analysis + 6 usage report endpoints (spec 30-35) + `/users` for Copilot adoption count + `/external/connections` for Graph Connectors.

Usage reports return CSV — parse inline using `StreamReader` + string splitting (same approach as Python).

`M365Insights` properties: TotalUsers, EnabledUsers, CopilotLicensesPurchased, CopilotLicensesAssigned, CopilotAdoptionPct, EmailSendAvg, EmailReceiveAvg, TeamsChatAvg, TeamsMeetingAvg, SharePointActiveSites, SharePointActivityRate, OneDriveAdoptionRate, OfficeDesktopActivations, GraphConnectorsCount.

Copilot SKU IDs to detect: `c28afa23-5a37-4837-938f-7cc48d0cca5c` and `f2b5e97e-f677-4bb5-8127-5c3ce7b6a64e`.

- [ ] **Step 2: Write M365Recommendations**

85+ plan checks. Most are license-only (check provisioning status). ~10 are enriched with M365Insights (Exchange, Teams, SharePoint, OneDrive, Office, Copilot apps). Port from `CopilotReadinessAssessment/python_assessment/Recommendations/m365/`.

- [ ] **Step 3: Build and commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/M365Pipeline.cs
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/M365Recommendations.cs
git commit -m "feat(copilot-readiness): add M365 pipeline with 85+ license checks + usage reports"
```

---

### Task 10: Purview Pipeline

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/PurviewPipeline.cs`
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/PurviewRecommendations.cs`

- [ ] **Step 1: Write PurviewPipeline**

Primarily license analysis from `/subscribedSkus`. Unlike the Python tool which uses PowerShell for Purview data, we can only check what's available via Graph API. For features requiring Exchange Online Management (DLP policies, sensitivity labels, retention, audit config), generate `"Insight"` status findings noting that deeper Purview inspection requires admin portal.

What IS available via Graph: sensitivity labels (`/security/informationProtection/sensitivityLabels`), DLP incidents (`/security/alerts_v2` filtered by category).

- [ ] **Step 2: Write PurviewRecommendations**

36 plan checks. Most are license-only. ~6 are enriched where Graph data is available. Port from `CopilotReadinessAssessment/python_assessment/Recommendations/purview/`.

- [ ] **Step 3: Build and commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/PurviewPipeline.cs
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/PurviewRecommendations.cs
git commit -m "feat(copilot-readiness): add Purview pipeline with 36 compliance checks"
```

---

### Task 11: Power Platform Pipeline

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/PowerPlatformPipeline.cs`
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/PowerPlatformRecommendations.cs`

- [ ] **Step 1: Write PowerPlatformPipeline**

Uses `HttpClient` with bearer token for `https://api.bap.microsoft.com/.default`. Calls spec endpoints 49-56.

`PowerPlatformInsights` properties: EnvironmentsByType (production/sandbox/developer), FlowsCloud, FlowsDesktop, FlowsCopilotPluginCandidates, AppsCanvas, AppsModelDriven, AppsTeamsIntegrated, ConnectionsPremium, ConnectionsStandard, EnterpriseConnectors (SAP/Salesforce/ServiceNow/SQL), AiModelsByType, DlpPoliciesTenantWide, DlpHttpBlocked, DlpCustomConnectorsBlocked, CapacityUsagePct.

Handle 403 gracefully — Power Platform admin API requires Power Platform Administrator role.

- [ ] **Step 2: Write PowerPlatformRecommendations**

17 plan checks + 2 pseudo-features (DLP_GOVERNANCE, AI_BUILDER_MODELS). DLP_GOVERNANCE is critical — checks if HTTP connector is blocked (Copilot extensibility blocker).

- [ ] **Step 3: Build and commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/PowerPlatformPipeline.cs
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/PowerPlatformRecommendations.cs
git commit -m "feat(copilot-readiness): add Power Platform pipeline with 17 checks"
```

---

### Task 12: Copilot Studio Recommendations

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/CopilotStudioRecommendations.cs`

- [ ] **Step 1: Write CopilotStudioRecommendations**

11 plan checks. All license-only — no dedicated API. Uses PowerPlatformInsights for context if available.

```csharp
namespace KryossApi.Services.CopilotReadiness.Recommendations;

public static class CopilotStudioRecommendations
{
    public static List<RecommendationResult> Generate(
        Dictionary<string, string> planStatuses,
        PowerPlatformInsights? ppInsights)
    {
        var results = new List<RecommendationResult>();
        foreach (var (plan, status) in planStatuses)
        {
            var friendly = FriendlyName(plan);
            if (status == "Success")
                results.Add(RecommendationResult.Success("copilot_studio", friendly,
                    $"{friendly} is active.", "Copilot Studio docs",
                    "https://learn.microsoft.com/microsoft-copilot-studio/"));
            else
                results.Add(RecommendationResult.Disabled("copilot_studio", friendly,
                    "Low", $"{friendly} is {status}.",
                    $"Enable {friendly} to extend Copilot with custom agents.",
                    "Copilot Studio docs",
                    "https://learn.microsoft.com/microsoft-copilot-studio/"));
        }
        return results;
    }

    private static string FriendlyName(string plan) => plan switch
    {
        "POWER_VIRTUAL_AGENTS" => "Power Virtual Agents",
        "COPILOT_STUDIO_IN_COPILOT_FOR_M365" => "Copilot Studio in Microsoft 365 Copilot",
        "CDS_VIRTUAL_AGENT_USL" => "Copilot Studio User License",
        "CDS_VIRTUAL_AGENT_BASE_MESSAGES" => "Copilot Studio Base Messages",
        _ => plan.Replace('_', ' ')
    };
}
```

- [ ] **Step 2: Build and commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Recommendations/CopilotStudioRecommendations.cs
git commit -m "feat(copilot-readiness): add Copilot Studio 11 license checks"
```

---

### Task 13: SharePoint Deep Pipeline

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/SharePointDeepPipeline.cs`

- [ ] **Step 1: Write SharePointDeepPipeline**

This pipeline feeds D1 (label coverage), D2 (oversharing), and D3 (external users). Uses Graph API to enumerate SharePoint sites, check file labels, check sharing links, enumerate guest users.

Key logic:
1. `GET /sites?$top=50` — get up to 50 sites (cap for performance)
2. For each site, `GET /sites/{id}/drives` → for default drive, `GET /drives/{id}/root/children?$top=500` → check `sensitivityLabel` property on each item
3. For each site, `GET /sites/{id}/permissions` → check for organization-wide sharing links
4. `GET /users?$filter=userType eq 'Guest'&$select=id,displayName,userPrincipalName,createdDateTime,signInActivity` → classify risk

Compute:
- `label_coverage_pct` = total labeled files / total files × 100
- `overshared_pct` = total overshared files / total files × 100
- Per-site breakdowns → `SharepointSiteResult` list
- External user classification → `ExternalUserResult` list (high-risk = active access + edit permission + recent sign-in)

Pagination: max 200 items per page via `@odata.nextLink`. Cap at 500 files per site. Total cap: 50 sites. Mark metrics with `"sampled": "true"` if caps were hit.

- [ ] **Step 2: Build and commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/Pipelines/SharePointDeepPipeline.cs
git commit -m "feat(copilot-readiness): add SharePoint deep pipeline for D1+D2+D3"
```

---

## Phase 4: Service Orchestrator + API Endpoints

### Task 14: CopilotReadinessService

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/ICopilotReadinessService.cs`
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/CopilotReadinessService.cs`

- [ ] **Step 1: Write interface**

```csharp
namespace KryossApi.Services.CopilotReadiness;

public interface ICopilotReadinessService
{
    Task<Guid> StartScanAsync(Guid organizationId, Guid tenantId, string customerTenantId);
    Task<CopilotReadinessScanDto?> GetLatestScanAsync(Guid organizationId);
    Task<CopilotReadinessScanDetailDto?> GetScanDetailAsync(Guid scanId);
    Task<List<CopilotReadinessScanSummaryDto>> GetScanHistoryAsync(Guid organizationId);
}
```

- [ ] **Step 2: Write service implementation**

Orchestrator creates scan row, acquires 3 tokens (Graph, Defender, Power Platform), runs 6 pipelines via `Task.WhenAll`, computes scores, persists everything in one transaction.

```csharp
namespace KryossApi.Services.CopilotReadiness;

using Azure.Identity;
using Microsoft.Graph;
using KryossApi.Services.CopilotReadiness.Pipelines;

public class CopilotReadinessService : ICopilotReadinessService
{
    private readonly KryossDbContext _db;
    private readonly M365Config _config;
    private readonly ILogger<CopilotReadinessService> _log;

    public CopilotReadinessService(KryossDbContext db, M365Config config,
        ILogger<CopilotReadinessService> log)
    {
        _db = db;
        _config = config;
        _log = log;
    }

    public async Task<Guid> StartScanAsync(Guid organizationId, Guid tenantId,
        string customerTenantId)
    {
        // 1. Determine credentials (shared app or per-customer)
        var tenant = await _db.M365Tenants.FindAsync(tenantId);
        var clientId = tenant!.ClientId ?? _config.ClientId;
        var clientSecret = tenant.ClientId != null
            ? DecryptSecret(tenant.ClientSecret!, organizationId)
            : _config.ClientSecret;

        // 2. Create scan row
        var scan = new CopilotReadinessScan
        {
            OrganizationId = organizationId,
            TenantId = tenantId,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _db.CopilotReadinessScans.Add(scan);
        await _db.SaveChangesAsync();

        // 3. Run scan on background thread
        _ = Task.Run(() => RunScanAsync(scan.Id, customerTenantId, clientId, clientSecret));

        return scan.Id;
    }

    private async Task RunScanAsync(Guid scanId, string customerTenantId,
        string clientId, string clientSecret)
    {
        using var scope = /* create new DI scope for background work */;
        var db = scope.ServiceProvider.GetRequiredService<KryossDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<CopilotReadinessService>>();

        try
        {
            var credential = new ClientSecretCredential(customerTenantId, clientId, clientSecret);
            var graph = new GraphServiceClient(credential);

            // Separate HTTP clients for Defender + Power Platform
            var defenderHttp = await CreateAuthenticatedClient(credential,
                "https://api.security.microsoft.com/.default",
                "https://api.security.microsoft.com");
            var ppHttp = await CreateAuthenticatedClient(credential,
                "https://api.bap.microsoft.com/.default",
                "https://api.bap.microsoft.com");
            var graphBetaHttp = await CreateAuthenticatedClient(credential,
                "https://graph.microsoft.com/.default",
                "https://graph.microsoft.com/beta");

            // 4. Run 6 pipelines in parallel
            var entraTask = EntraPipeline.RunAsync(graph, graphBetaHttp, log, default);
            var defenderTask = DefenderPipeline.RunAsync(graph, defenderHttp, log, default);
            var m365Task = M365Pipeline.RunAsync(graph, log, default);
            var purviewTask = PurviewPipeline.RunAsync(graph, log, default);
            var ppTask = PowerPlatformPipeline.RunAsync(ppHttp, log, default);
            var spTask = SharePointDeepPipeline.RunAsync(graph, log, default);

            await Task.WhenAll(entraTask, defenderTask, m365Task, purviewTask, ppTask, spTask);

            var pipelines = new[] { entraTask.Result, defenderTask.Result, m365Task.Result,
                purviewTask.Result, ppTask.Result, spTask.Result };

            // 5. Compute scores
            var allFindings = pipelines.SelectMany(p => p.Findings).ToList();
            var allMetrics = pipelines.SelectMany(p => p.Metrics).ToList();

            var spResult = spTask.Result;
            var labelPct = GetMetric(spResult, "label_coverage_pct", 0m);
            var oversharedPct = GetMetric(spResult, "overshared_pct", 0m);
            var highRiskExt = spResult.ExternalUsers.Count(u => u.RiskLevel == "High");
            var pendingInvites = GetMetric(spResult, "pending_invitations", 0);

            var caCompat = GetMetric(entraTask.Result, "ca_compat_score_pct", 0m);

            var entraGaps = allFindings.Count(f =>
                f.Service == "entra" &&
                (f.Status == "Action Required" || f.Status == "Warning") &&
                (f.Priority == "High" || f.Priority == "Medium"));
            var defCrit = allFindings.Count(f =>
                f.Service == "defender" && f.Status == "Critical");
            var defGaps = allFindings.Count(f =>
                f.Service == "defender" &&
                (f.Status == "Action Required" || f.Status == "Warning") &&
                (f.Priority == "High" || f.Priority == "Medium"));
            var purviewGaps = allFindings.Count(f =>
                f.Service == "purview" &&
                (f.Status == "Disabled" || f.Status == "Action Required" || f.Status == "Warning") &&
                f.Priority == "High");

            var scores = ScoringEngine.Compute(
                labelPct, oversharedPct, highRiskExt, pendingInvites,
                caCompat, entraGaps, defCrit, defGaps, purviewGaps);

            // 6. Persist results
            var scan = await db.CopilotReadinessScans.FindAsync(scanId);
            scan!.D1Score = scores.D1;
            scan.D2Score = scores.D2;
            scan.D3Score = scores.D3;
            scan.D4Score = scores.D4;
            scan.D5Score = scores.D5;
            scan.D6Score = scores.D6;
            scan.OverallScore = scores.Overall;
            scan.Verdict = scores.Verdict;
            scan.Status = pipelines.All(p => p.Status == "ok") ? "completed" : "partial";
            scan.PipelineStatus = JsonSerializer.Serialize(
                pipelines.ToDictionary(p => p.PipelineName, p => p.Status));
            scan.CompletedAt = DateTime.UtcNow;

            // Persist findings
            foreach (var f in allFindings)
            {
                db.CopilotReadinessFindings.Add(new CopilotReadinessFinding
                {
                    ScanId = scanId, Service = f.Service, Feature = f.Feature,
                    Status = f.Status, Priority = f.Priority,
                    Observation = f.Observation, Recommendation = f.Recommendation,
                    LinkText = f.LinkText, LinkUrl = f.LinkUrl,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Persist metrics
            foreach (var p in pipelines)
                foreach (var (key, val) in p.Metrics)
                    db.CopilotReadinessMetrics.Add(new CopilotReadinessMetric
                    {
                        ScanId = scanId, Dimension = InferDimension(key),
                        MetricKey = key, MetricValue = val, CreatedAt = DateTime.UtcNow
                    });

            // Persist SharePoint details
            foreach (var site in spResult.SharepointSites)
                db.CopilotReadinessSharepoint.Add(new CopilotReadinessSharepoint
                {
                    ScanId = scanId, SiteUrl = site.SiteUrl, SiteTitle = site.SiteTitle,
                    TotalFiles = site.TotalFiles, LabeledFiles = site.LabeledFiles,
                    OversharedFiles = site.OversharedFiles, RiskLevel = site.RiskLevel,
                    TopLabels = JsonSerializer.Serialize(site.TopLabels),
                    CreatedAt = DateTime.UtcNow
                });

            // Persist external users
            foreach (var u in spResult.ExternalUsers)
                db.CopilotReadinessExternalUsers.Add(new CopilotReadinessExternalUser
                {
                    ScanId = scanId, UserPrincipal = u.UserPrincipal,
                    DisplayName = u.DisplayName, EmailDomain = u.EmailDomain,
                    LastSignIn = u.LastSignIn, RiskLevel = u.RiskLevel,
                    SitesAccessed = u.SitesAccessed, HighestPermission = u.HighestPermission,
                    CreatedAt = DateTime.UtcNow
                });

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Copilot readiness scan {ScanId} failed", scanId);
            var scan = await db.CopilotReadinessScans.FindAsync(scanId);
            if (scan != null)
            {
                scan.Status = "failed";
                scan.PipelineStatus = JsonSerializer.Serialize(new { error = ex.Message });
                scan.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
    }

    // Read methods: GetLatestScanAsync, GetScanDetailAsync, GetScanHistoryAsync
    // ... standard EF queries returning DTOs
}
```

Note: The background `Task.Run` needs a DI scope. Follow the existing pattern used in the codebase — check how `M365Function.Scan` handles this (it may run synchronously). If no async background pattern exists, run synchronously and let the client poll.

- [ ] **Step 3: Register DI in Program.cs**

Add after existing M365 registration:

```csharp
builder.Services.AddScoped<ICopilotReadinessService, CopilotReadinessService>();
```

- [ ] **Step 4: Build and commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/ICopilotReadinessService.cs
git add KryossApi/src/KryossApi/Services/CopilotReadiness/CopilotReadinessService.cs
git add KryossApi/src/KryossApi/Program.cs
git commit -m "feat(copilot-readiness): add orchestrator service with 6 parallel pipelines"
```

---

### Task 15: API Endpoints

**Files:**
- Create: `KryossApi/src/KryossApi/Functions/Portal/CopilotReadinessFunction.cs`

- [ ] **Step 1: Write function class with 4 endpoints**

```csharp
namespace KryossApi.Functions.Portal;

public class CopilotReadinessFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly ICopilotReadinessService _service;

    public CopilotReadinessFunction(KryossDbContext db, ICurrentUserService user,
        ICopilotReadinessService service)
    {
        _db = db;
        _user = user;
        _service = service;
    }

    [Function("CopilotReadiness_Scan")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Scan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "v2/copilot-readiness/scan")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<CopilotReadinessScanRequest>();
        if (body?.OrganizationId == null || body.OrganizationId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        // Org access check (same pattern as M365Function)
        if (!_user.IsAdmin)
        {
            var orgBelongs = (_user.FranchiseId.HasValue &&
                await _db.Organizations.AnyAsync(o => o.Id == body.OrganizationId && o.FranchiseId == _user.FranchiseId.Value))
                || (_user.OrganizationId.HasValue && body.OrganizationId == _user.OrganizationId.Value);
            if (!orgBelongs)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        // Check M365 tenant connected
        var tenant = await _db.M365Tenants
            .FirstOrDefaultAsync(t => t.OrganizationId == body.OrganizationId && t.Status == "active");
        if (tenant == null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "M365 tenant not connected" });
            return bad;
        }

        var scanId = await _service.StartScanAsync(body.OrganizationId, tenant.Id, tenant.TenantId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { scanId, status = "running" });
        return response;
    }

    [Function("CopilotReadiness_Get")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "v2/copilot-readiness")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];
        if (!Guid.TryParse(orgIdStr, out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        // Org access check (omitted for brevity — same pattern as Scan)

        var result = await _service.GetLatestScanAsync(orgId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result ?? new { scanned = false });
        return response;
    }

    [Function("CopilotReadiness_Detail")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Detail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "v2/copilot-readiness/{scanId}")] HttpRequestData req,
        string scanId)
    {
        if (!Guid.TryParse(scanId, out var id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "invalid scanId" });
            return bad;
        }

        var result = await _service.GetScanDetailAsync(id);
        if (result == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "scan not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    [Function("CopilotReadiness_History")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> History(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "v2/copilot-readiness/history")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];
        if (!Guid.TryParse(orgIdStr, out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var result = await _service.GetScanHistoryAsync(orgId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }
}

public class CopilotReadinessScanRequest
{
    public Guid OrganizationId { get; set; }
}
```

- [ ] **Step 2: Build and commit**

```bash
git add KryossApi/src/KryossApi/Functions/Portal/CopilotReadinessFunction.cs
git commit -m "feat(copilot-readiness): add 4 API endpoints"
```

---

### Task 16: Timer Trigger (Weekly Scan)

**Files:**
- Create: `KryossApi/src/KryossApi/Functions/Timer/CopilotReadinessTimerFunction.cs`

- [ ] **Step 1: Write timer function**

```csharp
namespace KryossApi.Functions.Timer;

public class CopilotReadinessTimerFunction
{
    private readonly KryossDbContext _db;
    private readonly ICopilotReadinessService _service;
    private readonly ILogger<CopilotReadinessTimerFunction> _log;

    public CopilotReadinessTimerFunction(KryossDbContext db,
        ICopilotReadinessService service,
        ILogger<CopilotReadinessTimerFunction> log)
    {
        _db = db;
        _service = service;
        _log = log;
    }

    [Function("CopilotReadiness_WeeklyScan")]
    public async Task Run(
        [TimerTrigger("0 0 2 * * 0")] TimerInfo timer) // Sunday 02:00 UTC
    {
        var tenants = await _db.M365Tenants
            .Where(t => t.Status == "active")
            .Select(t => new { t.Id, t.OrganizationId, t.TenantId })
            .ToListAsync();

        _log.LogInformation("Weekly Copilot readiness scan: {Count} tenants", tenants.Count);

        foreach (var t in tenants)
        {
            // Skip if last scan < 5 days ago
            var recent = await _db.CopilotReadinessScans
                .AnyAsync(s => s.TenantId == t.Id && s.CreatedAt > DateTime.UtcNow.AddDays(-5));
            if (recent)
            {
                _log.LogInformation("Skipping {OrgId} — recent scan exists", t.OrganizationId);
                continue;
            }

            try
            {
                await _service.StartScanAsync(t.OrganizationId, t.Id, t.TenantId);
                _log.LogInformation("Started scan for org {OrgId}", t.OrganizationId);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to start scan for org {OrgId}", t.OrganizationId);
            }

            await Task.Delay(TimeSpan.FromSeconds(30)); // Rate limit
        }
    }
}
```

- [ ] **Step 2: Build and commit**

```bash
git add KryossApi/src/KryossApi/Functions/Timer/CopilotReadinessTimerFunction.cs
git commit -m "feat(copilot-readiness): add weekly timer trigger"
```

---

## Phase 5: Portal — Copilot Readiness Tab

### Task 17: API Hooks

**Files:**
- Create: `KryossPortal/src/api/copilotReadiness.ts`

- [ ] **Step 1: Write React Query hooks**

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from '@/api/client';

// Types
export interface CopilotReadinessScan {
  id: string;
  status: 'running' | 'completed' | 'partial' | 'failed';
  d1Score: number | null;
  d2Score: number | null;
  d3Score: number | null;
  d4Score: number | null;
  d5Score: number | null;
  d6Score: number | null;
  overallScore: number | null;
  verdict: string | null;
  pipelineStatus: Record<string, string>;
  startedAt: string;
  completedAt: string | null;
  findingsSummary: Record<string, { total: number; actionRequired: number; warning: number; success: number }>;
}

export interface CopilotReadinessFinding {
  id: number;
  service: string;
  feature: string;
  status: string;
  priority: string;
  observation: string | null;
  recommendation: string | null;
  linkText: string | null;
  linkUrl: string | null;
}

export interface CopilotReadinessScanDetail extends CopilotReadinessScan {
  findings: CopilotReadinessFinding[];
  metrics: Record<string, string>;
  sharepointSites: Array<{
    siteUrl: string;
    siteTitle: string;
    totalFiles: number;
    labeledFiles: number;
    oversharedFiles: number;
    riskLevel: string;
  }>;
  externalUsers: Array<{
    userPrincipal: string;
    displayName: string;
    emailDomain: string;
    lastSignIn: string | null;
    riskLevel: string;
    sitesAccessed: number;
  }>;
}

export interface CopilotReadinessScanSummary {
  id: string;
  overallScore: number;
  verdict: string;
  createdAt: string;
}

// Hooks
export function useCopilotReadiness(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['copilot-readiness', organizationId],
    queryFn: () =>
      apiFetch<CopilotReadinessScan | { scanned: false }>(
        `/v2/copilot-readiness?organizationId=${organizationId}`
      ),
    enabled: !!organizationId,
    refetchInterval: (data) =>
      data && 'status' in data && data.status === 'running' ? 10000 : false,
  });
}

export function useCopilotReadinessDetail(scanId: string | undefined) {
  return useQuery({
    queryKey: ['copilot-readiness-detail', scanId],
    queryFn: () =>
      apiFetch<CopilotReadinessScanDetail>(`/v2/copilot-readiness/${scanId}`),
    enabled: !!scanId,
  });
}

export function useCopilotReadinessHistory(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['copilot-readiness-history', organizationId],
    queryFn: () =>
      apiFetch<CopilotReadinessScanSummary[]>(
        `/v2/copilot-readiness/history?organizationId=${organizationId}`
      ),
    enabled: !!organizationId,
  });
}

export function useCopilotReadinessScan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (organizationId: string) =>
      apiFetch<{ scanId: string; status: string }>('/v2/copilot-readiness/scan', {
        method: 'POST',
        body: JSON.stringify({ organizationId }),
      }),
    onSuccess: (_data, organizationId) => {
      qc.invalidateQueries({ queryKey: ['copilot-readiness', organizationId] });
    },
  });
}
```

- [ ] **Step 2: Commit**

```bash
git add KryossPortal/src/api/copilotReadiness.ts
git commit -m "feat(copilot-readiness): add React Query hooks for portal"
```

---

### Task 18: CopilotReadinessTab Component

**Files:**
- Create: `KryossPortal/src/components/org-detail/CopilotReadinessTab.tsx`

- [ ] **Step 1: Write the tab component**

Full component with: score gauge cards, dimension breakdown, findings accordion, trend chart placeholder, pipeline status row. Uses shadcn/ui components (Card, Badge, Button, Accordion, Table, Skeleton). Follows M365Tab.tsx patterns.

Key sections:
1. Header with "Run Assessment" button + last scan date
2. Overall score gauge (large colored badge + verdict)
3. 6 dimension cards in a grid (color-coded 1-2 red, 3 amber, 4-5 green)
4. Copilot license status banner (if no Copilot license detected)
5. Findings accordion grouped by service
6. Pipeline status row
7. "Export Report" button

States: not-scanned → running (polling) → completed/partial/failed

This is a large component (~300-400 lines). Write it following the patterns in M365Tab.tsx — inline sub-components, lucide-react icons, sonner toast, useOrgParam hook.

- [ ] **Step 2: Commit**

```bash
git add KryossPortal/src/components/org-detail/CopilotReadinessTab.tsx
git commit -m "feat(copilot-readiness): add portal Copilot Readiness dashboard tab"
```

---

### Task 19: M365Tab Sub-Navigation

**Files:**
- Modify: `KryossPortal/src/components/org-detail/M365Tab.tsx`

- [ ] **Step 1: Add tab navigation**

Add a `Tabs` component (from shadcn/ui) wrapping the existing content as "Security Checks" tab and the new `CopilotReadinessTab` as second tab.

```tsx
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { CopilotReadinessTab } from './CopilotReadinessTab';

// In the connected state render:
<Tabs defaultValue="security-checks">
  <TabsList>
    <TabsTrigger value="security-checks">Security Checks</TabsTrigger>
    <TabsTrigger value="copilot-readiness">Copilot Readiness</TabsTrigger>
  </TabsList>
  <TabsContent value="security-checks">
    {/* existing findings table + KPI cards */}
  </TabsContent>
  <TabsContent value="copilot-readiness">
    <CopilotReadinessTab />
  </TabsContent>
</Tabs>
```

- [ ] **Step 2: Commit**

```bash
git add KryossPortal/src/components/org-detail/M365Tab.tsx
git commit -m "feat(copilot-readiness): add sub-tab navigation in M365 tab"
```

---

### Task 20: Report Dropdown Entry

**Files:**
- Modify: `KryossPortal/src/components/reports/ReportGenerator.tsx`

- [ ] **Step 1: Add m365 report type**

Add to `REPORT_TYPES` array:

```typescript
{ value: 'm365', label: 'M365 Security & Copilot Readiness' },
```

- [ ] **Step 2: Commit**

```bash
git add KryossPortal/src/components/reports/ReportGenerator.tsx
git commit -m "feat(copilot-readiness): add M365 report type to dropdown"
```

---

## Phase 6: Unified M365 Report

### Task 21: Report Builder

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadinessReportBuilder.cs`
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Write CopilotReadinessReportBuilder**

Static class with `BuildUnifiedM365Report(...)` method. Uses `StringBuilder` same as other report builders. 11 sections as specified in the design doc.

Parameters: org, runs, allResults (50 checks), branding, frameworkScores, hygieneScan, orgEnrichment, userInfo, copilotScan (CopilotReadinessScan with all navigation properties loaded), m365Findings (existing 50 checks), lang.

Uses shared helpers from ReportService: `AppendHtmlHead`, `AppendPageHeader`, `AppendFooter`, `AppendRunningFooterCss`.

Bilingual: All text via `lang == "es" ? spanishText : englishText` inline ternaries (same pattern as existing builders).

This is a large file (~800-1000 lines) — 11 report sections each building HTML via StringBuilder.

- [ ] **Step 2: Add "m365" case to ReportService switch**

In `ReportService.cs`, add data loading before the switch (guarded by `if (reportType == "m365")`):

```csharp
// Before the switch, after existing c-level loading:
CopilotReadinessScan? copilotScan = null;
if (reportType == "m365")
{
    copilotScan = await _db.CopilotReadinessScans
        .Include(s => s.Findings)
        .Include(s => s.Metrics)
        .Include(s => s.SharepointSites)
        .Include(s => s.ExternalUsers)
        .Where(s => s.OrganizationId == orgId)
        .OrderByDescending(s => s.CreatedAt)
        .FirstOrDefaultAsync();
}
```

Add case to switch:
```csharp
"m365" => CopilotReadinessReportBuilder.BuildUnifiedM365Report(
    org, runs, allResults, branding, frameworkScores, hygieneScan,
    orgEnrichment, userInfo, copilotScan, m365Findings, m365Connected, lang),
```

- [ ] **Step 3: Build and commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadinessReportBuilder.cs
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(copilot-readiness): add unified M365 report builder (11 sections)"
```

---

## Phase 7: Bilingual Strings

### Task 22: String Tables

**Files:**
- Create: `KryossApi/src/KryossApi/Services/CopilotReadiness/CopilotReadinessStrings.cs`

- [ ] **Step 1: Write bilingual string tables**

```csharp
namespace KryossApi.Services.CopilotReadiness;

public static class CopilotReadinessStrings
{
    public static string Get(string key, string lang) =>
        lang == "es" ? Es[key] : En[key];

    private static readonly Dictionary<string, string> En = new()
    {
        ["report_title"] = "M365 Security & Copilot Readiness Assessment",
        ["executive_summary"] = "Executive Summary",
        ["d1_name"] = "Information Protection",
        ["d2_name"] = "Data Sharing & Oversharing",
        ["d3_name"] = "External User Access",
        ["d4_name"] = "Conditional Access",
        ["d5_name"] = "Zero Trust (Entra + Defender)",
        ["d6_name"] = "Compliance & Governance",
        ["verdict_ready"] = "Ready",
        ["verdict_nearly"] = "Nearly Ready",
        ["verdict_not"] = "Not Ready",
        ["no_copilot_license"] = "No Microsoft 365 Copilot licenses detected",
        ["no_copilot_desc"] = "This assessment shows what your tenant needs BEFORE deploying Copilot. Address these findings to ensure a successful Copilot rollout.",
        ["section_security_posture"] = "M365 Security Posture",
        ["section_labels"] = "Sensitivity Labels",
        ["section_oversharing"] = "Data Sharing & Oversharing",
        ["section_external"] = "External User Access",
        ["section_ca"] = "Conditional Access",
        ["section_zerotrust"] = "Zero Trust & Compliance",
        ["section_licenses"] = "License Inventory",
        ["section_roadmap"] = "Remediation Roadmap",
        ["section_methodology"] = "Methodology",
        ["phase_critical"] = "Phase 1: Critical",
        ["phase_recommended"] = "Phase 2: Recommended",
        ["phase_ongoing"] = "Phase 3: Ongoing",
        ["run_assessment"] = "Run Assessment",
        ["last_scan"] = "Last scan",
        ["actions_before_copilot"] = "actions to complete before Copilot",
        // ... complete list
    };

    private static readonly Dictionary<string, string> Es = new()
    {
        ["report_title"] = "Evaluacion de Seguridad M365 y Preparacion para Copilot",
        ["executive_summary"] = "Resumen Ejecutivo",
        ["d1_name"] = "Proteccion de Informacion",
        ["d2_name"] = "Comparticion de Datos y Sobreexposicion",
        ["d3_name"] = "Acceso de Usuarios Externos",
        ["d4_name"] = "Acceso Condicional",
        ["d5_name"] = "Zero Trust (Entra + Defender)",
        ["d6_name"] = "Cumplimiento y Gobernanza",
        ["verdict_ready"] = "Listo",
        ["verdict_nearly"] = "Casi Listo",
        ["verdict_not"] = "No Listo",
        ["no_copilot_license"] = "No se detectaron licencias de Microsoft 365 Copilot",
        ["no_copilot_desc"] = "Esta evaluacion muestra lo que su tenant necesita ANTES de implementar Copilot. Resuelva estos hallazgos para garantizar una implementacion exitosa.",
        ["section_security_posture"] = "Postura de Seguridad M365",
        ["section_labels"] = "Etiquetas de Sensibilidad",
        ["section_oversharing"] = "Comparticion de Datos y Sobreexposicion",
        ["section_external"] = "Acceso de Usuarios Externos",
        ["section_ca"] = "Acceso Condicional",
        ["section_zerotrust"] = "Zero Trust y Cumplimiento",
        ["section_licenses"] = "Inventario de Licencias",
        ["section_roadmap"] = "Hoja de Ruta de Remediacion",
        ["section_methodology"] = "Metodologia",
        ["phase_critical"] = "Fase 1: Critico",
        ["phase_recommended"] = "Fase 2: Recomendado",
        ["phase_ongoing"] = "Fase 3: Continuo",
        ["run_assessment"] = "Ejecutar Evaluacion",
        ["last_scan"] = "Ultimo escaneo",
        ["actions_before_copilot"] = "acciones a completar antes de Copilot",
        // ... complete list
    };
}
```

- [ ] **Step 2: Commit**

```bash
git add KryossApi/src/KryossApi/Services/CopilotReadiness/CopilotReadinessStrings.cs
git commit -m "feat(copilot-readiness): add bilingual EN/ES string tables"
```

---

## Phase 8: Integration + Final Build

### Task 23: Final Build + Deploy Verification

- [ ] **Step 1: Full backend build**

Run: `cd KryossApi/src/KryossApi && dotnet build`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 2: Full portal build**

Run: `cd KryossPortal && npm run build`
Expected: Build succeeded.

- [ ] **Step 3: Run SQL migration against production**

Run migration 029 against KryossDb (same as Task 1 Step 2).

- [ ] **Step 4: Deploy backend**

Run: `cd KryossApi/src/KryossApi && func azure functionapp publish func-kryoss`

- [ ] **Step 5: Deploy portal**

Portal auto-deploys via Azure SWA GitHub Actions on push to main.

- [ ] **Step 6: Verify consent flow**

Navigate to portal → org → M365 tab → if not connected, click "Connect M365 Tenant". The consent URL should now request expanded permissions. Customer admin approves.

- [ ] **Step 7: Run first scan**

In portal, go to M365 tab → Copilot Readiness sub-tab → click "Run Assessment". Poll until completed. Verify:
- D1-D6 scores appear
- Overall score + verdict shown
- Findings accordion populated
- Pipeline status all green (or partial with explanation)

- [ ] **Step 8: Generate report**

In report dropdown, select "M365 Security & Copilot Readiness" → Open in new tab. Verify 11 sections render with correct branding.

- [ ] **Step 9: Final commit**

```bash
git add -A
git commit -m "feat(copilot-readiness): complete Copilot Readiness Assessment implementation"
```

---

## Summary

| Phase | Tasks | What it delivers |
|-------|-------|-----------------|
| 1. DB Foundation | 1-3 | 5 tables + EF entities + DbContext |
| 2. Scoring + Mapping | 4-6 | 169-plan dictionary + D1-D6 formula + DTOs |
| 3. Pipelines | 7-13 | 6 data collectors (Entra, Defender, M365, Purview, PP, SharePoint) + recommendation engines |
| 4. Orchestrator + API | 14-16 | Service + 4 endpoints + weekly timer |
| 5. Portal | 17-20 | Dashboard tab + API hooks + sub-navigation + report dropdown |
| 6. Report | 21 | 11-section unified M365 HTML report |
| 7. Bilingual | 22 | EN/ES string tables |
| 8. Integration | 23 | Build + deploy + verify |
