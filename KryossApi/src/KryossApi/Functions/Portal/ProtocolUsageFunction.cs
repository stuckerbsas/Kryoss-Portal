using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

[RequirePermission("assessment:read")]
public class ProtocolUsageFunction
{
    private readonly KryossDbContext _db;

    public ProtocolUsageFunction(KryossDbContext db) => _db = db;

    private static readonly string[] ProtocolControlIds =
    [
        "AUDIT-001", "AUDIT-002", "AUDIT-003", "AUDIT-004",
        "NTLM-USE-001", "NTLM-USE-002", "NTLM-USE-003", "NTLM-USE-004",
        "SMB1-USE-001", "SMB1-USE-002",
        "SAFE-TO-DISABLE-NTLM", "SAFE-TO-DISABLE-SMB1"
    ];

    [Function("ProtocolUsage_Get")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/protocol-usage")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var latestRunIds = await _db.AssessmentRuns
            .AsNoTracking()
            .Where(r => r.OrganizationId == orgId && r.CompletedAt != null)
            .GroupBy(r => r.MachineId)
            .Select(g => g.OrderByDescending(r => r.CompletedAt).First().Id)
            .ToListAsync();

        if (latestRunIds.Count == 0)
        {
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(new { machines = 0, results = Array.Empty<object>() });
            return resp;
        }

        var controlDefIds = await _db.ControlDefs
            .AsNoTracking()
            .Where(cd => ProtocolControlIds.Contains(cd.ControlId) && cd.IsActive)
            .Select(cd => new { cd.Id, cd.ControlId })
            .ToListAsync();

        var controlDefIdMap = controlDefIds.ToDictionary(c => c.Id, c => c.ControlId);
        var defIds = controlDefIds.Select(c => c.Id).ToList();

        var results = await _db.ControlResults
            .AsNoTracking()
            .Where(cr => latestRunIds.Contains(cr.RunId) && defIds.Contains(cr.ControlDefId))
            .Join(_db.AssessmentRuns.AsNoTracking(), cr => cr.RunId, r => r.Id, (cr, r) => new { cr, r.MachineId })
            .Join(_db.Machines.AsNoTracking(), x => x.MachineId, m => m.Id, (x, m) => new
            {
                machineId = m.Id,
                hostname = m.Hostname,
                controlDefId = x.cr.ControlDefId,
                x.cr.Status,
                x.cr.Finding,
                x.cr.ActualValue,
            })
            .ToListAsync();

        var perMachine = results
            .GroupBy(r => new { r.machineId, r.hostname })
            .Select(g =>
            {
                var controls = g.ToDictionary(
                    r => controlDefIdMap.TryGetValue(r.controlDefId, out var cid) ? cid : r.controlDefId.ToString(),
                    r => new { r.Status, r.Finding, r.ActualValue });

                return new
                {
                    g.Key.machineId,
                    g.Key.hostname,
                    controls,
                };
            })
            .OrderBy(m => m.hostname)
            .ToList();

        int ntlmOutboundTotal = 0, ntlmInboundTotal = 0, smb1Total = 0;
        int auditConfigured = 0;

        foreach (var m in perMachine)
        {
            if (m.controls.TryGetValue("AUDIT-001", out var a1) && a1.Status == "pass")
                auditConfigured++;

            if (m.controls.TryGetValue("NTLM-USE-001", out var n1) && int.TryParse(n1.ActualValue, out var nOut))
                ntlmOutboundTotal += nOut;
            if (m.controls.TryGetValue("NTLM-USE-002", out var n2) && int.TryParse(n2.ActualValue, out var nIn))
                ntlmInboundTotal += nIn;
            if (m.controls.TryGetValue("SMB1-USE-001", out var s1) && int.TryParse(s1.ActualValue, out var sCount))
                smb1Total += sCount;
        }

        var safeNtlm = perMachine.All(m =>
            !m.controls.TryGetValue("SAFE-TO-DISABLE-NTLM", out var v) || v.Status == "pass");
        var safeSmb1 = perMachine.All(m =>
            !m.controls.TryGetValue("SAFE-TO-DISABLE-SMB1", out var v) || v.Status == "pass");

        var resp2 = req.CreateResponse(HttpStatusCode.OK);
        await resp2.WriteAsJsonAsync(new
        {
            machines = perMachine.Count,
            auditConfigured,
            ntlm = new { outbound = ntlmOutboundTotal, inbound = ntlmInboundTotal, safeToDisable = safeNtlm },
            smb1 = new { events = smb1Total, safeToDisable = safeSmb1 },
            perMachine,
        });
        return resp2;
    }
}
