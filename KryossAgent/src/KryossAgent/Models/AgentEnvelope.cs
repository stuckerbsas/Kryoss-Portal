namespace KryossAgent.Models;

/// <summary>
/// Wire-format envelope used by the agent to ship encrypted payloads to
/// <c>/v1/results</c>. Byte-identical shape on both agent and server.
///
/// <para>
/// The format is JWE-inspired but intentionally narrower — we only support
/// ONE algorithm suite (RSA-OAEP-SHA256 + AES-256-GCM) and we do NOT
/// negotiate. The <c>Alg</c> field is read by the server and must match
/// the hardcoded server-side value. Any other value is rejected as an
/// attempted downgrade.
/// </para>
///
/// <para>
/// See <c>KryossApi/docs/security-baseline.md</c> for the full contract.
/// </para>
/// </summary>
public sealed class AgentEnvelope
{
    /// <summary>
    /// Format version. Bump when ANY other field's semantics change.
    /// Current: 1.
    /// </summary>
    public int V { get; set; } = 1;

    /// <summary>
    /// Algorithm suite identifier. Must be exactly
    /// <c>"RSA-OAEP-256+A256GCM"</c>.
    /// </summary>
    public string Alg { get; set; } = "RSA-OAEP-256+A256GCM";

    /// <summary>
    /// Base64: RSA-OAEP(SHA-256) ciphertext of the ephemeral 256-bit AES key.
    /// </summary>
    public string Epk { get; set; } = "";

    /// <summary>
    /// Base64: 96-bit GCM nonce, one-shot per envelope.
    /// </summary>
    public string Iv { get; set; } = "";

    /// <summary>
    /// Base64: AES-256-GCM ciphertext of the UTF-8 JSON payload.
    /// </summary>
    public string Ct { get; set; } = "";

    /// <summary>
    /// Base64: 128-bit GCM authentication tag.
    /// </summary>
    public string Tag { get; set; } = "";
}
