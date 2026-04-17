using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services.CloudAssessment.Pipelines;
using KryossApi.Services.CopilotReadiness.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace KryossApi.Services.CloudAssessment;

public class CloudAssessmentService : ICloudAssessmentService
{
    private readonly KryossDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly M365Config _m365Config;
    private readonly ILogger<CloudAssessmentService> _log;

    public CloudAssessmentService(
        KryossDbContext db,
        IServiceScopeFactory scopeFactory,
        M365Config m365Config,
        ILogger<CloudAssessmentService> log)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _m365Config = m365Config;
        _log = log;
    }

    // ── StartScanAsync ──────────────────────────────────────────────

    public async Task<Guid> StartScanAsync(Guid organizationId, Guid? tenantId)
    {
        // Resolve M365Tenant — explicit tenantId wins, else the first tenant for this org.
        var query = _db.M365Tenants.Where(t => t.OrganizationId == organizationId);
        if (tenantId.HasValue)
            query = query.Where(t => t.Id == tenantId.Value);

        var tenant = await query
            .Select(t => new { t.Id, t.TenantId, t.ClientId, t.ClientSecret, t.OrganizationId })
            .FirstOrDefaultAsync();

        if (tenant is null)
            throw new InvalidOperationException("No M365 tenant connected for this organization");

        // Same credentials selection as CopilotReadinessService.
        string clientId;
        string clientSecret;

        if (string.IsNullOrWhiteSpace(tenant.ClientId))
        {
            // Consent flow: use shared multi-tenant app registration.
            clientId = _m365Config.ClientId;
            clientSecret = _m365Config.ClientSecret;
        }
        else
        {
            // Legacy flow: per-customer app, secret is encrypted at rest.
            clientId = tenant.ClientId;
            clientSecret = await DecryptSecretForOrg(tenant.ClientSecret!, tenant.OrganizationId);
        }

        // Create scan row up front — portal can poll status while it runs.
        var scan = new CloudAssessmentScan
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            TenantId = tenant.Id,
            Status = "running",
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _db.CloudAssessmentScans.Add(scan);
        await _db.SaveChangesAsync();

        var scanId = scan.Id;

        // Run synchronously — Azure Functions on App Service plan tolerates 30-min HTTP
        // timeouts, and fire-and-forget gets killed on worker recycle. Mirrors the
        // CopilotReadinessService pattern.
        await RunScanInternalAsync(scanId, tenant.TenantId, clientId, clientSecret);

        return scanId;
    }

    // ── Background scan runner ──────────────────────────────────────

    private async Task RunScanInternalAsync(
        Guid scanId, string customerTenantId, string clientId, string clientSecret)
    {
        // Fresh DI scope — RunScanInternalAsync runs while the inbound request is
        // still alive but holds its own DbContext to avoid cross-request bleed.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KryossDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<CloudAssessmentService>>();

        try
        {
            AddActlog(db, "info", "scan.background.started", scanId,
                "Background CA scan started");
            await db.SaveChangesAsync();

            var credential = new ClientSecretCredential(customerTenantId, clientId, clientSecret);
            var graph = new GraphServiceClient(credential);
            var graphBetaHttp = await TryCreateAuthenticatedClient(
                credential, "https://graph.microsoft.com/.default",
                "https://graph.microsoft.com/beta", log);
            var defenderHttp = await TryCreateAuthenticatedClient(
                credential, "https://api.security.microsoft.com/.default",
                "https://api.security.microsoft.com", log);

            AddActlog(db, "info", "scan.tokens.acquired", scanId,
                $"Tokens: beta={graphBetaHttp != null} defender={defenderHttp != null}");
            await db.SaveChangesAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var ct = cts.Token;

            // CA-2/CA-3/CA-4: identity + endpoint + data + productivity pipelines run in parallel.
            var identityTask = TrackPipeline("identity",
                () => IdentityPipeline.RunAsync(graph, graphBetaHttp, log, ct),
                scanId, db, log);
            var endpointTask = TrackPipeline("endpoint",
                () => EndpointPipeline.RunAsync(graph, defenderHttp, log, ct),
                scanId, db, log);
            var dataTask = TrackPipeline("data",
                () => DataPipeline.RunAsync(graph, graphBetaHttp, log, ct),
                scanId, db, log);
            var productivityIns = new ProductivityInsights();
            var productivityTask = TrackPipeline("productivity",
                () => ProductivityPipeline.RunAsync(graph, graphBetaHttp, productivityIns, log, ct),
                scanId, db, log);

            try { await Task.WhenAll(identityTask, endpointTask, dataTask, productivityTask); }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                log.LogError("CA scan {ScanId} hit 10-min timeout", scanId);
                throw new TimeoutException("Pipeline execution exceeded 10 minutes");
            }

            var identityResult = identityTask.Result;
            var endpointResult = endpointTask.Result;
            var dataResult = dataTask.Result;
            var productivityResult = productivityTask.Result;

            // Aggregate findings + metrics by area.
            var findingCount = identityResult.Findings.Count + endpointResult.Findings.Count
                + dataResult.Findings.Count + productivityResult.Findings.Count;

            var allMetrics = new Dictionary<string, Dictionary<string, string>>();
            AddMetrics(allMetrics, "identity", identityResult.Metrics);
            AddMetrics(allMetrics, "endpoint", endpointResult.Metrics);
            AddMetrics(allMetrics, "data", dataResult.Metrics);
            AddMetrics(allMetrics, "productivity", productivityResult.Metrics);

            // Per-area scores (0.0-5.0 scale matching verdict system).
            var identityScore = ComputeIdentityAreaScore(identityResult.Metrics, identityResult.Findings);
            var endpointScore = ComputeEndpointAreaScore(endpointResult.Metrics, endpointResult.Findings);
            var dataScore = ComputeDataAreaScore(dataResult.Metrics, dataResult.Findings);
            var productivityScore = ComputeProductivityAreaScore(productivityResult.Metrics, productivityResult.Findings);

            var areaScores = JsonSerializer.Serialize(new Dictionary<string, decimal>
            {
                ["identity"] = identityScore,
                ["endpoint"] = endpointScore,
                ["data"] = dataScore,
                ["productivity"] = productivityScore
            });

            var pipelineStatus = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["identity"] = identityResult.Status,
                ["endpoint"] = endpointResult.Status,
                ["data"] = dataResult.Status,
                ["productivity"] = productivityResult.Status
            });

            // Overall = weighted average. Identity 30%, Data 30%, Endpoint 25%, Productivity 15%.
            var overallScore = Math.Round(
                (identityScore * 0.30m) +
                (dataScore * 0.30m) +
                (endpointScore * 0.25m) +
                (productivityScore * 0.15m),
                2);
            var verdict = VerdictFromScore(overallScore);

            // Update scan row.
            var scan = await db.CloudAssessmentScans.FindAsync(scanId);
            if (scan is null) return;

            scan.Status = "completed";
            scan.OverallScore = overallScore;
            scan.AreaScores = areaScores;
            scan.Verdict = verdict;
            scan.PipelineStatus = pipelineStatus;
            scan.CompletedAt = DateTime.UtcNow;

            // Persist findings — CloudAssessmentFinding has an Area column; tag per-pipeline.
            var now = DateTime.UtcNow;

            void AddFindings(string area, List<RecommendationResult> fs)
            {
                foreach (var f in fs)
                {
                    db.CloudAssessmentFindings.Add(new CloudAssessmentFinding
                    {
                        ScanId = scanId,
                        Area = area,
                        Service = f.Service,
                        Feature = f.Feature,
                        Status = f.Status,
                        Priority = f.Priority,
                        Observation = f.Observation,
                        Recommendation = f.Recommendation,
                        LinkText = f.LinkText,
                        LinkUrl = f.LinkUrl,
                        CreatedAt = now
                    });
                }
            }

            AddFindings("identity", identityResult.Findings);
            AddFindings("endpoint", endpointResult.Findings);
            AddFindings("data", dataResult.Findings);
            AddFindings("productivity", productivityResult.Findings);

            // Persist productivity license rows.
            foreach (var lic in productivityIns.Licenses)
            {
                db.CloudAssessmentLicenses.Add(new CloudAssessmentLicense
                {
                    ScanId = scanId,
                    SkuPartNumber = lic.SkuPartNumber,
                    FriendlyName = lic.FriendlyName,
                    Purchased = lic.Purchased,
                    Assigned = lic.Assigned,
                    Available = lic.Available,
                    CreatedAt = now
                });
            }

            // Persist productivity adoption rows.
            foreach (var adp in productivityIns.Adoptions)
            {
                db.CloudAssessmentAdoptions.Add(new CloudAssessmentAdoption
                {
                    ScanId = scanId,
                    Area = "productivity",
                    ServiceName = adp.ServiceName,
                    LicensedCount = adp.LicensedCount,
                    Active30d = adp.Active30d,
                    AdoptionRate = adp.AdoptionRate,
                    CreatedAt = now
                });
            }

            // Persist wasted license rows.
            foreach (var w in productivityIns.WastedLicenses)
            {
                db.CloudAssessmentWastedLicenses.Add(new CloudAssessmentWastedLicense
                {
                    ScanId = scanId,
                    UserPrincipal = w.UserPrincipal,
                    DisplayName = w.DisplayName,
                    Sku = w.Sku,
                    LastSignIn = w.LastSignIn,
                    DaysInactive = w.DaysInactive,
                    EstimatedCostYear = w.EstimatedCostYear,
                    CreatedAt = now
                });
            }

            // Persist metrics — Area column = dimension key from allMetrics.
            foreach (var (dimension, metricsDict) in allMetrics)
            {
                foreach (var (key, value) in metricsDict)
                {
                    db.CloudAssessmentMetrics.Add(new CloudAssessmentMetric
                    {
                        ScanId = scanId,
                        Area = dimension,
                        MetricKey = key,
                        MetricValue = value,
                        CreatedAt = now
                    });
                }
            }

            await db.SaveChangesAsync();

            AddActlog(db, "info", "scan.completed", scanId,
                $"CA scan completed identityScore={identityScore} endpointScore={endpointScore} dataScore={dataScore} productivityScore={productivityScore} findings={findingCount} wastedLicenses={productivityIns.WastedLicenses.Count}");
            await db.SaveChangesAsync();

            log.LogInformation(
                "Cloud Assessment scan {ScanId} completed. Identity={Identity} Endpoint={Endpoint} Data={Data} Productivity={Productivity} Verdict={Verdict}",
                scanId, identityScore, endpointScore, dataScore, productivityScore, verdict);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Cloud Assessment scan {ScanId} failed", scanId);

            try
            {
                // Tracker may be poisoned by failed inserts; reset before retry-fetch.
                db.ChangeTracker.Clear();

                var scan = await db.CloudAssessmentScans.FindAsync(scanId);
                if (scan is not null)
                {
                    scan.Status = "failed";
                    scan.CompletedAt = DateTime.UtcNow;
                    scan.PipelineStatus = JsonSerializer.Serialize(new
                    {
                        error = ex.Message,
                        inner = ex.InnerException?.Message,
                        stack = ex.StackTrace
                    });
                    await db.SaveChangesAsync();
                }

                AddActlog(db, "error", "scan.failed", scanId,
                    $"Scan failed: {ex.Message}"
                    + (ex.InnerException is not null ? $" (inner: {ex.InnerException.Message})" : ""));
                await db.SaveChangesAsync();
            }
            catch (Exception innerEx)
            {
                log.LogError(innerEx, "Failed to mark CA scan {ScanId} as failed", scanId);
            }
        }
    }

    // ── GetLatestScanAsync ──────────────────────────────────────────

    public async Task<object?> GetLatestScanAsync(Guid organizationId)
    {
        var scan = await _db.CloudAssessmentScans
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (scan is null) return null;

        // Separate query for findings summary grouped by area
        var findingsSummary = await _db.CloudAssessmentFindings
            .Where(f => f.ScanId == scan.Id)
            .GroupBy(f => f.Area)
            .Select(g => new
            {
                Area = g.Key,
                Total = g.Count(),
                ActionRequired = g.Count(f => f.Status == "Action Required"),
                Warning = g.Count(f => f.Status == "Warning"),
                Success = g.Count(f => f.Status == "Success"),
                Disabled = g.Count(f => f.Status == "Disabled")
            })
            .ToListAsync();

        return new
        {
            scan.Id,
            scan.Status,
            scan.OverallScore,
            AreaScores = ParseJsonDict(scan.AreaScores),
            scan.Verdict,
            PipelineStatus = ParsePipelineStatus(scan.PipelineStatus),
            scan.StartedAt,
            scan.CompletedAt,
            scan.CreatedAt,
            scan.TenantId,
            FindingsSummary = findingsSummary
        };
    }

    // ── GetScanDetailAsync ──────────────────────────────────────────

    public async Task<object?> GetScanDetailAsync(Guid scanId)
    {
        var scan = await _db.CloudAssessmentScans
            .FirstOrDefaultAsync(s => s.Id == scanId);

        if (scan is null) return null;

        var findings = await _db.CloudAssessmentFindings
            .Where(f => f.ScanId == scanId)
            .OrderBy(f => f.Area).ThenBy(f => f.Service).ThenBy(f => f.Feature)
            .Select(f => new
            {
                f.Area, f.Service, f.Feature, f.Status, f.Priority,
                f.Observation, f.Recommendation, f.LinkText, f.LinkUrl
            })
            .ToListAsync();

        var metrics = await _db.CloudAssessmentMetrics
            .Where(m => m.ScanId == scanId)
            .Select(m => new { m.Area, m.MetricKey, m.MetricValue })
            .ToListAsync();

        var licenses = await _db.CloudAssessmentLicenses
            .Where(l => l.ScanId == scanId)
            .Select(l => new
            {
                l.SkuPartNumber, l.FriendlyName, l.Purchased, l.Assigned, l.Available
            })
            .ToListAsync();

        var adoption = await _db.CloudAssessmentAdoptions
            .Where(a => a.ScanId == scanId)
            .Select(a => new
            {
                a.Area, a.ServiceName, a.LicensedCount, a.Active30d, a.AdoptionRate
            })
            .ToListAsync();

        var wastedLicenses = await _db.CloudAssessmentWastedLicenses
            .Where(w => w.ScanId == scanId)
            .Select(w => new
            {
                w.UserPrincipal, w.DisplayName, w.Sku,
                w.LastSignIn, w.DaysInactive, w.EstimatedCostYear
            })
            .ToListAsync();

        return new
        {
            scan.Id, scan.OrganizationId, scan.TenantId,
            scan.Status, scan.OverallScore,
            AreaScores = ParseJsonDict(scan.AreaScores),
            scan.Verdict,
            PipelineStatus = ParsePipelineStatus(scan.PipelineStatus),
            scan.StartedAt, scan.CompletedAt, scan.CreatedAt,
            Findings = findings,
            Metrics = metrics,
            Licenses = licenses,
            Adoption = adoption,
            WastedLicenses = wastedLicenses
        };
    }

    // ── GetScanHistoryAsync ─────────────────────────────────────────

    public async Task<List<object>> GetScanHistoryAsync(Guid organizationId, int limit = 20)
    {
        if (limit < 1) limit = 1;
        if (limit > 200) limit = 200;

        var history = await _db.CloudAssessmentScans
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(limit)
            .Select(s => new
            {
                s.Id,
                s.OverallScore,
                AreaScores = s.AreaScores,
                s.Verdict,
                s.Status,
                s.CreatedAt,
                s.CompletedAt
            })
            .ToListAsync();

        // Parse AreaScores JSON per row outside of SQL (EF can't parse JSON columns).
        return history
            .Select(h => (object)new
            {
                h.Id,
                h.OverallScore,
                AreaScores = ParseJsonDict(h.AreaScores),
                h.Verdict,
                h.Status,
                h.CreatedAt,
                h.CompletedAt
            })
            .ToList();
    }

    // ── CompareScansAsync ────────────────────────────────────────────

    public async Task<object?> CompareScansAsync(Guid scanAId, Guid scanBId)
    {
        var scanA = await _db.CloudAssessmentScans
            .Where(s => s.Id == scanAId)
            .Select(s => new
            {
                s.Id,
                s.OrganizationId,
                s.OverallScore,
                s.AreaScores,
                s.Verdict,
                s.CreatedAt,
                s.CompletedAt
            })
            .FirstOrDefaultAsync();

        var scanB = await _db.CloudAssessmentScans
            .Where(s => s.Id == scanBId)
            .Select(s => new
            {
                s.Id,
                s.OrganizationId,
                s.OverallScore,
                s.AreaScores,
                s.Verdict,
                s.CreatedAt,
                s.CompletedAt
            })
            .FirstOrDefaultAsync();

        if (scanA is null || scanB is null) return null;

        // Findings from both scans (minimal fields for matching + display).
        var findingsA = await _db.CloudAssessmentFindings
            .Where(f => f.ScanId == scanAId)
            .Select(f => new { f.Area, f.Service, f.Feature, f.Status, f.Priority, f.Observation, f.Recommendation })
            .ToListAsync();

        var findingsB = await _db.CloudAssessmentFindings
            .Where(f => f.ScanId == scanBId)
            .Select(f => new { f.Area, f.Service, f.Feature, f.Status, f.Priority, f.Observation, f.Recommendation })
            .ToListAsync();

        // Match by composite key (area|service|feature).
        string Key(string area, string service, string feature) =>
            $"{area?.ToLowerInvariant()}|{service?.ToLowerInvariant()}|{feature?.ToLowerInvariant()}";

        var keysA = findingsA.Select(f => Key(f.Area, f.Service, f.Feature)).ToHashSet();
        var keysB = findingsB.Select(f => Key(f.Area, f.Service, f.Feature)).ToHashSet();

        // Resolved = in A, not in B (fixed / removed between A and B).
        var resolvedFindings = findingsA
            .Where(f => !keysB.Contains(Key(f.Area, f.Service, f.Feature)))
            .Select(f => new { f.Area, f.Service, f.Feature, f.Status, f.Priority, f.Observation, f.Recommendation })
            .ToList();

        // New = in B, not in A (regression or newly detected).
        var newFindings = findingsB
            .Where(f => !keysA.Contains(Key(f.Area, f.Service, f.Feature)))
            .Select(f => new { f.Area, f.Service, f.Feature, f.Status, f.Priority, f.Observation, f.Recommendation })
            .ToList();

        var unchangedCount = keysA.Intersect(keysB).Count();

        // Deltas (B - A). Positive = improvement when higher scores are better.
        var areaScoresA = ParseScoreDict(scanA.AreaScores);
        var areaScoresB = ParseScoreDict(scanB.AreaScores);

        var deltas = new Dictionary<string, decimal>();
        foreach (var key in new[] { "identity", "endpoint", "data", "productivity" })
        {
            var a = areaScoresA.TryGetValue(key, out var av) ? av : 0m;
            var b = areaScoresB.TryGetValue(key, out var bv) ? bv : 0m;
            deltas[key] = Math.Round(b - a, 2);
        }
        deltas["overall"] = Math.Round((scanB.OverallScore ?? 0m) - (scanA.OverallScore ?? 0m), 2);

        return new
        {
            ScanA = new
            {
                scanA.Id,
                scanA.CreatedAt,
                scanA.CompletedAt,
                AreaScores = areaScoresA,
                scanA.OverallScore,
                scanA.Verdict
            },
            ScanB = new
            {
                scanB.Id,
                scanB.CreatedAt,
                scanB.CompletedAt,
                AreaScores = areaScoresB,
                scanB.OverallScore,
                scanB.Verdict
            },
            Deltas = deltas,
            ResolvedFindings = resolvedFindings,
            NewFindings = newFindings,
            UnchangedCount = unchangedCount
        };
    }

    private static Dictionary<string, decimal> ParseScoreDict(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, decimal>();
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, decimal>>(json);
            return raw ?? new Dictionary<string, decimal>();
        }
        catch
        {
            return new Dictionary<string, decimal>();
        }
    }

    // ── Scoring ─────────────────────────────────────────────────────

    /// <summary>
    /// Compute the Identity area score on the 0.00-5.00 verdict scale.
    /// Starts at 5.0, deducts for action_required + warning findings (capped),
    /// then applies metric-driven penalties for the highest-signal gaps.
    /// </summary>
    private static decimal ComputeIdentityAreaScore(
        Dictionary<string, string> metrics,
        List<RecommendationResult> findings)
    {
        decimal score = 5.0m;

        // Finding-based deductions.
        var actionRequired = findings.Count(f =>
            string.Equals(f.Status, "action_required", StringComparison.OrdinalIgnoreCase));
        var warnings = findings.Count(f =>
            string.Equals(f.Status, "warning", StringComparison.OrdinalIgnoreCase));

        var arDeduction = Math.Min(actionRequired * 0.1m, 2.5m);
        var warnDeduction = Math.Min(warnings * 0.05m, 1.0m);
        score -= arDeduction;
        score -= warnDeduction;

        // Metric-based bonus/penalty (highest-signal Identity gaps).
        if (TryGetDecimal(metrics, "ca_compat_score_pct", out var caCompat) && caCompat < 50)
            score -= 0.3m;

        if (TryGetDecimal(metrics, "mfa_registration_pct", out var mfaPct) && mfaPct < 80)
            score -= 0.3m;

        if (TryGetInt(metrics, "risky_users_high", out var riskyHigh) && riskyHigh > 0)
            score -= 0.2m;

        if (TryGetInt(metrics, "permanent_global_admins", out var pga) && pga > 5)
            score -= 0.2m;

        if (TryGetInt(metrics, "admins_without_mfa", out var adminsNoMfa) && adminsNoMfa > 0)
            score -= 0.3m;

        // Clamp to [0, 5] then round to 2 decimal places.
        if (score < 0m) score = 0m;
        if (score > 5m) score = 5m;
        return Math.Round(score, 2);
    }

    /// <summary>
    /// Compute the Endpoint area score on the 0.00-5.00 verdict scale.
    /// Spec weights: device compliance 30%, exposure 25%, app protection 20%,
    /// vulns 15%, Autopilot 10%. Implementation: start at 5.0, deduct for
    /// findings + metric gaps, clamp + round.
    /// </summary>
    private static decimal ComputeEndpointAreaScore(
        Dictionary<string, string> metrics,
        List<RecommendationResult> findings)
    {
        decimal score = 5.0m;

        // Finding-based deductions — endpoint findings only (caller passes endpointResult.Findings).
        var actionRequired = findings.Count(f =>
            string.Equals(f.Status, "action_required", StringComparison.OrdinalIgnoreCase));
        var warnings = findings.Count(f =>
            string.Equals(f.Status, "warning", StringComparison.OrdinalIgnoreCase));

        score -= Math.Min(actionRequired * 0.15m, 2.5m);
        score -= Math.Min(warnings * 0.07m, 1.0m);

        // Compliance rate: non_compliant / total.
        if (TryGetInt(metrics, "devices_total", out var devicesTotal) && devicesTotal > 0)
        {
            var nonCompliant = TryGetInt(metrics, "devices_non_compliant", out var nc) ? nc : 0;
            var rate = (decimal)nonCompliant / devicesTotal;
            if (rate > 0.15m) score -= 0.4m;
            else if (rate > 0.05m) score -= 0.2m;
        }

        // Exposure score (Defender): higher = worse.
        if (TryGetDecimal(metrics, "exposure_score", out var exposure))
        {
            if (exposure > 60m) score -= 0.4m;
            else if (exposure > 30m) score -= 0.2m;
        }

        // App protection coverage: any platform with devices but no APP policies = gap.
        var devicesIos = TryGetInt(metrics, "devices_ios", out var iosCount) ? iosCount : 0;
        var appProtIos = TryGetInt(metrics, "app_protection_ios", out var iosApp) ? iosApp : 0;
        var devicesAndroid = TryGetInt(metrics, "devices_android", out var androidCount) ? androidCount : 0;
        var appProtAndroid = TryGetInt(metrics, "app_protection_android", out var androidApp) ? androidApp : 0;
        if ((devicesIos > 0 && appProtIos == 0) || (devicesAndroid > 0 && appProtAndroid == 0))
            score -= 0.3m;

        // Vulnerability posture.
        if (TryGetInt(metrics, "vuln_critical", out var vulnCritical) && vulnCritical > 0)
            score -= 0.3m;

        // Autopilot maturity: Windows fleet of decent size with no profiles.
        var devicesWindows = TryGetInt(metrics, "devices_windows", out var winCount) ? winCount : 0;
        var autopilotProfiles = TryGetInt(metrics, "autopilot_profiles", out var ap) ? ap : 0;
        if (devicesWindows >= 5 && autopilotProfiles == 0)
            score -= 0.15m;

        // Clamp to [0, 5] then round to 2 decimal places.
        if (score < 0m) score = 0m;
        if (score > 5m) score = 5m;
        return Math.Round(score, 2);
    }

    /// <summary>
    /// Compute the Data area score on the 0.00-5.00 verdict scale.
    /// Spec weights: label coverage 30%, oversharing 25%, DLP posture 20%,
    /// external access 15%, Purview licensing 10%. Implementation: start at 5.0,
    /// deduct for findings + metric gaps, clamp + round.
    /// </summary>
    private static decimal ComputeDataAreaScore(
        Dictionary<string, string> metrics,
        List<RecommendationResult> findings)
    {
        decimal score = 5.0m;

        // Finding-based deductions — data findings only (caller passes dataResult.Findings).
        var actionRequired = findings.Count(f =>
            string.Equals(f.Status, "action_required", StringComparison.OrdinalIgnoreCase));
        var warnings = findings.Count(f =>
            string.Equals(f.Status, "warning", StringComparison.OrdinalIgnoreCase));

        score -= Math.Min(actionRequired * 0.12m, 2.5m);
        score -= Math.Min(warnings * 0.06m, 1.0m);

        // Label coverage (30% weight) — decimal percent of labeled items.
        if (TryGetDecimal(metrics, "label_coverage_pct", out var labelCoverage))
        {
            if (labelCoverage < 40m) score -= 0.6m;
            else if (labelCoverage < 60m) score -= 0.3m;
        }

        // Oversharing (25% weight) — higher = worse.
        if (TryGetDecimal(metrics, "overshared_pct", out var oversharedPct))
        {
            if (oversharedPct >= 20m) score -= 0.5m;
            else if (oversharedPct >= 10m) score -= 0.25m;
        }

        // DLP posture (20% weight): licensing + alert activity.
        if (TryGetBool(metrics, "dlp_licensed", out var dlpLicensed))
        {
            if (!dlpLicensed)
            {
                score -= 0.4m;
            }
            else if (TryGetInt(metrics, "dlp_alerts_last_30d", out var dlpAlerts) && dlpAlerts == 0)
            {
                // Licensed but no alerts in 30d — suggests DLP policies not configured.
                score -= 0.2m;
            }
        }

        // External access (15% weight) — guest user count.
        if (TryGetInt(metrics, "total_guests", out var totalGuests))
        {
            if (totalGuests > 50) score -= 0.3m;
            else if (totalGuests > 20) score -= 0.15m;
        }

        // Purview licensing (10% weight) — count how many of the six *_licensed flags are false.
        string[] purviewLicenseKeys =
        {
            "aip_p1_licensed",
            "aip_p2_licensed",
            "dlp_licensed",
            "insider_risk_licensed",
            "ediscovery_licensed",
            "advanced_audit_licensed"
        };
        var falseLicenseCount = 0;
        foreach (var key in purviewLicenseKeys)
        {
            if (TryGetBool(metrics, key, out var licensed) && !licensed)
                falseLicenseCount++;
        }
        if (falseLicenseCount >= 4) score -= 0.2m;
        if (falseLicenseCount == 6) score -= 0.2m; // additional — total -0.4 when all false.

        // Clamp to [0, 5] then round to 2 decimal places.
        if (score < 0m) score = 0m;
        if (score > 5m) score = 5m;
        return Math.Round(score, 2);
    }

    /// <summary>
    /// Compute the Productivity area score on the 0.00-5.00 verdict scale.
    /// Weights: overall adoption rate 40%, license utilization 25% (proxied via
    /// action_required finding deductions), Copilot adoption 15%, waste ratio 20%.
    /// Note: license utilization weight (25%) is folded into the base finding
    /// deductions — licensing action_required findings capture over-purchased/
    /// unassigned seats so no separate metric penalty is needed.
    /// </summary>
    private static decimal ComputeProductivityAreaScore(
        Dictionary<string, string> metrics,
        List<RecommendationResult> findings)
    {
        decimal score = 5.0m;

        // Finding-based deductions.
        var actionRequired = findings.Count(f =>
            string.Equals(f.Status, "action_required", StringComparison.OrdinalIgnoreCase));
        var warnings = findings.Count(f =>
            string.Equals(f.Status, "warning", StringComparison.OrdinalIgnoreCase));

        score -= Math.Min(actionRequired * 0.12m, 2.5m);
        score -= Math.Min(warnings * 0.06m, 1.0m);

        // Overall adoption signal (40% weight) — average of the four core workload rates
        // that are non-zero (zero = service not licensed, not unused).
        var adoptionRates = new List<decimal>();
        foreach (var key in new[] { "email_adoption_rate", "teams_adoption_rate",
                                    "sharepoint_adoption_rate", "onedrive_adoption_rate" })
        {
            if (TryGetDecimal(metrics, key, out var rate) && rate > 0m)
                adoptionRates.Add(rate);
        }
        if (adoptionRates.Count > 0)
        {
            var avgAdoption = adoptionRates.Average();
            if (avgAdoption < 40m) score -= 0.6m;
            else if (avgAdoption < 60m) score -= 0.3m;
        }

        // Copilot adoption (15% weight).
        if (TryGetInt(metrics, "copilot_licenses_purchased", out var copilotPurchased)
            && copilotPurchased > 0)
        {
            if (TryGetDecimal(metrics, "copilot_adoption_pct", out var copilotPct))
            {
                if (copilotPct < 50m) score -= 0.3m;
                else if (copilotPct < 80m) score -= 0.15m;
            }
        }

        // Waste ratio (20% weight — more waste = more penalty).
        if (TryGetInt(metrics, "wasted_license_count", out var wastedCount))
        {
            if (wastedCount > 10) score -= 0.4m;
            else if (wastedCount > 5) score -= 0.2m;
        }

        // Clamp to [0, 5] then round to 2 decimal places.
        if (score < 0m) score = 0m;
        if (score > 5m) score = 5m;
        return Math.Round(score, 2);
    }

    private static bool TryGetDecimal(Dictionary<string, string> metrics, string key, out decimal value)
    {
        value = 0m;
        if (!metrics.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetInt(Dictionary<string, string> metrics, string key, out int value)
    {
        value = 0;
        if (!metrics.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;
        return int.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetBool(Dictionary<string, string> metrics, string key, out bool value)
    {
        value = false;
        if (!metrics.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;
        value = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static string VerdictFromScore(decimal score) => score switch
    {
        >= 4.0m => "excellent",
        >= 3.0m => "good",
        >= 2.0m => "needs_attention",
        _ => "critical"
    };

    // ── Private helpers ─────────────────────────────────────────────

    private static Dictionary<string, object>? ParseJsonDict(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parse pipeline_status JSON string to object so it ships to portal as JSON object,
    /// not a string (otherwise portal Object.entries iterates characters).
    /// </summary>
    private static Dictionary<string, string>? ParsePipelineStatus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void AddActlog(KryossDbContext db, string severity, string action, Guid scanId, string message) =>
        db.Actlog.Add(new Actlog
        {
            Timestamp = DateTime.UtcNow,
            Severity = severity,
            Module = "cloud-assessment",
            Action = action,
            EntityType = "CloudAssessmentScan",
            EntityId = scanId.ToString(),
            Message = message
        });

    // ── Pipeline orchestration helpers (ported from CopilotReadinessService) ──

    private static async Task<HttpClient> CreateAuthenticatedClient(
        ClientSecretCredential credential, string scope, string baseUrl)
    {
        var token = await credential.GetTokenAsync(
            new Azure.Core.TokenRequestContext(new[] { scope }));
        var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        return http;
    }

    /// <summary>
    /// Acquire token + build HttpClient. Returns null if service principal missing in tenant
    /// (e.g. customer never consented to this API or didn't register app in their tenant).
    /// Pipelines must handle null clients gracefully.
    /// </summary>
    private static async Task<HttpClient?> TryCreateAuthenticatedClient(
        ClientSecretCredential credential, string scope, string baseUrl, ILogger log)
    {
        try
        {
            return await CreateAuthenticatedClient(credential, scope, baseUrl);
        }
        catch (Exception ex)
        {
            log.LogWarning(
                "Token acquisition failed for scope {Scope} — pipeline will be skipped. Error: {Error}",
                scope, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Wrap pipeline execution with actlog start/end tracking + exception capture.
    /// Never throw — always return a PipelineResult (failed if exception).
    /// </summary>
    private static async Task<PipelineResult> TrackPipeline(
        string name,
        Func<Task<PipelineResult>> pipelineFn,
        Guid scanId,
        KryossDbContext db,
        ILogger log)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await pipelineFn();
            sw.Stop();
            log.LogInformation("Pipeline {Name} completed in {Ms}ms status={Status}",
                name, sw.ElapsedMilliseconds, result.Status);

            try
            {
                db.Actlog.Add(new Actlog
                {
                    Timestamp = DateTime.UtcNow,
                    Severity = "info",
                    Module = "cloud-assessment",
                    Action = $"pipeline.{name}.done",
                    EntityType = "CloudAssessmentScan",
                    EntityId = scanId.ToString(),
                    Message = $"Pipeline {name} done in {sw.ElapsedMilliseconds}ms status={result.Status} findings={result.Findings.Count}"
                });
                await db.SaveChangesAsync();
            }
            catch { /* swallow */ }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            log.LogError(ex, "Pipeline {Name} crashed after {Ms}ms", name, sw.ElapsedMilliseconds);

            try
            {
                db.Actlog.Add(new Actlog
                {
                    Timestamp = DateTime.UtcNow,
                    Severity = "error",
                    Module = "cloud-assessment",
                    Action = $"pipeline.{name}.failed",
                    EntityType = "CloudAssessmentScan",
                    EntityId = scanId.ToString(),
                    Message = $"Pipeline {name} crashed in {sw.ElapsedMilliseconds}ms: {ex.Message}"
                });
                await db.SaveChangesAsync();
            }
            catch { /* swallow actlog errors */ }

            return new PipelineResult
            {
                PipelineName = name,
                Status = "failed",
                Error = ex.Message
            };
        }
    }

    private static void AddMetrics(
        Dictionary<string, Dictionary<string, string>> target,
        string dimension,
        Dictionary<string, string> source)
    {
        if (source.Count > 0)
            target[dimension] = new Dictionary<string, string>(source);
    }

    /// <summary>
    /// Decrypt a secret previously encrypted with the org's ApiSecret (AES-256-GCM).
    /// Mirrors M365Function.DecryptSecretForOrg / CopilotReadinessService.DecryptSecretForOrg.
    /// </summary>
    private async Task<string> DecryptSecretForOrg(string encrypted, Guid orgId)
    {
        if (encrypted.StartsWith("PLAIN:"))
            return Encoding.UTF8.GetString(Convert.FromBase64String(encrypted[6..]));

        if (!encrypted.StartsWith("ENC:"))
            return encrypted;

        var parts = encrypted[4..].Split(':');
        if (parts.Length != 3)
            throw new InvalidOperationException("Malformed encrypted secret");

        var org = await _db.Organizations
            .Where(o => o.Id == orgId)
            .Select(o => new { o.ApiSecret })
            .FirstOrDefaultAsync();

        var keyMaterial = org?.ApiSecret
            ?? throw new InvalidOperationException("Cannot decrypt: org has no ApiSecret");

        var key = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
        var nonce = Convert.FromBase64String(parts[0]);
        var ciphertext = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
