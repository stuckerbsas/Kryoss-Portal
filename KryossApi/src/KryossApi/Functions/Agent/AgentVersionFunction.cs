using System.Net;
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
            var metaBlob = container.GetBlobClient("latest/version.json");

            if (!await metaBlob.ExistsAsync())
            {
                var resp = req.CreateResponse(HttpStatusCode.OK);
                await resp.WriteAsJsonAsync(new { version = (string?)null, message = "No update available" });
                return resp;
            }

            var download = await metaBlob.DownloadContentAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.Body.WriteAsync(download.Value.Content.ToArray());
            return response;
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
