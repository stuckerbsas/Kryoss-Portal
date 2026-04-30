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

    // CMDB - Network diagnostics
    public DbSet<MachineNetworkDiag> MachineNetworkDiags => Set<MachineNetworkDiag>();
    public DbSet<MachineNetworkLatency> MachineNetworkLatencies => Set<MachineNetworkLatency>();
    public DbSet<MachineNetworkRoute> MachineNetworkRoutes => Set<MachineNetworkRoute>();

    // SNMP infrastructure
    public DbSet<SnmpConfig> SnmpConfigs => Set<SnmpConfig>();
    public DbSet<SnmpDevice> SnmpDevices => Set<SnmpDevice>();
    public DbSet<SnmpDeviceInterface> SnmpDeviceInterfaces => Set<SnmpDeviceInterface>();
    public DbSet<SnmpDeviceSupply> SnmpDeviceSupplies => Set<SnmpDeviceSupply>();
    public DbSet<SnmpDeviceProfile> SnmpDeviceProfiles => Set<SnmpDeviceProfile>();
    public DbSet<SnmpProfileOid> SnmpProfileOids => Set<SnmpProfileOid>();
    public DbSet<SnmpDeviceNeighbor> SnmpDeviceNeighbors => Set<SnmpDeviceNeighbor>();

    // External scans (cloud-side pentest)
    public DbSet<ExternalScan> ExternalScans => Set<ExternalScan>();
    public DbSet<ExternalScanResult> ExternalScanResults => Set<ExternalScanResult>();
    public DbSet<ExternalScanFinding> ExternalScanFindings => Set<ExternalScanFinding>();

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

    // Network Sites + Public IP (IA-11) + WAN Health (IA-3)
    public DbSet<MachinePublicIpHistory> MachinePublicIpHistory => Set<MachinePublicIpHistory>();
    public DbSet<NetworkSite> NetworkSites => Set<NetworkSite>();
    public DbSet<WanFinding> WanFindings => Set<WanFinding>();

    // CA-15: Drift Alerts
    public DbSet<CloudAssessmentAlertRule> CloudAssessmentAlertRules => Set<CloudAssessmentAlertRule>();
    public DbSet<CloudAssessmentAlertSent> CloudAssessmentAlertsSent => Set<CloudAssessmentAlertSent>();

    // CVE Scanner (A-01)
    public DbSet<CveEntry> CveEntries => Set<CveEntry>();
    public DbSet<MachineCveFinding> MachineCveFindings => Set<MachineCveFinding>();
    public DbSet<CveSyncLog> CveSyncLogs => Set<CveSyncLog>();

    // Patch Compliance
    public DbSet<MachinePatchStatus> MachinePatchStatuses => Set<MachinePatchStatus>();
    public DbSet<MachinePatch> MachinePatches => Set<MachinePatch>();

    // Available Updates (WUC-02)
    public DbSet<MachineAvailableUpdate> MachineAvailableUpdates => Set<MachineAvailableUpdate>();

    // DC Health (DC-02+03)
    public DbSet<DcHealthSnapshot> DcHealthSnapshots => Set<DcHealthSnapshot>();
    public DbSet<DcReplicationPartner> DcReplicationPartners => Set<DcReplicationPartner>();

    // Remediation
    public DbSet<RemediationAction> RemediationActions => Set<RemediationAction>();
    public DbSet<RemediationTask> RemediationTasks => Set<RemediationTask>();
    public DbSet<OrgAutoRemediate> OrgAutoRemediates => Set<OrgAutoRemediate>();
    public DbSet<RemediationLog> RemediationLogs => Set<RemediationLog>();
    public DbSet<MachineService> MachineServices => Set<MachineService>();

    // DB-NORM: Normalized child tables (replace JSON columns)
    public DbSet<ControlCheckParam> ControlCheckParams => Set<ControlCheckParam>();
    public DbSet<MachineLocalAdmin> MachineLocalAdmins => Set<MachineLocalAdmin>();
    public DbSet<MachineLoopStatus> MachineLoopStatuses => Set<MachineLoopStatus>();
    public DbSet<OrgPriorityService> OrgPriorityServices => Set<OrgPriorityService>();
    public DbSet<MachineTracerouteHop> MachineTracerouteHops => Set<MachineTracerouteHop>();
    public DbSet<CloudFindingProperty> CloudFindingProperties => Set<CloudFindingProperty>();
    public DbSet<CloudResourceRiskFlag> CloudResourceRiskFlags => Set<CloudResourceRiskFlag>();
    public DbSet<MailDomainSpfWarning> MailDomainSpfWarnings => Set<MailDomainSpfWarning>();
    public DbSet<MailDomainDkimSelector> MailDomainDkimSelectors => Set<MailDomainDkimSelector>();
    public DbSet<SharedMailboxDelegate> SharedMailboxDelegates => Set<SharedMailboxDelegate>();
    public DbSet<AlertPayloadField> AlertPayloadFields => Set<AlertPayloadField>();
    public DbSet<RemediationActionParam> RemediationActionParams => Set<RemediationActionParam>();
    public DbSet<CveProductMap> CveProductMaps => Set<CveProductMap>();
    public DbSet<Software> Software => Set<Software>();
    public DbSet<MachineSoftware> MachineSoftware => Set<MachineSoftware>();

    // Infrastructure Assessment (IA-0)
    public DbSet<InfraAssessmentScan> InfraAssessmentScans => Set<InfraAssessmentScan>();
    public DbSet<InfraAssessmentSite> InfraAssessmentSites => Set<InfraAssessmentSite>();
    public DbSet<InfraAssessmentDevice> InfraAssessmentDevices => Set<InfraAssessmentDevice>();
    public DbSet<InfraAssessmentConnectivity> InfraAssessmentConnectivity => Set<InfraAssessmentConnectivity>();
    public DbSet<InfraAssessmentCapacity> InfraAssessmentCapacity => Set<InfraAssessmentCapacity>();
    public DbSet<InfraAssessmentFinding> InfraAssessmentFindings => Set<InfraAssessmentFinding>();
    public DbSet<InfraHypervisorConfig> InfraHypervisorConfigs => Set<InfraHypervisorConfig>();
    public DbSet<InfraHypervisor> InfraHypervisors => Set<InfraHypervisor>();
    public DbSet<InfraVm> InfraVms => Set<InfraVm>();

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
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.MachineId);
            e.HasIndex(x => x.UserId);
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
            // Auth / key material column mappings
            e.Property(x => x.MachineSecret).HasColumnName("machine_secret");
            e.Property(x => x.SessionKey).HasColumnName("session_key");
            e.Property(x => x.SessionKeyExpiresAt).HasColumnName("session_key_expires_at");
            e.Property(x => x.PrevSessionKey).HasColumnName("prev_session_key");
            e.Property(x => x.PrevKeyExpiresAt).HasColumnName("prev_key_expires_at");
            e.Property(x => x.KeyRotatedAt).HasColumnName("key_rotated_at");
            e.Property(x => x.AuthVersion).HasColumnName("auth_version");
            e.Property(x => x.ConfigComplianceIntervalHours).HasColumnName("config_compliance_interval_hours");
            e.Property(x => x.ConfigSnmpIntervalMinutes).HasColumnName("config_snmp_interval_minutes");
            e.Property(x => x.ConfigEnableNetworkScan).HasColumnName("config_enable_network_scan");
            e.Property(x => x.ConfigNetworkScanIntervalHours).HasColumnName("config_network_scan_interval_hours");
            e.Property(x => x.ConfigEnablePassiveDiscovery).HasColumnName("config_enable_passive_discovery");
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

        // ── CMDB - Network diagnostics ──
        mb.Entity<MachineNetworkDiag>(e =>
        {
            e.ToTable("machine_network_diag");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            e.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId);
            e.Property(x => x.GatewayLatencyMs).HasColumnName("gateway_latency_ms");
            e.Property(x => x.GatewayIp).HasColumnName("gateway_ip");
            e.Property(x => x.DnsResolutionMs).HasColumnName("dns_resolution_ms");
            e.Property(x => x.CloudEndpointCount).HasColumnName("cloud_endpoint_count");
            e.Property(x => x.CloudEndpointAvgMs).HasColumnName("cloud_endpoint_avg_ms");
            e.Property(x => x.TriggeredByIpChange).HasColumnName("triggered_by_ip_change");
            e.Property(x => x.WifiCount).HasColumnName("wifi_count");
            e.Property(x => x.VpnAdapterCount).HasColumnName("vpn_adapter_count");
            e.Property(x => x.EthCount).HasColumnName("eth_count");
            e.HasMany(x => x.LatencyPeers).WithOne(x => x.Diag).HasForeignKey(x => x.DiagId);
            e.HasMany(x => x.Routes).WithOne(x => x.Diag).HasForeignKey(x => x.DiagId);
        });
        mb.Entity<MachineNetworkLatency>(e =>
        {
            e.ToTable("machine_network_latency");
            e.HasKey(x => x.Id);
        });
        mb.Entity<MachineNetworkRoute>(e =>
        {
            e.ToTable("machine_network_routes");
            e.HasKey(x => x.Id);
        });

        // ── Network Sites + Public IP (IA-11) ──
        mb.Entity<MachinePublicIpHistory>(e =>
        {
            e.ToTable("machine_public_ip_history");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
        });
        mb.Entity<NetworkSite>(e =>
        {
            e.ToTable("network_sites");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasMany(x => x.WanFindings).WithOne(x => x.Site).HasForeignKey(x => x.SiteId);
        });
        mb.Entity<WanFinding>(e =>
        {
            e.ToTable("wan_findings");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        });

        // ── CVE Scanner (A-01) ──
        mb.Entity<CveEntry>(e =>
        {
            e.ToTable("cve_entries");
            e.HasKey(x => x.Id);
        });
        mb.Entity<MachineCveFinding>(e =>
        {
            e.ToTable("machine_cve_findings");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        });
        mb.Entity<CveSyncLog>(e =>
        {
            e.ToTable("cve_sync_log");
            e.HasKey(x => x.Id);
        });

        // ── Patch Compliance ──
        mb.Entity<MachinePatchStatus>(e =>
        {
            e.ToTable("machine_patch_status");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasIndex(x => x.MachineId).IsUnique();
        });
        mb.Entity<MachinePatch>(e =>
        {
            e.ToTable("machine_patches");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            e.HasIndex(x => new { x.MachineId, x.HotfixId }).IsUnique();
        });

        // ── DC Health (DC-02+03) ──
        mb.Entity<DcHealthSnapshot>(e =>
        {
            e.ToTable("dc_health_snapshots");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasMany(x => x.ReplicationPartners).WithOne(x => x.Snapshot).HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.OrganizationId);
            e.HasIndex(x => x.MachineId);
        });
        mb.Entity<DcReplicationPartner>(e =>
        {
            e.ToTable("dc_replication_partners");
            e.HasKey(x => x.Id);
        });

        // ── SNMP infrastructure ──
        mb.Entity<SnmpConfig>(e =>
        {
            e.ToTable("snmp_configs");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        });
        mb.Entity<SnmpDevice>(e =>
        {
            e.ToTable("snmp_devices");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            e.HasMany(x => x.Interfaces).WithOne(x => x.Device).HasForeignKey(x => x.DeviceId);
            e.HasMany(x => x.Supplies).WithOne(x => x.Device).HasForeignKey(x => x.DeviceId);
            e.HasMany(x => x.Neighbors).WithOne(x => x.Device).HasForeignKey(x => x.DeviceId);
            e.Property(x => x.LldpNeighborCount).HasColumnName("lldp_neighbor_count");
            e.Property(x => x.CdpNeighborCount).HasColumnName("cdp_neighbor_count");
        });
        mb.Entity<SnmpDeviceNeighbor>(e =>
        {
            e.ToTable("snmp_device_neighbors");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.ResolvedDevice).WithMany().HasForeignKey(x => x.ResolvedDeviceId);
        });
        mb.Entity<SnmpDeviceInterface>(e =>
        {
            e.ToTable("snmp_device_interfaces");
            e.HasKey(x => x.Id);
        });
        mb.Entity<SnmpDeviceSupply>(e =>
        {
            e.ToTable("snmp_device_supplies");
            e.HasKey(x => x.Id);
        });
        mb.Entity<SnmpDeviceProfile>(e =>
        {
            e.ToTable("snmp_device_profiles");
            e.HasKey(x => x.Id);
            e.HasMany(x => x.Oids).WithOne(x => x.Profile).HasForeignKey(x => x.ProfileId);
        });
        mb.Entity<SnmpProfileOid>(e =>
        {
            e.ToTable("snmp_profile_oids");
            e.HasKey(x => x.Id);
        });

        // ── External scans (cloud-side pentest) ──
        mb.Entity<ExternalScan>(e =>
        {
            e.ToTable("external_scans");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasMany(x => x.Results).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Findings).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<ExternalScanResult>(e =>
        {
            e.ToTable("external_scan_results");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
        });

        mb.Entity<ExternalScanFinding>(e =>
        {
            e.ToTable("external_scan_findings");
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
            e.Property(x => x.FeatureInventory).HasColumnName("feature_inventory");
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
            e.Property(x => x.DkimS1Present).HasColumnName("dkim_s1_present");
            e.Property(x => x.DkimS2Present).HasColumnName("dkim_s2_present");
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

        // ── CA-15: Drift Alerts ──
        mb.Entity<CloudAssessmentAlertRule>(e =>
        {
            e.ToTable("cloud_assessment_alert_rules");
            e.HasKey(x => x.Id);
            e.Property(x => x.FranchiseId).HasColumnName("franchise_id");
            e.Property(x => x.RuleType).HasColumnName("rule_type").HasMaxLength(60);
            e.Property(x => x.Threshold).HasColumnName("threshold").HasColumnType("decimal(8,2)");
            e.Property(x => x.FrameworkCode).HasColumnName("framework_code").HasMaxLength(20);
            e.Property(x => x.IsEnabled).HasColumnName("is_enabled");
            e.Property(x => x.DeliveryChannel).HasColumnName("delivery_channel").HasMaxLength(20);
            e.Property(x => x.TargetEmail).HasColumnName("target_email").HasMaxLength(256);
            e.Property(x => x.WebhookUrl).HasColumnName("webhook_url").HasMaxLength(512);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.Franchise).WithMany().HasForeignKey(x => x.FranchiseId);
            e.HasIndex(x => x.FranchiseId);
        });

        mb.Entity<CloudAssessmentAlertSent>(e =>
        {
            e.ToTable("cloud_assessment_alerts_sent");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.RuleId).HasColumnName("rule_id");
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(20);
            e.Property(x => x.RuleType).HasColumnName("rule_type").HasMaxLength(60);
            e.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500);
            e.Property(x => x.DeliveryStatus).HasColumnName("delivery_status").HasMaxLength(20);
            e.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(500);
            e.Property(x => x.FiredAt).HasColumnName("fired_at");
            e.HasOne(x => x.Scan).WithMany().HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Rule).WithMany().HasForeignKey(x => x.RuleId);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasIndex(x => x.ScanId);
            e.HasIndex(x => x.OrganizationId);
        });

        // ── Remediation ──
        mb.Entity<RemediationAction>(e =>
        {
            e.ToTable("remediation_actions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.ControlDefId).HasColumnName("control_def_id");
            e.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(30);
            e.Property(x => x.RiskLevel).HasColumnName("risk_level").HasMaxLength(10);
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.ControlDef).WithMany().HasForeignKey(x => x.ControlDefId);
        });

        mb.Entity<RemediationTask>(e =>
        {
            e.ToTable("remediation_tasks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.MachineId).HasColumnName("machine_id");
            e.Property(x => x.ControlDefId).HasColumnName("control_def_id");
            e.Property(x => x.ActionId).HasColumnName("action_id");
            e.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(30);
            e.Property(x => x.Params).HasColumnName("params");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
            e.Property(x => x.PreviousValue).HasColumnName("previous_value");
            e.Property(x => x.NewValue).HasColumnName("new_value");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(1000);
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.ApprovedBy).HasColumnName("approved_by");
            e.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            e.Property(x => x.ExecutedAt).HasColumnName("executed_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.ScheduledFor).HasColumnName("scheduled_for");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            e.HasOne(x => x.ControlDef).WithMany().HasForeignKey(x => x.ControlDefId);
            e.HasOne(x => x.Action).WithMany().HasForeignKey(x => x.ActionId);
            e.HasIndex(x => new { x.MachineId, x.Status }).HasDatabaseName("ix_rem_task_machine");
            e.HasIndex(x => new { x.OrganizationId, x.CreatedAt }).HasDatabaseName("ix_rem_task_org");
        });

        mb.Entity<OrgAutoRemediate>(e =>
        {
            e.ToTable("org_auto_remediate");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.ControlDefId).HasColumnName("control_def_id");
            e.Property(x => x.EnabledBy).HasColumnName("enabled_by");
            e.Property(x => x.EnabledAt).HasColumnName("enabled_at");
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasOne(x => x.ControlDef).WithMany().HasForeignKey(x => x.ControlDefId);
            e.HasIndex(x => new { x.OrganizationId, x.ControlDefId }).IsUnique().HasDatabaseName("uq_org_auto_rem");
        });

        // ── Infrastructure Assessment (IA-0) ──
        mb.Entity<InfraAssessmentScan>(e =>
        {
            e.ToTable("infra_assessment_scans");
            e.HasKey(x => x.Id);
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(30);
            e.Property(x => x.Scope).HasColumnName("scope");
            e.Property(x => x.OverallHealth).HasColumnName("overall_health");
            e.Property(x => x.SiteCount).HasColumnName("site_count");
            e.Property(x => x.DeviceCount).HasColumnName("device_count");
            e.Property(x => x.FindingCount).HasColumnName("finding_count");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasMany(x => x.Sites).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Devices).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Connectivity).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Capacity).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Findings).WithOne(x => x.Scan).HasForeignKey(x => x.ScanId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<InfraAssessmentSite>(e =>
        {
            e.ToTable("infra_assessment_sites");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.SiteName).HasColumnName("site_name").HasMaxLength(200);
            e.Property(x => x.Location).HasColumnName("location").HasMaxLength(500);
            e.Property(x => x.SiteType).HasColumnName("site_type").HasMaxLength(30);
            e.Property(x => x.DeviceCount).HasColumnName("device_count");
            e.Property(x => x.UserCount).HasColumnName("user_count");
            e.Property(x => x.ConnectivityType).HasColumnName("connectivity_type").HasMaxLength(100);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        mb.Entity<InfraAssessmentDevice>(e =>
        {
            e.ToTable("infra_assessment_devices");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.SiteId).HasColumnName("site_id");
            e.Property(x => x.Hostname).HasColumnName("hostname").HasMaxLength(255);
            e.Property(x => x.DeviceType).HasColumnName("device_type").HasMaxLength(30);
            e.Property(x => x.Vendor).HasColumnName("vendor").HasMaxLength(200);
            e.Property(x => x.Model).HasColumnName("model").HasMaxLength(200);
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(200);
            e.Property(x => x.IpAddress).HasColumnName("ip_address").HasMaxLength(50);
            e.Property(x => x.Os).HasColumnName("os").HasMaxLength(200);
            e.Property(x => x.Firmware).HasColumnName("firmware").HasMaxLength(200);
            e.Property(x => x.SerialNumber).HasColumnName("serial_number").HasMaxLength(200);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId);
        });

        mb.Entity<InfraAssessmentConnectivity>(e =>
        {
            e.ToTable("infra_assessment_connectivity");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.SiteAId).HasColumnName("site_a_id");
            e.Property(x => x.SiteBId).HasColumnName("site_b_id");
            e.Property(x => x.LinkType).HasColumnName("link_type").HasMaxLength(30);
            e.Property(x => x.BandwidthMbps).HasColumnName("bandwidth_mbps");
            e.Property(x => x.LatencyMs).HasColumnName("latency_ms");
            e.Property(x => x.UptimePct).HasColumnName("uptime_pct");
            e.Property(x => x.CostMonthlyUsd).HasColumnName("cost_monthly_usd");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.SiteA).WithMany().HasForeignKey(x => x.SiteAId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.SiteB).WithMany().HasForeignKey(x => x.SiteBId).OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<InfraAssessmentCapacity>(e =>
        {
            e.ToTable("infra_assessment_capacity");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.DeviceId).HasColumnName("device_id");
            e.Property(x => x.MetricKey).HasColumnName("metric_key").HasMaxLength(100);
            e.Property(x => x.CurrentValue).HasColumnName("current_value");
            e.Property(x => x.PeakValue).HasColumnName("peak_value");
            e.Property(x => x.Threshold).HasColumnName("threshold");
            e.Property(x => x.TrendDirection).HasColumnName("trend_direction").HasMaxLength(20);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId);
        });

        mb.Entity<InfraAssessmentFinding>(e =>
        {
            e.ToTable("infra_assessment_findings");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.Area).HasColumnName("area").HasMaxLength(30);
            e.Property(x => x.Service).HasColumnName("service").HasMaxLength(200);
            e.Property(x => x.Feature).HasColumnName("feature").HasMaxLength(200);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(30);
            e.Property(x => x.Priority).HasColumnName("priority").HasMaxLength(20);
            e.Property(x => x.Observation).HasColumnName("observation");
            e.Property(x => x.Recommendation).HasColumnName("recommendation");
            e.Property(x => x.LinkText).HasColumnName("link_text").HasMaxLength(500);
            e.Property(x => x.LinkUrl).HasColumnName("link_url").HasMaxLength(2000);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // ── IA-1: Hypervisor Inventory ──
        mb.Entity<InfraHypervisorConfig>(e =>
        {
            e.ToTable("infra_hypervisor_configs");
            e.HasKey(x => x.Id);
            e.Property(x => x.OrganizationId).HasColumnName("organization_id");
            e.Property(x => x.Platform).HasColumnName("platform").HasMaxLength(20);
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            e.Property(x => x.HostUrl).HasColumnName("host_url").HasMaxLength(500);
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(200);
            e.Property(x => x.EncryptedPassword).HasColumnName("encrypted_password");
            e.Property(x => x.ApiToken).HasColumnName("api_token");
            e.Property(x => x.VerifySsl).HasColumnName("verify_ssl");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.LastTestedAt).HasColumnName("last_tested_at");
            e.Property(x => x.LastTestOk).HasColumnName("last_test_ok");
            e.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        mb.Entity<InfraHypervisor>(e =>
        {
            e.ToTable("infra_hypervisors");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.ConfigId).HasColumnName("config_id");
            e.Property(x => x.SiteId).HasColumnName("site_id");
            e.Property(x => x.Platform).HasColumnName("platform").HasMaxLength(20);
            e.Property(x => x.HostFqdn).HasColumnName("host_fqdn").HasMaxLength(300);
            e.Property(x => x.Version).HasColumnName("version").HasMaxLength(100);
            e.Property(x => x.ClusterName).HasColumnName("cluster_name").HasMaxLength(200);
            e.Property(x => x.CpuCoresTotal).HasColumnName("cpu_cores_total");
            e.Property(x => x.RamGbTotal).HasColumnName("ram_gb_total");
            e.Property(x => x.StorageGbTotal).HasColumnName("storage_gb_total");
            e.Property(x => x.CpuUsagePct).HasColumnName("cpu_usage_pct");
            e.Property(x => x.RamUsagePct).HasColumnName("ram_usage_pct");
            e.Property(x => x.VmCount).HasColumnName("vm_count");
            e.Property(x => x.VmRunning).HasColumnName("vm_running");
            e.Property(x => x.HaEnabled).HasColumnName("ha_enabled");
            e.Property(x => x.PowerState).HasColumnName("power_state").HasMaxLength(20);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasMany(x => x.Vms).WithOne(v => v.Hypervisor).HasForeignKey(v => v.HypervisorId).OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<InfraVm>(e =>
        {
            e.ToTable("infra_vms");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScanId).HasColumnName("scan_id");
            e.Property(x => x.HypervisorId).HasColumnName("hypervisor_id");
            e.Property(x => x.VmName).HasColumnName("vm_name").HasMaxLength(300);
            e.Property(x => x.Os).HasColumnName("os").HasMaxLength(200);
            e.Property(x => x.PowerState).HasColumnName("power_state").HasMaxLength(20);
            e.Property(x => x.CpuCores).HasColumnName("cpu_cores");
            e.Property(x => x.RamGb).HasColumnName("ram_gb");
            e.Property(x => x.DiskGb).HasColumnName("disk_gb");
            e.Property(x => x.CpuAvgPct).HasColumnName("cpu_avg_pct");
            e.Property(x => x.RamAvgPct).HasColumnName("ram_avg_pct");
            e.Property(x => x.DiskUsedPct).HasColumnName("disk_used_pct");
            e.Property(x => x.SnapshotCount).HasColumnName("snapshot_count");
            e.Property(x => x.OldestSnapshotDays).HasColumnName("oldest_snapshot_days");
            e.Property(x => x.LastBackup).HasColumnName("last_backup");
            e.Property(x => x.LastLogin).HasColumnName("last_login");
            e.Property(x => x.IpAddress).HasColumnName("ip_address").HasMaxLength(50);
            e.Property(x => x.ToolsStatus).HasColumnName("tools_status").HasMaxLength(50);
            e.Property(x => x.IsTemplate).HasColumnName("is_template");
            e.Property(x => x.IsIdle).HasColumnName("is_idle");
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // ── DB-NORM: Normalized child tables ──
        mb.Entity<ControlCheckParam>(e =>
        {
            e.ToTable("control_check_params");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.ControlDef).WithMany(x => x.CheckParams).HasForeignKey(x => x.ControlDefId);
        });

        mb.Entity<MachineLocalAdmin>(e =>
        {
            e.ToTable("machine_local_admins");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
        });

        mb.Entity<MachineLoopStatus>(e =>
        {
            e.ToTable("machine_loop_status");
            e.HasKey(x => new { x.MachineId, x.LoopName });
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
        });

        mb.Entity<OrgPriorityService>(e =>
        {
            e.ToTable("org_priority_services");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        });

        mb.Entity<RemediationLog>(e =>
        {
            e.ToTable("remediation_log");
            e.HasKey(x => x.Id);
        });

        mb.Entity<MachineTracerouteHop>(e =>
        {
            e.ToTable("machine_traceroute_hops");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Diag).WithMany(x => x.TracerouteHops).HasForeignKey(x => x.DiagId);
        });

        mb.Entity<CloudFindingProperty>(e =>
        {
            e.ToTable("cloud_finding_properties");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.AzureResource).WithMany(x => x.Properties).HasForeignKey(x => x.AzureResourceId);
        });

        mb.Entity<CloudResourceRiskFlag>(e =>
        {
            e.ToTable("cloud_resource_risk_flags");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.AzureResource).WithMany(x => x.RiskFlags).HasForeignKey(x => x.AzureResourceId);
        });

        mb.Entity<MailDomainSpfWarning>(e =>
        {
            e.ToTable("mail_domain_spf_warnings");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.MailDomain).WithMany().HasForeignKey(x => x.MailDomainId);
        });

        mb.Entity<MailDomainDkimSelector>(e =>
        {
            e.ToTable("mail_domain_dkim_selectors");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.MailDomain).WithMany().HasForeignKey(x => x.MailDomainId);
        });

        mb.Entity<SharedMailboxDelegate>(e =>
        {
            e.ToTable("shared_mailbox_delegates");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Mailbox).WithMany().HasForeignKey(x => x.MailboxId);
        });

        mb.Entity<AlertPayloadField>(e =>
        {
            e.ToTable("alert_payload_fields");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Alert).WithMany().HasForeignKey(x => x.AlertId);
        });

        mb.Entity<RemediationActionParam>(e =>
        {
            e.ToTable("remediation_action_params");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.RemediationAction).WithMany().HasForeignKey(x => x.RemediationActionId);
        });

        mb.Entity<CveProductMap>(e =>
        {
            e.ToTable("cve_product_map");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.CveEntry).WithMany().HasForeignKey(x => x.CveEntryId);
            e.HasOne(x => x.Software).WithMany().HasForeignKey(x => x.SoftwareId);
        });

        mb.Entity<Software>(e =>
        {
            e.ToTable("software");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        mb.Entity<MachineSoftware>(e =>
        {
            e.ToTable("machine_software");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            e.HasOne(x => x.Software).WithMany().HasForeignKey(x => x.SoftwareId);
        });

        // ── Available Updates (WUC-02) ──
        mb.Entity<MachineAvailableUpdate>(e =>
        {
            e.ToTable("machine_available_updates");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            e.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
            e.HasIndex(x => new { x.MachineId, x.IsPending });
            e.HasIndex(x => x.KbNumber);
            e.HasIndex(x => new { x.OrganizationId, x.IsPending });
        });
    }
}
