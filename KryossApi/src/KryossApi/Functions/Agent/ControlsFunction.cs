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
/// GET /api/v1/controls?assessmentId=X
///
/// Agent downloads check instructions. Only sends what the agent needs:
/// id, type, engine-specific fields, display message.
/// Does NOT send: expected values, severity, remediation, names.
///
/// Phase 1 scope enforcement:
///   The response is filtered by the calling machine's platform. The
///   machine is resolved via the X-Agent-Id header, and its platform_id
///   was set at enrollment time by the PlatformResolver service from
///   os_name. Servers (no platform match today) get an empty list, which
///   is the intended Phase 1 behavior — agent then exits with code 2.
/// </summary>
public class ControlsFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IHwidVerifier _hwid;
    private readonly ILogger<ControlsFunction> _logger;

    public ControlsFunction(
        KryossDbContext db,
        ICurrentUserService user,
        IHwidVerifier hwid,
        ILogger<ControlsFunction> logger)
    {
        _db = db;
        _user = user;
        _hwid = hwid;
        _logger = logger;
    }

    [Function("Controls")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/controls")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var assessmentIdStr = query["assessmentId"];

        if (!int.TryParse(assessmentIdStr, out var assessmentId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "assessmentId query parameter is required" });
            return bad;
        }

        // Identify the calling machine via X-Agent-Id header.
        var agentIdHeader = req.Headers.TryGetValues("X-Agent-Id", out var agentIdValues)
            ? agentIdValues.FirstOrDefault() : null;

        if (!Guid.TryParse(agentIdHeader, out var agentId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "X-Agent-Id header is required" });
            return bad;
        }

        // Resolve machine (must belong to the authenticated org). We load
        // the full entity (not a projection) so the hwid backfill path can
        // mutate + SaveChanges. The row is small.
        var machine = await _db.Machines
            .FirstOrDefaultAsync(m =>
                m.AgentId == agentId && m.OrganizationId == _user.OrganizationId);

        if (machine is null)
        {
            _logger.LogWarning("ControlsFunction: unknown agent {AgentId} for org {OrgId}",
                agentId, _user.OrganizationId);
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Machine not enrolled" });
            return notFound;
        }

        // Hardware fingerprint binding — rejects token cloning across
        // machines once the fleet has been through one enrollment cycle.
        var presentedHwid = req.Headers.TryGetValues("X-Hwid", out var hwidValues)
            ? hwidValues.FirstOrDefault() : null;
        var hwidResult = _hwid.Verify(machine, presentedHwid);
        if (hwidResult == HwidCheckResult.Mismatch)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Hardware binding mismatch" });
            return unauth;
        }
        // Backfill or no-op — persist the mutation (if any) alongside the
        // lookup. SaveChangesAsync is a noop when nothing is dirty.
        await _db.SaveChangesAsync();

        // Phase 1 scope policy: if we could not resolve a platform from
        // the OS string at enrollment, return an empty control list with
        // 200 OK. The agent will log "no controls" and exit code 2.
        if (machine.PlatformId is null)
        {
            _logger.LogInformation("ControlsFunction: machine {MachineId} has no platform scope; returning empty list",
                machine.Id);
            var empty = req.CreateResponse(HttpStatusCode.OK);
            await empty.WriteAsJsonAsync(new
            {
                version = DateTime.UtcNow.ToString("yyyy.MM.dd"),
                checks = Array.Empty<object>()
            });
            return empty;
        }

        // Load active controls that belong to the assessment AND are
        // mapped to the caller's platform. Single query, one round-trip.
        var controls = await (
            from ac in _db.AssessmentControls
            where ac.AssessmentId == assessmentId
            join cd in _db.ControlDefs on ac.ControlDefId equals cd.Id
            where cd.IsActive
            join cp in _db.ControlPlatforms
                on new { CdId = cd.Id, PId = machine.PlatformId.Value }
                equals new { CdId = cp.ControlDefId, PId = cp.PlatformId }
            select new { cd.ControlId, cd.Type, cd.Id }
        ).ToListAsync();

        var controlDefIds = controls.Select(c => c.Id).ToList();
        var allParams = await _db.ControlCheckParams
            .Where(p => controlDefIds.Contains(p.ControlDefId))
            .ToListAsync();
        var paramsByDef = allParams.GroupBy(p => p.ControlDefId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Server-only fields — strip from agent response
        HashSet<string> serverOnlyFields = ["expected", "operator", "missingBehavior", "expectedStartType", "optional"];

        // Fields that must always serialize as JSON strings even if the DB value looks numeric.
        // Agent ControlDef model declares these as string? — sending a JSON number causes deserialization crash.
        HashSet<string> alwaysStringFields = [
            "valueName", "hive", "path", "settingName", "subcategory", "profile",
            "logName", "storeName", "storeLocation", "drive", "serviceName",
            "parent", "display", "executable", "arguments", "field",
            "checkType", "protocol", "side", "privilege", "collection",
            "expectedSidsOrAccounts", "notes", "label", "payloadField"
        ];

        var checks = new List<object>();
        foreach (var control in controls)
        {
            var agentCheck = new Dictionary<string, object?>
            {
                ["id"] = control.ControlId,
                ["type"] = control.Type
            };

            if (paramsByDef.TryGetValue(control.Id, out var parms))
            {
                foreach (var p in parms)
                {
                    if (serverOnlyFields.Contains(p.ParamName)) continue;
                    if (p.ParamValue is null) continue;

                    if (alwaysStringFields.Contains(p.ParamName))
                    {
                        agentCheck[p.ParamName] = p.ParamValue;
                    }
                    else if (int.TryParse(p.ParamValue, out var intVal))
                        agentCheck[p.ParamName] = intVal;
                    else if (bool.TryParse(p.ParamValue, out var boolVal))
                        agentCheck[p.ParamName] = boolVal;
                    else if (p.ParamValue.StartsWith('['))
                    {
                        try { agentCheck[p.ParamName] = JsonSerializer.Deserialize<JsonElement>(p.ParamValue); }
                        catch { agentCheck[p.ParamName] = p.ParamValue; }
                    }
                    else
                        agentCheck[p.ParamName] = p.ParamValue;
                }
            }

            checks.Add(agentCheck);
        }

        _logger.LogInformation("ControlsFunction: returning {Count} controls for agent {AgentId} platform {PlatformId}",
            checks.Count, agentId, machine.PlatformId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            version = DateTime.UtcNow.ToString("yyyy.MM.dd"),
            checks
        });
        return response;
    }
}
