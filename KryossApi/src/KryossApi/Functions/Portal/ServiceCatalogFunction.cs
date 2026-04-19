using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

[RequirePermission("reports:read")]
public class ServiceCatalogFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public ServiceCatalogFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    [Function("ServiceCatalog_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/service-catalog")] HttpRequestData req)
    {
        var items = await _db.ServiceCatalog.Where(sc => sc.IsActive).OrderBy(sc => sc.SortOrder).ToListAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(items);
        return response;
    }

    [Function("FranchiseRates_Get")]
    public async Task<HttpResponseData> GetRate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/franchise-rates/{franchiseId:guid}")] HttpRequestData req,
        Guid franchiseId)
    {
        var rate = await _db.FranchiseServiceRates
            .Where(r => r.FranchiseId == franchiseId && r.EffectiveFrom <= DateTime.UtcNow)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(rate ?? new Data.Entities.FranchiseServiceRate
        {
            FranchiseId = franchiseId,
            HourlyRate = 150,
            Currency = "USD",
            EffectiveFrom = DateTime.UtcNow
        });
        return response;
    }

    [Function("FranchiseRates_Set")]
    [RequirePermission("admin:write")]
    public async Task<HttpResponseData> SetRate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/franchise-rates/{franchiseId:guid}")] HttpRequestData req,
        Guid franchiseId)
    {
        var body = await req.ReadFromJsonAsync<RateUpdateDto>();
        if (body == null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Missing body" });
            return bad;
        }

        var rate = new Data.Entities.FranchiseServiceRate
        {
            FranchiseId = franchiseId,
            HourlyRate = body.HourlyRate,
            Currency = body.Currency ?? "USD",
            MarginPct = body.MarginPct ?? 0,
            EffectiveFrom = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _db.FranchiseServiceRates.Add(rate);
        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(rate);
        return response;
    }

    private record RateUpdateDto(decimal HourlyRate, string? Currency, decimal? MarginPct);
}
