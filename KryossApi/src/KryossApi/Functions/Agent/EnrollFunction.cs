using System.Collections.Concurrent;
using System.Net;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Agent;

public class EnrollFunction
{
    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _rateLimitCache = new();
    private const int RateLimitMax = 30;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(15);

    private readonly IEnrollmentService _enrollment;
    private readonly IActlogService _actlog;
    private readonly ILogger<EnrollFunction> _logger;

    public EnrollFunction(IEnrollmentService enrollment, IActlogService actlog, ILogger<EnrollFunction> logger)
    {
        _enrollment = enrollment;
        _actlog = actlog;
        _logger = logger;
    }

    [Function("Enroll")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/enroll")] HttpRequestData req)
    {
        // IP-based rate limiting: 5 attempts per 15 minutes
        var clientIp = req.Headers.TryGetValues("X-Forwarded-For", out var fwdValues)
            ? (fwdValues.FirstOrDefault()?.Split(',')[0].Trim() ?? "unknown")
            : "unknown";

        var now = DateTime.UtcNow;
        var entry = _rateLimitCache.AddOrUpdate(
            clientIp,
            _ => (1, now),
            (_, existing) =>
            {
                if (now - existing.WindowStart > RateLimitWindow)
                    return (1, now);
                return (existing.Count + 1, existing.WindowStart);
            });

        if (entry.Count > RateLimitMax)
        {
            var tooMany = req.CreateResponse(HttpStatusCode.TooManyRequests);
            await tooMany.WriteAsJsonAsync(new { error = "Too many enrollment attempts. Please try again later." });
            return tooMany;
        }

        var body = await req.ReadFromJsonAsync<EnrollRequest>();
        if (body is null || string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.Hostname))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "code and hostname are required" });
            return bad;
        }

        // Hardware fingerprint — sent via X-Hwid header, NOT via the body.
        // Keeping it out of the JSON avoids any mass-assignment ambiguity
        // and lets future requests reuse the same header convention.
        var hwid = req.Headers.TryGetValues("X-Hwid", out var hwidValues)
            ? hwidValues.FirstOrDefault() : null;
        if (!string.IsNullOrWhiteSpace(hwid) && hwid.Length > 128)
            hwid = hwid[..128]; // defensive cap matches column width

        var result = await _enrollment.RedeemCodeAsync(
            body.Code, body.Hostname, body.Os, body.OsVersion, body.OsBuild, hwid, body.ProductType);
        if (result is null)
        {
            await _actlog.LogAsync("SEC", "agent", "enrollment.failed",
                $"Invalid or expired enrollment code attempted: {body.Code[..4]}***",
                entityType: "EnrollmentCode");

            var gone = req.CreateResponse(HttpStatusCode.Gone);
            await gone.WriteAsJsonAsync(new { error = "Code is invalid, expired, or already used" });
            return gone;
        }

        await _actlog.LogAsync("SEC", "agent", "machine.enrolled",
            $"Machine '{body.Hostname}' enrolled successfully",
            entityType: "Machine", entityId: result.AgentId.ToString());

        _logger.LogInformation("Machine {Hostname} enrolled with agent {AgentId}", body.Hostname, result.AgentId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            agentId = result.AgentId,
            apiKey = result.ApiKey,
            apiSecret = result.ApiSecret,
            publicKey = result.PublicKeyPem,
            assessmentId = result.AssessmentId,
            assessmentName = result.AssessmentName,
            protocolAuditEnabled = result.ProtocolAuditEnabled,
            isTrial = result.IsTrial,
            trialExpiresAt = result.TrialExpiresAt,
            organizationId = result.OrganizationId,
            machineSecret = result.MachineSecret,
            sessionKey = result.SessionKey,
            sessionKeyExpiresAt = result.SessionKeyExpiresAt
        });
        return response;
    }
}

public class EnrollRequest
{
    public string Code { get; set; } = null!;
    public string Hostname { get; set; } = null!;
    public string? Os { get; set; }
    public string? OsVersion { get; set; }
    public string? OsBuild { get; set; }
    public int? ProductType { get; set; }
}
