using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace KryossApi.Middleware;

public class SecurityHeadersMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        await next(context);

        var httpReq = await context.GetHttpRequestDataAsync();
        if (httpReq is null) return;

        var result = context.GetInvocationResult();
        if (result.Value is not Microsoft.Azure.Functions.Worker.Http.HttpResponseData resp) return;

        resp.Headers.TryAddWithoutValidation("X-Content-Type-Options", "nosniff");
        resp.Headers.TryAddWithoutValidation("X-Frame-Options", "DENY");
        resp.Headers.TryAddWithoutValidation("X-XSS-Protection", "0");
        resp.Headers.TryAddWithoutValidation("Referrer-Policy", "strict-origin-when-cross-origin");
        resp.Headers.TryAddWithoutValidation("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
        resp.Headers.TryAddWithoutValidation("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
        resp.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
    }
}
