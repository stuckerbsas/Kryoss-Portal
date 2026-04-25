using KryossApi.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KryossApi.Middleware;

/// <summary>
/// Sets SESSION_CONTEXT variables on the SQL connection so RLS policies
/// can filter data by is_admin, franchise_id, and organization_id.
/// Must run AFTER auth middleware has populated ICurrentUserService.
///
/// SKIPS agent routes (/v1/*) — agents authenticate via API Key/HMAC,
/// not portal users, so RLS doesn't apply to them.
/// Also gracefully handles LocalDB which doesn't support sp_set_session_context.
/// </summary>
public class RlsMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        // Skip RLS for agent routes — agents use API Key auth, not portal user context
        var httpReq = await context.GetHttpRequestDataAsync();
        if (httpReq is not null)
        {
            var path = httpReq.Url.AbsolutePath;
            if (path.Contains("/v1/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/v2/reports/diagnose/", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("/v2/version", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }
        }

        var user = context.InstanceServices.GetRequiredService<ICurrentUserService>();

        // Only set SESSION_CONTEXT if we have an authenticated portal user
        if (user.UserId != Guid.Empty)
        {
            try
            {
                var db = context.InstanceServices.GetRequiredService<KryossDbContext>();
                var conn = db.Database.GetDbConnection();

                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    EXEC sp_set_session_context @key = N'is_admin', @value = @isAdmin, @read_only = 1;
                    EXEC sp_set_session_context @key = N'franchise_id', @value = @franchiseId, @read_only = 1;
                    EXEC sp_set_session_context @key = N'organization_id', @value = @orgId, @read_only = 1;
                    """;

                var pAdmin = cmd.CreateParameter();
                pAdmin.ParameterName = "@isAdmin";
                pAdmin.Value = user.IsAdmin ? 1 : 0;
                cmd.Parameters.Add(pAdmin);

                var pFranchise = cmd.CreateParameter();
                pFranchise.ParameterName = "@franchiseId";
                pFranchise.Value = user.FranchiseId.HasValue
                    ? user.FranchiseId.Value.ToString()
                    : (object)DBNull.Value;
                cmd.Parameters.Add(pFranchise);

                var pOrg = cmd.CreateParameter();
                pOrg.ParameterName = "@orgId";
                pOrg.Value = user.OrganizationId.HasValue
                    ? user.OrganizationId.Value.ToString()
                    : (object)DBNull.Value;
                cmd.Parameters.Add(pOrg);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) when (ex.Message.Contains("sp_set_session_context", StringComparison.OrdinalIgnoreCase)
                                    || ex.Message.Contains("Could not find stored procedure", StringComparison.OrdinalIgnoreCase))
            {
                // LocalDB doesn't support sp_set_session_context — skip gracefully
                var logger = context.InstanceServices.GetService<ILogger<RlsMiddleware>>();
                logger?.LogDebug("RLS SESSION_CONTEXT skipped (not supported on this SQL edition)");
            }
        }

        await next(context);
    }
}
