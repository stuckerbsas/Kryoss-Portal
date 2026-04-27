using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Agent;

/// <summary>
/// POST /api/v1/results
/// Agent uploads assessment results (encrypted payload).
/// API decrypts → evaluates PASS/FAIL server-side → stores in DB.
/// </summary>
public class ResultsFunction
{
    private const string EnvelopeContentType = "application/kryoss-envelope+json";

    private readonly KryossDbContext _db;
    private readonly IEvaluationService _evaluation;
    private readonly IActlogService _actlog;
    private readonly ICryptoService _crypto;
    private readonly ICurrentUserService _currentUser;
    private readonly IHwidVerifier _hwid;
    private readonly IPublicIpTracker _ipTracker;
    private readonly ISiteClusterService _siteCluster;
    private readonly ExternalScanService _extScan;
    private readonly ILogger<ResultsFunction> _logger;

    public ResultsFunction(
        KryossDbContext db,
        IEvaluationService evaluation,
        IActlogService actlog,
        ICryptoService crypto,
        ICurrentUserService currentUser,
        IHwidVerifier hwid,
        IPublicIpTracker ipTracker,
        ISiteClusterService siteCluster,
        ExternalScanService extScan,
        ILogger<ResultsFunction> logger)
    {
        _db = db;
        _evaluation = evaluation;
        _actlog = actlog;
        _crypto = crypto;
        _currentUser = currentUser;
        _hwid = hwid;
        _ipTracker = ipTracker;
        _siteCluster = siteCluster;
        _extScan = extScan;
        _logger = logger;
    }

    [Function("Results")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/results")] HttpRequestData req,
        FunctionContext context)
    {
        // ─── Identity is resolved from the HMAC-validated headers, NOT the body ────
        // The authenticated org comes from ApiKeyAuthMiddleware (X-Api-Key ->
        // organizations row). The specific machine comes from the X-Agent-Id
        // header, which is part of the HMAC canonical string and therefore
        // tamper-evident. We intentionally ignore whatever AgentId the body
        // payload claims — treating the body as 100% untrusted client input
        // prevents cross-tenant mass-assignment (P0 #3 of the security baseline).
        var agentIdHeader = req.Headers.TryGetValues("X-Agent-Id", out var agentIdValues)
            ? agentIdValues.FirstOrDefault() : null;
        if (!Guid.TryParse(agentIdHeader, out var authenticatedAgentId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "X-Agent-Id header is required" });
            return bad;
        }

        if (_currentUser.OrganizationId is null || _currentUser.OrganizationId == Guid.Empty)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "No organization context" });
            return unauth;
        }

        // Body may have been consumed by HMAC middleware — it stores raw
        // bytes in FunctionContext.Items["RequestBodyBytes"] for downstream.
        byte[] bodyBytes;
        if (context.Items.TryGetValue("RequestBodyBytes", out var bodyObj) && bodyObj is byte[] cached)
        {
            bodyBytes = cached;
        }
        else
        {
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms);
            bodyBytes = ms.ToArray();
        }

        // SH-05: reject oversized payloads (10 MB max — typical payload ~175 KB)
        const int MaxBodyBytes = 10 * 1024 * 1024;
        if (bodyBytes.Length > MaxBodyBytes)
        {
            var big = req.CreateResponse(HttpStatusCode.RequestEntityTooLarge);
            await big.WriteAsJsonAsync(new { error = "payload_too_large", maxBytes = MaxBodyBytes });
            return big;
        }

        // CRIT-04: Detect content type and enforce envelope encryption when the
        // org has a public key configured (meaning the agent has the key too).
        // Plaintext fallback is only allowed for orgs that haven't completed
        // the encryption rollout (no public key in org_crypto_keys).
        var contentType = req.Headers.TryGetValues("Content-Type", out var ctValues)
            ? ctValues.FirstOrDefault() ?? "application/json"
            : "application/json";

        AgentPayload? payload;
        if (contentType.StartsWith(EnvelopeContentType, StringComparison.OrdinalIgnoreCase))
        {
            // Envelope path — RSA-unwrap + AES-GCM decrypt + deserialize.
            // Org id comes from ApiKeyAuthMiddleware which already validated
            // the X-Api-Key against organizations.api_key.
            AgentEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<AgentEnvelope>(bodyBytes,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Malformed envelope JSON");
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Malformed envelope" });
                return bad;
            }

            if (envelope is null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Empty envelope" });
                return bad;
            }

            try
            {
                payload = await _crypto.DecryptEnvelopeAsync<AgentPayload>(
                    _currentUser.OrganizationId.Value, envelope);
            }
            catch (Exception ex)
            {
                // Crypto failures intentionally return a generic 401 — never
                // a 500, never a stack trace. We log everything server-side.
                _logger.LogWarning(ex, "Envelope decryption failed for org {OrgId}",
                    _currentUser.OrganizationId);
                var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauth.WriteAsJsonAsync(new { error = "Decryption failed" });
                return unauth;
            }
        }
        else
        {
            // CRIT-04: Check if this org has a public key configured.
            // If yes, the agent MUST use envelope encryption — reject plaintext.
            var orgHasPublicKey = await _db.OrgCryptoKeys
                .AnyAsync(k => k.OrganizationId == _currentUser.OrganizationId!.Value);

            if (orgHasPublicKey)
            {
                _logger.LogWarning(
                    "REJECTED plaintext payload from org {OrgId} — org has public key, envelope required",
                    _currentUser.OrganizationId);
                var reject = req.CreateResponse(HttpStatusCode.BadRequest);
                await reject.WriteAsJsonAsync(new { error = "Envelope encryption required. Upgrade agent." });
                return reject;
            }

            // Plaintext fallback — only for orgs that haven't completed encryption rollout.
            _logger.LogWarning(
                "Plaintext payload received from org {OrgId} — agent should be upgraded to envelope encryption",
                _currentUser.OrganizationId);
            payload = JsonSerializer.Deserialize<AgentPayload>(bodyBytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        if (payload is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid payload" });
            return bad;
        }

        // Defense-in-depth: if the body claims an AgentId, it MUST match
        // the HMAC-signed X-Agent-Id header. A mismatch means either a
        // replay attempt across machines or a tampered payload. Log and
        // reject. The header remains the single source of truth.
        if (payload.AgentId != Guid.Empty && payload.AgentId != authenticatedAgentId)
        {
            _logger.LogWarning(
                "Agent-Id mismatch — header={HeaderId}, body={BodyId}, org={OrgId}",
                authenticatedAgentId, payload.AgentId, _currentUser.OrganizationId);
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Agent identity mismatch" });
            return unauth;
        }

        // Find machine by (agentId, organizationId). The org filter is the
        // actual tenant boundary — without it, an attacker with one valid
        // API key could post results against another tenant's machines.
        var machine = await _db.Machines
            .FirstOrDefaultAsync(m => m.AgentId == authenticatedAgentId
                                      && m.OrganizationId == _currentUser.OrganizationId);

        if (machine is null)
        {
            _logger.LogWarning("ResultsFunction: unknown agent {AgentId} for org {OrgId}",
                authenticatedAgentId, _currentUser.OrganizationId);
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Agent not enrolled" });
            return notFound;
        }

        // Hardware fingerprint binding (see ControlsFunction for rationale).
        // The EvaluationService will SaveChanges further down, so any
        // backfill mutation on `machine` rides along with that save.
        var presentedHwid = req.Headers.TryGetValues("X-Hwid", out var hwidValues)
            ? hwidValues.FirstOrDefault() : null;
        if (_hwid.Verify(machine, presentedHwid) == HwidCheckResult.Mismatch)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Hardware binding mismatch" });
            return unauth;
        }

        if (machine.IsTrial && machine.TrialExpiresAt.HasValue && machine.TrialExpiresAt < DateTime.UtcNow)
        {
            var expired = req.CreateResponse(HttpStatusCode.Unauthorized);
            await expired.WriteAsJsonAsync(new { error = "Trial expired", trialExpiresAt = machine.TrialExpiresAt });
            return expired;
        }

        // Evaluate results server-side
        var run = await _evaluation.EvaluateAsync(machine.Id, machine.OrganizationId, payload);

        // Track public IP + auto-rebuild network sites (non-fatal)
        try
        {
            await _ipTracker.TrackAsync(machine.Id, _currentUser.IpAddress);
            await _siteCluster.RebuildSitesAsync(machine.OrganizationId);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "IP tracking / site rebuild failed for {MachineId}", machine.Id); }

        // Auto-trigger external scan on detected public IP (24h dedup)
        try
        {
            var publicIp = _currentUser.IpAddress;
            if (!string.IsNullOrEmpty(publicIp))
            {
                var recentScan = await _db.ExternalScans
                    .AnyAsync(s => s.OrganizationId == machine.OrganizationId
                                   && s.Target == publicIp
                                   && s.CreatedAt > DateTime.UtcNow.AddHours(-24));
                if (!recentScan)
                {
                    var extScan = new ExternalScan
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = machine.OrganizationId,
                        Target = publicIp,
                        Status = "pending",
                        CreatedBy = Guid.Empty,
                        CreatedAt = DateTime.UtcNow,
                    };
                    _db.ExternalScans.Add(extScan);
                    await _db.SaveChangesAsync();
                    await _extScan.RunScanAsync(extScan.Id);
                    _logger.LogInformation("Auto external scan {ScanId} for IP {Ip}", extScan.Id, publicIp);
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Auto external scan failed for {MachineId}", machine.Id); }

        await _actlog.LogAsync("INFO", "assessment", "assessment.run.completed",
            $"Assessment completed for {machine.Hostname}: {run.Grade} ({run.GlobalScore}%)",
            entityType: "AssessmentRun", entityId: run.Id.ToString());

        _logger.LogInformation("Assessment run {RunId} for {Hostname}: {Grade} ({Score}%)",
            run.Id, machine.Hostname, run.Grade, run.GlobalScore);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            runId = run.Id,
            score = run.GlobalScore,
            grade = run.Grade,
            passCount = run.PassCount,
            warnCount = run.WarnCount,
            failCount = run.FailCount,
            yourPublicIp = _currentUser.IpAddress,
            speedtestRequested = false,
        });
        return response;
    }
}
