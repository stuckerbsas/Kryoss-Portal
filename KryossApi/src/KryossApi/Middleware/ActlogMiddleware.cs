using System.Diagnostics;
using KryossApi.Data;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KryossApi.Middleware;

/// <summary>
/// Logs every HTTP request to the actlog table.
/// Captures method, path, response code, duration, and user context.
/// </summary>
public class ActlogMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpReq = await context.GetHttpRequestDataAsync();
        if (httpReq is null)
        {
            await next(context);
            return;
        }

        var method = httpReq.Method;
        var path = httpReq.Url.PathAndQuery;

        if (path.Contains("/v1/heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var sw = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();

            try
            {
                // Determine response code from the invocation result
                short responseCode = 200;
                var result = context.GetInvocationResult().Value;
                if (result is HttpResponseData response)
                    responseCode = (short)response.StatusCode;

                // Determine severity based on response code
                var severity = responseCode switch
                {
                    >= 500 => "ERR",
                    401 or 403 => "SEC",
                    >= 400 => "WARN",
                    _ => "INFO"
                };

                // Determine module from path
                var module = path switch
                {
                    _ when path.Contains("/v1/") => "agent",
                    _ when path.Contains("/v2/machines") => "machines",
                    _ when path.Contains("/v2/organizations") => "organizations",
                    _ when path.Contains("/v2/assessment") => "assessment",
                    _ when path.Contains("/v2/controls") || path.Contains("/v2/catalog") => "controls",
                    _ when path.Contains("/v2/reports") => "reports",
                    _ when path.Contains("/v2/enrollment") => "enrollment",
                    _ when path.Contains("/v2/dashboard") => "assessment",
                    _ when path.Contains("/v2/recycle-bin") => "recycle_bin",
                    _ when path.Contains("/v2/me") => "auth",
                    _ when path.Contains("/v2/roles") || path.Contains("/v2/users") => "admin",
                    _ => "api"
                };

                // Include machine hostname in agent requests for easier debugging
                var user = context.InstanceServices.GetRequiredService<ICurrentUserService>();
                var hostname = user.MachineHostname;
                var msgPrefix = hostname is not null ? $"[{hostname}] " : "";

                var actlog = context.InstanceServices.GetRequiredService<IActlogService>();
                await actlog.LogAsync(
                    severity: severity,
                    module: module,
                    action: $"{method.ToLowerInvariant()}.{path.Split('?')[0].TrimEnd('/').Split('/').LastOrDefault()}",
                    entityType: hostname is not null ? "machine" : null,
                    entityId: user.MachineId?.ToString(),
                    message: $"{msgPrefix}{method} {path} -> {responseCode} ({sw.ElapsedMilliseconds}ms)",
                    responseCode: responseCode,
                    durationMs: (int)sw.ElapsedMilliseconds
                );
            }
            catch (Exception ex)
            {
                // Never let actlog failures break the request
                var logger = context.InstanceServices.GetRequiredService<ILogger<ActlogMiddleware>>();
                logger.LogError(ex, "Failed to write actlog entry");
            }
        }
    }
}
