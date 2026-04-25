using KryossApi.Data.Entities;
using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class TestFixtureRecipe : ISelfContainedRecipe
{
    public ReportDataNeeds DataNeeds => ReportDataNeeds.None;

    public string ReportTitle(ReportOptions options) => "Kryoss Block Library — Test Fixture";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("test-fixture");

        yield return new SectionHeaderBlock("1. SemaforoBlock");
        yield return new SemaforoBlock();

        yield return new SectionHeaderBlock("2. KpiBlock — Exec");
        yield return new KpiBlock(KpiVariant.Exec);
        yield return new SectionHeaderBlock("2b. KpiBlock — Business");
        yield return new KpiBlock(KpiVariant.Business);
        yield return new SectionHeaderBlock("2c. KpiBlock — Compact");
        yield return new KpiBlock(KpiVariant.Compact);

        yield return new SectionHeaderBlock("3. Top3RiskBlock");
        yield return new Top3RiskBlock();

        yield return new SectionHeaderBlock("4. CategoryBreakdownBlock");
        yield return new CategoryBreakdownBlock();

        yield return new SectionHeaderBlock("5. RiskScoreBlock");
        yield return new RiskScoreBlock();

        yield return new SectionHeaderBlock("6. ThreatVectorsBlock");
        yield return new ThreatVectorsBlock();

        yield return new SectionHeaderBlock("7. AssetMatrixBlock");
        yield return new AssetMatrixBlock();

        yield return new SectionHeaderBlock("8. TopFindingsBlock (10)");
        yield return new TopFindingsBlock(topN: 10);

        yield return new SectionHeaderBlock("9. IronSixBlock");
        yield return new IronSixBlock();

        yield return new SectionHeaderBlock("10. ControlDetailBlock");
        yield return new ControlDetailBlock();

        yield return new SectionHeaderBlock("11. GapAnalysisBlock");
        yield return new GapAnalysisBlock();

        yield return new SectionHeaderBlock("12. RiskRoiBlock");
        yield return new RiskRoiBlock();

        yield return new SectionHeaderBlock("13. DecisionsMatrixBlock");
        yield return new DecisionsMatrixBlock();

        yield return new SectionHeaderBlock("14. EvidenceAppendixBlock");
        yield return new EvidenceAppendixBlock();

        yield return new SectionHeaderBlock("15. FrameworkGaugeBlock");
        yield return new FrameworkGaugeBlock();

        yield return new SectionHeaderBlock("16. FrameworkCoverBlock");
        yield return new FrameworkCoverBlock();

        yield return new SectionHeaderBlock("17. ScoreTrendBlock (no delta)");
        yield return new ScoreTrendBlock(showDelta: false);
        yield return new SectionHeaderBlock("17b. ScoreTrendBlock (with delta)");
        yield return new ScoreTrendBlock(showDelta: true);

        yield return new SectionHeaderBlock("18. MethodologyBlock — Technical");
        yield return new MethodologyBlock(AudiencePerspective.Technical);
        yield return new SectionHeaderBlock("18b. MethodologyBlock — Audit");
        yield return new MethodologyBlock(AudiencePerspective.Audit);

        yield return new SectionHeaderBlock("19. CtaBlock — Simple");
        yield return new CtaBlock(CtaMode.Simple);
        yield return new SectionHeaderBlock("19b. CtaBlock — Stepped");
        yield return new CtaBlock(CtaMode.Stepped);

        yield return new SectionHeaderBlock("20. NextStepBlock");
        yield return new NextStepBlock();

        yield return new SectionHeaderBlock("21. ServiceCatalogBlock (flat)");
        yield return new ServiceCatalogBlock(showPricing: true);
        yield return new SectionHeaderBlock("21b. ServiceCatalogBlock (tier grid)");
        yield return new ServiceCatalogBlock(showPricing: true, tierGrid: true);

        yield return new SectionHeaderBlock("22. TimelineBlock");
        yield return new TimelineBlock();

        yield return new SectionHeaderBlock("23. NetworkBlock — Full");
        yield return new NetworkBlock(NetworkVariant.Full);
        yield return new SectionHeaderBlock("23b. NetworkBlock — Summary");
        yield return new NetworkBlock(NetworkVariant.Summary);
        yield return new SectionHeaderBlock("23c. NetworkBlock — SitesTable");
        yield return new NetworkBlock(NetworkVariant.SitesTable);

        yield return new SectionHeaderBlock("24. NetworkMiniBlock");
        yield return new NetworkMiniBlock();

        yield return new SectionHeaderBlock("25. CloudPostureBlock (compact)");
        yield return new CloudPostureBlock(compact: true);
        yield return new SectionHeaderBlock("25b. CloudPostureBlock (full)");
        yield return new CloudPostureBlock(compact: false);

        yield return new SectionHeaderBlock("26. CloudExecutiveBlock");
        yield return new CloudExecutiveBlock();

        yield return new SectionHeaderBlock("27. ExecOnePagerBlock");
        yield return new ExecOnePagerBlock();
    }

    public ReportData BuildSyntheticData()
    {
        var franchiseId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var franchise = new Franchise
        {
            Id = franchiseId,
            Name = "Acme MSP (Test Fixture)",
            BrandName = "TeamLogic IT",
            BrandColorPrimary = "#008852",
            BrandColorAccent = "#A2C564",
            ContactPhone = "+1 (305) 555-0100",
            ContactEmail = "support@acmemsp.test",
        };

        var brand = new Brand
        {
            Id = 1,
            Code = "TLIT",
            Name = "TeamLogic IT",
            ColorPrimary = "#008852",
            ColorAccent = "#A2C564",
            LogoUrl = LogoData.DataUri,
        };

        var org = new Organization
        {
            Id = orgId,
            FranchiseId = franchiseId,
            Franchise = franchise,
            Brand = brand,
            BrandId = 1,
            Name = "Contoso Financial Services",
            LegalName = "Contoso Financial Services LLC",
            Status = "current",
            IndustryCode = "52",
            EmployeeCountBand = "51-200",
            CreatedAt = now.AddMonths(-6),
        };

        var machines = BuildMachines(orgId, now);
        var runs = BuildRuns(orgId, machines, now);
        var controlResults = BuildControlResults(runs);

        return new ReportData
        {
            Org = org,
            Branding = new ReportBranding
            {
                CompanyName = "TeamLogic IT",
                PrimaryColor = "#008852",
                AccentColor = "#A2C564",
                LogoUrl = LogoData.DataUri,
            },
            UserInfo = new ReportUserInfo
            {
                FullName = "Federico Herrera",
                Email = "fherrera@teamlogicit.test",
                Phone = "+1 (305) 555-0199",
                JobTitle = "vCISO / Security Consultant",
                CompanyName = "TeamLogic IT",
            },
            Runs = runs,
            ControlResults = controlResults,
            FrameworkScores = BuildFrameworkScores(),
            Hygiene = BuildHygiene(now),
            Enrichment = BuildEnrichment(machines),
            PreviousMonthScore = 62.3m,
            ScoreHistory = BuildScoreHistory(now),
            CloudScan = BuildCloudScan(orgId, now),
            CloudFindings = BuildCloudFindings(now),
            AreaScores = new Dictionary<string, decimal>
            {
                ["identity"] = 78, ["endpoint"] = 65, ["data"] = 72, ["productivity"] = 81, ["azure"] = 58
            },
            CloudFrameworkScores = BuildCloudFrameworkScores(now),
            M365Connected = true,
            M365Findings = BuildM365Findings(now),
            SavedCtas = BuildCtas(orgId, now),
            ServiceCatalog = BuildServiceCatalog(),
            Rate = new FranchiseServiceRate
            {
                Id = 1, FranchiseId = franchiseId, HourlyRate = 175m, Currency = "USD",
                MarginPct = 30m, EffectiveFrom = now.AddMonths(-3),
            },
            NetworkDiags = BuildNetworkDiags(machines, now),
            Benchmarks = new BenchmarkData
            {
                FranchisePeers = new() { ["identity"] = 72, ["endpoint"] = 68, ["data"] = 65, ["productivity"] = 74, ["azure"] = 51 },
                IndustryBaseline = new() { ["identity"] = 75, ["endpoint"] = 70, ["data"] = 68, ["productivity"] = 76, ["azure"] = 55 },
                GlobalKryoss = new() { ["identity"] = 71, ["endpoint"] = 66, ["data"] = 64, ["productivity"] = 73, ["azure"] = 49 },
            },
        };
    }

    // ── Machine fleet ────────────────────────────────────────────────────
    private static List<Machine> BuildMachines(Guid orgId, DateTime now)
    {
        var hostnames = new[] { "WS-FIN-001", "WS-FIN-002", "WS-HR-003", "SRV-DC01", "SRV-FILE02" };
        var osNames = new[] { "Windows 11 Pro", "Windows 11 Enterprise", "Windows 10 Pro", "Windows Server 2022 Standard", "Windows Server 2022 Standard" };
        var cpus = new[] { "Intel Core i7-13700", "Intel Core i5-12400", "AMD Ryzen 5 5600X", "Intel Xeon E-2378", "Intel Xeon E-2334" };
        short[] rams = [16, 8, 16, 64, 32];
        var models = new[] { "OptiPlex 7090", "ThinkCentre M90q", "ProDesk 400 G9", "PowerEdge R450", "PowerEdge T350" };

        return Enumerable.Range(0, 5).Select(i =>
        {
            var id = Guid.NewGuid();
            return new Machine
            {
                Id = id,
                OrganizationId = orgId,
                AgentId = Guid.NewGuid(),
                Hostname = hostnames[i],
                OsName = osNames[i],
                OsVersion = "10.0.22631",
                Manufacturer = i < 3 ? "Dell" : "Dell",
                Model = models[i],
                CpuName = cpus[i],
                CpuCores = (short)(i < 3 ? 8 : 16),
                RamGb = rams[i],
                TpmPresent = true,
                TpmVersion = "2.0",
                SecureBoot = i != 2,
                Bitlocker = i < 3,
                IpAddress = $"192.168.1.{10 + i}",
                DomainStatus = "DomainJoined",
                DomainName = "contoso.local",
                AgentVersion = "1.6.4",
                IsActive = true,
                FirstSeenAt = now.AddDays(-90 + i * 5),
                LastSeenAt = now.AddHours(-i * 2),
                ProductType = (short)(i < 3 ? 1 : i == 3 ? 2 : 3),
                CreatedAt = now.AddDays(-90),
            };
        }).ToList();
    }

    // ── Runs (one per machine) ───────────────────────────────────────────
    private static List<AssessmentRun> BuildRuns(Guid orgId, List<Machine> machines, DateTime now)
    {
        decimal[] scores = [78.5m, 65.2m, 82.1m, 71.4m, 59.8m];
        short[] passes = [412, 340, 445, 370, 310];
        short[] fails = [115, 187, 82, 157, 217];

        return machines.Select((m, i) => new AssessmentRun
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            MachineId = m.Id,
            Machine = m,
            GlobalScore = scores[i],
            Grade = ReportHelpers.GetGrade(scores[i]),
            PassCount = passes[i],
            FailCount = fails[i],
            WarnCount = (short)(527 - passes[i] - fails[i]),
            TotalPoints = 527,
            EarnedPoints = (short)(scores[i] * 5.27m),
            AgentVersion = "1.6.4",
            StartedAt = now.AddHours(-3),
            CompletedAt = now.AddHours(-2),
            DurationMs = 45000 + i * 5000,
        }).ToList();
    }

    // ── Control results (varied across categories) ───────────────────────
    private static List<OrgControlResult> BuildControlResults(List<AssessmentRun> runs)
    {
        var categories = new[]
        {
            "Account Policies", "Local Policies", "Security Options", "Audit Policies",
            "Windows Firewall", "User Rights Assignment", "Network Security",
            "System Services", "Administrative Templates", "BitLocker Drive Encryption",
            "Windows Defender", "Credential Guard", "Event Log Settings", "Registry Permissions",
        };
        var severities = new[] { "critical", "high", "medium", "low" };
        var statuses = new[] { "pass", "pass", "fail", "pass", "warn", "fail", "pass" };

        var results = new List<OrgControlResult>();
        int controlNum = 1;

        foreach (var run in runs)
        {
            for (int c = 0; c < categories.Length; c++)
            {
                int checksPerCat = 8 + c % 5;
                for (int j = 0; j < checksPerCat; j++)
                {
                    var status = statuses[(controlNum + j) % statuses.Length];
                    var severity = severities[(c + j) % severities.Length];
                    results.Add(new OrgControlResult
                    {
                        ControlDefId = controlNum,
                        RunId = run.Id,
                        ControlId = $"BL-{controlNum:D4}",
                        Name = $"Ensure {categories[c]} policy {j + 1} is configured",
                        Category = categories[c],
                        Severity = severity,
                        Status = status,
                        Finding = status == "fail" ? $"Current value does not meet baseline requirement for {categories[c]} item {j + 1}" : null,
                        Remediation = status == "fail" ? $"Configure Group Policy: Computer Configuration > {categories[c]} > Setting {j + 1}" : null,
                    });
                    controlNum++;
                }
            }
        }

        // Inject specific controls that ThreatVectorsBlock detects
        var triggerControls = new[]
        {
            ("WDigest Authentication", "Credential Guard", "critical"),
            ("LSA Protection (RunAsPPL)", "Credential Guard", "critical"),
            ("Clear Text Passwords in Memory", "Security Options", "critical"),
            ("SMBv1 Client Driver", "Network Security", "high"),
            ("SMBv1 Server", "Network Security", "high"),
        };
        foreach (var run in runs)
        {
            foreach (var (name, cat, sev) in triggerControls)
            {
                controlNum++;
                results.Add(new OrgControlResult
                {
                    ControlDefId = controlNum,
                    RunId = run.Id,
                    ControlId = $"BL-{controlNum:D4}",
                    Name = $"Ensure {name} is configured correctly",
                    Category = cat,
                    Severity = sev,
                    Status = "fail",
                    Finding = $"{name} is not configured to the recommended baseline",
                    Remediation = $"Enable {name} via Group Policy or registry",
                });
            }
        }

        return results;
    }

    // ── Framework scores ─────────────────────────────────────────────────
    private static List<FrameworkScoreDto> BuildFrameworkScores() =>
    [
        new() { Code = "NIST", Name = "NIST Cybersecurity Framework", Score = 73.2, PassCount = 498, FailCount = 182 },
        new() { Code = "CIS", Name = "CIS Controls v8", Score = 71.8, PassCount = 485, FailCount = 195 },
        new() { Code = "HIPAA", Name = "HIPAA Security Rule", Score = 68.5, PassCount = 214, FailCount = 98 },
        new() { Code = "ISO27001", Name = "ISO/IEC 27001:2022", Score = 76.1, PassCount = 152, FailCount = 48 },
        new() { Code = "PCI-DSS", Name = "PCI DSS v4.0", Score = 59.3, PassCount = 16, FailCount = 11 },
    ];

    // ── AD Hygiene ───────────────────────────────────────────────────────
    private static HygieneScanDto BuildHygiene(DateTime now) => new()
    {
        ScannedAt = now.AddHours(-2),
        TotalMachines = 47,
        TotalUsers = 128,
        StaleMachines = 5,
        DormantMachines = 3,
        StaleUsers = 12,
        DormantUsers = 8,
        DisabledUsers = 15,
        PwdNeverExpire = 9,
        Findings =
        [
            new() { Id = 1, Name = "admin.old", ObjectType = "User", Status = "Stale", DaysInactive = 180, Detail = "Last logon 6 months ago, Domain Admin" },
            new() { Id = 2, Name = "svc_backup", ObjectType = "User", Status = "PwdNeverExpires", DaysInactive = 0, Detail = "Service account, password never expires" },
            new() { Id = 3, Name = "PC-LOBBY-01", ObjectType = "Computer", Status = "Dormant", DaysInactive = 120, Detail = "No logon in 120 days" },
            new() { Id = 4, Name = "john.smith", ObjectType = "User", Status = "Disabled", DaysInactive = 45, Detail = "Terminated employee, still in Admins group" },
            new() { Id = 5, Name = "svc_sql", ObjectType = "User", Status = "Kerberoastable", DaysInactive = 0, Detail = "SPN: MSSQLSvc/sql01.contoso.local:1433" },
            new() { Id = 6, Name = "svc_iis", ObjectType = "User", Status = "Kerberoastable", DaysInactive = 0, Detail = "SPN: HTTP/web01.contoso.local" },
            new() { Id = 7, Name = "svc_exchange", ObjectType = "User", Status = "Kerberoastable", DaysInactive = 0, Detail = "SPN: exchangeMDB/ex01.contoso.local" },
            new() { Id = 8, Name = "SRV-OLD-01", ObjectType = "Computer", Status = "Stale", DaysInactive = 200, Detail = "Server 2012 R2, decommissioned but still in AD" },
            new() { Id = 9, Name = "contoso.local", ObjectType = "Domain", Status = "DomainLevel", DaysInactive = 0, Detail = "Windows Server 2008 R2" },
            ..Enumerable.Range(10, 15).Select(i => new AdHygieneFinding
            {
                Id = i, Name = $"PC-DEPT-{i:D2}", ObjectType = "Computer", Status = "NoLAPS", DaysInactive = 0,
                Detail = "LAPS not deployed"
            }),
        ],
    };

    // ── Enrichment (disks, ports, threats) ────────────────────────────────
    private static OrgEnrichment BuildEnrichment(List<Machine> machines)
    {
        var m0 = machines[0].Id;
        var m1 = machines[1].Id;
        return new OrgEnrichment
        {
            Disks =
            [
                new() { Id = 1, MachineId = m0, DriveLetter = "C", Label = "OS", DiskType = "SSD", TotalGb = 512, FreeGb = 142.3m, FileSystem = "NTFS", UpdatedAt = DateTime.UtcNow },
                new() { Id = 2, MachineId = m0, DriveLetter = "D", Label = "Data", DiskType = "HDD", TotalGb = 1024, FreeGb = 687.1m, FileSystem = "NTFS", UpdatedAt = DateTime.UtcNow },
                new() { Id = 3, MachineId = m1, DriveLetter = "C", Label = "System", DiskType = "SSD", TotalGb = 256, FreeGb = 31.4m, FileSystem = "NTFS", UpdatedAt = DateTime.UtcNow },
            ],
            Ports =
            [
                new() { Id = 1, MachineId = m0, Port = 22, Protocol = "TCP", Status = "open", Service = "SSH", Risk = "medium", ScannedAt = DateTime.UtcNow },
                new() { Id = 2, MachineId = m0, Port = 80, Protocol = "TCP", Status = "open", Service = "HTTP", Risk = "high", ScannedAt = DateTime.UtcNow },
                new() { Id = 3, MachineId = m0, Port = 443, Protocol = "TCP", Status = "open", Service = "HTTPS", Risk = "low", ScannedAt = DateTime.UtcNow },
                new() { Id = 4, MachineId = m0, Port = 3389, Protocol = "TCP", Status = "open", Service = "RDP", Risk = "critical", ScannedAt = DateTime.UtcNow },
                new() { Id = 5, MachineId = m1, Port = 445, Protocol = "TCP", Status = "open", Service = "SMB", Risk = "high", ScannedAt = DateTime.UtcNow },
            ],
            Threats =
            [
                new() { Id = 1, MachineId = m0, ThreatName = "Suspicious PowerShell execution", Category = "Execution", Severity = "high", Vector = "Local", DetectedAt = DateTime.UtcNow.AddDays(-2) },
                new() { Id = 2, MachineId = m1, ThreatName = "Outdated TLS 1.0 enabled", Category = "Configuration", Severity = "medium", Vector = "Network", DetectedAt = DateTime.UtcNow.AddDays(-5) },
                new() { Id = 3, MachineId = m0, ThreatName = "Unpatched critical CVE-2024-38063", Category = "Vulnerability", Severity = "critical", Vector = "Remote", DetectedAt = DateTime.UtcNow.AddDays(-1) },
            ],
        };
    }

    // ── Score history (6 months) ─────────────────────────────────────────
    private static List<MonthlyScore> BuildScoreHistory(DateTime now) =>
        Enumerable.Range(0, 6).Select(i => new MonthlyScore
        {
            Month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-5 + i),
            Score = 55m + i * 4.2m + (i % 2 == 0 ? 1.5m : -0.8m),
        }).ToList();

    // ── Cloud Assessment ─────────────────────────────────────────────────
    private static CloudAssessmentScan BuildCloudScan(Guid orgId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = orgId,
        Status = "completed",
        OverallScore = 71.5m,
        Verdict = "needs_improvement",
        AreaScores = """{"identity":78,"endpoint":65,"data":72,"productivity":81,"azure":58}""",
        StartedAt = now.AddHours(-1),
        CompletedAt = now.AddMinutes(-45),
        CreatedAt = now.AddHours(-1),
        CopilotD1Score = 82, CopilotD2Score = 68, CopilotD3Score = 75,
        CopilotD4Score = 71, CopilotD5Score = 64, CopilotD6Score = 59,
        CopilotOverall = 70, CopilotVerdict = "moderate",
    };

    private static List<CloudAssessmentFinding> BuildCloudFindings(DateTime now)
    {
        var areas = new[] { "identity", "endpoint", "data", "productivity", "azure" };
        var services = new[] { "Conditional Access", "MFA", "Intune", "SharePoint", "Azure Storage" };
        var features = new[] { "Legacy Auth Block", "Admin MFA", "Compliance Policy", "External Sharing", "Public Blob" };
        var statuses = new[] { "fail", "pass", "fail", "warn", "fail", "pass", "pass", "fail" };
        var priorities = new[] { "critical", "high", "medium", "low" };

        return Enumerable.Range(0, 15).Select(i => new CloudAssessmentFinding
        {
            Id = i + 1,
            ScanId = Guid.NewGuid(),
            Area = areas[i % areas.Length],
            Service = services[i % services.Length],
            Feature = $"{features[i % features.Length]} — check {i + 1}",
            Status = statuses[i % statuses.Length],
            Priority = priorities[i % priorities.Length],
            Observation = $"Test observation for cloud finding #{i + 1}",
            Recommendation = statuses[i % statuses.Length] == "fail" ? $"Remediation step for finding #{i + 1}" : null,
            CreatedAt = now,
        }).ToList();
    }

    private static List<CloudAssessmentFrameworkScore> BuildCloudFrameworkScores(DateTime now) =>
    [
        new() { Id = Guid.NewGuid(), FrameworkId = Guid.NewGuid(), TotalControls = 45, CoveredControls = 38, PassingControls = 28, FailingControls = 10, UnmappedControls = 7, ScorePct = 73.7m, Grade = "C", ComputedAt = now },
        new() { Id = Guid.NewGuid(), FrameworkId = Guid.NewGuid(), TotalControls = 30, CoveredControls = 25, PassingControls = 19, FailingControls = 6, UnmappedControls = 5, ScorePct = 76.0m, Grade = "C+", ComputedAt = now },
    ];

    // ── M365 findings ────────────────────────────────────────────────────
    private static List<M365Finding> BuildM365Findings(DateTime now)
    {
        var categories = new[] { "Authentication", "Conditional Access", "Device Management", "Data Protection", "Email Security", "Admin Governance" };
        var severities = new[] { "critical", "high", "medium", "low" };
        var statuses = new[] { "fail", "pass", "fail", "pass", "warn", "pass", "fail", "pass", "pass", "fail" };

        return Enumerable.Range(1, 20).Select(i => new M365Finding
        {
            Id = i,
            TenantId = Guid.NewGuid(),
            CheckId = $"M365-{i:D3}",
            Name = $"M365 Security Check {i} — {categories[(i - 1) % categories.Length]}",
            Category = categories[(i - 1) % categories.Length],
            Severity = severities[(i - 1) % severities.Length],
            Status = statuses[(i - 1) % statuses.Length],
            Finding = statuses[(i - 1) % statuses.Length] == "fail" ? $"Finding detail for M365-{i:D3}" : null,
            ScannedAt = now,
        }).ToList();
    }

    // ── Executive CTAs ───────────────────────────────────────────────────
    private static List<ExecutiveCta> BuildCtas(Guid orgId, DateTime now) =>
    [
        new() { Id = Guid.NewGuid(), OrganizationId = orgId, PeriodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), PriorityCategory = "Incidentes", Title = "3 Critical Vulnerabilities Unpatched", Description = "CVE-2024-38063 (TCP/IP RCE), CVE-2024-43461 (MSHTML), CVE-2024-38178 (Scripting Engine) remain unpatched across 4 machines.", AutoDetectedRule = "critical_vuln_count > 0", CreatedAt = now },
        new() { Id = Guid.NewGuid(), OrganizationId = orgId, PeriodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), PriorityCategory = "Hardening", Title = "RDP Exposed on 2 Workstations", Description = "Port 3389 is open on WS-FIN-001 and WS-HR-003 without Network Level Authentication. Recommend VPN-only access.", AutoDetectedRule = "rdp_exposed > 0", CreatedAt = now },
        new() { Id = Guid.NewGuid(), OrganizationId = orgId, PeriodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), PriorityCategory = "Risk", Title = "Domain Admin with Stale Password", Description = "Account admin.old has not logged in for 180 days but retains Domain Admin privileges. Immediate review required.", AutoDetectedRule = "stale_admin", CreatedAt = now },
        new() { Id = Guid.NewGuid(), OrganizationId = orgId, PeriodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), PriorityCategory = "Budget", Title = "12 Wasted M365 Licenses ($4,320/yr)", Description = "12 Business Premium licenses assigned to inactive accounts. Reclaiming saves $360/month.", AutoDetectedRule = "wasted_licenses_value > 1000", CreatedAt = now },
    ];

    // ── Service catalog ──────────────────────────────────────────────────
    private static List<ServiceCatalogItem> BuildServiceCatalog() =>
    [
        new() { Id = 1, CategoryCode = "disk_encryption", NameEn = "BitLocker Encryption Deployment", NameEs = "Despliegue de cifrado BitLocker", UnitType = "per_machine", BaseHours = 1.5m, TriggerSource = "control_fail", Severity = "critical", SortOrder = 1 },
        new() { Id = 2, CategoryCode = "laps_deploy", NameEn = "LAPS Deployment", NameEs = "Despliegue de LAPS", UnitType = "per_machine", BaseHours = 0.5m, TriggerSource = "control_fail", Severity = "high", SortOrder = 2 },
        new() { Id = 3, CategoryCode = "firewall_hardening", NameEn = "Firewall Rule Remediation", NameEs = "Remediación de reglas de firewall", UnitType = "per_machine", BaseHours = 1m, TriggerSource = "control_fail", Severity = "high", SortOrder = 3 },
        new() { Id = 4, CategoryCode = "rdp_hardening", NameEn = "RDP Hardening & NLA Enforcement", NameEs = "Hardening RDP y NLA", UnitType = "per_machine", BaseHours = 1m, TriggerSource = "control_fail", Severity = "critical", SortOrder = 4 },
        new() { Id = 5, CategoryCode = "password_policy", NameEn = "Password Policy Remediation", NameEs = "Remediación de política de contraseñas", UnitType = "flat", BaseHours = 4m, TriggerSource = "control_fail", Severity = "medium", SortOrder = 5 },
        new() { Id = 6, CategoryCode = "m365_security", NameEn = "M365 Security Hardening", NameEs = "Hardening de seguridad M365", UnitType = "flat", BaseHours = 8m, TriggerSource = "m365_fail", Severity = "high", SortOrder = 6 },
        new() { Id = 7, CategoryCode = "audit_logging", NameEn = "Audit Logging Configuration", NameEs = "Configuración de logging de auditoría", UnitType = "flat", BaseHours = 3m, TriggerSource = "control_fail", Severity = "medium", SortOrder = 7 },
        new() { Id = 8, CategoryCode = "ad_restructuring", NameEn = "Active Directory Restructuring", NameEs = "Reestructuración de Active Directory", UnitType = "flat", BaseHours = 12m, TriggerSource = "hygiene_finding", Severity = "medium", SortOrder = 8 },
    ];

    // ── Network diagnostics ──────────────────────────────────────────────
    private static List<MachineNetworkDiag> BuildNetworkDiags(List<Machine> machines, DateTime now) =>
        machines.Take(3).Select((m, i) => new MachineNetworkDiag
        {
            Id = i + 1,
            MachineId = m.Id,
            Machine = m,
            DownloadMbps = 95.2m + i * 15,
            UploadMbps = 42.1m + i * 8,
            InternetLatencyMs = 12.5m + i * 3,
            GatewayLatencyMs = 1.2m + i * 0.3m,
            GatewayIp = "192.168.1.1",
            RouteCount = 12 + i,
            VpnDetected = i == 2,
            AdapterCount = 2 + i,
            WifiCount = 1,
            EthCount = 1,
            VpnAdapterCount = i == 2 ? 1 : 0,
            BandwidthSendMbps = 850m + i * 50,
            BandwidthRecvMbps = 920m + i * 30,
            DnsResolutionMs = 8.3m + i * 2,
            CloudEndpointCount = 6,
            CloudEndpointAvgMs = 25.4m + i * 5,
            ScannedAt = now.AddHours(-2),
            LatencyPeers =
            [
                new() { Id = i * 3 + 1, Host = "192.168.1.1", Reachable = true, AvgMs = 1.2m, MinMs = 1, MaxMs = 3, PacketLoss = 0, TotalSent = 10 },
                new() { Id = i * 3 + 2, Host = "192.168.1.100", Reachable = true, AvgMs = 2.5m, MinMs = 1, MaxMs = 8, PacketLoss = 0, TotalSent = 10 },
                new() { Id = i * 3 + 3, Host = "8.8.8.8", Reachable = true, AvgMs = 12.3m, MinMs = 10, MaxMs = 18, PacketLoss = 0, TotalSent = 10 },
            ],
            Routes =
            [
                new() { Id = i * 2 + 1, Destination = "0.0.0.0", Mask = "0.0.0.0", NextHop = "192.168.1.1", Metric = 25 },
                new() { Id = i * 2 + 2, Destination = "192.168.1.0", Mask = "255.255.255.0", NextHop = "0.0.0.0", Metric = 281 },
            ],
        }).ToList();
}
