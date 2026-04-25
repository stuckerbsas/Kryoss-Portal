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
/// POST /v1/collect
/// Offline collection endpoint. A collector machine uploads payloads on behalf
/// of machines without internet access. Authenticates via collector's own API key.
/// Auto-enrolls unknown machines using the provided enrollment code.
/// </summary>
public class CollectFunction
{
    private readonly KryossDbContext _db;
    private readonly IEvaluationService _evaluation;
    private readonly IEnrollmentService _enrollment;
    private readonly IActlogService _actlog;
    private readonly ICurrentUserService _currentUser;
    private readonly IPublicIpTracker _ipTracker;
    private readonly ISiteClusterService _siteCluster;
    private readonly ExternalScanService _extScan;
    private readonly ILogger<CollectFunction> _logger;

    public CollectFunction(
        KryossDbContext db,
        IEvaluationService evaluation,
        IEnrollmentService enrollment,
        IActlogService actlog,
        ICurrentUserService currentUser,
        IPublicIpTracker ipTracker,
        ISiteClusterService siteCluster,
        ExternalScanService extScan,
        ILogger<CollectFunction> logger)
    {
        _db = db;
        _evaluation = evaluation;
        _enrollment = enrollment;
        _actlog = actlog;
        _currentUser = currentUser;
        _ipTracker = ipTracker;
        _siteCluster = siteCluster;
        _extScan = extScan;
        _logger = logger;
    }

    [Function("Collect")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/collect")] HttpRequestData req,
        FunctionContext context)
    {
        if (_currentUser.OrganizationId is null || _currentUser.OrganizationId == Guid.Empty)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "No organization context" });
            return unauth;
        }

        var orgId = _currentUser.OrganizationId.Value;

        byte[] bodyBytes;
        if (context.Items.TryGetValue("RequestBodyBytes", out var bodyObj) && bodyObj is byte[] cached)
            bodyBytes = cached;
        else
        {
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms);
            bodyBytes = ms.ToArray();
        }

        CollectPayload? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CollectPayload>(bodyBytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Malformed collect payload");
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Malformed JSON" });
            return bad;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Hostname))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "hostname required" });
            return bad;
        }

        if (envelope.Payload is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "payload required" });
            return bad;
        }

        // Find existing machine by hostname + org
        var machine = await _db.Machines
            .FirstOrDefaultAsync(m => m.Hostname == envelope.Hostname
                                      && m.OrganizationId == orgId);

        // Auto-enroll if not found
        if (machine is null)
        {
            if (string.IsNullOrWhiteSpace(envelope.EnrollmentCode))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Machine not enrolled and no enrollmentCode provided" });
                return bad;
            }

            var enrollResult = await _enrollment.RedeemCodeAsync(
                envelope.EnrollmentCode,
                envelope.Hostname,
                envelope.OsName,
                envelope.OsVersion,
                envelope.OsBuild,
                envelope.Hwid,
                envelope.ProductType);

            if (enrollResult is null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Enrollment failed — invalid or exhausted code" });
                return bad;
            }

            machine = await _db.Machines
                .FirstOrDefaultAsync(m => m.Hostname == envelope.Hostname
                                          && m.OrganizationId == orgId);

            if (machine is null)
            {
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteAsJsonAsync(new { error = "Enrollment succeeded but machine not found" });
                return err;
            }

            _logger.LogInformation("Collect: auto-enrolled {Hostname} for org {OrgId}",
                envelope.Hostname, orgId);
        }

        // Process payload
        var run = await _evaluation.EvaluateAsync(machine.Id, orgId, envelope.Payload);

        // Track collector's IP as the machine's public IP (best we have for offline machines)
        try
        {
            await _ipTracker.TrackAsync(machine.Id, _currentUser.IpAddress);
            await _siteCluster.RebuildSitesAsync(orgId);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "IP tracking / site rebuild failed for collected {MachineId}", machine.Id); }

        // Auto-trigger external scan on detected public IP (24h dedup)
        try
        {
            var publicIp = _currentUser.IpAddress;
            if (!string.IsNullOrEmpty(publicIp))
            {
                var recentScan = await _db.ExternalScans
                    .AnyAsync(s => s.OrganizationId == orgId
                                   && s.Target == publicIp
                                   && s.CreatedAt > DateTime.UtcNow.AddHours(-24));
                if (!recentScan)
                {
                    var extScan = new ExternalScan
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = orgId,
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
        catch (Exception ex) { _logger.LogWarning(ex, "Auto external scan failed for collected {MachineId}", machine.Id); }

        await _actlog.LogAsync("INFO", "assessment", "assessment.collected",
            $"Offline collect for {machine.Hostname}: {run.Grade} ({run.GlobalScore}%)",
            entityType: "AssessmentRun", entityId: run.Id.ToString());

        _logger.LogInformation("Collected run {RunId} for {Hostname}: {Grade} ({Score}%)",
            run.Id, machine.Hostname, run.Grade, run.GlobalScore);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            runId = run.Id,
            hostname = machine.Hostname,
            score = run.GlobalScore,
            grade = run.Grade,
            enrolled = true,
        });
        return response;
    }
}

internal class CollectPayload
{
    public string Hostname { get; set; } = "";
    public string? Hwid { get; set; }
    public string? EnrollmentCode { get; set; }
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public string? OsBuild { get; set; }
    public int? ProductType { get; set; }
    public string? AgentVersion { get; set; }
    public string? CollectedAt { get; set; }
    public AgentPayload? Payload { get; set; }
}
