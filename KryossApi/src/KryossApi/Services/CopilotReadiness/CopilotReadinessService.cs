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

        // Run scan synchronously — Azure Functions fire-and-forget kills background tasks
        // on worker recycle. func-kryoss is on App Service plan (30-min HTTP timeout).
        await RunScanInternalAsync(scanId, customerTenantId, clientId, clientSecret);

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
            // Proof-of-life: background task started
            db.Actlog.Add(new Data.Entities.Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "copilot-readiness",
                Action = "scan.background.started",
                EntityType = "CopilotReadinessScan",
                EntityId = scanId.ToString(),
                Message = "Background scan worker started"
            });
            await db.SaveChangesAsync();

            var credential = new ClientSecretCredential(customerTenantId, clientId, clientSecret);
            var graph = new GraphServiceClient(credential);

            // Create authenticated HttpClients for non-Graph APIs (gracefully handle missing service principals)
            var defenderHttp = await TryCreateAuthenticatedClient(
                credential, "https://api.security.microsoft.com/.default",
                "https://api.security.microsoft.com", log);
            var bapHttp = await TryCreateAuthenticatedClient(
                credential, "https://api.bap.microsoft.com/.default",
                "https://api.bap.microsoft.com", log);
            var flowHttp = await TryCreateAuthenticatedClient(
                credential, "https://api.flow.microsoft.com/.default",
                "https://api.flow.microsoft.com", log);
            var graphBetaHttp = await TryCreateAuthenticatedClient(
                credential, "https://graph.microsoft.com/.default",
                "https://graph.microsoft.com/beta", log);

            // 10-min timeout on whole pipeline phase — prevent hang
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var ct = cts.Token;

            // Actlog: token phase done
            db.Actlog.Add(new Data.Entities.Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "copilot-readiness",
                Action = "scan.tokens.acquired",
                EntityType = "CopilotReadinessScan",
                EntityId = scanId.ToString(),
                Message = $"Tokens: defender={defenderHttp != null} bap={bapHttp != null} flow={flowHttp != null} beta={graphBetaHttp != null}"
            });
            await db.SaveChangesAsync();

            // Run 6 pipelines in parallel, track each individually
            var entraTask = TrackPipeline("entra", () => EntraPipeline.RunAsync(graph, graphBetaHttp, log, ct), scanId, db, log);
            var defenderTask = TrackPipeline("defender", () => DefenderPipeline.RunAsync(graph, defenderHttp, log, ct), scanId, db, log);
            var m365Task = TrackPipeline("m365", () => M365Pipeline.RunAsync(graph, log, ct), scanId, db, log);
            var purviewTask = TrackPipeline("purview", () => PurviewPipeline.RunAsync(graph, log, ct), scanId, db, log);
            var powerPlatformTask = TrackPipeline("power_platform", () => PowerPlatformPipeline.RunAsync(graph, bapHttp, flowHttp, log, ct), scanId, db, log);
            var sharepointTask = TrackPipeline("sharepoint", () => SharePointDeepPipeline.RunAsync(graph, log, ct), scanId, db, log);

            try
            {
                await Task.WhenAll(entraTask, defenderTask, m365Task, purviewTask, powerPlatformTask, sharepointTask);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                log.LogError("Scan {ScanId} hit 10-min timeout", scanId);
                throw new TimeoutException("Pipeline execution exceeded 10 minutes");
            }

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

            // Actlog success entry
            db.Actlog.Add(new Data.Entities.Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "copilot-readiness",
                Action = "scan.completed",
                EntityType = "CopilotReadinessScan",
                EntityId = scanId.ToString(),
                Message = $"Scan completed. Overall={scores.Overall} Verdict={scores.Verdict}"
            });
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
                // Clear change tracker — context may be poisoned with failed inserts
                db.ChangeTracker.Clear();

                // Use fresh fetch to get untracked scan row
                var scan = await db.CopilotReadinessScans.FindAsync(scanId);
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

                // Write actlog entry so failure visible in portal actlog view
                db.Actlog.Add(new Data.Entities.Actlog
                {
                    Timestamp = DateTime.UtcNow,
                    Severity = "error",
                    Module = "copilot-readiness",
                    Action = "scan.failed",
                    EntityType = "CopilotReadinessScan",
                    EntityId = scanId.ToString(),
                    Message = $"Scan failed: {ex.Message}"
                        + (ex.InnerException is not null ? $" (inner: {ex.InnerException.Message})" : "")
                });
                await db.SaveChangesAsync();
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
            .FirstOrDefaultAsync();

        if (scan is null) return null;

        // Separate query for findings summary (EF can't translate GroupBy inside projection)
        var findingsSummary = await _db.CopilotReadinessFindings
            .Where(f => f.ScanId == scan.Id)
            .GroupBy(f => f.Service)
            .Select(g => new
            {
                Service = g.Key,
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
            scan.D1Score,
            scan.D2Score,
            scan.D3Score,
            scan.D4Score,
            scan.D5Score,
            scan.D6Score,
            scan.OverallScore,
            scan.Verdict,
            PipelineStatus = ParsePipelineStatus(scan.PipelineStatus),
            scan.StartedAt,
            scan.CompletedAt,
            scan.CreatedAt,
            FindingsSummary = findingsSummary
        };
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

    // ── GetScanDetailAsync ──────────────────────────────────────────

    public async Task<object?> GetScanDetailAsync(Guid scanId)
    {
        var scan = await _db.CopilotReadinessScans
            .FirstOrDefaultAsync(s => s.Id == scanId);

        if (scan is null) return null;

        var findings = await _db.CopilotReadinessFindings
            .Where(f => f.ScanId == scanId)
            .OrderBy(f => f.Service).ThenBy(f => f.Feature)
            .Select(f => new
            {
                f.Service, f.Feature, f.Status, f.Priority,
                f.Observation, f.Recommendation, f.LinkText, f.LinkUrl
            })
            .ToListAsync();

        var metrics = await _db.CopilotReadinessMetrics
            .Where(m => m.ScanId == scanId)
            .Select(m => new { m.Dimension, m.MetricKey, m.MetricValue })
            .ToListAsync();

        var spSites = await _db.CopilotReadinessSharepoint
            .Where(sp => sp.ScanId == scanId)
            .Select(sp => new
            {
                sp.SiteUrl, sp.SiteTitle, sp.TotalFiles,
                sp.LabeledFiles, sp.OversharedFiles, sp.RiskLevel, sp.TopLabels
            })
            .ToListAsync();

        var extUsers = await _db.CopilotReadinessExternalUsers
            .Where(eu => eu.ScanId == scanId)
            .Select(eu => new
            {
                eu.UserPrincipal, eu.DisplayName, eu.EmailDomain,
                eu.LastSignIn, eu.RiskLevel, eu.SitesAccessed, eu.HighestPermission
            })
            .ToListAsync();

        return new
        {
            scan.Id, scan.OrganizationId, scan.TenantId,
            scan.Status, scan.D1Score, scan.D2Score, scan.D3Score,
            scan.D4Score, scan.D5Score, scan.D6Score,
            scan.OverallScore, scan.Verdict,
            PipelineStatus = ParsePipelineStatus(scan.PipelineStatus),
            scan.StartedAt, scan.CompletedAt, scan.CreatedAt,
            Findings = findings,
            Metrics = metrics,
            SharepointSites = spSites,
            ExternalUsers = extUsers
        };
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
    private static async Task<Pipelines.PipelineResult> TrackPipeline(
        string name,
        Func<Task<Pipelines.PipelineResult>> pipelineFn,
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
                db.Actlog.Add(new Data.Entities.Actlog
                {
                    Timestamp = DateTime.UtcNow,
                    Severity = "info",
                    Module = "copilot-readiness",
                    Action = $"pipeline.{name}.done",
                    EntityType = "CopilotReadinessScan",
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

            // Fire-and-forget actlog write (best effort, don't break scan)
            try
            {
                db.Actlog.Add(new Data.Entities.Actlog
                {
                    Timestamp = DateTime.UtcNow,
                    Severity = "error",
                    Module = "copilot-readiness",
                    Action = $"pipeline.{name}.failed",
                    EntityType = "CopilotReadinessScan",
                    EntityId = scanId.ToString(),
                    Message = $"Pipeline {name} crashed in {sw.ElapsedMilliseconds}ms: {ex.Message}"
                });
                await db.SaveChangesAsync();
            }
            catch { /* swallow actlog errors */ }

            return new Pipelines.PipelineResult
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
