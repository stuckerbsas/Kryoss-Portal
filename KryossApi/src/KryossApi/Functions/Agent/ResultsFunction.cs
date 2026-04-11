using System.Net;
using System.Text.Json;
using KryossApi.Data;
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
    private readonly ILogger<ResultsFunction> _logger;

    public ResultsFunction(
        KryossDbContext db,
        IEvaluationService evaluation,
        IActlogService actlog,
        ICryptoService crypto,
        ICurrentUserService currentUser,
        IHwidVerifier hwid,
        ILogger<ResultsFunction> logger)
    {
        _db = db;
        _evaluation = evaluation;
        _actlog = actlog;
        _crypto = crypto;
        _currentUser = currentUser;
        _hwid = hwid;
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

        // Detect content type. If it's an envelope we MUST decrypt; if it's
        // plaintext JSON we accept it during the rollout window but log a
        // warning so we can track adoption. Once all agents are upgraded
        // (tracked in security-baseline.md backlog), the plaintext branch
        // becomes a hard reject.
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
            // Plaintext fallback (rollout window). Will be removed.
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

        // Evaluate results server-side
        var run = await _evaluation.EvaluateAsync(machine.Id, machine.OrganizationId, payload);

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
            failCount = run.FailCount
        });
        return response;
    }
}
