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
    private const int MaxRequestsPerMinutePerOrg = 200;

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
            path.EndsWith("/speedtest", StringComparison.OrdinalIgnoreCase))
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

        // ── Per-org aggregate rate limit ──
        {
            var orgRateKey = $"org:{org.Id:N}";
            var now2 = DateTime.UtcNow;
            var orgEntry = _rateLimits.GetOrAdd(orgRateKey, _ => (0, now2));
            if (now2 - orgEntry.WindowStart > TimeSpan.FromMinutes(1))
            {
                _rateLimits[orgRateKey] = (1, now2);
            }
            else if (orgEntry.Count >= MaxRequestsPerMinutePerOrg)
            {
                logger.LogWarning("Org rate limit exceeded for org {OrgId}", org.Id);
                var resp = httpReq.CreateResponse((System.Net.HttpStatusCode)429);
                await resp.WriteAsJsonAsync(new { error = "org_rate_limit_exceeded" });
                context.GetInvocationResult().Value = resp;
                return;
            }
            else
            {
                _rateLimits[orgRateKey] = (orgEntry.Count + 1, orgEntry.WindowStart);
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

            // Build the signing string and read body ONCE (shared across all key attempts)
            var signingResult = await BuildSigningString(httpReq, context, timestampStr, logger);
            if (signingResult.Result != HmacValidationResult.Valid)
            {
                var errorMsg = signingResult.Result switch
                {
                    HmacValidationResult.TimestampMissing => "Missing or invalid X-Timestamp",
                    HmacValidationResult.TimestampSkew => "HMAC timestamp skew — check agent clock sync",
                    _ => "HMAC validation failed"
                };
                var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
                await resp.WriteAsJsonAsync(new { error = errorMsg });
                context.GetInvocationResult().Value = resp;
                return;
            }

            // ── Per-machine HMAC validation chain (Kerberos-inspired) ──
            // Try machine keys before org-level fallback.
            var machineKeyUsed = false;
            var agentIdHeaderForMachine = httpReq.Headers.TryGetValues("X-Agent-Id", out var aidMachineVals)
                ? aidMachineVals.FirstOrDefault() : null;

            if (!string.IsNullOrEmpty(agentIdHeaderForMachine) && Guid.TryParse(agentIdHeaderForMachine, out var machineGuid))
            {
                var machineAuth = await db.Machines
                    .IgnoreQueryFilters()
                    .Where(m => m.AgentId == machineGuid && m.OrganizationId == org.Id && m.DeletedAt == null)
                    .Select(m => new
                    {
                        m.SessionKey,
                        m.SessionKeyExpiresAt,
                        m.PrevSessionKey,
                        m.PrevKeyExpiresAt,
                        m.MachineSecret,
                        m.AuthVersion
                    })
                    .FirstOrDefaultAsync();

                if (machineAuth is not null && machineAuth.AuthVersion >= 2)
                {
                    var keyRotationSvc = context.InstanceServices.GetRequiredService<IKeyRotationService>();
                    var sig = signature.ToLowerInvariant();

                    // 1. Try session_key (if not expired)
                    if (!string.IsNullOrEmpty(machineAuth.SessionKey)
                        && machineAuth.SessionKeyExpiresAt.HasValue
                        && machineAuth.SessionKeyExpiresAt.Value > DateTime.UtcNow
                        && keyRotationSvc.ValidateHmac(signingResult.SigningString!, sig, machineAuth.SessionKey))
                    {
                        machineKeyUsed = true;
                    }
                    // 2. Try prev_session_key (grace period)
                    else if (!string.IsNullOrEmpty(machineAuth.PrevSessionKey)
                        && machineAuth.PrevKeyExpiresAt.HasValue
                        && machineAuth.PrevKeyExpiresAt.Value > DateTime.UtcNow
                        && keyRotationSvc.ValidateHmac(signingResult.SigningString!, sig, machineAuth.PrevSessionKey))
                    {
                        machineKeyUsed = true;
                    }
                    // 3. Try machine_secret (reauth fallback — forces key rotation)
                    else if (!string.IsNullOrEmpty(machineAuth.MachineSecret)
                        && keyRotationSvc.ValidateHmac(signingResult.SigningString!, sig, machineAuth.MachineSecret))
                    {
                        machineKeyUsed = true;
                        context.Items["ForceKeyRotation"] = true;
                    }
                }
            }

            // 4. Org-level fallback (backward compat for agents < v2.2)
            if (!machineKeyUsed)
            {
                var isOrgValid = ValidateHmacDirect(signingResult.SigningString!, signature, org.ApiSecret);
                if (!isOrgValid)
                {
                    logger.LogWarning("HMAC validation failed for org {OrgId}", org.Id);
                    var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
                    await resp.WriteAsJsonAsync(new { error = "Invalid HMAC signature" });
                    context.GetInvocationResult().Value = resp;
                    return;
                }
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
    /// Builds the canonical signing string and validates the timestamp.
    /// Reads the body stream once and stores bytes in context for downstream use.
    /// Returns the signing string for multi-key validation.
    /// </summary>
    private static async Task<SigningStringResult> BuildSigningString(HttpRequestData req, FunctionContext context,
        string? timestampStr, ILogger logger)
    {
        if (!long.TryParse(timestampStr, out var timestamp))
        {
            logger.LogWarning("HMAC validation failed: missing or invalid X-Timestamp");
            return new(HmacValidationResult.TimestampMissing, null);
        }

        var requestTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        var skew = DateTimeOffset.UtcNow - requestTime;
        if (Math.Abs(skew.TotalMinutes) > MaxTimestampSkew.TotalMinutes)
        {
            logger.LogWarning("HMAC validation failed: timestamp skew {Skew:F1}min exceeds max {Max}min (agent clock: {AgentTime:O}, server: {ServerTime:O})",
                Math.Abs(skew.TotalMinutes), MaxTimestampSkew.TotalMinutes,
                requestTime, DateTimeOffset.UtcNow);
            return new(HmacValidationResult.TimestampSkew, null);
        }

        var method = req.Method.ToUpperInvariant();
        var path = req.Url.PathAndQuery;
        var agentIdForHmac = req.Headers.TryGetValues("X-Agent-Id", out var agentIdHmacVals)
            ? agentIdHmacVals.FirstOrDefault() ?? "" : "";

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
            context.Items["RequestBodyBytes"] = bodyBytes;
        }

        var bodyHash = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();
        var signingString = $"{timestampStr}{method}{path}{agentIdForHmac}{bodyHash}";

        return new(HmacValidationResult.Valid, signingString);
    }

    /// <summary>
    /// Validates HMAC using fixed-time comparison against a given secret.
    /// </summary>
    private static bool ValidateHmacDirect(string signingString, string signature, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var expectedSig = Convert.ToHexString(
            HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(signingString))
        ).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSig),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant())
        );
    }

    private readonly record struct SigningStringResult(HmacValidationResult Result, string? SigningString);
}

internal enum HmacValidationResult
{
    Valid,
    TimestampMissing,
    TimestampSkew,
    SignatureMismatch
}
