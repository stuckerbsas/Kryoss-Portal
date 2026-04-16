using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services.CopilotReadiness.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace KryossApi.Services.CopilotReadiness;

public class CopilotReadinessService : ICopilotReadinessService
{
    private readonly KryossDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly M365Config _m365Config;
    private readonly ILogger<CopilotReadinessService> _log;

    public CopilotReadinessService(
        KryossDbContext db,
        IServiceScopeFactory scopeFactory,
        M365Config m365Config,
        ILogger<CopilotReadinessService> log)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _m365Config = m365Config;
        _log = log;
    }

    // ── StartScanAsync ──────────────────────────────────────────────

    public async Task<Guid> StartScanAsync(Guid organizationId, Guid tenantId, string customerTenantId)
    {
        // Determine credentials: shared app or per-customer
        var tenant = await _db.M365Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.ClientId, t.ClientSecret, t.OrganizationId })
            .FirstAsync();

        string clientId;
        string clientSecret;

        if (string.IsNullOrWhiteSpace(tenant.ClientId))
        {
            // Consent flow: use shared app registration credentials
            clientId = _m365Config.ClientId;
            clientSecret = _m365Config.ClientSecret;
        }
        else
        {
            // Legacy flow: decrypt per-customer credentials
            clientId = tenant.ClientId;
            clientSecret = await DecryptSecretForOrg(tenant.ClientSecret!, tenant.OrganizationId);
        }

        // Create scan row
        var scan = new CopilotReadinessScan
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            TenantId = tenantId,
            Status = "running",
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _db.CopilotReadinessScans.Add(scan);
        await _db.SaveChangesAsync();

        var scanId = scan.Id;

        // Fire-and-forget background scan
        _ = Task.Run(() => RunScanInternalAsync(scanId, customerTenantId, clientId, clientSecret));

        return scanId;
    }

    // ── Background scan runner ──────────────────────────────────────

    private async Task RunScanInternalAsync(
        Guid scanId, string customerTenantId, string clientId, string clientSecret)
    {
        // Must create a NEW DbContext scope since this runs on a background thread
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KryossDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<CopilotReadinessService>>();

        try
        {
            var credential = new ClientSecretCredential(customerTenantId, clientId, clientSecret);
            var graph = new GraphServiceClient(credential);

            // Create authenticated HttpClients for non-Graph APIs
            var defenderHttp = await CreateAuthenticatedClient(
                credential, "https://api.security.microsoft.com/.default",
                "https://api.security.microsoft.com");
            var bapHttp = await CreateAuthenticatedClient(
                credential, "https://api.bap.microsoft.com/.default",
                "https://api.bap.microsoft.com");
            var flowHttp = await CreateAuthenticatedClient(
                credential, "https://api.flow.microsoft.com/.default",
                "https://api.flow.microsoft.com");
            var graphBetaHttp = await CreateAuthenticatedClient(
                credential, "https://graph.microsoft.com/.default",
                "https://graph.microsoft.com/beta");

            var ct = CancellationToken.None;

            // Run 6 pipelines in parallel
            var entraTask = EntraPipeline.RunAsync(graph, graphBetaHttp, log, ct);
            var defenderTask = DefenderPipeline.RunAsync(graph, defenderHttp, log, ct);
            var m365Task = M365Pipeline.RunAsync(graph, log, ct);
            var purviewTask = PurviewPipeline.RunAsync(graph, log, ct);
            var powerPlatformTask = PowerPlatformPipeline.RunAsync(graph, bapHttp, flowHttp, log, ct);
            var sharepointTask = SharePointDeepPipeline.RunAsync(graph, log, ct);

            await Task.WhenAll(entraTask, defenderTask, m365Task, purviewTask, powerPlatformTask, sharepointTask);

            var entraResult = entraTask.Result;
            var defenderResult = defenderTask.Result;
            var m365Result = m365Task.Result;
            var purviewResult = purviewTask.Result;
            var ppResult = powerPlatformTask.Result;
            var spResult = sharepointTask.Result;

            // Aggregate all findings
            var allFindings = new List<RecommendationResult>();
            allFindings.AddRange(entraResult.Findings);
            allFindings.AddRange(defenderResult.Findings);
            allFindings.AddRange(m365Result.Findings);
            allFindings.AddRange(purviewResult.Findings);
            allFindings.AddRange(ppResult.Findings);
            allFindings.AddRange(spResult.Findings);

            // Aggregate all metrics
            var allMetrics = new Dictionary<string, Dictionary<string, string>>();
            AddMetrics(allMetrics, "entra", entraResult.Metrics);
            AddMetrics(allMetrics, "defender", defenderResult.Metrics);
            AddMetrics(allMetrics, "m365", m365Result.Metrics);
            AddMetrics(allMetrics, "purview", purviewResult.Metrics);
            AddMetrics(allMetrics, "power_platform", ppResult.Metrics);
            AddMetrics(allMetrics, "sharepoint", spResult.Metrics);

            // Extract scoring inputs
            var labelPct = decimal.TryParse(
                spResult.Metrics.GetValueOrDefault("label_coverage_pct", "0"), out var lp) ? lp : 0;
            var oversharedPct = decimal.TryParse(
                spResult.Metrics.GetValueOrDefault("overshared_pct", "0"), out var op) ? op : 0;
            var highRiskExt = spResult.ExternalUsers.Count(u => u.RiskLevel == "High");
            var pendingInvites = int.TryParse(
                spResult.Metrics.GetValueOrDefault("pending_invitations", "0"), out var pi) ? pi : 0;
            var caCompat = decimal.TryParse(
                entraResult.Metrics.GetValueOrDefault("ca_compat_score_pct", "0"), out var cc) ? cc : 0;

            // D5 inputs from allFindings
            var entraGaps = allFindings.Count(f =>
                f.Service == "entra" &&
                (f.Status == "action_required" || f.Status == "warning") &&
                (f.Priority == "High" || f.Priority == "Medium" || f.Priority == "high" || f.Priority == "medium"));
            var defCrit = allFindings.Count(f =>
                f.Service == "defender" &&
                (f.Status == "Critical" || f.Status == "critical"));
            var defGaps = allFindings.Count(f =>
                f.Service == "defender" &&
                (f.Status == "action_required" || f.Status == "warning") &&
                (f.Priority == "High" || f.Priority == "Medium" || f.Priority == "high" || f.Priority == "medium"));
            var purviewGaps = allFindings.Count(f =>
                f.Service == "purview" &&
                (f.Status == "disabled" || f.Status == "action_required" || f.Status == "warning") &&
                (f.Priority == "High" || f.Priority == "high"));

            // Compute D1-D6 scores using ScoringEngine
            // D2 input: overshared count (from SP sites)
            var oversharedCount = spResult.SharepointSites.Sum(s => s.OversharedFiles);
            var scores = ScoringEngine.Compute(
                labelPct, oversharedCount, highRiskExt, pendingInvites,
                caCompat, entraGaps, defCrit, defGaps, purviewGaps);

            // Build pipeline_status JSON
            var pipelineStatus = JsonSerializer.Serialize(new
            {
                entra = entraResult.Status,
                defender = defenderResult.Status,
                m365 = m365Result.Status,
                purview = purviewResult.Status,
                power_platform = ppResult.Status,
                sharepoint = spResult.Status
            });

            // Update scan row
            var scan = await db.CopilotReadinessScans.FindAsync(scanId);
            if (scan is null) return;

            scan.D1Score = scores.D1Labels;
            scan.D2Score = scores.D2Oversharing;
            scan.D3Score = scores.D3External;
            scan.D4Score = scores.D4ConditionalAccess;
            scan.D5Score = scores.D5ZeroTrust;
            scan.D6Score = scores.D6Purview;
            scan.OverallScore = scores.Overall;
            scan.Verdict = scores.Verdict;
            scan.Status = "completed";
            scan.PipelineStatus = pipelineStatus;
            scan.CompletedAt = DateTime.UtcNow;

            // Persist findings
            var now = DateTime.UtcNow;
            foreach (var f in allFindings)
            {
                db.CopilotReadinessFindings.Add(new CopilotReadinessFinding
                {
                    ScanId = scanId,
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

            // Persist metrics
            foreach (var (dimension, metricsDict) in allMetrics)
            {
                foreach (var (key, value) in metricsDict)
                {
                    db.CopilotReadinessMetrics.Add(new CopilotReadinessMetric
                    {
                        ScanId = scanId,
                        Dimension = dimension,
                        MetricKey = key,
                        MetricValue = value,
                        CreatedAt = now
                    });
                }
            }

            // Persist SharePoint sites
            foreach (var site in spResult.SharepointSites)
            {
                db.CopilotReadinessSharepoint.Add(new CopilotReadinessSharepoint
                {
                    ScanId = scanId,
                    SiteUrl = site.SiteUrl,
                    SiteTitle = site.SiteTitle,
                    TotalFiles = site.TotalFiles,
                    LabeledFiles = site.LabeledFiles,
                    OversharedFiles = site.OversharedFiles,
                    RiskLevel = site.RiskLevel,
                    TopLabels = site.TopLabels.Count > 0
                        ? string.Join(", ", site.TopLabels) : null,
                    CreatedAt = now
                });
            }

            // Persist external users
            foreach (var user in spResult.ExternalUsers)
            {
                db.CopilotReadinessExternalUsers.Add(new CopilotReadinessExternalUser
                {
                    ScanId = scanId,
                    UserPrincipal = user.UserPrincipal,
                    DisplayName = user.DisplayName,
                    EmailDomain = user.EmailDomain,
                    LastSignIn = user.LastSignIn?.UtcDateTime,
                    RiskLevel = user.RiskLevel,
                    SitesAccessed = user.SitesAccessed,
                    HighestPermission = user.HighestPermission,
                    CreatedAt = now
                });
            }

            await db.SaveChangesAsync();

            log.LogInformation(
                "Copilot Readiness scan {ScanId} completed. Overall={Overall} Verdict={Verdict}",
                scanId, scores.Overall, scores.Verdict);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Copilot Readiness scan {ScanId} failed", scanId);

            try
            {
                var scan = await db.CopilotReadinessScans.FindAsync(scanId);
                if (scan is not null)
                {
                    scan.Status = "failed";
                    scan.CompletedAt = DateTime.UtcNow;
                    scan.PipelineStatus = JsonSerializer.Serialize(new { error = ex.Message });
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception innerEx)
            {
                log.LogError(innerEx, "Failed to mark scan {ScanId} as failed", scanId);
            }
        }
    }

    // ── GetLatestScanAsync ──────────────────────────────────────────

    public async Task<object?> GetLatestScanAsync(Guid organizationId)
    {
        var scan = await _db.CopilotReadinessScans
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                s.Id,
                s.Status,
                s.D1Score,
                s.D2Score,
                s.D3Score,
                s.D4Score,
                s.D5Score,
                s.D6Score,
                s.OverallScore,
                s.Verdict,
                s.PipelineStatus,
                s.StartedAt,
                s.CompletedAt,
                s.CreatedAt,
                FindingsSummary = _db.CopilotReadinessFindings
                    .Where(f => f.ScanId == s.Id)
                    .GroupBy(f => f.Service)
                    .Select(g => new
                    {
                        Service = g.Key,
                        Total = g.Count(),
                        ActionRequired = g.Count(f => f.Status == "action_required"),
                        Warning = g.Count(f => f.Status == "warning"),
                        Success = g.Count(f => f.Status == "success"),
                        Disabled = g.Count(f => f.Status == "disabled")
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        return scan;
    }

    // ── GetScanDetailAsync ──────────────────────────────────────────

    public async Task<object?> GetScanDetailAsync(Guid scanId)
    {
        var scan = await _db.CopilotReadinessScans
            .Where(s => s.Id == scanId)
            .Select(s => new
            {
                s.Id,
                s.OrganizationId,
                s.TenantId,
                s.Status,
                s.D1Score,
                s.D2Score,
                s.D3Score,
                s.D4Score,
                s.D5Score,
                s.D6Score,
                s.OverallScore,
                s.Verdict,
                s.PipelineStatus,
                s.StartedAt,
                s.CompletedAt,
                s.CreatedAt,
                Findings = _db.CopilotReadinessFindings
                    .Where(f => f.ScanId == s.Id)
                    .OrderBy(f => f.Service).ThenBy(f => f.Feature)
                    .Select(f => new
                    {
                        f.Service,
                        f.Feature,
                        f.Status,
                        f.Priority,
                        f.Observation,
                        f.Recommendation,
                        f.LinkText,
                        f.LinkUrl
                    })
                    .ToList(),
                Metrics = _db.CopilotReadinessMetrics
                    .Where(m => m.ScanId == s.Id)
                    .Select(m => new
                    {
                        m.Dimension,
                        m.MetricKey,
                        m.MetricValue
                    })
                    .ToList(),
                SharepointSites = _db.CopilotReadinessSharepoint
                    .Where(sp => sp.ScanId == s.Id)
                    .Select(sp => new
                    {
                        sp.SiteUrl,
                        sp.SiteTitle,
                        sp.TotalFiles,
                        sp.LabeledFiles,
                        sp.OversharedFiles,
                        sp.RiskLevel,
                        sp.TopLabels
                    })
                    .ToList(),
                ExternalUsers = _db.CopilotReadinessExternalUsers
                    .Where(eu => eu.ScanId == s.Id)
                    .Select(eu => new
                    {
                        eu.UserPrincipal,
                        eu.DisplayName,
                        eu.EmailDomain,
                        eu.LastSignIn,
                        eu.RiskLevel,
                        eu.SitesAccessed,
                        eu.HighestPermission
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        return scan;
    }

    // ── GetScanHistoryAsync ─────────────────────────────────────────

    public async Task<List<object>> GetScanHistoryAsync(Guid organizationId)
    {
        var history = await _db.CopilotReadinessScans
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(10)
            .Select(s => (object)new
            {
                s.Id,
                s.OverallScore,
                s.Verdict,
                s.Status,
                s.CreatedAt
            })
            .ToListAsync();

        return history;
    }

    // ── Private helpers ─────────────────────────────────────────────

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
    /// Mirrors M365Function.DecryptSecretForOrg.
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
