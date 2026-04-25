using KryossApi.Services.CloudAssessment;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Timer;

/// <summary>
/// Nightly benchmark aggregate refresh.
/// Runs 03:00 UTC daily. Rebuilds franchise + global rollups from the
/// latest completed scan per organization.
/// </summary>
public class BenchmarkRefreshFunction
{
    private readonly IBenchmarkService _benchmark;
    private readonly ILogger<BenchmarkRefreshFunction> _log;

    public BenchmarkRefreshFunction(
        IBenchmarkService benchmark,
        ILogger<BenchmarkRefreshFunction> log)
    {
        _benchmark = benchmark;
        _log = log;
    }

    [Function("Benchmark_RefreshAggregates")]
    public async Task Run([TimerTrigger("0 0 3 * * *")] TimerInfo timer)
    {
        _log.LogInformation("Benchmark aggregate refresh started");

        try
        {
            var (franchises, franchiseMetrics) = await _benchmark.RefreshFranchiseAggregatesAsync(CancellationToken.None);
            _log.LogInformation(
                "Franchise aggregates refreshed: franchises={Franchises} metrics={Metrics}",
                franchises, franchiseMetrics);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Franchise aggregate refresh failed");
        }

        try
        {
            var (rows, globalMetrics) = await _benchmark.RefreshGlobalAggregatesAsync(CancellationToken.None);
            _log.LogInformation(
                "Global aggregates refreshed: rows={Rows} metrics={Metrics}",
                rows, globalMetrics);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Global aggregate refresh failed");
        }

        _log.LogInformation("Benchmark aggregate refresh completed");
    }
}
