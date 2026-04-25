using System.Diagnostics;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services.Reports;

public interface IReportDataLoader
{
    Task<ReportData> LoadAsync(Guid orgId, ReportOptions options, ReportDataNeeds needs = ReportDataNeeds.All);
    Task<(ReportData Data, Dictionary<string, long> QueryTimings)> LoadWithTimingsAsync(Guid orgId, ReportOptions options, ReportDataNeeds needs = ReportDataNeeds.All);
}

public class ReportDataLoader : IReportDataLoader
{
    private readonly KryossDbContext _db;
    private readonly IDbContextFactory<KryossDbContext> _dbFactory;
    private readonly ICurrentUserService _currentUser;

    public ReportDataLoader(KryossDbContext db, IDbContextFactory<KryossDbContext> dbFactory, ICurrentUserService currentUser)
    {
        _db = db;
        _dbFactory = dbFactory;
        _currentUser = currentUser;
    }

    public async Task<ReportData> LoadAsync(Guid orgId, ReportOptions options, ReportDataNeeds needs)
    {
        var (data, _) = await LoadCoreAsync(orgId, options, needs, trackTimings: false);
        return data;
    }

    public async Task<(ReportData Data, Dictionary<string, long> QueryTimings)> LoadWithTimingsAsync(
        Guid orgId, ReportOptions options, ReportDataNeeds needs)
    {
        return await LoadCoreAsync(orgId, options, needs, trackTimings: true);
    }

    private bool Has(ReportDataNeeds needs, ReportDataNeeds flag) => (needs & flag) != 0;

    private static async Task<T> SafeQuery<T>(Task<T> task, T fallback)
    {
        try { return await task; }
        catch { return fallback; }
    }

    private async Task<(ReportData, Dictionary<string, long>)> LoadCoreAsync(
        Guid orgId, ReportOptions options, ReportDataNeeds needs, bool trackTimings)
    {
        var timings = new Dictionary<string, long>();
        var sw = trackTimings ? new Stopwatch() : null;

        void StartTimer() { sw?.Restart(); }
        void RecordTimer(string name) { if (sw != null) timings[name] = sw.ElapsedMilliseconds; }

        // ── Phase 1: Org + branding (scoped context, has RLS) ────────────
        StartTimer();
        var org = await _db.Organizations
            .AsNoTracking()
            .Include(o => o.Franchise)
            .Include(o => o.Brand)
            .FirstOrDefaultAsync(o => o.Id == orgId)
            ?? throw new InvalidOperationException($"Organization {orgId} not found");
        RecordTimer("org");

        var brand = org.Brand;
        var franchise = org.Franchise;
        var branding = new ReportBranding
        {
            CompanyName = brand?.Name ?? franchise.BrandName ?? franchise.Name,
            PrimaryColor = brand?.ColorPrimary ?? franchise.BrandColorPrimary ?? "#006536",
            AccentColor = brand?.ColorAccent ?? franchise.BrandColorAccent ?? "#A2C564",
            LogoUrl = brand?.LogoUrl ?? franchise.BrandLogoUrl ?? LogoData.DataUri
        };

        // ── Phase 2: User info + latest run IDs (scoped context) ─────────
        StartTimer();
        var userInfo = await BuildUserInfoAsync(org);

        var latestRunIds = new List<Guid>();
        var runs = new List<AssessmentRun>();

        if (Has(needs, ReportDataNeeds.Runs))
        {
            latestRunIds = await _db.AssessmentRuns
                .AsNoTracking()
                .Where(r => r.OrganizationId == orgId && r.CompletedAt != null)
                .GroupBy(r => r.MachineId)
                .Select(g => g.OrderByDescending(r => r.CompletedAt).First().Id)
                .ToListAsync();

            if (latestRunIds.Count > 0)
            {
                runs = await _db.AssessmentRuns
                    .AsNoTracking()
                    .Include(r => r.Machine)
                    .Where(r => latestRunIds.Contains(r.Id))
                    .OrderBy(r => r.Machine.Hostname)
                    .ToListAsync();
            }
        }
        RecordTimer("runs+user");

        var machineIds = runs.Select(r => r.MachineId).ToList();

        // ── Phase 3: Parallel queries (each gets its own DbContext) ──────
        StartTimer();

        var tasks = new List<Task>();

        var controlResultsTask = SafeQuery(
            Has(needs, ReportDataNeeds.ControlResults) && latestRunIds.Count > 0
                ? RunWithFactory(db => LoadControlResultsAsync(db, latestRunIds, options))
                : Task.FromResult(new List<OrgControlResult>()),
            new List<OrgControlResult>());
        tasks.Add(controlResultsTask);

        var frameworkScoresTask = SafeQuery(
            Has(needs, ReportDataNeeds.FrameworkScores) && latestRunIds.Count > 0
                ? RunWithFactory(db => LoadFrameworkScoresAsync(db, latestRunIds))
                : Task.FromResult(new List<FrameworkScoreDto>()),
            new List<FrameworkScoreDto>());
        tasks.Add(frameworkScoresTask);

        var enrichmentTask = SafeQuery(
            Has(needs, ReportDataNeeds.Enrichment) && machineIds.Count > 0
                ? RunWithFactory(db => LoadEnrichmentAsync(db, machineIds))
                : Task.FromResult(new OrgEnrichment()),
            new OrgEnrichment());
        tasks.Add(enrichmentTask);

        var prevScoreTask = SafeQuery(
            Has(needs, ReportDataNeeds.PreviousScore)
                ? RunWithFactory(db => LoadPreviousScoreAsync(db, orgId))
                : Task.FromResult<decimal?>(null),
            (decimal?)null);
        tasks.Add(prevScoreTask);

        var scoreHistoryTask = SafeQuery(
            Has(needs, ReportDataNeeds.ScoreHistory)
                ? RunWithFactory(db => LoadScoreHistoryAsync(db, orgId))
                : Task.FromResult<List<MonthlyScore>?>(null),
            (List<MonthlyScore>?)null);
        tasks.Add(scoreHistoryTask);

        var hygieneTask = SafeQuery(
            Has(needs, ReportDataNeeds.Hygiene)
                ? RunWithFactory(db => LoadHygieneAsync(db, orgId))
                : Task.FromResult<HygieneScanDto?>(null),
            (HygieneScanDto?)null);
        tasks.Add(hygieneTask);

        var m365Task = SafeQuery(
            Has(needs, ReportDataNeeds.M365)
                ? RunWithFactory(db => LoadM365Async(db, orgId))
                : Task.FromResult<(bool Connected, List<M365Finding> Findings)>((false, new())),
            (false, new List<M365Finding>()));
        tasks.Add(m365Task);

        var cloudTask = SafeQuery(
            Has(needs, ReportDataNeeds.Cloud)
                ? RunWithFactory(db => LoadCloudAsync(db, orgId))
                : Task.FromResult<CloudData>(new()),
            new CloudData());
        tasks.Add(cloudTask);

        var networkTask = SafeQuery(
            Has(needs, ReportDataNeeds.Network) && machineIds.Count > 0
                ? RunWithFactory(db => LoadNetworkAsync(db, machineIds))
                : Task.FromResult(new List<MachineNetworkDiag>()),
            new List<MachineNetworkDiag>());
        tasks.Add(networkTask);

        var ctasTask = SafeQuery(
            Has(needs, ReportDataNeeds.Ctas)
                ? RunWithFactory(db => LoadCtasAsync(db, orgId))
                : Task.FromResult(new List<ExecutiveCta>()),
            new List<ExecutiveCta>());
        tasks.Add(ctasTask);

        var serviceCatalogTask = SafeQuery(
            Has(needs, ReportDataNeeds.ServiceCatalog)
                ? RunWithFactory(db => LoadServiceCatalogAsync(db, franchise?.Id))
                : Task.FromResult<(List<ServiceCatalogItem>, FranchiseServiceRate?)>((new(), null)),
            (new List<ServiceCatalogItem>(), (FranchiseServiceRate?)null));
        tasks.Add(serviceCatalogTask);

        await Task.WhenAll(tasks);
        RecordTimer("parallel_queries");

        var allResults = await controlResultsTask;
        var frameworkScores = await frameworkScoresTask;
        var enrichment = await enrichmentTask;
        var previousMonthScore = await prevScoreTask;
        var scoreHistory = await scoreHistoryTask;
        var hygieneScan = await hygieneTask;
        var (m365Connected, m365Findings) = await m365Task;
        var cloud = await cloudTask;
        var networkDiags = await networkTask;
        var savedCtas = await ctasTask;
        var (serviceCatalog, rate) = await serviceCatalogTask;

        var data = new ReportData
        {
            Org = org,
            Branding = branding,
            UserInfo = userInfo,
            Runs = runs,
            ControlResults = allResults,
            FrameworkScores = frameworkScores,
            Hygiene = hygieneScan,
            Enrichment = enrichment,
            PreviousMonthScore = previousMonthScore,
            ScoreHistory = scoreHistory,
            M365Connected = m365Connected,
            M365Findings = m365Findings,
            CloudScan = cloud.Scan,
            CloudFindings = cloud.Findings,
            AreaScores = cloud.AreaScores,
            CloudFrameworkScores = cloud.FrameworkScores,
            SavedCtas = savedCtas,
            ServiceCatalog = serviceCatalog,
            Rate = rate,
            NetworkDiags = networkDiags
        };

        return (data, timings);
    }

    // Creates a fresh DbContext, runs the query, disposes the context.
    private async Task<T> RunWithFactory<T>(Func<KryossDbContext, Task<T>> query)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await query(db);
    }

    // ── Individual query methods (each receives its own DbContext) ────────

    private static async Task<List<OrgControlResult>> LoadControlResultsAsync(
        KryossDbContext db, List<Guid> latestRunIds, ReportOptions options)
    {
        var results = await db.ControlResults
            .AsNoTracking()
            .Where(cr => latestRunIds.Contains(cr.RunId))
            .Join(db.ControlDefs.AsNoTracking().Include(cd => cd.Category), cr => cr.ControlDefId, cd => cd.Id,
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

        if (!string.IsNullOrEmpty(options.FrameworkCode))
        {
            var framework = await db.Frameworks
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Code == options.FrameworkCode && f.IsActive);

            if (framework != null)
            {
                var controlDefIdsInFramework = await db.ControlFrameworks
                    .AsNoTracking()
                    .Where(cf => cf.FrameworkId == framework.Id)
                    .Select(cf => cf.ControlDefId)
                    .ToListAsync();
                var controlDefIdsSet = new HashSet<int>(controlDefIdsInFramework);
                results = results.Where(r => controlDefIdsSet.Contains(r.ControlDefId)).ToList();
            }
        }

        return results;
    }

    private static async Task<List<FrameworkScoreDto>> LoadFrameworkScoresAsync(
        KryossDbContext db, List<Guid> latestRunIds)
    {
        return await db.RunFrameworkScores
            .AsNoTracking()
            .Where(fs => latestRunIds.Contains(fs.RunId))
            .GroupBy(fs => fs.FrameworkId)
            .Select(g => new
            {
                frameworkId = g.Key,
                avgScore = Math.Round(g.Average(fs => (double)fs.Score), 1),
                totalPass = g.Sum(fs => (int)fs.PassCount),
                totalFail = g.Sum(fs => (int)fs.FailCount),
            })
            .Join(db.Frameworks.AsNoTracking(), x => x.frameworkId, fw => fw.Id,
                (x, fw) => new FrameworkScoreDto
                {
                    Code = fw.Code,
                    Name = fw.Name,
                    Score = x.avgScore,
                    PassCount = (short)x.totalPass,
                    FailCount = (short)x.totalFail
                })
            .OrderBy(x => x.Code)
            .ToListAsync();
    }

    private static async Task<OrgEnrichment> LoadEnrichmentAsync(KryossDbContext db, List<Guid> machineIds)
    {
        var disks = await db.MachineDisks.AsNoTracking()
            .Where(d => machineIds.Contains(d.MachineId)).OrderBy(d => d.DriveLetter).ToListAsync();
        var ports = await db.MachinePorts.AsNoTracking()
            .Where(p => machineIds.Contains(p.MachineId)).OrderBy(p => p.Port).ToListAsync();
        var threats = await db.MachineThreats.AsNoTracking()
            .Where(t => machineIds.Contains(t.MachineId)).OrderByDescending(t => t.DetectedAt).ToListAsync();

        return new OrgEnrichment { Disks = disks, Ports = ports, Threats = threats };
    }

    private static async Task<decimal?> LoadPreviousScoreAsync(KryossDbContext db, Guid orgId)
    {
        var periodEnd = DateTime.UtcNow.AddDays(-30);
        var periodStart30 = DateTime.UtcNow.AddDays(-60);
        var prevScores = await db.AssessmentRuns
            .AsNoTracking()
            .Where(r => r.OrganizationId == orgId
                        && r.CompletedAt != null
                        && r.CompletedAt >= periodStart30
                        && r.CompletedAt < periodEnd
                        && r.GlobalScore != null)
            .Select(r => (decimal)r.GlobalScore!)
            .ToListAsync();
        return prevScores.Count > 0 ? Math.Round(prevScores.Average(), 1) : null;
    }

    private static async Task<List<MonthlyScore>?> LoadScoreHistoryAsync(KryossDbContext db, Guid orgId)
    {
        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
        var historyRaw = await db.AssessmentRuns
            .AsNoTracking()
            .Where(r => r.OrganizationId == orgId
                        && r.CompletedAt != null
                        && r.CompletedAt >= sixMonthsAgo
                        && r.GlobalScore != null)
            .Select(r => new { r.CompletedAt, Score = (decimal)r.GlobalScore! })
            .ToListAsync();

        if (historyRaw.Count == 0) return null;

        return historyRaw
            .GroupBy(r => new DateTime(r.CompletedAt!.Value.Year, r.CompletedAt.Value.Month, 1, 0, 0, 0, DateTimeKind.Utc))
            .Select(g => new MonthlyScore { Month = g.Key, Score = Math.Round(g.Average(x => x.Score), 1) })
            .OrderBy(m => m.Month)
            .ToList();
    }

    private static async Task<HygieneScanDto?> LoadHygieneAsync(KryossDbContext db, Guid orgId)
    {
        var scan = await db.AdHygieneScans
            .AsNoTracking()
            .Where(s => s.OrganizationId == orgId)
            .OrderByDescending(s => s.ScannedAt)
            .FirstOrDefaultAsync();

        if (scan == null) return null;

        var findings = await db.AdHygieneFindings
            .AsNoTracking()
            .Where(f => f.ScanId == scan.Id)
            .OrderBy(f => f.ObjectType).ThenByDescending(f => f.DaysInactive)
            .ToListAsync();

        return new HygieneScanDto
        {
            ScannedAt = scan.ScannedAt,
            TotalMachines = scan.TotalMachines,
            TotalUsers = scan.TotalUsers,
            StaleMachines = scan.StaleMachines,
            DormantMachines = scan.DormantMachines,
            StaleUsers = scan.StaleUsers,
            DormantUsers = scan.DormantUsers,
            DisabledUsers = scan.DisabledUsers,
            PwdNeverExpire = scan.PwdNeverExpire,
            Findings = findings
        };
    }

    private static async Task<(bool Connected, List<M365Finding> Findings)> LoadM365Async(
        KryossDbContext db, Guid orgId)
    {
        var m365Tenant = await db.M365Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.OrganizationId == orgId && t.ConsentGrantedAt != null);
        if (m365Tenant == null) return (false, new());

        var findings = await db.M365Findings
            .AsNoTracking()
            .Where(f => f.TenantId == m365Tenant.Id)
            .ToListAsync();
        return (true, findings);
    }

    private static async Task<CloudData> LoadCloudAsync(KryossDbContext db, Guid orgId)
    {
        var scan = await db.CloudAssessmentScans
            .AsNoTracking()
            .Where(s => s.OrganizationId == orgId && s.Status == "completed")
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (scan == null) return new CloudData();

        var findings = await db.CloudAssessmentFindings
            .AsNoTracking()
            .Where(f => f.ScanId == scan.Id)
            .ToListAsync();

        var fwScores = await db.CloudAssessmentFrameworkScores
            .AsNoTracking()
            .Where(s => s.ScanId == scan.Id)
            .ToListAsync();

        Dictionary<string, decimal>? areaScores = null;
        if (!string.IsNullOrEmpty(scan.AreaScores))
        {
            try { areaScores = JsonSerializer.Deserialize<Dictionary<string, decimal>>(scan.AreaScores); }
            catch { }
        }

        return new CloudData
        {
            Scan = scan,
            Findings = findings,
            AreaScores = areaScores,
            FrameworkScores = fwScores
        };
    }

    private static async Task<List<MachineNetworkDiag>> LoadNetworkAsync(
        KryossDbContext db, List<Guid> machineIds)
    {
        // Only load the LATEST diag per machine to avoid pulling entire history
        var latestIds = await db.MachineNetworkDiags
            .AsNoTracking()
            .Where(d => machineIds.Contains(d.MachineId))
            .GroupBy(d => d.MachineId)
            .Select(g => g.OrderByDescending(d => d.ScannedAt).First().Id)
            .ToListAsync();

        if (latestIds.Count == 0) return [];

        return await db.MachineNetworkDiags
            .AsNoTracking()
            .Include(d => d.LatencyPeers)
            .Include(d => d.Routes)
            .Include(d => d.Machine)
            .Where(d => latestIds.Contains(d.Id))
            .ToListAsync();
    }

    private static async Task<List<ExecutiveCta>> LoadCtasAsync(KryossDbContext db, Guid orgId)
    {
        try
        {
            var ctaPeriodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return await db.ExecutiveCtas
                .AsNoTracking()
                .Where(c => c.OrganizationId == orgId && c.PeriodStart == ctaPeriodStart)
                .ToListAsync();
        }
        catch { return new(); }
    }

    private static async Task<(List<ServiceCatalogItem> Catalog, FranchiseServiceRate? Rate)> LoadServiceCatalogAsync(
        KryossDbContext db, Guid? franchiseId)
    {
        var catalog = await db.ServiceCatalog
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        FranchiseServiceRate? rate = null;
        if (franchiseId.HasValue)
        {
            rate = await db.FranchiseServiceRates
                .AsNoTracking()
                .Where(r => r.FranchiseId == franchiseId.Value && r.EffectiveFrom <= DateTime.UtcNow)
                .OrderByDescending(r => r.EffectiveFrom)
                .FirstOrDefaultAsync();
        }

        return (catalog, rate);
    }

    private async Task<ReportUserInfo> BuildUserInfoAsync(Organization org)
    {
        var franchise = org.Franchise;
        string? fallbackPhone = franchise?.ContactPhone;

        if (_currentUser.UserId == Guid.Empty)
        {
            return new ReportUserInfo
            {
                FullName = _currentUser.DisplayName,
                Email = _currentUser.Email,
                Phone = _currentUser.Phone ?? fallbackPhone,
                JobTitle = _currentUser.JobTitle,
                CompanyName = franchise?.Name
            };
        }

        var dbUser = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == _currentUser.UserId)
            .Select(u => new { u.DisplayName, u.Email, u.Phone, u.JobTitle })
            .FirstOrDefaultAsync();

        return new ReportUserInfo
        {
            FullName = dbUser?.DisplayName ?? _currentUser.DisplayName,
            Email = dbUser?.Email ?? _currentUser.Email,
            Phone = dbUser?.Phone ?? _currentUser.Phone ?? fallbackPhone,
            JobTitle = dbUser?.JobTitle ?? _currentUser.JobTitle,
            CompanyName = franchise?.Name
        };
    }

    private class CloudData
    {
        public CloudAssessmentScan? Scan { get; set; }
        public List<CloudAssessmentFinding>? Findings { get; set; }
        public Dictionary<string, decimal>? AreaScores { get; set; }
        public List<CloudAssessmentFrameworkScore>? FrameworkScores { get; set; }
    }
}
