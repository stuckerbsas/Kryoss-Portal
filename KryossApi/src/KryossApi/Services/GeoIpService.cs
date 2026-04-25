using System.Text.Json;

namespace KryossApi.Services;

public interface IGeoIpService
{
    Task<GeoIpResult?> LookupAsync(string ip);
}

public class GeoIpResult
{
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public decimal? Lat { get; set; }
    public decimal? Lon { get; set; }
    public string? Isp { get; set; }
    public int? Asn { get; set; }
    public string? AsnOrg { get; set; }
    public string? ConnType { get; set; }
}

public class IpApiGeoIpService : IGeoIpService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<GeoIpResult?> LookupAsync(string ip)
    {
        try
        {
            var url = $"http://ip-api.com/json/{ip}?fields=status,country,regionName,city,lat,lon,isp,as,org,mobile,hosting";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.GetProperty("status").GetString() != "success")
                return null;

            var asRaw = root.TryGetProperty("as", out var asProp) ? asProp.GetString() : null;
            int? asn = null;
            string? asnOrg = null;
            if (asRaw != null)
            {
                var spaceIdx = asRaw.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    var numPart = asRaw[2..spaceIdx]; // skip "AS"
                    if (int.TryParse(numPart, out var n)) asn = n;
                    asnOrg = asRaw[(spaceIdx + 1)..];
                }
            }

            var isMobile = root.TryGetProperty("mobile", out var mob) && mob.GetBoolean();
            var isHosting = root.TryGetProperty("hosting", out var host) && host.GetBoolean();
            var connType = isMobile ? "cellular" : isHosting ? "hosting" : "business";

            return new GeoIpResult
            {
                Country = root.TryGetProperty("country", out var c) ? c.GetString() : null,
                Region = root.TryGetProperty("regionName", out var r) ? r.GetString() : null,
                City = root.TryGetProperty("city", out var ci) ? ci.GetString() : null,
                Lat = root.TryGetProperty("lat", out var la) ? la.GetDecimal() : null,
                Lon = root.TryGetProperty("lon", out var lo) ? lo.GetDecimal() : null,
                Isp = root.TryGetProperty("isp", out var isp) ? isp.GetString() : null,
                Asn = asn,
                AsnOrg = asnOrg,
                ConnType = connType,
            };
        }
        catch
        {
            return null;
        }
    }
}
