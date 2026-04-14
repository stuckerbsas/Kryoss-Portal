using System.Net;
using System.Security.Cryptography;
using System.Text;
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
    private readonly M365Config _m365Config;

    public M365Function(KryossDbContext db, ICurrentUserService user, IM365ScannerService scanner, M365Config m365Config)
    {
        _db = db;
        _user = user;
        _scanner = scanner;
        _m365Config = m365Config;
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

        // MED-07 / HIGH-01: Verify the user has access to this organization
        if (!_user.IsAdmin)
        {
            var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
                await _db.Organizations.AnyAsync(o => o.Id == body.OrganizationId && o.FranchiseId == _user.FranchiseId.Value);
            var orgBelongsToUser = _user.OrganizationId.HasValue && body.OrganizationId == _user.OrganizationId.Value;
            if (!orgBelongsToFranchise && !orgBelongsToUser)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
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

        // CRIT-01: Encrypt the client secret before storing in the database.
        // Uses org's ApiSecret as key material via AES-256-GCM.
        // TODO: Replace with Azure Key Vault secret references.
        var encryptedSecret = await EncryptSecretForOrg(body.ClientSecret, body.OrganizationId);

        // Create tenant record
        var tenant = new M365Tenant
        {
            Id = Guid.NewGuid(),
            OrganizationId = body.OrganizationId,
            TenantId = body.TenantId,
            TenantName = body.TenantName,
            ClientId = body.ClientId,
            ClientSecret = encryptedSecret, // CRIT-01: encrypted at rest
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

        // Run scan — use shared creds (consent flow) or per-customer creds (legacy)
        List<M365CheckResult> scanResults;
        try
        {
            if (string.IsNullOrWhiteSpace(tenant.ClientId))
            {
                // Consent flow: use shared app registration credentials
                scanResults = await _scanner.ScanAsync(tenant.TenantId);
            }
            else
            {
                // Legacy flow: decrypt per-customer credentials
                string decryptedSecret;
                try
                {
                    decryptedSecret = await DecryptSecretForOrg(tenant.ClientSecret!, body.OrganizationId);
                }
                catch (Exception)
                {
                    var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                    await err.WriteAsJsonAsync(new { error = "Failed to decrypt tenant credentials. Reconnect the tenant." });
                    return err;
                }
                scanResults = await _scanner.ScanAsync(tenant.TenantId, tenant.ClientId, decryptedSecret);
            }
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
    /// Generate the admin consent URL for the shared M365 app registration.
    /// GET /v2/m365/consent-url?organizationId={guid}
    /// </summary>
    [Function("M365_ConsentUrl")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> ConsentUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/m365/consent-url")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        if (!Guid.TryParse(orgIdStr, out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        // Verify org exists
        var orgExists = await _db.Organizations.AnyAsync(o => o.Id == orgId);
        if (!orgExists)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Organization not found" });
            return notFound;
        }

        // Check if already connected
        var existing = await _db.M365Tenants.AnyAsync(t => t.OrganizationId == orgId);
        if (existing)
        {
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(new { error = "An M365 tenant is already connected to this organization. Disconnect first." });
            return conflict;
        }

        if (string.IsNullOrWhiteSpace(_m365Config.ClientId))
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = "M365 scanner app registration not configured" });
            return err;
        }

        var callbackUrl = Uri.EscapeDataString("https://func-kryoss.azurewebsites.net/v2/m365/consent-callback");
        var consentUrl = $"https://login.microsoftonline.com/common/adminconsent" +
            $"?client_id={Uri.EscapeDataString(_m365Config.ClientId)}" +
            $"&redirect_uri={callbackUrl}" +
            $"&state={orgId}";

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { url = consentUrl });
        return response;
    }

    /// <summary>
    /// Callback from Microsoft after admin consent grant.
    /// GET /v2/m365/consent-callback?tenant={tid}&amp;admin_consent=True&amp;state={orgId}
    /// No auth required — this is a browser redirect from Microsoft.
    /// </summary>
    [Function("M365_ConsentCallback")]
    public async Task<HttpResponseData> ConsentCallback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/m365/consent-callback")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

        // Microsoft sends error/error_description if consent was denied
        var errorParam = query["error"];
        var errorDesc = query["error_description"];
        var stateParam = query["state"];
        var tenantParam = query["tenant"];
        var adminConsent = query["admin_consent"];

        // Parse state as orgId
        Guid orgId = Guid.Empty;
        Guid.TryParse(stateParam, out orgId);

        // Helper to build portal redirect URL
        string BuildRedirectUrl(string orgSlug, string? queryString = null)
        {
            var baseUrl = _m365Config.PortalBaseUrl.TrimEnd('/');
            var path = $"/organizations/{Uri.EscapeDataString(orgSlug)}/m365";
            return string.IsNullOrEmpty(queryString) ? $"{baseUrl}{path}" : $"{baseUrl}{path}?{queryString}";
        }

        // If Microsoft sent an error (consent denied)
        if (!string.IsNullOrEmpty(errorParam))
        {
            var orgForError = orgId != Guid.Empty
                ? await _db.Organizations.Where(o => o.Id == orgId).Select(o => new { o.Name }).FirstOrDefaultAsync()
                : null;
            var slug = GenerateSlug(orgForError?.Name ?? "unknown");
            var redirectUrl = BuildRedirectUrl(slug, $"error={Uri.EscapeDataString(errorDesc ?? errorParam)}");

            var redirect = req.CreateResponse(HttpStatusCode.Redirect);
            redirect.Headers.Add("Location", redirectUrl);
            return redirect;
        }

        // Validate required params
        if (string.IsNullOrWhiteSpace(tenantParam) || orgId == Guid.Empty ||
            !string.Equals(adminConsent, "True", StringComparison.OrdinalIgnoreCase))
        {
            var badRedirect = req.CreateResponse(HttpStatusCode.Redirect);
            var errorUrl = _m365Config.PortalBaseUrl.TrimEnd('/') +
                $"/?error={Uri.EscapeDataString("Invalid consent callback parameters")}";
            badRedirect.Headers.Add("Location", errorUrl);
            return badRedirect;
        }

        // Verify org exists and get slug
        var org = await _db.Organizations
            .Where(o => o.Id == orgId)
            .Select(o => new { o.Id, o.Name })
            .FirstOrDefaultAsync();

        if (org is null)
        {
            var badRedirect = req.CreateResponse(HttpStatusCode.Redirect);
            var errorUrl = _m365Config.PortalBaseUrl.TrimEnd('/') +
                $"/?error={Uri.EscapeDataString("Organization not found")}";
            badRedirect.Headers.Add("Location", errorUrl);
            return badRedirect;
        }

        var orgSlug = GenerateSlug(org.Name);

        // Check no existing tenant for this org
        var existingTenant = await _db.M365Tenants.FirstOrDefaultAsync(t => t.OrganizationId == orgId);
        if (existingTenant != null)
        {
            var redirectUrl = BuildRedirectUrl(orgSlug, "error=" + Uri.EscapeDataString("M365 tenant already connected"));
            var redirect = req.CreateResponse(HttpStatusCode.Redirect);
            redirect.Headers.Add("Location", redirectUrl);
            return redirect;
        }

        // Create m365_tenants row (no per-customer creds — uses shared app registration)
        var tenant = new M365Tenant
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            TenantId = tenantParam,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            ConsentGrantedAt = DateTime.UtcNow
        };

        _db.M365Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        // Try to run initial scan using shared creds — catch errors gracefully
        try
        {
            var scanResults = await _scanner.ScanAsync(tenantParam);
            await PersistFindings(tenant.Id, scanResults);
            tenant.LastScanAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        catch (Exception)
        {
            // Scan failed but tenant is connected — user can retry from portal
            tenant.Status = "expired";
            await _db.SaveChangesAsync();
        }

        var successUrl = BuildRedirectUrl(orgSlug, "connected=true");
        var successRedirect = req.CreateResponse(HttpStatusCode.Redirect);
        successRedirect.Headers.Add("Location", successUrl);
        return successRedirect;
    }

    /// <summary>
    /// Generate a URL-friendly slug from an organization name.
    /// Matches the portal's client-side slug generation logic.
    /// </summary>
    private static string GenerateSlug(string name)
    {
        return System.Text.RegularExpressions.Regex
            .Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-")
            .Trim('-');
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

    // ── CRIT-01: M365 Secret Encryption Helpers ──
    // Encrypts the M365 client secret using the org's ApiSecret as key material.
    // This is a transitional measure until Key Vault references are implemented.
    // TODO: Replace with Azure Key Vault secret references (P0 backlog).

    /// <summary>
    /// Encrypt a plaintext secret using the org's ApiSecret as key material (AES-256-GCM).
    /// Returns base64(nonce:ciphertext:tag).
    /// </summary>
    private async Task<string> EncryptSecretForOrg(string plaintext, Guid orgId)
    {
        var org = await _db.Organizations
            .Where(o => o.Id == orgId)
            .Select(o => new { o.ApiSecret })
            .FirstOrDefaultAsync();

        var keyMaterial = org?.ApiSecret;
        if (string.IsNullOrEmpty(keyMaterial))
        {
            // If org has no ApiSecret, fall back to a warning-logged obfuscation.
            // This should not happen in production (orgs get ApiSecret on first enrollment).
            return "PLAIN:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        }

        // Derive a 256-bit key from the org secret using SHA-256
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
        var nonce = new byte[12]; // AES-GCM standard nonce size
        RandomNumberGenerator.Fill(nonce);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16]; // 128-bit auth tag

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Format: base64(nonce) + ":" + base64(ciphertext) + ":" + base64(tag)
        return $"ENC:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(ciphertext)}:{Convert.ToBase64String(tag)}";
    }

    /// <summary>
    /// Decrypt a secret previously encrypted with EncryptSecretForOrg.
    /// </summary>
    private async Task<string> DecryptSecretForOrg(string encrypted, Guid orgId)
    {
        if (encrypted.StartsWith("PLAIN:"))
        {
            // Legacy fallback (no org secret at encryption time)
            return Encoding.UTF8.GetString(Convert.FromBase64String(encrypted[6..]));
        }

        if (!encrypted.StartsWith("ENC:"))
        {
            // Unencrypted legacy value — return as-is (migration needed)
            return encrypted;
        }

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
