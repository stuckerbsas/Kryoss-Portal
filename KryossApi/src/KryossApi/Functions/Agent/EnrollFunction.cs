using System.Net;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Agent;

public class EnrollFunction
{
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
            body.Code, body.Hostname, body.Os, body.OsVersion, body.OsBuild, hwid);
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
            protocolAuditEnabled = result.ProtocolAuditEnabled
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
}
