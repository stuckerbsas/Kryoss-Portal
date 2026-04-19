using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Action = KryossApi.Data.Entities.Action;

namespace KryossApi.Data;

public class KryossDbContext : DbContext
{
    public KryossDbContext(DbContextOptions<KryossDbContext> options) : base(options) { }

    // Auth & RBAC
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Action> Actions => Set<Action>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Actlog> Actlog => Set<Actlog>();
    public DbSet<ExecutiveCta> ExecutiveCtas => Set<ExecutiveCta>();

    // Core
    public DbSet<Franchise> Franchises => Set<Franchise>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Brand> Brands => Set<Brand>();

    // CMDB
    public DbSet<Machine> Machines => Set<Machine>();

    // Assessment
    public DbSet<ControlCategory> ControlCategories => Set<ControlCategory>();
    public DbSet<ControlDef> ControlDefs => Set<ControlDef>();
    public DbSet<Framework> Frameworks => Set<Framework>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<ControlFramework> ControlFrameworks => Set<ControlFramework>();
    public DbSet<ControlPlatform> ControlPlatforms => Set<ControlPlatform>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<AssessmentControl> AssessmentControls => Set<AssessmentControl>();
    public DbSet<AssessmentRun> AssessmentRuns => Set<AssessmentRun>();
    public DbSet<ControlResult> ControlResults => Set<ControlResult>();
    public DbSet<RunFrameworkScore> RunFrameworkScores => Set<RunFrameworkScore>();

    // Enrollment
    public DbSet<EnrollmentCode> EnrollmentCodes => Set<EnrollmentCode>();
    public DbSet<OrgCryptoKey> OrgCryptoKeys => Set<OrgCryptoKey>();

    // CMDB - Disk inventory
    public DbSet<MachineDisk> MachineDisks => Set<MachineDisk>();

    // CMDB - Port scan results
    public DbSet<MachinePort> MachinePorts => Set<MachinePort>();

    // AD Hygiene
    public DbSet<AdHygieneScan> AdHygieneScans => Set<AdHygieneScan>();
    public DbSet<AdHygieneFinding> AdHygieneFindings => Set<AdHygieneFinding>();

    // CMDB - Threat detection
    public DbSet<MachineThreat> MachineThreats => Set<MachineThreat>();

    // External scans (cloud-side pentest)
    public DbSet<ExternalScan> ExternalScans => Set<ExternalScan>();
    public DbSet<ExternalScanResult> ExternalScanResults => Set<ExternalScanResult>();

    // M365 / Entra ID
    public DbSet<M365Tenant> M365Tenants => Set<M365Tenant>();
    public DbSet<M365Finding> M365Findings => Set<M365Finding>();

    // Copilot Readiness (Phase 5)
    public DbSet<CopilotReadinessScan> CopilotReadinessScans => Set<CopilotReadinessScan>();
    public DbSet<CopilotReadinessMetric> CopilotReadinessMetrics => Set<CopilotReadinessMetric>();
    public DbSet<CopilotReadinessFinding> CopilotReadinessFindings => Set<CopilotReadinessFinding>();
    public DbSet<CopilotReadinessSharepoint> CopilotReadinessSharepoint => Set<CopilotReadinessSharepoint>();
    public DbSet<CopilotReadinessExternalUser> CopilotReadinessExternalUsers => Set<CopilotReadinessExternalUser>();

    // Cloud Assessment (CA-0)
    public DbSet<CloudAssessmentScan> CloudAssessmentScans => Set<CloudAssessmentScan>();
    public DbSet<CloudAssessmentFinding> CloudAssessmentFindings => Set<CloudAssessmentFinding>();
    public DbSet<CloudAssessmentMetric> CloudAssessmentMetrics => Set<CloudAssessmentMetric>();
    public DbSet<CloudAssessmentAzureSubscription> CloudAssessmentAzureSubscriptions => Set<CloudAssessmentAzureSubscription>();
    public DbSet<CloudAssessmentAzureResource> CloudAssessmentAzureResources => Set<CloudAssessmentAzureResource>();
    public DbSet<CloudAssessmentLicense> CloudAssessmentLicenses => Set<CloudAssessmentLicense>();
    public DbSet<CloudAssessmentAdoption> CloudAssessmentAdoptions => Set<CloudAssessmentAdoption>();
    public DbSet<CloudAssessmentWastedLicense> CloudAssessmentWastedLicenses => Set<CloudAssessmentWastedLicense>();
    public DbSet<CloudAssessmentFindingStatus> CloudAssessmentFindingStatuses => Set<CloudAssessmentFindingStatus>();
    public DbSet<CloudAssessmentSuggestion> CloudAssessmentSuggestions => Set<CloudAssessmentSuggestion>();

    // CA-9: Power BI Governance
    public DbSet<CloudAssessmentPowerBiConnection> CloudAssessmentPowerBiConnections => Set<CloudAssessmentPowerBiConnection>();
    public DbSet<CloudAssessmentPowerBiWorkspace> CloudAssessmentPowerBiWorkspaces => Set<CloudAssessmentPowerBiWorkspace>();
    public DbSet<CloudAssessmentPowerBiGateway> CloudAssessmentPowerBiGateways => Set<CloudAssessmentPowerBiGateway>();
    public DbSet<CloudAssessmentPowerBiCapacity> CloudAssessmentPowerBiCapacities => Set<CloudAssessmentPowerBiCapacity>();
    public DbSet<CloudAssessmentPowerBiActivitySummary> CloudAssessmentPowerBiActivitySummaries => Set<CloudAssessmentPowerBiActivitySummary>();

    // CA-11: Unified scan — SharePoint sites + external users (Copilot Readiness data)
    public DbSet<CloudAssessmentSharepointSite> CloudAssessmentSharepointSites => Set<CloudAssessmentSharepointSite>();
    public DbSet<CloudAssessmentExternalUser> CloudAssessmentExternalUsers => Set<CloudAssessmentExternalUser>();

    // CA-10: Mail Flow & Email Security
    public DbSet<CloudAssessmentMailDomain> CloudAssessmentMailDomains => Set<CloudAssessmentMailDomain>();
    public DbSet<CloudAssessmentMailboxRisk> CloudAssessmentMailboxRisks => Set<CloudAssessmentMailboxRisk>();
    public DbSet<CloudAssessmentSharedMailbox> CloudAssessmentSharedMailboxes => Set<CloudAssessmentSharedMailbox>();

    // CA-11: Benchmarks
    public DbSet<CloudAssessmentIndustryBenchmark> CloudAssessmentIndustryBenchmarks => Set<CloudAssessmentIndustryBenchmark>();
    public DbSet<CloudAssessmentBenchmarkComparison> CloudAssessmentBenchmarkComparisons => Set<CloudAssessmentBenchmarkComparison>();
    public DbSet<CloudAssessmentFranchiseAggregate> CloudAssessmentFranchiseAggregates => Set<CloudAssessmentFranchiseAggregate>();
    public DbSet<CloudAssessmentGlobalAggregate> CloudAssessmentGlobalAggregates => Set<CloudAssessmentGlobalAggregate>();

    // CA-8: Compliance frameworks
    public DbSet<CloudAssessmentFramework> CloudAssessmentFrameworks => Set<CloudAssessmentFramework>();
    public DbSet<CloudAssessmentFrameworkControl> CloudAssessmentFrameworkControls => Set<CloudAssessmentFrameworkControl>();
    public DbSet<CloudAssessmentFindingControlMapping> CloudAssessmentFindingControlMappings => Set<CloudAssessmentFindingControlMapping>();
    public DbSet<CloudAssessmentFrameworkScore> CloudAssessmentFrameworkScores => Set<CloudAssessmentFrameworkScore>();

    // Service Catalog & Franchise Billing
    public DbSet<ServiceCatalogItem> ServiceCatalog => Set<ServiceCatalogItem>();
    public DbSet<FranchiseServiceRate> FranchiseServiceRates => Set<FranchiseServiceRate>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // ── Auth ──
        mb.Entity<Module>(e =>
        {
            e.ToTable("modules");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
        });

        mb.Entity<Action>(e =>
        {
            e.ToTable("actions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
        });

        mb.Entity<Permission>(e =>
        {
            e.ToTable("permissions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.ModuleId, x.ActionId }).IsUnique();
        });

        mb.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        mb.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions");
            e.HasKey(x => new { x.RoleId, x.PermissionId });
        });

        mb.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        mb.Entity<ExecutiveCta>(e =>
        {
            e.ToTable("executive_ctas");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => x.DeletedAt == null);
            e.HasOne(x => x.Organization)
             .WithMany()
             .HasForeignKey(x => x.OrganizationId);
        });

        mb.Entity<Actlog>(e =>
        {
            e.ToTable("actlog");
            e.HasKey(x => x.Id);
            e.Property(x => x.Timestamp).HasColumnName("timestamp");
        });

        // ── Core ──
        mb.Entity<Franchise>(e =>
        {
            e.ToTable("franchises");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        mb.Entity<Organization>(e =>
        {
            e.ToTable("organizations");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => x.DeletedAt == null);
            e.HasOne(x => x.Franchise).WithMany(x => x.Organizations).HasForeignKey(x => x.FranchiseId);
            e.HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId);
        });

        mb.Entity<Brand>(e =>
        {
            e.ToTable("brands");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
        });

        // ── CMDB ──
        mb.Entity<Machine>(e =>
        {
            e.ToTable("machines");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => x.DeletedAt == null);
            e.HasIndex(x => x.AgentId).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.Hostname }).IsUnique();
            e.HasOne(x => x.Organization).WithMany(x => x.Machines).HasForeignKey(x => x.OrganizationId);
            // Platform scope resolved at enrollment time (Phase 1: W10/W11 only).
            // Nullable FK -- servers / unknown OS stay NULL and get 0 controls.
            e.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId).IsRequired(false);
        });

        // ── Assessment ──
        mb.Entity<ControlCategory>(e =>
        {
            e.ToTable("control_categories");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
        });

        mb.Entity<ControlDef>(e =>
        {
            e.ToTable("control_defs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ControlId).IsUnique();
            e.HasQueryFilter(x => x.DeletedAt == null);
            e.Property(x => x.Type).HasColumnName("type");
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId);
        });

        mb.Entity<Framework>(e =>
        {
            e.ToTable("frameworks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
        });

        mb.Entity<Platform>(e =>
        {
            e.ToTable("platforms");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
        });

        mb.Entity<ControlFramework>(e =>
        {
            e.ToTable("control_frameworks");
            e.HasKey(x => new { x.ControlDefId, x.FrameworkId });
        });

        mb.Entity<ControlPlatform>(e =>
        {
            e.ToTable("control_platforms");
            e.HasKey(x => new { x.ControlDefId, x.PlatformId });
        });

        mb.Entity<Assessment>(e =>
        {
            e.ToTable("assessments");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => x.DeletedAt == null);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        });

        mb.Entity<AssessmentControl>(e =>
        {
            e.ToTable("assessment_controls");
            e.HasKey(x => new { x.AssessmentId, x.ControlDefId });
        });

        mb.Entity<AssessmentRun>(e =>
        {
            e.ToTable("assessment_runs");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Organization).WithMany(x => x.AssessmentRuns).HasForeignKey(x => x.OrganizationId);
            e.HasOne(x => x.Machine).WithMany(x => x.AssessmentRuns).HasForeignKey(x => x.MachineId);
            e.HasOne(x => x.Assessment).WithMany().HasForeignKey(x => x.AssessmentId);
        });

        mb.Entity<ControlResult>(e =>
        {
            e.ToTable("control_results");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.RunId, x.ControlDefId }).IsUnique();
            e.HasOne(x => x.Run).WithMany(x => x.ControlResults).HasForeignKey(x => x.RunId);
            e.HasOne(x => x.ControlDef).WithMany().HasForeignKey(x => x.ControlDefId);
        });

        mb.Entity<RunFrameworkScore>(e =>
        {
            e.ToTable("run_framework_scores");
            e.HasKey(x => new { x.RunId, x.FrameworkId });
            e.HasOne(x => x.Run).WithMany(x => x.RunFrameworkScores).HasForeignKey(x => x.RunId);
        });

        // ── Enrollment ──
        mb.Entity<EnrollmentCode>(e =>
        {
            e.ToTable("enrollment_codes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasQueryFilter(x => x.DeletedAt == null);
            e.HasOne(x => x.Organization).WithMany(x => x.EnrollmentCodes).HasForeignKey(x => x.OrganizationId);
            e.HasOne(x => x.Assessment).WithMany().HasForeignKey(x => x.AssessmentId);
            e.HasOne(x => x.UsedByMachine).WithMany().HasForeignKey(x => x.UsedBy);
        });

        mb.Entity<OrgCryptoKey>(e =>
        {
            e.ToTable("org_crypto_keys");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => x.DeletedAt == null);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        });

        // ── CMDB - Disk inventory ──
        mb.Entity<MachineDisk>(e =>
        {
            e.ToTable("machine_disks");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
        });

        // ── CMDB - Port scan results ──
        mb.Entity<MachinePort>(e =>
        {
            e.ToTable("machine_ports");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
        });

        // ── AD Hygiene ──
        mb.Entity<AdHygieneScan>(e =>
        {
            e.ToTable("ad_hygiene_scans");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        });

        mb.Entity<AdHygieneFinding>(e =>
        {
            e.ToTable("ad_hygiene_findings");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Scan).WithMany(x => x.Findings).HasForeignKey(x => x.ScanId);
        });

        // ── CMDB - Threat detection ──
        mb.Entity<MachineThreat>(e =>
        {
            e.ToTable("machine_threats");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
        });

        // ── External scans (cloud-side pentest) ──
        mb.Entity<ExternalScan>(e =>
        {
            e.ToTable("external_scans");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasMany(x => x.Results).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<ExternalScanResult>(e =>
        {
            e.ToTable("external_scan_results");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
        });

        // ── M365 / Entra ID ──
        mb.Entity<M365Tenant>(e =>
        {
            e.ToTable("m365_tenants");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrganizationId).IsUnique();
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasMany(x => x.Findings).WithOne(x => x.Tenant).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<M365Finding>(e =>
        {
            e.ToTable("m365_findings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
        });

        // ── Copilot Readiness (Phase 5) ──
        mb.Entity<CopilotReadinessScan>(e =>
        {
            e.ToTable("copilot_readiness_scans");
            e.HasKey(x => x.Id);
            // snake_case column mappings
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.D1Score).HasColumnName("d1_score");
            e.Property(x => x.D2Score).HasColumnName("d2_score");
            e.Property(x => x.D3Score).HasColumnName("d3_score");
            e.Property(x => x.D4Score).HasColumnName("d4_score");
            e.Property(x => x.D5Score).HasColumnName("d5_score");
            e.Property(x => x.D6Score).HasColumnName("d6_score");
            e.Property(x => x.OverallScore).HasColumnName("overall_score");
            e.Property(x => x.PipelineStatus).HasColumnName("pipeline_status");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
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
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.MetricKey).HasColumnName("metric_key");
            e.Property(x => x.MetricValue).HasColumnName("metric_value");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<CopilotReadinessFinding>(e =>
        {
            e.ToTable("copilot_readiness_findings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.LinkText).HasColumnName("link_text");
            e.Property(x => x.LinkUrl).HasColumnName("link_url");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<CopilotReadinessSharepoint>(e =>
        {
            e.ToTable("copilot_readiness_sharepoint");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.SiteUrl).HasColumnName("site_url");
            e.Property(x => x.SiteTitle).HasColumnName("site_title");
            e.Property(x => x.TotalFiles).HasColumnName("total_files");
            e.Property(x => x.LabeledFiles).HasColumnName("labeled_files");
            e.Property(x => x.OversharedFiles).HasColumnName("overshared_files");
            e.Property(x => x.RiskLevel).HasColumnName("risk_level");
            e.Property(x => x.TopLabels).HasColumnName("top_labels");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<CopilotReadinessExternalUser>(e =>
        {
            e.ToTable("copilot_readiness_external_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.UserPrincipal).HasColumnName("user_principal");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.EmailDomain).HasColumnName("email_domain");
            e.Property(x => x.LastSignIn).HasColumnName("last_sign_in");
            e.Property(x => x.RiskLevel).HasColumnName("risk_level");
            e.Property(x => x.SitesAccessed).HasColumnName("sites_accessed");
            e.Property(x => x.HighestPermission).HasColumnName("highest_permission");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // ── Cloud Assessment (CA-0) ──
        mb.Entity<CloudAssessmentScan>(e =>
        {
            e.ToTable("cloud_assessment_scans");
            e.HasKey(x => x.Id);
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.AzureSubscriptionIds).HasColumnName("azure_subscription_ids");
            e.Property(x => x.OverallScore).HasColumnName("overall_score");
            e.Property(x => x.AreaScores).HasColumnName("area_scores");
            e.Property(x => x.PipelineStatus).HasColumnName("pipeline_status");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade).IsRequired(false);
            e.HasMany(x => x.Findings).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Metrics).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Licenses).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Adoptions).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.WastedLicenses).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.SharepointSites).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ExternalUsers).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.MailDomains).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.MailboxRisks).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.SharedMailboxes).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            // Copilot Readiness D1-D6 columns
            e.Property(x => x.CopilotD1Score).HasColumnName("copilot_d1_score");
            e.Property(x => x.CopilotD2Score).HasColumnName("copilot_d2_score");
            e.Property(x => x.CopilotD3Score).HasColumnName("copilot_d3_score");
            e.Property(x => x.CopilotD4Score).HasColumnName("copilot_d4_score");
            e.Property(x => x.CopilotD5Score).HasColumnName("copilot_d5_score");
            e.Property(x => x.CopilotD6Score).HasColumnName("copilot_d6_score");
            e.Property(x => x.CopilotOverall).HasColumnName("copilot_overall");
            e.Property(x => x.CopilotVerdict).HasColumnName("copilot_verdict");
        });

        mb.Entity<CloudAssessmentSharepointSite>(e =>
        {
            e.ToTable("cloud_assessment_sharepoint_sites");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.SiteUrl).HasColumnName("site_url");
            e.Property(x => x.SiteTitle).HasColumnName("site_title");
            e.Property(x => x.TotalFiles).HasColumnName("total_files");
            e.Property(x => x.LabeledFiles).HasColumnName("labeled_files");
            e.Property(x => x.OversharedFiles).HasColumnName("overshared_files");
            e.Property(x => x.RiskLevel).HasColumnName("risk_level");
            e.Property(x => x.TopLabels).HasColumnName("top_labels");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<CloudAssessmentExternalUser>(e =>
        {
            e.ToTable("cloud_assessment_external_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.UserPrincipal).HasColumnName("user_principal");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.EmailDomain).HasColumnName("email_domain");
            e.Property(x => x.LastSignIn).HasColumnName("last_sign_in");
            e.Property(x => x.RiskLevel).HasColumnName("risk_level");
            e.Property(x => x.SitesAccessed).HasColumnName("sites_accessed");
            e.Property(x => x.HighestPermission).HasColumnName("highest_permission");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<CloudAssessmentFinding>(e =>
        {
            e.ToTable("cloud_assessment_findings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.LinkText).HasColumnName("link_text");
            e.Property(x => x.LinkUrl).HasColumnName("link_url");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<CloudAssessmentMetric>(e =>
        {
            e.ToTable("cloud_assessment_metrics");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.MetricKey).HasColumnName("metric_key");
            e.Property(x => x.MetricValue).HasColumnName("metric_value");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<CloudAssessmentAzureSubscription>(e =>
        {
            e.ToTable("cloud_assessment_azure_subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.SubscriptionId).HasColumnName("subscription_id");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.ConsentState).HasColumnName("consent_state");
            e.Property(x => x.ConnectedAt).HasColumnName("connected_at");
            e.Property(x => x.LastVerifiedAt).HasColumnName("last_verified_at");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasIndex(x => new { x.OrganizationId, x.SubscriptionId }).IsUnique();
        });

        mb.Entity<CloudAssessmentAzureResource>(e =>
        {
            e.ToTable("cloud_assessment_azure_resources");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.SubscriptionId).HasColumnName("subscription_id");
            e.Property(x => x.ResourceType).HasColumnName("resource_type");
            e.Property(x => x.ResourceId).HasColumnName("resource_id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Location).HasColumnName("location");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.PropertiesJson).HasColumnName("properties_json");
            e.Property(x => x.RiskFlags).HasColumnName("risk_flags");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Scan).WithMany(s => s.AzureResources).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ScanId, x.ResourceType }).HasDatabaseName("ix_car_scan");
            e.HasIndex(x => new { x.ScanId, x.SubscriptionId }).HasDatabaseName("ix_car_subscription");
        });

        mb.Entity<CloudAssessmentLicense>(e =>
        {
            e.ToTable("cloud_assessment_licenses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.SkuPartNumber).HasColumnName("sku_part_number");
            e.Property(x => x.FriendlyName).HasColumnName("friendly_name");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<CloudAssessmentAdoption>(e =>
        {
            e.ToTable("cloud_assessment_adoption");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.ServiceName).HasColumnName("service_name");
            e.Property(x => x.LicensedCount).HasColumnName("licensed_count");
            e.Property(x => x.Active30d).HasColumnName("active_30d");
            e.Property(x => x.AdoptionRate).HasColumnName("adoption_rate");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<CloudAssessmentWastedLicense>(e =>
        {
            e.ToTable("cloud_assessment_wasted_licenses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.UserPrincipal).HasColumnName("user_principal");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.LastSignIn).HasColumnName("last_sign_in");
            e.Property(x => x.DaysInactive).HasColumnName("days_inactive");
            e.Property(x => x.EstimatedCostYear).HasColumnName("estimated_cost_year");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<CloudAssessmentFindingStatus>(e =>
        {
            e.ToTable("cloud_assessment_finding_status");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasIndex(x => new { x.OrganizationId, x.Area, x.Service, x.Feature }).IsUnique();
        });

        mb.Entity<CloudAssessmentSuggestion>(e =>
        {
            e.ToTable("cloud_assessment_suggestions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Area).HasMaxLength(30).IsRequired();
            e.Property(x => x.Service).HasMaxLength(30).IsRequired();
            e.Property(x => x.Feature).HasMaxLength(200).IsRequired();
            e.Property(x => x.SuggestionType).HasMaxLength(30).IsRequired();
            e.HasIndex(x => new { x.OrganizationId, x.ScanId });
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasOne(x => x.Scan).WithMany().HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── CA-9: Power BI Governance ──
        mb.Entity<CloudAssessmentPowerBiConnection>(e =>
        {
            e.ToTable("cloud_assessment_powerbi_connection");
            e.HasKey(x => x.OrganizationId);
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.ConnectionState).HasColumnName("connection_state").HasMaxLength(20);
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.LastVerifiedAt).HasColumnName("last_verified_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        });

        mb.Entity<CloudAssessmentPowerBiWorkspace>(e =>
        {
            e.ToTable("cloud_assessment_powerbi_workspaces");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id").HasMaxLength(50);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(500);
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(30);
            e.Property(x => x.State).HasColumnName("state").HasMaxLength(30);
            e.Property(x => x.IsOnDedicatedCapacity).HasColumnName("is_on_dedicated_capacity");
            e.Property(x => x.CapacityId).HasColumnName("capacity_id").HasMaxLength(50);
            e.Property(x => x.HasWorkspaceLevelSettings).HasColumnName("has_workspace_level_settings");
            e.Property(x => x.MemberCount).HasColumnName("member_count");
            e.Property(x => x.AdminCount).HasColumnName("admin_count");
            e.Property(x => x.ExternalUserCount).HasColumnName("external_user_count");
            e.Property(x => x.DatasetCount).HasColumnName("dataset_count");
            e.Property(x => x.ReportCount).HasColumnName("report_count");
            e.Property(x => x.DashboardCount).HasColumnName("dashboard_count");
            e.Property(x => x.DataflowCount).HasColumnName("dataflow_count");
            e.Property(x => x.LastUpdatedDate).HasColumnName("last_updated_date");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Scan).WithMany().HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ScanId).HasDatabaseName("ix_powerbi_ws_scan");
        });

        mb.Entity<CloudAssessmentPowerBiGateway>(e =>
        {
            e.ToTable("cloud_assessment_powerbi_gateways");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.GatewayId).HasColumnName("gateway_id").HasMaxLength(50);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(500);
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(30);
            e.Property(x => x.PublicKeyValid).HasColumnName("public_key_valid");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(30);
            e.Property(x => x.Version).HasColumnName("version").HasMaxLength(50);
            e.Property(x => x.ContactInformation).HasColumnName("contact_information").HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Scan).WithMany().HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ScanId).HasDatabaseName("ix_powerbi_gw_scan");
        });

        mb.Entity<CloudAssessmentPowerBiCapacity>(e =>
        {
            e.ToTable("cloud_assessment_powerbi_capacities");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.CapacityId).HasColumnName("capacity_id").HasMaxLength(50);
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(500);
            e.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(50);
            e.Property(x => x.Region).HasColumnName("region").HasMaxLength(50);
            e.Property(x => x.State).HasColumnName("state").HasMaxLength(30);
            e.Property(x => x.UsagePct).HasColumnName("usage_pct").HasColumnType("decimal(5,2)");
            e.Property(x => x.AdminCount).HasColumnName("admin_count");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Scan).WithMany().HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ScanId).HasDatabaseName("ix_powerbi_cap_scan");
        });

        mb.Entity<CloudAssessmentPowerBiActivitySummary>(e =>
        {
            e.ToTable("cloud_assessment_powerbi_activity_summary");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.ActivitiesTotal).HasColumnName("activities_total");
            e.Property(x => x.UniqueUsers).HasColumnName("unique_users");
            e.Property(x => x.ViewReportCount).HasColumnName("view_report_count");
            e.Property(x => x.EditReportCount).HasColumnName("edit_report_count");
            e.Property(x => x.CreateDatasetCount).HasColumnName("create_dataset_count");
            e.Property(x => x.DeleteCount).HasColumnName("delete_count");
            e.Property(x => x.ShareExternalCount).HasColumnName("share_external_count");
            e.Property(x => x.ExportCount).HasColumnName("export_count");
            e.Property(x => x.PeriodDays).HasColumnName("period_days");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Scan).WithMany().HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ScanId).HasDatabaseName("ix_powerbi_act_scan");
        });

        // ── CA-8: Compliance Frameworks ──
        mb.Entity<CloudAssessmentFramework>(e =>
        {
            e.ToTable("cloud_assessment_frameworks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Version).HasMaxLength(30);
            e.Property(x => x.Authority).HasMaxLength(200);
            e.Property(x => x.DocUrl).HasColumnName("doc_url").HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.Code).IsUnique();
        });

        mb.Entity<CloudAssessmentFrameworkControl>(e =>
        {
            e.ToTable("cloud_assessment_framework_controls");
            e.HasKey(x => x.Id);
            e.Property(x => x.FrameworkId).HasColumnName("framework_id");
            e.Property(x => x.ControlCode).HasColumnName("control_code").HasMaxLength(50).IsRequired();
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Priority).HasMaxLength(10);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Framework).WithMany(f => f.Controls).HasForeignKey(x => x.FrameworkId);
            e.HasIndex(x => new { x.FrameworkId, x.ControlCode }).IsUnique();
        });

        mb.Entity<CloudAssessmentFindingControlMapping>(e =>
        {
            e.ToTable("cloud_assessment_finding_control_mappings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Area).HasMaxLength(30).IsRequired();
            e.Property(x => x.Service).HasMaxLength(30).IsRequired();
            e.Property(x => x.Feature).HasMaxLength(200).IsRequired();
            e.Property(x => x.FrameworkControlId).HasColumnName("framework_control_id");
            e.Property(x => x.Coverage).HasMaxLength(20).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.FrameworkControl).WithMany(c => c.Mappings).HasForeignKey(x => x.FrameworkControlId);
            e.HasIndex(x => new { x.Area, x.Service, x.Feature });
            e.HasIndex(x => x.FrameworkControlId);
        });

        mb.Entity<CloudAssessmentFrameworkScore>(e =>
        {
            e.ToTable("cloud_assessment_framework_scores");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.FrameworkId).HasColumnName("framework_id");
            e.Property(x => x.TotalControls).HasColumnName("total_controls");
            e.Property(x => x.CoveredControls).HasColumnName("covered_controls");
            e.Property(x => x.PassingControls).HasColumnName("passing_controls");
            e.Property(x => x.FailingControls).HasColumnName("failing_controls");
            e.Property(x => x.UnmappedControls).HasColumnName("unmapped_controls");
            e.Property(x => x.ScorePct).HasColumnName("score_pct").HasColumnType("decimal(5,2)");
            e.Property(x => x.Grade).HasMaxLength(5);
            e.Property(x => x.ComputedAt).HasColumnName("computed_at");
            e.HasOne(x => x.Scan).WithMany(s => s.FrameworkScores).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Framework).WithMany().HasForeignKey(x => x.FrameworkId);
            e.HasIndex(x => x.ScanId);
        });

        // ── CA-10: Mail Flow & Email Security ──
        mb.Entity<CloudAssessmentMailDomain>(e =>
        {
            e.ToTable("cloud_assessment_mail_domains");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(255).IsRequired();
            e.Property(x => x.IsDefault).HasColumnName("is_default");
            e.Property(x => x.IsVerified).HasColumnName("is_verified");
            e.Property(x => x.SpfRecord).HasColumnName("spf_record");
            e.Property(x => x.SpfValid).HasColumnName("spf_valid");
            e.Property(x => x.SpfMechanism).HasColumnName("spf_mechanism").HasMaxLength(20);
            e.Property(x => x.SpfLookupCount).HasColumnName("spf_lookup_count");
            e.Property(x => x.SpfWarnings).HasColumnName("spf_warnings");
            e.Property(x => x.DkimS1Present).HasColumnName("dkim_s1_present");
            e.Property(x => x.DkimS2Present).HasColumnName("dkim_s2_present");
            e.Property(x => x.DkimSelectors).HasColumnName("dkim_selectors");
            e.Property(x => x.DmarcRecord).HasColumnName("dmarc_record");
            e.Property(x => x.DmarcValid).HasColumnName("dmarc_valid");
            e.Property(x => x.DmarcPolicy).HasColumnName("dmarc_policy").HasMaxLength(20);
            e.Property(x => x.DmarcSubdomainPolicy).HasColumnName("dmarc_subdomain_policy").HasMaxLength(20);
            e.Property(x => x.DmarcPct).HasColumnName("dmarc_pct");
            e.Property(x => x.DmarcRua).HasColumnName("dmarc_rua").HasMaxLength(500);
            e.Property(x => x.DmarcRuf).HasColumnName("dmarc_ruf").HasMaxLength(500);
            e.Property(x => x.MtaStsRecord).HasColumnName("mta_sts_record");
            e.Property(x => x.MtaStsPolicy).HasColumnName("mta_sts_policy").HasMaxLength(20);
            e.Property(x => x.BimiPresent).HasColumnName("bimi_present");
            e.Property(x => x.Score).HasColumnName("score").HasColumnType("decimal(3,1)");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Scan).WithMany(s => s.MailDomains).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ScanId).HasDatabaseName("ix_ca_mail_domains_scan");
            e.HasIndex(x => new { x.ScanId, x.Domain }).IsUnique().HasDatabaseName("ux_mail_domains_scan_domain");
        });

        mb.Entity<CloudAssessmentMailboxRisk>(e =>
        {
            e.ToTable("cloud_assessment_mailbox_risks");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.UserPrincipalName).HasColumnName("user_principal_name").HasMaxLength(500).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(500);
            e.Property(x => x.RiskType).HasColumnName("risk_type").HasMaxLength(50).IsRequired();
            e.Property(x => x.RiskDetail).HasColumnName("risk_detail");
            e.Property(x => x.ForwardTarget).HasColumnName("forward_target").HasMaxLength(500);
            e.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(20);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Scan).WithMany(s => s.MailboxRisks).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ScanId).HasDatabaseName("ix_ca_mailbox_risks_scan");
            e.HasIndex(x => new { x.ScanId, x.UserPrincipalName, x.RiskType }).IsUnique().HasDatabaseName("ux_mailbox_risks_scan_upn_type");
        });

        mb.Entity<CloudAssessmentSharedMailbox>(e =>
        {
            e.ToTable("cloud_assessment_shared_mailboxes");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.MailboxUpn).HasColumnName("mailbox_upn").HasMaxLength(500).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(500);
            e.Property(x => x.DelegatesCount).HasColumnName("delegates_count");
            e.Property(x => x.FullAccessUsers).HasColumnName("full_access_users");
            e.Property(x => x.SendAsUsers).HasColumnName("send_as_users");
            e.Property(x => x.HasPasswordEnabled).HasColumnName("has_password_enabled");
            e.Property(x => x.LastActivity).HasColumnName("last_activity");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Scan).WithMany(s => s.SharedMailboxes).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ScanId).HasDatabaseName("ix_ca_shared_mailboxes_scan");
            e.HasIndex(x => new { x.ScanId, x.MailboxUpn }).IsUnique().HasDatabaseName("ux_shared_mailboxes_scan_upn");
        });

        // ── CA-11: Benchmarks ──
        mb.Entity<CloudAssessmentIndustryBenchmark>(e =>
        {
            e.ToTable("cloud_assessment_industry_benchmarks");
            e.HasKey(x => x.Id);
            e.Property(x => x.IndustryCode).HasColumnName("industry_code").HasMaxLength(30).IsRequired();
            e.Property(x => x.EmployeeBand).HasColumnName("employee_band").HasMaxLength(20);
            e.Property(x => x.MetricKey).HasColumnName("metric_key").HasMaxLength(100).IsRequired();
            e.Property(x => x.BaselineValue).HasColumnName("baseline_value").HasColumnType("decimal(10,2)");
            e.Property(x => x.Percentile25).HasColumnName("percentile_25").HasColumnType("decimal(10,2)");
            e.Property(x => x.Percentile50).HasColumnName("percentile_50").HasColumnType("decimal(10,2)");
            e.Property(x => x.Percentile75).HasColumnName("percentile_75").HasColumnType("decimal(10,2)");
            e.Property(x => x.SampleSize).HasColumnName("sample_size");
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(100);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.IndustryCode, x.EmployeeBand, x.MetricKey }).IsUnique().HasDatabaseName("UQ_industry_benchmarks");
            e.HasIndex(x => new { x.IndustryCode, x.MetricKey }).HasDatabaseName("ix_industry_benchmarks_lookup");
        });

        mb.Entity<CloudAssessmentBenchmarkComparison>(e =>
        {
            e.ToTable("cloud_assessment_benchmark_comparisons");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.MetricKey).HasColumnName("metric_key").HasMaxLength(100).IsRequired();
            e.Property(x => x.OrgValue).HasColumnName("org_value").HasColumnType("decimal(10,2)");
            e.Property(x => x.FranchiseAvg).HasColumnName("franchise_avg").HasColumnType("decimal(10,2)");
            e.Property(x => x.FranchisePercentile).HasColumnName("franchise_percentile").HasColumnType("decimal(5,2)");
            e.Property(x => x.FranchiseSampleSize).HasColumnName("franchise_sample_size");
            e.Property(x => x.IndustryBaseline).HasColumnName("industry_baseline").HasColumnType("decimal(10,2)");
            e.Property(x => x.IndustryP25).HasColumnName("industry_p25").HasColumnType("decimal(10,2)");
            e.Property(x => x.IndustryP50).HasColumnName("industry_p50").HasColumnType("decimal(10,2)");
            e.Property(x => x.IndustryP75).HasColumnName("industry_p75").HasColumnType("decimal(10,2)");
            e.Property(x => x.IndustryPercentile).HasColumnName("industry_percentile").HasColumnType("decimal(5,2)");
            e.Property(x => x.GlobalAvg).HasColumnName("global_avg").HasColumnType("decimal(10,2)");
            e.Property(x => x.GlobalPercentile).HasColumnName("global_percentile").HasColumnType("decimal(5,2)");
            e.Property(x => x.GlobalSampleSize).HasColumnName("global_sample_size");
            e.Property(x => x.Verdict).HasColumnName("verdict").HasMaxLength(30);
            e.Property(x => x.ComputedAt).HasColumnName("computed_at");
            e.HasOne(x => x.Scan).WithMany().HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ScanId, x.MetricKey }).HasDatabaseName("ix_benchmark_scan");
        });

        mb.Entity<CloudAssessmentFranchiseAggregate>(e =>
        {
            e.ToTable("cloud_assessment_franchise_aggregates");
            e.HasKey(x => x.Id);
            e.Property(x => x.FranchiseId).HasColumnName("franchise_id");
            e.Property(x => x.MetricKey).HasColumnName("metric_key").HasMaxLength(100).IsRequired();
            e.Property(x => x.AvgValue).HasColumnName("avg_value").HasColumnType("decimal(10,2)");
            e.Property(x => x.Percentile25).HasColumnName("percentile_25").HasColumnType("decimal(10,2)");
            e.Property(x => x.Percentile50).HasColumnName("percentile_50").HasColumnType("decimal(10,2)");
            e.Property(x => x.Percentile75).HasColumnName("percentile_75").HasColumnType("decimal(10,2)");
            e.Property(x => x.SampleSize).HasColumnName("sample_size");
            e.Property(x => x.RefreshedAt).HasColumnName("refreshed_at");
            e.HasOne(x => x.Franchise).WithMany().HasForeignKey(x => x.FranchiseId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.FranchiseId, x.MetricKey }).IsUnique().HasDatabaseName("UQ_franchise_aggregates");
        });

        mb.Entity<CloudAssessmentGlobalAggregate>(e =>
        {
            e.ToTable("cloud_assessment_global_aggregates");
            e.HasKey(x => x.Id);
            e.Property(x => x.MetricKey).HasColumnName("metric_key").HasMaxLength(100).IsRequired();
            e.Property(x => x.IndustryCode).HasColumnName("industry_code").HasMaxLength(30);
            e.Property(x => x.EmployeeBand).HasColumnName("employee_band").HasMaxLength(20);
            e.Property(x => x.AvgValue).HasColumnName("avg_value").HasColumnType("decimal(10,2)");
            e.Property(x => x.Percentile25).HasColumnName("percentile_25").HasColumnType("decimal(10,2)");
            e.Property(x => x.Percentile50).HasColumnName("percentile_50").HasColumnType("decimal(10,2)");
            e.Property(x => x.Percentile75).HasColumnName("percentile_75").HasColumnType("decimal(10,2)");
            e.Property(x => x.SampleSize).HasColumnName("sample_size");
            e.Property(x => x.RefreshedAt).HasColumnName("refreshed_at");
            e.HasIndex(x => new { x.MetricKey, x.IndustryCode, x.EmployeeBand }).HasDatabaseName("ix_global_aggregates_lookup");
        });

        // Organization CA-11 columns + Franchise opt-in.
        mb.Entity<Organization>(e =>
        {
            e.Property(x => x.IndustryCode).HasColumnName("industry_code").HasMaxLength(30);
            e.Property(x => x.IndustrySubcode).HasColumnName("industry_subcode").HasMaxLength(50);
            e.Property(x => x.EmployeeCountBand).HasColumnName("employee_count_band").HasMaxLength(20);
        });

        mb.Entity<Franchise>(e =>
        {
            e.Property(x => x.BenchmarkOptIn).HasColumnName("benchmark_opt_in");
        });
    }
}
