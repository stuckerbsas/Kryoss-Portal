using System.Security.Cryptography;
using System.Text.Json;
using KryossAgent.Models;

namespace KryossAgent.Services;

/// <summary>
/// Envelope-encryption primitive for outbound payloads. Owns NO HTTP,
/// NO configuration, NO retries — only crypto. That separation is
/// intentional: security primitives must be independently testable and
/// not depend on things that change often (URLs, timeouts, etc.).
///
/// <para>
/// Algorithm suite (fixed, no negotiation):
/// <list type="bullet">
///   <item>Symmetric: AES-256-GCM, 96-bit nonce, 128-bit tag.</item>
///   <item>Key wrap: RSA-OAEP with SHA-256 (never PKCS#1 v1.5).</item>
/// </list>
/// </para>
///
/// <para>
/// Threading: the server public key is immutable after construction and
/// <see cref="Seal"/> allocates all scratch state on the stack or the
/// heap per call, so a single <see cref="SecurityService"/> instance is
/// safe to share across threads for the life of the agent process.
/// </para>
///
/// <para>
/// Baseline reference: <c>KryossApi/docs/security-baseline.md</c>.
/// </para>
/// </summary>
public sealed class SecurityService : IDisposable
{
    private const int AesKeyBytes = 32;     // 256-bit
    private const int GcmNonceBytes = 12;   // 96-bit (GCM recommended)
    private const int GcmTagBytes = 16;     // 128-bit

    private readonly RSA _serverPublicKey;

    public SecurityService(string serverPublicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(serverPublicKeyPem))
            throw new ArgumentException("Missing server public key PEM", nameof(serverPublicKeyPem));

        _serverPublicKey = RSA.Create();
        try
        {
            _serverPublicKey.ImportFromPem(serverPublicKeyPem);
        }
        catch (Exception ex)
        {
            _serverPublicKey.Dispose();
            throw new CryptographicException(
                "Server public key PEM could not be imported. " +
                "Check that the agent was enrolled and the PEM is intact.", ex);
        }

        if (_serverPublicKey.KeySize < 2048)
        {
            _serverPublicKey.Dispose();
            throw new CryptographicException(
                $"Server public key is too weak ({_serverPublicKey.KeySize} bits). " +
                "Minimum is 2048 bits.");
        }
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> into an <see cref="AgentEnvelope"/>
    /// that the server can decrypt with its matching private key.
    /// </summary>
    /// <param name="plaintext">
    /// UTF-8 encoded payload bytes (usually JSON). Caller is responsible for
    /// choosing the encoding.
    /// </param>
    /// <returns>
    /// A fully populated <see cref="AgentEnvelope"/> whose fields are safe
    /// to serialize directly to the wire.
    /// </returns>
    public AgentEnvelope Seal(ReadOnlySpan<byte> plaintext)
    {
        // 1. Ephemeral symmetric state. Never reused across envelopes.
        //    AES key lives on the stack so it is wiped on scope exit;
        //    we also zero it explicitly to be defensive about optimizers.
        Span<byte> aesKey = stackalloc byte[AesKeyBytes];
        Span<byte> nonce = stackalloc byte[GcmNonceBytes];
        RandomNumberGenerator.Fill(aesKey);
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[GcmTagBytes];

        try
        {
            // 2. Payload encryption. No associated data yet — this will gain
            //    length-prefixed (hwid || tokenId) AAD in baseline step #7
            //    when hardware fingerprint lands. Bumping Alg at that point.
            using (var gcm = new AesGcm(aesKey, GcmTagBytes))
            {
                gcm.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            // 3. Wrap the AES key with RSA-OAEP(SHA-256). This is the ONLY
            //    acceptable padding — PKCS#1 v1.5 is vulnerable to Bleichenbacher
            //    oracle attacks and must not be used here.
            var wrappedKey = _serverPublicKey.Encrypt(
                aesKey.ToArray(),
                RSAEncryptionPadding.OaepSHA256);

            return new AgentEnvelope
            {
                V = 1,
                Alg = "RSA-OAEP-256+A256GCM",
                Epk = Convert.ToBase64String(wrappedKey),
                Iv = Convert.ToBase64String(nonce),
                Ct = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(tag)
            };
        }
        finally
        {
            // Defense-in-depth: wipe the key material before leaving scope.
            // Stack slots may be reused by the caller's frame.
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    /// <summary>
    /// Convenience: serialize <paramref name="envelope"/> to UTF-8 JSON
    /// bytes ready to write on an HTTP request. Uses the AOT-safe
    /// source-generated context.
    /// </summary>
    public static byte[] SerializeEnvelope(AgentEnvelope envelope)
        => JsonSerializer.SerializeToUtf8Bytes(envelope, KryossJsonContext.Default.AgentEnvelope);

    public void Dispose() => _serverPublicKey.Dispose();
}
