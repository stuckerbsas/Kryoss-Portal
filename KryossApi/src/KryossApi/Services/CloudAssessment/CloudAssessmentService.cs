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

            AddActlog(db, "info", "scan.tokens.acquired", scanId,
                $"Tokens: beta={graphBetaHttp != null}");
            await db.SaveChangesAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var ct = cts.Token;

            // CA-1: Identity is the only pipeline. CA-2+ will add more.
            var identityTask = TrackPipeline("identity",
                () => IdentityPipeline.RunAsync(graph, graphBetaHttp, log, ct),
                scanId, db, log);

            try { await identityTask; }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                log.LogError("CA scan {ScanId} hit 10-min timeout", scanId);
                throw new TimeoutException("Pipeline execution exceeded 10 minutes");
            }

            var identityResult = identityTask.Result;

            // Aggregate findings + metrics by area.
            var allFindings = new List<RecommendationResult>(identityResult.Findings);

            var allMetrics = new Dictionary<string, Dictionary<string, string>>();
            AddMetrics(allMetrics, "identity", identityResult.Metrics);

            // Compute Identity area score (0.0-5.0 scale matching verdict system).
            var identityScore = ComputeIdentityAreaScore(identityResult.Metrics, allFindings);

            var areaScores = JsonSerializer.Serialize(new Dictionary<string, decimal>
            {
                ["identity"] = identityScore
            });

            var pipelineStatus = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["identity"] = identityResult.Status
            });

            // Overall = identity for CA-1; later phases will average across areas.
            var overallScore = identityScore;
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

            // Persist findings — CloudAssessmentFinding has an Area column on top
            // of CopilotReadinessFinding's shape, so tag every row with "identity".
            var now = DateTime.UtcNow;
            foreach (var f in allFindings)
            {
                db.CloudAssessmentFindings.Add(new CloudAssessmentFinding
                {
                    ScanId = scanId,
                    Area = "identity",
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
                $"CA scan completed identityScore={identityScore} findings={allFindings.Count}");
            await db.SaveChangesAsync();

            log.LogInformation(
                "Cloud Assessment scan {ScanId} completed. Identity={Identity} Verdict={Verdict}",
                scanId, identityScore, verdict);
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

    public async Task<List<object>> GetScanHistoryAsync(Guid organizationId)
    {
        var history = await _db.CloudAssessmentScans
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
