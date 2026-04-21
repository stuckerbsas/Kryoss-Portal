using System.Collections.Concurrent;
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
/// Also validates HMAC: HMAC(ApiSecret, timestamp + method + path + agentId + bodyHash).
/// Timestamp must be within 5 minutes (anti-replay).
/// HMAC signature is MANDATORY when the org has an ApiSecret configured (CRIT-03).
/// </summary>
public class ApiKeyAuthMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly TimeSpan MaxTimestampSkew = TimeSpan.FromMinutes(5);

    // ── HIGH-05: In-memory rate limiter per API key (= per machine) ──
    private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _rateLimits = new();
    private const int MaxRequestsPerMinute = 15;

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

        // Enroll and speedtest endpoints are public (no sensitive data)
        if (path.EndsWith("/enroll", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/speedtest", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/schedule", StringComparison.OrdinalIgnoreCase))
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

        // ── HIGH-05: Rate limiting per machine (X-Agent-Id) ──
        {
            var agentIdHeader = httpReq.Headers.TryGetValues("X-Agent-Id", out var aidValues)
                ? aidValues.FirstOrDefault() : null;
            var rateLimitKey = !string.IsNullOrEmpty(agentIdHeader) ? agentIdHeader : apiKey;

            var now = DateTime.UtcNow;
            var entry = _rateLimits.GetOrAdd(rateLimitKey, _ => (0, now));
            if (now - entry.WindowStart > TimeSpan.FromMinutes(1))
            {
                _rateLimits[rateLimitKey] = (1, now);
            }
            else if (entry.Count >= MaxRequestsPerMinute)
            {
                logger.LogWarning("Rate limit exceeded for {RateLimitKey}", rateLimitKey[..Math.Min(8, rateLimitKey.Length)]);
                var resp = httpReq.CreateResponse((System.Net.HttpStatusCode)429);
                await resp.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
                context.GetInvocationResult().Value = resp;
                return;
            }
            else
            {
                _rateLimits[rateLimitKey] = (entry.Count + 1, entry.WindowStart);
            }
        }

        // ── CRIT-03: HMAC signature is MANDATORY when org has an ApiSecret ──
        // Previously, omitting X-Signature would skip HMAC validation entirely,
        // allowing an attacker with just the API key to bypass signature checks.
        if (!string.IsNullOrEmpty(org.ApiSecret))
        {
            if (string.IsNullOrEmpty(signature))
            {
                logger.LogWarning("HMAC signature required but missing for org {OrgId}", org.Id);
                var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
                await resp.WriteAsJsonAsync(new { error = "X-Signature header required" });
                context.GetInvocationResult().Value = resp;
                return;
            }

            var hmacResult = await ValidateHmac(httpReq, context, org.ApiSecret, signature, timestampStr, logger);
            if (hmacResult != HmacValidationResult.Valid)
            {
                var errorMsg = hmacResult switch
                {
                    HmacValidationResult.TimestampMissing => "Missing or invalid X-Timestamp",
                    HmacValidationResult.TimestampSkew => "HMAC timestamp skew — check agent clock sync",
                    HmacValidationResult.SignatureMismatch => "Invalid HMAC signature",
                    _ => "HMAC validation failed"
                };
                var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
                await resp.WriteAsJsonAsync(new { error = errorMsg });
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
            currentUser.IpAddress =
                (httpReq.Headers.TryGetValues("X-Forwarded-For", out var fwdValues) ? fwdValues.FirstOrDefault() : null)
                ?? (httpReq.Headers.TryGetValues("X-Azure-ClientIP", out var azValues) ? azValues.FirstOrDefault() : null)
                ?? (httpReq.Headers.TryGetValues("X-Client-IP", out var cliValues) ? cliValues.FirstOrDefault() : null);
            logger.LogInformation("IP resolution: XFF={Xff} AzureClientIP={AzCli} XClientIP={XCli} → resolved={Ip}",
                httpReq.Headers.TryGetValues("X-Forwarded-For", out var xff2) ? xff2.FirstOrDefault() : "(none)",
                httpReq.Headers.TryGetValues("X-Azure-ClientIP", out var az2) ? az2.FirstOrDefault() : "(none)",
                httpReq.Headers.TryGetValues("X-Client-IP", out var cli2) ? cli2.FirstOrDefault() : "(none)",
                currentUser.IpAddress ?? "(null)");
        }

        await next(context);
    }

    /// <summary>
    /// Validates HMAC-SHA256 signature. Reads the body stream for hashing and stores
    /// the raw bytes in FunctionContext.Items["RequestBodyBytes"] so downstream
    /// functions can deserialize without re-reading the (possibly consumed) stream.
    /// </summary>
    private static async Task<HmacValidationResult> ValidateHmac(HttpRequestData req, FunctionContext context,
        string secret, string signature, string? timestampStr, ILogger logger)
    {
        // Validate timestamp (anti-replay)
        if (!long.TryParse(timestampStr, out var timestamp))
        {
            logger.LogWarning("HMAC validation failed: missing or invalid X-Timestamp");
            return HmacValidationResult.TimestampMissing;
        }

        var requestTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        var skew = DateTimeOffset.UtcNow - requestTime;
        if (Math.Abs(skew.TotalMinutes) > MaxTimestampSkew.TotalMinutes)
        {
            logger.LogWarning("HMAC validation failed: timestamp skew {Skew:F1}min exceeds max {Max}min (agent clock: {AgentTime:O}, server: {ServerTime:O})",
                Math.Abs(skew.TotalMinutes), MaxTimestampSkew.TotalMinutes,
                requestTime, DateTimeOffset.UtcNow);
            return HmacValidationResult.TimestampSkew;
        }

        // Build signing string: timestamp + method + path + agentId + bodyHash
        // Agent C-4: AgentId is now included in the canonical signing string
        // to prevent replay attacks that swap the X-Agent-Id header.
        var method = req.Method.ToUpperInvariant();
        var path = req.Url.PathAndQuery;
        var agentIdForHmac = req.Headers.TryGetValues("X-Agent-Id", out var agentIdHmacVals)
            ? agentIdHmacVals.FirstOrDefault() ?? "" : "";

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
        var signingString = $"{timestampStr}{method}{path}{agentIdForHmac}{bodyHash}";

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
            // Log signing string components (NOT the signatures themselves — MED-03)
            // to help diagnose agent/server mismatches.
            logger.LogWarning("HMAC mismatch for {Method} {Path} agentId={AgentId} body={Len}B ts={Ts}",
                method, path, agentIdForHmac, bodyBytes.Length, timestampStr);
        }

        return isValid ? HmacValidationResult.Valid : HmacValidationResult.SignatureMismatch;
    }
}

internal enum HmacValidationResult
{
    Valid,
    TimestampMissing,
    TimestampSkew,
    SignatureMismatch
}
