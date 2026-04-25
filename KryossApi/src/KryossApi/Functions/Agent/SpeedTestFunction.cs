using System.Net;
using System.Security.Cryptography;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace KryossApi.Functions.Agent;

public class SpeedTestFunction
{
    private const int DownloadSize = 10 * 1024 * 1024; // 10 MB

    [Function("SpeedTest_Download")]
    public async Task<HttpResponseData> Download(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/speedtest")] HttpRequestData req)
    {
        var data = RandomNumberGenerator.GetBytes(DownloadSize);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "application/octet-stream");
        resp.Headers.Add("Content-Length", DownloadSize.ToString());
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
}
