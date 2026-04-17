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
    public DbSet<CloudAssessmentLicense> CloudAssessmentLicenses => Set<CloudAssessmentLicense>();
    public DbSet<CloudAssessmentAdoption> CloudAssessmentAdoptions => Set<CloudAssessmentAdoption>();
    public DbSet<CloudAssessmentWastedLicense> CloudAssessmentWastedLicenses => Set<CloudAssessmentWastedLicense>();
    public DbSet<CloudAssessmentFindingStatus> CloudAssessmentFindingStatuses => Set<CloudAssessmentFindingStatus>();

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
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasIndex(x => new { x.OrganizationId, x.SubscriptionId }).IsUnique();
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
    }
}
