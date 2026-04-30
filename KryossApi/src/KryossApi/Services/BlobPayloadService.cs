using System.IO.Compression;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace KryossApi.Services;

public interface IBlobPayloadService
{
    Task<string> UploadAsync(Guid organizationId, Guid runId, string json);
    Task<string?> DownloadAsync(string blobUrl);
}

public class BlobPayloadService : IBlobPayloadService
{
    private const string ContainerName = "raw-payloads";
    private readonly BlobContainerClient _container;

    public BlobPayloadService(BlobServiceClient blobService)
    {
        _container = blobService.GetBlobContainerClient(ContainerName);
        _container.CreateIfNotExists();
    }

    public async Task<string> UploadAsync(Guid organizationId, Guid runId, string json)
    {
        var blobName = $"{organizationId}/{runId}.json.gz";
        var blob = _container.GetBlobClient(blobName);

        using var ms = new MemoryStream();
        await using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await gz.WriteAsync(bytes);
        }

        ms.Position = 0;
        await blob.UploadAsync(ms, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = "application/gzip",
                ContentEncoding = "gzip"
            },
            AccessTier = AccessTier.Cool
        });

        return blobName;
    }

    public async Task<string?> DownloadAsync(string blobUrl)
    {
        var blob = _container.GetBlobClient(blobUrl);
        if (!await blob.ExistsAsync()) return null;

        using var download = await blob.OpenReadAsync();
        await using var gz = new GZipStream(download, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
