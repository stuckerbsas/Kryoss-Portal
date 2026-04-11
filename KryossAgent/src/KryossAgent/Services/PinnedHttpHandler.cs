using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace KryossAgent.Services;

/// <summary>
/// <see cref="HttpClientHandler"/> that enforces SPKI (Subject Public Key
/// Info) pinning on the TLS leaf certificate. This is the agent's last line
/// of defense against a rogue CA issuing a fraudulent cert for the Kryoss
/// API hostname — something that has happened to real companies (DigiNotar,
/// Superfish) and would otherwise let any nation-state-level attacker MitM
/// the agent even through the existing TLS validation chain.
///
/// <para>
/// We pin the SPKI hash instead of the whole leaf cert because the leaf
/// cert rotates every ~90 days in Azure (auto-renewal) but the underlying
/// key material can be stable across renewals if the ops team chooses.
/// Pinning SPKI instead of the cert fingerprint means auto-renewal doesn't
/// brick every deployed agent.
/// </para>
///
/// <para>
/// Rotation story: the agent accepts MULTIPLE pins (primary + backup). To
/// roll a key: (1) add the new pin alongside the old one in config,
/// (2) redeploy, (3) switch the server cert, (4) remove the old pin in a
/// follow-up push. Never ship with a single pin in production — one
/// operator mistake and the whole fleet stops reporting.
/// </para>
///
/// <para>
/// Log-only mode: if <see cref="_pins"/> is null or empty, the handler
/// does NOT reject anything. It prints the observed SPKI hash to stderr
/// on every connect so operators can capture the production value before
/// flipping enforcement on. This lets us ship the pinning code BEFORE we
/// know the right pin value, without breaking deployments.
/// </para>
/// </summary>
public sealed class PinnedHttpHandler : HttpClientHandler
{
    private readonly HashSet<string>? _pins;
    private readonly bool _logOnly;

    // The SPKI hash doesn't change between requests in the same process,
    // so in log-only mode we print it once instead of spamming stderr.
    private int _logged;

    public PinnedHttpHandler(string[]? pins)
    {
        if (pins is null || pins.Length == 0)
        {
            _logOnly = true;
            _pins = null;
        }
        else
        {
            _logOnly = false;
            _pins = new HashSet<string>(pins, StringComparer.Ordinal);
        }

        // Always enforce the normal chain first — SPKI pinning is in
        // ADDITION to, not a replacement for, CA validation.
        ServerCertificateCustomValidationCallback = ValidateCallback;
    }

    private bool ValidateCallback(
        HttpRequestMessage sender,
        X509Certificate2? cert,
        X509Chain? chain,
        SslPolicyErrors sslErrors)
    {
        // Reject outright if the normal TLS chain is broken — NO exceptions.
        // (Letting a self-signed cert through just because it "matches the
        // pin" would defeat the point: the pin can be anything an attacker
        // generates locally.)
        if (sslErrors != SslPolicyErrors.None || cert is null)
        {
            Console.Error.WriteLine(
                $"[TLS] Chain validation failed: {sslErrors}. Rejecting connection.");
            return false;
        }

        var spkiHash = ComputeSpkiHash(cert);

        if (_logOnly)
        {
            // Print once per process lifetime, only in verbose mode.
            if (Interlocked.Exchange(ref _logged, 1) == 0
                && Environment.GetEnvironmentVariable("KRYOSS_VERBOSE") == "1")
            {
                Console.Error.WriteLine(
                    $"[TLS] SPKI pinning is in LOG-ONLY mode. Observed pin: {spkiHash}");
                Console.Error.WriteLine(
                    "[TLS] Set HKLM\\SOFTWARE\\Kryoss\\Agent\\SpkiPins to enforce.");
            }
            return true;
        }

        if (_pins!.Contains(spkiHash))
            return true;

        Console.Error.WriteLine(
            $"[TLS] SPKI pin MISMATCH — observed {spkiHash}, expected one of {string.Join(',', _pins)}. " +
            "Rejecting connection (possible MitM attack).");
        return false;
    }

    /// <summary>
    /// Computes base64(SHA-256(SubjectPublicKeyInfo)) — the same format
    /// Chrome / Firefox / HPKP use. Callable standalone from the config
    /// bootstrap tool if we build one later.
    /// </summary>
    public static string ComputeSpkiHash(X509Certificate2 cert)
    {
        // GetPublicKey() returns the raw key bytes without the SPKI
        // wrapper — we need the full ASN.1 SubjectPublicKeyInfo blob so
        // different algorithms (RSA, ECDSA) are comparable.
        var spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
        var digest = SHA256.HashData(spki);
        return Convert.ToBase64String(digest);
    }
}
