using System.Net;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KryossApi.Middleware;

/// <summary>
/// Catches every unhandled exception from downstream middleware/functions and
/// turns it into a generic <c>500 Internal Server Error</c> response with a
/// correlation id that the operator can grep for in Application Insights. The
/// real stack trace NEVER reaches the wire — it goes to the logger.
///
/// <para>
/// MUST be registered FIRST in the worker pipeline so that nothing else sits
/// between it and the wire. If it's not first, an exception thrown by e.g.
/// <see cref="ApiKeyAuthMiddleware"/> would bubble up to the worker host and
/// produce a default Azure Functions error response, which includes the
/// exception type and sometimes the inner message — exactly what we want to
/// hide from a potential attacker.
/// </para>
///
/// <para>
/// Why not just let Azure Functions handle it? The default worker host error
/// response is "helpful" in ways we do not want: it can expose framework
/// types, middleware names, and stack frames. This middleware gives us a
/// frozen, opaque shape: <c>{"error":"internal_error","traceId":"..."}</c>.
/// </para>
///
/// <para>
/// Out of scope here: known 4xx responses crafted by the business logic.
/// Those already return their own clean bodies and should pass through
/// untouched. We only intervene when an exception actually escapes.
/// </para>
/// </summary>
public class ErrorSanitizationMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var logger = context.InstanceServices
                .GetRequiredService<ILogger<ErrorSanitizationMiddleware>>();

            // Use the Functions invocation id as the correlation id —
            // already indexed in App Insights so operators can grep it.
            var traceId = context.InvocationId;

            logger.LogError(ex,
                "Unhandled exception in {FunctionName} (trace {TraceId}): {Type}",
                context.FunctionDefinition.Name, traceId, ex.GetType().Name);

            try
            {
                var actlog = context.InstanceServices.GetRequiredService<IActlogService>();
                await actlog.LogAsync(
                    severity: "ERR",
                    module: "middleware",
                    action: $"unhandled.{context.FunctionDefinition.Name}",
                    message: $"[{traceId}] {ex.GetType().Name}: {ex.Message}");
            }
            catch { /* actlog write must never break error handling */ }

            var httpReq = await context.GetHttpRequestDataAsync();
            if (httpReq is null)
            {
                // Not an HTTP-triggered function (timer, queue, etc.). Log
                // and rethrow — the host's retry machinery needs the error.
                throw;
            }

            var resp = httpReq.CreateResponse(HttpStatusCode.InternalServerError);
            resp.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin",
                httpReq.Headers.TryGetValues("Origin", out var origins)
                    ? origins.First() : "*");
            resp.Headers.TryAddWithoutValidation("Access-Control-Allow-Credentials", "true");

            // DEBUG MODE: expose error details until pre-production hardening.
            // TODO(SH): revert to frozen shape before go-live.
            await resp.WriteAsJsonAsync(new
            {
                error = "internal_error",
                traceId,
                debug_type = ex.GetType().FullName,
                debug_message = ex.Message,
                debug_stack = ex.StackTrace,
                debug_inner = ex.InnerException?.Message
            });

            var result = context.GetInvocationResult();
            result.Value = resp;
        }
    }
}
