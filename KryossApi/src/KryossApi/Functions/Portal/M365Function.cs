using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// M365 / Entra ID integration endpoints.
/// Connect, scan, retrieve findings, and disconnect M365 tenants.
/// </summary>
public class M365Function
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IM365ScannerService _scanner;

    public M365Function(KryossDbContext db, ICurrentUserService user, IM365ScannerService scanner)
    {
        _db = db;
        _user = user;
        _scanner = scanner;
    }

    /// <summary>
    /// Connect an M365 tenant to an organization and run the initial scan.
    /// POST /v2/m365/connect
    /// </summary>
    [Function("M365_Connect")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Connect(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/m365/connect")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<M365ConnectRequest>();
        if (body is null || string.IsNullOrWhiteSpace(body.TenantId) ||
            string.IsNullOrWhiteSpace(body.ClientId) || string.IsNullOrWhiteSpace(body.ClientSecret))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "tenantId, clientId, and clientSecret are required" });
            return bad;
        }

        if (body.OrganizationId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        // Check if tenant already connected for this org
        var existing = await _db.M365Tenants
            .FirstOrDefaultAsync(t => t.OrganizationId == body.OrganizationId);

        if (existing != null)
        {
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(new { error = "An M365 tenant is already connected to this organization. Disconnect first." });
            return conflict;
        }

        // Create tenant record
        var tenant = new M365Tenant
        {
            Id = Guid.NewGuid(),
            OrganizationId = body.OrganizationId,
            TenantId = body.TenantId,
            TenantName = body.TenantName,
            ClientId = body.ClientId,
            ClientSecret = body.ClientSecret, // TODO: encrypt with Key Vault
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        _db.M365Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        // Run initial scan
        List<M365CheckResult> scanResults;
        try
        {
            scanResults = await _scanner.ScanAsync(body.TenantId, body.ClientId, body.ClientSecret);
        }
        catch (Exception)
        {
            // If scan fails, still save the tenant but mark as disconnected
            tenant.Status = "expired";
            await _db.SaveChangesAsync();

            var err = req.CreateResponse(HttpStatusCode.UnprocessableEntity);
            await err.WriteAsJsonAsync(new
            {
                error = "Tenant connected but initial scan failed. Verify app registration permissions and credentials.",
                tenantId = tenant.Id
            });
            return err;
        }

        // Persist findings
        await PersistFindings(tenant.Id, scanResults);
        tenant.LastScanAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var passed = scanResults.Count(r => r.Status == "pass");
        var failed = scanResults.Count(r => r.Status == "fail");
        var warned = scanResults.Count(r => r.Status == "warn");

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            tenantId = tenant.Id,
            totalChecks = scanResults.Count,
            checksPassed = passed,
            checksFailed = failed,
            checksWarned = warned
        });
        return response;
    }

    /// <summary>
    /// Re-run the M365 scan for a connected tenant.
    /// POST /v2/m365/scan
    /// </summary>
    [Function("M365_Scan")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Scan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/m365/scan")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<M365ScanRequest>();
        if (body is null || body.OrganizationId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        var tenant = await _db.M365Tenants
            .FirstOrDefaultAsync(t => t.OrganizationId == body.OrganizationId);

        if (tenant is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "No M365 tenant connected to this organization" });
            return notFound;
        }

        if (string.IsNullOrWhiteSpace(tenant.ClientId) || string.IsNullOrWhiteSpace(tenant.ClientSecret))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Tenant credentials are missing. Reconnect the tenant." });
            return bad;
        }

        // Run scan
        List<M365CheckResult> scanResults;
        try
        {
            scanResults = await _scanner.ScanAsync(tenant.TenantId, tenant.ClientId, tenant.ClientSecret);
        }
        catch (Exception)
        {
            tenant.Status = "expired";
            await _db.SaveChangesAsync();

            var err = req.CreateResponse(HttpStatusCode.UnprocessableEntity);
            await err.WriteAsJsonAsync(new { error = "Scan failed. Credentials may be expired or permissions revoked." });
            return err;
        }

        // Clear old findings and persist new ones
        var oldFindings = await _db.M365Findings.Where(f => f.TenantId == tenant.Id).ToListAsync();
        _db.M365Findings.RemoveRange(oldFindings);

        await PersistFindings(tenant.Id, scanResults);
        tenant.LastScanAt = DateTime.UtcNow;
        tenant.Status = "active";
        await _db.SaveChangesAsync();

        var passed = scanResults.Count(r => r.Status == "pass");
        var failed = scanResults.Count(r => r.Status == "fail");
        var warned = scanResults.Count(r => r.Status == "warn");

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            totalChecks = scanResults.Count,
            checksPassed = passed,
            checksFailed = failed,
            checksWarned = warned,
            scannedAt = tenant.LastScanAt
        });
        return response;
    }

    /// <summary>
    /// Get latest M365 scan results for an organization.
    /// GET /v2/m365?organizationId={guid}
    /// </summary>
    [Function("M365_Get")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/m365")] HttpRequestData req)
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

        var tenant = await _db.M365Tenants
            .Where(t => t.OrganizationId == orgId)
            .Select(t => new
            {
                t.Id,
                t.TenantId,
                t.TenantName,
                t.Status,
                t.LastScanAt,
                t.CreatedAt,
                Findings = _db.M365Findings
                    .Where(f => f.TenantId == t.Id)
                    .OrderBy(f => f.CheckId)
                    .Select(f => new
                    {
                        f.CheckId,
                        f.Name,
                        f.Category,
                        f.Severity,
                        f.Status,
                        f.Finding,
                        f.ActualValue,
                        f.ScannedAt
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        if (tenant is null)
        {
            // Return empty response indicating no tenant connected
            var empty = req.CreateResponse(HttpStatusCode.OK);
            await empty.WriteAsJsonAsync(new { connected = false });
            return empty;
        }

        var findings = tenant.Findings;
        var totalChecks = findings.Count;
        var passed = findings.Count(f => f.Status == "pass");
        var failed = findings.Count(f => f.Status == "fail");
        var warned = findings.Count(f => f.Status == "warn");
        var info = findings.Count(f => f.Status == "info");

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            connected = true,
            tenantId = tenant.TenantId,
            tenantName = tenant.TenantName,
            status = tenant.Status,
            lastScanAt = tenant.LastScanAt,
            createdAt = tenant.CreatedAt,
            summary = new { totalChecks, passed, failed, warned, info },
            findings
        });
        return response;
    }

    /// <summary>
    /// Disconnect an M365 tenant from an organization.
    /// DELETE /v2/m365/disconnect
    /// </summary>
    [Function("M365_Disconnect")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Disconnect(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v2/m365/disconnect")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<M365DisconnectRequest>();
        if (body is null || body.OrganizationId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        var tenant = await _db.M365Tenants
            .FirstOrDefaultAsync(t => t.OrganizationId == body.OrganizationId);

        if (tenant is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "No M365 tenant connected to this organization" });
            return notFound;
        }

        // CASCADE will delete findings
        _db.M365Tenants.Remove(tenant);
        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { message = "M365 tenant disconnected" });
        return response;
    }

    // ── Helpers ──

    private async Task PersistFindings(Guid tenantId, List<M365CheckResult> results)
    {
        var now = DateTime.UtcNow;
        foreach (var r in results)
        {
            _db.M365Findings.Add(new M365Finding
            {
                TenantId = tenantId,
                CheckId = r.CheckId,
                Name = r.Name,
                Category = r.Category,
                Severity = r.Severity,
                Status = r.Status,
                Finding = r.Finding,
                ActualValue = r.ActualValue,
                ScannedAt = now
            });
        }
        await _db.SaveChangesAsync();
    }
}

// ── Request DTOs ──

public class M365ConnectRequest
{
    public Guid OrganizationId { get; set; }
    public string TenantId { get; set; } = null!;
    public string? TenantName { get; set; }
    public string ClientId { get; set; } = null!;
    public string ClientSecret { get; set; } = null!;
}

public class M365ScanRequest
{
    public Guid OrganizationId { get; set; }
}

public class M365DisconnectRequest
{
    public Guid OrganizationId { get; set; }
}
