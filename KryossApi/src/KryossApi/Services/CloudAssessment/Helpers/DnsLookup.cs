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
    Task<List<string>> GetNsRecordsAsync(string domain, CancellationToken ct);
    Task<List<string>> GetARecordsAsync(string domain, CancellationToken ct);
    Task<SoaResult?> GetSoaRecordAsync(string domain, CancellationToken ct);
    Task<List<CaaResult>> GetCaaRecordsAsync(string domain, CancellationToken ct);
    Task<bool> CheckDnssecAsync(string domain, CancellationToken ct);
}

public record SoaResult(string PrimaryNs, string AdminEmail, uint Serial, uint Refresh, uint Retry, uint Expire, uint MinTtl);
public record CaaResult(int Flags, string Tag, string Value);

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

    public async Task<List<string>> GetNsRecordsAsync(string domain, CancellationToken ct)
    {
        try
        {
            var result = await _client.QueryAsync(domain, QueryType.NS, cancellationToken: ct);
            return result.Answers
                .NsRecords()
                .Select(r => r.NSDName.Value.TrimEnd('.'))
                .ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (DnsResponseException ex)
        {
            _log.LogDebug(ex, "DNS lookup failed for {Name} ({Type})", domain, QueryType.NS);
            return [];
        }
    }

    public async Task<List<string>> GetARecordsAsync(string domain, CancellationToken ct)
    {
        try
        {
            var result = await _client.QueryAsync(domain, QueryType.A, cancellationToken: ct);
            return result.Answers
                .ARecords()
                .Select(r => r.Address.ToString())
                .ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (DnsResponseException ex)
        {
            _log.LogDebug(ex, "DNS lookup failed for {Name} ({Type})", domain, QueryType.A);
            return [];
        }
    }

    public async Task<SoaResult?> GetSoaRecordAsync(string domain, CancellationToken ct)
    {
        try
        {
            var result = await _client.QueryAsync(domain, QueryType.SOA, cancellationToken: ct);
            var soa = result.Answers.SoaRecords().FirstOrDefault();
            if (soa == null) return null;
            return new SoaResult(
                soa.MName.Value.TrimEnd('.'),
                soa.RName.Value.TrimEnd('.'),
                soa.Serial,
                soa.Refresh,
                soa.Retry,
                soa.Expire,
                soa.Minimum);
        }
        catch (OperationCanceledException) { throw; }
        catch (DnsResponseException ex)
        {
            _log.LogDebug(ex, "DNS lookup failed for {Name} ({Type})", domain, QueryType.SOA);
            return null;
        }
    }

    public async Task<List<CaaResult>> GetCaaRecordsAsync(string domain, CancellationToken ct)
    {
        try
        {
            var result = await _client.QueryAsync(domain, QueryType.CAA, cancellationToken: ct);
            return result.Answers
                .CaaRecords()
                .Select(r => new CaaResult(r.Flags, r.Tag, r.Value))
                .ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (DnsResponseException ex)
        {
            _log.LogDebug(ex, "DNS lookup failed for {Name} ({Type})", domain, QueryType.CAA);
            return [];
        }
    }

    public async Task<bool> CheckDnssecAsync(string domain, CancellationToken ct)
    {
        try
        {
            var result = await _client.QueryAsync(domain, QueryType.DNSKEY, cancellationToken: ct);
            return result.Answers.Any();
        }
        catch (OperationCanceledException) { throw; }
        catch (DnsResponseException ex)
        {
            _log.LogDebug(ex, "DNSSEC check failed for {Name}", domain);
            return false;
        }
    }
}
