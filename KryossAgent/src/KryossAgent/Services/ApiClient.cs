using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KryossAgent.Config;
using KryossAgent.Models;
using static KryossAgent.Models.KryossJsonContext;

namespace KryossAgent.Services;

/// <summary>
/// HTTP client for Kryoss API. Handles HMAC-SHA256 request signing for all
/// authenticated endpoints AND envelope encryption (RSA-OAEP + AES-GCM)
/// for the results upload. See KryossApi/docs/security-baseline.md.
/// </summary>
public class ApiClient : IDisposable
{
    private const string EnvelopeContentType = "application/kryoss-envelope+json";

    private readonly HttpClient _http;
    private readonly AgentConfig _config;
    private readonly SecurityService? _security;

    // Hardware fingerprint is computed once at startup and attached to every
    // signed request. The server binds it to the machine on first contact
    // and rejects any subsequent request whose hwid differs — this is the
    // primary defense against API-key + agent-id cloning to another host.
    // See KryossApi/docs/security-baseline.md §Hardware binding.
    private readonly string _hwid = HardwareFingerprint.Compute();

    public ApiClient(AgentConfig config)
    {
        _config = config;

        // Pinned TLS handler — rejects any server whose SPKI doesn't match
        // the configured pin(s). Runs in log-only mode until SpkiPins is
        // populated in the registry, so a fresh install doesn't hard-fail
        // on day one. See PinnedHttpHandler for the rotation story.
        var handler = new PinnedHttpHandler(config.SpkiPins);
        _http = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri(config.ApiUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(120)
        };

        // Envelope encryption is only available after the agent is enrolled
        // and has received the org's public key. Pre-enrollment flow does
        // not need encryption (the enrollment endpoint itself is plaintext
        // because the agent has nothing to encrypt TO yet).
        if (!string.IsNullOrWhiteSpace(_config.PublicKeyPem))
        {
            try
            {
                _security = new SecurityService(_config.PublicKeyPem);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[WARN] SecurityService init failed — payloads will NOT be encrypted: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// POST /v1/enroll — no HMAC needed (public endpoint).
    /// </summary>
    public async Task<EnrollmentResponse?> EnrollAsync(string code, string hostname,
        PlatformInfo? platform, int productType = 0)
    {
        var enrollBody = new EnrollRequest
        {
            Code = code, Hostname = hostname,
            Os = platform?.Os, OsVersion = platform?.Version, OsBuild = platform?.Build,
            ProductType = productType
        };
        var json = JsonSerializer.Serialize(enrollBody, KryossJsonContext.Default.EnrollRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Send hwid on enrollment so the server can bind it immediately,
        // rather than waiting for the first signed request.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/enroll")
        {
            Content = content
        };
        request.Headers.Add("X-Hwid", _hwid);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string error;
            try { error = await response.Content.ReadAsStringAsync(); }
            catch { error = response.StatusCode.ToString(); }
            LogAuthFailure((int)response.StatusCode, "/v1/enroll");
            throw new ApiException($"Enrollment failed ({response.StatusCode}): {error}");
        }

        return await response.Content.ReadFromJsonAsync(KryossJsonContext.Default.EnrollmentResponse);
    }

    /// <summary>
    /// GET /v1/schedule — HMAC signed. Returns the assigned scan time slot.
    /// </summary>
    public async Task<ScheduleResponse?> GetScheduleAsync()
    {
        var path = "/v1/schedule";
        var request = CreateSignedRequest(HttpMethod.Get, path);
        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            LogAuthFailure((int)response.StatusCode, "/v1/schedule");
            return null;
        }

        return await response.Content.ReadFromJsonAsync(KryossJsonContext.Default.ScheduleResponse);
    }

    /// <summary>
    /// GET /v1/controls?assessmentId=X — HMAC signed.
    /// </summary>
    public async Task<ControlsResponse?> GetControlsAsync(int assessmentId)
    {
        var path = $"/v1/controls?assessmentId={assessmentId}";
        var request = CreateSignedRequest(HttpMethod.Get, path);
        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            string error;
            try { error = await response.Content.ReadAsStringAsync(); }
            catch { error = response.StatusCode.ToString(); }
            LogAuthFailure((int)response.StatusCode, "/v1/controls");
            throw new ApiException($"Get controls failed ({response.StatusCode}): {error}");
        }

        return await response.Content.ReadFromJsonAsync(KryossJsonContext.Default.ControlsResponse);
    }

    /// <summary>
    /// POST /v1/results — HMAC signed + envelope encrypted.
    ///
    /// <para>
    /// Flow: serialize payload → seal into AgentEnvelope (RSA+AES-GCM)
    /// → serialize envelope → HMAC-sign the envelope bytes → POST with
    /// content-type <c>application/kryoss-envelope+json</c>.
    /// </para>
    ///
    /// <para>
    /// The HMAC still covers the envelope bytes, so the transport layer
    /// is authenticated independently of the envelope integrity (which
    /// is guaranteed by GCM's auth tag). Defense in depth.
    /// </para>
    /// </summary>
    public async Task<ResultsResponse?> SubmitResultsAsync(AssessmentPayload payload)
    {
        // 1. Serialize the payload to UTF-8 JSON bytes.
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            payload, KryossJsonContext.Default.AssessmentPayload);

        // 2. Build the request body: envelope bytes if encryption is
        //    available, otherwise fall back to raw JSON for backward
        //    compatibility during the rollout window. This fallback will
        //    be removed once all deployed servers accept envelopes
        //    (tracked in security-baseline.md).
        byte[] bodyBytes;
        string contentType;
        if (_security is not null)
        {
            var envelope = _security.Seal(plaintext);
            bodyBytes = SecurityService.SerializeEnvelope(envelope);
            contentType = EnvelopeContentType;
        }
        else
        {
            bodyBytes = plaintext;
            contentType = "application/json";
            if (Environment.GetEnvironmentVariable("KRYOSS_VERBOSE") == "1")
                Console.Error.WriteLine(
                    "[WARN] Submitting results WITHOUT envelope encryption.");
        }

        var path = "/v1/results";
        var request = CreateSignedRequest(HttpMethod.Post, path, bodyBytes);
        request.Content = new ByteArrayContent(bodyBytes);
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string error;
            try { error = await response.Content.ReadAsStringAsync(); }
            catch { error = response.StatusCode.ToString(); }
            LogAuthFailure((int)response.StatusCode, "/v1/results");
            throw new ApiException($"Submit results failed ({response.StatusCode}): {error}");
        }

        return await response.Content.ReadFromJsonAsync(KryossJsonContext.Default.ResultsResponse);
    }

    /// <summary>
    /// POST /v1/hygiene — submit AD hygiene findings (HMAC signed).
    /// </summary>
    public async Task SubmitHygieneAsync(Models.HygienePayload hygienePayload)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(hygienePayload, Models.KryossJsonContext.Default.HygienePayload);
        var path = "/v1/hygiene";
        var request = CreateSignedRequest(HttpMethod.Post, path, json);
        request.Content = new ByteArrayContent(json);
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await SendWithRetryAsync(request, json);
        if (!response.IsSuccessStatusCode)
        {
            string error;
            try { error = await response.Content.ReadAsStringAsync(); }
            catch { error = response.StatusCode.ToString(); }
            Console.Error.WriteLine($"[WARN] Hygiene upload failed ({response.StatusCode}): {error}");
        }
    }

    /// <summary>
    /// Upload port scan results from a network scan to the API.
    /// </summary>
    public async Task SubmitPortResultsAsync(Models.PortPayload portPayload)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(portPayload, Models.KryossJsonContext.Default.PortPayload);
        var path = "/v1/ports";
        var request = CreateSignedRequest(HttpMethod.Post, path, json);
        request.Content = new ByteArrayContent(json);
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await SendWithRetryAsync(request, json);
        if (!response.IsSuccessStatusCode)
        {
            string error;
            try { error = await response.Content.ReadAsStringAsync(); }
            catch { error = response.StatusCode.ToString(); }
            Console.Error.WriteLine($"[WARN] Port scan upload failed ({response.StatusCode}): {error}");
        }
    }

    public async Task<(int Saved, int Skipped)> SubmitPortResultsBulkAsync(Models.PortBulkPayload bulkPayload)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(bulkPayload, Models.KryossJsonContext.Default.PortBulkPayload);
        var path = "/v1/ports/bulk";
        var request = CreateSignedRequest(HttpMethod.Post, path, json);
        request.Content = new ByteArrayContent(json);
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await SendWithRetryAsync(request, json);
        if (!response.IsSuccessStatusCode)
        {
            string error;
            try { error = await response.Content.ReadAsStringAsync(); }
            catch { error = response.StatusCode.ToString(); }
            Console.Error.WriteLine($"[WARN] Bulk port upload failed ({response.StatusCode}): {error}");
            return (0, bulkPayload.Machines.Count);
        }

        try
        {
            var respBody = await response.Content.ReadAsStringAsync();
            var doc = System.Text.Json.JsonDocument.Parse(respBody);
            var saved = doc.RootElement.GetProperty("saved").GetInt32();
            var skipped = doc.RootElement.GetProperty("skipped").GetInt32();
            return (saved, skipped);
        }
        catch { return (bulkPayload.Machines.Count, 0); }
    }

    public async Task<Models.SnmpCredentials?> GetSnmpCredentialsAsync()
    {
        var path = "/v1/snmp-config";
        var request = CreateSignedRequest(HttpMethod.Get, path);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsByteArrayAsync();
        return System.Text.Json.JsonSerializer.Deserialize(json, Models.KryossJsonContext.Default.SnmpCredentials);
    }

    public async Task<Models.SnmpProfilesResponse?> GetSnmpProfilesAsync(List<string> sysObjectIds)
    {
        if (sysObjectIds.Count == 0) return null;
        var qs = string.Join(",", sysObjectIds);
        var path = $"/v1/snmp-profiles?sysObjectIds={Uri.EscapeDataString(qs)}";
        var request = CreateSignedRequest(HttpMethod.Get, path);
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsByteArrayAsync();
        return System.Text.Json.JsonSerializer.Deserialize(json, Models.KryossJsonContext.Default.SnmpProfilesResponse);
    }

    [UnconditionalSuppressMessage("AOT", "IL2026",
        Justification = "SNMP payloads use source-gen serializable types.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "SNMP payloads use source-gen serializable types.")]
    public async Task SubmitSnmpResultsAsync(Models.SnmpScanResult snmpResult)
    {
        // Large networks can have 200+ devices — batch in chunks of 50
        // to avoid HttpClient timeout and server-side processing bottleneck.
        const int batchSize = 50;
        var allDevices = snmpResult.Devices;

        for (int i = 0; i < allDevices.Count; i += batchSize)
        {
            var batch = new Models.SnmpScanResult
            {
                Devices = allDevices.Skip(i).Take(batchSize).ToList(),
                Unreachable = i == 0 ? snmpResult.Unreachable : [],
                ScannedAt = snmpResult.ScannedAt,
            };

            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                batch, Models.KryossJsonContext.Default.SnmpScanResult);
            var path = "/v1/snmp";
            var request = CreateSignedRequest(HttpMethod.Post, path, json);
            request.Content = new ByteArrayContent(json);
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            var response = await SendWithRetryAsync(request, json);
            if (!response.IsSuccessStatusCode)
            {
                string error;
                try { error = await response.Content.ReadAsStringAsync(); }
                catch { error = response.StatusCode.ToString(); }
                Console.Error.WriteLine($"[WARN] SNMP upload batch {i / batchSize + 1} failed ({response.StatusCode}): {error}");
            }
        }
    }

    public async Task<string?> DownloadReportAsync(string type = "preventas", string tone = "detailed")
    {
        var path = $"/v1/report?type={type}&tone={tone}";
        var request = CreateSignedRequest(HttpMethod.Get, path);
        request.Headers.Add("Accept", "text/html");

        try
        {
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync();
        }
        catch { return null; }
    }

    public async Task<HeartbeatResponse?> SendHeartbeatAsync(HeartbeatPayload heartbeat)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(heartbeat, KryossJsonContext.Default.HeartbeatPayload);
        var path = "/v1/heartbeat";
        var request = CreateSignedRequest(HttpMethod.Post, path, json);
        request.Content = new ByteArrayContent(json);
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        try
        {
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                LogAuthFailure((int)response.StatusCode, "/v1/heartbeat");
                return null;
            }
            var stream = await response.Content.ReadAsStreamAsync();
            var hbResponse = await JsonSerializer.DeserializeAsync(stream, KryossJsonContext.Default.HeartbeatResponse);
            if (hbResponse?.NewMachineSecret is not null)
                _config.MachineSecret = hbResponse.NewMachineSecret;
            if (hbResponse?.NewSessionKey is not null)
            {
                _config.SessionKey = hbResponse.NewSessionKey;
                _config.SessionKeyExpiresAt = hbResponse.NewSessionKeyExpiresAt;
            }
            bool configChanged = hbResponse?.NewMachineSecret is not null || hbResponse?.NewSessionKey is not null;
            if (hbResponse?.Config is { } rc)
            {
                _config.ComplianceIntervalHours = rc.ComplianceIntervalHours;
                _config.ScanIntervalMinutes = rc.SnmpIntervalMinutes;
                _config.EnableNetworkScan = rc.EnableNetworkScan;
                _config.NetworkScanIntervalHours = rc.NetworkScanIntervalHours;
                _config.EnablePassiveDiscovery = rc.EnablePassiveDiscovery;
                configChanged = true;
            }
            if (configChanged)
                _config.Save();
            return hbResponse;
        }
        catch { return null; }
    }

    public async Task<bool> ReportTaskResultAsync(TaskResultPayload result)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(result, KryossJsonContext.Default.TaskResultPayload);
        var path = "/v1/task-result";
        var request = CreateSignedRequest(HttpMethod.Post, path, json);
        request.Content = new ByteArrayContent(json);
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        try
        {
            var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// POST /v1/collect — upload an offline collect payload on behalf of another machine.
    /// </summary>
    public async Task SubmitCollectAsync(OfflineCollectPayload envelope)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, KryossJsonContext.Default.OfflineCollectPayload);
        var path = "/v1/collect";
        var request = CreateSignedRequest(HttpMethod.Post, path, json);
        request.Content = new ByteArrayContent(json);
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string error;
            try { error = await response.Content.ReadAsStringAsync(); }
            catch { error = response.StatusCode.ToString(); }
            throw new ApiException($"Collect upload failed ({response.StatusCode}): {error}");
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, byte[]? bodyBytes = null, string? contentType = null)
    {
        const int maxRetries = 3;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                // Re-create the request (HttpRequestMessage can't be resent)
                var path = request.RequestUri!.OriginalString;
                var method = request.Method;
                request.Dispose();
                request = CreateSignedRequest(method, path, bodyBytes);
                if (bodyBytes != null)
                {
                    request.Content = new ByteArrayContent(bodyBytes);
                    request.Content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue(contentType ?? "application/json");
                }
            }

            var response = await _http.SendAsync(request);

            if ((int)response.StatusCode == 429)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta?.TotalMilliseconds
                    ?? (1000 * Math.Pow(2, attempt));
                var delay = (int)Math.Min(retryAfter, 30000);
                if (Environment.GetEnvironmentVariable("KRYOSS_VERBOSE") == "1")
                    Console.Error.WriteLine($"  [RETRY] 429 on attempt {attempt + 1}, waiting {delay}ms");
                await Task.Delay(delay);
                response.Dispose();
                continue;
            }

            return response;
        }

        // Final attempt exhausted — return last 429 so caller can handle
        request = CreateSignedRequest(request.Method, request.RequestUri!.OriginalString, bodyBytes);
        if (bodyBytes != null)
        {
            request.Content = new ByteArrayContent(bodyBytes);
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(contentType ?? "application/json");
        }
        return await _http.SendAsync(request);
    }

    private HttpRequestMessage CreateSignedRequest(HttpMethod method, string path, byte[]? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Api-Key", _config.ApiKey);

        // Identify the specific machine inside the org (API key is per-org).
        // Backend uses this to resolve machines.platform_id for scope filtering.
        if (_config.AgentId != Guid.Empty)
            request.Headers.Add("X-Agent-Id", _config.AgentId.ToString());

        // Hardware fingerprint — see field comment at the top of the class.
        request.Headers.Add("X-Hwid", _hwid);

        var sessionExpired = _config.SessionKeyExpiresAt.HasValue && _config.SessionKeyExpiresAt.Value < DateTime.UtcNow;
        var signingKey = (_config.SessionKey is not null && !sessionExpired ? _config.SessionKey : null)
            ?? _config.MachineSecret ?? _config.ApiSecret;
        if (!string.IsNullOrEmpty(signingKey))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var bodyHash = Convert.ToHexString(
                SHA256.HashData(body ?? [])
            ).ToLowerInvariant();

            // C-4: Include AgentId in the HMAC canonical string to prevent
            // replay attacks that swap the X-Agent-Id header. The server-side
            // validation in ApiKeyAuthMiddleware.cs matches this format.
            var agentId = _config.AgentId != Guid.Empty ? _config.AgentId.ToString() : "";
            var signingString = $"{timestamp}{method.Method.ToUpperInvariant()}{path}{agentId}{bodyHash}";
            var keyBytes = Encoding.UTF8.GetBytes(signingKey);
            var signature = Convert.ToHexString(
                HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(signingString))
            ).ToLowerInvariant();
            CryptographicOperations.ZeroMemory(keyBytes);

            if (Environment.GetEnvironmentVariable("KRYOSS_VERBOSE") == "1")
            {
                Console.Error.WriteLine($"  [HMAC] ts={timestamp} method={method.Method.ToUpperInvariant()} path={path}");
                Console.Error.WriteLine($"  [HMAC] agentId={agentId} bodyLen={body?.Length ?? 0} bodyHash={bodyHash[..16]}...");
                Console.Error.WriteLine($"  [HMAC] keyLen={signingKey.Length} keySource={(_config.SessionKey is not null && !sessionExpired ? "session" : _config.MachineSecret is not null ? "machine" : "org")}");
                Console.Error.WriteLine($"  [HMAC] localUtc={DateTimeOffset.UtcNow:O}");
            }

            request.Headers.Add("X-Timestamp", timestamp);
            request.Headers.Add("X-Signature", signature);
        }
        else if (Environment.GetEnvironmentVariable("KRYOSS_VERBOSE") == "1")
        {
            Console.Error.WriteLine($"  [HMAC] WARNING: no signing key available — no HMAC signature will be sent");
        }

        return request;
    }

    public async Task<VersionInfo?> CheckLatestVersionAsync()
    {
        var path = "/v1/agent/latest-version";
        var request = CreateSignedRequest(HttpMethod.Get, path);
        try
        {
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            return System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    public async Task<byte[]?> DownloadAgentBinaryAsync()
    {
        var path = "/v1/agent/download";
        var request = CreateSignedRequest(HttpMethod.Get, path);
        try
        {
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch { return null; }
    }

    private static void LogAuthFailure(int statusCode, string endpoint)
    {
        if (statusCode is not (401 or 403 or 429)) return;
        var msg = $"Auth failure on {endpoint}: HTTP {statusCode}";
        try
        {
            if (!EventLog.SourceExists("KryossAgent"))
                EventLog.CreateEventSource("KryossAgent", "Application");
            EventLog.WriteEntry("KryossAgent", msg, EventLogEntryType.Warning, 5001);
        }
        catch { /* non-SYSTEM context may lack EventLog write access */ }
    }

    public void Dispose()
    {
        _security?.Dispose();
        _http.Dispose();
    }
}

public class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}
