using DnsClient;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CloudAssessment.Helpers;

/// <summary>
/// Thin wrapper over DnsClient.NET for the CA-10 Mail Flow pipeline.
/// Returns flat collections so the pipeline does not depend on the
/// DnsClient record types directly. Transport/protocol errors
/// (DnsResponseException) are swallowed and logged at Debug so callers
/// don't have to wrap every call — NXDOMAIN already yields an empty
/// answer set without throwing. OperationCanceledException is always
/// rethrown so cancellation is honored.
/// </summary>
public interface IDnsLookup
{
    Task<List<string>> GetTxtRecordsAsync(string domain, CancellationToken ct);
    Task<List<string>> GetMxRecordsAsync(string domain, CancellationToken ct);
    Task<string?> GetCnameAsync(string name, CancellationToken ct);
}

public class DnsLookup : IDnsLookup
{
    private readonly ILogger<DnsLookup> _log;
    private readonly LookupClient _client = new(new LookupClientOptions
    {
        Timeout = TimeSpan.FromSeconds(5),
        UseCache = true,
        Retries = 2
    });

    public DnsLookup(ILogger<DnsLookup> log)
    {
        _log = log;
    }

    public async Task<List<string>> GetTxtRecordsAsync(string domain, CancellationToken ct)
    {
        try
        {
            var result = await _client.QueryAsync(domain, QueryType.TXT, cancellationToken: ct);
            return result.Answers
                .TxtRecords()
                .SelectMany(r => r.Text)
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DnsResponseException ex)
        {
            _log.LogDebug(ex, "DNS lookup failed for {Name} ({Type})", domain, QueryType.TXT);
            return [];
        }
    }

    public async Task<List<string>> GetMxRecordsAsync(string domain, CancellationToken ct)
    {
        try
        {
            var result = await _client.QueryAsync(domain, QueryType.MX, cancellationToken: ct);
            return result.Answers
                .MxRecords()
                .OrderBy(m => m.Preference)
                .Select(m => m.Exchange.Value.TrimEnd('.'))
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DnsResponseException ex)
        {
            _log.LogDebug(ex, "DNS lookup failed for {Name} ({Type})", domain, QueryType.MX);
            return [];
        }
    }

    public async Task<string?> GetCnameAsync(string name, CancellationToken ct)
    {
        try
        {
            var result = await _client.QueryAsync(name, QueryType.CNAME, cancellationToken: ct);
            var record = result.Answers.CnameRecords().FirstOrDefault();
            return record?.CanonicalName.Value?.TrimEnd('.');
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DnsResponseException ex)
        {
            _log.LogDebug(ex, "DNS lookup failed for {Name} ({Type})", name, QueryType.CNAME);
            return null;
        }
    }
}
