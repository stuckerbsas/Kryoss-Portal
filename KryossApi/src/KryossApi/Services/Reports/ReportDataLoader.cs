using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services.Reports;

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
        // 1. Org + branding
        var org = await _db.Organizations
            .Include(o => o.Franchise)
            .Include(o => o.Brand)
            .FirstOrDefaultAsync(o => o.Id == orgId)
            ?? throw new InvalidOperationException($"Organization {orgId} not found");

        var brand = org.Brand;
        var franchise = org.Franchise;
        var branding = new ReportBranding
        {
            CompanyName = brand?.Name ?? franchise.BrandName ?? franchise.Name,
            PrimaryColor = brand?.ColorPrimary ?? franchise.BrandColorPrimary ?? "#006536",
            AccentColor = brand?.ColorAccent ?? franchise.BrandColorAccent ?? "#A2C564",
            LogoUrl = brand?.LogoUrl ?? franchise.BrandLogoUrl ?? LogoData.DataUri
        };

        // 2. User info
        var userInfo = await BuildUserInfoAsync(org);

        // 3. Latest run per machine
        var latestRunIds = await _db.AssessmentRuns
            .Where(r => r.OrganizationId == orgId && r.CompletedAt != null)
            .GroupBy(r => r.MachineId)
            .Select(g => g.OrderByDescending(r => r.CompletedAt).First().Id)
            .ToListAsync();

        var runs = latestRunIds.Count > 0
            ? await _db.AssessmentRuns
                .Include(r => r.Machine)
                .Where(r => latestRunIds.Contains(r.Id))
                .OrderBy(r => r.Machine.Hostname)
                .ToListAsync()
            : new List<AssessmentRun>();

        // 4. Control results joined with ControlDefs
        var allResults = latestRunIds.Count > 0
            ? await _db.ControlResults
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
                .ToListAsync()
            : new List<OrgControlResult>();

        // 5. Framework filter
        if (!string.IsNullOrEmpty(options.FrameworkCode))
        {
            var framework = await _db.Frameworks
                .FirstOrDefaultAsync(f => f.Code == options.FrameworkCode && f.IsActive);

            if (framework != null)
            {
                var controlDefIdsInFramework = await _db.ControlFrameworks
                    .Where(cf => cf.FrameworkId == framework.Id)
                    .Select(cf => cf.ControlDefId)
                    .ToListAsync();
                var controlDefIdsSet = new HashSet<int>(controlDefIdsInFramework);
                allResults = allResults.Where(r => controlDefIdsSet.Contains(r.ControlDefId)).ToList();
            }
        }

        // 6. Framework scores
        var frameworkScores = latestRunIds.Count > 0
            ? await _db.RunFrameworkScores
                .Where(fs => latestRunIds.Contains(fs.RunId))
                .GroupBy(fs => fs.FrameworkId)
                .Select(g => new
                {
                    frameworkId = g.Key,
                    avgScore = Math.Round(g.Average(fs => (double)fs.Score), 1),
                    totalPass = g.Sum(fs => (int)fs.PassCount),
                    totalFail = g.Sum(fs => (int)fs.FailCount),
                })
                .Join(_db.Frameworks, x => x.frameworkId, fw => fw.Id,
                    (x, fw) => new FrameworkScoreDto
                    {
                        Code = fw.Code,
                        Name = fw.Name,
                        Score = x.avgScore,
                        PassCount = (short)x.totalPass,
                        FailCount = (short)x.totalFail
                    })
                .OrderBy(x => x.Code)
                .ToListAsync()
            : new List<FrameworkScoreDto>();

        // 7. Enrichment (disks, ports, threats)
        var machineIds = runs.Select(r => r.MachineId).ToList();
        var enrichment = new OrgEnrichment();
        if (machineIds.Count > 0)
        {
            enrichment.Disks = await _db.MachineDisks.Where(d => machineIds.Contains(d.MachineId)).OrderBy(d => d.DriveLetter).ToListAsync();
            enrichment.Ports = await _db.MachinePorts.Where(p => machineIds.Contains(p.MachineId)).OrderBy(p => p.Port).ToListAsync();
            enrichment.Threats = await _db.MachineThreats.Where(t => machineIds.Contains(t.MachineId)).OrderByDescending(t => t.DetectedAt).ToListAsync();
        }

        // 8. Previous month score
        var periodEnd = DateTime.UtcNow.AddDays(-30);
        var periodStart30 = DateTime.UtcNow.AddDays(-60);
        var prevScores = await _db.AssessmentRuns
            .Where(r => r.OrganizationId == orgId
                        && r.CompletedAt != null
                        && r.CompletedAt >= periodStart30
                        && r.CompletedAt < periodEnd
                        && r.GlobalScore != null)
            .Select(r => (decimal)r.GlobalScore!)
            .ToListAsync();
        decimal? previousMonthScore = prevScores.Count > 0 ? Math.Round(prevScores.Average(), 1) : null;

        // 9. Score history (last 6 months, for sparkline)
        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
        var historyRaw = await _db.AssessmentRuns
            .Where(r => r.OrganizationId == orgId
                        && r.CompletedAt != null
                        && r.CompletedAt >= sixMonthsAgo
                        && r.GlobalScore != null)
            .Select(r => new { r.CompletedAt, Score = (decimal)r.GlobalScore! })
            .ToListAsync();

        List<MonthlyScore>? scoreHistory = null;
        if (historyRaw.Count > 0)
        {
            scoreHistory = historyRaw
                .GroupBy(r => new DateTime(r.CompletedAt!.Value.Year, r.CompletedAt.Value.Month, 1, 0, 0, 0, DateTimeKind.Utc))
                .Select(g => new MonthlyScore { Month = g.Key, Score = Math.Round(g.Average(x => x.Score), 1) })
                .OrderBy(m => m.Month)
                .ToList();
        }

        // 10. AD Hygiene
        var hygieneScan = await _db.AdHygieneScans
            .Where(s => s.OrganizationId == orgId)
            .OrderByDescending(s => s.ScannedAt)
            .Select(s => new HygieneScanDto
            {
                ScannedAt = s.ScannedAt,
                TotalMachines = s.TotalMachines,
                TotalUsers = s.TotalUsers,
                StaleMachines = s.StaleMachines,
                DormantMachines = s.DormantMachines,
                StaleUsers = s.StaleUsers,
                DormantUsers = s.DormantUsers,
                DisabledUsers = s.DisabledUsers,
                PwdNeverExpire = s.PwdNeverExpire,
                Findings = _db.AdHygieneFindings
                    .Where(f => f.ScanId == s.Id)
                    .OrderBy(f => f.ObjectType).ThenByDescending(f => f.DaysInactive)
                    .ToList()
            })
            .FirstOrDefaultAsync();

        // 11. M365
        var m365Connected = false;
        var m365Findings = new List<M365Finding>();
        var m365Tenant = await _db.M365Tenants
            .FirstOrDefaultAsync(t => t.OrganizationId == orgId && t.ConsentGrantedAt != null);
        if (m365Tenant != null)
        {
            m365Connected = true;
            m365Findings = await _db.M365Findings
                .Where(f => f.TenantId == m365Tenant.Id)
                .ToListAsync();
        }

        // 12. Cloud Assessment
        CloudAssessmentScan? cloudScan = null;
        List<CloudAssessmentFinding>? cloudFindings = null;
        Dictionary<string, decimal>? areaScores = null;
        List<CloudAssessmentFrameworkScore>? cloudFrameworkScores = null;

        cloudScan = await _db.CloudAssessmentScans
            .Where(s => s.OrganizationId == orgId && s.Status == "completed")
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (cloudScan != null)
        {
            cloudFindings = await _db.CloudAssessmentFindings
                .Where(f => f.ScanId == cloudScan.Id)
                .ToListAsync();

            cloudFrameworkScores = await _db.CloudAssessmentFrameworkScores
                .Where(s => s.ScanId == cloudScan.Id)
                .ToListAsync();

            if (!string.IsNullOrEmpty(cloudScan.AreaScores))
            {
                try
                {
                    areaScores = JsonSerializer.Deserialize<Dictionary<string, decimal>>(cloudScan.AreaScores);
                }
                catch { /* malformed JSON — leave null */ }
            }
        }

        // 13. CTAs
        var savedCtas = new List<ExecutiveCta>();
        try
        {
            var ctaPeriodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            savedCtas = await _db.ExecutiveCtas
                .Where(c => c.OrganizationId == orgId && c.PeriodStart == ctaPeriodStart)
                .ToListAsync();
        }
        catch { /* migration 028 (executive_ctas) may not be applied */ }

        // 14. Service catalog
        var serviceCatalog = await _db.ServiceCatalog
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        // 15. Franchise rate
        FranchiseServiceRate? rate = null;
        if (franchise != null)
        {
            rate = await _db.FranchiseServiceRates
                .Where(r => r.FranchiseId == franchise.Id && r.EffectiveFrom <= DateTime.UtcNow)
                .OrderByDescending(r => r.EffectiveFrom)
                .FirstOrDefaultAsync();
        }

        return new ReportData
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
            CloudScan = cloudScan,
            CloudFindings = cloudFindings,
            AreaScores = areaScores,
            CloudFrameworkScores = cloudFrameworkScores,
            SavedCtas = savedCtas,
            ServiceCatalog = serviceCatalog,
            Rate = rate
        };
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
}
