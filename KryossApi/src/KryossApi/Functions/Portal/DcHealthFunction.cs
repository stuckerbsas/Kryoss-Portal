using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class DcHealthFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public DcHealthFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    [Function("DcHealth_Submit")]
    public async Task<HttpResponseData> Submit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/dc-health")] HttpRequestData req,
        FunctionContext context)
    {
        DcHealthSubmitRequest? body = null;
        if (context.Items.TryGetValue("RequestBodyBytes", out var rawObj) && rawObj is byte[] rawBytes && rawBytes.Length > 0)
            body = JsonSerializer.Deserialize<DcHealthSubmitRequest>(rawBytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        else
            body = await req.ReadFromJsonAsync<DcHealthSubmitRequest>();

        if (body is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid DC health payload" });
            return bad;
        }

        var orgId = _user.OrganizationId;
        if (orgId is null)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Organization context required" });
            return unauth;
        }

        var agentIdHeader = req.Headers.TryGetValues("X-Agent-Id", out var vals) ? vals.FirstOrDefault() : null;
        Guid? machineId = null;
        if (Guid.TryParse(agentIdHeader, out var mid))
        {
            var machine = await _db.Machines.FirstOrDefaultAsync(m => m.Id == mid && m.OrganizationId == orgId.Value);
            if (machine != null) machineId = machine.Id;
        }
        if (machineId is null)
        {
            var machine = await _db.Machines
                .Where(m => m.OrganizationId == orgId.Value && m.Hostname == body.ScannedBy)
                .FirstOrDefaultAsync();
            machineId = machine?.Id;
        }

        if (machineId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Machine not found" });
            return bad;
        }

        var snapshot = new DcHealthSnapshot
        {
            Id = Guid.NewGuid(),
            MachineId = machineId.Value,
            OrganizationId = orgId.Value,
            SchemaVersion = body.SchemaVersion,
            SchemaVersionLabel = body.SchemaVersionLabel,
            ForestLevel = body.ForestLevel,
            DomainLevel = body.DomainLevel,
            ForestName = body.ForestName,
            DomainName = body.DomainName,
            SchemaMaster = body.SchemaMaster,
            DomainNamingMaster = body.DomainNamingMaster,
            PdcEmulator = body.PdcEmulator,
            RidMaster = body.RidMaster,
            InfrastructureMaster = body.InfrastructureMaster,
            FsmoSinglePoint = body.FsmoSinglePoint,
            ReplPartnerCount = body.ReplPartnerCount,
            ReplFailureCount = body.ReplFailureCount,
            LastSuccessfulRepl = body.LastSuccessfulRepl,
            SiteCount = body.SiteCount,
            SubnetCount = body.SubnetCount,
            DcCount = body.DcCount,
            GcCount = body.GcCount,
            ScannedAt = DateTime.UtcNow,
            ScannedBy = body.ScannedBy,
        };

        _db.DcHealthSnapshots.Add(snapshot);

        if (body.ReplicationPartners != null)
        {
            foreach (var rp in body.ReplicationPartners)
            {
                _db.DcReplicationPartners.Add(new DcReplicationPartner
                {
                    Id = Guid.NewGuid(),
                    SnapshotId = snapshot.Id,
                    PartnerHostname = rp.PartnerHostname,
                    PartnerDn = rp.PartnerDn,
                    Direction = rp.Direction,
                    NamingContext = rp.NamingContext,
                    LastSuccess = rp.LastSuccess,
                    LastAttempt = rp.LastAttempt,
                    FailureCount = rp.FailureCount,
                    LastError = rp.LastError,
                    Transport = rp.Transport,
                });
            }
        }

        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { snapshotId = snapshot.Id, partners = body.ReplicationPartners?.Count ?? 0 });
        return response;
    }

    [Function("DcHealth_Get")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/dc-health")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        Guid orgId;
        if (Guid.TryParse(orgIdStr, out var parsed))
            orgId = parsed;
        else if (_user.OrganizationId.HasValue)
            orgId = _user.OrganizationId.Value;
        else
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        if (!_user.IsAdmin)
        {
            var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
                await _db.Organizations.AnyAsync(o => o.Id == orgId && o.FranchiseId == _user.FranchiseId.Value);
            var orgBelongsToUser = _user.OrganizationId.HasValue && orgId == _user.OrganizationId.Value;
            if (!orgBelongsToFranchise && !orgBelongsToUser)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        var snapshots = await _db.DcHealthSnapshots
            .Where(s => s.OrganizationId == orgId)
            .OrderByDescending(s => s.ScannedAt)
            .Take(20)
            .Select(s => new
            {
                s.Id,
                s.MachineId,
                machineHostname = s.Machine.Hostname,
                s.SchemaVersion,
                s.SchemaVersionLabel,
                s.ForestLevel,
                s.DomainLevel,
                s.ForestName,
                s.DomainName,
                s.SchemaMaster,
                s.DomainNamingMaster,
                s.PdcEmulator,
                s.RidMaster,
                s.InfrastructureMaster,
                s.FsmoSinglePoint,
                s.ReplPartnerCount,
                s.ReplFailureCount,
                s.LastSuccessfulRepl,
                s.SiteCount,
                s.SubnetCount,
                s.DcCount,
                s.GcCount,
                s.ScannedAt,
                s.ScannedBy,
                replicationPartners = s.ReplicationPartners
                    .OrderBy(rp => rp.PartnerHostname)
                    .Select(rp => new
                    {
                        rp.PartnerHostname,
                        rp.Direction,
                        rp.NamingContext,
                        rp.LastSuccess,
                        rp.LastAttempt,
                        rp.FailureCount,
                        rp.LastError,
                        rp.Transport,
                    })
                    .ToList()
            })
            .ToListAsync();

        var latest = snapshots.FirstOrDefault();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            latest,
            history = snapshots.Select(s => new { s.Id, s.ScannedAt, s.ScannedBy, s.ReplFailureCount, s.DcCount }).ToList(),
        });
        return response;
    }
}

public class DcHealthSubmitRequest
{
    public string? ScannedBy { get; set; }
    public int? SchemaVersion { get; set; }
    public string? SchemaVersionLabel { get; set; }
    public string? ForestLevel { get; set; }
    public string? DomainLevel { get; set; }
    public string? ForestName { get; set; }
    public string? DomainName { get; set; }
    public string? SchemaMaster { get; set; }
    public string? DomainNamingMaster { get; set; }
    public string? PdcEmulator { get; set; }
    public string? RidMaster { get; set; }
    public string? InfrastructureMaster { get; set; }
    public bool FsmoSinglePoint { get; set; }
    public int ReplPartnerCount { get; set; }
    public int ReplFailureCount { get; set; }
    public DateTime? LastSuccessfulRepl { get; set; }
    public int SiteCount { get; set; }
    public int SubnetCount { get; set; }
    public int DcCount { get; set; }
    public int GcCount { get; set; }
    public List<DcReplPartnerDto>? ReplicationPartners { get; set; }
}

public class DcReplPartnerDto
{
    public string? PartnerHostname { get; set; }
    public string? PartnerDn { get; set; }
    public string? Direction { get; set; }
    public string? NamingContext { get; set; }
    public DateTime? LastSuccess { get; set; }
    public DateTime? LastAttempt { get; set; }
    public int FailureCount { get; set; }
    public string? LastError { get; set; }
    public string? Transport { get; set; }
}
