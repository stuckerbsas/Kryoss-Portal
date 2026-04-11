using System.Security.Cryptography;
using System.Text;
using KryossApi.Data;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KryossApi.Middleware;

/// <summary>
/// Authenticates agent requests via X-Api-Key header + HMAC-SHA256 signature.
/// Resolves org from API key and populates ICurrentUserService with org context.
/// Also validates HMAC: HMAC(ApiSecret, timestamp + method + path + bodyHash).
/// Timestamp must be within 5 minutes (anti-replay).
/// </summary>
public class ApiKeyAuthMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly TimeSpan MaxTimestampSkew = TimeSpan.FromMinutes(5);

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpReq = await context.GetHttpRequestDataAsync();
        if (httpReq is null)
        {
            await next(context);
            return;
        }

        // Only process agent routes (v1/*)
        var path = httpReq.Url.AbsolutePath;
        if (!path.Contains("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // Enroll endpoint is public (uses enrollment code, not API key)
        if (path.EndsWith("/enroll", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var apiKey = httpReq.Headers.TryGetValues("X-Api-Key", out var apiKeyValues)
            ? apiKeyValues.FirstOrDefault() : null;
        var signature = httpReq.Headers.TryGetValues("X-Signature", out var sigValues)
            ? sigValues.FirstOrDefault() : null;
        var timestampStr = httpReq.Headers.TryGetValues("X-Timestamp", out var tsValues)
            ? tsValues.FirstOrDefault() : null;

        if (string.IsNullOrEmpty(apiKey))
        {
            var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "X-Api-Key header required" });
            context.GetInvocationResult().Value = resp;
            return;
        }

        var logger = context.InstanceServices.GetRequiredService<ILogger<ApiKeyAuthMiddleware>>();
        var db = context.InstanceServices.GetRequiredService<KryossDbContext>();

        // Resolve org by API key
        var org = await db.Organizations
            .IgnoreQueryFilters()
            .Where(o => o.ApiKey == apiKey && o.DeletedAt == null)
            .Select(o => new { o.Id, o.FranchiseId, o.ApiSecret })
            .FirstOrDefaultAsync();

        if (org is null)
        {
            logger.LogWarning("Invalid API key attempted: {KeyPrefix}...", apiKey[..Math.Min(8, apiKey.Length)]);
            var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "Invalid API key" });
            context.GetInvocationResult().Value = resp;
            return;
        }

        // Validate HMAC signature if present (required for non-enroll agent routes)
        if (!string.IsNullOrEmpty(org.ApiSecret) && !string.IsNullOrEmpty(signature))
        {
            if (!await ValidateHmac(httpReq, context, org.ApiSecret, signature, timestampStr, logger))
            {
                var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
                await resp.WriteAsJsonAsync(new { error = "Invalid HMAC signature" });
                context.GetInvocationResult().Value = resp;
                return;
            }

            // Anti-replay: reject a signature we've already seen within the
            // TTL window. HMAC timestamp check gives ±5 min freshness, the
            // nonce cache plugs the remaining "replay within the window" gap.
            // Include X-Agent-Id in the nonce key so parallel requests from
            // different machines in the same org don't collide.
            var agentIdForNonce = httpReq.Headers.TryGetValues("X-Agent-Id", out var agentIdNonceVals)
                ? agentIdNonceVals.FirstOrDefault() ?? "" : "";
            var nonceKey = $"{agentIdForNonce}:{signature}";
            var nonceCache = context.InstanceServices.GetRequiredService<INonceCache>();
            if (!nonceCache.TryRegister(org.Id, nonceKey))
            {
                logger.LogWarning("HMAC replay detected for org {OrgId} — signature already used",
                    org.Id);
                var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
                await resp.WriteAsJsonAsync(new { error = "Replay detected" });
                context.GetInvocationResult().Value = resp;
                return;
            }
        }

        // Populate current user context for agent requests
        var currentUser = context.InstanceServices.GetRequiredService<ICurrentUserService>() as CurrentUserService;
        if (currentUser is not null)
        {
            currentUser.OrganizationId = org.Id;
            currentUser.FranchiseId = org.FranchiseId;
            currentUser.IpAddress = httpReq.Headers.TryGetValues("X-Forwarded-For", out var fwdValues)
                ? fwdValues.FirstOrDefault() : null;
        }

        await next(context);
    }

    /// <summary>
    /// Validates HMAC-SHA256 signature. Reads the body stream for hashing and stores
    /// the raw bytes in FunctionContext.Items["RequestBodyBytes"] so downstream
    /// functions can deserialize without re-reading the (possibly consumed) stream.
    /// </summary>
    private static async Task<bool> ValidateHmac(HttpRequestData req, FunctionContext context,
        string secret, string signature, string? timestampStr, ILogger logger)
    {
        // Validate timestamp (anti-replay)
        if (!long.TryParse(timestampStr, out var timestamp))
        {
            logger.LogWarning("HMAC validation failed: missing or invalid X-Timestamp");
            return false;
        }

        var requestTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        var skew = DateTimeOffset.UtcNow - requestTime;
        if (Math.Abs(skew.TotalMinutes) > MaxTimestampSkew.TotalMinutes)
        {
            logger.LogWarning("HMAC validation failed: timestamp skew {Skew}min exceeds max {Max}min",
                Math.Abs(skew.TotalMinutes), MaxTimestampSkew.TotalMinutes);
            return false;
        }

        // Build signing string: timestamp + method + path + bodyHash
        var method = req.Method.ToUpperInvariant();
        var path = req.Url.PathAndQuery;

        // Read body for hashing — must use async (Kestrel disallows sync IO)
        byte[] bodyBytes;
        if (req.Body.CanSeek)
        {
            req.Body.Position = 0;
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms);
            bodyBytes = ms.ToArray();
            req.Body.Position = 0;
        }
        else
        {
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms);
            bodyBytes = ms.ToArray();
            // Stream is consumed — store bytes so downstream functions can read them
            context.Items["RequestBodyBytes"] = bodyBytes;
        }

        var bodyHash = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();
        var signingString = $"{timestampStr}{method}{path}{bodyHash}";

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var expectedSig = Convert.ToHexString(
            HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(signingString))
        ).ToLowerInvariant();

        var isValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSig),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant())
        );

        if (!isValid)
        {
            logger.LogWarning("HMAC mismatch — debug info:");
            logger.LogWarning("  Method: {Method} | Path: {Path} | BodyLen: {Len}", method, path, bodyBytes.Length);
            logger.LogWarning("  BodyHash:   {Hash}", bodyHash);
            logger.LogWarning("  Expected:   {Exp}", expectedSig[..16] + "...");
            logger.LogWarning("  Got:        {Got}", signature.ToLowerInvariant()[..Math.Min(16, signature.Length)] + "...");
        }

        return isValid;
    }
}
