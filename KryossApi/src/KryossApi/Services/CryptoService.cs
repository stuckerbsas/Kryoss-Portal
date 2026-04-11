using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using KryossApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services;

public interface ICryptoService
{
    /// <summary>
    /// Generates an RSA-2048 key pair for an org. Stores public key in DB, private key in Key Vault.
    /// </summary>
    Task<string> GenerateKeyPairAsync(Guid organizationId);

    /// <summary>
    /// Decrypts an <see cref="AgentEnvelope"/> received from the agent and
    /// deserializes the inner JSON to <typeparamref name="T"/>. RSA-unwrap
    /// the AES key, then AES-256-GCM decrypt the payload, then deserialize.
    /// </summary>
    Task<T?> DecryptEnvelopeAsync<T>(Guid organizationId, AgentEnvelope envelope);
}

/// <summary>
/// Hybrid encryption: AES-256-GCM for payload + RSA-2048 for key wrapping.
/// Private keys stored in Azure Key Vault; public keys in org_crypto_keys table.
/// </summary>
public class CryptoService : ICryptoService
{
    private readonly KryossDbContext _db;
    private readonly ILogger<CryptoService> _logger;
    private readonly SecretClient? _keyVault;

    public CryptoService(KryossDbContext db, ILogger<CryptoService> logger)
    {
        _db = db;
        _logger = logger;

        var vaultUrl = Environment.GetEnvironmentVariable("KeyVaultUrl");
        if (!string.IsNullOrEmpty(vaultUrl))
            _keyVault = new SecretClient(new Uri(vaultUrl), new DefaultAzureCredential());
    }

    public async Task<string> GenerateKeyPairAsync(Guid organizationId)
    {
        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())
        ).ToLowerInvariant();

        // Deactivate previous keys
        var existingKeys = await _db.OrgCryptoKeys
            .Where(k => k.OrganizationId == organizationId && k.IsActive)
            .ToListAsync();
        foreach (var k in existingKeys)
        {
            k.IsActive = false;
            k.RotatedAt = DateTime.UtcNow;
        }

        // Store private key in Key Vault
        var secretName = $"org-{organizationId:N}-rsa-private";
        if (_keyVault is not null)
        {
            await _keyVault.SetSecretAsync(secretName, privateKeyPem);
        }
        else
        {
            _logger.LogWarning("Key Vault not configured — private key for org {OrgId} stored only in memory!", organizationId);
        }

        // Store public key + reference in DB
        var cryptoKey = new Data.Entities.OrgCryptoKey
        {
            OrganizationId = organizationId,
            PublicKeyPem = publicKeyPem,
            KeyVaultRef = $"https://kryoss-vault.vault.azure.net/secrets/{secretName}",
            Fingerprint = fingerprint,
            IsActive = true
        };
        _db.OrgCryptoKeys.Add(cryptoKey);
        await _db.SaveChangesAsync();

        return publicKeyPem;
    }

    public async Task<T?> DecryptEnvelopeAsync<T>(Guid organizationId, AgentEnvelope envelope)
    {
        // ─── Algorithm enforcement (downgrade defense) ─────────────────────
        // We accept exactly ONE suite. If a future version of the agent
        // negotiates differently, the server side must be upgraded first.
        // Reject anything else as a downgrade attempt.
        if (envelope.V != 1)
            throw new CryptographicException($"Unsupported envelope version: {envelope.V}");
        if (envelope.Alg != "RSA-OAEP-256+A256GCM")
            throw new CryptographicException($"Unsupported envelope alg: {envelope.Alg}");

        // ─── Resolve org's active key ──────────────────────────────────────
        var cryptoKey = await _db.OrgCryptoKeys
            .FirstOrDefaultAsync(k => k.OrganizationId == organizationId && k.IsActive);

        if (cryptoKey is null)
            throw new InvalidOperationException($"No active crypto key for org {organizationId}");

        // ─── Fetch private key from Key Vault ──────────────────────────────
        // The private key NEVER lives in process memory longer than this
        // method's lifetime. Future hardening (P2): unwrap via Key Vault
        // CryptographyClient.UnwrapKey so the private key never even leaves
        // the vault — see security-baseline.md §Key management lifecycle.
        if (_keyVault is null)
            throw new InvalidOperationException("Key Vault not configured — cannot decrypt");

        var secretName = $"org-{organizationId:N}-rsa-private";
        var secret = await _keyVault.GetSecretAsync(secretName);
        var privateKeyPem = secret.Value.Value;

        // ─── RSA-OAEP unwrap of the ephemeral AES key ──────────────────────
        byte[] aesKey;
        using (var rsa = RSA.Create())
        {
            rsa.ImportFromPem(privateKeyPem);
            aesKey = rsa.Decrypt(
                Convert.FromBase64String(envelope.Epk),
                RSAEncryptionPadding.OaepSHA256);
        }

        try
        {
            // ─── AES-256-GCM decrypt ───────────────────────────────────────
            // GCM auth tag verifies BOTH ciphertext and (currently empty) AAD.
            // If the tag check fails, AesGcm throws CryptographicException
            // and we surface it to the caller — no plaintext is produced.
            var iv = Convert.FromBase64String(envelope.Iv);
            var tag = Convert.FromBase64String(envelope.Tag);
            var ciphertext = Convert.FromBase64String(envelope.Ct);
            var plaintext = new byte[ciphertext.Length];

            using (var gcm = new AesGcm(aesKey, 16))
            {
                gcm.Decrypt(iv, ciphertext, tag, plaintext);
            }

            // ─── Deserialize ───────────────────────────────────────────────
            return JsonSerializer.Deserialize<T>(plaintext,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }
}

/// <summary>
/// Wire-format envelope sent by the agent on encrypted endpoints.
/// Byte-identical to <c>KryossAgent.Models.AgentEnvelope</c>.
///
/// <para>
/// Field naming intentionally short (JWE-inspired) to keep the on-the-wire
/// payload compact. Do NOT rename without bumping <see cref="V"/>.
/// </para>
/// </summary>
public class AgentEnvelope
{
    public int V { get; set; } = 1;
    public string Alg { get; set; } = "RSA-OAEP-256+A256GCM";
    public string Epk { get; set; } = "";
    public string Iv  { get; set; } = "";
    public string Ct  { get; set; } = "";
    public string Tag { get; set; } = "";
}
