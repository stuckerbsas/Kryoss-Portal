using System.Security.Cryptography;

namespace KryossApi.Services;

public interface IKeyRotationService
{
    (string machineSecret, string sessionKey, DateTime expiresAt) GenerateInitialKeys();
    (string newSessionKey, DateTime expiresAt)? TryRotate(
        string? currentSessionKey, DateTime? expiresAt, out string? prevKey, out DateTime? prevExpiry);
    bool ValidateHmac(string signingString, string signature, string key);
}

public class KeyRotationService : IKeyRotationService
{
    private static readonly TimeSpan SessionKeyLifetime = TimeSpan.FromHours(48);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);
    private static readonly double RotationThreshold = 0.5;

    public (string machineSecret, string sessionKey, DateTime expiresAt) GenerateInitialKeys()
    {
        var secret = GenerateKey();
        var session = GenerateKey();
        var expiresAt = DateTime.UtcNow.Add(SessionKeyLifetime);
        return (secret, session, expiresAt);
    }

    public (string newSessionKey, DateTime expiresAt)? TryRotate(
        string? currentSessionKey, DateTime? expiresAt, out string? prevKey, out DateTime? prevExpiry)
    {
        prevKey = null;
        prevExpiry = null;

        if (currentSessionKey is null || expiresAt is null)
            return null;

        var elapsed = DateTime.UtcNow - (expiresAt.Value - SessionKeyLifetime);
        if (elapsed < SessionKeyLifetime * RotationThreshold)
            return null;

        prevKey = currentSessionKey;
        prevExpiry = DateTime.UtcNow.Add(GracePeriod);

        var newKey = GenerateKey();
        var newExpiry = DateTime.UtcNow.Add(SessionKeyLifetime);
        return (newKey, newExpiry);
    }

    public bool ValidateHmac(string signingString, string signature, string key)
    {
        using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(key));
        var computed = Convert.ToHexString(hmac.ComputeHash(
            System.Text.Encoding.UTF8.GetBytes(signingString))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(computed),
            System.Text.Encoding.UTF8.GetBytes(signature));
    }

    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
