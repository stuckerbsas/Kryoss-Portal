using System.Net;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace KryossApi.Functions.Agent;

public class SpeedTestFunction
{
    private const int DefaultDownloadSize = 10 * 1024 * 1024;
    private const int MaxDownloadSize = 25 * 1024 * 1024;
    private const string ContainerName = "speedtest";
    private const string TestFileName = "testfile-100mb.bin";
    private const int TestFileSizeBytes = 100 * 1024 * 1024;

    private readonly IConfiguration _config;

    public SpeedTestFunction(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Returns SAS URLs for direct blob download/upload speed testing.
    /// Agent downloads/uploads directly against Azure Blob Storage (high throughput)
    /// instead of routing through Azure Functions (limited to ~50 Mbps).
    /// </summary>
    [Function("SpeedTest_Sas")]
    public async Task<HttpResponseData> GetSasTokens(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/speedtest/sas")] HttpRequestData req)
    {
        var connStr = _config["AzureWebJobsStorage"];
        if (string.IsNullOrEmpty(connStr))
        {
            var err = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            await err.WriteStringAsync("{\"error\":\"storage_not_configured\"}");
            return err;
        }

        try
        {
            var blobService = new BlobServiceClient(connStr);
            var container = blobService.GetBlobContainerClient(ContainerName);
            await container.CreateIfNotExistsAsync(PublicAccessType.None);

            var testBlob = container.GetBlobClient(TestFileName);
            if (!await testBlob.ExistsAsync())
            {
                await SeedTestFileAsync(testBlob);
            }

            var now = DateTimeOffset.UtcNow;
            var expiry = now.AddMinutes(5);

            var downloadSas = testBlob.GenerateSasUri(BlobSasPermissions.Read, expiry);

            var uploadBlobName = $"upload/{now:yyyyMMddHHmmss}_{Guid.NewGuid():N}.bin";
            var uploadBlob = container.GetBlobClient(uploadBlobName);
            var uploadSas = uploadBlob.GenerateSasUri(BlobSasPermissions.Create | BlobSasPermissions.Write, expiry);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            resp.Headers.Add("Content-Type", "application/json");
            resp.Headers.Add("Cache-Control", "no-store");
            await resp.WriteStringAsync(
                $"{{\"downloadUrl\":\"{downloadSas}\",\"uploadUrl\":\"{uploadSas}\",\"downloadSizeBytes\":{TestFileSizeBytes}}}");
            return resp;
        }
        catch (Exception ex)
        {
            var resp = req.CreateResponse(HttpStatusCode.InternalServerError);
            await resp.WriteStringAsync($"{{\"error\":\"sas_generation_failed\"}}");
            return resp;
        }
    }

    private static async Task SeedTestFileAsync(BlobClient blob)
    {
        var buffer = new byte[1024 * 1024]; // 1 MB chunk
        Random.Shared.NextBytes(buffer);

        using var stream = new MemoryStream(TestFileSizeBytes);
        for (int i = 0; i < 100; i++)
            stream.Write(buffer, 0, buffer.Length);
        stream.Position = 0;

        await blob.UploadAsync(stream, new BlobHttpHeaders { ContentType = "application/octet-stream" });
    }

    // Legacy endpoints kept for backward compatibility with pre-v2.10 agents

    [Function("SpeedTest_Download")]
    public async Task<HttpResponseData> Download(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/speedtest")] HttpRequestData req)
    {
        var size = DefaultDownloadSize;
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (int.TryParse(qs["size"], out var requested) && requested > 0)
            size = Math.Min(requested, MaxDownloadSize);

        var data = new byte[size];
        Random.Shared.NextBytes(data);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "application/octet-stream");
        resp.Headers.Add("Content-Length", size.ToString());
        resp.Headers.Add("Cache-Control", "no-store");
        await resp.Body.WriteAsync(data);
        return resp;
    }

    [Function("SpeedTest_Upload")]
    public async Task<HttpResponseData> Upload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/speedtest")] HttpRequestData req)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await req.Body.ReadAsync(buffer)) > 0)
            total += read;

        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "application/json");
        await resp.WriteStringAsync($"{{\"bytesReceived\":{total}}}");
        return resp;
    }

    [Function("SpeedTest_Ping")]
    public HttpResponseData Ping(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/speedtest/ping")] HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Cache-Control", "no-store");
        return resp;
    }
}
