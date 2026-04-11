using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KryossApi.Functions.Portal;

/// <summary>
/// Downloads a pre-patched KryossAgent.exe binary for a given organization.
/// The binary is read from Azure Blob Storage, patched with enrollment code,
/// API URL, org name, MSP name, and brand colors, then streamed to the caller.
/// </summary>
[RequirePermission("assessment:export")]
public class AgentDownloadFunction
{
    private readonly KryossDbContext _db;
    private readonly IEnrollmentService _enrollment;
    private readonly IConfiguration _config;

    public AgentDownloadFunction(KryossDbContext db, IEnrollmentService enrollment, IConfiguration config)
    {
        _db = db;
        _enrollment = enrollment;
        _config = config;
    }

    [Function("AgentDownload")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/agent/download")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["orgId"];

        if (!Guid.TryParse(orgIdStr, out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Missing or invalid orgId query parameter" });
            return bad;
        }

        // Load org with Brand and Franchise navigation properties
        var org = await _db.Organizations
            .Include(o => o.Brand)
            .Include(o => o.Franchise)
            .FirstOrDefaultAsync(o => o.Id == orgId);

        if (org is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Organization not found" });
            return notFound;
        }

        // Find existing multi-use enrollment code or create a new one
        var existingCode = await _db.EnrollmentCodes
            .Where(c => c.OrganizationId == orgId
                && c.MaxUses != null
                && c.UseCount < c.MaxUses
                && c.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(c => c.ExpiresAt)
            .Select(c => c.Code)
            .FirstOrDefaultAsync();

        var enrollmentCode = existingCode
            ?? await _enrollment.GenerateCodeAsync(orgId, null, "Agent download", 30, 999);

        // Read template binary from Azure Blob Storage
        var connectionString = _config["AzureWebJobsStorage"];
        var blobClient = new BlobServiceClient(connectionString)
            .GetBlobContainerClient("kryoss-agent-templates")
            .GetBlobClient("latest/KryossAgent.exe");

        if (!await blobClient.ExistsAsync())
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = "Agent template binary not found in storage" });
            return err;
        }

        var download = await blobClient.DownloadContentAsync();
        var templateBytes = download.Value.Content.ToArray();

        // Build patch values
        var apiUrl = _config["AgentApiUrl"] ?? "https://func-kryoss.azurewebsites.net";
        var primaryColor = org.Brand?.ColorPrimary ?? "#006536";
        var accentColor = org.Brand?.ColorAccent ?? "#A2C564";
        var orgName = org.Name;
        var mspName = org.Franchise?.Name ?? org.Name;

        var values = new Dictionary<string, string>
        {
            ["enrollmentCode"] = enrollmentCode,
            ["apiUrl"] = apiUrl,
            ["orgName"] = orgName,
            ["mspName"] = mspName,
            ["primaryColor"] = primaryColor,
            ["accentColor"] = accentColor,
        };

        var patchedBinary = BinaryPatcher.Patch(templateBytes, values);

        // Build slug from org name for filename
        var slug = Regex.Replace(org.Name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(slug)) slug = "org";

        // Wrap in ZIP to avoid browser/SmartScreen blocking .exe downloads
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry($"KryossAgent-{slug}.exe", CompressionLevel.Fastest);
            using var entryStream = entry.Open();
            await entryStream.WriteAsync(patchedBinary);
        }
        zipStream.Position = 0;

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/zip");
        response.Headers.Add("Content-Disposition", $"attachment; filename=\"KryossAgent-{slug}.zip\"");
        await zipStream.CopyToAsync(response.Body);
        return response;
    }
}
