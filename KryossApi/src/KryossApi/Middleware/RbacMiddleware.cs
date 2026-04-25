using System.Net;
using System.Reflection;
using KryossApi.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace KryossApi.Middleware;

/// <summary>
/// Checks if the current user has the required permission for the endpoint.
/// Uses [RequirePermission("module:action")] attribute on functions.
/// Agent routes (v1/*) skip RBAC — they use API Key auth.
/// </summary>
public class RbacMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpReq = await context.GetHttpRequestDataAsync();
        if (httpReq is null)
        {
            await next(context);
            return;
        }

        // Agent routes and public endpoints don't use RBAC
        var path = httpReq.Url.AbsolutePath;
        if (path.Contains("/v1/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/v2/reports/diagnose/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/v2/version", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/consent-callback", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/connect-callback", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Find the target function method and check for RequirePermission attribute
        var entryPoint = context.FunctionDefinition.EntryPoint;
        var lastDot = entryPoint.LastIndexOf('.');
        if (lastDot < 0)
        {
            await next(context);
            return;
        }

        var typeName = entryPoint[..lastDot];
        var methodName = entryPoint[(lastDot + 1)..];
        var targetType = Assembly.GetExecutingAssembly().GetType(typeName);
        var method = targetType?.GetMethod(methodName);

        var requiredPermission = method?.GetCustomAttribute<RequirePermissionAttribute>()?.Permission
            ?? targetType?.GetCustomAttribute<RequirePermissionAttribute>()?.Permission;

        if (requiredPermission is null)
        {
            await next(context);
            return;
        }

        var user = context.InstanceServices.GetRequiredService<ICurrentUserService>();

        // super_admin bypasses all permission checks
        if (user.IsAdmin)
        {
            await next(context);
            return;
        }

        if (user.UserId == Guid.Empty)
        {
            var resp = httpReq.CreateResponse(HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "Authentication required" });
            context.GetInvocationResult().Value = resp;
            return;
        }

        if (!user.Permissions.Contains(requiredPermission))
        {
            var resp = httpReq.CreateResponse(HttpStatusCode.Forbidden);
            await resp.WriteAsJsonAsync(new { error = $"Permission '{requiredPermission}' required" });
            context.GetInvocationResult().Value = resp;
            return;
        }

        await next(context);
    }
}

/// <summary>
/// Marks a function as requiring a specific permission slug (e.g., "assessment:read").
/// Can be applied to the function class or the Run method.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequirePermissionAttribute : Attribute
{
    public string Permission { get; }
    public RequirePermissionAttribute(string permission) => Permission = permission;
}
