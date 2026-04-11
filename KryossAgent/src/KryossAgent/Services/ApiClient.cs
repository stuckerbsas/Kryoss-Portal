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
            Timeout = TimeSpan.FromSeconds(30)
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
        PlatformInfo? platform, HardwareInfo? hardware)
    {
        var enrollBody = new EnrollRequest
        {
            Code = code, Hostname = hostname,
            Os = platform?.Os, OsVersion = platform?.Version, OsBuild = platform?.Build
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
            throw new ApiException($"Enrollment failed ({response.StatusCode}): {error}");
        }

        return await response.Content.ReadFromJsonAsync(KryossJsonContext.Default.EnrollmentResponse);
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
            throw new ApiException($"Submit results failed ({response.StatusCode}): {error}");
        }

        return await response.Content.ReadFromJsonAsync(KryossJsonContext.Default.ResultsResponse);
    }

    /// <summary>
    /// POST /v1/hygiene — submit AD hygiene findings (HMAC signed).
    /// </summary>
    public async Task SubmitHygieneAsync(object hygienePayload)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(hygienePayload);
        var path = "/v1/hygiene";
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
            Console.Error.WriteLine($"[WARN] Hygiene upload failed ({response.StatusCode}): {error}");
        }
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
        // Not yet part of the HMAC canonical string (v1 format is frozen);
        // upgrading the canonical format is a coordinated server+agent
        // change tracked in security-baseline.md.
        request.Headers.Add("X-Hwid", _hwid);

        if (!string.IsNullOrEmpty(_config.ApiSecret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var bodyHash = Convert.ToHexString(
                SHA256.HashData(body ?? [])
            ).ToLowerInvariant();

            var signingString = $"{timestamp}{method.Method.ToUpperInvariant()}{path}{bodyHash}";
            var signature = Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(_config.ApiSecret),
                    Encoding.UTF8.GetBytes(signingString)
                )
            ).ToLowerInvariant();

            request.Headers.Add("X-Timestamp", timestamp);
            request.Headers.Add("X-Signature", signature);
        }

        return request;
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
