using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace KryossApi.Functions.Agent;

public class AgentVersionFunction
{
    private readonly IConfiguration _config;

    public AgentVersionFunction(IConfiguration config)
    {
        _config = config;
    }

    [Function("Agent_LatestVersion")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/agent/latest-version")] HttpRequestData req)
    {
        var connStr = _config["AgentBlobConnection"] ?? _config["AzureWebJobsStorage"];
        if (string.IsNullOrEmpty(connStr))
        {
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(new { version = (string?)null, message = "Auto-update not configured" });
            return resp;
        }

        try
        {
            var container = new BlobContainerClient(connStr, "kryoss-agent-templates");

            // Prefer version.json (structured), fall back to version.txt (legacy)
            var jsonBlob = container.GetBlobClient("latest/version.json");
            if (await jsonBlob.ExistsAsync())
            {
                var download = await jsonBlob.DownloadContentAsync();
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                await response.Body.WriteAsync(download.Value.Content.ToArray());
                return response;
            }

            var txtBlob = container.GetBlobClient("latest/version.txt");
            if (await txtBlob.ExistsAsync())
            {
                var download = await txtBlob.DownloadContentAsync();
                var raw = download.Value.Content.ToString().Trim();
                // version.txt format: "2.2.2" or "2.2.2+commithash"
                var version = raw.Contains('+') ? raw[..raw.IndexOf('+')] : raw;

                // Compute hash of the binary if available
                string? hash = null;
                var exeBlob = container.GetBlobClient("latest/KryossAgent.exe");
                if (await exeBlob.ExistsAsync())
                {
                    var exeDownload = await exeBlob.DownloadContentAsync();
                    hash = Convert.ToHexString(SHA256.HashData(exeDownload.Value.Content.ToArray()));
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { version, hash });
                return response;
            }

            var noUpdate = req.CreateResponse(HttpStatusCode.OK);
            await noUpdate.WriteAsJsonAsync(new { version = (string?)null, message = "No update available" });
            return noUpdate;
        }
        catch
        {
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(new { version = (string?)null, message = "Update check failed" });
            return resp;
        }
    }

    [Function("Agent_DownloadBinary")]
    public async Task<HttpResponseData> DownloadBinary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/agent/download")] HttpRequestData req)
    {
        var connStr = _config["AgentBlobConnection"] ?? _config["AzureWebJobsStorage"];
        if (string.IsNullOrEmpty(connStr))
            return req.CreateResponse(HttpStatusCode.NotFound);

        try
        {
            var container = new BlobContainerClient(connStr, "kryoss-agent-templates");
            var blob = container.GetBlobClient("latest/KryossAgent.exe");

            if (!await blob.ExistsAsync())
                return req.CreateResponse(HttpStatusCode.NotFound);

            var download = await blob.DownloadStreamingAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/octet-stream");
            response.Headers.Add("Content-Disposition", "attachment; filename=KryossAgent.exe");
            await download.Value.Content.CopyToAsync(response.Body);
            return response;
        }
        catch
        {
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }
}
